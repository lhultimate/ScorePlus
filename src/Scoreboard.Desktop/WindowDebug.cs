using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Scoreboard.Desktop;

/// <summary>
/// Opt-in window audit for the "the app started but nothing showed up" reports. Set
/// SCOREPLUS_WINDOW_DEBUG=1 and every open window, plus what the screen backend says about the
/// monitor it sits on, is written to stderr a moment after launch and again a few seconds later.
/// A compositor screenshot cannot tell a window that was never drawn from one that was drawn
/// off-screen, shrunk by a bad monitor reading, or left invisible; these numbers can.
/// </summary>
internal static class WindowDebug
{
    public static bool Enabled => Environment.GetEnvironmentVariable("SCOREPLUS_WINDOW_DEBUG") is { Length: > 0 };

    /// <summary>Writes the audit for the running application's windows at the given delays.</summary>
    public static void Schedule(Avalonia.Application app, params double[] seconds)
    {
        Log($"scheduled delays=[{string.Join(",", seconds)}] enabled={Enabled}");
        if (!Enabled) return;
        foreach (var delay in seconds)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
            timer.Tick += (_, _) => { timer.Stop(); Dump(app); };
            timer.Start();
        }
    }

    public static void Dump(Avalonia.Application app)
    {
        try
        {
            var lifetime = app.ApplicationLifetime as ClassicDesktopStyleApplicationLifetime;
            var windows = lifetime?.Windows ?? [];
            Log($"windows={windows.Count}");
            foreach (var window in windows)
            {
                Log($"window title={Quote(window.Title)} class={window.GetType().Name}");
                Log($"  visible={window.IsVisible} enabled={window.IsEnabled} state={window.WindowState} opacity={window.Opacity:0.###}");
                Log($"  frame={window.Position} client={window.Bounds} desired={window.Width}x{window.Height} " +
                    $"min={window.MinWidth}x{window.MinHeight} max={window.MaxWidth}x{window.MaxHeight} " +
                    $"scaling={window.RenderScaling:0.##} topmost={window.Topmost} " +
                    $"decorations={window.SystemDecorations} alpha={window.TransparencyLevelHint.Count} " +
                    $"background={Describe(window.Background)}");
                if (window.Content is Avalonia.Visual cv)
                    Log($"  content={cv.GetType().Name} bounds={cv.Bounds} opacity={cv.Opacity:0.###} " +
                        $"isVisible={cv.IsVisible} transform={cv.RenderTransform}");
                Log($"  screens: {DescribeScreens(window)}");
                Log($"  visualChildren={window.GetVisualChildren().Count()} descendants={window.GetVisualDescendants().Count()}");
                Snapshot(window);
            }
        }
        catch (Exception ex)
        {
            Log($"window audit failed: {ex}");
        }
    }

    static string DescribeScreens(Avalonia.Controls.Window window)
    {
        try
        {
            var screens = window.Screens;
            if (screens is null) return "none";
            var on = screens.ScreenFromWindow(window);
            var all = string.Join(" ", screens.All.Select(s =>
                $"{s.DisplayName}:{s.Bounds} scale={s.Scaling} working={s.WorkingArea} primary={s.IsPrimary}"));
            return $"fromWindow={(on is null ? "null" : $"{on.DisplayName}:{on.Bounds} scale={on.Scaling}")} all=[{all}]";
        }
        catch (Exception ex)
        {
            return $"failed: {ex.Message}";
        }
    }

    /// <summary>
    /// SCOREPLUS_SNAPSHOT=/tmp/x renders each window's visual tree into a PNG. This is what the app
    /// draws, with no compositor, XWayland or screenshot tool in the way: if the picture is right and
    /// the screen is not, the fault is in the display path; if the picture is blank, the fault is in
    /// the app.
    /// <para>
    /// The whole tree has to be walked. `window.Render(ctx)` alone paints only the window's own
    /// layer — its background — because a visual's Render never draws its children; that is the
    /// compositor's job. Calling it on its own produced a pure black PNG for an app whose windows
    /// were demonstrably on screen, which would have sent the next reader hunting a render bug that
    /// does not exist.
    /// </para>
    /// </summary>
    static void Snapshot(Avalonia.Controls.Window window)
    {
        var path = Environment.GetEnvironmentVariable("SCOREPLUS_SNAPSHOT");
        if (string.IsNullOrEmpty(path) || window.Bounds.Width < 2 || window.Bounds.Height < 2) return;
        try
        {
            var w = (int)window.Bounds.Width;
            var h = (int)window.Bounds.Height;
            using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
            using (var ctx = rtb.CreateDrawingContext())
                RenderTree(window, ctx);
            var file = $"{path}-{Sanitize(window.Title)}-{w}x{h}.png";
            rtb.Save(file);
            Log($"  snapshot={file}");
        }
        catch (Exception ex)
        {
            Log($"  snapshot failed: {ex.Message}");
        }
    }

    /// <summary>Draws a visual and, recursively, everything below it, the way the compositor does.</summary>
    static void RenderTree(Avalonia.Visual visual, Avalonia.Media.DrawingContext ctx)
    {
        if (!visual.IsVisible || visual.Opacity <= 0) return;
        Avalonia.Media.DrawingContext.PushedState? opacityScope =
            visual.Opacity < 1 ? ctx.PushOpacity(visual.Opacity) : null;
        var bounds = visual.Bounds;
        Avalonia.Media.DrawingContext.PushedState? transform =
            bounds.X != 0 || bounds.Y != 0
                ? ctx.PushTransform(Avalonia.Matrix.CreateTranslation(bounds.X, bounds.Y))
                : null;
        try
        {
            visual.Render(ctx);
            foreach (var child in visual.GetVisualChildren())
                RenderTree(child, ctx);
        }
        finally
        {
            transform?.Dispose();
            opacityScope?.Dispose();
        }
    }

    static string Sanitize(string? value) =>
        new string((value ?? "window").Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

    static string Describe(Avalonia.Media.IBrush? brush) => brush switch
    {
        null => "null",
        Avalonia.Media.SolidColorBrush s => $"#{s.Color}",
        _ => brush.GetType().Name,
    };

    static string Quote(string? value) => $"\"{value}\"";

    static void Log(string message)
    {
        Console.Error.WriteLine($"[win] {message}");
        // stderr is swallowed by some launch paths (a WinExe apphost, a detached launcher);
        // the file keeps the audit readable no matter how the app was started.
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "scoreplus-window-debug.log"),
                $"{DateTimeOffset.Now:HH:mm:ss.fff} {message}\n");
        }
        catch { }
    }
}
