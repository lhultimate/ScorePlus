using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// Twelve buttons that cue a library item in one press. The assignments live in preferences beside
/// the quick layouts, so the main rail and every detached panel show the same media. Left-click an
/// assigned button to play it; right-click to reassign or clear it.
/// </summary>
public sealed class QuickMediaBar : Border
{
    const int Slots = 12;

    readonly MediaSession _session;
    readonly string _theme;
    readonly Button[] _buttons = new Button[Slots];

    readonly Color _panel = Color.FromRgb(0x09, 0x10, 0x18);
    readonly Color _surface = Color.FromRgb(0x10, 0x18, 0x24);
    readonly Color _text = Color.FromRgb(0xF4, 0xF7, 0xFB);
    readonly Color _muted = Color.FromRgb(0x93, 0xA2, 0xB5);
    readonly Color _edge = Color.FromRgb(0x2A, 0x3A, 0x4A);
    readonly Color _accentEdge;
    readonly Color _accentFill;

    public event Action<string>? Status;

    public QuickMediaBar(MediaSession session, string theme, bool compact = false)
    {
        _session = session;
        _theme = theme;
        _accentEdge = UiPalette.Accent(PreferencesStore.Load());
        _accentFill = UiPalette.Mix(_accentEdge, _panel, 0.66);

        Background = new SolidColorBrush(_panel);
        CornerRadius = new CornerRadius(9);
        Padding = new Thickness(8, compact ? 6 : 7, 8, compact ? 6 : 7);
        Margin = new Thickness(0, 0, 0, 8);
        ThemeManager.SetSkipTheme(this, true);

        var panel = new StackPanel();
        if (!compact)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "QUICK MEDIA",
                Foreground = new SolidColorBrush(_muted),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(1, 0, 0, 4)
            });
        }

        var row = new UniformGrid { Rows = 3, Columns = 4 };
        for (var index = 0; index < _buttons.Length; index++)
        {
            var slot = index;
            var button = new Button
            {
                Content = $"{index + 1}",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(4, 5, 4, 5),
                Margin = new Thickness(2),
                MinHeight = 28,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                ToolTip = "Left-click to cue · right-click to assign or clear"
            };
            ThemeManager.SetSkipTheme(button, true);
            button.Click += (_, _) => Activate(slot);
            button.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowMenu(slot, button); };
            _buttons[index] = button;
            row.Children.Add(button);
        }
        panel.Children.Add(row);
        Child = panel;

        _session.StateChanged += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        var bindings = PreferencesStore.Load().QuickMedia ?? [];
        for (var index = 0; index < _buttons.Length; index++)
        {
            var button = _buttons[index];
            var binding = index < bindings.Count ? bindings[index] : null;
            var entry = binding is { IsSet: true }
                ? _session.Library.FirstOrDefault(item => string.Equals(item.Path, binding.Path, StringComparison.OrdinalIgnoreCase))
                : null;
            button.BorderThickness = new Thickness(1);
            if (entry is null)
            {
                button.Content = binding is { IsSet: true } ? "missing" : $"{index + 1}";
                button.ToolTip = binding is { IsSet: true }
                    ? "That media is no longer in the library — right-click to reassign"
                    : "Unassigned — click to choose media";
                button.Foreground = new SolidColorBrush(binding is { IsSet: true } ? Color.FromRgb(0xFF, 0xD1, 0x66) : _muted);
                button.Background = new SolidColorBrush(_surface);
                button.BorderBrush = new SolidColorBrush(_edge);
                button.Opacity = 1;
                continue;
            }
            var label = QuickLabels.Clean(binding!.Label);
            if (label.Length == 0) label = entry.Label;
            var onAir = _session.IsOnAir(entry);
            button.Content = $"{(onAir ? "● " : "")}{Trim(label, 13)}";
            button.ToolTip = onAir
                ? $"{entry.KindLabel} · {entry.Label} · ON AIR — click to take it off air"
                : $"{entry.KindLabel} · {entry.Label} · tag {entry.Tag} — click to put it on air";
            button.Foreground = new SolidColorBrush(onAir ? _text : _muted);
            button.Background = new SolidColorBrush(onAir ? _accentFill : _surface);
            button.BorderBrush = new SolidColorBrush(onAir ? _accentEdge : _edge);
            button.Opacity = onAir ? 1 : 0.7;
        }
    }

    static string Trim(string text, int length) => text.Length <= length ? text : text[..Math.Max(1, length - 1)] + "…";

    void ShowMenu(int slot, Button button)
    {
        var bindings = PreferencesStore.Load().QuickMedia ?? [];
        var isSet = slot < bindings.Count && bindings[slot].IsSet;
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(_surface),
            BorderBrush = new SolidColorBrush(_edge),
            Foreground = new SolidColorBrush(_text),
            PlacementTarget = button,
            Placement = PlacementMode.Bottom
        };
        var text = new SolidColorBrush(_text);
        var assign = new MenuItem { Header = isSet ? "Reassign…" : "Assign media…", Foreground = text };
        assign.Click += (_, _) => Assign(slot);
        var clear = new MenuItem { Header = "Clear this button", IsEnabled = isSet, Foreground = text };
        clear.Click += (_, _) => Clear(slot);
        menu.Items.Add(assign);
        menu.Items.Add(clear);
        menu.IsOpen = true;
    }

    void Clear(int slot)
    {
        var preferences = PreferencesStore.Load();
        var bindings = preferences.QuickMedia ?? [];
        while (bindings.Count <= slot) bindings.Add(new QuickMediaBinding());
        bindings[slot] = new QuickMediaBinding();
        preferences.QuickMedia = bindings;
        PreferencesStore.Save(preferences);
        Status?.Invoke($"QUICK MEDIA {slot + 1} · cleared");
        Refresh();
    }

    void Activate(int slot)
    {
        var bindings = PreferencesStore.Load().QuickMedia ?? [];
        var binding = slot < bindings.Count ? bindings[slot] : null;
        if (binding is not { IsSet: true }) { Assign(slot); return; }
        var entry = _session.Library.FirstOrDefault(item =>
            string.Equals(item.Path, binding.Path, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            Status?.Invoke("QUICK MEDIA · that item is gone; right-click the button to reassign it");
            Refresh();
            return;
        }
        var onAir = _session.ToggleOnAir(entry);
        Status?.Invoke(onAir
            ? $"QUICK MEDIA {slot + 1} · ON AIR · {entry.KindLabel} · {entry.Label}"
            : $"QUICK MEDIA {slot + 1} · OFF AIR · {entry.KindLabel} · {entry.Label}");
        Refresh();
    }

    void Assign(int slot)
    {
        var owner = Window.GetWindow(this);
        if (owner is null) return;
        var preferences = PreferencesStore.Load();
        var bindings = preferences.QuickMedia ?? [];
        while (bindings.Count <= slot) bindings.Add(new QuickMediaBinding());
        var picked = QuickMediaPickerWindow.Ask(owner, _session, _theme, bindings[slot]);
        if (picked is null) return;
        bindings[slot] = picked;
        preferences.QuickMedia = bindings;
        PreferencesStore.Save(preferences);
        Status?.Invoke(picked.IsSet ? $"QUICK MEDIA {slot + 1} · {picked.Label}" : $"QUICK MEDIA {slot + 1} · cleared");
        Refresh();
    }
}

/// <summary>Chooses one library item for a quick-media button.</summary>
internal sealed class QuickMediaPickerWindow : Window
{
    readonly ListBox _items = new() { MinHeight = 210, Margin = new Thickness(0, 2, 0, 8) };
    readonly TextBox _label = new() { MinHeight = 28, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 2, 0, 8) };
    QuickMediaBinding? _result;

    sealed record MediaChoice(string Path, string Label, string Detail)
    {
        public override string ToString() => $"{Label}   ·   {Detail}";
    }

    QuickMediaPickerWindow(MediaSession session, string theme, QuickMediaBinding current)
    {
        Title = "Quick media";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        root.Children.Add(new TextBlock
        {
            Text = "Choose media",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "The button puts this item on air immediately, and the same button takes it off air again. Cameras and microphones can run together; files play; a speaker bus toggles that output on or off without losing its level.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var choices = session.Library
            .Select(entry => new MediaChoice(entry.Path, entry.Label, $"{entry.KindLabel} · tag {entry.Tag}"))
            .ToArray();
        _items.ItemsSource = choices;
        _items.SelectedItem = choices.FirstOrDefault(choice =>
            string.Equals(choice.Path, current.Path, StringComparison.OrdinalIgnoreCase));
        if (_items.SelectedItem is null && choices.Length > 0) _items.SelectedIndex = 0;
        _items.MouseDoubleClick += (_, _) => Accept();
        root.Children.Add(_items);

        root.Children.Add(new TextBlock { Text = "BUTTON LABEL (OPTIONAL)", FontSize = 10, FontWeight = FontWeights.Bold, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4) });
        _label.Text = QuickLabels.Clean(current.Label);
        _label.ToolTip = "Shown on the button; leave empty to use the media name";
        root.Children.Add(_label);

        if (choices.Length == 0)
            root.Children.Add(new TextBlock
            {
                Text = "The media library is empty. Import a clip or add a camera or microphone first.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = 36, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        var save = new Button { Content = "Assign", IsDefault = true, MinHeight = 36, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0), IsEnabled = choices.Length > 0 };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        save.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);

        Content = root;
        ThemeManager.Apply(this, theme);
        Loaded += (_, _) => _items.Focus();
    }

    void Accept()
    {
        if (_items.SelectedItem is not MediaChoice choice) return;
        _result = new QuickMediaBinding
        {
            Path = choice.Path,
            Label = string.IsNullOrWhiteSpace(_label.Text) ? choice.Label : _label.Text.Trim()
        };
        DialogResult = true;
        Close();
    }

    public static QuickMediaBinding? Ask(Window owner, MediaSession session, string theme, QuickMediaBinding current)
    {
        var dialog = new QuickMediaPickerWindow(session, theme, current) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}
