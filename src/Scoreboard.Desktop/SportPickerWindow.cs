using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// Asks what sport a new game is played in. It opens from the launch box's New Game button and from
/// the console's New Game button, so a game never starts as football just because that was the last
/// one: the rules, the period label, the scoring buttons and the sport data all come from here.
/// Written in the operator's own chrome — never a system message box.
/// </summary>
public sealed class SportPickerWindow : Window
{
    readonly ListBox _sports = new() { Margin = new Thickness(0, 0, 0, 14), MinHeight = 190, MaxHeight = 270 };
    readonly ComboBox _template = new() { MinHeight = 36, Margin = new Thickness(0, 0, 0, 5) };
    readonly TextBlock _templateDetail = new()
    {
        FontSize = 10,
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0x6B, 0x7E)),
        Margin = new Thickness(2, 0, 2, 12)
    };
    readonly TextBox _gameName = new()
    {
        FontSize = 13,
        MinHeight = 38,
        Padding = new Thickness(8, 7, 8, 7),
        ToolTip = "Optional. Leave blank and the game is saved as Untitled.spg."
    };
    readonly TextBlock _detail = new()
    {
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
        Margin = new Thickness(2, 0, 2, 16)
    };
    string? _selectedId;

    /// <summary>The sport profile the operator chose; null when the dialog was cancelled.</summary>
    public string? SportId => _selectedId;

    /// <summary>The optional fixture name typed before the game starts.</summary>
    public string GameName => _gameName.Text.Trim();

    /// <summary>The starting scorebug or stadium design chosen for this game.</summary>
    public string TemplateId => (_template.SelectedItem as ScorebugPreset)?.Id ?? ScorebugPresets.Classic;

    sealed record SportChoice(string Id, string Label, string Detail)
    {
        public override string ToString() => Label;
    }

    public SportPickerWindow(SportCatalog catalog, string? current, string theme, string heading = "START A NEW GAME")
    {
        Title = $"{heading} — ScorePlus";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        WindowStyle = WindowStyle.SingleBorderWindow;

        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var accent = new Border
        {
            Width = 4,
            Height = 22,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0xD7, 0xA3)),
            Margin = new Thickness(0, 0, 10, 0)
        };
        ThemeManager.SetSkipTheme(accent, true);
        titleRow.Children.Add(accent);
        titleRow.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        root.Children.Add(titleRow);

        root.Children.Add(new TextBlock
        {
            Text = "What sport is this game? The clock, the periods, the scoring buttons and the sport data all follow the profile you pick. A new game starts blank — score, clocks, down, possession and the on-air layout reset — while your scorebug designs, rosters, keybinds and media library stay.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78,
            Margin = new Thickness(0, 0, 0, 14)
        });

        root.Children.Add(new TextBlock
        {
            Text = "GAME NAME (OPTIONAL)",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
            Margin = new Thickness(0, 0, 0, 5)
        });
        root.Children.Add(_gameName);
        root.Children.Add(new TextBlock
        {
            Text = "Leave it blank to save the game as Untitled.spg. You can change it later on the production bar.",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0x6B, 0x7E)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 4, 2, 12)
        });

        root.Children.Add(new TextBlock
        {
            Text = "STARTING DESIGN",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
            Margin = new Thickness(0, 0, 0, 5)
        });
        _template.ItemsSource = ScorebugPresets.All;
        _template.DisplayMemberPath = nameof(ScorebugPreset.Name);
        _template.SelectionChanged += (_, _) =>
            _templateDetail.Text = (_template.SelectedItem as ScorebugPreset)?.Description ?? "";
        _template.SelectedIndex = 0;
        root.Children.Add(_template);
        root.Children.Add(_templateDetail);

        root.Children.Add(new TextBlock
        {
            Text = "SPORT PROFILE",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
            Margin = new Thickness(0, 0, 0, 5)
        });

        var choices = catalog.All.Select(sport => new SportChoice(sport.Id, sport.Name, Describe(sport))).ToArray();
        _sports.ItemsSource = choices;
        _sports.DisplayMemberPath = nameof(SportChoice.Label);
        _sports.SelectionChanged += (_, _) => _detail.Text = (_sports.SelectedItem as SportChoice)?.Detail ?? "";
        _sports.MouseDoubleClick += (_, _) => Start();
        root.Children.Add(_sports);
        root.Children.Add(_detail);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = MakeButton("Cancel", primary: false);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => { _selectedId = null; Close(); };
        buttons.Children.Add(cancel);
        var start = MakeButton("Start New Game", primary: true);
        start.IsDefault = true;
        start.Click += (_, _) => Start();
        buttons.Children.Add(start);
        root.Children.Add(buttons);

        Content = root;
        ThemeManager.Apply(this, theme);

        var index = catalog.All.ToList().FindIndex(sport => string.Equals(sport.Id, current, StringComparison.OrdinalIgnoreCase));
        _sports.SelectedIndex = index >= 0 ? index : 0;
        if (_sports.SelectedIndex < 0 && choices.Length > 0) _sports.SelectedIndex = 0;
        Loaded += (_, _) => _gameName.Focus();
    }

    /// <summary>The rules line under the list, so the profile is not just a name.</summary>
    static string Describe(SportDefinition sport)
    {
        var length = sport.PeriodSeconds > 0
            ? $"{Math.Round(sport.PeriodSeconds / 60.0, sport.PeriodSeconds % 60 == 0 ? 0 : 1)} min"
            : "no clock";
        var scoring = sport.ScoringOptions.Length > 0 ? string.Join(" / ", sport.ScoringOptions) : "no scoring buttons";
        return $"{sport.Periods} × {sport.Unit} · {length} per {sport.Unit.ToLowerInvariant()} · scoring {scoring}";
    }

    static Button MakeButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 9, 14, 9),
            Margin = new Thickness(6, 0, 0, 0),
            MinHeight = 38,
            Cursor = Cursors.Hand
        };
        if (primary)
        {
            button.Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x6B, 0x50));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x14, 0xA6, 0x7C));
            button.Foreground = Brushes.White;
            ThemeManager.SetSkipTheme(button, true);
        }
        return button;
    }

    void Start()
    {
        if (_sports.SelectedItem is not SportChoice choice) return;
        _selectedId = choice.Id;
        DialogResult = true;
        Close();
    }
}
