// ScorePlus Linux port — dirty WPF compatibility shims.
//
// TECH DEBT: these exist only so the ported code compiles while the real Avalonia/ffmpeg/pulse
// replacements land. Media/audio/camera paths remain compatibility stubs; animation calls now use
// a real Avalonia dispatcher clock so transitions and completion callbacks are time-based. Grep
// "WpfCompat" to find every shim site.
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace Scoreboard.Desktop;

// ---------- WPF enums / small value types ----------

public enum Visibility { Visible, Hidden, Collapsed }

public enum MediaState { Manual, Pause, Stop, Close }

public enum PixelFormats { Pbgra32, Bgra32, Bgr24 }

public sealed class BitmapCacheOption
{
    public static readonly BitmapCacheOption OnLoad = new();
    public static readonly BitmapCacheOption None = new();
}

// ---------- images / thumbnails ----------

public class BitmapSource : Avalonia.Media.IImage
{
    public int PixelWidth { get; init; } = 1;
    public int PixelHeight { get; init; } = 1;
    public Avalonia.Size Size => new(PixelWidth, PixelHeight);

    public static BitmapSource Create(int width, int height, double dpiX, double dpiY,
        PixelFormats format, byte[]? palette, IntPtr pixels, int count, int stride)
        => new() { PixelWidth = Math.Max(1, width), PixelHeight = Math.Max(1, height) };

    public void CopyPixels(byte[] buffer, int stride, int offset) { } // stub: flat black
    public void Freeze() { }
    public bool IsFrozen => true;

    // Dirty port shim: bitmap data is intentionally not decoded or rendered yet.
    void Avalonia.Media.IImage.Draw(Avalonia.Media.DrawingContext context,
        Avalonia.Rect sourceRect, Avalonia.Rect destRect) { }
}

public sealed class BitmapImage : BitmapSource
{
    public BitmapCacheOption CacheOption { get; set; } = BitmapCacheOption.OnLoad;
    public Uri? UriSource { get; set; }
    // Dirty: the decoded stream is dropped; frames come back as a 1x1 placeholder until the
    // ffmpeg/imaging pass lands. TODO(port): Avalonia Bitmap over the given stream.
    public System.IO.Stream? StreamSource { get; set; }
    public void BeginInit() { }
    public void EndInit() { }
}

public sealed class RenderTargetBitmap : BitmapSource
{
    public RenderTargetBitmap(int width, int height, double dpiX, double dpiY, PixelFormats format)
    {
        PixelWidth = Math.Max(1, width);
        PixelHeight = Math.Max(1, height);
    }
    public void Render(object visual) { }
}

// Cropping preserves the requested placeholder dimensions, but does not create pixel data.
public sealed class CroppedBitmap : BitmapSource
{
    public CroppedBitmap(Avalonia.Media.IImage source)
    {
        PixelWidth = Math.Max(1, (int)Math.Round(source.Size.Width));
        PixelHeight = Math.Max(1, (int)Math.Round(source.Size.Height));
    }

    public CroppedBitmap(Avalonia.Media.IImage source, Int32Rect sourceRect)
    {
        PixelWidth = Math.Max(1, sourceRect.Width);
        PixelHeight = Math.Max(1, sourceRect.Height);
    }
}

public sealed class DrawingVisual
{
    public DrawingContext RenderOpen() => new();
}

public sealed class DrawingContext : IDisposable
{
    public void DrawVideo(MediaPlayer player, Avalonia.Rect rect) { } // stub: no frame source on Linux yet
    public void DrawRectangle(Brush? fill, Pen? pen, Avalonia.Rect rect) { }
    public void Dispose() { }
}

// ---------- media: MediaPlayer / MediaElement (real playback lands with the ffmpeg player) ----------

public sealed class MediaFailedEventArgsStub : EventArgs
{
    public Exception? ErrorException { get; init; }
}

public struct DurationLike
{
    public bool HasTimeSpan { get; }
    public TimeSpan TimeSpan { get; }
    public DurationLike(TimeSpan value) { HasTimeSpan = true; TimeSpan = value; }
    public static readonly DurationLike Automatic = default;
}

public class MediaPlayer
{
    public bool ScrubbingEnabled { get; set; }
    public double Volume { get; set; } = 1;
    public TimeSpan Position { get; set; }
    public DurationLike NaturalDuration { get; private set; }
    public Uri? Source { get; set; }

    public event EventHandler MediaOpened = delegate { };
    public event EventHandler MediaEnded = delegate { };
    public event EventHandler<MediaFailedEventArgsStub> MediaFailed = delegate { };

    public void Open(Uri uri)
    {
        Source = uri;
        Position = TimeSpan.Zero;
        NaturalDuration = new DurationLike(TimeSpan.Zero);
        // TODO(ffmpeg): real duration/playback arrives with the LinuxVideoPlayer pass.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => MediaOpened(this, EventArgs.Empty));
    }
    public void Close() { Source = null; }
    public void Play() { }
    public void Pause() { }
    public void Stop() { Position = TimeSpan.Zero; }
}

public sealed class MediaElement : Avalonia.Controls.Control
{
    public MediaState LoadedBehavior { get; set; }
    public MediaState UnloadedBehavior { get; set; }
    public Stretch Stretch { get; set; }
    public bool IsMuted { get; set; }
    public double Volume { get; set; } = 1;
    public bool ScrubbingEnabled { get; set; }
    public Uri? Source { get; set; }
    public Visibility Visibility { get; set; } = Visibility.Visible;
    public TimeSpan Position { get; set; }
    public DurationLike NaturalDuration { get; private set; }

    public event EventHandler MediaOpened = delegate { };
    public event EventHandler MediaEnded = delegate { };
    public event EventHandler<MediaFailedEventArgsStub> MediaFailed = delegate { };

    public void Play() { }
    public void Pause() { }
    public void Stop() { Position = TimeSpan.Zero; }
    public void Close() { Source = null; }

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);
}

internal static class MediaShimExt
{
    public static void MediaFailedHandlerHack() { }
}

// ---------- WPF-flavoured animation surface (snaps to end value, fires Completed immediately) ----------

public enum FillBehavior { HoldEnd, Stop }
public enum EasingMode { EaseIn, EaseOut, EaseInOut }

public interface IEasingFunction { }

public sealed class QuadraticEase : IEasingFunction { public EasingMode EasingMode { get; set; } }
public sealed class CubicEase : IEasingFunction { public EasingMode EasingMode { get; set; } }
public sealed class QuarticEase : IEasingFunction { public EasingMode EasingMode { get; set; } }
public sealed class SineEase : IEasingFunction { public EasingMode EasingMode { get; set; } }
public sealed class BackEase : IEasingFunction { public EasingMode EasingMode { get; set; } public double Amplitude { get; set; } }
public sealed class BounceEase : IEasingFunction { public EasingMode EasingMode { get; set; } public int Bounces { get; set; } public double Bounciness { get; set; } }
public sealed class ElasticEase : IEasingFunction { public EasingMode EasingMode { get; set; } public int Oscillations { get; set; } public double Springiness { get; set; } }

public struct RepeatBehavior
{
    public double CycleCount { get; }
    public RepeatBehavior(double cycles) { CycleCount = cycles; }
    public static readonly RepeatBehavior Forever = new(double.PositiveInfinity);
}

public struct KeyTime
{
    public TimeSpan TimeSpan { get; }
    public double Fraction { get; }
    public bool IsPercent { get; }
    KeyTime(TimeSpan span, double fraction, bool isPercent) { TimeSpan = span; Fraction = fraction; IsPercent = isPercent; }
    public static KeyTime FromTimeSpan(TimeSpan value) => new(value, 0, false);
    public static KeyTime FromPercent(double percent) => new(TimeSpan.Zero, percent, true);
}

public abstract class WpfAnimation
{
    public FillBehavior FillBehavior { get; set; } = FillBehavior.HoldEnd;
    public RepeatBehavior RepeatBehavior { get; set; } = new(1);
    public bool AutoReverse { get; set; }
    public event EventHandler? Completed;
    internal void RaiseCompleted() => Completed?.Invoke(this, EventArgs.Empty);
    internal abstract bool TryFinal(out object value);
    internal abstract object ValueAt(double progress);
    public abstract TimeSpan Duration { get; set; }
}

public sealed class DoubleAnimation : WpfAnimation
{
    public double From { get; }
    public double To { get; }
    public override TimeSpan Duration { get; set; }
    public IEasingFunction? EasingFunction { get; set; }
    public DoubleAnimation(double from, double to, TimeSpan duration)
    { From = from; To = to; Duration = duration; }
    public DoubleAnimation(object fromValue, object toValue, TimeSpan duration)
    { From = Convert.ToDouble(fromValue); To = Convert.ToDouble(toValue); Duration = duration; }
    internal override bool TryFinal(out object value) { value = To; return true; }
    internal override object ValueAt(double progress)
    {
        var eased = WpfAnimationMath.Ease(progress, EasingFunction);
        return From + (To - From) * eased;
    }
}

public sealed class LinearDoubleKeyFrame
{
    public double Value { get; }
    public KeyTime KeyTime { get; }
    public LinearDoubleKeyFrame(double value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public LinearDoubleKeyFrame(double value, KeyTime keyTime, IEasingFunction ease) { Value = value; KeyTime = keyTime; }
}

public sealed class EasingDoubleKeyFrame
{
    public double Value { get; }
    public KeyTime KeyTime { get; }
    public IEasingFunction? EasingFunction { get; set; }
    public EasingDoubleKeyFrame(double value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public EasingDoubleKeyFrame(double value, KeyTime keyTime, IEasingFunction ease) { Value = value; KeyTime = keyTime; EasingFunction = ease; }
}

public sealed class LinearColorKeyFrame
{
    public Color Value { get; }
    public KeyTime KeyTime { get; }
    public LinearColorKeyFrame(Color value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
}

public sealed class EasingColorKeyFrame
{
    public Color Value { get; }
    public KeyTime KeyTime { get; }
    public IEasingFunction? EasingFunction { get; set; }
    public EasingColorKeyFrame(Color value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
}

public sealed class DoubleAnimationUsingKeyFrames : WpfAnimation
{
    public List<object> KeyFrames { get; } = [];
    public override TimeSpan Duration { get; set; }
    internal override bool TryFinal(out object value) { value = ValueAt(1); return true; }
    internal override object ValueAt(double progress) => WpfAnimationMath.DoubleAt(KeyFrames, Duration, progress);
}

public sealed class ColorAnimationUsingKeyFrames : WpfAnimation
{
    public List<object> KeyFrames { get; } = [];
    public override TimeSpan Duration { get; set; }
    internal override bool TryFinal(out object value) { value = ValueAt(1); return true; }
    internal override object ValueAt(double progress) => WpfAnimationMath.ColorAt(KeyFrames, Duration, progress);
}

internal static class WpfAnimationMath
{
    public static double Ease(double value, IEasingFunction? function)
    {
        value = Math.Clamp(value, 0, 1);
        if (function is null) return value;
        double In(double p) => function switch
        {
            QuadraticEase => p * p,
            CubicEase => p * p * p,
            QuarticEase => p * p * p * p,
            SineEase => 1 - Math.Cos(p * Math.PI / 2),
            BackEase back => p * p * ((back.Amplitude + 1) * p - back.Amplitude),
            _ => p
        };
        var mode = function switch
        {
            QuadraticEase q => q.EasingMode, CubicEase c => c.EasingMode, QuarticEase q => q.EasingMode,
            SineEase s => s.EasingMode, BackEase b => b.EasingMode, BounceEase b => b.EasingMode,
            ElasticEase e => e.EasingMode, _ => EasingMode.EaseInOut
        };
        return mode switch
        {
            EasingMode.EaseIn => In(value), EasingMode.EaseOut => 1 - In(1 - value),
            _ => value < 0.5 ? In(value * 2) / 2 : 1 - In((1 - value) * 2) / 2
        };
    }

    static double Time(KeyTime key, TimeSpan duration) => key.IsPercent ? key.Fraction : duration.TotalSeconds <= 0 ? 1 : key.TimeSpan.TotalSeconds / duration.TotalSeconds;

    public static double DoubleAt(List<object> frames, TimeSpan duration, double progress)
    {
        var values = frames.Select(frame => frame switch
        {
            LinearDoubleKeyFrame linear => (linear.Value, linear.KeyTime, (IEasingFunction?)null),
            EasingDoubleKeyFrame easing => (easing.Value, easing.KeyTime, easing.EasingFunction),
            _ => (1d, KeyTime.FromPercent(1), (IEasingFunction?)null)
        }).ToList();
        if (values.Count == 0) return 1;
        progress = Math.Clamp(progress, 0, 1);
        var at = values.Select(value => (value.Item1, Time: Time(value.Item2, duration), value.Item3)).ToList();
        if (progress <= at[0].Time) return at[0].Item1;
        for (var i = 1; i < at.Count; i++)
        {
            if (progress > at[i].Time) continue;
            var span = Math.Max(0.000001, at[i].Time - at[i - 1].Time);
            var local = Ease((progress - at[i - 1].Time) / span, at[i].Item3);
            return at[i - 1].Item1 + (at[i].Item1 - at[i - 1].Item1) * local;
        }
        return at[^1].Item1;
    }

    public static Color ColorAt(List<object> frames, TimeSpan duration, double progress)
    {
        var values = frames.Select(frame => frame switch
        {
            LinearColorKeyFrame linear => (linear.Value, linear.KeyTime, (IEasingFunction?)null),
            EasingColorKeyFrame easing => (easing.Value, easing.KeyTime, easing.EasingFunction),
            _ => (Colors.White, KeyTime.FromPercent(1), (IEasingFunction?)null)
        }).ToList();
        if (values.Count == 0) return Colors.White;
        progress = Math.Clamp(progress, 0, 1);
        var at = values.Select(value => (value.Item1, Time: Time(value.Item2, duration), value.Item3)).ToList();
        if (progress <= at[0].Time) return at[0].Item1;
        for (var i = 1; i < at.Count; i++)
        {
            if (progress > at[i].Time) continue;
            var span = Math.Max(0.000001, at[i].Time - at[i - 1].Time);
            var local = Ease((progress - at[i - 1].Time) / span, at[i].Item3);
            var a = at[i - 1].Item1; var b = at[i].Item1;
            return Color.FromArgb((byte)Math.Round(a.A + (b.A - a.A) * local), (byte)Math.Round(a.R + (b.R - a.R) * local), (byte)Math.Round(a.G + (b.G - a.G) * local), (byte)Math.Round(a.B + (b.B - a.B) * local));
        }
        return at[^1].Item1;
    }
}

/// <summary>Runs the existing WPF-shaped animation calls with a small Avalonia dispatcher clock.</summary>
public static class WpfAnimationExtensions
{
    sealed class Running
    {
        public required AvaloniaObject Target;
        public required AvaloniaProperty Property;
        public required WpfAnimation Animation;
        public long Started;
    }

    static readonly List<Running> Active = [];
    static DispatcherTimer? _timer;

    public static void BeginAnimation(this AvaloniaObject target, AvaloniaProperty property, WpfAnimation? animation)
    {
        Active.RemoveAll(run => ReferenceEquals(run.Target, target) && run.Property == property);
        if (animation is null) { StopClockIfIdle(); return; }
        var run = new Running { Target = target, Property = property, Animation = animation, Started = Stopwatch.GetTimestamp() };
        Active.Add(run);
        Apply(run, 0);
        EnsureClock();
    }

    static void EnsureClock()
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    static void StopClockIfIdle()
    {
        if (Active.Count != 0) return;
        _timer?.Stop();
        _timer = null;
    }

    static void Tick()
    {
        var now = Stopwatch.GetTimestamp();
        for (var i = Active.Count - 1; i >= 0; i--)
        {
            var run = Active[i];
            var duration = Math.Max(0.001, run.Animation.Duration.TotalSeconds);
            var elapsed = (now - run.Started) / (double)Stopwatch.Frequency;
            var cycleLength = duration * (run.Animation.AutoReverse ? 2 : 1);
            var finite = !double.IsPositiveInfinity(run.Animation.RepeatBehavior.CycleCount);
            var total = cycleLength * Math.Max(1, run.Animation.RepeatBehavior.CycleCount);
            if (finite && elapsed >= total)
            {
                Apply(run, run.Animation.AutoReverse && ((long)Math.Ceiling(run.Animation.RepeatBehavior.CycleCount) & 1) != 0 ? 0 : 1);
                Active.RemoveAt(i);
                run.Animation.RaiseCompleted();
                continue;
            }
            var within = elapsed % cycleLength;
            var progress = within / duration;
            Apply(run, progress > 1 ? 2 - progress : progress);
        }
        StopClockIfIdle();
    }

    static void Apply(Running run, double progress)
    {
        try { run.Target.SetValue(run.Property, run.Animation.ValueAt(progress)); } catch { }
    }
}


/// <summary>WPF's Slider value-change args; Avalonia handlers adapt to these at the wire sites.</summary>
public sealed class RoutedPropertyChangedEventArgs<T> : EventArgs
{
    public T OldValue { get; init; }
    public T NewValue { get; init; }
}

// ---------- NAudio stand-ins (WASAPI does not exist on Linux; real path = pw-play/ffmpeg) ----------
// TECH DEBT: every method below is inert. Audio routing, mic monitor, and the output meter report
// "available" but produce no sound. TODO(port): replace with PipeWire/ffmpeg players.

public enum DataFlow { Render, Capture }
public enum DeviceState { Active = 1, Uninitialized = 2, Disabled = 4, NotPresent = 8 }
public enum Role { Console, Multimedia, Communications }
public enum AudioClientShareMode { Shared, Exclusive }
public enum WaveFormatEncoding { Pcm = 1, IeeeFloat = 3, Extensible = 0xFFFE }

public sealed class WaveFormat
{
    public int BitsPerSample { get; } = 32;
    public WaveFormatEncoding Encoding { get; } = WaveFormatEncoding.IeeeFloat;
    public int SampleRate { get; } = 48000;
    public int Channels { get; } = 2;
}

public sealed class WaveInEventArgs : EventArgs
{
    public byte[] Buffer { get; init; } = [];
    public int BytesRecorded { get; init; }
}

public interface IWavePlayer
{
    void Init(object audioSource);
    void Play();
    void Pause();
    void Stop();
    void Dispose();
}

public sealed class MMDevice : IDisposable
{
    public string ID { get; init; } = "stub-device";
    public string FriendlyName { get; init; } = "Stub output";
    public void Dispose() { }
}

public sealed class MMDeviceEnumerator : IDisposable
{
    public List<MMDevice> EnumerateAudioEndPoints(DataFlow flow, DeviceState states) => [];
    public MMDevice? GetDefaultAudioEndpoint(DataFlow flow, Role role) => null; // stub: no devices
    public MMDevice? GetDevice(string id) => null;
    public void Dispose() { }
}

public sealed class AudioFileReader : IDisposable
{
    public float Volume { get; set; } = 1;
    public TimeSpan TotalTime => TimeSpan.Zero;
    public TimeSpan CurrentTime { get; set; }
    public AudioFileReader(string path) { }
    public void Dispose() { }
}

public sealed class BufferedWaveProvider
{
    public bool DiscardOnBufferOverflow { get; set; }
    public bool ReadFully { get; set; }
    public BufferedWaveProvider(WaveFormat format) { }
    public void AddSamples(byte[] buffer, int offset, int count) { }
}

public abstract class WasapiDeviceStub : IDisposable
{
    public WaveFormat WaveFormat { get; } = new();
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public void StartRecording() { }
    public void StopRecording() { }
    public void Dispose() { }
    protected void RaiseData(WaveInEventArgs e) => DataAvailable?.Invoke(this, e);
}

public sealed class WasapiCapture : WasapiDeviceStub
{
    public WasapiCapture() { }
    public WasapiCapture(MMDevice device) { }
}

public sealed class WasapiLoopbackCapture : WasapiDeviceStub
{
    public WasapiLoopbackCapture() { }
}

public sealed class WasapiOut : IWavePlayer
{
    public WasapiOut(AudioClientShareMode shareMode, bool useEventSync, int latency) { }
    public WasapiOut(MMDevice device, AudioClientShareMode shareMode, bool useEventSync, int latency) { }
    public void Init(object audioSource) { }
    public void Play() { }
    public void Pause() { }
    public void Stop() { }
    public void Dispose() { }
}

// ---------- OpenCV stand-ins (OpenCvSharp4 was dropped; real path = ffmpeg/v4l2) ----------
// TECH DEBT: cameras never open (IsOpened false) so live feed and replay stay unavailable.
// TODO(port): ffmpeg-based LinuxCameraFeed.

public enum VideoCaptureAPIs { ANY, V4L2, DSHOW }
public enum VideoCaptureProperties { PosFrames = 1, FrameWidth = 3, FrameHeight = 4 }

public sealed class FourCC
{
    public static readonly FourCC MJPG = new();
}

public sealed class Mat : IDisposable
{
    public int Width => 0;
    public int Height => 0;
    public long Step() => 0;
    public IntPtr Data => IntPtr.Zero;
    public bool Empty() => true;
    public void Dispose() { }
}

public sealed class VideoCapture : IDisposable
{
    public VideoCapture(string path) { }
    public VideoCapture(int index) { }
    public VideoCapture(int index, VideoCaptureAPIs api) { }
    public bool IsOpened() => false; // stub: no capture backend wired yet
    public void Set(VideoCaptureProperties property, double value) { }
    public bool Read(Mat frame) => false;
    public void Release() { }
    public void Dispose() { }
}

public sealed class VideoWriter : IDisposable
{
    public VideoWriter(string path, FourCC fourCC, double fps, Avalonia.Size frameSize) { }
    public bool IsOpened() => false;
    public void Write(Mat frame) { }
    public void Release() { }
    public void Dispose() { }
}
