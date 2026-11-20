using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// The Rosters &amp; stats tab: one player desk, showing the visitors' roster or the home roster.
/// The desk belongs to the team it is showing, so its highlighted row wears that team's colour.
/// Every control window has its own copy of the desk, but they share one roster library.
/// </summary>
public partial class RosterDock : UserControl
{
    GameEngine? _engine;
    bool _showingHome = true;
    // The console's Live switch is the show's live state. When a host provides it, the stats desk
    // reads and drives the same switch instead of keeping a second one that can disagree.
    Func<bool>? _getLive;
    Action<bool>? _setLive;

    /// <summary>The tab's Update Output button, wired to the console's own publish by the host.</summary>
    public Action? PublishOutput { get; set; }

    public RosterDock()
    {
        InitializeComponent();
        // The desk paints itself from its own palette (the theme's page and surfaces); the generic
        // theme pass must not flatten those shades into one colour.
        ThemeManager.SetSkipTheme(this, true);
        ApplyTheme("Dark");
        Loaded += (_, _) => RefreshSummary();
    }

    /// <summary>Points the tab at the game. Every window has its own dock but one engine and library.</summary>
    public void Attach(GameEngine engine, Action? publishOutput = null, Func<bool>? getLive = null, Action<bool>? setLive = null)
    {
        if (_engine is not null) _engine.Changed -= EngineChanged;
        _engine = engine;
        PublishOutput = publishOutput;
        _getLive = getLive;
        _setLive = setLive;
        // The desk follows the console's Live switch when its host supplies one; a detached desk
        // without a host falls back to the saved preference so it still works on its own.
        _engine.PlayerStatsLive = getLive?.Invoke() ?? PreferencesStore.RosterStatsLive;
        PreferencesStore.RosterStatsLive = _engine.PlayerStatsLive;
        AwayPanel.Attach(engine, home: false);
        HomePanel.Attach(engine, home: true);
        _engine.Changed += EngineChanged;
        ShowSide(_showingHome);
        SyncLiveButton();
        RefreshSummary();
    }

    void SyncLiveButton()
    {
        var live = _getLive?.Invoke() ?? _engine?.PlayerStatsLive == true;
        RosterLiveButton.Content = live ? "Live: ON" : "Live: OFF";
        RosterLiveButton.Foreground = (Brush)(Resources[live ? "RosterAccentBrush" : "RosterMutedBrush"] ?? Brushes.Gray);
        RosterLiveButton.ToolTip = live
            ? "Player stat edits print on the graphic as you make them. Press to stage them instead."
            : "Player stat edits stay staged until Update Output. Press to print them as you type.";
    }

    void Live_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var live = !(_getLive?.Invoke() ?? _engine.PlayerStatsLive);
        if (_setLive is not null)
        {
            // The host owns the one Live state for the console and this desk.
            _setLive(live);
        }
        else
        {
            _engine.PlayerStatsLive = live;
            PreferencesStore.RosterStatsLive = live;
            PreferencesStore.Save(PreferencesStore.Load());
        }
        SyncLiveButton();
        // Turning Live on publishes what was waiting: the operator asked for it to be current.
        if (live && _engine.PublishStagedPlayers()) PublishOutput?.Invoke();
    }

    void UpdateOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        _engine.PublishStagedPlayers();
        PublishOutput?.Invoke();
    }

    /// <summary>Matches the desk to the operator theme (black page in Dark, light page in Light).</summary>
    public void ApplyTheme(string theme)
    {
        RosterPalette.Apply(theme, Resources);
        Background = (Brush)Resources["RosterPanelBrush"];
        AwayPanel.ApplyTheme(theme);
        HomePanel.ApplyTheme(theme);
        UpdateTabs();
    }

    public void RefreshView()
    {
        (ShowingHome ? HomePanel : AwayPanel).RefreshView();
        RefreshSummary();
    }

    public void SetHintVisibility(bool show)
    {
        HintText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AwayPanel.SetHintVisibility(show);
        HomePanel.SetHintVisibility(show);
    }

    /// <summary>The side currently on the desk, for the checks.</summary>
    public bool ShowingHome => _showingHome;

    /// <summary>Which panel is on screen right now.</summary>
    public RosterPanel ActivePanel => _showingHome ? HomePanel : AwayPanel;

    void EngineChanged()
    {
        // Every stats desk shares one engine, so a Live press in this window (or a detached control
        // panel) has to relabel the switch everywhere.
        SyncLiveButton();
        RefreshSummary();
    }

    /// <summary>Applies the Preferences choice for automatic stat updates to the open desks.</summary>
    public void ApplyStatsPreference()
    {
        if (_engine is null) return;
        var live = PreferencesStore.RosterStatsLive;
        if (_setLive is not null) _setLive(live);
        else _engine.PlayerStatsLive = live;
        SyncLiveButton();
        if (live && _engine.PublishStagedPlayers()) PublishOutput?.Invoke();
    }

    void RefreshSummary()
    {
        if (_engine is not null)
        {
            var pending = _engine.State.HomePlayerPending is not null || _engine.State.AwayPlayerPending is not null;
            RosterUpdateButton.Content = pending ? "Update Output •" : "Update Output";
            RosterUpdateButton.Foreground = (Brush)(Resources[pending ? "RosterAccentBrush" : "RosterHeaderTextBrush"] ?? Brushes.Gray);
        }
        var count = RosterStore.All().Count;
        LibraryText.Text = count switch
        {
            0 => "no rosters yet",
            1 => "1 roster in the library ·",
            _ => $"{count} rosters in the library ·"
        };
    }

    void SideTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string side }) return;
        ShowSide(side == "home");
    }

    void ShowSide(bool home)
    {
        _showingHome = home;
        AwayPanel.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
        HomePanel.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        DeskText.Text = home ? "HOME ROSTER" : "VISITORS ROSTER";
        (home ? HomePanel : AwayPanel).RefreshView();
        UpdateTabs();
    }

    void UpdateTabs()
    {
        // The side on the desk wears the operator's accent, like a selected tab in the console.
        var selected = (Brush)(Resources["RosterAccentBrush"] ?? Brushes.LimeGreen);
        var selectedText = (Brush)(Resources["RosterAccentTextBrush"] ?? Brushes.Black);
        var idle = (Brush)(Resources["RosterTabBrush"] ?? Brushes.Transparent);
        var idleText = (Brush)(Resources["RosterMutedBrush"] ?? Brushes.Gray);
        AwaySideTab.Background = _showingHome ? idle : selected;
        HomeSideTab.Background = _showingHome ? selected : idle;
        AwaySideTab.Foreground = _showingHome ? idleText : selectedText;
        HomeSideTab.Foreground = _showingHome ? selectedText : idleText;
    }

    void NewRoster_Click(object sender, RoutedEventArgs e) => (ShowingHome ? HomePanel : AwayPanel).CreateRoster();

    /// <summary>The header's Import… button, aimed at the side currently on the desk.</summary>
    void Import_Click(object sender, RoutedEventArgs e) => ActivePanel.ImportFromDialog();

    /// <summary>The header's Export… button, aimed at the side currently on the desk.</summary>
    void Export_Click(object sender, RoutedEventArgs e) => ActivePanel.ExportToDialog();
}
