using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// The league-cloud setup desk. It edits the endpoint settings, tests the feed, and can add a
/// ready-made lower-third layout whose text follows {{LEAGUE_GAMES}}.
/// </summary>
public sealed class LeagueCloudWindow : Window
{
    readonly LeagueCloudService _service;
    readonly UserPreferences _preferences;
    readonly ScorebugCatalog _catalog;
    readonly ProgramSelection _program;

    readonly CheckBox _enabled = new() { Content = "Publish this game and fetch the league feed automatically" };
    readonly TextBox _upload = new();
    readonly TextBox _feed = new();
    readonly TextBox _league = new();
    readonly TextBox _poll = new();
    readonly PasswordBox _key = new();
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) };
    readonly ListBox _games = new() { MinHeight = 120, MaxHeight = 190, FontSize = 11 };

    public LeagueCloudWindow(LeagueCloudService service, UserPreferences preferences, ScorebugCatalog catalog, ProgramSelection program)
    {
        _service = service;
        _preferences = preferences;
        _catalog = catalog;
        _program = program;
        Title = "ScorePlus — League Cloud";
        Width = 620;
        Height = 610;
        MinWidth = 520;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");
        Content = Build();
        Fill();
        _service.Status += OnStatus;
        _service.GamesChanged += OnGamesChanged;
        Closed += (_, _) =>
        {
            _service.Status -= OnStatus;
            _service.GamesChanged -= OnGamesChanged;
        };
        ThemeManager.Apply(this, preferences.Theme);
        RefreshGames();
        _status.Text = preferences.LeagueCloudEnabled
            ? "League cloud is on. Changes upload after a short delay; the feed refreshes on the interval below."
            : "League cloud is off. Configure an upload URL, a feed URL, or both.";
    }

    UIElement Build()
    {
        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 16) };
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "LEAGUE CLOUD", FontSize = 18, FontWeight = FontWeights.Bold });
        heading.Children.Add(new TextBlock
        {
            Text = "Upload the live game to a league server and fetch the rest of the league for a ticker or lower third.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Opacity = 0.75,
            Margin = new Thickness(0, 4, 0, 10)
        });
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Save", IsDefault = true, MinHeight = 34, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        var cancel = new Button { Content = "Close", IsCancel = true, MinHeight = 34, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        save.Click += (_, _) => { if (Save()) Close(); };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var panel = new StackPanel();
        panel.Children.Add(_enabled);
        panel.Children.Add(Help("The snapshot includes game name, sport, teams, score, period, clock and records. The API key stays in your local preferences."));

        panel.Children.Add(Label("Upload endpoint (POST)"));
        AddBox(panel, _upload, "https://league.example.com/api/games (receives the current game JSON)");

        panel.Children.Add(Label("League feed endpoint (GET)"));
        AddBox(panel, _feed, "https://league.example.com/api/games?league={league} (array, or { games: [...] })");

        var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(0, 0, 5, 0) };
        left.Children.Add(Label("League id"));
        left.Children.Add(_league);
        left.Children.Add(Help("Sent as the league field and added to the feed URL when it has no {league} placeholder."));
        var right = new StackPanel { Margin = new Thickness(5, 0, 0, 0) };
        right.Children.Add(Label("Poll every (seconds)"));
        right.Children.Add(_poll);
        right.Children.Add(Help("5–300 seconds. Local changes also upload shortly after they happen."));
        Grid.SetColumn(right, 1);
        row.Children.Add(left);
        row.Children.Add(right);
        panel.Children.Add(row);

        panel.Children.Add(Label("API key / bearer token (optional)"));
        _key.MinHeight = 32;
        _key.Padding = new Thickness(8, 4, 8, 4);
        _key.Margin = new Thickness(0, 0, 0, 2);
        panel.Children.Add(_key);
        panel.Children.Add(Help("Use a private HTTPS endpoint. Do not put a key in a public layout or share the preferences file."));

        panel.Children.Add(Label("League games received"));
        panel.Children.Add(_games);
        panel.Children.Add(_status);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var upload = new Button { Content = "Upload now", MinHeight = 32, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
        var fetch = new Button { Content = "Fetch now", MinHeight = 32, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
        var ticker = new Button { Content = "Add league ticker layout", MinHeight = 32, Padding = new Thickness(12, 5, 12, 5) };
        upload.Click += async (_, _) => { if (Save()) { _status.Text = "Uploading…"; await _service.UploadNowAsync(); } };
        fetch.Click += async (_, _) => { if (Save()) { _status.Text = "Fetching…"; await _service.FetchNowAsync(); } };
        ticker.Click += (_, _) => { if (Save()) AddTicker(); };
        actions.Children.Add(upload);
        actions.Children.Add(fetch);
        actions.Children.Add(ticker);
        panel.Children.Add(actions);

        var scroll = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 8, 0, 8)
        };
        root.Children.Add(scroll);
        return root;
    }

    static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
        FontSize = 11,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 9, 0, 3)
    };

    static void AddBox(Panel panel, TextBox box, string tooltip)
    {
        box.MinHeight = 32;
        box.Padding = new Thickness(8, 4, 8, 4);
        box.Margin = new Thickness(0, 0, 0, 1);
        box.ToolTip = tooltip;
        panel.Children.Add(box);
    }

    static TextBlock Help(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x7C, 0x8C)),
        FontSize = 10,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2)
    };

    void Fill()
    {
        _enabled.IsChecked = _preferences.LeagueCloudEnabled;
        _upload.Text = _preferences.LeagueCloudUploadUrl;
        _feed.Text = _preferences.LeagueCloudFeedUrl;
        _league.Text = _preferences.LeagueCloudLeagueId;
        _poll.Text = _preferences.LeagueCloudPollSeconds.ToString();
        _key.Password = _preferences.LeagueCloudApiKey;
    }

    bool Save()
    {
        _preferences.LeagueCloudEnabled = _enabled.IsChecked == true;
        _preferences.LeagueCloudUploadUrl = _upload.Text.Trim();
        _preferences.LeagueCloudFeedUrl = _feed.Text.Trim();
        _preferences.LeagueCloudLeagueId = string.IsNullOrWhiteSpace(_league.Text) ? "default" : _league.Text.Trim();
        _preferences.LeagueCloudApiKey = _key.Password.Trim();
        if (int.TryParse(_poll.Text.Trim(), out var seconds)) _preferences.LeagueCloudPollSeconds = Math.Clamp(seconds, 5, 300);
        try { PreferencesStore.Save(_preferences); } catch (Exception ex) { _status.Text = $"Could not save preferences: {ex.Message}"; return false; }
        _service.ApplySettings();
        if (_preferences.LeagueCloudEnabled) _service.Start();
        return true;
    }

    void OnStatus(string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnStatus(message))); return; }
        _status.Text = message;
    }

    void OnGamesChanged(IReadOnlyList<LeagueGame> games)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnGamesChanged(games))); return; }
        RefreshGames(games);
    }

    void RefreshGames(IReadOnlyList<LeagueGame>? games = null)
    {
        var list = games ?? _service.Games;
        _games.ItemsSource = list.Select(game => game.TickerLine).ToArray();
    }

    /// <summary>
    /// Adds a same-size transparent layout with one text element bound to the cloud ticker. It is
    /// added to the display previewed by this desk, selected for editing, and left unpushed so the
    /// operator can style it before Update Output.
    /// </summary>
    void AddTicker()
    {
        var displayIndex = _catalog.Displays.FindIndex(display =>
            string.Equals(display.Id, _program.DisplayId, StringComparison.OrdinalIgnoreCase));
        if (displayIndex < 0) displayIndex = _catalog.ActiveDisplayIndex;
        _catalog.SelectDisplay(displayIndex);
        var source = _catalog.Layout;
        var width = Math.Max(640, source.Width);
        var height = Math.Max(360, source.Height);
        _catalog.AddLayout("League Ticker");
        _catalog.Update(layout =>
        {
            layout.Width = width;
            layout.Height = height;
            layout.Background = "#00000000";
            layout.Elements.Clear();
            layout.Elements.Add(new ScorebugElement
            {
                Name = "League Ticker",
                Kind = ScorebugElementKind.Text,
                Content = "{{LEAGUE_GAMES}}",
                X = 70,
                Y = height - 92,
                Width = width - 140,
                Height = 58,
                FontSize = 22,
                Foreground = "#FFFFFFFF",
                Background = "#E6080B10",
                BorderColor = "#00000000",
                BorderThickness = 0,
                TextAlign = ScorebugTextAlign.Left
            });
        });
        _status.Text = "League ticker layout added. It follows the live cloud feed; style it here, then Update Output.";
    }
}
