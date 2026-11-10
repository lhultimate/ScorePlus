using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// Asks which display an audience window should show. Only used when a game actually has more than
/// one display; with a single display the answer is obvious and no window is shown.
/// </summary>
internal sealed class DisplayPickerWindow : Window
{
    readonly ListBox _list = new();
    readonly ScorebugCatalog _catalog;
    string? _result;

    DisplayPickerWindow(ScorebugCatalog catalog, string theme, string? preferredDisplayId,
        string title = "Audience output",
        string heading = "Which display is this window for?",
        string detail = "Each display has its own audience window and its own layout. Opening one again adds another window rather than replacing the first.",
        string confirm = "Open output",
        string? excludeDisplayId = null)
    {
        _catalog = catalog;
        Title = title;
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        root.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var targets = catalog.Displays
            .Where(display => !string.Equals(display.Id, excludeDisplayId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _list.ItemsSource = targets.Select(display => new DisplayChoice(display, catalog.Displays.IndexOf(display) + 1)).ToArray();
        _list.DisplayMemberPath = "Label";
        _list.FontSize = 13;
        _list.MinHeight = 96;
        _list.Margin = new Thickness(0, 0, 0, 12);
        var preferred = targets.ToList().FindIndex(display => string.Equals(display.Id, preferredDisplayId, StringComparison.OrdinalIgnoreCase));
        _list.SelectedIndex = preferred >= 0 ? preferred : 0;
        _list.MouseDoubleClick += (_, _) => Accept();
        root.Children.Add(_list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = 36, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        var open = new Button { Content = confirm, IsDefault = true, MinHeight = 36, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        open.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(open);
        root.Children.Add(buttons);
        Content = root;
        ThemeManager.Apply(this, theme);
        Loaded += (_, _) => _list.Focus();
    }

    void Accept()
    {
        if (_list.SelectedItem is not DisplayChoice choice) return;
        _result = choice.Display.Id;
        DialogResult = true;
        Close();
    }

    /// <summary>The chosen display id, or null when the operator cancelled.</summary>
    public static string? Ask(Window owner, ScorebugCatalog catalog, string theme, string? preferredDisplayId,
        string title = "Audience output",
        string heading = "Which display is this window for?",
        string detail = "Each display has its own audience window and its own layout. Opening one again adds another window rather than replacing the first.",
        string confirm = "Open output")
    {
        var window = new DisplayPickerWindow(catalog, theme, preferredDisplayId, title, heading, detail, confirm) { Owner = owner };
        return window.ShowDialog() == true ? window._result : null;
    }

    /// <summary>
    /// Picks the display a studio layout should be moved or copied to. The display being edited is
    /// left out, because that is where the layout already is.
    /// </summary>
    public static string? AskLayoutTarget(Window owner, ScorebugCatalog catalog, string theme, bool copy)
    {
        var window = new DisplayPickerWindow(catalog, theme, null,
            title: copy ? "Copy layout" : "Move layout",
            heading: copy ? "Copy this layout to which display?" : "Move this layout to which display?",
            detail: copy
                ? "The copy gets its own identity and element ids, so editing it on the other display leaves this one alone. Every display keeps its own layouts."
                : "The layout, artwork and all, joins the other display and leaves this one. A display always keeps at least one layout.",
            confirm: copy ? "Copy layout" : "Move layout",
            excludeDisplayId: catalog.ActiveDisplay.Id) { Owner = owner };
        return window.ShowDialog() == true ? window._result : null;
    }

    sealed class DisplayChoice
    {
        public DisplayChoice(ScorebugDisplay display, int number)
        {
            Display = display;
            var liveNames = new List<string>();
            if (display.Layouts.Count > 0)
                liveNames.Add(display.Layouts[Math.Clamp(display.PublishedIndex, 0, display.Layouts.Count - 1)].Name);
            foreach (var id in display.PinnedLayoutIds ?? [])
            {
                var pinned = display.Layouts.FirstOrDefault(layout => string.Equals(layout.Id, id, StringComparison.OrdinalIgnoreCase));
                if (pinned is not null && !liveNames.Contains(pinned.Name)) liveNames.Add(pinned.Name);
            }
            var live = liveNames.Count == 0 ? "no layouts" : string.Join(" + ", liveNames);
            Label = $"{number} · {display.Name}   ·   on air: {live}   ·   {display.Layouts.Count} layout{(display.Layouts.Count == 1 ? "" : "s")}";
        }

        public ScorebugDisplay Display { get; }
        public string Label { get; }
    }
}
