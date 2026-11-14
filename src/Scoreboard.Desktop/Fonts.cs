using System.IO;
using Avalonia.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>One choice in the studio's font picker. <see cref="Source"/> is what an element stores.</summary>
public sealed record FontChoice(string Label, string Source, bool Imported = false)
{
    public override string ToString() => Imported ? $"{Label}  ·  imported" : Label;
}

/// <summary>
/// Every font the studio can put on a graphic: the fonts that ship with Windows (well over the
/// twenty-five asked for), plus any family the operator imports from a .ttf/.otf file. Imported
/// fonts are copied into the app's Fonts folder so a saved layout still finds them.
/// </summary>
public static class FontCatalog
{
    public const string Fallback = "Segoe UI";

    // Ordered first in the picker: the faces a broadcast operator reaches for, all shipped with Windows.
    static readonly string[] Preferred =
    [
        "Segoe UI", "Segoe UI Semibold", "Segoe UI Black", "Arial", "Arial Black", "Arial Narrow",
        "Bahnschrift", "Calibri", "Cambria", "Candara", "Comic Sans MS", "Consolas", "Constantia",
        "Corbel", "Courier New", "Franklin Gothic Medium", "Gabriola", "Georgia", "Impact",
        "Lucida Console", "Lucida Sans Unicode", "Malgun Gothic", "Microsoft Sans Serif",
        "Palatino Linotype", "Segoe Print", "Segoe Script", "Sitka Text", "Sylfaen", "Tahoma",
        "Times New Roman", "Trebuchet MS", "Verdana"
    ];

    static List<FontChoice>? _cache;

    public static string Folder => AppPaths.Combine("Fonts");

    public static IReadOnlyList<FontChoice> All => _cache ??= Build();

    /// <summary>Rebuilds the list after a font file was imported.</summary>
    public static void Refresh() => _cache = null;

    static List<FontChoice> Build()
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { foreach (var family in Fonts.SystemFontFamilies) installed.Add(family.Source); }
        catch { /* a font subsystem failure must not empty the picker */ }

        var choices = new List<FontChoice>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Preferred)
            if (installed.Contains(name) && used.Add(name))
                choices.Add(new FontChoice(name, name));
        foreach (var name in installed.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            if (used.Add(name))
                choices.Add(new FontChoice(name, name));
        if (choices.Count == 0) choices.Add(new FontChoice(Fallback, Fallback));

        choices.AddRange(ImportedFonts());
        return choices;
    }

    /// <summary>The font families found in the app's Fonts folder, as file-backed choices.</summary>
    static IEnumerable<FontChoice> ImportedFonts()
    {
        if (!Directory.Exists(Folder)) yield break;
        foreach (var path in Directory.EnumerateFiles(Folder, "*.*")
                     .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".ttf" or ".otf" or ".ttc")
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            FontFamily? family = null;
            try { family = Fonts.GetFontFamilies(new Uri(path)).FirstOrDefault(); }
            catch { /* a damaged file is skipped, not fatal */ }
            if (family is null) continue;
            var label = family.Source.Contains('#') ? family.Source[(family.Source.LastIndexOf('#') + 1)..] : Path.GetFileNameWithoutExtension(path);
            yield return new FontChoice(label, family.Source, Imported: true);
        }
    }

    /// <summary>Copies a font file in and returns the value an element should store.</summary>
    public static string Import(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".ttf" or ".otf" or ".ttc")) throw new InvalidOperationException("Choose a .ttf, .otf or .ttc font file.");
        Directory.CreateDirectory(Folder);
        var target = Path.Combine(Folder, $"{Path.GetFileNameWithoutExtension(path)}-{Guid.NewGuid():N}{extension}");
        File.Copy(path, target);
        var family = Fonts.GetFontFamilies(new Uri(target)).FirstOrDefault()
            ?? throw new InvalidOperationException("That file does not contain a font Windows can read.");
        Refresh();
        return family.Source;
    }

    public static FontFamily Resolve(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return new FontFamily(Fallback);
        try { return new FontFamily(source); }
        catch { return new FontFamily(Fallback); }
    }

    /// <summary>The picker entry for a stored source, even when the font was never in the list.</summary>
    public static FontChoice ChoiceFor(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return All[0];
        return All.FirstOrDefault(c => string.Equals(c.Source, source, StringComparison.OrdinalIgnoreCase))
            ?? new FontChoice(source.Contains('#') ? source[(source.LastIndexOf('#') + 1)..] : source, source, true);
    }
}
