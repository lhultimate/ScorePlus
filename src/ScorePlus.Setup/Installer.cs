using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace ScorePlus.Setup;

/// <summary>
/// Puts the published app on the machine and takes it off again. Everything lives under the user's
/// own profile, so installing needs no administrator rights: the app, its shortcuts, and the two
/// registry keys Windows reads to list it in Settings → Apps.
/// </summary>
public static class Installer
{
    public const string ProductName = "ScorePlus";
    public const string Publisher = "ScorePlus";
    public const string AppExecutable = "ScorePlus.exe";

    /// <summary>The setup's own version, set by the release build.</summary>
    public static string Version { get; } = typeof(Installer).Assembly.GetName().Version is { } version
        ? $"{version.Major}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}"
        : "1.0.0";

    /// <summary>
    /// The version as the operator knows it — Alpha 1.2.0. Display only: the registry, the install
    /// marker and the updater keep comparing the numeric <see cref="Version"/>, so a label can
    /// never make a build look older or newer than it is.
    /// </summary>
    public static string DisplayVersion => $"Alpha {Version}";

    static readonly string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ScorePlus";
    static readonly string AppKey = @"Software\ScorePlus";

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", ProductName);

    public sealed record Result(bool Ok, string Message);

    /// <summary>
    /// The only files the installer puts in a folder. Everything else there belongs to the user, which
    /// is why uninstalling never walks the directory: a wrong install folder must not become a way to
    /// delete somebody's documents.
    /// </summary>
    static readonly string[] OwnFileNames = [AppExecutable, "Uninstall.exe", "install.json"];

    public static bool IsOwnFile(string? name) =>
        name is not null && OwnFileNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Refuses folders that are never a program's home: a drive root, the profile itself, Desktop,
    /// Downloads, Documents, Music, Pictures, Videos, OneDrive, Windows, Program Files, AppData, and
    /// any existing folder that already holds files ScorePlus did not put there. A safe folder is one
    /// of its own, empty or already an install of ours.
    /// </summary>
    public static bool IsSafeInstallDirectory(string directory, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(directory))
        {
            reason = "Choose where ScorePlus should be installed.";
            return false;
        }

        string full;
        try { full = Path.GetFullPath(directory.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch
        {
            reason = "That is not a usable folder name.";
            return false;
        }

        if (string.Equals(full, Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            reason = "Choose a folder of its own, not a whole drive.";
            return false;
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var protectedFolders = new List<string>
        {
            profile,
            Path.Combine(profile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs)
        };
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrWhiteSpace(oneDrive))
        {
            protectedFolders.Add(oneDrive);
            protectedFolders.Add(Path.Combine(oneDrive, "Downloads"));
            protectedFolders.Add(Path.Combine(oneDrive, "Desktop"));
            protectedFolders.Add(Path.Combine(oneDrive, "Documents"));
        }

        foreach (var folder in protectedFolders.Where(folder => !string.IsNullOrWhiteSpace(folder)).Select(folder => folder.TrimEnd(Path.DirectorySeparatorChar)))
        {
            if (string.Equals(full, folder, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"{Path.GetFileName(full)} already holds your own files, so ScorePlus will not install there. Use a folder made for it.";
                return false;
            }
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(Path.DirectorySeparatorChar);
        if (windows.Length > 0 && (full.Equals(windows, StringComparison.OrdinalIgnoreCase) || full.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            reason = "Windows will not let a program live in its own folder. Choose somewhere else.";
            return false;
        }

        // An existing folder is only usable if it is empty or already ours; never overwrite somebody
        // else's files, and never leave the uninstaller pointing at a folder full of them.
        if (Directory.Exists(full) && !LooksLikeOurInstall(full))
        {
            string? foreign = null;
            try { foreign = Directory.EnumerateFileSystemEntries(full).FirstOrDefault(entry => !IsOwnFile(Path.GetFileName(entry))); }
            catch { foreign = "something"; }
            if (foreign is not null)
            {
                reason = $"That folder already holds other files (for example {Path.GetFileName(foreign)}). Let ScorePlus make its own folder instead.";
                return false;
            }
        }

        return true;
    }

    /// <summary>True when the folder holds the markers an install of ours leaves behind.</summary>
    static bool LooksLikeOurInstall(string directory)
    {
        try
        {
            return File.Exists(Path.Combine(directory, "install.json")) || File.Exists(Path.Combine(directory, AppExecutable));
        }
        catch { return false; }
    }

    /// <summary>Unpacks the app, writes shortcuts and the install marker.</summary>
    /// <param name="shortcuts">False for a silent or portable install that must not touch the shell.</param>
    /// <param name="register">False keeps the registry untouched, for a folder-only install.</param>
    public static Result Install(string directory, bool desktopShortcut, Action<string>? progress = null, bool shortcuts = true, bool register = true)
    {
        try
        {
            if (!IsSafeInstallDirectory(directory, out var refusal)) return new(false, refusal);
            directory = Path.GetFullPath(directory.Trim());

            CloseRunningApp(progress);
            Directory.CreateDirectory(directory);
            progress?.Invoke("Copying ScorePlus…");
            ExtractPayload(directory);

            var executable = Path.Combine(directory, AppExecutable);
            if (!File.Exists(executable)) return new(false, "The installer payload is missing its application.");

            if (shortcuts)
            {
                progress?.Invoke("Creating shortcuts…");
                var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                Directory.CreateDirectory(startMenu);
                CreateShortcut(Path.Combine(startMenu, $"{ProductName}.lnk"), executable, string.Empty, directory, "ScorePlus live production console");
                if (desktopShortcut)
                {
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    CreateShortcut(Path.Combine(desktop, $"{ProductName}.lnk"), executable, string.Empty, directory, "ScorePlus live production console");
                }
            }

            var uninstaller = Path.Combine(directory, "Uninstall.exe");
            try { File.Copy(Environment.ProcessPath!, uninstaller, true); } catch { }

            // The marker is what tells the app it is allowed to check for updates.
            File.WriteAllText(Path.Combine(directory, "install.json"),
                $"{{\n  \"installed\": true,\n  \"version\": \"{Version}\",\n  \"installedAt\": \"{DateTimeOffset.Now:O}\"\n}}\n");

            if (register)
            {
                progress?.Invoke("Registering ScorePlus…");
                using (var key = Registry.CurrentUser.CreateSubKey(AppKey))
                {
                    key?.SetValue("InstallDir", directory);
                    key?.SetValue("Version", Version);
                }
                using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    key?.SetValue("DisplayName", ProductName);
                    key?.SetValue("DisplayVersion", Version);
                    key?.SetValue("Publisher", Publisher);
                    key?.SetValue("InstallLocation", directory);
                    key?.SetValue("DisplayIcon", executable);
                    key?.SetValue("UninstallString", $"\"{uninstaller}\" /uninstall");
                    key?.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key?.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }

            progress?.Invoke("Installed.");
            return new(true, $"ScorePlus {Version} is installed in {directory}.");
        }
        catch (Exception ex)
        {
            return new(false, ex.Message);
        }
    }

    /// <summary>
    /// Removes the app, its shortcuts and its registry entries. Game files stay untouched, and so does
    /// everything else in the install folder: only the three files the installer wrote are deleted. A
    /// folder that was chosen badly must never turn an uninstall into a way to erase a Downloads folder.
    /// </summary>
    public static Result Uninstall(Action<string>? progress = null)
    {
        try
        {
            var directory = RegisteredDirectory();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || !LooksLikeOurInstall(directory))
            {
                RemoveRegistry();
                progress?.Invoke("Removed.");
                return new(true, "ScorePlus is not installed on this computer. Its saved games, media and preferences are still in your profile.");
            }

            CloseRunningApp(progress);
            progress?.Invoke("Removing shortcuts…");
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", $"{ProductName}.lnk"));
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{ProductName}.lnk"));

            progress?.Invoke("Removing ScorePlus files…");
            // Only what this installer wrote. The uninstaller itself may be running from this folder;
            // the copy in %TEMP% is what finishes the job, so a locked file cannot stop the removal.
            var leftovers = RemoveOwnFiles(directory);

            RemoveRegistry();
            progress?.Invoke("Removed.");
            var kept = leftovers == 0
                ? ""
                : $" {leftovers} other item(s) in that folder were not part of ScorePlus and were left untouched.";
            return new(true, $"ScorePlus was removed. Your saved games, media and preferences are still in your profile.{kept}");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    /// <summary>
    /// Deletes exactly the three files the installer wrote and, only if that leaves the folder empty,
    /// the folder itself. Returns how many items were left behind — a number that is expected to be
    /// non-zero whenever the app was installed somewhere it had no business being.
    /// </summary>
    public static int RemoveOwnFiles(string directory)
    {
        foreach (var name in OwnFileNames) TryDelete(Path.Combine(directory, name));
        var leftovers = Directory.Exists(directory) ? Directory.GetFileSystemEntries(directory).Length : 0;
        if (leftovers == 0) { try { Directory.Delete(directory, false); } catch { } }
        return leftovers;
    }

    /// <summary>Where the registry says ScorePlus lives, whether or not anything is there.</summary>
    public static string? RegisteredDirectory()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppKey);
            if (key?.GetValue("InstallDir") is string dir && !string.IsNullOrWhiteSpace(dir)) return dir;
        }
        catch { }
        return null;
    }

    /// <summary>A folder that really holds this app, used to offer an upgrade in place.</summary>
    public static string? InstalledDirectory()
    {
        var directory = RegisteredDirectory();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;
        return LooksLikeOurInstall(directory) ? directory : null;
    }

    static void RemoveRegistry()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(AppKey, false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
    }

    static void ExtractPayload(string directory)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
        if (stream is null) throw new InvalidOperationException("This setup was built without the app payload. Run scripts/build-release.sh to make a real installer.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
            var target = Path.GetFullPath(Path.Combine(directory, entry.FullName));
            if (!target.StartsWith(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The update payload contains an unexpected path.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    /// <summary>An update can be installed while ScorePlus is on air; it is asked to close first.</summary>
    static void CloseRunningApp(Action<string>? progress)
    {
        foreach (var process in Process.GetProcessesByName("ScorePlus"))
        {
            try
            {
                progress?.Invoke("Closing ScorePlus…");
                if (!process.CloseMainWindow()) { process.Kill(); continue; }
                if (!process.WaitForExit(4000)) process.Kill();
            }
            catch { }
        }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>Makes a real .lnk through the Windows shell, so it looks and behaves like any other.</summary>
    public static void CreateShortcut(string shortcutPath, string target, string arguments, string workingDirectory, string description)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(shortcutPath);
            link.TargetPath = target;
            link.Arguments = arguments;
            link.WorkingDirectory = workingDirectory;
            link.Description = description;
            link.IconLocation = target;
            link.Save();
        }
        catch { /* A missing shortcut must never fail the install itself. */ }
    }
}
