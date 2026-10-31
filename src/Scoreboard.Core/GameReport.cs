using System.Net;
using System.Text;

namespace Scoreboard.Core;

/// <summary>
/// The printable record of a game: the score, every value the console holds, the sport's own
/// controls, the custom stats, both rosters in full (zeroes included) and the event timeline.
/// Written as one self-contained HTML page so it can be printed, saved as PDF or e-mailed after
/// the game. <see cref="Build"/> takes the rosters so a report shows every player, not just the
/// two who happened to be on the graphic.
/// </summary>
public static class GameReport
{
    public static string Build(GameState state, IEnumerable<Roster>? rosters = null)
    {
        var books = rosters?.Where(roster => roster is not null).ToList() ?? [];
        var html = new StringBuilder();

        html.AppendLine("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.AppendLine($"<title>{E(state.HomeTeam)} vs {E(state.AwayTeam)} · Game report</title>");
        html.AppendLine("<style>");
        html.AppendLine("""
            :root{--ink:#17212b;--muted:#64748b;--line:#d7dee7;--soft:#f1f5f9;--accent:#0f9b74}
            *{box-sizing:border-box}
            body{font:15px/1.5 "Segoe UI",Arial,sans-serif;max-width:1000px;margin:36px auto;padding:0 20px;color:var(--ink);background:#fff}
            h1{font-size:26px;margin:0 0 4px}
            h2{font-size:12px;text-transform:uppercase;letter-spacing:.09em;color:var(--muted);margin:26px 0 8px;border-bottom:2px solid var(--soft);padding-bottom:6px}
            .muted{color:var(--muted)}
            .score{font-size:38px;font-weight:700;margin:16px 0;letter-spacing:.02em}
            .cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:12px}
            .card{border:1px solid var(--line);border-radius:10px;padding:13px 16px;background:#fff;break-inside:avoid}
            .card b{display:block;font-size:11px;letter-spacing:.09em;text-transform:uppercase;color:var(--muted);margin-bottom:6px}
            .roster{border:1px solid var(--line);border-radius:10px;padding:13px 16px;margin-bottom:12px}
            .roster-head{display:flex;justify-content:space-between;align-items:baseline;gap:12px;flex-wrap:wrap}
            .roster-head strong{font-size:16px}
            .side{font-size:11px;letter-spacing:.08em;text-transform:uppercase;color:var(--accent);font-weight:700}
            table{border-collapse:collapse;width:100%;margin-top:8px;font-size:13px}
            th,td{text-align:left;padding:6px 8px;border-bottom:1px solid var(--line);white-space:nowrap}
            th{background:var(--soft);font-size:11px;text-transform:uppercase;letter-spacing:.05em;color:#475569}
            td.num{text-align:right;font-variant-numeric:tabular-nums}
            .scroller{overflow-x:auto}
            .empty{color:var(--muted);font-style:italic}
            @media print{body{margin:0;max-width:none;padding:0}h2{margin-top:18px}tr{break-inside:avoid}}
            """);
        html.AppendLine("</style></head><body>");

        // ---------- header ----------
        html.AppendLine($"<h1>{E(state.HomeTeam)} vs {E(state.AwayTeam)}</h1>");
        html.AppendLine($"<div class=\"muted\">{E(SportName(state))} · Game report · generated {DateTime.Now:yyyy-MM-dd HH:mm}</div>");
        html.AppendLine($"<div class=\"score\">{E(state.HomeAbbreviation)} {state.HomeScore} — {state.AwayScore} {E(state.AwayAbbreviation)}</div>");

        // ---------- game state ----------
        html.AppendLine("<h2>Game</h2><div class=\"cards\">");
        html.AppendLine(Card("Clock",
            $"{E(PeriodDisplay.Format(state))} of {state.TotalPeriods}",
            $"Game clock: {GameEngine.FormatClock(state.ClockSeconds)} {(state.ClockRunning ? "(running)" : "(stopped)")}",
            state.ClockCountsUp ? "Clock counts up" : "Clock counts down",
            $"Period length: {GameEngine.FormatClock(state.PeriodSeconds)}"));
        html.AppendLine(Card("Score",
            $"{E(state.HomeTeam)} ({E(state.HomeAbbreviation)}): <b>{state.HomeScore}</b>",
            $"{E(state.AwayTeam)} ({E(state.AwayAbbreviation)}): <b>{state.AwayScore}</b>",
            $"Scoring values: {string.Join(", ", state.ScoringOptions)}",
            $"Timeouts: {state.HomeTimeouts} — {state.AwayTimeouts}"));
        html.AppendLine(Card("Possession",
            $"{E(state.HomePossession ? state.HomeTeam : state.AwayTeam)} has possession",
            $"Timeout clock: {GameEngine.FormatClock(state.TimeoutClockSeconds)} {(state.TimeoutClockRunning ? "(running)" : "(stopped)")}"));
        html.AppendLine(Card("Record",
            $"Sport: {E(SportName(state))}",
            $"Saved: {state.SavedAt:yyyy-MM-dd HH:mm}",
            $"Events logged: {state.Events.Count}",
            $"Custom stats: {state.Stats.Count}"));
        html.AppendLine("</div>");

        // ---------- the sport's own detail ----------
        html.AppendLine($"<h2>{E(SportName(state))} detail</h2><div class=\"cards\">");
        AppendSportDetail(html, state);
        html.AppendLine("</div>");

        // ---------- every sport control, whatever the sport ----------
        if (state.Controls.Count > 0)
        {
            html.AppendLine("<h2>Sport values</h2><div class=\"scroller\"><table><thead><tr><th>Value</th><th>Setting</th></tr></thead><tbody>");
            foreach (var control in state.Controls)
            {
                var printed = SportValues.Display(state, control);
                if (printed.Length == 0) printed = "OFF";
                html.AppendLine($"<tr><td>{E(control.Label)}</td><td class=\"num\">{E(printed)}</td></tr>");
            }
            html.AppendLine("</tbody></table></div>");
        }

        // ---------- active players ----------
        html.AppendLine("<h2>On the graphic</h2><div class=\"cards\">");
        html.AppendLine(ActivePlayerCard("Home", state.HomeTeam, state.HomePlayerCleared, state.HomePlayer));
        html.AppendLine(ActivePlayerCard("Visitors", state.AwayTeam, state.AwayPlayerCleared, state.AwayPlayer));
        html.AppendLine("</div>");

        // ---------- custom stats ----------
        html.AppendLine("<h2>Custom stats</h2>");
        if (state.Stats is { Count: > 0 })
        {
            // Printed as "name: value" lines rather than table cells, so a stat reads the same on
            // the page as it does in the control panel and in a printed handout.
            html.AppendLine("<div class=\"card\">");
            foreach (var stat in state.Stats)
                html.AppendLine($"{E(stat.Name)}: {E(stat.Display)}<br/>");
            html.AppendLine("</div>");
        }
        else
        {
            html.AppendLine("<p class=\"empty\">This game has no custom stats.</p>");
        }

        // ---------- rosters ----------
        html.AppendLine("<h2>Rosters</h2>");
        if (books.Count == 0)
        {
            html.AppendLine("<p class=\"empty\">No rosters were supplied with this report.</p>");
        }
        else
        {
            foreach (var roster in books) AppendRoster(html, roster, state);
        }

        // ---------- timeline ----------
        html.AppendLine("<h2>Event timeline</h2>");
        if (state.Events.Count == 0)
        {
            html.AppendLine("<p class=\"empty\">Nothing has been logged in this game yet.</p>");
        }
        else
        {
            html.AppendLine("<div class=\"scroller\"><table><thead><tr><th>Time</th><th>Score at event</th><th>Action</th></tr></thead><tbody>");
            foreach (var gameEvent in state.Events)
                html.AppendLine($"<tr><td>{gameEvent.At:yyyy-MM-dd HH:mm:ss}</td><td>{gameEvent.HomeScore?.ToString() ?? "—"} — {gameEvent.AwayScore?.ToString() ?? "—"}</td><td><b>{E(gameEvent.Type)}</b> · {E(gameEvent.Description)}</td></tr>");
            html.AppendLine("</tbody></table></div>");
        }

        html.AppendLine("<p class=\"muted\">ScorePlus game report · use your browser's Print command to print this page or save it as a PDF.</p>");
        html.AppendLine("</body></html>");
        return html.ToString();
    }

    // ---------- sections ----------

    /// <summary>Every sport's own bookkeeping, so no field a console holds is missing from the record.</summary>
    static void AppendSportDetail(StringBuilder html, GameState state)
    {
        switch (state.SportId)
        {
            case "football":
                html.AppendLine(Card("Down &amp; distance",
                    $"{state.Down}{Ordinal(state.Down)} &amp; {state.YardsToGo}",
                    $"To go: {state.YardsToGo} yards",
                    $"Down: {state.Down} of 4",
                    $"New series at 1st &amp; 10"));
                html.AppendLine(Card("Ball spot",
                    $"Ball on {(state.BallOnHomeSide ? state.HomeTeam : state.AwayTeam)}'s {state.YardsToGoal}",
                    $"Yard line: {state.YardsToGoal} (0–50)",
                    $"Possession: {E(state.HomePossession ? state.HomeTeam : state.AwayTeam)}"));
                html.AppendLine(Card("Clocks",
                    $"Play clock: {state.PlayClockSeconds}s {(state.PlayClockRunning ? "(running)" : "(stopped)")}",
                    $"Timeout clock: {GameEngine.FormatClock(state.TimeoutClockSeconds)} {(state.TimeoutClockRunning ? "(running)" : "(stopped)")}",
                    $"Game clock: {GameEngine.FormatClock(state.ClockSeconds)}"));
                html.AppendLine(Card("Team totals",
                    $"Total yards: {E(state.HomeTeam)} {state.HomeYards} — {state.AwayYards} {E(state.AwayTeam)}",
                    $"Turnovers: {state.HomeTurnovers} — {state.AwayTurnovers}",
                    $"Timeouts: {state.HomeTimeouts} — {state.AwayTimeouts}"));
                break;

            case "basketball":
                html.AppendLine(Card("Basketball",
                    $"Fouls: {state.HomeFouls} — {state.AwayFouls}",
                    $"Shot clock: {state.ShotClockSeconds}s {(state.ShotClockRunning ? "(running)" : "(stopped)")}",
                    $"Possession: {E(state.HomePossession ? state.HomeTeam : state.AwayTeam)}",
                    $"Timeouts: {state.HomeTimeouts} — {state.AwayTimeouts}"));
                break;

            case "baseball":
                html.AppendLine(Card("Count",
                    $"Count: {state.Balls} balls, {state.Strikes} strikes, {state.Outs} outs",
                    $"Inning: {E(PeriodDisplay.Format(state))} of {state.TotalPeriods}"));
                html.AppendLine(Card("Team totals",
                    $"Hits: {E(state.HomeTeam)} {state.HomeHits} — {state.AwayHits} {E(state.AwayTeam)}",
                    $"Errors: {SportValues.Read(state, "baseball.errors.home")} — {SportValues.Read(state, "baseball.errors.away")}",
                    $"Runners: {(SportValues.Read(state, "baseball.first") != 0 ? "1st " : "")}{(SportValues.Read(state, "baseball.second") != 0 ? "2nd " : "")}{(SportValues.Read(state, "baseball.third") != 0 ? "3rd" : "")}".TrimEnd()));
                break;

            case "soccer":
                html.AppendLine(Card("Soccer",
                    $"Cards (yellow/red): {state.HomeYellowCards}/{state.HomeRedCards} — {state.AwayYellowCards}/{state.AwayRedCards}",
                    $"Shots: {state.HomeShots} — {state.AwayShots}",
                    $"Corners: {state.HomeCorners} — {state.AwayCorners}",
                    $"Saves: {SportValues.Read(state, "soccer.saves.home")} — {SportValues.Read(state, "soccer.saves.away")}",
                    $"Offsides: {SportValues.Read(state, "soccer.offsides.home")} — {SportValues.Read(state, "soccer.offsides.away")}"));
                break;

            case "hockey":
                html.AppendLine(Card("Hockey",
                    $"Power play: {(SportValues.Read(state, "hockey.powerplay") != 0 ? "ON" : "OFF")}",
                    $"Empty net: {(SportValues.Read(state, "hockey.empty.net") != 0 ? "ON" : "OFF")}",
                    $"Shots: {SportValues.Read(state, "hockey.shots.home")} — {SportValues.Read(state, "hockey.shots.away")}",
                    $"Penalty minutes: {SportValues.Read(state, "hockey.penalty.home")} — {SportValues.Read(state, "hockey.penalty.away")}"));
                break;

            case "lacrosse":
                html.AppendLine(Card("Lacrosse",
                    $"Shots: {SportValues.Read(state, "lacrosse.shots.home")} — {SportValues.Read(state, "lacrosse.shots.away")}",
                    $"Saves: {SportValues.Read(state, "lacrosse.saves.home")} — {SportValues.Read(state, "lacrosse.saves.away")}",
                    $"Faceoff wins: {SportValues.Read(state, "lacrosse.faceoffs.home")} — {SportValues.Read(state, "lacrosse.faceoffs.away")}",
                    $"Power play: {(SportValues.Read(state, "lacrosse.powerplay") != 0 ? "ON" : "OFF")}"));
                break;

            case "rugby":
                html.AppendLine(Card("Rugby",
                    $"Tries: {SportValues.Read(state, "rugby.tries.home")} — {SportValues.Read(state, "rugby.tries.away")}",
                    $"Conversions: {SportValues.Read(state, "rugby.conversions.home")} — {SportValues.Read(state, "rugby.conversions.away")}",
                    $"Penalty goals: {SportValues.Read(state, "rugby.penalties.home")} — {SportValues.Read(state, "rugby.penalties.away")}",
                    $"Cards (yellow/red): {SportValues.Read(state, "rugby.yellow.home")}/{SportValues.Read(state, "rugby.red.home")} — {SportValues.Read(state, "rugby.yellow.away")}/{SportValues.Read(state, "rugby.red.away")}"));
                break;

            case "cricket":
                html.AppendLine(Card("Cricket",
                    $"Overs bowled: {SportValues.Read(state, "cricket.overs")}",
                    $"Wickets: {SportValues.Read(state, "cricket.wickets")}",
                    $"Extras: {SportValues.Read(state, "cricket.extras")}",
                    $"Target: {SportValues.Read(state, "cricket.target")}",
                    $"Day / night: {(SportValues.Read(state, "cricket.daynight") != 0 ? "ON" : "OFF")}"));
                break;

            case "tennis":
                html.AppendLine(Card("Tennis",
                    $"Sets: {SportValues.Read(state, "tennis.sets.home")} — {SportValues.Read(state, "tennis.sets.away")}",
                    $"Games: {SportValues.Read(state, "tennis.games.home")} — {SportValues.Read(state, "tennis.games.away")}",
                    $"Serve: {(SportValues.Read(state, "tennis.serve") != 0 ? "home" : "visitors")}",
                    $"Tiebreak: {(SportValues.Read(state, "tennis.tiebreak") != 0 ? "ON" : "OFF")}"));
                break;

            case "volleyball":
                html.AppendLine(Card("Volleyball",
                    $"Sets: {SportValues.Read(state, "volleyball.sets.home")} — {SportValues.Read(state, "volleyball.sets.away")}",
                    $"Serve: {(SportValues.Read(state, "volleyball.serve") != 0 ? "home" : "visitors")}",
                    $"Set point: {(SportValues.Read(state, "volleyball.setpoint") != 0 ? "ON" : "OFF")}"));
                break;

            case "wrestling":
                html.AppendLine(Card("Wrestling",
                    $"Near fall: {SportValues.Read(state, "wrestling.nearfall.home")} — {SportValues.Read(state, "wrestling.nearfall.away")}",
                    $"Riding time: {SportValues.Read(state, "wrestling.ridingtime.home")} — {SportValues.Read(state, "wrestling.ridingtime.away")}",
                    $"Blood time: {SportValues.Read(state, "wrestling.bloodtime.home")} — {SportValues.Read(state, "wrestling.bloodtime.away")}",
                    $"Overtime: {(SportValues.Read(state, "wrestling.overtime") != 0 ? "ON" : "OFF")}"));
                break;

            case "australian-football":
                html.AppendLine(Card("Australian Football",
                    $"Behinds: {SportValues.Read(state, "afl.behinds.home")} — {SportValues.Read(state, "afl.behinds.away")}",
                    $"Tackles: {SportValues.Read(state, "afl.tackles.home")} — {SportValues.Read(state, "afl.tackles.away")}",
                    $"Inside 50s: {SportValues.Read(state, "afl.inside50.home")} — {SportValues.Read(state, "afl.inside50.away")}",
                    $"Free kicks: {SportValues.Read(state, "afl.freekicks.home")} — {SportValues.Read(state, "afl.freekicks.away")}"));
                break;

            default:
                html.AppendLine(Card("Custom sport",
                    $"Periods: {state.TotalPeriods} × {GameEngine.FormatClock(state.PeriodSeconds)}",
                    $"Scoring values: {string.Join(", ", state.ScoringOptions)}",
                    $"Timeouts: {state.HomeTimeouts} — {state.AwayTimeouts}"));
                break;
        }
    }

    /// <summary>One roster, with a column for every stat anyone on it carries — zeros included.</summary>
    static void AppendRoster(StringBuilder html, Roster roster, GameState state)
    {
        var columns = roster.StatNames();
        var side = string.Equals(roster.Id, state.HomeRosterId, StringComparison.OrdinalIgnoreCase) ? "Home roster"
            : string.Equals(roster.Id, state.AwayRosterId, StringComparison.OrdinalIgnoreCase) ? "Visitors roster"
            : roster.Id == state.HomePlayer?.RosterId ? "Home roster"
            : roster.Id == state.AwayPlayer?.RosterId ? "Visitors roster"
            : "Roster";
        html.AppendLine("<div class=\"roster\">");
        html.AppendLine($"<div class=\"roster-head\"><strong>{E(roster.Name)}</strong><span class=\"side\">{E(side)} · {roster.Players.Count} player{(roster.Players.Count == 1 ? "" : "s")}</span></div>");
        if (roster.Players.Count == 0)
        {
            html.AppendLine("<p class=\"empty\">This roster has no players yet.</p></div>");
            return;
        }
        html.AppendLine("<div class=\"scroller\"><table><thead><tr><th>#</th><th>Player</th><th>Position</th>");
        foreach (var column in columns) html.AppendLine($"<th>{E(column)}</th>");
        html.AppendLine("</tr></thead><tbody>");
        foreach (var player in roster.Players)
        {
            html.AppendLine($"<tr><td class=\"num\">{player.Number}</td><td>{E(player.Name)}</td><td>{E(player.Position)}</td>");
            foreach (var column in columns)
            {
                // A stat the player never entered is a real zero on the report, not a blank cell.
                var stat = player.Stats.FirstOrDefault(candidate => string.Equals(candidate.Name, column, StringComparison.OrdinalIgnoreCase));
                html.AppendLine($"<td class=\"num\">{E(stat?.Display ?? "0")}</td>");
            }
            html.AppendLine("</tr>");
        }
        html.AppendLine("</tbody></table></div></div>");
    }

    /// <summary>Who is on the graphic for one side, with every stat the snapshot carries.</summary>
    static string ActivePlayerCard(string side, string team, bool cleared, SelectedPlayer? player)
    {
        if (player is null)
            return Card(side, $"{E(team)}", cleared ? "No player on the graphic (cleared by the operator)" : "No player selected");
        var body = new StringBuilder();
        body.AppendLine($"{E(team)}");
        body.AppendLine($"#{player.Number} {E(player.Name)}".Trim());
        if (!string.IsNullOrWhiteSpace(player.Position)) body.AppendLine($"Position: {E(player.Position)}");
        foreach (var stat in player.Stats) body.AppendLine($"{E(stat.Name)}: {E(stat.Display)}");
        return $"<div class=\"card\"><b>{E(side)}</b>{body.ToString().TrimEnd().Replace("\r\n", "<br/>").Replace("\n", "<br/>")}</div>";
    }

    static string Card(string title, params string[] lines)
    {
        var body = new StringBuilder();
        foreach (var line in lines)
            if (!string.IsNullOrWhiteSpace(line)) body.Append(line).Append("<br/>");
        return $"<div class=\"card\"><b>{title}</b>{body.ToString().TrimEnd()}</div>";
    }

    static string SportName(GameState state)
    {
        var builtIn = SportDefinition.BuiltIns.FirstOrDefault(sport => string.Equals(sport.Id, state.SportId, StringComparison.OrdinalIgnoreCase));
        if (builtIn is not null) return builtIn.Name;
        var text = (state.SportId ?? string.Empty).Replace('-', ' ').Replace('_', ' ').Trim();
        return text.Length == 0 ? "Custom" : char.ToUpperInvariant(text[0]) + text[1..];
    }

    static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    static string Ordinal(int value) => value switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
}
