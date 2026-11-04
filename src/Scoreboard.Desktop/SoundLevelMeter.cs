using NAudio.Wave;

namespace Scoreboard.Desktop;

/// <summary>
/// A peak meter for what this machine is actually sending to its speakers. It listens to the default
/// output with WASAPI loopback rather than to any one clip, so it reads a live camera mix, a replay
/// or a routed ad exactly as the room hears them, and it keeps reading while nothing plays — silence
/// is information too. One capture is shared by every window, since the machine has one output.
/// </summary>
public static class SoundLevelMeter
{
    static readonly object Gate = new();
    static WasapiLoopbackCapture? _capture;
    static bool _floats = true;
    static float _held;
    static double _level;
    static DateTime _last = DateTime.UtcNow;

    static SoundLevelMeter()
    {
        try
        {
            _capture = new WasapiLoopbackCapture();
            _floats = _capture.WaveFormat.BitsPerSample == 32;
            _capture.DataAvailable += OnData;
            _capture.StartRecording();
            Available = true;
        }
        catch
        {
            // No sound card, a busy device, an unusual service account: the meter simply reads zero.
            try { _capture?.Dispose(); } catch { }
            _capture = null;
            Available = false;
        }
    }

    /// <summary>False when the machine has no output to listen to; the meter then stays dark.</summary>
    public static bool Available { get; }

    /// <summary>
    /// Answers "is any of this show's audio actually on air?". The show installs it once, and it is
    /// asked on every read, so a speaker switched off silences the bar immediately instead of leaving
    /// it dancing to whatever else the machine happens to be playing. Null keeps the old behaviour.
    /// </summary>
    public static Func<bool>? AudibleGate { get; set; }

    /// <summary>True while the gate says nothing the show plays is reaching a speaker.</summary>
    public static bool Suppressed => AudibleGate?.Invoke() == false;

    static void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        var peak = 0f;
        if (_floats)
        {
            for (var offset = 0; offset + 4 <= e.BytesRecorded; offset += 4)
            {
                var sample = Math.Abs(BitConverter.ToSingle(e.Buffer, offset));
                if (sample > peak) peak = sample;
            }
        }
        else
        {
            for (var offset = 0; offset + 2 <= e.BytesRecorded; offset += 2)
            {
                var sample = Math.Abs(BitConverter.ToInt16(e.Buffer, offset) / 32768f);
                if (sample > peak) peak = sample;
            }
        }
        lock (Gate) { if (peak > _held) _held = peak; }
    }

    /// <summary>
    /// The level to draw, 0–1. It rises instantly to the loudest sample since the last read and falls
    /// with a slow, time-based decay, so the bar is readable instead of strobing, and true silence
    /// still settles at zero.
    /// </summary>
    public static double Read()
    {
        lock (Gate)
        {
            if (Suppressed)
            {
                _held = 0;
                _level = 0;
                _last = DateTime.UtcNow;
                return 0;
            }
            var now = DateTime.UtcNow;
            var seconds = Math.Clamp((now - _last).TotalSeconds, 0, 1);
            _last = now;
            var decayed = _level * Math.Pow(0.10, seconds / 0.55);
            _level = Math.Max(Math.Min(1, _held), decayed);
            _held = 0;
            if (_level < 0.002) _level = 0;
            return _level;
        }
    }

    /// <summary>The level in dBFS, which is what an operator reads: 0 dB is full scale.</summary>
    public static double Decibels(double level) => level <= 0 ? double.NegativeInfinity : 20 * Math.Log10(level);
}
