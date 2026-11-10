using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// The little box behind the … beside a team name: name, abbreviation, logo and both palette
/// colours. Team logos live here now, not in the studio, so the same logo follows the team onto
/// every layout and every scorebug element that points at it.
/// </summary>
public sealed class TeamIdentityWindow : Window
{
    readonly GameEngine _engine;
    readonly bool _home;
    readonly TextBox _name = new();
    readonly TextBox _abbreviation = new();
    readonly TextBox _mascot = new();
    readonly TextBox _record = new();
    readonly TextBox _divisionRecord = new();
    readonly TextBox _ranking = new();
    readonly CheckBox _divisionGame = new();
    readonly TextBox _primaryHex = new();
    readonly TextBox _secondaryHex = new();
    readonly Border _primarySwatch = new() { Width = 46, Height = 30, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
    readonly Border _secondarySwatch = new() { Width = 46, Height = 30, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
    readonly Image _logoPreview = new() { Stretch = Stretch.Uniform };
    readonly TextBlock _logoEmpty = new() { Text = "NO LOGO", Opacity = 0.45, FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    string? _logoPath;
    string? _pendingLogoSource;

    public TeamIdentityWindow(GameEngine engine, bool home, string theme)
    {
        _engine = engine;
        _home = home;
        var state = engine.State;
        Title = home ? "Home team details" : "Visitors team details";
        Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(23, 28, 37));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        _name.Text = home ? state.HomeTeam : state.AwayTeam;
        _abbreviation.Text = home ? state.HomeAbbreviation : state.AwayAbbreviation;
        _mascot.Text = home ? state.HomeMascot : state.AwayMascot;
        _record.Text = home ? state.HomeRecord : state.AwayRecord;
        _divisionRecord.Text = home ? state.HomeDivisionRecord : state.AwayDivisionRecord;
        _ranking.Text = home ? state.HomeRanking : state.AwayRanking;
        _divisionGame.IsChecked = state.DivisionGame;
        _primaryHex.Text = home ? state.HomePrimaryColor : state.AwayPrimaryColor;
        _secondaryHex.Text = home ? state.HomeSecondaryColor : state.AwaySecondaryColor;
        _logoPath = home ? state.HomeLogoPath : state.AwayLogoPath;

        var root = new DockPanel { Margin = new Thickness(18) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var save = new Button { Content = "Done", IsDefault = true, Padding = new Thickness(18, 8, 18, 8) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(18, 8, 18, 8) };
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var heading = new TextBlock
        {
            Text = home ? "HOME TEAM" : "VISITORS",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)),
            Margin = new Thickness(2, 0, 0, 10)
        };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(158) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ---------- logo ----------
        var logoColumn = new StackPanel();
        var logoFrame = new Border
        {
            Height = 112,
            Background = new SolidColorBrush(Color.FromRgb(10, 15, 22)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 55, 68))
        };
        var logoStack = new Grid();
        logoStack.Children.Add(_logoEmpty);
        logoStack.Children.Add(_logoPreview);
        logoFrame.Child = logoStack;
        logoColumn.Children.Add(logoFrame);
        var chooseLogo = new Button { Content = "Select image…", Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(8, 6, 8, 6), FontSize = 12 };
        chooseLogo.Click += (_, _) => ChooseLogo();
        logoColumn.Children.Add(chooseLogo);
        var removeLogo = new Button { Content = "Remove image", Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(8, 6, 8, 6), FontSize = 12 };
        removeLogo.Click += (_, _) => { _pendingLogoSource = null; _logoPath = null; ShowLogo(); };
        logoColumn.Children.Add(removeLogo);
        Grid.SetColumn(logoColumn, 0);
        body.Children.Add(logoColumn);

        // ---------- fields ----------
        var fields = new StackPanel();
        fields.Children.Add(Label("Name"));
        fields.Children.Add(_name);
        fields.Children.Add(Label("Abbreviation"));
        _abbreviation.CharacterCasing = CharacterCasing.Upper;
        _abbreviation.MaxLength = 8;
        fields.Children.Add(_abbreviation);
        fields.Children.Add(Label("Mascot name"));
        _mascot.MaxLength = 80;
        fields.Children.Add(_mascot);
        fields.Children.Add(Label("Record"));
        _record.MaxLength = 24;
        _record.ToolTip = "Overall record, for example 8-2 or 12-4-1. Add it to the scorebug with {{HOME_RECORD}} / {{AWAY_RECORD}}; a division record prints after it in brackets.";
        fields.Children.Add(_record);
        fields.Children.Add(Label("Division record"));
        _divisionRecord.MaxLength = 24;
        _divisionRecord.ToolTip = "Division wins-losses, for example 2-2. The record prints as 4-2 (2-2) when this is set.";
        fields.Children.Add(_divisionRecord);
        fields.Children.Add(Label("Ranking"));
        _ranking.MaxLength = 12;
        _ranking.ToolTip = "Ranking shown on the team, for example #21. Printed by {{HOME_RANKING}} / {{AWAY_RANKING}}.";
        fields.Children.Add(_ranking);
        fields.Children.Add(Label("Game type"));
        _divisionGame.Content = "This is a division game — update division records at the final";
        _divisionGame.FontSize = 11;
        _divisionGame.Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178));
        _divisionGame.Margin = new Thickness(4, 2, 4, 2);
        _divisionGame.ToolTip = "When the game ends, the result is added to both teams' division records as well as their overall records.";
        fields.Children.Add(_divisionGame);
        fields.Children.Add(Label("Primary color"));
        fields.Children.Add(ColorRow(_primarySwatch, _primaryHex, () => PickColor(_primaryHex, _primarySwatch)));
        fields.Children.Add(Label("Secondary color"));
        fields.Children.Add(ColorRow(_secondarySwatch, _secondaryHex, () => PickColor(_secondaryHex, _secondarySwatch)));
        fields.Children.Add(new TextBlock
        {
            Text = "Any colour on the scorebug can use a team's primary or secondary colour. The logo is available to logo elements on every layout.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(124, 138, 156)),
            Margin = new Thickness(4, 12, 4, 0)
        });
        Grid.SetColumn(fields, 2);
        body.Children.Add(fields);

        root.Children.Add(body);
        Content = root;
        ShowLogo();
        PaintSwatches();
        // These two show the colours that are already set. The theme pass repaints every Border
        // it finds, which is why the swatches used to look empty until a colour was picked.
        ThemeManager.SetSkipTheme(_primarySwatch, true);
        ThemeManager.SetSkipTheme(_secondarySwatch, true);
        ThemeManager.Apply(this, theme);
        PaintSwatches();
        // Enter settles the field being typed instead of dismissing the window, so a whole identity
        // (name, abbreviation, colours) can be filled in without reopening it.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter || e.OriginalSource is not TextBox) return;
            e.Handled = true;
            Apply();
        };
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)),
        Margin = new Thickness(4, 8, 0, 3)
    };

    static FrameworkElement ColorRow(Border swatch, TextBox hex, Action choose)
    {
        var row = new DockPanel();
        var pick = new Button { Content = "Choose…", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(6, 0, 0, 0) };
        pick.Click += (_, _) => choose();
        DockPanel.SetDock(pick, Dock.Right);
        DockPanel.SetDock(swatch, Dock.Right);
        swatch.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(pick);
        row.Children.Add(swatch);
        hex.FontFamily = new FontFamily("Consolas");
        row.Children.Add(hex);
        return row;
    }

    void PickColor(TextBox hex, Border swatch)
    {
        var picked = ColorPickerWindow.PickBeside(this, hex.Text);
        if (picked is null) return;
        hex.Text = picked;
        try { swatch.Background = (Brush)new BrushConverter().ConvertFromString(picked)!; } catch { }
    }

    void ChooseLogo()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a team logo",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        _pendingLogoSource = dialog.FileName;
        _logoPath = dialog.FileName;
        ShowLogo();
    }

    void ShowLogo()
    {
        _logoPreview.Source = null;
        if (!string.IsNullOrWhiteSpace(_logoPath) && File.Exists(_logoPath))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(_logoPath);
                image.EndInit();
                _logoPreview.Source = image;
                _logoEmpty.Visibility = Visibility.Collapsed;
                return;
            }
            catch { }
        }
        _logoEmpty.Visibility = Visibility.Visible;
    }

    void PaintSwatches()
    {
        try { _primarySwatch.Background = (Brush)new BrushConverter().ConvertFromString(_primaryHex.Text)!; } catch { }
        try { _secondarySwatch.Background = (Brush)new BrushConverter().ConvertFromString(_secondaryHex.Text)!; } catch { }
    }

    /// <summary>Publishes what is typed. Returns false when a pending logo could not be copied.</summary>
    bool Apply()
    {
        var logo = _logoPath;
        if (_pendingLogoSource is not null)
        {
            try
            {
                var folder = AppPaths.Combine("TeamLogos");
                Directory.CreateDirectory(folder);
                var target = Path.Combine(folder, $"{( _home ? "home" : "away")}-{Guid.NewGuid():N}{Path.GetExtension(_pendingLogoSource)}");
                File.Copy(_pendingLogoSource, target);
                logo = target;
                // The import happened: a second Enter must not import the same picture again.
                _logoPath = target;
                _pendingLogoSource = null;
            }
            catch (Exception ex)
            {
                NoticeWindow.Tell(this, "That logo could not be imported", ex.Message, warning: true);
                return false;
            }
        }
        _engine.SetTeamIdentity(_home, _name.Text, _abbreviation.Text, logo, _primaryHex.Text, _secondaryHex.Text, _mascot.Text, _record.Text, _ranking.Text, _divisionRecord.Text);
        _engine.SetDivisionGame(_divisionGame.IsChecked == true);
        return true;
    }

    void Save()
    {
        if (!Apply()) return;
        // DialogResult is only valid for a modal dialog; the window can also be shown modelessly.
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = true;
        Close();
    }
}
