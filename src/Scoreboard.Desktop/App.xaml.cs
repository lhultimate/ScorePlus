using System.Windows;
using System.Windows.Input;
using System.IO;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Media decoders and other native components can fail on threads the dispatcher never
        // sees. Log those too, so a window that closes on its own leaves a trace to diagnose.
        // F11 belongs to the application, not to one window: the studio, output, media panel and
        // dialogs all get the same true fullscreen behavior.
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent, new KeyEventHandler(HandleWindowKey));
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            NoticeWindow.Tell(MainWindow, "ScorePlus hit an unexpected error",
                "Your game autosave was kept.\n\n" + args.Exception.Message, warning: true);
            args.Handled = true;
        };
        base.OnStartup(e);

        // The main window opens first and stays locked behind the launch box, so the operator can
        // see the workspace they are about to load into before choosing a game.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        MainWindow window;
        try { window = new MainWindow(waitForLaunch: true); }
        catch (Exception ex)
        {
            LogCrash(ex);
            NoticeWindow.Tell(null, "ScorePlus could not start", ex.Message, warning: true);
            Shutdown();
            return;
        }
        window.Closed += (_, _) => Shutdown();
        // The console opens filling the screen — full windowed, so the title bar and taskbar stay —
        // and ScreenFit keeps that true on a portrait monitor as well as a wide one.
        window.WindowState = WindowState.Maximized;
        window.Show();

        var launcher = new LauncherWindow { Owner = window };
        if (launcher.ShowDialog() != true) { window.Close(); return; }
        window.StartFromLauncher(launcher.Mode, launcher.GamePath, launcher.SportId);
    }

    static void HandleWindowKey(object sender, KeyEventArgs e)
    {
        if (sender is not Window window || e.Handled) return;
        if (e.Key == Key.Escape && WindowFullscreen.IsFullscreen(window))
        {
            WindowFullscreen.Exit(window);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.F11) return;
        // F11 on the console, a detached panel or the audience output all mean real fullscreen:
        // no title bar, no frame and no taskbar. The old maximize toggle made a maximized console
        // look like it had been returned to a window at the side of the screen.
        WindowFullscreen.Toggle(window);
        e.Handled = true;
    }

    /// <summary>Appends a timestamped error to <c>%LOCALAPPDATA%\ScorePlus\logs\errors.log</c>.</summary>
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
