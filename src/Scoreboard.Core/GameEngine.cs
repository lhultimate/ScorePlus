using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scoreboard.Core;

/// <summary>How football's down-and-distance line reads. The numeric down and distance are kept,
/// so switching back to yards restores the operator's last values.</summary>
public enum FootballDownMode
{
    Normal,
    Goal,
    Pat,
    TwoPoint,
    None
}

/// <summary>What replaces the game clock on the graphic.</summary>
public enum GameClockMessage
{
    Clock,
    Final,
    EndOfRegulation
}

public sealed record SportDefinition(string Id, string Name, string Unit, int Periods, int PeriodSeconds, int[] ScoringOptions, bool HasShotClock = false, bool CountUp = false, int Timeouts = 5)
{
    /// <summary>
    /// The sport's own values — balls, strikes and bases, power play, overs and wickets, sets — with
    /// the starting value a new game uses. Edited in Sport Profiles, copied onto the game when it starts.
    /// </summary>
    public SportControl[] Controls { get; init; } = [];

    public static IReadOnlyList<SportDefinition> BuiltIns { get; } = new[]
    {
        new SportDefinition("football", "American Football", "Quarter", 4, 15 * 60, [1, 2, 3, 6, 7, 8], true, false, 3) { Controls = [.. SportControls.For("football")] },
        new SportDefinition("australian-football", "Australian Football", "Quarter", 4, 20 * 60, [1, 6], false, false, 0) { Controls = [.. SportControls.For("australian-football")] },
        new SportDefinition("baseball", "Baseball", "Inning", 9, 0, [1], false, true, 0) { Controls = [.. SportControls.For("baseball")] },
        new SportDefinition("basketball", "Basketball", "Quarter", 4, 12 * 60, [1, 2, 3], true, false, 5) { Controls = [.. SportControls.For("basketball")] },
        new SportDefinition("cricket", "Cricket", "Innings", 2, 0, [1, 2, 3, 4, 5, 6], false, true, 0) { Controls = [.. SportControls.For("cricket")] },
        new SportDefinition("hockey", "Hockey", "Period", 3, 20 * 60, [1], false, false, 1) { Controls = [.. SportControls.For("hockey")] },
        new SportDefinition("lacrosse", "Lacrosse", "Quarter", 4, 12 * 60, [1], true, false, 3) { Controls = [.. SportControls.For("lacrosse")] },
        new SportDefinition("rugby", "Rugby", "Half", 2, 40 * 60, [2, 3, 5, 7], false, false, 3) { Controls = [.. SportControls.For("rugby")] },
        new SportDefinition("soccer", "Soccer", "Half", 2, 45 * 60, [1], false, true, 0) { Controls = [.. SportControls.For("soccer")] },
        new SportDefinition("tennis", "Tennis", "Set", 3, 0, [1], false, true, 0) { Controls = [.. SportControls.For("tennis")] },
        new SportDefinition("volleyball", "Volleyball", "Set", 5, 0, [1], false, true, 2) { Controls = [.. SportControls.For("volleyball")] },
        new SportDefinition("wrestling", "Wrestling", "Period", 3, 3 * 60, [1, 2, 3], false, false, 0) { Controls = [.. SportControls.For("wrestling")] },
        // The blank starting point: everything editable in Sport Profiles.
        new SportDefinition("generic", "Custom", "Period", 2, 10 * 60, [1, 2, 3], false, false, 3)
    };
}

/// <summary>
/// A single custom value such as Turnovers or Penalties. A value is either a number (the
/// steppers work on it) or any free text — "N/A", "3rd", "TBD" — which is what the graphic prints.
/// </summary>
public sealed record GameStat(string Id, string Name, int Value = 0, string? Text = null)
{
    /// <summary>What the graphic and the report show: the typed text when there is one.</summary>
    public string Display => !string.IsNullOrWhiteSpace(Text) ? Text : Value.ToString();

    /// <summary>True when the stat holds a number, so +/− may step it.</summary>
    public bool IsNumber => string.IsNullOrWhiteSpace(Text);
}

public static class CommonStats
{
    /// <summary>Suggested value names offered when adding a custom value.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "Field goal attempts", "Field goal yards", "Turnovers", "Penalties", "Penalty yards",
        "Shots", "Shots on goal", "Saves", "Corners", "Rebounds", "Assists", "Steals", "Blocks",
        "Fouls", "Hits", "Errors", "Faceoff wins", "Possessions"
    ];

    /// <summary>
    /// The values a new game starts with. They are ordinary custom values: the operator can change,
    /// remove or add to them, and they are saved with the game like every other value.
    /// </summary>
    public static IReadOnlyList<string> DefaultsFor(string? sportId) => (sportId ?? "").Trim().ToLowerInvariant() switch
    {
        "football" => ["Field goal attempts", "Field goal yards", "Turnovers", "Penalties", "Penalty yards"],
        "australian-football" => ["Inside 50s", "Clearances", "Turnovers"],
        "basketball" => ["Turnovers", "Rebounds", "Assists", "Steals"],
        "baseball" => ["Errors", "Left on base", "Double plays"],
        "cricket" => ["Fours", "Sixes", "Extras"],
        "hockey" => ["Shots", "Power play goals", "Faceoff wins"],
        "lacrosse" => ["Shots", "Saves", "Faceoff wins"],
        "rugby" => ["Tries", "Conversions", "Turnovers"],
        "soccer" => ["Shots", "Shots on goal", "Corners", "Fouls"],
        "tennis" => ["Aces", "Double faults", "Break points"],
        "volleyball" => ["Aces", "Blocks", "Digs"],
        "wrestling" => ["Takedowns", "Escapes", "Near falls"],
        _ => ["Turnovers", "Penalties"]
    };
}

/// <summary>A quick-layout button captured with a saved game: its label and the layout it points at.</summary>
public sealed class SavedQuickLayout
{
    public string Label { get; set; } = "";
    public string DisplayId { get; set; } = "";
    public string LayoutId { get; set; } = "";
}

/// <summary>A quick-media button captured with a saved game: its label and library path.</summary>
public sealed class SavedQuickMedia
{
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class GameState
{
    public string SportId { get; set; } = "basketball";
    /// <summary>A free-form label for this fixture, shown on the live production bar and saved with the game.</summary>
    public string GameName { get; set; } = "";
    public string HomeTeam { get; set; } = "HOME";
    public string AwayTeam { get; set; } = "VISITORS";
    public string HomeAbbreviation { get; set; } = "HOME";
    public string AwayAbbreviation { get; set; } = "AWAY";
    /// <summary>The team's mascot, printed through the {{HOME_MASCOT}} / {{AWAY_MASCOT}} bindings.</summary>
    public string HomeMascot { get; set; } = "";
    public string AwayMascot { get; set; } = "";
    /// <summary>Season record typed in team details, e.g. "8-2" or "12-4-1". Printed by {{HOME_RECORD}}.</summary>
    public string HomeRecord { get; set; } = "";
    public string AwayRecord { get; set; } = "";
    /// <summary>Division record, printed inside brackets after the overall record: 4-2 (2-2).</summary>
    public string HomeDivisionRecord { get; set; } = "";
    public string AwayDivisionRecord { get; set; } = "";
    /// <summary>A ranking such as #21, typed in team details.</summary>
    public string HomeRanking { get; set; } = "";
    public string AwayRanking { get; set; } = "";
    /// <summary>When on, ending the game also applies the result to both division records.</summary>
    public bool DivisionGame { get; set; }

    /// <summary>The overall record with the division record in brackets, when one is set.</summary>
    [JsonIgnore]
    public string HomeRecordDisplay => CombineRecord(HomeRecord, HomeDivisionRecord);
    [JsonIgnore]
    public string AwayRecordDisplay => CombineRecord(AwayRecord, AwayDivisionRecord);

    static string CombineRecord(string overall, string division)
    {
        overall = (overall ?? "").Trim();
        division = (division ?? "").Trim();
        if (division.Length == 0) return overall;
        return overall.Length == 0 ? $"({division})" : $"{overall} ({division})";
    }
    /// <summary>The quick layout and media buttons in use when the game was saved, so opening it brings them back.</summary>
    public List<SavedQuickLayout> QuickLayouts { get; set; } = [];
    public List<SavedQuickMedia> QuickMedia { get; set; } = [];
    /// <summary>Saved macro buttons, with their ordered steps, so a game reopens with the show's shortcuts.</summary>
    public List<SavedMacro> Macros { get; set; } = [];
    /// <summary>True when this file carries the quick-button lists, including deliberately empty ones.</summary>
    public bool QuickButtonsSaved { get; set; }
    /// <summary>Team logo imported from the control panel. Graphics draw it, the studio never edits it.</summary>
    public string? HomeLogoPath { get; set; }
    public string? AwayLogoPath { get; set; }
    /// <summary>Team palette. Any colour on the scorebug can point at one of these four slots.</summary>
    public string HomePrimaryColor { get; set; } = "#FF38D7A3";
    public string HomeSecondaryColor { get; set; } = "#FF101820";
    public string AwayPrimaryColor { get; set; } = "#FF4EA8FF";
    public string AwaySecondaryColor { get; set; } = "#FF101820";
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    /// <summary>
    /// The last scoring button pressed, so an animation can say +3 only for a +3. This is the
    /// actual change after clamping (not the requested amount), and it survives a save.
    /// </summary>
    public int LastScoreAmount { get; set; }
    public bool LastScoreHome { get; set; } = true;
    /// <summary>
    /// Increases once for every scoring action. The graphic watches this instead of the score
    /// itself, so a clock update cannot replay the touchdown celebration and a score correction
    /// does not pretend to be one.
    /// </summary>
    public long LastScoreSequence { get; set; }
    [JsonIgnore]
    public int LastScoreMagnitude => Math.Abs(LastScoreAmount);
    [JsonIgnore]
    public string LastScoreTeam => LastScoreHome ? HomeTeam : AwayTeam;
    [JsonIgnore]
    public string LastScoreAbbreviation => LastScoreHome ? HomeAbbreviation : AwayAbbreviation;
    public int Period { get; set; } = 1;
    public int HomeTimeouts { get; set; } = 5;
    public int AwayTimeouts { get; set; } = 5;
    /// <summary>How many timeouts each team started with, so used ones can still be drawn.</summary>
    public int HomeTimeoutLimit { get; set; } = 5;
    public int AwayTimeoutLimit { get; set; } = 5;
    public int HomeFouls { get; set; }
    public int AwayFouls { get; set; }
    public bool HomePossession { get; set; } = true;
    public int ClockSeconds { get; set; } = 720;

    /// <summary>Soccer's added time, accrued by its own start/stop clock: how long the match runs past 90:00.</summary>
    public int StoppageSeconds { get; set; }
    public bool StoppageRunning { get; set; }
    public int PlayClockSeconds { get; set; } = 40;
    public int TimeoutClockSeconds { get; set; } = 60;
    public int PeriodSeconds { get; set; } = 720;
    public int TotalPeriods { get; set; } = 4;
    public string PeriodLabel { get; set; } = "Quarter";
    public int[] ScoringOptions { get; set; } = [1, 2, 3];
    public int ShotClockSeconds { get; set; } = 30;
    public int Down { get; set; } = 1;
    /// <summary>Normal, goal-to-go, PAT, two-point attempt, or no down line at all.</summary>
    public FootballDownMode FootballDownMode { get; set; }
    /// <summary>Clock, Final or End of Reg. on the graphic.</summary>
    public GameClockMessage ClockMessage { get; set; }

    /// <summary>The down alone, including the PAT and two-point labels.</summary>
    [JsonIgnore]
    public string DownDisplay => FootballDownMode switch
    {
        FootballDownMode.Pat => "PAT",
        FootballDownMode.TwoPoint => "2 PT ATT",
        FootballDownMode.None => "",
        _ => $"{Down}{Ordinal(Down)}"
    };

    /// <summary>The distance alone; Goal replaces a number after a first down at the goal line.</summary>
    [JsonIgnore]
    public string ToGoDisplay => FootballDownMode switch
    {
        FootballDownMode.Normal => YardsToGo.ToString(),
        FootballDownMode.Goal => GameEngine.UppercaseDownWordsEnabled ? "GOAL" : "Goal",
        _ => ""
    };

    /// <summary>The full down-and-distance line, blank when the operator chose none.</summary>
    [JsonIgnore]
    public string DownDistanceDisplay => FootballDownMode switch
    {
        FootballDownMode.Pat => "PAT",
        FootballDownMode.TwoPoint => "2 PT ATT",
        FootballDownMode.None => "",
        FootballDownMode.Goal => $"{DownDisplay} & {ToGoDisplay}",
        _ => DownPending ? $"{DownDisplay} {(GameEngine.UppercaseDownWordsEnabled ? "DOWN" : "Down")}" : $"{DownDisplay} & {ToGoDisplay}"
    };

    /// <summary>The words or time the audience clock carries.</summary>
    [JsonIgnore]
    public string GameClockText => ClockMessage switch
    {
        GameClockMessage.Final => "FINAL",
        GameClockMessage.EndOfRegulation => "END OF REG.",
        _ => GameEngine.FormatClock(ClockSeconds)
    };

    static string Ordinal(int value) => value switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

    public string QuarterDisplayFormat { get; set; } = "Full";
    public int YardsToGo { get; set; } = 10;
    /// <summary>Ball spot as a yard line, 0–50. Independent of who has possession.</summary>
    public int YardsToGoal { get; set; } = 35;
    /// <summary>Whose half of the field the ball spot sits on. Separate from possession, so the
    /// graphic can read "Home 37" while the visitors have the ball.</summary>
    public bool BallOnHomeSide { get; set; } = true;
    public int HomeYellowCards { get; set; }
    public int AwayYellowCards { get; set; }
    public int HomeRedCards { get; set; }
    public int AwayRedCards { get; set; }
    /// <summary>The console's penalty box: whether a flag is up, what it was for, and which side.</summary>
    public bool PenaltyActive { get; set; }
    public string PenaltyType { get; set; } = "Holding";
    public bool PenaltyHome { get; set; } = true;

    /// <summary>
    /// True between Next down and the moment the new distance is set or published, so the graphic
    /// can read "2nd Down" instead of printing the previous series' distance beside it.
    /// </summary>
    public bool DownPending { get; set; }
    public int Balls { get; set; }
    public int Strikes { get; set; }
    public int Outs { get; set; }
    public int HomeHits { get; set; }
    public int AwayHits { get; set; }
    public int HomeYards { get; set; }
    public int AwayYards { get; set; }
    public int HomeTurnovers { get; set; }
    public int AwayTurnovers { get; set; }
    public int HomeShots { get; set; }
    public int AwayShots { get; set; }
    public int HomeCorners { get; set; }
    public int AwayCorners { get; set; }
    /// <summary>
    /// The sport's own values (bases, power play, overs, sets…). Values that already have a field
    /// above — balls, strikes, fouls, cards — stay in that field, so old saves and templates still read.
    /// </summary>
    public Dictionary<string, int> SportValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The controls this game was started with, so an edited profile never rewrites a live game.</summary>
    public List<SportControl> Controls { get; set; } = [];
    public List<GameStat> Stats { get; set; } = [];
    /// <summary>The roster each side draws players from, chosen in the Rosters tab.</summary>
    public string? HomeRosterId { get; set; }
    public string? AwayRosterId { get; set; }
    /// <summary>The player currently on the graphic for each side, copied from the roster.</summary>
    public SelectedPlayer? HomePlayer { get; set; }
    public SelectedPlayer? AwayPlayer { get; set; }
    /// <summary>
    /// True when the operator deliberately put no player on the graphic for that side. Without
    /// this, reloading a roster would quietly put its first player back up.
    /// </summary>
    public bool HomePlayerCleared { get; set; }
    public bool AwayPlayerCleared { get; set; }
    public bool ClockRunning { get; set; }
    public bool ShotClockRunning { get; set; }
    public bool PlayClockRunning { get; set; }
    public bool TimeoutClockRunning { get; set; }
    public bool ClockCountsUp { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
    /// <summary>True once the operator explicitly ends the game; a new game clears it.</summary>
    public bool GameEnded { get; set; }
    /// <summary>True once this game's result has been added to the team records, so it is never counted twice.</summary>
    public bool RecordSettled { get; set; }
    public List<GameEvent> Events { get; set; } = [];

    /// <summary>The colour behind a team palette slot, or null for a fixed colour.</summary>
    public string? ColorFor(ScorebugColorSource source) => source switch
    {
        ScorebugColorSource.HomePrimary => Blank(HomePrimaryColor),
        ScorebugColorSource.HomeSecondary => Blank(HomeSecondaryColor),
        ScorebugColorSource.AwayPrimary => Blank(AwayPrimaryColor),
        ScorebugColorSource.AwaySecondary => Blank(AwaySecondaryColor),
        _ => null
    };

    /// <summary>
    /// An unset team colour is no colour at all: returning the empty string made every resolver
    /// treat it as a real value and paint the element transparent instead of using its own colour.
    /// </summary>
    static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public string? LogoFor(ScorebugLogoSide side) => side switch
    {
        ScorebugLogoSide.Home => HomeLogoPath,
        ScorebugLogoSide.Away => AwayLogoPath,
        _ => null
    };
}

public sealed record GameEvent(DateTimeOffset At, string Type, string Description, int? HomeScore = null, int? AwayScore = null);
public sealed record GameSnapshot(GameState State, string Label);

/// <summary>The two shapes a clock can print in: 09:59 (padded) or 9:59 / :59 (compact).</summary>
public enum ClockStyle { Padded, Compact }

public sealed class GameEngine
{
    /// <summary>What a flag can be for. Free text is not offered: a penalty is picked, never typed.</summary>
    public static readonly string[] PenaltyTypes =
    [
        "Offside", "False start", "Encroachment", "Neutral zone infraction", "Delay of game",
        "Illegal formation", "Illegal shift", "Illegal motion", "Ineligible receiver downfield",
        "Illegal block", "Holding", "Illegal use of hands", "Face mask", "Horse-collar tackle",
        "Personal foul", "Roughing the passer", "Roughing the kicker", "Running into the kicker",
        "Pass interference", "Defensive holding", "Illegal contact", "Intentional grounding",
        "Chop block", "Clipping", "Tripping", "Targeting", "Helmet-to-helmet",
        "Unnecessary roughness", "Unsportsmanlike conduct", "Taunting", "Too many men on the field",
        "Illegal substitution", "Kick catch interference", "Fair catch interference", "Leaping", "Other"
    ];

    /// <summary>Flags offered for a sport, from that sport's own rulebook; empty when it has none.</summary>
    public static string[] PenaltyTypesFor(string? sportId) =>
        sportId is not null && PenaltyTypesBySport.TryGetValue(sportId, out var types) ? types : [];

    static readonly Dictionary<string, string[]> PenaltyTypesBySport = new(StringComparer.OrdinalIgnoreCase)
    {
        ["football"] = PenaltyTypes,
        ["australian-football"] =
        [
            "Holding the ball", "Throw", "Deliberate out of bounds", "Push in the back", "High tackle",
            "Rough conduct", "Interference", "50-metre penalty", "Reportable offence", "Other"
        ],
        ["baseball"] =
        [
            "Balk", "Catcher's interference", "Batter interference", "Runner interference", "Obstruction",
            "Illegal pitch", "Hit by pitch", "Unsportsmanlike conduct", "Ejection", "Other"
        ],
        ["basketball"] =
        [
            "Personal foul", "Shooting foul", "Offensive foul", "Team foul", "Technical foul",
            "Flagrant foul", "Defensive three seconds", "Delay of game", "Unsportsmanlike conduct", "Other"
        ],
        ["cricket"] =
        [
            "No-ball", "Wide", "Bye", "Leg bye", "Dead ball", "Obstruction", "Hit wicket",
            "Time wasting", "Dangerous play", "Unsportsmanlike conduct", "Other"
        ],
        ["hockey"] =
        [
            "Green card", "Yellow card", "Red card", "Penalty corner", "Penalty stroke", "Obstruction",
            "Stick tackle", "Back of stick", "Foot", "Dangerous play", "Other"
        ],
        ["lacrosse"] =
        [
            "Slash", "Tripping", "Cross-check", "Illegal body check", "Holding", "Push", "Interference",
            "Offside", "Unsportsmanlike conduct", "Ejection", "Other"
        ],
        ["rugby"] =
        [
            "High tackle", "Offside", "Not releasing", "Not rolling away", "Scrum infringement",
            "Lineout infringement", "Dangerous play", "Foul play", "Deliberate knock-on", "Dissent", "Other"
        ],
        ["soccer"] =
        [
            "Foul", "Offside", "Handball", "Penalty kick", "Free kick", "Throw-in", "Corner", "Goal kick",
            "Yellow card", "Red card", "Simulation", "Dissent", "Other"
        ],
        ["tennis"] =
        [
            "Code violation", "Warning", "Point penalty", "Game penalty", "Default", "Hindrance",
            "Coaching", "Racquet abuse", "Unsportsmanlike conduct", "Other"
        ],
        ["volleyball"] =
        [
            "Double hit", "Lift", "Net touch", "Foot fault", "Rotation fault", "Delay",
            "Unsportsmanlike conduct", "Red card", "Other"
        ],
        ["wrestling"] =
        [
            "Stalling", "Fleeing", "Illegal hold", "Caution", "Injury time", "Blood time",
            "Unsportsmanlike conduct", "Other"
        ]
    };

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    readonly Stopwatch _watch = Stopwatch.StartNew();
    long _lastTick;
    // Time already elapsed toward each clock's next whole-second change. Keeping this across a
    // pause is what lets a clock stopped 0.1s before the next second resume and change 0.1s later.
    long _gameClockCarry, _playClockCarry, _shotClockCarry, _timeoutClockCarry, _stoppageClockCarry;
    readonly Stack<GameSnapshot> _undo = new();
    public SportCatalog Sports { get; }
    public GameState State { get; private set; } = new();
    public event Action? Changed;
    /// <summary>
    /// True while a hardware controller is feeding the clock. The device is authoritative then,
    /// so the local timer must not also tick the same clock down between snapshots.
    /// </summary>
    public bool HardwareControlled { get; set; }

    /// <summary>
    /// When on, the end of the final period (or full time in soccer) adds the result to each team's
    /// record. Off by default; a side with no record is left alone.
    /// </summary>
    public static bool AutoUpdateRecordEnabled { get; set; }

    public SportDefinition Sport => Sports.Find(State.SportId) ?? SportDefinition.BuiltIns[^1];

    public GameEngine(SportCatalog? sports = null) => Sports = sports ?? new SportCatalog();

    /// <param name="keepTeamIdentity">Keeps the teams from the game being replaced: names,
    /// abbreviations, mascots, records, logos, colours and the selected rosters and players. A sport
    /// switch is still a new game, but the teams do not change sports with it.</param>
    public void NewGame(string sportId, bool keepTeamIdentity = false)
    {
        var sport = Sports.Find(sportId) ?? SportDefinition.BuiltIns[^1];
        var previous = keepTeamIdentity ? State : null;
        // Football has three timeouts a side, whatever a legacy profile or saved game says.
        var timeouts = sport.Id == "football" ? Math.Clamp(sport.Timeouts, 0, 3) : sport.Timeouts;
        State = new GameState { SportId = sport.Id, // Soccer counts up from 0:00 to the goal time (90:00 for two 45-minute halves).
        ClockCountsUp = sport.CountUp || sport.Id == "soccer",
        // Soccer alone opens at 0:00 (it counts to 90:00); every other sport keeps its clock preset.
        ClockSeconds = sport.Id == "soccer" ? 0 : sport.PeriodSeconds,
        PeriodSeconds = sport.PeriodSeconds, TotalPeriods = sport.Periods, PeriodLabel = sport.Unit, ScoringOptions = sport.ScoringOptions.ToArray(), HomeTimeouts = timeouts, AwayTimeouts = timeouts, HomeTimeoutLimit = timeouts, AwayTimeoutLimit = timeouts, PlayClockSeconds = 40, TimeoutClockSeconds = 60, Controls = sport.Controls.Select(c => c with { }).ToList() };
        // New games open with useful custom values, so the console is not an empty card every time.
        foreach (var name in CommonStats.DefaultsFor(sport.Id))
            State.Stats.Add(new GameStat(Guid.NewGuid().ToString("N"), name));
        // A profile's starting values (a full count of balls to start, a power play already on) are
        // what a new game opens with; stepping them afterwards changes only this game.
        foreach (var control in State.Controls)
            SportValues.Write(State, control.Id, Math.Clamp(control.Default, control.Min, control.Max));
        if (previous is not null)
        {
            State.HomeTeam = previous.HomeTeam; State.AwayTeam = previous.AwayTeam;
            State.HomeAbbreviation = previous.HomeAbbreviation; State.AwayAbbreviation = previous.AwayAbbreviation;
            State.HomeMascot = previous.HomeMascot; State.AwayMascot = previous.AwayMascot;
            State.HomeRecord = previous.HomeRecord; State.AwayRecord = previous.AwayRecord;
            State.HomeDivisionRecord = previous.HomeDivisionRecord; State.AwayDivisionRecord = previous.AwayDivisionRecord;
            State.HomeRanking = previous.HomeRanking; State.AwayRanking = previous.AwayRanking;
            State.DivisionGame = previous.DivisionGame;
            State.HomeLogoPath = previous.HomeLogoPath; State.AwayLogoPath = previous.AwayLogoPath;
            State.HomePrimaryColor = previous.HomePrimaryColor; State.HomeSecondaryColor = previous.HomeSecondaryColor;
            State.AwayPrimaryColor = previous.AwayPrimaryColor; State.AwaySecondaryColor = previous.AwaySecondaryColor;
            State.HomeRosterId = previous.HomeRosterId; State.AwayRosterId = previous.AwayRosterId;
            State.HomePlayer = CopyPlayer(previous.HomePlayer); State.AwayPlayer = CopyPlayer(previous.AwayPlayer);
            State.HomePlayerCleared = previous.HomePlayerCleared; State.AwayPlayerCleared = previous.AwayPlayerCleared;
        }
        _undo.Clear(); ResetClockCarries(); _lastTick = _watch.ElapsedTicks; Changed?.Invoke();
    }

    /// <summary>Sets one of the sport's own values, clamped to what its control allows.</summary>
    public void SetSportValue(string id, int value)
    {
        var control = SportValues.FindControl(State, id);
        if (control is null) return;
        var clamped = Math.Clamp(value, control.Min, Math.Max(control.Min, control.Max));
        if (SportValues.Read(State, control.Id) == clamped) return;
        SaveUndo(control.Label);
        SportValues.Write(State, control.Id, clamped);
        Log("Sport", $"{control.Label}: {SportValues.Display(State, control)}");
        Changed?.Invoke();
    }

    /// <summary>Steps a sport value by its own step, the way a console button does.</summary>
    public void AdjustSportValue(string id, int direction)
    {
        var control = SportValues.FindControl(State, id);
        if (control is null) return;
        var step = control.Kind == SportControlKind.Flag ? 1 : Math.Max(1, control.Step);
        SetSportValue(control.Id, SportValues.Read(State, control.Id) + direction * step);
    }

    /// <summary>Flips an on/off sport control such as power play or a runner on base.</summary>
    public void ToggleSportFlag(string id)
    {
        var control = SportValues.FindControl(State, id);
        if (control is null || control.Kind != SportControlKind.Flag) return;
        SetSportValue(control.Id, SportValues.Read(State, control.Id) == 0 ? 1 : 0);
    }

    /// <summary>Labels the fixture without changing the sport, teams or score.</summary>
    public void SetGameName(string? name)
    {
        var clean = (name ?? "").Trim();
        if (clean.Length > 120) clean = clean[..120];
        if (State.GameName == clean) return;
        State.GameName = clean;
        Changed?.Invoke();
    }

    public void SetTeams(string home, string away)
    {
        SaveUndo("Teams"); State.HomeTeam = home; State.AwayTeam = away;
        State.HomeAbbreviation = Abbreviate(home); State.AwayAbbreviation = Abbreviate(away); Log("Teams", $"{home} vs {away}"); Changed?.Invoke();
    }

    /// <summary>
    /// Saves everything the control panel's team pop-up edits: name, abbreviation, logo and both
    /// palette colours. Team identity publishes live like the score, so the graphic never waits.
    /// </summary>
    public void SetTeamIdentity(bool home, string name, string abbreviation, string? logoPath, string primary, string secondary, string? mascot = null, string? record = null, string? ranking = null, string? divisionRecord = null)
    {
        name = name.Trim();
        if (name.Length == 0) name = home ? State.HomeTeam : State.AwayTeam;
        abbreviation = abbreviation.Trim().ToUpperInvariant();
        if (abbreviation.Length == 0) abbreviation = Abbreviate(name);
        mascot = (mascot ?? "").Trim();
        record = (record ?? "").Trim();
        ranking = NormalizeRanking(ranking);
        divisionRecord = (divisionRecord ?? "").Trim();
        primary = NormalizeColor(primary, home ? "#FF38D7A3" : "#FF4EA8FF");
        secondary = NormalizeColor(secondary, "#FF101820");
        if (home)
        {
            if (State.HomeTeam == name && State.HomeAbbreviation == abbreviation && State.HomeLogoPath == logoPath
                && State.HomeMascot == mascot && State.HomeRecord == record && State.HomeRanking == ranking
                && State.HomeDivisionRecord == divisionRecord
                && State.HomePrimaryColor == primary && State.HomeSecondaryColor == secondary) return;
            SaveUndo("Home team details");
            State.HomeTeam = name; State.HomeAbbreviation = abbreviation; State.HomeLogoPath = logoPath;
            State.HomeMascot = mascot; State.HomeRecord = record; State.HomeRanking = ranking;
            State.HomeDivisionRecord = divisionRecord;
            State.HomePrimaryColor = primary; State.HomeSecondaryColor = secondary;
        }
        else
        {
            if (State.AwayTeam == name && State.AwayAbbreviation == abbreviation && State.AwayLogoPath == logoPath
                && State.AwayMascot == mascot && State.AwayRecord == record && State.AwayRanking == ranking
                && State.AwayDivisionRecord == divisionRecord
                && State.AwayPrimaryColor == primary && State.AwaySecondaryColor == secondary) return;
            SaveUndo("Visitors team details");
            State.AwayTeam = name; State.AwayAbbreviation = abbreviation; State.AwayLogoPath = logoPath;
            State.AwayMascot = mascot; State.AwayRecord = record; State.AwayRanking = ranking;
            State.AwayDivisionRecord = divisionRecord;
            State.AwayPrimaryColor = primary; State.AwaySecondaryColor = secondary;
        }
        Log("Teams", $"{State.HomeTeam} ({State.HomeAbbreviation}) vs {State.AwayTeam} ({State.AwayAbbreviation})");
        Changed?.Invoke();
    }

    static string NormalizeRanking(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return "";
        return text.StartsWith('#') ? text : $"#{text}";
    }

    /// <summary>Marks this game as a division fixture, so a result also updates both division records.</summary>
    public void SetDivisionGame(bool enabled)
    {
        if (State.DivisionGame == enabled) return;
        SaveUndo("Division game");
        State.DivisionGame = enabled;
        Log("Record", enabled ? "Division game" : "Not a division game");
        Changed?.Invoke();
    }

    static string NormalizeColor(string? value, string fallback)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return fallback;
        // #RGB, #ARGB, #RRGGBB or #AARRGGBB. Parsed by the renderer; validated here so a bad
        // entry can never break a graphic.
        if (text[0] != '#') return fallback;
        var digits = text[1..];
        return digits.Length is 3 or 4 or 6 or 8 && digits.All(Uri.IsHexDigit) ? text : fallback;
    }

    public void Score(bool home, int amount)
    {
        SaveUndo("Score");
        var before = home ? State.HomeScore : State.AwayScore;
        if (home) State.HomeScore = Math.Max(0, State.HomeScore + amount); else State.AwayScore = Math.Max(0, State.AwayScore + amount);
        var after = home ? State.HomeScore : State.AwayScore;
        State.LastScoreAmount = after - before;
        State.LastScoreHome = home;
        State.LastScoreSequence++;
        Log("Score", $"{(home ? State.HomeTeam : State.AwayTeam)} {(amount >= 0 ? "+" : "−")}{Math.Abs(amount)}"); Changed?.Invoke();
    }

    public void SetScore(bool home, int value)
    {
        SaveUndo("Score correction"); if (home) State.HomeScore = Math.Max(0, value); else State.AwayScore = Math.Max(0, value);
        State.LastScoreAmount = 0; State.LastScoreHome = home;
        Log("Score", $"{(home ? State.HomeTeam : State.AwayTeam)} score set to {Math.Max(0, value)}"); Changed?.Invoke();
    }

    public void ToggleClock()
    {
        // Stop on exactly the value the console is displaying. Ticking here consumed the leftover
        // fraction before the operator pressed Stop, so the number could change under the pointer.
        // The fraction already elapsed stays in _gameClockCarry, so starting again reaches the next
        // whole second when it is actually due rather than waiting a fresh second.
        SaveUndo("Clock"); State.ClockRunning = !State.ClockRunning; _lastTick = _watch.ElapsedTicks;
        Log("Clock", State.ClockRunning ? "Clock started" : $"Clock stopped at {FormatClock(State.ClockSeconds)}"); Changed?.Invoke();
    }

    public void SetClock(int seconds)
    {
        Tick(); SaveUndo("Clock correction"); State.ClockSeconds = Math.Max(0, seconds); _gameClockCarry = 0; _lastTick = _watch.ElapsedTicks;
        Log("Clock", $"Clock set to {FormatClock(State.ClockSeconds)}"); Changed?.Invoke();
    }

    public void AdjustClock(int seconds) => SetClock(State.ClockSeconds + seconds);

    /// <summary>Starts or stops soccer's stoppage clock. Its total is how long the clock runs past 90:00.</summary>
    public void ToggleStoppageClock()
    {
        if (State.SportId != "soccer") return;
        Tick(); SaveUndo("Stoppage clock");
        State.StoppageRunning = !State.StoppageRunning;
        _lastTick = _watch.ElapsedTicks;
        Log("Stoppage", State.StoppageRunning ? "Stoppage clock started" : $"Stoppage clock stopped at {FormatClock(State.StoppageSeconds)}");
        Changed?.Invoke();
    }

    public void AdjustStoppageClock(int seconds)
    {
        if (State.SportId != "soccer") return;
        Tick(); SaveUndo("Stoppage time");
        State.StoppageSeconds = Math.Clamp(State.StoppageSeconds + seconds, 0, 59 * 60);
        Log("Stoppage", $"Stoppage time {FormatClock(State.StoppageSeconds)}");
        Changed?.Invoke();
    }

    public void ResetStoppageClock()
    {
        if (State.SportId != "soccer") return;
        Tick(); SaveUndo("Stoppage time");
        State.StoppageSeconds = 0;
        State.StoppageRunning = false;
        _stoppageClockCarry = 0;
        Log("Stoppage", "Stoppage time cleared"); Changed?.Invoke();
    }

    /// <summary>
    /// Advances every running clock by the stopwatch time since the last tick. The fraction left
    /// over is held per clock, so pausing and resuming does not add a fresh full second to the
    /// number that was already showing.
    /// </summary>
    public void Tick()
    {
        if (HardwareControlled) { _lastTick = _watch.ElapsedTicks; return; }
        var now = _watch.ElapsedTicks;
        var elapsed = now - _lastTick;
        if (elapsed <= 0) return;
        _lastTick = now;
        var changed = false;
        var periodEnded = false;
        if (State.ClockRunning)
        {
            var seconds = WholeSeconds(ref _gameClockCarry, elapsed);
            if (seconds > 0)
            {
                changed = true;
                if (State.ClockCountsUp)
                {
                    // Soccer runs past the goal time by exactly the added time accrued on the stoppage
                    // clock, then stops: 90:00 + whatever stoppage was played.
                    var limit = State.SportId == "soccer"
                        ? State.PeriodSeconds * Math.Max(1, State.TotalPeriods) + Math.Max(0, State.StoppageSeconds)
                        : int.MaxValue;
                    if ((long)State.ClockSeconds + seconds >= limit)
                    {
                        State.ClockSeconds = limit; State.ClockRunning = false; _gameClockCarry = 0;
                        periodEnded = true; Log("Clock", "Full time");
                    }
                    else State.ClockSeconds += seconds;
                }
                else
                {
                    State.ClockSeconds = (int)Math.Max(0L, (long)State.ClockSeconds - seconds);
                    if (State.ClockSeconds == 0)
                    {
                        State.ClockRunning = false; _gameClockCarry = 0;
                        periodEnded = true; Log("Clock", "Period ended");
                    }
                }
            }
        }
        if (State.StoppageRunning)
        {
            var seconds = WholeSeconds(ref _stoppageClockCarry, elapsed);
            if (seconds > 0)
            {
                State.StoppageSeconds = (int)Math.Min(59L * 60, (long)State.StoppageSeconds + seconds);
                changed = true;
            }
        }
        if (State.ShotClockRunning)
        {
            var seconds = WholeSeconds(ref _shotClockCarry, elapsed);
            if (seconds > 0)
            {
                State.ShotClockSeconds = (int)Math.Max(0L, (long)State.ShotClockSeconds - seconds);
                changed = true;
                if (State.ShotClockSeconds == 0) { State.ShotClockRunning = false; _shotClockCarry = 0; Log("Shot clock", "Shot clock expired"); }
            }
        }
        if (State.PlayClockRunning)
        {
            var seconds = WholeSeconds(ref _playClockCarry, elapsed);
            if (seconds > 0)
            {
                State.PlayClockSeconds = (int)Math.Max(0L, (long)State.PlayClockSeconds - seconds);
                changed = true;
                if (State.PlayClockSeconds == 0) { State.PlayClockRunning = false; _playClockCarry = 0; Log("Play clock", "Play clock expired"); }
            }
        }
        if (State.TimeoutClockRunning)
        {
            var seconds = WholeSeconds(ref _timeoutClockCarry, elapsed);
            if (seconds > 0)
            {
                State.TimeoutClockSeconds = (int)Math.Max(0L, (long)State.TimeoutClockSeconds - seconds);
                changed = true;
                if (State.TimeoutClockSeconds == 0) { State.TimeoutClockRunning = false; _timeoutClockCarry = 0; Log("Timeout clock", "Timeout clock expired"); }
            }
        }
        if (periodEnded) SettleRecord();
        // Only notify when a clock's displayed value changes, so sub-second ticks cost nothing.
        if (changed) Changed?.Invoke();
    }

    /// <summary>Moves whole seconds out of a clock's fractional carry, leaving the remainder for next time.</summary>
    static int WholeSeconds(ref long carry, long elapsedTicks)
    {
        carry += elapsedTicks;
        var seconds = carry / Stopwatch.Frequency;
        carry %= Stopwatch.Frequency;
        return (int)Math.Min(seconds, int.MaxValue);
    }

    void ResetClockCarries() =>
        _gameClockCarry = _playClockCarry = _shotClockCarry = _timeoutClockCarry = _stoppageClockCarry = 0;

    /// <summary>The clock value that means full time, or 0 when the sport has no automatic finish.</summary>
    int FullTimeLimit => State.SportId == "soccer"
        ? State.PeriodSeconds * Math.Max(1, State.TotalPeriods) + Math.Max(0, State.StoppageSeconds)
        : 0;

    /// <summary>True when the clock has reached the end of the last period (or full time in soccer).</summary>
    bool GameIsComplete()
    {
        if (State.PeriodSeconds <= 0) return false;
        if (State.ClockCountsUp)
        {
            var limit = FullTimeLimit;
            return limit > 0 && State.ClockSeconds >= limit;
        }
        return State.Period >= State.TotalPeriods && State.ClockSeconds == 0;
    }

    /// <summary>
    /// Adds the final result to each team's record once. Empty or malformed records are left alone,
    /// and the saved flag keeps a game from being counted twice. Undo restores the flag with the
    /// records, so an undone result can be applied again if the operator ends the game once more.
    /// </summary>
    void ApplyFinalRecord(bool recordUndo = true)
    {
        if (State.RecordSettled) return;
        var result = Math.Sign(State.HomeScore - State.AwayScore);
        var home = ApplyResult(State.HomeRecord, result);
        var away = ApplyResult(State.AwayRecord, -result);
        var homeDivision = State.DivisionGame ? ApplyResult(State.HomeDivisionRecord, result) : State.HomeDivisionRecord;
        var awayDivision = State.DivisionGame ? ApplyResult(State.AwayDivisionRecord, -result) : State.AwayDivisionRecord;
        if (home == State.HomeRecord && away == State.AwayRecord
            && homeDivision == State.HomeDivisionRecord && awayDivision == State.AwayDivisionRecord)
        { State.RecordSettled = true; return; }
        if (recordUndo) SaveUndo("Final record");
        State.RecordSettled = true;
        State.HomeRecord = home;
        State.AwayRecord = away;
        State.HomeDivisionRecord = homeDivision;
        State.AwayDivisionRecord = awayDivision;
        var division = State.DivisionGame ? $" · division {homeDivision}–{awayDivision}" : "";
        Log("Record", $"{State.HomeTeam} {State.HomeRecordDisplay} · {State.AwayTeam} {State.AwayRecordDisplay}{division}");
    }

    /// <summary>
    /// The clock reaching the end marks the game final. Automatic record updates still depend on
    /// the preference; if it is off, End Game can apply the result later without counting it twice.
    /// </summary>
    void SettleRecord()
    {
        if (!GameIsComplete()) return;
        State.GameEnded = true;
        State.ClockMessage = GameClockMessage.Final;
        if (!AutoUpdateRecordEnabled) return;
        ApplyFinalRecord();
    }

    /// <summary>
    /// Adds one result to a win-loss or win-loss-tie record. "4-0" plus a win is "5-0"; a tie on a
    /// two-part record becomes "4-0-1". Empty or malformed text is returned untouched, which is how
    /// a team with no record stays that way.
    /// </summary>
    public static string ApplyResult(string? record, int result)
    {
        var original = record ?? "";
        var text = original.Replace('–', '-').Replace('—', '-').Trim();
        if (text.Length == 0) return original;
        var parts = text.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3) return original;
        var numbers = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
            if (!int.TryParse(parts[index], out numbers[index]) || numbers[index] < 0) return original;
        var wins = numbers[0];
        var losses = numbers[1];
        var ties = parts.Length == 3 ? numbers[2] : 0;
        if (result > 0) wins++;
        else if (result < 0) losses++;
        else ties++;
        return parts.Length == 2 && ties == 0 ? $"{wins}-{losses}" : $"{wins}-{losses}-{ties}";
    }

    /// <summary>
    /// Ends the game on the operator's command: stops every clock, marks the game final and adds
    /// the result to the team records. This is explicit, so it applies even when automatic record
    /// updates are off. A side with no record is left alone.
    /// </summary>
    public bool EndGame()
    {
        if (State.GameEnded && State.RecordSettled) return false;
        SaveUndo("End game");
        State.ClockRunning = false;
        State.StoppageRunning = false;
        State.ShotClockRunning = false;
        State.PlayClockRunning = false;
        State.TimeoutClockRunning = false;
        State.GameEnded = true;
        State.ClockMessage = GameClockMessage.Final;
        ApplyFinalRecord(recordUndo: false);
        Log("Game", "Game ended");
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Starts the same game over after the final: the score, every clock, the period and this
    /// game's stats return to a new game, while the teams and everything that describes them —
    /// names, colours, logos, records and the selected rosters and players — stay put.
    /// </summary>
    public bool RestartGame()
    {
        if (!State.GameEnded) return false;
        var sport = State.SportId;
        NewGame(sport, keepTeamIdentity: true);
        Log("Game", "Game restarted");
        Changed?.Invoke();
        return true;
    }

    public void NextPeriod()
    {
        SaveUndo("Period"); State.Period++;
        // Soccer's clock runs 0–90 across both halves; it does not restart at 45:00.
        if (State.SportId != "soccer") State.ClockSeconds = State.PeriodSeconds;
        State.ClockRunning = false;
        _gameClockCarry = 0;
        Log("Period", $"{State.PeriodLabel} {State.Period}"); Changed?.Invoke();
    }

    public void SetPeriod(int period)
    {
        var value = Math.Clamp(period, 1, 99);
        if (State.Period == value) return;
        SaveUndo("Period"); State.Period = value;
        Log("Period", $"{State.PeriodLabel} {State.Period}"); Changed?.Invoke();
    }

    public void PreviousPeriod()
    {
        if (State.Period <= 1) return;
        SaveUndo("Period"); State.Period--;
        if (State.SportId != "soccer") State.ClockSeconds = State.PeriodSeconds;
        State.ClockRunning = false;
        _gameClockCarry = 0;
        Log("Period", $"{State.PeriodLabel} {State.Period}"); Changed?.Invoke();
    }

    public void TogglePossession() { SaveUndo("Possession"); State.HomePossession = !State.HomePossession; Log("Possession", $"{(State.HomePossession ? State.HomeTeam : State.AwayTeam)} possession"); Changed?.Invoke(); }

    public void SetPossession(bool home) { if (State.HomePossession == home) return; SaveUndo("Possession"); State.HomePossession = home; Log("Possession", $"{(home ? State.HomeTeam : State.AwayTeam)} possession"); Changed?.Invoke(); }

    public void SetDown(int down) { if (State.SportId != "football") return; var value = Math.Clamp(down, 1, 4); if (State.Down == value && State.FootballDownMode == FootballDownMode.Normal) return; SaveUndo("Down"); // A typed down goes up at once too; the distance stays until it is set.
        State.FootballDownMode = FootballDownMode.Normal; State.DownPending = DownPreviewEnabled; State.Down = value; Log("Down", $"Down {State.Down}, {State.YardsToGo} to go"); Changed?.Invoke(); }

    /// <summary>Switches the down line to goal, PAT, two-point attempt, or nothing.</summary>
    public void SetFootballDownMode(FootballDownMode mode)
    {
        if (State.SportId != "football" || State.FootballDownMode == mode) return;
        SaveUndo("Down display");
        State.FootballDownMode = mode;
        State.DownPending = false;
        Log("Down", mode switch
        {
            FootballDownMode.Goal => $"{State.DownDisplay} & Goal",
            FootballDownMode.Pat => "PAT",
            FootballDownMode.TwoPoint => "2 PT ATT",
            FootballDownMode.None => "Down and distance hidden",
            _ => $"{State.DownDisplay} & {State.ToGoDisplay}"
        });
        Changed?.Invoke();
    }

    /// <summary>Sets the words the audience clock prints instead of the time.</summary>
    public void SetClockMessage(GameClockMessage message)
    {
        if (State.ClockMessage == message) return;
        SaveUndo("Clock message");
        State.ClockMessage = message;
        Log("Clock", message switch
        {
            GameClockMessage.Final => "Clock reads Final",
            GameClockMessage.EndOfRegulation => "Clock reads End of Reg.",
            _ => $"Clock reads {GameEngine.FormatClock(State.ClockSeconds)}"
        });
        Changed?.Invoke();
    }

    /// <summary>Legacy football saves and profiles may have carried five timeouts; cap them at three.</summary>
    void NormalizeFootballTimeouts()
    {
        if (State.SportId != "football") return;
        State.HomeTimeoutLimit = Math.Clamp(State.HomeTimeoutLimit, 0, 3);
        State.AwayTimeoutLimit = Math.Clamp(State.AwayTimeoutLimit, 0, 3);
        State.HomeTimeouts = Math.Clamp(State.HomeTimeouts, 0, 3);
        State.AwayTimeouts = Math.Clamp(State.AwayTimeouts, 0, 3);
    }

    public void SetTimeoutCount(bool home, int value)
    {
        // A football side has three timeouts. Other sports keep their profile's allotment, and
        // legacy saves with five are normalised when they are opened.
        var max = State.SportId == "football" ? 3 : 99;
        var clamped = Math.Clamp(value, 0, max);
        if ((home ? State.HomeTimeouts : State.AwayTimeouts) == clamped) return;
        SaveUndo("Timeouts"); if (home) State.HomeTimeouts = clamped; else State.AwayTimeouts = clamped;
        Log("Timeout", $"{(home ? State.HomeTeam : State.AwayTeam)} timeouts {clamped}"); Changed?.Invoke();
    }

    public void TogglePlayClock()
    {
        if (State.SportId != "football") return;
        // Pause on the number already on screen instead of first consuming an unseen fraction.
        SaveUndo("Play clock");
        State.PlayClockRunning = !State.PlayClockRunning;
        // Starting an expired clock reloads it: otherwise it would sit at 0 and stop again a
        // second later, which looks like the button does nothing.
        if (State.PlayClockRunning && State.PlayClockSeconds <= 0) { State.PlayClockSeconds = 40; _playClockCarry = 0; }
        _lastTick = _watch.ElapsedTicks;
        Log("Play clock", State.PlayClockRunning ? $"Play clock started at {State.PlayClockSeconds}" : "Play clock stopped"); Changed?.Invoke();
    }

    public void SetPlayClock(int seconds)
    {
        if (State.SportId != "football") return;
        Tick(); SaveUndo("Play clock"); State.PlayClockSeconds = Math.Clamp(seconds, 0, 99); _playClockCarry = 0; _lastTick = _watch.ElapsedTicks;
        Log("Play clock", $"Play clock set to {State.PlayClockSeconds}"); Changed?.Invoke();
    }

    public void ResetPlayClock(int seconds = 40)
    {
        if (State.SportId != "football") return;
        Tick(); SaveUndo("Play clock reset"); State.PlayClockSeconds = Math.Clamp(seconds, 1, 99); State.PlayClockRunning = false; _playClockCarry = 0; _lastTick = _watch.ElapsedTicks;
        Log("Play clock", $"Play clock reset to {State.PlayClockSeconds}"); Changed?.Invoke();
    }

    public void ToggleTimeoutClock()
    {
        Tick(); SaveUndo("Timeout clock"); State.TimeoutClockRunning = !State.TimeoutClockRunning; _lastTick = _watch.ElapsedTicks;
        Log("Timeout clock", State.TimeoutClockRunning ? "Timeout clock started" : "Timeout clock stopped"); Changed?.Invoke();
    }

    public void SetTimeoutClock(int seconds)
    {
        Tick(); SaveUndo("Timeout clock"); State.TimeoutClockSeconds = Math.Clamp(seconds, 0, 599); _timeoutClockCarry = 0; _lastTick = _watch.ElapsedTicks;
        Log("Timeout clock", $"Timeout clock set to {FormatClock(State.TimeoutClockSeconds)}"); Changed?.Invoke();
    }

    public void ResetTimeoutClock(int seconds = 60)
    {
        Tick(); SaveUndo("Timeout clock reset"); State.TimeoutClockSeconds = Math.Clamp(seconds, 1, 599); State.TimeoutClockRunning = false; _timeoutClockCarry = 0; _lastTick = _watch.ElapsedTicks;
        Log("Timeout clock", $"Timeout clock reset to {FormatClock(State.TimeoutClockSeconds)}"); Changed?.Invoke();
    }
    // The down, distance and spot that have actually gone to air, and the one before it. "Last
    // down" is a broadcast idea, not an editing idea: it puts back what the audience saw, and only
    // once a newer down has also been published.
    (int Down, int YardsToGo, int YardsToGoal)? _publishedDown;
    (int Down, int YardsToGo, int YardsToGoal)? _previousPublishedDown;

    /// <summary>Called when the console publishes, so the down on air is remembered for recall.</summary>
    public void MarkPublished()
    {
        State.DownPending = false;
        var current = (State.Down, State.YardsToGo, State.YardsToGoal);
        if (_publishedDown == current) return;
        _previousPublishedDown = _publishedDown;
        _publishedDown = current;
    }

    /// <summary>
    /// True when there is an earlier published down to go back to. Until a changed down has been
    /// published, Last down has nothing to recall and stays out of the way.
    /// </summary>
    public bool CanRecallLastDown => State.SportId == "football"
        && _previousPublishedDown is { } previous
        && previous != (State.Down, State.YardsToGo, State.YardsToGoal);

    /// <summary>
    /// Puts the down, distance and spot back to what went to air before the current one. It only
    /// stages the change — the operator presses Update Output when they want it on air — so a recall
    /// can be checked before the audience sees it.
    /// </summary>
    public void RecallLastDown()
    {
        if (!CanRecallLastDown || _previousPublishedDown is not { } last) return;
        SaveUndo("Recall last down"); State.DownPending = false; State.FootballDownMode = FootballDownMode.Normal;
        State.Down = last.Down;
        State.YardsToGo = last.YardsToGo;
        State.YardsToGoal = last.YardsToGoal;
        Log("Down", $"Recalled {State.Down} & {State.YardsToGo} — press Update Output");
        Changed?.Invoke();
    }

    /// <summary>
    /// When on, the down line's words print in full caps: "1st & GOAL" and "2nd DOWN". The
    /// ordinal, the distance and the PAT labels do not change. Off by default.
    /// </summary>
    public static bool UppercaseDownWordsEnabled { get; set; }

    /// <summary>Whether Next down shows the new down alone until the distance is set.</summary>
    public static bool DownPreviewEnabled { get; set; }

    /// <summary>When on, pressing New series also publishes it without Update Output.</summary>
    public static bool NewSeriesLiveEnabled { get; set; }
    bool _publishNewSeries;

    public void AdvanceDown() { if (State.SportId != "football") return; SaveUndo("Down"); State.FootballDownMode = FootballDownMode.Normal; State.DownPending = DownPreviewEnabled; if (State.Down >= 4) { State.Down=1; State.YardsToGo=10; State.YardsToGoal=Math.Clamp(State.YardsToGoal-State.YardsToGo,0,50); } else State.Down++; Log("Down", $"Down {State.Down}, {State.YardsToGo} to go"); Changed?.Invoke(); }
    public void PreviousDown() { if (State.SportId != "football") return; SaveUndo("Previous down"); State.FootballDownMode = FootballDownMode.Normal; State.DownPending = false; if (State.Down > 1) State.Down--; Log("Down", $"Down {State.Down}, {State.YardsToGo} to go"); Changed?.Invoke(); }
    public void NewSeries() { if (State.SportId != "football") return; SaveUndo("New series"); State.FootballDownMode = FootballDownMode.Normal; State.DownPending = false; State.Down = 1; State.YardsToGo = 10; _publishNewSeries = NewSeriesLiveEnabled; Log("Series", "New series · 1st & 10"); Changed?.Invoke(); }

    /// <summary>True once after New series asked to go straight to air; the host publishes it.</summary>
    public bool TakePublishRequest() { var requested = _publishNewSeries; _publishNewSeries = false; return requested; }
    public void SetPeriodFormat(string format) { if(format is not ("Full" or "Short" or "Number" or "Ordinal"))return; if(State.QuarterDisplayFormat==format)return; SaveUndo("Period format"); State.QuarterDisplayFormat=format; Log("Period", $"Period display: {PeriodDisplay.Format(State)}"); Changed?.Invoke(); }
    /// <summary>Yard line is 0–50: a ball spot never goes past midfield, whichever team has it.</summary>
    public void SetFootballDistance(int yardsToGo,int? yardsToGoal=null) { if (State.SportId != "football") return; SaveUndo("Football distance"); State.FootballDownMode = FootballDownMode.Normal; State.DownPending = false; State.YardsToGo=Math.Clamp(yardsToGo,1,99); if(yardsToGoal is not null)State.YardsToGoal=Math.Clamp(yardsToGoal.Value,0,50); Log("Yards", $"{State.YardsToGo} to go, ball at {State.YardsToGoal}"); Changed?.Invoke(); }

    /// <summary>Which team's half the ball spot is on. Deliberately independent of possession.</summary>
    public void SetBallSide(bool home)
    {
        if (State.SportId != "football" || State.BallOnHomeSide == home) return;
        SaveUndo("Ball side"); State.BallOnHomeSide = home;
        Log("Ball spot", $"Ball on {(home ? State.HomeTeam : State.AwayTeam)}'s {State.YardsToGoal}");
        Changed?.Invoke();
    }
    /// <summary>
    /// Sets the penalty box in one go. Applying the same flag twice is not an edit, so it neither
    /// logs again nor creates a second undo point.
    /// </summary>
    public void SetPenalty(bool active, string type, bool home)
    {
        // Each sport brings its own flag list; a sport with no flags (Custom) never raises one.
        var types = PenaltyTypesFor(State.SportId);
        if (types.Length == 0) return;
        type = (type ?? "").Trim();
        if (type.Length == 0) type = types[0];
        if (State.PenaltyActive == active && string.Equals(State.PenaltyType, type, StringComparison.Ordinal) && State.PenaltyHome == home) return;
        SaveUndo("Penalty");
        var was = State.PenaltyActive;
        State.PenaltyActive = active;
        State.PenaltyType = type;
        State.PenaltyHome = home;
        var team = home ? State.HomeTeam : State.AwayTeam;
        Log("Penalty", active ? $"{team}: {type}" : was ? "Flag cleared" : "No flag");
        Changed?.Invoke();
    }

    public void AddCard(bool home,bool red=false) { if(State.SportId!="soccer")return; SaveUndo(red?"Red card":"Yellow card"); if(red){if(home)State.HomeRedCards++;else State.AwayRedCards++;}else{if(home)State.HomeYellowCards++;else State.AwayYellowCards++;} Log("Card",$"{(home?State.HomeTeam:State.AwayTeam)} {(red?"red":"yellow")} card");Changed?.Invoke(); }
    public void BaseballBall() { if(State.SportId!="baseball")return; SaveUndo("Ball"); State.Balls++; if(State.Balls>=4){State.Balls=0;State.Strikes=0;Log("Count","Walk · count reset");}else Log("Count","Ball");Changed?.Invoke(); }
    public void BaseballStrike(bool foul=false) { if(State.SportId!="baseball")return; SaveUndo(foul?"Foul ball":"Strike"); if(!foul||State.Strikes<2)State.Strikes++; if(State.Strikes>=3){State.Outs++;State.Balls=0;State.Strikes=0;Log("Count","Strikeout · batter out");}else Log("Count",foul?"Foul ball":"Strike");Changed?.Invoke(); }
    public void BaseballOut() { if(State.SportId!="baseball")return; SaveUndo("Out"); State.Outs++; State.Balls=0; State.Strikes=0; Log("Out","Out recorded · count reset");Changed?.Invoke(); }
    public void BaseballHit(bool home) { if(State.SportId!="baseball")return; SaveUndo("Hit"); if(home)State.HomeHits++;else State.AwayHits++; State.Balls=0;State.Strikes=0;Log("Hit",$"{(home?State.HomeTeam:State.AwayTeam)} hit");Changed?.Invoke(); }
    public void Timeout(bool home) { SaveUndo("Timeout"); if (home) State.HomeTimeouts = Math.Max(0, State.HomeTimeouts - 1); else State.AwayTimeouts = Math.Max(0, State.AwayTimeouts - 1); Log("Timeout", $"{(home ? State.HomeTeam : State.AwayTeam)} timeout"); Changed?.Invoke(); }
    public void AdjustTimeouts(bool home, int delta) => SetTimeoutCount(home, (home ? State.HomeTimeouts : State.AwayTimeouts) + delta);
    public void AddFoul(bool home) { if(State.SportId!="basketball")return; SaveUndo("Foul"); if (home) State.HomeFouls++; else State.AwayFouls++; Log("Foul", $"{(home ? State.HomeTeam : State.AwayTeam)} foul"); Changed?.Invoke(); }
    public void SetShotClock(int seconds) { if(State.SportId!="basketball")return; SaveUndo("Shot clock");State.ShotClockSeconds=Math.Clamp(seconds,0,99);State.ShotClockRunning=false;_shotClockCarry=0;Log("Shot clock",$"Shot clock set to {State.ShotClockSeconds}");Changed?.Invoke(); }
    public void ToggleShotClock() { if(State.SportId!="basketball")return;SaveUndo("Shot clock");State.ShotClockRunning=!State.ShotClockRunning;_lastTick=_watch.ElapsedTicks;Log("Shot clock",State.ShotClockRunning?"Shot clock started":"Shot clock stopped");Changed?.Invoke(); }
    public void ResetShotClock(int seconds=24) { if(State.SportId!="basketball")return;SaveUndo("Shot clock reset");State.ShotClockSeconds=Math.Clamp(seconds,1,99);State.ShotClockRunning=false;_shotClockCarry=0;Log("Shot clock",$"Shot clock reset to {State.ShotClockSeconds}");Changed?.Invoke(); }
    public void AddFootballYards(bool home,int yards) { if(State.SportId!="football")return;SaveUndo("Rushing/receiving yards");if(home)State.HomeYards=Math.Max(0,State.HomeYards+yards);else State.AwayYards=Math.Max(0,State.AwayYards+yards);Log("Yards",$"{(home?State.HomeTeam:State.AwayTeam)} {yards:+#;-#;0} yards");Changed?.Invoke(); }
    public void AddTurnover(bool home) { if(State.SportId!="football")return;SaveUndo("Turnover");if(home)State.HomeTurnovers++;else State.AwayTurnovers++;Log("Turnover",$"{(home?State.HomeTeam:State.AwayTeam)} turnover");Changed?.Invoke(); }
    public void AddSoccerShot(bool home) { if(State.SportId!="soccer")return;SaveUndo("Shot");if(home)State.HomeShots++;else State.AwayShots++;Log("Shot",$"{(home?State.HomeTeam:State.AwayTeam)} shot");Changed?.Invoke(); }
    public void AddSoccerCorner(bool home) { if(State.SportId!="soccer")return;SaveUndo("Corner");if(home)State.HomeCorners++;else State.AwayCorners++;Log("Corner",$"{(home?State.HomeTeam:State.AwayTeam)} corner");Changed?.Invoke(); }

    public GameStat AddStat(string name)
    {
        var trimmed = name.Trim();
        var existing = State.Stats.FirstOrDefault(s => string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        SaveUndo("Stat"); var stat = new GameStat(Guid.NewGuid().ToString("N"), trimmed.Length == 0 ? "Stat" : trimmed);
        State.Stats.Add(stat); Log("Stat", $"Added stat {stat.Name}"); Changed?.Invoke(); return stat;
    }

    public void RemoveStat(string id)
    {
        var stat = State.Stats.FirstOrDefault(s => s.Id == id); if (stat is null) return;
        SaveUndo("Remove stat"); State.Stats.Remove(stat); Log("Stat", $"Removed stat {stat.Name}"); Changed?.Invoke();
    }

    public void AdjustStat(string id, int delta)
    {
        var stat = State.Stats.FirstOrDefault(s => s.Id == id); if (stat is null) return;
        SaveUndo(stat.Name); var index = State.Stats.IndexOf(stat);
        // Stepping a stat makes it a number again, even if it was holding text.
        var updated = stat with { Value = Math.Max(0, stat.Value + delta), Text = null };
        State.Stats[index] = updated;
        Log(stat.Name, $"{updated.Name}: {updated.Display}"); Changed?.Invoke();
    }

    /// <summary>Sets a custom value to an exact number, for the typed override in the control panel.</summary>
    public void SetStat(string id, int value)
    {
        var stat = State.Stats.FirstOrDefault(s => s.Id == id); if (stat is null) return;
        var clamped = Math.Max(0, value);
        if (stat.Value == clamped && stat.IsNumber) return;
        SaveUndo(stat.Name); var index = State.Stats.IndexOf(stat);
        var updated = stat with { Value = clamped, Text = null };
        State.Stats[index] = updated;
        Log(stat.Name, $"{updated.Name}: {updated.Display}"); Changed?.Invoke();
    }

    /// <summary>
    /// Sets a custom value to whatever the operator typed. A plain number becomes the value
    /// (so the steppers keep working); anything else is stored as text and prints as-is.
    /// </summary>
    public void SetStatText(string id, string text)
    {
        var stat = State.Stats.FirstOrDefault(s => s.Id == id); if (stat is null) return;
        var trimmed = (text ?? "").Trim();
        if (int.TryParse(trimmed, out var number))
        {
            number = Math.Max(0, number);
            if (stat.IsNumber && stat.Value == number) return;
            SaveUndo(stat.Name); var valueIndex = State.Stats.IndexOf(stat);
            State.Stats[valueIndex] = stat with { Value = number, Text = null };
        }
        else
        {
            if (string.Equals(stat.Text ?? stat.Display, trimmed, StringComparison.Ordinal)) return;
            SaveUndo(stat.Name); var textIndex = State.Stats.IndexOf(stat);
            State.Stats[textIndex] = stat with { Text = trimmed };
        }
        Log(stat.Name, $"{stat.Name}: {trimmed}"); Changed?.Invoke();
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var snapshot = _undo.Pop(); State = snapshot.State; ResetClockCarries(); _lastTick = _watch.ElapsedTicks; Log("Undo", $"Undid {snapshot.Label}"); Changed?.Invoke(); return true;
    }

    // ---------- rosters ----------

    /// <summary>Chooses which roster a side draws from. The current player is kept when it exists there.</summary>
    public void AssignRoster(bool home, string? rosterId)
    {
        if ((home ? State.HomeRosterId : State.AwayRosterId) == rosterId) return;
        SaveUndo(home ? "Home roster" : "Visitors roster");
        if (home) State.HomeRosterId = rosterId; else State.AwayRosterId = rosterId;
        // A roster on its own never puts a name on the graphic: the operator selects the player they
        // want, so assigning a roster cannot print a stranger by accident. A player who is already
        // on air stays on air when the new roster also holds them.
        var roster = RosterStore.Book.Find(rosterId);
        var previous = home ? State.HomePlayer : State.AwayPlayer;
        var player = roster?.Find(previous?.PlayerId);
        if (home) State.HomePlayerCleared = player is null; else State.AwayPlayerCleared = player is null;
        ApplySelected(home, roster, player);
        Log("Roster", roster is null
            ? $"{(home ? State.HomeTeam : State.AwayTeam)} has no roster"
            : $"{(home ? State.HomeTeam : State.AwayTeam)} roster: {roster.Name}");
        Changed?.Invoke();
    }

    /// <summary>Puts one player on the graphic. Every "selected player" binding follows it.</summary>
    public void SelectPlayer(bool home, string? playerId)
    {
        var roster = RosterStore.Book.Find(home ? State.HomeRosterId : State.AwayRosterId);
        var player = roster?.Find(playerId);
        if (player is null) return;
        if ((home ? State.HomePlayer : State.AwayPlayer)?.PlayerId == player.Id && !(home ? State.HomePlayerCleared : State.AwayPlayerCleared)) return;
        SaveUndo("Selected player");
        if (home) State.HomePlayerCleared = false; else State.AwayPlayerCleared = false;
        ApplySelected(home, roster, player);
        Log("Player", $"{(home ? State.HomeTeam : State.AwayTeam)}: {player.Record}");
        Changed?.Invoke();
    }

    /// <summary>
    /// Takes the player off the graphic for one side, so a player element prints nothing. The
    /// choice survives roster edits and saving, until a player is selected again.
    /// </summary>
    public void ClearSelectedPlayer(bool home)
    {
        if ((home ? State.HomePlayerCleared : State.AwayPlayerCleared) && (home ? State.HomePlayer : State.AwayPlayer) is null) return;
        SaveUndo("Selected player");
        if (home) State.HomePlayerCleared = true; else State.AwayPlayerCleared = true;
        ApplySelected(home, null, null);
        Log("Player", $"{(home ? State.HomeTeam : State.AwayTeam)}: no player on the graphic");
        Changed?.Invoke();
    }

    /// <summary>
    /// Re-reads both selected players from the library, so an edited name, number or stat reaches
    /// the graphic without reselecting the player. Silent: callers announce the change they made.
    /// </summary>
    public void RefreshSelectedPlayers()
    {
        ResolveSelected(true);
        ResolveSelected(false);
    }

    /// <summary>Re-reads one side's selected player from the library, keeping the same player by id.</summary>
    void ResolveSelected(bool home)
    {
        var rosterId = home ? State.HomeRosterId : State.AwayRosterId;
        if (string.IsNullOrWhiteSpace(rosterId)) { ApplySelected(home, null, null); return; }
        var roster = RosterStore.Book.Find(rosterId);
        // A game opened where the roster library is missing keeps the player it carries, so the
        // graphic never goes blank just because the library moved with someone else.
        if (roster is null) return;
        // No player on the graphic is a normal state, not an empty slot to fill in: a side shows a
        // player only after the operator selected one for it.
        if (home ? State.HomePlayerCleared : State.AwayPlayerCleared) { ApplySelected(home, roster, null); return; }
        var current = home ? State.HomePlayer : State.AwayPlayer;
        var player = roster.Find(current?.PlayerId);
        ApplySelected(home, roster, player);
    }

    /// <summary>Copies the roster entry into the game state. A null player clears the selection.</summary>
    void ApplySelected(bool home, Roster? roster, RosterPlayer? player)
    {
        var snapshot = roster is not null && player is not null ? SelectedPlayer.From(roster, player) : null;
        if (home) State.HomePlayer = snapshot; else State.AwayPlayer = snapshot;
    }

    /// <summary>Re-resolves the selected player after the roster was edited, keeping the game on air.</summary>
    public void ReloadSelectedPlayer(bool home)
    {
        ResolveSelected(home);
        Changed?.Invoke();
    }

    /// <summary>Replaces the saved macro buttons without adding an undo point for every keystroke.</summary>
    public void StoreMacros(IEnumerable<SavedMacro> macros)
    {
        State.Macros = macros.Select(macro => macro.Copy()).ToList();
        Changed?.Invoke();
    }

    public void ResetGame()
    {
        SaveUndo("Game reset"); var home = State.HomeTeam; var away = State.AwayTeam; var id = State.SportId;
        NewGame(id); State.HomeTeam = home; State.AwayTeam = away; State.HomeAbbreviation = Abbreviate(home); State.AwayAbbreviation = Abbreviate(away);
        Log("Game", "Game reset"); Changed?.Invoke();
    }

    /// <summary>
    /// Writes the game, and — when a design is supplied — every display and layout with it, so the
    /// show reopens on the same screens. A plain call writes just the game state, which is what the
    /// suites and the older tooling expect.
    /// </summary>
    public void Save(string path, ScorebugDocument? design = null)
    {
        Tick(); State.SavedAt = DateTimeOffset.Now; var json = GameFiles.Serialize(State, design);
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + ".tmp"; File.WriteAllText(temporary, json); File.Move(temporary, full, true);
    }

    /// <summary>
    /// Reads a game and returns the design it carried, or null for a file written before designs
    /// were saved with the game (the current scorebug then stays put).
    /// </summary>
    public ScorebugDocument? Load(string path)
    {
        var (game, design) = GameFiles.Read(path);
        State = game;
        // Older saves simply have no name; normalize hand-edited files as well.
        State.GameName = (State.GameName ?? "").Trim();
        if (State.GameName.Length > 120) State.GameName = State.GameName[..120];
        // Older saves could hold a spot past midfield; bring it back into the 0–50 range.
        State.YardsToGoal = Math.Clamp(State.YardsToGoal, 0, 50);
        NormalizeFootballTimeouts();
        // A file written before sport values existed keeps the current profile's control list, so
        // the console still has balls, bases and power play to work with.
        State.SportValues ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        State.Macros ??= [];
        foreach (var macro in State.Macros) macro.Normalize();
        State.Controls ??= [];
        if (State.Controls.Count == 0)
            State.Controls = (Sports.Find(State.SportId)?.Controls ?? []).Select(c => c with { }).ToList();
        // Roster edits made since the file was written still reach the graphic on load.
        RefreshSelectedPlayers();
        _undo.Clear(); ResetClockCarries(); _lastTick = _watch.ElapsedTicks; Changed?.Invoke();
        return design;
    }

    /// <summary>
    /// True when the game holds values that only mean something for the sport being played — a
    /// count, power play, bases, wickets, cards. Switching sport starts a new game, so the operator
    /// is warned that these are about to be cleared rather than losing them silently.
    /// </summary>
    public bool HasSportSpecificData()
    {
        if (State.Controls.Any(control => SportValues.Read(State, control.Id) != control.Default)) return true;
        return State.Balls != 0 || State.Strikes != 0 || State.Outs != 0
            || State.HomeFouls != 0 || State.AwayFouls != 0
            || State.HomeHits != 0 || State.AwayHits != 0
            || State.HomeShots != 0 || State.AwayShots != 0
            || State.HomeCorners != 0 || State.AwayCorners != 0
            || State.HomeYellowCards != 0 || State.AwayYellowCards != 0
            || State.HomeRedCards != 0 || State.AwayRedCards != 0;
    }

    /// <summary>
    /// Everything that is staged until Update Output is pressed. Score and the running clocks are
    /// deliberately excluded: those publish live, so they never mark the output as out of date.
    /// </summary>
    public static string StagedSignature(GameState s) => string.Join('|',
        s.SportId,
        s.Period, s.TotalPeriods, s.PeriodLabel, s.QuarterDisplayFormat,
        s.HomeMascot, s.AwayMascot,
        s.HomeTimeouts, s.AwayTimeouts, s.HomeFouls, s.AwayFouls, s.HomePossession,
        // The down number may go live ahead of the rest, but the Yards/Goal/PAT choice is part of
        // the staged down-and-distance line and must wait for Update Output.
        s.Down, s.FootballDownMode, s.YardsToGo, s.YardsToGoal, s.BallOnHomeSide,
        s.HomeYellowCards, s.AwayYellowCards, s.HomeRedCards, s.AwayRedCards,
        s.PenaltyActive, s.PenaltyType, s.PenaltyHome, s.DownPending,
        s.Balls, s.Strikes, s.Outs, s.HomeHits, s.AwayHits,
        s.HomeYards, s.AwayYards, s.HomeTurnovers, s.AwayTurnovers,
        s.HomeShots, s.AwayShots, s.HomeCorners, s.AwayCorners,
        string.Join(",", s.Controls.Select(c => $"{c.Id}={SportValues.Read(s, c.Id)}")),
        string.Join(",", s.Stats.Select(x => $"{x.Name}={x.Display}")));

    /// <summary>What the graphic reads from a selected player, compared by the live publish.</summary>
    public static string PlayerSignature(SelectedPlayer? player) => player is null
        ? "-"
        : $"{player.PlayerId}:{player.Number}:{player.Name}:{player.Position}:" +
          string.Join(",", player.Stats.Select(x => $"{x.Name}={x.Display}"));

    /// <summary>
    /// Applies a partial reading from a scoreboard controller. Hardware input is live, so it does
    /// not create undo points, and clock-only frames do not flood the event log. Score, period and
    /// possession changes are logged once each.
    /// </summary>
    public bool ApplyHardwareSnapshot(DeviceSnapshot snapshot)
    {
        var changed = false;
        var notable = false;

        void SetScore(bool home, int? value)
        {
            if (value is not int next) return;
            next = Math.Max(0, next);
            if (home ? State.HomeScore == next : State.AwayScore == next) return;
            var before = home ? State.HomeScore : State.AwayScore;
            if (home) State.HomeScore = next; else State.AwayScore = next;
            State.LastScoreAmount = next - before;
            State.LastScoreHome = home;
            State.LastScoreSequence++;
            changed = notable = true;
        }

        SetScore(true, snapshot.HomeScore);
        SetScore(false, snapshot.AwayScore);

        if (snapshot.ClockSeconds is int clock && clock >= 0 && State.ClockSeconds != clock)
        {
            State.ClockSeconds = clock;
            changed = true;
        }
        if (snapshot.ClockRunning is bool running && State.ClockRunning != running)
        {
            State.ClockRunning = running;
            changed = true;
        }
        if (snapshot.Period is int period)
        {
            period = Math.Clamp(period, 1, 99);
            if (State.Period != period) { State.Period = period; changed = notable = true; }
        }
        if (snapshot.HomeTimeouts is int homeTimeouts)
        {
            homeTimeouts = Math.Clamp(homeTimeouts, 0, 99);
            if (State.HomeTimeouts != homeTimeouts) { State.HomeTimeouts = homeTimeouts; changed = notable = true; }
        }
        if (snapshot.AwayTimeouts is int awayTimeouts)
        {
            awayTimeouts = Math.Clamp(awayTimeouts, 0, 99);
            if (State.AwayTimeouts != awayTimeouts) { State.AwayTimeouts = awayTimeouts; changed = notable = true; }
        }
        if (snapshot.HomePossession is bool possession && State.HomePossession != possession)
        {
            State.HomePossession = possession;
            changed = notable = true;
        }
        if (snapshot.Down is int down)
        {
            down = Math.Clamp(down, 1, 4);
            if (State.Down != down) { State.Down = down; changed = true; }
        }
        if (snapshot.YardsToGo is int yardsToGo)
        {
            yardsToGo = Math.Clamp(yardsToGo, 1, 99);
            if (State.YardsToGo != yardsToGo) { State.YardsToGo = yardsToGo; changed = true; }
        }
        if (snapshot.BallOn is int ballOn)
        {
            ballOn = Math.Clamp(ballOn, 0, 50);
            if (State.YardsToGoal != ballOn) { State.YardsToGoal = ballOn; changed = true; }
        }
        if (snapshot.ShotClockSeconds is int shotClock)
        {
            shotClock = Math.Clamp(shotClock, 0, 99);
            if (State.ShotClockSeconds != shotClock) { State.ShotClockSeconds = shotClock; changed = true; }
        }
        if (snapshot.ShotClockRunning is bool shotRunning && State.ShotClockRunning != shotRunning)
        {
            State.ShotClockRunning = shotRunning;
            changed = true;
        }
        if (snapshot.PlayClockSeconds is int playClock)
        {
            playClock = Math.Clamp(playClock, 0, 99);
            if (State.PlayClockSeconds != playClock) { State.PlayClockSeconds = playClock; changed = true; }
        }
        if (snapshot.PlayClockRunning is bool playRunning && State.PlayClockRunning != playRunning)
        {
            State.PlayClockRunning = playRunning;
            changed = true;
        }
        if (snapshot.HomeFouls is int homeFouls)
        {
            homeFouls = Math.Max(0, homeFouls);
            if (State.HomeFouls != homeFouls) { State.HomeFouls = homeFouls; changed = true; }
        }
        if (snapshot.AwayFouls is int awayFouls)
        {
            awayFouls = Math.Max(0, awayFouls);
            if (State.AwayFouls != awayFouls) { State.AwayFouls = awayFouls; changed = true; }
        }
        if (snapshot.Balls is int balls)
        {
            balls = Math.Clamp(balls, 0, 3);
            if (State.Balls != balls) { State.Balls = balls; changed = true; }
        }
        if (snapshot.Strikes is int strikes)
        {
            strikes = Math.Clamp(strikes, 0, 2);
            if (State.Strikes != strikes) { State.Strikes = strikes; changed = true; }
        }
        if (snapshot.Outs is int outs)
        {
            outs = Math.Clamp(outs, 0, 2);
            if (State.Outs != outs) { State.Outs = outs; changed = true; }
        }

        if (!changed) return false;
        if (notable)
            Log("Hardware", $"{snapshot.Source ?? "Controller"}: {State.AwayAbbreviation} {State.AwayScore} · {State.HomeAbbreviation} {State.HomeScore} · {FormatClock(State.ClockSeconds)} · {PeriodDisplay.Format(State)}");
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Copies the fields a hardware controller owns onto the published copy. Unlike
    /// <see cref="UpdateLiveScoreClock"/>, this carries sport-specific values such as down and
    /// distance, so a controller's data reaches the audience without an Update Output press.
    /// </summary>
    public bool UpdateLiveController(GameState source)
    {
        if (State.HomeScore == source.HomeScore && State.AwayScore == source.AwayScore
            && State.ClockSeconds == source.ClockSeconds && State.ClockRunning == source.ClockRunning
            && State.Period == source.Period && State.HomeTimeouts == source.HomeTimeouts
            && State.AwayTimeouts == source.AwayTimeouts && State.HomePossession == source.HomePossession
            && State.Down == source.Down && State.YardsToGo == source.YardsToGo
            && State.FootballDownMode == source.FootballDownMode && State.ClockMessage == source.ClockMessage
            && State.HomeRanking == source.HomeRanking && State.AwayRanking == source.AwayRanking
            && State.HomeDivisionRecord == source.HomeDivisionRecord && State.AwayDivisionRecord == source.AwayDivisionRecord
            && State.DivisionGame == source.DivisionGame
            && State.YardsToGoal == source.YardsToGoal && State.BallOnHomeSide == source.BallOnHomeSide
            && State.ShotClockSeconds == source.ShotClockSeconds && State.ShotClockRunning == source.ShotClockRunning
            && State.PlayClockSeconds == source.PlayClockSeconds && State.PlayClockRunning == source.PlayClockRunning
            && State.TimeoutClockSeconds == source.TimeoutClockSeconds
            && State.TimeoutClockRunning == source.TimeoutClockRunning
            && State.HomeFouls == source.HomeFouls && State.AwayFouls == source.AwayFouls
            && State.Balls == source.Balls && State.Strikes == source.Strikes && State.Outs == source.Outs) return false;
        State.HomeScore = source.HomeScore; State.AwayScore = source.AwayScore;
        State.ClockSeconds = source.ClockSeconds; State.ClockRunning = source.ClockRunning;
        State.Period = source.Period; State.HomeTimeouts = source.HomeTimeouts;
        State.AwayTimeouts = source.AwayTimeouts; State.HomePossession = source.HomePossession;
        State.Down = source.Down; State.YardsToGo = source.YardsToGo;
        State.FootballDownMode = source.FootballDownMode; State.ClockMessage = source.ClockMessage;
        State.HomeRanking = source.HomeRanking; State.AwayRanking = source.AwayRanking;
        State.HomeDivisionRecord = source.HomeDivisionRecord; State.AwayDivisionRecord = source.AwayDivisionRecord;
        State.DivisionGame = source.DivisionGame;
        State.YardsToGoal = source.YardsToGoal; State.BallOnHomeSide = source.BallOnHomeSide;
        State.ShotClockSeconds = source.ShotClockSeconds; State.ShotClockRunning = source.ShotClockRunning;
        State.PlayClockSeconds = source.PlayClockSeconds; State.PlayClockRunning = source.PlayClockRunning;
        State.TimeoutClockSeconds = source.TimeoutClockSeconds; State.TimeoutClockRunning = source.TimeoutClockRunning;
        State.HomeFouls = source.HomeFouls; State.AwayFouls = source.AwayFouls;
        State.Balls = source.Balls; State.Strikes = source.Strikes; State.Outs = source.Outs;
        Changed?.Invoke();
        return true;
    }

    public string ExportJson() => JsonSerializer.Serialize(State, JsonOptions);
    public GameState CloneState() => JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(State, JsonOptions), JsonOptions)!;
    public void LoadFromState(GameState state){State=JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state,JsonOptions),JsonOptions)!;NormalizeFootballTimeouts();RefreshSelectedPlayers();_undo.Clear();ResetClockCarries();_lastTick=_watch.ElapsedTicks;Changed?.Invoke();}
    public bool UpdateLiveScoreClock(GameState source)
    {
        if (State.HomeScore == source.HomeScore && State.AwayScore == source.AwayScore &&
            State.HomeTeam == source.HomeTeam && State.AwayTeam == source.AwayTeam &&
            State.HomeAbbreviation == source.HomeAbbreviation && State.AwayAbbreviation == source.AwayAbbreviation &&
            State.HomeLogoPath == source.HomeLogoPath && State.AwayLogoPath == source.AwayLogoPath &&
            State.HomeMascot == source.HomeMascot && State.AwayMascot == source.AwayMascot &&
            State.HomeRecord == source.HomeRecord && State.AwayRecord == source.AwayRecord &&
            State.HomeDivisionRecord == source.HomeDivisionRecord && State.AwayDivisionRecord == source.AwayDivisionRecord &&
            State.HomeRanking == source.HomeRanking && State.AwayRanking == source.AwayRanking &&
            // Yards/Goal/PAT is staged with the distance, so a live publish leaves it alone.
            State.ClockMessage == source.ClockMessage &&
            State.HomePrimaryColor == source.HomePrimaryColor && State.HomeSecondaryColor == source.HomeSecondaryColor &&
            State.AwayPrimaryColor == source.AwayPrimaryColor && State.AwaySecondaryColor == source.AwaySecondaryColor &&
            State.ClockSeconds == source.ClockSeconds && State.ClockRunning == source.ClockRunning &&
            State.ClockCountsUp == source.ClockCountsUp && State.ShotClockSeconds == source.ShotClockSeconds &&
            State.ShotClockRunning == source.ShotClockRunning &&
            State.PlayClockSeconds == source.PlayClockSeconds && State.PlayClockRunning == source.PlayClockRunning &&
            State.TimeoutClockSeconds == source.TimeoutClockSeconds && State.TimeoutClockRunning == source.TimeoutClockRunning &&
            // Player data is live too: a substitution on the roster desk reprints the graphic at once.
            State.HomePlayerCleared == source.HomePlayerCleared && State.AwayPlayerCleared == source.AwayPlayerCleared &&
            PlayerSignature(State.HomePlayer) == PlayerSignature(source.HomePlayer) &&
            PlayerSignature(State.AwayPlayer) == PlayerSignature(source.AwayPlayer) &&
            // The new down goes on air at once when the preview is on; the distance waits for publish.
            (!source.DownPending || (State.Down == source.Down && State.DownPending))) return false;
        State.HomeScore=source.HomeScore;State.AwayScore=source.AwayScore;
        State.LastScoreAmount=source.LastScoreAmount;State.LastScoreHome=source.LastScoreHome;State.LastScoreSequence=source.LastScoreSequence;
        State.HomeTeam=source.HomeTeam;State.AwayTeam=source.AwayTeam;
        State.HomeAbbreviation=source.HomeAbbreviation;State.AwayAbbreviation=source.AwayAbbreviation;
        State.HomeLogoPath=source.HomeLogoPath;State.AwayLogoPath=source.AwayLogoPath;
        State.HomeMascot=source.HomeMascot;State.AwayMascot=source.AwayMascot;
        State.HomeRecord=source.HomeRecord;State.AwayRecord=source.AwayRecord;
        State.HomeDivisionRecord=source.HomeDivisionRecord;State.AwayDivisionRecord=source.AwayDivisionRecord;
        State.HomeRanking=source.HomeRanking;State.AwayRanking=source.AwayRanking;
        // Down may go live on its own; the down display mode waits for Update Output.
        State.ClockMessage=source.ClockMessage;
        State.HomePrimaryColor=source.HomePrimaryColor;State.HomeSecondaryColor=source.HomeSecondaryColor;
        State.AwayPrimaryColor=source.AwayPrimaryColor;State.AwaySecondaryColor=source.AwaySecondaryColor;
        State.ClockSeconds=source.ClockSeconds;State.ClockRunning=source.ClockRunning;State.ClockCountsUp=source.ClockCountsUp;
        State.ShotClockSeconds=source.ShotClockSeconds;State.ShotClockRunning=source.ShotClockRunning;
        State.PlayClockSeconds=source.PlayClockSeconds;State.PlayClockRunning=source.PlayClockRunning;
        State.TimeoutClockSeconds=source.TimeoutClockSeconds;State.TimeoutClockRunning=source.TimeoutClockRunning;
        // Live: the down changes, the distance does not; the pending flag stays until publication.
        if (source.DownPending) { State.Down=source.Down; State.DownPending=true; }
        State.HomePlayerCleared=source.HomePlayerCleared;State.AwayPlayerCleared=source.AwayPlayerCleared;
        State.HomePlayer=CopyPlayer(source.HomePlayer);State.AwayPlayer=CopyPlayer(source.AwayPlayer);
        Changed?.Invoke();
        return true;
    }

    /// <summary>A detached copy of a selected player, so the two engines never share a live object.</summary>
    static SelectedPlayer? CopyPlayer(SelectedPlayer? player) => player is null
        ? null
        : JsonSerializer.Deserialize<SelectedPlayer>(JsonSerializer.Serialize(player, JsonOptions), JsonOptions)!;

    void SaveUndo(string label) { _undo.Push(new GameSnapshot(JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(State, JsonOptions), JsonOptions)!, label)); if (_undo.Count > 100) { var keep = _undo.Reverse().TakeLast(100).ToArray(); _undo.Clear(); foreach (var x in keep) _undo.Push(x); } }
    void Log(string type, string description) => State.Events.Add(new GameEvent(DateTimeOffset.Now, type, description, State.HomeScore, State.AwayScore));
    static string Abbreviate(string value) => string.Concat(value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3).Select(s => char.ToUpperInvariant(s[0])));
    /// <summary>
    /// How a clock prints. Padded is 10:00, 09:59 and 0:59; compact drops the minutes' leading
    /// zero and leaves only :59 under a minute. A show-wide preference, so every clock agrees.
    /// </summary>
    public static ClockStyle ClockDisplay { get; set; } = ClockStyle.Padded;

    public static string FormatClock(int seconds) => FormatClock(seconds, ClockDisplay);

    public static string FormatClock(int seconds, ClockStyle style)
    {
        seconds = Math.Max(0, seconds);
        var minutes = seconds / 60;
        var remainder = seconds % 60;
        return style == ClockStyle.Compact
            ? minutes == 0 ? $":{remainder:00}" : $"{minutes}:{remainder:00}"
            // Padded keeps the familiar two-digit minutes (09:59) but a zero minute is just
            // "0:59", not "00:59" — that is the shape the operator asked for.
            : minutes == 0 ? $"0:{remainder:00}" : $"{minutes:00}:{remainder:00}";
    }
}
