using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// A yes/no confirmation in the operator's own chrome rather than a system message box. Used for
/// destructive library actions such as deleting a roster, so the prompt looks like the rest of the
/// app and follows the chosen theme.
/// </summary>
public sealed class ConfirmWindow : Window
{
    /// <summary>True when the operator pressed the confirming button.</summary>
    public bool Confirmed { get; private set; }

    public ConfirmWindow(string theme, string heading, string detail, string confirmText, bool destructive = false)
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
            Background = new SolidColorBrush(destructive ? Color.FromRgb(0xFF, 0x6B, 0x6B) : Color.FromRgb(0x38, 0xD7, 0xA3)),
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
            Opacity = 0.78,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = MakeButton("Cancel", primary: false);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => { Confirmed = false; Close(); };
        buttons.Children.Add(cancel);

        var confirm = MakeButton(confirmText, primary: true);
        confirm.IsDefault = true;
        confirm.Click += (_, _) => { Confirmed = true; Close(); };
        buttons.Children.Add(confirm);
        root.Children.Add(buttons);

        Content = root;
        ThemeManager.Apply(this, theme);
        Loaded += (_, _) => cancel.Focus();
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
            // Keep the one affirmative action green in both themes.
            ThemeManager.SetSkipTheme(button, true);
        }
        return button;
    }

    /// <summary>Asks a yes/no question in the app's chrome. Returns true only for the confirm button.</summary>
    public static bool Ask(Window owner, string theme, string heading, string detail, string confirmText, bool destructive = false)
    {
        var dialog = new ConfirmWindow(theme, heading, detail, confirmText, destructive)
        {
            Owner = owner,
            // A fullscreen main window is topmost. An owned non-topmost dialog can end up behind
            // that window, which made End Game look as though the confirmation never opened.
            Topmost = owner.Topmost
        };
        dialog.Loaded += (_, _) =>
        {
            dialog.Activate();
            dialog.Focus();
        };
        dialog.ShowDialog();
        return dialog.Confirmed;
    }
}
