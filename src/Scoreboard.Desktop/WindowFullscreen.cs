using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Scoreboard.Desktop;

/// <summary>
/// One fullscreen implementation for every app window. It removes the title bar and frame,
/// disables resizing, covers the taskbar, and restores the exact window style and geometry when
/// the operator leaves fullscreen. ScreenFit deliberately steps aside while this is active.
/// </summary>
public static class WindowFullscreen
{
    sealed class State
    {
        public WindowStyle Style;
        public ResizeMode Resize;
        public WindowState WindowState;
        public bool Topmost;
        public bool ShowInTaskbar;
        public double Left, Top, Width, Height;
        public double MaxWidth, MaxHeight;
        public Window? Shield;
    }

    static readonly ConditionalWeakTable<Window, State> States = new();

    public static bool IsFullscreen(Window window) => States.TryGetValue(window, out _);

    public static void Toggle(Window window)
    {
        if (IsFullscreen(window)) Exit(window);
        else Enter(window);
    }

    public static void Enter(Window window)
    {
        if (IsFullscreen(window) || !window.IsLoaded) return;
        var state = new State
        {
            Style = window.WindowStyle,
            Resize = window.ResizeMode,
            WindowState = window.WindowState,
            Topmost = window.Topmost,
            ShowInTaskbar = window.ShowInTaskbar,
            Left = window.Left,
            Top = window.Top,
            Width = window.Width,
            Height = window.Height,
            MaxWidth = window.MaxWidth,
            MaxHeight = window.MaxHeight
        };
        States.Add(window, state);

        // Changing WindowStyle makes WPF rebuild the window surface. A background-coloured shield
        // covers that one-frame rebuild; without it the new client area can appear white before
        // the app's content is painted again.
        state.Shield = ShowShield(window);

        // The title bar and frame must be gone before the bounds are applied. Resetting the
        // maximized state first prevents Windows from re-applying the work-area size afterwards.
        window.WindowState = WindowState.Normal;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.MaxWidth = double.PositiveInfinity;
        window.MaxHeight = double.PositiveInfinity;
        window.Topmost = true;
        Apply(window);
        if (state.Shield is not null) BringToFront(state.Shield);
        // A style change can recreate the HWND. Re-apply once the new handle exists.
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (IsFullscreen(window)) Apply(window);
            if (state.Shield is not null) BringToFront(state.Shield);
        }));

        if (state.Shield is not null)
        {
            // ContentRendered normally closes the shield as soon as the new frame is painted. The
            // fallback timer prevents a shield from being stranded if that event does not fire.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                CloseShield(state);
            };
            timer.Start();
            EventHandler? rendered = null;
            rendered = (_, _) =>
            {
                window.ContentRendered -= rendered;
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    timer.Stop();
                    if (IsFullscreen(window)) Apply(window);
                    CloseShield(state);
                }));
            };
            window.ContentRendered += rendered;
        }
    }

    public static void Exit(Window window)
    {
        if (!States.TryGetValue(window, out var state)) return;
        States.Remove(window);
        // Cover the reverse style change too: restoring the frame makes WPF expose its client
        // surface again, which otherwise flashes white on the way back to a normal window.
        state.Shield = ShowShield(window);
        window.Topmost = state.Topmost;
        window.ShowInTaskbar = state.ShowInTaskbar;
        window.WindowStyle = state.Style;
        window.ResizeMode = state.Resize;
        window.MaxWidth = state.MaxWidth;
        window.MaxHeight = state.MaxHeight;
        window.WindowState = WindowState.Normal;
        window.Left = state.Left;
        window.Top = state.Top;
        window.Width = state.Width;
        window.Height = state.Height;
        window.WindowState = state.WindowState;
        window.Activate();
        if (state.Shield is not null)
        {
            BringToFront(state.Shield);
            ScheduleShieldClose(window, state);
        }
    }

    static void ScheduleShieldClose(Window window, State state)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CloseShield(state);
        };
        timer.Start();
        EventHandler? rendered = null;
        rendered = (_, _) =>
        {
            window.ContentRendered -= rendered;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                timer.Stop();
                CloseShield(state);
            }));
        };
        window.ContentRendered += rendered;
    }

    static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        var dpi = GetDpiForWindow(handle);
        var scale = dpi > 0 ? dpi / 96.0 : VisualTreeHelper.GetDpi(window).DpiScaleX;
        window.Left = info.Monitor.Left / scale;
        window.Top = info.Monitor.Top / scale;
        window.Width = (info.Monitor.Right - info.Monitor.Left) / scale;
        window.Height = (info.Monitor.Bottom - info.Monitor.Top) / scale;

        // WPF sizes are in device-independent units; SetWindowPos takes physical pixels. Doing
        // both makes the window fill the monitor even when its DPI differs from the primary one.
        SetWindowPos(handle, HwndTopmost,
            info.Monitor.Left, info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left,
            info.Monitor.Bottom - info.Monitor.Top,
            SwpShowWindow | SwpFrameChanged);
        window.Activate();
    }

    static Window? ShowShield(Window window)
    {
        var bounds = MonitorBounds(window);
        if (bounds is null) return null;
        var background = window.Background ?? Brushes.Black;
        var snapshot = Snapshot(window);
        var shield = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            IsHitTestVisible = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = background,
            // A frozen image of the window is much less noticeable than a flat colour while the
            // real window changes style. If capture is unavailable, fall back to the background.
            Content = new Border
            {
                Background = background,
                Child = snapshot is null ? null : new Image { Source = snapshot, Stretch = Stretch.Fill }
            },
            Left = bounds.Value.Left,
            Top = bounds.Value.Top,
            Width = bounds.Value.Width,
            Height = bounds.Value.Height
        };
        try
        {
            shield.Show();
            shield.UpdateLayout();
            shield.Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
            return shield;
        }
        catch
        {
            try { shield.Close(); } catch { }
            return null;
        }
    }

    static ImageSource? Snapshot(Window window)
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
            var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
            if (width < 2 || height < 2) return null;
            var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(window);
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    static void CloseShield(State state)
    {
        try { state.Shield?.Close(); } catch { }
        state.Shield = null;
    }

    static (double Left, double Top, double Width, double Height)? MonitorBounds(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return null;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return null;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return null;
        var dpi = GetDpiForWindow(handle);
        var scale = dpi > 0 ? dpi / 96.0 : VisualTreeHelper.GetDpi(window).DpiScaleX;
        return (info.Monitor.Left / scale, info.Monitor.Top / scale,
            (info.Monitor.Right - info.Monitor.Left) / scale,
            (info.Monitor.Bottom - info.Monitor.Top) / scale);
    }

    static void BringToFront(Window shield)
    {
        var handle = new WindowInteropHelper(shield).Handle;
        if (handle == IntPtr.Zero) return;
        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    const uint MonitorDefaultToNearest = 2;
    const uint SwpShowWindow = 0x0040;
    const uint SwpFrameChanged = 0x0020;
    const uint SwpNoSize = 0x0001;
    const uint SwpNoMove = 0x0002;
    const uint SwpNoActivate = 0x0010;
    static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr handle);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }
}
