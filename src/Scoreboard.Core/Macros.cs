using System.Text.Json.Serialization;

namespace Scoreboard.Core;

/// <summary>One operation in a macro. Steps run in list order when the macro button is pressed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MacroActionKind
{
    Score,
    SetDown,
    SetTimeouts,
    SetGameClock,
    SetPlayClock,
    SetTimeoutClock,
    SetPeriod,
    PlayVideo,
    RosterStat,
    /// <summary>Any button on the operator console, identified by its action id.</summary>
    Control,
    /// <summary>A sport-specific counter or flag, set to an exact value or changed by a delta.</summary>
    SportValue,
    /// <summary>Football: the yards needed for a first down.</summary>
    SetFootballDistance,
    /// <summary>Football: the ball's yard line, from 0 to 50.</summary>
    SetFootballBallSpot,
    /// <summary>Which side has possession.</summary>
    SetPossession,
    /// <summary>A custom value from the console's values card.</summary>
    CustomValue
}

/// <summary>A single console, media or roster change saved inside a macro.</summary>
public sealed class MacroAction
{
    public MacroActionKind Kind { get; set; }

    /// <summary>True for the home side, false for the visitors.</summary>
    public bool Home { get; set; } = true;

    /// <summary>Points to add for a score action.</summary>
    public int Amount { get; set; } = 1;

    /// <summary>The target value for a set action, or the amount for a roster stat.</summary>
    public int Value { get; set; }

    /// <summary>Add to the current roster stat instead of replacing it.</summary>
    public bool Delta { get; set; }

    /// <summary>The video path, console action id, or sport-control id for this step.</summary>
    public string Path { get; set; } = "";
    /// <summary>A readable name for a console action or sport value, kept for the step list.</summary>
    public string Label { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string StatName { get; set; } = "";

    public MacroAction Copy() => new()
    {
        Kind = Kind,
        Home = Home,
        Amount = Amount,
        Value = Value,
        Delta = Delta,
        Path = Path,
        Label = Label,
        PlayerId = PlayerId,
        PlayerName = PlayerName,
        StatName = StatName
    };

    public void Normalize()
    {
        Path = (Path ?? "").Trim();
        Label = (Label ?? "").Trim();
        PlayerId = (PlayerId ?? "").Trim();
        PlayerName = (PlayerName ?? "").Trim();
        StatName = (StatName ?? "").Trim();
        Amount = Math.Clamp(Amount, -999, 999);
        Value = Math.Clamp(Value, -999, 9999);
    }

    [JsonIgnore]
    public string Summary => Kind switch
    {
        MacroActionKind.Score => $"{(Home ? "Home" : "Visitors")} score {(Amount >= 0 ? "+" : "−")}{Math.Abs(Amount)}",
        MacroActionKind.SetDown => $"Down {Value}",
        MacroActionKind.SetTimeouts => $"{(Home ? "Home" : "Visitors")} timeouts {Value}",
        MacroActionKind.SetGameClock => $"Game clock {Value}s",
        MacroActionKind.SetPlayClock => $"Play clock {Value}s",
        MacroActionKind.SetTimeoutClock => $"Timeout clock {Value}s",
        MacroActionKind.SetPeriod => $"Period {Value}",
        MacroActionKind.PlayVideo => $"Play {System.IO.Path.GetFileName(Path)}",
        MacroActionKind.RosterStat => $"{(Home ? "Home" : "Visitors")} · {PlayerName} · {StatName} {(Delta ? (Value >= 0 ? "+" : "−") + Math.Abs(Value) : "= " + Value)}",
        MacroActionKind.Control => $"Console · {(string.IsNullOrWhiteSpace(Label) ? Path : Label)}",
        MacroActionKind.SportValue => $"{(string.IsNullOrWhiteSpace(Label) ? Path : Label)} {(Delta ? (Value >= 0 ? "+ " : "− ") + Math.Abs(Value) : "= " + Value)}",
        MacroActionKind.SetFootballDistance => $"Distance to go · {Value} yd",
        MacroActionKind.SetFootballBallSpot => $"Ball spot · {Value} yd",
        MacroActionKind.SetPossession => $"{(Home ? "Home" : "Visitors")} possession",
        MacroActionKind.CustomValue => $"{(string.IsNullOrWhiteSpace(Label) ? Path : Label)} {(Delta ? (Value >= 0 ? "+ " : "− ") + Math.Abs(Value) : "= " + Value)}",
        _ => Kind.ToString()
    };
}

/// <summary>A row of macro buttons saved with the game.</summary>
public sealed class SavedMacro
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "New macro";
    /// <summary>Optional button colour, stored as a hex string. Empty uses the theme accent.</summary>
    public string Color { get; set; } = "";
    public List<MacroAction> Actions { get; set; } = [];

    public SavedMacro Copy() => new()
    {
        Id = Id,
        Label = Label,
        Color = Color,
        Actions = (Actions ?? []).Select(action => action.Copy()).ToList()
    };

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N");
        Label = (Label ?? "").Trim();
        if (Label.Length == 0) Label = "Macro";
        Color = (Color ?? "").Trim();
        // A malformed colour is not a reason to drop the macro; it just falls back to the accent.
        if (Color.Length is not (7 or 9) || Color[0] != '#' || !Color[1..].All(char.IsAsciiHexDigit)) Color = "";
        Actions ??= [];
        Actions = Actions.Where(action => action is not null).Take(80).ToList();
        foreach (var action in Actions) action.Normalize();
    }

    [JsonIgnore]
    public int ActionCount => Actions?.Count ?? 0;
}