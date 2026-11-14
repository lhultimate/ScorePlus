using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// Chooses what a league ticker rotates through. The ticker element itself stays editable in the
/// studio; this window only decides which received games, headlines, timing and alert behaviour
/// feed it.
/// </summary>
public sealed class TickerWindow : Window
{
    readonly GameEngine _engine;
    readonly LeagueCloudService _service;
    readonly CheckBox _auto = new() { Content = "Automatically rotate one card at a time" };
    readonly CheckBox _alerts = new() { Content = "Interrupt with score alerts when the local or a chosen game scores" };
    readonly TextBox _dwell = new();
    readonly TextBox _transition = new();
    readonly TextBox _news = new() { AcceptsReturn = true, MinHeight = 88, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly StackPanel _gamesPanel = new();
    readonly TextBlock _gamesHint = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x7C, 0x8C)),
        FontSize = 10,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2)
    };
    readonly Dictionary<string, CheckBox> _gameChecks = new(StringComparer.OrdinalIgnoreCase);

    public TickerWindow(GameEngine engine, LeagueCloudService service)
    {
        _engine = engine;
        _service = service;
        Title = "ScorePlus — Ticker rotation";
        Width = 620;
        Height = 690;
        MinWidth = 520;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");
        Content = Build();
        Fill();
        _service.GamesChanged += OnGamesChanged;
        Closed += (_, _) => _service.GamesChanged -= OnGamesChanged;
        RefreshGames();
        ThemeManager.Apply(this, PreferencesStore.Load().Theme);
    }

    UIElement Build()
    {
        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 16) };
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "TICKER ROTATION", FontSize = 18, FontWeight = FontWeights.Bold });
        heading.Children.Add(new TextBlock
        {
            Text = "Pick the league games to cycle through, add headlines, and choose whether score changes interrupt the rotation.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Opacity = 0.75,
            Margin = new Thickness(0, 4, 0, 10)
        });
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Save", IsDefault = true, MinHeight = 34, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        var close = new Button { Content = "Close", IsCancel = true, MinHeight = 34, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        save.Click += (_, _) => { if (Save()) Close(); };
        close.Click += (_, _) => Close();
        footer.Children.Add(save);
        footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var panel = new StackPanel();
        panel.Children.Add(_auto);
        panel.Children.Add(Help("Use this on a ticker whose text contains the League cloud ticker token. In the studio, tick the element's Ticker box so it scrolls or rotates."));
        panel.Children.Add(_alerts);

        var timing = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        timing.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timing.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var hold = new StackPanel { Margin = new Thickness(0, 0, 5, 0) };
        hold.Children.Add(Label("Hold each card (seconds)"));
        hold.Children.Add(_dwell);
        hold.Children.Add(Help("1–60 seconds. The score alert stays up for at least four seconds."));
        var fade = new StackPanel { Margin = new Thickness(5, 0, 0, 0) };
        fade.Children.Add(Label("Fade between cards (seconds)"));
        fade.Children.Add(_transition);
        fade.Children.Add(Help("0 is a hard cut; 0.4 is a quick television dissolve."));
        Grid.SetColumn(fade, 1);
        timing.Children.Add(hold);
        timing.Children.Add(fade);
        panel.Children.Add(timing);

        panel.Children.Add(Label("Games in the rotation"));
        panel.Children.Add(_gamesPanel);
        panel.Children.Add(_gamesHint);
        var gameButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var all = new Button { Content = "Select all", MinHeight = 28, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        var none = new Button { Content = "Clear", MinHeight = 28, Padding = new Thickness(10, 3, 10, 3) };
        all.Click += (_, _) => SetAllGames(true);
        none.Click += (_, _) => SetAllGames(false);
        gameButtons.Children.Add(all);
        gameButtons.Children.Add(none);
        panel.Children.Add(gameButtons);

        panel.Children.Add(Label("News headlines"));
        panel.Children.Add(_news);
        panel.Children.Add(Help("One headline per line. They join the rotation after the selected games."));

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
        var state = _engine.State;
        _auto.IsChecked = state.TickerAutoRotate;
        _alerts.IsChecked = state.TickerScoreAlerts;
        _dwell.Text = state.TickerDwellSeconds.ToString("0.##", CultureInfo.CurrentCulture);
        _transition.Text = state.TickerTransitionSeconds.ToString("0.##", CultureInfo.CurrentCulture);
        _news.Text = string.Join(Environment.NewLine, state.TickerNews ?? []);
    }

    void OnGamesChanged(IReadOnlyList<LeagueGame> games)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnGamesChanged(games)));
            return;
        }
        RefreshGames();
    }

    void RefreshGames()
    {
        var prior = _gameChecks.ToDictionary(pair => pair.Key, pair => pair.Value.IsChecked == true, StringComparer.OrdinalIgnoreCase);
        _gameChecks.Clear();
        _gamesPanel.Children.Clear();
        var games = _service.Games;
        if (games.Count == 0)
        {
            _gamesHint.Text = "No league games have arrived yet. Open League Cloud, fetch the feed, then return here.";
            return;
        }

        var state = _engine.State;
        var selected = (state.TickerGameIds ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chooseAll = !state.TickerGamesChosen;
        _gamesHint.Text = "Only ticked games appear in the rotation. News still appears when no games are ticked.";
        for (var index = 0; index < games.Count; index++)
        {
            var game = games[index];
            // A feed without ids is always included (there is nothing stable to filter on), so its
            // row is checked and cannot be switched off.
            var key = string.IsNullOrWhiteSpace(game.GameId) ? $"__game-{index}" : game.GameId;
            var check = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = game.TickerLine,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1)
                },
                Tag = game.GameId,
                IsEnabled = !string.IsNullOrWhiteSpace(game.GameId),
                Margin = new Thickness(0, 1, 0, 1)
            };
            check.IsChecked = string.IsNullOrWhiteSpace(game.GameId)
                || (prior.TryGetValue(key, out var wasChecked)
                    ? wasChecked
                    : chooseAll || selected.Contains(game.GameId));
            _gameChecks[key] = check;
            _gamesPanel.Children.Add(check);
        }
    }

    void SetAllGames(bool value)
    {
        foreach (var check in _gameChecks.Values) check.IsChecked = value;
    }

    bool Save()
    {
        var ids = _gameChecks
            .Where(pair => pair.Value.IsChecked == true && !pair.Key.StartsWith("__game-", StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToList();
        var lines = (_news.Text ?? "")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);
        if (!double.TryParse(_dwell.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var dwell)
            && !double.TryParse(_dwell.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out dwell))
            dwell = _engine.State.TickerDwellSeconds;
        if (!double.TryParse(_transition.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var transition)
            && !double.TryParse(_transition.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out transition))
            transition = _engine.State.TickerTransitionSeconds;
        _engine.SetTickerSettings(ids, lines, _auto.IsChecked == true, _alerts.IsChecked == true, dwell, transition);
        return true;
    }
}
