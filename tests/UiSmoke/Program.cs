using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
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
        // The smoke hook suppresses the launch box: the suite drives its own windows, and the
        // first dispatcher pump would otherwise block inside the real App's startup dialog.
#if DEBUG
        App.SuppressStartupForSmoke = true;
#endif
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
            // The rebuilt motion makers are code-built windows, so opening them is itself a smoke
            // test: the layout timeline must draw, and a temporary piece must join the clip.
            var motionCatalogPath = Path.Combine(Path.GetTempPath(), $"smoke-motion-{Guid.NewGuid():N}.json");
            var motionCatalog = new ScorebugCatalog(motionCatalogPath);
            var motionEngine = new GameEngine();
            motionEngine.NewGame("basketball");
            motionEngine.Score(true, 3);
            var transitionMaker = new MotionMakerWindow(MotionKind.Transition, motionCatalog, motionEngine, "Dark");
            transitionMaker.Show();
            transitionMaker.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(180));
            var layoutTimeline = FindAll<Canvas>(transitionMaker)
                .FirstOrDefault(canvas => Math.Abs(canvas.Width - 1040) < 0.5);
            if (layoutTimeline is null || layoutTimeline.Children.Count < 4)
                throw new Exception("FAIL: the transition builder did not draw its layout-wide timeline");
            var addTemporaryText = FindAll<Button>(transitionMaker).FirstOrDefault(button => (button.Content as string) == "+ Text")
                ?? throw new Exception("FAIL: the timeline has no add-text action");
            addTemporaryText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            transitionMaker.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(80));
            var clipField = typeof(MotionMakerWindow).GetField("_clip", BindingFlags.Instance | BindingFlags.NonPublic);
            if (clipField?.GetValue(transitionMaker) is not MotionClip transitionClip || transitionClip.Pieces.Count != 1)
                throw new Exception("FAIL: adding a temporary item did not add it to the transition clip");
            // The reorder commit is the same one a row drop uses: a temporary piece can move to
            // the front or the end, while the whole-graphic row stays pinned first.
            var drop = typeof(MotionMakerWindow).GetMethod("CommitTimelineDrop", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new Exception("FAIL: the timeline has no drop commit");
            var pieceId = transitionClip.Pieces[0].Id;
            if ((bool)drop.Invoke(transitionMaker, ["", 1])!)
                throw new Exception("FAIL: the whole-graphic row was allowed to move");
            if (!(bool)drop.Invoke(transitionMaker, [pieceId, 1])!
                || !string.Equals(transitionClip.PieceOrder.FirstOrDefault(), pieceId, StringComparison.OrdinalIgnoreCase))
                throw new Exception("FAIL: dropping a temporary piece at the top did not reorder the timeline");
            if (!(bool)drop.Invoke(transitionMaker, [pieceId, 99])!
                || !string.Equals(transitionClip.PieceOrder.LastOrDefault(), pieceId, StringComparison.OrdinalIgnoreCase))
                throw new Exception("FAIL: dropping a temporary piece at the bottom did not reorder the timeline");

            // A saved clip remembers its layout. Loading it while a different layout is active
            // must reopen the authored layout, not the one that happened to be on screen.
            transitionClip.Name = "Smoke timeline clip";
            var authoredLayoutId = motionCatalog.Layout.Id;
            transitionClip.TimelineLayoutId = authoredLayoutId;
            motionCatalog.SaveTransition(transitionClip);
            transitionMaker.Close();
            motionCatalog.AddLayout("Smoke second layout");
            if (string.Equals(motionCatalog.Layout.Id, authoredLayoutId, StringComparison.OrdinalIgnoreCase))
                throw new Exception("FAIL: the smoke catalog did not switch to its second layout");
            var reloadMaker = new MotionMakerWindow(MotionKind.Transition, motionCatalog, motionEngine, "Dark");
            reloadMaker.Show();
            reloadMaker.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(180));
            var libraryList = (ListBox?)typeof(MotionMakerWindow)
                .GetField("_libraryList", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(reloadMaker)
                ?? throw new Exception("FAIL: the transition maker has no saved-clip list");
            var savedRow = libraryList.Items.OfType<object>()
                .FirstOrDefault(row => (row.ToString() ?? "").Contains("Smoke timeline clip", StringComparison.Ordinal));
            if (savedRow is null) throw new Exception("FAIL: the saved transition did not reach the library");
            libraryList.SelectedItem = savedRow;
            typeof(MotionMakerWindow).GetMethod("LoadFromLibrary", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(reloadMaker, null);
            reloadMaker.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(80));
            var smokeTimelinePicker = (ComboBox?)typeof(MotionMakerWindow)
                .GetField("_timelineLayoutPicker", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(reloadMaker);
            var pickedChoice = smokeTimelinePicker?.SelectedItem;
            var pickedLayout = pickedChoice?.GetType().GetProperty("Layout")?.GetValue(pickedChoice) as ScorebugLayout;
            if (!string.Equals(pickedLayout?.Id, authoredLayoutId, StringComparison.OrdinalIgnoreCase))
                throw new Exception($"FAIL: loading a clip reopened “{pickedLayout?.Name ?? "none"}” instead of its authored layout");
            if (!string.Equals((clipField.GetValue(reloadMaker) as MotionClip)?.TimelineLayoutId, authoredLayoutId, StringComparison.OrdinalIgnoreCase))
                throw new Exception("FAIL: loading a clip did not restore its timeline layout id");
            reloadMaker.Close();
            Console.WriteLine("PASS: dragging a timeline row reorders the clip, and loading it reopens the authored layout");

            // A delayed row still owns keys at its own times. The row used to clamp every key to
            // the phase boundary, which made late keys look pinned to the switch break, impossible
            // to drag past it and easy to overwrite with the wrong value.
            {
                var keyCatalogPath = Path.Combine(Path.GetTempPath(), $"smoke-keys-{Guid.NewGuid():N}.json");
                var keyCatalog = new ScorebugCatalog(keyCatalogPath);
                keyCatalog.Update(layout =>
                {
                    layout.Elements.Clear();
                    for (var index = 0; index < 4; index++)
                        layout.Elements.Add(new ScorebugElement
                        {
                            Name = $"Key item {index + 1}", Content = $"ITEM {index + 1}",
                            X = 40 + index * 24, Y = 60, Width = 210, Height = 64, FontSize = 24
                        });
                });
                var keyClip = MotionClip.NewTransition("Smoke key clip");
                keyClip.TimelineLayoutId = keyCatalog.Layout.Id;
                keyClip.ItemStagger = 0.08;
                keyClip.PieceOrder = keyCatalog.Layout.Elements.Select(element => element.Id).ToList();
                var keyMaker = new MotionMakerWindow(MotionKind.Transition, keyCatalog, motionEngine, "Dark", keyClip);
                keyMaker.Show();
                keyMaker.UpdateLayout();
                Pump(TimeSpan.FromMilliseconds(180));
                var liveClip = (MotionClip?)clipField?.GetValue(keyMaker)
                    ?? throw new Exception("FAIL: the keyframe probe has no clip");
                var lateId = keyCatalog.Layout.Elements[^1].Id;
                var exitPhase = liveClip.Phases[0];
                exitPhase.Tracks.RemoveAll(track => string.Equals(track.PieceId, lateId, StringComparison.OrdinalIgnoreCase));
                var lateTrack = new MotionTrack(MotionTarget.Opacity, (0, 1), (exitPhase.Seconds, 0)) { PieceId = lateId };
                exitPhase.Tracks.Add(lateTrack);
                var lateColor = new ColorTrack(MotionColorRole.Text, (0, "#FFFFFFFF"), (exitPhase.Seconds, "#FF000000")) { PieceId = lateId };
                exitPhase.Colors.Add(lateColor);
                var refreshAll = typeof(MotionMakerWindow).GetMethod("RefreshAll", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new Exception("FAIL: the maker cannot refresh its timeline");
                refreshAll.Invoke(keyMaker, null);
                var probeTimelineList = (ListBox?)typeof(MotionMakerWindow)
                    .GetField("_timelineList", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(keyMaker)
                    ?? throw new Exception("FAIL: the keyframe probe has no timeline list");
                probeTimelineList.SelectedIndex = probeTimelineList.Items.Count - 1;
                var probePhaseList = (ListBox?)typeof(MotionMakerWindow)
                    .GetField("_phaseList", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(keyMaker)
                    ?? throw new Exception("FAIL: the keyframe probe has no phase list");
                probePhaseList.SelectedIndex = 0;
                typeof(MotionMakerWindow).GetMethod("LoadPhase", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(keyMaker, null);
                var probeTrackList = (ListBox?)typeof(MotionMakerWindow)
                    .GetField("_trackList", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(keyMaker)
                    ?? throw new Exception("FAIL: the keyframe probe has no track list");
                probeTrackList.SelectedIndex = 0;
                var probeKeyList = (ListBox?)typeof(MotionMakerWindow)
                    .GetField("_keyList", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(keyMaker)
                    ?? throw new Exception("FAIL: the keyframe probe has no key list");
                probeKeyList.SelectedIndex = 0;
                keyMaker.UpdateLayout();
                var probeKeyTime = (TextBox?)typeof(MotionMakerWindow)
                    .GetField("_keyTime", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(keyMaker)
                    ?? throw new Exception("FAIL: the keyframe probe has no time box");
                var probeKeyValue = (TextBox?)typeof(MotionMakerWindow)
                    .GetField("_keyValue", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(keyMaker)
                    ?? throw new Exception("FAIL: the keyframe probe has no value box");
                var updateKey = typeof(MotionMakerWindow).GetMethod("UpdateKey", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new Exception("FAIL: the keyframe probe cannot update a key");
                probeKeyTime.Text = "0.1";
                probeKeyValue.Text = "0.42";
                updateKey.Invoke(keyMaker, null);
                var editedKey = lateTrack.Keys.FirstOrDefault(key => Math.Abs(key.Time - 0.1) < 0.001)
                    ?? throw new Exception("FAIL: updating a key did not retime it");
                if (Math.Abs(editedKey.Value - 0.42) > 0.001)
                    throw new Exception($"FAIL: updating a key did not apply its value ({editedKey.Value})");
                if (probeKeyList.SelectedIndex < 0)
                    throw new Exception("FAIL: updating a key threw the selection away");
                // Enter must commit the value box, not just drop focus.
                probeKeyTime.Text = "0.12";
                probeKeyValue.Text = "0.77";
                var keySource = PresentationSource.FromVisual(keyMaker)
                    ?? throw new Exception("FAIL: the keyframe probe has no presentation source");
                probeKeyValue.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, keySource, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
                Pump(TimeSpan.FromMilliseconds(60));
                editedKey = lateTrack.Keys.FirstOrDefault(key => Math.Abs(key.Time - 0.12) < 0.005)
                    ?? throw new Exception("FAIL: Enter did not commit the key's time");
                if (Math.Abs(editedKey.Value - 0.77) > 0.001)
                    throw new Exception($"FAIL: Enter did not commit the key's value ({editedKey.Value})");
                // The value must reach the picture, not just the model: the selected item's host is
                // left at this key's value because PreviewAt put the preview on the edited frame.
                var keyPreview = typeof(MotionMakerWindow)
                    .GetField("_transitionPreview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(keyMaker);
                var keyHosts = keyPreview?.GetType().GetProperty("Hosts")?.GetValue(keyPreview) as System.Collections.IDictionary;
                if (keyHosts is null || keyHosts[lateId] is not FrameworkElement keyedHost)
                    throw new Exception("FAIL: the keyframe probe could not find the previewed item");
                if (Math.Abs(keyedHost.Opacity - 0.77) > 0.01)
                    throw new Exception($"FAIL: the edited key did not reach the preview ({keyedHost.Opacity:0.###})");

                // The last row starts 0.24s after the phase. A click on its own key must map back
                // to that key's time, and its dot must sit where the value is, not at the boundary.
                var xForGlobal = typeof(MotionMakerWindow).GetMethod("XForGlobal", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new Exception("FAIL: the keyframe probe cannot place a time");
                var timeAt = typeof(MotionMakerWindow).GetMethod("TimeAt", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new Exception("FAIL: the keyframe probe cannot read a time");
                var delay = (keyCatalog.Layout.Elements.Count - 1) * keyClip.ItemStagger;
                var timelineCanvas = FindAll<Canvas>(keyMaker).First(canvas => Math.Abs(canvas.Width - 1040) < 0.5);
                var keyY = 34 + (probeTimelineList.Items.Count - 1) * 34 + 18;
                var keyPoint = new Point((double)xForGlobal.Invoke(keyMaker, [delay + 0.12])!, keyY);
                var mapped = (double)timeAt.Invoke(keyMaker, [keyPoint, null])!;
                if (Math.Abs(mapped - 0.12) > 0.01)
                    throw new Exception($"FAIL: a delayed key mapped back to {mapped:0.###}s instead of 0.12s");
                var keyDot = timelineCanvas.Children.OfType<System.Windows.Shapes.Ellipse>()
                    .FirstOrDefault(dot => ReferenceEquals(dot.Tag, editedKey));
                if (keyDot is null) throw new Exception("FAIL: the edited key has no dot on the timeline");
                var dotCentre = Canvas.GetLeft(keyDot) + keyDot.Width / 2;
                var expectedCentre = (double)xForGlobal.Invoke(keyMaker, [delay + 0.12])!;
                if (Math.Abs(dotCentre - expectedCentre) > 1)
                    throw new Exception($"FAIL: the key dot is drawn at {dotCentre:0.#} instead of {expectedCentre:0.#}");
                var retimeKey = typeof(MotionMakerWindow).GetMethod("RetimeKey", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new Exception("FAIL: the keyframe probe cannot retime a key");
                var retimePoint = new Point((double)xForGlobal.Invoke(keyMaker, [delay + 0.16])!, keyY);
                retimeKey.Invoke(keyMaker, [lateTrack, editedKey, retimePoint]);
                if (Math.Abs(editedKey.Time - 0.16) > 0.01)
                    throw new Exception($"FAIL: dragging a delayed key retimed it to {editedKey.Time:0.###}s instead of 0.16s");

                // Colour keys sit below the numeric dots and follow the same delayed clock.
                var probeColorList = (ListBox?)typeof(MotionMakerWindow)
                    .GetField("_colorList", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(keyMaker)
                    ?? throw new Exception("FAIL: the keyframe probe has no color key list");
                var firstColorKey = lateColor.Keys.OrderBy(key => key.Time).First();
                var colorRow = probeColorList.Items.OfType<object>().FirstOrDefault(item =>
                    ReferenceEquals(item.GetType().GetProperty("Key")?.GetValue(item), firstColorKey));
                if (colorRow is null) throw new Exception("FAIL: the color key never reached the timeline inspector");
                probeColorList.SelectedItem = colorRow;
                var probeColorTime = (TextBox?)typeof(MotionMakerWindow)
                    .GetField("_colorTime", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(keyMaker)
                    ?? throw new Exception("FAIL: the keyframe probe has no color time box");
                var probeColorValue = (TextBox?)typeof(MotionMakerWindow)
                    .GetField("_colorValue", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(keyMaker)
                    ?? throw new Exception("FAIL: the keyframe probe has no color value box");
                probeColorTime.Text = "0.05";
                probeColorValue.Text = "#FF112233";
                var updateColor = typeof(MotionMakerWindow).GetMethod("UpdateColor", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new Exception("FAIL: the keyframe probe cannot update a color key");
                updateColor.Invoke(keyMaker, null);
                var editedColor = lateColor.Keys.FirstOrDefault(key => Math.Abs(key.Time - 0.05) < 0.001)
                    ?? throw new Exception("FAIL: updating a color key did not retime it");
                if (!string.Equals(editedColor.Color, "#FF112233", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"FAIL: updating a color key did not apply its colour ({editedColor.Color})");
                var retimeColor = typeof(MotionMakerWindow).GetMethod("RetimeColorKey", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new Exception("FAIL: the keyframe probe cannot retime a color key");
                var colorPoint = new Point((double)xForGlobal.Invoke(keyMaker, [delay + 0.09])!, keyY + 9);
                retimeColor.Invoke(keyMaker, [lateColor, editedColor, colorPoint]);
                if (Math.Abs(editedColor.Time - 0.09) > 0.01)
                    throw new Exception($"FAIL: dragging a delayed color key retimed it to {editedColor.Time:0.###}s instead of 0.09s");

                // A colour key must repaint what is actually drawn, including a gradient plate that
                // cannot hold a single Colour animation. Scrub first, then play, then restore.
                var colourPhase = new MotionPhase("Colour probe", 0.4);
                colourPhase.Colors.Add(new ColorTrack(MotionColorRole.Text, (0, "#FF000000"), (0.4, "#FFFF0000")));
                colourPhase.Colors.Add(new ColorTrack(MotionColorRole.Background, (0, "#FF000000"), (0.4, "#FF00FF00")));
                var colourText = new TextBlock { Text = "COLOUR", Foreground = new SolidColorBrush(Colors.White), FontSize = 24 };
                var colourPlate = new Border
                {
                    Width = 180, Height = 70,
                    Background = new LinearGradientBrush(Colors.Blue, Colors.White, 0),
                    Child = colourText
                };
                var colourWindow = new Window
                {
                    Content = colourPlate, Width = 220, Height = 110,
                    WindowStyle = WindowStyle.None, ShowInTaskbar = false,
                    ShowActivated = false, Left = -2000, Top = -2000
                };
                colourWindow.Show();
                colourWindow.UpdateLayout();
                Pump(TimeSpan.FromMilliseconds(50));
                var playbackType = typeof(MotionMakerWindow).Assembly.GetType("Scoreboard.Desktop.MotionPlayback")
                    ?? throw new Exception("FAIL: the colour probe cannot find the motion player");
                var applyAt = playbackType.GetMethod("ApplyAt", BindingFlags.Public | BindingFlags.Static)
                    ?? throw new Exception("FAIL: the colour probe cannot scrub a phase");
                var playPhase = playbackType.GetMethod("PlayPhase", BindingFlags.Public | BindingFlags.Static)
                    ?? throw new Exception("FAIL: the colour probe cannot play a phase");
                var stopMotion = playbackType.GetMethod("Stop", BindingFlags.Public | BindingFlags.Static)
                    ?? throw new Exception("FAIL: the colour probe cannot stop a phase");
                applyAt.Invoke(null, [colourPlate, colourPhase, 0.2, null]);
                if (colourText.Foreground is not SolidColorBrush scrubbedText || scrubbedText.Color.R < 90 || scrubbedText.Color.G > 40)
                    throw new Exception($"FAIL: a colour key did not reach the text brush ({colourText.Foreground})");
                if (colourPlate.Background is not SolidColorBrush scrubbedPlate || scrubbedPlate.Color.G < 90 || scrubbedPlate.Color.R > 40)
                    throw new Exception($"FAIL: a colour key did not reach a gradient plate ({colourPlate.Background})");
                stopMotion.Invoke(null, [colourPlate]);
                if (colourPlate.Background is not LinearGradientBrush)
                    throw new Exception("FAIL: stopping a colour key did not restore the plate's gradient");
                playPhase.Invoke(null, [colourPlate, colourPhase, 1.0, null, 1.0, false, false, null]);
                Pump(TimeSpan.FromMilliseconds(180));
                if (colourText.Foreground is not SolidColorBrush playedText || playedText.Color.R < 90 || playedText.Color.G > 60)
                    throw new Exception($"FAIL: a played colour key did not animate the text ({colourText.Foreground})");
                stopMotion.Invoke(null, [colourPlate]);
                colourWindow.Close();
                keyMaker.Close();
                Console.WriteLine("PASS: delayed keyframes and colour keys keep their own times, can be retimed, and accept keyed values");
            }

            var animationMaker = new MotionMakerWindow(MotionKind.Animation, motionCatalog, motionEngine, "Dark");
            animationMaker.Show();
            animationMaker.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(180));
            if (!FindAll<Button>(animationMaker).Any(button => (button.Content as string) == "+3 take"))
                throw new Exception("FAIL: the animation maker does not offer score-specific takes");
            animationMaker.Close();
            Console.WriteLine("PASS: the transition timeline draws every item and adds temporary pieces; the animation maker offers score takes");
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
            // A real WPF popup pushes a dispatcher frame that this harness's nested pump cannot
            // unwind, so the full suite selects programmatically. SP_PROBE_ONLY opens both real
            // lists, takes a pick and closes them, then exits before any nested pump.
            var dropdownProbe = Environment.GetEnvironmentVariable("SP_PROBE_ONLY") == "1";
            Exception? dropdownFailure = null;
            DispatcherUnhandledExceptionEventHandler captureDropdownFailure = (_, args) => { dropdownFailure = args.Exception; args.Handled = true; };
            Application.Current.DispatcherUnhandledException += captureDropdownFailure;
            try
            {
                foreach (var box in new[] { penaltyTypeBox, penaltyTeamBox })
                {
                    if (dropdownProbe)
                    {
                        box.IsDropDownOpen = true;
                        penaltyWindow.UpdateLayout();
                    }
                    box.SelectedIndex = box.Items.Count > 1 ? 1 : 0;
                    penaltyWindow.UpdateLayout();
                    if (dropdownProbe)
                    {
                        box.IsDropDownOpen = false;
                        penaltyWindow.UpdateLayout();
                    }
                }
            }
            catch (Exception ex) { dropdownFailure = ex; }
            finally { Application.Current.DispatcherUnhandledException -= captureDropdownFailure; }
            penaltyWindow.Close();
            if (dropdownFailure is not null)
                throw new Exception($"FAIL: a penalty drop-down threw {dropdownFailure.GetType().Name}: {dropdownFailure.Message}", dropdownFailure);
            Console.WriteLine(dropdownProbe
                ? "PASS: both penalty drop-downs open, take a pick and leave the console standing"
                : "PASS: both penalty pickers take a pick and leave the console standing");
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

            // Macros are part of the main workspace. Conditionals now belong to the Display Studio,
            // where their rules sit beside the display they affect.
            var tabShell = new MainWindow(LaunchMode.New) { Width = 1280, Height = 800 };
            tabShell.Show();
            tabShell.UpdateLayout();
            ((Button)tabShell.FindName("MacrosTab")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            tabShell.UpdateLayout();
            if (tabShell.FindName("MacroHost") is not ContentControl { Content: MacroDock })
                throw new Exception("FAIL: the Macros tab does not host the macro board");
            var macroDock = (MacroDock)((ContentControl)tabShell.FindName("MacroHost")!).Content!;
            var firstDrag = new SavedMacro { Label = "First drag", Color = "#FF6B6B" };
            var secondDrag = new SavedMacro { Label = "Second drag", Color = "#4C9AFF" };
            tabShell.Engine.StoreMacros([firstDrag, secondDrag]);
            macroDock.RefreshView(force: true);
            tabShell.UpdateLayout();
            var macroCards = FindAll<Border>(macroDock).Where(border => border.Width >= 200 && border.Tag is SavedMacro).ToList();
            var dragStrips = FindAll<TextBlock>(macroDock).Where(text => text.Text.Contains("DRAG", StringComparison.Ordinal)).ToList();
            if (macroCards.Count != 2 || dragStrips.Count != 2 || macroCards.Any(card => card.ContextMenu is null)
                || macroCards.Any(card => FindAll<Button>(card).Any(button => (button.Content as string) is "Edit" or "Delete")))
                throw new Exception("FAIL: macro cards lost their drag strip, context menu or removable action row");
            var macroMenu = macroCards[0].ContextMenu!;
            macroMenu.PlacementTarget = macroCards[0];
            macroMenu.IsOpen = true;
            Pump(TimeSpan.FromMilliseconds(100));
            if (!macroMenu.IsOpen)
                throw new Exception("FAIL: the macro context menu did not open");
            macroMenu.IsOpen = false;
            var moveMacro = typeof(MacroDock).GetMethod("MoveMacro", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new Exception("FAIL: macro reordering is missing");
            moveMacro.Invoke(macroDock, [firstDrag.Id, secondDrag.Id]);
            if (tabShell.Engine.State.Macros[0].Id != secondDrag.Id)
                throw new Exception("FAIL: dragging a macro did not reorder the board");
            var macroNameBox = (TextBox)macroDock.FindName("NameBox")!;
            macroNameBox.Text = "Saved with the game";
            macroDock.CommitPendingEdits();
            if (!tabShell.Engine.State.Macros.Any(macro => macro.Label == "Saved with the game"))
                throw new Exception("FAIL: a pending macro edit was not committed to the game");
            if (tabShell.FindName("ConditionalsTab") is not null || tabShell.FindName("ConditionalsHost") is not null)
                throw new Exception("FAIL: the Conditionals editor still owns a main-window tab");
            var gameNameBox = (TextBox)tabShell.FindName("GameNameBox")!;
            if (!gameNameBox.IsVisible || gameNameBox.ActualWidth < 100)
                throw new Exception($"FAIL: the game name field is not visible on the production bar ({gameNameBox.ActualWidth:0}px)");
            gameNameBox.Text = "Friday Night Lights";
            Pump(TimeSpan.FromMilliseconds(100));
            if (tabShell.Engine.State.GameName != "Friday Night Lights")
                throw new Exception("FAIL: the game name field did not reach the game state");
            tabShell.Close();
            Console.WriteLine("PASS: macros open in the main workspace and conditionals have no main-window tab");

            // A macro runs its steps in order against the live game.
            var macroEngine = new GameEngine();
            macroEngine.NewGame("football");
            var macroSession = new MediaSession(PreferencesStore.Load());
            try
            {
                var macro = new SavedMacro
                {
                    Label = "Smoke drive",
                    Actions =
                    [
                        new MacroAction { Kind = MacroActionKind.Score, Home = true, Amount = 3 },
                        new MacroAction { Kind = MacroActionKind.SetDown, Value = 1 },
                        new MacroAction { Kind = MacroActionKind.SetTimeouts, Home = false, Value = 2 }
                    ]
                };
                macroEngine.StoreMacros([macro]);
                var result = new MacroRunner(macroEngine, macroSession).Run(macroEngine.State.Macros[0]);
                if (macroEngine.State.HomeScore != 3 || macroEngine.State.Down != 1 || macroEngine.State.AwayTimeouts != 2
                    || !result.Contains("3 steps run", StringComparison.Ordinal))
                    throw new Exception($"FAIL: macro steps did not run in order ({result})");
            }
            finally { macroSession.Stop(); macroSession.Dispose(); }
            Console.WriteLine("PASS: a macro changes score, down and timeouts in one press");

            // Every console button and every sport/custom value is reachable from a macro, not just
            // the original score/down/clock fields.
            var extendedEngine = new GameEngine();
            extendedEngine.NewGame("football");
            var extendedSession = new MediaSession(PreferencesStore.Load());
            try
            {
                var custom = extendedEngine.State.Stats.First();
                var extended = new SavedMacro
                {
                    Label = "Everything",
                    Actions =
                    [
                        new MacroAction { Kind = MacroActionKind.Control, Path = "football.down.next", Label = "Next down" },
                        new MacroAction { Kind = MacroActionKind.SportValue, Path = "football.firstdowns.home", Value = 4, Label = "Home 1st downs" },
                        new MacroAction { Kind = MacroActionKind.SetFootballDistance, Value = 12 },
                        new MacroAction { Kind = MacroActionKind.SetFootballBallSpot, Value = 35 },
                        new MacroAction { Kind = MacroActionKind.SetPossession, Home = false },
                        new MacroAction { Kind = MacroActionKind.CustomValue, Path = custom.Id, Value = 7, Label = custom.Name }
                    ]
                };
                var pressed = "";
                var extendedResult = new MacroRunner(extendedEngine, extendedSession, id => pressed = id).Run(extended);
                if (pressed != "football.down.next"
                    || SportValues.Read(extendedEngine.State, "football.firstdowns.home") != 4
                    || extendedEngine.State.YardsToGo != 12 || extendedEngine.State.YardsToGoal != 35
                    || extendedEngine.State.HomePossession
                    || extendedEngine.State.Stats.First(stat => stat.Id == custom.Id).Value != 7
                    || !extendedResult.Contains("6 steps run", StringComparison.Ordinal))
                    throw new Exception($"FAIL: extended macro steps did not run ({extendedResult})");
            }
            finally { extendedSession.Stop(); extendedSession.Dispose(); }
            Console.WriteLine("PASS: a macro can press a console button and set every kind of value");

            // The multiview is a source picker per tile: program previews, audience outputs, cameras,
            // replay and blank spacers all share one wall.
            var multiviewClick = typeof(MainWindow).GetMethod("Multiview_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new Exception("FAIL: Multiview_Click is missing");
            var multiviewOwner = new MainWindow(LaunchMode.New) { Width = 1280, Height = 800 };
            multiviewOwner.Show();
            multiviewOwner.UpdateLayout();
            multiviewClick.Invoke(multiviewOwner, [multiviewOwner, new RoutedEventArgs()]);
            Pump(TimeSpan.FromMilliseconds(250));
            var multiview = Application.Current.Windows.OfType<MultiviewWindow>().LastOrDefault()
                ?? throw new Exception("FAIL: the Multiview button opened no multiview window");
            multiview.UpdateLayout();
            var sourceBoxes = FindAll<ComboBox>(multiview)
                .Where(box => box.Items.Cast<object>().Any(item => (item.ToString() ?? "").Contains("AUDIENCE OUTPUT", StringComparison.Ordinal)))
                .ToList();
            var sourceLabels = sourceBoxes.SelectMany(box => box.Items.Cast<object>())
                .Select(item => item.ToString() ?? "").ToList();
            if (sourceBoxes.Count < 3
                || sourceLabels.All(label => !label.Contains("PROGRAM PREVIEW", StringComparison.Ordinal))
                || sourceLabels.All(label => !label.Contains("AUDIENCE OUTPUT", StringComparison.Ordinal))
                || sourceLabels.All(label => !label.StartsWith("REPLAY", StringComparison.Ordinal)))
                throw new Exception("FAIL: the multiview did not offer preview, audience and replay sources");

            // Moving the replay tile away and back also proves its persistent frame follows its
            // new home instead of making the old tile the only legal parent.
            var replayBox = FindAll<ComboBox>(multiview)
                .First(box => (box.SelectedItem?.ToString() ?? "").StartsWith("REPLAY", StringComparison.Ordinal));
            var previewChoice = replayBox.Items.Cast<object>()
                .FirstOrDefault(item => (item.ToString() ?? "").Contains("PROGRAM PREVIEW", StringComparison.Ordinal))
                ?? throw new Exception("FAIL: the multiview had no preview source to switch to");
            replayBox.SelectedItem = previewChoice;
            Pump(TimeSpan.FromMilliseconds(200));
            var replayAgain = FindAll<ComboBox>(multiview)
                .First(box => box.Items.Cast<object>().Any(item => (item.ToString() ?? "").StartsWith("REPLAY", StringComparison.Ordinal)));
            var replayChoice = replayAgain.Items.Cast<object>()
                .First(item => (item.ToString() ?? "").StartsWith("REPLAY", StringComparison.Ordinal));
            replayAgain.SelectedItem = replayChoice;
            Pump(TimeSpan.FromMilliseconds(200));
            if (!FindAll<ComboBox>(multiview).Any(box => (box.SelectedItem?.ToString() ?? "").StartsWith("REPLAY", StringComparison.Ordinal)))
                throw new Exception("FAIL: the replay tile did not follow a source switch");

            multiview.Close();
            multiviewOwner.Close();
            Pump(TimeSpan.FromMilliseconds(150));
            Console.WriteLine("PASS: the multiview lets every tile choose a preview, audience, camera, replay or blank source");

            // The dock is a single media panel: tiles, transport and tag editor, no inline event log.
            var shell = new MainWindow { Width = 1280, Height = 800 };
            shell.Show();
            shell.UpdateLayout();
            // The dock lives on the MEDIA tab now, so it is only laid out once that tab is open.
            shell.ShowMediaTab(true);
            shell.UpdateLayout();
            var shellContent = (FrameworkElement)shell.Content;
            var nameBoxForHeader = (TextBox)shell.FindName("GameNameBox")!;
            if (nameBoxForHeader.FontSize < 15 || nameBoxForHeader.Width < 210)
                throw new Exception("FAIL: the Game Name field is still too small");
            if (!FindAll<TextBlock>(shell).Any(label => label.Text == "Game Name"))
                throw new Exception("FAIL: the Game Name label is missing or still in caps");
            if (!FindAll<Button>(shell).Any(button => (button.ToolTip as string ?? string.Empty).StartsWith("About ScorePlus", StringComparison.OrdinalIgnoreCase)))
                throw new Exception("FAIL: the main window has no About button");
            var dock = FindAll<MediaDock>(shell).FirstOrDefault() ?? throw new Exception("FAIL: the main window hosts no media dock");
            var transport = (Button)dock.FindName("PlayPauseButton")!;
            var transportBottom = transport.TransformToAncestor(shellContent).Transform(new Point(0, transport.ActualHeight)).Y;
            if (transportBottom > shellContent.ActualHeight + 0.5)
                throw new Exception($"FAIL: the media dock runs past the window ({transportBottom:0} of {shellContent.ActualHeight:0})");
            if (dock.FindName("Tiles") is not WrapPanel || dock.FindName("TagBox") is not ComboBox)
                throw new Exception("FAIL: the unified media dock is missing its tiles or tag editor");
            if (dock.FindName("TransitionBox") is not ComboBox mediaTransitionBox || mediaTransitionBox.Items.Count < 4)
                throw new Exception("FAIL: the unified media dock is missing its switch-animation picker");
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
            if (dock.Resources["DockRail"] is not SolidColorBrush railBrush || !IsColor(railBrush, 8, 8, 8))
                throw new Exception($"FAIL: the Add & Route rail still has a non-black background ({dock.Resources["DockRail"]})");
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
            if (!FindAll<Button>(shell).Any(b => (b.ToolTip as string ?? string.Empty).StartsWith("League cloud", StringComparison.OrdinalIgnoreCase)))
                throw new Exception("FAIL: the league-cloud button is missing from the top row");
            // The phone's media card must separate a paused clip from playing media, and speakers
            // are their own on-air row rather than being reported as playing clips.
            Directory.CreateDirectory(appDataPath);
            var remoteMediaPath = Path.Combine(appDataPath, $"remote-paused-{Guid.NewGuid():N}.wav");
            WriteSilentWav(remoteMediaPath, 1);
            var mediaSession = (MediaSession?)typeof(MainWindow)
                .GetField("_session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(shell);
            if (mediaSession is null) throw new Exception("FAIL: the shell has no media session");
            var pausedClip = new MediaEntry(remoteMediaPath, "Ads", Name: "Paused test clip");
            mediaSession.Library.Add(pausedClip);
            mediaSession.SetClipTransition(pausedClip, "Fade");
            if (mediaSession.Library.First(item => item.Path == remoteMediaPath).Transition != "Fade")
                throw new Exception("FAIL: a clip's switch animation was not saved to the media library");
            mediaSession.Play(pausedClip);
            mediaSession.TogglePlayPause();
            mediaSession.Library.Add(new MediaEntry(MediaLibrary.SpeakerPath(0), Name: "Test speaker"));
            var snapshotMethod = typeof(MainWindow).GetMethod("MediaSnapshotJson", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var snapshotJson = (string?)snapshotMethod?.Invoke(shell, null);
            using var snapshot = System.Text.Json.JsonDocument.Parse(snapshotJson!);
            bool SnapshotHas(string property, string? value) => snapshot.RootElement.GetProperty(property)
                .EnumerateArray().Any(item => item.GetString() == value);
            if (SnapshotHas("live", "Paused test clip"))
                throw new Exception("FAIL: the remote calls a paused clip live");
            if (!SnapshotHas("paused", "Paused test clip"))
                throw new Exception("FAIL: the remote lost the paused clip");
            if (!SnapshotHas("speakers", "Test speaker"))
                throw new Exception("FAIL: the remote lost the speaker on-air row");
            if (!snapshot.RootElement.GetProperty("clips").EnumerateArray().Any(item =>
                    item.GetProperty("label").GetString() == "Test speaker" && item.GetProperty("onAir").GetBoolean()))
                throw new Exception("FAIL: the remote speaker row does not say ON AIR");
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
            if (editor.FindName("ConditionalsButton") is not Button conditionalsButton)
                throw new Exception("FAIL: Display Studio does not offer Conditionals");
            conditionalsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var conditionals = Application.Current.Windows.OfType<AutomationWindow>().FirstOrDefault();
            if (conditionals is null || !conditionals.IsVisible || conditionals.DisplayId != catalog.ActiveDisplay.Id)
                throw new Exception("FAIL: Display Studio did not open the Conditionals window for its display");
            conditionals.Close();

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
            Console.WriteLine("PASS: Display Studio loads");

            // ---------- Display Studio: another layout is a live, resizeable feed ----------
            {
                var nestedPath = Path.Combine(Path.GetTempPath(), $"smoke-live-layout-{Guid.NewGuid():N}.json");
                ScorebugEditorWindow? nestedEditor = null;
                try
                {
                    var nestedCatalog = new ScorebugCatalog(nestedPath);
                    var liveSourceId = nestedCatalog.Layout.Id;
                    nestedCatalog.Update(layout =>
                    {
                        layout.Elements.Clear();
                        layout.Elements.Add(new ScorebugElement
                        {
                            Name = "Live score", Kind = ScorebugElementKind.Text, Content = "{{HOME_SCORE}}",
                            X = 30, Y = 30, Width = 260, Height = 90, FontSize = 56
                        });
                    });
                    nestedCatalog.AddLayout("Feed host");
                    var feedElement = new ScorebugElement
                    {
                        Name = "Live layout", Kind = ScorebugElementKind.Layout, Content = "",
                        NestedLayoutId = liveSourceId, NestedPreserveAspect = true,
                        X = 140, Y = 100, Width = 480, Height = 270
                    };
                    nestedCatalog.Update(layout => { layout.Elements.Clear(); layout.Elements.Add(feedElement); });
                    var nestedEngine = new GameEngine();
                    nestedEngine.NewGame("football");
                    nestedEditor = new ScorebugEditorWindow(nestedCatalog, nestedEngine);
                    nestedEditor.Show();
                    nestedEditor.UpdateLayout();
                    if (!nestedEditor.Title.Contains("Display Studio", StringComparison.Ordinal))
                        throw new Exception($"FAIL: the editor is still titled '{nestedEditor.Title}'");
                    var nestedCanvas = (Canvas)nestedEditor.FindName("DesignCanvas")!;
                    var feedHost = nestedCanvas.Children.OfType<Border>()
                        .SingleOrDefault(border => (border.Tag as ScorebugElement)?.Id == feedElement.Id)
                        ?? throw new Exception("FAIL: the live layout has no element on the studio canvas");
                    var feed = FindAll<ScorebugLayoutFeed>(feedHost).SingleOrDefault()
                        ?? throw new Exception("FAIL: the live layout did not build a nested canvas");
                    var nestedView = feed.Children.OfType<ScorebugCanvasView>().SingleOrDefault()
                        ?? throw new Exception("FAIL: the live layout has no live feed view");
                    if (nestedView.Layout.Id != liveSourceId)
                        throw new Exception("FAIL: the live layout is not drawing its source layout");
                    nestedEngine.Score(true, 7);
                    Pump(TimeSpan.FromMilliseconds(650));
                    nestedEditor.UpdateLayout();
                    if (!FindAll<TextBlock>(nestedView).Any(text => text.Text.Trim() == "7"))
                        throw new Exception("FAIL: a score change did not reach the live nested layout");
                    var nestedLayers = (ListBox)nestedEditor.FindName("Layers")!;
                    nestedLayers.SelectedIndex = 0;
                    nestedEditor.UpdateLayout();
                    var nestedAspect = (CheckBox)nestedEditor.FindName("NestedAspectInput")!;
                    var nestedPicker = (ComboBox)nestedEditor.FindName("NestedLayoutInput")!;
                    var pickedId = nestedPicker.SelectedItem?.GetType().GetProperty("Id")?.GetValue(nestedPicker.SelectedItem) as string;
                    if (nestedAspect.IsChecked != true || pickedId != liveSourceId)
                        throw new Exception("FAIL: the live layout panel does not show its source and fit mode");
                    var resizeHandle = FindAll<Border>(feedHost)
                        .FirstOrDefault(border => border.Cursor == Cursors.SizeNWSE && border.Visibility == Visibility.Visible)
                        ?? throw new Exception("FAIL: a selected live layout has no resize handle");
                    var nestedWidth = (TextBox)nestedEditor.FindName("WidthInput")!;
                    nestedWidth.Text = "360";
                    Pump(TimeSpan.FromMilliseconds(650));
                    nestedEditor.UpdateLayout();
                    if (Math.Abs(nestedCatalog.Layout.Elements.Single().Width - 360) > 0.5
                        || Math.Abs(feedHost.Width - 360) > 0.5)
                        throw new Exception($"FAIL: the live layout did not resize ({nestedCatalog.Layout.Elements.Single().Width:0}, {feedHost.Width:0})");
                    Console.WriteLine("PASS: Display Studio embeds another layout as a live, resizeable feed");
                }
                finally
                {
                    nestedEditor?.Close();
                    if (File.Exists(nestedPath)) File.Delete(nestedPath);
                }
            }

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
                    // Multiple selected objects share one outer frame and one resize handle. Their own
                    // handles are hidden so the drag scales the group, not one item at a time.
                    var groupBox = (Border)multiEditor.FindName("GroupSelectionBox")!;
                    var groupHandle = (Border)multiEditor.FindName("GroupResizeHandle")!;
                    if (groupBox.Visibility != Visibility.Visible || groupHandle.Visibility != Visibility.Visible)
                        throw new Exception("FAIL: a multi-selection has no group resize box and handle");
                    if (groupBox.Background is SolidColorBrush groupFill && groupFill.Color.A != 0)
                        throw new Exception("FAIL: the group resize box tints the artwork instead of only outlining it");
                    var groupElements = multiCatalog.Layout.Elements.Where(element => selectedIds.Contains(element.Id)).ToArray();
                    var expectedGroupWidth = groupElements.Max(element => element.X + element.Width) - groupElements.Min(element => element.X);
                    var expectedGroupHeight = groupElements.Max(element => element.Y + element.Height) - groupElements.Min(element => element.Y);
                    if (Math.Abs(groupBox.ActualWidth - expectedGroupWidth) > 0.5 || Math.Abs(groupBox.ActualHeight - expectedGroupHeight) > 0.5)
                        throw new Exception($"FAIL: the group box is {groupBox.ActualWidth:0}×{groupBox.ActualHeight:0}, expected {expectedGroupWidth:0}×{expectedGroupHeight:0}");
                    var individualHandles = FindAll<Border>((Canvas)multiEditor.FindName("DesignCanvas")!)
                        .Count(handle => handle.Tag is ScorebugElement && handle.Width == 16 && handle.Height == 16
                            && handle.Visibility == Visibility.Visible);
                    if (individualHandles != 0)
                        throw new Exception($"FAIL: {individualHandles} individual resize handles are still visible inside a group selection");
                    var scaleGroup = typeof(ScorebugEditorWindow).GetMethod("ScaleGroupElements", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                    var groupItems = new[]
                    {
                        new ScorebugElement { X = 100, Y = 100, Width = 100, Height = 50, FontSize = 20 },
                        new ScorebugElement { X = 200, Y = 150, Width = 60, Height = 40, FontSize = 20 }
                    };
                    scaleGroup.Invoke(null, new object[] { groupItems, new Rect(100, 100, 160, 90), 2.0, 0.5, true });
                    if (Math.Abs(groupItems[0].Width - 200) > 0.01 || Math.Abs(groupItems[0].Height - 25) > 0.01
                        || Math.Abs(groupItems[1].X - 300) > 0.01 || Math.Abs(groupItems[1].Y - 125) > 0.01
                        || Math.Abs(groupItems[1].Width - 120) > 0.01 || Math.Abs(groupItems[1].Height - 20) > 0.01
                        || Math.Abs(groupItems[0].FontSize - 10) > 0.01)
                        throw new Exception("FAIL: group resize did not scale every selected item around the group box");
                    // A panel save that was queued before the drag must not write one rounded width
                    // onto the whole selection while the resize is still finishing.
                    var widthField = (TextBox)multiEditor.FindName("WidthInput")!;
                    var widthsBeforeStaleSave = groupElements.ToDictionary(element => element.Id, element => element.Width);
                    var resizingField = typeof(ScorebugEditorWindow).GetField("_resizing", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    var settlingField = typeof(ScorebugEditorWindow).GetField("_resizeSettling", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    var saveProperties = typeof(ScorebugEditorWindow).GetMethod("SaveProperties", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    resizingField.SetValue(multiEditor, true);
                    widthField.Text = "999";
                    saveProperties.Invoke(multiEditor, null);
                    resizingField.SetValue(multiEditor, false);
                    settlingField.SetValue(multiEditor, true);
                    widthField.Text = "998";
                    saveProperties.Invoke(multiEditor, null);
                    settlingField.SetValue(multiEditor, false);
                    if (groupElements.Any(element => Math.Abs(element.Width - widthsBeforeStaleSave[element.Id]) > 0.001))
                        throw new Exception("FAIL: a delayed property save overwrote the group's finished sizes");
                    widthField.Text = groupElements[0].Width.ToString("0.##");
                    Console.WriteLine("PASS: a multi-selection has one group box whose handle scales every item together");
                    RaiseKey(multiEditor, PresentationSource.FromVisual(multiEditor), Key.Delete);
                    multiEditor.UpdateLayout();
                    if (multiCatalog.Layout.Elements.Count != multiBefore - 2)
                        throw new Exception($"FAIL: Delete removed {multiBefore - multiCatalog.Layout.Elements.Count} objects instead of 2");
                    multiEditor.Close();
                }
                finally { if (File.Exists(multiPath)) File.Delete(multiPath); }
                Console.WriteLine("PASS: the studio keeps a multi-selection and Delete removes every selected object");
            }

            // ---------- layout hand-over that slides matching items between their old and new spots ----------
            {
                var movePath = Path.Combine(Path.GetTempPath(), $"smoke-move-{Guid.NewGuid():N}.json");
                ScorebugCanvasView? moveView = null;
                Window? moveWindow = null;
                try
                {
                    var moveCatalog = new ScorebugCatalog(movePath);
                    moveCatalog.Update(layout =>
                    {
                        layout.Elements.Clear();
                        layout.Elements.Add(new ScorebugElement
                        {
                            Name = "Mover", Content = "MOVE", Kind = ScorebugElementKind.Text,
                            X = 120, Y = 100, Width = 240, Height = 90, FontSize = 36
                        });
                        layout.Transition = LayoutTransition.Cut;
                        layout.TransitionSeconds = 0;
                    });
                    var originalId = moveCatalog.Layout.Elements[0].Id;
                    var movedIndex = moveCatalog.AddLayout("Moved", copyActive: true);
                    var movedId = moveCatalog.Layouts[movedIndex].Elements[0].Id;
                    if (movedId == originalId)
                        throw new Exception("FAIL: the duplicate layout did not get independent element identity");
                    moveCatalog.SelectLayout(movedIndex);
                    moveCatalog.Update(layout =>
                    {
                        layout.Elements[0].X = 620;
                        layout.Elements[0].Y = 340;
                        layout.Transition = LayoutTransition.Move;
                        layout.TransitionSeconds = 0.5;
                    });
                    moveCatalog.SelectLayout(0);
                    moveView = new ScorebugCanvasView(moveCatalog, engine, stateProvider: () => engine.State,
                        layoutProvider: () => moveCatalog.Layout);
                    moveWindow = new Window
                    {
                        Width = 800, Height = 450, Content = moveView, ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None, Left = -32000, Top = -32000
                    };
                    moveWindow.Show();
                    moveWindow.UpdateLayout();
                    Pump(TimeSpan.FromMilliseconds(60));
                    moveCatalog.SelectLayout(movedIndex);
                    var canvasField = typeof(ScorebugCanvasView).GetField("_canvas", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    var moveCanvas = (Canvas)canvasField.GetValue(moveView)!;
                    var movingHost = moveCanvas.Children.OfType<Border>().Single();
                    var movingGroup = movingHost.RenderTransform as TransformGroup;
                    var movingSlide = movingGroup?.Children.OfType<TranslateTransform>().LastOrDefault();
                    if (movingSlide is null || movingSlide.X > -490 || movingSlide.X < -510)
                        throw new Exception($"FAIL: the moved duplicate did not start at its old position ({movingSlide?.X})");
                    Pump(TimeSpan.FromMilliseconds(120));
                    if (movingSlide.X <= -489)
                        throw new Exception("FAIL: the moved duplicate did not travel toward its new position");
                    Pump(TimeSpan.FromMilliseconds(520));
                    if (movingHost.RenderTransform is TransformGroup leftover
                        && leftover.Children.OfType<TranslateTransform>().Any(slide => Math.Abs(slide.X) > 0.5))
                        throw new Exception("FAIL: the move hand-over left the item offset after it finished");
                    Console.WriteLine("PASS: a duplicate's moved item slides from its old position to its new one");
                }
                finally
                {
                    moveView?.Dispose();
                    moveWindow?.Close();
                    if (File.Exists(movePath)) File.Delete(movePath);
                }
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
            if (preferencesWindow.FindName("TieRuleInput") is not ComboBox || preferencesWindow.FindName("TiePeriodInput") is not TextBox)
                throw new Exception("FAIL: preferences is missing the tie-rule controls");
            if (preferencesWindow.FindName("OvertimeFinalInput") is not CheckBox)
                throw new Exception("FAIL: preferences is missing the overtime final label option");
            preferencesWindow.Close();
            var aboutWindow = new AboutWindow("Dark");
            aboutWindow.Show();
            aboutWindow.UpdateLayout();
            if (!FindAll<TextBlock>(aboutWindow).Any(text => text.Text == "ScorePlus")
                || !FindAll<TextBlock>(aboutWindow).Any(text => text.Text == "LHUltimate")
                || !FindAll<TextBlock>(aboutWindow).Any(text => text.Text.StartsWith("Version ", StringComparison.Ordinal))
                || !FindAll<Image>(aboutWindow).Any(image => image.Source is not null))
                throw new Exception("FAIL: the About box does not show the logo, name, version and credits");
            aboutWindow.Close();
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
                if (sports.Find("football")!.Timeouts != 3 || !sports.IsOverridden("football"))
                    throw new Exception($"FAIL: football profile timeouts were not capped at three ({sports.Find("football")!.Timeouts})");
                profileWindow.Close();
                Console.WriteLine("PASS: editing a built-in sport profile saves the timeout count");
            }
            finally { if (File.Exists(profileCatalogPath)) File.Delete(profileCatalogPath); }

            engine.AddStat("Turnovers");
            var statsPanel = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            statsPanel.Show();
            statsPanel.UpdateLayout();
            var statsHost = (Panel)statsPanel.Panel.FindName("StatsHost")!;
            if (statsHost.Children.Count != engine.State.Stats.Count) throw new Exception("FAIL: custom value rows were not created");
            // The starter set must fit the supported minimum window without its value controls
            // being cut off by the row or the card. A fixed row used to clip the input wells while
            // reporting a larger ActualHeight, so check the row's explicit height too.
            statsPanel.Width = 1040;
            statsPanel.Height = 915;
            statsPanel.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(80));
            var statsViewport = (ScrollViewer)statsPanel.Panel.FindName("StatsScroller")!;
            if (statsViewport.ScrollableHeight > 0.5)
                throw new Exception($"FAIL: the default custom values need scrolling at the minimum window size ({statsViewport.ScrollableHeight:0.#}px)");
            foreach (Grid row in statsHost.Children)
            {
                var controls = row.Children.OfType<Grid>().Last();
                var caption = row.Children.OfType<TextBlock>().First();
                var bottom = controls.TransformToAncestor(row)
                    .TransformBounds(new Rect(0, 0, controls.ActualWidth, controls.ActualHeight)).Bottom;
                var clipHeight = double.IsNaN(row.Height) ? row.ActualHeight : row.Height;
                if (caption.ActualHeight <= 0 || controls.ActualHeight < 22 || bottom > clipHeight + 0.5)
                    throw new Exception($"FAIL: a custom value row clips its controls (caption {caption.ActualHeight:0.#}, controls {controls.ActualHeight:0.#}, bottom {bottom:0.#}, row {clipHeight:0.#})");
            }
            var stat2 = engine.State.Stats.First(s => s.Name == "Turnovers");
            engine.AdjustStat(stat2.Id, 3);
            statsPanel.UpdateLayout();
            if (!statsPanel.Panel.HomeScoreText.Equals(engine.State.HomeScore.ToString()))
                throw new Exception("FAIL: control panel did not refresh after a value change");
            Console.WriteLine("PASS: custom values render and update in the control panel");

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
            var capsBox = FindAll<CheckBox>(saveWindow).FirstOrDefault(box => (box.Content as string) == "Down line prints in full caps")
                ?? throw new Exception("FAIL: preferences have no full-caps down line option");
            if (capsBox.IsChecked == true)
                throw new Exception("FAIL: the full-caps down line is on by default");
            capsBox.IsChecked = true;
            saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (prefsProbe.Theme != "Light") throw new Exception("FAIL: saving preferences did not apply the theme");
            if (!prefsProbe.UppercaseDownWords || !GameEngine.UppercaseDownWordsEnabled)
                throw new Exception("FAIL: the full-caps down line preference did not save or apply");
            // System is offered alongside the pinned themes and can be saved.
            var systemChoices = ((ComboBox)saveWindow.FindName("ThemeSelector")!).ItemsSource as string[];
            if (systemChoices is null || !systemChoices.Contains("System"))
                throw new Exception("FAIL: preferences do not offer the System theme");
            var systemWindow = new SettingsWindow(prefsProbe);
            systemWindow.Show();
            systemWindow.UpdateLayout();
            ((ComboBox)systemWindow.FindName("ThemeSelector")!).SelectedItem = "System";
            var capsOff = FindAll<CheckBox>(systemWindow).First(box => (box.Content as string) == "Down line prints in full caps");
            if (capsOff.IsChecked != true)
                throw new Exception("FAIL: the saved full-caps down line did not reload");
            capsOff.IsChecked = false;
            FindAll<Button>(systemWindow).First(b => (b.Content as string) == "Save")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (prefsProbe.Theme != "System")
                throw new Exception($"FAIL: the System theme saved as '{prefsProbe.Theme}'");
            if (prefsProbe.UppercaseDownWords || GameEngine.UppercaseDownWordsEnabled)
                throw new Exception("FAIL: turning the full-caps down line off did not save or apply");
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

                // History and the object clipboard use the same saves as the rest of the studio.
                var beforeAdd = freshCatalog.Layout.Elements.Count;
                var addButton = FindAll<Button>(addEditor).First(b => (b.Content as string) == "TEXT +");
                addButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                addEditor.UpdateLayout();
                if (freshCatalog.Layout.Elements.Count != beforeAdd + 1)
                    throw new Exception("FAIL: adding an element did not change the layout");
                var undoButton = (Button)addEditor.FindName("UndoButton")!;
                var redoButton = (Button)addEditor.FindName("RedoButton")!;
                if (!undoButton.IsEnabled) throw new Exception("FAIL: undo did not enable after an edit");
                undoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                addEditor.UpdateLayout();
                if (freshCatalog.Layout.Elements.Count != beforeAdd)
                    throw new Exception("FAIL: undo did not restore the layout before the add");
                if (!redoButton.IsEnabled) throw new Exception("FAIL: redo did not enable after an undo");
                redoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                addEditor.UpdateLayout();
                if (freshCatalog.Layout.Elements.Count != beforeAdd + 1)
                    throw new Exception("FAIL: redo did not restore the added element");
                // A redo rebuilds the canvas and clears the layer selection; pick a row again, as
                // the operator would, before exercising the object clipboard.
                studioLayers.SelectedItems.Clear();
                studioLayers.SelectedItems.Add(studioLayers.Items[studioLayers.Items.Count - 1]);
                addEditor.UpdateLayout();
                var copyMethod = typeof(ScorebugEditorWindow).GetMethod("CopySelection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var pasteMethod = typeof(ScorebugEditorWindow).GetMethod("PasteSelection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                copyMethod.Invoke(addEditor, null);
                pasteMethod.Invoke(addEditor, null);
                addEditor.UpdateLayout();
                if (freshCatalog.Layout.Elements.Count != beforeAdd + 2)
                    throw new Exception("FAIL: copy and paste did not add an independent element");
                var photoButton = FindAll<Button>(addEditor).First(b => (b.Content as string) == "PLAYER PHOTO +");
                photoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                addEditor.UpdateLayout();
                var portrait = freshCatalog.Layout.Elements[^1];
                if (portrait.Kind != ScorebugElementKind.Image || portrait.PlayerPhotoSide != ScorebugPlayerPhotoSide.Home)
                    throw new Exception("FAIL: the studio's player-photo button did not add a home portrait element");
                Console.WriteLine("PASS: studio undo/redo, object copy/paste and the player-photo element work");

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
            Rect Bounds(FrameworkElement element)
            {
                var topLeft = element.TransformToAncestor(view).Transform(new Point(0, 0));
                return new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
            }
            Border FootballCardFor(DependencyObject node)
            {
                var cardStyle = (Style)view.FindResource("Card");
                for (var current = node; current is not null && current != view; current = VisualTreeHelper.GetParent(current))
                    if (current is Border border && border.Style == cardStyle) return border;
                throw new Exception("FAIL: a console control is not inside a card");
            }
            void AssertInsideCard(FrameworkElement element, string label)
            {
                if (!element.IsVisible) throw new Exception($"FAIL: {label} is hidden at this window size");
                var bounds = Bounds(element);
                var cardBounds = Bounds(FootballCardFor(element));
                if (bounds.Left < cardBounds.Left - 0.5 || bounds.Top < cardBounds.Top - 0.5 ||
                    bounds.Right > cardBounds.Right + 0.5 || bounds.Bottom > cardBounds.Bottom + 0.5)
                    throw new Exception($"FAIL: {label} is outside its card ({bounds:0.#} inside {cardBounds:0.#})");
            }
            void AssertFootballCardFits()
            {
                foreach (var name in new[]
                         {
                             "NewSeriesButton", "RecallDownButton", "FootballModePicker", "PenaltyStrip"
                         })
                {
                    var control = view.FindName(name) as FrameworkElement
                        ?? throw new Exception($"FAIL: {name} is missing from the console");
                    AssertInsideCard(control, name);
                }
                var modeLabel = view.FindName("FootballModeLabel") as TextBlock
                    ?? throw new Exception("FAIL: the football down-line label is missing");
                var modeNext = view.FindName("FootballModeNextButton") as Button
                    ?? throw new Exception("FAIL: the next down-line arrow is missing");
                var modePrev = view.FindName("FootballModePrevButton") as Button
                    ?? throw new Exception("FAIL: the previous down-line arrow is missing");
                // The whole arrow button must be inside its card, not just the row around it.
                AssertInsideCard(modeNext, "FootballModeNextButton");
                AssertInsideCard(modePrev, "FootballModePrevButton");
                // The glyph is drawn as a vector, so no font's overhang can clip half the arrow,
                // and the fixed row must give the full button its 38 pixels rather than shrinking
                // it through the shared button margin.
                if (modeNext.ActualHeight < 37 || modePrev.ActualHeight < 37 ||
                    modeNext.Margin != new Thickness(0) || modePrev.Margin != new Thickness(0))
                    throw new Exception($"FAIL: the down-line arrows are squeezed ({modePrev.ActualHeight:0.#} / {modeNext.ActualHeight:0.#} px)");
                var nextGlyph = FindAll<System.Windows.Shapes.Path>(modeNext).FirstOrDefault()
                    ?? throw new Exception("FAIL: the next down-line arrow has no vector glyph");
                var prevGlyph = FindAll<System.Windows.Shapes.Path>(modePrev).FirstOrDefault()
                    ?? throw new Exception("FAIL: the previous down-line arrow has no vector glyph");
                void AssertGlyphInside(Button button, System.Windows.Shapes.Path glyph, string label)
                {
                    var glyphBounds = glyph.TransformToAncestor(button)
                        .TransformBounds(new Rect(glyph.RenderSize));
                    if (glyphBounds.Left < -0.5 || glyphBounds.Top < -0.5 ||
                        glyphBounds.Right > button.ActualWidth + 0.5 || glyphBounds.Bottom > button.ActualHeight + 0.5)
                        throw new Exception($"FAIL: the {label} arrow glyph is clipped ({glyphBounds:0.#} inside {button.ActualWidth:0.#}x{button.ActualHeight:0.#})");
                }
                AssertGlyphInside(modeNext, nextGlyph, "next");
                AssertGlyphInside(modePrev, prevGlyph, "previous");
                if (modeLabel.Text != "Yards")
                    throw new Exception($"FAIL: the down-line arrows start on {modeLabel.Text}, not Yards");
                modeNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (modeLabel.Text != "Goal" || engine.State.FootballDownMode != FootballDownMode.Goal)
                    throw new Exception($"FAIL: next from Yards did not reach Goal ({modeLabel.Text}, {engine.State.FootballDownMode})");
                modeNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (modeLabel.Text != "PAT" || engine.State.FootballDownMode != FootballDownMode.Pat)
                    throw new Exception($"FAIL: next from Goal did not reach PAT ({modeLabel.Text}, {engine.State.FootballDownMode})");
                modeNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (modeLabel.Text != "2 PT ATT" || engine.State.FootballDownMode != FootballDownMode.TwoPoint)
                    throw new Exception($"FAIL: the arrows did not reach 2 PT ATT ({modeLabel.Text}, {engine.State.FootballDownMode})");
                modeNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (modeLabel.Text != "No line" || engine.State.FootballDownMode != FootballDownMode.None)
                    throw new Exception($"FAIL: the arrows did not reach No line ({modeLabel.Text}, {engine.State.FootballDownMode})");
                modeNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (modeLabel.Text != "Yards" || engine.State.FootballDownMode != FootballDownMode.Normal)
                    throw new Exception($"FAIL: the arrows did not wrap back to Yards ({modeLabel.Text}, {engine.State.FootballDownMode})");
                modePrev.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (modeLabel.Text != "No line" || engine.State.FootballDownMode != FootballDownMode.None)
                    throw new Exception($"FAIL: previous from Yards did not wrap to No line ({modeLabel.Text}, {engine.State.FootballDownMode})");
                engine.SetFootballDownMode(FootballDownMode.Normal);
                view.Refresh();
                var ballSide = FindAll<Button>(view).FirstOrDefault(b => (b.Tag as string) == "football.ball.side")
                    ?? throw new Exception("FAIL: the ball-side switch is missing from the console");
                AssertInsideCard(ballSide, "the ball-side switch");
                // The compact step row runs minus / value / plus, and the ball-side switch has its
                // own clear line above those arrows instead of touching the Ball On controls.
                Button StepButton(string hostName, string content)
                {
                    var host = view.FindName(hostName) as ContentControl
                        ?? throw new Exception($"FAIL: {hostName} is missing from the console");
                    return FindAll<Button>(host).FirstOrDefault(button => Equals(button.Content, content))
                        ?? throw new Exception($"FAIL: {hostName} has no {content} step button");
                }
                foreach (var hostName in new[] { "DownHost", "ToGoHost", "BallOnHost" })
                {
                    if (Left(StepButton(hostName, "−")) >= Left(StepButton(hostName, "+")))
                        throw new Exception($"FAIL: {hostName} puts + before − instead of − on the left and + on the right");
                }
                var ballMinus = StepButton("BallOnHost", "−");
                var ballPlus = StepButton("BallOnHost", "+");
                var ballSideBounds = Bounds(ballSide);
                var ballStepTop = Math.Min(Bounds(ballMinus).Top, Bounds(ballPlus).Top);
                if (ballSideBounds.Bottom > ballStepTop - 0.5)
                    throw new Exception($"FAIL: the ball-side switch clips into the Ball On step arrows ({ballSideBounds.Bottom:0.#} vs {ballStepTop:0.#})");
                var clockValue = (FrameworkElement)view.FindName("ClockValue")!;
                var clockMessage = (FrameworkElement)view.FindName("ClockMessageBox")!;
                AssertInsideCard(clockValue, "ClockValue");
                AssertInsideCard(clockMessage, "ClockMessageBox");
                if (Bounds(clockValue).IntersectsWith(Bounds(clockMessage)))
                    throw new Exception("FAIL: the clock-message picker overlaps the game-clock value");
                var quarter = (FrameworkElement)view.FindName("QuarterHost")!;
                var clockGap = Bounds(quarter).Left - Bounds(clockMessage).Right;
                if (clockGap < 16)
                    throw new Exception($"FAIL: the clock-message picker is only {clockGap:0}px from Quarter");
                var downValue = FindAll<TextBox>((DependencyObject)view.FindName("DownHost")!).First();
                var toGoValue = FindAll<TextBox>((DependencyObject)view.FindName("ToGoHost")!).First();
                var ballValue = FindAll<TextBox>((DependencyObject)view.FindName("BallOnHost")!).First();
                var downTop = Bounds(downValue).Top;
                if (Math.Abs(Bounds(toGoValue).Top - downTop) > 0.5 || Math.Abs(Bounds(ballValue).Top - downTop) > 0.5)
                    throw new Exception("FAIL: the Down, To Go and Ball On value rows are not aligned");
            }
            AssertFootballCardFits();
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
            AssertFootballCardFits();
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
            if (profileShell.Engine.State.HomeTimeouts != 3)
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
            if (savedProfile.Timeouts != 3 || !savedProfile.ScoringOptions.SequenceEqual([2, 3, 7]))
                throw new Exception($"FAIL: the profile dialog saved {savedProfile.Timeouts} timeouts and {string.Join('/', savedProfile.ScoringOptions)} scoring");
            profileDialog.Close();
            Console.WriteLine("PASS: saving from the sport profile window reaches the running catalog immediately");

            // Icons: one bar per timeout, and a possession dot that follows the team with the ball.
            var iconEngine = new GameEngine();
            iconEngine.NewGame("football");
            var barsItem = new ScorebugElement { Name = "Timeouts", Content = "", Kind = ScorebugElementKind.Text, Icon = ScorebugIcon.TimeoutBars, Width = 160, Height = 60 };
            var iconsView = new ScorebugIconView(barsItem, () => iconEngine.State);
            var barCount = FindAll<System.Windows.Shapes.Rectangle>(iconsView).Count();
            if (barCount != iconEngine.State.HomeTimeoutLimit)
                throw new Exception($"FAIL: timeout icon drew {barCount} bars for {iconEngine.State.HomeTimeoutLimit} timeouts");
            foreach (var line in FindAll<System.Windows.Shapes.Rectangle>(iconsView))
                if (line.Width <= line.Height * 2)
                    throw new Exception($"FAIL: timeout bar is not a horizontal line ({line.Width:0}x{line.Height:0})");
            var barStack = FindAll<StackPanel>(iconsView).FirstOrDefault()
                ?? throw new Exception("FAIL: timeout lines have no stack to sit in");
            if (barStack.Orientation != Orientation.Horizontal)
                throw new Exception("FAIL: timeout lines are not in a row");
            iconEngine.Timeout(true);
            iconsView.Update();
            if (FindAll<System.Windows.Shapes.Rectangle>(iconsView).Count() != iconEngine.State.HomeTimeoutLimit)
                throw new Exception("FAIL: a used timeout should keep its bar, greyed out, for the full allotment");
            var usedBar = FindAll<System.Windows.Shapes.Rectangle>(iconsView).Last();
            if (usedBar.Fill is not SolidColorBrush usedBrush || usedBrush.Color != Color.FromRgb(0x9A, 0xA5, 0xB2))
                throw new Exception("FAIL: a used timeout bar did not switch to the light-grey out colour");
            barsItem.IconVertical = true;
            iconsView.Update();
            var verticalStack = FindAll<StackPanel>(iconsView).FirstOrDefault();
            if (verticalStack?.Orientation != Orientation.Vertical)
                throw new Exception("FAIL: the vertical timeout option did not stack the bars");
            barsItem.IconVertical = false;
            var iconName = new IconNameConverter().Convert(ScorebugIcon.PossessionDot, typeof(string), null, CultureInfo.InvariantCulture) as string;
            if (iconName != "Possession Dot")
                throw new Exception($"FAIL: the icon picker still shows compact enum names ('{iconName}')");
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
            // A layout rebuild creates a fresh arrow view. Its first paint must be a still so the
            // mark does not replay its entrance just because the operator changed layouts.
            var arrowEngine = new GameEngine();
            arrowEngine.NewGame("football");
            arrowEngine.SetPossession(true);
            var arrowItem = new ScorebugElement
            {
                Name = "Possession arrow", Kind = ScorebugElementKind.Text, Width = 80, Height = 40,
                Icon = ScorebugIcon.PossessionArrow, FontSize = 26,
                Transition = ScorebugTransition.WheelUp, TransitionSeconds = 0.6
            };
            var arrowView = new ScorebugIconView(arrowItem, () => arrowEngine.State);
            var arrowMark = FindAll<System.Windows.Shapes.Polygon>(arrowView).Single();
            if (arrowMark.Opacity < 0.999 || arrowMark.RenderTransform is TranslateTransform)
                throw new Exception("FAIL: the possession arrow plays an entrance on its first paint");
            arrowEngine.SetPossession(false);
            arrowView.Update();
            if (arrowMark.RenderTransform is not TranslateTransform)
                throw new Exception("FAIL: the possession arrow stopped animating when possession actually changed");
            Console.WriteLine("PASS: timeout bars and the possession dot follow live data, and the dot can be flipped");
            Console.WriteLine("PASS: the possession arrow paints its first frame still and only animates a real possession change");

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
            var studioIcon = ((ComboBox)iconStudio.FindName("IconInput")!).SelectedItem;
            if (studioIcon is not (ScorebugIcon.TimeoutBars or ScorebugIcon.PossessionDot))
                throw new Exception("FAIL: the studio did not show the selected element's icon");
            if (((FrameworkElement)iconStudio.FindName("IconCustomInput")!).Visibility != Visibility.Collapsed)
                throw new Exception("FAIL: the made-icon picker appears on a built-in icon");
            var verticalVisibility = ((FrameworkElement)iconStudio.FindName("IconVerticalInput")!).Visibility;
            var expectedVerticalVisibility = studioIcon is ScorebugIcon.TimeoutBars ? Visibility.Visible : Visibility.Collapsed;
            if (verticalVisibility != expectedVerticalVisibility)
                throw new Exception($"FAIL: the vertical-bar property visibility is {verticalVisibility} for {studioIcon}");
            var iconColourSwatch = (Border)iconStudio.FindName("IconColorSwatch")!;
            if (!iconColourSwatch.IsVisible || iconColourSwatch.ActualWidth < 20)
                throw new Exception("FAIL: the studio's icon colour row has no visible swatch");
            ((TextBox)iconStudio.FindName("IconColorInput")!).Text = "#FFFF0000";
            iconStudio.UpdateLayout();
            if (iconColourSwatch.Background is not SolidColorBrush iconColourBrush || iconColourBrush.Color != Colors.Red)
                throw new Exception($"FAIL: the studio's icon colour swatch did not follow the typed colour (found={iconColourSwatch.Background})");
            iconStudio.Close();
            Console.WriteLine("PASS: icon elements draw on the scorebug canvas and edit from the studio");

            // The window is named Icon Creator, and the optional state label is separated from the
            // condition that actually decides when a design applies.
            {
                var creator = new IconMakerWindow("Dark");
                creator.Show();
                creator.UpdateLayout();
                if (creator.Title != "ScorePlus — Icon Creator")
                    throw new Exception($"FAIL: the icon window is still titled {creator.Title}");
                if (!FindAll<TextBlock>(creator).Any(text => text.Text == "ICON CREATOR"))
                    throw new Exception("FAIL: the icon window heading is not ICON CREATOR");
                if (!FindAll<TextBlock>(creator).Any(text => text.Text == "STATE NAME (OPTIONAL)")
                    || !FindAll<TextBlock>(creator).Any(text => text.Text == "WHEN THIS DATA IS TRUE"))
                    throw new Exception("FAIL: the state name and its condition still share one heading");
                if (!FindAll<Button>(creator).Any(button => (button.Content as string) == "Import .spi…")
                    || !FindAll<Button>(creator).Any(button => (button.Content as string) == "Export .spi…"))
                    throw new Exception("FAIL: the Icon Creator does not offer .spi import and export");
                var colourInput = FindAll<TextBox>(creator).FirstOrDefault(box => box.Name == "IconColor");
                var colourSwatch = FindAll<Border>(creator).FirstOrDefault(border => border.Name == "IconColorSwatch");
                if (colourInput is null || colourSwatch is null)
                    throw new Exception("FAIL: the Icon Creator has no colour thumbnail beside its colour field");
                colourInput.Text = "#FFFF0000";
                creator.UpdateLayout();
                if (colourSwatch.Background is not SolidColorBrush colourBrush || colourBrush.Color != Colors.Red)
                    throw new Exception($"FAIL: the Icon Creator colour thumbnail did not follow the typed colour (found={colourSwatch.Background})");
                creator.Close();
                Console.WriteLine("PASS: the Icon Creator labels state names separately and offers .spi import and export");
            }

            // A made icon must stay selected after the picker writes it, and a wheel over a
            // closed drop-down must not silently move to the next value.
            {
                var madeIconPath = Path.Combine(appDataPath, $"made-icons-{Guid.NewGuid():N}.json");
                CustomIconStore.UsePath(madeIconPath);
                var made = CustomIconStore.Save(new CustomIcon
                {
                    Name = "Smoke crest", Glyph = "★", Color = "#FFFFFFFF"
                });
                var madeCatalog = new ScorebugCatalog(Path.Combine(appDataPath, $"made-{Guid.NewGuid():N}.json"));
                madeCatalog.Update(layout =>
                {
                    layout.Elements.Clear();
                    layout.Elements.Add(new ScorebugElement
                    {
                        Name = "Icon target", Kind = ScorebugElementKind.Text, Content = "",
                        Icon = ScorebugIcon.None
                    });
                });
                var madeStudio = new ScorebugEditorWindow(madeCatalog, iconEngine, null, "Dark");
                madeStudio.Show();
                madeStudio.UpdateLayout();
                ((ListBox)madeStudio.FindName("Layers")!).SelectedIndex = 0;
                madeStudio.UpdateLayout();
                var iconType = (ComboBox)madeStudio.FindName("IconInput")!;
                iconType.SelectedItem = ScorebugIcon.Custom;
                madeStudio.UpdateLayout();
                var madeChoice = (ComboBox)madeStudio.FindName("IconCustomInput")!;
                if (madeChoice.Visibility != Visibility.Visible)
                    throw new Exception("FAIL: the Made icon picker stayed hidden after choosing Custom");
                var choice = madeChoice.Items.Cast<object>().FirstOrDefault(item => item.ToString() == made.Name);
                if (choice is null)
                    throw new Exception("FAIL: the Made icon picker does not list the saved icon");
                madeChoice.SelectedItem = choice;
                madeStudio.UpdateLayout();
                var target = madeCatalog.Layout.Elements[0];
                if (target.Icon != ScorebugIcon.Custom || !string.Equals(target.CustomIconId, made.Id, StringComparison.Ordinal))
                    throw new Exception($"FAIL: selecting a made icon saved {target.Icon}/{target.CustomIconId}");
                if (madeChoice.SelectedItem is null)
                    throw new Exception("FAIL: the Made icon picker went blank after choosing an icon");
                var contentSite = madeChoice.Template.FindName("ContentSite", madeChoice) as ContentPresenter;
                if (contentSite?.Content?.ToString() != made.Name)
                    throw new Exception("FAIL: the Made icon picker does not show the chosen icon's name");
                var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent
                };
                iconType.RaiseEvent(wheel);
                if (!wheel.Handled || !Equals(iconType.SelectedItem, ScorebugIcon.Custom))
                    throw new Exception("FAIL: a mouse wheel over a closed drop-down changed its value");
                madeStudio.Close();
                CustomIconStore.UsePath(null);
                Console.WriteLine("PASS: a made icon stays selected and a closed drop-down ignores the mouse wheel");
            }

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

            // A detached panel keeps the synced chain alive even though its production header — the
            // row with Transition Builder and Multiview — is deliberately absent.
            var popOutRequests = 0;
            var chainPanel = new GameControlWindow(engine, () => { }, _ => { }, () => true);
            chainPanel.NewPanelRequested += () => popOutRequests++;
            chainPanel.Show();
            chainPanel.UpdateLayout();
            if (FindAll<Button>(chainPanel).Any(b => (b.Content as string) is "Transition Builder" or "Animation Maker" or "Multiview" or "Display Studio" or "Save Game" or "Open Game"))
                throw new Exception("FAIL: a detached panel still carries production-header buttons");
            chainPanel.RequestNewPanel();
            if (popOutRequests != 1) throw new Exception($"FAIL: the detached panel's synced chain raised the request {popOutRequests} times");
            chainPanel.Close();

            // A real detached panel is the main operator body, not a second console layout. It carries
            // the same preview rail, four desks and audience monitor, but none of the production header
            // actions that belong to the main window only.
            var mirrorOwner = new MainWindow(LaunchMode.New) { Width = 1440, Height = 900 };
            mirrorOwner.Show();
            mirrorOwner.UpdateLayout();
            mirrorOwner.OpenControlPanel();
            mirrorOwner.UpdateLayout();
            var mirror = Application.Current.Windows.OfType<GameControlWindow>().LastOrDefault()
                ?? throw new Exception("FAIL: the main window opened no detached panel");
            mirror.UpdateLayout();
            foreach (var required in new[] { "PreviewRail", "DisplaySelector", "PreviewHost", "AudienceHost",
                         "QuickMediaHost", "QuickLayoutsHost", "ControlTab", "MediaTab", "RosterTab", "MacrosTab",
                         "PanelHost", "MediaControl", "RosterControl", "MacroHost", "SoundMeterSegments" })
                if (mirror.FindName(required) is null)
                    throw new Exception($"FAIL: the detached panel is missing the main body's {required}");
            if (mirror.FindName("PreviewRail") is Border rail && rail.Visibility != Visibility.Visible)
                throw new Exception("FAIL: the detached panel hid its program preview rail");
            if (FindAll<Button>(mirror).Any(b => (b.Content as string) is "Transition Builder" or "Animation Maker"
                    or "Multiview" or "Display Studio" or "Save Game" or "Open Game"))
                throw new Exception("FAIL: the detached panel copied the main window's production header");
            mirror.Close();
            mirrorOwner.Close();
            Console.WriteLine("PASS: a detached panel mirrors the main operator body with no production header, and the synced chain still works");

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
                if (playback.ActiveFor("")?.Path != ad.Path) throw new Exception("FAIL: an audio cue cleared the video element");
                playback.Start(highlight);
                if (playback.ActiveFor("Ads")?.Path != ad.Path) throw new Exception("FAIL: a different tag replaced the ad box");
                if (playback.ActiveFor("Highlights")?.Path != highlight.Path) throw new Exception("FAIL: the new tag did not reach its own video element");
                var sameFileNewTag = new MediaEntry(ad.Path, "Highlights");
                playback.Start(sameFileNewTag);
                if (playback.ActiveFor("Ads")?.Path != ad.Path || playback.ActiveFor("Highlights")?.Path != ad.Path)
                    throw new Exception("FAIL: the same file under another tag replaced its other tag");
                playback.Stop(sameFileNewTag);
                if (playback.ActiveFor("Ads")?.Path != ad.Path)
                    throw new Exception("FAIL: stopping one tag stopped the same file under another tag");
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

                // Cameras route by tag: a new camera replaces the one already on air under the same
                // tag, while cameras carrying other tags remain live. This is what lets a wide and
                // a tight camera feed separate scorebug boxes, without two cameras fighting over
                // one box.
                var cameraCue = new MediaPlayback();
                var wideCamera = new MediaEntry(MediaLibrary.CameraPath(0), "Camera");
                var benchCamera = new MediaEntry(MediaLibrary.CameraPath(1), "Bench");
                var tightCamera = new MediaEntry(MediaLibrary.CameraPath(2), "camera");
                var afterCameraCue = MediaLibrary.CueCamera([wideCamera, benchCamera], tightCamera);
                if (afterCameraCue.Count != 2
                    || afterCameraCue.Any(camera => camera.Path == wideCamera.Path)
                    || !afterCameraCue.Any(camera => camera.Path == benchCamera.Path)
                    || !afterCameraCue.Any(camera => camera.Path == tightCamera.Path))
                    throw new Exception("FAIL: a same-tag camera did not replace the previous camera while keeping another tag live");
                cameraCue.SetLiveCameras(afterCameraCue);
                if (cameraCue.ActiveFor("Camera")?.Path != tightCamera.Path
                    || cameraCue.ActiveFor("Bench")?.Path != benchCamera.Path)
                    throw new Exception("FAIL: the tag-matched cameras did not route to their own boxes");
                // A caller handing in a flat list must get the same replacement, not two cameras
                // fighting over the first matching tag.
                cameraCue.SetLiveCameras([wideCamera, benchCamera, tightCamera]);
                if (cameraCue.LiveCameras.Count != 2
                    || cameraCue.LiveCameras.Count(camera => camera.Tag.Equals("Camera", StringComparison.OrdinalIgnoreCase)) != 1
                    || cameraCue.ActiveFor("Camera")?.Path != tightCamera.Path)
                    throw new Exception("FAIL: the playback list kept two cameras on the same tag");
                Console.WriteLine("PASS: a camera replaces only its own tag; different tags stay on air together");

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

            // Clear takes the media side off air without deleting the library.
            var clearSession = new MediaSession(new UserPreferences());
            try
            {
                var clearEntry = new MediaEntry(Path.Combine(Path.GetTempPath(), "smoke-clear.mp4"), "Ads");
                clearSession.Library.Add(clearEntry);
                clearSession.Playlists.Add(new MediaPlaylist { Name = "Clear test", Paths = [clearEntry.Path] });
                clearSession.Playback.Start(clearEntry);
                clearSession.ClearOnAir();
                if (clearSession.Library.Count != 1 || clearSession.Playlists.Count != 1
                    || clearSession.Playback.NowPlaying is not null || clearSession.Current is not null)
                    throw new Exception("FAIL: Clear deleted library media or left something on air");
                // Replay opens frozen at the live edge and waits for Start replay.
                if (clearSession.IsReplaying || clearSession.PlayReplay())
                    throw new Exception("FAIL: Start replay ran without an open replay");
            }
            finally { clearSession.Dispose(); }
            Console.WriteLine("PASS: Clear takes media off air but keeps the library, and Start replay refuses with nothing open");

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

            // The audience window can be opened with its annotation tools already showing, and the
            // toolbar never edits the underlying graphic. The node runtime is checked by applying a
            // temporary colour and switching a layout without touching the saved artwork.
            var annotateCatalog = new ScorebugCatalog(Path.Combine(appDataPath, $"smoke-annotation-{Guid.NewGuid():N}.json"));
            var annotateEngine = new GameEngine();
            annotateEngine.NewGame("football");
            var annotateOutput = new OutputWindow(annotateEngine, annotateCatalog, "Dark", annotations: true);
            annotateOutput.Show();
            annotateOutput.UpdateLayout();
            if (!annotateOutput.AnnotationToolsOpen || annotateOutput.FindName("ScorebugHost") is null)
                throw new Exception("FAIL: the audience annotation toolbar did not open");
            if (!annotateOutput.Title.StartsWith("Operator Draw Window", StringComparison.Ordinal))
                throw new Exception($"FAIL: the draw window is titled {annotateOutput.Title}");
            var widthBox = FindAll<TextBox>(annotateOutput).FirstOrDefault(box =>
                (box.ToolTip as string ?? "").StartsWith("Pen width", StringComparison.Ordinal))
                ?? throw new Exception("FAIL: the annotation toolbar has no typeable pen-width box");
            widthBox.Text = "9";
            annotateOutput.UpdateLayout();
            if (Math.Abs(annotateOutput.AnnotationWidth - 9) > 0.001)
                throw new Exception($"FAIL: typing a pen width produced {annotateOutput.AnnotationWidth}");
            var fadePicker = FindAll<ComboBox>(annotateOutput).FirstOrDefault(box =>
                (box.ToolTip as string ?? "").Contains("fades", StringComparison.Ordinal))
                ?? throw new Exception("FAIL: the annotation toolbar has no fade-time picker");
            if ((fadePicker.SelectedItem as string) != "1 s")
                throw new Exception($"FAIL: the default annotation fade is {fadePicker.SelectedItem}, not 1 s");
            var ink = FindAll<InkCanvas>(annotateOutput).SingleOrDefault()
                ?? throw new Exception("FAIL: the draw window has no ink surface");
            void AddInkStroke() => ink.Strokes.Add(new Stroke(new StylusPointCollection
            {
                new StylusPoint(10, 10), new StylusPoint(100, 100)
            }));
            AddInkStroke();
            Pump(TimeSpan.FromMilliseconds(1400));
            if (ink.Strokes.Count != 0)
                throw new Exception("FAIL: the default 1 s ink hold did not fade the stroke away");
            fadePicker.SelectedItem = "Permanent";
            AddInkStroke();
            Pump(TimeSpan.FromMilliseconds(1200));
            if (ink.Strokes.Count != 1)
                throw new Exception("FAIL: permanent ink was cleared by the fade timer");
            fadePicker.SelectedItem = "0.5 s";
            Pump(TimeSpan.FromMilliseconds(900));
            if (ink.Strokes.Count != 0)
                throw new Exception("FAIL: the 0.5 s ink hold did not fade the stroke away");
            var savedAnnotation = PreferencesStore.Load();
            if (Math.Abs(savedAnnotation.AnnotationWidth - 9) > 0.001
                || Math.Abs(savedAnnotation.AnnotationFadeSeconds - 0.5) > 0.001)
                throw new Exception($"FAIL: annotation settings did not save ({savedAnnotation.AnnotationWidth}, {savedAnnotation.AnnotationFadeSeconds})");
            annotateOutput.CloseAnnotationTools();
            if (annotateOutput.AnnotationToolsOpen
                || !annotateOutput.Title.StartsWith("ScorePlus —", StringComparison.Ordinal))
                throw new Exception("FAIL: Done did not close the annotation toolbar and restore the audience title");
            annotateOutput.Close();

            // The pen must be on the real audience window, not a second duplicate window. Opening
            // annotation for a display that already has output should reuse that output.
            var annotationMain = new MainWindow(LaunchMode.New) { Width = 1280, Height = 800 };
            annotationMain.Show();
            annotationMain.UpdateLayout();
            var audienceWindow = annotationMain.OpenOutputWindow();
            var secondAudience = annotationMain.OpenOutputWindow();
            var drawWindow = annotationMain.OpenOutputWindow(annotations: true);
            if (audienceWindow is null || secondAudience is null || !ReferenceEquals(audienceWindow, drawWindow)
                || drawWindow is null || !drawWindow.AnnotationToolsOpen
                || !drawWindow.Title.StartsWith("Operator Draw Window", StringComparison.Ordinal))
                throw new Exception("FAIL: annotation opened a duplicate instead of putting the pen on the audience window");
            if (secondAudience.AnnotationToolsOpen)
                throw new Exception("FAIL: the second audience window received the operator toolbar");
            var drawInk = FindAll<InkCanvas>(drawWindow).Single();
            var audienceInk = FindAll<InkCanvas>(secondAudience).Single();
            drawInk.Strokes.Add(new Stroke(new StylusPointCollection
            {
                new StylusPoint(12, 12), new StylusPoint(120, 80)
            }));
            Pump(TimeSpan.FromMilliseconds(80));
            if (audienceInk.Strokes.Count != 1)
                throw new Exception("FAIL: the pen did not reach the second audience window");
            annotationMain.Close();
            Pump(TimeSpan.FromMilliseconds(250));

            // The in-progress stroke belongs to the shared collection from the first point, and the
            // fade hold is held back until the stroke is released. This is the exact ordering the
            // operator sees when drawing on one output and watching another.
            var liveBoard = new AnnotationBoard(TimeSpan.FromSeconds(0.5));
            var livePath = Path.Combine(appDataPath, $"smoke-live-ink-{Guid.NewGuid():N}.json");
            var inkEngine = new GameEngine();
            inkEngine.NewGame("football");
            try
            {
                var liveCatalog = new ScorebugCatalog(livePath);
                var liveOperator = new OutputWindow(inkEngine, liveCatalog, "Dark", annotations: true, annotationBoard: liveBoard);
                var liveAudience = new OutputWindow(inkEngine, liveCatalog, "Dark", annotationBoard: liveBoard);
                liveOperator.Show();
                liveAudience.Show();
                liveAudience.ShowAudienceInk();
                liveOperator.UpdateLayout();
                liveAudience.UpdateLayout();
                var liveAudienceInk = FindAll<InkCanvas>(liveAudience).Single();
                var liveStroke = new Stroke(new StylusPointCollection { new StylusPoint(10, 10) });
                liveBoard.BeginStroke();
                liveBoard.Strokes.Add(liveStroke);
                Pump(TimeSpan.FromMilliseconds(750));
                if (liveBoard.Strokes.Count != 1)
                    throw new Exception("FAIL: ink started fading before the operator released the pen");
                liveStroke.StylusPoints.Add(new StylusPoint(100, 100));
                Pump(TimeSpan.FromMilliseconds(60));
                if (liveAudienceInk.Strokes.Count != 1 || liveAudienceInk.Strokes[0].StylusPoints.Count != 2)
                    throw new Exception("FAIL: the audience did not receive the in-progress stroke points");
                liveBoard.EndStroke();
                Pump(TimeSpan.FromMilliseconds(850));
                if (liveBoard.Strokes.Count != 0)
                    throw new Exception("FAIL: the fade hold did not start after the stroke finished");
                liveOperator.Close();
                liveAudience.Close();
                Console.WriteLine("PASS: live ink reaches the audience during the stroke, and its fade starts on release");
            }
            finally { if (File.Exists(livePath)) File.Delete(livePath); }

            // The pen draws in the scorebug's design space, so its surface sits exactly on the
            // artwork. On a tall, vertical monitor the graphic is letterboxed; a window-sized ink
            // canvas put the pen in the black bars above it.
            var alignPath = Path.Combine(appDataPath, $"smoke-annotation-align-{Guid.NewGuid():N}.json");
            try
            {
                var alignCatalog = new ScorebugCatalog(alignPath);
                var alignEngine = new GameEngine();
                alignEngine.NewGame("football");
                var alignOutput = new OutputWindow(alignEngine, alignCatalog, "Dark", annotations: true)
                {
                    Width = 760, Height = 1100
                };
                alignOutput.Show();
                alignOutput.UpdateLayout();
                Pump(TimeSpan.FromMilliseconds(80));
                alignOutput.UpdateLayout();
                var alignRoot = (FrameworkElement)alignOutput.Content;
                var alignInk = FindAll<InkCanvas>(alignOutput).Single();
                var alignView = FindAll<ScorebugCanvasView>(alignOutput).Single();
                var alignViewbox = alignView.Children.OfType<Viewbox>().Single();
                var alignSurface = (FrameworkElement)alignInk.Parent;
                if (Math.Abs(alignOutput.AnnotationSurfaceSize.Width - alignCatalog.Layout.Width) > 0.5
                    || Math.Abs(alignOutput.AnnotationSurfaceSize.Height - alignCatalog.Layout.Height) > 0.5)
                    throw new Exception($"FAIL: the pen surface is {alignOutput.AnnotationSurfaceSize}, not the layout's design size");
                var alignScale = Math.Min(alignViewbox.ActualWidth / alignSurface.Width,
                    alignViewbox.ActualHeight / alignSurface.Height);
                var drawn = new Rect((alignViewbox.ActualWidth - alignSurface.Width * alignScale) / 2,
                    (alignViewbox.ActualHeight - alignSurface.Height * alignScale) / 2,
                    alignSurface.Width * alignScale, alignSurface.Height * alignScale);
                var graphicRect = alignViewbox.TransformToAncestor(alignRoot).TransformBounds(drawn);
                var inkRect = alignInk.TransformToAncestor(alignRoot).TransformBounds(
                    new Rect(0, 0, alignInk.ActualWidth, alignInk.ActualHeight));
                if (Math.Abs(graphicRect.Left - inkRect.Left) > 1 || Math.Abs(graphicRect.Top - inkRect.Top) > 1
                    || Math.Abs(graphicRect.Width - inkRect.Width) > 1 || Math.Abs(graphicRect.Height - inkRect.Height) > 1)
                    throw new Exception($"FAIL: the pen surface {inkRect} does not sit on the graphic {graphicRect}");
                if (inkRect.Top <= 0 || inkRect.Bottom >= alignRoot.ActualHeight - 1)
                    throw new Exception("FAIL: the pen surface fills the window instead of the letterboxed graphic");
                alignOutput.Close();
                Console.WriteLine("PASS: the pen surface follows the scorebug onto a tall, letterboxed window");
            }
            finally { if (File.Exists(alignPath)) File.Delete(alignPath); }

            // A bypass rule follows the live game and patches the output without publishing; the same
            // rule with the switch off waits for the published copy, as it always did.
            var bypassPath = Path.Combine(appDataPath, $"smoke-bypass-{Guid.NewGuid():N}.json");
            try
            {
                var bypassCatalog = new ScorebugCatalog(bypassPath);
                var bypassDisplay = bypassCatalog.ActiveDisplay;
                var bypassTarget = bypassCatalog.Layout.Elements.First(element => element.Name == "Home team");
                const string bypassColor = "#FF12AB34";
                var onAir = new GameEngine();
                onAir.NewGame("football");
                onAir.SetPlayClock(9);
                var working = new GameEngine();
                working.NewGame("football");
                working.SetPlayClock(2);
                var bypassRule = new AutomationRule { Name = "Live flag", BypassOutputUpdate = true };
                bypassRule.Conditions.Add(new AutomationCondition { Field = AutomationField.PlayClock, Operator = AutomationOperator.Less, Value = 5 });
                bypassRule.Actions.Add(new AutomationAction { Kind = AutomationActionKind.SetTextColor, LayoutId = bypassDisplay.Layouts[0].Id, ElementId = bypassTarget.Id, Value = bypassColor });
                bypassCatalog.UpdateAutomations(bypassDisplay.Id, [bypassRule]);
                using var bypassRuntime = new AutomationRuntime(bypassCatalog, onAir, working);
                ScorebugLayout LiveLayout() => bypassRuntime.Effective(bypassDisplay.Id, bypassCatalog.PublishedCompositeOf(bypassDisplay.Id));
                if (LiveLayout().Elements.First(element => element.Id == bypassTarget.Id).Foreground != bypassColor)
                    throw new Exception("FAIL: the bypass rule did not use the live game state");
                bypassRule.BypassOutputUpdate = false;
                bypassCatalog.UpdateAutomations(bypassDisplay.Id, [bypassRule]);
                if (LiveLayout().Elements.First(element => element.Id == bypassTarget.Id).Foreground == bypassColor)
                    throw new Exception("FAIL: a non-bypass rule applied without Update Output");
                bypassRule.BypassOutputUpdate = true;
                bypassCatalog.UpdateAutomations(bypassDisplay.Id, [bypassRule]);
                working.SetPlayClock(8);
                if (LiveLayout().Elements.First(element => element.Id == bypassTarget.Id).Foreground == bypassColor)
                    throw new Exception("FAIL: the bypass rule ignored its live condition");
                Console.WriteLine("PASS: a bypass rule applies from live state, while an ordinary rule still waits for Update Output");
            }
            finally { if (File.Exists(bypassPath)) File.Delete(bypassPath); }

            // A rule can hold after it matches (wait until), then run after-wait actions, with an
            // else branch while it is not matching. This is the flag-down layout round trip.
            var flowPath = Path.Combine(appDataPath, $"smoke-flow-{Guid.NewGuid():N}.json");
            try
            {
                var flowCatalog = new ScorebugCatalog(flowPath);
                var flowDisplay = flowCatalog.ActiveDisplay;
                var flowEngine = new GameEngine();
                flowEngine.NewGame("football");
                var flagIndex = flowCatalog.AddLayout("Flag layout");
                var normalId = flowCatalog.Layouts[0].Id;
                const string elseColor = "#FF445566";
                var elseTarget = flowCatalog.Layouts[0].Elements.First(element => element.Name == "Home team");
                var flowRule = new AutomationRule { Name = "Flag layout round trip" };
                flowRule.Conditions.Add(new AutomationCondition { Field = AutomationField.PenaltyActive, Operator = AutomationOperator.IsTrue });
                flowRule.Conditions.Add(new AutomationCondition { Field = AutomationField.PenaltyActive, Operator = AutomationOperator.IsFalse, WaitUntil = true });
                flowRule.Actions.Add(new AutomationAction { Kind = AutomationActionKind.SwitchLayout, LayoutId = flowCatalog.Layouts[flagIndex].Id, Phase = AutomationActionPhase.OnMatch });
                flowRule.Actions.Add(new AutomationAction { Kind = AutomationActionKind.SwitchLayout, LayoutId = normalId, Phase = AutomationActionPhase.AfterWait });
                flowRule.Actions.Add(new AutomationAction { Kind = AutomationActionKind.SetTextColor, Phase = AutomationActionPhase.Else, ElementId = elseTarget.Id, Value = elseColor });
                flowCatalog.UpdateAutomations(flowDisplay.Id, [flowRule]);
                using var flowRuntime = new AutomationRuntime(flowCatalog, flowEngine);
                ScorebugLayout ProgramLayout() => flowRuntime.Effective(flowDisplay.Id, flowCatalog.Layouts[0]);
                if (flowCatalog.PublishedIndex != 0)
                    throw new Exception("FAIL: the wait rule switched layouts before its trigger matched");
                if (ProgramLayout().Elements.First(element => element.Id == elseTarget.Id).Foreground != elseColor)
                    throw new Exception("FAIL: the else action did not apply while the rule was not matching");
                flowEngine.SetPenalty(true, "Holding", true);
                if (flowCatalog.PublishedIndex != flagIndex)
                    throw new Exception($"FAIL: the flag rule did not switch to the flag layout (published {flowCatalog.PublishedIndex})");
                if (ProgramLayout().Elements.First(element => element.Id == elseTarget.Id).Foreground == elseColor)
                    throw new Exception("FAIL: the else action kept applying while the flag rule was matching");
                if (!flowRuntime.IsRuleActive(flowDisplay.Id, flowRule.Id))
                    throw new Exception("FAIL: the flag rule is not reported active while it waits");
                flowEngine.SetPenalty(false, "Holding", true);
                if (flowCatalog.PublishedIndex != 0)
                    throw new Exception($"FAIL: the wait-until step did not switch back to the program layout (published {flowCatalog.PublishedIndex})");
                flowEngine.SetPenalty(true, "Holding", true);
                if (flowCatalog.PublishedIndex != flagIndex)
                    throw new Exception("FAIL: the flag rule did not fire again after it returned to its start");
                flowEngine.SetPenalty(false, "Holding", true);
                Console.WriteLine("PASS: a rule can wait until a condition changes and switch layouts on match, after the wait, and else");
            }
            finally { if (File.Exists(flowPath)) File.Delete(flowPath); }

            var rulePath = Path.Combine(appDataPath, $"smoke-rules-{Guid.NewGuid():N}.json");
            var ruleCatalog = new ScorebugCatalog(rulePath);
            var ruleEngine = new GameEngine();
            ruleEngine.NewGame("football");
            ruleEngine.SetPlayClock(3);
            var ruleDisplay = ruleCatalog.ActiveDisplay;
            var ruleTarget = ruleCatalog.Layout.Elements.First(element => element.Name == "Home team");
            var rule = new AutomationRule { Name = "Smoke rule" };
            rule.Conditions.Add(new AutomationCondition { Field = AutomationField.PlayClock, Operator = AutomationOperator.Less, Value = 5 });
            rule.Actions.Add(new AutomationAction { Kind = AutomationActionKind.SetTextColor, LayoutId = ruleDisplay.Layouts[0].Id, ElementId = ruleTarget.Id, Value = "#FFFF3B30" });
            ruleCatalog.UpdateAutomations(ruleDisplay.Id, [rule]);
            using (var runtime = new AutomationRuntime(ruleCatalog, ruleEngine))
            {
                var effective = runtime.Effective(ruleDisplay.Id, ruleCatalog.PublishedCompositeOf(ruleDisplay.Id));
                var changed = effective.Elements.First(element => element.Id == ruleTarget.Id);
                if (changed.Foreground != "#FFFF3B30" || ruleTarget.Foreground == "#FFFF3B30")
                    throw new Exception("FAIL: a node rule did not recolour the temporary layout without changing the design");
                var second = ruleCatalog.AddLayout("Automated layout");
                var switchRule = new AutomationRule { Name = "Switch" };
                switchRule.Conditions.Add(new AutomationCondition { Field = AutomationField.PlayClock, Operator = AutomationOperator.Less, Value = 5 });
                switchRule.Actions.Add(new AutomationAction { Kind = AutomationActionKind.SwitchLayout, LayoutId = ruleCatalog.Layouts[second].Id });
                ruleCatalog.UpdateAutomations(ruleDisplay.Id, [rule, switchRule]);
                if (ruleCatalog.PublishedIndex != second)
                    throw new Exception($"FAIL: a node rule did not switch layouts (published {ruleCatalog.PublishedIndex}, wanted {second})");
                // The node editor is a real window with trigger, condition and action cards. Open it
                // on the same graph and make sure its add-rule path persists another rule.
                var nodeWindow = new AutomationWindow(ruleCatalog, ruleEngine, "Dark", ruleDisplay.Id, runtime);
                nodeWindow.Show();
                nodeWindow.UpdateLayout();
                if (nodeWindow.Title != "Conditionals" || !FindAll<TextBlock>(nodeWindow).Any(text => text.Text == "CONDITIONALS"))
                    throw new Exception($"FAIL: the node editor is still titled {nodeWindow.Title}");
                if (nodeWindow.Rules.Count != 2 || !FindAll<TextBlock>(nodeWindow).Any(text => text.Text == "TRIGGER")
                    || !FindAll<TextBlock>(nodeWindow).Any(text => text.Text == "CONDITION 1")
                    || !FindAll<TextBlock>(nodeWindow).Any(text => text.Text == "ACTION 1"))
                    throw new Exception("FAIL: the node editor did not draw its trigger, condition and action cards");
                var ruleCombos = FindAll<ComboBox>(nodeWindow).Where(combo => combo.SelectedItem is not null).ToArray();
                if (ruleCombos.Any(combo => combo.SelectedItem.ToString()?.Contains("Choice", StringComparison.Ordinal) == true))
                    throw new Exception("FAIL: a node dropdown is showing its CLR type name");
                if (!ruleCombos.Any(combo => combo.SelectedItem.ToString() == "Play clock"))
                    throw new Exception("FAIL: the node field dropdown is not showing the field label");
                if (!FindAll<Button>(nodeWindow).Any(button => (button.Content as string) == "Export…")
                    || !FindAll<Button>(nodeWindow).Any(button => (button.Content as string) == "Import…"))
                    throw new Exception("FAIL: the node editor has no .spr export/import actions");
                var bypassBox = FindAll<CheckBox>(nodeWindow).FirstOrDefault(box => (box.Content as string) == "Bypass having to update output")
                    ?? throw new Exception("FAIL: the node editor has no Bypass having to update output option");
                bypassBox.IsChecked = true;
                nodeWindow.UpdateLayout();
                if (!nodeWindow.Rules.Any(rule => rule.BypassOutputUpdate))
                    throw new Exception("FAIL: the bypass checkbox did not reach the rule");
                nodeWindow.AddRule();
                nodeWindow.UpdateLayout();
                if (nodeWindow.Rules.Count != 3)
                    throw new Exception("FAIL: the node editor did not add a rule");
                nodeWindow.Close();
            }
            Console.WriteLine("PASS: annotation toolbar opens; conditionals recolor the live layout, switch layouts, and draw in the conditionals editor");

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
            freshGame.Engine.AddStat("Old custom value");
            freshGame.PublishOutput();
            freshGame.StartNewGame("football");
            if (freshGame.Engine.State.HomeScore != 0 || freshGame.Engine.State.AwayScore != 0 || freshGame.Engine.State.Down != 1
                || freshGame.Engine.State.Stats.Any(s => s.Name == "Old custom value")
                || !freshGame.Engine.State.Stats.Any(s => s.Name == "Field goal attempts"))
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

            // End Game has to show its confirmation and settle the result. This is a modal test, not
            // just a call to the engine: the reported bug was that the dialog never appeared.
            {
                var ending = new MainWindow(LaunchMode.New) { Width = 1280, Height = 800 };
                ending.Show();
                ending.UpdateLayout();
                ending.StartNewGame("football", announce: false);
                ending.Engine.SetTeamIdentity(true, "Restart Home", "RST", null, "#FF112233", "#FF445566", mascot: "Eagles", record: "2-0", ranking: "#7", divisionRecord: "1-0");
                ending.Engine.Score(true, 3);
                var restartTeam = ending.Engine.State.HomeTeam;
                var restartColor = ending.Engine.State.HomePrimaryColor;
                ending.UpdateLayout();
                var endButton = FindAll<Button>(ending).FirstOrDefault(button => (button.Content as string) == "End Game")
                    ?? throw new Exception("FAIL: the main window has no End Game button");
                var endTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                var sawEndConfirm = false;
                endTimer.Tick += (_, _) =>
                {
                    var confirm = Application.Current.Windows.OfType<ConfirmWindow>().FirstOrDefault();
                    if (confirm is null) return;
                    endTimer.Stop();
                    sawEndConfirm = true;
                    var finish = FindAll<Button>(confirm).FirstOrDefault(button => (button.Content as string) == "End game")
                        ?? throw new Exception("FAIL: the End Game confirmation has no End game action");
                    finish.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                endTimer.Start();
                endButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(TimeSpan.FromMilliseconds(250));
                if (!sawEndConfirm || !ending.Engine.State.GameEnded || !ending.Engine.State.RecordSettled)
                    throw new Exception("FAIL: End Game did not show its confirmation and settle the result");
                if ((endButton.Content as string) != "Restart Game")
                    throw new Exception($"FAIL: the final game still offers “{endButton.Content}” instead of Restart Game");
                if (ending.Engine.State.HomeRecord != "3-0")
                    throw new Exception($"FAIL: End Game did not settle the record before the restart ({ending.Engine.State.HomeRecord})");
                var sawRestartConfirm = false;
                var restartTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                restartTimer.Tick += (_, _) =>
                {
                    var confirm = Application.Current.Windows.OfType<ConfirmWindow>().FirstOrDefault();
                    if (confirm is null) return;
                    restartTimer.Stop();
                    sawRestartConfirm = true;
                    var restart = FindAll<Button>(confirm).FirstOrDefault(button => (button.Content as string) == "Restart game")
                        ?? throw new Exception("FAIL: the Restart Game confirmation has no Restart game action");
                    restart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                restartTimer.Start();
                endButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(TimeSpan.FromMilliseconds(250));
                if (!sawRestartConfirm || ending.Engine.State.GameEnded
                    || ending.Engine.State.HomeScore != 0 || ending.Engine.State.AwayScore != 0
                    || ending.Engine.State.Period != 1)
                    throw new Exception("FAIL: Restart Game did not clear the final and start a fresh game");
                if (ending.Engine.State.HomeTeam != restartTeam
                    || ending.Engine.State.HomePrimaryColor != restartColor
                    || ending.Engine.State.HomeRecord != "3-0")
                    throw new Exception("FAIL: Restart Game did not keep the team details and settled record");
                if ((endButton.Content as string) != "End Game")
                    throw new Exception("FAIL: the Restart Game button did not become End Game again");
                ending.Close();
                Pump(TimeSpan.FromMilliseconds(200));
                Console.WriteLine("PASS: End Game settles the result, then Restart Game resets the score and keeps the teams");
            }

            // A clock-expired game with automatic records off is final but unsettled. End Game must
            // still be the action that applies its tie, not a Restart Game that discards it.
            {
                var tieEnding = new MainWindow(LaunchMode.New) { Width = 1280, Height = 800 };
                tieEnding.Show();
                tieEnding.UpdateLayout();
                tieEnding.StartNewGame("football", announce: false);
                tieEnding.Engine.SetTeamIdentity(true, "Tie Home", "TIE", null, "#FF112233", "#FF445566", record: "5-2");
                tieEnding.Engine.SetTeamIdentity(false, "Tie Away", "TIEA", null, "#FF223344", "#FF556677", record: "5-2");
                tieEnding.Engine.Score(true, 7);
                tieEnding.Engine.Score(false, 7);
                tieEnding.Engine.State.GameEnded = true;
                tieEnding.Engine.State.ClockMessage = GameClockMessage.Final;
                var tieEndButton = FindAll<Button>(tieEnding).FirstOrDefault(button => (button.Content as string) == "End Game")
                    ?? throw new Exception("FAIL: an unsettled final no longer offers End Game");
                var tieTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                var sawTieConfirm = false;
                tieTimer.Tick += (_, _) =>
                {
                    var confirm = Application.Current.Windows.OfType<ConfirmWindow>().FirstOrDefault();
                    if (confirm is null) return;
                    tieTimer.Stop();
                    sawTieConfirm = true;
                    var finish = FindAll<Button>(confirm).FirstOrDefault(button => (button.Content as string) == "End game")
                        ?? throw new Exception("FAIL: the tie End Game confirmation has no End game action");
                    finish.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                tieTimer.Start();
                tieEndButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(TimeSpan.FromMilliseconds(250));
                if (!sawTieConfirm || !tieEnding.Engine.State.RecordSettled
                    || tieEnding.Engine.State.HomeRecord != "5-2-1"
                    || tieEnding.Engine.State.AwayRecord != "5-2-1")
                    throw new Exception($"FAIL: End Game did not apply the tie to an unsettled final ({tieEnding.Engine.State.HomeRecord} / {tieEnding.Engine.State.AwayRecord})");
                tieEnding.Close();
                Pump(TimeSpan.FromMilliseconds(200));
                Console.WriteLine("PASS: End Game applies a tie after the clock has already marked the game final");
            }

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
            // The output refreshes all values while a clock runs. Repeating the value that is
            // already crossing must not snap the transition to its end.
            {
                var transitionItem = new ScorebugElement
                {
                    Name = "Transition probe", Content = "0", Kind = ScorebugElementKind.Text,
                    Width = 120, Height = 80, FontSize = 48,
                    Transition = ScorebugTransition.Fade, TransitionSeconds = 0.8
                };
                var transitionBlock = new ScorebugTextBlock(transitionItem);
                transitionBlock.UpdateText("0", ScorebugTransition.Cut);
                var transitionHost = new Window
                {
                    Width = 220, Height = 140, Content = transitionBlock,
                    ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -32000, Top = -32000
                };
                transitionHost.Show();
                Pump(TimeSpan.FromMilliseconds(80));
                transitionBlock.UpdateText("1", ScorebugTransition.Fade, 0.8);
                transitionBlock.ApplyStyle(transitionItem, null);
                transitionBlock.UpdateText("1", ScorebugTransition.Fade, 0.8);
                Pump(TimeSpan.FromMilliseconds(80));
                if (transitionBlock.Text != "0")
                    throw new Exception("FAIL: refreshing the same value cut its text transition short");
                // A genuinely newer value must also wait its turn instead of snapping the running
                // crossing to its final frame.
                transitionBlock.UpdateText("2", ScorebugTransition.Fade, 0.8);
                if (transitionBlock.Text != "0")
                    throw new Exception("FAIL: a newer value snapped the running text transition");
                Pump(TimeSpan.FromMilliseconds(1700));
                if (transitionBlock.Text != "2")
                    throw new Exception($"FAIL: the newest queued value did not arrive ({transitionBlock.Text})");
                // Returning to the value already crossing in cancels the detour queued behind it.
                transitionBlock.UpdateText("3", ScorebugTransition.Fade, 0.8);
                Pump(TimeSpan.FromMilliseconds(40));
                transitionBlock.UpdateText("4", ScorebugTransition.Fade, 0.8);
                transitionBlock.UpdateText("3", ScorebugTransition.Fade, 0.8);
                Pump(TimeSpan.FromMilliseconds(1700));
                if (transitionBlock.Text != "3")
                    throw new Exception($"FAIL: a stale queued value won after the state returned ({transitionBlock.Text})");
                transitionHost.Close();
                Console.WriteLine("PASS: a repeated or newer value refresh leaves a running text transition alone");
            }

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
                // The program preview is the staging desk: it must show the staged down-line type,
                // not the copy that is still on air. Add the value to layout one and cycle it.
                layoutMain.Scorebug.Update(layout => layout.Elements.Add(new ScorebugElement
                {
                    Name = "Preview down", Content = "{{DOWNS}}", X = 40, Y = 40,
                    Width = 400, Height = 60, FontSize = 30
                }));
                layoutMain.Engine.SetFootballDownMode(FootballDownMode.Pat);
                layoutMain.UpdateLayout();
                if (!FindAll<TextBlock>(preview).Any(t => t.Text == "PAT"))
                    throw new Exception("FAIL: the program preview did not show the staged PAT down line");
                layoutMain.Engine.SetFootballDownMode(FootballDownMode.TwoPoint);
                layoutMain.UpdateLayout();
                if (!FindAll<TextBlock>(preview).Any(t => t.Text == "2 PT ATT"))
                    throw new Exception("FAIL: the program preview did not show the staged two-point down line");
                layoutMain.Engine.SetFootballDownMode(FootballDownMode.Normal);
                layoutMain.UpdateLayout();
                if (!FindAll<TextBlock>(preview).Any(t => t.Text == "1st & 10"))
                    throw new Exception("FAIL: the program preview did not return to the staged yards down line");
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
                // Each layout owns video celebrations keyed by the exact signed score change.
                var scoreVideoPanel = layoutStudio.FindName("ScoreVideoPanel") as DockPanel;
                var scoreVideoRows = layoutStudio.FindName("ScoreVideoRows") as StackPanel;
                if (scoreVideoPanel is null || scoreVideoRows is null || scoreVideoPanel.Visibility != Visibility.Visible)
                    throw new Exception("FAIL: the score-video controls are missing or hidden in the studio");
                layoutMain.Scorebug.ApplyScoreVideoToLayout(6, new ScoreVideo
                {
                    Path = @"C:\clips\touchdown.mp4", ChromaKeyColor = "#FF00B140", ChromaKeyTolerance = 0.21
                });
                layoutMain.Scorebug.ApplyScoreVideoToLayout(-6, new ScoreVideo
                {
                    Path = @"C:\clips\touchdown-undo.mp4", ChromaKeyColor = "#FF2020FF", ChromaKeyTolerance = 0.17
                });
                layoutStudio.UpdateLayout();
                if (scoreVideoRows.Children.Count != 2
                    || !FindAll<TextBlock>(scoreVideoRows).Any(text => text.Text == "+6")
                    || !FindAll<TextBlock>(scoreVideoRows).Any(text => text.Text == "-6"))
                    throw new Exception("FAIL: signed score changes did not create score-video rows");
                if (layoutMain.Scorebug.Layouts[1].ScoreVideos.Count != 2
                    || layoutMain.Scorebug.Layouts[0].ScoreVideos.Count != 0)
                    throw new Exception("FAIL: a score video leaked onto the display's other layout");
                var motionsButton = FindAll<Button>(layoutStudio).First(button =>
                    string.Equals(button.Content?.ToString(), "Motions…", StringComparison.Ordinal));
                motionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(TimeSpan.FromMilliseconds(150));
                var motionWindow = Application.Current.Windows.OfType<Window>()
                    .FirstOrDefault(window => window.Title == "Transitions & score celebrations");
                if (motionWindow is null
                    || !FindAll<TextBlock>(motionWindow).Any(text => text.Text == "SCORE VIDEOS"))
                    throw new Exception("FAIL: the Motions window does not show the score-video controls");
                motionWindow.Close();
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
                var picture = FindAll<System.Windows.Shapes.Rectangle>(surface).FirstOrDefault(rect => rect.Fill is DrawingBrush)
                    ?? throw new Exception("FAIL: a video element has no decoded picture surface");
                var pictureBrush = (DrawingBrush)picture.Fill;
                if (pictureBrush.Drawing is not VideoDrawing)
                    throw new Exception("FAIL: the video picture is not drawn through a VideoDrawing, so effects cannot reach the frames");
                if (pictureBrush.Stretch != Stretch.Uniform)
                    throw new Exception($"FAIL: a preserved-aspect video letterboxes with {pictureBrush.Stretch}");
                video.PreserveVideoAspect = false;
                video.VideoCropLeft = 0.1; video.VideoCropTop = 0.2;
                video.VideoCropRight = 0.3; video.VideoCropBottom = 0.15;
                surface.SetElement(video);
                if (pictureBrush.Stretch != Stretch.UniformToFill)
                    throw new Exception($"FAIL: a free video box should fill its frame, not {pictureBrush.Stretch}");
                var crop = pictureBrush.Viewbox;
                if (Math.Abs(crop.X - 0.1) > 0.001 || Math.Abs(crop.Y - 0.2) > 0.001
                    || Math.Abs(crop.Width - 0.6) > 0.001 || Math.Abs(crop.Height - 0.65) > 0.001)
                    throw new Exception($"FAIL: video crop changes did not reach the brush ({crop})");
                if (Math.Abs(video.Width / video.Height - video.VideoAspect) > 0.001)
                    throw new Exception("FAIL: the preserved-aspect box does not match its picture");
                // Chroma key: the selected green disappears, while another colour survives. Rendering
                // the shader here catches a missing or uncompiled .ps before a show depends on it.
                var keyed = new Border
                {
                    Width = 8, Height = 8, Background = Brushes.Lime,
                    Effect = new ChromaKeyEffect { KeyColor = Colors.Lime, Tolerance = 0.1 }
                };
                // Render it over magenta: checking alpha alone missed the original bug, where the
                // shader left green in RGB and compositing added the green back over the scorebug.
                var keyedStage = new Grid
                {
                    Width = 8, Height = 8, Background = Brushes.Magenta,
                    Children = { keyed }
                };
                keyedStage.Measure(new Size(8, 8));
                keyedStage.Arrange(new Rect(0, 0, 8, 8));
                var keyedBitmap = new RenderTargetBitmap(8, 8, 96, 96, PixelFormats.Pbgra32);
                keyedBitmap.Render(keyedStage);
                var keyedPixels = new byte[8 * 8 * 4];
                keyedBitmap.CopyPixels(keyedPixels, 8 * 4, 0);
                var center = (4 * 8 + 4) * 4;
                if (keyedPixels[center] < 240 || keyedPixels[center + 1] > 15 || keyedPixels[center + 2] < 240)
                    throw new Exception($"FAIL: the chroma-key shader did not leave the background showing through ({keyedPixels[center]},{keyedPixels[center + 1]},{keyedPixels[center + 2]})");
                video.ChromaKeyColor = "#FF00FF00";
                video.ChromaKeyTolerance = 0.1;
                surface.SetElement(video);
                if (picture.Effect is not ChromaKeyEffect)
                    throw new Exception("FAIL: a keyed video box did not receive the chroma-key effect");
                video.ChromaKeyColor = "";
                surface.SetElement(video);
                if (picture.Effect is not null)
                    throw new Exception("FAIL: clearing the chroma key left the effect on the video");
                surface.Dispose();
                Console.WriteLine("PASS: video boxes honour the preserved aspect ratio and chroma key, and each element carries its own audio output");
            }

            // ---------- a video element's chroma key survives the studio's own save path ----------
            var chromaVideoPath = Path.Combine(Path.GetTempPath(), $"smoke-video-key-{Guid.NewGuid():N}.json");
            try
            {
                var chromaVideoCatalog = new ScorebugCatalog(chromaVideoPath);
                var chromaVideo = new ScorebugElement
                {
                    Name = "Keyed ad", Kind = ScorebugElementKind.Video, MediaTag = "Ads",
                    X = 40, Y = 40, Width = 480, Height = 270
                };
                chromaVideoCatalog.Update(layout => layout.Elements.Add(chromaVideo));
                var chromaTarget = chromaVideoCatalog.Layout.Elements.First(item => item.Id == chromaVideo.Id);
                var chromaEditor = new ScorebugEditorWindow(chromaVideoCatalog, engine);
                chromaEditor.Show();
                chromaEditor.UpdateLayout();
                var chromaLayers = (ListBox)chromaEditor.FindName("Layers")!;
                var chromaElements = chromaVideoCatalog.Layout.Elements.ToArray();
                chromaLayers.SelectedIndex = chromaElements.Length - 1 - Array.IndexOf(chromaElements, chromaTarget);
                chromaEditor.UpdateLayout();
                var chromaKeyInput = (TextBox)chromaEditor.FindName("ChromaKeyInput")!;
                chromaKeyInput.Text = "#FF00FF00";
                var chromaTolerance = (TextBox)chromaEditor.FindName("ChromaKeyToleranceInput")!;
                chromaTolerance.Text = "0.09";
                Pump(TimeSpan.FromMilliseconds(500));
                var savedChroma = chromaVideoCatalog.Layout.Elements.First(item => item.Id == chromaVideo.Id);
                if (savedChroma.ChromaKeyColor != "#FF00FF00" || Math.Abs(savedChroma.ChromaKeyTolerance - 0.09) > 0.001)
                    throw new Exception($"FAIL: the studio did not save the video chroma key ({savedChroma.ChromaKeyColor}, {savedChroma.ChromaKeyTolerance:0.00})");
                chromaEditor.Close();
                var reopenedChroma = new ScorebugCatalog(chromaVideoPath);
                var reopenedKeyedVideo = reopenedChroma.Layout.Elements.First(item => item.Id == chromaVideo.Id);
                if (reopenedKeyedVideo.ChromaKeyColor != "#FF00FF00" || Math.Abs(reopenedKeyedVideo.ChromaKeyTolerance - 0.09) > 0.001)
                    throw new Exception("FAIL: a video element's chroma key did not survive reopening the layout");
            }
            finally { if (File.Exists(chromaVideoPath)) File.Delete(chromaVideoPath); }
            Console.WriteLine("PASS: a video element's chroma key and tolerance save through the studio and survive reopening");

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
                    if (block.Text == "24")
                        throw new Exception("FAIL: a newer value cut the running value animation short");
                    Pump(TimeSpan.FromMilliseconds(600));
                    if (block.Text != "24")
                        throw new Exception($"FAIL: a value changed mid-animation settled on '{block.Text}' instead of the newest value");
                    transitionHost.Close();
                }
                Console.WriteLine("PASS: any colour can point at a team's primary or secondary colour, team logos reach the graphic, and the newest value lands without cutting the crossing");
            }
            finally { if (File.Exists(colorPath)) File.Delete(colorPath); }

            // ---------- touchdown and field-goal clips actually play on the graphic ----------
            var scoreEventPath = Path.Combine(Path.GetTempPath(), $"smoke-score-event-{Guid.NewGuid():N}.json");
            try
            {
                var scoreEventCatalog = new ScorebugCatalog(scoreEventPath);
                var scoreEventEngine = new GameEngine();
                scoreEventEngine.NewGame("football");
                var touchdown = new MotionClip { Name = "Probe touchdown", Kind = MotionKind.Animation };
                var touchdownPhase = new MotionPhase("Motion", 1.2);
                touchdownPhase.Tracks.Add(new MotionTrack(MotionTarget.Scale, (0, 1), (1.2, 1.35)));
                touchdown.Phases.Add(touchdownPhase);
                touchdown.Normalize();
                var fieldGoal = new MotionClip { Name = "Probe field goal", Kind = MotionKind.Animation };
                var fieldGoalPhase = new MotionPhase("Motion", 0.8);
                fieldGoalPhase.Tracks.Add(new MotionTrack(MotionTarget.Rotate, (0, 0), (0.8, 6)));
                fieldGoal.Phases.Add(fieldGoalPhase);
                fieldGoal.Normalize();
                scoreEventCatalog.ApplyScoreAnimationToLayout(true, touchdown);
                scoreEventCatalog.ApplyScoreAnimationToLayout(false, fieldGoal);
                var eventView = new ScorebugCanvasView(scoreEventCatalog, scoreEventEngine,
                    stateProvider: () => scoreEventEngine.State, layoutProvider: () => scoreEventCatalog.Layout);
                var eventWindow = new Window
                {
                    Width = 960, Height = 540, Content = eventView, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, Left = -20000, Top = -20000
                };
                eventWindow.Show();
                eventWindow.UpdateLayout();
                var eventCanvas = (Canvas)(typeof(ScorebugCanvasView)
                    .GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(eventView)
                    ?? throw new Exception("FAIL: the score-event probe could not find the graphic canvas"));
                bool CanvasScaleStarted() => eventCanvas.RenderTransform is TransformGroup group
                    && group.Children.OfType<ScaleTransform>().Any(scale => scale.ScaleX > 1.005);
                bool CanvasRotationStarted() => eventCanvas.RenderTransform is TransformGroup group
                    && group.Children.OfType<RotateTransform>().Any(rotate => Math.Abs(rotate.Angle) > 0.1);
                scoreEventEngine.Score(true, 6);
                Pump(TimeSpan.FromMilliseconds(220));
                if (!CanvasScaleStarted())
                    throw new Exception("FAIL: a touchdown did not animate the whole graphic");
                Pump(TimeSpan.FromMilliseconds(1300));
                if (eventCanvas.RenderTransform is TransformGroup settled
                    && settled.Children.OfType<ScaleTransform>().Any(scale => Math.Abs(scale.ScaleX - 1) > 0.01))
                    throw new Exception("FAIL: the touchdown animation did not settle back to the layout");
                // A seven- or eight-point press is still a touchdown; it must use the same clip.
                scoreEventEngine.Score(true, 7);
                Pump(TimeSpan.FromMilliseconds(220));
                if (!CanvasScaleStarted())
                    throw new Exception("FAIL: a seven-point touchdown did not play the touchdown celebration");
                Pump(TimeSpan.FromMilliseconds(1300));
                scoreEventEngine.Score(true, 3);
                Pump(TimeSpan.FromMilliseconds(180));
                if (!CanvasRotationStarted())
                    throw new Exception("FAIL: a field goal did not animate the whole graphic");
                eventWindow.Close();
                Console.WriteLine("PASS: touchdown (including seven-point) and field-goal clips animate the whole graphic and settle");
            }
            finally { if (File.Exists(scoreEventPath)) File.Delete(scoreEventPath); }

            // ---------- a whole-graphic colour key paints the backdrop, never every item plate ----------
            var colourEventPath = Path.Combine(Path.GetTempPath(), $"smoke-score-colour-{Guid.NewGuid():N}.json");
            try
            {
                var colourCatalog = new ScorebugCatalog(colourEventPath);
                colourCatalog.Update(layout =>
                {
                    layout.Elements.Clear();
                    layout.Background = "#FF05080C";
                    layout.Elements.Add(new ScorebugElement
                    {
                        Name = "Home plate", Content = "HOME", X = 40, Y = 40, Width = 360, Height = 110,
                        Background = "#FF102030", Foreground = "#FFE0E8F0", BorderColor = "#FF35C79A",
                        BorderThickness = 4, FontSize = 42
                    });
                    layout.Elements.Add(new ScorebugElement
                    {
                        Name = "Clock", Content = "12:00", X = 440, Y = 40, Width = 300, Height = 110,
                        Background = "#FF301020", Foreground = "#FFFFE090", BorderColor = "#FFE8B23A",
                        BorderThickness = 4, FontSize = 42
                    });
                });
                var colourEngine = new GameEngine();
                colourEngine.NewGame("football");
                var colourClip = new MotionClip { Name = "Colour probe", Kind = MotionKind.Animation };
                var colourPhase = new MotionPhase("Motion", 0.7);
                colourPhase.Colors.Add(new ColorTrack(MotionColorRole.Background, (0, "#FF05080C"), (0.7, "#FF0B6B50")));
                colourPhase.Colors.Add(new ColorTrack(MotionColorRole.Text, (0, "#FFE0E8F0"), (0.35, "#FF38D7A3"), (0.7, "#FFE0E8F0")));
                colourClip.Phases.Add(colourPhase);
                colourClip.Normalize();
                colourCatalog.ApplyScoreAnimationToLayout(true, colourClip);
                var colourView = new ScorebugCanvasView(colourCatalog, colourEngine,
                    stateProvider: () => colourEngine.State, layoutProvider: () => colourCatalog.Layout);
                var colourWindow = new Window
                {
                    Width = 960, Height = 540, Content = colourView, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, Left = -20000, Top = -20000
                };
                colourWindow.Show();
                colourWindow.UpdateLayout();
                var colourCanvas = (Canvas)(typeof(ScorebugCanvasView)
                    .GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(colourView)
                    ?? throw new Exception("FAIL: the colour probe could not find the graphic canvas"));
                var colourPlates = FindAll<Border>(colourView).Where(border => border.Child is ScorebugTextBlock).ToList();
                string[] PlateColours() => colourPlates.Select(border => border.Background switch
                {
                    SolidColorBrush solid => solid.Color.ToString(),
                    _ => "not-solid"
                }).ToArray();
                var beforePlates = PlateColours();
                var beforeBackdrop = (colourCanvas.Background as SolidColorBrush)?.Color.ToString();
                colourEngine.Score(true, 6);
                Pump(TimeSpan.FromMilliseconds(350));
                var duringPlates = PlateColours();
                var homeText = FindAll<TextBlock>(colourView).FirstOrDefault(text => text.Text == "HOME");
                if (homeText?.Foreground is not SolidColorBrush textBrush
                    || textBrush.Color.G < 100 || textBrush.Color.R > 100)
                    throw new Exception($"FAIL: a whole-graphic text colour key did not reach item text ({homeText?.Foreground})");
                if ((colourCanvas.Background as SolidColorBrush)?.Color.ToString() == beforeBackdrop)
                    throw new Exception("FAIL: a whole-graphic background key did not reach the layout backdrop");
                if (!beforePlates.SequenceEqual(duringPlates))
                    throw new Exception($"FAIL: a celebration recoloured item plates ({string.Join(",", beforePlates)} → {string.Join(",", duringPlates)})");
                Pump(TimeSpan.FromMilliseconds(800));
                if (!beforePlates.SequenceEqual(PlateColours()))
                    throw new Exception("FAIL: item plate colours did not return after the celebration");
                colourWindow.Close();
                Console.WriteLine("PASS: a celebration's whole-graphic background key paints the backdrop and leaves every item plate alone");
            }
            finally { if (File.Exists(colourEventPath)) File.Delete(colourEventPath); }

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
                var qb = homeRoster.AddPlayer(); qb.Number = 12; qb.Name = "Smith"; qb.Position = "QB"; qb.Group = "Offense";
                var rb = homeRoster.AddPlayer(); rb.Number = 28; rb.Name = "Jones"; rb.Position = "RB"; rb.Group = "Offense";
                foreach (var player in homeRoster.Players) player.SetStat("Yards", 0);
                qb.SetStat("Yards", 184);
                rb.SetStat("Yards", 96);
                var visitorRoster = RosterStore.Book.Add("Visitors squad");
                var wr = visitorRoster.AddPlayer(); wr.Number = 7; wr.Name = "Brown"; wr.Position = "WR"; wr.Group = "Visitors"; wr.SetStat("Yards", 64);
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
                // The roster desk is a true black surface in the dark theme, with neutral borders and
                // readable text. The old blue-tinted navy read as a different program beside the console.
                rosterWindow.ApplyTheme("Dark");
                rosterWindow.UpdateLayout();
                static double Contrast(Color one, Color two)
                {
                    static double Channel(byte value)
                    {
                        var v = value / 255.0;
                        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
                    }
                    var first = 0.2126 * Channel(one.R) + 0.7152 * Channel(one.G) + 0.0722 * Channel(one.B);
                    var second = 0.2126 * Channel(two.R) + 0.7152 * Channel(two.G) + 0.0722 * Channel(two.B);
                    var light = Math.Max(first, second);
                    var dark = Math.Min(first, second);
                    return (light + 0.05) / (dark + 0.05);
                }
                static Color ResourceColor(FrameworkElement owner, string key) =>
                    owner.Resources[key] is SolidColorBrush brush ? brush.Color
                    : throw new Exception($"FAIL: the roster desk has no {key}");
                var dockBackground = (rosterDock.Background as SolidColorBrush)?.Color ?? Colors.Transparent;
                if (dockBackground != Colors.Black)
                    throw new Exception($"FAIL: the roster desk background is {dockBackground}, not black");
                var rosterSurface = ResourceColor(rosterDock, "RosterSurfaceBrush");
                var rosterAlt = ResourceColor(rosterDock, "RosterSurfaceAltBrush");
                var rosterText = ResourceColor(rosterDock, "RosterTextBrush");
                var rosterMuted = ResourceColor(rosterDock, "RosterMutedBrush");
                if (rosterSurface.R > 14 || rosterSurface.G > 14 || rosterSurface.B > 14
                    || rosterAlt.R > 24 || rosterAlt.G > 24 || rosterAlt.B > 24)
                    throw new Exception($"FAIL: the roster cards are still tinted ({rosterSurface}, {rosterAlt})");
                if (Contrast(rosterText, rosterSurface) < 4.5 || Contrast(rosterMuted, rosterAlt) < 4.5)
                    throw new Exception("FAIL: roster text and muted labels are not readable on the black cards");

                if (!rosterDock.ShowingHome || !rosterDock.ActivePanel.IsHome)
                    throw new Exception("FAIL: the roster desk does not open on the home side");
                var homePanel = rosterDock.ActivePanel;
                var homeRosterBox = (ComboBox)homePanel.FindName("RosterBox")!;
                if (homeRosterBox.Items.Count != 2)
                    throw new Exception($"FAIL: the roster picker lists {homeRosterBox.Items.Count} rosters instead of 2");
                homeRosterBox.SelectedIndex = 0;
                rosterWindow.UpdateLayout();
                if (rosterEngine.State.HomePlayer is not null)
                    throw new Exception($"FAIL: picking the home roster put {rosterEngine.State.HomePlayer.Name} on the graphic without Select player");
                if (!FindAll<Button>(homePanel).Any(b => (b.Content as string)?.Contains("OFFENSE") == true))
                    throw new Exception("FAIL: the roster list did not show the Offense group section");
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
                if (rosterEngine.State.AwayPlayer is not null)
                    throw new Exception($"FAIL: picking the visitors roster put {rosterEngine.State.AwayPlayer.Name} on the graphic without Select player");
                ((Button)rosterDock.FindName("HomeSideTab")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                rosterWindow.UpdateLayout();
                homePanel = rosterDock.ActivePanel;

                // The list feeds the editor; search filters it, and "Select player" switches the player.
                var playerList = (ListBox)homePanel.FindName("PlayerList")!;
                var search = (TextBox)homePanel.FindName("SearchInput")!;
                search.Text = "Smith";
                rosterWindow.UpdateLayout();
                var shownPlayers = playerList.Items.OfType<ListBoxItem>().Count(item => item.Tag is RosterPlayer);
                if (shownPlayers != 1)
                    throw new Exception($"FAIL: searching the roster left {shownPlayers} player rows");
                search.Text = "";
                rosterWindow.UpdateLayout();
                var jonesRow = playerList.Items.OfType<ListBoxItem>()
                    .FirstOrDefault(item => item.Tag is RosterPlayer { Name: "Jones" })
                    ?? throw new Exception("FAIL: the roster list has no Jones row");
                playerList.SelectedItem = jonesRow;
                rosterWindow.UpdateLayout();
                if (rosterEngine.State.HomePlayer is not null)
                    throw new Exception("FAIL: switching rows in the roster editor put a player on the graphic");
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
                // The desk is black in Dark, with neutral cards and wells rather than navy.
                var listCard = FindAll<Border>(homePanel).FirstOrDefault(b => b.Background is SolidColorBrush s && s.Color == Color.FromRgb(10, 10, 10))
                    ?? throw new Exception("FAIL: the roster desk does not use the black surface colour");
                var inputWell = FindAll<TextBox>(homePanel).FirstOrDefault() ?? throw new Exception("FAIL: the roster desk has no fields");
                if (inputWell.Background is not SolidColorBrush well || well.Color != Color.FromRgb(5, 5, 5))
                    throw new Exception($"FAIL: a roster field is not the black input well colour ({inputWell.Background})");
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

            // ---------- custom values accept any text, straight from the console ----------
            {
                var textPanelEngine = new GameEngine();
                textPanelEngine.NewGame("football");
                var textPanel = new GameControlWindow(textPanelEngine, () => { }, _ => { }, () => true);
                textPanel.Show();
                textPanel.UpdateLayout();
                var penalties = textPanelEngine.AddStat("Penalties");
                textPanel.UpdateLayout();
                var statHost = (Panel)textPanel.Panel.FindName("StatsHost")!;
                var penaltyIndex = textPanelEngine.State.Stats.ToList().FindIndex(s => s.Id == penalties.Id);
                var penaltyRow = (FrameworkElement)statHost.Children[penaltyIndex];
                var statBox = FindAll<TextBox>(penaltyRow).Single();
                textPanelEngine.SetStatText(penalties.Id, "3rd");
                textPanel.UpdateLayout();
                if (statBox.Text != "3rd")
                    throw new Exception($"FAIL: the console shows '{statBox.Text}' for a text value");
                // Typing free text into the middle box commits it.
                statBox.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, statBox) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
                statBox.Text = "N/A";
                statBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(textPanel), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
                textPanel.UpdateLayout();
                if (textPanelEngine.State.Stats.First(s => s.Id == penalties.Id).Display != "N/A")
                    throw new Exception($"FAIL: typing text into a value stored '{textPanelEngine.State.Stats.First(s => s.Id == penalties.Id).Display}'");
                if (statBox.Text != "N/A")
                    throw new Exception("FAIL: the value box did not keep the typed text");
                textPanel.Close();
                Console.WriteLine("PASS: custom values on the console take any text, not only numbers");
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
                // Adding a layout selects it in the studio, and the control preview follows that
                // selection. Start from the first layout so the forward arrow stages the new one.
                monitorMain.Scorebug.SelectLayout(0);
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

            // ---------- autosave interval: five by default, one minute minimum, zero never ----------
            {
                var restorePrefs = PreferencesStore.Load();
                var autosavePrefs = new UserPreferences();
                if (autosavePrefs.AutosaveMinutes != 5)
                    throw new Exception($"FAIL: autosave defaults to {autosavePrefs.AutosaveMinutes} minutes");
                var autosaveWindow = new SettingsWindow(autosavePrefs);
                autosaveWindow.Show();
                autosaveWindow.UpdateLayout();
                var autosaveBox = (TextBox)autosaveWindow.FindName("AutosaveMinutesInput")!;
                if (autosaveBox.Text != "5")
                    throw new Exception($"FAIL: the autosave field starts on '{autosaveBox.Text}'");
                autosaveBox.Text = "0";
                FindAll<Button>(autosaveWindow).First(b => (b.Content as string) == "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (autosavePrefs.AutosaveMinutes != 0 || PreferencesStore.Load().AutosaveMinutes != 0)
                    throw new Exception($"FAIL: choosing never left autosave at {autosavePrefs.AutosaveMinutes}/{PreferencesStore.Load().AutosaveMinutes}");
                var oneMinutePrefs = new UserPreferences { AutosaveMinutes = 0 };
                var oneMinuteWindow = new SettingsWindow(oneMinutePrefs);
                oneMinuteWindow.Show();
                oneMinuteWindow.UpdateLayout();
                ((TextBox)oneMinuteWindow.FindName("AutosaveMinutesInput")!).Text = "1";
                FindAll<Button>(oneMinuteWindow).First(b => (b.Content as string) == "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (oneMinutePrefs.AutosaveMinutes != 1)
                    throw new Exception($"FAIL: the one-minute autosave interval saved as {oneMinutePrefs.AutosaveMinutes}");
                PreferencesStore.Save(restorePrefs);
                Console.WriteLine("PASS: autosave defaults to 5 minutes, accepts 1 minute, and 0 means never");
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

                // Both spellings of the next alpha read the same, and the beta's numeric stage sits
                // above every alpha so an installed a1.6 copy sees it as an update.
                if (UpdateChecker.VersionFromTag("a1.7") != "1.70.0"
                    || UpdateChecker.VersionFromTag("a1.70") != "1.70.0"
                    || !UpdateChecker.IsNewer("a1.7", "a1.6")
                    || !UpdateChecker.IsNewer("a1.70", "a1.6")
                    || UpdateChecker.VersionFromTag("b1.0") != "2.0.0"
                    || !UpdateChecker.IsNewer("b1.0", "a1.70"))
                    throw new Exception("FAIL: a1.7/a1.70 and the b1.0 beta do not compare against a1.6 correctly");

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
                    if (FindAll<System.Windows.Shapes.Rectangle>(barsView).Count() != artEngine.State.HomeTimeoutLimit)
                        throw new Exception($"FAIL: timeout bars show {FindAll<System.Windows.Shapes.Rectangle>(barsView).Count()} bars for {artEngine.State.HomeTimeoutLimit} timeouts");
                    if (FindAll<ScorebugTextBlock>(artCanvas).Any(t => t.Text.Contains("POSSESSION", StringComparison.Ordinal)))
                        throw new Exception("FAIL: the old text is still drawn under the artwork");
                    // Live: taking another timeout greys more bars without shrinking the allotment.
                    artEngine.SetTimeoutCount(true, 1);
                    Pump(TimeSpan.FromMilliseconds(200));
                    var artBars = FindAll<System.Windows.Shapes.Rectangle>(barsView);
                    if (artBars.Count() != artEngine.State.HomeTimeoutLimit)
                        throw new Exception("FAIL: a used timeout removed a bar from the allotment");
                    var expectedUsed = Math.Max(0, artEngine.State.HomeTimeoutLimit - artEngine.State.HomeTimeouts);
                    if (artBars.Count(bar => bar.Fill is SolidColorBrush brush && brush.Color == Color.FromRgb(0x9A, 0xA5, 0xB2)) != expectedUsed)
                        throw new Exception($"FAIL: expected {expectedUsed} greyed timeout bars, got {artBars.Count(bar => bar.Fill is SolidColorBrush brush && brush.Color == Color.FromRgb(0x9A, 0xA5, 0xB2))}");
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
                    if (!panelHtml.Contains("Experimental:", StringComparison.Ordinal)
                        || !panelHtml.Contains("speakerNow", StringComparison.Ordinal)
                        || !panelHtml.Contains("PAUSED ·", StringComparison.Ordinal))
                        throw new Exception("FAIL: the remote media card is missing its paused state, speaker row or experimental warning");
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
