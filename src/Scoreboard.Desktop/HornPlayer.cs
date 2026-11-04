using System.IO;
using System.Windows;
using NAudio.Wave;

namespace Scoreboard.Desktop;

public enum HornSound
{
    AirHorn,
    Buzzer,
    Siren,
    Custom
}

/// <summary>
/// A small, disposable audio channel for the console horn. The three built-in sounds use bundled
/// recordings, with the local synthesizer retained as a fallback; a custom file is played through
/// NAudio's AudioFileReader.
/// </summary>
public static class HornPlayer
{
    const double TwoPi = Math.PI * 2;
    const int SampleRate = 44100;

    static readonly object Gate = new();
    /// <summary>Raised when a horn starts; useful to diagnostics and unattended checks.</summary>
    public static event Action<HornSound>? Started;

    static WaveOut? _output;
    static IWaveProvider? _provider;
    static IDisposable? _source;
    static Stream? _resourceStream;

    public static bool Play(string? sound, string? customPath)
    {
        var kind = Parse(sound);
        lock (Gate)
        {
            StopLocked();
            Started?.Invoke(kind);
            try
            {
                // Use the recorded built-in sounds where available. The synthesizer below stays as
                // a fallback for builds or hosts where the embedded assets cannot be loaded.
                if (kind is HornSound.AirHorn or HornSound.Buzzer or HornSound.Siren && TryPlayBundled(kind)) return true;

                if (kind == HornSound.Custom && !string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
                {
                    var reader = new AudioFileReader(customPath);
                    var output = new WaveOut { Volume = 0.9f };
                    output.Init(reader);
                    _provider = reader;
                    _source = reader;
                    _output = output;
                    output.PlaybackStopped += (_, _) => Finished(output);
                    output.Play();
                }
                else
                {
                    var wave = new RawSourceWaveStream(new MemoryStream(Generate(kind)), new WaveFormat(SampleRate, 16, 1));
                    // The built-ins are intentionally a little quieter than a custom file: they are
                    // generated tones rather than a mixed show asset, and shouldn't clip the mix.
                    var output = new WaveOut { Volume = 0.72f };
                    output.Init(wave);
                    _provider = wave;
                    _source = wave;
                    _output = output;
                    output.PlaybackStopped += (_, _) => Finished(output);
                    output.Play();
                }
                return true;
            }
            catch
            {
                StopLocked();
                return false;
            }
        }
    }

    public static void Stop()
    {
        lock (Gate) StopLocked();
    }

    static void Finished(WaveOut finished)
    {
        // A previous sound can finish after a new one started. Only the current output may clean up.
        lock (Gate)
        {
            if (ReferenceEquals(_output, finished)) StopLocked();
        }
    }

    static void StopLocked()
    {
        try { _output?.Stop(); } catch { }
        try { _output?.Dispose(); } catch { }
        try { _source?.Dispose(); } catch { }
        try { _resourceStream?.Dispose(); } catch { }
        _output = null;
        _source = null;
        _provider = null;
        _resourceStream = null;
    }

    static bool TryPlayBundled(HornSound kind)
    {
        var file = kind switch
        {
            HornSound.AirHorn => "horn-air.mp3",
            HornSound.Buzzer => "horn-buzzer.mp3",
            HornSound.Siren => "horn-siren.mp3",
            _ => null
        };
        if (file is null) return false;
        var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/ScorePlus;component/Assets/{file}"))
            ?? Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{file}"));
        if (resource is null) return false;
        var stream = resource.Stream;
        AudioFileReader? reader = null;
        try
        {
            reader = new AudioFileReader(stream);
            var output = new WaveOut { Volume = 0.70f };
            output.Init(reader);
            _provider = reader;
            _source = reader;
            _resourceStream = stream;
            _output = output;
            output.PlaybackStopped += (_, _) => Finished(output);
            output.Play();
            return true;
        }
        catch
        {
            try { reader?.Dispose(); } catch { }
            try { stream.Dispose(); } catch { }
            return false;
        }
    }

    public static HornSound Parse(string? value) => Enum.TryParse<HornSound>(value, true, out var result)
        ? result
        : HornSound.AirHorn;

    static byte[] Generate(HornSound kind)
    {
        var seconds = kind switch
        {
            HornSound.Buzzer => 0.92,
            HornSound.Siren => 2.25,
            _ => 1.35
        };
        var attack = kind switch
        {
            HornSound.Buzzer => 0.012,
            HornSound.Siren => 0.12,
            _ => 0.045
        };
        var release = kind switch
        {
            HornSound.Buzzer => 0.14,
            HornSound.Siren => 0.32,
            _ => 0.20
        };
        var samples = (int)(SampleRate * seconds);
        var data = new byte[samples * 2];
        double phase1 = 0, phase2 = 0, phase3 = 0, phase4 = 0;

        for (var index = 0; index < samples; index++)
        {
            var time = (double)index / SampleRate;
            var remaining = seconds - time;
            var envelope = Smooth(Math.Min(1, time / attack)) * Smooth(Math.Min(1, remaining / release));
            double sample;

            switch (kind)
            {
                case HornSound.Buzzer:
                {
                    // A warm, low horn with a little tremolo. A rounded harmonic stack replaces the
                    // old square wave, which was the harshest part of this sound.
                    const double frequency = 196;
                    phase1 += TwoPi * frequency / SampleRate;
                    phase2 += TwoPi * frequency * 2 / SampleRate;
                    phase3 += TwoPi * frequency * 3 / SampleRate;
                    phase4 += TwoPi * frequency * 4 / SampleRate;
                    sample = Math.Sin(phase1) * 0.34
                        + Math.Sin(phase2) * 0.14
                        + Math.Sin(phase3) * 0.065
                        + Math.Sin(phase4) * 0.025;
                    sample *= 0.92 + 0.08 * Math.Sin(TwoPi * 12 * time);
                    break;
                }
                case HornSound.Siren:
                {
                    // A slow, smooth wail instead of a bright two-tone alarm.
                    var sweep = 500 + 175 * Math.Sin(TwoPi * 0.48 * time - Math.PI / 2);
                    phase1 += TwoPi * sweep / SampleRate;
                    phase2 += TwoPi * sweep * 2 / SampleRate;
                    phase3 += TwoPi * sweep * 3 / SampleRate;
                    sample = Math.Sin(phase1) * 0.34
                        + Math.Sin(phase2) * 0.085
                        + Math.Sin(phase3) * 0.025;
                    break;
                }
                default:
                {
                    // A slightly detuned harmonic stack reads as an air horn without the hard
                    // edges of a square wave or an overdriven saw.
                    var fundamental = 220 * (1 + 0.003 * Math.Sin(TwoPi * 5.1 * time));
                    phase1 += TwoPi * fundamental / SampleRate;
                    phase2 += TwoPi * fundamental * 1.007 / SampleRate;
                    phase3 += TwoPi * fundamental * 2 / SampleRate;
                    phase4 += TwoPi * fundamental * 3 / SampleRate;
                    sample = Math.Sin(phase1) * 0.31
                        + Math.Sin(phase2) * 0.20
                        + Math.Sin(phase3) * 0.10
                        + Math.Sin(phase4) * 0.045;
                    break;
                }
            }

            // A gentle soft clip adds body without introducing the crackle of hard clipping.
            sample = Math.Tanh(sample * 1.12);
            sample *= envelope;
            var value = (short)(Math.Clamp(sample, -1, 1) * short.MaxValue * 0.78);
            data[index * 2] = (byte)(value & 0xFF);
            data[index * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }
        return data;
    }

    static double Smooth(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - 2 * value);
    }
}
