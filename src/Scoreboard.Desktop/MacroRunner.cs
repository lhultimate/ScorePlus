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
    readonly Action<string>? _executeControl;

    public MacroRunner(GameEngine engine, MediaSession session, Action<string>? executeControl = null)
    {
        _engine = engine;
        _session = session;
        // A console button runs through the same path as a keybind, so a macro and a key press
        // can never disagree about what an action does.
        _executeControl = executeControl;
    }

    public string Run(SavedMacro macro)
    {
        var completed = 0;
        var skipped = 0;
        foreach (var action in macro.Actions)
        {
            if (Execute(action)) completed++;
            else skipped++;
        }
        return skipped == 0
            ? $"MACRO · {macro.Label} · {completed} step{(completed == 1 ? "" : "s")} run"
            : $"MACRO · {macro.Label} · {completed} run, {skipped} skipped (missing player or video)";
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
                return PlayVideo(action.Path);
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
            default:
                return false;
        }
    }

    bool PlayVideo(string path)
    {
        path = (path ?? "").Trim();
        if (path.Length == 0) return false;
        var entry = _session.Library.FirstOrDefault(item =>
            string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        if (entry is null && File.Exists(path))
        {
            _session.Import([path], MediaLibrary.DefaultTag);
            entry = _session.Library.FirstOrDefault(item =>
                string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        }
        if (entry is null) return false;
        _session.Play(entry);
        return true;
    }

    bool ChangeRosterStat(MacroAction action)
    {
        if (string.IsNullOrWhiteSpace(action.StatName)) return false;
        var state = _engine.State;
        var roster = RosterStore.Book.Find(action.Home ? state.HomeRosterId : state.AwayRosterId);
        if (roster is null) return false;
        var player = roster.Players.FirstOrDefault(candidate =>
            (!string.IsNullOrWhiteSpace(action.PlayerId) && string.Equals(candidate.Id, action.PlayerId, StringComparison.OrdinalIgnoreCase))
            || (string.IsNullOrWhiteSpace(action.PlayerId) && string.Equals(candidate.Name, action.PlayerName, StringComparison.OrdinalIgnoreCase)));
        if (player is null) return false;
        var before = player.Stat(action.StatName);
        player.SetStat(action.StatName, action.Delta ? before + action.Value : action.Value);
        RosterStore.Save();
        // The graphic prints a snapshot of the selected player, so refresh the on-air copy too.
                _engine.ReloadSelectedPlayer(action.Home);
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
