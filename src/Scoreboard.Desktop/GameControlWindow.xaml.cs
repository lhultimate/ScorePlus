using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// A detached copy of the control surface. The game, the media library and every audience output
/// are shared, but the display and layout this window is previewing are its own, so two panels can
/// line up two different screens at the same time.
/// </summary>
public partial class GameControlWindow : Window
{
    readonly GameEngine _engine;
    GameEngine? _publishedEngine;
    ScorebugCatalog? _catalog;
    ScorebugCanvasView? _preview;
    AudiencePreview? _audience;
    MacroDock? _macroDock;
    MediaSession? _ownedSession;
    // The rail's peak meter, redrawn a few times a second; the level itself is shared by every
    // window, just like the main window's rail.
    readonly Border[] _meterBars = new Border[22];
    readonly DispatcherTimer _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    int _meterLit = -1;
    bool _tabHook;
    bool _displaySync;
    ConsoleTab _tab = ConsoleTab.Control;

    /// <summary>The desks a control window can carry; the main window also hosts Macros.</summary>
    public enum ConsoleTab { Control, Media, Rosters, Macros }

    public ControlPanelView Panel { get; }

    /// <summary>This window's display, staged layouts and quick media. Null only in the minimal test host.</summary>
    QuickMediaBar? _quickMedia;
    QuickLayoutBar? _quickLayouts;

    public ProgramSelection? Program { get; private set; }

    /// <summary>The media dock in this window; every panel shares one session but keeps its own view.</summary>
    public MediaDock Media => MediaControl;

    /// <summary>The roster desk in this window; every panel shares one roster library.</summary>
    public RosterDock Rosters => RosterControl;

    public event Action? NewPanelRequested;

    public GameControlWindow(GameEngine engine, Action publishOutput, Action<bool> setLiveScoreClock, Func<bool> getLiveScoreClock, MediaSession? media = null)
        : this(engine, new DelegateHost { Publish = publishOutput, SetLive = setLiveScoreClock, GetLive = getLiveScoreClock }, media)
    {
    }

    /// <summary>The minimal host: no catalog, so no program strip. Used by the checks.</summary>
    public GameControlWindow(GameEngine engine, IControlPanelHost host, MediaSession? media = null)
    {
        InitializeComponent();
        ScreenFit.Track(this);
        _engine = engine;
        _publishedEngine = engine;
        if (host is DelegateHost detached) detached.NewPanel = () => NewPanelRequested?.Invoke();
        Panel = new ControlPanelView(engine, host);
        PanelHost.Content = Panel;
        RosterControl.Attach(engine);
        BuildSoundMeter();
        _meterTimer.Tick += (_, _) => UpdateSoundMeter();
        _meterTimer.Start();
        // A panel with no catalog has no display, layouts or quick buttons to reach. The real
        // detached panel always gets the main window's full rail. The minimal host is used by the
        // checks and by callers that only need the console, so it keeps the body compact.
        PreviewRail.Visibility = Visibility.Collapsed;
        PreviewColumn.MinWidth = 0;
        PreviewColumn.Width = new GridLength(0);
        SplitterColumn.Width = new GridLength(0);
        PreviewSplitter.Visibility = Visibility.Collapsed;
        QuickLayoutsHost.Visibility = Visibility.Collapsed;
        MacrosTab.Visibility = Visibility.Collapsed;
        AttachMedia(media);
        ApplyTheme(PreferencesStore.Load().Theme);
    }

    /// <summary>
    /// A full panel: it previews and publishes its own display, while the game data and the media
    /// session come from the main window.
    /// </summary>
    public GameControlWindow(GameEngine engine, GameEngine previewEngine, ScorebugCatalog catalog,
        Action publishGameState, Func<bool> hasStagedGame, Action<bool> setLiveScoreClock, Func<bool> getLiveScoreClock,
        MediaSession? media, string theme, AutomationRuntime? automation = null)
    {
        InitializeComponent();
        ScreenFit.Track(this);
        _engine = engine;
        _publishedEngine = previewEngine;
        _catalog = catalog;
        Program = new ProgramSelection(catalog);
        // The preview is staged; the small audience monitor below reads the published copy.
        _preview = new ScorebugCanvasView(catalog, engine, stateProvider: () => engine.State, playback: media?.Playback, layoutProvider: () => Program!.Layout, displayId: Program!.DisplayId, automation: automation);
        PreviewHost.Content = _preview;
        // The monitor reads the published engine and layout, so it reflects a real audience window.
        _audience = new AudiencePreview(catalog, previewEngine, media?.Playback, Program.DisplayId, automation);
        AudienceHost.Content = _audience;
        var host = new WindowHost(Program, publishGameState, hasStagedGame, setLiveScoreClock, getLiveScoreClock, () => NewPanelRequested?.Invoke());
        Panel = new ControlPanelView(engine, host);
        PanelHost.Content = Panel;
        Program.Changed += ProgramChanged;
        catalog.Changed += CatalogChanged;
        _engine.Changed += MeterChanged;
        if (!ReferenceEquals(previewEngine, engine)) previewEngine.Changed += MeterChanged;
        BuildSoundMeter();
        _meterTimer.Tick += (_, _) => UpdateSoundMeter();
        _meterTimer.Start();
        // The same quick media, quick layout and macro desks as the main window, on the same
        // preferences and the same media session.
        var session = media ?? new MediaSession(PreferencesStore.Load());
        if (media is null) _ownedSession = session;
        _quickMedia = new QuickMediaBar(session, theme);
        _quickMedia.Status += message => MediaStatus.Text = message;
        QuickMediaHost.Content = _quickMedia;
        _quickLayouts = new QuickLayoutBar(catalog, engine, theme);
        _quickLayouts.Status += message => MediaStatus.Text = message;
        QuickLayoutsHost.Content = _quickLayouts;
        _macroDock = new MacroDock(engine, session, theme, Panel.Execute);
        _macroDock.Status += message => MediaStatus.Text = message;
        MacroHost.Content = _macroDock;
        RosterControl.Attach(engine);
        AttachMedia(media);
        ApplyTheme(theme);
        SyncDisplayPicker();
        ProgramChanged();
        ShowTab(ConsoleTab.Control);
    }

    void AttachMedia(MediaSession? media)
    {
        if (media is null) return;
        MediaControl.Attach(media);
        media.Status += message => MediaStatus.Text = message;
    }

    /// <summary>Applies the operator theme to this window and to its media dock.</summary>
    public void ApplyTheme(string theme)
    {
        ThemeManager.Apply(this, theme);
        // The theme pass repaints buttons; the selected tab keeps its accent outline on top. Both
        // Loaded and ContentRendered are hooked because the first render can repaint the buttons
        // after Loaded, which is why a brand-new panel used to show no outline at all.
        if (ControlTab is not null) ApplyTabVisuals();
        if (!_tabHook)
        {
            _tabHook = true;
            Loaded += (_, _) => ApplyTabVisuals();
            ContentRendered += (_, _) => ApplyTabVisuals();
        }
        MediaControl.ApplyTheme(theme);
        RosterControl.ApplyTheme(theme);
        _macroDock?.ApplyTheme(theme);
        _quickLayouts?.Refresh();
        // In a tab the dock has the full panel width, so its buttons are the size of the console's.
        MediaControl.SetLargeMode(true);
    }

    public void SetHintVisibility(bool show)
    {
        Panel.SetHintVisibility(show);
        RosterControl.SetHintVisibility(show);
        PreviewAudienceHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Allows the host to keep the panel chain going without a production header button.</summary>
    public void RequestNewPanel() => NewPanelRequested?.Invoke();

    public void SyncLiveState(bool live) => Panel.SyncLiveState(live);

    /// <summary>Repaints the program preview with the newest values, skipping any switch delay.</summary>
    public void RefreshPreview() => _preview?.ApplyNow();

    public void Execute(string actionId) => Panel.Execute(actionId);

    public void ApplyKeybinds(KeybindSettings settings) => Panel.ApplyKeybinds(settings);

    // ---------- program ----------

    void CatalogChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(CatalogChanged); return; }
        SyncDisplayPicker();
        ProgramChanged();
    }

    void ProgramChanged()
    {
        if (Program is null) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(ProgramChanged); return; }
        PreviewLayoutLabel.Text = Program.Label;
        var count = Program.Display.Layouts.Count;
        LayoutPrevButton.IsEnabled = LayoutNextButton.IsEnabled = count > 1;
        _preview?.PrepareTransition(Program.Layout);
        _preview?.Rebuild();
        Panel.Refresh();
        RefreshMeterStatus();
    }

    /// <summary>Keeps the panel's meter line in step with the game as it is played.</summary>
    void MeterChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(MeterChanged); return; }
        RefreshMeterStatus();
    }

    /// <summary>Builds the same peak meter as the main rail: one column of segments repainted by the timer.</summary>
    void BuildSoundMeter()
    {
        var unlit = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x30));
        unlit.Freeze();
        for (var index = 0; index < _meterBars.Length; index++)
        {
            var fraction = (index + 1) / (double)_meterBars.Length;
            var colour = fraction > 0.92 ? Color.FromRgb(0xFF, 0x6B, 0x6B)
                : fraction > 0.78 ? Color.FromRgb(0xE8, 0xB2, 0x3A)
                : Color.FromRgb(0x38, 0xD7, 0xA3);
            var bar = new Border
            {
                Background = unlit,
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(1, 0, 1, 0),
                ToolTip = fraction > 0.92 ? "Full scale — the output is clipping" : null
            };
            bar.Tag = colour;
            ThemeManager.SetSkipTheme(bar, true);
            _meterBars[index] = bar;
            SoundMeterSegments.Children.Add(bar);
        }
    }

    void UpdateSoundMeter()
    {
        if (_meterBars[0] is null) return;
        var level = SoundLevelMeter.Read();
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
        RefreshMeterStatus();
    }

    void RefreshMeterStatus()
    {
        if (Program is null)
        {
            OnAirInfo.Text = "STANDBY";
            ShowInfo.Text = "Start a game to go live";
            return;
        }
        OnAirInfo.Text = Program.HasStagedChanges
            ? "STAGED · UPDATE TO AIR"
            : $"ON AIR · {Program.Display.Name.ToUpperInvariant()}";
        var state = _publishedEngine?.State ?? _engine.State;
        ShowInfo.Text = $"{state.AwayAbbreviation} {state.AwayScore} · {state.HomeAbbreviation} {state.HomeScore} · {GameEngine.FormatClock(state.ClockSeconds)} · {PeriodDisplay.Format(state)}";
    }

    /// <summary>Fills the display picker without treating the sync as the operator's choice.</summary>
    void SyncDisplayPicker()
    {
        if (_catalog is null || Program is null) return;
        _displaySync = true;
        try
        {
            var names = _catalog.Displays.Select((display, index) => $"{index + 1} · {display.Name}").ToArray();
            if (DisplaySelector.ItemsSource is not IEnumerable<string> existing || !existing.SequenceEqual(names))
                DisplaySelector.ItemsSource = names;
            var index = _catalog.Displays.FindIndex(display => string.Equals(display.Id, Program.DisplayId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && DisplaySelector.SelectedIndex != index) DisplaySelector.SelectedIndex = index;
        }
        finally { _displaySync = false; }
    }

    void Display_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_displaySync || Program is null || _catalog is null || DisplaySelector.SelectedIndex < 0) return;
        var display = _catalog.Displays[DisplaySelector.SelectedIndex];
        Program.SelectDisplay(display.Id);
    }

    void LayoutPrev_Click(object sender, RoutedEventArgs e) => Program?.Shift(-1);

    void LayoutNext_Click(object sender, RoutedEventArgs e) => Program?.Shift(1);

    // ---------- tabs ----------

    /// <summary>Switches this panel between the console and the media desk.</summary>
    public void ShowMediaTab(bool media) => ShowTab(media ? ConsoleTab.Media : ConsoleTab.Control);

    public void ShowRosterTab(bool rosters) => ShowTab(rosters ? ConsoleTab.Rosters : ConsoleTab.Control);

    /// <summary>Which desk this window is showing, for the checks and for restoring a view.</summary>
    public ConsoleTab Tab => _tab;

    void ControlTab_Click(object sender, RoutedEventArgs e) => ShowTab(ConsoleTab.Control);

    void MediaTab_Click(object sender, RoutedEventArgs e) => ShowTab(ConsoleTab.Media);

    void RosterTab_Click(object sender, RoutedEventArgs e) => ShowTab(ConsoleTab.Rosters);

    void MacrosTab_Click(object sender, RoutedEventArgs e) => ShowTab(ConsoleTab.Macros);

    /// <summary>Switches between the console, media, roster and macro desks; all fill one pane.</summary>
    void ShowTab(ConsoleTab tab)
    {
        if (_tab == ConsoleTab.Macros && tab != ConsoleTab.Macros) _macroDock?.CommitPendingEdits();
        _tab = tab;
        PanelHost.Visibility = tab == ConsoleTab.Control ? Visibility.Visible : Visibility.Collapsed;
        MediaControl.Visibility = tab == ConsoleTab.Media ? Visibility.Visible : Visibility.Collapsed;
        RosterControl.Visibility = tab == ConsoleTab.Rosters ? Visibility.Visible : Visibility.Collapsed;
        MacroHost.Visibility = tab == ConsoleTab.Macros ? Visibility.Visible : Visibility.Collapsed;
        ApplyTabVisuals();
        if (tab == ConsoleTab.Media) MediaControl.RefreshView();
        if (tab == ConsoleTab.Rosters) RosterControl.RefreshView();
        if (tab == ConsoleTab.Macros) _macroDock?.RefreshView(force: true);
    }

    /// <summary>
    /// Paints the selected tab. The theme pass repaints every button when the window loads, so the
    /// outline has to be restored after it or the first tab shows no outline until it is clicked.
    /// </summary>
    void ApplyTabVisuals()
    {
        // The operator's own accent, the same one the main window's tabs use.
        var accent = UiPalette.Accent(PreferencesStore.Load());
        var on = UiPalette.Frozen(UiPalette.Soft(accent));
        var off = (Brush)new BrushConverter().ConvertFromString("#141A23")!;
        var onBorder = UiPalette.Frozen(accent);
        var offBorder = (Brush)new BrushConverter().ConvertFromString("#28323F")!;
        var muted = (Brush)new BrushConverter().ConvertFromString("#93A0B2")!;
        Button[] tabs = [ControlTab, MediaTab, RosterTab, MacrosTab];
        var active = (int)_tab;
        for (var i = 0; i < tabs.Length; i++)
        {
            tabs[i].Background = i == active ? on : off;
            tabs[i].BorderBrush = i == active ? onBorder : offBorder;
            tabs[i].Foreground = i == active ? Brushes.White : muted;
        }
    }

    void Undo_Click(object sender, RoutedEventArgs e) => Panel.Execute("global.undo");

    void Keybinds_Click(object sender, RoutedEventArgs e) => Panel.ShowKeybindsDialog();

    void NewPanel_Click(object sender, RoutedEventArgs e) => NewPanelRequested?.Invoke();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Enter commits the field being typed in and keeps this panel open.
        if (key == Key.Enter && Keyboard.FocusedElement is TextBox box && !box.AcceptsReturn)
        {
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }
        if (Panel.TryHandleKey(key, Keyboard.Modifiers)) { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Program is not null)
        {
            Program.Changed -= ProgramChanged;
            Program.Dispose();
        }
        if (_catalog is not null) _catalog.Changed -= CatalogChanged;
        _engine.Changed -= MeterChanged;
        if (_publishedEngine is not null && !ReferenceEquals(_publishedEngine, _engine))
            _publishedEngine.Changed -= MeterChanged;
        _meterTimer.Stop();
        _macroDock?.CommitPendingEdits();
        _preview?.Dispose();
        _ownedSession?.Dispose();
        base.OnClosed(e);
    }

    /// <summary>Adapter for callers that only supply publish/live callbacks (also used by tests).</summary>
    sealed class DelegateHost : IControlPanelHost
    {
        public Action Publish = () => { };
        public Action<bool> SetLive = _ => { };
        public Func<bool> GetLive = () => true;
        public Action NewPanel = () => { };

        public void PublishOutput() => Publish();
        public bool LiveScoreClock { get => GetLive(); set => SetLive(value); }
        public bool HasStagedChanges => false;
        public void OpenNewPanel() => NewPanel();
        public string LayoutLabel => "1 · Layout";
        public void ShiftLayout(int delta) { }
    }

    /// <summary>
    /// The panel's host: publishing sends the shared game data and then this window's own staged
    /// layouts, so one panel never publishes the layout another panel is holding.
    /// </summary>
    sealed class WindowHost : IControlPanelHost
    {
        readonly ProgramSelection _program;
        readonly Action _publishGameState;
        readonly Func<bool> _hasStagedGame;
        readonly Action<bool> _setLive;
        readonly Func<bool> _getLive;
        readonly Action _newPanel;

        public WindowHost(ProgramSelection program, Action publishGameState, Func<bool> hasStagedGame,
            Action<bool> setLive, Func<bool> getLive, Action newPanel)
        {
            _program = program;
            _publishGameState = publishGameState;
            _hasStagedGame = hasStagedGame;
            _setLive = setLive;
            _getLive = getLive;
            _newPanel = newPanel;
        }

        public void PublishOutput()
        {
            _publishGameState();
            _program.PublishStaged();
        }

        public bool LiveScoreClock { get => _getLive(); set => _setLive(value); }

        public bool HasStagedChanges => _hasStagedGame() || _program.HasStagedChanges;

        public void OpenNewPanel() => _newPanel();

        public string LayoutLabel => _program.Label;

        public void ShiftLayout(int delta) => _program.Shift(delta);
    }
}

internal sealed class AddStatDialog : Window
{
    readonly ComboBox _presets = new();
    readonly TextBox _custom = new();

    public string StatName { get; private set; } = "";

    public AddStatDialog(IReadOnlyCollection<string> existing)
    {
        Title = "Add custom value";
        Width = 380; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(23, 28, 37));
        Foreground = Brushes.White;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Choose a common value or type your own", Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)), Margin = new Thickness(4, 0, 0, 6) });
        _presets.ItemsSource = CommonStats.Names.Where(n => !existing.Contains(n, StringComparer.OrdinalIgnoreCase)).ToArray();
        _presets.SelectedIndex = 0;
        panel.Children.Add(_presets);
        panel.Children.Add(new TextBlock { Text = "Custom value name (overrides the choice above)", Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)), Margin = new Thickness(4, 8, 0, 6) });
        panel.Children.Add(_custom);
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(255, 114, 114)), Margin = new Thickness(4, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var add = new Button { Content = "Add value", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        add.Click += (_, _) =>
        {
            var name = _custom.Text.Trim();
            if (name.Length == 0) name = (_presets.SelectedItem as string ?? "").Trim();
            if (name.Length == 0) { error.Text = "Choose a value or type a name."; return; }
            if (existing.Contains(name, StringComparer.OrdinalIgnoreCase)) { error.Text = "That value already exists."; return; }
            StatName = name;
            DialogResult = true;
            Close();
        };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(add); buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => _custom.Focus();
    }
}

internal sealed class ClockTimeDialog : Window
{
    readonly TextBox _input = new();
    readonly TextBlock _error = new();
    public int Seconds { get; private set; }

    public ClockTimeDialog(string title, int seconds)
    {
        Title = title;
        Width = 360; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(23, 28, 37));
        Foreground = Brushes.White;
        Seconds = seconds;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Enter MM:SS (for example 10:30) or a total number of seconds.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)), Margin = new Thickness(4, 0, 0, 8) });
        _input.Text = ClockFormat.Format(seconds);
        panel.Children.Add(_input);
        _error.Foreground = new SolidColorBrush(Color.FromRgb(255, 114, 114));
        _error.Margin = new Thickness(4, 4, 0, 0);
        _error.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_error);
        var apply = new Button { Content = "Apply", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        apply.Click += (_, _) =>
        {
            if (!ClockFormat.TryParse(_input.Text, out var parsed)) { _error.Text = "Use MM:SS or a whole number of seconds."; return; }
            Seconds = parsed;
            DialogResult = true;
            Close();
        };
        panel.Children.Add(apply);
        Content = panel;
        ThemeManager.Apply(this, PreferencesStore.Load().Theme);
        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }
}
