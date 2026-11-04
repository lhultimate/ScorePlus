using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// Owns the live controller connection. Daktronics RTD is decoded locally. The other products
/// either publish through a Sportzcast/Scorebird bridge as JSON, or can be captured raw for
/// diagnostics when their native byte format is not public.
/// </summary>
public sealed class HardwareService : IDisposable
{
    readonly GameEngine _engine;
    CancellationTokenSource? _cancellation;
    Task? _worker;
    SerialPort? _port;
    // One tracker per clock, so a console's START/STOP on the game, shot or play clock is heard.
    readonly ClockRunTracker _gameClockRun = new();
    readonly ClockRunTracker _shotClockRun = new();
    readonly ClockRunTracker _playClockRun = new();

    public event Action<DeviceSnapshot>? Snapshot;
    public event Action<string>? Status;
    public event Action<string>? Raw;

    public HardwareService(GameEngine engine) => _engine = engine;

    public HardwareSettings? Active { get; private set; }
    public bool IsRunning => _worker is { IsCompleted: false };

    public void Start(HardwareSettings settings)
    {
        Stop();
        var copy = settings.Clone();
        Active = copy;
        // Raw capture is a diagnostic tap, not a clock source. It must not freeze the local clock.
        _engine.HardwareControlled = copy.Source != HardwareTransport.SerialCapture;
        _cancellation = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(copy, _cancellation.Token));
    }

    public void Stop()
    {
        var cancellation = _cancellation;
        _cancellation = null;
        var worker = _worker;
        _worker = null;
        cancellation?.Cancel();
        try { _port?.Close(); } catch { }
        _port = null;
        _engine.HardwareControlled = false;
        _gameClockRun.Reset();
        _shotClockRun.Reset();
        _playClockRun.Reset();
        Active = null;
        // The callbacks to the UI only use BeginInvoke/Dispatcher.BeginInvoke, so waiting here
        // cannot deadlock the dispatcher. It is still bounded: a stuck serial driver must not
        // make closing the app hang.
        try { worker?.Wait(400); } catch { }
        cancellation?.Dispose();
        Status?.Invoke("Hardware disconnected.");
    }

    async Task RunAsync(HardwareSettings settings, CancellationToken cancellation)
    {
        var device = HardwareDevices.Find(settings.DeviceId);
        try
        {
            Status?.Invoke($"Connecting to {device.Name}…");
            switch (settings.Source)
            {
                case HardwareTransport.Simulator:
                    await RunSimulatorAsync(cancellation);
                    break;
                case HardwareTransport.DaktronicsRtd:
                    await RunDaktronicsAsync(settings, cancellation);
                    break;
                case HardwareTransport.SerialCapture:
                    await RunCaptureAsync(settings, cancellation);
                    break;
                case HardwareTransport.SportzcastMqtt:
                    await RunMqttAsync(settings, cancellation);
                    break;
                case HardwareTransport.ScorebirdWebSocket:
                    await RunWebSocketAsync(settings, cancellation);
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested) Status?.Invoke($"Hardware error: {ex.Message}");
        }
        finally
        {
            // An old worker finishing after a reconnect must not switch off the new session's
            // hardware clock mode.
            if (ReferenceEquals(Active, settings)) _engine.HardwareControlled = false;
        }
    }

    // ---------- simulator ----------

    async Task RunSimulatorAsync(CancellationToken cancellation)
    {
        Status?.Invoke("Simulator running — no hardware required. Watch the score, clock and period update.");
        var clock = 12 * 60;
        var home = 12;
        var away = 7;
        var period = 1;
        var down = 1;
        var toGo = 10;
        var ballOn = 35;
        var shot = 24;
        var play = 25;
        var tick = 0;
        while (!cancellation.IsCancellationRequested)
        {
            clock = clock <= 0 ? 12 * 60 : clock - 1;
            if (tick > 0 && tick % 6 == 0)
            {
                if (period >= 4) { period = 1; home = 0; away = 0; }
                else period++;
            }
            if (tick > 0 && tick % 3 == 0)
            {
                home += 1;
                down = down == 4 ? 1 : down + 1;
                toGo = down == 1 ? 10 : Math.Max(1, 10 - down * 2);
                ballOn = Math.Clamp(ballOn + 8, 1, 50);
            }
            shot = shot <= 0 ? 24 : shot - 1;
            play = play <= 0 ? 25 : play - 1;
            Snapshot?.Invoke(new DeviceSnapshot(
                home, away, clock, true, period,
                3, 2, true, down, toGo, ballOn,
                shot, true, play, true,
                2, 1, 4, 2, 1, "Simulator"));
            tick++;
            try { await Task.Delay(500, cancellation); }
            catch (OperationCanceledException) { break; }
        }
    }

    // ---------- serial ----------

    SerialPort OpenSerial(HardwareSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SerialPort))
            throw new InvalidOperationException("Choose a serial port first.");
        var port = new SerialPort(settings.SerialPort, Math.Clamp(settings.BaudRate, 300, 921600),
            Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 500,
            WriteTimeout = 500,
            Handshake = Handshake.None,
            DtrEnable = true,
            RtsEnable = true
        };
        port.Open();
        _port = port;
        return port;
    }

    async Task RunDaktronicsAsync(HardwareSettings settings, CancellationToken cancellation)
    {
        using var port = OpenSerial(settings);
        var decoder = new DaktronicsRtdDecoder(_engine.State.SportId);
        var buffer = new byte[4096];
        var started = false;
        Status?.Invoke($"Daktronics RTD connected on {port.PortName} · {port.BaudRate} 8-N-1. Press STOP on the console once to send a full state.");
        while (!cancellation.IsCancellationRequested)
        {
            var read = await port.BaseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellation);
            if (read <= 0) continue;
            decoder.SetSport(_engine.State.SportId);
            var snapshot = decoder.Feed(buffer.AsSpan(0, read));
            if (snapshot is null) continue;
            if (!started)
            {
                started = true;
                Status?.Invoke($"Receiving {HardwareDevices.Find(settings.DeviceId).Name} data on {port.PortName}.");
            }
            Snapshot?.Invoke(InferRunningClocks(snapshot));
        }
    }

    async Task RunCaptureAsync(HardwareSettings settings, CancellationToken cancellation)
    {
        using var port = OpenSerial(settings);
        Status?.Invoke($"Raw capture on {port.PortName} · {port.BaudRate} 8-N-1. This mode logs bytes but does not decode or change the game.");
        var buffer = new byte[4096];
        while (!cancellation.IsCancellationRequested)
        {
            var read = await port.BaseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellation);
            if (read > 0) Raw?.Invoke(DescribeBytes(buffer, read));
        }
    }

    static string DescribeBytes(byte[] buffer, int count)
    {
        var hex = Convert.ToHexString(buffer.AsSpan(0, count));
        var text = new string(buffer.Take(count).Select(value => value is >= 32 and < 127 ? (char)value : '.').ToArray());
        return $"{DateTime.Now:HH:mm:ss.fff}  {count} bytes  {hex}  |{text}|";
    }

    /// <summary>
    /// Daktronics RTD does not carry a clock-running bit in every sport, so every clock the console
    /// can start or stop is judged from its own readings. Without this a START on the shot or play
    /// clock left the software frozen on the number the console sent.
    /// </summary>
    DeviceSnapshot InferRunningClocks(DeviceSnapshot snapshot)
    {
        var now = Environment.TickCount64;
        return snapshot with
        {
            ClockRunning = snapshot.ClockRunning ?? _gameClockRun.Track(snapshot.ClockSeconds, now),
            ShotClockRunning = snapshot.ShotClockRunning ?? _shotClockRun.Track(snapshot.ShotClockSeconds, now),
            PlayClockRunning = snapshot.PlayClockRunning ?? _playClockRun.Track(snapshot.PlayClockSeconds, now)
        };
    }

    // ---------- Sportzcast MQTT ----------

    async Task RunMqttAsync(HardwareSettings settings, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(settings.MqttHost) || string.IsNullOrWhiteSpace(settings.MqttTopic))
            throw new InvalidOperationException("Enter the MQTT host and topic first.");
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await RunMqttSessionAsync(settings, cancellation);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (cancellation.IsCancellationRequested) break;
                Status?.Invoke($"Sportzcast MQTT: {ex.Message} Retrying in 5 seconds.");
            }
            try { await Task.Delay(5000, cancellation); } catch (OperationCanceledException) { break; }
        }
    }

    async Task RunMqttSessionAsync(HardwareSettings settings, CancellationToken cancellation)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(settings.MqttHost, Math.Clamp(settings.MqttPort, 1, 65535), cancellation);
        await using var network = client.GetStream();
        Stream stream = network;
        SslStream? tls = null;

        try
        {
            if (settings.MqttTls)
            {
                tls = new SslStream(network, false);
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = settings.MqttHost
                }, cancellation);
                stream = tls;
            }

            await stream.WriteAsync(ConnectPacket(settings), cancellation);
            var connack = await ReadPacketAsync(stream, cancellation);
            if (connack is null || connack.Value.Header >> 4 != 2 || connack.Value.Payload.Length < 2)
                throw new InvalidOperationException("no CONNACK");
            if (connack.Value.Payload[1] != 0)
                throw new InvalidOperationException($"broker refused connection (code {connack.Value.Payload[1]})");

            await stream.WriteAsync(SubscribePacket(settings.MqttTopic), cancellation);
            Status?.Invoke($"Sportzcast MQTT connected · {settings.MqttHost}:{settings.MqttPort} · {settings.MqttTopic}");

            while (!cancellation.IsCancellationRequested)
            {
                var packet = await ReadPacketAsync(stream, cancellation);
                if (packet is null) break;
                var type = packet.Value.Header >> 4;
                if (type != 3) continue; // PUBLISH only
                var header = packet.Value.Header;
                var payload = packet.Value.Payload;
                var offset = 0;
                var qos = (header >> 1) & 3;
                if (qos > 0) offset += 2;
                if (payload.Length < offset + 2) continue;
                var topicLength = (payload[offset] << 8) | payload[offset + 1];
                offset += 2;
                if (payload.Length < offset + topicLength) continue;
                offset += topicLength;
                var json = Encoding.UTF8.GetString(payload, offset, payload.Length - offset);
                ApplyJson(json, "Sportzcast");
            }
        }
        finally
        {
            tls?.Dispose();
        }
    }

    static byte[] ConnectPacket(HardwareSettings settings)
    {
        var body = new MemoryStream();
        WriteField(body, "MQTT");
        body.WriteByte(4); // MQTT 3.1.1
        var flags = (byte)0x02; // clean session
        var hasUser = !string.IsNullOrWhiteSpace(settings.MqttUsername);
        // MQTT 3.1.1 does not allow a password flag without a username.
        var hasPassword = hasUser && !string.IsNullOrWhiteSpace(settings.MqttPassword);
        if (hasUser) flags |= 0x80;
        if (hasPassword) flags |= 0x40;
        body.WriteByte(flags);
        body.WriteByte(0); // keepalive disabled; the read loop owns the connection
        body.WriteByte(0);
        WriteField(body, "scoreplus-" + Guid.NewGuid().ToString("N")[..8]);
        if (hasUser) WriteField(body, settings.MqttUsername);
        if (hasPassword) WriteField(body, settings.MqttPassword);
        return MqttPacket(0x10, body.ToArray());
    }

    static byte[] SubscribePacket(string topic)
    {
        var body = new MemoryStream();
        body.WriteByte(0);
        body.WriteByte(1); // packet identifier
        WriteField(body, topic);
        body.WriteByte(0); // QoS 0
        return MqttPacket(0x82, body.ToArray());
    }

    static void WriteField(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.WriteByte((byte)(bytes.Length >> 8));
        stream.WriteByte((byte)(bytes.Length & 0xFF));
        stream.Write(bytes);
    }

    static byte[] MqttPacket(byte header, byte[] body)
    {
        using var packet = new MemoryStream();
        packet.WriteByte(header);
        var length = body.Length;
        do
        {
            var digit = (byte)(length % 128);
            length /= 128;
            if (length > 0) digit |= 0x80;
            packet.WriteByte(digit);
        } while (length > 0);
        packet.Write(body);
        return packet.ToArray();
    }

    static async Task<(byte Header, byte[] Payload)?> ReadPacketAsync(Stream stream, CancellationToken cancellation)
    {
        var first = new byte[1];
        if (await stream.ReadAsync(first, cancellation) == 0) return null;
        var multiplier = 1;
        var length = 0;
        for (var index = 0; index < 4; index++)
        {
            var one = new byte[1];
            if (await stream.ReadAsync(one, cancellation) == 0) return null;
            length += (one[0] & 127) * multiplier;
            if ((one[0] & 128) == 0) break;
            multiplier *= 128;
        }
        var payload = new byte[length];
        if (length > 0) await stream.ReadExactlyAsync(payload, cancellation);
        return (first[0], payload);
    }

    // ---------- Scorebird WebSocket ----------

    async Task RunWebSocketAsync(HardwareSettings settings, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(settings.WebSocketUrl))
            throw new InvalidOperationException("Enter the Scorebird WebSocket URL first.");
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await RunWebSocketSessionAsync(settings, cancellation);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (cancellation.IsCancellationRequested) break;
                Status?.Invoke($"Scorebird WebSocket: {ex.Message} Retrying in 5 seconds.");
            }
            try { await Task.Delay(5000, cancellation); } catch (OperationCanceledException) { break; }
        }
    }

    async Task RunWebSocketSessionAsync(HardwareSettings settings, CancellationToken cancellation)
    {
        using var socket = new ClientWebSocket();
        if (!string.IsNullOrWhiteSpace(settings.WebSocketToken))
            socket.Options.SetRequestHeader("Authorization", "Bearer " + settings.WebSocketToken.Trim());
        await socket.ConnectAsync(new Uri(settings.WebSocketUrl, UriKind.Absolute), cancellation);
        Status?.Invoke($"Scorebird WebSocket connected · {settings.WebSocketUrl}");

        var buffer = new byte[32 * 1024];
        while (!cancellation.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation);
                if (result.MessageType == WebSocketMessageType.Close) return;
                if (result.MessageType == WebSocketMessageType.Text)
                    message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (message.Length == 0) continue;
            var json = Encoding.UTF8.GetString(message.ToArray());
            ApplyJson(json, "Scorebird");
        }
    }

    void ApplyJson(string json, string source)
    {
        if (!DeviceJson.TryParse(json, source, out var snapshot)) return;
        Snapshot?.Invoke(snapshot);
    }

    public void Dispose()
    {
        Stop();
    }
}

/// <summary>
/// Reads the JSON shape published by Sportzcast, and the common aliases used by Scorebird
/// overlays. Missing fields stay null so a partial message never zeroes a live game.
/// </summary>
internal static class DeviceJson
{
    public static bool TryParse(string json, string source, out DeviceSnapshot snapshot)
    {
        snapshot = new DeviceSnapshot(Source: source);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var root = document.RootElement;
            if (TryParseObject(root, source, out snapshot)) return true;

            // Scorebird and some Sportzcast deployments wrap the game in a data/event object.
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object) continue;
                if (property.Name is not ("data" or "game" or "payload" or "scoreboard" or "state" or "event")) continue;
                if (TryParseObject(property.Value, source, out snapshot)) return true;
            }
            return false;
        }
        catch (JsonException) { return false; }
    }

    static bool TryParseObject(JsonElement root, string source, out DeviceSnapshot snapshot)
    {
        snapshot = new DeviceSnapshot(Source: source);
        var homeScore = Score(root, true);
        var awayScore = Score(root, false);
        var clock = Clock(root);
        var period = Int(root, "Period", "Quarter", "Inning") ?? Ordinal(root, "PeriodOrdinal");
        var homeTimeouts = Int(root, "HomeNumberOfTO", "HomeTimeOuts", "HomeTimeouts", "HomeTO");
        var awayTimeouts = Int(root, "GuestNumberOfTO", "AwayNumberOfTO", "GuestTimeOuts", "AwayTimeOuts", "GuestTimeouts", "AwayTimeouts", "GuestTO", "AwayTO");
        var possession = Possession(root);
        var down = Int(root, "Down", "DownNumber");
        var yardsToGo = Int(root, "ToGo", "YardsToGo", "Distance");
        var ballOn = Int(root, "BallOn", "YardLine", "BallSpot");
        var shotClock = Clock(root, "ShotClock", "ShotClockSeconds");
        var playClock = Clock(root, "PlayClock", "PlayClockSeconds");
        var homeFouls = Int(root, "HomeFouls", "HomeTeamFouls");
        var awayFouls = Int(root, "GuestFouls", "AwayFouls", "VisitorFouls");
        var balls = Int(root, "Balls", "BallCount");
        var strikes = Int(root, "Strikes", "StrikeCount");
        var outs = Int(root, "Outs", "OutCount");
        var clockRunning = Bool(root, "ClockRun", "ClockRunning");
        var shotRunning = Bool(root, "ShotClockRun", "ShotClockRunning");
        var playRunning = Bool(root, "PlayClockRun", "PlayClockRunning");

        snapshot = new DeviceSnapshot(homeScore, awayScore, clock, clockRunning, period,
            homeTimeouts, awayTimeouts, possession, down, yardsToGo, ballOn, shotClock,
            shotRunning, playClock, playRunning, homeFouls, awayFouls, balls, strikes, outs, source);
        return snapshot.HasValues;
    }

    static int? Score(JsonElement root, bool home)
    {
        var prefixes = home
            ? new[] { "Home" }
            : new[] { "Away", "Guest", "Visitor", "Visitors" };
        foreach (var prefix in prefixes)
        {
            var direct = Int(root, prefix + "Score", prefix + "Total", prefix);
            if (direct is not null) return direct;
            var hundreds = Int(root, prefix + "Hundreds");
            var tens = Int(root, prefix + "Tens");
            var ones = Int(root, prefix + "Ones");
            if (hundreds is not null || tens is not null || ones is not null)
                return (hundreds ?? 0) * 100 + (tens ?? 0) * 10 + (ones ?? 0);
        }
        return null;
    }

    static int? Clock(JsonElement root, params string[] names)
    {
        var direct = Int(root, names.Length == 0 ? ["ClockSeconds", "GameClockSeconds"] : names);
        if (direct is not null) return direct;
        var text = Text(root, names.Length == 0 ? ["Clock", "GameClock", "Time", "GameTime"] : names);
        if (ParseClock(text) is int fromText) return fromText;

        var digits = new[]
        {
            Int(root, "ClockDigit1"), Int(root, "ClockDigit2"),
            Int(root, "ClockDigit3"), Int(root, "ClockDigit4")
        };
        if (digits.All(value => value is not null))
            return ((digits[0]!.Value * 10 + digits[1]!.Value) * 60) + digits[2]!.Value * 10 + digits[3]!.Value;
        return null;
    }

    static int? ParseClock(string? text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return null;
        var colon = text.IndexOf(':');
        if (colon >= 0)
        {
            if (!int.TryParse(text[..colon], out var minutes)) return null;
            var secondsText = text[(colon + 1)..];
            var dot = secondsText.IndexOf('.');
            if (dot >= 0) secondsText = secondsText[..dot];
            return int.TryParse(secondsText, out var seconds) ? Math.Max(0, minutes * 60 + seconds) : null;
        }
        if (text.All(char.IsDigit) && text.Length is 3 or 4)
        {
            var split = text.Length - 2;
            return int.Parse(text[..split], CultureInfo.InvariantCulture) * 60
                + int.Parse(text[split..], CultureInfo.InvariantCulture);
        }
        return int.TryParse(text, out var value) ? Math.Max(0, value) : null;
    }

    static int? Ordinal(JsonElement root, string name)
    {
        var text = Text(root, name);
        if (string.IsNullOrWhiteSpace(text)) return null;
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : null;
    }

    static bool? Possession(JsonElement root)
    {
        var flag = Bool(root, "HomePossession", "PossessionHome");
        if (flag is not null) return flag;
        var text = Text(root, "Possession", "PossessionTeam");
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Contains("home", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("away", StringComparison.OrdinalIgnoreCase)
            || text.Contains("guest", StringComparison.OrdinalIgnoreCase)
            || text.Contains("visit", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    static int? Int(JsonElement root, params string[] names)
    {
        if (!TryGet(root, out var value, names)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()?.Trim();
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number;
        }
        return null;
    }

    static bool? Bool(JsonElement root, params string[] names)
    {
        if (!TryGet(root, out var value, names)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
            JsonValueKind.String => value.GetString()?.Trim().ToLowerInvariant() switch
            {
                "true" or "1" or "yes" or "on" => true,
                "false" or "0" or "no" or "off" => false,
                _ => null
            },
            _ => null
        };
    }

    static string? Text(JsonElement root, params string[] names)
    {
        if (!TryGet(root, out var value, names)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    static bool TryGet(JsonElement root, out JsonElement value, params string[] names)
    {
        foreach (var property in root.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }
}
