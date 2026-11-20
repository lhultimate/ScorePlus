using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Scoreboard.Desktop;

/// <summary>The shape a live source shares with a video element: frames, a newest picture and a start.</summary>
public interface ILiveSource
{
    event Action<BitmapSource>? FrameReady;
    BitmapSource? LastFrame { get; }
    void Start();
}

/// <summary>One monitor windows draws on, as a source a video element can show.</summary>
public sealed record ScreenDevice(int Index, string Name, int X, int Y, int Width, int Height)
{
    public string Label => $"{Name}   ·   Screen {Index + 1}";
}

/// <summary>
/// A desktop as a live media source. ScorePlus captures the chosen monitor straight out of GDI, so
/// a slide deck, a browser, a stat package or another program can be embedded in a scorebug video
/// box exactly like a camera: it routes by tag, it can be put on air by a macro or a conditional,
/// and it is renamed like any other library item. It is video only — a screen has no sound of its
/// own, so a capture card's audio still comes from a microphone item.
/// </summary>
public sealed class ScreenFeed : ILiveSource, IDisposable
{
    const double FramesPerSecond = 12;
    static readonly Dictionary<int, ScreenFeed> Feeds = [];

    readonly int _index;
    readonly Dispatcher _dispatcher;
    Thread? _thread;
    volatile bool _wanted;
    bool _framePending;
    string? _error;

    ScreenFeed(int index, Dispatcher dispatcher)
    {
        _index = index;
        _dispatcher = dispatcher;
    }

    public static ScreenFeed Get(int index)
    {
        var key = Math.Max(0, index);
        if (Feeds.TryGetValue(key, out var existing)) return existing;
        var feed = new ScreenFeed(key, Dispatcher.CurrentDispatcher);
        Feeds[key] = feed;
        return feed;
    }

    /// <summary>Raised on the UI dispatcher with each captured desktop picture, already frozen.</summary>
    public event Action<BitmapSource>? FrameReady;

    public BitmapSource? LastFrame { get; private set; }

    public string? Error => _error;

    public bool IsLive => _wanted;

    /// <summary>Where the scorebug's video element gets its picture, by screen number.</summary>
    public static ScreenFeed For(MediaEntry entry) => Get(entry.ScreenIndex);

    public void Start()
    {
        if (_wanted) return;
        if (Screens().Count == 0) { _error = "Windows reported no display to capture."; return; }
        _wanted = true;
        _error = null;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = $"Screen {_index + 1}" };
        _thread.Start();
    }

    public void Stop()
    {
        _wanted = false;
        var thread = _thread;
        _thread = null;
        try { thread?.Join(1000); } catch { }
    }

    void CaptureLoop()
    {
        var interval = (int)(1000 / FramesPerSecond);
        while (_wanted)
        {
            var started = Environment.TickCount64;
            var device = Screens().FirstOrDefault(screen => screen.Index == _index);
            if (device is null)
            {
                _error = $"Screen {_index + 1} is not connected.";
                break;
            }
            if (Capture(device) is { } frame) Publish(frame);
            var spent = (int)(Environment.TickCount64 - started);
            var wait = interval - spent;
            if (wait > 0) Thread.Sleep(wait);
        }
        _wanted = false;
    }

    void Publish(BitmapSource frame)
    {
        if (_framePending) return;
        _framePending = true;
        _dispatcher.BeginInvoke(() =>
        {
            _framePending = false;
            LastFrame = frame;
            try { FrameReady?.Invoke(frame); } catch { }
        });
    }

    /// <summary>
    /// One picture of a monitor. BitBlt with SRCCOPY reads the composited desktop, so hardware
    /// accelerated windows and layered windows come out the way they look on the screen, and no
    /// extra package is needed for it.
    /// </summary>
    BitmapSource? Capture(ScreenDevice device)
    {
        if (device.Width <= 0 || device.Height <= 0) return null;
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, device.Width, device.Height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero) return null;
            var previous = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, device.Width, device.Height, screenDc, device.X, device.Y, SRCCOPY))
                return null;
            SelectObject(memoryDc, previous);
            var picture = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            picture.Freeze();
            return picture;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Every monitor Windows will let the app capture, in its own order.</summary>
    public static IReadOnlyList<ScreenDevice> Screens()
    {
        var found = new List<ScreenDevice>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref Rect _, IntPtr _) =>
            {
                var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var bounds = info.rcMonitor;
                    found.Add(new ScreenDevice(found.Count,
                        string.IsNullOrWhiteSpace(info.szDevice) ? $"Screen {found.Count + 1}" : info.szDevice.Trim(),
                        bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { found.Clear(); }
        if (found.Count > 0) return found;
        // Enumeration can fail in a service session. One desktop-sized source is better than none.
        var width = GetSystemMetrics(0);
        var height = GetSystemMetrics(1);
        if (width > 0 && height > 0) found.Add(new ScreenDevice(0, "Primary screen", 0, 0, width, height));
        return found;
    }

    public static string ScreenName(int index) =>
        Screens().FirstOrDefault(screen => screen.Index == index)?.Name ?? $"Screen {index + 1}";

    public void Dispose()
    {
        Stop();
        Feeds.Remove(_index);
    }

    const int SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    delegate bool MonitorEnum(IntPtr monitor, IntPtr deviceContext, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clip, MonitorEnum callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, int rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr deviceContext);
}
