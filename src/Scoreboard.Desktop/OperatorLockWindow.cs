using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// The operator lock's password prompt. It creates a password the first time, confirms it before
/// locking, and checks it before releasing the desk. The password is never displayed or stored in
/// clear text; only its hash reaches preferences.
/// </summary>
public sealed class OperatorLockWindow : Window
{
    readonly PasswordBox _password = new();
    readonly PasswordBox? _confirm;
    readonly string? _expectedHash;
    readonly TextBlock _error = new();
    string? _result;

    OperatorLockWindow(string theme, string heading, string detail, bool creating, string? expectedHash)
    {
        _expectedHash = expectedHash;
        Title = "ScorePlus — Operator Lock";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");

        var panel = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 17, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = detail,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
            FontSize = 11,
            Margin = new Thickness(0, 7, 0, 12)
        });
        panel.Children.Add(new TextBlock { Text = "Password", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)), Margin = new Thickness(2, 0, 0, 3) });
        _password.MinHeight = 36;
        _password.Padding = new Thickness(9, 5, 9, 5);
        _password.Background = new SolidColorBrush(Color.FromRgb(5, 5, 5));
        _password.Foreground = Brushes.White;
        _password.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38));
        panel.Children.Add(_password);
        if (creating)
        {
            panel.Children.Add(new TextBlock { Text = "Repeat password", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)), Margin = new Thickness(2, 9, 0, 3) });
            _confirm = new PasswordBox
            {
                MinHeight = 36,
                Padding = new Thickness(9, 5, 9, 5),
                Background = new SolidColorBrush(Color.FromRgb(5, 5, 5)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38))
            };
            panel.Children.Add(_confirm);
        }
        _error.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x72, 0x72));
        _error.FontSize = 11;
        _error.TextWrapping = TextWrapping.Wrap;
        _error.Margin = new Thickness(2, 8, 0, 0);
        panel.Children.Add(_error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = creating ? "Set password" : "Lock", IsDefault = true, MinWidth = 96, Padding = new Thickness(12, 6, 12, 6) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6, 0, 0, 0) };
        ok.Click += (_, _) => Submit(creating);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
        ThemeManager.Apply(this, theme);
        Loaded += (_, _) => _password.Focus();
    }

    void Submit(bool creating)
    {
        var first = _password.Password;
        if (first.Length == 0)
        {
            _error.Text = "Enter a password first.";
            _password.Focus();
            return;
        }
        if (creating)
        {
            if (_confirm is null || !string.Equals(first, _confirm.Password, StringComparison.Ordinal))
            {
                _error.Text = "The two passwords do not match.";
                _confirm?.Focus();
                return;
            }
            _result = first;
        }
        else if (!OperatorLock.Matches(first, _expectedHash))
        {
            _error.Text = "That password does not match the operator lock.";
            _password.SelectAll();
            _password.Focus();
            return;
        }
        DialogResult = true;
        Close();
    }

    /// <summary>Asks for a new lock password. Null means the operator cancelled.</summary>
    public static string? Create(Window? owner, string theme)
    {
        var dialog = new OperatorLockWindow(theme, "SET THE OPERATOR LOCK", "This password locks the console, media, rosters, detached control panels and the phone remote. Keep it separate from the remote password.", creating: true, expectedHash: null);
        if (owner is { IsLoaded: true }) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog._result : null;
    }

    /// <summary>Confirms the existing lock password. True means the desk may lock (or unlock).</summary>
    public static bool Confirm(Window? owner, string theme, string hash, string heading, string detail)
    {
        var dialog = new OperatorLockWindow(theme, heading, detail, creating: false, expectedHash: hash);
        if (owner is { IsLoaded: true }) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }
}
