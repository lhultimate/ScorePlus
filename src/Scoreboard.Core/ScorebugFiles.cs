using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scoreboard.Core;

/// <summary>
/// The two files the scorebug studio trades with other shows and other machines:
/// a ScorePlus Layout (<c>.spl</c>) holding one layout, and a ScorePlus Display (<c>.spd</c>)
/// holding a whole screen — every layout on it, in order, with what was being edited and what
/// was on air, and all of their links. Both are plain, indented JSON so a broken file can be
/// read by eye, and both carry every element property: colours, fonts, gradients, borders,
/// rotation, transitions, video tags and links all survive the trip.
/// </summary>
public static class ScorebugFiles
{
    public const string LayoutExtension = "spl";
    public const string DisplayExtension = "spd";

    /// <summary>The shape version of these files, so a future reader can migrate them.</summary>
    public const int CurrentVersion = 1;

    public const string LayoutFilter = "ScorePlus Layout (*.spl)|*.spl|All files (*.*)|*.*";
    public const string DisplayFilter = "ScorePlus Display (*.spd)|*.spd|All files (*.*)|*.*";

    static readonly JsonSerializerOptions Write = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    static readonly JsonSerializerOptions Read = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>What a .spl file holds: one layout, plus enough header to say what wrote it.</summary>
    public sealed class LayoutFile
    {
        public string Format { get; set; } = "ScorePlus Layout";
        public int Version { get; set; } = CurrentVersion;
        public string Product { get; set; } = "ScorePlus";
        public string SavedBy { get; set; } = ProductVersion();
        public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
        public ScorebugLayout? Layout { get; set; }
    }

    /// <summary>What a .spd file holds: one display and all of its layouts.</summary>
    public sealed class DisplayFile
    {
        public string Format { get; set; } = "ScorePlus Display";
        public int Version { get; set; } = CurrentVersion;
        public string Product { get; set; } = "ScorePlus";
        public string SavedBy { get; set; } = ProductVersion();
        public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
        public string Name { get; set; } = "Display";
        public int ActiveIndex { get; set; }
        public int PublishedIndex { get; set; }
        public List<ScorebugLayout> Layouts { get; set; } = [];
    }

    static string ProductVersion()
    {
        try { return typeof(ScorebugFiles).Assembly.GetName().Version?.ToString() ?? "1.0.0"; }
        catch { return "1.0.0"; }
    }

    // ---------- one layout ----------

    public static string SerializeLayout(ScorebugLayout layout) =>
        JsonSerializer.Serialize(new LayoutFile { Layout = layout }, Write);

    /// <summary>
    /// Reads a .spl body. A bare layout object (what an older build would have written, or what a
    /// hand-written file looks like) is accepted too, so nothing has to be wrapped by hand.
    /// </summary>
    public static ScorebugLayout? ParseLayout(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var file = JsonSerializer.Deserialize<LayoutFile>(json, Read);
            if (file?.Layout is { Elements: not null }) return file.Layout;
        }
        catch { }
        try
        {
            var bare = JsonSerializer.Deserialize<ScorebugLayout>(json, Read);
            return bare?.Elements is null ? null : bare;
        }
        catch { return null; }
    }

    public static void SaveLayout(string path, ScorebugLayout layout)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
        WriteAtomically(path, SerializeLayout(layout));
    }

    public static ScorebugLayout? LoadLayout(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            return ParseLayout(File.ReadAllText(path));
        }
        catch { return null; }
    }

    // ---------- one display ----------

    public static string SerializeDisplay(ScorebugDisplay display) => JsonSerializer.Serialize(new DisplayFile
    {
        Name = display.Name,
        ActiveIndex = display.ActiveIndex,
        PublishedIndex = display.PublishedIndex,
        Layouts = display.Layouts
    }, Write);

    public static ScorebugDisplay? ParseDisplay(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var file = JsonSerializer.Deserialize<DisplayFile>(json, Read);
            if (file is null) return null;
            var layouts = (file.Layouts ?? []).Where(layout => layout is not null && layout.Elements is not null).ToList();
            if (layouts.Count == 0) return null;
            return new ScorebugDisplay
            {
                Name = string.IsNullOrWhiteSpace(file.Name) ? "Imported display" : file.Name.Trim(),
                Layouts = layouts,
                ActiveIndex = Math.Clamp(file.ActiveIndex, 0, layouts.Count - 1),
                PublishedIndex = Math.Clamp(file.PublishedIndex, 0, layouts.Count - 1)
            };
        }
        catch { return null; }
    }

    public static void SaveDisplay(string path, ScorebugDisplay display)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
        WriteAtomically(path, SerializeDisplay(display));
    }

    public static ScorebugDisplay? LoadDisplay(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            return ParseDisplay(File.ReadAllText(path));
        }
        catch { return null; }
    }

    // ---------- bringing a file into a game ----------

    /// <summary>
    /// A deep copy with a brand new layout id and new element ids, so an imported layout never
    /// collides with one already in the game. Everything else — artwork, links, video tags,
    /// transitions — is carried over untouched.
    /// </summary>
    public static ScorebugLayout FreshCopy(ScorebugLayout layout, string? name = null) =>
        FreshCopy(layout, name, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The shared link map matters for a display: a part linked across two layouts in the file has
    /// to stay one part after the import, so every layout in the file draws from the same map.
    /// </summary>
    static ScorebugLayout FreshCopy(ScorebugLayout layout, string? name, Dictionary<string, string> links)
    {
        var copy = layout.Copy(string.IsNullOrWhiteSpace(name) ? layout.Name : name);
        copy.Elements ??= [];
        if (string.IsNullOrWhiteSpace(copy.Id)) copy.Id = Guid.NewGuid().ToString("N");
        // Parts that shared a link id in the file still share one afterwards — but a new one, so the
        // import cannot hijack a part that is already linked in this game.
        foreach (var element in copy.Elements)
        {
            element.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(element.LinkId)) continue;
            if (!links.TryGetValue(element.LinkId, out var fresh)) links[element.LinkId] = fresh = Guid.NewGuid().ToString("N");
            element.LinkId = fresh;
        }
        return copy;
    }

    /// <summary>
    /// A deep copy of a whole display with fresh ids throughout, keeping the layouts' own identity
    /// relative to each other. A layout link always points at another display, which is not part of
    /// the file, so those are cleared rather than left aimed at an id that may mean something else here.
    /// </summary>
    public static ScorebugDisplay FreshDisplay(ScorebugDisplay display, string? name = null)
    {
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var copies = (display.Layouts ?? []).Select(layout => FreshCopy(layout, null, links)).ToList();
        foreach (var copy in copies)
        {
            // A layout link always points at a layout on *another* display, and that display is not
            // part of this file, so the import starts with the links cleared rather than aimed at
            // whatever happens to share an id here.
            copy.LinkDisplayId = null;
            copy.LinkLayoutId = null;
        }
        var fresh = new ScorebugDisplay
        {
            Name = string.IsNullOrWhiteSpace(name) ? display.Name : name.Trim(),
            Layouts = copies,
            ActiveIndex = Math.Clamp(display.ActiveIndex, 0, Math.Max(0, copies.Count - 1)),
            PublishedIndex = Math.Clamp(display.PublishedIndex, 0, Math.Max(0, copies.Count - 1))
        };
        if (string.IsNullOrWhiteSpace(fresh.Id)) fresh.Id = Guid.NewGuid().ToString("N");
        return fresh;
    }

    /// <summary>Writes beside the target first, so an interrupted save never leaves half a file.</summary>
    static void WriteAtomically(string path, string content)
    {
        var full = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        var temp = full + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, full, true);
    }
}
