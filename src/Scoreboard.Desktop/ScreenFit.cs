using System;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Scoreboard.Desktop;

/// <summary>
/// Keeps operator windows usable on any monitor, including a portrait screen or a laptop panel at
/// 150% scaling. A window is never allowed to stay wider or taller than the monitor it sits on, so
/// dragging it onto a vertical display shrinks it into that display's work area instead of leaving
/// half the console off-screen.
/// </summary>
internal static class ScreenFit
{
    /// <summary>Follows the window from monitor to monitor and keeps its bounds on the current one.</summary>
    public static void Track(Window window)
    {
        // WPF's SourceInitialized is Avalonia's Opened, LocationChanged is PositionChanged, and the
        // maximized state arrives as a WindowState property change rather than its own event.
        window.Opened += (_, _) => Apply(window);
        window.PositionChanged += (_, _) => Apply(window);
        window.Resized += (_, _) => Apply(window);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Window.WindowStateProperty) Apply(window);
        };
    }

    /// <summary>
    /// Keeps the window inside the monitor it is on, in layout units. A maximized window is measured
    /// against the whole monitor rather than the work area: clamping it to the work area minus a
    /// frame margin left a strip of desktop down the side and bottom, which is exactly the window
    /// that would not go full screen on any monitor, portrait included.
    /// <para>
    /// Monitor geometry comes from Avalonia's screen backend — xrandr under X11, the portal under
    /// Wayland — divided by that monitor's scale factor, which is the same division the Windows port
    /// did with GetDpi. No win32 handles, and Wayland's no-coordinate model cannot be measured any
    /// other way.
    /// </para>
    /// </summary>
    public static void Apply(Window window)
    {
        Avalonia.Platform.Screen? screen;
        try
        {
            var screens = window.Screens;
            screen = screens?.ScreenFromWindow(window) ?? screens?.Primary;
        }
        // A window that has never been shown has no platform screen to measure against yet.
        catch (Exception) { return; }
        if (screen is null) return;

        var scaling = screen.Scaling > 0 ? screen.Scaling : window.RenderScaling;
        if (scaling <= 0) scaling = 1;

        var maximized = window.WindowState == WindowState.Maximized;
        var area = maximized ? screen.Bounds : screen.WorkingArea;
        var width = Math.Max(320, area.Width / scaling);
        var height = Math.Max(240, area.Height / scaling);

        window.MaxWidth = width;
        window.MaxHeight = height;
        // Only a floating window is pulled back in; resizing a maximized one would knock it out of
        // the maximized state the desktop just applied.
        if (maximized) return;
        if (window.Width > width) window.Width = width;
        if (window.Height > height) window.Height = height;
    }
}
