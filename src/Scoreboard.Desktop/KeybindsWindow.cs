using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Scoreboard.Desktop;

public sealed class KeybindsWindow : Window
{
    readonly KeybindSettings _settings;
    readonly ListBox _actions = new() { Margin = new Thickness(8) };
    readonly TextBlock _current = new() { Text = "No key assigned", Margin = new Thickness(8) };
    readonly TextBlock _status = new() { Text = "", Margin = new Thickness(8), Foreground = new SolidColorBrush(Color.FromRgb(56, 215, 163)) };
    bool _capturing;

    public KeybindSettings Result => _settings;

    /// <param name="sportId">Only this sport's actions are listed, alongside the shared console.</param>
    /// <param name="sportName">Printed in the heading so the list is clearly about that game.</param>
    public KeybindsWindow(KeybindSettings settings, string? sportId = null, string? sportName = null)
    {
        _settings = settings.Clone();
        Title = "ScorePlus — Keybinds";
        Width = 620; Height = 620; MinWidth = 520; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(14, 18, 24));
        Foreground = Brushes.White;

        var root = new DockPanel { Margin = new Thickness(12) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var assign = new Button { Content = "Press a key to assign", Padding = new Thickness(12, 10, 12, 10) };
        var clear = new Button { Content = "Clear" };
        var defaults = new Button { Content = "Restore defaults" };
        var done = new Button { Content = "Save & close", IsDefault = true };
        buttons.Children.Add(assign); buttons.Children.Add(clear); buttons.Children.Add(defaults); buttons.Children.Add(done);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var info = new StackPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(info, Dock.Bottom);
        info.Children.Add(new TextBlock { Text = "SELECTED BUTTON", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 2) });
        info.Children.Add(_current);
        info.Children.Add(_status);
        root.Children.Add(info);

        var header = new TextBlock
        {
            // The list is the current sport's, so the heading says which game it belongs to.
            Text = $"Choose a {sportName ?? "sport"} button, then press the key combination you want to use. " +
                   "Shortcuts work while the Control Panel is focused. Shared buttons — score, clocks and output — are always listed.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 8, 8)
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _actions.ItemsSource = ControlActions.ForSport(sportId).Select(a => new ActionItem(a)).ToArray();
        _actions.SelectionChanged += (_, _) => ShowSelected();
        root.Children.Add(_actions);
        Content = root;

        assign.Click += (_, _) => BeginCapture();
        clear.Click += (_, _) =>
        {
            if (Selected is { } item) { _settings.Bindings.Remove(item.Definition.Id); _status.Text = $"Cleared {item.Definition.Label}"; ShowSelected(); }
        };
        defaults.Click += (_, _) =>
        {
            _settings.Bindings = KeybindStore.Defaults().Bindings;
            _status.Text = "Restored default shortcuts";
            ShowSelected();
        };
        done.Click += (_, _) => { DialogResult = true; Close(); };
        Loaded += (_, _) => _actions.SelectedIndex = 0;
    }

    ActionItem? Selected => _actions.SelectedItem as ActionItem;

    void ShowSelected()
    {
        if (Selected is not { } item) { _current.Text = "No button selected"; return; }
        _current.Text = _settings.Bindings.TryGetValue(item.Definition.Id, out var gesture)
            ? $"{item.Definition.Label}  →  {gesture}"
            : $"{item.Definition.Label}  →  no key assigned";
    }

    void BeginCapture()
    {
        if (Selected is null) return;
        _capturing = true;
        _status.Text = $"Listening… press a key for {Selected!.Definition.Label} (Esc cancels)";
        Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturing) { base.OnPreviewKeyDown(e); return; }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        if (key == Key.Escape) { _capturing = false; _status.Text = "Cancelled"; return; }
        if (Selected is not { } item) return;
        var modifiers = Keyboard.Modifiers;
        var gesture = KeybindStore.ToText(key, modifiers);
        foreach (var existing in _settings.Bindings.Where(b => b.Value == gesture && b.Key != item.Definition.Id).Select(b => b.Key).ToArray())
            _settings.Bindings.Remove(existing);
        _settings.Bindings[item.Definition.Id] = gesture;
        _capturing = false;
        _status.Text = $"Assigned {gesture} to {item.Definition.Label}";
        ShowSelected();
    }

    sealed class ActionItem(ControlActionDefinition definition)
    {
        public ControlActionDefinition Definition { get; } = definition;
        public override string ToString() => $"{Definition.Group}  ·  {Definition.Label}";
    }
}
