using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public sealed record ControlActionDefinition(string Id, string Group, string Label);

public static class ControlActions
{
    public static IReadOnlyList<ControlActionDefinition> All { get; } = Build();

    /// <summary>
    /// The fixed console actions plus every sport value's +, − and switch, so the keybind list offers
    /// "Visitors hits +1" or "Power play on / off" alongside the football controls.
    /// </summary>
    static List<ControlActionDefinition> Build()
    {
        var list = CoreActions().ToList();
        foreach (var control in SportControls.All)
        {
            var group = GroupFor(control.Id);
            var label = $"{control.Label}";
            if (control.Kind == SportControlKind.Flag)
                list.Add(new($"sport.{control.Id}.toggle", group, $"{label} on / off"));
            else
            {
                list.Add(new($"sport.{control.Id}.plus", group, $"{label} +1"));
                list.Add(new($"sport.{control.Id}.minus", group, $"{label} −1"));
            }
        }
        return list;
    }

    /// <summary>The sport a control id belongs to, as a readable group name for the keybind list.</summary>
    static string GroupFor(string id)
    {
        var dot = id.IndexOf('.');
        var key = dot > 0 ? id[..dot] : id;
        return SportDefinition.BuiltIns.FirstOrDefault(s => string.Equals(s.Id, key, StringComparison.OrdinalIgnoreCase))?.Name
            ?? char.ToUpperInvariant(key[0]) + key[1..];
    }

    static IEnumerable<ControlActionDefinition> CoreActions() =>
    [
        new("home.score.plus1","Score","Home score +1"),
        new("home.score.plus2","Score","Home score +2"),
        new("home.score.plus3","Score","Home score +3"),
        new("home.score.plus6","Score","Home touchdown +6"),
        new("home.score.minus1","Score","Home score −1"),
        new("home.score.minus2","Score","Home score −2"),
        new("home.score.minus3","Score","Home score −3"),
        new("home.score.minus6","Score","Home touchdown −6"),
        new("away.score.plus1","Score","Away score +1"),
        new("away.score.plus2","Score","Away score +2"),
        new("away.score.plus3","Score","Away score +3"),
        new("away.score.plus6","Score","Away touchdown +6"),
        new("away.score.minus1","Score","Away score −1"),
        new("away.score.minus2","Score","Away score −2"),
        new("away.score.minus3","Score","Away score −3"),
        new("away.score.minus6","Score","Away touchdown −6"),
        new("home.timeout","Score","Home timeout used"),
        new("away.timeout","Score","Away timeout used"),
        new("home.timeout.plus","Score","Home timeouts +1"),
        new("away.timeout.plus","Score","Away timeouts +1"),
        new("possession.toggle","Score","Toggle possession"),
        new("possession.away","Score","Visitors possession"),
        new("possession.home","Score","Home possession"),
        new("clock.toggle","Clock","Start / stop clock"),
        new("clock.minus60","Clock","Clock −1 minute"),
        new("clock.plus60","Clock","Clock +1 minute"),
        new("clock.minus1","Clock","Clock −1 second"),
        new("clock.plus1","Clock","Clock +1 second"),
        new("clock.set","Clock","Set clock"),
        new("period.next","Clock","Next period"),
        new("period.previous","Clock","Previous period"),
        new("football.playclock.toggle","Clock","Play clock start / stop"),
        new("football.playclock.reset40","Clock","Play clock reset 40"),
        new("football.playclock.reset25","Clock","Play clock reset 25"),
        new("football.playclock.minus","Clock","Play clock −1 second"),
        new("football.playclock.plus","Clock","Play clock +1 second"),
        new("football.playclock.set","Clock","Set play clock"),
        new("timeoutclock.toggle","Clock","Timeout clock start / stop"),
        new("timeoutclock.reset","Clock","Timeout clock reset 1:00"),
        new("timeoutclock.minus10","Clock","Timeout clock −10 seconds"),
        new("timeoutclock.minus1","Clock","Timeout clock −1 second"),
        new("timeoutclock.plus1","Clock","Timeout clock +1 second"),
        new("timeoutclock.plus10","Clock","Timeout clock +10 seconds"),
        new("timeoutclock.set","Clock","Set timeout clock"),
        new("football.down.next","Football","Next down"),
        new("football.down.previous","Football","Previous down"),
        new("football.distance.minus","Football","Distance −1 yard"),
        new("football.distance.plus","Football","Distance +1 yard"),
        new("football.ball.minus","Football","Ball spot −1 yard"),
        new("football.ball.plus","Football","Ball spot +1 yard"),
        new("football.ball.side","Football","Ball spot side (home / visitors)"),
        new("football.series.new","Football","New series"),
        new("football.down.recall","Football","Recall last down"),
        new("penalty.toggle","Football","Flag down on / off"),
        new("output.publish","Output","Update Output (publish staged changes)"),
        new("output.live.toggle","Output","Live score & clocks on / off"),
        new("basketball.foul.home","Basketball","Home foul"),
        new("basketball.foul.away","Basketball","Away foul"),
        new("basketball.shotclock.toggle","Basketball","Shot clock start / stop"),
        new("basketball.shotclock.reset","Basketball","Shot clock reset 24"),
        new("basketball.shotclock.minus","Basketball","Shot clock −1 second"),
        new("basketball.shotclock.plus","Basketball","Shot clock +1 second"),
        new("basketball.shotclock.set","Basketball","Set shot clock"),
        new("baseball.ball","Baseball","Ball"),
        new("baseball.strike","Baseball","Strike"),
        new("baseball.foul","Baseball","Foul ball"),
        new("baseball.out","Baseball","Out"),
        new("baseball.hit.home","Baseball","Home hit"),
        new("baseball.hit.away","Baseball","Away hit"),
        new("soccer.yellow.home","Soccer","Home yellow card"),
        new("soccer.yellow.away","Soccer","Away yellow card"),
        new("soccer.red.home","Soccer","Home red card"),
        new("soccer.red.away","Soccer","Away red card"),
        new("soccer.shot.home","Soccer","Home shot"),
        new("soccer.shot.away","Soccer","Away shot"),
        new("soccer.corner.home","Soccer","Home corner"),
        new("soccer.corner.away","Soccer","Away corner"),
        new("global.undo","General","Undo last action")
    ];

    /// <summary>
    /// The actions worth showing for one sport: the console's shared buttons, the clocks and the
    /// output, plus that sport's own data. A baseball console never offers "Power play on / off",
    /// and a hockey one never offers downs, so the keybind list stays about the game in front of you.
    /// </summary>
    public static IReadOnlyList<ControlActionDefinition> ForSport(string? sportId)
    {
        if (string.IsNullOrWhiteSpace(sportId)) return All;
        return All.Where(action => SportOf(action.Id) is not { } sport
            || string.Equals(sport, sportId, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    /// <summary>
    /// The sport an action belongs to, or null for the shared console. Two shapes exist: the fixed
    /// core actions start with the sport — "baseball.ball", "football.down.next" — and the sport
    /// data buttons are named "sport.hockey.powerplay.toggle", with the profile id in the middle.
    /// </summary>
    static string? SportOf(string id)
    {
        var parts = id.Split('.');
        if (parts.Length < 2) return null;
        if (string.Equals(parts[0], "sport", StringComparison.OrdinalIgnoreCase)) return parts[1];
        return SportDefinition.BuiltIns.Any(sport => string.Equals(sport.Id, parts[0], StringComparison.OrdinalIgnoreCase))
            ? parts[0]
            : null;
    }

    public static string LabelFor(string id) => All.FirstOrDefault(a => a.Id == id)?.Label ?? ScoreLabel(id) ?? id;

    /// <summary>Score buttons are built from a sport's scoring options, so names like +7 exist without an entry above.</summary>
    static string? ScoreLabel(string id)
    {
        foreach (var (prefix, team) in new[] { ("home.score.", "Home"), ("away.score.", "Away") })
        {
            if (!id.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = id[prefix.Length..];
            if (rest.StartsWith("plus", StringComparison.Ordinal) && int.TryParse(rest[4..], out var plus)) return $"{team} score +{plus}";
            if (rest.StartsWith("minus", StringComparison.Ordinal) && int.TryParse(rest[5..], out var minus)) return $"{team} score −{minus}";
        }
        return null;
    }
}

public sealed class KeybindSettings
{
    public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public KeybindSettings Clone() => new() { Bindings = new Dictionary<string, string>(Bindings, StringComparer.OrdinalIgnoreCase) };
}

public static class KeybindStore
{
    static readonly string FilePath = AppPaths.Combine("keybinds.json");

    // Shortcuts older builds installed on first run. Nobody chose these, so a file holding exactly
    // this set is treated as untouched and cleared; anything the operator changed is kept.
    static readonly (string Action, string Gesture)[] LegacyDefaults =
    [
        ("clock.toggle", "Space"),
        ("football.down.next", "F1"),
        ("football.down.previous", "F2"),
        ("output.publish", "F5"),
        ("global.undo", "Ctrl+Z")
    ];

    public static KeybindSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return Defaults();
            var stored = JsonSerializer.Deserialize<KeybindSettings>(File.ReadAllText(FilePath)) ?? Defaults();
            return IsLegacyDefaults(stored) ? new() : stored;
        }
        catch { return new(); }
    }

    static bool IsLegacyDefaults(KeybindSettings settings) =>
        settings.Bindings.Count == LegacyDefaults.Length &&
        LegacyDefaults.All(d => settings.Bindings.TryGetValue(d.Action, out var gesture)
            && string.Equals(gesture, d.Gesture, StringComparison.OrdinalIgnoreCase));

    public static void Save(KeybindSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }

    public static string ToText(Key key, ModifierKeys modifiers)
    {
        var text = "";
        if ((modifiers & ModifierKeys.Control) != 0) text += "Ctrl+";
        if ((modifiers & ModifierKeys.Shift) != 0) text += "Shift+";
        if ((modifiers & ModifierKeys.Alt) != 0) text += "Alt+";
        if ((modifiers & ModifierKeys.Windows) != 0) text += "Win+";
        return text + KeyName(key);
    }

    public static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        Key.OemTilde => "`",
        Key.Back => "Backspace",
        Key.Escape => "Esc",
        Key.Return => "Enter",
        _ => key.ToString()
    };

    public static bool TryParse(string text, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None; modifiers = ModifierKeys.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default: return false;
            }
        }
        var name = parts[^1];
        if (name.Length == 1 && char.IsDigit(name[0])) { key = (Key)((int)Key.D0 + (name[0] - '0')); return true; }
        switch (name)
        {
            case "-": key = Key.OemMinus; return true;
            case "=": key = Key.OemPlus; return true;
            case ",": key = Key.OemComma; return true;
            case ".": key = Key.OemPeriod; return true;
            case "/": key = Key.OemQuestion; return true;
            case ";": key = Key.OemSemicolon; return true;
            case "'": key = Key.OemQuotes; return true;
            case "[": key = Key.OemOpenBrackets; return true;
            case "]": key = Key.OemCloseBrackets; return true;
            case "\\": key = Key.OemPipe; return true;
            case "`": key = Key.OemTilde; return true;
            case "Backspace": key = Key.Back; return true;
            case "Esc": key = Key.Escape; return true;
            case "Enter": key = Key.Return; return true;
        }
        if (string.Equals(name, "Space", StringComparison.OrdinalIgnoreCase)) { key = Key.Space; return true; }
        if (!Enum.TryParse<Key>(name, true, out var parsed)) return false;
        key = parsed;
        return true;
    }

    public static string? Match(KeybindSettings settings, Key key, ModifierKeys modifiers)
    {
        foreach (var (actionId, gesture) in settings.Bindings)
        {
            if (!TryParse(gesture, out var boundKey, out var boundModifiers)) continue;
            if (boundKey == key && boundModifiers == modifiers) return actionId;
        }
        return null;
    }

    /// <summary>No shortcuts out of the box: every binding is the operator's own choice.</summary>
    public static KeybindSettings Defaults() => new();
}
