using System.Windows.Threading;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// Keeps one optional league feed in step with the desk. Uploads a small snapshot of the current
/// game after local changes and polls the feed for the rest of the league. It is deliberately
/// isolated from the show's normal save path: a cloud outage can only change the status line, never
/// stop a clock or a graphic.
/// </summary>
public sealed class LeagueCloudService : IDisposable
{
    readonly GameEngine _engine;
    readonly UserPreferences _preferences;
    readonly LeagueCloudClient _client = new();
    readonly DispatcherTimer _poll = new();
    readonly DispatcherTimer _uploadDelay = new() { Interval = TimeSpan.FromSeconds(1.5) };
    bool _running;
    bool _disposed;

    public LeagueCloudService(GameEngine engine, UserPreferences preferences)
    {
        _engine = engine;
        _preferences = preferences;
        _poll.Tick += async (_, _) => await SyncAsync(upload: false, fetch: true);
        _uploadDelay.Tick += async (_, _) =>
        {
            _uploadDelay.Stop();
            await SyncAsync(upload: true, fetch: false);
        };
    }

    public event Action<string>? Status;
    public event Action<IReadOnlyList<LeagueGame>>? GamesChanged;

    public bool Enabled => _preferences.LeagueCloudEnabled;
    public IReadOnlyList<LeagueGame> Games => _engine.State.LeagueGames;

    /// <summary>Starts polling when the operator has configured and enabled the feed.</summary>
    public void Start()
    {
        if (_disposed || !Enabled) return;
        _running = true;
        ApplySettings();
        if (!_poll.IsEnabled) _poll.Start();
        _ = SyncAsync(upload: true, fetch: true);
    }

    public void Stop()
    {
        _running = false;
        _poll.Stop();
        _uploadDelay.Stop();
    }

    /// <summary>Re-reads the interval and endpoint choices after the cloud window saves them.</summary>
    public void ApplySettings()
    {
        _poll.Interval = TimeSpan.FromSeconds(Math.Clamp(_preferences.LeagueCloudPollSeconds, 5, 300));
        if (!Enabled)
        {
            Stop();
            Status?.Invoke("LEAGUE CLOUD · off");
            return;
        }
        if (!_running)
        {
            _running = true;
            _poll.Start();
        }
    }

    /// <summary>Queues an upload shortly after a game change, coalescing a run of score presses.</summary>
    public void MarkDirty()
    {
        if (!_running || !Enabled) return;
        _uploadDelay.Stop();
        _uploadDelay.Start();
    }

    public async Task UploadNowAsync() => await SyncAsync(upload: true, fetch: false);

    public async Task FetchNowAsync() => await SyncAsync(upload: false, fetch: true);

    async Task SyncAsync(bool upload, bool fetch)
    {
        if (!_running || !Enabled || _disposed) return;
        if (upload && string.IsNullOrWhiteSpace(_preferences.LeagueCloudUploadUrl)
            && string.IsNullOrWhiteSpace(_preferences.LeagueCloudFeedUrl)) return;

        var parts = new List<string>();
        if (upload && !string.IsNullOrWhiteSpace(_preferences.LeagueCloudUploadUrl))
        {
            var error = await _client.UploadAsync(
                _preferences.LeagueCloudUploadUrl,
                _preferences.LeagueCloudApiKey,
                LeagueCloudUpload.From(_engine.State, _preferences.LeagueCloudLeagueId));
            parts.Add(error is null ? "uploaded" : error);
        }

        if (fetch && !string.IsNullOrWhiteSpace(_preferences.LeagueCloudFeedUrl))
        {
            var (games, error) = await _client.FetchAsync(
                _preferences.LeagueCloudFeedUrl,
                _preferences.LeagueCloudApiKey,
                _preferences.LeagueCloudLeagueId);
            if (error is null)
            {
                _engine.SetLeagueGames(games);
                GamesChanged?.Invoke(games);
                parts.Add(games.Count == 0 ? "feed empty" : $"{games.Count} league game{(games.Count == 1 ? "" : "s")}");
            }
            else parts.Add(error);
        }

        if (parts.Count > 0) Status?.Invoke("LEAGUE CLOUD · " + string.Join(" · ", parts));
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
        _client.Dispose();
    }
}
