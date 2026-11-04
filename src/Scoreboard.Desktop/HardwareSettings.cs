namespace Scoreboard.Desktop;

/// <summary>How ScorePlus receives a controller's live data.</summary>
public enum HardwareTransport
{
    Simulator,
    DaktronicsRtd,
    SportzcastMqtt,
    ScorebirdWebSocket,
    SerialCapture
}

public sealed record HardwareTransportOption(string Value, string Name)
{
    public override string ToString() => Name;
}

public static class HardwareTransports
{
    public static IReadOnlyList<HardwareTransportOption> All { get; } =
    [
        new(nameof(HardwareTransport.Simulator), "Simulator (no hardware)"),
        new(nameof(HardwareTransport.DaktronicsRtd), "Daktronics RTD (direct serial)"),
        new(nameof(HardwareTransport.SportzcastMqtt), "Sportzcast MQTT (decoded JSON)"),
        new(nameof(HardwareTransport.ScorebirdWebSocket), "Scorebird NeST (WebSocket JSON)"),
        new(nameof(HardwareTransport.SerialCapture), "Raw serial capture (diagnostic)")
    ];
}

/// <summary>One supported console and the path ScorePlus recommends for it.</summary>
public sealed record HardwareDeviceProfile(
    string Id,
    string Name,
    HardwareTransport Transport,
    int BaudRate,
    string Summary,
    string Notes)
{
    // WPF uses ToString for a selected ComboBox item even when DisplayMemberPath is set. Without
    // this the closed box fell back to the type name instead of the controller name.
    public override string ToString() => Name;
}

public static class HardwareDevices
{
    public const string DaktronicsAllSport5000 = "daktronics-allsport-5000";

    public static IReadOnlyList<HardwareDeviceProfile> All { get; } =
    [
        new("simulator", "Simulator — no hardware",
            HardwareTransport.Simulator, 0,
            "Runs a live test feed with no controller attached. Use this to verify the hardware window, the live game updates and the audience output.",
            "The simulator generates score, clock, period, possession, down/distance, shot clock and baseball count data. Choose Connect and watch the console update. It is a test source, not a substitute for a real controller."),
        new(DaktronicsAllSport5000, "Daktronics All Sport 5000",
            HardwareTransport.DaktronicsRtd, 19200,
            "Native RTD decoder — score, clock, period, timeouts, possession, down and distance, play/shot clock and baseball count.",
            "Connect the console's 25-pin RTD output with a USB-serial adapter (19200, 8 data bits, no parity, 1 stop bit, no flow control). A null-modem cable is commonly required. Press STOP on the console once after connecting so it sends a full state."),
        new("all-american-8000", "All American 8000",
            HardwareTransport.SportzcastMqtt, 9600,
            "Recommended path: Sportzcast ScoreLink MQTT JSON. Native serial output is proprietary and undocumented.",
            "The 8000's controller output is not publicly documented. A Sportzcast ScoreLink decodes it; enter the bot number/topic and credentials below. Raw capture is available for diagnostics."),
        new("all-american-9000", "All American 9000",
            HardwareTransport.SportzcastMqtt, 9600,
            "Recommended path: Sportzcast ScoreLink MQTT JSON. The 9000 has a DB9 RS232 I/O port, but no public byte format.",
            "Use the RS232 I/O port with a Sportzcast ScoreLink for decoded data. If you are reverse-engineering the stream, switch to Raw serial capture and use a null-modem adapter."),
        new("oes-isc-9000", "OES ISC 9000",
            HardwareTransport.SportzcastMqtt, 115200,
            "Recommended path: Sportzcast ScoreLink MQTT JSON. OES' own UART format is proprietary and requires OES firmware/configuration.",
            "OES documents 115200 8N1 for the Pro network option, but not the field layout. For a direct test switch to Raw serial capture; for live data use a Sportzcast bridge."),
        new("oes-isc-edge", "OES ISC Edge",
            HardwareTransport.SportzcastMqtt, 115200,
            "Recommended path: Sportzcast ScoreLink MQTT JSON. No public ISC Edge data-output specification was found.",
            "OES has not published an ISC Edge data format. Use a Sportzcast bridge for decoded live data, or Raw serial capture with the console's GAME OUT port."),
        new("scorebird-nest", "Scorebird NeST",
            HardwareTransport.ScorebirdWebSocket, 9600,
            "Scorebird cloud WebSocket JSON. Scorebird issues API/overlay credentials on request.",
            "NeST reads the controller and publishes through Scorebird. Ask support@scorebird.com for API access, then paste the WebSocket URL (and token if required). JSON field names vary by sport; ScorePlus reads the common score/clock/period aliases."),
        new("sportzcast-scorebot", "Sportzcast Scorebot",
            HardwareTransport.SportzcastMqtt, 9600,
            "Sportzcast MQTT JSON. Subscribes to bot/{number}/json; QoS 0, so occasional updates may be lost.",
            "Cloud broker: score.sportzcast.net:8883 with TLS, or a local ScoreConnect/ScoreLink+ broker on port 1883. Username is your Sportzcast email and password is your access token. Topic example: bot/50/json."),
        new("electro-mech", "Electro-Mech",
            HardwareTransport.SportzcastMqtt, 9600,
            "Recommended path: Sportzcast ScoreLink MQTT JSON. Electro-Mech's 1/4-inch stereo data output has no public protocol.",
            "Native output uses a 1/4-inch stereo data jack (often D1/D2), not RS-232. Use the matching Sportzcast adapter and bridge for decoded data, or Raw serial capture with a suitable interface."),
        new("fairplay-mp70-mp80", "Fair-Play MP-70 / MP-80",
            HardwareTransport.SportzcastMqtt, 9600,
            "Recommended path: Sportzcast ScoreLink MQTT JSON. Fair-Play names its native formats MP-69 and MP-70 but does not publish their bytes.",
            "Use a Sportzcast bridge for decoded data. For direct capture, connect the controller's RS-232 output through an FTDI adapter and null-modem cable, then select Raw serial capture.")
    ];

    public static HardwareDeviceProfile Find(string? id) =>
        All.FirstOrDefault(device => string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? All.First(device => device.Id == DaktronicsAllSport5000);
}

/// <summary>Everything the hardware desk remembers between launches.</summary>
public sealed class HardwareSettings
{
    public bool AutoConnect { get; set; }
    public string DeviceId { get; set; } = HardwareDevices.DaktronicsAllSport5000;
    public string Transport { get; set; } = nameof(HardwareTransport.DaktronicsRtd);
    public string SerialPort { get; set; } = "";
    public int BaudRate { get; set; } = 19200;

    public string MqttHost { get; set; } = "score.sportzcast.net";
    public int MqttPort { get; set; } = 8883;
    public bool MqttTls { get; set; } = true;
    public string MqttUsername { get; set; } = "";
    public string MqttPassword { get; set; } = "";
    public string MqttTopic { get; set; } = "bot/0/json";

    public string WebSocketUrl { get; set; } = "";
    public string WebSocketToken { get; set; } = "";

    public HardwareTransport Source => Enum.TryParse<HardwareTransport>(Transport, true, out var value)
        ? value
        : HardwareTransport.DaktronicsRtd;

    public void ApplyProfile(HardwareDeviceProfile profile)
    {
        DeviceId = profile.Id;
        Transport = profile.Transport.ToString();
        BaudRate = profile.BaudRate;
    }

    public HardwareSettings Clone() => new()
    {
        AutoConnect = AutoConnect,
        DeviceId = DeviceId,
        Transport = Transport,
        SerialPort = SerialPort,
        BaudRate = BaudRate,
        MqttHost = MqttHost,
        MqttPort = MqttPort,
        MqttTls = MqttTls,
        MqttUsername = MqttUsername,
        MqttPassword = MqttPassword,
        MqttTopic = MqttTopic,
        WebSocketUrl = WebSocketUrl,
        WebSocketToken = WebSocketToken
    };
}
