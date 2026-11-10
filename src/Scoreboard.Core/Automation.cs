using System.Text.Json;

namespace Scoreboard.Core;

/// <summary>What a rule looks at in the game state.</summary>
public enum AutomationField
{
    PlayClock,
    GameClock,
    ShotClock,
    TimeoutClock,
    Period,
    HomeScore,
    AwayScore,
    HomeTimeouts,
    AwayTimeouts,
    Down,
    YardsToGo,
    HomeFouls,
    AwayFouls,
    Balls,
    Strikes,
    Outs,
    HomePossession,
    ClockRunning,
    PlayClockRunning,
    Sport,
    CustomStat,
    // Added after the first release of rules so existing numeric enum values never move.
    StoppageClock,
    StoppageRunning,
    ShotClockRunning,
    TimeoutClockRunning,
    HomeTimeoutLimit,
    AwayTimeoutLimit,
    YardsToGoal,
    BallOnHomeSide,
    DownPending,
    HomeYellowCards,
    AwayYellowCards,
    HomeRedCards,
    AwayRedCards,
    HomeHits,
    AwayHits,
    HomeYards,
    AwayYards,
    HomeTurnovers,
    AwayTurnovers,
    HomeShots,
    AwayShots,
    HomeCorners,
    AwayCorners,
    PenaltyActive,
    PenaltyType,
    PenaltyHome,
    GameEnded,
    RecordSettled,
    /// <summary>Who has the ball, as the word Home or Away.</summary>
    PossessionSide
}

public enum AutomationOperator
{
    Less,
    LessOrEqual,
    Equal,
    NotEqual,
    GreaterOrEqual,
    Greater,
    Contains,
    IsTrue,
    IsFalse
}

/// <summary>What a matched rule does. Element actions target one element by its stable id.</summary>
public enum AutomationActionKind
{
    SetTextColor,
    SetBackgroundColor,
    SetBorderColor,
    SetOpacity,
    MoveBy,
    MoveTo,
    Resize,
    Show,
    Hide,
    SetText,
    SwitchLayout
}

/// <summary>When an action runs in a rule whose conditions include a wait-until step.</summary>
public enum AutomationActionPhase
{
    OnMatch,
    AfterWait,
    Else
}

/// <summary>
/// One condition in a rule. Numeric fields use <see cref="Value"/>; the sport name and a custom
/// stat's name use <see cref="Text"/> and <see cref="Key"/>.
/// </summary>
public sealed class AutomationCondition
{
    public AutomationField Field { get; set; }
    public AutomationOperator Operator { get; set; } = AutomationOperator.Less;
    public double Value { get; set; }
    public string Text { get; set; } = "";
    public string Key { get; set; } = "";

    /// <summary>Hold this condition until the matched rule reaches its wait step.</summary>
    public bool WaitUntil { get; set; }

    public bool Matches(GameState state)
    {
        var number = AutomationRules.ReadNumber(this, state);
        var text = AutomationRules.ReadText(this, state);
        return Operator switch
        {
            AutomationOperator.Less => number < Value,
            AutomationOperator.LessOrEqual => number <= Value,
            AutomationOperator.Equal => AutomationRules.IsTextField(Field)
                ? string.Equals(text, Text.Trim(), StringComparison.OrdinalIgnoreCase)
                : Math.Abs(number - Value) < 0.0001,
            AutomationOperator.NotEqual => AutomationRules.IsTextField(Field)
                ? !string.Equals(text, Text.Trim(), StringComparison.OrdinalIgnoreCase)
                : Math.Abs(number - Value) >= 0.0001,
            AutomationOperator.GreaterOrEqual => number >= Value,
            AutomationOperator.Greater => number > Value,
            AutomationOperator.Contains => text.Contains(Text.Trim(), StringComparison.OrdinalIgnoreCase),
            AutomationOperator.IsTrue => number > 0,
            AutomationOperator.IsFalse => number <= 0,
            _ => false
        };
    }

    public AutomationCondition Copy() => new()
    {
        Field = Field,
        Operator = Operator,
        Value = Value,
        Text = Text,
        Key = Key,
        WaitUntil = WaitUntil
    };
}

/// <summary>
/// One action in a rule. <see cref="ElementId"/> is the element to change; <see cref="LayoutId"/>
/// is the layout that contains it. SwitchLayout uses <see cref="LayoutId"/> only.
/// </summary>
public sealed class AutomationAction
{
    public AutomationActionKind Kind { get; set; } = AutomationActionKind.SetTextColor;

    /// <summary>When this action runs: on the trigger, after the wait, or as the else branch.</summary>
    public AutomationActionPhase Phase { get; set; }

    public string LayoutId { get; set; } = "";
    public string ElementId { get; set; } = "";

    /// <summary>Colour, text, or the layout id when the action is SwitchLayout.</summary>
    public string Value { get; set; } = "#FFFF3B30";

    /// <summary>Where a colour action gets its colour: a fixed value or a team palette slot.</summary>
    public ScorebugColorSource ColorSource { get; set; } = ScorebugColorSource.Custom;

    /// <summary>The first number: opacity, x, width, and so on.</summary>
    public double Number { get; set; } = 1;

    /// <summary>The second number: y for MoveBy/MoveTo, height for Resize.</summary>
    public double Number2 { get; set; }

    public AutomationAction Copy() => new()
    {
        Kind = Kind,
        Phase = Phase,
        LayoutId = LayoutId,
        ElementId = ElementId,
        Value = Value,
        ColorSource = ColorSource,
        Number = Number,
        Number2 = Number2
    };
}

/// <summary>
/// A node graph rule: all (or any) conditions in the middle, then the actions on the right.
/// Rules travel with their display, so an automation survives a saved game and an .spd export.
/// </summary>
public sealed class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New rule";
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// This rule reads the live game and writes its result to the output immediately, instead of
    /// waiting for Update Output to publish the state and layout first.
    /// </summary>
    public bool BypassOutputUpdate { get; set; }

    public bool MatchAny { get; set; }
    public List<AutomationCondition> Conditions { get; set; } = [];
    public List<AutomationAction> Actions { get; set; } = [];

    /// <summary>True when the rule's trigger conditions match. Wait-until conditions are ignored here.</summary>
    public bool Matches(GameState state) => MatchesPhase(state, waitUntil: false);

    /// <summary>True when every configured wait-until condition matches, or there are none.</summary>
    public bool MatchesWait(GameState state) => MatchesPhase(state, waitUntil: true);

    bool MatchesPhase(GameState state, bool waitUntil)
    {
        var selected = (Conditions ?? []).Where(condition => condition.WaitUntil == waitUntil).ToArray();
        if (selected.Length == 0) return true;
        return MatchAny
            ? selected.Any(condition => condition.Matches(state))
            : selected.All(condition => condition.Matches(state));
    }

    public AutomationRule Copy() =>
        JsonSerializer.Deserialize<AutomationRule>(JsonSerializer.Serialize(this)) ?? new AutomationRule();
}

public static class AutomationRules
{
    public static readonly (AutomationField Field, string Label)[] Fields =
    [
        (AutomationField.PlayClock, "Play clock"),
        (AutomationField.GameClock, "Game clock"),
        (AutomationField.ShotClock, "Shot clock"),
        (AutomationField.TimeoutClock, "Timeout clock"),
        (AutomationField.Period, "Period"),
        (AutomationField.HomeScore, "Home score"),
        (AutomationField.AwayScore, "Visitors score"),
        (AutomationField.HomeTimeouts, "Home timeouts"),
        (AutomationField.AwayTimeouts, "Visitors timeouts"),
        (AutomationField.Down, "Down"),
        (AutomationField.YardsToGo, "Yards to go"),
        (AutomationField.HomeFouls, "Home fouls"),
        (AutomationField.AwayFouls, "Visitors fouls"),
        (AutomationField.Balls, "Balls"),
        (AutomationField.Strikes, "Strikes"),
        (AutomationField.Outs, "Outs"),
        (AutomationField.HomePossession, "Home has possession"),
        (AutomationField.PossessionSide, "Possession"),
        (AutomationField.ClockRunning, "Game clock running"),
        (AutomationField.PlayClockRunning, "Play clock running"),
        (AutomationField.Sport, "Sport"),
        (AutomationField.CustomStat, "Custom value"),
        (AutomationField.StoppageClock, "Added time"),
        (AutomationField.StoppageRunning, "Added time running"),
        (AutomationField.ShotClockRunning, "Shot clock running"),
        (AutomationField.TimeoutClockRunning, "Timeout clock running"),
        (AutomationField.HomeTimeoutLimit, "Home timeouts at start"),
        (AutomationField.AwayTimeoutLimit, "Visitors timeouts at start"),
        (AutomationField.YardsToGoal, "Ball on yard line"),
        (AutomationField.BallOnHomeSide, "Ball on home half"),
        (AutomationField.DownPending, "Down is waiting for distance"),
        (AutomationField.HomeYellowCards, "Home yellow cards"),
        (AutomationField.AwayYellowCards, "Visitors yellow cards"),
        (AutomationField.HomeRedCards, "Home red cards"),
        (AutomationField.AwayRedCards, "Visitors red cards"),
        (AutomationField.HomeHits, "Home hits"),
        (AutomationField.AwayHits, "Visitors hits"),
        (AutomationField.HomeYards, "Home yards"),
        (AutomationField.AwayYards, "Visitors yards"),
        (AutomationField.HomeTurnovers, "Home turnovers"),
        (AutomationField.AwayTurnovers, "Visitors turnovers"),
        (AutomationField.HomeShots, "Home shots"),
        (AutomationField.AwayShots, "Visitors shots"),
        (AutomationField.HomeCorners, "Home corners"),
        (AutomationField.AwayCorners, "Visitors corners"),
        (AutomationField.PenaltyActive, "Flag is down"),
        (AutomationField.PenaltyType, "Flag type"),
        (AutomationField.PenaltyHome, "Flag is on home team"),
        (AutomationField.GameEnded, "Game ended"),
        (AutomationField.RecordSettled, "Result recorded")
    ];

    public static readonly (AutomationOperator Operator, string Label)[] Operators =
    [
        (AutomationOperator.Greater, "greater than"),
        (AutomationOperator.GreaterOrEqual, "greater than or equal to"),
        (AutomationOperator.Equal, "equal to"),
        (AutomationOperator.NotEqual, "not equal to"),
        (AutomationOperator.LessOrEqual, "less than or equal to"),
        (AutomationOperator.Less, "less than"),
        (AutomationOperator.Contains, "contains"),
        (AutomationOperator.IsTrue, "is true"),
        (AutomationOperator.IsFalse, "is false")
    ];

    /// <summary>Colour choices offered by a colour action, including both teams' palettes.</summary>
    public static readonly (ScorebugColorSource Source, string Label)[] ColorSources =
    [
        (ScorebugColorSource.Custom, "Custom colour"),
        (ScorebugColorSource.HomePrimary, "Home primary"),
        (ScorebugColorSource.HomeSecondary, "Home secondary"),
        (ScorebugColorSource.AwayPrimary, "Away primary"),
        (ScorebugColorSource.AwaySecondary, "Away secondary")
    ];

    public static readonly (AutomationActionPhase Phase, string Label)[] ActionPhases =
    [
        (AutomationActionPhase.OnMatch, "When matched"),
        (AutomationActionPhase.AfterWait, "After wait"),
        (AutomationActionPhase.Else, "Else")
    ];

    public static readonly (AutomationActionKind Kind, string Label)[] Actions =
    [
        (AutomationActionKind.SetTextColor, "Text color"),
        (AutomationActionKind.SetBackgroundColor, "Background color"),
        (AutomationActionKind.SetBorderColor, "Border color"),
        (AutomationActionKind.SetOpacity, "Opacity"),
        (AutomationActionKind.MoveBy, "Move by"),
        (AutomationActionKind.MoveTo, "Move to"),
        (AutomationActionKind.Resize, "Resize"),
        (AutomationActionKind.Show, "Show"),
        (AutomationActionKind.Hide, "Hide"),
        (AutomationActionKind.SetText, "Set text"),
        (AutomationActionKind.SwitchLayout, "Switch layout")
    ];

    public static string FieldLabel(AutomationField field) =>
        Fields.FirstOrDefault(option => option.Field == field).Label ?? field.ToString();

    public static string ActionLabel(AutomationActionKind kind) =>
        Actions.FirstOrDefault(option => option.Kind == kind).Label ?? kind.ToString();

    public static string OperatorLabel(AutomationOperator op) =>
        Operators.FirstOrDefault(option => option.Operator == op).Label ?? op.ToString();

    public static bool IsBooleanField(AutomationField field) => field is
        AutomationField.HomePossession or AutomationField.ClockRunning or AutomationField.PlayClockRunning
        or AutomationField.StoppageRunning or AutomationField.ShotClockRunning
        or AutomationField.TimeoutClockRunning or AutomationField.BallOnHomeSide
        or AutomationField.DownPending or AutomationField.PenaltyActive
        or AutomationField.PenaltyHome or AutomationField.GameEnded or AutomationField.RecordSettled;

    public static bool IsTextField(AutomationField field) =>
        field is AutomationField.Sport or AutomationField.PenaltyType or AutomationField.PossessionSide;

    public static bool IsNumericField(AutomationField field) => !IsBooleanField(field) && !IsTextField(field);

    /// <summary>A short explanation beside a yes/no condition, so the card is never just a blank.</summary>
    public static string BooleanHint(AutomationField field) => field switch
    {
        AutomationField.HomePossession => "True when the home team has the ball.",
        AutomationField.PenaltyActive => "True while a flag is down.",
        AutomationField.PenaltyHome => "True when the flag is against the home team.",
        AutomationField.BallOnHomeSide => "True when the ball is on the home team's half.",
        AutomationField.DownPending => "True between a new down and its distance being set.",
        AutomationField.ClockRunning => "True while the game clock is running.",
        AutomationField.PlayClockRunning => "True while the play clock is running.",
        AutomationField.ShotClockRunning => "True while the shot clock is running.",
        AutomationField.StoppageRunning => "True while added time is running.",
        AutomationField.TimeoutClockRunning => "True while the timeout clock is running.",
        AutomationField.GameEnded => "True after End Game or the final whistle.",
        AutomationField.RecordSettled => "True once the result has been added to the records.",
        _ => "True while this condition is running."
    };

    /// <summary>The operator list shown for a field, so a boolean never offers “is below”.</summary>
    public static IReadOnlyList<(AutomationOperator Operator, string Label)> OperatorsFor(AutomationField field)
    {
        if (IsBooleanField(field))
            return [(AutomationOperator.IsTrue, "is true"), (AutomationOperator.IsFalse, "is false")];
        if (IsTextField(field))
            return [(AutomationOperator.Equal, "equal to"), (AutomationOperator.NotEqual, "not equal to"),
                (AutomationOperator.Contains, "contains")];
        return Operators.Where(option => option.Operator is not (AutomationOperator.Contains
            or AutomationOperator.IsTrue or AutomationOperator.IsFalse)).ToArray();
    }

    public static double ReadNumber(AutomationCondition condition, GameState state) => condition.Field switch
    {
        AutomationField.PlayClock => state.PlayClockSeconds,
        AutomationField.GameClock => state.ClockSeconds,
        AutomationField.ShotClock => state.ShotClockSeconds,
        AutomationField.TimeoutClock => state.TimeoutClockSeconds,
        AutomationField.Period => state.Period,
        AutomationField.HomeScore => state.HomeScore,
        AutomationField.AwayScore => state.AwayScore,
        AutomationField.HomeTimeouts => state.HomeTimeouts,
        AutomationField.AwayTimeouts => state.AwayTimeouts,
        AutomationField.Down => state.Down,
        AutomationField.YardsToGo => state.YardsToGo,
        AutomationField.HomeFouls => state.HomeFouls,
        AutomationField.AwayFouls => state.AwayFouls,
        AutomationField.Balls => state.Balls,
        AutomationField.Strikes => state.Strikes,
        AutomationField.Outs => state.Outs,
        AutomationField.HomePossession => state.HomePossession ? 1 : 0,
        AutomationField.ClockRunning => state.ClockRunning ? 1 : 0,
        AutomationField.PlayClockRunning => state.PlayClockRunning ? 1 : 0,
        AutomationField.CustomStat => ReadStat(condition.Key, state),
        AutomationField.StoppageClock => state.StoppageSeconds,
        AutomationField.StoppageRunning => state.StoppageRunning ? 1 : 0,
        AutomationField.ShotClockRunning => state.ShotClockRunning ? 1 : 0,
        AutomationField.TimeoutClockRunning => state.TimeoutClockRunning ? 1 : 0,
        AutomationField.HomeTimeoutLimit => state.HomeTimeoutLimit,
        AutomationField.AwayTimeoutLimit => state.AwayTimeoutLimit,
        AutomationField.YardsToGoal => state.YardsToGoal,
        AutomationField.BallOnHomeSide => state.BallOnHomeSide ? 1 : 0,
        AutomationField.DownPending => state.DownPending ? 1 : 0,
        AutomationField.HomeYellowCards => state.HomeYellowCards,
        AutomationField.AwayYellowCards => state.AwayYellowCards,
        AutomationField.HomeRedCards => state.HomeRedCards,
        AutomationField.AwayRedCards => state.AwayRedCards,
        AutomationField.HomeHits => state.HomeHits,
        AutomationField.AwayHits => state.AwayHits,
        AutomationField.HomeYards => state.HomeYards,
        AutomationField.AwayYards => state.AwayYards,
        AutomationField.HomeTurnovers => state.HomeTurnovers,
        AutomationField.AwayTurnovers => state.AwayTurnovers,
        AutomationField.HomeShots => state.HomeShots,
        AutomationField.AwayShots => state.AwayShots,
        AutomationField.HomeCorners => state.HomeCorners,
        AutomationField.AwayCorners => state.AwayCorners,
        AutomationField.PenaltyActive => state.PenaltyActive ? 1 : 0,
        AutomationField.PenaltyHome => state.PenaltyHome ? 1 : 0,
        AutomationField.GameEnded => state.GameEnded ? 1 : 0,
        AutomationField.RecordSettled => state.RecordSettled ? 1 : 0,
        _ => 0
    };

    public static string ReadText(AutomationCondition condition, GameState state) => condition.Field switch
    {
        AutomationField.Sport => state.SportId,
        AutomationField.PenaltyType => state.PenaltyType,
        AutomationField.PossessionSide => state.HomePossession ? "Home" : "Away",
        AutomationField.CustomStat => state.Stats.FirstOrDefault(stat =>
            string.Equals(stat.Name, condition.Key, StringComparison.OrdinalIgnoreCase))?.Display ?? "",
        _ => ""
    };

    static double ReadStat(string name, GameState state)
    {
        var stat = state.Stats.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        return stat?.Value ?? 0;
    }

    public static List<AutomationRule> Normalize(IEnumerable<AutomationRule?>? rules)
    {
        var normalized = new List<AutomationRule>();
        foreach (var rule in (rules ?? []).OfType<AutomationRule>().Take(200))
        {
            rule.Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id.Trim();
            rule.Name = string.IsNullOrWhiteSpace(rule.Name) ? "Untitled rule" : rule.Name.Trim();
            rule.Conditions ??= [];
            rule.Actions ??= [];
            rule.Conditions = rule.Conditions.Where(c => c is not null).Take(24).Select(c => c.Copy()).ToList();
            rule.Actions = rule.Actions.Where(a => a is not null).Take(24).Select(a => a.Copy()).ToList();
            foreach (var condition in rule.Conditions)
            {
                if (!Enum.IsDefined(condition.Field)) condition.Field = AutomationField.PlayClock;
                var allowed = OperatorsFor(condition.Field);
                if (!allowed.Any(option => option.Operator == condition.Operator))
                    condition.Operator = allowed[0].Operator;
                condition.Value = double.IsFinite(condition.Value) ? condition.Value : 0;
                condition.Text = (condition.Text ?? "").Trim();
                condition.Key = (condition.Key ?? "").Trim();
            }
            foreach (var action in rule.Actions)
            {
                if (!Enum.IsDefined(action.Kind)) action.Kind = AutomationActionKind.SetTextColor;
                if (!Enum.IsDefined(action.Phase)) action.Phase = AutomationActionPhase.OnMatch;
                if (!Enum.IsDefined(action.ColorSource)) action.ColorSource = ScorebugColorSource.Custom;
                action.LayoutId = (action.LayoutId ?? "").Trim();
                action.ElementId = (action.ElementId ?? "").Trim();
                action.Value = (action.Value ?? "").Trim();
                action.Number = double.IsFinite(action.Number) ? action.Number : 0;
                action.Number2 = double.IsFinite(action.Number2) ? action.Number2 : 0;
                if ((action.Kind is AutomationActionKind.SetTextColor or AutomationActionKind.SetBackgroundColor
                    or AutomationActionKind.SetBorderColor) && action.ColorSource == ScorebugColorSource.Custom
                    && string.IsNullOrWhiteSpace(action.Value))
                    action.Value = "#FFFF3B30";
            }
            normalized.Add(rule);
        }
        return normalized;
    }

    /// <summary>The colour an action paints, resolving a team palette slot against the current game.</summary>
    public static string ResolveColor(AutomationAction action, GameState? state) =>
        action.ColorSource == ScorebugColorSource.Custom
            ? action.Value
            : state?.ColorFor(action.ColorSource) ?? action.Value;

    static string ColorLabel(AutomationAction action) =>
        action.ColorSource == ScorebugColorSource.Custom
            ? action.Value
            : ColorSources.FirstOrDefault(choice => choice.Source == action.ColorSource).Label ?? action.Value;

    public static string Describe(AutomationAction action, string? elementName = null)
    {
        var target = string.IsNullOrWhiteSpace(elementName) ? "element" : elementName;
        return action.Kind switch
        {
            AutomationActionKind.SetTextColor => $"{target} text → {ColorLabel(action)}",
            AutomationActionKind.SetBackgroundColor => $"{target} background → {ColorLabel(action)}",
            AutomationActionKind.SetBorderColor => $"{target} border → {ColorLabel(action)}",
            AutomationActionKind.SetOpacity => $"{target} opacity → {action.Number:0.##}",
            AutomationActionKind.MoveBy => $"{target} move → {action.Number:0.#}, {action.Number2:0.#}",
            AutomationActionKind.MoveTo => $"{target} move to {action.Number:0.#}, {action.Number2:0.#}",
            AutomationActionKind.Resize => $"{target} resize → {action.Number:0.#} × {action.Number2:0.#}",
            AutomationActionKind.Show => $"{target} show",
            AutomationActionKind.Hide => $"{target} hide",
            AutomationActionKind.SetText => $"{target} text → “{action.Value}”",
            AutomationActionKind.SwitchLayout => "switch layout",
            _ => action.Kind.ToString()
        };
    }
}
