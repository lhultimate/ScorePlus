using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scoreboard.Core;

/// <summary>One recoverable autosave, summarised for the launch screen.</summary>
public sealed record AutosaveEntry(string Path, DateTimeOffset SavedAt, string SportId, string AwayTeam, string HomeTeam, int AwayScore, int HomeScore)
{
    public string WhenLabel => SavedAt.ToLocalTime().ToString("MMM d · HH:mm:ss");

    public string ScoreLine => $"{AwayTeam} {AwayScore} – {HomeScore} {HomeTeam}";

    public string Label => $"{WhenLabel}   {ScoreLine}";

    public string Detail => $"{SportId.ToUpperInvariant()} · {WhenLabel}";
}

/// <summary>
/// Rolling autosaves on disk. Same-minute saves overwrite the newest file, so the list shows one
/// recoverable point per minute of play instead of a hundred copies of the same second.
/// </summary>
public static class AutosaveStore
{
    /// <summary>How many recoverable points are kept. Older ones are pruned.</summary>
    public const int Keep = 8;

    static readonly TimeSpan RollInterval = TimeSpan.FromSeconds(60);
    static string? _folderOverride;

    /// <summary>Test hook: keep a suite's autosaves out of the operator's real folder.</summary>
    public static void UseFolder(string? folder) => _folderOverride = folder;

    public static string Folder => _folderOverride ?? AppPaths.Combine("autosaves");

    /// <param name="startNewPoint">Forces a fresh recoverable point instead of updating the newest
    /// one. Used when a new game starts, so the game that just ended keeps its own autosave.</param>
    public static IReadOnlyList<AutosaveEntry> Save(GameEngine engine, bool startNewPoint = false, ScorebugDocument? design = null)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var existing = List();
            var newest = existing.FirstOrDefault();
            var roll = startNewPoint || newest is null || DateTimeOffset.Now - newest.SavedAt >= RollInterval;
            string path;
            if (!roll && newest is not null) path = newest.Path;
            else
            {
                // Names have one-second resolution, so two points in the same second need a suffix
                // rather than silently overwriting the earlier one.
                var stem = Path.Combine(Folder, $"autosave-{DateTime.Now:yyyyMMdd-HHmmss}");
                path = stem + ".spg";
                for (var n = 2; File.Exists(path); n++) path = $"{stem}-{n}.spg";
            }
            // A recovery point keeps the screens too, so restoring one really continues the show.
            engine.Save(path, design);
            Prune();
        }
        catch
        {
            // A failed autosave must never interrupt the game; it is reported by the caller.
        }
        return List();
    }

    /// <summary>Every recoverable point, newest first.</summary>
    public static IReadOnlyList<AutosaveEntry> List()
    {
        try
        {
            if (!Directory.Exists(Folder)) return [];
            var entries = new List<AutosaveEntry>();
            foreach (var path in Directory.EnumerateFiles(Folder))
            {
                if (!IsGameFile(path)) continue;
                if (Read(path) is { } entry) entries.Add(entry);
            }
            return entries.OrderByDescending(e => e.SavedAt).ToArray();
        }
        catch { return []; }
    }

    public static AutosaveEntry? Load(string path) => File.Exists(path) ? Read(path) : null;

    /// <summary>Prunes the folder now; used at launch so old piles shrink without waiting for a save.</summary>
    public static void Trim() => Prune();

    /// <summary>
    /// Removes every recoverable point on this computer, including any file that is not readable as
    /// a game. Saved .spg files elsewhere are untouched. Returns how many were deleted.
    /// </summary>
    public static int DeleteAll()
    {
        var deleted = 0;
        try
        {
            if (!Directory.Exists(Folder)) return 0;
            foreach (var path in Directory.EnumerateFiles(Folder).Where(IsGameFile).ToArray())
            {
                try { File.Delete(path); deleted++; } catch { }
            }
            // Deleting the recovery points is final: the one-time legacy import must not undo it.
            if (deleted > 0 || File.Exists(MarkerPath)) File.WriteAllText(MarkerPath, DateTimeOffset.Now.ToString("u"));
        }
        catch
        {
            // A locked file simply stays; the ones that could be removed are gone.
        }
        return deleted;
    }

    /// <summary>
    /// Brings the single recovery file from older builds into the list, once. The marker is what
    /// makes it once: without it, deleting every autosave would pull the old file back on the next
    /// launch, because the import only ran when the list was empty.
    /// </summary>
    public static void ImportLegacy()
    {
        try
        {
            if (File.Exists(MarkerPath)) return;
            Directory.CreateDirectory(Folder);
            Import(AppPaths.Combine("recovery.scoreboard"));
            // The pre-rename folder is only a fallback; a redirected root (tests) must stay isolated.
            if (AppPaths.IsUsingDefaultRoot) Import(Path.Combine(AppPaths.PreviousRoot, "recovery.scoreboard"));
            File.WriteAllText(MarkerPath, DateTimeOffset.Now.ToString("u"));
        }
        catch
        {
            // An unreadable legacy file must not stop the app from starting.
        }
    }

    static string MarkerPath => Path.Combine(Folder, ".legacy-imported");

    /// <summary>Moves a pre-autosave-folder recovery file into the list, once.</summary>
    public static void Import(string? legacyPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(legacyPath) || !File.Exists(legacyPath)) return;
            if (List().Count > 0) return;
            Directory.CreateDirectory(Folder);
            var state = Read(legacyPath);
            var stamp = state?.SavedAt.LocalDateTime ?? File.GetLastWriteTime(legacyPath);
            var target = Path.Combine(Folder, $"autosave-{stamp:yyyyMMdd-HHmmss}.spg");
            if (!File.Exists(target)) File.Copy(legacyPath, target);
        }
        catch { }
    }

    static bool IsGameFile(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() is ".spg" or ".scoreboard" or ".json";

    static void Prune()
    {
        var extra = List().Skip(Keep).ToArray();
        foreach (var entry in extra)
        {
            try { File.Delete(entry.Path); } catch { }
        }
    }

    static AutosaveEntry? Read(string path)
    {
        try
        {
            var (state, _) = GameFiles.Read(path);
            if (state is null) return null;
            var saved = state.SavedAt == default ? new DateTimeOffset(File.GetLastWriteTime(path)) : state.SavedAt;
            return new AutosaveEntry(path, saved, state.SportId, state.AwayTeam, state.HomeTeam, state.AwayScore, state.HomeScore);
        }
        catch { return null; }
    }

    static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };
}
