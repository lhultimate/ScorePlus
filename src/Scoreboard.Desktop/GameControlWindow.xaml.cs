using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    ScorebugCatalog? _catalog;
    ScorebugCanvasView? _preview;
    AudiencePreview? _audience;
    bool _tabHook;
    bool _displaySync;
    ConsoleTab _tab = ConsoleTab.Control;

    /// <summary>The three desks a control window carries: console, media and rosters.</summary>
    public enum ConsoleTab { Control, Media, Rosters }

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
        if (host is DelegateHost detached) detached.NewPanel = () => NewPanelRequested?.Invoke();
        Panel = new ControlPanelView(engine, host);
        PanelHost.Content = Panel;
        RosterControl.Attach(engine);
        ProgramStrip.Visibility = Visibility.Collapsed;
        // Without a display there is nothing for a show line to describe, so the meter stays hidden.
        Meter.Visibility = Visibility.Collapsed;
        // A panel with no catalog has no layouts to reach, so the quick buttons stay off too.
        QuickLayoutsHost.Visibility = Visibility.Collapsed;
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
        _catalog = catalog;
        Program = new ProgramSelection(catalog);
        _preview = new ScorebugCanvasView(catalog, previewEngine, playback: media?.Playback, layoutProvider: () => Program!.Layout, displayId: Program!.DisplayId, automation: automation);
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
        // The same quick media and quick layout buttons as the main rail, on the same preferences.
        _quickMedia = new QuickMediaBar(media ?? new MediaSession(PreferencesStore.Load()), theme);
        _quickMedia.Status += message => MediaStatus.Text = message;
        QuickMediaHost.Content = _quickMedia;
        _quickLayouts = new QuickLayoutBar(catalog, engine, theme);
        _quickLayouts.Status += message => MediaStatus.Text = message;
        QuickLayoutsHost.Content = _quickLayouts;
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
        _quickLayouts?.Refresh();
        // In a tab the dock has the full panel width, so its buttons are the size of the console's.
        MediaControl.SetLargeMode(true);
    }

    public void SetHintVisibility(bool show)
    {
        Panel.SetHintVisibility(show);
        RosterControl.SetHintVisibility(show);
    }

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
        ProgramHint.Text = Program.HasStagedChanges
            ? "Preview only — press Update Output to put this display on air."
            : "This display is on air with the layout shown here.";
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

    void RefreshMeterStatus()
    {
        if (Program is null) return;
        var state = _engine.State;
        Meter.SetStatus(
            Program.HasStagedChanges ? "STAGED · UPDATE TO AIR" : $"ON AIR · {Program.Display.Name.ToUpperInvariant()}",
            $"{state.AwayAbbreviation} {state.AwayScore} · {state.HomeAbbreviation} {state.HomeScore} · {GameEngine.FormatClock(state.ClockSeconds)} · {PeriodDisplay.Format(state)}");
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

    /// <summary>Switches between the console, the media desk and the roster desk; all fill one pane.</summary>
    void ShowTab(ConsoleTab tab)
    {
        _tab = tab;
        PanelHost.Visibility = tab == ConsoleTab.Control ? Visibility.Visible : Visibility.Collapsed;
        MediaControl.Visibility = tab == ConsoleTab.Media ? Visibility.Visible : Visibility.Collapsed;
        RosterControl.Visibility = tab == ConsoleTab.Rosters ? Visibility.Visible : Visibility.Collapsed;
        ApplyTabVisuals();
        if (tab == ConsoleTab.Media) MediaControl.RefreshView();
        if (tab == ConsoleTab.Rosters) RosterControl.RefreshView();
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
        Button[] tabs = [ControlTab, MediaTab, RosterTab];
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
        _preview?.Dispose();
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
        Title = "Add custom stat";
        Width = 380; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(23, 28, 37));
        Foreground = Brushes.White;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Choose a common stat or type your own", Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)), Margin = new Thickness(4, 0, 0, 6) });
        _presets.ItemsSource = CommonStats.Names.Where(n => !existing.Contains(n, StringComparer.OrdinalIgnoreCase)).ToArray();
        _presets.SelectedIndex = 0;
        panel.Children.Add(_presets);
        panel.Children.Add(new TextBlock { Text = "Custom name (overrides the choice above)", Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)), Margin = new Thickness(4, 8, 0, 6) });
        panel.Children.Add(_custom);
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(255, 114, 114)), Margin = new Thickness(4, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var add = new Button { Content = "Add stat", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        add.Click += (_, _) =>
        {
            var name = _custom.Text.Trim();
            if (name.Length == 0) name = (_presets.SelectedItem as string ?? "").Trim();
            if (name.Length == 0) { error.Text = "Choose a stat or type a name."; return; }
            if (existing.Contains(name, StringComparer.OrdinalIgnoreCase)) { error.Text = "That stat already exists."; return; }
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
