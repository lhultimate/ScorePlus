using System.Text.RegularExpressions;

namespace Scoreboard.Core;

public static class ScoreboardTemplates
{
    static readonly Regex StatToken = new(@"\{\{STAT:(?<name>[^}]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A value read from the player currently selected for one side, e.g.
    /// <c>{{PLAYER:HOME:Name}}</c>, <c>{{PLAYER:AWAY:Number}}</c> or <c>{{PLAYER:HOME:Yards}}</c>.
    /// Switching the selected player in the Rosters tab reprints every one of these.
    /// </summary>
    static readonly Regex PlayerToken = new(@"\{\{PLAYER:(?<side>HOME|AWAY):(?<field>[^}]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A sport's own value, e.g. <c>{{SPORT:baseball.balls}}</c>, <c>{{SPORT:hockey.powerplay}}</c> or
    /// <c>{{SPORT:cricket.overs}}</c>. A flag prints its words (PP, BOTTOM, BONUS) instead of 1 or 0.
    /// </summary>
    static readonly Regex SportToken = new(@"\{\{SPORT:(?<id>[^}]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Bind(string content, GameState state, string? periodFormat = null) =>
        PlayerToken.Replace(StatToken.Replace(SportToken.Replace(ReplaceFixed(content, state, periodFormat), match =>
        {
            var control = SportValues.FindControl(state, match.Groups["id"].Value.Trim());
            return control is null ? string.Empty : SportValues.Display(state, control);
        }), match =>
        {
            var name = match.Groups["name"].Value.Trim();
            var stat = state.Stats.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            return stat is null ? string.Empty : stat.Display;
        }), match =>
        {
            var home = match.Groups["side"].Value.Equals("HOME", StringComparison.OrdinalIgnoreCase);
            var field = match.Groups["field"].Value;
            // {{PLAYER:HOME:id:<playerId>:Yards}} pins one player from that side's roster, so an
            // element can show a specific player while somebody else is the selected one.
            if (field.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = field[3..];
                var split = rest.IndexOf(':');
                if (split <= 0) return string.Empty;
                var roster = RosterStore.Book.Find(home ? state.HomeRosterId : state.AwayRosterId);
                var pinned = roster?.Find(rest[..split].Trim());
                return pinned is null ? string.Empty : PlayerFields.Read(pinned, rest[(split + 1)..]);
            }
            var player = home ? state.HomePlayer : state.AwayPlayer;
            return player?.Field(field) ?? string.Empty;
        });

    static string ReplaceFixed(string content, GameState state, string? periodFormat) => content
        // Team names render exactly as the operator typed them: full text, original case.
        .Replace("{{HOME_TEAM}}", state.HomeTeam)
        .Replace("{{AWAY_TEAM}}", state.AwayTeam)
        .Replace("{{HOME_ABBR}}", state.HomeAbbreviation)
        .Replace("{{AWAY_ABBR}}", state.AwayAbbreviation)
        .Replace("{{HOME_MASCOT}}", state.HomeMascot)
        .Replace("{{AWAY_MASCOT}}", state.AwayMascot)
        .Replace("{{HOME_SCORE}}", state.HomeScore.ToString())
        .Replace("{{AWAY_SCORE}}", state.AwayScore.ToString())
        .Replace("{{PERIOD}}", PeriodDisplay.Format(state, periodFormat).ToUpperInvariant())
        .Replace("{{GAME_CLOCK}}", GameEngine.FormatClock(state.ClockSeconds))
        .Replace("{{STOPPAGE}}", GameEngine.FormatClock(state.StoppageSeconds))
        .Replace("{{PLAY_CLOCK}}", state.PlayClockSeconds.ToString())
        .Replace("{{TIMEOUT_CLOCK}}", GameEngine.FormatClock(state.TimeoutClockSeconds))
        .Replace("{{TIMEOUTS}}", $"TOL {state.HomeTimeouts}–{state.AwayTimeouts}")
        .Replace("{{POSSESSION}}", state.HomePossession ? state.HomeAbbreviation : state.AwayAbbreviation)
        // A flag's own words: empty unless a flag is down, so an icon bound to {{PENALTY_TYPE}}
        // arrives with the flag and leaves when it is cleared.
        .Replace("{{PENALTY_TYPE}}", state.PenaltyActive ? state.PenaltyType : string.Empty)
        .Replace("{{PENALTY_TEAM}}", state.PenaltyActive ? (state.PenaltyHome ? state.HomeTeam : state.AwayTeam) : string.Empty)
        .Replace("{{PENALTY}}", state.PenaltyActive ? $"{(state.PenaltyHome ? state.HomeTeam : state.AwayTeam)} — {state.PenaltyType}" : string.Empty)
        .Replace("{{DOWN}}", $"{state.Down}{Ordinal(state.Down)}")
        .Replace("{{TO_GO}}", state.YardsToGo.ToString())
        // The ball spot is a plain yard line (0-50) with its own side, independent of possession.
        .Replace("{{BALL_ON}}", state.YardsToGoal.ToString())
        .Replace("{{BALL_SIDE}}", state.BallOnHomeSide ? state.HomeAbbreviation : state.AwayAbbreviation)
        .Replace("{{BALL_TEAM}}", state.BallOnHomeSide ? state.HomeTeam : state.AwayTeam)
        .Replace("{{BALL_SIDE_SPOT}}", $"{(state.BallOnHomeSide ? state.HomeAbbreviation : state.AwayAbbreviation)} {state.YardsToGoal}")
        // Next down shows the new down alone until the distance catches up, then 2nd & 4.
        .Replace("{{DOWNS}}", state.DownPending ? $"{state.Down}{Ordinal(state.Down)} Down" : $"{state.Down}{Ordinal(state.Down)} & {state.YardsToGo}")
        .Replace("{{BALL_SPOT}}", $"BALL {state.YardsToGoal}")
        .Replace("{{SPORT}}", state.SportId.ToUpperInvariant())
        .Replace("{{YARDS_TO_GO}}", state.YardsToGo.ToString())
        .Replace("{{HOME_YARDS}}", state.HomeYards.ToString())
        .Replace("{{AWAY_YARDS}}", state.AwayYards.ToString())
        .Replace("{{SOCCER_CARDS}}", $"Y {state.HomeYellowCards}/{state.AwayYellowCards} · R {state.HomeRedCards}/{state.AwayRedCards}")
        .Replace("{{BASEBALL_OUTS}}", state.Outs.ToString())
        .Replace("{{BASKETBALL_FOULS}}", $"{state.HomeFouls}–{state.AwayFouls}")
        .Replace("{{YARDS}}", $"{state.HomeYards}–{state.AwayYards}")
        .Replace("{{TURNOVERS}}", $"{state.HomeTurnovers}–{state.AwayTurnovers}")
        .Replace("{{FOULS}}", $"{state.HomeFouls}–{state.AwayFouls}")
        .Replace("{{SHOT_CLOCK}}", state.ShotClockSeconds.ToString())
        .Replace("{{BASEBALL_COUNT}}", $"{state.Balls}-{state.Strikes} · {state.Outs} OUT")
        .Replace("{{HITS}}", $"{state.HomeHits}–{state.AwayHits}")
        .Replace("{{CARDS}}", $"Y{state.HomeYellowCards}/{state.AwayYellowCards} R{state.HomeRedCards}/{state.AwayRedCards}")
        // Per-side card counts. Empty at zero, so an icon bound to one appears only once that
        // team has actually been shown the card.
        .Replace("{{HOME_YELLOW}}", state.HomeYellowCards == 0 ? string.Empty : state.HomeYellowCards.ToString())
        .Replace("{{AWAY_YELLOW}}", state.AwayYellowCards == 0 ? string.Empty : state.AwayYellowCards.ToString())
        .Replace("{{HOME_RED}}", state.HomeRedCards == 0 ? string.Empty : state.HomeRedCards.ToString())
        .Replace("{{AWAY_RED}}", state.AwayRedCards == 0 ? string.Empty : state.AwayRedCards.ToString())
        .Replace("{{SHOTS}}", $"{state.HomeShots}–{state.AwayShots}")
        .Replace("{{CORNERS}}", $"{state.HomeCorners}–{state.AwayCorners}")
        // The classics by name, so a hand-typed binding works as well as the studio's picker.
        .Replace("{{BALLS}}", state.Balls.ToString())
        .Replace("{{STRIKES}}", state.Strikes.ToString())
        .Replace("{{OUTS}}", state.Outs.ToString())
        .Replace("{{BASES}}", string.Concat(
            SportValues.Read(state, "baseball.first") != 0 ? "1B " : string.Empty,
            SportValues.Read(state, "baseball.second") != 0 ? "2B " : string.Empty,
            SportValues.Read(state, "baseball.third") != 0 ? "3B" : string.Empty).Trim())
        .Replace("{{INNING_HALF}}", SportValues.Read(state, "baseball.half") != 0 ? "BOTTOM" : "TOP")
        .Replace("{{POWERPLAY}}", SportValues.Read(state, "hockey.powerplay") != 0 ? "PP" : string.Empty)
        .Replace("{{DATE}}", DateTime.Now.ToString("yyyy-MM-dd"))
        .Replace("{{TIME}}", DateTime.Now.ToString("HH:mm:ss"));

    static string Ordinal(int value) => value switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
}
