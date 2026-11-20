using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Scoreboard.Core;
using Scoreboard.Desktop;

namespace Screenshots;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var output = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "site", "assets"));
        Directory.CreateDirectory(output);

        Environment.SetEnvironmentVariable("SCOREPLUS_SMOKE", "1");
        var appData = Path.Combine(Path.GetTempPath(), $"scoreplus-shots-{Guid.NewGuid():N}");
        AppPaths.UseRoot(appData);
        PreferencesStore.UsePath(Path.Combine(appData, "preferences.json"));
        AutosaveStore.UseFolder(Path.Combine(appData, "autosaves"));

        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        MainWindow.PromptOnExit = false;
        MainWindow.CheckForUpdatesOnLoad = false;
        MainWindow.WarnOnSportSwitch = false;

        try
        {
            var engine = RealGame();
            var catalog = new ScorebugCatalog(Path.Combine(appData, "shots.sbd"));
            ApplyShowcase(catalog);

            Capture(new LauncherWindow(), Path.Combine(output, "launcher.png"), 1180, 820);
            var newGame = new SportPickerWindow(new SportCatalog(), "football", "Dark")
            {
                SizeToContent = SizeToContent.Manual,
                Width = 660,
                Height = 780
            };
            Capture(newGame, Path.Combine(output, "new-game.png"), 660, 780);
            var console = new MainWindow(LaunchMode.New) { Width = 1440, Height = 960 };
            Seed(console.Engine);
            console.PublishOutput();
            console.UpdateLayout();
            Capture(console, Path.Combine(output, "console.png"), 1440, 960);
            console.Close();

            var macroWindow = new MainWindow(LaunchMode.New) { Width = 1440, Height = 960 };
            Seed(macroWindow.Engine);
            macroWindow.PublishOutput();
            macroWindow.UpdateLayout();
            if (macroWindow.FindName("MacroHost") is ContentControl macroHost
                && macroHost.Content is MacroDock macroDock)
                macroDock.RefreshView(force: true);
            if (macroWindow.FindName("MacrosTab") is Button macrosTab)
                macrosTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(250);
            Capture(macroWindow, Path.Combine(output, "macros.png"), 1440, 960);
            macroWindow.Close();

            var studio = new ScorebugEditorWindow(catalog, engine) { Width = 1892, Height = 1004 };
            Capture(studio, Path.Combine(output, "studio.png"), 1892, 1004, selectLayer: true);
            studio.Close();

            var ruleCatalog = new ScorebugCatalog(Path.Combine(appData, "rules.sbd"));
            ApplyShowcase(ruleCatalog);
            var active = ruleCatalog.ActiveDisplay;
            var layout = active.Layouts[0];
            var clock = layout.Elements.FirstOrDefault(element =>
                (element.Content ?? "").Contains("GAME_CLOCK", StringComparison.OrdinalIgnoreCase));
            var rule = new AutomationRule { Name = "Last-minute decimals", LayoutId = layout.Id };
            rule.Conditions.Add(new AutomationCondition
            {
                Field = AutomationField.GameClock,
                Operator = AutomationOperator.LessOrEqual,
                Value = 60
            });
            if (clock is not null)
                rule.Actions.Add(new AutomationAction
                {
                    Kind = AutomationActionKind.SetClockDecimals,
                    LayoutId = layout.Id,
                    ElementId = clock.Id,
                    Number = 1
                });
            ruleCatalog.UpdateAutomations(active.Id, [rule]);
            var runtime = new AutomationRuntime(ruleCatalog, engine, engine);
            var automation = new AutomationWindow(ruleCatalog, engine, "Dark", active.Id, runtime);
            Capture(automation, Path.Combine(output, "conditionals.png"), 1500, 820);
            automation.Close();

            Capture(new SettingsWindow(new UserPreferences { Theme = "Dark" }), Path.Combine(output, "settings.png"), 1900, 1100, borderless: true);
            var keybinds = new KeybindsWindow(new KeybindSettings())
            {
                Background = new SolidColorBrush(Color.FromRgb(11, 17, 24))
            };
            ThemeManager.Apply(keybinds, "Dark");
            Capture(keybinds, Path.Combine(output, "keybinds.png"), 1500, 900);
            Capture(new SportProfileWindow(new SportCatalog()), Path.Combine(output, "sport-profiles.png"), 1600, 1000, borderless: true);
            var team = new TeamIdentityWindow(engine, true, "Dark")
            {
                SizeToContent = SizeToContent.Manual,
                Width = 1200,
                Height = 900
            };
            Capture(team, Path.Combine(output, "team-identity.png"), 1200, 900);
            Capture(new AboutWindow("Dark"), Path.Combine(output, "about.png"), 760, 540, borderless: true);

            SaveOnAir(catalog, engine, Path.Combine(output, "on-air.png"));
            CaptureWindows(output, catalog, engine);
            Console.WriteLine($"Screenshots written to {output}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            app.Shutdown();
        }
    }

    static GameEngine RealGame()
    {
        var engine = new GameEngine();
        engine.NewGame("football");
        Seed(engine);
        return engine;
    }

    static void Seed(GameEngine engine)
    {
        engine.SetGameName("Rivalry Night");
        engine.SetTeamIdentity(true, "Portland", "PDX", TeamLogo("PDX", Color.FromRgb(242, 107, 58)), "#F26B3A", "#101820",
            record: "12-3", ranking: "4", divisionRecord: "4-1");
        engine.SetTeamIdentity(false, "Seattle", "SEA", TeamLogo("SEA", Color.FromRgb(56, 215, 163)), "#38D7A3", "#101820",
            record: "10-5", ranking: "7", divisionRecord: "3-2");
        engine.SetScore(true, 24);
        engine.SetScore(false, 17);
        engine.SetPeriod(4);
        engine.SetClock(252);
        engine.SetPlayClock(18);
        engine.SetDown(3);
        engine.SetFootballDistance(7, 38);
        engine.SetPossession(true);
        engine.SetTimeoutCount(true, 2);
        engine.SetTimeoutCount(false, 1);
        engine.SetTimeoutClock(42);
        engine.StoreMacros(
        [
            Macro("TIMEOUT", "#FF6B2C",
                new MacroAction { Kind = MacroActionKind.SetTimeouts, Home = true, Value = 2 },
                new MacroAction { Kind = MacroActionKind.SetGameClock, Value = 252 },
                new MacroAction { Kind = MacroActionKind.Wait, WaitSeconds = 1.5 },
                new MacroAction { Kind = MacroActionKind.Note, Text = "SPONSOR READ" }),
            Macro("TOUCHDOWN", "#38D7A3",
                new MacroAction { Kind = MacroActionKind.Score, Home = true, Amount = 6 },
                new MacroAction { Kind = MacroActionKind.SetDown, Value = 1 },
                new MacroAction { Kind = MacroActionKind.SetFootballDistance, Value = 10 }),
            Macro("FIELD GOAL", "#38D7A3",
                new MacroAction { Kind = MacroActionKind.Score, Home = true, Amount = 3 }),
            Macro("HALFTIME", "#62A8FF",
                new MacroAction { Kind = MacroActionKind.SetPeriod, Value = 2 },
                new MacroAction { Kind = MacroActionKind.SetGameClock, Value = 720 },
                new MacroAction { Kind = MacroActionKind.ClockControl, Path = "game", Value = 1 }),
            Macro("SPONSOR", "#D6B36A",
                new MacroAction { Kind = MacroActionKind.Note, Text = "SPONSOR CARD" }),
            Macro("KICKOFF", "#62A8FF",
                new MacroAction { Kind = MacroActionKind.SetPossession, Home = true },
                new MacroAction { Kind = MacroActionKind.SetDown, Value = 1 },
                new MacroAction { Kind = MacroActionKind.SetFootballDistance, Value = 10 }),
            Macro("PERIOD BREAK", "#FF6B2C",
                new MacroAction { Kind = MacroActionKind.SetPeriod, Value = 4 },
                new MacroAction { Kind = MacroActionKind.ClockControl, Path = "game", Value = 1 }),
            Macro("RESET DOWN", "#A78BFA",
                new MacroAction { Kind = MacroActionKind.SetDown, Value = 1 },
                new MacroAction { Kind = MacroActionKind.SetFootballDistance, Value = 10 })
        ], 1);
    }

    static SavedMacro Macro(string label, string color, params MacroAction[] actions) => new()
    {
        Label = label,
        Color = color,
        Actions = actions.ToList()
    };

    static string TeamLogo(string label, Color color)
    {
        var path = Path.Combine(Path.GetTempPath(), $"scoreplus-{label}-logo.png");
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen())
        {
            draw.DrawEllipse(new SolidColorBrush(Color.FromRgb(11, 17, 24)), new Pen(new SolidColorBrush(color), 7), new Point(128, 128), 95, 95);
            var text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                62, new SolidColorBrush(Colors.White), 1.0);
            draw.DrawText(text, new Point(128 - text.Width / 2, 128 - text.Height / 2));
        }
        var bitmap = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    static void ApplyShowcase(ScorebugCatalog catalog) => catalog.Update(layout =>
    {
        layout.Name = "Broadcast Classic";
        layout.Width = 1280;
        layout.Height = 720;
        layout.Background = "#00000000";
        layout.Elements.Clear();
        layout.Elements.AddRange(
        [
            new() { Name = "Scorebug shadow", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle, X = 56, Y = 560, Width = 1168, Height = 138, CornerRadius = 2, Background = "#66000000" },
            new() { Name = "Away accent", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle, X = 48, Y = 552, Width = 14, Height = 138, Background = "#FFF26B3A" },
            new() { Name = "Home accent", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle, X = 1218, Y = 552, Width = 14, Height = 138, Background = "#FF38D7A3" },
            new() { Name = "Top edge", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle, X = 62, Y = 552, Width = 1156, Height = 10, Background = "#FFF26B3A" },
            new() { Name = "Bug plate", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle, X = 62, Y = 562, Width = 1156, Height = 128, Background = "#F00B1118" },
            new() { Name = "Away logo", Kind = ScorebugElementKind.Logo, LogoSide = ScorebugLogoSide.Away, Content = "AWAY LOGO", X = 84, Y = 580, Width = 86, Height = 86, FontSize = 18 },
            new() { Name = "Home logo", Kind = ScorebugElementKind.Logo, LogoSide = ScorebugLogoSide.Home, Content = "HOME LOGO", X = 1110, Y = 580, Width = 86, Height = 86, FontSize = 18 },
            new() { Name = "Away team", Content = "{{AWAY_TEAM}}", X = 188, Y = 578, Width = 240, Height = 40, FontSize = 30, Bold = true },
            new() { Name = "Away record", Content = "{{AWAY_RECORD}}", X = 188, Y = 618, Width = 240, Height = 22, FontSize = 13 },
            new() { Name = "Away score", Content = "{{AWAY_SCORE}}", X = 430, Y = 574, Width = 130, Height = 84, FontSize = 66, Bold = true, ForegroundSource = ScorebugColorSource.AwayPrimary },
            new() { Name = "Period", Content = "{{PERIOD}}", X = 574, Y = 570, Width = 160, Height = 28, FontSize = 18 },
            new() { Name = "Game clock", Content = "{{GAME_CLOCK}}", X = 574, Y = 596, Width = 160, Height = 52, FontSize = 40, Bold = true, Transition = ScorebugTransition.Cut },
            new() { Name = "Down and distance", Content = "{{DOWNS}}", X = 548, Y = 650, Width = 212, Height = 24, FontSize = 13 },
            new() { Name = "Home score", Content = "{{HOME_SCORE}}", X = 704, Y = 574, Width = 130, Height = 84, FontSize = 66, Bold = true, ForegroundSource = ScorebugColorSource.HomePrimary },
            new() { Name = "Home team", Content = "{{HOME_TEAM}}", X = 846, Y = 578, Width = 240, Height = 40, FontSize = 30, Bold = true },
            new() { Name = "Home record", Content = "{{HOME_RECORD}}", X = 846, Y = 618, Width = 240, Height = 22, FontSize = 13 },
            new() { Name = "Away divider", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle, X = 556, Y = 574, Width = 8, Height = 70, Background = "#6638D7A3" },
            new() { Name = "Home divider", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle, X = 828, Y = 574, Width = 8, Height = 70, Background = "#66F26B3A" },
            new() { Name = "Show name", Content = "SCOREPLUS · RIVALRY NIGHT", X = 48, Y = 528, Width = 420, Height = 20, FontSize = 13 },
            new() { Name = "Live badge", Content = "LIVE · ON AIR", X = 960, Y = 528, Width = 272, Height = 20, FontSize = 13, TextAlign = ScorebugTextAlign.Right }
        ]);
    });

    static void SaveOnAir(ScorebugCatalog catalog, GameEngine engine, string path)
    {
        var root = new Grid
        {
            Width = 1920,
            Height = 1080,
            Background = new LinearGradientBrush(
                Color.FromRgb(14, 24, 29), Color.FromRgb(28, 19, 16), new Point(0, 0), new Point(1, 1))
        };
        var glow = new RadialGradientBrush(Color.FromArgb(110, 243, 111, 54), Color.FromArgb(0, 243, 111, 54))
        {
            Center = new Point(0.32, 0.72),
            GradientOrigin = new Point(0.32, 0.72),
            RadiusX = 0.55,
            RadiusY = 0.55
        };
        root.Children.Add(new Border { Background = glow });
        var view = new ScorebugCanvasView(catalog, engine,
            stateProvider: () => engine.State,
            layoutProvider: () => catalog.Layout)
        {
            Width = 1920,
            Height = 1080,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        root.Children.Add(view);
        root.Measure(new Size(1920, 1080));
        root.Arrange(new Rect(0, 0, 1920, 1080));
        root.UpdateLayout();
        Save(root, 1920, 1080, path);
        view.Dispose();
    }

    static void CaptureWindows(string output, ScorebugCatalog catalog, GameEngine engine)
    {
        var preferences = new UserPreferences { Theme = "Dark" };
        var session = new MediaSession(preferences);
        SeedMedia(session);
        SeedRoster(engine);

        Capture(new OutputWindow(engine, catalog, "Dark"), Path.Combine(output, "output.png"), 1280, 720);

        var hardware = new HardwareWindow(preferences, new HardwareService(engine));
        ThemeManager.Apply(hardware, "Dark");
        Capture(hardware, Path.Combine(output, "hardware.png"), 1100, 1000, borderless: true);

        var program = new ProgramSelection(catalog);
        var published = new GameEngine();
        var automation = new AutomationRuntime(catalog, published, engine);
        Capture(new MultiviewWindow(session, preferences, catalog, engine, published, program, automation, session.Playback),
            Path.Combine(output, "multiview.png"), 1320, 820);

        var booth = new ReplayBoothController(session, engine, preferences);
        Capture(new ReplayBoothWindow(booth, preferences), Path.Combine(output, "replay-booth.png"), 1340, 860);

        var league = new LeagueCloudService(engine, preferences);
        Capture(new LeagueCloudWindow(league, preferences, catalog, program, engine),
            Path.Combine(output, "league-cloud.png"), 760, 780, borderless: true);
        Capture(new TickerWindow(engine, league), Path.Combine(output, "ticker.png"), 820, 760);

        Capture(new EventLogWindow(engine, "Dark", false), Path.Combine(output, "event-log.png"), 1080, 760);
        Capture(new MotionMakerWindow(MotionKind.Transition, catalog, engine, "Dark"),
            Path.Combine(output, "transition-builder.png"), 1440, 920);
        Capture(new MotionMakerWindow(MotionKind.Animation, catalog, engine, "Dark"),
            Path.Combine(output, "animation-maker.png"), 1440, 920);
        Capture(new IconMakerWindow("Dark"), Path.Combine(output, "icon-creator.png"), 1320, 1000, borderless: true);

        Capture(new GameControlWindow(engine, published, catalog, () => { }, () => false, _ => { }, () => true,
            session, "Dark", automation), Path.Combine(output, "control-panel.png"), 1280, 900);

        var newGame = new SportPickerWindow(new SportCatalog(), "football", "Dark")
        {
            SizeToContent = SizeToContent.Manual,
            Width = 920,
            Height = 1000
        };
        Capture(newGame, Path.Combine(output, "new-game.png"), 920, 1000, borderless: true);

        var pickerType = typeof(MainWindow).Assembly.GetType("Scoreboard.Desktop.DisplayPickerWindow")!;
        var pickerCtor = pickerType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(ctor => ctor.GetParameters().Length == 8);
        var picker = (Window)pickerCtor.Invoke([catalog, "Dark", null,
            "Audience output", "Which display is this window for?",
            "Each display has its own audience window and its own layout. Opening one again adds another window rather than replacing the first.",
            "Open output", null]);
        picker.SizeToContent = SizeToContent.Manual;
        Capture(picker, Path.Combine(output, "display-picker.png"), 640, 360, borderless: true);

        var mediaShell = new MainWindow(LaunchMode.New) { Width = 1440, Height = 960 };
        Seed(mediaShell.Engine);
        SeedRoster(mediaShell.Engine);
        SeedMedia(SessionOf(mediaShell));
        mediaShell.PublishOutput();
        ShowForShot(mediaShell);
        mediaShell.ShowMediaTab(true);
        Pump(350);
        Capture(mediaShell, Path.Combine(output, "media.png"), 1440, 960);

        var statsShell = new MainWindow(LaunchMode.New) { Width = 1440, Height = 960 };
        Seed(statsShell.Engine);
        SeedRoster(statsShell.Engine);
        ShowForShot(statsShell);
        Click(statsShell, "RosterTab_Click");
        Pump(350);
        Capture(statsShell, Path.Combine(output, "stats.png"), 1440, 960);
    }

    static void SeedMedia(MediaSession session)
    {
        session.CreateFolder("Game Day");
        session.Library.Add(new MediaEntry(@"C:\ScorePlus\Media\Game Day\Show Open.mp4", "Show", "Game Day", Name: "Show Open"));
        session.Library.Add(new MediaEntry(@"C:\ScorePlus\Media\Highlights\Halftime Reel.mp4", "Highlights", "Highlights", Name: "Halftime Reel"));
        session.Library.Add(new MediaEntry(@"C:\ScorePlus\Media\Highlights\Big Play.mp4", "Highlights", "Highlights", Name: "Big Play"));
        session.Library.Add(new MediaEntry(@"C:\ScorePlus\Media\Sponsors\Spot 04.mp4", "Ads", "Sponsors", Name: "Spot 04"));
        session.Library.Add(new MediaEntry(@"C:\ScorePlus\Media\Music\Crowd Bed.mp3", "Music", Name: "Crowd Bed"));
        session.AddCamera(0, "Game Day");
        session.AddMicrophone(0, "Game Day");
        session.AddSpeaker(0, "House");
        var playlist = session.CreatePlaylist("Show Open");
        session.AddToPlaylist(playlist, session.Library[0]);
    }

    static void SeedRoster(GameEngine engine)
    {
        var roster = RosterStore.Book.Add("Portland Varsity");
        (int Number, string Name, string Position, string Group, (string Name, int Value)[] Stats)[] players =
        [
            (12, "Jalen Morgan", "QB", "Offense", [("Passing yards", 284), ("Passing TDs", 3), ("Interceptions thrown", 0), ("Completions", 24), ("Attempts", 31), ("Rushing yards", 22)]),
            (28, "Andre Rivera", "RB", "Offense", [("Rushing yards", 96), ("Rushing TDs", 1), ("Carries", 18), ("Receiving yards", 24)]),
            (7, "Kai Lee", "WR", "Offense", [("Receiving yards", 118), ("Receptions", 7), ("Receiving TDs", 2)]),
            (88, "Marcus Hayes", "TE", "Offense", [("Receiving yards", 46), ("Receptions", 4)]),
            (54, "Devin Cole", "LB", "Defense", [("Tackles", 9), ("Solo tackles", 6), ("Sacks", 1)]),
            (23, "Noah Park", "CB", "Defense", [("Tackles", 5), ("Passes defended", 2), ("Interceptions", 1)]),
            (5, "Eli Brooks", "K", "Special Teams", [("Field goals", 1), ("Extra points", 3)]),
            (41, "Sam Ortiz", "LS", "Special Teams", [("Tackles", 1)])
        ];
        foreach (var player in players)
        {
            var entry = roster.AddPlayer();
            entry.Number = player.Number;
            entry.Name = player.Name;
            entry.Position = player.Position;
            entry.Group = player.Group;
            foreach (var stat in player.Stats) entry.SetStat(stat.Name, stat.Value);
        }
        roster.EnsureColumns(players.SelectMany(player => player.Stats.Select(stat => stat.Name)));
        RosterStore.Save();
        engine.AssignRoster(true, roster.Id);
        engine.SelectPlayer(true, roster.Players[0].Id);
    }

    static MediaSession SessionOf(MainWindow window) =>
        (MediaSession)typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    static void Click(MainWindow window, string method) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [window, new RoutedEventArgs()]);

    static void ShowForShot(Window window)
    {
        window.WindowState = WindowState.Normal;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.Show();
        Pump(220);
        window.UpdateLayout();
    }

    static void Capture(Window window, string path, int width, int height, bool selectLayer = false, bool borderless = false)
    {
        window.WindowState = WindowState.Normal;
        window.Width = width;
        window.Height = height;
        if (borderless)
        {
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
        }
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.Opacity = 1;
        window.Show();
        Pump(220);
        window.UpdateLayout();
        // A combo opened by the first focus would otherwise cover the page in a screenshot.
        foreach (var combo in FindAll<ComboBox>(window)) combo.IsDropDownOpen = false;
        if (selectLayer && window.FindName("Layers") is ListBox layers && layers.Items.Count > 6)
        {
            layers.SelectedIndex = 6;
            Pump(120);
            window.UpdateLayout();
        }
        var content = window.Content as FrameworkElement ?? window;
        // Several dialogs leave their content transparent and paint the window behind it. Rendering
        // the content alone made those screenshots white, with white labels invisible.
        if (content is Panel panel && panel.Background is null) panel.Background = window.Background;
        else if (content is Border border && border.Background is null) border.Background = window.Background;
        else if (content is Control control && control.Background is null) control.Background = window.Background;
        content.UpdateLayout();
        var renderWidth = borderless ? width : (int)Math.Ceiling(content.ActualWidth);
        var renderHeight = borderless ? height : (int)Math.Ceiling(content.ActualHeight);
        Save(content, renderWidth, renderHeight, path);
        window.Close();
    }

    static void Save(FrameworkElement element, int width, int height, string path)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine($"{Path.GetFileName(path)}  {width}×{height}");
    }

    static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in FindAll<T>(child)) yield return nested;
        }
    }

    static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
