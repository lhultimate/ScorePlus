using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System.IO;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public partial class App : Application
{
    // The console is a classic desktop app: AppBuilder picks the windowing system, hooks this
    // Application up to it, and the desktop lifetime owns the main window and every popup that
    // shows against it — that call blocks until the lifetime shuts down.
    static int Main(string[] args)
    {
        // Avalonia reports failed bindings and styling misses through its trace log; setting
        // SCOREPLUS_TRACE=1 pipes that to the console while a port issue is being chased.
        if (Environment.GetEnvironmentVariable("SCOREPLUS_TRACE") is { Length: > 0 })
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(Console.Error));

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }

    private static ClassicDesktopStyleApplicationLifetime desktop = null!;

    public override void OnFrameworkInitializationCompleted()
    {
        // Media decoders and other native components can fail on threads the dispatcher never
        // sees. Log those too, so a window that closes on its own leaves a trace to diagnose.
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);

        // AppBuilder created the lifetime; this app only ends when the main window goes away.
        desktop = (ClassicDesktopStyleApplicationLifetime) ApplicationLifetime;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // The main window opens first and stays locked behind the launch box, so the operator can
        // see the workspace they are about to load into before choosing a game.
        MainWindow window;
        try { window = new MainWindow(waitForLaunch: true); }
        catch (Exception ex)
        {
            LogCrash(ex);
            // The dialog is the operator-facing half. The console half is what lets the Linux
            // smoke test and CI see what actually broke instead of screenshotming a message box.
            if (Environment.GetEnvironmentVariable("SCOREPLUS_TRACE") is { Length: > 0 })
                Console.Error.WriteLine(ex);
            NoticeWindow.Tell(null, "ScorePlus could not start", ex.Message, warning: true);
            desktop.TryShutdown();
            return;
        }
        desktop.MainWindow = window;
        // Opt-in audit: what every window actually looks like to the platform, which is the only way
        // to tell "never drawn" from "drawn off-screen" without standing at the monitor.
        if (WindowDebug.Enabled) WindowDebug.Schedule(this, 1.5, 6);
        // The app follows the main window: when it closes, the lifetime ends.
        window.Closed += (_, _) => desktop.TryShutdown();

        // The console opens filling the screen — full windowed, so the title bar and taskbar stay —
        // and ScreenFit keeps that true on a portrait monitor as well as a wide one.
        window.WindowState = WindowState.Maximized;
        window.Show();

        // Dev/CI hook: SCOREPLUS_NO_LAUNCHER=1 goes straight to a new game instead of waiting on
        // the launch box, so a headless smoke test can exercise the real workspace unattended.
        if (Environment.GetEnvironmentVariable("SCOREPLUS_NO_LAUNCHER") is { Length: > 0 })
        {
            window.StartFromLauncher(LaunchMode.New, null);
            return;
        }

        // The launch box is modal to the console: the operator picks a game before the workspace
        // unlocks. Avalonia's ShowDialog hands back a Task instead of blocking the way WPF's did,
        // and blocking the UI thread with Wait() would deadlock the very dialog being waited on —
        // so the choice is applied from the dialog's own Closed event.
        var launcher = new LauncherWindow();
        launcher.Closed += (_, _) =>
        {
            if (launcher.DialogResult == true) window.StartFromLauncher(launcher.Mode, launcher.GamePath, launcher.SportId);
            else window.Close();
        };
        launcher.ShowDialog(window);
    }

    /// <summary>Appends a timestamped error to <c>logs/errors.log</c> under the app's config root.</summary>
    static void LogCrash(Exception? error)
    {
        try
        {
            var folder = AppPaths.Combine("logs");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "errors.log"), $"{DateTimeOffset.Now:u} {error}\n\n");
        }
        catch
        {
            // Logging must never be the reason the app fails to report a problem.
        }
    }
}
