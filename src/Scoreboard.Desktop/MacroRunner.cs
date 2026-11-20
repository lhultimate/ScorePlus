using System.IO;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// Runs the ordered steps of a macro against the live game and media session. The runner is
/// deliberately small and synchronous: pressing a macro should feel like pressing the individual
/// controls in sequence, and a later step must see the result of every earlier step.
/// </summary>
public sealed class MacroRunner
{
    readonly GameEngine _engine;
    readonly MediaSession _session;
    readonly ScorebugCatalog? _catalog;
    readonly Action<string>? _executeControl;
    readonly Action? _publishOutput;

    public MacroRunner(GameEngine engine, MediaSession session, Action<string>? executeControl = null,
        ScorebugCatalog? catalog = null, Action? publishOutput = null)
    {
        _engine = engine;
        _session = session;
        _catalog = catalog;
        // A console button runs through the same path as a keybind, so a macro and a key press
        // can never disagree about what an action does.
        _executeControl = executeControl;
        _publishOutput = publishOutput;
    }

    /// <summary>Counters and loop guards for one press of a macro, shared by the macros it chains to.</summary>
    sealed class RunScope
    {
        public readonly HashSet<string> Running = new(StringComparer.OrdinalIgnoreCase);
        public bool Held;
        public int Completed;
        public int Skipped;
    }

    // A macro that runs another macro can chain, so the depth is capped and a macro that is
    // already running is skipped rather than recursing forever.
    const int MaxChainDepth = 8;

    public async Task<string> RunAsync(SavedMacro macro)
    {
        var scope = new RunScope();
        scope.Running.Add(macro.Id);
        await RunStepsAsync(macro.Actions ?? [], scope, depth: 0);
        var waiting = _engine.MacroOutputHold ? " · press Update Output to publish" : string.Empty;
        var completed = scope.Completed;
        var skipped = scope.Skipped;
        var result = skipped == 0
            ? $"MACRO · {macro.Label} · {completed} step{(completed == 1 ? "" : "s")} run{waiting}"
            : $"MACRO · {macro.Label} · {completed} run, {skipped} skipped (missing player, source or macro){waiting}";
        // Every macro press belongs in the show record, including how many steps it actually ran.
        _engine.RecordEvent("Macro", result);
        return result;
    }

    /// <summary>
    /// True for a step that acts at once rather than waiting for Update Output: playback, a layout
    /// switch, a pause, a heading, a level change, or a chained macro (whose own steps decide).
    /// </summary>
    static bool IsDirect(MacroAction action) => action.BypassOutput || action.Kind is MacroActionKind.PlayVideo
        or MacroActionKind.PutOnAir or MacroActionKind.TakeOffAir or MacroActionKind.SwitchLayout
        or MacroActionKind.Wait or MacroActionKind.Note or MacroActionKind.RunMacro
        or MacroActionKind.MediaVolume or MacroActionKind.Replay;

    async Task RunStepsAsync(IEnumerable<MacroAction> steps, RunScope scope, int depth)
    {
        foreach (var action in steps)
        {
            // A direct step is never staged. Every other step defaults to staged, so a macro cannot
            // accidentally put a score or clock on air before the operator presses Update Output.
            // Once a step holds output, later checked steps stay held too; otherwise they would
            // publish the earlier staged values along with their own.
            var direct = IsDirect(action);
            if (!direct)
            {
                _engine.HoldMacroOutput();
                scope.Held = true;
            }
            if (action.Kind == MacroActionKind.Wait)
            {
                var pause = Math.Clamp(double.IsFinite(action.WaitSeconds) ? action.WaitSeconds : 0, 0, 60);
                if (pause > 0) await Task.Delay(TimeSpan.FromSeconds(pause));
                scope.Completed++;
                continue;
            }
            if (action.Kind == MacroActionKind.Note)
            {
                // A heading is a signpost in the step list, not a change to the show.
                scope.Completed++;
                continue;
            }
            if (action.Kind == MacroActionKind.RunMacro)
            {
                if (await RunLinkedAsync(action, scope, depth)) scope.Completed++;
                else scope.Skipped++;
                continue;
            }
            var ran = Execute(action);
            if (ran) scope.Completed++;
            else scope.Skipped++;
            // A checked step has to publish, not merely avoid the hold. A macro made only of
            // checked steps previously changed the working game and left the output untouched.
            if (ran && direct && !scope.Held) _publishOutput?.Invoke();
        }
    }

    /// <summary>Runs the macro named by a Run macro step, inside the same press and hold state.</summary>
    async Task<bool> RunLinkedAsync(MacroAction action, RunScope scope, int depth)
    {
        if (depth >= MaxChainDepth) return false;
        var macros = _engine.State.Macros;
        var target = macros.FirstOrDefault(macro =>
                !string.IsNullOrWhiteSpace(action.Path)
                && string.Equals(macro.Id, action.Path, StringComparison.OrdinalIgnoreCase))
            ?? macros.FirstOrDefault(macro => string.Equals(macro.Label, action.Label, StringComparison.OrdinalIgnoreCase));
        if (target is null) return false;
        // A loop (or a macro that runs itself) is skipped, not a failure of the whole press.
        if (!scope.Running.Add(target.Id)) return false;
        try { await RunStepsAsync(target.Actions ?? [], scope, depth + 1); }
        finally { scope.Running.Remove(target.Id); }
        return true;
    }

    bool Execute(MacroAction action)
    {
        switch (action.Kind)
        {
            case MacroActionKind.Score:
                _engine.Score(action.Home, action.Amount);
                return true;
            case MacroActionKind.SetDown:
                _engine.SetDown(action.Value);
                return true;
            case MacroActionKind.SetTimeouts:
                _engine.SetTimeoutCount(action.Home, action.Value);
                return true;
            case MacroActionKind.SetGameClock:
                _engine.SetClock(action.Value);
                return true;
            case MacroActionKind.SetPlayClock:
                _engine.SetPlayClock(action.Value);
                return true;
            case MacroActionKind.SetTimeoutClock:
                _engine.SetTimeoutClock(action.Value);
                return true;
            case MacroActionKind.SetPeriod:
                _engine.SetPeriod(action.Value);
                return true;
            case MacroActionKind.PlayVideo:
                return PlayVideo(action.Path, restartClip: true);
            case MacroActionKind.PutOnAir:
                return PlayVideo(action.Path, restartClip: false);
            case MacroActionKind.TakeOffAir:
                return TakeOffAir(action.Path);
            case MacroActionKind.SwitchLayout:
                return SwitchLayout(action);
            case MacroActionKind.PenaltyFlag:
                _engine.SetPenalty(action.Value != 0, action.Path, action.Home);
                return true;
            case MacroActionKind.RosterStat:
                return ChangeRosterStat(action);
            case MacroActionKind.Control:
                if (_executeControl is null || string.IsNullOrWhiteSpace(action.Path)) return false;
                _executeControl(action.Path);
                return true;
            case MacroActionKind.SportValue:
                return ChangeSportValue(action);
            case MacroActionKind.SetFootballDistance:
                if (_engine.State.SportId != "football") return false;
                _engine.SetFootballDistance(action.Value);
                return true;
            case MacroActionKind.SetFootballBallSpot:
                if (_engine.State.SportId != "football") return false;
                _engine.SetFootballDistance(_engine.State.YardsToGo, action.Value);
                return true;
            case MacroActionKind.SetPossession:
                _engine.SetPossession(action.Home);
                return true;
            case MacroActionKind.CustomValue:
                return ChangeCustomValue(action);
            case MacroActionKind.ClockControl:
                return ControlClock(action);
            case MacroActionKind.ClockMessage:
                _engine.SetClockMessage((GameClockMessage)Math.Clamp(action.Value, 0, 2));
                return true;
            case MacroActionKind.MediaVolume:
                return SetMediaVolume(action);
            case MacroActionKind.TickerNews:
                return ApplyTickerNews(action);
            case MacroActionKind.Replay:
                return RunReplay(action);
            default:
                return false;
        }
    }

    /// <summary>
    /// One media step on its own, for a conditional that puts a library item on or off air
    /// without a whole macro behind it. It goes through the same path a macro step does.
    /// </summary>
    public bool RunMediaStep(string path, bool onAir) =>
        onAir ? PlayVideo(path, restartClip: false) : TakeOffAir(path);

    /// <summary>The library item a step names, importing a file the library has not seen yet.</summary>
    MediaEntry? FindMedia(string? path)
    {
        path = (path ?? "").Trim();
        if (path.Length == 0) return null;
        var entry = _session.Library.FirstOrDefault(item =>
            string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        if (entry is null && File.Exists(path))
        {
            _session.Import([path], MediaLibrary.DefaultTag);
            entry = _session.Library.FirstOrDefault(item =>
                string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        }
        return entry;
    }

    bool PlayVideo(string path, bool restartClip)
    {
        var entry = FindMedia(path);
        if (entry is null) return false;
        // Cameras, microphones and speaker buses are on/off sources, not transport clips. A macro
        // should put one on air without restarting a source that is already live.
        if (entry.IsCamera || entry.IsMicrophone || entry.IsSpeaker)
        {
            if (_session.IsOnAir(entry)) return true;
            return _session.ToggleOnAir(entry);
        }
        // Put-on-air is idempotent for a clip that is already the current cue; Play video is the
        // explicit restart. Both still send the clip to the audience through the media session.
        if (!restartClip && _session.IsOnAir(entry)) return true;
        _session.Play(entry);
        return true;
    }

    bool SwitchLayout(MacroAction action)
    {
        if (_catalog is null || string.IsNullOrWhiteSpace(action.Path)) return false;
        var display = _catalog.FindDisplay(action.DisplayId)
            ?? _catalog.FindDisplayOfLayout(action.Path);
        if (display is null) return false;
        var layout = _catalog.FindLayout(display.Id, action.Path);
        if (layout is null) return false;
        return _catalog.ActivateLayout(display.Id, layout.Id, publish: true);
    }

    bool TakeOffAir(string path)
    {
        path = (path ?? "").Trim();
        if (path.Length == 0) return false;
        var entry = _session.Library.FirstOrDefault(item =>
            string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return false;
        // Already off air is not a failure: a macro can safely make sure a source is clear.
        if (!_session.IsOnAir(entry)) return true;
        _session.ToggleOnAir(entry);
        return true;
    }

    /// <summary>
    /// Starts, stops, toggles or resets one clock. Start and stop are explicit so a macro can be
    /// pressed twice without flipping a running clock back on; toggle stays for a single button.
    /// </summary>
    bool ControlClock(MacroAction action)
    {
        var clock = (action.Path ?? "").Trim().ToLowerInvariant();
        var operation = Math.Clamp(action.Value, 0, 3);
        var resetSeconds = Math.Max(0, action.Amount);
        var state = _engine.State;
        switch (clock)
        {
            case "play":
                if (state.SportId != "football") return false;
                if (operation == 3) { _engine.ResetPlayClock(resetSeconds > 0 ? resetSeconds : 40); return true; }
                if (operation == 2 || (operation == 0) != state.PlayClockRunning) _engine.TogglePlayClock();
                return true;
            case "timeout":
                if (operation == 3) { _engine.ResetTimeoutClock(resetSeconds > 0 ? resetSeconds : 60); return true; }
                if (operation == 2 || (operation == 0) != state.TimeoutClockRunning) _engine.ToggleTimeoutClock();
                return true;
            case "shot":
                if (state.SportId != "basketball") return false;
                if (operation == 3) { _engine.ResetShotClock(resetSeconds > 0 ? resetSeconds : 24); return true; }
                if (operation == 2 || (operation == 0) != state.ShotClockRunning) _engine.ToggleShotClock();
                return true;
            case "stoppage":
                if (state.SportId != "soccer") return false;
                if (operation == 3) { _engine.ResetStoppageClock(); return true; }
                if (operation == 2 || (operation == 0) != state.StoppageRunning) _engine.ToggleStoppageClock();
                return true;
            default:
                if (operation == 3) { _engine.SetClock(resetSeconds); return true; }
                if (operation == 2 || (operation == 0) != state.ClockRunning) _engine.ToggleClock();
                return true;
        }
    }

    bool SetMediaVolume(MacroAction action)
    {
        var entry = FindMedia(action.Path);
        if (entry is null) return false;
        if (action.Mute)
        {
            if (!entry.IsSpeaker) return false;
            return _session.SetSpeakerMuted(entry, action.Muted);
        }
        _session.SetClipVolume(entry, Math.Clamp(action.Value, 0, 100) / 100.0);
        return true;
    }

    /// <summary>
    /// One replay step: change the speed, set up a replay at a chosen point back from the live
    /// edge, start it on air, or come back to live. The steps drive the same session calls the
    /// media rail uses, so a macro can set up and start a replay without dragging anything.
    /// </summary>
    bool RunReplay(MacroAction action)
    {
        switch ((action.Path ?? "").Trim().ToLowerInvariant())
        {
            case "speed":
                if (action.ReplaySpeed <= 0) return false;
                _session.SetReplaySpeed(action.ReplaySpeed);
                return true;
            case "prepare":
                if (!_session.BeginReplay()) return false;
                // WaitSeconds is how far back from the live edge to open. Zero keeps the live edge,
                // and Start Replay then steps back on its own.
                if (action.WaitSeconds > 0) _session.SeekReplay(action.WaitSeconds);
                return true;
            case "start":
                // Start Replay puts the prepared replay on air exactly as the operator left it. A
                // macro that says start means "run it", so a paused replay is rolled on too.
                var started = _session.PlayReplay();
                if (started && !_session.IsCurrentPlaying) _session.TogglePlayPause();
                return started;
            case "playpause":
                _session.TogglePlayPause();
                return true;
            case "live":
                return _session.ReturnToLive();
            default:
                return false;
        }
    }

    bool ApplyTickerNews(MacroAction action)
    {
        var headlines = (action.Text ?? "")
            .Split(['\r', '\n', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var state = _engine.State;
        // Everything the ticker page also owns is passed through, so a macro step replaces the
        // headlines without turning the rotation or the score alerts off.
        _engine.SetTickerSettings(state.TickerGameIds, headlines, state.TickerAutoRotate,
            state.TickerScoreAlerts, state.TickerDwellSeconds, state.TickerTransitionSeconds);
        return true;
    }

    bool ChangeRosterStat(MacroAction action)
    {
        if (string.IsNullOrWhiteSpace(action.StatName)) return false;
        var state = _engine.State;
        var roster = RosterStore.Book.Find(action.Home ? state.HomeRosterId : state.AwayRosterId);
        if (roster is null) return false;
        // A step with no player named follows the show: it changes whoever is on air for that side,
        // so the same macro works for any team and any substitution without being edited.
        var playerId = (action.PlayerId ?? "").Trim();
        if (playerId.Length == 0) playerId = (action.Home ? state.HomePlayer : state.AwayPlayer)?.PlayerId ?? "";
        var player = roster.Players.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, playerId, StringComparison.OrdinalIgnoreCase))
            ?? (string.IsNullOrWhiteSpace(action.PlayerId) && !string.IsNullOrWhiteSpace(action.PlayerName)
                ? roster.Players.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, action.PlayerName, StringComparison.OrdinalIgnoreCase))
                : null);
        if (player is null) return false;
        var before = player.Stat(action.StatName);
        player.SetStat(action.StatName, action.Delta ? before + action.Value : action.Value);
        RosterStore.Save();
        _engine.RecordEvent("Roster", $"{(action.Home ? "Home" : "Visitors")} · {player.Record} · {action.StatName} {(action.Delta ? (action.Value >= 0 ? "+" : "−") + Math.Abs(action.Value) : "= " + action.Value)} = {player.Stat(action.StatName)}");
        // The graphic prints a snapshot of the selected player. With automatic stat updates off,
        // the change waits for Update Output like every other roster edit on the desk.
        _engine.ReloadSelectedPlayer(action.Home, publish: _engine.PlayerStatsLive);
        return true;
    }

    bool ChangeCustomValue(MacroAction action)
    {
        var stat = _engine.State.Stats.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, action.Path, StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(action.Path)
                && string.Equals(candidate.Name, action.Label, StringComparison.OrdinalIgnoreCase)));
        if (stat is null) return false;
        if (action.Delta) _engine.AdjustStat(stat.Id, action.Value);
        else _engine.SetStat(stat.Id, action.Value);
        return true;
    }

    bool ChangeSportValue(MacroAction action)
    {
        var control = _engine.State.Controls.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, action.Path, StringComparison.OrdinalIgnoreCase));
        if (control is null) return false;
        if (control.Kind == SportControlKind.Flag)
        {
            var wanted = action.Delta ? (SportValues.Read(_engine.State, control.Id) != 0 ? 0 : 1) : (action.Value != 0 ? 1 : 0);
            _engine.SetSportValue(control.Id, wanted);
        }
        else if (action.Delta) _engine.AdjustSportValue(control.Id, action.Value);
        else _engine.SetSportValue(control.Id, action.Value);
        return true;
    }
}
