using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scoreboard.Core;

/// <summary>
/// One game in a league cloud feed. The feed is intentionally small and transport-neutral: the
/// same shape can be served by a league's existing scoreboard service, a small Worker, or a
/// spreadsheet export. Only the values needed by a standings/ticker display travel with it.
/// </summary>
public sealed class LeagueGame
{
    public string GameId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Sport { get; set; } = "";
    public string HomeTeam { get; set; } = "";
    public string AwayTeam { get; set; } = "";
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public int Period { get; set; }
    public string PeriodLabel { get; set; } = "";
    public string Clock { get; set; } = "";
    public string Status { get; set; } = "live";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string Label => !string.IsNullOrWhiteSpace(Name)
        ? Name.Trim()
        : $"{AwayTeam} at {HomeTeam}".Trim();

    [JsonIgnore]
    public string ScoreLine => $"{AwayTeam} {AwayScore}  ·  {HomeTeam} {HomeScore}";

    [JsonIgnore]
    public string StateLine => string.Join(" · ", new[]
    {
        string.IsNullOrWhiteSpace(PeriodLabel) ? "" : PeriodLabel,
        Clock,
        Status switch
        {
            "final" => "FINAL",
            "end_of_reg" => "END OF REG.",
            "scheduled" => "SCHEDULED",
            _ => "LIVE"
        }
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    [JsonIgnore]
    public string TickerLine => $"{AwayTeam} {AwayScore} @ {HomeTeam} {HomeScore} · {StateLine}";

    public LeagueGame Copy() => new()
    {
        GameId = GameId,
        Name = Name,
        Sport = Sport,
        HomeTeam = HomeTeam,
        AwayTeam = AwayTeam,
        HomeScore = HomeScore,
        AwayScore = AwayScore,
        Period = Period,
        PeriodLabel = PeriodLabel,
        Clock = Clock,
        Status = Status,
        UpdatedAt = UpdatedAt
    };
}

/// <summary>
/// Builds the one-line league ticker used by {{LEAGUE_GAMES}}. Games are separated with a middle
/// dot so a lower third can carry several results in a single text element. A league with no feed
/// yet prints a useful placeholder instead of an empty strip.
/// </summary>
public static class LeagueTicker
{
    public static string Format(IEnumerable<LeagueGame>? games)
    {
        var list = (games ?? []).Where(game => game is not null).Take(12).ToList();
        if (list.Count == 0) return "LEAGUE SCORES · waiting for cloud feed";
        return string.Join("   ·   ", list.Select(game => game.TickerLine));
    }
}

/// <summary>The snapshot uploaded for the operator's current game.</summary>
public sealed class LeagueCloudUpload
{
    public string League { get; set; } = "";
    public string GameId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Sport { get; set; } = "";
    public string HomeTeam { get; set; } = "";
    public string AwayTeam { get; set; } = "";
    public string HomeAbbreviation { get; set; } = "";
    public string AwayAbbreviation { get; set; } = "";
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public int Period { get; set; }
    public string PeriodLabel { get; set; } = "";
    public string Clock { get; set; } = "";
    public string Status { get; set; } = "live";
    public string HomeRecord { get; set; } = "";
    public string AwayRecord { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static LeagueCloudUpload From(GameState state, string league)
    {
        var status = state.GameEnded ? "final"
            : state.ClockMessage == GameClockMessage.EndOfRegulation ? "end_of_reg"
            : "live";
        return new LeagueCloudUpload
        {
            League = (league ?? "").Trim(),
            GameId = state.GameId,
            Name = state.GameName,
            Sport = state.SportId,
            HomeTeam = state.HomeTeam,
            AwayTeam = state.AwayTeam,
            HomeAbbreviation = state.HomeAbbreviation,
            AwayAbbreviation = state.AwayAbbreviation,
            HomeScore = state.HomeScore,
            AwayScore = state.AwayScore,
            Period = state.Period,
            PeriodLabel = PeriodDisplay.Format(state),
            Clock = state.GameClockText,
            Status = status,
            HomeRecord = state.HomeRecordDisplay,
            AwayRecord = state.AwayRecordDisplay,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}

/// <summary>
/// Reads and writes the optional league feed. This class does not assume a particular provider:
/// upload is a POST to the configured URL and fetch is a GET returning an array (or an object with
/// a games/data array). The same JSON property names are accepted in camelCase or PascalCase, so a
/// small league service can be written in any language.
/// </summary>
public sealed class LeagueCloudClient : IDisposable
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    readonly HttpClient _http;
    readonly bool _ownsClient;

    public LeagueCloudClient(HttpClient? http = null)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }

    public async Task<string?> UploadAsync(string url, string? apiKey, LeagueCloudUpload upload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return "No upload URL is set.";
        if (!TryHttp(url, out var uri, out var error)) return error;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(JsonSerializer.Serialize(upload, JsonOptions), Encoding.UTF8, "application/json")
            };
            AddKey(request, apiKey);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return $"Cloud upload returned {(int)response.StatusCode}: {Short(body)}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return $"Cloud upload failed: {ex.Message}";
        }
    }

    public async Task<(IReadOnlyList<LeagueGame> Games, string? Error)> FetchAsync(
        string url, string? apiKey, string league, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return ([], "No feed URL is set.");
        if (!TryFeedUrl(url, league, out var uri, out var error)) return ([], error);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            AddKey(request, apiKey);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return ([], $"Cloud feed returned {(int)response.StatusCode}: {Short(body)}");
            }
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (ParseGames(json), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ([], $"Cloud feed failed: {ex.Message}");
        }
    }

    static void AddKey(HttpRequestMessage request, string? apiKey)
    {
        var key = (apiKey ?? "").Trim();
        if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    static bool TryHttp(string value, out Uri uri, out string? error)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("http" or "https"))
        {
            uri = null!;
            error = "Use a full http:// or https:// cloud URL.";
            return false;
        }
        uri = parsed;
        error = null;
        return true;
    }

    static bool TryFeedUrl(string value, string league, out Uri uri, out string? error)
    {
        if (!TryHttp(value, out uri, out error)) return false;
        var text = uri.ToString();
        if (text.Contains("{league}", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Replace("{league}", Uri.EscapeDataString(league ?? ""), StringComparison.OrdinalIgnoreCase);
        }
        else if (!text.Contains("league=", StringComparison.OrdinalIgnoreCase))
        {
            text += (text.Contains('?') ? "&" : "?") + "league=" + Uri.EscapeDataString(league ?? "");
        }
        return Uri.TryCreate(text, UriKind.Absolute, out uri!) && uri.Scheme is "http" or "https";
    }

    /// <summary>Parses the feed shape for callers that cache or test a payload without a request.</summary>
    public static IReadOnlyList<LeagueGame> Parse(string json) => ParseGames(json);

    static IReadOnlyList<LeagueGame> ParseGames(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        JsonElement array;
        if (root.ValueKind == JsonValueKind.Array) array = root;
        else if (root.ValueKind == JsonValueKind.Object
                 && (TryProperty(root, "games", out array) || TryProperty(root, "data", out array)))
        {
            if (array.ValueKind != JsonValueKind.Array) return [];
        }
        else return [];

        var games = new List<LeagueGame>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var game = ParseGame(item);
            if (!string.IsNullOrWhiteSpace(game.GameId) || !string.IsNullOrWhiteSpace(game.HomeTeam))
                games.Add(game);
        }
        return games;
    }

    static LeagueGame ParseGame(JsonElement item) => new()
    {
        GameId = Text(item, "gameId", "game_id", "id"),
        Name = Text(item, "name", "gameName", "game_name"),
        Sport = Text(item, "sport", "sportId", "sport_id"),
        HomeTeam = Text(item, "homeTeam", "home_team", "home"),
        AwayTeam = Text(item, "awayTeam", "away_team", "away"),
        HomeScore = Number(item, "homeScore", "home_score", "home_score_total"),
        AwayScore = Number(item, "awayScore", "away_score", "away_score_total"),
        Period = Number(item, "period", "quarter", "inning"),
        PeriodLabel = Text(item, "periodLabel", "period_label", "periodName", "period_name"),
        Clock = Text(item, "clock", "gameClock", "game_clock"),
        Status = TextOr(item, "live", "status", "state"),
        UpdatedAt = Time(item)
    };

    static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }

    static string Text(JsonElement item, params string[] names)
    {
        foreach (var name in names)
            if (TryProperty(item, name, out var value))
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString()?.Trim() ?? "",
                    JsonValueKind.Number => value.GetRawText(),
                    _ => ""
                };
        return "";
    }

    static string TextOr(JsonElement item, string fallback, params string[] names)
    {
        var text = Text(item, names);
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    static int Number(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryProperty(item, name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
        }
        return 0;
    }

    static DateTimeOffset Time(JsonElement item)
    {
        var text = Text(item, "updatedAt", "updated_at", "timestamp");
        return DateTimeOffset.TryParse(text, out var parsed) ? parsed : DateTimeOffset.UtcNow;
    }

    static string Short(string value)
    {
        var text = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 240 ? text : text[..240] + "…";
    }
}
