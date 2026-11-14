using System.IO;

namespace Scoreboard.Desktop;

/// <summary>
/// Sends a clip's sound to a chosen speaker. WPF's MediaElement can only use the Windows default
/// output, so routing to a second sound card is done here with WASAPI: the clip's MediaElement is
/// muted and the same file is played through this router on the device the operator picked.
/// A clip left on "Default output" keeps using the MediaElement, so nothing changes for the
/// common case and formats WASAPI cannot decode still play normally.
/// </summary>
public sealed class AudioRouter : IDisposable
{
    public sealed record OutputDevice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    IWavePlayer? _output;
    AudioFileReader? _reader;
    float _volume = 1;

    /// <summary>Every active playback device, in Windows' own order.</summary>
    public static IReadOnlyList<OutputDevice> Devices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(device => new OutputDevice(device.ID, device.FriendlyName))
                .ToArray();
        }
        catch { return []; }
    }

    public static string? DefaultDeviceName()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).FriendlyName;
        }
        catch { return null; }
    }

    public bool IsPlaying { get; private set; }

    /// <summary>Why the last routed clip could not be opened, if it could not.</summary>
    public string? LastError { get; private set; }

    public void Play(string path, string? deviceId, double volume, double startSeconds = 0)
    {
        Stop();
        LastError = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        MMDeviceEnumerator? enumerator = null;
        try
        {
            var reader = new AudioFileReader(path) { Volume = _volume = (float)Math.Clamp(volume, 0, 1) };
            if (startSeconds > 0.05)
            {
                var target = TimeSpan.FromSeconds(startSeconds);
                if (target < reader.TotalTime) reader.CurrentTime = target;
            }
            enumerator = new MMDeviceEnumerator();
            var device = Resolve(enumerator, deviceId);
            var output = device is null
                ? new WasapiOut(AudioClientShareMode.Shared, true, 100)
                : new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
            output.Init(reader);
            output.Play();
            _reader = reader;
            _output = output;
            IsPlaying = true;
        }
        catch (Exception ex)
        {
            // A codec WASAPI cannot read, or a device that has gone away, leaves this clip silent.
            LastError = $"{Path.GetFileName(path)} on {(deviceId ?? "the default speaker")}: {ex.Message}";
            Stop();
        }
        finally
        {
            try { enumerator?.Dispose(); } catch { }
        }
    }

    static MMDevice? Resolve(MMDeviceEnumerator enumerator, string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;
        try
        {
            var match = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .FirstOrDefault(d => string.Equals(d.ID, deviceId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(d.FriendlyName, deviceId, StringComparison.OrdinalIgnoreCase));
            if (match is null) return null;
            // Ask the enumerator for a fresh handle on the same endpoint; a device object borrowed
            // from a disposed collection is not reliable for opening a stream.
            try { return enumerator.GetDevice(match.ID) ?? match; } catch { return match; }
        }
        catch { return null; }
    }

    public void SetVolume(double volume)
    {
        _volume = (float)Math.Clamp(volume, 0, 1);
        if (_reader is not null) _reader.Volume = _volume;
    }

    public void Pause()
    {
        try { _output?.Pause(); } catch { }
        IsPlaying = false;
    }

    public void Resume()
    {
        try { _output?.Play(); } catch { }
        IsPlaying = _output is not null;
    }

    public void Seek(double seconds)
    {
        if (_reader is null) return;
        try
        {
            var target = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (target < _reader.TotalTime) _reader.CurrentTime = target;
        }
        catch { }
    }

    public double Position
    {
        get
        {
            try { return _reader?.CurrentTime.TotalSeconds ?? 0; } catch { return 0; }
        }
    }

    public void Stop()
    {
        IsPlaying = false;
        try { _output?.Stop(); } catch { }
        try { _output?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        _output = null;
        _reader = null;
    }

    public void Dispose() => Stop();
}
