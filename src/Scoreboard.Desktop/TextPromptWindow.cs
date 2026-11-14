using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// A one-line text prompt in the operator's theme — used for names that belong to the library
/// rather than to the game (a roster, for example). Returns null when cancelled.
/// </summary>
public sealed class TextPromptWindow : Window
{
    readonly TextBox _input = new();
    readonly TextBlock _error = new();
    string? _result;

    TextPromptWindow(string title, string label, string initial)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(23, 28, 37));
        Foreground = Brushes.White;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(4, 0, 4, 6) });
        _input.Text = initial;
        panel.Children.Add(_input);
        _error.Foreground = new SolidColorBrush(Color.FromRgb(255, 114, 114));
        _error.Margin = new Thickness(4, 4, 0, 0);
        _error.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 84, Padding = new Thickness(10, 6, 10, 6) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(6, 0, 0, 0) };
        ok.Click += (_, _) =>
        {
            var text = _input.Text.Trim();
            if (text.Length == 0) { _error.Text = "Type a name first."; return; }
            _result = text;
            DialogResult = true;
            Close();
        };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
        ThemeManager.Apply(this, PreferencesStore.Load().Theme);
        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }

    /// <summary>Asks for a name. Returns the trimmed text, or null when the operator cancelled.</summary>
    public static string? Ask(Window? owner, string title, string label, string initial)
    {
        var dialog = new TextPromptWindow(title, label, initial);
        if (owner is not null && owner.IsLoaded) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}
