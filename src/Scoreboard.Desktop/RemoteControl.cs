using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// A phone-sized remote console, served straight from the app over the local network. It uses a
/// plain TCP listener rather than <c>HttpListener</c> because a wildcard HttpListener prefix needs an
/// administrator URL reservation; a socket needs nothing, so the feature works out of the box.
/// Every action runs on the UI thread through the same control-panel code the on-screen buttons use.
/// </summary>
public sealed class RemoteControlServer : IDisposable
{
    const int MaxRequestBytes = 32 * 1024;
    static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    readonly GameEngine _engine;
    readonly IControlPanelHost _host;
    readonly Func<string, bool> _execute;
    readonly Dispatcher _dispatcher;
    readonly int _port;
    readonly byte[] _passwordHash;
    readonly Dictionary<string, DateTimeOffset> _sessions = new(StringComparer.Ordinal);
    readonly Dictionary<string, (int Failures, DateTimeOffset First)> _attempts = new(StringComparer.Ordinal);
    readonly object _gate = new();
    TcpListener? _listener;
    CancellationTokenSource? _cancel;

    /// <summary>Raised with a human message when the server starts, stops or cannot bind.</summary>
    public event Action<string>? Status;

    public RemoteControlServer(GameEngine engine, IControlPanelHost host, Func<string, bool> execute, Dispatcher dispatcher, int port, string password)
    {
        _engine = engine;
        _host = host;
        _execute = execute;
        _dispatcher = dispatcher;
        _port = port;
        _passwordHash = Hash(password ?? string.Empty);
    }

    public bool Running => _listener is not null;

    /// <summary>
    /// What <c>/watch</c> draws: the layout and state going out on the program display right now.
    /// Set by the main window, because the server only knows the control panel — the program
    /// selection belongs to the studio.
    /// </summary>
    public static Func<(ScorebugLayout Layout, GameState State)?>? WatchSource { get; set; }

    /// <summary>
    /// The program graphic as a PNG, rendered by the app's own scorebug view. <c>/watch</c> used to
    /// rebuild the layout in HTML, which lost the real fonts, the palette colours and the made
    /// icons; a picture of the actual graphic cannot drift from what the audience sees.
    /// The number is a version the page watches so it only downloads a frame that changed.
    /// </summary>
    public static Func<(byte[] Png, long Version)?>? WatchImage { get; set; }

    /// <summary>
    /// The wall board page. It shows one picture of the program graphic and swaps that picture when
    /// the version changes, so nothing is rebuilt in the browser and no font or colour can be lost.
    /// </summary>
    const string WatchPage =
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
        "<title>ScorePlus — Program</title><style>" +
        "html,body{margin:0;height:100%;background:#000;overflow:hidden}" +
        "#shot{position:absolute;inset:0;width:100%;height:100%;object-fit:contain;display:none}" +
        "#wait{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;" +
        "color:#7C8A9C;font:600 14px/1.4 'Segoe UI',Arial,sans-serif;text-align:center;padding:24px}" +
        "</style></head><body>" +
        "<img id=\"shot\" alt=\"ScorePlus program graphic\">" +
        "<div id=\"wait\">Nothing is on the program output yet. Press Update Output in ScorePlus.</div>" +
        "<script>(function(){var shot=document.getElementById('shot'),wait=document.getElementById('wait'),now='';" +
        "function tick(){fetch('/api/watch/version',{cache:'no-store'}).then(function(r){return r.ok?r.json():null;})" +
        ".then(function(v){if(!v||!v.version)return;var next=String(v.version);if(next===now)return;now=next;" +
        "shot.onload=function(){shot.style.display='block';wait.style.display='none';};" +
        "shot.src='/watch.png?v='+encodeURIComponent(next);}).catch(function(){})" +
        ".then(function(){setTimeout(tick,1000);});}tick();})();</script></body></html>";

    /// <summary>The addresses an operator can type on a phone on the same network.</summary>
    public static IReadOnlyList<string> Urls(int port)
    {
        var urls = new List<string> { $"http://localhost:{port}" };
        try
        {
            foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
                if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    urls.Add($"http://{address}:{port}");
        }
        catch { /* No network adapter is not an error: localhost still works. */ }
        return urls;
    }

    public void Start()
    {
        if (_listener is not null) return;
        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            _ = Task.Run(() => AcceptLoop(token), token);
            Status?.Invoke($"REMOTE ON · {Urls(_port).LastOrDefault()} · password required · /watch is the program feed");
        }
        catch (Exception ex)
        {
            _listener = null;
            Status?.Invoke($"REMOTE OFF · {ex.Message}");
        }
    }

    public void Stop()
    {
        try { _cancel?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _cancel?.Dispose();
        _cancel = null;
        lock (_gate) _sessions.Clear();
        Status?.Invoke("REMOTE OFF");
    }

    public void Dispose() => Stop();

    async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(token); }
            catch { break; }
            _ = Task.Run(() => Serve(client, token), token);
        }
    }

    async Task Serve(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 10000;
                using var stream = client.GetStream();
                var request = await ReadRequest(stream, token);
                if (request is null) return;
                await Handle(stream, request, token);
            }
            catch { /* A dropped phone connection is normal; nothing to report. */ }
        }
    }

    sealed record RemoteRequest(string Method, string Path, Dictionary<string, string> Headers, string Body);

    static async Task<RemoteRequest?> ReadRequest(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[4096];
        var data = new List<byte>();
        while (data.Count < MaxRequestBytes)
        {
            var read = await stream.ReadAsync(buffer, token);
            if (read <= 0) return null;
            data.AddRange(buffer.AsSpan(0, read).ToArray());
            var text = Encoding.UTF8.GetString(data.ToArray());
            var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd < 0) continue;
            var lines = text[..headerEnd].Split("\r\n");
            var start = lines[0].Split(' ');
            if (start.Length < 2) return null;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var colon = line.IndexOf(':');
                if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
            var length = headers.TryGetValue("Content-Length", out var sizeText) && int.TryParse(sizeText, out var size) ? Math.Clamp(size, 0, MaxRequestBytes) : 0;
            var bodyStart = headerEnd + 4;
            var bytes = data.ToArray();
            while (length > 0 && bytes.Length < bodyStart + length)
            {
                var more = await stream.ReadAsync(buffer, token);
                if (more <= 0) break;
                var merged = new byte[bytes.Length + more];
                bytes.CopyTo(merged, 0);
                buffer.AsSpan(0, more).CopyTo(merged.AsSpan(bytes.Length));
                bytes = merged;
            }
            var body = bytes.Length > bodyStart ? Encoding.UTF8.GetString(bytes, bodyStart, Math.Min(length, bytes.Length - bodyStart)) : string.Empty;
            var path = start[1];
            var query = string.Empty;
            var q = path.IndexOf('?');
            if (q >= 0) { query = path[(q + 1)..]; path = path[..q]; }
            if (query.Length > 0) body = body.Length > 0 ? body + "&" + query : query;
            return new RemoteRequest(start[0].ToUpperInvariant(), path, headers, body);
        }
        return null;
    }

    /// <summary>
    /// The phone's Media and Roster tabs read through these, so the server never keeps a copy of the
    /// library: the phone always shows what the desktop shows.
    /// </summary>
    public static Func<string>? MediaJson { get; set; }
    public static Func<string, string, bool>? MediaAction { get; set; }
    public static Func<string>? RosterJson { get; set; }
    public static Func<string, string, string, bool>? RosterAction { get; set; }

    /// <summary>The phone's flag card: raise or clear a penalty, and pick what it was for and who.</summary>
    public static Func<string, string, string, bool>? PenaltyAction { get; set; }

    async Task Handle(NetworkStream stream, RemoteRequest request, CancellationToken token)
    {
        var ip = "remote";
        var authorized = IsAuthorized(request);
        if (request.Path == "/" && request.Method == "GET")
        {
            if (!authorized) { await Write(stream, 200, "text/html; charset=utf-8", RemotePage.Login(Html(RemotePage.ProductName), "Enter the remote password set in ScorePlus → Preferences.")); return; }
            await Write(stream, 200, "text/html; charset=utf-8", RemotePage.Panel());
            return;
        }
        if (request.Path == "/logo.png" && request.Method == "GET")
        {
            // The brand mark is the only asset the remote page needs; it is public like the sign-in page.
            var logo = LogoBytes.Value;
            if (logo is null) { await Write(stream, 404, "text/plain; charset=utf-8", "No logo."); return; }
            await WriteBytes(stream, 200, "image/png", logo);
            return;
        }
        if (request.Path == "/watch" && request.Method == "GET")
        {
            // The program feed: a page any phone, tablet or spare screen on the same network can
            // open. Read-only and public, because it is exactly what the audience is already
            // seeing; there is nothing here to control.
            await Write(stream, 200, "text/html; charset=utf-8", WatchPage);
            return;
        }
        if (request.Path == "/watch.png" && request.Method == "GET")
        {
            // The picture itself, drawn by the app's own scorebug view. This is what makes the wall
            // board look like the broadcast: real fonts, real palette colours, real logos.
            var frame = WatchImage?.Invoke();
            if (frame is null) { await Write(stream, 404, "text/plain; charset=utf-8", "Nothing is on the program output yet."); return; }
            await WriteBytes(stream, 200, "image/png", frame.Value.Png);
            return;
        }
        if (request.Path == "/api/watch/version" && request.Method == "GET")
        {
            // The page polls this, never the picture: a wall screen only downloads a new frame when
            // the graphic actually changed, and never reloads the page between changes.
            var frame = WatchImage?.Invoke();
            var version = frame is null ? string.Empty : frame.Value.Version.ToString();
            await Write(stream, 200, "application/json; charset=utf-8", $"{{\"version\":\"{version}\"}}");
            return;
        }
        if (request.Path == "/login" && request.Method == "POST")
        {
            var form = Form(request.Body);
            var password = form.GetValueOrDefault("password", string.Empty);
            if (TooManyAttempts(ip)) { await Write(stream, 429, "text/html; charset=utf-8", RemotePage.Login(RemotePage.ProductName, "Too many attempts. Wait half a minute and try again.")); return; }
            if (PasswordMatches(password))
            {
                var session = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                lock (_gate) _sessions[session] = DateTimeOffset.UtcNow + SessionLifetime;
                await Write(stream, 303, "text/html; charset=utf-8", "", $"sp_session={session}; HttpOnly; Path=/; SameSite=Lax; Max-Age={(int)SessionLifetime.TotalSeconds}");
                return;
            }
            RecordFailure(ip);
            await Write(stream, 200, "text/html; charset=utf-8", RemotePage.Login(RemotePage.ProductName, "That password does not match. Try again."));
            return;
        }
        if (request.Path == "/logout")
        {
            lock (_gate) _sessions.Clear();
            await Write(stream, 303, "text/html; charset=utf-8", "", "sp_session=; HttpOnly; Path=/; SameSite=Lax; Max-Age=0");
            return;
        }
        if (!authorized) { await Write(stream, 401, "text/plain; charset=utf-8", "Sign in first."); return; }

        if (request.Path == "/api/state" && request.Method == "GET")
        {
            await Write(stream, 200, "application/json; charset=utf-8", Snapshot());
            return;
        }
        if (request.Path == "/api/action" && request.Method == "POST")
        {
            var form = Form(request.Body);
            var result = Execute(form);
            await Write(stream, result.Ok ? 200 : 400, "application/json; charset=utf-8", JsonSerializer.Serialize(new { ok = result.Ok, message = result.Message }));
            return;
        }
        if (request.Path == "/api/media" && request.Method == "GET")
        {
            await Write(stream, 200, "application/json; charset=utf-8", _dispatcher.Invoke(() => MediaJson?.Invoke()) ?? "{}");
            return;
        }
        if (request.Path == "/api/rosters" && request.Method == "GET")
        {
            await Write(stream, 200, "application/json; charset=utf-8", _dispatcher.Invoke(() => RosterJson?.Invoke()) ?? "{}");
            return;
        }
        if (request.Path == "/api/media/action" && request.Method == "POST")
        {
            var form = Form(request.Body);
            var ok = _dispatcher.Invoke(() => MediaAction?.Invoke(form.GetValueOrDefault("a", string.Empty), form.GetValueOrDefault("id", string.Empty)) ?? false);
            var message = ok ? string.Empty : "That media button is not available.";
            await Write(stream, ok ? 200 : 400, "application/json; charset=utf-8", JsonSerializer.Serialize(new { ok, message }));
            return;
        }
        if (request.Path == "/api/penalty/action" && request.Method == "POST")
        {
            var form = Form(request.Body);
            var ok = _dispatcher.Invoke(() => PenaltyAction?.Invoke(form.GetValueOrDefault("a", string.Empty), form.GetValueOrDefault("type", string.Empty), form.GetValueOrDefault("side", "home")) ?? false);
            await Write(stream, ok ? 200 : 400, "application/json; charset=utf-8", JsonSerializer.Serialize(new { ok }));
            return;
        }
        if (request.Path == "/api/roster/action" && request.Method == "POST")
        {
            var form = Form(request.Body);
            var ok = _dispatcher.Invoke(() => RosterAction?.Invoke(form.GetValueOrDefault("a", string.Empty), form.GetValueOrDefault("side", "home"), form.GetValueOrDefault("id", string.Empty)) ?? false);
            var message = ok ? string.Empty : "That roster button is not available.";
            await Write(stream, ok ? 200 : 400, "application/json; charset=utf-8", JsonSerializer.Serialize(new { ok, message }));
            return;
        }
        await Write(stream, 404, "text/plain; charset=utf-8", "Not found.");
    }

    /// <summary>Runs one remote action on the UI thread, so the engine and every window stay in step.</summary>
    (bool Ok, string Message) Execute(Dictionary<string, string> form)
    {
        var action = form.GetValueOrDefault("a", string.Empty).Trim();
        if (action.Length == 0) return (false, "No action.");
        try
        {
            return _dispatcher.Invoke(() =>
            {
                var side = string.Equals(form.GetValueOrDefault("side"), "home", StringComparison.OrdinalIgnoreCase);
                int Number(string key, int fallback) => int.TryParse(form.GetValueOrDefault(key, string.Empty), out var parsed) ? parsed : fallback;
                switch (action)
                {
                    case "score.set": _engine.SetScore(side, Number("value", 0)); return (true, string.Empty);
                    case "clock.set": _engine.SetClock(Number("value", _engine.State.ClockSeconds)); return (true, string.Empty);
                    // Typed-over clocks from the phone, which used to need a desktop dialog.
                    case "playclock.set": _engine.SetPlayClock(Number("value", _engine.State.PlayClockSeconds)); return (true, string.Empty);
                    case "shotclock.set": _engine.SetShotClock(Number("value", _engine.State.ShotClockSeconds)); return (true, string.Empty);
                    case "timeoutclock.set": _engine.SetTimeoutClock(Number("value", _engine.State.TimeoutClockSeconds)); return (true, string.Empty);
                    case "panel.new": _host.OpenNewPanel(); return (true, string.Empty);
                    case "period.set": _engine.SetPeriod(Number("value", _engine.State.Period)); return (true, string.Empty);
                    case "sport.set": _engine.SetSportValue(form.GetValueOrDefault("control", string.Empty), Number("value", 0)); return (true, string.Empty);
                    case "football.down.set": _engine.SetDown(Number("value", 1)); return (true, string.Empty);
                    // The remote's down card steps without a confirmation dialog: the operator is
                    // not looking at the console, so a modal question would sit unseen.
                    case "football.down.plus": _engine.AdvanceDown(); return (true, string.Empty);
                    case "football.down.minus": _engine.PreviousDown(); return (true, string.Empty);
                    case "football.distance.set": _engine.SetFootballDistance(Number("value", _engine.State.YardsToGo)); return (true, string.Empty);
                    case "football.ball.set": _engine.SetFootballDistance(_engine.State.YardsToGo, Number("value", _engine.State.YardsToGoal)); return (true, string.Empty);
                    case "stat.adjust": _engine.AdjustStat(form.GetValueOrDefault("id", string.Empty), Number("value", 0)); return (true, string.Empty);
                    case "team.name":
                        var name = form.GetValueOrDefault("value", string.Empty).Trim();
                        if (name.Length == 0) return (false, "Enter a team name.");
                        if (side) _engine.SetTeams(name, _engine.State.AwayTeam); else _engine.SetTeams(_engine.State.HomeTeam, name);
                        return (true, string.Empty);
                    case "team.abbr":
                    {
                        var abbreviation = form.GetValueOrDefault("value", string.Empty).Trim();
                        if (abbreviation.Length == 0) return (false, "Enter an abbreviation.");
                        var s0 = _engine.State;
                        _engine.SetTeamIdentity(side, side ? s0.HomeTeam : s0.AwayTeam, abbreviation,
                            side ? s0.HomeLogoPath : s0.AwayLogoPath,
                            side ? s0.HomePrimaryColor : s0.AwayPrimaryColor,
                            side ? s0.HomeSecondaryColor : s0.AwaySecondaryColor);
                        return (true, string.Empty);
                    }
                    case "team.color":
                    {
                        var colour = form.GetValueOrDefault("value", string.Empty).Trim();
                        if (colour.Length == 0) return (false, "Enter a hex colour.");
                        if (!colour.StartsWith('#')) colour = "#" + colour;
                        // Both #RRGGBB and #AARRGGBB are accepted; the graphic stores either.
                        if (colour.Length is not (7 or 9) || !colour[1..].All(Uri.IsHexDigit)) return (false, "Use a colour like #38D7A3.");
                        var s0 = _engine.State;
                        _engine.SetTeamIdentity(side, side ? s0.HomeTeam : s0.AwayTeam,
                            side ? s0.HomeAbbreviation : s0.AwayAbbreviation,
                            side ? s0.HomeLogoPath : s0.AwayLogoPath,
                            colour,
                            side ? s0.HomeSecondaryColor : s0.AwaySecondaryColor);
                        return (true, string.Empty);
                    }
                    case "stat.add":
                    {
                        var statName = form.GetValueOrDefault("value", string.Empty).Trim();
                        if (statName.Length == 0) return (false, "Enter a stat name.");
                        _engine.AddStat(statName);
                        return (true, string.Empty);
                    }
                    case "stat.remove":
                        _engine.RemoveStat(form.GetValueOrDefault("id", string.Empty));
                        return (true, string.Empty);
                    case "stat.set":
                        var id = form.GetValueOrDefault("id", string.Empty);
                        _engine.SetStatText(id, form.GetValueOrDefault("value", string.Empty));
                        return (true, string.Empty);
                    case "layout.next": _host.ShiftLayout(1); return (true, string.Empty);
                    case "layout.prev": _host.ShiftLayout(-1); return (true, string.Empty);
                    case "output.publish": _host.PublishOutput(); return (true, string.Empty);
                    case "live.on": _host.LiveScoreClock = true; return (true, string.Empty);
                    case "live.off": _host.LiveScoreClock = false; return (true, string.Empty);
                    default:
                        // Everything else goes through the console's own action table, so a remote
                        // button and its keybind can never drift apart.
                        return _execute(action) ? (true, string.Empty) : (false, $"Unknown action '{action}'.");
                }
            });
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    string Snapshot() => _dispatcher.Invoke(() =>
    {
        var s = _engine.State;
        var state = new
        {
            ok = true,
            home = new { name = s.HomeTeam, abbr = s.HomeAbbreviation, score = s.HomeScore, timeouts = s.HomeTimeouts, color = Hex(s.HomePrimaryColor) },
            away = new { name = s.AwayTeam, abbr = s.AwayAbbreviation, score = s.AwayScore, timeouts = s.AwayTimeouts, color = Hex(s.AwayPrimaryColor) },
            clock = GameEngine.FormatClock(s.ClockSeconds),
            clockRunning = s.ClockRunning,
            period = s.Period,
            periodLabel = s.PeriodLabel,
            periods = s.TotalPeriods,
            down = s.Down,
            toGo = s.YardsToGo,
            ballOn = s.YardsToGoal,
            ballHome = s.BallOnHomeSide,
            playClock = s.PlayClockSeconds,
            playRunning = s.PlayClockRunning,
            shotClock = s.ShotClockSeconds,
            shotRunning = s.ShotClockRunning,
            timeoutClock = GameEngine.FormatClock(s.TimeoutClockSeconds),
            timeoutRunning = s.TimeoutClockRunning,
            possession = s.HomePossession ? "home" : "away",
            football = s.SportId == "football",
            hasShotClock = _engine.Sport.HasShotClock,
            sportId = s.SportId,
            sportName = _engine.Sport.Name,
            scoring = s.ScoringOptions,
            controls = s.Controls.Select(c => new
            {
                id = c.Id,
                label = c.Label,
                flag = c.Kind == SportControlKind.Flag,
                value = SportValues.Read(s, c.Id),
                display = SportValues.Display(s, c),
                min = c.Min,
                max = c.Max
            }),
            stats = s.Stats.Select(x => new { id = x.Id, name = x.Name, display = x.Display }),
            penalty = new { active = s.PenaltyActive, type = s.PenaltyType, home = s.PenaltyHome },
            penaltyTypes = GameEngine.PenaltyTypes,
            layout = _host.LayoutLabel,
            staged = _host.HasStagedChanges,
            live = _host.LiveScoreClock
        };
        return JsonSerializer.Serialize(state);
    });

    /// <summary>#RRGGBB for a browser field and a colour swatch: WPF stores #AARRGGBB, CSS does not.</summary>
    static string Hex(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return string.Empty;
        if (!text.StartsWith('#')) text = "#" + text;
        if (text.Length == 9) return "#" + text[3..];
        return text;
    }

    bool IsAuthorized(RemoteRequest request)
    {
        if (!request.Headers.TryGetValue("Cookie", out var cookie)) return false;
        var token = cookie.Split(';').Select(part => part.Trim()).FirstOrDefault(part => part.StartsWith("sp_session=", StringComparison.Ordinal))?["sp_session=".Length..];
        if (string.IsNullOrWhiteSpace(token)) return false;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(token, out var expiry)) return false;
            if (expiry < DateTimeOffset.UtcNow) { _sessions.Remove(token); return false; }
            return true;
        }
    }

    bool PasswordMatches(string password) =>
        CryptographicOperations.FixedTimeEquals(Hash(password ?? string.Empty), _passwordHash);

    static byte[] Hash(string text) => SHA256.HashData(Encoding.UTF8.GetBytes(text));

    bool TooManyAttempts(string ip)
    {
        lock (_gate)
        {
            if (!_attempts.TryGetValue(ip, out var entry)) return false;
            if (DateTimeOffset.UtcNow - entry.First > TimeSpan.FromSeconds(30)) { _attempts.Remove(ip); return false; }
            return entry.Failures >= 6;
        }
    }

    void RecordFailure(string ip)
    {
        lock (_gate)
        {
            if (_attempts.TryGetValue(ip, out var entry) && DateTimeOffset.UtcNow - entry.First < TimeSpan.FromSeconds(30))
                _attempts[ip] = (entry.Failures + 1, entry.First);
            else
                _attempts[ip] = (1, DateTimeOffset.UtcNow);
        }
    }

    static Dictionary<string, string> Form(string body)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in (body ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=');
            if (split < 0) { values[Uri.UnescapeDataString(pair.Replace('+', ' '))] = string.Empty; continue; }
            var key = Uri.UnescapeDataString(pair[..split].Replace('+', ' '));
            var value = Uri.UnescapeDataString(pair[(split + 1)..].Replace('+', ' '));
            values[key] = value;
        }
        return values;
    }

    static string Html(string text) => WebUtility.HtmlEncode(text);

    /// <summary>The app's own brand mark, read once out of the assembly.</summary>
    static readonly Lazy<byte[]?> LogoBytes = new(() =>
    {
        try
        {
            // Assembly-qualified first: the remote can be hosted by a test harness whose entry
            // assembly is not ScorePlus, and a plain pack URI resolves against the entry assembly.
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/ScorePlus;component/Assets/logo.png"))
                ?? System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/logo.png"));
            if (info is null) return null;
            using var source = info.Stream;
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch { return null; }
    });

    static async Task WriteBytes(NetworkStream stream, int status, string contentType, byte[] body)
    {
        var reason = status switch { 200 => "OK", 404 => "Not Found", _ => "OK" };
        var head = new StringBuilder()
            .Append($"HTTP/1.1 {status} {reason}\r\n")
            .Append($"Content-Type: {contentType}\r\n")
            .Append($"Content-Length: {body.Length}\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.UTF8.GetBytes(head.ToString()));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    static async Task Write(NetworkStream stream, int status, string contentType, string body, string? extraHeaders = null)
    {
        var reason = status switch { 200 => "OK", 303 => "See Other", 400 => "Bad Request", 401 => "Unauthorized", 404 => "Not Found", 429 => "Too Many Requests", _ => "OK" };
        var bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
        var head = new StringBuilder()
            .Append($"HTTP/1.1 {status} {reason}\r\n")
            .Append($"Content-Type: {contentType}\r\n")
            .Append($"Content-Length: {bytes.Length}\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("X-Frame-Options: DENY\r\n")
            .Append("Referrer-Policy: no-referrer\r\n")
            .Append("Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'; img-src 'self' data:\r\n")
            .Append("Connection: close\r\n");
        if (!string.IsNullOrEmpty(extraHeaders)) head.Append($"Set-Cookie: {extraHeaders}\r\n");
        if (status == 303) head.Append("Location: /\r\n");
        head.Append("\r\n");
        var headBytes = Encoding.UTF8.GetBytes(head.ToString());
        await stream.WriteAsync(headBytes);
        if (bytes.Length > 0) await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }
}
