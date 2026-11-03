using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public partial class MainWindow : Window, IControlPanelHost
{
    readonly GameEngine _engine = new();
    readonly GameEngine _publishedEngine = new();
    readonly ScorebugCatalog _scorebug = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(5) };
    // The rail's peak meter, redrawn a few times a second; the level itself is shared by every window.
    readonly DispatcherTimer _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    readonly Border[] _meterBars = new Border[22];
    int _meterLit = -1;
    readonly List<GameControlWindow> _controlPanels = [];
    readonly ControlPanelView _panel;
    // This window's own display choice and staged layouts. Another panel can stage a different
    // display at the same time; only Update Output sends anything to an audience.
    readonly ProgramSelection _program;
    readonly MediaSession _session;
    bool _displaySync;
    readonly MediaPlayback _playback;
    // Every audience window ever opened this session. Opening the output again adds another one
    // instead of focusing the old window, so a show can spread the graphic over several displays.
    readonly List<OutputWindow> _outputs = [];
    EventLogWindow? _log;
    RemoteControlServer? _remote;
    ScorebugCanvasView? _preview;
    AudiencePreview? _audience;
    // The four assignable quick-layout buttons above the rail's meter.
    QuickLayoutBar? _quickLayouts;
    int _eventCount = -1;
    readonly UserPreferences _preferences = PreferencesStore.Load();
    string? _projectPath;
    bool _initializing = true;
    bool _liveScoreClock = true;
    // Set while the sport picker is being filled in for the operator, so syncing the control cannot
    // be mistaken for the operator choosing a sport (which starts a new game).
    bool _sportSync;
    // The next autosave starts a fresh recoverable point instead of updating the newest one. Set
    // when a new game starts or a game is loaded, so the previous game keeps its own autosave.
    bool _startNewAutosavePoint = true;
    // The game state and the design as they were the last time they went into a .spg file (or
    // started fresh), so a changed layout counts as an unsaved change too.
    string? _savedStateJson;
    string? _savedDesignJson;
    bool _closingAfterPrompt;
    GameControlWindow.ConsoleTab _tab = GameControlWindow.ConsoleTab.Control;

    /// <summary>
    /// The exit prompt is on for operators. The UI checks turn it off because they create and close
    /// windows deliberately, and a modal prompt would stall an unattended run.
    /// </summary>
    public static bool PromptOnExit { get; set; } = true;

    /// <summary>
    /// Warns before a sport switch clears the old sport's values. The checks turn it off because they
    /// change sports while driving the console, and a modal warning would stall an unattended run.
    /// </summary>
    public static bool WarnOnSportSwitch { get; set; } = true;

    /// <param name="mode">How the app was started: <see cref="LaunchMode.New"/> skips the autosave,
    /// <see cref="LaunchMode.Open"/> loads <paramref name="gamePath"/>. Null keeps the legacy
    /// behaviour of restoring the last autosave, which the tests rely on.</param>
    /// <param name="waitForLaunch">True when the launcher box opens on top of this window: no game
    /// is started or restored until the operator chooses one, and the workspace stays locked.</param>
    public MainWindow(LaunchMode? mode = null, string? gamePath = null, bool waitForLaunch = false)
    {
        InitializeComponent();
        // Follow the window onto portrait monitors and laptops at 150% scaling instead of hanging
        // the console off the screen; the console itself is reflowable down to its minimum width.
        ScreenFit.Track(this);
        _session = new MediaSession(_preferences);
        _playback = _session.Playback;
        ThemeManager.Apply(this, _preferences.Theme);
        BuildSoundMeter();
        _meterTimer.Tick += (_, _) => UpdateSoundMeter();
        _meterTimer.Start();
        DisplayTiming.Apply(_preferences);
        _quickLayouts = new QuickLayoutBar(_scorebug, _engine, _preferences.Theme);
        _quickLayouts.Status += message => StatusText.Text = message;
        QuickLayoutsHost.Content = _quickLayouts;
        SportSelector.SelectedValuePath = "Id";
        RefreshSports();
        SportSelector.SelectedValue = _engine.State.SportId;

        _engine.Changed += Refresh;
        _engine.Changed += EngineChanged;
        _timer.Tick += (_, _) => _engine.Tick();
        _timer.Start();
        _autosave.Tick += (_, _) => AutoSave();
        _autosave.Start();

        _program = new ProgramSelection(_scorebug);
        _program.Changed += ProgramChanged;
        _preview = new ScorebugCanvasView(_scorebug, _publishedEngine, playback: _playback, layoutProvider: () => _program.Layout, displayId: _program.DisplayId);
        PreviewHost.Content = _preview;
        // The monitor reads the published engine and the published layout, so it is a real
        // reflection of the audience window rather than another staged preview.
        _audience = new AudiencePreview(_scorebug, _publishedEngine, _playback, _program.DisplayId);
        AudienceHost.Content = _audience;
        SyncDisplayPicker();
        RefreshLayoutIndicators();
        ShowTab(GameControlWindow.ConsoleTab.Control);
        // The theme pass repaints every button when the window loads, which used to wipe the green
        // outline off the already-selected tab until another tab was clicked.
        Loaded += (_, _) =>
        {
            ApplyTabVisuals();
            // The remote and the update check belong to a live window, not to the launch sequence.
            if (!waitForLaunch) { StartRemote(); _ = CheckForUpdatesAsync(); }
        };

        _panel = new ControlPanelView(_engine, this);
        ControlHost.Content = _panel;
        _panel.SetHintVisibility(_preferences.ShowHeadingHints);

        RosterControl.Attach(_engine);
        RosterControl.ApplyTheme(_preferences.Theme);
        RosterControl.SetHintVisibility(_preferences.ShowHeadingHints);
        MediaControl.Attach(_session);
        MediaControl.ApplyTheme(_preferences.Theme);
        // The dock now lives in a full-width tab instead of a narrow rail, so its controls are the
        // same size as the console's.
        MediaControl.SetLargeMode(true);
        _session.Status += message => StatusText.Text = message;
        ApplyHintVisibility();

        Refresh();
        _initializing = false;

        if (waitForLaunch)
        {
            // The main window is already up behind the launcher, with nothing to change yet.
            _savedStateJson = _engine.ExportJson();
            _savedDesignJson = DesignJson();
            SetWorkspaceEnabled(false);
            StatusText.Text = "WAITING · choose New Game or Open Game in the launch window";
            return;
        }

        // No recovery prompt: a "New Game" launch always starts clean, "Open Game" loads the chosen
        // file, and the window shows whatever the newest autosave holds only when no choice was made.
        if (mode == LaunchMode.New)
        {
            StartNewGame(string.IsNullOrWhiteSpace(_preferences.LastSport) ? "football" : _preferences.LastSport, announce: false);
            StatusText.Text = "NEW GAME · set the teams and you are live";
        }
        else if (mode == LaunchMode.Open && !string.IsNullOrWhiteSpace(gamePath))
        {
            LoadGame(gamePath);
        }
        else if (mode is null)
        {
            AutosaveStore.ImportLegacy();
            var newest = AutosaveStore.List().FirstOrDefault();
            if (newest is not null && LoadGame(newest.Path))
                StatusText.Text = $"AUTOSAVE RESTORED · {newest.WhenLabel} (New Game starts fresh)";
        }

        _savedStateJson ??= _engine.ExportJson();
        _savedDesignJson ??= DesignJson();

        // Panels are opened deliberately with New Panel; starting a game never spawns a second window.
    }

    /// <summary>
    /// Applies the choice made in the launch box to the window that is already open behind it.
    /// </summary>
    public void StartFromLauncher(LaunchMode mode, string? path, string? sportId = null)
    {
        if (mode == LaunchMode.Open && !string.IsNullOrWhiteSpace(path) && LoadGame(path))
            StatusText.Text = $"GAME OPENED · {Path.GetFileNameWithoutExtension(path)}";
        else
        {
            StartNewGame(sportId ?? (string.IsNullOrWhiteSpace(_preferences.LastSport) ? "football" : _preferences.LastSport), announce: false);
            StatusText.Text = "NEW GAME · set the teams and you are live";
        }
        _savedStateJson = _engine.ExportJson();
        _savedDesignJson = DesignJson();
        SetWorkspaceEnabled(true);
        Activate();
        StartRemote();
        _ = CheckForUpdatesAsync();
    }

    /// <summary>Locks or unlocks everything inside the window while the launch box is up.</summary>
    void SetWorkspaceEnabled(bool enabled)
    {
        Root.IsEnabled = enabled;
        // A locked window reads as dimmed, so nobody tries to type into a game that is not there.
        Root.Opacity = enabled ? 1 : 0.55;
    }

    /// <summary>The live game, exposed so the operator surface (and the checks) can drive it.</summary>
    public GameEngine Engine => _engine;

    /// <summary>The copy currently on air (preview and audience output).</summary>
    public GameState PublishedState => _publishedEngine.State;

    /// <summary>The console embedded in this window, exposed for the checks.</summary>
    public ControlPanelView Panel => _panel;

    /// <summary>The monitor that mirrors what the audience is watching, exposed for the checks.</summary>
    public AudiencePreview? AudienceMonitor => _audience;

    /// <summary>The scorebug layouts and design, exposed so the checks can drive the layout arrows.</summary>
    public ScorebugCatalog Scorebug => _scorebug;

    // ---------- IControlPanelHost ----------
    public bool LiveScoreClock
    {
        get => _liveScoreClock;
        set => SetLiveScoreClock(value);
    }

    public bool HasStagedChanges =>
        GameEngine.StagedSignature(_engine.State) != GameEngine.StagedSignature(_publishedEngine.State) ||
        _program.HasStagedChanges;

    /// <summary>The layout the preview is showing: the one that Update Output will publish.</summary>
    public string LayoutLabel => _program.Label;

    public void OpenNewPanel() => OpenControlPanel();

    /// <summary>
    /// Sends the game data (score, clocks, down, stats) to the audience copies. It deliberately
    /// touches no layout, so a detached panel can publish its own display right after it.
    /// </summary>
    public void PublishGameState()
    {
        _publishedEngine.LoadFromState(_engine.CloneState());
        _preview?.Refresh();
        // The monitor listens to the published engine itself, so the copy above already repaints it.
        foreach (var output in _outputs) output.Publish();
    }

    public void PublishOutput()
    {
        // The chosen layouts go on air with the game data: the preview and output still read
        // different copies, so the switch only happens here.
        PublishGameState();
        _program.PublishStaged();
        RefreshLayoutIndicators();
        StatusText.Text = $"OUTPUT UPDATED · {LayoutLabel.ToUpperInvariant()} · {DateTime.Now:HH:mm:ss}";
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
    }

    /// <summary>Moves the previewed layout; nothing goes on air until Update Output is pressed.</summary>
    public void ShiftLayout(int delta)
    {
        _program.Shift(delta);
        _preview?.Rebuild();
        RefreshLayoutIndicators();
        StatusText.Text = _program.HasStagedChanges
            ? $"LAYOUT · {LayoutLabel} · preview only — press Update Output"
            : $"LAYOUT · {LayoutLabel} · ON AIR";
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
    }

    void ProgramChanged()
    {
        SyncDisplayPicker();
        RefreshLayoutIndicators();
        _preview?.Rebuild();
    }

    /// <summary>Fills the display picker without treating the sync as the operator's choice.</summary>
    void SyncDisplayPicker()
    {
        _displaySync = true;
        try
        {
            var names = _scorebug.Displays.Select(display => display.Name).ToArray();
            if (DisplaySelector.ItemsSource is not IEnumerable<string> existing || !existing.SequenceEqual(names))
                DisplaySelector.ItemsSource = names;
            var index = _scorebug.Displays.FindIndex(display => string.Equals(display.Id, _program.DisplayId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && DisplaySelector.SelectedIndex != index) DisplaySelector.SelectedIndex = index;
        }
        finally { _displaySync = false; }
    }

    void Display_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_displaySync || DisplaySelector.SelectedIndex < 0 || DisplaySelector.SelectedIndex >= _scorebug.Displays.Count) return;
        _program.SelectDisplay(_scorebug.Displays[DisplaySelector.SelectedIndex].Id);
        _preview?.Rebuild();
        RefreshLayoutIndicators();
        StatusText.Text = $"PREVIEW · {_program.Display.Name} · {LayoutLabel}";
    }

    void RefreshLayoutIndicators()
    {
        PreviewLayoutLabel.Text = LayoutLabel;
        var count = _program.Display.Layouts.Count;
        LayoutPrevButton.IsEnabled = LayoutNextButton.IsEnabled = count > 1;
    }

    // ---------- tabs ----------

    /// <summary>Switches the operator surface between the console and the media desk.</summary>
    public void ShowMediaTab(bool media) => ShowTab(media ? GameControlWindow.ConsoleTab.Media : GameControlWindow.ConsoleTab.Control);

    void ControlTab_Click(object sender, RoutedEventArgs e) => ShowTab(GameControlWindow.ConsoleTab.Control);

    void MediaTab_Click(object sender, RoutedEventArgs e) => ShowTab(GameControlWindow.ConsoleTab.Media);

    void RosterTab_Click(object sender, RoutedEventArgs e) => ShowTab(GameControlWindow.ConsoleTab.Rosters);

    /// <summary>Switches the operator surface between the console, the media desk and the roster desk.</summary>
    void ShowTab(GameControlWindow.ConsoleTab tab)
    {
        _tab = tab;
        ControlHost.Visibility = tab == GameControlWindow.ConsoleTab.Control ? Visibility.Visible : Visibility.Collapsed;
        MediaControl.Visibility = tab == GameControlWindow.ConsoleTab.Media ? Visibility.Visible : Visibility.Collapsed;
        RosterControl.Visibility = tab == GameControlWindow.ConsoleTab.Rosters ? Visibility.Visible : Visibility.Collapsed;
        ApplyTabVisuals();
        if (tab == GameControlWindow.ConsoleTab.Media) MediaControl.RefreshView();
        if (tab == GameControlWindow.ConsoleTab.Rosters) RosterControl.RefreshView();
    }

    /// <summary>
    /// Paints the active tab. Split out because the theme pass repaints every button when the
    /// window loads, which silently erased the green outline on the tab that was already selected.
    /// </summary>
    void ApplyTabVisuals()
    {
        // Active-tab colours follow the operator's accent from Preferences.
        var accent = UiPalette.Accent(_preferences);
        var on = UiPalette.Frozen(UiPalette.Soft(accent));
        var off = (Brush)new BrushConverter().ConvertFromString("#141A23")!;
        var onBorder = UiPalette.Frozen(accent);
        var offBorder = (Brush)new BrushConverter().ConvertFromString("#28323F")!;
        var muted = (Brush)new BrushConverter().ConvertFromString("#93A0B2")!;
        Button[] tabs = [ControlTab, MediaTab, RosterTab];
        var active = (int)_tab;
        for (var i = 0; i < tabs.Length; i++)
        {
            tabs[i].Background = i == active ? on : off;
            tabs[i].BorderBrush = i == active ? onBorder : offBorder;
            tabs[i].Foreground = i == active ? Brushes.White : muted;
        }
    }

    void LayoutPrev_Click(object sender, RoutedEventArgs e) => ShiftLayout(-1);

    void LayoutNext_Click(object sender, RoutedEventArgs e) => ShiftLayout(1);

    // ---------- window chrome ----------
    void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var s = _engine.State;
        if (SportSelector.SelectedValue is not string selectedId || selectedId != s.SportId)
        {
            _sportSync = true;
            try { SportSelector.SelectedValue = s.SportId; }
            finally { _sportSync = false; }
        }
        _eventCount = s.Events.Count;
    }

    void RefreshSports() => SportSelector.ItemsSource = _engine.Sports.All;

    /// <summary>
    /// Builds the rail's peak meter once: a column of segments whose fills the tick only repaints.
    /// Green up to a healthy level, amber near the top, red at full scale.
    /// </summary>
    void BuildSoundMeter()
    {
        var unlit = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x30));
        unlit.Freeze();
        for (var index = 0; index < _meterBars.Length; index++)
        {
            var fraction = (index + 1) / (double)_meterBars.Length;
            var color = fraction > 0.92 ? Color.FromRgb(0xFF, 0x6B, 0x6B)
                : fraction > 0.78 ? Color.FromRgb(0xE8, 0xB2, 0x3A)
                : Color.FromRgb(0x38, 0xD7, 0xA3);
            var bar = new Border
            {
                Background = unlit,
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(1, 0, 1, 0),
                ToolTip = fraction > 0.92 ? "Full scale — the output is clipping" : null
            };
            bar.Tag = color;
            ThemeManager.SetSkipTheme(bar, true);
            _meterBars[index] = bar;
            SoundMeterSegments.Children.Add(bar);
        }
    }

    /// <summary>
    /// Repaints the meter and the show line beside it: what is on air, and the score and clock the
    /// audience is actually looking at. Read from the published copy, not from staged edits.
    /// </summary>
    void UpdateSoundMeter()
    {
        if (_meterBars[0] is null) return;
        var level = SoundLevelMeter.Read();
        // A square-root scale so quiet speech still moves the bar, the way a real meter does.
        var lit = (int)Math.Round(Math.Sqrt(level) * _meterBars.Length);
        if (lit != _meterLit)
        {
            _meterLit = lit;
            var unlit = (Brush)new BrushConverter().ConvertFromString("#1B2430")!;
            for (var index = 0; index < _meterBars.Length; index++)
                _meterBars[index].Background = index < lit ? new SolidColorBrush((Color)_meterBars[index].Tag) : unlit;
        }
        SoundLevelText.Text = level <= 0
            ? (SoundLevelMeter.Available ? "−∞ dB" : "no output")
            : $"{SoundLevelMeter.Decibels(level):0} dB";
        if (_preview is null) { OnAirInfo.Text = "STANDBY"; ShowInfo.Text = "Start a game to go live"; return; }
        OnAirInfo.Text = _program.HasStagedChanges ? "STAGED · UPDATE TO AIR" : $"ON AIR · {_program.Display.Name.ToUpperInvariant()}";
        var s = _publishedEngine.State;
        ShowInfo.Text = $"{s.AwayAbbreviation} {s.AwayScore} · {s.HomeAbbreviation} {s.HomeScore} · {GameEngine.FormatClock(s.ClockSeconds)} · {PeriodDisplay.Format(s)}";
    }

    void ApplyHintVisibility()
    {
        var visibility = _preferences.ShowHeadingHints ? Visibility.Visible : Visibility.Collapsed;
        SportHint.Visibility = PreviewAudienceHint.Visibility = visibility;
        MediaControl.SetHintVisibility(_preferences.ShowHeadingHints);
        RosterControl.SetHintVisibility(_preferences.ShowHeadingHints);
        _log?.SetHintVisibility(_preferences.ShowHeadingHints);
    }

    void EventLog_Click(object sender, RoutedEventArgs e)
    {
        if (_log is { IsVisible: true }) { _log.Activate(); return; }
        _log = new EventLogWindow(_engine, _preferences.Theme, _preferences.ShowHeadingHints) { Owner = this };
        _log.Closed += (_, _) => _log = null;
        _log.Show();
    }

    void ScorebugStudio_Click(object sender, RoutedEventArgs e)
    {
        // The studio edits exactly the display and layout this window is previewing.
        var displayIndex = _scorebug.Displays.FindIndex(display => string.Equals(display.Id, _program.DisplayId, StringComparison.OrdinalIgnoreCase));
        if (displayIndex >= 0) _scorebug.SelectDisplay(displayIndex);
        _scorebug.SelectLayout(_program.LayoutIndex);
        var studio = new ScorebugEditorWindow(_scorebug, _engine, _playback, _preferences.Theme) { Owner = this };
        studio.ShowDialog();
        // The studio can add, delete or switch layouts; the preview and every panel follow.
        _preview?.Rebuild();
        RefreshLayoutIndicators();
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
        // A quick button may point at the layout that was just renamed or removed.
        _quickLayouts?.Refresh();
    }

    /// <summary>
    /// Opens one of the two motion makers on the display and layout this window is previewing, so
    /// what the operator builds applies to the graphic they are looking at.
    /// </summary>
    void OpenMotionMaker(MotionKind kind)
    {
        var displayIndex = _scorebug.Displays.FindIndex(display => string.Equals(display.Id, _program.DisplayId, StringComparison.OrdinalIgnoreCase));
        if (displayIndex >= 0) _scorebug.SelectDisplay(displayIndex);
        _scorebug.SelectLayout(_program.LayoutIndex);
        var maker = new MotionMakerWindow(kind, _scorebug, _engine, _preferences.Theme) { Owner = this };
        maker.ShowDialog();
        _preview?.Rebuild();
        RefreshLayoutIndicators();
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
        _quickLayouts?.Refresh();
    }

    void TransitionMaker_Click(object sender, RoutedEventArgs e) => OpenMotionMaker(MotionKind.Transition);

    void AnimationMaker_Click(object sender, RoutedEventArgs e) => OpenMotionMaker(MotionKind.Animation);

    void Preferences_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_preferences) { Owner = this };
        ThemeManager.Apply(dialog, _preferences.Theme);
        if (dialog.ShowDialog() != true) return; // Cancel discards: nothing is applied or saved.
        ThemeManager.Apply(this, _preferences.Theme);
        // The new theme paints buttons as it runs; put the active tab's outline back on top.
        ApplyTabVisuals();
        MediaControl.ApplyTheme(_preferences.Theme);
        RosterControl.ApplyTheme(_preferences.Theme);
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.ApplyTheme(_preferences.Theme);
        foreach (var output in _outputs) output.SetBroadcastTheme(_preferences.Theme);
        ApplyHintVisibility();
        _panel.SetHintVisibility(_preferences.ShowHeadingHints);
        // A changed value delay reaches every graphic already on screen, in this window and out.
        DisplayTiming.Apply(_preferences);
        // The quick buttons may have been switched between staging and publishing.
        _quickLayouts?.Refresh();
        _preview?.ApplyNow();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.RefreshPreview();
        foreach (var output in _outputs) output.RefreshGraphic();
        ApplyTabVisuals();
        // Turning the remote on or changing its password takes effect without a restart.
        StartRemote();
    }

    /// <summary>
    /// Starts (or restarts) the phone remote from the saved preferences. It is off unless enabled and
    /// protected by a password, so an unattended install never exposes a control surface.
    /// </summary>
    void StartRemote()
    {
        _remote?.Dispose();
        _remote = null;
        if (!_preferences.RemoteEnabled || string.IsNullOrWhiteSpace(_preferences.RemotePassword)) return;
        var server = new RemoteControlServer(_engine, this, id => { _panel.Execute(id); return true; }, Dispatcher, _preferences.RemotePort, _preferences.RemotePassword);
        server.Status += message => Dispatcher.BeginInvoke(new Action(() => StatusText.Text = message));
        server.Start();
        _remote = server;
    }

    /// <summary>
    /// Looks for a newer installed build, then downloads, verifies and starts its installer. Only an
    /// installed copy checks; a folder build stays exactly as it is.
    /// </summary>
    async Task CheckForUpdatesAsync()
    {
        if (!_preferences.CheckForUpdates || !AppInfo.IsInstalled) return;
        var manifest = await UpdateChecker.CheckAsync(_preferences.UpdateFeedUrl);
        if (manifest is null) return;
        var detail = $"Version {manifest.Version} is ready (this build is {AppInfo.Version}). " +
                     (string.IsNullOrWhiteSpace(manifest.Notes) ? "Download and install it now?" : $"{manifest.Notes}\n\nDownload and install it now?");
        if (!ConfirmWindow.Ask(this, _preferences.Theme, "UPDATE AVAILABLE", detail, "Download and install")) return;
        StatusText.Text = $"UPDATING · downloading {manifest.Version}";
        var (path, error) = await UpdateChecker.DownloadAsync(manifest);
        if (path is null)
        {
            StatusText.Text = "UPDATE FAILED";
            NoticeWindow.Tell(this, "UPDATE FAILED", error ?? "The update could not be downloaded.", warning: true);
            return;
        }
        if (!UpdateChecker.Run(path))
        {
            StatusText.Text = "UPDATE COULD NOT START";
            NoticeWindow.Tell(this, "UPDATE COULD NOT START", $"The new version was downloaded to {path}, but Windows did not start the installer.", warning: true);
            return;
        }
        _closingAfterPrompt = true;
        Close();
    }

    void NewPanel_Click(object sender, RoutedEventArgs e) => OpenControlPanel();

    void Keybinds_Click(object sender, RoutedEventArgs e) => _panel.ShowKeybindsDialog();

    public void OpenControlPanel()
    {
        var panel = new GameControlWindow(_engine, _publishedEngine, _scorebug, PublishGameState, HasStagedGameState,
            SetLiveScoreClock, () => _liveScoreClock, _session, _preferences.Theme);
        panel.NewPanelRequested += OpenControlPanel;
        panel.Closed += (_, _) => _controlPanels.Remove(panel);
        panel.ApplyTheme(_preferences.Theme);
        panel.SetHintVisibility(_preferences.ShowHeadingHints);
        _controlPanels.Add(panel);
        panel.Show();
    }

    /// <summary>True when the game data differs from what is on air, ignoring any layout choice.</summary>
    public bool HasStagedGameState() =>
        GameEngine.StagedSignature(_engine.State) != GameEngine.StagedSignature(_publishedEngine.State);

    void SportProfiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SportProfileWindow(_engine.Sports) { Owner = this };
        ThemeManager.Apply(dialog, _preferences.Theme);
        dialog.ShowDialog();
        var activeId = SportSelector.SelectedValue as string;
        _sportSync = true;
        try
        {
            RefreshSports();
            if (_engine.Sports.Find(activeId) is not null) SportSelector.SelectedValue = activeId;
        }
        finally { _sportSync = false; }
        if (!dialog.Changed) return;
        // The catalog was edited in place, so the console and any detached panel rebuild from it
        // now; only a new game re-reads the rules (timeouts, scoring buttons, period length).
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
        StatusText.Text = $"SPORT PROFILES SAVED · {_engine.Sport.Name} · start a new game to use the new rules";
    }

    void Sport_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _sportSync || SportSelector.SelectedValue is not string id) return;
        // A sport switch starts a brand new game, so anything the old sport was tracking is about to
        // go. Say so plainly instead of deleting a period's worth of counts without a word.
        if (WarnOnSportSwitch && _engine.HasSportSpecificData())
        {
            var next = _engine.Sports.Find(id)?.Name ?? id;
            var detail = $"Switching to {next} starts a new game. The {_engine.Sport.Name} values this game is tracking — its counts, flags and figures — are cleared with it, along with the score, clocks and custom stats. The roster library, sport profiles, media and your layouts stay.";
            if (!ConfirmWindow.Ask(this, _preferences.Theme, "SWITCH SPORT", detail, "Switch sport", destructive: true))
            {
                // Put the picker back on the sport being played; nothing has been touched yet.
                _sportSync = true;
                try { SportSelector.SelectedValue = _engine.State.SportId; }
                finally { _sportSync = false; }
                return;
            }
        }
        StartNewGame(id, announce: false);
        StatusText.Text = $"NEW {_engine.Sport.Name.ToUpperInvariant()} GAME";
    }

    void NewGame_Click(object sender, RoutedEventArgs e)
    {
        // The sport is part of starting a game; the chooser also states what a new game resets.
        var picker = new SportPickerWindow(_engine.Sports, _engine.State.SportId, _preferences.Theme) { Owner = this };
        if (picker.ShowDialog() != true || string.IsNullOrWhiteSpace(picker.SportId)) return;
        StartNewGame(picker.SportId);
    }

    /// <summary>
    /// A new game resets every piece of game state and takes the audience output with it, so nothing
    /// from the previous game survives on air. Operator settings that are not part of a game — sport
    /// profiles, keybinds, preferences, the media library and the scorebug design — are untouched.
    /// </summary>
    public void StartNewGame(string sportId, bool announce = true)
    {
        _engine.NewGame(sportId);
        _projectPath = null;
        _startNewAutosavePoint = true;
        _savedStateJson = _engine.ExportJson();
        _preferences.LastSport = sportId;
        try { PreferencesStore.Save(_preferences); } catch { }
        // A new game really starts over: one factory layout and no folder/playlist organisation.
        // Imported media files stay in the library; the game's design and shelves begin blank.
        _scorebug.ResetToDefault();
        _savedDesignJson = DesignJson();
        _program.Sync();
        SyncDisplayPicker();
        _session.ResetOrganization();
        MediaControl.ResetView();
        foreach (var panel in _controlPanels.Where(panel => panel.IsVisible)) panel.Media.ResetView();
        ResetOutputToCurrentGame();
        RefreshLayoutIndicators();
        if (announce) StatusText.Text = $"NEW {_engine.Sport.Name.ToUpperInvariant()} GAME";
    }

    /// <summary>
    /// Puts the published copy (preview and audience output) on the same fresh state and takes any
    /// cued clip off the scorebug, so a new game really is a blank slate on air.
    /// </summary>
    void ResetOutputToCurrentGame()
    {
        _publishedEngine.LoadFromState(_engine.CloneState());
        // Rebuild, not refresh: a new game gets brand-new visuals, so no score, down, possession
        // or last video frame can be carried over from the game before it.
        _preview?.Rebuild();
        _audience?.Refresh();
        foreach (var output in _outputs) { output.Publish(); output.Rebuild(); }
        _panel.Refresh();
        // Take the cued clip off the scorebug last, so a media hiccup cannot block the game reset.
        _session.Stop();
    }

    bool LoadGame(string path)
    {
        try
        {
            var design = _engine.Load(path);
            // A file written by an older build has no design; the current scorebug then stays as it
            // is rather than being replaced by a factory layout.
            if (design is not null) _scorebug.ApplyDocument(design);
            _projectPath = path;
            _startNewAutosavePoint = true;
            // Point the picker at the loaded sport without letting that look like a sport change,
            // which would start a brand new game and throw the loaded one away.
            _sportSync = true;
            try
            {
                RefreshSports();
                SportSelector.SelectedValue = _engine.State.SportId;
            }
            finally { _sportSync = false; }
            _publishedEngine.LoadFromState(_engine.CloneState());
            _program.Sync();
            SyncDisplayPicker();
            // Rebuild, not refresh: visuals made before this game was read (for example a logo
            // element built while the teams had no crests) must take the loaded values.
            _preview?.Rebuild();
            foreach (var output in _outputs) output.Rebuild();
            _savedStateJson = _engine.ExportJson();
            _savedDesignJson = DesignJson();
            StatusText.Text = $"GAME LOADED · {System.IO.Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"COULD NOT OPEN GAME · {ex.Message}";
            return false;
        }
    }

    // ---------- output ----------
    void Output_Click(object sender, RoutedEventArgs e) => OpenOutputWindow();

    /// <summary>
    /// Asks which display this audience window is for, then opens a brand-new window for it. With
    /// only one display there is nothing to ask, so the question is skipped.
    /// </summary>
    public OutputWindow? OpenOutputWindow(string? displayId = null)
    {
        if (displayId is null)
        {
            if (_scorebug.Displays.Count == 1) displayId = _scorebug.Displays[0].Id;
            else
            {
                displayId = DisplayPickerWindow.Ask(this, _scorebug, _preferences.Theme, _program.DisplayId);
                if (displayId is null) return null;
            }
        }
        var output = new OutputWindow(_publishedEngine, _scorebug, _preferences.Theme, _playback, displayId)
        {
            Title = $"ScorePlus — {_scorebug.FindDisplay(displayId)?.Name ?? "Program"} Audience Output"
        };
        _outputs.Add(output);
        output.Closed += (_, _) => _outputs.Remove(output);
        output.Show();
        // Cascade each new window instead of stacking it exactly on top of the last one, so every
        // output opened this session stays visibly on screen. Position is only known after Show.
        var step = 32 * (_outputs.Count - 1);
        if (step > 0)
        {
            output.WindowStartupLocation = WindowStartupLocation.Manual;
            output.Left += step;
            output.Top += step;
        }
        output.Activate();
        if (_liveScoreClock) output.UpdateLiveScoreClock(_engine.State);
        else output.RefreshDisplay();
        return output;
    }

    void SetLiveScoreClock(bool enabled)
    {
        _liveScoreClock = enabled;
        foreach (var panel in _controlPanels) panel.SyncLiveState(enabled);
        _panel.SyncLiveState(enabled);
        if (enabled) PublishLiveValues();
    }

    void EngineChanged() { if (_liveScoreClock) PublishLiveValues(); }

    void PublishLiveValues()
    {
        if (!_publishedEngine.UpdateLiveScoreClock(_engine.State)) return;
        _preview?.Refresh();
        foreach (var output in _outputs.Where(o => o.IsVisible)) output.UpdateLiveScoreClock(_engine.State);
    }

    void GameReport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "HTML game report (*.html)|*.html", DefaultExt = "html", FileName = $"{_engine.State.HomeAbbreviation}-{_engine.State.AwayAbbreviation}-report" };
        if (dialog.ShowDialog() != true) return;
        // The report carries every player on every roster, including the ones who never touched the
        // ball, so a zero is printed rather than an empty table.
        File.WriteAllText(dialog.FileName, GameReport.Build(_engine.State, RosterStore.Book.Rosters));
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        StatusText.Text = "GAME REPORT EXPORTED";
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "ScorePlus Game (*.spg)|*.spg|JSON (*.json)|*.json", DefaultExt = "spg", FileName = $"{_engine.State.HomeAbbreviation}-{_engine.State.AwayAbbreviation}" };
        if (dialog.ShowDialog() != true) return;
        SaveGameTo(dialog.FileName);
    }

    /// <summary>Writes the game and remembers that this is now the saved state.</summary>
    bool SaveGameTo(string path)
    {
        try
        {
            // The game travels with its screens: every display, every layout and what each one has
            // on air, so opening the file puts the show back exactly where it was left.
            _engine.Save(path, _scorebug.ExportDocument());
            _projectPath = path;
            _savedStateJson = _engine.ExportJson();
            _savedDesignJson = DesignJson();
            StatusText.Text = $"GAME SAVED · {System.IO.Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"COULD NOT SAVE GAME · {ex.Message}";
            return false;
        }
    }

    bool HasUnsavedChanges() =>
        (_savedStateJson is not null && _savedStateJson != _engine.ExportJson())
        || (_savedDesignJson is not null && _savedDesignJson != DesignJson());

    /// <summary>A comparable snapshot of the design, for the unsaved-changes check.</summary>
    string DesignJson() => System.Text.Json.JsonSerializer.Serialize(_scorebug.ExportDocument());

    /// <summary>Saves to the game's own file, or asks for one when the game has never been saved.</summary>
    bool SaveBeforeClosing()
    {
        if (!string.IsNullOrWhiteSpace(_projectPath)) return SaveGameTo(_projectPath);
        var dialog = new SaveFileDialog
        {
            Filter = "ScorePlus Game (*.spg)|*.spg|JSON (*.json)|*.json",
            DefaultExt = "spg",
            FileName = $"{_engine.State.HomeAbbreviation}-{_engine.State.AwayAbbreviation}"
        };
        return dialog.ShowDialog() == true && SaveGameTo(dialog.FileName);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (PromptOnExit && !_closingAfterPrompt && HasUnsavedChanges())
        {
            e.Cancel = true;
            var detail = "You have changes that are not in a saved game file. Autosave keeps a recovery point every few seconds, but the .spg on disk is out of date. Save it before closing?";
            var choice = UnsavedChangesWindow.Ask(this, _preferences.Theme, detail);
            if (choice == UnsavedChoice.Cancel) return;
            if (choice == UnsavedChoice.Save && !SaveBeforeClosing()) return;
            _closingAfterPrompt = true;
            // WPF forbids Close/ShowDialog while the closing event is still running. Re-post the
            // close once this cancellation has fully finished, so Discard really exits.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Close));
            return;
        }
        base.OnClosing(e);
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { DefaultExt = "spg", Filter = "ScorePlus Game (*.spg)|*.spg|Scoreboard Game (*.scoreboard;*.json)|*.scoreboard;*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        LoadGame(dialog.FileName);
    }

    // ---------- lifecycle ----------
    void AutoSave()
    {
        try
        {
            AutosaveStore.Save(_engine, _startNewAutosavePoint, _scorebug.ExportDocument());
            _startNewAutosavePoint = false;
            AutosaveText.Text = $"AUTOSAVED {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { StatusText.Text = $"AUTOSAVE WARNING · {ex.Message}"; }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Enter settles whatever is being typed and leaves the window open. There is no default
        // button on this window, so without this an Enter in a field would do nothing at all.
        if (key == Key.Enter && Keyboard.FocusedElement is TextBox box && !box.AcceptsReturn)
        {
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }
        if (_panel.TryHandleKey(key, Keyboard.Modifiers)) { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _engine.Changed -= Refresh;
        _engine.Changed -= EngineChanged;
        _remote?.Dispose();
        _meterTimer.Stop();
        _program.Dispose();
        _preview?.Dispose();
        _session.Dispose();
        foreach (var panel in _controlPanels.ToArray()) panel.Close();
        foreach (var output in _outputs.ToArray()) output.Close();
        _log?.Close();
        base.OnClosed(e);
    }
}
