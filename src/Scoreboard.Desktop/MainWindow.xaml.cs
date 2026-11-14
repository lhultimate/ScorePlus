using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
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
    // Node rules run against the published state unless a rule explicitly bypasses Update Output.
    readonly AutomationRuntime _automation;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    // Every fifteen seconds, not five: a recovery point that overwrites the newest file within the
    // minute gains nothing from being written three times as often, and each write is a stall.
    readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(15) };
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
    readonly HardwareService _hardware;
    LeagueCloudService? _leagueCloud;
    bool _displaySync;
    readonly MediaPlayback _playback;
    // Every audience window ever opened this session. Opening the output again adds another one
    // instead of focusing the old window, so a show can spread the graphic over several displays.
    readonly List<OutputWindow> _outputs = [];
    // One ink board per display, shared by every output window for that display. The pen then shows
    // on the projector and on the operator's copy rather than existing inside a single window.
    readonly Dictionary<string, AnnotationBoard> _annotationBoards = new(StringComparer.OrdinalIgnoreCase);
    EventLogWindow? _log;
    RemoteControlServer? _remote;
    // What /watch is showing, one frame per display, and a counter the page watches so a wall
    // screen only fetches a picture when the graphic changed.
    readonly Dictionary<string,(string Signature, byte[]? Png, long Version)> _watchFrames = new(StringComparer.Ordinal);
    long _watchVersion;
    ScorebugCanvasView? _preview;
    AudiencePreview? _audience;
    // The assignable quick media and quick layout buttons above the rail's meter.
    QuickMediaBar? _quickMedia;
    QuickLayoutBar? _quickLayouts;
    int _eventCount = -1;
    readonly UserPreferences _preferences = LoadPreferences();

    /// <summary>Reads preferences and arms the Next down preview before the console can use it.</summary>
    static UserPreferences LoadPreferences()
    {
        var prefs = PreferencesStore.Load();
        GameEngine.DownPreviewEnabled = prefs.NextDownPreview;
        GameEngine.NewSeriesLiveEnabled = prefs.NewSeriesLive;
        return prefs;
    }
    string? _projectPath;
    bool _initializing = true;
    bool _liveScoreClock = true;
    // The desk can be locked over the top of a live game. The overlay and the remote both read this.
    bool _operatorLocked;
    bool _workspaceEnabled = true;
    // Set while the sport picker is being filled in for the operator, so syncing the control cannot
    // be mistaken for the operator choosing a sport (which starts a new game).
    bool _sportSync;
    bool _gameNameSync;
    // The next autosave starts a fresh recoverable point instead of updating the newest one. Set
    // when a new game starts or a game is loaded, so the previous game keeps its own autosave.
    bool _startNewAutosavePoint = true;
    DateTime _lastAutosaveAt;
    // The game state and the design as they were the last time they went into a .spg file (or
    // started fresh), so a changed layout counts as an unsaved change too.
    string? _savedStateJson;
    string? _savedDesignJson;
    bool _closingAfterPrompt;
    int _lastGameClock = -1;
    int _lastPlayClock = -1;
    bool _lastGameClockRunning;
    bool _lastPlayClockRunning;
    bool _autoHornReady;
    GameControlWindow.ConsoleTab _tab = GameControlWindow.ConsoleTab.Control;
    MacroDock? _macroDock;

    /// <summary>
    /// The exit prompt is on for operators. The UI checks turn it off because they create and close
    /// windows deliberately, and a modal prompt would stall an unattended run.
    /// </summary>
    public static bool PromptOnExit { get; set; } = true;

    // True once the launch box has actually started or opened a game. While it is false the window
    // is still the empty workspace behind the launcher, so closing must never ask to save.
    bool _gameStarted;

    /// <summary>Lets unattended checks create windows without opening a network update dialog.</summary>
    public static bool CheckForUpdatesOnLoad { get; set; } = true;

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
        _hardware = new HardwareService(_engine);
        _hardware.Snapshot += snapshot => Dispatcher.BeginInvoke(new Action(() => ApplyHardware(snapshot)));
        _hardware.Status += message => Dispatcher.BeginInvoke(new Action(() => StatusText.Text = message));
        ThemeManager.Apply(this, _preferences.Theme);
        BuildSoundMeter();
        _meterTimer.Tick += (_, _) => UpdateSoundMeter();
        _meterTimer.Start();
        DisplayTiming.Apply(_preferences);
        _quickMedia = new QuickMediaBar(_session, _preferences.Theme);
        _quickMedia.Status += message => StatusText.Text = message;
        QuickMediaHost.Content = _quickMedia;
        _quickLayouts = new QuickLayoutBar(_scorebug, _engine, _preferences.Theme);
        _quickLayouts.Status += message => StatusText.Text = message;
        QuickLayoutsHost.Content = _quickLayouts;
        SportSelector.SelectedValuePath = "Id";
        RefreshSports();
        SportSelector.SelectedValue = _engine.State.SportId;

        _engine.Changed += Refresh;
        _engine.Changed += EngineChanged;
        _leagueCloud = new LeagueCloudService(_engine, _preferences);
        _leagueCloud.Status += message => Dispatcher.BeginInvoke(new Action(() => StatusText.Text = message));
        _engine.Changed += () => _leagueCloud?.MarkDirty();
        _timer.Tick += (_, _) => _engine.Tick();
        _timer.Start();
        _autosave.Tick += (_, _) => AutoSave();
        _autosave.Start();

        _automation = new AutomationRuntime(_scorebug, _publishedEngine, _engine);
        _program = new ProgramSelection(_scorebug);
        _program.Changed += ProgramChanged;
        // The preview is the staging desk: it must show the values the operator is about to put on
        // air, including the Yards/Goal/PAT choice, while the monitor below stays on the published
        // copy. Reading the published engine here made the preview lag one Update Output behind.
        _preview = new ScorebugCanvasView(_scorebug, _engine, stateProvider: () => _engine.State, playback: _playback, layoutProvider: () => _program.Layout, displayId: _program.DisplayId, automation: _automation);
        PreviewHost.Content = _preview;
        // The monitor reads the published engine and the published layout, so it is a real
        // reflection of the audience window rather than another staged preview.
        _audience = new AudiencePreview(_scorebug, _publishedEngine, _playback, _program.DisplayId, _automation);
        AudienceHost.Content = _audience;
        RefreshEndGameButton();
        SyncDisplayPicker();
        RefreshLayoutIndicators();
        ShowTab(GameControlWindow.ConsoleTab.Control);
        // The theme pass repaints every button when the window loads, which used to wipe the green
        // outline off the already-selected tab until another tab was clicked.
        Loaded += (_, _) =>
        {
            ApplyTabVisuals();
            // The remote and the update check belong to a live window, not to the launch sequence.
            if (!waitForLaunch) { StartRemote(); StartHardware(); _leagueCloud?.Start(); if (CheckForUpdatesOnLoad) _ = CheckForUpdatesAsync(); }
        };

        _panel = new ControlPanelView(_engine, this);
        ControlHost.Content = _panel;
        _panel.SetHintVisibility(_preferences.ShowHeadingHints);

        RosterControl.Attach(_engine);
        RosterControl.ApplyTheme(_preferences.Theme);
        RosterControl.SetHintVisibility(_preferences.ShowHeadingHints);
        _macroDock = new MacroDock(_engine, _session, _preferences.Theme, _panel.Execute);
        _macroDock.Status += message => StatusText.Text = message;
        MacroHost.Content = _macroDock;
        MediaControl.Attach(_session);
        MediaControl.ApplyTheme(_preferences.Theme);
        // The dock now lives in a full-width tab instead of a narrow rail, so its controls are the
        // same size as the console's.
        MediaControl.SetLargeMode(true);
        // The phone's Media and Roster tabs read and drive the same library and game as the desk.
        RemoteControlServer.MediaJson = MediaSnapshotJson;
        RemoteControlServer.MediaAction = MediaAction;
        RemoteControlServer.RosterJson = RosterSnapshotJson;
        RemoteControlServer.RosterAction = RosterAction;
        RemoteControlServer.PenaltyAction = PenaltyAction;
        RemoteControlServer.LockState = () => _operatorLocked;
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
        _gameStarted = true;

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
        _gameStarted = true;
        SetWorkspaceEnabled(true);
        Activate();
        StartRemote();
        StartHardware();
        _leagueCloud?.Start();
        if (CheckForUpdatesOnLoad) _ = CheckForUpdatesAsync();
    }

    /// <summary>Locks or unlocks everything inside the window while the launch box is up.</summary>
    void SetWorkspaceEnabled(bool enabled)
    {
        _workspaceEnabled = enabled;
        Root.IsEnabled = enabled && !_operatorLocked;
        if (LockButton is not null) LockButton.IsEnabled = enabled && !_operatorLocked;
        // A locked window reads as dimmed, so nobody tries to type into a game that is not there.
        Root.Opacity = _operatorLocked ? 0.35 : enabled ? 1 : 0.55;
    }

    /// <summary>Locks the desk after asking for the password, so a passer-by cannot lock it by accident.</summary>
    void Lock_Click(object sender, RoutedEventArgs e)
    {
        if (_operatorLocked) return;
        if (string.IsNullOrWhiteSpace(_preferences.OperatorLockPasswordHash))
        {
            var created = OperatorLockWindow.Create(this, _preferences.Theme);
            if (created is null) return;
            _preferences.OperatorLockPasswordHash = OperatorLock.Hash(created);
            PreferencesStore.Save(_preferences);
            SetOperatorLocked(true);
            return;
        }
        if (!OperatorLockWindow.Confirm(this, _preferences.Theme, _preferences.OperatorLockPasswordHash,
                "LOCK SCOREPLUS", "Enter the operator lock password to lock the console, media, rosters and remote.")) return;
        SetOperatorLocked(true);
    }

    void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (!OperatorLock.Matches(UnlockPasswordInput.Password, _preferences.OperatorLockPasswordHash))
        {
            UnlockError.Text = "That password does not match the operator lock.";
            UnlockPasswordInput.SelectAll();
            UnlockPasswordInput.Focus();
            return;
        }
        SetOperatorLocked(false);
    }

    void UnlockPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Unlock_Click(sender, e);
    }

    /// <summary>Disables the operator surface, and every detached control panel, behind the lock.</summary>
    void SetOperatorLocked(bool locked)
    {
        _operatorLocked = locked;
        Root.IsEnabled = _workspaceEnabled && !locked;
        LockButton.IsEnabled = _workspaceEnabled && !locked;
        Root.Opacity = locked ? 0.35 : _workspaceEnabled ? 1 : 0.55;
        LockOverlay.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        LockButton.Content = locked ? "🔓" : "🔒";
        foreach (var panel in _controlPanels) panel.IsEnabled = !locked;
        if (locked)
        {
            UnlockPasswordInput.Clear();
            UnlockError.Text = string.Empty;
            Dispatcher.BeginInvoke(new Action(() => UnlockPasswordInput.Focus()));
        }
        else
        {
            UnlockPasswordInput.Clear();
            UnlockError.Text = string.Empty;
            _panel.Refresh();
        }
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
    /// <summary>The library as the phone's Media tab lists it.</summary>
    string MediaSnapshotJson()
    {
        // Every independent source can be live at once — clips, cameras and microphones — so the
        // phone lists them all. A paused clip is reported as paused, not as live; speakers are a
        // separate on-air row because a speaker bus is a route, not a playing clip.
        var live = new List<string>();
        var paused = new List<string>();
        void Add(List<string> list, string? label)
        {
            if (!string.IsNullOrWhiteSpace(label) && !live.Contains(label) && !paused.Contains(label)) list.Add(label);
        }
        if (_session.IsCurrentPlaying) Add(live, _session.Current?.Label);
        else Add(paused, _session.Current?.Label);
        if (_session.IsBackgroundPlaying) Add(live, _session.BackgroundAudio?.Label);
        else Add(paused, _session.BackgroundAudio?.Label);
        if (_session.Playback.IsPlaying)
            foreach (var file in _session.Playback.ActiveFiles) Add(live, file.Label);
        else
            foreach (var file in _session.Playback.ActiveFiles) Add(paused, file.Label);
        foreach (var camera in _session.LiveCameras)
            if (!Same(_session.Current, camera)) Add(live, camera.Label);
        foreach (var microphone in _session.LiveMicrophones)
            if (!Same(_session.Current, microphone)) Add(live, microphone.Label);
        var speakers = _session.LiveSpeakers.Select(speaker => speaker.Label).ToList();
        static bool Same(MediaEntry? left, MediaEntry right) =>
            left is not null && string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
        return JsonSerializer.Serialize(new
        {
            current = string.Join(" + ", live.Count > 0 ? live : paused),
            live,
            paused,
            speakers,
            clips = _session.Library.Select(entry => new
            {
                id = entry.Path,
                label = entry.Label,
                kind = entry.KindLabel,
                onAir = _session.IsOnAir(entry)
            }).ToList()
        });
    }

    /// <summary>The three media buttons a phone can reach: on air, play/pause, and back off air.</summary>
    bool MediaAction(string action, string id)
    {
        var entry = _session.Library.FirstOrDefault(item => string.Equals(item.Path, id, StringComparison.OrdinalIgnoreCase));
        switch (action)
        {
            case "toggle": _session.TogglePlayPause(); return true;
            case "play" when entry is not null: _session.Play(entry); return true;
            case "onair" when entry is not null: _session.ToggleOnAir(entry); return true;
            default: return false;
        }
    }

    /// <summary>Both rosters, their players and the library, as the phone's Roster tab lists them.</summary>
    string RosterSnapshotJson()
    {
        var s = _engine.State;
        object Side(bool home)
        {
            var rosterId = home ? s.HomeRosterId : s.AwayRosterId;
            var roster = RosterStore.Book.Find(rosterId);
            var selected = (home ? s.HomePlayer : s.AwayPlayer)?.PlayerId;
            return new
            {
                name = home ? s.HomeTeam : s.AwayTeam,
                rosterId,
                rosterName = roster?.Name ?? string.Empty,
                players = (roster?.Players ?? []).Select(player => new
                {
                    id = player.Id,
                    name = player.Name,
                    number = player.Number,
                    position = player.Position,
                    group = player.Group,
                    stats = player.Stats.Select(stat => new { name = stat.Name, value = stat.Value, display = stat.Display }).ToList(),
                    selected = string.Equals(player.Id, selected, StringComparison.OrdinalIgnoreCase)
                }).ToList()
            };
        }
        return JsonSerializer.Serialize(new
        {
            home = Side(true),
            away = Side(false),
            rosters = RosterStore.All().Select(roster => new { id = roster.Id, name = roster.Name }).ToList()
        });
    }

    /// <summary>Assigns a roster or puts one player on the graphic, from the phone.</summary>
    bool RosterAction(string action, string side, string id, string stat, string value)
    {
        var home = !string.Equals(side, "away", StringComparison.OrdinalIgnoreCase);
        switch (action)
        {
            case "roster": _engine.AssignRoster(home, string.IsNullOrWhiteSpace(id) ? null : id); return true;
            case "player":
                // No player id is the phone's off-air button: nobody on the graphic for that side.
                if (string.IsNullOrWhiteSpace(id)) _engine.ClearSelectedPlayer(home);
                else _engine.SelectPlayer(home, id);
                return true;
            case "clear": _engine.ClearSelectedPlayer(home); return true;
            case "stat":
            {
                if (string.IsNullOrWhiteSpace(stat) || !int.TryParse(value, out var number)) return false;
                var roster = RosterStore.Book.Find(home ? _engine.State.HomeRosterId : _engine.State.AwayRosterId);
                var player = roster?.Find(id);
                if (player is null) return false;
                player.SetStat(stat, number);
                RosterStore.Save();
                // The graphic prints a snapshot of the selected player, so an edit to the player on
                // air has to be re-resolved or the phone would change the number backstage only.
                _engine.ReloadSelectedPlayer(home);
                return true;
            }
            default: return false;
        }
    }

    /// <summary>
    /// The phone's flag card. Choosing the type or the side leaves the flag exactly as it is, so a
    /// penalty can be corrected on air without the mark flickering off between two taps.
    /// </summary>
    bool PenaltyAction(string action, string type, string side)
    {
        var home = !string.Equals(side, "away", StringComparison.OrdinalIgnoreCase);
        var chosen = string.IsNullOrWhiteSpace(type) ? _engine.State.PenaltyType : type;
        switch (action)
        {
            case "flag": _engine.SetPenalty(!_engine.State.PenaltyActive, chosen, home); return true;
            case "clear": _engine.SetPenalty(false, chosen, home); return true;
            case "type":
            case "team": _engine.SetPenalty(_engine.State.PenaltyActive, chosen, home); return true;
            default: return false;
        }
    }

    public void PublishGameState()
    {
        // Remember the down going to air, so Last down can put back the one before it.
        _engine.MarkPublished();
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
        // Arm the hand-over video while the operator is still looking at the preview. If they wait
        // a beat before Update Output, the first frame is decoded and the cut does not start with a
        // three-second black gap. The preview is armed before its own rebuild so its swap can
        // consume the prepared player too.
        PrepareTransitionForStagedLayout();
        _preview?.Rebuild();
    }

    void PrepareTransitionForStagedLayout()
    {
        _preview?.PrepareTransition(_program.Layout);
        // A link stages more than the previewed display. Arm every visible output for the layout
        // it will actually publish, so a linked screen gets the same preloaded hand-over.
        foreach (var output in _outputs.Where(output => output.IsVisible))
            if (_program.LayoutFor(output.DisplayId) is { } layout)
                output.PrepareTransition(layout);
    }

    /// <summary>Fills the display picker without treating the sync as the operator's choice.</summary>
    void SyncDisplayPicker()
    {
        _displaySync = true;
        try
        {
            var names = _scorebug.Displays.Select((display, index) => $"{index + 1} · {display.Name}").ToArray();
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

    void MacrosTab_Click(object sender, RoutedEventArgs e) => ShowTab(GameControlWindow.ConsoleTab.Macros);

    /// <summary>Switches the operator surface between the console, media, roster and macros.</summary>
    void ShowTab(GameControlWindow.ConsoleTab tab)
    {
        // Leaving the macro editor is a natural save point; the operator should not lose the
        // name or step they just typed by clicking another tab.
        if (_tab == GameControlWindow.ConsoleTab.Macros && tab != GameControlWindow.ConsoleTab.Macros)
            _macroDock?.CommitPendingEdits();
        _tab = tab;
        ControlHost.Visibility = tab == GameControlWindow.ConsoleTab.Control ? Visibility.Visible : Visibility.Collapsed;
        MediaControl.Visibility = tab == GameControlWindow.ConsoleTab.Media ? Visibility.Visible : Visibility.Collapsed;
        RosterControl.Visibility = tab == GameControlWindow.ConsoleTab.Rosters ? Visibility.Visible : Visibility.Collapsed;
        MacroHost.Visibility = tab == GameControlWindow.ConsoleTab.Macros ? Visibility.Visible : Visibility.Collapsed;
        ApplyTabVisuals();
        if (tab == GameControlWindow.ConsoleTab.Media) MediaControl.RefreshView();
        if (tab == GameControlWindow.ConsoleTab.Rosters) RosterControl.RefreshView();
        if (tab == GameControlWindow.ConsoleTab.Macros) _macroDock?.RefreshView();
    }

    /// <summary>
    /// Paints the active tab. Split out because the theme pass repaints every button when the
    /// window loads, which silently erased the green outline on the tab that was already selected.
    /// </summary>
    void ApplyTabVisuals()
    {
        // Active-tab colours follow the operator's accent from Preferences and the active theme.
        var light = ThemeManager.Resolve(_preferences.Theme) == "Light";
        var accent = UiPalette.Accent(_preferences);
        var on = light
            ? UiPalette.Frozen(UiPalette.Mix(accent, Colors.White, 0.78))
            : UiPalette.Frozen(UiPalette.Soft(accent));
        var off = (Brush)new BrushConverter().ConvertFromString(light ? "#F3F5F8" : "#141A23")!;
        var onBorder = UiPalette.Frozen(accent);
        var offBorder = (Brush)new BrushConverter().ConvertFromString(light ? "#C9D2DC" : "#28323F")!;
        var muted = (Brush)new BrushConverter().ConvertFromString(light ? "#586576" : "#93A0B2")!;
        var onText = (Brush)new BrushConverter().ConvertFromString(light ? "#18202B" : "#F4F7FB")!;
        Button[] tabs = [ControlTab, MediaTab, RosterTab, MacrosTab];
        var active = (int)_tab;
        for (var i = 0; i < tabs.Length; i++)
        {
            tabs[i].Background = i == active ? on : off;
            tabs[i].BorderBrush = i == active ? onBorder : offBorder;
            tabs[i].Foreground = i == active ? onText : muted;
        }
    }

    void LayoutPrev_Click(object sender, RoutedEventArgs e) => ShiftLayout(-1);

    void LayoutNext_Click(object sender, RoutedEventArgs e) => ShiftLayout(1);

    // ---------- window chrome ----------
    void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        RefreshEndGameButton();
        var s = _engine.State;
        if (SportSelector.SelectedValue is not string selectedId || selectedId != s.SportId)
        {
            _sportSync = true;
            try { SportSelector.SelectedValue = s.SportId; }
            finally { _sportSync = false; }
        }
        // Follow a new or loaded game without overwriting a name the operator is currently typing.
        if (GameNameBox is not null && !GameNameBox.IsKeyboardFocusWithin && GameNameBox.Text != s.GameName)
        {
            _gameNameSync = true;
            try { GameNameBox.Text = s.GameName; }
            finally { _gameNameSync = false; }
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
        SoundLevelText.Text = SoundLevelMeter.Suppressed
            ? "speakers off"
            : level <= 0
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
        var studio = new ScorebugEditorWindow(_scorebug, _engine, _playback, _preferences.Theme, _automation) { Owner = this };
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
        var maker = new MotionMakerWindow(kind, _scorebug, _engine, _preferences.Theme, layoutId: _program.Layout.Id) { Owner = this };
        maker.ShowDialog();
        _preview?.Rebuild();
        RefreshLayoutIndicators();
        _panel.Refresh();
        foreach (var panel in _controlPanels.Where(p => p.IsVisible)) panel.Panel.Refresh();
        _quickLayouts?.Refresh();
    }

    void TransitionMaker_Click(object sender, RoutedEventArgs e) => OpenMotionMaker(MotionKind.Transition);

    void AnimationMaker_Click(object sender, RoutedEventArgs e) => OpenMotionMaker(MotionKind.Animation);

    void AdLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AdLogWindow(_preferences.Theme) { Owner = this };
        dialog.ShowDialog();
    }

    void Hardware_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HardwareWindow(_preferences, _hardware) { Owner = this };
        ThemeManager.Apply(dialog, _preferences.Theme);
        dialog.ShowDialog();
    }

    /// <summary>Applies a controller reading and, when live publishing is on, sends it to air.</summary>
    void ApplyHardware(DeviceSnapshot snapshot)
    {
        if (!_engine.ApplyHardwareSnapshot(snapshot) || !_liveScoreClock) return;
        if (!_publishedEngine.UpdateLiveController(_engine.State)) return;
        _preview?.Refresh();
        foreach (var output in _outputs.Where(output => output.IsVisible)) output.UpdateLiveController(_engine.State);
    }

    /// <summary>Starts the controller saved in Preferences when the operator asked for it.</summary>
    void StartHardware()
    {
        if (!_preferences.Hardware.AutoConnect || _hardware.IsRunning) return;
        _hardware.Start(_preferences.Hardware);
    }

    void Info_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutWindow(_preferences.Theme) { Owner = this };
        dialog.ShowDialog();
    }

    void Preferences_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_preferences) { Owner = this };
        ThemeManager.Apply(dialog, _preferences.Theme);
        if (dialog.ShowDialog() != true) return; // Cancel discards: nothing is applied or saved.
        ThemeManager.Apply(this, _preferences.Theme);
        _leagueCloud?.ApplySettings();
        // The new theme paints buttons as it runs; put the active tab's outline back on top.
        ApplyTabVisuals();
        MediaControl.ApplyTheme(_preferences.Theme);
        RosterControl.ApplyTheme(_preferences.Theme);
        _macroDock?.ApplyTheme(_preferences.Theme);
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
        RemoteControlServer.WatchSource = null;
        RemoteControlServer.WatchImage = null;
        RemoteControlServer.LockState = () => _operatorLocked;
        if (!_preferences.RemoteEnabled || string.IsNullOrWhiteSpace(_preferences.RemotePassword)) return;
        var server = new RemoteControlServer(_engine, this, id => { _panel.Execute(id); return true; }, Dispatcher, _preferences.RemotePort, _preferences.RemotePassword);
        // The audience feed draws the published program, on the UI thread: the program selection and
        // the published game belong to this window, and the server asks from a background thread.
        RemoteControlServer.WatchImage = display => Dispatcher.Invoke(() => WatchImageSnapshot(display));
        server.Status += message => Dispatcher.BeginInvoke(new Action(() => StatusText.Text = message));
        server.Start();
        _remote = server;
    }

    /// <summary>What a browser watching /watch should see: the program layout and the published game.</summary>
    (ScorebugLayout Layout, GameState State)? WatchSnapshot() =>
        (_automation.Effective(_program.DisplayId, _scorebug.PublishedCompositeOf(_program.DisplayId)), _publishedEngine.State);

    /// <summary>
    /// The program graphic as a PNG, drawn by the same view the audience window draws. A wall board
    /// then gets the real fonts, palette colours, logos and made icons, and the page only downloads
    /// a new frame when the artwork or the published game changed.
    /// </summary>
    (byte[] Png, long Version)? WatchImageSnapshot(string? displayKey)
    {
        var display = ResolveWatchDisplay(displayKey);
        if (display is null) return null;
        // The wall board shows what the audience output is actually sending, not the layout that is
        // still being staged in the studio preview.
        var layout = _automation.Effective(display.Id, _scorebug.PublishedCompositeOf(display.Id));
        var state = _publishedEngine.State;
        var signature = JsonSerializer.Serialize(layout) + "|" + GameEngine.StagedSignature(state);
        if (!_watchFrames.TryGetValue(display.Id, out var cached) || !string.Equals(cached.Signature, signature, StringComparison.Ordinal))
        {
            _watchVersion++;
            cached = (signature, null, _watchVersion);
        }
        cached.Png ??= RenderProgramPng(layout);
        if (cached.Png is null) return null;
        _watchFrames[display.Id] = cached;
        return (cached.Png, cached.Version);
    }

    /// <summary>Resolves /watch?display=2, a display id, or a display name; no key means this window's display.</summary>
    ScorebugDisplay? ResolveWatchDisplay(string? key)
    {
        if (_scorebug.Displays.Count == 0) return null;
        var fallback = _scorebug.FindDisplay(_program.DisplayId) ?? _scorebug.Displays[0];
        if (string.IsNullOrWhiteSpace(key)) return fallback;
        string clean;
        try { clean = Uri.UnescapeDataString(key).Trim(); }
        catch { clean = key.Trim(); }
        if (int.TryParse(clean, out var number) && number >= 1 && number <= _scorebug.Displays.Count)
            return _scorebug.Displays[number - 1];
        return _scorebug.FindDisplay(clean)
            ?? _scorebug.Displays.FirstOrDefault(display => string.Equals(display.Name, clean, StringComparison.OrdinalIgnoreCase))
            ?? fallback;
    }

    /// <summary>One off-screen render of the program layout, encoded as PNG for the browser.</summary>
    byte[]? RenderProgramPng(ScorebugLayout layout)
    {
        ScorebugCanvasView? view = null;
        try
        {
            // A 1280×720 layout rendered at 1280×720 looked soft on a wall board. Render at up to
            // three times the design size (capped at 4K wide), so the PNG has real pixels for a
            // large screen instead of a browser stretching the same small bitmap.
            var scale = Math.Clamp((int)Math.Ceiling(1920.0 / Math.Max(1, layout.Width)), 1, 3);
            if (layout.Width * scale > 3840) scale = 1;
            var width = Math.Max(1, layout.Width) * scale;
            var height = Math.Max(1, layout.Height) * scale;
            view = new ScorebugCanvasView(_scorebug, _engine,
                stateProvider: () => _publishedEngine.State, playback: null, layoutProvider: () => layout)
            {
                Width = width,
                Height = height
            };
            view.Measure(new Size(width, height));
            view.Arrange(new Rect(0, 0, width, height));
            view.UpdateLayout();
            RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.HighQuality);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            bitmap.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var buffer = new MemoryStream();
            encoder.Save(buffer);
            return buffer.ToArray();
        }
        catch { return null; }
        finally { view?.Dispose(); }
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
        var shown = manifest.Label ?? manifest.Version;
        var detail = $"Version {shown} is ready (this build is {AppInfo.Release}). " +
                     (string.IsNullOrWhiteSpace(manifest.Notes) ? "Download and install it now?" : $"{manifest.Notes}\n\nDownload and install it now?");
        if (!ConfirmWindow.Ask(this, _preferences.Theme, "UPDATE AVAILABLE", detail, "Download and install")) return;
        StatusText.Text = $"UPDATING · downloading {shown}";
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
            SetLiveScoreClock, () => _liveScoreClock, _session, _preferences.Theme, _automation);
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

    void GameName_Changed(object sender, TextChangedEventArgs e)
    {
        if (_initializing || _gameNameSync) return;
        _engine.SetGameName(GameNameBox.Text);
    }

    void Sport_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _sportSync || SportSelector.SelectedValue is not string id) return;
        // A sport switch starts a brand new game, so anything the old sport was tracking is about to
        // go. Say so plainly instead of deleting a period's worth of counts without a word.
        if (WarnOnSportSwitch && _engine.HasSportSpecificData())
        {
            var next = _engine.Sports.Find(id)?.Name ?? id;
            var detail = $"Switching to {next} starts a new game. The {_engine.Sport.Name} values this game is tracking — its counts, flags and figures — are cleared with it, along with the score, clocks and custom values. The roster library, sport profiles, media and your layouts stay.";
            if (!ConfirmWindow.Ask(this, _preferences.Theme, "SWITCH SPORT", detail, "Switch sport", destructive: true))
            {
                // Put the picker back on the sport being played; nothing has been touched yet.
                _sportSync = true;
                try { SportSelector.SelectedValue = _engine.State.SportId; }
                finally { _sportSync = false; }
                return;
            }
        }
        // A sport switch changes the sport, not the show: the artwork and the teams carry over.
        StartNewGame(id, announce: false, keepDesign: true);
        StatusText.Text = $"NEW {_engine.Sport.Name.ToUpperInvariant()} GAME";
    }

    /// <summary>
    /// The header's game button follows the game: End Game until the final, then Restart Game,
    /// which starts the same fixture over without losing the teams.
    /// </summary>
    void RefreshEndGameButton()
    {
        if (EndGameButton is null) return;
        // A clock-expired game can be final without its records being settled when automatic
        // records are off. Keep offering End Game in that state so the result can still be applied;
        // only a fully settled final becomes Restart Game.
        var final = _engine.State.GameEnded && _engine.State.RecordSettled;
        var content = final ? "Restart Game" : "End Game";
        if (!Equals(EndGameButton.Content, content)) EndGameButton.Content = content;
        EndGameButton.ToolTip = final
            ? "Start the same game over: the score, clocks and game stats reset; the teams, their records and the design stay"
            : _engine.State.GameEnded
                ? "Add this final result to each team's record. A team with no record is left alone"
                : "Stop every clock, mark the game final and add the result to each team's record";
    }

    void NewGame_Click(object sender, RoutedEventArgs e)
    {
        // The sport is part of starting a game; the chooser also states what a new game resets.
        var picker = new SportPickerWindow(_engine.Sports, _engine.State.SportId, _preferences.Theme) { Owner = this };
        if (picker.ShowDialog() != true || string.IsNullOrWhiteSpace(picker.SportId)) return;
        StartNewGame(picker.SportId);
    }

    /// <summary>
    /// Ends the game on command, for a game called before the clock expires. It stops every clock
    /// and adds the result to the records that are set; the confirmation says that plainly.
    /// </summary>
    void EndGame_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.State.GameEnded && _engine.State.RecordSettled)
        {
            // A fully settled final restarts the same fixture instead of ending it a second time.
            // The score and clocks start over; the teams, their details and the design stay.
            if (!ConfirmWindow.Ask(this, _preferences.Theme, "RESTART GAME",
                "This starts the same game over. The score, clocks, period and this game's stats are cleared. The teams — names, colours, logos, records and selected players — and the scorebug design stay.",
                "Restart game")) return;
            StartNewGame(_engine.State.SportId, announce: false, keepDesign: true, restart: true);
            StatusText.Text = "GAME RESTARTED · same teams, fresh score and clock";
            AutoSave();
            return;
        }
        var detail = "This stops every clock and marks the game final. The result is added to each team's record that is already set — a win, loss or tie. A team with no record is left alone.";
        if (!ConfirmWindow.Ask(this, _preferences.Theme, "END GAME", detail, "End game")) return;
        var homeRecord = _engine.State.HomeRecord;
        var awayRecord = _engine.State.AwayRecord;
        var wasSettled = _engine.State.RecordSettled;
        if (!_engine.EndGame())
        {
            StatusText.Text = "GAME ENDED · start a New Game to play again";
            return;
        }
        StatusText.Text = _engine.State.ClockMessage == GameClockMessage.EndOfRegulation
            ? "TIED · END OF REG. · advance to overtime, or choose a tie period in Preferences"
            : homeRecord != _engine.State.HomeRecord || awayRecord != _engine.State.AwayRecord
                ? "GAME ENDED · team records updated"
                : wasSettled ? "GAME ENDED · records were already updated" : "GAME ENDED · no team records were set";
        // Keep the final state in the recovery point immediately, not at the next autosave tick.
        AutoSave();
    }

    /// <summary>
    /// A new game resets every piece of game state and takes the audience output with it, so nothing
    /// from the previous game survives on air. Operator settings that are not part of a game — sport
    /// profiles, keybinds, preferences, the media library and the scorebug design — are untouched.
    /// </summary>
    public void StartNewGame(string sportId, bool announce = true, bool keepDesign = false, bool restart = false)
    {
        _autoHornReady = false;
        // keepDesign is the sport switch: the teams, their rosters and the scorebug layout stay.
        if (restart)
        {
            // A restart keeps the fixture and the design; only the game itself starts over.
            if (!_engine.RestartGame()) return;
        }
        else _engine.NewGame(sportId, keepTeamIdentity: keepDesign);
        _projectPath = null;
        _startNewAutosavePoint = true;
        _savedStateJson = _engine.ExportJson();
        _preferences.LastSport = sportId;
        try { PreferencesStore.Save(_preferences); } catch { }
        // A new game really starts over: one factory layout and no folder/playlist organisation.
        // Imported media files stay in the library; the game's design and shelves begin blank.
        if (!keepDesign) _scorebug.ResetToDefault();
        _savedDesignJson = DesignJson();
        _program.Sync();
        SyncDisplayPicker();
        if (!restart) _session.ResetOrganization();
        if (!restart) MediaControl.ResetView();
        if (!restart) foreach (var panel in _controlPanels.Where(panel => panel.IsVisible)) panel.Media.ResetView();
        ResetOutputToCurrentGame();
        RefreshLayoutIndicators();
        _macroDock?.RefreshView(force: true);
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
        _autoHornReady = false;
        try
        {
            var design = _engine.Load(path);
            // A file written by an older build has no design; the current scorebug then stays as it
            // is rather than being replaced by a factory layout.
            if (design is not null) _scorebug.ApplyDocument(design);
            ApplySavedQuickButtons();
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
            _macroDock?.RefreshView(force: true);
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

    void Annotate_Click(object sender, RoutedEventArgs e) => OpenOutputWindow(annotations: true);

    AnnotationBoard BoardFor(string displayId)
    {
        if (_annotationBoards.TryGetValue(displayId, out var board)) return board;
        var seconds = PreferencesStore.Load().AnnotationFadeSeconds;
        var hold = seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Clamp(seconds, 0.5, 10));
        board = new AnnotationBoard(hold);
        _annotationBoards[displayId] = board;
        return board;
    }

    /// <summary>
    /// Asks which display this audience window is for, then opens a brand-new window for it. With
    /// only one display there is nothing to ask, so the question is skipped. Annotation reuses an
    /// audience window already open for that display, and mirrors its strokes to every other output
    /// on the same display, so the pen is genuinely on the audience picture.
    /// </summary>
    public OutputWindow? OpenOutputWindow(string? displayId = null, bool annotations = false)
    {
        if (displayId is null)
        {
            if (_scorebug.Displays.Count == 1) displayId = _scorebug.Displays[0].Id;
            else
            {
                displayId = annotations
                    ? DisplayPickerWindow.Ask(this, _scorebug, _preferences.Theme, _program.DisplayId,
                        title: "Operator Draw Window",
                        heading: "Which display are you drawing on?",
                        detail: "The pen and highlighter appear on this display's audience picture. Opening the draw window again reuses an output already showing that display.",
                        confirm: "Open draw window")
                    : DisplayPickerWindow.Ask(this, _scorebug, _preferences.Theme, _program.DisplayId);
                if (displayId is null) return null;
            }
        }
        var board = BoardFor(displayId);
        if (annotations)
        {
            var existing = _outputs.FirstOrDefault(output => output.IsVisible
                && string.Equals(output.DisplayId, displayId, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                board.Open();
                foreach (var other in _outputs.Where(output => output.IsVisible
                    && string.Equals(output.DisplayId, displayId, StringComparison.OrdinalIgnoreCase)))
                    other.ShowAudienceInk();
                existing.OpenAnnotationTools();
                existing.Activate();
                return existing;
            }
        }
        var output = new OutputWindow(_publishedEngine, _scorebug, _preferences.Theme, _playback, displayId, _automation, annotations, board);
        _outputs.Add(output);
        output.Closed += (_, _) => _outputs.Remove(output);
        output.Show();
        if (string.Equals(displayId, _program.DisplayId, StringComparison.OrdinalIgnoreCase))
            output.PrepareTransition(_program.Layout);
        if (annotations)
        {
            foreach (var other in _outputs.Where(candidate => !ReferenceEquals(candidate, output) && candidate.IsVisible
                && string.Equals(candidate.DisplayId, displayId, StringComparison.OrdinalIgnoreCase)))
                other.ShowAudienceInk();
        }
        else if (board.IsOpen)
        {
            output.ShowAudienceInk();
        }
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

    void EngineChanged()
    {
        CheckAutoHorns();
        // New series can ask to go on air by itself; publish the whole staged state in that case.
        if (_engine.TakePublishRequest()) { PublishGameState(); return; }
        if (_liveScoreClock) PublishLiveValues();
    }

    /// <summary>
    /// The clock ticks and the hardware feed both raise Changed. Arm each expiry only while its
    /// clock was running, so loading a game at 0:00 or typing 0 into a stopped clock is silent.
    /// </summary>
    void CheckAutoHorns()
    {
        var state = _engine.State;
        if (!_autoHornReady)
        {
            _autoHornReady = true;
            _lastGameClock = state.ClockSeconds;
            _lastPlayClock = state.PlayClockSeconds;
            _lastGameClockRunning = state.ClockRunning;
            _lastPlayClockRunning = state.PlayClockRunning;
            return;
        }

        var gameExpired = _preferences.HornOnGameClockZero
            && !state.ClockCountsUp
            && state.ClockSeconds == 0
            && _lastGameClock > 0
            && _lastGameClockRunning
            && !state.ClockRunning;
        var playExpired = _preferences.HornOnPlayClockZero
            && state.PlayClockSeconds == 0
            && _lastPlayClock > 0
            && _lastPlayClockRunning
            && !state.PlayClockRunning;
        if (gameExpired || playExpired)
            HornPlayer.Play(_preferences.HornSound, _preferences.HornCustomPath);

        _lastGameClock = state.ClockSeconds;
        _lastPlayClock = state.PlayClockSeconds;
        _lastGameClockRunning = state.ClockRunning;
        _lastPlayClockRunning = state.PlayClockRunning;
    }

    void PublishLiveValues()
    {
        // A connected console owns down and distance as well as the score and clocks, so an
        // operator's recall of the console's last down has to ride the same live path as the
        // console's own frames. Without this the audience kept the wrong down.
        if (_engine.HardwareControlled)
        {
            _publishedEngine.UpdateLiveController(_engine.State);
            _publishedEngine.UpdateLiveScoreClock(_engine.State);
            _preview?.Refresh();
            foreach (var output in _outputs.Where(o => o.IsVisible))
            {
                output.UpdateLiveController(_engine.State);
                output.UpdateLiveScoreClock(_engine.State);
            }
            return;
        }
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

    /// <summary>Copies the live quick buttons into the game state so they travel with the save.</summary>
    void CaptureQuickButtons()
    {
        // A macro edit is part of the game too. Commit the visible editor before writing the file,
        // otherwise a name, colour or step added since the last Save macro would be left behind.
        _macroDock?.CommitPendingEdits();
        var preferences = PreferencesStore.Load();
        _engine.State.QuickLayouts = (preferences.QuickLayouts ?? []).Select(binding => new SavedQuickLayout
        {
            Label = binding.Label,
            DisplayId = binding.DisplayId,
            LayoutId = binding.LayoutId
        }).ToList();
        _engine.State.QuickMedia = (preferences.QuickMedia ?? []).Select(binding => new SavedQuickMedia
        {
            Label = binding.Label,
            Path = binding.Path
        }).ToList();
        _engine.State.QuickButtonsSaved = true;
    }

    /// <summary>Restores the quick buttons a loaded game carried, and refreshes both bars.</summary>
    void ApplySavedQuickButtons()
    {
        var layouts = _engine.State.QuickLayouts ?? [];
        var media = _engine.State.QuickMedia ?? [];
        // A save from before this feature carries neither list; keep the operator's current buttons.
        if (!_engine.State.QuickButtonsSaved && layouts.Count == 0 && media.Count == 0) return;
        var preferences = PreferencesStore.Load();
        preferences.QuickLayouts = layouts.Select(binding => new QuickLayoutBinding
        {
            Label = binding.Label,
            DisplayId = binding.DisplayId,
            LayoutId = binding.LayoutId
        }).ToList();
        preferences.QuickMedia = media.Select(binding => new QuickMediaBinding
        {
            Label = binding.Label,
            Path = binding.Path
        }).ToList();
        PreferencesStore.Save(preferences);
        _preferences.QuickLayouts = preferences.QuickLayouts;
        _preferences.QuickMedia = preferences.QuickMedia;
        _quickLayouts?.Refresh();
        _quickMedia?.Refresh();
    }

    /// <summary>Writes the game and remembers that this is now the saved state.</summary>
    bool SaveGameTo(string path)
    {
        try
        {
            // The game travels with its screens: every display, every layout and what each one has
            // on air, so opening the file puts the show back exactly where it was left.
            CaptureQuickButtons();
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
        // Make pending macro edits visible to the unsaved-changes check. A close prompt can then
        // offer to save them instead of silently dropping the operator's last macro change.
        if (!_closingAfterPrompt) _macroDock?.CommitPendingEdits();
        if (PromptOnExit && _gameStarted && !_closingAfterPrompt && HasUnsavedChanges())
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

    /// <summary>Opens the multiview: every camera live, with the replay preview beside it.</summary>
    void Multiview_Click(object sender, RoutedEventArgs e)
    {
        var window = new MultiviewWindow(_session, _preferences, _scorebug, _engine, _publishedEngine, _program, _automation, _playback) { Owner = this };
        window.Show();
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
        var minutes = _preferences.AutosaveMinutes;
        if (minutes <= 0)
        {
            AutosaveText.Text = "AUTOSAVE OFF";
            return;
        }

        var now = DateTime.Now;
        // A new game gets its own recovery point straight away. After that the operator's chosen
        // interval decides when the next point is written.
        if (!_startNewAutosavePoint && _lastAutosaveAt != default
            && now - _lastAutosaveAt < TimeSpan.FromMinutes(minutes)) return;

        try
        {
            CaptureQuickButtons();
            AutosaveStore.Save(_engine, _startNewAutosavePoint, _scorebug.ExportDocument());
            _startNewAutosavePoint = false;
            _lastAutosaveAt = now;
            AutosaveText.Text = $"AUTOSAVED {now:HH:mm:ss}";
        }
        catch (Exception ex) { StatusText.Text = $"AUTOSAVE WARNING · {ex.Message}"; }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // A locked desk must not answer a keybind either: the overlay covers the console, but a
        // window-level shortcut would still change the game behind it while nobody is watching.
        if (_operatorLocked)
        {
            base.OnPreviewKeyDown(e);
            return;
        }
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

    void LeagueCloud_Click(object sender, RoutedEventArgs e)
    {
        if (_leagueCloud is null) return;
        var dialog = new LeagueCloudWindow(_leagueCloud, _preferences, _scorebug, _program) { Owner = this };
        dialog.ShowDialog();
        _leagueCloud.ApplySettings();
    }

    protected override void OnClosed(EventArgs e)
    {
        // Every timer belongs to this window. Leaving one running after the window is gone is a
        // common reason a WPF process stays in Task Manager, so stop them before tearing anything
        // else down.
        _timer.Stop();
        _autosave.Stop();
        _meterTimer.Stop();
        try { if (_gameStarted) AutoSave(); } catch { }

        // Each cleanup is independent: one native media or capture component failing must not skip
        // the rest and leave the process behind.
        try { _engine.Changed -= Refresh; _engine.Changed -= EngineChanged; } catch { }
        try { _leagueCloud?.Dispose(); } catch { }
        try { _hardware.Dispose(); } catch { }
        try { HornPlayer.Stop(); } catch { }
        try { _remote?.Dispose(); } catch { }
        try { _automation.Dispose(); } catch { }
        try { _program.Dispose(); } catch { }
        try { _preview?.Dispose(); } catch { }
        try { _session.Dispose(); } catch { }
        foreach (var panel in _controlPanels.ToArray()) try { panel.Close(); } catch { }
        foreach (var output in _outputs.ToArray()) try { output.Close(); } catch { }
        try { _log?.Close(); } catch { }
        _annotationBoards.Clear();
        base.OnClosed(e);
    }
}
