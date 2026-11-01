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
    public void Attach(GameEngine engine)
    {
        if (_engine is not null) _engine.Changed -= EngineChanged;
        _engine = engine;
        AwayPanel.Attach(engine, home: false);
        HomePanel.Attach(engine, home: true);
        _engine.Changed += EngineChanged;
        ShowSide(_showingHome);
        RefreshSummary();
    }

    /// <summary>Matches the desk to the operator theme (near-black page in Dark, light page in Light).</summary>
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

    void EngineChanged() => RefreshSummary();

    void RefreshSummary()
    {
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
