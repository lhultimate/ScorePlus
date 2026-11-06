using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Scoreboard.Desktop;

/// <summary>What this build is, and whether it came from the installer.</summary>
public static class AppInfo
{
    /// <summary>The build's version, from the assembly the publish set. The installer writes the same one.</summary>
    public static string Version { get; } = typeof(AppInfo).Assembly.GetName().Version is { } version
        ? $"{version.Major}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}"
        : "1.0.0";

    /// <summary>
    /// The release the operator knows this build by — a1.1, a1.02 — written into the assembly by the
    /// release build. Assembly versions stay numeric because the updater compares them, so the label
    /// lives beside it rather than replacing it.
    /// </summary>
    public static string Release { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            is { Length: > 0 } label && !string.Equals(label, Version, StringComparison.OrdinalIgnoreCase)
            ? label.Split('+')[0]
            : Version;

    public const string RegistryKey = @"Software\ScorePlus";

    /// <summary>
    /// True only for an installed copy. A build run straight out of the dist folder must never try to
    /// update itself over the top of a working file; the installer is what puts the marker in place.
    /// </summary>
    public static bool IsInstalled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
                if (key?.GetValue("InstallDir") is string dir && Directory.Exists(dir)) return true;
            }
            catch { }
            return File.Exists(Path.Combine(AppContext.BaseDirectory, "install.json"));
        }
    }

    public static string? InstallDir
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
                if (key?.GetValue("InstallDir") is string dir) return dir;
            }
            catch { }
            return null;
        }
    }
}

/// <summary>One release as described by the update feed.</summary>
public sealed record UpdateManifest(string Version, string Url, string? Sha256, string? Notes)
{
    /// <summary>The human release label from the tag (a1.7, b1.0), when the feed knows it.</summary>
    public string? Label { get; init; }
}

/// <summary>
/// The feed an installed copy asks by default: this project's own releases, newest first. It is the
/// GitHub API rather than a hand-uploaded win.json so that publishing a release — a prerelease
/// included — is the entire update ritual, and nothing can go stale or be forgotten.
/// </summary>
public static class UpdateFeed
{
    public const string BuiltIn = "https://api.github.com/repos/lhultimate/ScorePlus/releases?per_page=20";

    /// <summary>
    /// The two assets every release carries: the installer an installed copy updates from, and the
    /// portable build. Their names are fixed, but the extension is not assumed — GitHub happily
    /// takes <c>ScorePlusSetup</c>, <c>ScorePlusSetup.exe</c> or a versioned spelling — so an asset is
    /// matched on its base name. <c>ScorePlusPortable</c> is deliberately never offered as an update:
    /// a copy run from a folder replaces itself when you download the new one.
    /// </summary>
    public const string SetupAssetName = "ScorePlusSetup";

    /// <summary>The portable build's asset name, kept here so both release files are named in one place.</summary>
    public const string PortableAssetName = "ScorePlusPortable";

    /// <summary>True for the release's installer asset, whatever extension it was uploaded with.</summary>
    public static bool IsSetupAsset(string? assetName) =>
        assetName is { Length: > 0 }
        && string.Equals(Path.GetFileNameWithoutExtension(assetName), SetupAssetName, StringComparison.OrdinalIgnoreCase);

    public static string Resolve(string? preferred) => string.IsNullOrWhiteSpace(preferred) ? BuiltIn : preferred.Trim();
}

/// <summary>
/// Checks a release feed for a newer build and can fetch and verify it. Two shapes are understood:
/// a GitHub releases array (prereleases included, which is what /releases/latest skips) and a plain
/// manifest object for any other host. This is deliberately small and explicit: the app asks one
/// URL for JSON, compares versions, and hands the verified installer to Windows. There is no
/// background service and no account.
/// </summary>
public static class UpdateChecker
{
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    static UpdateChecker()
    {
        // GitHub's API refuses a request with no User-Agent, and asks for its own media type.
        try
        {
            Client.DefaultRequestHeaders.UserAgent.ParseAdd($"ScorePlus/{AppInfo.Version}");
            Client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json, application/json");
        }
        catch { }
    }

    /// <summary>
    /// Returns the newer release, or null when this build is current, the feed cannot be read, or
    /// nothing suitable is published. A failed check is silent: an offline laptop must not nag
    /// mid-show.
    /// </summary>
    public static async Task<UpdateManifest?> CheckAsync(string? feedUrl, CancellationToken token = default)
    {
        try
        {
            var json = await Client.GetStringAsync(UpdateFeed.Resolve(feedUrl), token);
            var manifest = json.TrimStart().StartsWith('[') ? ParseReleases(json) : ParseManifest(json);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Url)) return null;
            return IsNewer(manifest.Version, AppInfo.Release) ? manifest : null;
        }
        catch { return null; }
    }

    /// <summary>A single-object manifest, for a feed hosted outside GitHub.</summary>
    public static UpdateManifest? ParseManifest(string json)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, Options);
            return manifest is { Version: not null, Url: not null } ? manifest : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Reads a GitHub releases array and returns the newest published release that actually carries
    /// an installer. Drafts are ignored (their assets are not served), prereleases are kept — that is
    /// the whole point — and the highest version wins rather than the first entry, so a back-dated
    /// tag cannot hide a newer build. The checksum comes from the asset's own digest, which GitHub
    /// computes on upload, so there is no manifest to publish or forget.
    /// </summary>
    public static UpdateManifest? ParseReleases(string json)
    {
        List<GithubRelease>? releases;
        try { releases = JsonSerializer.Deserialize<List<GithubRelease>>(json, Options); }
        catch { return null; }
        if (releases is null) return null;

        UpdateManifest? best = null;
        Version? bestVersion = null;

        foreach (var release in releases)
        {
            if (release.Draft) continue;
            var version = VersionFromTag(release.TagName);
            if (version is null) continue;

            var setup = release.Assets.FirstOrDefault(asset =>
                UpdateFeed.IsSetupAsset(asset.Name)
                && !string.Equals(asset.State, "starter", StringComparison.OrdinalIgnoreCase));
            if (setup is null || string.IsNullOrWhiteSpace(setup.BrowserDownloadUrl)) continue;

            var parsed = Version.Parse(version);
            if (bestVersion is not null && parsed <= bestVersion) continue;

            bestVersion = parsed;
            best = new UpdateManifest(version, setup.BrowserDownloadUrl, Sha256Of(setup.Digest), NotesOf(release))
            {
                Label = release.TagName
            };
        }

        return best;
    }

    /// <summary>
    /// Turns a tag into a comparable version: "v1.2.0", "1.2", "release-1.2.3" and "a1.01" all work.
    /// Anything with no number in it cannot be compared, so it is not offered as an update.
    /// </summary>
    public static string? VersionFromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var match = Regex.Match(tag, @"\d+(?:\.\d+)*");
        if (!match.Success) return null;

        var parts = match.Value.Split('.');
        if (parts.Length > 3) parts = parts[..3];
        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var value) || value < 0) return null;
            numbers[i] = value;
        }
        // Alpha and beta tags use the same two-digit minor rule as the release scripts, so a1.6
        // reads as 1.60.0 and sorts after a1.51 (1.51.0). Plain release tags keep their own digits.
        var alpha = Regex.IsMatch(tag, @"^\s*(?:alpha|a)(?=\d)", RegexOptions.IgnoreCase);
        var beta = Regex.IsMatch(tag, @"^\s*(?:beta|b)(?=\d)", RegexOptions.IgnoreCase);
        var stable = Regex.IsMatch(tag, @"^\s*(?:stable|c)(?=\d)", RegexOptions.IgnoreCase);
        if (parts.Length >= 2 && parts[1].Length == 1 && (alpha || beta)) numbers[1] *= 10;
        // Beta builds sit above every alpha build, and a c-tag (the eventual stable channel) sits
        // above beta. The major number is the stage, which also lets an older alpha updater, which
        // only understands numbers, see b1.0 as 2.0.0 and update to it.
        var major = beta ? 2 : stable ? 3 : numbers[0];
        return $"{major}.{numbers[1]}.{numbers[2]}";
    }

    /// <summary>
    /// Compares two release labels. Numeric versions decide first; on an exact tie the stage does,
    /// so b1.0 is newer than a1.70 but a numeric manifest for b1.0 does not re-offer itself to b1.0.
    /// A manifest without a stage prefix therefore compares as an alpha/base release on a tie.
    /// </summary>
    public static bool IsNewer(string remote, string current)
    {
        var remoteVersion = VersionFromTag(remote);
        var currentVersion = VersionFromTag(current);
        if (remoteVersion is null || currentVersion is null) return false;
        if (!Version.TryParse(remoteVersion, out var newer) || !Version.TryParse(currentVersion, out var mine)) return false;
        var comparison = newer.CompareTo(mine);
        if (comparison != 0) return comparison > 0;
        return StageRank(remote) > StageRank(current);
    }

    static int StageRank(string label)
    {
        if (Regex.IsMatch(label, @"^\s*(?:stable|c)(?=\d)", RegexOptions.IgnoreCase)) return 2;
        if (Regex.IsMatch(label, @"^\s*(?:beta|b)(?=\d)", RegexOptions.IgnoreCase)) return 1;
        return 0;
    }

    /// <summary>
    /// Downloads the release to a temp file and verifies its SHA-256 when the feed carries one.
    /// A mismatch deletes the file: an update that cannot be proven is never run.
    /// </summary>
    public static async Task<(string? Path, string? Error)> DownloadAsync(UpdateManifest manifest, CancellationToken token = default)
    {
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "ScorePlusUpdate");
            Directory.CreateDirectory(directory);
            var fileName = Path.GetFileName(new Uri(manifest.Url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = UpdateFeed.SetupAssetName + ".exe";
            var path = Path.Combine(directory, fileName);

            using (var response = await Client.GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(path);
                await response.Content.CopyToAsync(file, token);
            }

            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                await using var stream = File.OpenRead(path);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(path);
                    return (null, "The download did not match the published checksum, so it was discarded.");
                }
            }
            return (path, null);
        }
        catch (Exception ex) { return (null, ex.Message); }
    }

    /// <summary>Starts the verified installer and lets it replace this copy.</summary>
    public static bool Run(string path)
    {
        try
        {
            // /SILENT keeps the wizard invisible; the installer closes ScorePlus itself before
            // copying files, so the running app never locks what is being replaced.
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Arguments = "/SILENT /CLOSEAPPLICATIONS /NORESTART" });
            return true;
        }
        catch { return false; }
    }

    /// <summary>GitHub's asset digest reads "sha256:HEX"; the manifest wants the bare hex.</summary>
    static string? Sha256Of(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        const string prefix = "sha256:";
        var value = digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? digest[prefix.Length..] : digest;
        return value.Length == 64 && value.All(Uri.IsHexDigit) ? value.ToUpperInvariant() : null;
    }

    /// <summary>One or two sentences for the prompt: the release title, else its first line.</summary>
    static string? NotesOf(GithubRelease release)
    {
        var name = release.Name?.Trim();
        if (!string.IsNullOrEmpty(name)) return name;
        var line = release.Body?.Split('\n').Select(text => text.Trim()).FirstOrDefault(text => text.Length > 0);
        if (line is null) return null;
        return line.Length > 180 ? line[..180] : line;
    }

    sealed class GithubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GithubAsset> Assets { get; set; } = [];
    }

    sealed class GithubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
    }
}
