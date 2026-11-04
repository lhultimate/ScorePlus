using System.Text;

namespace Scoreboard.Core;

/// <summary>
/// A partial game reading from a scoreboard controller. Every value is optional because the
/// controller may only send the fields that changed, and because not every product exposes every
/// field. A null value means "this frame did not say", never "set it to zero".
/// </summary>
public sealed record DeviceSnapshot(
    int? HomeScore = null,
    int? AwayScore = null,
    int? ClockSeconds = null,
    bool? ClockRunning = null,
    int? Period = null,
    int? HomeTimeouts = null,
    int? AwayTimeouts = null,
    bool? HomePossession = null,
    int? Down = null,
    int? YardsToGo = null,
    int? BallOn = null,
    int? ShotClockSeconds = null,
    bool? ShotClockRunning = null,
    int? PlayClockSeconds = null,
    bool? PlayClockRunning = null,
    int? HomeFouls = null,
    int? AwayFouls = null,
    int? Balls = null,
    int? Strikes = null,
    int? Outs = null,
    string? Source = null)
{
    public bool HasValues => HomeScore is not null || AwayScore is not null || ClockSeconds is not null
        || ClockRunning is not null || Period is not null || HomeTimeouts is not null
        || AwayTimeouts is not null || HomePossession is not null || Down is not null
        || YardsToGo is not null || BallOn is not null || ShotClockSeconds is not null
        || ShotClockRunning is not null || PlayClockSeconds is not null || PlayClockRunning is not null
        || HomeFouls is not null || AwayFouls is not null || Balls is not null || Strikes is not null
        || Outs is not null;
}

/// <summary>
/// Works out whether a clock is running from the numbers a controller sends. Daktronics RTD
/// carries the clock value on every button press but no running flag, so a reading that decreases
/// means the clock is running and one that holds still for a moment means it was stopped.
/// One tracker per clock, so the game, shot and play clocks are judged independently.
/// </summary>
public sealed class ClockRunTracker
{
    readonly int _stillMilliseconds;
    int? _last;
    long _lastAt;
    bool? _running;

    public ClockRunTracker(int stillMilliseconds = 1200) =>
        _stillMilliseconds = Math.Max(100, stillMilliseconds);

    /// <summary>The running state after this reading, or null while there is nothing to go on yet.</summary>
    public bool? Track(int? seconds, long nowMilliseconds)
    {
        if (seconds is not int clock) return _running;
        if (_last is null)
        {
            _last = clock;
            _lastAt = nowMilliseconds;
            return _running;
        }
        if (_last != clock)
        {
            _running = clock < _last;
            _last = clock;
            _lastAt = nowMilliseconds;
            return _running;
        }
        if (nowMilliseconds - _lastAt > _stillMilliseconds) _running = false;
        return _running;
    }

    public void Reset()
    {
        _last = null;
        _lastAt = 0;
        _running = null;
    }
}

/// <summary>
/// Decoder for Daktronics Enhanced RTD data from an All Sport 5000-family console.
///
/// The console emits a stream of fixed-width character fields, but each packet carries only the
/// slice that changed. This class keeps the field buffer between packets and splices each packet
/// at its one-based offset, then reads the football, basketball or baseball fields from it.
///
/// Wire format: SYN SOH &lt;header&gt; STX &lt;text&gt; EOT &lt;two hex checksum digits&gt; ETX.
/// The checksum is the low byte of the sum of every byte from SOH through EOT.
/// </summary>
public sealed class DaktronicsRtdDecoder
{
    readonly List<byte> _wire = [];
    char[] _buffer = [];
    string _sportId = "basketball";

    public string SportId => _sportId;
    public int BufferLength => _buffer.Length;
    public string BufferText => new(_buffer);

    public DaktronicsRtdDecoder(string? sportId = null) => Reset(sportId);

    /// <summary>Selects the field table. Changing sport starts a fresh buffer.</summary>
    public void SetSport(string? sportId)
    {
        var normalized = NormalizeSport(sportId);
        if (normalized == _sportId && _buffer.Length > 0) return;
        Reset(normalized);
    }

    public void Reset(string? sportId = null)
    {
        _sportId = NormalizeSport(sportId ?? _sportId);
        _buffer = Enumerable.Repeat(' ', BufferSize(_sportId)).ToArray();
        _wire.Clear();
    }

    /// <summary>
    /// Adds bytes from a serial read and returns the newest complete state. A read may contain
    /// several packets; all are applied, and the last one is returned.
    /// </summary>
    public DeviceSnapshot? Feed(ReadOnlySpan<byte> data)
    {
        foreach (var value in data) _wire.Add(value);
        DeviceSnapshot? newest = null;
        while (ProcessOne() is { } snapshot) newest = snapshot;
        return newest;
    }

    DeviceSnapshot? ProcessOne()
    {
        var syn = _wire.IndexOf(0x16);
        if (syn < 0)
        {
            // Idle line noise has no value. Do not let an unterminated read grow forever.
            if (_wire.Count > 1024) _wire.RemoveRange(0, _wire.Count - 64);
            return null;
        }
        if (syn > 0) _wire.RemoveRange(0, syn);

        var soh = _wire.IndexOf(0x01);
        if (soh < 0) return null;
        var stx = _wire.IndexOf(0x02, soh + 1);
        if (stx < 0) return null;
        var eot = _wire.IndexOf(0x04, stx + 1);
        if (eot < 0) return null;
        if (_wire.Count < eot + 4) return null;

        var etx = _wire.IndexOf(0x17, eot + 3);
        if (etx < 0) return null;

        var checksumText = Encoding.ASCII.GetString(_wire.ToArray(), eot + 1, 2);
        if (!byte.TryParse(checksumText, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var expected))
        {
            _wire.RemoveRange(0, etx + 1);
            return null;
        }

        var actual = 0;
        for (var index = soh; index <= eot; index++) actual = (actual + _wire[index]) & 0xFF;
        if (actual != expected)
        {
            _wire.RemoveRange(0, etx + 1);
            return null;
        }

        var header = Encoding.ASCII.GetString(_wire.ToArray(), soh + 1, stx - soh - 1);
        var text = Encoding.ASCII.GetString(_wire.ToArray(), stx + 1, eot - stx - 1);
        _wire.RemoveRange(0, etx + 1);

        // Daktronics offsets are one-based. The header ends with a four-digit start offset.
        if (header.Length < 4 || !int.TryParse(header[^4..], out var offset) || offset <= 0) return null;
        var start = offset - 1;
        if (start >= _buffer.Length) return null;
        var count = Math.Min(text.Length, _buffer.Length - start);
        for (var index = 0; index < count; index++) _buffer[start + index] = text[index];
        return Decode();
    }

    DeviceSnapshot Decode()
    {
        int? clock = ReadClock(1, 5) ?? ReadClock(6, 8);
        int? homeScore = ReadInt(108, 4);
        int? awayScore = ReadInt(112, 4);
        int? period = ReadInt(142, 2);
        int? homeTimeouts = ReadInt(122, 2);
        int? awayTimeouts = ReadInt(130, 2);
        bool? homePossession = null;
        var homeMarker = ReadText(210, 1);
        var awayMarker = ReadText(215, 1);
        if (homeMarker.Contains('<')) homePossession = true;
        else if (awayMarker.Contains('>')) homePossession = false;

        int? playClock = null;
        int? shotClock = null;
        int? down = null;
        int? yardsToGo = null;
        int? ballOn = null;
        int? homeFouls = null;
        int? awayFouls = null;
        int? balls = null;
        int? strikes = null;
        int? outs = null;

        switch (_sportId)
        {
            case "football":
                playClock = ReadClock(201, 8);
                ballOn = ReadInt(220, 2);
                down = ParseDown(ReadText(222, 3));
                yardsToGo = ReadInt(225, 2);
                break;
            case "basketball":
                shotClock = ReadClock(201, 8);
                homeFouls = ReadInt(236, 2);
                awayFouls = ReadInt(238, 2);
                break;
            case "baseball":
                balls = ReadInt(222, 1);
                strikes = ReadInt(223, 1);
                outs = ReadInt(224, 1);
                break;
            case "hockey":
            case "lacrosse":
                shotClock = ReadClock(201, 8);
                break;
        }

        return new DeviceSnapshot(
            homeScore, awayScore, clock, null, period, homeTimeouts, awayTimeouts,
            homePossession, down, yardsToGo, ballOn, shotClock, null, playClock, null,
            homeFouls, awayFouls, balls, strikes, outs, "Daktronics RTD");
    }

    string ReadText(int position, int length)
    {
        var start = position - 1;
        if (start < 0 || start >= _buffer.Length) return "";
        return new string(_buffer, start, Math.Min(length, _buffer.Length - start)).Trim();
    }

    int? ReadInt(int position, int length)
    {
        var text = ReadText(position, length);
        return int.TryParse(text, out var value) ? value : null;
    }

    int? ReadClock(int position, int length)
    {
        var text = ReadText(position, length);
        if (text.Length == 0) return null;
        return ParseClock(text);
    }

    static int? ParseClock(string text)
    {
        text = text.Trim().TrimEnd('z', 'Z');
        if (text.Length == 0) return null;
        var colon = text.IndexOf(':');
        if (colon >= 0)
        {
            if (!int.TryParse(text[..colon].Trim(), out var minutes)) return null;
            var secondsText = text[(colon + 1)..].Trim();
            var dot = secondsText.IndexOf('.');
            if (dot >= 0) secondsText = secondsText[..dot];
            if (!int.TryParse(secondsText, out var seconds)) return null;
            return Math.Max(0, minutes * 60 + seconds);
        }
        if (text.Contains('.'))
        {
            // A tenths clock such as "24.0" is seconds, not minutes.
            var dot = text.IndexOf('.');
            if (!int.TryParse(text[..dot], out var whole)) return null;
            return Math.Max(0, whole);
        }
        return int.TryParse(text, out var value) ? Math.Max(0, value) : null;
    }

    static int? ParseDown(string text)
    {
        var digit = text.FirstOrDefault(char.IsDigit);
        return digit == default ? null : digit - '0';
    }

    static string NormalizeSport(string? sportId) => sportId?.ToLowerInvariant() switch
    {
        "football" or "baseball" or "basketball" or "volleyball" or "hockey" or "lacrosse" => sportId!,
        _ => "basketball"
    };

    static int BufferSize(string sportId) => sportId switch
    {
        "baseball" => 343,
        "hockey" or "lacrosse" => 493,
        "volleyball" => 262,
        _ => 295
    };
}
