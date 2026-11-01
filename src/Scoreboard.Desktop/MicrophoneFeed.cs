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
    MMDevice? Resolve(DataFlow flow, int index)
    {
        try
        {
            var devices = _enumerator!.EnumerateAudioEndPoints(flow, DeviceState.Active);
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
