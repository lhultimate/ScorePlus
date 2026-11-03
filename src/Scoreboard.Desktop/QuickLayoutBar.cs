using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// Eight buttons that put a chosen layout on any display in one press. The assignment lives in
/// preferences, so each window — the main rail and every detached panel — offers the same eight.
/// By default a press only stages the layout; Preferences can make the same press update output.
/// Right-click a button for a menu that reassigns it or clears it.
/// </summary>
public sealed class QuickLayoutBar : Border
{
    const int Slots = 8;

    readonly ScorebugCatalog _catalog;
    readonly GameEngine _engine;
    readonly string _theme;
    readonly Button[] _buttons = new Button[Slots];
    readonly TextBlock? _caption;

    // The bar opts out of the window's theme pass so an accent-filled button keeps its fill, which
    // means it has to resolve the palette itself — including the light theme, which it used to ignore.
    readonly Color _panel;
    readonly Color _surface;
    readonly Color _text;
    readonly Color _muted;
    readonly Color _edge;
    readonly Color _accentEdge;
    readonly Color _accentFill;

    /// <summary>Raised with a line for the status bar whenever a quick button is used.</summary>
    public event Action<string>? Status;

    public QuickLayoutBar(ScorebugCatalog catalog, GameEngine engine, string theme, bool compact = false)
    {
        _catalog = catalog;
        _engine = engine;
        _theme = theme;
        // The quick bar is ScorePlus navy in both themes: it sits beside the sound meter and is part
        // of the console, so following a light desktop made it read as a card from another program.
        var navy = Color.FromRgb(0x09, 0x10, 0x18);
        _panel = navy;
        _surface = Color.FromRgb(0x10, 0x18, 0x24);
        _text = Color.FromRgb(0xF4, 0xF7, 0xFB);
        _muted = Color.FromRgb(0x93, 0xA2, 0xB5);
        _edge = Color.FromRgb(0x2A, 0x3A, 0x4A);
        _accentEdge = UiPalette.Accent(PreferencesStore.Load());
        _accentFill = UiPalette.Mix(_accentEdge, navy, 0.66);

        Background = new SolidColorBrush(_panel);
        CornerRadius = new CornerRadius(9);
        Padding = new Thickness(8, compact ? 6 : 7, 8, compact ? 6 : 7);
        Margin = new Thickness(0, 0, 0, 8);
        ThemeManager.SetSkipTheme(this, true);

        var panel = new StackPanel();
        if (!compact)
        {
            _caption = new TextBlock
            {
                Text = "QUICK LAYOUTS",
                Foreground = new SolidColorBrush(_muted),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(1, 0, 0, 4)
            };
            panel.Children.Add(_caption);
        }
        // Two rows of four: eight buttons stay readable in the rail and in a detached panel.
        var row = new UniformGrid { Rows = 2, Columns = 4 };
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
                ToolTip = "Left-click to use · right-click to reassign or clear"
            };
            ThemeManager.SetSkipTheme(button, true);
            button.Click += (_, _) => Activate(slot);
            button.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowMenu(slot, button); };
            _buttons[index] = button;
            row.Children.Add(button);
        }
        panel.Children.Add(row);
        Child = panel;

        _catalog.Changed += Refresh;
        Refresh();
    }

    /// <summary>Re-reads preferences and the catalog; call after Preferences closes or a display moves.</summary>
    public void Refresh()
    {
        var preferences = PreferencesStore.Load();
        var bindings = preferences.QuickLayouts ?? [];
        for (var index = 0; index < _buttons.Length; index++)
        {
            var button = _buttons[index];
            button.BorderThickness = new Thickness(1);
            var binding = index < bindings.Count ? bindings[index] : null;
            var layout = binding is { IsSet: true } ? _catalog.FindLayout(binding.DisplayId, binding.LayoutId) : null;
            if (layout is null)
            {
                button.Content = $"{index + 1}";
                button.ToolTip = "Unassigned — click to choose a layout on any display";
                button.Foreground = new SolidColorBrush(_muted);
                button.Background = new SolidColorBrush(_surface);
                button.BorderBrush = new SolidColorBrush(_edge);
                continue;
            }
            var display = _catalog.FindDisplay(binding!.DisplayId);
            var label = string.IsNullOrWhiteSpace(binding.Label) ? layout.Name : binding.Label;
            button.Content = $"{index + 1} · {Trim(label, 14)}";
            button.ToolTip = $"{layout.Name} on {display?.Name ?? "display"}" +
                (preferences.QuickLayoutsPublish ? " — press updates output" : " — press stages it; Update Output sends it") +
                " · right-click to reassign or clear";
            button.Foreground = new SolidColorBrush(_text);
            button.Background = new SolidColorBrush(_accentFill);
            button.BorderBrush = new SolidColorBrush(_accentEdge);
        }
    }

    static string Trim(string text, int length) => text.Length <= length ? text : text[..Math.Max(1, length - 1)] + "…";

    /// <summary>Right-click menu: reassign the slot, or empty it so the button does nothing.</summary>
    void ShowMenu(int slot, Button button)
    {
        var bindings = PreferencesStore.Load().QuickLayouts ?? [];
        var isSet = slot < bindings.Count && bindings[slot].IsSet;
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(_surface),
            BorderBrush = new SolidColorBrush(_edge),
            Foreground = new SolidColorBrush(_text),
            PlacementTarget = button,
            Placement = PlacementMode.Bottom
        };
        var assign = new MenuItem { Header = isSet ? "Reassign…" : "Assign a layout…" };
        assign.Click += (_, _) => Assign(slot);
        var clear = new MenuItem { Header = "Clear this button", IsEnabled = isSet };
        clear.Click += (_, _) => Clear(slot);
        menu.Items.Add(assign);
        menu.Items.Add(clear);
        menu.IsOpen = true;
    }

    void Clear(int slot)
    {
        var preferences = PreferencesStore.Load();
        var bindings = preferences.QuickLayouts ?? [];
        while (bindings.Count <= slot) bindings.Add(new QuickLayoutBinding());
        bindings[slot] = new QuickLayoutBinding();
        preferences.QuickLayouts = bindings;
        PreferencesStore.Save(preferences);
        Status?.Invoke($"QUICK {slot + 1} · cleared — the button does nothing until it is reassigned");
        Refresh();
    }

    void Activate(int slot)
    {
        var preferences = PreferencesStore.Load();
        var bindings = preferences.QuickLayouts ?? [];
        var binding = slot < bindings.Count ? bindings[slot] : null;
        if (binding is not { IsSet: true }) { Assign(slot); return; }
        var layout = _catalog.FindLayout(binding.DisplayId, binding.LayoutId);
        if (layout is null)
        {
            Status?.Invoke("QUICK LAYOUT · that layout is gone; right-click the button to reassign it");
            Refresh();
            return;
        }
        var publish = preferences.QuickLayoutsPublish;
        if (!_catalog.ActivateLayout(binding.DisplayId, binding.LayoutId, publish)) return;
        var display = _catalog.FindDisplay(binding.DisplayId);
        Status?.Invoke(publish
            ? $"QUICK {slot + 1} · {layout.Name} on {display?.Name} is on air"
            : $"QUICK {slot + 1} · {layout.Name} staged on {display?.Name} · Update Output sends it");
        Refresh();
    }

    void Assign(int slot)
    {
        var owner = Window.GetWindow(this);
        if (owner is null) return;
        var preferences = PreferencesStore.Load();
        var bindings = preferences.QuickLayouts ?? [];
        while (bindings.Count <= slot) bindings.Add(new QuickLayoutBinding());
        var current = bindings[slot];
        var picked = QuickLayoutPickerWindow.Ask(owner, _catalog, _theme, current);
        if (picked is null) return;
        bindings[slot] = picked;
        preferences.QuickLayouts = bindings;
        PreferencesStore.Save(preferences);
        Status?.Invoke(picked.IsSet ? $"QUICK {slot + 1} · {picked.Label}" : $"QUICK {slot + 1} · cleared");
        Refresh();
    }
}

/// <summary>
/// Chooses what a quick button points at: any display and any layout on it. It offers every display
/// in the show, not just the one the studio is editing, which is the point of a quick button.
/// </summary>
internal sealed class QuickLayoutPickerWindow : Window
{
    readonly ComboBox _displays = new() { MinHeight = 30, Margin = new Thickness(0, 2, 0, 8) };
    readonly ListBox _layouts = new() { MinHeight = 180, Margin = new Thickness(0, 2, 0, 8) };
    readonly TextBox _label = new() { MinHeight = 28, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 2, 0, 8) };
    readonly ScorebugCatalog _catalog;
    readonly string _theme;
    QuickLayoutBinding? _result;

    /// <summary>A display offered by name, so no combo ever falls back to a type name.</summary>
    sealed record DisplayChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>A layout offered by name, for the same reason.</summary>
    sealed record LayoutChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    QuickLayoutPickerWindow(ScorebugCatalog catalog, string theme, QuickLayoutBinding? current)
    {
        _catalog = catalog;
        _theme = theme;
        Title = "Quick layout";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(20, 16, 20, 14) };
        root.Children.Add(new TextBlock { Text = "Assign a quick layout", FontSize = 17, FontWeight = FontWeights.Bold });
        root.Children.Add(new TextBlock
        {
            Text = "Pick any display and any layout on it. The eight buttons above the sound meter — and in every detached panel — will use this.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            Margin = new Thickness(0, 0, 0, 10)
        });
        root.Children.Add(new TextBlock { Text = "DISPLAY", FontSize = 10, FontWeight = FontWeights.Bold, Opacity = 0.7 });
        _displays.ItemsSource = catalog.Displays.Select(display => new DisplayChoice(display.Id, display.Name)).ToArray();
        _displays.SelectionChanged += (_, _) => FillLayouts();
        root.Children.Add(_displays);
        root.Children.Add(new TextBlock { Text = "LAYOUT", FontSize = 10, FontWeight = FontWeights.Bold, Opacity = 0.7 });
        root.Children.Add(_layouts);
        root.Children.Add(new TextBlock { Text = "BUTTON LABEL (optional)", FontSize = 10, FontWeight = FontWeights.Bold, Opacity = 0.7 });
        _label.Text = current?.Label ?? "";
        root.Children.Add(_label);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var clear = new Button { Content = "Clear button", MinHeight = 34, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(4, 0, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = 34, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(4, 0, 0, 0) };
        var use = new Button { Content = "Use this layout", IsDefault = true, MinHeight = 34, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(4, 0, 0, 0) };
        clear.Click += (_, _) => { _result = new QuickLayoutBinding(); DialogResult = true; Close(); };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        use.Click += (_, _) => Accept();
        buttons.Children.Add(clear);
        buttons.Children.Add(cancel);
        buttons.Children.Add(use);
        root.Children.Add(buttons);

        Content = root;
        ThemeManager.Apply(this, theme);
        var displayIndex = current is { IsSet: true }
            ? catalog.Displays.FindIndex(display => string.Equals(display.Id, current.DisplayId, StringComparison.OrdinalIgnoreCase))
            : catalog.ActiveDisplayIndex;
        _displays.SelectedIndex = Math.Clamp(displayIndex, 0, Math.Max(0, catalog.Displays.Count - 1));
        Loaded += (_, _) =>
        {
            if (current is not { IsSet: true }) { _label.Focus(); return; }
            var displayChoice = _displays.Items.OfType<DisplayChoice>()
                .FirstOrDefault(choice => string.Equals(choice.Id, current.DisplayId, StringComparison.OrdinalIgnoreCase));
            if (displayChoice is not null) _displays.SelectedItem = displayChoice;
            var layoutChoice = _layouts.Items.OfType<LayoutChoice>()
                .FirstOrDefault(choice => string.Equals(choice.Id, current.LayoutId, StringComparison.OrdinalIgnoreCase));
            if (layoutChoice is not null) _layouts.SelectedItem = layoutChoice;
            _label.Focus();
        };
    }

    void FillLayouts()
    {
        var id = (_displays.SelectedItem as DisplayChoice)?.Id;
        var display = _catalog.Displays.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        _layouts.ItemsSource = display?.Layouts.Select(layout => new LayoutChoice(layout.Id, layout.Name)).ToArray();
        if (_layouts.Items.Count > 0 && _layouts.SelectedIndex < 0) _layouts.SelectedIndex = 0;
    }

    void Accept()
    {
        if (_displays.SelectedItem is not DisplayChoice display || _layouts.SelectedItem is not LayoutChoice layout) return;
        _result = new QuickLayoutBinding
        {
            DisplayId = display.Id,
            LayoutId = layout.Id,
            Label = _label.Text.Trim()
        };
        DialogResult = true;
        Close();
    }

    public static QuickLayoutBinding? Ask(Window owner, ScorebugCatalog catalog, string theme, QuickLayoutBinding? current)
    {
        var dialog = new QuickLayoutPickerWindow(catalog, theme, current) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}
