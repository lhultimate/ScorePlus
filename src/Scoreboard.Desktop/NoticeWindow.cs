using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// A one-button message in the operator's own chrome. ScorePlus never drops a system message box on
/// the operator: those ignore the theme, carry Windows' own iconography, and look like the app has
/// crashed rather than telling them what to do next.
/// </summary>
public sealed class NoticeWindow : Window
{
    public NoticeWindow(string theme, string heading, string detail, bool warning = false)
    {
        Title = $"{heading} — ScorePlus";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var accent = new Border
        {
            Width = 4,
            Height = 22,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(warning ? Color.FromRgb(0xE0, 0x9A, 0x3C) : Color.FromRgb(0x38, 0xD7, 0xA3)),
            Margin = new Thickness(0, 0, 10, 0)
        };
        ThemeManager.SetSkipTheme(accent, true);
        titleRow.Children.Add(accent);
        titleRow.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(titleRow);

        root.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button
        {
            Content = "OK",
            IsDefault = true,
            IsCancel = true,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(18, 9, 18, 9),
            MinHeight = 38,
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x6B, 0x50)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x14, 0xA6, 0x7C)),
            Foreground = Brushes.White
        };
        // The affirmative button keeps the app's green in both themes.
        ThemeManager.SetSkipTheme(ok, true);
        ok.Click += (_, _) => Close();
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        Content = root;
        ThemeManager.Apply(this, theme);
        Loaded += (_, _) => ok.Focus();
    }

    /// <summary>
    /// Tells the operator something in the app's own chrome. The theme comes from preferences, so
    /// callers do not have to know it — and a broken preference file still opens a readable box.
    /// </summary>
    public static void Tell(Window? owner, string heading, string detail, bool warning = false)
    {
        string theme;
        try { theme = PreferencesStore.Load().Theme; } catch { theme = "Dark"; }
        var dialog = new NoticeWindow(theme, heading, detail, warning);
        if (owner is not null && owner.IsLoaded) dialog.Owner = owner;
        dialog.ShowDialog();
    }
}
