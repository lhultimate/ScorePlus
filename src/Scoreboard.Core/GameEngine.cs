using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scoreboard.Core;

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
        new SportDefinition("australian-football", "Australian Football", "Quarter", 4, 20 * 60, [1, 2, 3, 6], false, false, 0) { Controls = [.. SportControls.For("australian-football")] },
        new SportDefinition("baseball", "Baseball", "Inning", 9, 0, [1], false, true, 3) { Controls = [.. SportControls.For("baseball")] },
        new SportDefinition("basketball", "Basketball", "Quarter", 4, 12 * 60, [1, 2, 3], true, false, 5) { Controls = [.. SportControls.For("basketball")] },
        new SportDefinition("cricket", "Cricket", "Innings", 2, 0, [1, 2, 3, 4, 6], false, true, 0) { Controls = [.. SportControls.For("cricket")] },
        new SportDefinition("hockey", "Hockey", "Period", 3, 20 * 60, [1], true, false, 1) { Controls = [.. SportControls.For("hockey")] },
        new SportDefinition("lacrosse", "Lacrosse", "Quarter", 4, 12 * 60, [1], true, false, 3) { Controls = [.. SportControls.For("lacrosse")] },
        new SportDefinition("rugby", "Rugby", "Half", 2, 40 * 60, [2, 3, 5, 7], false, false, 3) { Controls = [.. SportControls.For("rugby")] },
        new SportDefinition("soccer", "Soccer", "Half", 2, 45 * 60, [1], false, false, 5) { Controls = [.. SportControls.For("soccer")] },
        new SportDefinition("tennis", "Tennis", "Set", 3, 0, [1], false, true, 3) { Controls = [.. SportControls.For("tennis")] },
        new SportDefinition("volleyball", "Volleyball", "Set", 5, 0, [1], false, true, 2) { Controls = [.. SportControls.For("volleyball")] },
        new SportDefinition("wrestling", "Wrestling", "Period", 3, 3 * 60, [1, 2, 3], false, false, 0) { Controls = [.. SportControls.For("wrestling")] },
        // The blank starting point: everything editable in Sport Profiles.
        new SportDefinition("generic", "Custom", "Period", 2, 10 * 60, [1, 2, 3], false, false, 3)
    };
}

/// <summary>
/// A single user-defined statistic such as Turnovers or Penalties. A stat is either a number (the
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
    /// <summary>Suggested stat names offered when adding a custom stat.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "Turnovers", "Penalties", "Penalty yards", "Shots", "Shots on goal", "Saves", "Corners",
        "Rebounds", "Assists", "Steals", "Blocks", "Fouls", "Hits", "Errors", "Faceoff wins", "Possessions"
    ];
}

public sealed class GameState
{
    public string SportId { get; set; } = "basketball";
    public string HomeTeam { get; set; } = "HOME";
    public string AwayTeam { get; set; } = "VISITORS";
    public string HomeAbbreviation { get; set; } = "HOME";
    public string AwayAbbreviation { get; set; } = "AWAY";
    /// <summary>The team's mascot, printed through the {{HOME_MASCOT}} / {{AWAY_MASCOT}} bindings.</summary>
    public string HomeMascot { get; set; } = "";
    public string AwayMascot { get; set; } = "";
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
    public int Period { get; set; } = 1;
    public int HomeTimeouts { get; set; } = 5;
    public int AwayTimeouts { get; set; } = 5;
    public int HomeFouls { get; set; }
    public int AwayFouls { get; set; }
    public bool HomePossession { get; set; } = true;
    public int ClockSeconds { get; set; } = 720;
    public int PlayClockSeconds { get; set; } = 40;
    public int TimeoutClockSeconds { get; set; } = 60;
    public int PeriodSeconds { get; set; } = 720;
    public int TotalPeriods { get; set; } = 4;
    public string PeriodLabel { get; set; } = "Quarter";
    public int[] ScoringOptions { get; set; } = [1, 2, 3];
    public int ShotClockSeconds { get; set; } = 30;
    public int Down { get; set; } = 1;
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
    public List<GameEvent> Events { get; set; } = [];

    /// <summary>The colour behind a team palette slot, or null for a fixed colour.</summary>
    public string? ColorFor(ScorebugColorSource source) => source switch
    {
        ScorebugColorSource.HomePrimary => HomePrimaryColor,
        ScorebugColorSource.HomeSecondary => HomeSecondaryColor,
        ScorebugColorSource.AwayPrimary => AwayPrimaryColor,
        ScorebugColorSource.AwaySecondary => AwaySecondaryColor,
        _ => null
    };

    public string? LogoFor(ScorebugLogoSide side) => side switch
    {
        ScorebugLogoSide.Home => HomeLogoPath,
        ScorebugLogoSide.Away => AwayLogoPath,
        _ => null
    };
}

public sealed record GameEvent(DateTimeOffset At, string Type, string Description, int? HomeScore = null, int? AwayScore = null);
public sealed record GameSnapshot(GameState State, string Label);

public sealed class GameEngine
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    readonly Stopwatch _watch = Stopwatch.StartNew();
    long _lastTick;
    readonly Stack<GameSnapshot> _undo = new();
    public SportCatalog Sports { get; }
    public GameState State { get; private set; } = new();
    public event Action? Changed;
    /// <summary>
    /// True while a hardware controller is feeding the clock. The device is authoritative then,
    /// so the local timer must not also tick the same clock down between snapshots.
    /// </summary>
    public bool HardwareControlled { get; set; }
    public SportDefinition Sport => Sports.Find(State.SportId) ?? SportDefinition.BuiltIns[^1];

    public GameEngine(SportCatalog? sports = null) => Sports = sports ?? new SportCatalog();

    public void NewGame(string sportId)
    {
        var sport = Sports.Find(sportId) ?? SportDefinition.BuiltIns[^1];
        State = new GameState { SportId = sport.Id, ClockSeconds = sport.PeriodSeconds, PeriodSeconds = sport.PeriodSeconds, TotalPeriods = sport.Periods, PeriodLabel = sport.Unit, ScoringOptions = sport.ScoringOptions.ToArray(), ClockCountsUp = sport.CountUp, HomeTimeouts = sport.Timeouts, AwayTimeouts = sport.Timeouts, PlayClockSeconds = 40, TimeoutClockSeconds = 60, Controls = sport.Controls.Select(c => c with { }).ToList() };
        // A profile's starting values (a full count of balls to start, a power play already on) are
        // what a new game opens with; stepping them afterwards changes only this game.
        foreach (var control in State.Controls)
            SportValues.Write(State, control.Id, Math.Clamp(control.Default, control.Min, control.Max));
        _undo.Clear(); _lastTick = _watch.ElapsedTicks; Changed?.Invoke();
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

    public void SetTeams(string home, string away)
    {
        SaveUndo("Teams"); State.HomeTeam = home; State.AwayTeam = away;
        State.HomeAbbreviation = Abbreviate(home); State.AwayAbbreviation = Abbreviate(away); Log("Teams", $"{home} vs {away}"); Changed?.Invoke();
    }

    /// <summary>
    /// Saves everything the control panel's team pop-up edits: name, abbreviation, logo and both
    /// palette colours. Team identity publishes live like the score, so the graphic never waits.
    /// </summary>
    public void SetTeamIdentity(bool home, string name, string abbreviation, string? logoPath, string primary, string secondary, string? mascot = null)
    {
        name = name.Trim();
        if (name.Length == 0) name = home ? State.HomeTeam : State.AwayTeam;
        abbreviation = abbreviation.Trim().ToUpperInvariant();
        if (abbreviation.Length == 0) abbreviation = Abbreviate(name);
        mascot = (mascot ?? "").Trim();
        primary = NormalizeColor(primary, home ? "#FF38D7A3" : "#FF4EA8FF");
        secondary = NormalizeColor(secondary, "#FF101820");
        if (home)
        {
            if (State.HomeTeam == name && State.HomeAbbreviation == abbreviation && State.HomeLogoPath == logoPath
                && State.HomeMascot == mascot && State.HomePrimaryColor == primary && State.HomeSecondaryColor == secondary) return;
            SaveUndo("Home team details");
            State.HomeTeam = name; State.HomeAbbreviation = abbreviation; State.HomeLogoPath = logoPath;
            State.HomeMascot = mascot;
            State.HomePrimaryColor = primary; State.HomeSecondaryColor = secondary;
        }
        else
        {
            if (State.AwayTeam == name && State.AwayAbbreviation == abbreviation && State.AwayLogoPath == logoPath
                && State.AwayMascot == mascot && State.AwayPrimaryColor == primary && State.AwaySecondaryColor == secondary) return;
            SaveUndo("Visitors team details");
            State.AwayTeam = name; State.AwayAbbreviation = abbreviation; State.AwayLogoPath = logoPath;
            State.AwayMascot = mascot;
            State.AwayPrimaryColor = primary; State.AwaySecondaryColor = secondary;
        }
        Log("Teams", $"{State.HomeTeam} ({State.HomeAbbreviation}) vs {State.AwayTeam} ({State.AwayAbbreviation})");
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
        SaveUndo("Score"); if (home) State.HomeScore = Math.Max(0, State.HomeScore + amount); else State.AwayScore = Math.Max(0, State.AwayScore + amount);
        Log("Score", $"{(home ? State.HomeTeam : State.AwayTeam)} {(amount >= 0 ? "+" : "−")}{Math.Abs(amount)}"); Changed?.Invoke();
    }

    public void SetScore(bool home, int value)
    {
        SaveUndo("Score correction"); if (home) State.HomeScore = Math.Max(0, value); else State.AwayScore = Math.Max(0, value);
        Log("Score", $"{(home ? State.HomeTeam : State.AwayTeam)} score set to {Math.Max(0, value)}"); Changed?.Invoke();
    }

    public void ToggleClock()
    {
        Tick(); SaveUndo("Clock"); State.ClockRunning = !State.ClockRunning; _lastTick = _watch.ElapsedTicks;
        Log("Clock", State.ClockRunning ? "Clock started" : "Clock stopped"); Changed?.Invoke();
    }

    public void SetClock(int seconds)
    {
        Tick(); SaveUndo("Clock correction"); State.ClockSeconds = Math.Max(0, seconds); _lastTick = _watch.ElapsedTicks;
        Log("Clock", $"Clock set to {FormatClock(State.ClockSeconds)}"); Changed?.Invoke();
    }

    public void AdjustClock(int seconds) => SetClock(State.ClockSeconds + seconds);

    public void Tick()
    {
        if (HardwareControlled) { _lastTick = _watch.ElapsedTicks; return; }
        var now = _watch.ElapsedTicks;
        var elapsed = (int)((now - _lastTick) / Stopwatch.Frequency);
        if (elapsed <= 0) return;
        _lastTick += (long)elapsed * Stopwatch.Frequency;
        // Every clock counts: a play or timeout clock that expires on its own must still raise the
        // event, or the panel never repaints the final 0.
        var wasRunning = State.ClockRunning || State.ShotClockRunning || State.PlayClockRunning || State.TimeoutClockRunning;
        if (State.ClockRunning)
        {
            State.ClockSeconds = State.ClockCountsUp ? State.ClockSeconds + elapsed : Math.Max(0, State.ClockSeconds - elapsed);
            if (State.ClockSeconds == 0 && !State.ClockCountsUp) { State.ClockRunning = false; Log("Clock", "Period ended"); }
        }
        if (State.ShotClockRunning)
        {
            State.ShotClockSeconds = Math.Max(0, State.ShotClockSeconds - elapsed);
            if (State.ShotClockSeconds == 0) { State.ShotClockRunning = false; Log("Shot clock", "Shot clock expired"); }
        }
        if (State.PlayClockRunning)
        {
            State.PlayClockSeconds = Math.Max(0, State.PlayClockSeconds - elapsed);
            if (State.PlayClockSeconds == 0) { State.PlayClockRunning = false; Log("Play clock", "Play clock expired"); }
        }
        if (State.TimeoutClockRunning)
        {
            State.TimeoutClockSeconds = Math.Max(0, State.TimeoutClockSeconds - elapsed);
            if (State.TimeoutClockSeconds == 0) { State.TimeoutClockRunning = false; Log("Timeout clock", "Timeout clock expired"); }
        }
        // Only notify the UI when a running clock actually advanced, so idle ticks cost nothing.
        if (wasRunning || State.ClockRunning || State.ShotClockRunning || State.PlayClockRunning || State.TimeoutClockRunning) Changed?.Invoke();
    }

    public void NextPeriod()
    {
        SaveUndo("Period"); State.Period++; State.ClockSeconds = State.PeriodSeconds; State.ClockRunning = false;
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
        SaveUndo("Period"); State.Period--; State.ClockSeconds = State.PeriodSeconds; State.ClockRunning = false;
        Log("Period", $"{State.PeriodLabel} {State.Period}"); Changed?.Invoke();
    }

    public void TogglePossession() { SaveUndo("Possession"); State.HomePossession = !State.HomePossession; Log("Possession", $"{(State.HomePossession ? State.HomeTeam : State.AwayTeam)} possession"); Changed?.Invoke(); }

    public void SetPossession(bool home) { if (State.HomePossession == home) return; SaveUndo("Possession"); State.HomePossession = home; Log("Possession", $"{(home ? State.HomeTeam : State.AwayTeam)} possession"); Changed?.Invoke(); }

    public void SetDown(int down) { if (State.SportId != "football") return; var value = Math.Clamp(down, 1, 4); if (State.Down == value) return; RememberDown(); SaveUndo("Down"); State.Down = value; Log("Down", $"Down {State.Down}, {State.YardsToGo} to go"); Changed?.Invoke(); }

    public void SetTimeoutCount(bool home, int value)
    {
        var clamped = Math.Clamp(value, 0, 99);
        if ((home ? State.HomeTimeouts : State.AwayTimeouts) == clamped) return;
        SaveUndo("Timeouts"); if (home) State.HomeTimeouts = clamped; else State.AwayTimeouts = clamped;
        Log("Timeout", $"{(home ? State.HomeTeam : State.AwayTeam)} timeouts {clamped}"); Changed?.Invoke();
    }

    public void TogglePlayClock()
    {
        if (State.SportId != "football") return;
        Tick(); SaveUndo("Play clock");
        State.PlayClockRunning = !State.PlayClockRunning;
        // Starting an expired clock reloads it: otherwise it would sit at 0 and stop again a
        // second later, which looks like the button does nothing.
        if (State.PlayClockRunning && State.PlayClockSeconds <= 0) State.PlayClockSeconds = 40;
        _lastTick = _watch.ElapsedTicks;
        Log("Play clock", State.PlayClockRunning ? $"Play clock started at {State.PlayClockSeconds}" : "Play clock stopped"); Changed?.Invoke();
    }

    public void SetPlayClock(int seconds)
    {
        if (State.SportId != "football") return;
        Tick(); SaveUndo("Play clock"); State.PlayClockSeconds = Math.Clamp(seconds, 0, 99); _lastTick = _watch.ElapsedTicks;
        Log("Play clock", $"Play clock set to {State.PlayClockSeconds}"); Changed?.Invoke();
    }

    public void ResetPlayClock(int seconds = 40)
    {
        if (State.SportId != "football") return;
        Tick(); SaveUndo("Play clock reset"); State.PlayClockSeconds = Math.Clamp(seconds, 1, 99); State.PlayClockRunning = false; _lastTick = _watch.ElapsedTicks;
        Log("Play clock", $"Play clock reset to {State.PlayClockSeconds}"); Changed?.Invoke();
    }

    public void ToggleTimeoutClock()
    {
        Tick(); SaveUndo("Timeout clock"); State.TimeoutClockRunning = !State.TimeoutClockRunning; _lastTick = _watch.ElapsedTicks;
        Log("Timeout clock", State.TimeoutClockRunning ? "Timeout clock started" : "Timeout clock stopped"); Changed?.Invoke();
    }

    public void SetTimeoutClock(int seconds)
    {
        Tick(); SaveUndo("Timeout clock"); State.TimeoutClockSeconds = Math.Clamp(seconds, 0, 599); _lastTick = _watch.ElapsedTicks;
        Log("Timeout clock", $"Timeout clock set to {FormatClock(State.TimeoutClockSeconds)}"); Changed?.Invoke();
    }

    public void ResetTimeoutClock(int seconds = 60)
    {
        Tick(); SaveUndo("Timeout clock reset"); State.TimeoutClockSeconds = Math.Clamp(seconds, 1, 599); State.TimeoutClockRunning = false; _lastTick = _watch.ElapsedTicks;
        Log("Timeout clock", $"Timeout clock reset to {FormatClock(State.TimeoutClockSeconds)}"); Changed?.Invoke();
    }
    // The down, distance and spot as they stood before the last change, so a mis-tap can be put back
    // with one button instead of four edits or a global undo.
    (int Down, int YardsToGo, int YardsToGoal)? _lastDown;

    void RememberDown() => _lastDown = (State.Down, State.YardsToGo, State.YardsToGoal);

    /// <summary>Puts the down, distance and ball spot back to where they were before the last change.</summary>
    public void RecallLastDown()
    {
        if (State.SportId != "football" || _lastDown is not { } last) return;
        SaveUndo("Recall last down");
        State.Down = last.Down;
        State.YardsToGo = last.YardsToGo;
        State.YardsToGoal = last.YardsToGoal;
        _lastDown = null;
        Log("Down", $"Recalled {State.Down} & {State.YardsToGo}");
        Changed?.Invoke();
    }

    public void AdvanceDown() { if (State.SportId != "football") return; RememberDown(); SaveUndo("Down"); if (State.Down >= 4) { State.Down=1; State.YardsToGo=10; State.YardsToGoal=Math.Clamp(State.YardsToGoal-State.YardsToGo,0,50); } else State.Down++; Log("Down", $"Down {State.Down}, {State.YardsToGo} to go"); Changed?.Invoke(); }
    public void PreviousDown() { if (State.SportId != "football") return; RememberDown(); SaveUndo("Previous down"); if (State.Down > 1) State.Down--; Log("Down", $"Down {State.Down}, {State.YardsToGo} to go"); Changed?.Invoke(); }
    public void NewSeries() { if (State.SportId != "football") return; RememberDown(); SaveUndo("New series"); State.Down = 1; State.YardsToGo = 10; Log("Series", "New series · 1st & 10"); Changed?.Invoke(); }
    public void SetPeriodFormat(string format) { if(format is not ("Full" or "Short" or "Number" or "Ordinal"))return; if(State.QuarterDisplayFormat==format)return; SaveUndo("Period format"); State.QuarterDisplayFormat=format; Log("Period", $"Period display: {PeriodDisplay.Format(State)}"); Changed?.Invoke(); }
    /// <summary>Yard line is 0–50: a ball spot never goes past midfield, whichever team has it.</summary>
    public void SetFootballDistance(int yardsToGo,int? yardsToGoal=null) { if (State.SportId != "football") return; RememberDown(); SaveUndo("Football distance"); State.YardsToGo=Math.Clamp(yardsToGo,1,99); if(yardsToGoal is not null)State.YardsToGoal=Math.Clamp(yardsToGoal.Value,0,50); Log("Yards", $"{State.YardsToGo} to go, ball at {State.YardsToGoal}"); Changed?.Invoke(); }

    /// <summary>Which team's half the ball spot is on. Deliberately independent of possession.</summary>
    public void SetBallSide(bool home)
    {
        if (State.SportId != "football" || State.BallOnHomeSide == home) return;
        SaveUndo("Ball side"); State.BallOnHomeSide = home;
        Log("Ball spot", $"Ball on {(home ? State.HomeTeam : State.AwayTeam)}'s {State.YardsToGoal}");
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
    public void SetShotClock(int seconds) { if(State.SportId!="basketball")return; SaveUndo("Shot clock");State.ShotClockSeconds=Math.Clamp(seconds,0,99);State.ShotClockRunning=false;Log("Shot clock",$"Shot clock set to {State.ShotClockSeconds}");Changed?.Invoke(); }
    public void ToggleShotClock() { if(State.SportId!="basketball")return;SaveUndo("Shot clock");State.ShotClockRunning=!State.ShotClockRunning;_lastTick=_watch.ElapsedTicks;Log("Shot clock",State.ShotClockRunning?"Shot clock started":"Shot clock stopped");Changed?.Invoke(); }
    public void ResetShotClock(int seconds=24) { if(State.SportId!="basketball")return;SaveUndo("Shot clock reset");State.ShotClockSeconds=Math.Clamp(seconds,1,99);State.ShotClockRunning=false;Log("Shot clock",$"Shot clock reset to {State.ShotClockSeconds}");Changed?.Invoke(); }
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

    /// <summary>Sets a custom stat to an exact value, for the typed override in the control panel.</summary>
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
    /// Sets a custom stat to whatever the operator typed. A plain number becomes the stat's value
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
        var snapshot = _undo.Pop(); State = snapshot.State; _lastTick = _watch.ElapsedTicks; Log("Undo", $"Undid {snapshot.Label}"); Changed?.Invoke(); return true;
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
        // Older saves could hold a spot past midfield; bring it back into the 0–50 range.
        State.YardsToGoal = Math.Clamp(State.YardsToGoal, 0, 50);
        // A file written before sport values existed keeps the current profile's control list, so
        // the console still has balls, bases and power play to work with.
        State.SportValues ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        State.Controls ??= [];
        if (State.Controls.Count == 0)
            State.Controls = (Sports.Find(State.SportId)?.Controls ?? []).Select(c => c with { }).ToList();
        // Roster edits made since the file was written still reach the graphic on load.
        RefreshSelectedPlayers();
        _undo.Clear(); _lastTick = _watch.ElapsedTicks; Changed?.Invoke();
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
        s.Down, s.YardsToGo, s.YardsToGoal, s.BallOnHomeSide,
        s.HomeYellowCards, s.AwayYellowCards, s.HomeRedCards, s.AwayRedCards,
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
            if (home) State.HomeScore = next; else State.AwayScore = next;
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
        // A console drives down, distance and spot straight onto the game, so remember them before
        // the frame lands: the recall button then puts back the console's previous down, not just
        // a down someone typed into the panel. Repeated identical frames do not overwrite it.
        if (State.SportId == "football"
            && ((snapshot.Down is int nextDown && Math.Clamp(nextDown, 1, 4) != State.Down)
                || (snapshot.YardsToGo is int nextToGo && Math.Clamp(nextToGo, 1, 99) != State.YardsToGo)
                || (snapshot.BallOn is int nextSpot && Math.Clamp(nextSpot, 0, 50) != State.YardsToGoal)))
            RememberDown();
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
    public void LoadFromState(GameState state){State=JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state,JsonOptions),JsonOptions)!;RefreshSelectedPlayers();_undo.Clear();_lastTick=_watch.ElapsedTicks;Changed?.Invoke();}
    public bool UpdateLiveScoreClock(GameState source)
    {
        if (State.HomeScore == source.HomeScore && State.AwayScore == source.AwayScore &&
            State.HomeTeam == source.HomeTeam && State.AwayTeam == source.AwayTeam &&
            State.HomeAbbreviation == source.HomeAbbreviation && State.AwayAbbreviation == source.AwayAbbreviation &&
            State.HomeLogoPath == source.HomeLogoPath && State.AwayLogoPath == source.AwayLogoPath &&
            State.HomeMascot == source.HomeMascot && State.AwayMascot == source.AwayMascot &&
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
            PlayerSignature(State.AwayPlayer) == PlayerSignature(source.AwayPlayer)) return false;
        State.HomeScore=source.HomeScore;State.AwayScore=source.AwayScore;
        State.HomeTeam=source.HomeTeam;State.AwayTeam=source.AwayTeam;
        State.HomeAbbreviation=source.HomeAbbreviation;State.AwayAbbreviation=source.AwayAbbreviation;
        State.HomeLogoPath=source.HomeLogoPath;State.AwayLogoPath=source.AwayLogoPath;
        State.HomeMascot=source.HomeMascot;State.AwayMascot=source.AwayMascot;
        State.HomePrimaryColor=source.HomePrimaryColor;State.HomeSecondaryColor=source.HomeSecondaryColor;
        State.AwayPrimaryColor=source.AwayPrimaryColor;State.AwaySecondaryColor=source.AwaySecondaryColor;
        State.ClockSeconds=source.ClockSeconds;State.ClockRunning=source.ClockRunning;State.ClockCountsUp=source.ClockCountsUp;
        State.ShotClockSeconds=source.ShotClockSeconds;State.ShotClockRunning=source.ShotClockRunning;
        State.PlayClockSeconds=source.PlayClockSeconds;State.PlayClockRunning=source.PlayClockRunning;
        State.TimeoutClockSeconds=source.TimeoutClockSeconds;State.TimeoutClockRunning=source.TimeoutClockRunning;
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
    public static string FormatClock(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";
}
