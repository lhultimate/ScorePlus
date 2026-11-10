using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scoreboard.Core;

/// <summary>
/// One designed look for a made icon while a data condition is true — for example, the away
/// possession icon, or the home possession icon. It carries its own glyph, plate, colour,
/// position, size, rotation and opacity, so switching possession can change the whole mark.
/// </summary>
public sealed class CustomIconState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New data state";

    /// <summary>The game value that selects this design, such as Possession equals Away.</summary>
    public AutomationCondition Condition { get; set; } = new()
    {
        Field = AutomationField.PossessionSide,
        Operator = AutomationOperator.Equal,
        Text = "Away"
    };

    public string Glyph { get; set; } = "★";
    public string Color { get; set; } = "#FFFFFF";
    public string Plate { get; set; } = "#00000000";
    public bool Round { get; set; } = true;

    /// <summary>Where the icon sits inside its scorebug element, in design pixels.</summary>
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }

    /// <summary>1 is the natural size; 2 doubles the mark, 0.5 halves it.</summary>
    public double Scale { get; set; } = 1;

    /// <summary>How solid the whole mark is, 0–1.</summary>
    public double Opacity { get; set; } = 1;

    public double Rotation { get; set; }

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Name) ? DescribeCondition() : $"{DescribeCondition()} · {Name}";

    public bool Matches(GameState? state) => state is not null && Condition is not null && Condition.Matches(state);

    public CustomIconState Copy() =>
        JsonSerializer.Deserialize<CustomIconState>(JsonSerializer.Serialize(this)) ?? new CustomIconState();

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N");
        Name = (Name ?? string.Empty).Trim();
        Condition ??= new AutomationCondition();
        if (!Enum.IsDefined(Condition.Field)) Condition.Field = AutomationField.PossessionSide;
        var allowed = AutomationRules.OperatorsFor(Condition.Field);
        if (!allowed.Any(option => option.Operator == Condition.Operator))
            Condition.Operator = allowed[0].Operator;
        Condition.Value = double.IsFinite(Condition.Value) ? Condition.Value : 0;
        Condition.Text = (Condition.Text ?? "").Trim();
        Condition.Key = (Condition.Key ?? "").Trim();
        Glyph = string.IsNullOrWhiteSpace(Glyph) ? "★" : Glyph.Trim();
        if (Glyph.Length > 3) Glyph = Glyph[..3];
        Color = CustomIcon.Hex(Color, "#FFFFFF");
        Plate = CustomIcon.Hex(Plate, "#00000000");
        OffsetX = double.IsFinite(OffsetX) ? Math.Clamp(OffsetX, -2000, 2000) : 0;
        OffsetY = double.IsFinite(OffsetY) ? Math.Clamp(OffsetY, -2000, 2000) : 0;
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, 0.05, 8) : 1;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0, 1) : 1;
        Rotation = double.IsFinite(Rotation) ? Math.Clamp(Rotation, -360, 360) : 0;
    }

    public override string ToString() => Label;

    public string DescribeCondition()
    {
        var field = AutomationRules.FieldLabel(Condition.Field);
        var op = AutomationRules.OperatorLabel(Condition.Operator);
        var value = AutomationRules.IsBooleanField(Condition.Field)
            ? ""
            : AutomationRules.IsTextField(Condition.Field)
                ? $" {Condition.Text}"
                : $" {Condition.Value:0.##}";
        return $"{field} {op}{value}";
    }
}

/// <summary>What a made icon draws at this moment, after any matching data state is chosen.</summary>
public sealed record CustomIconLook(
    string Glyph,
    string Color,
    string Plate,
    bool Round,
    double OffsetX,
    double OffsetY,
    double Scale,
    double Opacity,
    double Rotation);

/// <summary>
/// An icon the operator drew: a glyph, its colour and the plate behind it. Icons live outside the
/// game, like the roster library, so a house icon survives a new game and travels inside the layout
/// that uses it. A data state can replace the whole look when the game value it names is true.
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

    /// <summary>Where the fallback look sits inside its scorebug element, in design pixels.</summary>
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Scale { get; set; } = 1;
    public double Opacity { get; set; } = 1;
    public double Rotation { get; set; }

    /// <summary>
    /// Designed looks selected by live data. The first matching state wins; when none match, the
    /// fallback look above is used.
    /// </summary>
    public List<CustomIconState> States { get; set; } = [];

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Name) ? Glyph : Name;

    /// <summary>What lists show: before this they printed the type name, "Scoreboard.Core.CustomIcon".</summary>
    public override string ToString() => Label;

    public CustomIcon Copy() =>
        JsonSerializer.Deserialize<CustomIcon>(JsonSerializer.Serialize(this)) ?? new CustomIcon();

    /// <summary>Chooses the first data state that matches the game, or the fallback look.</summary>
    public CustomIconLook Resolve(GameState? state)
    {
        if (state is not null)
        {
            foreach (var candidate in States ?? [])
            {
                if (candidate is null || !candidate.Matches(state)) continue;
                return new CustomIconLook(candidate.Glyph, candidate.Color, candidate.Plate, candidate.Round,
                    candidate.OffsetX, candidate.OffsetY, candidate.Scale, candidate.Opacity, candidate.Rotation);
            }
        }
        return new CustomIconLook(Glyph, Color, Plate, Round, OffsetX, OffsetY, Scale, Opacity, Rotation);
    }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N");
        Name = string.IsNullOrWhiteSpace(Name) ? "Icon" : Name.Trim();
        Glyph = string.IsNullOrWhiteSpace(Glyph) ? "★" : Glyph.Trim();
        if (Glyph.Length > 3) Glyph = Glyph[..3];
        Color = Hex(Color, "#FFFFFF");
        Plate = Hex(Plate, "#00000000");
        OffsetX = double.IsFinite(OffsetX) ? Math.Clamp(OffsetX, -2000, 2000) : 0;
        OffsetY = double.IsFinite(OffsetY) ? Math.Clamp(OffsetY, -2000, 2000) : 0;
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, 0.05, 8) : 1;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0, 1) : 1;
        Rotation = double.IsFinite(Rotation) ? Math.Clamp(Rotation, -360, 360) : 0;
        States ??= [];
        States = States.Where(state => state is not null).Take(48).ToList();
        foreach (var state in States) state.Normalize();
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
/// The icon library (<c>%LOCALAPPDATA%\ScorePlus\icons.json</c>): every icon made in the Icon Creator,
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
            existing.OffsetX = icon.OffsetX;
            existing.OffsetY = icon.OffsetY;
            existing.Scale = icon.Scale;
            existing.Opacity = icon.Opacity;
            existing.Rotation = icon.Rotation;
            existing.States = icon.States.Select(state => state.Copy()).ToList();
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
