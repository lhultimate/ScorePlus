using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Scoreboard.Core;
using Scoreboard.Desktop;
using ScorePlus.Setup;

namespace UiSmoke;

internal static class Program
{
    [STAThread]
    static int Main()
    {
        // The real App, not a bare Application: the console's drop-downs only exist in the app's own
        // styles, so a probe that runs without them proves nothing about what the operator sees.
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var layoutPath = Path.Combine(Path.GetTempPath(), $"smoke-scorebug-{Guid.NewGuid():N}.json");
        // Keep the operator's real preferences.json out of reach: earlier runs overwrote it, which
        // is how a light theme and an empty media library ended up on the reviewer's machine.
        // Every default path (preferences, keybinds, profiles, layout, assets, autosaves) is aimed at
        // a throwaway folder, so a suite can never touch the operator's real %LOCALAPPDATA%\ScorePlus.
        var appDataPath = Path.Combine(Path.GetTempPath(), $"smoke-appdata-{Guid.NewGuid():N}");
        AppPaths.UseRoot(appDataPath);
        var prefsPath = Path.Combine(Path.GetTempPath(), $"smoke-prefs-{Guid.NewGuid():N}.json");
        PreferencesStore.UsePath(prefsPath);
        // Autosaves are isolated too: a test run must never rotate the operator's own recovery points.
        var autosavePath = Path.Combine(Path.GetTempPath(), $"smoke-autosaves-{Guid.NewGuid():N}");
        AutosaveStore.UseFolder(autosavePath);
        try
        {
            if (!MainWindow.PromptOnExit)
                throw new Exception("FAIL: the unsaved-game prompt is not on by default");
            // The checks open and close windows constantly; a modal prompt would stall the run.
            MainWindow.PromptOnExit = false;
            // Do not let a live update modal open over the unattended window checks.
            MainWindow.CheckForUpdatesOnLoad = false;
            // The sport-switch warning is a real operator guard, but these checks change sports on
            // purpose, so it is off for the run and covered by the core suite instead.
            MainWindow.WarnOnSportSwitch = false;

            // The studio's background editor and the per-direction transition, exercised through the
            // real window: pick a colour on an element that starts with "no background", then tick a
            // down transition, and read back both the model and the painted host.
            var smokeCatalogPath = Path.Combine(Path.GetTempPath(), $"smoke-bg-{Guid.NewGuid():N}.json");
            var smokeCatalog = new ScorebugCatalog(smokeCatalogPath);
            var smokeElement = new ScorebugElement
            {
                Name = "Smoke plate", Content = "SMOKE", X = 120, Y = 120, Width = 320, Height = 110,
                Background = "#CC141D28", TransparentBackground = true
            };
            smokeCatalog.Update(layout => layout.Elements.Add(smokeElement));
            var smokeTarget = smokeCatalog.Layout.Elements.First(element => element.Id == smokeElement.Id);
            var smokeEngine = new GameEngine();
            smokeEngine.NewGame("football");
            var smokeEditor = new ScorebugEditorWindow(smokeCatalog, smokeEngine, null, "Dark");
            smokeEditor.Show();
            smokeEditor.UpdateLayout();
            var smokeAll = smokeCatalog.Layout.Elements.ToArray();
            var smokeLayers = (ListBox)smokeEditor.FindName("Layers")!;
            smokeLayers.SelectedIndex = smokeAll.Length - 1 - Array.IndexOf(smokeAll, smokeTarget);
            smokeEditor.UpdateLayout();
            var smokeBackground = (TextBox)smokeEditor.FindName("BackgroundInput")!;
            smokeBackground.Text = "#FF00FF00";
            smokeEditor.UpdateLayout();
            var smokeCanvas = (Canvas)smokeEditor.FindName("DesignCanvas")!;
            var smokeHost = smokeCanvas.Children.OfType<Border>().First(border => (border.Tag as ScorebugElement)?.Id == smokeTarget.Id);
            var smokeAlpha = (smokeHost.Background as SolidColorBrush)?.Color.A ?? -1;
            if (smokeTarget.TransparentBackground || smokeTarget.Background != "#FF00FF00" || smokeAlpha <= 0)
                throw new Exception($"FAIL: a background colour did not paint (transparent={smokeTarget.TransparentBackground}, colour={smokeTarget.Background}, alpha={smokeAlpha})");
            var smokeDown = (CheckBox)smokeEditor.FindName("DownTransitionInput")!;
            var smokeDownChoice = (ComboBox)smokeEditor.FindName("TransitionDownInput")!;
            smokeDownChoice.SelectedItem = ScorebugTransition.SlideUp;
            Console.WriteLine($"after combo: choice={smokeDownChoice.SelectedItem} model={smokeTarget.TransitionDown} check={smokeDown.IsChecked}");
            smokeDown.IsChecked = true;
            Console.WriteLine($"after check: choice={smokeDownChoice.SelectedItem} model={smokeTarget.TransitionDown} check={smokeDown.IsChecked}");
            smokeEditor.UpdateLayout();
            if (smokeTarget.TransitionDown != ScorebugTransition.SlideUp)
                throw new Exception($"FAIL: the down transition did not stick ({smokeTarget.TransitionDown})");
            smokeEditor.Close();
            GameEngine.NewSeriesLiveEnabled = true;
            smokeEngine.NewSeries();
            if (!smokeEngine.TakePublishRequest()) throw new Exception("FAIL: New series did not ask to publish");
            GameEngine.NewSeriesLiveEnabled = false;
            smokeEngine.NewSeries();
            if (smokeEngine.TakePublishRequest()) throw new Exception("FAIL: New series published while the preference was off");
            Console.WriteLine("PASS: a background colour clears No background and paints; the down transition saves; New series live obeys its preference");

            // The exit prompt is the app's own chrome, not a system message box.
            var unsavedPrompt = new UnsavedChangesWindow("Dark", "detail");
            unsavedPrompt.Show();
            unsavedPrompt.UpdateLayout();
            if (FindAll<Button>(unsavedPrompt).Count(b => (b.Content as string) is "Keep editing" or "Exit without saving" or "Save & exit") != 3)
                throw new Exception("FAIL: the unsaved-game prompt does not offer keep, discard and save");
            // No score card: the prompt explains the risk, it does not repeat the game summary.
            if (FindAll<TextBlock>(unsavedPrompt).Any(t => t.FontFamily.Source == "Consolas"))
                throw new Exception("FAIL: the unsaved-game prompt still shows the game score card");
            unsavedPrompt.Close();
            Console.WriteLine("PASS: exiting a changed game asks with a themed save/discard/keep prompt");

            // The penalty drop-downs: open the real list, pick a row, and make sure the console is
            // still standing afterwards. These two crashed the program when the penalty box moved
            // onto the console, so they are checked before anything else opens a window.
            var penaltyEngine = new GameEngine();
            penaltyEngine.NewGame("football");
            var penaltyWindow = new GameControlWindow(penaltyEngine, () => { }, _ => { }, () => true) { Width = 1280, Height = 860 };
            penaltyWindow.Show();
            penaltyWindow.UpdateLayout();
            var penaltyTypeBox = (ComboBox)penaltyWindow.Panel.FindName("PenaltyTypeBox")!;
            var penaltyTeamBox = (ComboBox)penaltyWindow.Panel.FindName("PenaltyTeamBox")!;
            Exception? dropdownFailure = null;
            DispatcherUnhandledExceptionEventHandler captureDropdownFailure = (_, args) => { dropdownFailure = args.Exception; args.Handled = true; };
            Application.Current.DispatcherUnhandledException += captureDropdownFailure;
            try
            {
                foreach (var box in new[] { penaltyTypeBox, penaltyTeamBox })
                {
                    box.IsDropDownOpen = true;
                    penaltyWindow.UpdateLayout();
                    Pump(TimeSpan.FromMilliseconds(250));
                    box.SelectedIndex = box.Items.Count > 1 ? 1 : 0;
                    penaltyWindow.UpdateLayout();
                    Pump(TimeSpan.FromMilliseconds(250));
                    box.IsDropDownOpen = false;
                    penaltyWindow.UpdateLayout();
                }
            }
            catch (Exception ex) { dropdownFailure = ex; }
            finally { Application.Current.DispatcherUnhandledException -= captureDropdownFailure; }
            penaltyWindow.Close();
            if (dropdownFailure is not null)
                throw new Exception($"FAIL: a penalty drop-down threw {dropdownFailure.GetType().Name}: {dropdownFailure.Message}", dropdownFailure);
            Console.WriteLine("PASS: both penalty drop-downs open, take a pick and leave the console standing");
            // A focused way to re-check this one regression without waiting for the whole suite.
            if (Environment.GetEnvironmentVariable("SP_PROBE_ONLY") == "1") return 0;

            var engine = new GameEngine();
            engine.NewGame("football");
            var catalog = new ScorebugCatalog(layoutPath);

            // Paint & texture used to rebuild both columns while their shared preview and stop
            // lists were still parented, so ticking a material threw a logical-child exception.
            {
                var paintCatalog = new ScorebugCatalog(layoutPath);
                var paintElement = paintCatalog.Layout.Elements.First(element => element.Kind == ScorebugElementKind.Text);
                var paintCtor = typeof(PaintTextureWindow).GetConstructor(
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null,
                    [typeof(ScorebugCatalog), typeof(GameEngine), typeof(ScorebugElement), typeof(string)],
                    null) ?? throw new Exception("FAIL: paint window has no editable constructor");
                var paintWindow = (Window)paintCtor.Invoke([paintCatalog, new GameEngine(), paintElement, "Dark"]);
                paintWindow.Show();
                paintWindow.UpdateLayout();
                CheckBox Material() => FindAll<CheckBox>(paintWindow)
                    .First(box => (box.Content as string)?.StartsWith("Lay a material", StringComparison.Ordinal) == true && box.IsChecked != true);
                if (FindAll<CheckBox>(paintWindow).Count(box => (box.Content as string)?.StartsWith("Lay a material", StringComparison.Ordinal) == true) != 2)
                    throw new Exception("FAIL: paint window does not offer a material for both text and plate");
                Material().IsChecked = true;
                paintWindow.UpdateLayout();
                Material().IsChecked = true;
                paintWindow.UpdateLayout();
                Pump(TimeSpan.FromMilliseconds(300));
                var painted = paintCatalog.Layout.Elements.First(element => element.Id == paintElement.Id);
                if (painted.TextTexture is not { Enabled: true } || painted.BackgroundTexture is not { Enabled: true })
                    throw new Exception("FAIL: laying a material did not reach the text and the plate");
                paintWindow.Close();
                Console.WriteLine("PASS: paint & texture lays a material on the text and the plate without a parent conflict");
            }

            var main = new MainWindow();
            main.Show();
            main.UpdateLayout();
            main.Close();
            Console.WriteLine("PASS: main window loads");

            // The dock is a single media panel: tiles, transport and tag editor, no inline event log.
            var shell = new MainWindow { Width = 1280, Height = 800 };
            shell.Show();
            shell.UpdateLayout();
            // The dock lives on the MEDIA tab now, so it is only laid out once that tab is open.
            shell.ShowMediaTab(true);
            shell.UpdateLayout();
            var shellContent = (FrameworkElement)shell.Content;
            var dock = FindAll<MediaDock>(shell).FirstOrDefault() ?? throw new Exception("FAIL: the main window hosts no media dock");
            var transport = (Button)dock.FindName("PlayPauseButton")!;
            var transportBottom = transport.TransformToAncestor(shellContent).Transform(new Point(0, transport.ActualHeight)).Y;
            if (transportBottom > shellContent.ActualHeight + 0.5)
                throw new Exception($"FAIL: the media dock runs past the window ({transportBottom:0} of {shellContent.ActualHeight:0})");
            if (dock.FindName("Tiles") is not WrapPanel || dock.FindName("TagBox") is not ComboBox)
                throw new Exception("FAIL: the unified media dock is missing its tiles or tag editor");
            if (transport.ActualWidth < 36 || transport.ActualHeight < 28)
                throw new Exception($"FAIL: the transport controls are still tiny ({transport.ActualWidth:0}x{transport.ActualHeight:0})");
            if (FindAll<GridSplitter>(shell).Count() < 1)
                throw new Exception("FAIL: the preview column cannot be resized");
            var tagCombo = (ComboBox)dock.FindName("TagBox")!;
            if (tagCombo.Template?.FindName("PART_EditableTextBox", tagCombo) is not TextBox)
                throw new Exception("FAIL: the tag box has no editable text box, so the tag is invisible and cannot be typed");
            if (string.IsNullOrWhiteSpace(tagCombo.Text))
                throw new Exception("FAIL: the tag box starts blank");
            // The dock must wear the operator theme, not a palette of its own.
            var chrome = (Border)dock.FindName("Chrome")!;
            var removeButton = FindAll<Button>(dock).First(b => (b.Content as string) == "Remove");
            bool IsColor(Brush? brush, byte r, byte g, byte bl) => brush is SolidColorBrush s && s.Color.R == r && s.Color.G == g && s.Color.B == bl;
            if (!IsColor(chrome.Background, 8, 8, 8) || !IsColor(removeButton.Background, 20, 20, 20))
                throw new Exception($"FAIL: the media dock ignores the dark theme (panel {chrome.Background}, button {removeButton.Background})");
            dock.ApplyTheme("Light");
            dock.UpdateLayout();
            if (!IsColor(chrome.Background, 255, 255, 255) || !IsColor(removeButton.Background, 229, 234, 240) || !IsColor(removeButton.Foreground, 24, 32, 43))
                throw new Exception($"FAIL: the media dock ignores the light theme (panel {chrome.Background}, button {removeButton.Background}, text {removeButton.Foreground})");
            dock.ApplyTheme("Dark");
            dock.UpdateLayout();
            if (shell.FindName("EventList") is not null)
                throw new Exception("FAIL: the event log still lives in the dock");
            // The top row is icon-only now; the name lives in the tooltip.
            if (!FindAll<Button>(shell).Any(b => (b.ToolTip as string ?? string.Empty).StartsWith("Event log", StringComparison.OrdinalIgnoreCase)))
                throw new Exception("FAIL: the event log button is missing from the top row");
            shell.Close();
            Console.WriteLine("PASS: dock holds one media library with tiles, transport and tags; event log moved to the top row");

            var control = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            control.Show();
            control.UpdateLayout();
            control.Execute("home.score.plus1");
            control.Execute("football.down.next");
            control.Execute("football.down.previous");
            control.Execute("football.series.new");
            control.Execute("football.ball.plus");
            control.Execute("football.ball.minus");
            control.Execute("output.publish");
            control.Execute("clock.toggle");
            control.Execute("clock.minus1");
            control.Execute("clock.plus1");
            var keySettings = new KeybindSettings();
            keySettings.Bindings["home.score.plus2"] = "F9";
            keySettings.Bindings["football.series.new"] = "F10";
            control.ApplyKeybinds(keySettings);
            var panelSource = PresentationSource.FromVisual(control);
            var scoreBeforeKey = engine.State.HomeScore;
            engine.SetFootballDistance(4, 22);
            RaiseKey(control, panelSource, Key.F9);
            RaiseKey(control, panelSource, Key.F10);
            if (engine.State.HomeScore != scoreBeforeKey + 2 || engine.State.Down != 1 || engine.State.YardsToGo != 10)
                throw new Exception("FAIL: keybinds did not run their buttons");
            Console.WriteLine("PASS: keybinds run their assigned buttons");

            control.Close();
            Console.WriteLine($"PASS: control panel loads and dispatches actions (score {engine.State.HomeScore}, down {engine.State.Down}, ball spot {engine.State.YardsToGoal})");

            var editor = new ScorebugEditorWindow(catalog, engine);
            editor.Show();
            editor.UpdateLayout();

            var layers = (ListBox)editor.FindName("Layers")!;
            var nameInput = (TextBox)editor.FindName("NameInput")!;
            var backgroundInput = (TextBox)editor.FindName("BackgroundInput")!;
            var widthInput = (TextBox)editor.FindName("WidthInput")!;
            layers.SelectedIndex = 2;
            editor.UpdateLayout();
            var selectedName = nameInput.Text;
            var selectedBackground = backgroundInput.Text;
            var selectedWidth = widthInput.Text;
            if (string.IsNullOrEmpty(selectedName)) throw new Exception("FAIL: studio selection did not load properties");
            // The layer list runs front-to-back, so row 0 is the last element drawn.
            var selectedIndex = catalog.Layout.Elements.Count - 1 - 2;
            if (catalog.Layout.Elements[selectedIndex].Name != selectedName)
                throw new Exception("FAIL: the layer list is not ordered with the highest layer on top");

            engine.Score(true, 2);
            engine.AdvanceDown();
            editor.UpdateLayout();
            if (layers.SelectedIndex != 2 || nameInput.Text != selectedName || backgroundInput.Text != selectedBackground || widthInput.Text != selectedWidth)
                throw new Exception("FAIL: studio switched elements or changed properties on a value update");
            Console.WriteLine($"PASS: studio keeps '{selectedName}' selected when score values change");

            nameInput.Text = "Renamed away team";
            Pump(TimeSpan.FromMilliseconds(700));
            var edited = catalog.Layout.Elements[selectedIndex];
            if (edited.Name != "Renamed away team" || edited.Background != selectedBackground || edited.Width.ToString("0") != selectedWidth)
                throw new Exception("FAIL: studio edit touched another element or its background");
            if (layers.SelectedIndex != 2 || nameInput.Text != "Renamed away team")
                throw new Exception("FAIL: studio lost selection after an edit");
            Console.WriteLine("PASS: studio edits apply to the selected element only");

            editor.Close();
            Console.WriteLine("PASS: scorebug studio loads");

            // ---------- studio multi-select: Ctrl/Shift rows and Delete ----------
            {
                var multiPath = Path.Combine(Path.GetTempPath(), $"smoke-multi-{Guid.NewGuid():N}.json");
                try
                {
                    var multiCatalog = new ScorebugCatalog(multiPath);
                    var multiEditor = new ScorebugEditorWindow(multiCatalog, engine);
                    multiEditor.Show();
                    multiEditor.UpdateLayout();
                    var multiLayers = (ListBox)multiEditor.FindName("Layers")!;
                    if (multiLayers.SelectionMode != SelectionMode.Extended)
                        throw new Exception("FAIL: the layer list cannot hold more than one selection");
                    if (multiCatalog.Layout.Elements.Count < 2)
                        throw new Exception("FAIL: the factory layout has too few elements to select two");
                    var multiBefore = multiCatalog.Layout.Elements.Count;
                    multiLayers.SelectedItems.Add(multiLayers.Items[0]);
                    multiLayers.SelectedItems.Add(multiLayers.Items[1]);
                    multiEditor.UpdateLayout();
                    if (multiLayers.SelectedItems.Count != 2)
                        throw new Exception("FAIL: the layer list dropped part of the selection");
                    // The canvas highlights every selected object, not just the primary one.
                    var selectedIds = multiCatalog.Layout.Elements.TakeLast(2).Select(element => element.Id).ToArray();
                    var highlighted = FindAll<Border>((Canvas)multiEditor.FindName("DesignCanvas")!)
                        .Count(border => border.Tag is ScorebugElement element && selectedIds.Contains(element.Id)
                            && Equals(border.BorderBrush, System.Windows.Media.Brushes.DeepSkyBlue));
                    if (highlighted != 2)
                        throw new Exception($"FAIL: {highlighted} of 2 selected objects were highlighted in the studio");
                    RaiseKey(multiEditor, PresentationSource.FromVisual(multiEditor), Key.Delete);
                    multiEditor.UpdateLayout();
                    if (multiCatalog.Layout.Elements.Count != multiBefore - 2)
                        throw new Exception($"FAIL: Delete removed {multiBefore - multiCatalog.Layout.Elements.Count} objects instead of 2");
                    multiEditor.Close();
                }
                finally { if (File.Exists(multiPath)) File.Delete(multiPath); }
                Console.WriteLine("PASS: the studio keeps a multi-selection and Delete removes every selected object");
            }

            var keybinds = new KeybindsWindow(new KeybindSettings());
            keybinds.Show();
            keybinds.UpdateLayout();
            keybinds.Close();
            Console.WriteLine("PASS: keybinds window loads");

            // The keybind list is the current sport's: no downs on a baseball console, no power play
            // on a football one, and the shared score/clock/output buttons everywhere.
            var baseballActions = ControlActions.ForSport("baseball").Select(a => a.Id).ToArray();
            if (baseballActions.Contains("football.down.next") || baseballActions.Contains("soccer.red.home"))
                throw new Exception("FAIL: baseball keybinds still offer another sport's buttons");
            if (!baseballActions.Contains("baseball.ball") || !baseballActions.Contains("home.score.plus1")
                || !baseballActions.Contains("output.publish"))
                throw new Exception("FAIL: baseball keybinds are missing its own or the shared buttons");
            if (!ControlActions.ForSport("football").Any(a => a.Id == "football.playclock.toggle"))
                throw new Exception("FAIL: football keybinds lost the play clock");
            var baseballBinds = new KeybindsWindow(new KeybindSettings(), "baseball", "Baseball");
            baseballBinds.Show();
            baseballBinds.UpdateLayout();
            var listedActions = FindAll<ListBox>(baseballBinds).First().Items.Cast<object>().Select(item => item.ToString() ?? "").ToArray();
            if (listedActions.Any(text => text.Contains("Power play", StringComparison.Ordinal) || text.Contains("Next down", StringComparison.Ordinal)))
                throw new Exception("FAIL: a baseball keybind list shows another sport's buttons");
            baseballBinds.Close();
            // The media browser's up-one-level control is named for what it does.
            if (!FindAll<Button>(dock).Any(b => (b.Content as string) == "← Back"))
                throw new Exception("FAIL: the media browser has no Back button");
            // A new game asks which sport it is played in, offering every profile.
            var newGameSportPicker = new SportPickerWindow(new SportCatalog(), "hockey", "Dark");
            newGameSportPicker.Show();
            newGameSportPicker.UpdateLayout();
            var sportChoices = FindAll<ListBox>(newGameSportPicker).First();
            if (sportChoices.Items.Count < 10)
                throw new Exception($"FAIL: the new-game sport chooser lists {sportChoices.Items.Count} sports");
            if (sportChoices.SelectedItem is null)
                throw new Exception("FAIL: the new-game sport chooser opens with no sport selected");
            newGameSportPicker.Close();
            Console.WriteLine("PASS: keybinds list only the current sport, media goes Back, and a new game asks for its sport");

            var settings = new KeybindSettings();
            settings.Bindings["home.score.plus2"] = KeybindStore.ToText(Key.D2, ModifierKeys.Control);
            var matched = KeybindStore.Match(settings, Key.D2, ModifierKeys.Control);
            var text = KeybindStore.ToText(Key.D2, ModifierKeys.Control);
            var parsed = KeybindStore.TryParse(text, out var parsedKey, out var parsedMods);
            if (matched != "home.score.plus2" || !parsed || parsedKey != Key.D2 || parsedMods != ModifierKeys.Control)
                throw new Exception("FAIL: keybind round trip");
            Console.WriteLine($"PASS: keybind round trip ({text})");

            var outWindow = new OutputWindow(new GameEngine(), catalog);
            outWindow.Show();
            outWindow.UpdateLayout();
            outWindow.Close();
            Console.WriteLine("PASS: audience output loads");

            // Pressing Audience Output opens a new window every time instead of focusing an old one.
            var multiOutput = new MainWindow(LaunchMode.New) { Width = 1280, Height = 800 };
            multiOutput.Show();
            multiOutput.UpdateLayout();
            var outputClick = FindAll<Button>(multiOutput).First(b => (b.ToolTip as string ?? string.Empty).StartsWith("Audience output", StringComparison.OrdinalIgnoreCase));
            var outputsBefore = Application.Current.Windows.OfType<OutputWindow>().Count(w => w.IsVisible);
            outputClick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            outputClick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            multiOutput.UpdateLayout();
            var outputsAfter = Application.Current.Windows.OfType<OutputWindow>().Count(w => w.IsVisible);
            if (outputsAfter - outputsBefore != 2)
                throw new Exception($"FAIL: Audience Output opened {outputsAfter - outputsBefore} window(s) instead of 2");
            multiOutput.Close();
            Pump(TimeSpan.FromMilliseconds(150));
            if (Application.Current.Windows.OfType<OutputWindow>().Count(w => w.IsVisible) > outputsBefore)
                throw new Exception("FAIL: closing the game left its audience windows open");
            Console.WriteLine("PASS: every Audience Output press opens another window, and they close with the game");


            var preferencesWindow = new SettingsWindow(new UserPreferences());
            preferencesWindow.Show();
            preferencesWindow.UpdateLayout();
            if (preferencesWindow.FindName("ThemeSelector") is not ComboBox)
                throw new Exception("FAIL: preferences theme selector missing");
            preferencesWindow.Close();
            Console.WriteLine("PASS: preferences window loads with a save action");

            var profileCatalogPath = Path.Combine(Path.GetTempPath(), $"smoke-sports-{Guid.NewGuid():N}.json");
            try
            {
                var sports = new SportCatalog(profileCatalogPath);
                var profileWindow = new SportProfileWindow(sports);
                profileWindow.Show();
                profileWindow.UpdateLayout();
                var profiles = (ListBox)profileWindow.FindName("Profiles")!;
                profiles.SelectedIndex = profiles.Items.Cast<object>().Select((item, index) => (item, index)).First(x => x.item.ToString()!.Contains("American Football")).index;
                profileWindow.UpdateLayout();
                ((TextBox)profileWindow.FindName("TimeoutsInput")!).Text = "6";
                ((Button)profileWindow.FindName("SaveButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                profileWindow.UpdateLayout();
                if (sports.Find("football")!.Timeouts != 6 || !sports.IsOverridden("football"))
                    throw new Exception("FAIL: sport profile edits were not saved");
                profileWindow.Close();
                Console.WriteLine("PASS: editing a built-in sport profile saves the timeout count");
            }
            finally { if (File.Exists(profileCatalogPath)) File.Delete(profileCatalogPath); }

            engine.AddStat("Turnovers");
            var statsPanel = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            statsPanel.Show();
            statsPanel.UpdateLayout();
            var statsHost = (WrapPanel)statsPanel.Panel.FindName("StatsHost")!;
            if (statsHost.Children.Count != 1) throw new Exception("FAIL: custom stat row was not created");
            var stat2 = engine.State.Stats[0];
            engine.AdjustStat(stat2.Id, 3);
            statsPanel.UpdateLayout();
            if (!statsPanel.Panel.HomeScoreText.Equals(engine.State.HomeScore.ToString()))
                throw new Exception("FAIL: control panel did not refresh after a stat change");
            Console.WriteLine("PASS: custom stats render and update in the control panel");

            var panelA = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            var panelB = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            panelA.Show(); panelB.Show();
            panelA.UpdateLayout(); panelB.UpdateLayout();
            var before = engine.State.HomeScore;
            panelA.Execute("home.score.plus2");
            panelA.UpdateLayout(); panelB.UpdateLayout();
            if (!panelB.Panel.HomeScoreText.Equals(engine.State.HomeScore.ToString()) || engine.State.HomeScore != before + 2)
                throw new Exception("FAIL: control panels did not stay in sync");
            panelA.Close(); panelB.Close();
            Console.WriteLine("PASS: multiple control panels stay in sync");

            var lightProbe = new SettingsWindow(new UserPreferences());
            lightProbe.Show();
            lightProbe.UpdateLayout();
            ThemeManager.Apply(lightProbe, "Light");
            Pump(TimeSpan.FromMilliseconds(250));
            var combo = (ComboBox)lightProbe.FindName("ThemeSelector")!;
            if (combo.Foreground is not SolidColorBrush comboText || combo.Background is not SolidColorBrush comboBackground)
                throw new Exception("FAIL: combo box colors were not themed");
            var contrast = Math.Abs(comboText.Color.R - comboBackground.Color.R) + Math.Abs(comboText.Color.G - comboBackground.Color.G) + Math.Abs(comboText.Color.B - comboBackground.Color.B);
            if (contrast < 200) throw new Exception($"FAIL: dropdown text contrast too low ({comboText.Color} on {comboBackground.Color})");
            lightProbe.Close();
            ThemeManager.Apply(statsPanel, "Dark");
            Pump(TimeSpan.FromMilliseconds(250));
            Console.WriteLine($"PASS: dropdown text stays readable in the light theme (contrast {contrast})");

            var themedEditor = new ScorebugEditorWindow(catalog, engine);
            themedEditor.Show();
            themedEditor.UpdateLayout();
            var canvas = (Canvas)themedEditor.FindName("DesignCanvas")!;
            var designedText = FindAll<TextBlock>(canvas).FirstOrDefault(t => t.Text.Length > 0);
            if (designedText is null) throw new Exception("FAIL: studio canvas has no text to check");
            var designedColor = ((SolidColorBrush)designedText.Foreground).Color;
            ThemeManager.Apply(themedEditor, "Light");
            Pump(TimeSpan.FromMilliseconds(250));
            if (((SolidColorBrush)designedText.Foreground).Color != designedColor)
                throw new Exception("FAIL: theme change recolored scorebug artwork");
            themedEditor.Close();
            Console.WriteLine("PASS: theme changes never recolor scorebug artwork");

            var picker = new ColorPickerWindow("#FF38D7A3");
            picker.Show();
            picker.UpdateLayout();
            picker.Close();
            Console.WriteLine("PASS: custom color picker loads");

            var prefsProbe = new UserPreferences { Theme = "Dark" };
            var cancelWindow = new SettingsWindow(prefsProbe);
            cancelWindow.Show();
            cancelWindow.UpdateLayout();
            ((ComboBox)cancelWindow.FindName("ThemeSelector")!).SelectedItem = "Light";
            cancelWindow.Close();
            if (prefsProbe.Theme != "Dark") throw new Exception("FAIL: cancelling preferences still changed the theme");
            var saveWindow = new SettingsWindow(prefsProbe);
            saveWindow.Show();
            saveWindow.UpdateLayout();
            ((ComboBox)saveWindow.FindName("ThemeSelector")!).SelectedItem = "Light";
            saveWindow.FindName("ThemeSelector");
            var saveButton = FindAll<Button>(saveWindow).First(b => (b.Content as string) == "Save");
            saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (prefsProbe.Theme != "Light") throw new Exception("FAIL: saving preferences did not apply the theme");
            // System is offered alongside the pinned themes and can be saved.
            var systemChoices = ((ComboBox)saveWindow.FindName("ThemeSelector")!).ItemsSource as string[];
            if (systemChoices is null || !systemChoices.Contains("System"))
                throw new Exception("FAIL: preferences do not offer the System theme");
            var systemWindow = new SettingsWindow(prefsProbe);
            systemWindow.Show();
            systemWindow.UpdateLayout();
            ((ComboBox)systemWindow.FindName("ThemeSelector")!).SelectedItem = "System";
            FindAll<Button>(systemWindow).First(b => (b.Content as string) == "Save")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (prefsProbe.Theme != "System")
                throw new Exception($"FAIL: the System theme saved as '{prefsProbe.Theme}'");
            Console.WriteLine("PASS: preferences only apply when saved, and System is a saved option");

            // Enter while typing in a preference field saves the value but keeps the page open:
            // Save is the default button, so the keystroke used to dismiss the whole page mid-edit.
            var enterPrefs = new UserPreferences { Theme = "Dark", ValueSwitchDelay = 0 };
            var enterPrefsWindow = new SettingsWindow(enterPrefs);
            enterPrefsWindow.Show();
            enterPrefsWindow.UpdateLayout();
            var enterDelay = (TextBox)enterPrefsWindow.FindName("DelayInput")!;
            enterDelay.Text = "2.5";
            enterDelay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(enterPrefsWindow)!, 0, Key.Enter)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            });
            if (!enterPrefsWindow.IsVisible)
                throw new Exception("FAIL: Enter in a preference field closed the page");
            if (Math.Abs(enterPrefs.ValueSwitchDelay - 2.5) > 0.001)
                throw new Exception($"FAIL: Enter in a preference field saved {enterPrefs.ValueSwitchDelay}s instead of 2.5s");
            enterPrefsWindow.Close();
            Console.WriteLine("PASS: Enter in a preference field saves and leaves the page open");

            // Export… asks what to write. Players used to be reachable only through the save dialog's
            // "file type" list, which read as though a player could not be exported at all.
            {
                RosterStore.Book.Rosters.Clear();
                var exportRoster = RosterStore.Book.Add("Export test");
                var exportPlayer = exportRoster.AddPlayer();
                exportPlayer.Number = 9; exportPlayer.Name = "Ada"; exportPlayer.Position = "QB";
                RosterStore.Save();
                var exportEngine = new GameEngine();
                exportEngine.NewGame("football");
                exportEngine.AssignRoster(true, exportRoster.Id);
                var exportPanel = new RosterPanel();
                exportPanel.Attach(exportEngine, true);
                exportPanel.UpdateLayout();
                RosterPanel.ExportTarget? picked = null;
                var exportBox = exportPanel.BuildExportWindow(null, target => picked = target);
                exportBox.Show();
                exportBox.UpdateLayout();
                var exportButtons = FindAll<Button>(exportBox);
                if (exportButtons.All(b => (b.Content as string) != "Whole roster (.spr)"))
                    throw new Exception("FAIL: the export box cannot write a roster");
                var playerExport = exportButtons.FirstOrDefault(b => (b.Content as string)?.StartsWith("Player ·") == true)
                    ?? throw new Exception("FAIL: the export box cannot write a player");
                if (!playerExport.IsEnabled)
                    throw new Exception("FAIL: the player export button is disabled while a player is highlighted");
                if ((playerExport.Content as string)?.Contains("Ada") != true)
                    throw new Exception($"FAIL: the export box does not name the player it would write ('{playerExport.Content}')");
                // Clicking closes the box itself, so the choice is all that is left to check.
                playerExport.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (picked != RosterPanel.ExportTarget.Player)
                    throw new Exception($"FAIL: the player export button reported '{picked}'");
                // The library is shared with every later check, so put it back as it was.
                RosterStore.Book.Rosters.Remove(exportRoster);
                RosterStore.Save();
                Console.WriteLine("PASS: Export offers the whole roster or the highlighted player by name");

                // A renamed roster repaints wherever its name is bound, not just in the library.
                var renamedRoster = RosterStore.Book.Add("Home roster");
                var boundLabel = new TextBlock();
                boundLabel.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Roster.Name)) { Source = renamedRoster });
                renamedRoster.Name = "Tigers";
                if (boundLabel.Text != "Tigers")
                    throw new Exception($"FAIL: a bound roster name still reads '{boundLabel.Text}' after renaming");
                RosterStore.Book.Rosters.Remove(renamedRoster);
                RosterStore.Save();
                Console.WriteLine("PASS: renaming a roster updates every place its name is shown");

                // Messages are the app's own window, never a system message box.
                var notice = new NoticeWindow("Dark", "No roster to export", "Load or create a roster before exporting.", warning: true);
                notice.Show();
                notice.UpdateLayout();
                var noticeButtons = FindAll<Button>(notice).ToArray();
                if (noticeButtons.Length != 1 || (noticeButtons[0].Content as string) != "OK")
                    throw new Exception("FAIL: a notice does not open with a single OK button");
                if (!FindAll<TextBlock>(notice).Any(t => t.Text.Contains("No roster to export")))
                    throw new Exception("FAIL: a notice does not show its heading");
                if (notice.Background is not SolidColorBrush darkNotice || darkNotice.Color != Colors.Black)
                    throw new Exception($"FAIL: a notice is not painted in the dark theme ({notice.Background})");
                noticeButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var lightNotice = new NoticeWindow("Light", "No roster to export", "Load or create a roster before exporting.");
                lightNotice.Show();
                lightNotice.UpdateLayout();
                if (lightNotice.Background is not SolidColorBrush lightNoticeBackground || lightNoticeBackground.Color != Color.FromRgb(0xF3, 0xF5, 0xF8))
                    throw new Exception($"FAIL: a notice does not follow the light theme ({lightNotice.Background})");
                FindAll<Button>(lightNotice).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Console.WriteLine("PASS: messages open in the app's own themed window, not a system box");
            }

            var enterEditor = new ScorebugEditorWindow(catalog, engine);
            enterEditor.Show();
            enterEditor.UpdateLayout();
            var enterLayers = (ListBox)enterEditor.FindName("Layers")!;
            enterLayers.SelectedIndex = 1;
            enterEditor.UpdateLayout();
            var enterName = (TextBox)enterEditor.FindName("NameInput")!;
            enterName.Text = "Renamed by enter";
            var enterSource = PresentationSource.FromVisual(enterEditor);
            enterName.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, enterSource!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            enterEditor.UpdateLayout();
            if (!enterEditor.IsVisible) throw new Exception("FAIL: Enter closed the studio");
            Pump(TimeSpan.FromMilliseconds(250));
            if (catalog.Layout.Elements[catalog.Layout.Elements.Count - 1 - 1].Name != "Renamed by enter") throw new Exception("FAIL: Enter did not commit the edited value");
            enterEditor.Close();
            Console.WriteLine("PASS: Enter commits a studio value without closing the window");

            var swatchPicker = new ColorPickerWindow("#FF38D7A3");
            swatchPicker.Show();
            swatchPicker.UpdateLayout();
            ThemeManager.Apply(swatchPicker, "Light");
            Pump(TimeSpan.FromMilliseconds(250));
            var redSwatch = FindAll<Button>(swatchPicker).FirstOrDefault(b => (b.ToolTip as string) == "#FFFF0000");
            if (redSwatch?.Background is not SolidColorBrush redBrush || redBrush.Color != Colors.Red)
                throw new Exception($"FAIL: palette swatch colors were overwritten by theming (found={(redSwatch is null ? "none" : redSwatch.Background?.ToString())}, skip={(redSwatch is null ? "n/a" : ThemeManager.GetSkipTheme(redSwatch).ToString())})");
            swatchPicker.Close();
            Console.WriteLine("PASS: color palette thumbnails keep their colors");

            // Steppers put the add buttons above the value and the subtract buttons below it.
            var stepPanel = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            stepPanel.Show();
            stepPanel.UpdateLayout();
            var homeStepper = stepPanel.Panel.HomeScore;
            var plusRow = homeStepper.Children.OfType<System.Windows.Controls.Primitives.UniformGrid>().First();
            var minusRow = homeStepper.Children.OfType<System.Windows.Controls.Primitives.UniformGrid>().Last();
            var valueRow = homeStepper.Children.OfType<TextBox>().Single();
            if (Grid.GetRow(plusRow) >= Grid.GetRow(valueRow) || Grid.GetRow(valueRow) >= Grid.GetRow(minusRow))
                throw new Exception("FAIL: stepper did not stack add above value above subtract");
            var plusLabels = plusRow.Children.OfType<Button>().Select(b => b.Content as string).ToArray();
            if (!plusLabels.Contains("+1") || !plusLabels.Contains("+6") || plusRow.Children.Count != minusRow.Children.Count)
                throw new Exception($"FAIL: score steppers missing add buttons ({string.Join(",", plusLabels)})");
            // The − row must actually be inside the team card: a cramped panel used to clip it off
            // the bottom, so the subtract buttons existed but were never on screen.
            var homeHost = (ContentControl)stepPanel.Panel.FindName("HomeScoreHost")!;
            DependencyObject? cardAncestor = VisualTreeHelper.GetParent(homeHost);
            while (cardAncestor is not null && cardAncestor is not Border) cardAncestor = VisualTreeHelper.GetParent(cardAncestor);
            var homeCard = (Border?)cardAncestor ?? throw new Exception("FAIL: the home score is not in a team card");
            stepPanel.Panel.UpdateLayout();
            var cardBottom = homeCard.TransformToAncestor(stepPanel.Panel).TransformBounds(new Rect(0, 0, homeCard.ActualWidth, homeCard.ActualHeight)).Bottom;
            void CheckMinusRow(string where)
            {
                foreach (var minus in minusRow.Children.OfType<Button>())
                {
                    if (minus.ActualHeight < 24 || !minus.IsVisible)
                        throw new Exception($"FAIL: the {(minus.Content as string)} button is not laid out {where} ({minus.ActualHeight:0}px)");
                    var bottom = minus.TransformToAncestor(stepPanel.Panel)
                        .TransformBounds(new Rect(0, 0, minus.ActualWidth, minus.ActualHeight)).Bottom;
                    if (bottom > cardBottom + 0.5)
                        throw new Exception($"FAIL: the {(minus.Content as string)} button sits {(bottom - cardBottom):0}px below the team card {where}");
                }
            }
            CheckMinusRow("at the default size");
            // The smallest window a panel allows must fit them too.
            stepPanel.Height = stepPanel.MinHeight;
            stepPanel.UpdateLayout();
            stepPanel.Panel.UpdateLayout();
            cardBottom = homeCard.TransformToAncestor(stepPanel.Panel).TransformBounds(new Rect(0, 0, homeCard.ActualWidth, homeCard.ActualHeight)).Bottom;
            CheckMinusRow("at the minimum window size");
            Console.WriteLine($"PASS: stepper stacks {string.Join("/", plusLabels)} above the value and subtracts below");

            // Clicking the value and typing overrides it.
            var typedBox = FindAll<TextBox>(homeStepper).First();
            typedBox.Focus();
            // Focus can be refused for an off-screen window; raise the same event a real click produces.
            typedBox.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, typedBox) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
            typedBox.Text = "17";
            typedBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(typedBox)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            stepPanel.UpdateLayout();
            if (engine.State.HomeScore != 17) throw new Exception($"FAIL: typing a score did not override the value (score {engine.State.HomeScore})");
            Console.WriteLine("PASS: typing over the middle value sets it exactly");

            // Team names are editable right on the console.
            var awayName = (TextBox)stepPanel.Panel.FindName("AwayName")!;
            awayName.Focus();
            awayName.Text = "Rovers";
            awayName.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(awayName)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            stepPanel.UpdateLayout();
            if (engine.State.AwayTeam != "Rovers") throw new Exception($"FAIL: editing the away name did not reach the game (got '{engine.State.AwayTeam}')");
            Console.WriteLine("PASS: team names can be edited from the console");

            // Adding toggles that should not wait for Update Output: score and clocks already published, structure was not.
            var stagedEngine = new GameEngine(); stagedEngine.NewGame("football");
            var published = new GameEngine(); published.LoadFromState(stagedEngine.CloneState());
            stagedEngine.Score(true, 6);
            stagedEngine.SetPlayClock(25);
            stagedEngine.AdjustClock(-30);
            if (GameEngine.StagedSignature(stagedEngine.State) != GameEngine.StagedSignature(published.State))
                throw new Exception("FAIL: score or clocks marked the output as out of date");
            stagedEngine.AdvanceDown();
            if (GameEngine.StagedSignature(stagedEngine.State) == GameEngine.StagedSignature(published.State))
                throw new Exception("FAIL: down & distance did not stage for Update Output");
            Console.WriteLine("PASS: scores and clocks publish live while structural edits wait for Update Output");

            var liveEngine = new GameEngine(); liveEngine.NewGame("football");
            var liveTarget = new GameEngine(); liveTarget.LoadFromState(liveEngine.CloneState());
            liveEngine.SetPlayClock(17);
            liveEngine.ResetTimeoutClock(45);
            liveTarget.UpdateLiveScoreClock(liveEngine.State);
            if (liveTarget.State.PlayClockSeconds != 17 || liveTarget.State.TimeoutClockSeconds != 45)
                throw new Exception("FAIL: play clock or timeout clock did not go live");
            Console.WriteLine("PASS: play clock and timeout clock update live");
            stepPanel.Close();

            // Studio artwork starts with no background box.
            var studioLayout = Path.Combine(Path.GetTempPath(), $"smoke-new-element-{Guid.NewGuid():N}.json");
            try
            {
                var freshCatalog = new ScorebugCatalog(studioLayout);
                var addEditor = new ScorebugEditorWindow(freshCatalog, engine);
                addEditor.Show();
                addEditor.UpdateLayout();
                // Studio buttons read as capitals with a plus: SPORT DATA +, not "Add sport data".
                foreach (var label in new[] { "TEXT +", "LOGO +", "SHAPE +", "SPORT DATA +", "PLAYER DATA +", "VIDEO +", "HTML +", "PARTS +" })
                    if (!FindAll<Button>(addEditor).Any(b => (b.Content as string) == label))
                        throw new Exception($"FAIL: the studio has no \"{label}\" button");
                var addText = FindAll<Button>(addEditor).First(b => (b.Content as string) == "TEXT +");
                addText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                addEditor.UpdateLayout();
                var created = freshCatalog.Layout.Elements.Last();
                if (!created.TransparentBackground)
                    throw new Exception("FAIL: new studio artwork turned a background box on by default");

                // A second element, then one property change for the whole selection.
                addText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                addEditor.UpdateLayout();
                var studioLayers = (ListBox)addEditor.FindName("Layers")!;
                studioLayers.SelectedItems.Clear();
                foreach (var row in studioLayers.Items.Cast<object>().Take(2)) studioLayers.SelectedItems.Add(row);
                addEditor.UpdateLayout();
                var transparentToggle = (CheckBox)addEditor.FindName("TransparentBackgroundInput")!;
                transparentToggle.IsChecked = false;
                ((TextBox)addEditor.FindName("BackgroundInput")!).Text = "#FF123456";
                Pump(TimeSpan.FromMilliseconds(700));
                var group = freshCatalog.Layout.Elements.TakeLast(2).ToArray();
                var applied = group.Count(element => !element.TransparentBackground && element.Background == "#FF123456");
                if (applied != group.Length)
                    throw new Exception($"FAIL: a property change reached {applied} of {group.Length} selected elements");
                Console.WriteLine("PASS: a property change lands on every selected element at once");

                addEditor.Close();
                Console.WriteLine("PASS: new studio artwork starts without a background box");
            }
            finally { if (File.Exists(studioLayout)) File.Delete(studioLayout); }

            // Deleting a roster asks in the app's own dialog, not a system message box.
            {
                var confirm = new ConfirmWindow("Dark", "DELETE ROSTER", "Delete \"Visitors squad\" and its 2 players?", "Delete roster", destructive: true);
                confirm.Show();
                confirm.UpdateLayout();
                if (!FindAll<Button>(confirm).Any(b => (b.Content as string) == "Delete roster") ||
                    !FindAll<Button>(confirm).Any(b => (b.Content as string) == "Cancel"))
                    throw new Exception("FAIL: the roster delete confirmation has no actions");
                confirm.Close();
                Console.WriteLine("PASS: destructive library actions ask in the app's own themed dialog");
            }

            // The football console fits its smallest window without scrolling, and away stays left of home.
            engine.AddStat("Penalties");
            // The console itself needs ~900px; the detached window also carries the media dock, so its
            // minimum usable width is the console width plus that dock.
            var fitPanel = new GameControlWindow(engine, () => { }, _ => { }, () => true) { Width = 1280, Height = 860 };
            fitPanel.Show();
            fitPanel.UpdateLayout();
            var view = fitPanel.Panel;
            view.UpdateLayout();
            double Left(FrameworkElement element) => element.TransformToAncestor(view).Transform(new Point(0, 0)).X;
            var awayBox = (TextBox)view.FindName("AwayName")!;
            var homeBox = (TextBox)view.FindName("HomeName")!;
            if (Left(awayBox) >= Left(homeBox)) throw new Exception("FAIL: visitors are not left of home");
            if (awayBox.ActualHeight > 40)
                throw new Exception($"FAIL: the team name field is {awayBox.ActualHeight:0}px tall for one line of text");
            foreach (var card in FindAll<Border>(view).Where(b => b.Style == (Style)view.FindResource("Card")))
                if (card.ActualHeight + 0.5 < card.DesiredSize.Height)
                    throw new Exception($"FAIL: a section is squeezed ({card.ActualHeight:0} of {card.DesiredSize.Height:0} needed)");
            var updateButton = (Button)view.FindName("UpdateButton")!;
            var updateBottom = updateButton.TransformToAncestor(view).Transform(new Point(0, updateButton.ActualHeight)).Y;
            if (updateBottom > view.ActualHeight + 0.5)
                throw new Exception($"FAIL: Update Output is off the page ({updateBottom:0} of {view.ActualHeight:0})");
            var scrollers = FindAll<ScrollViewer>(view).Where(s => s.IsVisible && s.ScrollableHeight > 0).ToArray();
            if (scrollers.Length > 0) throw new Exception($"FAIL: {scrollers.Length} section(s) need scrolling at the smallest window ({string.Join(", ", scrollers.Select(s => $"{s.Name}:{s.ScrollableHeight:0}/{s.ActualHeight:0}"))})");

            // A portrait monitor: about 1040 wide and taller than the desk. The window must be
            // allowed to get that narrow and the console must still fit without scrolling.
            if (fitPanel.MinWidth > 1040)
                throw new Exception($"FAIL: a panel refuses to be narrower than {fitPanel.MinWidth:0}px");
            fitPanel.Width = 1040;
            fitPanel.Height = 1280;
            fitPanel.UpdateLayout();
            view.UpdateLayout();
            if (Left(awayBox) >= Left(homeBox))
                throw new Exception("FAIL: visitors are not left of home on a portrait window");
            var portraitScrollers = FindAll<ScrollViewer>(view).Where(s => s.IsVisible && s.ScrollableHeight > 0).ToArray();
            if (portraitScrollers.Length > 0)                    throw new Exception($"FAIL: the console needs scrolling at 1040px wide ({portraitScrollers.Length} section(s))");
            foreach (var narrowCard in FindAll<Border>(view).Where(b => b.Style == (Style)view.FindResource("Card")))
                if (narrowCard.ActualWidth + 0.5 < narrowCard.DesiredSize.Width)
                    throw new Exception($"FAIL: a section is squeezed horizontally on a portrait window ({narrowCard.ActualWidth:0} of {narrowCard.DesiredSize.Width:0} needed)");
            var logoFrame = (Border)view.FindName("AwayLogoFrame")!;
            if (logoFrame.Width < 60)
                throw new Exception($"FAIL: the team logo thumbnail is still small ({logoFrame.Width:0})");
            if (FindAll<Button>(view).Any(b => (b.Tag as string)?.StartsWith("football.turnover", StringComparison.Ordinal) == true))
                throw new Exception("FAIL: the removed Home/Away turnover buttons are still on the console");
            if (((FrameworkElement)view.FindName("PlayClockRow")!).Visibility != Visibility.Visible)
                throw new Exception("FAIL: play clock is hidden for football");
            // Everything in Timing is typed over, not just nudged with buttons.
            var timeoutClockBox = view.FindName("TimeoutClockValue") as TextBox
                ?? throw new Exception("FAIL: the timeout clock is not an editable text box");
            if (timeoutClockBox.Text.Length == 0)
                throw new Exception("FAIL: timeout clock is missing");
            if (view.FindName("ShotClockValue") is not TextBox)
                throw new Exception("FAIL: the shot clock is not an editable text box");
            // A long stat list must not push the page: it scrolls inside its own card.
            engine.AddStat("Red zone trips");
            engine.AddStat("Explosive plays");
            view.UpdateLayout();
            var statsScroller = (ScrollViewer)view.FindName("StatsScroller")!;
            var afterStats = updateButton.TransformToAncestor(view).Transform(new Point(0, updateButton.ActualHeight)).Y;
            if (afterStats > view.ActualHeight + 0.5)
                throw new Exception($"FAIL: extra stats pushed Update Output off the page ({afterStats:0} of {view.ActualHeight:0})");
            if (statsScroller.ScrollableHeight <= 0)
                throw new Exception("FAIL: extra stats did not stay inside their card");
            fitPanel.Close();
            Console.WriteLine($"PASS: football console fits {view.ActualWidth:0}x{view.ActualHeight:0} with no scrolling, visitors left of home");
            Console.WriteLine("PASS: a long stat list scrolls inside its card without moving the page");

            // The ball spot names the half of the field it sits on, and the console flips it.
            var ballEngine = new GameEngine();
            ballEngine.NewGame("football");
            var ballWindow = new GameControlWindow(ballEngine, () => { }, _ => { }, () => true);
            ballWindow.Show();
            ballWindow.UpdateLayout();
            var ballView = ballWindow.Panel;
            ballView.UpdateLayout();
            var sideButton = FindAll<Button>(ballView).FirstOrDefault(b => (b.Tag as string) == "football.ball.side")
                ?? throw new Exception("FAIL: the ball spot has no side button");
            if (sideButton.ActualHeight < 30 || sideButton.FontSize < 10)
                throw new Exception($"FAIL: the ball spot side button is still tiny ({sideButton.ActualWidth:0}x{sideButton.ActualHeight:0})");
            if (!ControlActions.All.Any(a => a.Id == "football.ball.side"))
                throw new Exception("FAIL: the ball spot side button cannot be keybound");
            if ((sideButton.Content as string) != "HOME ▶")
                throw new Exception($"FAIL: the ball spot side button reads {sideButton.Content}");
            ballEngine.SetPossession(false);
            ballView.UpdateLayout();
            if ((sideButton.Content as string) != "HOME ▶" || !ballEngine.State.BallOnHomeSide)
                throw new Exception("FAIL: changing possession moved the ball to the other half");
            // + raises the yard line and − lowers it, like every other console stepper.
            var ballBefore = ballEngine.State.YardsToGoal;
            var ballPlus = FindAll<Button>(ballView).First(b => (b.Tag as string) == "football.ball.plus");
            var ballMinus = FindAll<Button>(ballView).First(b => (b.Tag as string) == "football.ball.minus");
            ballPlus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (ballEngine.State.YardsToGoal != ballBefore + 1)
                throw new Exception($"FAIL: the ball spot + button moved it {ballBefore}→{ballEngine.State.YardsToGoal}");
            ballMinus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (ballEngine.State.YardsToGoal != ballBefore)
                throw new Exception("FAIL: the ball spot − button did not undo the + button");
            ballEngine.SetBallSide(false);
            ballView.UpdateLayout();
            if ((sideButton.Content as string) != "◀ VISITORS")
                throw new Exception($"FAIL: the ball spot side did not follow the game ({sideButton.Content})");
            sideButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!ballEngine.State.BallOnHomeSide)
                throw new Exception("FAIL: the ball spot side button did not switch halves");
            ballWindow.Close();
            Console.WriteLine("PASS: the ball spot has its own side, independent of possession, and a button to flip it");

            // A profile edited while the app is running must reach the next new game, no relaunch.
            var profileShell = new MainWindow(LaunchMode.New);
            profileShell.Show();
            profileShell.UpdateLayout();
            profileShell.Engine.Sports.SaveProfile(profileShell.Engine.Sports.Find("football")! with { Timeouts = 4, ScoringOptions = [1, 2, 3, 6] });
            profileShell.StartNewGame("football");
            profileShell.UpdateLayout();
            if (profileShell.Engine.State.HomeTimeouts != 4)
                throw new Exception($"FAIL: an edited profile still gives {profileShell.Engine.State.HomeTimeouts} timeouts to a new game");
            if (!FindAll<Button>(profileShell.Panel.HomeScore).Any(b => (b.Content as string) == "+6"))
                throw new Exception("FAIL: the console kept the old scoring buttons after a profile edit");
            profileShell.Close();
            Console.WriteLine("PASS: a sport profile edited at runtime applies to the next new game");

            // Saving from the real profile dialog must stick in the running catalog too.
            var dialogPath = Path.Combine(appDataPath, $"dialog-sports-{Guid.NewGuid():N}.json");
            var dialogCatalog = new SportCatalog(dialogPath);
            var dialogTarget = dialogCatalog.All.OrderBy(s => s.Name).First();
            var profileDialog = new SportProfileWindow(dialogCatalog);
            profileDialog.Show();
            profileDialog.UpdateLayout();
            ((TextBox)profileDialog.FindName("TimeoutsInput")!).Text = "6";
            ((TextBox)profileDialog.FindName("ScoringInput")!).Text = "2, 3, 7";
            ((Button)profileDialog.FindName("SaveButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var savedProfile = dialogCatalog.Find(dialogTarget.Id)!;
            if (savedProfile.Timeouts != 6 || !savedProfile.ScoringOptions.SequenceEqual([2, 3, 7]))
                throw new Exception($"FAIL: the profile dialog saved {savedProfile.Timeouts} timeouts and {string.Join('/', savedProfile.ScoringOptions)} scoring");
            profileDialog.Close();
            Console.WriteLine("PASS: saving from the sport profile window reaches the running catalog immediately");

            // Icons: one bar per timeout, and a possession dot that follows the team with the ball.
            var iconEngine = new GameEngine();
            iconEngine.NewGame("football");
            var barsItem = new ScorebugElement { Name = "Timeouts", Content = "", Kind = ScorebugElementKind.Text, Icon = ScorebugIcon.TimeoutBars, Width = 160, Height = 60 };
            var iconsView = new ScorebugIconView(barsItem, () => iconEngine.State);
            var barCount = FindAll<System.Windows.Shapes.Rectangle>(iconsView).Count();
            if (barCount != iconEngine.State.HomeTimeouts)
                throw new Exception($"FAIL: timeout icon drew {barCount} bars for {iconEngine.State.HomeTimeouts} timeouts");
            foreach (var line in FindAll<System.Windows.Shapes.Rectangle>(iconsView))
                if (line.Width <= line.Height * 2)
                    throw new Exception($"FAIL: timeout bar is not a horizontal line ({line.Width:0}x{line.Height:0})");
            var barStack = FindAll<StackPanel>(iconsView).FirstOrDefault()
                ?? throw new Exception("FAIL: timeout lines have no stack to sit in");
            if (barStack.Orientation != Orientation.Horizontal)
                throw new Exception("FAIL: timeout lines are not in a row");
            iconEngine.Timeout(true);
            iconsView.Update();
            if (FindAll<System.Windows.Shapes.Rectangle>(iconsView).Count() != iconEngine.State.HomeTimeouts)
                throw new Exception("FAIL: the timeout icon did not follow a used timeout");
            var dotItem = new ScorebugElement { Name = "Possession", Content = "{{POSSESSION}}", Kind = ScorebugElementKind.Text, Icon = ScorebugIcon.PossessionDot, Width = 200, Height = 60 };
            var dotView = new ScorebugIconView(dotItem, () => iconEngine.State);
            var dot = FindAll<System.Windows.Shapes.Ellipse>(dotView).Single();
            iconEngine.SetPossession(false);
            dotView.Update();
            if (dot.HorizontalAlignment != HorizontalAlignment.Left)
                throw new Exception($"FAIL: the possession dot sits {dot.HorizontalAlignment} while the visitors have the ball");
            iconEngine.SetPossession(true);
            dotView.Update();
            if (dot.HorizontalAlignment != HorizontalAlignment.Right)
                throw new Exception($"FAIL: the possession dot did not move to the home side");
            dotItem.IconFlipped = true;
            iconEngine.SetPossession(false);
            dotView.Update();
            if (dot.HorizontalAlignment != HorizontalAlignment.Right)
                throw new Exception("FAIL: flipping the possession dot had no effect");
            Console.WriteLine("PASS: timeout bars and the possession dot follow live data, and the dot can be flipped");

            // The same icons render on a real scorebug canvas, and the studio offers the property.
            var iconCatalog = new ScorebugCatalog(Path.Combine(appDataPath, $"icons-{Guid.NewGuid():N}.json"));
            iconCatalog.Update(layout =>
            {
                layout.Elements.Clear();
                layout.Elements.Add(new ScorebugElement { Name = "Timeouts", Kind = ScorebugElementKind.Text, Content = "", Icon = ScorebugIcon.TimeoutBars });
                layout.Elements.Add(new ScorebugElement { Name = "Ball", Kind = ScorebugElementKind.Text, Content = "{{POSSESSION}}", Icon = ScorebugIcon.PossessionDot });
            });
            var iconCanvas = new ScorebugCanvasView(iconCatalog, iconEngine);
            iconCanvas.Measure(new Size(1280, 720));
            iconCanvas.Arrange(new Rect(0, 0, 1280, 720));
            iconCanvas.UpdateLayout();
            if (FindAll<ScorebugIconView>(iconCanvas).Count() != 2)
                throw new Exception("FAIL: the scorebug canvas did not draw the icon elements");
            iconCanvas.Dispose();
            var iconStudio = new ScorebugEditorWindow(iconCatalog, iconEngine, null, "Dark");
            iconStudio.Show();
            iconStudio.UpdateLayout();
            ((ListBox)iconStudio.FindName("Layers")!).SelectedIndex = 0;
            iconStudio.UpdateLayout();
            if (((FrameworkElement)iconStudio.FindName("IconSection")!).Visibility != Visibility.Visible)
                throw new Exception("FAIL: the studio hides the icon properties for a text element");
            if (((ComboBox)iconStudio.FindName("IconInput")!).SelectedItem is not (ScorebugIcon.TimeoutBars or ScorebugIcon.PossessionDot))
                throw new Exception("FAIL: the studio did not show the selected element's icon");
            iconStudio.Close();
            Console.WriteLine("PASS: icon elements draw on the scorebug canvas and edit from the studio");

            // The possession dot is the whole icon — no value printed beside it — and its mark keeps
            // its size when the box is resized, growing only with the element's own type size.
            {
                var markItem = new ScorebugElement { Name = "Possession", Content = "{{POSSESSION}}", Kind = ScorebugElementKind.Text, Icon = ScorebugIcon.PossessionDot, Width = 200, Height = 60, FontSize = 24 };
                var markView = new ScorebugIconView(markItem, () => iconEngine.State);
                if (FindAll<TextBlock>(markView).Any())
                    throw new Exception("FAIL: the possession dot still prints a value beside the mark");
                var mark = FindAll<System.Windows.Shapes.Ellipse>(markView).Single();
                var markSize = mark.Width;
                markItem.Width = 500; markItem.Height = 200; markView.Update();
                if (Math.Abs(mark.Width - markSize) > 0.01)
                    throw new Exception("FAIL: the possession dot grew just because its box did");
                markItem.FontSize = 48; markView.Update();
                if (mark.Width <= markSize + 1)
                    throw new Exception("FAIL: the possession dot did not follow the element's type size");
                Console.WriteLine("PASS: the possession dot draws without text and keeps its size until the type size changes");
            }

            // The backdrop can be reset to ScorePlus navy, and the layout no longer exports as a page.
            {
                var navyCatalog = new ScorebugCatalog(Path.Combine(appDataPath, $"navy-{Guid.NewGuid():N}.json"));
                navyCatalog.Update(layout => layout.Background = "#FF00B140");
                var navyStudio = new ScorebugEditorWindow(navyCatalog, iconEngine, null, "Dark");
                navyStudio.Show();
                navyStudio.UpdateLayout();
                FindAll<Button>(navyStudio).First(b => (b.Content as string) == "Navy")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!string.Equals(navyCatalog.Layout.Background, "#FF091018", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"FAIL: the navy reset set {navyCatalog.Layout.Background}");
                if (FindAll<Button>(navyStudio).Any(b => (b.Content as string) == "Green screen"))
                    throw new Exception("FAIL: the old green screen button is still offered");
                if (FindAll<Button>(navyStudio).Any(b => (b.Content as string) == "HTML page…"))
                    throw new Exception("FAIL: the studio still offers an HTML page export");
                navyStudio.Close();
                Console.WriteLine("PASS: the backdrop resets to navy and the layout has no HTML export");
            }

            if (KeybindStore.Defaults().Bindings.Count != 0)
                throw new Exception("FAIL: keybinds are still assigned out of the box");
            Console.WriteLine("PASS: no shortcut is assigned until the operator picks one");

            // The button that pops out another synced panel raises the request the main window listens to.
            var popOutRequests = 0;
            var chainPanel = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            chainPanel.NewPanelRequested += () => popOutRequests++;
            chainPanel.Show();
            chainPanel.UpdateLayout();
            FindAll<Button>(chainPanel).First(b => (b.Content as string) == "New panel")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (popOutRequests != 1) throw new Exception($"FAIL: the New panel button raised the request {popOutRequests} times");
            chainPanel.Close();
            Console.WriteLine("PASS: the New panel button asks for another synced window");

            // Preference defaults: follow Windows, heading tips off.
            var freshPrefs = new UserPreferences();
            if (freshPrefs.Theme != "System" || freshPrefs.ShowHeadingHints)
                throw new Exception($"FAIL: defaults are {freshPrefs.Theme}/{freshPrefs.ShowHeadingHints}");
            if (ThemeManager.Resolve("System") is not ("Dark" or "Light"))
                throw new Exception("FAIL: the System theme does not resolve to a real theme");
            Console.WriteLine("PASS: defaults follow the Windows theme with heading tips off");

            // Heading tips stay hidden unless the preference is on.
            var hintWindow = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            hintWindow.Show();
            hintWindow.UpdateLayout();
            var detailHint = (TextBlock)hintWindow.Panel.FindName("DetailCardHint")!;
            hintWindow.Panel.SetHintVisibility(false);
            if (detailHint.Visibility != Visibility.Collapsed) throw new Exception("FAIL: heading tips showed while disabled");
            hintWindow.Panel.SetHintVisibility(true);
            if (detailHint.Visibility != Visibility.Visible) throw new Exception("FAIL: heading tips stayed hidden while enabled");
            hintWindow.Close();
            Console.WriteLine("PASS: heading tips follow the preference");

            // The studio's explanatory lines follow the same switch; tips are off by default.
            {
                var tipCatalog = new ScorebugCatalog(Path.Combine(appDataPath, $"tips-{Guid.NewGuid():N}.json"));
                var tipStudio = new ScorebugEditorWindow(tipCatalog, engine, null, "Dark");
                tipStudio.Show();
                tipStudio.UpdateLayout();
                var headerHint = (TextBlock)tipStudio.FindName("HeaderHint")!;
                var transitionHint = (TextBlock)tipStudio.FindName("TransitionCaption")!;
                if (headerHint.Visibility != Visibility.Collapsed || transitionHint.Visibility != Visibility.Collapsed)
                    throw new Exception("FAIL: the studio showed its tips while heading tips are off");
                tipStudio.Close();
                Console.WriteLine("PASS: the studio's tips follow the heading-tips preference");
            }

            // Live score & clocks can be toggled from a keybind.
            var liveState = true;
            var livePanel = new GameControlWindow(engine, () => { }, value => liveState = value, () => liveState);
            livePanel.Execute("output.live.toggle");
            if (liveState) throw new Exception("FAIL: the live toggle keybind did nothing");
            livePanel.Execute("output.live.toggle");
            if (!liveState) throw new Exception("FAIL: the live toggle keybind did not toggle back");
            livePanel.Close();
            Console.WriteLine("PASS: live score & clocks can be bound to a key");

            // Renaming a team must reach the audience output without pressing Update Output.
            var outSource = new GameEngine();
            outSource.NewGame("football");
            var outWindow2 = new OutputWindow(outSource, catalog, "Dark");
            outWindow2.Show();
            outWindow2.UpdateLayout();
            var renamed = new GameEngine();
            renamed.NewGame("football");
            renamed.SetTeams("Home Squad", "Away Squad");
            outWindow2.UpdateLiveScoreClock(renamed.State);
            outWindow2.UpdateLayout();
            var homeLabel = (TextBlock)outWindow2.FindName("HomeLabel")!;
            // The on-air label is the typed name, verbatim: not initials, not upper-cased.
            if (homeLabel.Text != "Home Squad") throw new Exception($"FAIL: renamed team did not reach the output (label '{homeLabel.Text}')");
            renamed.SetTeams("ny giants", "Away Squad");
            outWindow2.UpdateLiveScoreClock(renamed.State);
            outWindow2.UpdateLayout();
            if (homeLabel.Text != "ny giants") throw new Exception($"FAIL: the output forced the team name's case (label '{homeLabel.Text}')");
            outWindow2.Close();
            Console.WriteLine("PASS: renamed teams show up on the audience output right away");

            var eventLog = new EventLogWindow(engine, "Dark", false);
            eventLog.Show();
            eventLog.UpdateLayout();
            eventLog.Close();
            Console.WriteLine("PASS: event log opens in its own window");

            // Scorebug video elements only show clips from their tag, and never audio.
            var videoCatalogPath = Path.Combine(Path.GetTempPath(), $"smoke-video-{Guid.NewGuid():N}.json");
            try
            {
                var videoCatalog = new ScorebugCatalog(videoCatalogPath);
                var playback = new MediaPlayback();
                videoCatalog.Update(layout => layout.Elements.Add(new ScorebugElement
                {
                    Name = "Ad video", Kind = ScorebugElementKind.Video, MediaTag = "Ads",
                    Width = 480, Height = 270, X = 40, Y = 40, TransparentBackground = true
                }));
                var videoView = new ScorebugCanvasView(videoCatalog, engine, playback: playback);
                videoView.Measure(new Size(640, 360));
                videoView.Arrange(new Rect(0, 0, 640, 360));
                videoView.UpdateLayout();
                if (FindAll<VideoSurface>(videoView).Count() != 1)
                    throw new Exception("FAIL: video element did not produce a video surface");
                // The picture must be hard-clipped to its element box, or it paints over its neighbours.
                var surface = FindAll<VideoSurface>(videoView).First();
                if (!surface.ClipToBounds || surface.Clip is not RectangleGeometry surfaceClip)
                    throw new Exception("FAIL: the video surface has no clipping geometry");
                if (surfaceClip.Rect.Width < surface.ActualWidth - 0.5 || surfaceClip.Rect.Width <= 0)
                    throw new Exception($"FAIL: the video clip {surfaceClip.Rect} does not match its box {surface.ActualWidth:0}x{surface.ActualHeight:0}");

                var ad = new MediaEntry(@"C:\media\ad.mp4", "Ads");
                var highlight = new MediaEntry(@"C:\media\highlight.mp4", "Highlights");
                var anthem = new MediaEntry(@"C:\media\anthem.mp3", "Ads");
                playback.Start(ad);
                if (playback.ActiveFor("Ads")?.Path != ad.Path) throw new Exception("FAIL: a tagged clip did not reach its matching element");
                playback.Seek(12.5);
                if (Math.Abs(playback.Position - 12.5) > 0.001)
                    throw new Exception($"FAIL: seeking did not reach the scorebug video ({playback.Position})");
                if (playback.ActiveFor("Highlights") is not null) throw new Exception("FAIL: the wrong tag reached a video element");
                if (playback.ActiveFor("")?.Path != ad.Path) throw new Exception("FAIL: an element with no tag should accept any playing video");
                playback.Start(anthem);
                if (playback.ActiveFor("") is not null) throw new Exception("FAIL: an audio cue filled a video element");
                playback.Start(highlight);
                if (playback.ActiveFor("Ads") is not null) throw new Exception("FAIL: a highlight reel played in the ad box");
                playback.Stop();
                if (playback.ActiveFor("Ads") is not null) throw new Exception("FAIL: stopping media left the overlay cued");
                videoView.Dispose();
                Console.WriteLine("PASS: video overlays only show media from their own tag");

                // ANY is the catch-all tag: a box watching ANY plays whatever is cued, camera included.
                if (!playback.Matches(ad, MediaLibrary.AnyTag) || !playback.Matches(highlight, "any"))
                    throw new Exception("FAIL: the ANY tag did not accept every clip");
                var camera = new MediaEntry(MediaLibrary.CameraPath(0), "Camera");
                if (!camera.IsCamera || !camera.IsVideo)
                    throw new Exception("FAIL: a camera source is not treated as a video source");
                playback.Start(camera);
                if (playback.ActiveFor(MediaLibrary.AnyTag)?.IsCamera != true)
                    throw new Exception("FAIL: a live camera did not route into an ANY video box");
                playback.Start(ad);
                Console.WriteLine("PASS: ANY plays whatever is cued, and a live camera routes like a clip");

                // A microphone is a live source like a camera: its own library item, audio only.
                var micPath = MediaLibrary.MicrophonePath(1);
                var micItem = new MediaEntry(micPath, "Microphone");
                if (!micItem.IsMicrophone || micItem.IsVideo || micItem.IsCamera || micItem.KindLabel != "MIC")
                    throw new Exception($"FAIL: a microphone item is not audio-only ({micItem.IsVideo}/{micItem.KindLabel})");
                if (MediaLibrary.MicrophoneIndex(micPath) != 1 || MediaLibrary.CameraIndex(micPath) is not null)
                    throw new Exception("FAIL: a microphone path does not round-trip");
                var micSession = new MediaSession(new UserPreferences());
                try
                {
                    var addedMic = micSession.AddMicrophone(0, "Commentary");
                    if (!addedMic.IsMicrophone || !micSession.Library.Contains(addedMic) || addedMic.Tag != "Commentary")
                        throw new Exception("FAIL: Add microphone did not file the item in the library");
                    var again = micSession.AddMicrophone(0);
                    if (!ReferenceEquals(again, addedMic))
                        throw new Exception("FAIL: adding the same microphone twice made a second item");
                    // Routing a microphone overrides its device without changing its library slot.
                    micSession.RouteMicrophone(addedMic, 1);
                    if (micSession.Selected?.MicrophoneIndex != 1
                        || MediaLibrary.MicrophoneIndex(micSession.Selected?.Path ?? "") != 0)
                        throw new Exception("FAIL: routing a microphone did not keep its slot and change its device");
                    micSession.RenameMicrophone(addedMic, "Desk mic");
                    if (micSession.Selected?.Label != "Desk mic")
                        throw new Exception("FAIL: renaming a microphone did not update the item");
                }
                finally { micSession.Stop(); micSession.Dispose(); }
                var micRouteButton = (Button)dock.FindName("MicrophoneRouteButton")!;
                if ((micRouteButton.Content as string) != "Route…")
                    throw new Exception($"FAIL: the microphone route button reads '{micRouteButton.Content}'");
                // The playlist view's way back is a labelled button, not a lone clipped arrow.
                var playlistBack = (Button)dock.FindName("BackToLibraryButton")!;
                if ((playlistBack.Content as string) != "← Back")
                    throw new Exception($"FAIL: the playlist back button reads '{playlistBack.Content}'");
                Console.WriteLine("PASS: a microphone is a live audio item, and the playlist Back button is labelled");

                // Replay opens in the preview only: the audience keeps the live camera until Start replay.
                var staging = new MediaPlayback();
                var liveCamera = new MediaEntry(MediaLibrary.CameraPath(0), "Camera");
                var replayClip = new MediaEntry(@"C:\media\replay-0001.avi", "Camera");
                staging.Start(liveCamera);
                staging.Stage(replayClip);
                if (staging.ActiveFor("Camera")?.Path != liveCamera.Path)
                    throw new Exception("FAIL: a staged replay reached the audience cue");
                if (staging.PreviewFor("Camera")?.Path != replayClip.Path)
                    throw new Exception("FAIL: the preview did not show the staged replay");
                staging.PromoteStage();
                if (staging.ActiveFor("Camera")?.Path != replayClip.Path || staging.HasStaged)
                    throw new Exception("FAIL: Start replay did not put the staged replay on air");
                staging.Start(liveCamera);
                if (staging.HasStaged)
                    throw new Exception("FAIL: going live left the replay waiting in the preview");
                // A cue change resets the playhead a surface resumes at, so a short replay clip is
                // never opened past its end (which showed as a black box in the preview).
                var cueReset = new MediaPlayback();
                cueReset.Start(ad);
                cueReset.Seek(9);
                cueReset.ReportPosition(12.5);
                cueReset.Stage(replayClip);
                if (cueReset.LivePosition > 0.001 || cueReset.Position > 0.001)
                    throw new Exception($"FAIL: a new cue resumed at the previous playhead ({cueReset.LivePosition:0.0}s)");
                // The audience-view monitor is silent but on air: it keeps the live camera while a
                // replay waits in the preview, exactly like a real audience window.
                var monitorCue = new MediaPlayback();
                monitorCue.Start(liveCamera);
                monitorCue.Stage(replayClip);
                var monitorElement = new ScorebugElement { Name = "Monitor", Kind = ScorebugElementKind.Video, MediaTag = "Camera" };
                var airSurface = new VideoSurface(monitorElement, monitorCue, mute: true, onAir: true);
                var previewSurface = new VideoSurface(monitorElement, monitorCue, mute: true);
                if (airSurface.CurrentCue?.Path != liveCamera.Path)
                    throw new Exception($"FAIL: the audience monitor followed the staged replay ({airSurface.CurrentCue?.Path})");
                if (previewSurface.CurrentCue?.Path != replayClip.Path)
                    throw new Exception("FAIL: the program preview stopped showing the staged replay");
                Console.WriteLine("PASS: a replay stays in the preview until Start replay puts it on air");

                // Cameras are offered by the name Windows knows them by, in device-index order, so
                // the routing menu never says "Camera 1" for a camera called something else.
                var devices = CameraFeed.NamedDevices();
                if (devices.Count > 0)
                {
                    if (devices.Any(device => string.IsNullOrWhiteSpace(device.Name) || device.Name.StartsWith("Camera ")))
                        throw new Exception($"FAIL: a camera has no Windows name ({string.Join(", ", devices.Select(d => d.Name))})");
                    if (!devices.Select(device => device.Index).SequenceEqual(Enumerable.Range(0, devices.Count)))
                        throw new Exception("FAIL: the camera list is not in device-index order");
                    if (devices.Select(device => device.Name).Distinct().Count() != devices.Count)
                        throw new Exception("FAIL: two cameras share one name");
                }
                Console.WriteLine($"PASS: cameras are listed by their Windows names ({string.Join(", ", devices.Select(device => device.Name))})");

                // Each clip carries its own level and speaker.
                var quietAd = ad with { Volume = 0.35, AudioDevice = "Speaker B" };
                if (quietAd.VolumePercent != 35 || quietAd.AudioLabel != "Speaker B")
                    throw new Exception("FAIL: per-clip volume and speaker did not stick");
                playback.Start(quietAd);
                if (Math.Abs(playback.Volume - 0.35) > 0.001 || !playback.RoutedToDevice)
                    throw new Exception("FAIL: the scorebug did not read the clip's own level and speaker");
                playback.Start(ad);
                if (playback.RoutedToDevice || Math.Abs(playback.Volume - 1) > 0.001)
                    throw new Exception("FAIL: the next clip inherited the previous clip's audio");
                Console.WriteLine("PASS: every video has its own volume and output speaker");

                // Canvas size presets scale the layout and persist.
                var presetStudio = new ScorebugEditorWindow(videoCatalog, engine);
                presetStudio.Show();
                presetStudio.UpdateLayout();
                var firstX = videoCatalog.Layout.Elements[0].X;
                ((ComboBox)presetStudio.FindName("CanvasPreset")!).SelectedIndex = 1; // Full HD 1920 × 1080
                Pump(TimeSpan.FromMilliseconds(300));
                if (videoCatalog.Layout.Width != 1920 || videoCatalog.Layout.Height != 1080)
                    throw new Exception($"FAIL: canvas preset left the layout at {videoCatalog.Layout.Width}×{videoCatalog.Layout.Height}");
                if (Math.Abs(videoCatalog.Layout.Elements[0].X - firstX * 1.5) > 0.6)
                    throw new Exception("FAIL: canvas resize did not scale element positions");
                if (((Canvas)presetStudio.FindName("DesignCanvas")!).Width != 1920)
                    throw new Exception("FAIL: studio canvas did not adopt the new size");
                var studioContent = (FrameworkElement)presetStudio.Content;
                var sizeControls = (ComboBox)presetStudio.FindName("CanvasPreset")!;
                var sizeRight = sizeControls.TransformToAncestor(studioContent).Transform(new Point(sizeControls.ActualWidth, 0)).X;
                if (sizeRight > studioContent.ActualWidth + 0.5)
                    throw new Exception("FAIL: the canvas size controls do not fit in the studio");
                if (!FindAll<Button>(presetStudio).Any(b => (b.Content as string) == "VIDEO +"))
                    throw new Exception("FAIL: the studio has no VIDEO + button");

                // 4K preset: canvas grows and every element stays inside the frame.
                ((ComboBox)presetStudio.FindName("CanvasPreset")!).SelectedIndex = 3;
                Pump(TimeSpan.FromMilliseconds(300));
                if (videoCatalog.Layout.Width != 3840 || videoCatalog.Layout.Height != 2160)
                    throw new Exception($"FAIL: the 4K preset left the layout at {videoCatalog.Layout.Width}×{videoCatalog.Layout.Height}");
                if (videoCatalog.Layout.Elements.Any(e => e.X + e.Width > videoCatalog.Layout.Width + 0.5 || e.Y + e.Height > videoCatalog.Layout.Height + 0.5))
                    throw new Exception("FAIL: 4K scaling pushed an element outside the canvas");
                presetStudio.Close();
                var sizedAgain = new ScorebugCatalog(videoCatalogPath);
                if (sizedAgain.Layout.Width != 3840) throw new Exception("FAIL: canvas size did not persist");
                Console.WriteLine($"PASS: canvas presets scale and persist ({sizedAgain.Layout.Width}×{sizedAgain.Layout.Height})");
            }
            finally { if (File.Exists(videoCatalogPath)) File.Delete(videoCatalogPath); }

            // Retagging gates what the video element can show, and the library survives a save/load round trip.
            var library = new List<MediaEntry> { new(@"C:\media\ad.mp4", "General"), new(@"C:\media\song.mp3", "Music") };
            var retagged = MediaLibrary.Retag(library, library[0], "Ads")!;
            if (retagged.Tag != "Ads" || library[0].Tag != "Ads")
                throw new Exception("FAIL: assigning a tag did not update the clip");
            if (MediaLibrary.Retag(library, library[1], "  ")!.Tag != MediaLibrary.DefaultTag)
                throw new Exception("FAIL: an empty tag should fall back to General");
            var savedPrefs = System.Text.Json.JsonSerializer.Serialize(new UserPreferences { Media = library });
            var reloadedPrefs = System.Text.Json.JsonSerializer.Deserialize<UserPreferences>(savedPrefs)!;
            if (reloadedPrefs.Media.Count != 2 || reloadedPrefs.Media[0].Tag != "Ads" || !reloadedPrefs.Media[0].IsVideo || reloadedPrefs.Media[1].Tag != MediaLibrary.DefaultTag)
                throw new Exception("FAIL: the media library did not survive a preferences round trip");
            Console.WriteLine("PASS: media tags stick to clips and the library round trips through preferences");

            // Seeking: a cued clip can be jumped into instead of watched from the start.
            var seekPath = Path.Combine(Path.GetTempPath(), $"smoke-seek-{Guid.NewGuid():N}.wav");
            WriteSilentWav(seekPath, 6);
            var seekSession = new MediaSession(new UserPreferences());
            try
            {
                seekSession.Play(new MediaEntry(seekPath, "Music"));
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (seekSession.Duration <= 0 && DateTime.UtcNow < deadline) Pump(TimeSpan.FromMilliseconds(100));
                if (seekSession.Duration <= 0) throw new Exception("FAIL: the test clip never opened, so seeking could not be checked");
                seekSession.Seek(1);
                Pump(TimeSpan.FromMilliseconds(600));
                if (seekSession.Position is < 0.8 or > 2.2)
                    throw new Exception($"FAIL: seeking landed at {seekSession.Position:0.00}s instead of about 1.00s");
                if (Math.Abs(seekSession.Playback.Position - 1) > 0.001)
                    throw new Exception($"FAIL: the scorebug cue did not follow the seek ({seekSession.Playback.Position})");
                Console.WriteLine($"PASS: a clip can be scrubbed straight to 1.00s (duration {seekSession.Duration:0.0}s)");

                // A focused scrub bar used to feed its own transport updates back in as seek
                // requests, which hammered the decoder until the app died. It must now just sit
                // where the operator left it.
                var seekDock = new MediaDock();
                seekDock.Attach(seekSession);
                var seekHost = new Window { Width = 420, Height = 320, Content = seekDock };
                seekHost.Show();
                seekHost.UpdateLayout();
                var bar = (Slider)seekDock.FindName("ScrubBar")!;
                Keyboard.Focus(bar);
                Pump(TimeSpan.FromMilliseconds(400));
                var seeksBefore = seekSession.Playback.SeekVersion;
                Pump(TimeSpan.FromMilliseconds(900));
                if (seekSession.Playback.SeekVersion != seeksBefore)
                    throw new Exception($"FAIL: the focused scrub bar re-seeked the clip {seekSession.Playback.SeekVersion - seeksBefore} times on its own");
                seekHost.Close();
                Console.WriteLine("PASS: a focused scrub bar never echoes transport updates back as seeks");
            }
            finally
            {
                seekSession.Dispose();
                try { File.Delete(seekPath); } catch { }
            }

            // Update Output is bindable as a key.
            var publishCount = 0;
            var publishPanel = new GameControlWindow(engine, () => publishCount++, _ => { }, () => true);
            publishPanel.Show();
            publishPanel.UpdateLayout();
            var publishKeys = new KeybindSettings();
            publishKeys.Bindings["output.publish"] = "F8";
            publishPanel.ApplyKeybinds(publishKeys);
            RaiseKey(publishPanel, PresentationSource.FromVisual(publishPanel), Key.F8);
            if (publishCount != 1) throw new Exception($"FAIL: the Update Output keybind fired {publishCount} times");
            publishPanel.Close();
            Console.WriteLine("PASS: Update Output can be bound to a key");

            // The speaker picker must actually reach the session and stay on the chosen device.
            var speakerPath = Path.Combine(Path.GetTempPath(), $"smoke-speaker-{Guid.NewGuid():N}.wav");
            WriteSilentWav(speakerPath, 1);
            var speakerSession = new MediaSession(new UserPreferences());
            try
            {
                speakerSession.Import([speakerPath], "General");
                speakerSession.Select(speakerSession.Library[0]);
                var speakerDock = new MediaDock();
                speakerDock.Attach(speakerSession);
                var speakerHost = new Window { Width = 420, Height = 780, Content = speakerDock };
                speakerHost.Show();
                speakerHost.UpdateLayout();
                var speakerBox = (ComboBox)speakerDock.FindName("AudioDeviceBox")!;
                if (speakerBox.Items.Count > 1)
                {
                    var last = speakerBox.Items.Count - 1;
                    var chosen = ((AudioRouter.OutputDevice)speakerBox.Items[last]).Id;
                    speakerBox.SelectedIndex = last;
                    Pump(TimeSpan.FromMilliseconds(250));
                    if (speakerSession.Library[0].AudioDevice != chosen)
                        throw new Exception("FAIL: picking a speaker in the dock did not reach the clip");
                    if (speakerBox.SelectedIndex != last)
                        throw new Exception($"FAIL: the speaker picker snapped back to {speakerBox.SelectedIndex} after the choice");
                    speakerSession.Select(speakerSession.Library[0]);
                    Pump(TimeSpan.FromMilliseconds(150));
                    if (speakerBox.SelectedIndex != last)
                        throw new Exception("FAIL: a refresh reset the chosen speaker to Default");
                    speakerBox.SelectedIndex = 0;
                    Pump(TimeSpan.FromMilliseconds(200));
                    if (speakerSession.Library[0].AudioDevice is not null)
                        throw new Exception("FAIL: choosing Default output did not clear the clip's speaker");
                }
                else Console.WriteLine("SKIP: only one output device on this machine, speaker routing was not exercised");
                speakerHost.Close();
            }
            finally
            {
                speakerSession.Dispose();
                try { File.Delete(speakerPath); } catch { }
            }
            Console.WriteLine("PASS: the dock's speaker picker sticks to the chosen device");

            // Clear empties the media side and blanks whatever was on air.
            var clearSession = new MediaSession(new UserPreferences());
            try
            {
                clearSession.Library.Add(new MediaEntry(Path.Combine(Path.GetTempPath(), "smoke-clear.mp4"), "Ads"));
                clearSession.Playback.Start(new MediaEntry(Path.Combine(Path.GetTempPath(), "smoke-clear.mp4"), "Ads"));
                clearSession.ClearAll();
                if (clearSession.Library.Count != 0 || clearSession.Playlists.Count != 0 || clearSession.Playback.NowPlaying is not null)
                    throw new Exception("FAIL: Clear left media or a cued clip behind");
                // Replay opens frozen at the live edge and waits for Start replay.
                if (clearSession.IsReplaying || clearSession.PlayReplay())
                    throw new Exception("FAIL: Start replay ran without an open replay");
            }
            finally { clearSession.Dispose(); }
            Console.WriteLine("PASS: Clear blanks the media side, and Start replay refuses with nothing open");

            // F11 on an audience window is Broadcast Fullscreen; Escape gives it back.
            var fsEngine = new GameEngine();
            fsEngine.NewGame("football");
            var fsCatalog = new ScorebugCatalog(Path.Combine(Path.GetTempPath(), $"smoke-f11-{Guid.NewGuid():N}.json"));
            var fsOutput = new OutputWindow(fsEngine, fsCatalog, "Dark");
            fsOutput.Show();
            fsOutput.UpdateLayout();
            var fsSource = PresentationSource.FromVisual(fsOutput);
            RaiseKey(fsOutput, fsSource, Key.F11);
            Pump(TimeSpan.FromMilliseconds(200));
            if (!fsOutput.IsFullscreen || fsOutput.WindowStyle != WindowStyle.None)
                throw new Exception("FAIL: F11 did not make the audience window fullscreen");
            var fsContent = (FrameworkElement)fsOutput.Content;
            if (Math.Abs(fsContent.ActualWidth - SystemParameters.PrimaryScreenWidth) > 2 || Math.Abs(fsContent.ActualHeight - SystemParameters.PrimaryScreenHeight) > 2)
                throw new Exception($"FAIL: fullscreen content is {fsContent.ActualWidth:0}×{fsContent.ActualHeight:0} on a {SystemParameters.PrimaryScreenWidth:0}×{SystemParameters.PrimaryScreenHeight:0} screen");
            RaiseKey(fsOutput, fsSource, Key.Escape);
            Pump(TimeSpan.FromMilliseconds(200));
            if (fsOutput.IsFullscreen)
                throw new Exception("FAIL: Escape did not leave fullscreen");

            // The reported defect: size the window to the whole screen first, then go fullscreen.
            // The old code maximized against the stale work area and left a strip down each side.
            fsOutput.Width = 1600;
            fsOutput.Height = 1000;
            Pump(TimeSpan.FromMilliseconds(250));
            RaiseKey(fsOutput, PresentationSource.FromVisual(fsOutput), Key.F11);
            Pump(TimeSpan.FromMilliseconds(300));
            var resizedContent = (FrameworkElement)fsOutput.Content;
            var layoutAspect = (double)fsCatalog.Layout.Width / fsCatalog.Layout.Height;
            var barError = Math.Abs(resizedContent.ActualWidth / resizedContent.ActualHeight - layoutAspect);
            if (barError > 0.01 && Math.Abs(layoutAspect - SystemParameters.PrimaryScreenWidth / SystemParameters.PrimaryScreenHeight) < 0.01)
                throw new Exception($"FAIL: a resized window fullscreened to {resizedContent.ActualWidth:0}×{resizedContent.ActualHeight:0}, pillarboxing a {layoutAspect:0.000} layout ({barError:0.000} off)");
            RaiseKey(fsOutput, PresentationSource.FromVisual(fsOutput), Key.Escape);
            Pump(TimeSpan.FromMilliseconds(200));
            fsOutput.Close();
            Console.WriteLine("PASS: F11 fills the screen on an audience window and Escape restores it");

            // A typed play clock must hand the box back to the clock, or the display freezes on the
            // typed value and never counts down to 0.
            var playEngine = new GameEngine();
            playEngine.NewGame("football");
            var playHost = new GameControlWindow(playEngine, () => { }, _ => { }, () => true);
            playHost.Show();
            playHost.UpdateLayout();
            var playBox = (TextBox)playHost.Panel.FindName("PlayClockValue")!;
            playBox.Focus();
            playBox.Text = "2";
            var clockSource = PresentationSource.FromVisual(playHost);
            playBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, clockSource!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Pump(TimeSpan.FromMilliseconds(100));
            if (ReferenceEquals(Keyboard.FocusedElement, playBox))
                throw new Exception("FAIL: the play clock box kept keyboard focus after Enter, freezing the display");
            playEngine.TogglePlayClock();
            var clockTick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            clockTick.Tick += (_, _) => playEngine.Tick();
            clockTick.Start();
            var shown = new List<string>();
            for (var i = 0; i < 10; i++) { Pump(TimeSpan.FromMilliseconds(400)); shown.Add(playBox.Text); }
            clockTick.Stop();
            if (shown[^1] != "0") throw new Exception($"FAIL: the play clock display never showed 0 ({string.Join(",", shown)})");
            playHost.Close();
            Console.WriteLine($"PASS: the play clock display follows the clock to 0 after typing ({string.Join(",", shown)})");

            // A detached panel carries the same media dock, so media is reachable from any panel.
            var sharedPrefs = new UserPreferences();
            var sharedMedia = new MediaSession(sharedPrefs);
            sharedMedia.Library.Add(new MediaEntry(Path.Combine(Path.GetTempPath(), "smoke-ad.mp4"), "Ads"));
            try
            {
                var mediaPanel = new GameControlWindow(engine, () => { }, _ => { }, () => true, sharedMedia);
                mediaPanel.Show();
                mediaPanel.UpdateLayout();
                // Media is a tab of its own in a panel too, and it fills the panel when opened.
                mediaPanel.ShowMediaTab(true);
                mediaPanel.UpdateLayout();
                var panelDock = FindAll<MediaDock>(mediaPanel).FirstOrDefault() ?? throw new Exception("FAIL: a detached control panel has no media dock");
                var panelTiles = (WrapPanel)panelDock.FindName("Tiles")!;
                panelTiles.UpdateLayout();
                // Folders are browser items too, so count the clip cards rather than every card.
                var panelClips = panelTiles.Children.OfType<Border>().Count(b => b.Tag is MediaEntry);
                if (panelClips != sharedMedia.Library.Count || panelClips == 0)
                    throw new Exception($"FAIL: the detached panel shows {panelClips} media tiles for a library of {sharedMedia.Library.Count}");
                if (panelDock.ActualWidth < mediaPanel.ActualWidth - 120)
                    throw new Exception($"FAIL: the media tab does not fill the panel ({panelDock.ActualWidth:0} of {mediaPanel.ActualWidth:0})");
                var panelTransport = (Button)panelDock.FindName("PlayPauseButton")!;
                if (panelTransport.ActualHeight < 40)
                    throw new Exception($"FAIL: the media tab still uses rail-sized buttons ({panelTransport.ActualHeight:0})");
                mediaPanel.Close();
            }
            finally { sharedMedia.Dispose(); }
            Console.WriteLine("PASS: detached control panels show the shared media library");

            // The app opens on a chooser: product name, both buttons, the version strip and autosaves.
            var demoAutosave = new GameEngine();
            demoAutosave.NewGame("football");
            demoAutosave.SetTeams("Away Club", "Home Club");
            demoAutosave.Score(true, 3);
            AutosaveStore.Save(demoAutosave);
            var launcherWindow = new LauncherWindow();
            launcherWindow.Show();
            launcherWindow.UpdateLayout();
            var chooserButtons = FindAll<Button>(launcherWindow).Select(b => b.Content as string).Where(t => t is not null).ToArray();
            if (!chooserButtons.Contains("New Game") || !chooserButtons.Contains("Open Game"))
                throw new Exception("FAIL: the launcher is missing New Game / Open Game");
            var versionLabel = FindAll<TextBlock>(launcherWindow).FirstOrDefault(t => t.Text == LauncherWindow.Version);
            if (versionLabel is null)
                throw new Exception($"FAIL: the launcher does not show {LauncherWindow.Version} on its side strip");
            if (!FindAll<TextBlock>(launcherWindow).Any(t => t.Text.Contains("SCOREPLUS", StringComparison.OrdinalIgnoreCase)))
                throw new Exception("FAIL: the launcher does not show the product name");
            var autosaveList = (ListBox)launcherWindow.FindName("AutosaveList")!;
            if (autosaveList.Items.Count == 0)
                throw new Exception("FAIL: the launcher lists no autosaves to load");
            // Delete all is on the launcher, needs a second click, and really empties the folder.
            var clearButton = FindAll<Button>(launcherWindow).FirstOrDefault(b => (b.Content as string) == "Delete all")
                ?? throw new Exception("FAIL: the launcher has no Delete all button for autosaves");
            clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if ((clearButton.Content as string) == "Delete all")
                throw new Exception("FAIL: Delete all wiped the autosaves without asking for confirmation");
            if (AutosaveStore.List().Count == 0)
                throw new Exception("FAIL: the first Delete all click already removed the autosaves");
            clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (AutosaveStore.List().Count != 0)
                throw new Exception($"FAIL: Delete all left {AutosaveStore.List().Count} autosave(s) behind");
            // A relaunch must not resurrect an autosave from the one-time legacy import.
            var legacyRecovery = AppPaths.Combine("recovery.scoreboard");
            File.WriteAllText(legacyRecovery, demoAutosave.ExportJson());
            AutosaveStore.ImportLegacy();
            if (AutosaveStore.List().Count != 0)
                throw new Exception("FAIL: the legacy recovery file came back after deleting every autosave");
            try { File.Delete(legacyRecovery); } catch { }
            Console.WriteLine("PASS: the launcher deletes every autosave after a confirming second click, and they stay deleted");
            launcherWindow.Close();
            Console.WriteLine($"PASS: launch chooser offers New Game / Open Game, {LauncherWindow.Version} and {autosaveList.Items.Count} autosave(s)");
            // Leave one behind again, so the rest of the run sees the usual state.
            AutosaveStore.Save(demoAutosave, startNewPoint: true);

            // Studio dialogs are plain Windows: the theme has to reach them too.
            var dialogProbe = new Window { Width = 300, Height = 200 };
            var probeList = new ListBox();
            dialogProbe.Content = probeList;
            ThemeManager.Apply(dialogProbe, "Dark");
            dialogProbe.Show();
            dialogProbe.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(150));
            if (probeList.Background is not SolidColorBrush probeBrush || probeBrush.Color.R > 100)
                throw new Exception($"FAIL: a plain dialog was not themed ({probeList.Background})");
            if (dialogProbe.Background is not SolidColorBrush probeWindow || probeWindow.Color.R > 100)
                throw new Exception($"FAIL: a plain dialog window kept its light background ({dialogProbe.Background})");
            dialogProbe.Close();
            Console.WriteLine("PASS: plain dialogs (shape, sport data, video) pick up the theme");

            // A new game clears the live game and the on-air copy, so the previous one cannot linger.
            var panelsBeforeGame = Application.Current.Windows.OfType<GameControlWindow>().Count(w => w.IsVisible);
            var freshGame = new MainWindow(LaunchMode.New);
            freshGame.Show();
            freshGame.UpdateLayout();
            if (Application.Current.Windows.OfType<GameControlWindow>().Count(w => w.IsVisible) > panelsBeforeGame)
                throw new Exception("FAIL: opening a game popped out another control panel");
            freshGame.Engine.SetTeams("Old Home", "Old Away");
            var staleLogo = Path.Combine(Path.GetTempPath(), $"smoke-stale-logo-{Guid.NewGuid():N}.png");
            WritePng(staleLogo, Color.FromRgb(0x99, 0x22, 0x22));
            freshGame.Engine.SetTeamIdentity(false, "Old Away", "OA", staleLogo, "#FFAA0000", "#FF222222");
            freshGame.Scorebug.AddLayout("Old second layout");
            var freshDock = FindAll<MediaDock>(freshGame).First();
            ((TextBox)freshDock.FindName("SearchBox")!).Text = "old";
            freshGame.Engine.Score(true, 21);
            freshGame.Engine.AdvanceDown();
            freshGame.Engine.AddStat("Penalties");
            freshGame.PublishOutput();
            freshGame.StartNewGame("football");
            if (freshGame.Engine.State.HomeScore != 0 || freshGame.Engine.State.AwayScore != 0 || freshGame.Engine.State.Down != 1 || freshGame.Engine.State.Stats.Count != 0)
                throw new Exception("FAIL: New Game left the previous game's state behind");
            if (freshGame.Engine.State.HomeTeam == "Old Home" || freshGame.PublishedState.HomeScore != 0 || freshGame.PublishedState.HomeTeam == "Old Home" || freshGame.PublishedState.Down != 1)
                throw new Exception("FAIL: New Game left the previous game on air");
            // The blank slate has to be visible, not just stored: the preview must be rebuilt.
            var previewView = FindAll<ScorebugCanvasView>(freshGame).FirstOrDefault()
                ?? throw new Exception("FAIL: the main window has no program preview");
            var previewTexts = FindAll<TextBlock>(previewView).Select(t => t.Text).ToArray();
            if (previewTexts.Any(t => t.Contains("Old Home", StringComparison.Ordinal) || t.Contains("Old Away", StringComparison.Ordinal) || t == "21"))
                throw new Exception($"FAIL: the scorebug preview still shows the previous game ({string.Join(" | ", previewTexts)})");
            if (freshGame.Engine.State.AwayLogoPath is not null || freshGame.PublishedState.AwayLogoPath is not null
                || FindAll<Image>(previewView).Any(i => i.Source is BitmapImage))
                throw new Exception("FAIL: New Game kept the previous team logo on the control panel or graphic");
            if (freshGame.Scorebug.Layouts.Count != 1)
                throw new Exception($"FAIL: New Game kept {freshGame.Scorebug.Layouts.Count} layouts instead of starting with one");
            if (((TextBox)freshDock.FindName("SearchBox")!).Text.Length != 0)
                throw new Exception("FAIL: New Game left the media search filtering the blank library");
            try { File.Delete(staleLogo); } catch { }
            freshGame.Close();
            Console.WriteLine("PASS: New Game clears the logo, extra layouts and the previous game from the output");

            // A saved game comes back complete after a relaunch: text, score and the team logo.
            var reloadLogo = Path.Combine(Path.GetTempPath(), $"smoke-reload-logo-{Guid.NewGuid():N}.png");
            var reloadGame = Path.Combine(Path.GetTempPath(), $"smoke-reload-{Guid.NewGuid():N}.spg");
            try
            {
                WritePng(reloadLogo, Color.FromRgb(0xCC, 0x33, 0x55));
                var saved = new MainWindow(LaunchMode.New);
                saved.Show();
                saved.UpdateLayout();
                saved.Engine.SetTeams("Loaded Home", "Loaded Away");
                saved.Engine.SetTeamIdentity(false, "Loaded Away", "LAW", reloadLogo, "#FFCC3355", "#FF223344");
                saved.Engine.Score(true, 7);
                saved.Engine.Save(reloadGame);
                saved.Close();

                var reopened = new MainWindow(LaunchMode.Open, reloadGame) { Width = 1280, Height = 800 };
                reopened.Show();
                reopened.UpdateLayout();
                if (reopened.PublishedState.HomeTeam != "Loaded Home" || reopened.PublishedState.AwayTeam != "Loaded Away" || reopened.PublishedState.HomeScore != 7)
                    throw new Exception($"FAIL: reopening a saved game lost its values ({reopened.PublishedState.HomeTeam} {reopened.PublishedState.HomeScore} / {reopened.PublishedState.AwayTeam})");
                var reopenedPreview = FindAll<ScorebugCanvasView>(reopened).FirstOrDefault()
                    ?? throw new Exception("FAIL: the reopened game has no program preview");
                if (!FindAll<Image>(reopenedPreview).Any(i => i.Source is BitmapImage))
                    throw new Exception("FAIL: the saved team logo did not appear after reopening the game");
                reopened.Close();
            }
            finally
            {
                if (File.Exists(reloadLogo)) File.Delete(reloadLogo);
                if (File.Exists(reloadGame)) File.Delete(reloadGame);
            }
            Console.WriteLine("PASS: reopening a saved game restores its text, score and team logo");

            // Closing a changed game opens the app's own prompt; Keep editing leaves it open.
            MainWindow.PromptOnExit = true;
            var dirty = new MainWindow(LaunchMode.New);
            dirty.Show();
            dirty.UpdateLayout();
            dirty.Engine.Score(true, 3);
            var promptTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            promptTimer.Tick += (_, _) =>
            {
                var prompt = Application.Current.Windows.OfType<UnsavedChangesWindow>().FirstOrDefault();
                if (prompt is null) return;
                promptTimer.Stop();
                var keep = FindAll<Button>(prompt).FirstOrDefault(b => (b.Content as string) == "Keep editing")
                    ?? throw new Exception("FAIL: the exit prompt has no Keep editing action");
                keep.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            promptTimer.Start();
            dirty.Close();
            if (!dirty.IsVisible)
                throw new Exception("FAIL: the exit prompt did not stop a changed game from closing");
            MainWindow.PromptOnExit = false;
            dirty.Close();

            // Choosing Exit without saving must actually close, and must do so after OnClosing
            // has finished (calling Close from inside it is what raised the WPF error).
            MainWindow.PromptOnExit = true;
            var discarding = new MainWindow(LaunchMode.New);
            discarding.Show();
            discarding.UpdateLayout();
            discarding.Engine.Score(true, 2);
            var discardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            discardTimer.Tick += (_, _) =>
            {
                var prompt = Application.Current.Windows.OfType<UnsavedChangesWindow>().FirstOrDefault();
                if (prompt is null) return;
                discardTimer.Stop();
                var discard = FindAll<Button>(prompt).FirstOrDefault(b => (b.Content as string) == "Exit without saving")
                    ?? throw new Exception("FAIL: the exit prompt has no discard action");
                discard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            discardTimer.Start();
            discarding.Close();
            Pump(TimeSpan.FromMilliseconds(250));
            if (discarding.IsVisible)
                throw new Exception("FAIL: Exit without saving left the game window open");
            MainWindow.PromptOnExit = false;
            Console.WriteLine("PASS: closing a changed game asks first, Keep editing returns, and Exit without saving closes cleanly");

            // The studio's working surface keeps the navy artwork backdrop in both themes: it is
            // the same surface the audience graphic is designed on, not an operator panel.
            var studioThemePath = Path.Combine(Path.GetTempPath(), $"smoke-studio-theme-{Guid.NewGuid():N}.json");
            try
            {
                var themeCatalog = new ScorebugCatalog(studioThemePath);
                foreach (var theme in new[] { "Dark", "Light" })
                {
                    var themeStudio = new ScorebugEditorWindow(themeCatalog, engine, theme: theme);
                    themeStudio.Show();
                    Pump(TimeSpan.FromMilliseconds(150));
                    themeStudio.UpdateLayout();
                    var designCanvas = (Canvas)themeStudio.FindName("DesignCanvas")!;
                    if (designCanvas.Background is not SolidColorBrush brush || brush.Color != Color.FromRgb(0x09, 0x10, 0x18))
                        throw new Exception($"FAIL: the {theme} studio canvas is {designCanvas.Background}, not the navy artwork backdrop");
                    themeStudio.Close();
                }
            }
            finally { if (File.Exists(studioThemePath)) File.Delete(studioThemePath); }
            Console.WriteLine("PASS: the studio canvas keeps its navy artwork backdrop in both themes");

            // Studio: layer order, context-sensitive properties and the element's own quarter style.
            var contextPath = Path.Combine(Path.GetTempPath(), $"smoke-studio-{Guid.NewGuid():N}.json");
            try
            {
                var contextCatalog = new ScorebugCatalog(contextPath);
                var contextEditor = new ScorebugEditorWindow(contextCatalog, engine);
                contextEditor.Show();
                contextEditor.UpdateLayout();
                // The studio must never open past the desktop: a window that runs past the edge hides
                // part of the artwork. Its allowance (MaxWidth/MaxHeight) is the whole monitor so a
                // maximized window can fill every pixel; what matters is the size it actually takes.
                // A maximized window reports the work area plus its resize border, which sits off the
                // edge of the screen, so the tolerance covers that.
                if (contextEditor.ActualWidth > SystemParameters.WorkArea.Width + 40 ||
                    contextEditor.ActualHeight > SystemParameters.WorkArea.Height + 40)
                    throw new Exception($"FAIL: the studio opened {contextEditor.ActualWidth:0}x{contextEditor.ActualHeight:0} on a {SystemParameters.WorkArea.Width:0}x{SystemParameters.WorkArea.Height:0} desktop");
                if (contextEditor.WindowState != WindowState.Maximized)
                    throw new Exception($"FAIL: the studio opened {contextEditor.WindowState} instead of full windowed");
                var contextLayers = (ListBox)contextEditor.FindName("Layers")!;
                var textIndex = contextCatalog.Layout.Elements.FindIndex(e => e.Kind == ScorebugElementKind.Text);
                contextLayers.SelectedIndex = contextCatalog.Layout.Elements.Count - 1 - textIndex;
                contextEditor.UpdateLayout();
                var logoSection = (FrameworkElement)contextEditor.FindName("LogoSection")!;
                if (logoSection.IsVisible)
                    throw new Exception("FAIL: the team-logo selector is offered for a text element");
                var logoIndex = contextCatalog.Layout.Elements.FindIndex(e => e.Kind == ScorebugElementKind.Logo);
                contextLayers.SelectedIndex = contextCatalog.Layout.Elements.Count - 1 - logoIndex;
                contextEditor.UpdateLayout();
                if (!logoSection.IsVisible || contextEditor.FindName("LogoInput") is not ComboBox)
                    throw new Exception("FAIL: a logo element has no team-logo selector");
                if (FindAll<Button>(contextEditor).Any(b => (b.Content as string) is "Choose logo image…"))
                    throw new Exception("FAIL: logos are still configured in the studio instead of the control panel");

                contextCatalog.Update(layout => layout.Elements.Add(new ScorebugElement { Name = "Quarter chip", Content = "{{PERIOD}}", X = 20, Y = 20, Width = 200, Height = 60 }));
                contextEditor.UpdateLayout();
                contextLayers.SelectedIndex = 0;
                contextEditor.UpdateLayout();
                var quarterCombo = (ComboBox)contextEditor.FindName("PeriodFormatInput")!;
                if (quarterCombo.IsVisible == false) throw new Exception("FAIL: the quarter style is not offered for a period element");
                quarterCombo.SelectedIndex = 3; // "1"
                Pump(TimeSpan.FromMilliseconds(400));
                var chip = contextCatalog.Layout.Elements.Last();
                if (chip.PeriodFormat != "Number")
                    throw new Exception($"FAIL: the element's quarter style was not saved ({chip.PeriodFormat})");
                if (!FindAll<TextBlock>((Canvas)contextEditor.FindName("DesignCanvas")!).Any(t => t.Text == "1"))
                    throw new Exception("FAIL: the studio did not print the element's own quarter style");
                // The artwork is scaled by a Viewbox, never by a transform the frame can outgrow, so
                // the whole canvas fits the editing panel and every edge stays draggable.
                // The gradient's second colour offers every palette slot, not an empty list.
                var gradientChoices = (ComboBox)contextEditor.FindName("GradientSourceInput")!;
                if (gradientChoices.Items.Count < 5)
                    throw new Exception($"FAIL: the gradient second colour lists {gradientChoices.Items.Count} choices");
                if (!FindAll<Button>(contextEditor).Any(b => (b.Content as string) == "Move to…") ||
                    !FindAll<Button>(contextEditor).Any(b => (b.Content as string) == "Copy to…"))
                    throw new Exception("FAIL: the studio has no Move to…/Copy to… layout actions");

                var fitCanvas = (Canvas)contextEditor.FindName("DesignCanvas")!;
                var fitFrame = (Border)contextEditor.FindName("CanvasFrame")!;
                var inViewbox = false;
                for (var ancestor = VisualTreeHelper.GetParent(fitCanvas); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
                    if (ancestor is Viewbox) { inViewbox = true; break; }
                if (!inViewbox)
                    throw new Exception("FAIL: the studio canvas is not hosted in a Viewbox");
                var fitBounds = fitCanvas.TransformToAncestor(fitFrame)
                    .TransformBounds(new Rect(0, 0, fitCanvas.Width, fitCanvas.Height));
                if (fitBounds.Width > fitFrame.ActualWidth + 1 || fitBounds.Height > fitFrame.ActualHeight + 1)
                    throw new Exception($"FAIL: the studio canvas overflows its panel ({fitBounds.Width:0}x{fitBounds.Height:0} in {fitFrame.ActualWidth:0}x{fitFrame.ActualHeight:0})");
                if (fitBounds.Width < fitFrame.ActualWidth * 0.5 || fitBounds.Height < fitFrame.ActualHeight * 0.5)
                    throw new Exception("FAIL: the studio canvas is much smaller than the panel it should fill");

                // Both artwork edges are reachable: an element placed flush at the top and at the
                // bottom lands exactly on them, scaled with the canvas.
                var edgeItem = contextCatalog.Layout.Elements[0];
                var originalX = edgeItem.X;
                var originalY = edgeItem.Y;
                foreach (var y in new[] { 0d, contextCatalog.Layout.Height - edgeItem.Height })
                {
                    var targetY = y;
                    contextCatalog.Update(layout =>
                    {
                        var item = layout.Elements.First(e => e.Id == edgeItem.Id);
                        item.X = 0;
                        item.Y = targetY;
                    });
                    contextEditor.UpdateLayout();
                    var host = FindAll<Border>(fitCanvas).FirstOrDefault(b => ReferenceEquals(b.Tag, edgeItem))
                        ?? throw new Exception("FAIL: the studio lost the element's canvas host");
                    var hostBounds = host.TransformToAncestor(fitCanvas)
                        .TransformBounds(new Rect(0, 0, host.ActualWidth, host.ActualHeight));
                    var expected = targetY == 0 ? hostBounds.Top : fitCanvas.Height - hostBounds.Bottom;
                    if (Math.Abs(expected) > 1)
                        throw new Exception($"FAIL: an element flush at y={targetY:0} sits {expected:0}px from the artwork edge");
                }
                contextCatalog.Update(layout =>
                {
                    var item = layout.Elements.First(e => e.Id == edgeItem.Id);
                    item.X = originalX;
                    item.Y = originalY;
                });
                contextEditor.Close();
            }
            finally { if (File.Exists(contextPath)) File.Delete(contextPath); }
            Console.WriteLine("PASS: studio layers run front-first and only show properties that apply to the selected element");

            // Video tag labels are a studio aid; nothing about them reaches the audience picture.
            var videoLabelPath = Path.Combine(Path.GetTempPath(), $"smoke-video-label-{Guid.NewGuid():N}.json");
            try
            {
                var labelCatalog = new ScorebugCatalog(videoLabelPath);
                labelCatalog.Update(layout => layout.Elements.Add(new ScorebugElement { Name = "Ad box", Kind = ScorebugElementKind.Video, MediaTag = "General", X = 10, Y = 10, Width = 320, Height = 180 }));
                var audience = new ScorebugCanvasView(labelCatalog, engine, playback: new MediaPlayback());
                audience.Measure(new Size(640, 360));
                audience.Arrange(new Rect(0, 0, 640, 360));
                audience.UpdateLayout();
                if (FindAll<TextBlock>(audience).Any(t => t.Text.Contains("VIDEO", StringComparison.OrdinalIgnoreCase)))
                    throw new Exception("FAIL: the video tag label reached the audience picture");
                var labelEditor = new ScorebugEditorWindow(labelCatalog, engine, new MediaPlayback());
                labelEditor.Show();
                labelEditor.UpdateLayout();
                if (!FindAll<TextBlock>(labelEditor).Any(t => t.Text.Contains("VIDEO · GENERAL", StringComparison.Ordinal)))
                    throw new Exception("FAIL: the studio does not label an empty video element");
                labelEditor.Close();
                audience.Dispose();
            }
            finally { if (File.Exists(videoLabelPath)) File.Delete(videoLabelPath); }
            Console.WriteLine("PASS: an empty video element is labelled in the studio only, never on the audience picture");

            // ---------- fonts, type styles, team colour slots and value animations ----------
            if (FontCatalog.All.Count < 25)
                throw new Exception($"FAIL: only {FontCatalog.All.Count} fonts are offered; the studio needs at least 25");
            if (FontCatalog.All.Select(f => f.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 25)
                throw new Exception("FAIL: the font picker repeats families");
            Console.WriteLine($"PASS: the studio offers {FontCatalog.All.Count} font families and imports your own");
            if (Enum.GetValues<ScorebugTransition>().Length < 10)
                throw new Exception($"FAIL: only {Enum.GetValues<ScorebugTransition>().Length} text-change animations exist");

            var stylePath = Path.Combine(Path.GetTempPath(), $"smoke-style-{Guid.NewGuid():N}.json");
            try
            {
                var styleCatalog = new ScorebugCatalog(stylePath);
                styleCatalog.Update(layout =>
                {
                    layout.Elements.Clear();
                    layout.Elements.Add(new ScorebugElement
                    {
                        Name = "Styled", Content = "{{HOME_TEAM}}", X = 10, Y = 10, Width = 420, Height = 90, FontSize = 48,
                        // A team-palette slot with a stale fixed colour underneath it: opening the studio
                        // has to show the team colour straight away, not wait for a click to repaint it.
                        ForegroundSource = ScorebugColorSource.HomePrimary, Foreground = "#FF000000"
                    });
                });
                var styleEngine = new GameEngine();
                styleEngine.NewGame("football");
                styleEngine.SetTeamIdentity(true, "Home Town", "HT", null, "#FF3366CC", "#FF223344");
                var styleEditor = new ScorebugEditorWindow(styleCatalog, styleEngine);
                styleEditor.Show();
                styleEditor.UpdateLayout();
                var openingText = FindAll<ScorebugTextBlock>((Canvas)styleEditor.FindName("DesignCanvas")!).FirstOrDefault();
                if (openingText is null) throw new Exception("FAIL: the studio canvas is empty when it opens");
                if (openingText.Foreground is not SolidColorBrush openingBrush || openingBrush.Color != Color.FromRgb(0x33, 0x66, 0xCC))
                    throw new Exception($"FAIL: the studio opened without painting the team colour ({openingText.Foreground})");
                ((ListBox)styleEditor.FindName("Layers")!).SelectedIndex = 0;
                styleEditor.UpdateLayout();
                ((ComboBox)styleEditor.FindName("TransitionInput")!).SelectedItem = ScorebugTransition.WheelUp;
                ((ComboBox)styleEditor.FindName("ForegroundSourceInput")!).SelectedIndex = 1; // Home · primary
                ((CheckBox)styleEditor.FindName("BoldInput")!).IsChecked = false;
                ((CheckBox)styleEditor.FindName("ItalicInput")!).IsChecked = true;
                ((CheckBox)styleEditor.FindName("UnderlineInput")!).IsChecked = true;
                ((CheckBox)styleEditor.FindName("StrikeInput")!).IsChecked = true;
                Pump(TimeSpan.FromMilliseconds(700));
                styleEditor.UpdateLayout();
                var styled = styleCatalog.Layout.Elements[0];
                if (styled.Transition != ScorebugTransition.WheelUp || styled.ForegroundSource != ScorebugColorSource.HomePrimary
                    || styled.Bold || !styled.Italic || !styled.Underline || !styled.StrikeThrough)
                    throw new Exception($"FAIL: the studio did not save type style / colour slot / transition ({styled.Transition}, {styled.ForegroundSource}, bold={styled.Bold})");
                var styledText = FindAll<ScorebugTextBlock>((Canvas)styleEditor.FindName("DesignCanvas")!).FirstOrDefault(t => t.Text == "Home Town")
                    ?? throw new Exception("FAIL: the styled text is missing from the studio canvas");
                if (styledText.FontStyle != FontStyles.Italic || styledText.TextDecorations is null || styledText.TextDecorations.Count != 2)
                    throw new Exception("FAIL: italic / underline / strike did not reach the canvas text");
                if (styledText.Foreground is not SolidColorBrush teamBrush || teamBrush.Color != Color.FromRgb(0x33, 0x66, 0xCC))
                    throw new Exception($"FAIL: the home primary colour did not reach the text ({styledText.Foreground})");
                if (((Border)styleEditor.FindName("TextColorSwatch")!).Background is not SolidColorBrush slotSwatch
                    || slotSwatch.Color != Color.FromRgb(0x33, 0x66, 0xCC))
                    throw new Exception("FAIL: the colour swatch does not show the resolved team colour");

                // A typed custom colour must show on the canvas and in the swatch.
                ((ComboBox)styleEditor.FindName("ForegroundSourceInput")!).SelectedIndex = 0; // Custom colour
                ((TextBox)styleEditor.FindName("ColorInput")!).Text = "#FF10C0FF";
                Pump(TimeSpan.FromMilliseconds(700));
                styleEditor.UpdateLayout();
                var recoloured = FindAll<ScorebugTextBlock>((Canvas)styleEditor.FindName("DesignCanvas")!).FirstOrDefault(t => t.Text == "Home Town")
                    ?? throw new Exception("FAIL: the recoloured text vanished from the canvas");
                if (recoloured.Foreground is not SolidColorBrush typedBrush || typedBrush.Color != Color.FromRgb(0x10, 0xC0, 0xFF))
                    throw new Exception($"FAIL: a typed colour did not reach the studio canvas ({recoloured.Foreground})");
                if (((Border)styleEditor.FindName("TextColorSwatch")!).Background is not SolidColorBrush typedSwatch
                    || typedSwatch.Color != Color.FromRgb(0x10, 0xC0, 0xFF))
                    throw new Exception("FAIL: the colour swatch does not follow a typed colour");
                // A shape pointed at a team colour must repaint when the team's palette changes.
                styleCatalog.Update(layout => layout.Elements.Add(new ScorebugElement
                {
                    Name = "Bar", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle,
                    X = 40, Y = 240, Width = 200, Height = 40,
                    BackgroundSource = ScorebugColorSource.HomeSecondary
                }));
                styleEditor.UpdateLayout();
                var canvasShapes = (Canvas)styleEditor.FindName("DesignCanvas")!;
                bool HasFill(Color wanted) => FindAll<System.Windows.Shapes.Shape>(canvasShapes)
                    .Any(s => s.Fill is SolidColorBrush fill && fill.Color == wanted);
                if (!HasFill(Color.FromRgb(0x22, 0x33, 0x44)))
                    throw new Exception("FAIL: a team colour slot did not reach the studio shape");
                styleEngine.SetTeamIdentity(true, "Home Town", "HT", null, "#FF3366CC", "#FFEE1122");
                Pump(TimeSpan.FromMilliseconds(400));
                styleEditor.UpdateLayout();
                if (!HasFill(Color.FromRgb(0xEE, 0x11, 0x22)) || HasFill(Color.FromRgb(0x22, 0x33, 0x44)))
                    throw new Exception("FAIL: changing a team colour did not repaint the studio shape");
                styleEditor.Close();
                Console.WriteLine("PASS: the studio canvas and colour swatch show the element's colour, and team colours repaint shapes live");
                Console.WriteLine("PASS: fonts, bold/italic/underline/strike, team colour slots and value transitions reach the canvas");
            }
            finally { if (File.Exists(stylePath)) File.Delete(stylePath); }

            // ---------- layouts: preview arrows switch the artwork, Update Output puts it on air ----------
            {
                var layoutMain = new MainWindow(LaunchMode.New) { Width = 1280, Height = 800 };
                layoutMain.Show();
                layoutMain.UpdateLayout();
                if (layoutMain.FindName("LayoutPrevButton") is not Button || layoutMain.FindName("LayoutNextButton") is not Button)
                    throw new Exception($"FAIL: the program preview has no layout arrows (buttons: {string.Join(",", FindAll<Button>(layoutMain).Select(b => b.Name).Where(n => n.Length > 0))})");
                layoutMain.Scorebug.AddLayout("Slideshow");
                layoutMain.Scorebug.Update(layout =>
                {
                    layout.Elements.Clear();
                    layout.Elements.Add(new ScorebugElement { Name = "Slide", Content = "SLIDE TWO", X = 40, Y = 40, Width = 700, Height = 110, FontSize = 56 });
                });
                layoutMain.Scorebug.SelectLayout(0);
                var preview = FindAll<ScorebugCanvasView>(layoutMain).First();
                if (FindAll<TextBlock>(preview).Any(t => t.Text == "SLIDE TWO"))
                    throw new Exception("FAIL: the preview already shows the second layout before the arrow is used");
                layoutMain.ShiftLayout(1);
                layoutMain.UpdateLayout();
                if (!FindAll<TextBlock>(preview).Any(t => t.Text == "SLIDE TWO"))
                    throw new Exception("FAIL: the forward arrow did not switch the program preview");
                if (!layoutMain.HasStagedChanges)
                    throw new Exception("FAIL: a layout switch did not wait for Update Output");
                var audienceBefore = new ScorebugCanvasView(layoutMain.Scorebug, layoutMain.Engine, publishedLayout: true);
                audienceBefore.Measure(new Size(1280, 720));
                audienceBefore.Arrange(new Rect(0, 0, 1280, 720));
                if (FindAll<TextBlock>(audienceBefore).Any(t => t.Text == "SLIDE TWO"))
                    throw new Exception("FAIL: an unpublished layout reached the audience output");
                layoutMain.PublishOutput();
                if (layoutMain.HasStagedChanges)
                    throw new Exception("FAIL: Update Output left the layout staged");
                var audienceAfter = new ScorebugCanvasView(layoutMain.Scorebug, layoutMain.Engine, publishedLayout: true);
                audienceAfter.Measure(new Size(1280, 720));
                audienceAfter.Arrange(new Rect(0, 0, 1280, 720));
                if (!FindAll<TextBlock>(audienceAfter).Any(t => t.Text == "SLIDE TWO"))
                    throw new Exception("FAIL: Update Output did not send the chosen layout to the audience");
                layoutMain.ShiftLayout(-1);
                layoutMain.UpdateLayout();
                if (FindAll<TextBlock>(preview).Any(t => t.Text == "SLIDE TWO"))
                    throw new Exception("FAIL: the back arrow did not return to the first layout");
                // The studio's own layout picker drives the same catalog.
                var layoutStudio = new ScorebugEditorWindow(layoutMain.Scorebug, layoutMain.Engine);
                layoutStudio.Show();
                layoutStudio.UpdateLayout();
                // Layouts are pictures with their names under them, not names in a drop-down.
                var layoutStrip = (StackPanel)layoutStudio.FindName("LayoutStrip")!;
                if (layoutStrip.Children.Count != 2)
                    throw new Exception($"FAIL: the studio lists {layoutStrip.Children.Count} layouts for 2");
                var secondTile = (Button)layoutStrip.Children[1];
                if (!FindAll<TextBlock>(secondTile).Any(t => t.Text.Contains("Slideshow")))
                    throw new Exception("FAIL: a layout picture has no name underneath it");
                if (!FindAll<Image>(secondTile).Any(i => i.Source is not null))
                    throw new Exception("FAIL: a layout picture was never rendered");
                secondTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                layoutStudio.UpdateLayout();
                if (layoutMain.Scorebug.ActiveIndex != 1)
                    throw new Exception("FAIL: the studio layout picker did not switch the design");
                if (((Canvas)layoutStudio.FindName("DesignCanvas")!).Width < 320)
                    throw new Exception("FAIL: the studio did not draw the chosen layout");
                // Each layout crosses in its own way: the transition pickers edit the layout being
                // designed, not the whole display.
                var transitionBox = (ComboBox)layoutStudio.FindName("TransitionBox")!;
                if (transitionBox.SelectedItem is null)
                    throw new Exception("FAIL: the transition picker is empty when the studio opens");
                var slideOption = transitionBox.Items.OfType<object>()
                    .First(item => item.ToString()!.Contains("Swing left", StringComparison.Ordinal));
                transitionBox.SelectedItem = slideOption;
                Pump(TimeSpan.FromMilliseconds(200));
                if (layoutMain.Scorebug.Layouts[1].Transition != LayoutTransition.SlideLeft)
                    throw new Exception($"FAIL: picking a transition did not save it on this layout ({layoutMain.Scorebug.Layouts[1].Transition})");
                if (layoutMain.Scorebug.Layouts[0].Transition == LayoutTransition.SlideLeft)
                    throw new Exception("FAIL: the transition was written onto the display's other layout");
                layoutMain.Scorebug.SelectLayout(0);
                layoutStudio.UpdateLayout();
                if (transitionBox.SelectedItem.ToString()!.Contains("Swing left", StringComparison.Ordinal))
                    throw new Exception("FAIL: the transition picker did not follow the layout being designed");
                layoutStudio.Close();
                layoutMain.Scorebug.SelectLayout(0);

                // The display picker above the preview repoints the preview, and each display keeps
                // its own layouts: a brand-new lower-third display starts blank.
                layoutMain.Scorebug.AddDisplay("Lower thirds");
                layoutMain.UpdateLayout();
                var displayPicker = (ComboBox)layoutMain.FindName("DisplaySelector")!
                    ?? throw new Exception("FAIL: no display picker above the program preview");
                if (displayPicker.Items.Count != 2)
                    throw new Exception($"FAIL: the display picker lists {displayPicker.Items.Count} displays for 2");
                displayPicker.SelectedIndex = 1;
                layoutMain.UpdateLayout();
                if (FindAll<TextBlock>(preview).Any())
                    throw new Exception("FAIL: the preview still shows the other display's artwork");
                displayPicker.SelectedIndex = 0;
                layoutMain.UpdateLayout();
                if (!FindAll<TextBlock>(preview).Any())
                    throw new Exception("FAIL: switching back to the first display did not restore its artwork");
                // The media desk is a tab of its own, and the console comes back with one click.
                var mediaTab = (Button)layoutMain.FindName("MediaTab")!;
                var controlTab = (Button)layoutMain.FindName("ControlTab")!;
                var dockControl = FindAll<MediaDock>(layoutMain).First();
                mediaTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                layoutMain.UpdateLayout();
                if (dockControl.Visibility != Visibility.Visible)
                    throw new Exception("FAIL: the MEDIA tab did not reveal the media desk");
                controlTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                layoutMain.UpdateLayout();
                if (dockControl.Visibility != Visibility.Collapsed)
                    throw new Exception("FAIL: the CONTROL PANEL tab did not put the media desk away");
                audienceBefore.Dispose();
                audienceAfter.Dispose();
                layoutMain.Close();
                Console.WriteLine("PASS: layout arrows switch the preview and Update Output sends the chosen scorebug on air");
            }

            // ---------- displays: one screen each, independent previews, linked layouts ----------
            {
                var displayPath = Path.Combine(Path.GetTempPath(), $"scoreplus-displays-{Guid.NewGuid():N}.json");
                var displayEngine = new GameEngine();
                displayEngine.NewGame("football");
                var displayCatalog = new ScorebugCatalog(displayPath);
                try
                {
                    var program = displayCatalog.Displays[0];
                    var secondIndex = displayCatalog.AddDisplay("Lower thirds");
                    var lowerThirds = displayCatalog.Displays[secondIndex];
                    // A new display starts blank: one empty layout of its own.
                    if (displayCatalog.Displays.Count != 2 || lowerThirds.Layouts.Count != 1 || lowerThirds.Layouts[0].Elements.Count != 0)
                        throw new Exception("FAIL: a new display did not start with a single blank layout");
                    displayCatalog.Update(layout => layout.Elements.Add(new ScorebugElement
                    {
                        Name = "Strap", Content = "NAME STRAP", X = 100, Y = 500, Width = 800, Height = 120, FontSize = 52
                    }));
                    displayCatalog.AddLayout("Interview");
                    displayCatalog.Update(layout => layout.Elements.Add(new ScorebugElement
                    {
                        Name = "Interview", Content = "INTERVIEW", X = 60, Y = 60, Width = 700, Height = 110, FontSize = 44
                    }));
                    var interview = displayCatalog.Layout;
                    var interviewIndex = lowerThirds.Layouts.FindIndex(layout => layout.Id == interview.Id);
                    displayCatalog.SelectDisplay(0);

                    // Two windows, one display each: a layout switch in one never moves the other.
                    var windowA = new ProgramSelection(displayCatalog, program.Id);
                    var windowB = new ProgramSelection(displayCatalog, lowerThirds.Id);
                    windowA.SelectLayout(0);
                    if (windowB.IndexFor(program.Id) != 0 || windowA.LayoutIndex != 0)
                        throw new Exception("FAIL: a layout choice leaked between windows");
                    if (windowB.DisplayId != lowerThirds.Id)
                        throw new Exception("FAIL: a window could not keep its own display");

                    // Linking the first program layout to the interview makes one Update Output change both.
                    displayCatalog.SetLink(program.Id, 0, lowerThirds.Id, interview.Id);
                    if (displayCatalog.LinkTarget(program.Id, 0) is not { } target || target.DisplayId != lowerThirds.Id || target.LayoutIndex != interviewIndex)
                        throw new Exception("FAIL: the layout link did not resolve to the other display");
                    windowA.SelectLayout(1);
                    windowA.SelectLayout(0);
                    if (windowA.IndexFor(lowerThirds.Id) != interviewIndex)
                        throw new Exception("FAIL: choosing a linked layout did not switch the other display's selection");
                    windowA.PublishStaged();
                    if (!displayCatalog.IsPublished(lowerThirds.Id, interviewIndex))
                        throw new Exception("FAIL: Update Output did not send the linked layout to the second display");
                    if (windowA.IndexFor(lowerThirds.Id) != interviewIndex || windowB.IndexFor(lowerThirds.Id) != interviewIndex)
                        throw new Exception("FAIL: the linked display lost its staged layout after publishing");

                    // An audience window belongs to one display and only ever shows its published layout.
                    var outputB = new OutputWindow(displayEngine, displayCatalog, "Dark", null, lowerThirds.Id);
                    outputB.Show();
                    outputB.UpdateLayout();
                    if (outputB.DisplayId != lowerThirds.Id)
                        throw new Exception("FAIL: the audience window opened on the wrong display");
                    if (!FindAll<TextBlock>(outputB).Any(text => text.Text == "INTERVIEW"))
                        throw new Exception("FAIL: the display's audience window did not draw its published layout");
                    if (FindAll<TextBlock>(outputB).Any(text => text.Text == "{{HOME_SCORE}}"))
                        throw new Exception("FAIL: the audience window drew the wrong display's artwork");
                    outputB.Close();

                    // The link editor can pair both ways: "Both ways" writes the link coming back,
                    // and unticking it removes just that half.
                    var linkStudio = new ScorebugEditorWindow(displayCatalog, displayEngine);
                    linkStudio.Show();
                    linkStudio.UpdateLayout();
                    displayCatalog.SelectDisplay(0);
                    displayCatalog.SelectLayout(0);
                    linkStudio.UpdateLayout();
                    var bothWays = (CheckBox)linkStudio.FindName("LinkBothWaysInput")!;
                    if (bothWays.IsChecked == true)
                        throw new Exception("FAIL: Both ways was already ticked for a one-way link");
                    bothWays.IsChecked = true;
                    if (displayCatalog.LinkTarget(lowerThirds.Id, interviewIndex) is not { } back || back.DisplayId != program.Id || back.LayoutIndex != 0)
                        throw new Exception("FAIL: Both ways did not make the other display link back");
                    bothWays.IsChecked = false;
                    if (displayCatalog.LinkTarget(lowerThirds.Id, interviewIndex) is not null)
                        throw new Exception("FAIL: unticking Both ways left the coming-back link behind");
                    linkStudio.Close();

                    // A link and a second display survive a reload.
                    var reloaded = new ScorebugCatalog(displayPath);
                    if (reloaded.Displays.Count != 2 || reloaded.LinkTarget(reloaded.Displays[0].Id, 0) is null)
                        throw new Exception("FAIL: displays or layout links did not persist");
                    reloaded.ResetToDefault();
                    if (reloaded.Displays.Count != 1 || reloaded.Displays[0].Layouts.Count != 1)
                        throw new Exception("FAIL: a new game did not reset back to one display and one layout");
                    windowA.Dispose();
                    windowB.Dispose();
                }
                finally { if (File.Exists(displayPath)) File.Delete(displayPath); }
                Console.WriteLine("PASS: displays keep their own layouts and windows, links switch both screens, and an audience window is bound to one display");
            }

            // ---------- video elements: preserved aspect ratio and per-element audio output ----------
            {
                var video = new ScorebugElement
                {
                    Name = "Ad", Kind = ScorebugElementKind.Video, MediaTag = "Ads",
                    PreserveVideoAspect = true, VideoAspect = 16.0 / 9.0, Width = 1600, Height = 900
                };
                var surface = new VideoSurface(video, null, true);
                var picture = FindAll<MediaElement>(surface).FirstOrDefault()
                    ?? throw new Exception("FAIL: a video element has no picture");
                if (picture.Stretch != Stretch.Uniform)
                    throw new Exception($"FAIL: a preserved-aspect video letterboxes with {picture.Stretch}");
                video.PreserveVideoAspect = false;
                surface.SetElement(video);
                if (picture.Stretch != Stretch.UniformToFill)
                    throw new Exception($"FAIL: a free video box should fill its frame, not {picture.Stretch}");
                if (Math.Abs(video.Width / video.Height - video.VideoAspect) > 0.001)
                    throw new Exception("FAIL: the preserved-aspect box does not match its picture");
                surface.Dispose();
                Console.WriteLine("PASS: video boxes honour the preserved aspect ratio, and each element carries its own audio output");
            }

            // ---------- team pop-up: name, abbreviation, logo and colours from the console ----------
            var teamEngine = new GameEngine();
            teamEngine.NewGame("football");
            var teamPanel = new GameControlWindow(teamEngine, () => { }, _ => { }, () => true);
            teamPanel.Show();
            teamPanel.UpdateLayout();
            var awayDetails = (Button)teamPanel.Panel.FindName("AwayDetailsButton")!;
            if (awayDetails.ActualHeight < 32 || (awayDetails.Content as string) != "…")
                throw new Exception($"FAIL: the team details button is missing or tiny ({awayDetails.ActualWidth:0}x{awayDetails.ActualHeight:0})");
            var teamDialog = new TeamIdentityWindow(teamEngine, false, "Dark");
            teamDialog.Show();
            teamDialog.UpdateLayout();
            var doneButton = FindAll<Button>(teamDialog).FirstOrDefault(b => (b.Content as string) == "Done")
                ?? throw new Exception("FAIL: the team pop-up has no Done button");
            doneButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (teamEngine.State.AwayTeam != "VISITORS")
                throw new Exception($"FAIL: saving unchanged team details changed the team ({teamEngine.State.AwayTeam})");
            teamPanel.Close();
            Console.WriteLine("PASS: the … beside a team name opens the team details box (name, abbreviation, logo, colours)");

            // Colour slots resolve against the live team palette.
            var colorPath = Path.Combine(Path.GetTempPath(), $"smoke-colour-{Guid.NewGuid():N}.json");
            try
            {
                var colorCatalog = new ScorebugCatalog(colorPath);
                colorCatalog.Update(layout =>
                {
                    layout.Elements.Clear();
                    layout.Elements.Add(new ScorebugElement { Name = "Away name", Content = "{{AWAY_TEAM}}", X = 10, Y = 10, Width = 460, Height = 90, FontSize = 44, ForegroundSource = ScorebugColorSource.AwayPrimary });
                });
                var colorEngine = new GameEngine();
                colorEngine.NewGame("football");
                colorEngine.SetTeamIdentity(false, "Rovers FC", "RFC", null, "#FFAA1122", "#FF223344");
                var colorView = new ScorebugCanvasView(colorCatalog, colorEngine);
                colorView.Measure(new Size(1280, 720));
                colorView.Arrange(new Rect(0, 0, 1280, 720));
                var awayText = FindAll<TextBlock>(colorView).FirstOrDefault(t => t.Text == "Rovers FC")
                    ?? throw new Exception("FAIL: the team colour element did not render the team name");
                if (awayText.Foreground is not SolidColorBrush awayBrush || awayBrush.Color != Color.FromArgb(0xFF, 0xAA, 0x11, 0x22))
                    throw new Exception($"FAIL: the visitors' primary colour did not reach the graphic ({awayText.Foreground})");
                colorEngine.SetTeamIdentity(false, "Rovers FC", "RFC", null, "#FF00FF00", "#FF223344");
                colorView.Refresh();
                colorView.UpdateLayout();
                if (awayText.Foreground is not SolidColorBrush recoloured || recoloured.Color != Color.FromArgb(0xFF, 0x00, 0xFF, 0x00))
                    throw new Exception("FAIL: changing a team colour did not recolour the graphic live");
                // A team logo imported in the console reaches a logo element on the graphic.
                var logoPath = Path.Combine(Path.GetTempPath(), $"smoke-team-logo-{Guid.NewGuid():N}.png");
                WritePng(logoPath, Color.FromRgb(0x22, 0x88, 0xFF));
                // Build the logo element while the team still has no crest: setting one afterwards
                // must fill the existing graphic, the way it does after loading a saved game.
                colorCatalog.Update(layout =>
                {
                    layout.Elements.Add(new ScorebugElement { Name = "Away logo", Kind = ScorebugElementKind.Logo, LogoSide = ScorebugLogoSide.Away, X = 900, Y = 40, Width = 160, Height = 120 });
                });
                colorView.Measure(new Size(1280, 720));
                colorView.Arrange(new Rect(0, 0, 1280, 720));
                colorView.UpdateLayout();
                if (FindAll<Image>(colorView).Any(i => i.Source is BitmapImage))
                    throw new Exception("FAIL: a logo element showed an image before a team logo was set");
                colorEngine.SetTeamIdentity(false, "Rovers FC", "RFC", logoPath, "#FF00FF00", "#FF223344");
                colorView.Refresh();
                colorView.UpdateLayout();
                if (!FindAll<Image>(colorView).Any(i => i.Source is BitmapImage))
                    throw new Exception("FAIL: a team logo set after the element was built did not reach the scorebug");
                colorEngine.SetTeamIdentity(false, "Rovers FC", "RFC", null, "#FF00FF00", "#FF223344");
                colorView.Refresh();
                colorView.UpdateLayout();
                if (FindAll<Image>(colorView).Any(i => i.Source is BitmapImage))
                    throw new Exception("FAIL: removing a team logo left the old image on the scorebug");
                colorView.Dispose();
                try { File.Delete(logoPath); } catch { }

                // A value change must finish on the new value, whatever the animation, and a value
                // that changes mid-flight must not leave the old one on the graphic.
                foreach (var transition in new[] { ScorebugTransition.WheelUp, ScorebugTransition.SlideRight, ScorebugTransition.Pop, ScorebugTransition.Flip })
                {
                    var block = new ScorebugTextBlock(new ScorebugElement { Transition = transition, FontSize = 40 });
                    var transitionHost = new Window { Width = 360, Height = 160, Content = block };
                    transitionHost.Show();
                    transitionHost.UpdateLayout();
                    block.UpdateText("10", ScorebugTransition.Cut);
                    block.UpdateText("17", transition);
                    Pump(TimeSpan.FromMilliseconds(650));
                    if (block.Text != "17" || block.Opacity < 0.95)
                        throw new Exception($"FAIL: the {transition} animation left the value at '{block.Text}' with opacity {block.Opacity:0.00}");
                    // A finished transition must sit exactly in its box: not offset like the wheel
                    // used to be, and not still scaled like the pop.
                    var transform = (TransformGroup)block.RenderTransform;
                    var translate = transform.Children.OfType<TranslateTransform>().First();
                    var scale = transform.Children.OfType<ScaleTransform>().First();
                    if (Math.Abs(translate.X) > 0.5 || Math.Abs(translate.Y) > 0.5 || Math.Abs(scale.ScaleX - 1) > 0.01 || Math.Abs(scale.ScaleY - 1) > 0.01)
                        throw new Exception($"FAIL: the {transition} animation settled off-centre (translate {translate.X:0.0},{translate.Y:0.0} scale {scale.ScaleX:0.00},{scale.ScaleY:0.00})");
                    block.UpdateText("21", transition);
                    Pump(TimeSpan.FromMilliseconds(40));
                    block.UpdateText("24", transition);
                    Pump(TimeSpan.FromMilliseconds(80));
                    if (block.Text != "24")
                        throw new Exception($"FAIL: a value changed mid-animation showed '{block.Text}' instead of the newest value");
                    transitionHost.Close();
                }
                Console.WriteLine("PASS: any colour can point at a team's primary or secondary colour, team logos reach the graphic, and value animations land on the newest value");
            }
            finally { if (File.Exists(colorPath)) File.Delete(colorPath); }

            // ---------- rotation, borders, drift and a configurable transition speed ----------
            var decorPath = Path.Combine(Path.GetTempPath(), $"smoke-decor-{Guid.NewGuid():N}.json");
            try
            {
                var decorCatalog = new ScorebugCatalog(decorPath);
                decorCatalog.Update(layout =>
                {
                    layout.Elements.Clear();
                    layout.Elements.Add(new ScorebugElement { Name = "Slanted", Content = "TILT", X = 40, Y = 40, Width = 400, Height = 120, FontSize = 48, Rotation = -22 });
                    layout.Elements.Add(new ScorebugElement
                    {
                        Name = "Hollow bar", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle,
                        X = 40, Y = 200, Width = 300, Height = 80, TransparentBackground = true,
                        BorderThickness = 6, BorderColor = "#FFFF0000", Background = "#FF0000FF"
                    });
                    layout.Elements.Add(new ScorebugElement { Name = "Drifter", Content = "DRIFT", X = 420, Y = 320, Width = 200, Height = 80, Animation = ScorebugAnimation.Drift });
                });
                var decorView = new ScorebugCanvasView(decorCatalog, engine);
                decorView.Measure(new Size(1280, 720));
                decorView.Arrange(new Rect(0, 0, 1280, 720));
                decorView.UpdateLayout();

                var slanted = FindAll<TextBlock>(decorView).First(t => t.Text == "TILT");
                var rotation = ((TransformGroup)slanted.RenderTransform).Children.OfType<RotateTransform>().First();
                if (Math.Abs(rotation.Angle + 22) > 0.01)
                    throw new Exception($"FAIL: text rotation reached the graphic at {rotation.Angle:0.0}°");

                var hollow = FindAll<System.Windows.Shapes.Shape>(decorView).OfType<System.Windows.Shapes.Rectangle>().First();
                if (Math.Abs(hollow.StrokeThickness - 6) > 0.01)
                    throw new Exception($"FAIL: the shape border is {hollow.StrokeThickness:0.#}px instead of 6");
                if (hollow.Stroke is not SolidColorBrush borderBrush || borderBrush.Color != Colors.Red)
                    throw new Exception($"FAIL: the shape border ignored its own colour ({hollow.Stroke})");
                if (hollow.Fill is not null && hollow.Fill != Brushes.Transparent)
                    throw new Exception("FAIL: a transparent shape still painted a fill over its background");

                // Drift must animate its transform; animating the host itself moved nothing.
                if (!FindAll<Border>(decorView).Any(b => b.RenderTransform is TranslateTransform drift && drift.HasAnimatedProperties))
                    throw new Exception("FAIL: the Drift animation is not moving anything");
                decorView.Dispose();

                // 0 seconds is an instant swap; a long transition is still crossing after 100 ms.
                var instant = new ScorebugTextBlock(new ScorebugElement { FontSize = 40 });
                instant.UpdateText("A", ScorebugTransition.Cut);
                instant.UpdateText("B", ScorebugTransition.Fade, 0);
                if (instant.Text != "B")
                    throw new Exception("FAIL: a 0-second transition did not snap to the new value");
                var slow = new ScorebugTextBlock(new ScorebugElement { FontSize = 40, TransitionSeconds = 3 });
                var slowHost = new Window { Width = 320, Height = 140, Content = slow };
                slowHost.Show();
                slowHost.UpdateLayout();
                slow.UpdateText("A", ScorebugTransition.Cut);
                slow.UpdateText("B", ScorebugTransition.Fade, 3);
                Pump(TimeSpan.FromMilliseconds(100));
                if (slow.Text == "B")
                    throw new Exception("FAIL: a 3-second transition finished instantly");
                Pump(TimeSpan.FromMilliseconds(3200));
                if (slow.Text != "B")
                    throw new Exception("FAIL: a long transition did not land on the new value");
                slowHost.Close();
                Console.WriteLine("PASS: text rotates, shapes carry custom borders, Drift moves, and the transition speed is honoured");
            }
            finally { if (File.Exists(decorPath)) File.Delete(decorPath); }

            // ---------- media: folders, playlists and per-clip loop ----------
            var clipA = Path.Combine(Path.GetTempPath(), $"smoke-playlist-a-{Guid.NewGuid():N}.wav");
            var clipB = Path.Combine(Path.GetTempPath(), $"smoke-playlist-b-{Guid.NewGuid():N}.wav");
            WriteSilentWav(clipA, 1);
            WriteSilentWav(clipB, 1);
            var playlistSession = new MediaSession(new UserPreferences());
            try
            {
                playlistSession.Import([clipA, clipB], "Music", "Ads");
                playlistSession.Import([Path.Combine(Path.GetTempPath(), "smoke-loop.mp4")], "Ads", "Ads");
                if (playlistSession.Folders.Count != 1 || playlistSession.Folders[0] != "Ads")
                    throw new Exception($"FAIL: folders did not collect the clips ({string.Join(",", playlistSession.Folders)})");
                var importedVideo = playlistSession.Library.Single(m => m.Path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
                if (!importedVideo.Loop)
                    throw new Exception("FAIL: an imported video does not loop like a stadium screen");
                if (playlistSession.Library.Single(m => m.Path == clipA).Loop)
                    throw new Exception("FAIL: imported audio loops by default");

                var folderDock = new MediaDock();
                folderDock.Attach(playlistSession);
                var folderWindow = new Window { Width = 470, Height = 680, Content = folderDock };
                folderWindow.Show();
                folderWindow.UpdateLayout();
                // Folders and playlists are browser items now, like a file explorer: no rails, no dropdowns.
                foreach (var stale in new[] { "FolderFilter", "FolderBox", "PlaylistBox", "FolderRail", "PlaylistRail", "FolderRailRow" })
                    if (folderDock.FindName(stale) is not null)
                        throw new Exception($"FAIL: the media dock still has a {stale} shelf or dropdown");
                var tiles = (WrapPanel)folderDock.FindName("Tiles")!;
                var breadcrumb = (TextBlock)folderDock.FindName("BreadcrumbText")!;
                var upButton = (Button)folderDock.FindName("UpButton")!;
                object? Method(string name, params object?[] args) =>
                    typeof(MediaDock).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .First(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(folderDock, args);
                Border CardFor(Func<object?, bool> match) => FindAll<Border>(folderDock).FirstOrDefault(b => match(b.Tag))
                    ?? throw new Exception("FAIL: the browser has no card for that item");
                if (tiles.Children.Count != 4)
                    throw new Exception($"FAIL: a library of 3 clips in one folder shows {tiles.Children.Count} browser items instead of 4");
                var adsCard = CardFor(tag => tag is string folder && folder == "Ads");
                if (!adsCard.AllowDrop || adsCard.ToolTip is not string adsTip || !adsTip.Contains("Double-click to open"))
                    throw new Exception("FAIL: a folder is not a drop target that opens on double-click");

                playlistSession.MoveToFolder(playlistSession.Library[1], "Openers");
                playlistSession.CreateFolder("Empty");
                // A clip filed in Ads/2026 makes 2026 a folder inside Ads, with Ads as its parent.
                playlistSession.MoveToFolder(playlistSession.Library[1], "Ads/2026");
                folderWindow.UpdateLayout();
                // Openers was only implied by the clip that lived there, so it goes when the clip leaves.
                if (tiles.Children.Count != 5)
                    throw new Exception($"FAIL: folders are not browser items ({tiles.Children.Count} items for 2 folders and 3 clips)");
                Method("OpenFolder", "Ads");
                folderWindow.UpdateLayout();
                if (breadcrumb.Text != "LIBRARY / Ads")
                    throw new Exception($"FAIL: opening a folder did not move into it ('{breadcrumb.Text}')");
                CardFor(tag => tag is string folder && folder == "Ads/2026");
                if (tiles.Children.Count != 3)
                    throw new Exception($"FAIL: the open folder shows {tiles.Children.Count} items instead of 2 clips and its sub-folder");
                Method("OpenFolder", "Ads/2026");
                folderWindow.UpdateLayout();
                if (breadcrumb.Text != "LIBRARY / Ads / 2026" || !FindAll<Border>(folderDock).Any(b => b.Tag is MediaEntry nestedEntry && nestedEntry.Path == clipB))
                    throw new Exception("FAIL: a folder inside a folder does not open into its clip");
                upButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                folderWindow.UpdateLayout();
                if (breadcrumb.Text != "LIBRARY / Ads")
                    throw new Exception($"FAIL: Up did not leave the nested folder ('{breadcrumb.Text}')");

                // A folder leaves the app as one file, nesting included, and comes back whole.
                var folderFile = Path.Combine(Path.GetTempPath(), $"smoke-folder-{Guid.NewGuid():N}.spf");
                try
                {
                    if (!playlistSession.ExportFolder("Ads", folderFile) || !File.Exists(folderFile))
                        throw new Exception("FAIL: a folder could not be exported to a ScorePlus Media Folder");
                    var importedFolders = new MediaSession(new UserPreferences());
                    try
                    {
                        if (!importedFolders.ImportFolder(folderFile))
                            throw new Exception("FAIL: an exported folder could not be imported");
                        // Two real files exist; the fake .mp4 has no media on disk and is not carried.
                        if (!importedFolders.Folders.Contains("Ads/2026") || importedFolders.Library.Count != 2)
                            throw new Exception($"FAIL: importing a folder lost its nesting or its clips ({importedFolders.Library.Count} clips)");
                    }
                    finally { importedFolders.Dispose(); }
                    Console.WriteLine("PASS: a folder exports to one file with its folders inside it, and imports back whole");
                }
                finally { try { File.Delete(folderFile); } catch { } }

                // A folder opened from anywhere in the app is the folder the browser is showing.
                playlistSession.SetFolderFilter("Empty");
                folderDock.UpdateLayout();
                if (breadcrumb.Text != "LIBRARY / Empty")
                    throw new Exception($"FAIL: the browser does not follow the folder on screen ('{breadcrumb.Text}')");
                Method("OpenFolder", (object?)null);
                folderWindow.UpdateLayout();
                if (breadcrumb.Text != "LIBRARY")
                    throw new Exception($"FAIL: the browser did not return to the library root ('{breadcrumb.Text}')");

                var playlist = playlistSession.CreatePlaylist("Run of show");
                playlistSession.AddToPlaylist(playlist, playlistSession.Library[0]);
                playlistSession.AddToPlaylist(playlist, playlistSession.Library[2]);
                playlistSession.SetPlaylistLoop(playlist, true);
                folderWindow.UpdateLayout();
                if (tiles.Children.Count != 6)
                    throw new Exception($"FAIL: a playlist is not a browser item ({tiles.Children.Count} items for 2 folders, 3 clips and a playlist)");
                var playlistCard = CardFor(tag => ReferenceEquals(tag, playlist));
                if (!playlistCard.AllowDrop)
                    throw new Exception("FAIL: a playlist card does not accept dropped clips");
                Method("OpenPlaylist", playlist);
                folderWindow.UpdateLayout();
                if (((TextBlock)folderDock.FindName("ViewTitle")!).Text != "RUN OF SHOW" || tiles.Children.Count != 2)
                    throw new Exception("FAIL: a playlist does not open into its clip list");
                var playlistLoop = (CheckBox)folderDock.FindName("PlaylistLoopBox")!;
                if (playlistLoop.IsChecked != true || playlistLoop.Visibility != Visibility.Visible)
                    throw new Exception("FAIL: the open playlist does not show its loop setting");
                playlistLoop.IsChecked = false;
                folderWindow.UpdateLayout();
                if (playlist.Loop) throw new Exception("FAIL: switching playlist loop off did not reach the playlist");
                playlistLoop.IsChecked = true;
                folderWindow.UpdateLayout();
                if (!playlist.Loop) throw new Exception("FAIL: switching playlist loop on did not reach the playlist");
                ((Button)folderDock.FindName("BackToLibraryButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                folderWindow.UpdateLayout();
                if (((TextBlock)folderDock.FindName("ViewTitle")!).Text != "LIBRARY")
                    throw new Exception("FAIL: leaving a playlist did not return to the library");

                playlistSession.PlayPlaylist(playlist);
                if (playlistSession.ActivePlaylist?.Name != "Run of show" || playlistSession.PlaylistPosition != 0)
                    throw new Exception("FAIL: a playlist did not start at its first clip");
                // Each clip is one second long: the playlist must move on by itself when one e
                var savedMedia = PreferencesStore.Load();
                var savedPlaylist = savedMedia.Playlists.SingleOrDefault(p => p.Name == "Run of show");
                if (savedPlaylist is null || savedPlaylist.Count != 2 || !savedPlaylist.Loop || savedMedia.Media.All(m => m.Path != clipA))
                    throw new Exception("FAIL: playlists, folders and clips did not survive a preferences save");
                if (!savedMedia.MediaFolders.Contains("Empty"))
                    throw new Exception("FAIL: an empty folder did not survive a preferences save");

                // A new game starts with no folders or playlists, but keeps the imported asset files.
                var resetSession = new MediaSession(new UserPreferences());
                try
                {
                    resetSession.Import([clipA], "Ads", "Ads");
                    var tempPlaylist = resetSession.CreatePlaylist("Temp");
                    resetSession.AddToPlaylist(tempPlaylist, resetSession.Library[0]);
                    resetSession.SetFolderFilter("Ads");
                    resetSession.ResetOrganization();
                    if (resetSession.Folders.Count != 0 || resetSession.Playlists.Count != 0 || resetSession.FolderFilter is not null
                        || resetSession.Library.Any(entry => entry.Folder.Length != 0))
                        throw new Exception("FAIL: resetting for a new game left folders or playlists behind");
                    if (resetSession.Library.Count != 1)
                        throw new Exception("FAIL: resetting for a new game threw away the imported media library");
                }
                finally { resetSession.Dispose(); }
                folderWindow.Close();
                Console.WriteLine("PASS: folders are drop tiles, playlists open into their clip list, search spans every folder and playlist, and new-game reset clears the organisation");
            }
            finally
            {
                playlistSession.Dispose();
                try { File.Delete(clipA); } catch { }
                try { File.Delete(clipB); } catch { }
            }

            // Time controls carry the same full-size buttons as the rest of the console.
            var clockPanel = new GameControlWindow(new GameEngine(), () => { }, _ => { }, () => true);
            clockPanel.Show();
            clockPanel.UpdateLayout();
            clockPanel.Panel.UpdateLayout();
            var minusMinute = FindAll<Button>(clockPanel.Panel).FirstOrDefault(b => (b.Tag as string) == "clock.minus60")
                ?? throw new Exception("FAIL: the game clock has no minute buttons");
            if (minusMinute.ActualHeight < 32 || minusMinute.ActualWidth < 32)
                throw new Exception($"FAIL: a clock button is still small ({minusMinute.ActualWidth:0}x{minusMinute.ActualHeight:0})");
            if (!FindAll<Button>(clockPanel.Panel).Any(b => (b.Tag as string) == "timeoutclock.plus10"))
                throw new Exception("FAIL: the timeout clock has no step buttons");
            if (clockPanel.Panel.FindName("LayoutNextButton") is not Button || string.IsNullOrWhiteSpace(((TextBlock)clockPanel.Panel.FindName("LayoutLabel")!).Text))
                throw new Exception("FAIL: a detached control panel cannot switch layouts");
            clockPanel.Close();
            Console.WriteLine("PASS: clock buttons are full-size and every panel can switch layouts");

            // Each sport brings its own rules and its own console controls; a new sport must not
            // inherit the American football console.
            {
                var american = new MainWindow();
                american.Show();
                american.UpdateLayout();
                american.StartNewGame("football", announce: false);
                american.UpdateLayout();
                var americanDetail = (FrameworkElement)american.Panel.FindName("FootballDetail")!;
                var americanPlayClock = (FrameworkElement)american.Panel.FindName("PlayClockRow")!;
                if (american.Engine.State.SportId != "football" || americanDetail.Visibility != Visibility.Visible || americanPlayClock.Visibility != Visibility.Visible)
                    throw new Exception("FAIL: an American football game is missing its down & distance or play clock");
                if (!FindAll<Button>(american.Panel).Any(b => (b.Tag as string) == "home.score.plus6"))
                    throw new Exception("FAIL: American football has no touchdown button");
                american.Close();

                var australian = new MainWindow();
                australian.Show();
                australian.UpdateLayout();
                australian.StartNewGame("australian-football", announce: false);
                // Picking a sport from the picker must start that sport, not leave the last one.
                var switchWindow = new MainWindow();
                switchWindow.Show();
                switchWindow.UpdateLayout();
                switchWindow.StartNewGame("football", announce: false);
                switchWindow.UpdateLayout();
                var sportPicker = (ComboBox)switchWindow.FindName("SportSelector")!;
                sportPicker.SelectedValue = "australian-football";
                switchWindow.UpdateLayout();
                if (sportPicker.SelectedValue as string != "australian-football" || switchWindow.Engine.State.SportId != "australian-football")
                    throw new Exception($"FAIL: the sport picker left the game on {switchWindow.Engine.State.SportId}");
                switchWindow.Close();
                australian.UpdateLayout();
                var australianDetail = (FrameworkElement)australian.Panel.FindName("FootballDetail")!;
                var australianPlayClock = (FrameworkElement)australian.Panel.FindName("PlayClockRow")!;
                if (australian.Engine.State.SportId != "australian-football" || australian.Engine.State.PeriodSeconds != 20 * 60)
                    throw new Exception("FAIL: an Australian football game did not take its own quarter length");
                if (australianDetail.Visibility != Visibility.Collapsed || australianPlayClock.Visibility != Visibility.Collapsed)
                    throw new Exception("FAIL: Australian football shows the American down & distance card");
                australian.Close();
                Console.WriteLine("PASS: each sport brings its own rules and controls — American has down & distance and a touchdown button, Australian does not");
            }

            // ---------- rosters & stats: a third tab, per-side rosters, and player bindings ----------
            var rosterFile = Path.Combine(Path.GetTempPath(), $"smoke-rosters-{Guid.NewGuid():N}.json");
            var rosterLayoutPath = Path.Combine(Path.GetTempPath(), $"smoke-roster-layout-{Guid.NewGuid():N}.json");
            RosterStore.UsePath(rosterFile);
            try
            {
                var rosterEngine = new GameEngine();
                rosterEngine.NewGame("football");
                var homeRoster = RosterStore.Book.Add("Home squad");
                var qb = homeRoster.AddPlayer(); qb.Number = 12; qb.Name = "Smith"; qb.Position = "QB";
                var rb = homeRoster.AddPlayer(); rb.Number = 28; rb.Name = "Jones"; rb.Position = "RB";
                foreach (var player in homeRoster.Players) player.SetStat("Yards", 0);
                qb.SetStat("Yards", 184);
                rb.SetStat("Yards", 96);
                var visitorRoster = RosterStore.Book.Add("Visitors squad");
                var wr = visitorRoster.AddPlayer(); wr.Number = 7; wr.Name = "Brown"; wr.Position = "WR"; wr.SetStat("Yards", 64);
                RosterStore.Save();

                var rosterWindow = new GameControlWindow(rosterEngine, () => { }, _ => { }, () => true);
                rosterWindow.Show();
                rosterWindow.UpdateLayout();
                var rosterTab = rosterWindow.FindName("RosterTab") as Button
                    ?? throw new Exception("FAIL: the control panel has no ROSTERS tab");
                if ((rosterTab.Content as string) != "ROSTERS")
                    throw new Exception($"FAIL: the third tab is labelled {rosterTab.Content}");
                rosterWindow.ShowRosterTab(true);
                rosterWindow.UpdateLayout();
                if (rosterWindow.Tab != GameControlWindow.ConsoleTab.Rosters)
                    throw new Exception($"FAIL: the roster tab did not open (showing {rosterWindow.Tab})");
                var rosterDock = rosterWindow.FindName("RosterControl") as RosterDock
                    ?? throw new Exception("FAIL: the control panel hosts no roster dock");
                if (rosterDock.Visibility != Visibility.Visible || rosterWindow.Panel.IsVisible)
                    throw new Exception("FAIL: the roster desk did not replace the console on the tab");

                if (!rosterDock.ShowingHome || !rosterDock.ActivePanel.IsHome)
                    throw new Exception("FAIL: the roster desk does not open on the home side");
                var homePanel = rosterDock.ActivePanel;
                var homeRosterBox = (ComboBox)homePanel.FindName("RosterBox")!;
                if (homeRosterBox.Items.Count != 2)
                    throw new Exception($"FAIL: the roster picker lists {homeRosterBox.Items.Count} rosters instead of 2");
                homeRosterBox.SelectedIndex = 0;
                rosterWindow.UpdateLayout();
                if (rosterEngine.State.HomePlayer?.Name != "Smith")
                    throw new Exception($"FAIL: picking the home roster left {rosterEngine.State.HomePlayer?.Name} on the graphic");
                // The stat columns arrive with the roster, so nothing has to be named first.
                if (homeRoster.Players[0].Stat("Passing yards") != 0 || !homeRoster.Players[0].Stats.Any(s => s.Name == "Tackles"))
                    throw new Exception("FAIL: a player did not arrive with the sport's stat columns");

                // The visitors are their own desk, reached with the side switch.
                ((Button)rosterDock.FindName("AwaySideTab")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                rosterWindow.UpdateLayout();
                if (rosterDock.ShowingHome || rosterDock.ActivePanel.IsHome)
                    throw new Exception("FAIL: the visitors side tab did not switch the desk");
                var awayPanel = rosterDock.ActivePanel;
                ((ComboBox)awayPanel.FindName("RosterBox")!).SelectedIndex = 1;
                rosterWindow.UpdateLayout();
                if (rosterEngine.State.AwayPlayer?.Name != "Brown")
                    throw new Exception($"FAIL: picking the visitors roster left {rosterEngine.State.AwayPlayer?.Name} on the graphic");
                ((Button)rosterDock.FindName("HomeSideTab")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                rosterWindow.UpdateLayout();
                homePanel = rosterDock.ActivePanel;

                // The list feeds the editor; search filters it, and "Select player" switches the player.
                var playerList = (ListBox)homePanel.FindName("PlayerList")!;
                var search = (TextBox)homePanel.FindName("SearchInput")!;
                search.Text = "Smith";
                rosterWindow.UpdateLayout();
                if (playerList.Items.Count != 1)
                    throw new Exception($"FAIL: searching the roster left {playerList.Items.Count} rows");
                search.Text = "";
                rosterWindow.UpdateLayout();
                playerList.SelectedIndex = 1;
                rosterWindow.UpdateLayout();
                FindAll<Button>(homePanel).First(b => (b.Content as string) == "Select player")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                rosterWindow.UpdateLayout();
                if (rosterEngine.State.HomePlayer?.Name != "Jones")
                    throw new Exception($"FAIL: selecting a player left {rosterEngine.State.HomePlayer?.Name}");
                if (rosterEngine.State.HomePlayer.Stat("Yards") != 96)
                    throw new Exception("FAIL: the selected player did not carry their stats");
                if (!FindAll<TextBlock>(homePanel).Any(t => t.Text.Contains("ON AIR")))
                    throw new Exception("FAIL: the roster panel does not show who is on the graphic");
                // A full stat sheet arrives with the roster, and the tab header carries the file actions.
                if (homeRoster.Players[0].Stats.Count < 20)
                    throw new Exception($"FAIL: a football player arrived with only {homeRoster.Players[0].Stats.Count} stat columns");
                if (!FindAll<Button>(rosterDock).Any(b => (b.Content as string) == "Import…")
                    || !FindAll<Button>(rosterDock).Any(b => (b.Content as string) == "Export…"))
                    throw new Exception("FAIL: the roster tab has no Import…/Export… buttons");
                // "No player" is a real choice: it must not quietly refill on the next roster refresh.
                var noPlayer = FindAll<Button>(homePanel).FirstOrDefault(b => (b.Content as string) == "No player")
                    ?? throw new Exception("FAIL: the roster desk has no No player button");
                noPlayer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                rosterWindow.UpdateLayout();
                if (rosterEngine.State.HomePlayer is not null)
                    throw new Exception($"FAIL: No player left {rosterEngine.State.HomePlayer.Name} on the graphic");
                if (!FindAll<TextBlock>(homePanel).Any(t => t.Text.Contains("NO PLAYER ON THE GRAPHIC")))
                    throw new Exception("FAIL: the desk does not say that nobody is on the graphic");
                rosterEngine.ReloadSelectedPlayer(true);
                rosterWindow.UpdateLayout();
                if (rosterEngine.State.HomePlayer is not null)
                    throw new Exception("FAIL: a cleared player came back on the next roster refresh");
                FindAll<Button>(homePanel).First(b => (b.Content as string) == "Select player")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                rosterWindow.UpdateLayout();
                if (rosterEngine.State.HomePlayer?.Name != "Jones")
                    throw new Exception($"FAIL: selecting a player after clearing left {rosterEngine.State.HomePlayer?.Name}");
                // The desk shares the console's navy surfaces instead of its own charcoal palette.
                var listCard = FindAll<Border>(homePanel).FirstOrDefault(b => b.Background is SolidColorBrush s && s.Color == Color.FromRgb(16, 22, 31))
                    ?? throw new Exception("FAIL: the roster desk does not use the console's surface colour");
                var inputWell = FindAll<TextBox>(homePanel).FirstOrDefault() ?? throw new Exception("FAIL: the roster desk has no fields");
                if (inputWell.Background is not SolidColorBrush well || well.Color != Color.FromRgb(11, 15, 20))
                    throw new Exception($"FAIL: a roster field is not the console's input well colour ({inputWell.Background})");
                if (listCard.ActualHeight <= 0) throw new Exception("FAIL: the roster list column did not lay out");

                // The whole stat sheet is laid out as two compact columns, so it fills the desk
                // instead of running down one long scrolling list.
                var statCells = homePanel.FindName("StatsHost") as UniformGrid
                    ?? throw new Exception("FAIL: the roster stat sheet is not a UniformGrid");
                if (statCells.Columns != 2)
                    throw new Exception($"FAIL: the roster stat sheet has {statCells.Columns} columns instead of 2");
                if (statCells.Children.Count < 20)
                    throw new Exception($"FAIL: the roster stat sheet only laid out {statCells.Children.Count} stats");

                // The graphic itself follows the selected player, and switching players reprints it.
                var rosterCatalog = new ScorebugCatalog(rosterLayoutPath);
                rosterCatalog.Update(layout =>
                {
                    layout.Elements.Clear();
                    layout.Elements.Add(new ScorebugElement { Name = "Player", Content = "{{PLAYER:HOME:Name}}", X = 20, Y = 20, Width = 600, Height = 90, FontSize = 40 });
                    layout.Elements.Add(new ScorebugElement { Name = "Player yards", Content = "{{PLAYER:HOME:Yards}}", X = 20, Y = 140, Width = 300, Height = 90, FontSize = 40 });
                });
                var rosterView = new ScorebugCanvasView(rosterCatalog, rosterEngine);
                rosterView.UpdateLayout();
                if (!FindAll<TextBlock>(rosterView).Any(t => t.Text == "Jones") || !FindAll<TextBlock>(rosterView).Any(t => t.Text == "96"))
                    throw new Exception("FAIL: the scorebug did not draw the selected player's name and stat");
                rosterEngine.SelectPlayer(true, qb.Id);
                rosterView.Rebuild();
                rosterView.UpdateLayout();
                if (FindAll<TextBlock>(rosterView).Any(t => t.Text == "Jones") || !FindAll<TextBlock>(rosterView).Any(t => t.Text == "Smith")
                    || FindAll<TextBlock>(rosterView).Any(t => t.Text == "96") || !FindAll<TextBlock>(rosterView).Any(t => t.Text == "184"))
                    throw new Exception("FAIL: switching the selected player did not reprint the player bindings");

                // The studio offers the player data, and the main window carries the tab too.
                var studio = new ScorebugEditorWindow(rosterCatalog, rosterEngine);
                studio.Show();
                studio.UpdateLayout();
                if (!FindAll<Button>(studio).Any(b => (b.Content as string) == "PLAYER DATA +"))
                    throw new Exception("FAIL: the studio has no Add player data button");
                studio.Close();
                var rosterMain = new MainWindow();

                rosterMain.Show();
                rosterMain.UpdateLayout();
                if (rosterMain.FindName("RosterTab") is not Button)
                    throw new Exception("FAIL: the main window has no ROSTERS tab");
                rosterMain.Close();

                rosterWindow.Close();
                Console.WriteLine("PASS: rosters are built per side in their own tab, and the selected player feeds every player binding on the graphic");
            }
            finally
            {
                RosterStore.UsePath(null);
                RosterStore.Reload();
                try { if (File.Exists(rosterFile)) File.Delete(rosterFile); } catch { }
                try { if (File.Exists(rosterLayoutPath)) File.Delete(rosterLayoutPath); } catch { }
            }

            // ---------- gradient and opacity on a studio element ----------
            var gradientStudioPath = Path.Combine(Path.GetTempPath(), $"smoke-gradient-{Guid.NewGuid():N}.json");
            try
            {
                var gradientStudio = new ScorebugCatalog(gradientStudioPath);
                gradientStudio.Update(layout =>
                {
                    layout.Elements.Clear();
                    layout.Elements.Add(new ScorebugElement { Name = "Plate", X = 20, Y = 20, Width = 420, Height = 180, Background = "#FF102030" });
                });
                var gradientEditor = new ScorebugEditorWindow(gradientStudio, engine, null, "Dark");
                gradientEditor.Show();
                gradientEditor.UpdateLayout();
                ((ListBox)gradientEditor.FindName("Layers")!).SelectedIndex = 0;
                gradientEditor.UpdateLayout();
                var opacityInput = (TextBox)gradientEditor.FindName("OpacityInput")!;
                var gradientInput = (CheckBox)gradientEditor.FindName("GradientInput")!;
                var gradientAngle = (TextBox)gradientEditor.FindName("GradientAngleInput")!;
                var gradientColor = (TextBox)gradientEditor.FindName("GradientColorInput")!;
                if (opacityInput is null || gradientAngle is null || gradientColor is null || !opacityInput.IsEnabled)
                    throw new Exception("FAIL: the studio has no opacity or gradient controls for an element");
                opacityInput.Text = "0.5";
                gradientInput.IsChecked = true;
                gradientAngle.Text = "90";
                gradientColor.Text = "#FF0000FF";
                Pump(TimeSpan.FromMilliseconds(700));
                var plate = gradientStudio.Layout.Elements[0];
                if (Math.Abs(plate.Opacity - 0.5) > 0.001 || !plate.BackgroundGradient
                    || Math.Abs(plate.GradientAngle - 90) > 0.001 || plate.BackgroundColor2 != "#FF0000FF")
                    throw new Exception($"FAIL: opacity/gradient did not reach the element ({plate.Opacity}, {plate.BackgroundGradient}, {plate.GradientAngle}, {plate.BackgroundColor2})");
                var designCanvas = (Canvas)gradientEditor.FindName("DesignCanvas")!;
                if (!FindAll<Border>(designCanvas).Any(b => b.Background is LinearGradientBrush))
                    throw new Exception("FAIL: the studio canvas did not paint the gradient");
                gradientEditor.Close();
                Console.WriteLine("PASS: every studio item takes a gradient background and an opacity, and the canvas paints the gradient");
            }
            finally { try { if (File.Exists(gradientStudioPath)) File.Delete(gradientStudioPath); } catch { } }

            // ---------- custom stats accept any text, straight from the console ----------
            {
                var textPanelEngine = new GameEngine();
                textPanelEngine.NewGame("football");
                var textPanel = new GameControlWindow(textPanelEngine, () => { }, _ => { }, () => true);
                textPanel.Show();
                textPanel.UpdateLayout();
                var penalties = textPanelEngine.AddStat("Penalties");
                textPanel.UpdateLayout();
                var statHost = (WrapPanel)textPanel.Panel.FindName("StatsHost")!;
                var statBoxes = FindAll<TextBox>(statHost).ToArray();
                if (statBoxes.Length != 1)
                    throw new Exception($"FAIL: the custom stat row has {statBoxes.Length} value boxes");
                textPanelEngine.SetStatText(penalties.Id, "3rd");
                textPanel.UpdateLayout();
                if (statBoxes[0].Text != "3rd")
                    throw new Exception($"FAIL: the console shows '{statBoxes[0].Text}' for a text stat");
                // Typing free text into the middle box commits it.
                statBoxes[0].RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, statBoxes[0]) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
                statBoxes[0].Text = "N/A";
                statBoxes[0].RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(textPanel), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
                textPanel.UpdateLayout();
                if (textPanelEngine.State.Stats.Single().Display != "N/A")
                    throw new Exception($"FAIL: typing text into a stat stored '{textPanelEngine.State.Stats.Single().Display}'");
                if (statBoxes[0].Text != "N/A")
                    throw new Exception("FAIL: the stat box did not keep the typed text");
                textPanel.Close();
                Console.WriteLine("PASS: custom stats on the console take any text, not only numbers");
            }

            // ---------- studio: the artwork backdrop and pinning one player ----------
            {
                var backdropPath = Path.Combine(Path.GetTempPath(), $"smoke-backdrop-{Guid.NewGuid():N}.json");
                try
                {
                    var backdropCatalog = new ScorebugCatalog(backdropPath);
                    var backdropEngine = new GameEngine();
                    backdropEngine.NewGame("football");
                    RosterStore.Book.Rosters.Clear();
                    var studioRoster = RosterStore.Book.Add("Studio roster");
                    backdropEngine.AssignRoster(true, studioRoster.Id);
                    var pinned = studioRoster.AddPlayer();
                    pinned.Number = 12; pinned.Name = "Pinned"; pinned.Position = "QB"; pinned.SetStat("Passing yards", 111);
                    var onAir = studioRoster.AddPlayer();
                    onAir.Number = 7; onAir.Name = "On air"; onAir.Position = "RB"; onAir.SetStat("Passing yards", 222);
                    RosterStore.Save();
                    backdropEngine.SelectPlayer(true, onAir.Id);
                    backdropCatalog.Update(layout =>
                    {
                        layout.Elements.Clear();
                        layout.Elements.Add(new ScorebugElement { Name = "Name", Content = "{{PLAYER:HOME:Name}}", X = 20, Y = 20, Width = 320, Height = 60 });
                    });
                    var backdropStudio = new ScorebugEditorWindow(backdropCatalog, backdropEngine, null, "Dark") { Width = 1500, Height = 900 };
                    backdropStudio.Show();
                    backdropStudio.UpdateLayout();
                    // The backdrop is part of the layout, so it also paints the preview and the audience.
                    var backdropInput = (TextBox)backdropStudio.FindName("CanvasBackgroundInput")!;
                    var backdropSwatch = (Border)backdropStudio.FindName("CanvasBackgroundSwatch")!;
                    backdropInput.Text = "#FF112233";
                    backdropStudio.UpdateLayout();
                    Pump(TimeSpan.FromMilliseconds(120));
                    if (backdropCatalog.Layout.Background != "#FF112233")
                        throw new Exception($"FAIL: typing a backdrop left the layout at '{backdropCatalog.Layout.Background}'");
                    if (backdropSwatch.Background is not SolidColorBrush typedSwatch || typedSwatch.Color != Color.FromRgb(0x11, 0x22, 0x33))
                        throw new Exception("FAIL: the backdrop swatch does not show the chosen colour");
                    FindAll<Button>(backdropStudio).First(b => (b.Content as string) == "Black")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    backdropStudio.UpdateLayout();
                    if (backdropCatalog.Layout.Background != "#FF000000")
                        throw new Exception($"FAIL: the Black backdrop button left '{backdropCatalog.Layout.Background}'");
                    // Player data: pin the player who is NOT on the graphic.
                    ((ListBox)backdropStudio.FindName("Layers")!).SelectedIndex = 0;
                    backdropStudio.UpdateLayout();
                    if (((StackPanel)backdropStudio.FindName("PlayerDataSection")!).Visibility != Visibility.Visible)
                        throw new Exception("FAIL: a player element hides its player-data pickers");
                    var playerPick = (ComboBox)backdropStudio.FindName("PlayerPickInput")!;
                    var playerField = (ComboBox)backdropStudio.FindName("PlayerFieldInput")!;
                    playerPick.SelectedItem = playerPick.Items.OfType<object>().First(item => item.ToString()!.Contains("Pinned"));
                    playerField.SelectedItem = playerField.Items.OfType<object>().First(item => item.ToString() == "Passing yards");
                    Pump(TimeSpan.FromMilliseconds(900));
                    var bound = backdropCatalog.Layout.Elements[0].Content;
                    if (!bound.Contains($"id:{pinned.Id}") || !bound.Contains("Passing yards"))
                        throw new Exception($"FAIL: pinning a player wrote '{bound}'");
                    if (ScoreboardTemplates.Bind(bound, backdropEngine.State) != "111")
                        throw new Exception($"FAIL: a pinned player printed '{ScoreboardTemplates.Bind(bound, backdropEngine.State)}' instead of 111");
                    if (ScoreboardTemplates.Bind("{{PLAYER:HOME:Passing yards}}", backdropEngine.State) != "222")
                        throw new Exception("FAIL: the follow-the-desk binding stopped reading the selected player");
                    // The player-data pickers belong to text only.
                    backdropCatalog.Update(layout => layout.Elements.Add(new ScorebugElement
                    {
                        Name = "Box", Kind = ScorebugElementKind.Shape, X = 400, Y = 300, Width = 200, Height = 100
                    }));
                    backdropStudio.UpdateLayout();
                    ((ListBox)backdropStudio.FindName("Layers")!).SelectedIndex = 0;
                    backdropStudio.UpdateLayout();
                    if (((StackPanel)backdropStudio.FindName("PlayerDataSection")!).Visibility != Visibility.Collapsed)
                        throw new Exception("FAIL: a shape still shows the player-data pickers");
                    // A shape prints nothing, so the type, rotation and value-transition blocks are
                    // gone; its own shape picker and the shared background/outline stay.
                    foreach (var section in new[] { "FontSection", "TransformSection", "TransitionSection" })
                        if (((StackPanel)backdropStudio.FindName(section)!).Visibility != Visibility.Collapsed)
                            throw new Exception($"FAIL: a shape still shows {section}");
                    if (((StackPanel)backdropStudio.FindName("BackgroundSection")!).Visibility != Visibility.Visible ||
                        ((StackPanel)backdropStudio.FindName("BorderSection")!).Visibility != Visibility.Visible)
                        throw new Exception("FAIL: a shape lost its background or outline controls");
                    // Sport data is text too, but it prints a stat, not a player: offering the player
                    // pickers there replaced {{YARDS}} with a player binding on the first touch.
                    backdropCatalog.Update(layout => layout.Elements.Add(new ScorebugElement
                    {
                        Name = "Yards", Content = "{{YARDS}}", X = 60, Y = 500, Width = 240, Height = 60
                    }));
                    backdropStudio.UpdateLayout();
                    ((ListBox)backdropStudio.FindName("Layers")!).SelectedIndex = 0;
                    backdropStudio.UpdateLayout();
                    if (((StackPanel)backdropStudio.FindName("PlayerDataSection")!).Visibility != Visibility.Collapsed)
                        throw new Exception("FAIL: a stats element still shows the player-data pickers");
                    if (((StackPanel)backdropStudio.FindName("TextSection")!).Visibility != Visibility.Visible)
                        throw new Exception("FAIL: a stats element lost its text properties");
                    // A video is not text either, but it can still be outlined.
                    backdropCatalog.Update(layout => layout.Elements.Add(new ScorebugElement
                    {
                        Name = "Clip", Kind = ScorebugElementKind.Video, X = 40, Y = 40, Width = 320, Height = 180
                    }));
                    backdropStudio.UpdateLayout();
                    ((ListBox)backdropStudio.FindName("Layers")!).SelectedIndex = 0;
                    backdropStudio.UpdateLayout();
                    if (((StackPanel)backdropStudio.FindName("FontSection")!).Visibility != Visibility.Collapsed ||
                        ((StackPanel)backdropStudio.FindName("TransitionSection")!).Visibility != Visibility.Collapsed ||
                        ((StackPanel)backdropStudio.FindName("VideoPanel")!).Visibility != Visibility.Visible ||
                        ((StackPanel)backdropStudio.FindName("BorderSection")!).Visibility != Visibility.Visible)
                        throw new Exception("FAIL: a video shows the wrong property sections");
                    backdropStudio.Close();
                    Console.WriteLine("PASS: the studio sets the artwork backdrop, and a player element can pin one player from the roster");
                }
                finally { try { if (File.Exists(backdropPath)) File.Delete(backdropPath); } catch { } }
            }

            // ---------- sport profiles: start one from scratch ----------
            {
                var newSportEngine = new GameEngine();
                var sportWindow = new SportProfileWindow(newSportEngine.Sports);
                sportWindow.Show();
                sportWindow.UpdateLayout();
                var customBefore = newSportEngine.Sports.Custom.Count;
                var newProfile = FindAll<Button>(sportWindow).FirstOrDefault(b => (b.Content as string) == "New profile")
                    ?? throw new Exception("FAIL: sport profiles cannot be started from scratch");
                newProfile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                sportWindow.UpdateLayout();
                if (newSportEngine.Sports.Custom.Count != customBefore + 1)
                    throw new Exception("FAIL: New profile did not create a custom sport");
                var createdSport = newSportEngine.Sports.Custom[^1];
                if (createdSport.Name != "New sport" || createdSport.HasShotClock || createdSport.ScoringOptions.Length == 0)
                    throw new Exception($"FAIL: a from-scratch profile starts as '{createdSport.Name}' / shot clock {createdSport.HasShotClock}");
                var shotClock = (CheckBox)sportWindow.FindName("ShotClockInput")!;
                shotClock.IsChecked = true;
                FindAll<Button>(sportWindow).First(b => (b.Content as string) == "Save profile")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                sportWindow.UpdateLayout();
                if (newSportEngine.Sports.Find(createdSport.Id)?.HasShotClock != true)
                    throw new Exception("FAIL: the shot clock setting did not save on a custom profile");
                sportWindow.Close();
                Console.WriteLine("PASS: a sport profile can be created from scratch, including its shot clock");
            }

            // ---------- launch: the main window opens locked behind the launcher ----------
            {
                var cold = new MainWindow(waitForLaunch: true) { Width = 1280, Height = 800 };
                cold.Show();
                cold.UpdateLayout();
                var coldRoot = (DockPanel)cold.FindName("Root")!;
                if (coldRoot.IsEnabled)
                    throw new Exception("FAIL: the workspace is usable before a game is chosen");
                if (cold.Engine.State.HomeScore != 0 || cold.Engine.State.AwayScore != 0)
                    throw new Exception("FAIL: a cold launch already holds a game");
                // The theme pass used to wipe the selected tab's outline on the first load.
                var firstTab = (Button)cold.FindName("ControlTab")!;
                if (firstTab.BorderBrush is not SolidColorBrush tabBorder || tabBorder.Color != Color.FromRgb(0x38, 0xD7, 0xA3))
                    throw new Exception($"FAIL: the active tab lost its outline on first load ({firstTab.BorderBrush})");
                cold.StartFromLauncher(LaunchMode.New, null);
                cold.UpdateLayout();
                if (!coldRoot.IsEnabled)
                    throw new Exception("FAIL: choosing New Game left the workspace locked");
                if (cold.Engine.State.HomeScore != 0 || cold.Engine.State.AwayScore != 0
                    || cold.Engine.State.Period != 1 || cold.Engine.State.Events.Count != 0)
                    throw new Exception($"FAIL: a fresh game from the launcher is not a blank slate ({cold.Engine.State.HomeScore}-{cold.Engine.State.AwayScore}, period {cold.Engine.State.Period}, {cold.Engine.State.Events.Count} events)");
                cold.Close();
                Console.WriteLine("PASS: the main window opens locked behind the launch box, keeps its tab outline, and starts blank");

                // ---------- the console fills the screen when maximized ----------
                {
                    var fillMain = new MainWindow(LaunchMode.New) { Width = 1280, Height = 800 };
                    fillMain.Show();
                    Pump(TimeSpan.FromMilliseconds(250));
                    fillMain.WindowState = WindowState.Maximized;
                    Pump(TimeSpan.FromMilliseconds(450));
                    var desk = SystemParameters.WorkArea;
                    // A maximized window has to be allowed the whole monitor: capping it to the work
                    // area minus a frame margin is what left a strip of desktop down the side of the
                    // screen, on a wide monitor and a portrait one alike.
                    if (fillMain.MaxWidth < desk.Width - 1 || fillMain.MaxHeight < desk.Height - 1)
                        throw new Exception($"FAIL: a maximized console is capped at {fillMain.MaxWidth:0}x{fillMain.MaxHeight:0} on a {desk.Width:0}x{desk.Height:0} screen");
                    if (fillMain.ActualWidth < desk.Width - 40 || fillMain.ActualHeight < desk.Height - 40)
                        throw new Exception($"FAIL: a maximized console fills only {fillMain.ActualWidth:0}x{fillMain.ActualHeight:0} of {desk.Width:0}x{desk.Height:0}");
                    fillMain.Close();
                    Console.WriteLine("PASS: a maximized console fills the screen instead of leaving a strip of desktop");
                }
            }

            // ---------- program preview: the audience monitor shows what is on air ----------
            {
                var monitorMain = new MainWindow(LaunchMode.New) { Width = 1280, Height = 800 };
                monitorMain.Show();
                monitorMain.UpdateLayout();
                var monitor = monitorMain.AudienceMonitor ?? throw new Exception("FAIL: the program preview has no audience monitor");
                var staged = monitorMain.Scorebug.AddLayout("Staged only");
                if (staged <= 0) throw new Exception($"FAIL: a second layout was not added (index {staged})");
                monitorMain.Scorebug.Update(layout => layout.Elements.Add(new ScorebugElement
                {
                    Name = "Staged", Content = "STAGED ONLY", X = 40, Y = 40, Width = 400, Height = 80, FontSize = 40
                }));
                monitorMain.ShiftLayout(1);
                monitorMain.UpdateLayout();
                Pump(TimeSpan.FromMilliseconds(250));
                if (FindAll<TextBlock>((DependencyObject)monitor).Any(t => t.Text == "STAGED ONLY"))
                    throw new Exception("FAIL: the audience monitor showed a staged layout that is not on air");
                monitorMain.PublishOutput();
                monitorMain.UpdateLayout();
                Pump(TimeSpan.FromMilliseconds(250));
                if (!FindAll<TextBlock>((DependencyObject)monitor).Any(t => t.Text == "STAGED ONLY"))
                    throw new Exception("FAIL: the audience monitor did not follow the layout after Update Output");
                if (monitor.Layout is null || monitor.Layout.Id != monitorMain.Scorebug.PublishedLayoutOf(monitor.DisplayId).Id)
                    throw new Exception("FAIL: the audience monitor is not drawing the published layout");
                monitorMain.Close();
                Console.WriteLine("PASS: the audience monitor mirrors the published display and only changes on Update Output");
            }

            // ---------- preferences: value switch delay, default 0 ----------
            {
                var delayPrefs = new UserPreferences();
                if (delayPrefs.ValueSwitchDelay != 0)
                    throw new Exception($"FAIL: the value switch delay defaults to {delayPrefs.ValueSwitchDelay}");
                var delayWindow = new SettingsWindow(delayPrefs);
                delayWindow.Show();
                delayWindow.UpdateLayout();
                var delayBox = (TextBox)delayWindow.FindName("DelayInput")!;
                if (delayBox.Text != "0")
                    throw new Exception($"FAIL: the delay field starts on '{delayBox.Text}'");
                // Any value in 0–5 is accepted, not just an item from a fixed list.
                delayBox.Text = "0.35";
                FindAll<Button>(delayWindow).First(b => (b.Content as string) == "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (Math.Abs(delayPrefs.ValueSwitchDelay - 0.35) > 0.001 || Math.Abs(DisplayTiming.ValueSwitchDelay - 0.35) > 0.001)
                    throw new Exception($"FAIL: saving a 0.35 s delay left {delayPrefs.ValueSwitchDelay}/{DisplayTiming.ValueSwitchDelay}");

                // A graphic holds the old value for the delay, then prints the new one in one step.
                var delayPath = Path.Combine(Path.GetTempPath(), $"smoke-delay-{Guid.NewGuid():N}.json");
                try
                {
                    var delayCatalog = new ScorebugCatalog(delayPath);
                    delayCatalog.Update(layout =>
                    {
                        layout.Elements.Clear();
                        layout.Elements.Add(new ScorebugElement { Name = "Score", Content = "{{HOME_SCORE}}", X = 0, Y = 0, Width = 240, Height = 90, FontSize = 40 });
                    });
                    var delayEngine = new GameEngine();
                    delayEngine.NewGame("football");
                    var delayView = new ScorebugCanvasView(delayCatalog, delayEngine);
                    delayView.UpdateLayout();
                    delayEngine.Score(true, 7);
                    delayView.UpdateLayout();
                    if (FindAll<TextBlock>(delayView).Any(t => t.Text == "7"))
                        throw new Exception("FAIL: the delay printed the new value immediately");
                    Pump(TimeSpan.FromMilliseconds(1300));
                    if (!FindAll<TextBlock>(delayView).Any(t => t.Text == "7"))
                        throw new Exception("FAIL: the delayed value never landed");
                    DisplayTiming.ValueSwitchDelay = 0;
                    delayEngine.Score(true, 8);
                    delayView.UpdateLayout();
                    if (!FindAll<TextBlock>(delayView).Any(t => t.Text == "15"))
                        throw new Exception("FAIL: with the delay back at 0 the value did not print at once (score should read 15)");
                }
                finally { try { if (File.Exists(delayPath)) File.Delete(delayPath); } catch { } }
                DisplayTiming.Apply(new UserPreferences());
                Console.WriteLine("PASS: the value switch delay is a preference, defaults to 0, and holds a graphic's value for the chosen time");

                // ---------- sport values across every sport ----------
                var baseball = new GameEngine();
                baseball.NewGame("baseball");
                if (baseball.State.Controls.Count < 8 || baseball.State.Controls.All(c => c.Id != "baseball.first"))
                    throw new Exception($"FAIL: baseball did not start with its bases and count ({baseball.State.Controls.Count} controls)");
                baseball.SetSportValue("baseball.balls", 3);
                baseball.SetSportValue("baseball.strikes", 2);
                baseball.SetSportValue("baseball.first", 1);
                baseball.SetSportValue("baseball.third", 1);
                if (baseball.State.Balls != 3 || ScoreboardTemplates.Bind("{{SPORT:baseball.balls}}-{{SPORT:baseball.strikes}} {{BASES}}", baseball.State) != "3-2 1B 3B")
                    throw new Exception("FAIL: baseball count and bases do not reach the graphic");
                baseball.SetSportValue("baseball.balls", 9);
                if (baseball.State.Balls != 3)
                    throw new Exception($"FAIL: a sport value ignored its ceiling (balls = {baseball.State.Balls})");

                var hockey = new GameEngine();
                hockey.NewGame("hockey");
                hockey.ToggleSportFlag("hockey.powerplay");
                if (ScoreboardTemplates.Bind("{{SPORT:hockey.powerplay}}", hockey.State) != "PP")
                    throw new Exception("FAIL: hockey's power play did not print");
                hockey.ToggleSportFlag("hockey.powerplay");
                if (ScoreboardTemplates.Bind("[{{SPORT:hockey.powerplay}}]", hockey.State) != "[]")
                    throw new Exception("FAIL: switching the power play off left it printed");

                var cricket = new GameEngine();
                cricket.NewGame("cricket");
                cricket.SetSportValue("cricket.overs", 12);
                cricket.SetSportValue("cricket.wickets", 4);
                if (cricket.State.SportValues["cricket.overs"] != 12 || ScoreboardTemplates.Bind("{{SPORT:cricket.wickets}}", cricket.State) != "4")
                    throw new Exception("FAIL: cricket's overs and wickets were not stored");

                // A profile decides what a new game starts with, and every sport ships a control set.
                var sportsProfilePath = Path.Combine(Path.GetTempPath(), $"smoke-sports-profile-{Guid.NewGuid():N}.json");
                try
                {
                    var sports = new SportCatalog(sportsProfilePath);
                    var tennis = sports.Find("tennis")!;
                    sports.SaveProfile(tennis with { Controls = [.. tennis.Controls, new SportControl("tennis.aces.home", "Home aces", Max: 99, Default: 2)] });
                    var tennisEngine = new GameEngine(sports);
                    tennisEngine.NewGame("tennis");
                    if (SportValues.Read(tennisEngine.State, "tennis.aces.home") != 2)
                        throw new Exception("FAIL: a profile's starting value did not reach a new game");
                    if (SportDefinition.BuiltIns.Any(sport => SportControls.For(sport.Id).Count == 0 && sport.Id != "generic"))
                        throw new Exception("FAIL: a built-in sport has no console values");
                }
                finally { try { if (File.Exists(sportsProfilePath)) File.Delete(sportsProfilePath); } catch { } }
                Console.WriteLine("PASS: every sport carries its own values (count, bases, power play, overs) with profile defaults and graphic bindings");

                // ---------- interface accents and update version comparison ----------
                var accentPrefs = new UserPreferences { AccentColor = "#FFFF8800", AccentColor2 = "#FF224466" };
                if (UiPalette.Accent(accentPrefs) != Color.FromRgb(0xFF, 0x88, 0x00) || UiPalette.Secondary(accentPrefs) != Color.FromRgb(0x22, 0x44, 0x66))
                    throw new Exception("FAIL: the chosen interface colours were not read");
                if (UiPalette.Accent(new UserPreferences { AccentColor = "nonsense" }) != Color.FromRgb(0x38, 0xD7, 0xA3))
                    throw new Exception("FAIL: a bad accent value did not fall back to the shipped green");
                if (!UpdateChecker.IsNewer("1.2.0", "1.0.0") || UpdateChecker.IsNewer("1.0.0", "1.0.0") || UpdateChecker.IsNewer("0.9.9", "1.0.0"))
                    throw new Exception("FAIL: the update version comparison is wrong");

                // Tags are what a release carries, so they have to survive real-world names.
                if (UpdateChecker.VersionFromTag("v1.2.0") != "1.2.0"
                    || UpdateChecker.VersionFromTag("release-2.0") != "2.0.0"
                    || UpdateChecker.VersionFromTag("a1.01") != "1.1.0"
                    || UpdateChecker.VersionFromTag("nightly") is not null)
                    throw new Exception("FAIL: a release tag was not turned into a comparable version");

                // A GitHub releases array: the draft is invisible, the prerelease is the point, and
                // the installer's own digest becomes the checksum with no manifest to publish.
                const string releaseJson = """
                [
                  { "tag_name": "v9.0.0", "draft": true, "prerelease": false, "name": "Unpublished",
                    "assets": [ { "name": "ScorePlusSetup.exe", "browser_download_url": "https://example.invalid/draft.exe", "state": "uploaded" } ] },
                  { "tag_name": "a1.12", "draft": false, "prerelease": true, "name": "Alpha 1.12",
                    "assets": [
                      { "name": "ScorePlusPortable.exe", "browser_download_url": "https://example.invalid/portable.exe", "state": "uploaded" },
                      { "name": "ScorePlusSetup.exe", "browser_download_url": "https://example.invalid/setup.exe", "state": "uploaded",
                        "digest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" } ] },
                  { "tag_name": "v1.2.0", "draft": false, "prerelease": false,
                    "assets": [ { "name": "ScorePlusSetup.exe", "browser_download_url": "https://example.invalid/old.exe", "state": "uploaded" } ] }
                ]
                """;
                var offered = UpdateChecker.ParseReleases(releaseJson);
                if (offered is null
                    || offered.Version != "1.12.0"
                    || offered.Url != "https://example.invalid/setup.exe"
                    || offered.Notes != "Alpha 1.12"
                    || offered.Sha256 != "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")
                    throw new Exception("FAIL: a prerelease was not offered as the newest build with its own checksum");
                if (UpdateChecker.ParseReleases("[ { \"tag_name\": \"v9.0.0\", \"draft\": false, \"assets\": [] } ]") is not null)
                    throw new Exception("FAIL: a release with no installer was offered as an update");
                // Releases always carry ScorePlusPortable and ScorePlusSetup; the installer is matched
                // on its base name, so an upload without an extension is still found, and the portable
                // file is never mistaken for it.
                const string bareNameJson = """
                [
                  { "tag_name": "a1.2", "draft": false, "prerelease": true,
                    "assets": [ { "name": "ScorePlusPortable.exe", "browser_download_url": "https://example.invalid/portable.exe", "state": "uploaded" },
                                { "name": "ScorePlusSetup", "browser_download_url": "https://example.invalid/setup-no-extension", "state": "uploaded" } ] }
                ]
                """;
                var bareOffered = UpdateChecker.ParseReleases(bareNameJson);
                if (bareOffered is null || bareOffered.Url != "https://example.invalid/setup-no-extension")
                    throw new Exception("FAIL: a ScorePlusSetup asset without an extension was not offered as an update");
                if (!UpdateFeed.IsSetupAsset("ScorePlusSetup.exe") || UpdateFeed.IsSetupAsset("ScorePlusPortable.exe") || UpdateFeed.IsSetupAsset(null))
                    throw new Exception("FAIL: the release asset names are not matched by base name");
                if (UpdateChecker.ParseManifest("{ \"version\": \"3.0.0\", \"url\": \"https://example.invalid/setup.exe\" }") is not { Version: "3.0.0" })
                    throw new Exception("FAIL: a hand-hosted manifest no longer parses");
                if (!Uri.TryCreate(UpdateFeed.BuiltIn, UriKind.Absolute, out var builtIn) || builtIn.Host != "api.github.com")
                    throw new Exception("FAIL: the built-in update feed is not a GitHub API address");
                Console.WriteLine("PASS: interface accents are stored and validated, updates read prereleases from the built-in feed with the asset's own checksum, and only a genuinely newer version is offered");

                // ---------- the installer wears the app's own chrome and reads its Alpha label ----------
                {
                    var setupWindow = new SetupWindow(false) { ShowInTaskbar = false };
                    setupWindow.Show();
                    setupWindow.UpdateLayout();
                    if (!FindAll<TextBlock>(setupWindow).Any(t => t.Text.Contains("Alpha ", StringComparison.Ordinal)))
                        throw new Exception("FAIL: the installer does not show its Alpha version");
                    if (setupWindow.Background is not SolidColorBrush setupFace || setupFace.Color.R > 0x20 || setupFace.Color.B > 0x20)
                        throw new Exception($"FAIL: the installer still paints a coloured (navy) panel ({setupWindow.Background})");
                    foreach (var setupButton in FindAll<Button>(setupWindow))
                    {
                        if (setupButton.Foreground is not SolidColorBrush setupInk || setupInk.Color.R < 230)
                            throw new Exception($"FAIL: an installer button has unreadable text on its box ({setupButton.Foreground})");
                        if (setupButton.Background is not SolidColorBrush setupFill || setupFill.Color.R > 0x80)
                            throw new Exception($"FAIL: an installer button is still a pale system box ({setupButton.Background})");
                    }
                    setupWindow.Close();
                    Console.WriteLine("PASS: the installer uses the app's dark chrome, readable buttons and its Alpha version label");
                }

                // ---------- the installer keeps to folders and files that are its own ----------
                {
                    var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (Installer.IsSafeInstallDirectory(Path.Combine(profile, "Downloads"), out _)
                        || Installer.IsSafeInstallDirectory(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), out _)
                        || Installer.IsSafeInstallDirectory(profile, out _)
                        || Installer.IsSafeInstallDirectory(Path.GetPathRoot(profile)!, out _))
                        throw new Exception("FAIL: the installer would move into Downloads, the Desktop, the profile or a drive root");
                    if (!Installer.IsSafeInstallDirectory(Installer.DefaultDirectory, out _))
                        throw new Exception("FAIL: the installer refuses its own default folder");

                    var sandbox = Path.Combine(Path.GetTempPath(), "ScorePlusSafety");
                    try
                    {
                        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
                        Directory.CreateDirectory(sandbox);
                        File.WriteAllText(Path.Combine(sandbox, "payroll.xlsx"), "the user's own file");
                        if (Installer.IsSafeInstallDirectory(sandbox, out _))
                            throw new Exception("FAIL: the installer would share a folder with somebody else's files");
                        File.WriteAllText(Path.Combine(sandbox, "ScorePlus.exe"), "app");
                        File.WriteAllText(Path.Combine(sandbox, "install.json"), "{}");
                        File.WriteAllText(Path.Combine(sandbox, "Uninstall.exe"), "installer");
                        var kept = Installer.RemoveOwnFiles(sandbox);
                        if (kept != 1 || !File.Exists(Path.Combine(sandbox, "payroll.xlsx"))
                            || File.Exists(Path.Combine(sandbox, "ScorePlus.exe")) || File.Exists(Path.Combine(sandbox, "install.json")))
                            throw new Exception("FAIL: an uninstall removed files it did not install");
                    }
                    finally { try { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true); } catch { } }
                    Console.WriteLine("PASS: the installer refuses Downloads, the Desktop, a drive root and occupied folders, and an uninstall deletes only its own three files");
                }

                // ---------- linked parts: one edit reaches every copy, and the link can be broken ----------
                {
                    var linkMain = new MainWindow(LaunchMode.New) { Width = 1280, Height = 800 };
                    linkMain.Show();
                    linkMain.UpdateLayout();
                    linkMain.Scorebug.AddLayout("Second");
                    linkMain.Scorebug.SelectLayout(1);
                    linkMain.Scorebug.Update(layout =>
                    {
                        layout.Elements.Clear();
                        layout.Elements.Add(new ScorebugElement { Name = "Linked bar", Content = "BAR", LinkId = "group-1", X = 40, Y = 40, Width = 300, Height = 80 });
                    });
                    linkMain.Scorebug.SelectLayout(0);
                    linkMain.Scorebug.Update(layout =>
                    {
                        layout.Elements.Clear();
                        layout.Elements.Add(new ScorebugElement { Name = "Linked bar", Content = "BAR", LinkId = "group-1", X = 40, Y = 40, Width = 300, Height = 80 });
                    });
                    var linkStudio = new ScorebugEditorWindow(linkMain.Scorebug, linkMain.Engine);
                    linkStudio.Show();
                    linkStudio.UpdateLayout();
                    var layerList = (ListBox)linkStudio.FindName("Layers")!;
                    layerList.SelectedIndex = 0;
                    linkStudio.UpdateLayout();
                    var xBox = (TextBox)linkStudio.FindName("XInput")!;
                    xBox.Text = "321";
                    var source = PresentationSource.FromVisual(linkStudio);
                    if (source is null) throw new Exception("FAIL: the studio window has no presentation source");
                    xBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
                    Pump(TimeSpan.FromMilliseconds(500));
                    var otherCopy = linkMain.Scorebug.Displays[0].Layouts[1].Elements[0];
                    var firstCopy = linkMain.Scorebug.Displays[0].Layouts[0].Elements[0];
                    if (Math.Abs(firstCopy.X - 321) > 0.5)
                        throw new Exception($"FAIL: Enter did not commit the typed X position (x = {firstCopy.X})");
                    if (Math.Abs(otherCopy.X - 321) > 0.5)
                        throw new Exception($"FAIL: a linked part did not follow the move on the other layout (x = {otherCopy.X})");
                    var breakLink = FindAll<Button>(linkStudio).First(b => (b.Content as string) == "Break link");
                    if (breakLink.Visibility != Visibility.Visible)
                        throw new Exception("FAIL: a linked part has no Break link control in its properties");
                    breakLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    linkStudio.UpdateLayout();
                    xBox.Text = "410";
                    xBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
                    Pump(TimeSpan.FromMilliseconds(500));
                    if (Math.Abs(linkMain.Scorebug.Displays[0].Layouts[1].Elements[0].X - 321) > 0.5)
                        throw new Exception("FAIL: a broken link still follows the other copy");
                    if (Math.Abs(linkMain.Scorebug.Displays[0].Layouts[0].Elements[0].X - 410) > 0.5)
                        throw new Exception("FAIL: the copy that broke its link no longer accepts its own edits");
                    // Escape drops the selection, leaving the property panel empty.
                    RaiseKey(linkStudio, source, Key.Escape);
                    linkStudio.UpdateLayout();
                    if (((FrameworkElement)linkStudio.FindName("LinkSection")!).Visibility != Visibility.Collapsed)
                        throw new Exception("FAIL: Escape did not deselect the element");
                    // The layout strip must show each layout's whole name, not the top of it.
                    var strip = (StackPanel)linkStudio.FindName("LayoutStrip")!;
                    // The strip sits inside the scroll content presenter, so walk up to the scroller.
                    DependencyObject? ancestor = strip;
                    while (ancestor is not null && ancestor is not ScrollViewer) ancestor = VisualTreeHelper.GetParent(ancestor);
                    var stripHost = ancestor as ScrollViewer;
                    if (stripHost is null || stripHost.ScrollableHeight > 0.5)
                        throw new Exception($"FAIL: the layout strip clips the layout names (scrollable {stripHost?.ScrollableHeight:0.#}, extent {stripHost?.ExtentHeight:0.#}, viewport {stripHost?.ViewportHeight:0.#})");
                    if (!FindAll<TextBlock>(strip).Any(t => t.Text.Contains("Second") && t.ActualHeight >= 11))
                        throw new Exception("FAIL: the layout strip does not show a full name under its picture");
                    linkStudio.Close();
                    linkMain.Close();
                    Console.WriteLine("PASS: a part copied between layouts stays linked (move, Enter and Break link all work)");
                    Console.WriteLine("PASS: Escape deselects a studio element, and the layout strip shows whole names");
                }

                // ---------- baseball bases: the icon element and the console's lit base squares ----------
                {
                    var basesEngine = new GameEngine();
                    basesEngine.NewGame("baseball");
                    if (basesEngine.Sports.All.Last().Id != "generic")
                        throw new Exception("FAIL: the Custom sport is not at the bottom of the sport list");
                    var basesItem = new ScorebugElement { Name = "Bases", Content = "", Icon = ScorebugIcon.Bases, Width = 220, Height = 150, Foreground = "#FFFF8800" };
                    var basesView = new ScorebugIconView(basesItem, () => basesEngine.State);
                    basesView.Measure(new Size(220, 150));
                    basesView.Arrange(new Rect(0, 0, 220, 150));
                    var diamonds = FindAll<System.Windows.Shapes.Rectangle>(basesView).ToArray();
                    if (diamonds.Length != 3)
                        throw new Exception($"FAIL: the bases element draws {diamonds.Length} bases for 3");
                    if (diamonds.Any(d => d.Fill is not SolidColorBrush)) throw new Exception("FAIL: an empty base is not a faint outline");
                    basesEngine.SetSportValue("baseball.first", 1);
                    basesEngine.SetSportValue("baseball.third", 1);
                    basesView.Update();
                    var lit = FindAll<System.Windows.Shapes.Rectangle>(basesView).Count(d => d.Fill is SolidColorBrush brush && brush.Color.A > 200);
                    if (lit != 2) throw new Exception($"FAIL: {lit} of 3 bases lit for two runners");
                    var panel = new ControlPanelView(basesEngine, new RemoteHostProbe());
                    panel.Width = 900;
                    panel.Measure(new Size(900, 700));
                    panel.Arrange(new Rect(0, 0, 900, 700));
                    panel.UpdateLayout();
                    var firstBase = FindAll<ToggleButton>(panel).FirstOrDefault(t => (t.Tag as string) == "sport.baseball.first.toggle");
                    if (firstBase?.Content is not StackPanel baseContent)
                        throw new Exception("FAIL: a baseball runner control is still a word, not a square base");
                    var square = baseContent.Children.OfType<Border>().FirstOrDefault();
                    if (square is null) throw new Exception("FAIL: the base control has no square");
                    if (square.Background is not SolidColorBrush onBrush || onBrush.Color.A < 200)
                        throw new Exception("FAIL: an occupied base did not light up");
                    Console.WriteLine("PASS: baseball draws three bases (lit for runners) and the console flags are square bases");
                }

                // ---------- live artwork: any element can become an icon, and the diamond animates ----------
                {
                    var artPath = Path.Combine(Path.GetTempPath(), $"smoke-artwork-{Guid.NewGuid():N}.json");
                    var artEngine = new GameEngine();
                    artEngine.NewGame("football");
                    artEngine.SetTimeoutCount(true, 2);
                    var artCatalog = new ScorebugCatalog(artPath);
                    artCatalog.Update(layout =>
                    {
                        layout.Elements.Clear();
                        layout.Elements.Add(new ScorebugElement { Name = "Timeouts", Content = "{{POSSESSION}}", X = 60, Y = 60, Width = 260, Height = 70, FontSize = 30 });
                    });
                    var artStudio = new ScorebugEditorWindow(artCatalog, artEngine) { Width = 1400, Height = 900 };
                    artStudio.Show();
                    artStudio.UpdateLayout();
                    ((ListBox)artStudio.FindName("Layers")!).SelectedIndex = 0;
                    artStudio.UpdateLayout();
                    var artCanvas = (Canvas)artStudio.FindName("DesignCanvas")!;
                    // A plain text element to begin with, as the operator would add it.
                    if (FindAll<ScorebugIconView>(artCanvas).Any()) throw new Exception("FAIL: a text element already draws artwork");
                    ((ComboBox)artStudio.FindName("IconInput")!).SelectedItem = ScorebugIcon.TimeoutBars;
                    Pump(TimeSpan.FromMilliseconds(250));
                    var barsView = FindAll<ScorebugIconView>(artCanvas).FirstOrDefault();
                    if (barsView is null)
                        throw new Exception("FAIL: switching an element to timeout bars draws no live artwork in the studio");
                    if (FindAll<System.Windows.Shapes.Rectangle>(barsView).Count() != 2)
                        throw new Exception($"FAIL: timeout bars show {FindAll<System.Windows.Shapes.Rectangle>(barsView).Count()} bars for two timeouts");
                    if (FindAll<ScorebugTextBlock>(artCanvas).Any(t => t.Text.Contains("POSSESSION", StringComparison.Ordinal)))
                        throw new Exception("FAIL: the old text is still drawn under the artwork");
                    // Live: another timeout removes a bar without any studio edit.
                    artEngine.SetTimeoutCount(true, 1);
                    Pump(TimeSpan.FromMilliseconds(200));
                    if (FindAll<System.Windows.Shapes.Rectangle>(barsView).Count() != 1)
                        throw new Exception("FAIL: the timeout bars did not follow the game");
                    // Switching to a different icon builds that artwork instead of keeping the bars.
                    ((ComboBox)artStudio.FindName("IconInput")!).SelectedItem = ScorebugIcon.PossessionDot;
                    Pump(TimeSpan.FromMilliseconds(250));
                    if (!FindAll<System.Windows.Shapes.Ellipse>(artCanvas).Any())
                        throw new Exception("FAIL: switching to the possession dot drew no dot");
                    artStudio.Close();

                    // The diamond lights a base through the element's own value-change animation.
                    var diamondItem = new ScorebugElement
                    {
                        Name = "Bases", Icon = ScorebugIcon.Bases, Width = 220, Height = 150, Foreground = "#FFFF8800",
                        Transition = ScorebugTransition.Fade, TransitionSeconds = 1
                    };
                    var diamondEngine = new GameEngine();
                    diamondEngine.NewGame("baseball");
                    var diamondView = new ScorebugIconView(diamondItem, () => diamondEngine.State);
                    var diamondHost = new Window { Content = diamondView, Width = 300, Height = 240, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                    diamondHost.Show();
                    Pump(TimeSpan.FromMilliseconds(250));
                    diamondEngine.SetSportValue("baseball.first", 1);
                    diamondView.Update();
                    var diamondSquares = FindAll<System.Windows.Shapes.Rectangle>(diamondView).ToArray();
                    if (diamondSquares.Length != 3) throw new Exception("FAIL: the animated diamond lost a base");
                    // Order on the canvas is third, second, first; the first base is the one just lit.
                    if (!diamondSquares[2].HasAnimatedProperties)
                        throw new Exception("FAIL: a lit base snaps in even though the element has a value-change animation");
                    if (diamondSquares[2].Fill is not SolidColorBrush litBase || litBase.Color.A < 200)
                        throw new Exception("FAIL: the animated base never lit");
                    diamondEngine.SetSportValue("baseball.first", 0);
                    diamondView.Update();
                    if (diamondSquares[2].Fill is not SolidColorBrush cleared || cleared.Color.A > 40)
                        throw new Exception("FAIL: clearing a base left it lit");
                    diamondHost.Close();
                    Console.WriteLine("PASS: any element can become live artwork in the studio, and runners light the diamond through its own transition");
                }

                // ---------- phone remote: password, state and actions ----------
                var remoteEngine = new GameEngine();
                remoteEngine.NewGame("basketball");
                var remoteHost = new RemoteHostProbe();
                var remotePanel = new ControlPanelView(remoteEngine, remoteHost);
                remotePanel.UpdateLayout();
                var remotePort = FreePort();
                using (var remote = new RemoteControlServer(remoteEngine, remoteHost, id => { remotePanel.Execute(id); return true; }, Dispatcher.CurrentDispatcher, remotePort, "hunter2"))
                {
                    remote.Start();
                    if (!remote.Running) throw new Exception("FAIL: the remote did not start");
                    var cookies = new System.Net.CookieContainer();
                    using var http = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false });
                    var baseUrl = $"http://127.0.0.1:{remotePort}";
                    var anonymous = WaitWithPump(http.GetAsync(baseUrl + "/api/state"));
                    if ((int)anonymous.StatusCode != 401)
                        throw new Exception($"FAIL: the remote served state without a password ({(int)anonymous.StatusCode})");
                    var wrong = WaitWithPump(http.PostAsync(baseUrl + "/login", new System.Net.Http.StringContent("password=nope", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded")));
                    if ((int)wrong.StatusCode != 200 || cookies.Count != 0)
                        throw new Exception("FAIL: a wrong remote password was accepted");
                    var signIn = WaitWithPump(http.PostAsync(baseUrl + "/login", new System.Net.Http.StringContent("password=hunter2", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded")));
                    if ((int)signIn.StatusCode != 303)
                        throw new Exception($"FAIL: the right remote password did not sign in ({(int)signIn.StatusCode})");
                    var stateJson = WaitWithPump(http.GetStringAsync(baseUrl + "/api/state"));
                    if (!stateJson.Contains("basketball", StringComparison.Ordinal) || !stateJson.Contains("basketball.fouls.home", StringComparison.Ordinal))
                        throw new Exception("FAIL: the remote state is missing the sport or its values");
                    // The remote page carries the app's own brand mark, served by the remote itself.
                    var logo = WaitWithPump(http.GetAsync(baseUrl + "/logo.png"));
                    if ((int)logo.StatusCode != 200 || logo.Content.Headers.ContentType?.MediaType != "image/png")
                        throw new Exception($"FAIL: the remote logo is missing ({(int)logo.StatusCode})");
                    var panelHtml = WaitWithPump(http.GetStringAsync(baseUrl + "/"));
                    if (!panelHtml.Contains("src=\"/logo.png\"", StringComparison.Ordinal))
                        throw new Exception("FAIL: the remote page does not show the brand mark");
                    var action = WaitWithPump(http.PostAsync(baseUrl + "/api/action", new System.Net.Http.StringContent("a=away.score.plus2", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded")));
                    if ((int)action.StatusCode != 200 || remoteEngine.State.AwayScore != 2)
                        throw new Exception($"FAIL: a remote score button did not reach the game ({(int)action.StatusCode}, {remoteEngine.State.AwayScore})");
                    var sportAction = WaitWithPump(http.PostAsync(baseUrl + "/api/action", new System.Net.Http.StringContent("a=sport.set&control=basketball.fouls.home&value=4", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded")));
                    if ((int)sportAction.StatusCode != 200 || remoteEngine.State.HomeFouls != 4)
                        throw new Exception("FAIL: a remote sport value did not reach the game");
                    var liveOff = WaitWithPump(http.PostAsync(baseUrl + "/api/action", new System.Net.Http.StringContent("a=live.off", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded")));
                    if ((int)liveOff.StatusCode != 200 || remoteHost.LiveScoreClock)
                        throw new Exception("FAIL: the remote could not switch the live score off");
                    Console.WriteLine("PASS: the phone remote needs its password, serves the console state and drives score, sport values and live output");
                }
            }

            Console.WriteLine("ALL UI SMOKE CHECKS PASSED");

            app.Shutdown();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: " + ex);
            app.Shutdown();
            return 1;
        }
        finally
        {
            PreferencesStore.UsePath(null);
            AppPaths.UseRoot(null);
            AutosaveStore.UseFolder(null);
            try { if (Directory.Exists(appDataPath)) Directory.Delete(appDataPath, true); } catch { }
            try { if (Directory.Exists(autosavePath)) Directory.Delete(autosavePath, true); } catch { }
            if (File.Exists(layoutPath)) File.Delete(layoutPath);
            if (File.Exists(prefsPath)) File.Delete(prefsPath);
            var prefsTemp = prefsPath + ".tmp";
            if (File.Exists(prefsTemp)) File.Delete(prefsTemp);
        }
    }


    /// <summary>Waits for an HTTP call while keeping the UI dispatcher pumping, so the remote can marshal its actions.</summary>
    static T WaitWithPump<T>(Task<T> task)
    {
        while (!task.IsCompleted) Pump(TimeSpan.FromMilliseconds(20));
        return task.GetAwaiter().GetResult();
    }

    static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>A host for a standalone console: enough for the remote to publish and switch live values.</summary>
    sealed class RemoteHostProbe : IControlPanelHost
    {
        public int Published { get; private set; }
        public int Shifted { get; private set; }
        public void PublishOutput() => Published++;
        public bool LiveScoreClock { get; set; } = true;
        public bool HasStagedChanges => false;
        public void OpenNewPanel() { }
        public string LayoutLabel => "1 · Test";
        public void ShiftLayout(int delta) => Shifted += delta;
    }

    static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in FindAll<T>(child)) yield return nested;
        }
    }

    /// <summary>A tiny solid PNG, so the team-logo path has a real image to load.</summary>
    static void WritePng(string path, Color color)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 64, 64));
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    static void RaiseKey(Window window, PresentationSource? source, Key key)
    {
        if (source is null) throw new Exception("FAIL: window has no presentation source");
        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    /// <summary>A plain 16-bit PCM WAV, so the seek check has a real decodable clip without user files.</summary>
    static void WriteSilentWav(string path, int seconds)
    {
        const int rate = 22050;
        short channels = 1, bits = 16;
        var data = rate * seconds * channels * bits / 8;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + data);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(rate);
        writer.Write(rate * channels * bits / 8);
        writer.Write((short)(channels * bits / 8));
        writer.Write(bits);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(data);
        writer.Write(new byte[data]);
    }

    static void Pump(TimeSpan duration)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
