using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>What the control surface needs from whatever window hosts it.</summary>
public interface IControlPanelHost
{
    void PublishOutput();
    bool LiveScoreClock { get; set; }
    bool HasStagedChanges { get; }
    void OpenNewPanel();

    /// <summary>"1 · Broadcast Classic" — the layout the preview is showing and would publish.</summary>
    string LayoutLabel { get; }

    /// <summary>Moves the previewed layout forward or back; Update Output puts it on air.</summary>
    void ShiftLayout(int delta);
}

/// <summary>
/// The whole operator surface: team panels, down &amp; distance, clocks, timeouts, possession,
/// and custom stats. Used full-size in the main window and inside a popped-out panel, so both
/// always behave identically.
/// </summary>
public partial class ControlPanelView : UserControl
{
    static readonly Brush StagedBrush = Freeze(Color.FromRgb(255, 209, 102));
    static readonly Brush PublishedBrush = Freeze(Color.FromRgb(56, 215, 163));
    static Brush Freeze(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }

    readonly GameEngine _engine;
    readonly IControlPanelHost _host;
    readonly Stepper _awayScore, _homeScore, _down, _toGo, _ballOn, _awayTimeouts, _homeTimeouts;
    readonly TextBox _quarterValue = new();
    readonly Button _ballSide;
    readonly Dictionary<string, Stepper> _statValues = [];
    // The sport's own values: a typed-over number per counter, a switch per flag.
    readonly Dictionary<string, TextBox> _sportValueBoxes = [];
    readonly Dictionary<string, ToggleButton> _sportFlagButtons = [];
    // Baseball's three runner flags are drawn as square bases that light up rather than as words.
    readonly Dictionary<string, Border> _baseSquares = [];
    readonly Dictionary<string, TextBlock> _baseLabels = [];
    static readonly Brush BaseLit = Freeze(Color.FromRgb(0x38, 0xD7, 0xA3));
    static readonly Brush BaseEmpty = Freeze(Color.FromArgb(0x66, 0x38, 0xD7, 0xA3));
    string _sportControlsSignature = "";
    string? _awayLogoLoaded;
    string? _homeLogoLoaded;
    string _scoreSignature = "";
    string _statsSignature = "";
    bool _syncingLive;
    bool _loadingNames;
    bool _showHints;
    KeybindSettings _keybinds = KeybindStore.Load();
    bool _hornSync;

    public ControlPanelView(GameEngine engine, IControlPanelHost host)
    {
        InitializeComponent();
        _engine = engine;
        _host = host;

        _awayScore = BuildScoreStepper(false);
        _homeScore = BuildScoreStepper(true);
        AwayScoreHost.Content = _awayScore;
        HomeScoreHost.Content = _homeScore;

        _down = new Stepper("Down", 26);
        _down.SetButtons([new("+", 1, "football.down.next")], [new("−", -1, "football.down.previous")]);
        _down.Stepped += Step;
        _down.Committed += value => _engine.SetDown(value);
        DownHost.Content = _down;

        _toGo = new Stepper("To go", 26);
        _toGo.SetButtons([new("+", 1, "football.distance.plus")], [new("−", -1, "football.distance.minus")]);
        _toGo.Stepped += Step;
        _toGo.Committed += value => _engine.SetFootballDistance(value);
        ToGoHost.Content = _toGo;

        _ballOn = new Stepper("BALL ON", 26);
        _ballOn.ToolTip = "Ball spot as a yard line, 0–50, on either team's half. Its side is independent of possession.";
        // + raises the yard line, − lowers it, matching every other stepper on the panel.
        _ballOn.SetButtons([new("+", 1, "football.ball.plus")], [new("−", -1, "football.ball.minus")]);
        _ballOn.Stepped += Step;
        _ballOn.Committed += value => _engine.SetFootballDistance(_engine.State.YardsToGo, value);
        // A full-size console button, keybindable like the rest, naming the half the ball is on.
        _ballSide = new Button
        {
            Content = "HOME ▶",
            Tag = "football.ball.side",
            Style = (Style)FindResource("Tiny"),
            Margin = new Thickness(2, 6, 2, 0),
            FontSize = 11,
            ToolTip = "Which team's half of the field the ball is on — independent of possession"
        };
        _ballSide.Click += Action_Click;
        var ballColumn = new StackPanel();
        ballColumn.Children.Add(_ballOn);
        ballColumn.Children.Add(_ballSide);
        BallOnHost.Content = ballColumn;

        // How the quarter prints (Quarter 1 / Q1 / 1) belongs to the graphic, not the console:
        // it is a property of the element in Scorebug Studio.
        QuarterHost.Content = BuildQuarterControl();

        _awayTimeouts = BuildTimeoutStepper(false);
        _homeTimeouts = BuildTimeoutStepper(true);
        AwayTimeoutsHost.Content = _awayTimeouts;
        HomeTimeoutsHost.Content = _homeTimeouts;

        AwayName.KeyDown += Name_KeyDown;
        HomeName.KeyDown += Name_KeyDown;
        AwayName.LostKeyboardFocus += Name_LostFocus;
        HomeName.LostKeyboardFocus += Name_LostFocus;

        // Update Output keeps its designed fill instead of the generic panel surface, so the
        // operator's secondary interface colour is what the main action actually wears.
        ThemeManager.SetSkipTheme(UpdateButton, true);
        ThemeManager.SetSkipTheme(HornButton, true);
        SyncHorn();

        _engine.Changed += Refresh;
        Loaded += (_, _) => { ApplyKeybindHints(); Refresh(); };
        Refresh();
    }

    /// <summary>The steppers the tests and the host inspect.</summary>
    public string HomeScoreText => _homeScore.Text;
    public string AwayScoreText => _awayScore.Text;
    public string PlayClockText => PlayClockValue.Text;
    public Stepper HomeScore => _homeScore;
    public Stepper AwayScore => _awayScore;
    public Stepper BallOn => _ballOn;

    Stepper BuildScoreStepper(bool home)
    {
        // No caption: the card already says VISITORS or HOME, and the line it freed is what keeps
        // the − row inside the card when a panel opens at its smallest allowed size.
        var stepper = new Stepper(null, 36);
        stepper.Stepped += Step;
        stepper.Committed += value => _engine.SetScore(home, value);
        return stepper;
    }

    Stepper BuildTimeoutStepper(bool home)
    {
        var stepper = new Stepper(home ? "Home" : "Visitors", 21);
        stepper.SetButtons([new("+", 1, home ? "home.timeout.plus" : "away.timeout.plus")], [new("−", -1, home ? "home.timeout" : "away.timeout")]);
        stepper.Stepped += Step;
        stepper.Committed += value => _engine.SetTimeoutCount(home, value);
        return stepper;
    }

    /// <summary>A compact − / quarter / + row, built from the same full-size console buttons.</summary>
    FrameworkElement BuildQuarterControl()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = new TextBlock { Text = "QUARTER", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        caption.Style = (Style)FindResource("Caption");
        grid.Children.Add(caption);
        _quarterValue.FontFamily = new FontFamily("Consolas");
        _quarterValue.FontSize = 22;
        _quarterValue.FontWeight = FontWeights.Bold;
        _quarterValue.TextAlignment = TextAlignment.Center;
        _quarterValue.Background = Brushes.Transparent;
        _quarterValue.BorderThickness = new Thickness(0);
        _quarterValue.Margin = new Thickness(4, 0, 4, 0);
        _quarterValue.ToolTip = "Type a period number and press Enter";
        _quarterValue.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (int.TryParse(_quarterValue.Text.Trim(), out var period)) _engine.SetPeriod(period);
            else _quarterValue.Text = _engine.State.Period.ToString();
            Keyboard.ClearFocus();
            e.Handled = true;
        };
        _quarterValue.LostKeyboardFocus += (_, _) => _quarterValue.Text = _engine.State.Period.ToString();
        Grid.SetColumn(_quarterValue, 1);
        grid.Children.Add(_quarterValue);
        var minus = new Button { Content = "−", Tag = "period.previous", Style = (Style)FindResource("Tiny"), MinWidth = 44 };
        minus.Click += Action_Click;
        Grid.SetColumn(minus, 2);
        grid.Children.Add(minus);
        var plus = new Button { Content = "+", Tag = "period.next", Style = (Style)FindResource("Tiny"), MinWidth = 44 };
        plus.Click += Action_Click;
        Grid.SetColumn(plus, 3);
        grid.Children.Add(plus);
        return grid;
    }

    /// <summary>Adds/subtracts through the action table so keybinds and buttons share one path.</summary>
    void Step(Stepper.StepButton button)
    {
        if (button.ActionId is not null) Execute(button.ActionId);
        else if (button.Delta != 0) _engine.Score(false, button.Delta);
    }

    /// <summary>Small explanatory tips next to headings; off by default (Preferences → Show heading tips).</summary>
    public void SetHintVisibility(bool show)
    {
        _showHints = show;
        DetailCardHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SyncLiveState(bool live)
    {
        if (LiveOutputToggle is null) return;
        _syncingLive = true;
        LiveOutputToggle.IsChecked = live;
        LiveOutputToggle.Content = live ? "Live: ON" : "Live: OFF";
        _syncingLive = false;
    }

    public void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var s = _engine.State;

        _loadingNames = true;
        if (!AwayName.IsKeyboardFocused) AwayName.Text = s.AwayTeam;
        if (!HomeName.IsKeyboardFocused) HomeName.Text = s.HomeTeam;
        _loadingNames = false;

        RebuildScoreButtons(s);
        _awayScore.Value = s.AwayScore;
        _homeScore.Value = s.HomeScore;
        _awayTimeouts.Value = s.AwayTimeouts;
        _homeTimeouts.Value = s.HomeTimeouts;
        _down.Value = s.Down;
        _toGo.Value = s.YardsToGo;
        _ballOn.Value = s.YardsToGoal;
        _ballSide.Content = s.BallOnHomeSide ? "HOME ▶" : "◀ VISITORS";
        if (!_quarterValue.IsKeyboardFocused) _quarterValue.Text = s.Period.ToString();

        // Team identity: the logo thumbnail. The abbreviation and colours live in the team box;
        // printing them beside VISITORS / HOME only repeated the heading.
        ApplyLogo(AwayLogoImage, ref _awayLogoLoaded, s.AwayLogoPath);
        ApplyLogo(HomeLogoImage, ref _homeLogoLoaded, s.HomeLogoPath);
        LayoutLabel.Text = _host.LayoutLabel;

        if (!ClockValue.IsKeyboardFocused) ClockValue.Text = GameEngine.FormatClock(s.ClockSeconds);
        if (!PlayClockValue.IsKeyboardFocused) PlayClockValue.Text = s.PlayClockSeconds.ToString();
        if (!ShotClockValue.IsKeyboardFocused) ShotClockValue.Text = s.ShotClockSeconds.ToString();
        if (!TimeoutClockValue.IsKeyboardFocused) TimeoutClockValue.Text = GameEngine.FormatClock(s.TimeoutClockSeconds);
        ClockToggle.Content = s.ClockRunning ? "Stop" : "Start";
        PlayClockToggle.Content = s.PlayClockRunning ? "Stop" : "Start";
        ShotClockToggle.Content = s.ShotClockRunning ? "Stop" : "Start";
        TimeoutClockToggle.Content = s.TimeoutClockRunning ? "Stop" : "Start";

        PossessionValue.Text = s.HomePossession ? s.HomeTeam : s.AwayTeam;
        AwayPossessionTag.Text = s.HomePossession ? "" : "◀ POSSESSION";
        HomePossessionTag.Text = s.HomePossession ? "POSSESSION ▶" : "";
        AwayPossessionButton.FontWeight = s.HomePossession ? FontWeights.Normal : FontWeights.Bold;
        HomePossessionButton.FontWeight = s.HomePossession ? FontWeights.Bold : FontWeights.Normal;

        var football = s.SportId == "football";
        FootballDetail.Visibility = football ? Visibility.Visible : Visibility.Collapsed;
        OtherDetail.Visibility = football ? Visibility.Collapsed : Visibility.Visible;
        NewSeriesButton.Visibility = football ? Visibility.Visible : Visibility.Collapsed;
        DetailCardTitle.Text = football ? "DOWN & DISTANCE" : "SPORT CONTROLS";
        DetailCardHint.Text = football ? $"{Ordinal(s.Down)} & {s.YardsToGo} · ball on {(s.BallOnHomeSide ? "home" : "visitors")}' {s.YardsToGoal}" : string.Empty;
        DetailCardHint.Visibility = _showHints && football ? Visibility.Visible : Visibility.Collapsed;
        PlayClockRow.Visibility = football ? Visibility.Visible : Visibility.Collapsed;
        // Football has a play clock; showing a shot clock there as well only crowded the page.
        ShotClockRow.Visibility = _engine.Sport.HasShotClock && !football ? Visibility.Visible : Visibility.Collapsed;
        BuildSportControls(s);

        var staged = _host.HasStagedChanges;
        StagedText.Text = staged ? "●  NOT ON AIR YET — press Update Output" : "EVERYTHING ON AIR";
        StagedText.Foreground = staged ? StagedBrush : PublishedBrush;
        UpdateButton.FontWeight = staged ? FontWeights.Bold : FontWeights.SemiBold;
        SyncLiveState(_host.LiveScoreClock);

        RefreshStats();
    }

    /// <summary>Loads a team logo once per path; the console refreshes every clock tick.</summary>
    void ApplyLogo(Image target, ref string? cache, string? path)
    {
        var wanted = File.Exists(path) ? path : null;
        if (string.Equals(cache, wanted)) return;
        cache = wanted;
        if (wanted is null) { target.Source = null; return; }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(wanted);
            image.EndInit();
            image.Freeze();
            target.Source = image;
        }
        catch { target.Source = null; }
    }


    void AwayDetails_Click(object sender, RoutedEventArgs e) => EditTeam(false);
    void HomeDetails_Click(object sender, RoutedEventArgs e) => EditTeam(true);

    void EditTeam(bool home)
    {
        var dialog = new TeamIdentityWindow(_engine, home, PreferencesStore.Load().Theme) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) Refresh();
    }

    void LayoutPrev_Click(object sender, RoutedEventArgs e) => ShiftLayout(-1);
    void LayoutNext_Click(object sender, RoutedEventArgs e) => ShiftLayout(1);

    void ShiftLayout(int delta)
    {
        _host.ShiftLayout(delta);
        Refresh();
    }

    void ShotClock14_Click(object sender, RoutedEventArgs e) => _engine.SetShotClock(14);

    // Shot clock and timeout clock are typed over exactly like the game clock: the timing section
    // should not have second-class values that can only be nudged with buttons.
    void ShotClock_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitShotClock();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ShotClockValue.Text = _engine.State.ShotClockSeconds.ToString();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    void ShotClock_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitShotClock();

    void CommitShotClock()
    {
        if (int.TryParse(ShotClockValue.Text.Trim(), out var seconds)) _engine.SetShotClock(seconds);
        else ShotClockValue.Text = _engine.State.ShotClockSeconds.ToString();
    }

    void TimeoutClock_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTimeoutClock();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TimeoutClockValue.Text = GameEngine.FormatClock(_engine.State.TimeoutClockSeconds);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    void TimeoutClock_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitTimeoutClock();

    void CommitTimeoutClock()
    {
        if (ClockFormat.TryParse(TimeoutClockValue.Text, out var seconds)) _engine.SetTimeoutClock(seconds);
        else TimeoutClockValue.Text = GameEngine.FormatClock(_engine.State.TimeoutClockSeconds);
    }

    void RebuildScoreButtons(GameState state)
    {
        var signature = string.Join(",", state.ScoringOptions);
        if (signature == _scoreSignature) return;
        _scoreSignature = signature;
        var options = state.ScoringOptions.Length == 0 ? [1] : state.ScoringOptions;
        _awayScore.SetButtons(
            options.Select(o => new Stepper.StepButton($"+{o}", o, $"away.score.plus{o}")),
            options.Select(o => new Stepper.StepButton($"−{o}", -o, $"away.score.minus{o}")));
        _homeScore.SetButtons(
            options.Select(o => new Stepper.StepButton($"+{o}", o, $"home.score.plus{o}")),
            options.Select(o => new Stepper.StepButton($"−{o}", -o, $"home.score.minus{o}")));
    }

    /// <summary>
    /// Draws the sport's own values — bases, count, power play, overs, sets — as one compact control
    /// each, so a baseball or hockey game has everything it needs without ever scrolling the page.
    /// </summary>
    void BuildSportControls(GameState state)
    {
        var signature = state.SportId + "|" + string.Join("|", state.Controls.Select(c => $"{c.Id}:{c.Label}:{c.Kind}:{c.Min}:{c.Max}:{c.OnText}"));
        if (signature != _sportControlsSignature)
        {
            _sportControlsSignature = signature;
            OtherDetail.Children.Clear();
            _sportValueBoxes.Clear();
            _sportFlagButtons.Clear();
            _baseSquares.Clear();
            _baseLabels.Clear();
            foreach (var control in state.Controls)
                OtherDetail.Children.Add(control.Kind == SportControlKind.Flag ? BuildFlagControl(control) : BuildCounterControl(control));
        }
        RefreshSportControlValues(state);
    }

    /// <summary>A compact − / value / + control: label on the left, typed-over value in the middle.</summary>
    FrameworkElement BuildCounterControl(SportControl control)
    {
        var grid = new Grid { Width = 186, Height = 34, Margin = new Thickness(0, 0, 6, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = control.Label.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 4, 0),
            ToolTip = control.Label
        };
        grid.Children.Add(label);

        var minus = CompactButton("−", $"sport.{control.Id}.minus", $"{control.Label} −1");
        Grid.SetColumn(minus, 1);
        grid.Children.Add(minus);

        var value = new TextBox
        {
            Text = "0",
            Width = 38,
            Height = 30,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 2, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = $"{control.Label} — click to type a value, then press Enter"
        };
        value.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitSportValue(control);
                // Enter settles the value and hands the box back, so the next keypress is a shortcut
                // instead of another digit in a field nobody meant to keep typing in.
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                value.Text = SportValues.Read(_engine.State, control.Id).ToString();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        };
        value.LostKeyboardFocus += (_, _) => CommitSportValue(control);
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);

        var plus = CompactButton("+", $"sport.{control.Id}.plus", $"{control.Label} +1");
        Grid.SetColumn(plus, 3);
        grid.Children.Add(plus);

        _sportValueBoxes[control.Id] = value;
        return grid;
    }

    /// <summary>An on/off sport value — power play, a runner on base, top or bottom of the inning.</summary>
    FrameworkElement BuildFlagControl(SportControl control)
    {
        var toggle = new ToggleButton
        {
            Height = 34,
            MinWidth = 150,
            Padding = new Thickness(9, 4, 9, 4),
            Margin = new Thickness(0, 0, 6, 5),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Tag = $"sport.{control.Id}.toggle",
            Content = control.Label.ToUpperInvariant()
        };
        // A runner on base is a base, not an abbreviation: a square that fills in when occupied.
        if (IsBaseControl(control.Id))
        {
            var baseLabel = new TextBlock
            {
                Text = control.Id switch
                {
                    "baseball.first" => "1ST",
                    "baseball.second" => "2ND",
                    _ => "3RD"
                },
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var square = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1.5),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(square);
            content.Children.Add(baseLabel);
            toggle.Content = content;
            toggle.ToolTip = $"{control.Label} — click to set or clear the runner";
            _baseSquares[control.Id] = square;
            _baseLabels[control.Id] = baseLabel;
        }
        toggle.Click += Action_Click;
        _sportFlagButtons[control.Id] = toggle;
        return toggle;
    }

    static bool IsBaseControl(string id) =>
        id is "baseball.first" or "baseball.second" or "baseball.third";

    Button CompactButton(string text, string actionId, string tip)
    {
        var button = new Button
        {
            Content = text,
            Tag = actionId,
            Width = 30,
            Height = 30,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0),
            FontSize = 14,
            ToolTip = tip
        };
        button.Click += Action_Click;
        return button;
    }

    void CommitSportValue(SportControl control)
    {
        if (!_sportValueBoxes.TryGetValue(control.Id, out var box)) return;
        if (int.TryParse(box.Text.Trim(), out var value)) _engine.SetSportValue(control.Id, value);
        box.Text = SportValues.Read(_engine.State, control.Id).ToString();
    }

    void RefreshSportControlValues(GameState state)
    {
        foreach (var control in state.Controls)
        {
            if (control.Kind == SportControlKind.Flag)
            {
                if (!_sportFlagButtons.TryGetValue(control.Id, out var toggle)) continue;
                var on = SportValues.Read(state, control.Id) != 0;
                toggle.IsChecked = on;
                if (_baseSquares.TryGetValue(control.Id, out var square))
                {
                    square.Background = on ? BaseLit : Brushes.Transparent;
                    square.BorderBrush = on ? BaseLit : BaseEmpty;
                    if (_baseLabels.TryGetValue(control.Id, out var label)) label.Foreground = on ? BaseLit : null;
                    continue;
                }
                toggle.Content = on ? $"{control.Label.ToUpperInvariant()} · {control.FlagOnLabel}" : $"{control.Label.ToUpperInvariant()} · {control.FlagOffLabel}";
            }
            else if (_sportValueBoxes.TryGetValue(control.Id, out var box) && !box.IsKeyboardFocused)
            {
                box.Text = SportValues.Read(state, control.Id).ToString();
            }
        }
    }

    void RefreshStats()
    {
        var stats = _engine.State.Stats;
        var signature = string.Join("|", stats.Select(s => $"{s.Id}:{s.Name}"));
        if (signature != _statsSignature)
        {
            _statsSignature = signature;
            StatsHost.Children.Clear();
            _statValues.Clear();
            foreach (var stat in stats) StatsHost.Children.Add(BuildStatRow(stat));
        }
        foreach (var stat in stats)
            if (_statValues.TryGetValue(stat.Id, out var value)) value.Text = stat.Display;
    }

    /// <summary>Custom stats use the same add-above / value / subtract-below shape as scores, two per row.</summary>
    FrameworkElement BuildStatRow(GameStat stat)
    {
        // A custom stat accepts any text, not just a number: "N/A", "3rd" and "TBD" are all values.
        var stepper = new Stepper(stat.Name, 18) { Width = 104, Margin = new Thickness(0, 0, 4, 4), AcceptText = true };
        stepper.SetButtons
        (
            [new("＋ 1", 1, null, $"{stat.Name} +1")],
            [new("− 1", -1, null, $"{stat.Name} −1")]
        );
        stepper.Stepped += button => _engine.AdjustStat(stat.Id, button.Delta);
        stepper.Committed += value => _engine.SetStat(stat.Id, value);
        stepper.TextCommitted += text => _engine.SetStatText(stat.Id, text);
        stepper.Text = stat.Display;

        var host = new Grid { Width = 110, Margin = new Thickness(0, 0, 4, 2), ToolTip = $"Remove {stat.Name}" };
        host.Children.Add(stepper);
        var remove = new Button
        {
            Content = "×", Width = 16, Height = 16, Padding = new Thickness(0), FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, ToolTip = $"Remove {stat.Name}"
        };
        remove.Click += (_, _) => _engine.RemoveStat(stat.Id);
        host.Children.Add(remove);

        _statValues[stat.Id] = stepper;
        return host;
    }

    void Name_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        CommitNames();
        Keyboard.ClearFocus();
    }

    void Name_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitNames();

    void CommitNames()
    {
        if (_loadingNames) return;
        var home = HomeName.Text.Trim();
        var away = AwayName.Text.Trim();
        if (home.Length == 0) home = _engine.State.HomeTeam;
        if (away.Length == 0) away = _engine.State.AwayTeam;
        if (home == _engine.State.HomeTeam && away == _engine.State.AwayTeam) return;
        _engine.SetTeams(home, away);
    }

    void ClockValue_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (ClockFormat.TryParse(ClockValue.Text, out var seconds)) _engine.SetClock(seconds);
            else ClockValue.Text = GameEngine.FormatClock(_engine.State.ClockSeconds);
            // Leave the box: while it holds focus the display deliberately stops following the
            // engine, so a typed value would freeze the clock on screen (no countdown, no 0).
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { ClockValue.Text = GameEngine.FormatClock(_engine.State.ClockSeconds); Keyboard.ClearFocus(); e.Handled = true; }
    }

    void ClockValue_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ClockFormat.TryParse(ClockValue.Text, out var seconds)) _engine.SetClock(seconds);
        else ClockValue.Text = GameEngine.FormatClock(_engine.State.ClockSeconds);
    }

    void PlayClock_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitPlayClock();
            // Hand the box back to the running clock; otherwise it keeps the typed value on screen
            // and the play clock never counts down (or prints its final 0).
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { PlayClockValue.Text = _engine.State.PlayClockSeconds.ToString(); Keyboard.ClearFocus(); e.Handled = true; }
    }

    void PlayClock_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitPlayClock();

    void CommitPlayClock()
    {
        if (int.TryParse(PlayClockValue.Text.Trim(), out var seconds)) _engine.SetPlayClock(seconds);
        else PlayClockValue.Text = _engine.State.PlayClockSeconds.ToString();
    }

    void PlayClock40_Click(object sender, RoutedEventArgs e) => _engine.ResetPlayClock(40);
    void PlayClock25_Click(object sender, RoutedEventArgs e) => _engine.ResetPlayClock(25);

    void AddStat_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddStatDialog(_engine.State.Stats.Select(s => s.Name).ToArray()) { Owner = Window.GetWindow(this) };
        ThemeManager.Apply(dialog, PreferencesStore.Load().Theme);
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.StatName)) _engine.AddStat(dialog.StatName);
    }

    // ---------- horn ----------

    void SyncHorn()
    {
        if (HornType is null) return;
        var preferences = PreferencesStore.Load();
        _hornSync = true;
        try
        {
            foreach (var item in HornType.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag as string, preferences.HornSound, StringComparison.OrdinalIgnoreCase))
                {
                    HornType.SelectedItem = item;
                    break;
                }
            }
            HornPickButton.ToolTip = string.IsNullOrWhiteSpace(preferences.HornCustomPath)
                ? "Choose a custom horn audio file"
                : $"Custom horn: {preferences.HornCustomPath}";
        }
        finally { _hornSync = false; }
    }

    void HornType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_hornSync || HornType?.SelectedItem is not ComboBoxItem item) return;
        var sound = item.Tag as string ?? nameof(HornSound.AirHorn);
        var preferences = PreferencesStore.Load();
        preferences.HornSound = sound;
        PreferencesStore.Save(preferences);
        HornPickButton.ToolTip = string.IsNullOrWhiteSpace(preferences.HornCustomPath)
            ? "Choose a custom horn audio file"
            : $"Custom horn: {preferences.HornCustomPath}";
        if (HornPlayer.Parse(sound) == HornSound.Custom && string.IsNullOrWhiteSpace(preferences.HornCustomPath))
            PickHorn();
    }

    void Horn_Click(object sender, RoutedEventArgs e)
    {
        var preferences = PreferencesStore.Load();
        if (HornPlayer.Parse(preferences.HornSound) == HornSound.Custom
            && string.IsNullOrWhiteSpace(preferences.HornCustomPath))
        {
            PickHorn();
            preferences = PreferencesStore.Load();
        }
        if (!HornPlayer.Play(preferences.HornSound, preferences.HornCustomPath))
            HornButton.ToolTip = "Could not play that horn. Pick a WAV, MP3 or AIFF file.";
    }

    void HornPick_Click(object sender, RoutedEventArgs e) => PickHorn();

    void PickHorn()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a horn audio file",
            Filter = "Audio files (*.wav;*.mp3;*.aiff;*.aif)|*.wav;*.mp3;*.aiff;*.aif|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        var preferences = PreferencesStore.Load();
        preferences.HornSound = nameof(HornSound.Custom);
        preferences.HornCustomPath = dialog.FileName;
        PreferencesStore.Save(preferences);
        SyncHorn();
    }

    void LiveOutput_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingLive || LiveOutputToggle is null || _host is null) return;
        var enabled = LiveOutputToggle.IsChecked == true;
        LiveOutputToggle.Content = enabled ? "Live: ON" : "Live: OFF";
        _host.LiveScoreClock = enabled;
    }

    void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string actionId }) Execute(actionId);
    }

    public void Execute(string actionId)
    {
        switch (actionId)
        {
            case "output.publish": _host.PublishOutput(); break;
            case "output.live.toggle": _host.LiveScoreClock = !_host.LiveScoreClock; break;
            case "possession.away": _engine.SetPossession(false); break;
            case "possession.home": _engine.SetPossession(true); break;
            case "possession.toggle": _engine.TogglePossession(); break;
            case "home.timeout.plus": _engine.AdjustTimeouts(true, 1); break;
            case "away.timeout.plus": _engine.AdjustTimeouts(false, 1); break;
            case "home.timeout": _engine.Timeout(true); break;
            case "away.timeout": _engine.Timeout(false); break;
            case "clock.toggle": _engine.ToggleClock(); break;
            case "clock.minus60": _engine.AdjustClock(-60); break;
            case "clock.plus60": _engine.AdjustClock(60); break;
            case "clock.minus1": _engine.AdjustClock(-1); break;
            case "clock.plus1": _engine.AdjustClock(1); break;
            case "clock.set": SetClockDialog(); break;
            case "period.next": _engine.NextPeriod(); break;
            case "period.previous": _engine.PreviousPeriod(); break;
            case "football.playclock.toggle": _engine.TogglePlayClock(); break;
            case "football.playclock.reset40": _engine.ResetPlayClock(40); break;
            case "football.playclock.reset25": _engine.ResetPlayClock(25); break;
            case "football.playclock.minus": _engine.SetPlayClock(_engine.State.PlayClockSeconds - 1); break;
            case "football.playclock.plus": _engine.SetPlayClock(_engine.State.PlayClockSeconds + 1); break;
            case "football.playclock.set": SetPlayClockDialog(); break;
            case "timeoutclock.toggle": _engine.ToggleTimeoutClock(); break;
            case "timeoutclock.reset": _engine.ResetTimeoutClock(60); break;
            case "timeoutclock.minus10": _engine.SetTimeoutClock(_engine.State.TimeoutClockSeconds - 10); break;
            case "timeoutclock.minus1": _engine.SetTimeoutClock(_engine.State.TimeoutClockSeconds - 1); break;
            case "timeoutclock.plus1": _engine.SetTimeoutClock(_engine.State.TimeoutClockSeconds + 1); break;
            case "timeoutclock.plus10": _engine.SetTimeoutClock(_engine.State.TimeoutClockSeconds + 10); break;
            case "timeoutclock.set": SetTimeoutClockDialog(); break;
            case "football.down.next": NextDown(); break;
            case "football.down.previous": _engine.PreviousDown(); break;
            case "football.down.recall": _engine.RecallLastDown(); break;
            case "football.distance.minus": _engine.SetFootballDistance(_engine.State.YardsToGo - 1); break;
            case "football.distance.plus": _engine.SetFootballDistance(_engine.State.YardsToGo + 1); break;
            case "football.ball.minus": _engine.SetFootballDistance(_engine.State.YardsToGo, Math.Max(0, _engine.State.YardsToGoal - 1)); break;
            case "football.ball.side": _engine.SetBallSide(!_engine.State.BallOnHomeSide); break;
            case "football.ball.plus": _engine.SetFootballDistance(_engine.State.YardsToGo, Math.Min(50, _engine.State.YardsToGoal + 1)); break;
            case "football.series.new": _engine.NewSeries(); break;
            case "basketball.foul.home": _engine.AddFoul(true); break;
            case "basketball.foul.away": _engine.AddFoul(false); break;
            case "basketball.shotclock.toggle": _engine.ToggleShotClock(); break;
            case "basketball.shotclock.reset": _engine.ResetShotClock(); break;
            case "basketball.shotclock.minus": _engine.SetShotClock(_engine.State.ShotClockSeconds - 1); break;
            case "basketball.shotclock.plus": _engine.SetShotClock(_engine.State.ShotClockSeconds + 1); break;
            case "basketball.shotclock.set": SetShotClockDialog(); break;
            case "baseball.ball": _engine.BaseballBall(); break;
            case "baseball.strike": _engine.BaseballStrike(); break;
            case "baseball.foul": _engine.BaseballStrike(true); break;
            case "baseball.out": _engine.BaseballOut(); break;
            case "baseball.hit.home": _engine.BaseballHit(true); break;
            case "baseball.hit.away": _engine.BaseballHit(false); break;
            case "soccer.yellow.home": _engine.AddCard(true); break;
            case "soccer.yellow.away": _engine.AddCard(false); break;
            case "soccer.red.home": _engine.AddCard(true, true); break;
            case "soccer.red.away": _engine.AddCard(false, true); break;
            case "soccer.shot.home": _engine.AddSoccerShot(true); break;
            case "soccer.shot.away": _engine.AddSoccerShot(false); break;
            case "soccer.corner.home": _engine.AddSoccerCorner(true); break;
            case "soccer.corner.away": _engine.AddSoccerCorner(false); break;
            case "global.undo": _engine.Undo(); break;
            default:
                if (TrySportAction(actionId)) break;
                if (TryScoreAction(actionId, out var home, out var amount)) _engine.Score(home, amount);
                break;
        }
    }

    /// <summary>
    /// Sport values share one naming scheme — <c>sport.&lt;control id&gt;.plus</c>, <c>.minus</c> and
    /// <c>.toggle</c> — so every sport's buttons, keybinds and the remote control use the same path.
    /// </summary>
    bool TrySportAction(string actionId)
    {
        if (!actionId.StartsWith("sport.", StringComparison.Ordinal)) return false;
        var rest = actionId["sport.".Length..];
        var cut = rest.LastIndexOf('.');
        if (cut <= 0) return false;
        var id = rest[..cut];
        switch (rest[(cut + 1)..])
        {
            case "plus": _engine.AdjustSportValue(id, 1); return true;
            case "minus": _engine.AdjustSportValue(id, -1); return true;
            case "toggle": _engine.ToggleSportFlag(id); return true;
            default: return false;
        }
    }

    static bool TryScoreAction(string actionId, out bool home, out int amount)
    {
        home = false;
        amount = 0;
        string rest;
        if (actionId.StartsWith("home.score.", StringComparison.Ordinal)) { home = true; rest = actionId["home.score.".Length..]; }
        else if (actionId.StartsWith("away.score.", StringComparison.Ordinal)) rest = actionId["away.score.".Length..];
        else return false;

        var sign = 0;
        if (rest.StartsWith("plus", StringComparison.Ordinal)) { sign = 1; rest = rest[4..]; }
        else if (rest.StartsWith("minus", StringComparison.Ordinal)) { sign = -1; rest = rest[5..]; }
        if (sign == 0 || !int.TryParse(rest, out var value)) return false;
        amount = sign * value;
        return true;
    }

    void NextDown()
    {
        if (_engine.State.SportId != "football") return;
        var owner = Window.GetWindow(this);
        if (_engine.State.Down == 4
            && !ConfirmWindow.Ask(owner, PreferencesStore.Load().Theme, "NEW SERIES",
                "Fourth down: start a new series and reset distance?", "Start new series"))
            return;
        _engine.AdvanceDown();
    }

    void SetClockDialog()
    {
        var dialog = new ClockTimeDialog("Set game clock", _engine.State.ClockSeconds) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) _engine.SetClock(dialog.Seconds);
    }

    void SetTimeoutClockDialog()
    {
        var dialog = new ClockTimeDialog("Set timeout clock", _engine.State.TimeoutClockSeconds) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) _engine.SetTimeoutClock(dialog.Seconds);
    }

    void SetPlayClockDialog()
    {
        var dialog = new ClockTimeDialog("Set play clock", _engine.State.PlayClockSeconds) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) _engine.SetPlayClock(dialog.Seconds);
    }

    void SetShotClockDialog()
    {
        var dialog = new ClockTimeDialog("Set shot clock", _engine.State.ShotClockSeconds) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) _engine.SetShotClock(dialog.Seconds);
    }

    public void ShowKeybindsDialog()
    {
        var dialog = new KeybindsWindow(_keybinds, _engine.State.SportId, _engine.Sport.Name) { Owner = Window.GetWindow(this) };
        ThemeManager.Apply(dialog, PreferencesStore.Load().Theme);
        if (dialog.ShowDialog() == true)
        {
            ApplyKeybinds(dialog.Result);
            KeybindStore.Save(_keybinds);
        }
    }

    public void ApplyKeybinds(KeybindSettings settings)
    {
        _keybinds = settings;
        ApplyKeybindHints();
    }

    public void ApplyKeybindHints()
    {
        var hints = _keybinds.Bindings;
        foreach (var button in FindTaggedButtons(this))
        {
            var id = (string)button.Tag;
            var label = ControlActions.LabelFor(id);
            button.ToolTip = hints.TryGetValue(id, out var gesture) ? $"{label}  ·  {gesture}" : label;
        }
    }

    /// <summary>Routes a keypress through the bindings. False means the key was not ours.</summary>
    public bool TryHandleKey(Key key, ModifierKeys modifiers)
    {
        if (Keyboard.FocusedElement is TextBox) return false;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return false;
        var actionId = KeybindStore.Match(_keybinds, key, modifiers);
        if (actionId is null) return false;
        Execute(actionId);
        return true;
    }

    static IEnumerable<Button> FindTaggedButtons(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && button.Tag is string) yield return button;
            foreach (var nested in FindTaggedButtons(child)) yield return nested;
        }
    }

    static string Ordinal(int n) => n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th" };
}
