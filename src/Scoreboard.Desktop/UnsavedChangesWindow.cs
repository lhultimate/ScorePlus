using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Scoreboard.Desktop;

public enum UnsavedChoice
{
    Cancel,
    Save,
    Discard
}

/// <summary>
/// The exit prompt, in the app's own chrome rather than a system message box. It offers the three
/// outcomes that matter: put the game in a file, leave without it, or stay and keep working.
/// </summary>
public sealed class UnsavedChangesWindow : Window
{
    public UnsavedChangesWindow(string theme, string detail)
    {
        Title = "Unsaved game — ScorePlus";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");

        var root = new Grid { Margin = new Thickness(26, 24, 26, 22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var accent = new Border
        {
            Width = 4,
            Height = 22,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0x66)),
            Margin = new Thickness(0, 0, 10, 0)
        };
        ThemeManager.SetSkipTheme(accent, true);
        heading.Children.Add(accent);
        heading.Children.Add(new TextBlock
        {
            Text = "THIS GAME ISN'T IN A FILE YET",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
        });
        root.Children.Add(heading);

        var body = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        Grid.SetRow(body, 1);
        body.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });
        root.Children.Add(body);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right
        };
        Grid.SetRow(buttons, 2);

        var cancel = MakeButton("Keep editing", primary: false);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => { Choice = UnsavedChoice.Cancel; Close(); };
        buttons.Children.Add(cancel);

        var discard = MakeButton("Exit without saving", primary: false);
        discard.Click += (_, _) => { Choice = UnsavedChoice.Discard; Close(); };
        buttons.Children.Add(discard);

        var save = MakeButton("Save & exit", primary: true);
        save.IsDefault = true;
        save.Click += (_, _) => { Choice = UnsavedChoice.Save; Close(); };
        buttons.Children.Add(save);

        root.Children.Add(buttons);
        Content = root;
        ThemeManager.Apply(this, theme);
    }

    public UnsavedChoice Choice { get; private set; } = UnsavedChoice.Cancel;

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
            Cursor = Avalonia.Input.Cursors.Hand
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

    public static UnsavedChoice Ask(Window owner, string theme, string detail)
    {
        var dialog = new UnsavedChangesWindow(theme, detail) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Choice;
    }
}
