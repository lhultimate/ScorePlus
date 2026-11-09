using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scoreboard.Core;

/// <summary>
/// A ScorePlus Icon file (<c>.spi</c>) carries one made icon and all of its data states between
/// machines. It is plain, indented JSON, like the other ScorePlus interchange files. Imports get
/// fresh ids for the icon and every state, so opening the same file twice adds two independent
/// library entries instead of silently replacing the first one.
/// </summary>
public static class IconFiles
{
    public const string Extension = "spi";
    public const int CurrentVersion = 1;
    public const string Filter = "ScorePlus Icon (*.spi)|*.spi|All files (*.*)|*.*";

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

    /// <summary>What a .spi file holds: one icon, plus enough header to say what wrote it.</summary>
    public sealed class IconFile
    {
        public string Format { get; set; } = "ScorePlus Icon";
        public int Version { get; set; } = CurrentVersion;
        public string Product { get; set; } = "ScorePlus";
        public string SavedBy { get; set; } = ProductVersion();
        public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
        public CustomIcon? Icon { get; set; }
    }

    static string ProductVersion()
    {
        try { return typeof(IconFiles).Assembly.GetName().Version?.ToString() ?? "1.0.0"; }
        catch { return "1.0.0"; }
    }

    /// <summary>Writes one icon, including its fallback look and every data state, as JSON.</summary>
    public static string Serialize(CustomIcon icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        var copy = icon.Copy();
        copy.Normalize();
        return JsonSerializer.Serialize(new IconFile { Icon = copy }, Write);
    }

    /// <summary>
    /// Reads one icon. A bare icon object is accepted too, so a hand-written or older file still
    /// opens. The returned copy has fresh ids, ready to be added to the library.
    /// </summary>
    public static CustomIcon? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        CustomIcon? icon = null;
        try
        {
            var file = JsonSerializer.Deserialize<IconFile>(json, Read);
            icon = file?.Icon;
        }
        catch { }

        if (icon is null)
        {
            try { icon = JsonSerializer.Deserialize<CustomIcon>(json, Read); }
            catch { return null; }
        }
        if (icon is null) return null;

        icon.Normalize();
        icon.Id = Guid.NewGuid().ToString("N");
        foreach (var state in icon.States) state.Id = Guid.NewGuid().ToString("N");
        return icon;
    }

    public static void Save(string path, CustomIcon icon)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
        WriteAtomically(path, Serialize(icon));
    }

    public static CustomIcon? Load(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            return Parse(File.ReadAllText(path));
        }
        catch { return null; }
    }

    /// <summary>Writes beside the target first, so an interrupted save never leaves half a file.</summary>
    static void WriteAtomically(string path, string content)
    {
        var full = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        var temporary = full + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, full, true);
    }
}
