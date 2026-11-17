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
    /// <summary>Switch one library source off air: a camera, microphone, speaker or playing clip.</summary>
    TakeOffAir,
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
    CustomValue,
    /// <summary>Pause before running the next step, in seconds.</summary>
    Wait,
    /// <summary>Put a library clip or live source on air without restarting a source already live.</summary>
    PutOnAir,
    /// <summary>Switch a display to a layout. The layout goes on air immediately.</summary>
    SwitchLayout,
    /// <summary>Raise or clear a referee flag for one side.</summary>
    PenaltyFlag
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

    /// <summary>
    /// Let this step publish its change immediately. When false the step stays staged until
    /// Update Output is pressed. Media steps always bypass, because playback is never staged.
    /// </summary>
    public bool BypassOutput { get; set; }

    /// <summary>The video path, console action id, or sport-control id for this step.</summary>
    public string Path { get; set; } = "";
    /// <summary>A readable name for a console action or sport value, kept for the step list.</summary>
    public string Label { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string StatName { get; set; } = "";
    /// <summary>The display a SwitchLayout step targets.</summary>
    public string DisplayId { get; set; } = "";

    /// <summary>How long a Wait step pauses before the next step runs.</summary>
    public double WaitSeconds { get; set; } = 1;

    public MacroAction Copy() => new()
    {
        Kind = Kind,
        Home = Home,
        Amount = Amount,
        Value = Value,
        Delta = Delta,
        BypassOutput = BypassOutput,
        Path = Path,
        Label = Label,
        DisplayId = DisplayId,
        PlayerId = PlayerId,
        PlayerName = PlayerName,
        StatName = StatName,
        WaitSeconds = WaitSeconds
    };

    public void Normalize()
    {
        Path = (Path ?? "").Trim();
        Label = (Label ?? "").Trim();
        DisplayId = (DisplayId ?? "").Trim();
        PlayerId = (PlayerId ?? "").Trim();
        PlayerName = (PlayerName ?? "").Trim();
        StatName = (StatName ?? "").Trim();
        Amount = Math.Clamp(Amount, -999, 999);
        Value = Math.Clamp(Value, -999, 9999);
        WaitSeconds = Math.Clamp(double.IsFinite(WaitSeconds) ? WaitSeconds : 1, 0, 60);
    }

    [JsonIgnore]
    public string Summary => Kind switch
    {
        MacroActionKind.Score => $"{(Home ? "Home" : "Visitors")} score {(Amount >= 0 ? "+" : "−")}{Math.Abs(Amount)}",
        MacroActionKind.SetDown => $"Down {Value}",
        MacroActionKind.SetTimeouts => $"{(Home ? "Home" : "Visitors")} timeouts {Value}",
        MacroActionKind.SetGameClock => $"Game clock {GameEngine.FormatClock(Value)}",
        MacroActionKind.SetPlayClock => $"Play clock {Value}s",
        MacroActionKind.SetTimeoutClock => $"Timeout clock {Value}s",
        MacroActionKind.SetPeriod => $"Period {Value}",
        MacroActionKind.PlayVideo => $"Media · {(string.IsNullOrWhiteSpace(Label) ? System.IO.Path.GetFileName(Path) : Label)}",
        MacroActionKind.TakeOffAir => $"Off air · {(string.IsNullOrWhiteSpace(Label) ? System.IO.Path.GetFileName(Path) : Label)}",
        MacroActionKind.RosterStat => $"{(Home ? "Home" : "Visitors")} · {PlayerName} · {StatName} {(Delta ? (Value >= 0 ? "+" : "−") + Math.Abs(Value) : "= " + Value)}",
        MacroActionKind.Control => $"Console · {(string.IsNullOrWhiteSpace(Label) ? Path : Label)}",
        MacroActionKind.SportValue => $"{(string.IsNullOrWhiteSpace(Label) ? Path : Label)} {(Delta ? (Value >= 0 ? "+ " : "− ") + Math.Abs(Value) : "= " + Value)}",
        MacroActionKind.SetFootballDistance => $"Distance to go · {Value} yd",
        MacroActionKind.SetFootballBallSpot => $"Ball spot · {Value} yd",
        MacroActionKind.SetPossession => $"{(Home ? "Home" : "Visitors")} possession",
        MacroActionKind.CustomValue => $"{(string.IsNullOrWhiteSpace(Label) ? Path : Label)} {(Delta ? (Value >= 0 ? "+ " : "− ") + Math.Abs(Value) : "= " + Value)}",
        MacroActionKind.Wait => $"Wait {WaitSeconds:0.##}s",
        MacroActionKind.PutOnAir => $"On air · {(string.IsNullOrWhiteSpace(Label) ? System.IO.Path.GetFileName(Path) : Label)}",
        MacroActionKind.SwitchLayout => $"Layout · {(string.IsNullOrWhiteSpace(Label) ? Path : Label)}",
        MacroActionKind.PenaltyFlag => Value == 0
            ? $"Clear flag · {(Home ? "Home" : "Visitors")}"
            : $"Flag down · {(Home ? "Home" : "Visitors")} · {(string.IsNullOrWhiteSpace(Path) ? "flag" : Path)}",
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
    /// <summary>Legacy row-break hint for boards saved before explicit grid cells.</summary>
    public bool StartNewRow { get; set; }
    /// <summary>The board row this button occupies. -1 means place it automatically.</summary>
    public int Row { get; set; } = -1;
    /// <summary>The board column this button occupies. -1 means place it automatically.</summary>
    public int Column { get; set; } = -1;
    /// <summary>The board page this macro belongs to. Page 0 is the first page.</summary>
    public int Page { get; set; }
    public List<MacroAction> Actions { get; set; } = [];

    public SavedMacro Copy() => new()
    {
        Id = Id,
        Label = Label,
        Color = Color,
        StartNewRow = StartNewRow,
        Row = Row,
        Column = Column,
        Page = Page,
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
        Row = Math.Clamp(Row, -1, 999);
        Column = Math.Clamp(Column, -1, 999);
        Page = Math.Clamp(Page, 0, 98);
        Actions ??= [];
        Actions = Actions.Where(action => action is not null).Take(80).ToList();
        foreach (var action in Actions) action.Normalize();
    }

    [JsonIgnore]
    public int ActionCount => Actions?.Count ?? 0;
}