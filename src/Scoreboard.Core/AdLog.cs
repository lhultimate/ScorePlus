using System.IO;
using System.Text.Json;

namespace Scoreboard.Core;

/// <summary>
/// One line of the advertisement log: a clip, how long it runs, and how many times it has been
/// played. The count is an impression — every time the clip goes to air, whether it was cued by
/// hand, from a playlist or from a quick-media button, it goes up by one.
/// </summary>
public sealed class AdImpression
{
    /// <summary>The clip this line counts. Kept even when the file leaves the library, so an old
    /// campaign's numbers are not silently thrown away.</summary>
    public string Path { get; set; } = "";

    /// <summary>The name shown in the log: the clip's own name, else its file name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The clip's length in seconds; 0 when it has never been opened.</summary>
    public double Seconds { get; set; }

    public int Plays { get; set; }

    public DateTimeOffset? LastPlayed { get; set; }

    /// <summary>"12 s", "1.5 min" — the length beside the count, as the operator reads a spot list.</summary>
    public string DurationLabel => Seconds <= 0
        ? "—"
        : Seconds >= 60 ? $"{Seconds / 60:0.#} min" : $"{Seconds:0.#} s";

    public string PlaysLabel => Plays == 1 ? "1 play" : $"{Plays} plays";

    public string LastLabel => LastPlayed is { } at ? at.LocalDateTime.ToString("d MMM HH:mm") : "never";
}

/// <summary>What is written to <c>adverts.json</c>.</summary>
public sealed class AdLogFile
{
    public List<AdImpression> Clips { get; set; } = [];
}

/// <summary>
/// The advertisement log (<c>%LOCALAPPDATA%\ScorePlus\adverts.json</c>): one impression line per
/// clip the show has played, with its length. Kept outside the game so a new game never resets a
/// campaign's numbers; the log's own Reset button is the only thing that does.
/// </summary>
public static class AdLogStore
{
    static AdLogFile? _log;
    static string? _overridePath;

    /// <summary>Test hook: aim storage at a throwaway file so a suite never rewrites the real log.</summary>
    public static void UsePath(string? path)
    {
        _overridePath = path;
        _log = null;
    }

    /// <summary>Raised after an impression is counted or the log is reset, so an open window follows it.</summary>
    public static event Action? Changed;

    static string FilePath => _overridePath ?? AppPaths.Combine("adverts.json");

    static AdLogFile Log => _log ??= Load();

    static AdLogFile Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AdLogFile();
            var log = JsonSerializer.Deserialize<AdLogFile>(File.ReadAllText(FilePath)) ?? new AdLogFile();
            log.Clips ??= [];
            foreach (var clip in log.Clips)
            {
                clip.Path ??= "";
                clip.Name ??= "";
            }
            return log;
        }
        catch
        {
            // A damaged log must never stop a show; start empty rather than crash.
            return new AdLogFile();
        }
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Log, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true);
        }
        catch
        {
            // Storage trouble is reported at worst by the window's own line; playback carries on.
        }
        Changed?.Invoke();
    }

    static AdImpression? Find(string path) =>
        Log.Clips.FirstOrDefault(clip => string.Equals(clip.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Counts one impression of a clip. The length is refreshed whenever the clip is opened with a
    /// known duration, so a log written before lengths were recorded fills itself in on the next play.
    /// </summary>
    public static void Count(string path, string? name, double seconds = 0)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = path.Trim();
        var clip = Find(path);
        if (clip is null)
        {
            clip = new AdImpression { Path = path, Name = DisplayName(path, name) };
            Log.Clips.Add(clip);
        }
        else if (!string.IsNullOrWhiteSpace(name)) clip.Name = DisplayName(path, name);
        if (double.IsFinite(seconds) && seconds > 0) clip.Seconds = seconds;
        clip.Plays++;
        clip.LastPlayed = DateTimeOffset.Now;
        Save();
    }

    /// <summary>Notes a clip's length without counting an impression (a library import, say).</summary>
    public static void RememberLength(string path, string? name, double seconds)
    {
        if (string.IsNullOrWhiteSpace(path) || !double.IsFinite(seconds) || seconds <= 0) return;
        path = path.Trim();
        var clip = Find(path);
        if (clip is null)
        {
            clip = new AdImpression { Path = path, Name = DisplayName(path, name) };
            Log.Clips.Add(clip);
        }
        else if (!string.IsNullOrWhiteSpace(name)) clip.Name = DisplayName(path, name);
        clip.Seconds = seconds;
        Save();
    }

    /// <summary>Every line, busiest first — what the log window lists.</summary>
    public static IReadOnlyList<AdImpression> All() => Log.Clips
        .OrderByDescending(clip => clip.Plays)
        .ThenBy(clip => clip.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>Impressions across every clip.</summary>
    public static int TotalPlays => Log.Clips.Sum(clip => clip.Plays);

    /// <summary>How many clips have been played at least once.</summary>
    public static int PlayedCount => Log.Clips.Count(clip => clip.Plays > 0);

    /// <summary>
    /// Sets every impression back to zero. The lines and their lengths stay, so the log still shows
    /// what the show carries; only the counts and the last-played times are cleared.
    /// </summary>
    public static void Reset()
    {
        foreach (var clip in Log.Clips)
        {
            clip.Plays = 0;
            clip.LastPlayed = null;
        }
        Save();
    }

    static string DisplayName(string path, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        try
        {
            var file = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(file) ? path : file;
        }
        catch { return path; }
    }
}
