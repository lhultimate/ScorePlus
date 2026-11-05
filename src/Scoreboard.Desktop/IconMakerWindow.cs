using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// The icon maker: a glyph, its colour and its plate, saved to the icon library so any element in
/// any layout can use it. It is deliberately small — an icon is a badge, not a drawing program.
/// </summary>
public sealed class IconMakerWindow : Window
{
    readonly string _theme;
    readonly ListBox _list = new();
    readonly TextBox _name = new() { MinWidth = 200 };
    readonly TextBox _glyph = new() { MinWidth = 70, MaxWidth = 120 };
    readonly TextBox _color = new() { MinWidth = 110 };
    readonly TextBox _plate = new() { MinWidth = 110 };
    readonly CheckBox _round = new() { Content = "Round plate" };
    readonly Border _preview = new()
    {
        Width = 108,
        Height = 108,
        CornerRadius = new CornerRadius(54),
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 4, 0, 0)
    };
    readonly TextBlock _previewGlyph = new()
    {
        FontSize = 54,
        FontWeight = FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
    readonly TextBlock _status = new() { FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _delete;
    CustomIcon _editing = new();

    public IconMakerWindow(string theme)
    {
        _theme = theme;
        Title = "ScorePlus — Icon Maker";
        Width = 720;
        Height = 560;
        MinWidth = 620;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        ScreenFit.Track(this);

        _preview.Child = _previewGlyph;
        ThemeManager.SetSkipTheme(_preview, true);

        var root = new DockPanel { Margin = new Thickness(20, 18, 20, 16) };

        // ---- header ----
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var accent = new Border
        {
            Width = 4,
            Height = 24,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0xD7, 0xA3)),
            Margin = new Thickness(0, 0, 10, 0)
        };
        ThemeManager.SetSkipTheme(accent, true);
        titleRow.Children.Add(accent);
        titleRow.Children.Add(new TextBlock
        {
            Text = "ICON MAKER",
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(titleRow);
        header.Children.Add(new TextBlock
        {
            Text = "A made icon is a glyph on its own plate. Save it here, then choose it in the studio's Icon list as “Custom” and pick it under Made icon.",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // ---- footer ----
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var close = new Button
        {
            Content = "Close",
            IsCancel = true,
            MinHeight = 36,
            MinWidth = 96,
            Padding = new Thickness(14, 7, 14, 7)
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);

        var save = new Button
        {
            Content = "Save icon",
            IsDefault = true,
            MinHeight = 36,
            MinWidth = 120,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0)
        };
        save.Click += (_, _) => SaveIcon();
        DockPanel.SetDock(save, Dock.Right);
        footer.Children.Add(save);

        _delete = new Button
        {
            Content = "Delete…",
            MinHeight = 36,
            MinWidth = 96,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0)
        };
        _delete.Click += (_, _) => DeleteIcon();
        DockPanel.SetDock(_delete, Dock.Right);
        footer.Children.Add(_delete);

        footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        // ---- library and editor side by side ----
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var library = new DockPanel();
        library.Children.Add(new TextBlock
        {
            Text = "ICON LIBRARY",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Opacity = 0.8,
            Margin = new Thickness(0, 0, 0, 6)
        });
        DockPanel.SetDock(library.Children[0], Dock.Top);
        var newButton = new Button { Content = "New icon", MinHeight = 32, Margin = new Thickness(0, 6, 0, 0) };
        newButton.Click += (_, _) => StartNew();
        DockPanel.SetDock(newButton, Dock.Bottom);
        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is CustomIcon icon) LoadIcon(icon); };
        library.Children.Add(newButton);
        library.Children.Add(_list);
        Grid.SetColumn(library, 0);
        columns.Children.Add(library);

        var editor = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        editor.Children.Add(FieldLabel("NAME"));
        editor.Children.Add(_name);
        var glyphRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        glyphRow.Children.Add(Field("GLYPH", _glyph));
        glyphRow.Children.Add(Field("COLOUR", _color));
        glyphRow.Children.Add(Field("PLATE", _plate));
        editor.Children.Add(glyphRow);
        editor.Children.Add(new TextBlock
        {
            Text = "The glyph is one to three characters — ★ ● ▲ ⚑ ⬤ ✚ or a letter. Colour and plate take #RRGGBB or #AARRGGBB; a plate starting #00 is transparent.",
            FontSize = 10,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        _round.Margin = new Thickness(0, 10, 0, 0);
        _round.Checked += (_, _) => UpdatePreview();
        _round.Unchecked += (_, _) => UpdatePreview();
        editor.Children.Add(_round);
        editor.Children.Add(new TextBlock
        {
            Text = "PREVIEW",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Opacity = 0.8,
            Margin = new Thickness(0, 14, 0, 0)
        });
        editor.Children.Add(_preview);
        Grid.SetColumn(editor, 2);
        columns.Children.Add(editor);
        root.Children.Add(columns);

        foreach (var box in new[] { _name, _glyph, _color, _plate })
            box.TextChanged += (_, _) => UpdatePreview();

        Content = root;
        ThemeManager.Apply(this, _theme);
        StartNew();
        RefreshLibrary();
    }

    static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        Opacity = 0.8,
        Margin = new Thickness(0, 0, 0, 4)
    };

    static StackPanel Field(string label, TextBox box)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        panel.Children.Add(FieldLabel(label));
        panel.Children.Add(box);
        return panel;
    }

    void RefreshLibrary()
    {
        var keep = _editing.Id;
        var icons = CustomIconStore.All();
        _list.ItemsSource = null;
        _list.ItemsSource = icons;
        _list.SelectedItem = icons.FirstOrDefault(icon => string.Equals(icon.Id, keep, StringComparison.OrdinalIgnoreCase));
        _status.Text = icons.Count == 0 ? "No icons yet: fill in a glyph and press Save icon." : $"{icons.Count} icon{(icons.Count == 1 ? "" : "s")} saved.";
    }

    void StartNew()
    {
        _editing = new CustomIcon();
        _list.SelectedItem = null;
        Fill(_editing);
        _delete.IsEnabled = false;
        _status.Text = "A new icon. Press Save icon to add it to the library.";
    }

    void LoadIcon(CustomIcon icon)
    {
        _editing = icon.Copy();
        Fill(_editing);
        _delete.IsEnabled = true;
        _status.Text = $"Editing “{icon.Name}”.";
    }

    void Fill(CustomIcon icon)
    {
        _name.Text = icon.Name;
        _glyph.Text = icon.Glyph;
        _color.Text = icon.Color;
        _plate.Text = icon.Plate;
        _round.IsChecked = icon.Round;
        UpdatePreview();
    }

    CustomIcon Read()
    {
        var icon = _editing.Copy();
        icon.Name = _name.Text;
        icon.Glyph = _glyph.Text;
        icon.Color = _color.Text;
        icon.Plate = _plate.Text;
        icon.Round = _round.IsChecked == true;
        icon.Normalize();
        return icon;
    }

    void UpdatePreview()
    {
        var icon = Read();
        _previewGlyph.Text = icon.Glyph;
        _previewGlyph.Foreground = ScorebugRender.ParseBrush(icon.Color);
        _preview.Background = ScorebugRender.ParseBrush(icon.Plate);
        _preview.CornerRadius = new CornerRadius(icon.Round ? 54 : 16);
    }

    void SaveIcon()
    {
        var icon = Read();
        _editing = CustomIconStore.Save(icon);
        _glyph.Text = icon.Glyph;
        _color.Text = icon.Color;
        _plate.Text = icon.Plate;
        RefreshLibrary();
        _status.Text = $"Saved “{icon.Name}”. The studio's Made icon list now offers it.";
    }

    void DeleteIcon()
    {
        if (CustomIconStore.Find(_editing.Id) is not { } icon) return;
        if (!ConfirmWindow.Ask(this, _theme, $"Delete the icon “{icon.Name}”?",
                "Elements that use it will draw nothing until another icon is chosen.", "Delete", destructive: true)) return;
        CustomIconStore.Remove(icon.Id);
        StartNew();
        RefreshLibrary();
        _status.Text = $"Deleted “{icon.Name}”.";
    }
}
