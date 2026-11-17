namespace Scoreboard.Core;

/// <summary>One item in a rotating ticker. The key stays stable while its text changes, so a new
/// score or clock can refresh the current card without restarting the rotation.</summary>
public sealed record TickerItem(string Key, string Text);

/// <summary>
/// Builds the cards a league ticker rotates through. The operator can include only chosen league
/// games, add their own headlines, and keep score alerts separate from the normal rotation.
/// </summary>
public static class TickerRotation
{
    public static bool IsBound(string? content) =>
        content is not null
        && (content.Contains("{{LEAGUE_GAMES}}", StringComparison.OrdinalIgnoreCase)
            || content.Contains("{{LEAGUE_TICKER}}", StringComparison.OrdinalIgnoreCase));

    /// <summary>The games the operator included, in feed order. Until the operator has made a
    /// choice, every received game is included so a new ticker has something to show.</summary>
    public static IReadOnlyList<LeagueGame> SelectedGames(GameState state)
    {
        var games = (state.LeagueGames ?? [])
            .Where(game => game is not null)
            .Take(24)
            .ToList();
        if (!state.TickerGamesChosen) return games;
        var ids = (state.TickerGameIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A feed without ids cannot be filtered safely, so keep those games rather than dropping
        // them from a rotation the operator can see in the setup window.
        return games.Where(game => string.IsNullOrWhiteSpace(game.GameId) || ids.Contains(game.GameId)).ToList();
    }

    /// <summary>The ordered cards for a rotation: the selected games, then news headlines.</summary>
    public static IReadOnlyList<TickerItem> Build(GameState state)
    {
        var items = new List<TickerItem>();
        foreach (var game in SelectedGames(state))
        {
            var key = string.IsNullOrWhiteSpace(game.GameId)
                ? $"game:{items.Count}"
                : $"game:{game.GameId}";
            items.Add(new TickerItem(key, game.TickerLine));
        }
        var news = (state.TickerNews ?? [])
            .Where(headline => !string.IsNullOrWhiteSpace(headline))
            .Select(headline => headline.Trim())
            .Where(headline => headline.Length > 0)
            .Take(30)
            .ToList();
        for (var index = 0; index < news.Count; index++)
            items.Add(new TickerItem($"news:{index}", $"NEWS · {news[index]}"));
        if (items.Count == 0) items.Add(new TickerItem("placeholder", "LEAGUE SCORES · waiting for cloud feed"));
        return items;
    }

    /// <summary>A local score change, worded for a ticker card. Empty when the last action was not
    /// a scoring action or score alerts are switched off.</summary>
    public static string LocalScoreAlert(GameState state)
    {
        if (!state.TickerScoreAlerts || state.LastScoreAmount == 0) return "";
        var team = string.IsNullOrWhiteSpace(state.LastScoreTeam)
            ? (state.LastScoreHome ? state.HomeTeam : state.AwayTeam)
            : state.LastScoreTeam;
        var change = $"{(state.LastScoreAmount > 0 ? "+" : "−")}{state.LastScoreMagnitude}";
        return $"SCORE ALERT · {team} {change} · {state.AwayTeam} {state.AwayScore} – {state.HomeTeam} {state.HomeScore}";
    }

    /// <summary>A score change in another league game, worded for the same alert card.</summary>
    public static string LeagueScoreAlert(LeagueGame game) =>
        $"SCORE ALERT · {game.AwayTeam} {game.AwayScore} – {game.HomeTeam} {game.HomeScore}"
        + (string.IsNullOrWhiteSpace(game.StateLine) ? "" : $" · {game.StateLine}");
}
