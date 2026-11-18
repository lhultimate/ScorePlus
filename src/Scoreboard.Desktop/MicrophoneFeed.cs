using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Scoreboard.Desktop;

/// <summary>A capture device the operator can add to the media library, by its Windows name.</summary>
public sealed record MicrophoneDevice(int Index, string Name);

/// <summary>
/// A live microphone in the media library. Like a camera it has no file: the sound is captured from
/// the device and sent straight to the output the item is set to, so a commentary mic can feed the
/// broadcast mix or the arena speakers. Cueing another item releases the device, so two entries can
/// never hold the same microphone open.
/// </summary>
public sealed class MicrophoneFeed : IDisposable
{
    static readonly object Gate = new();
    static readonly Dictionary<int, MicrophoneFeed> Feeds = [];

    readonly int _index;
    // A device handle is only usable while its enumerator lives, so one stays open for the cue.
    MMDeviceEnumerator? _enumerator;
    WasapiCapture? _capture;
    WasapiOut? _output;
    BufferedWaveProvider? _buffer;
    string? _outputDeviceId;
    double _volume = 1;
    bool _running;
    // The input peak meter. A microphone is an input, so a loopback capture of it reads silence;
    // measuring the samples as they arrive is the only honest level for its own bar.
    readonly object _levelGate = new();
    float _levelHeld;
    double _level;
    DateTime _levelAt = DateTime.UtcNow;
    bool _floats = true;
    // A microphone's level has to be readable while it is only sitting in the multiview, not just
    // while it is being monitored: a camera's sound bar asks for one whether or not the mic is
    // cued, and a cued camera never opens its microphone. This silent capture measures the input
    // without sending it anywhere, and it is released when nothing has asked for a level lately.
    readonly object _meterGate = new();
    WasapiCapture? _meter;
    MMDeviceEnumerator? _meterEnumerator;
    bool _meterFloats = true;
    DateTime _meterReadAt = DateTime.UtcNow;
    Timer? _meterIdle;

    MicrophoneFeed(int index) => _index = index;

    /// <summary>One feed per capture device, shared by every window and every cue.</summary>
    public static MicrophoneFeed Get(int index)
    {
        lock (Gate)
        {
            index = Math.Max(0, index);
            if (!Feeds.TryGetValue(index, out var feed))
            {
                feed = new MicrophoneFeed(index);
                Feeds[index] = feed;
            }
            return feed;
        }
    }

    /// <summary>Every active microphone, in Windows' own order, with the name Windows gives it.</summary>
    public static IReadOnlyList<MicrophoneDevice> NamedDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select((device, index) => new MicrophoneDevice(index, device.FriendlyName))
                .ToArray();
        }
        catch { return []; }
    }

    /// <summary>The name of one microphone, falling back to its position when it cannot be read.</summary>
    public static string MicrophoneName(int index)
    {
        var devices = NamedDevices();
        return index >= 0 && index < devices.Count ? devices[index].Name : $"Microphone {index + 1}";
    }

    public int Index => _index;

    /// <summary>True while this microphone is being listened to and monitored.</summary>
    public bool IsRunning => _running;

    /// <summary>Why the last capture or monitor attempt failed, if it did.</summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Opens the microphone and starts sending it to <paramref name="outputDeviceId"/> (null is the
    /// Windows default output). The monitor runs through WASAPI with a small buffer, so the operator
    /// hears the room a fraction of a second behind the microphone, not a clip behind.
    /// </summary>
    public void Start(string? outputDeviceId)
    {
        if (_running && string.Equals(_outputDeviceId, outputDeviceId, StringComparison.OrdinalIgnoreCase)) return;
        Stop();
        Error = null;
        try
        {
            _enumerator = new MMDeviceEnumerator();
            var captureDevice = Resolve(DataFlow.Capture, _index);
            _capture = captureDevice is null ? new WasapiCapture() : new WasapiCapture(captureDevice);
            _floats = _capture.WaveFormat.BitsPerSample == 32;
            _buffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
            var outputDevice = Resolve(DataFlow.Render, outputDeviceId);
            _output = outputDevice is null
                ? new WasapiOut(AudioClientShareMode.Shared, true, 100)
                : new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 100);
            _output.Init(_buffer);
            _capture.DataAvailable += OnData;
            _capture.StartRecording();
            _output.Play();
            _outputDeviceId = outputDeviceId;
            _running = true;
            // The monitor now measures the same input, so a second capture on the device is waste.
            StopMeter();
        }
        catch (Exception ex)
        {
            Error = $"Microphone {_index + 1}: {ex.Message}";
            Stop();
        }
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        var peak = Peak(e.Buffer, e.BytesRecorded, _floats);
        lock (_levelGate) { if (peak > _levelHeld) _levelHeld = peak; }
        // The monitor level is applied to the captured samples, so the level the room hears is the
        // one set on the dock and nothing downstream has to know about it.
        var buffer = e.Buffer;
        if (_volume < 0.999)
        {
            buffer = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, buffer, e.BytesRecorded);
            Scale(buffer, e.BytesRecorded, (float)Math.Clamp(_volume, 0, 1));
        }
        try { _buffer?.AddSamples(buffer, 0, e.BytesRecorded); } catch { }
    }

    /// <summary>The loudest sample in a capture buffer, 0–1. WASAPI mix formats are 32-bit float.</summary>
    static float Peak(byte[] buffer, int bytes, bool floats)
    {
        var peak = 0f;
        if (floats)
        {
            for (var offset = 0; offset + 4 <= bytes; offset += 4)
            {
                var sample = Math.Abs(BitConverter.ToSingle(buffer, offset));
                if (sample > peak) peak = sample;
            }
        }
        else
        {
            for (var offset = 0; offset + 2 <= bytes; offset += 2)
            {
                var sample = Math.Abs(BitConverter.ToInt16(buffer, offset) / 32768f);
                if (sample > peak) peak = sample;
            }
        }
        return Math.Min(1, peak);
    }

    /// <summary>
    /// The microphone's own input level, 0–1, with the same fast rise and slow fall as the output
    /// meters. It measures the device even while the microphone is not cued or on air, because a
    /// camera's sound bar has to move before the mic is opened for monitoring.
    /// </summary>
    public double ReadLevel()
    {
        if (!_running) EnsureMeter();
        lock (_levelGate)
        {
            var now = DateTime.UtcNow;
            var seconds = Math.Clamp((now - _levelAt).TotalSeconds, 0, 1);
            _levelAt = now;
            // A microphone bar is read far more often than the output meters, and a slow fall made
            // it look a beat behind the room, so it releases faster.
            var decayed = _level * Math.Pow(0.10, seconds / 0.35);
            _level = Math.Max(Math.Min(1, _levelHeld), decayed);
            _levelHeld = 0;
            return _level < 0.002 ? 0 : _level;
        }
    }

    void OnMeterData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        var peak = Peak(e.Buffer, e.BytesRecorded, _meterFloats);
        lock (_levelGate) { if (peak > _levelHeld) _levelHeld = peak; }
    }

    /// <summary>Opens the level-only capture when the microphone is not being monitored already.</summary>
    void EnsureMeter()
    {
        var now = DateTime.UtcNow;
        lock (_meterGate)
        {
            _meterReadAt = now;
            if (_meter is not null) return;
            MMDeviceEnumerator? enumerator = null;
            WasapiCapture? capture = null;
            try
            {
                enumerator = new MMDeviceEnumerator();
                var device = Resolve(enumerator, DataFlow.Capture, _index);
                capture = device is null ? new WasapiCapture() : new WasapiCapture(device);
                _meterFloats = capture.WaveFormat.BitsPerSample == 32;
                capture.DataAvailable += OnMeterData;
                capture.StartRecording();
            }
            catch
            {
                // A device another program holds exclusively simply leaves the bar at zero.
                if (capture is not null)
                {
                    capture.DataAvailable -= OnMeterData;
                    try { capture.Dispose(); } catch { }
                }
                try { enumerator?.Dispose(); } catch { }
                return;
            }
            _meter = capture;
            _meterEnumerator = enumerator;
            // The wall can close while the meter is open, and nothing would then ask for a level
            // again. Five idle seconds is long enough to ride out a theme or tile rebuild.
            _meterIdle = new Timer(_ => ReleaseIdleMeter(), null, 5000, 2000);
        }
    }

    void ReleaseIdleMeter()
    {
        lock (_meterGate)
        {
            if ((DateTime.UtcNow - _meterReadAt).TotalSeconds < 5) return;
        }
        StopMeter();
    }

    /// <summary>Closes the level-only capture, leaving any monitoring untouched.</summary>
    void StopMeter()
    {
        WasapiCapture? capture;
        MMDeviceEnumerator? enumerator;
        Timer? idle;
        lock (_meterGate)
        {
            capture = _meter;
            enumerator = _meterEnumerator;
            idle = _meterIdle;
            _meter = null;
            _meterEnumerator = null;
            _meterIdle = null;
        }
        idle?.Dispose();
        if (capture is not null)
        {
            capture.DataAvailable -= OnMeterData;
            try { capture.StopRecording(); } catch { }
            try { capture.Dispose(); } catch { }
        }
        try { enumerator?.Dispose(); } catch { }
        lock (_levelGate) _levelHeld = 0;
    }

    /// <summary>Scales a capture buffer in place; WASAPI mix formats are 32-bit float, 16-bit is the fallback.</summary>
    void Scale(byte[] buffer, int bytes, float amount)
    {
        var format = _capture?.WaveFormat;
        if (format is { Encoding: WaveFormatEncoding.IeeeFloat, BitsPerSample: 32 })
        {
            var samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, bytes - bytes % 4));
            for (var index = 0; index < samples.Length; index++) samples[index] *= amount;
            return;
        }
        if (format is { BitsPerSample: 16 })
        {
            var samples = MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, bytes - bytes % 2));
            for (var index = 0; index < samples.Length; index++) samples[index] = (short)(samples[index] * amount);
        }
    }

    /// <summary>Changes where the microphone is heard, without releasing the capture device.</summary>
    public void SetOutput(string? outputDeviceId)
    {
        if (!_running)
        {
            _outputDeviceId = outputDeviceId;
            return;
        }
        Start(outputDeviceId);
    }

    public void SetVolume(double volume)
    {
        // The capture callback reads this on the next buffer, so a level change is immediate.
        _volume = Math.Clamp(volume, 0, 1);
    }

    public void Stop()
    {
        _running = false;
        lock (_levelGate) { _levelHeld = 0; _level = 0; _levelAt = DateTime.UtcNow; }
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnData;
            try { _capture.StopRecording(); } catch { }
            try { _capture.Dispose(); } catch { }
            _capture = null;
        }
        try { _output?.Stop(); } catch { }
        try { _output?.Dispose(); } catch { }
        _output = null;
        _buffer = null;
        try { _enumerator?.Dispose(); } catch { }
        _enumerator = null;
    }

    /// <summary>An active capture or render device by its position in Windows' order.</summary>
    MMDevice? Resolve(DataFlow flow, int index) => Resolve(_enumerator!, flow, index);

    static MMDevice? Resolve(MMDeviceEnumerator enumerator, DataFlow flow, int index)
    {
        try
        {
            var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
            return index >= 0 && index < devices.Count ? devices[index] : null;
        }
        catch { return null; }
    }

    /// <summary>A render device by the id or friendly name the items store.</summary>
    MMDevice? Resolve(DataFlow flow, string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;
        try
        {
            var devices = _enumerator!.EnumerateAudioEndPoints(flow, DeviceState.Active);
            var match = devices.FirstOrDefault(device => string.Equals(device.ID, deviceId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(device.FriendlyName, deviceId, StringComparison.OrdinalIgnoreCase));
            return match is null ? null : _enumerator.GetDevice(match.ID) ?? match;
        }
        catch { return null; }
    }

    public void Dispose() => Stop();
}
