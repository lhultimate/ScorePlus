using System.IO;
using System.Text.Json;

namespace Scoreboard.Core;

/// <summary>
/// An icon the operator drew: a glyph, its colour and the plate behind it. Icons live outside the
/// game, like the roster library, so a house icon survives a new game and travels inside the layout
/// that uses it.
/// </summary>
public sealed class CustomIcon
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New icon";

    /// <summary>One to three characters — a star, a ball, a flag — drawn as the icon itself.</summary>
    public string Glyph { get; set; } = "★";

    /// <summary>The glyph's colour.</summary>
    public string Color { get; set; } = "#FFFFFF";

    /// <summary>The plate behind the glyph. A fully transparent plate leaves the glyph alone.</summary>
    public string Plate { get; set; } = "#00000000";

    /// <summary>A round plate, the way a badge reads; otherwise a soft-cornered square.</summary>
    public bool Round { get; set; } = true;

    public string Label => string.IsNullOrWhiteSpace(Name) ? Glyph : Name;

    /// <summary>What lists show: before this they printed the type name, "Scoreboard.Core.CustomIcon".</summary>
    public override string ToString() => Label;

    public CustomIcon Copy() => (CustomIcon)MemberwiseClone();

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N");
        Name = string.IsNullOrWhiteSpace(Name) ? "Icon" : Name.Trim();
        Glyph = string.IsNullOrWhiteSpace(Glyph) ? "★" : Glyph.Trim();
        if (Glyph.Length > 3) Glyph = Glyph[..3];
        Color = Hex(Color, "#FFFFFF");
        Plate = Hex(Plate, "#00000000");
    }

    /// <summary>Takes <c>#RRGGBB</c>, <c>#AARRGGBB</c> or the same without the hash, or a fallback.</summary>
    public static string Hex(string? value, string fallback)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return fallback;
        if (!text.StartsWith('#')) text = "#" + text;
        if (text.Length is 7 or 9 && text[1..].All(Uri.IsHexDigit)) return text.ToUpperInvariant();
        return fallback;
    }
}

/// <summary>What is written to <c>icons.json</c>.</summary>
public sealed class CustomIconFile
{
    public List<CustomIcon> Icons { get; set; } = [];
}

/// <summary>
/// The icon library (<c>%LOCALAPPDATA%\ScorePlus\icons.json</c>): every icon made in the icon maker,
/// available to every layout. An icon used by a layout also travels inside that layout, so an export
/// is never missing its artwork.
/// </summary>
public static class CustomIconStore
{
    static CustomIconFile? _library;
    static string? _overridePath;

    /// <summary>Test hook: aim storage at a throwaway file so a suite never rewrites the real library.</summary>
    public static void UsePath(string? path)
    {
        _overridePath = path;
        _library = null;
    }

    /// <summary>Raised after an icon is saved or removed, so an open maker and studio follow it.</summary>
    public static event Action? Changed;

    static string FilePath => _overridePath ?? AppPaths.Combine("icons.json");

    static CustomIconFile Library => _library ??= Load();

    static CustomIconFile Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new CustomIconFile();
            var library = JsonSerializer.Deserialize<CustomIconFile>(File.ReadAllText(FilePath)) ?? new CustomIconFile();
            library.Icons ??= [];
            foreach (var icon in library.Icons) icon?.Normalize();
            library.Icons.RemoveAll(icon => icon is null);
            return library;
        }
        catch
        {
            // A damaged library must never stop a show; start empty rather than crash.
            return new CustomIconFile();
        }
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Library, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true);
        }
        catch
        {
            // Storage trouble is reported by the maker's own line; drawing carries on.
        }
        Changed?.Invoke();
    }

    /// <summary>Every icon, by name — what the studio's picker lists.</summary>
    public static IReadOnlyList<CustomIcon> All() => Library.Icons
        .OrderBy(icon => icon.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static CustomIcon? Find(string? id) => string.IsNullOrWhiteSpace(id)
        ? null
        : Library.Icons.FirstOrDefault(icon => string.Equals(icon.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adds an icon or updates the one with the same id, so saving an edit twice never leaves two
    /// copies. Returns the stored icon, whose id the caller keeps on the element.
    /// </summary>
    public static CustomIcon Save(CustomIcon icon)
    {
        icon.Normalize();
        var existing = Find(icon.Id);
        if (existing is null) Library.Icons.Add(icon);
        else
        {
            existing.Name = icon.Name;
            existing.Glyph = icon.Glyph;
            existing.Color = icon.Color;
            existing.Plate = icon.Plate;
            existing.Round = icon.Round;
        }
        Save();
        return Find(icon.Id) ?? icon;
    }

    public static void Remove(string? id)
    {
        if (Find(id) is { } icon)
        {
            Library.Icons.Remove(icon);
            Save();
        }
    }
}
