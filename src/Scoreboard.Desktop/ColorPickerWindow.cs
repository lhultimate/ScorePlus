using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Scoreboard.Desktop;

/// <summary>Custom color picker: type a hex value, drag RGB/alpha sliders, or tap a palette swatch.</summary>
public sealed class ColorPickerWindow : Window
{
    readonly TextBox _hex = new();
    readonly Border _preview = new();
    readonly Slider _red = new() { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly Slider _green = new() { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly Slider _blue = new() { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly Slider _alpha = new() { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true, Value = 255 };
    readonly TextBlock _redText = new(), _greenText = new(), _blueText = new(), _alphaText = new();
    bool _updating;

    public string SelectedHex { get; private set; } = "#FFFFFFFF";

    public ColorPickerWindow(string initial)
    {
        Title = "Choose color";
        Width = 480; SizeToContent = SizeToContent.Height; MinWidth = 440;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(23, 28, 37));
        Foreground = Brushes.White;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "CUSTOM COLOR", FontWeight = FontWeights.Bold, Margin = new Thickness(2, 0, 0, 8) });

        var previewRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        _preview.Height = 48; _preview.CornerRadius = new CornerRadius(6);
        _preview.BorderBrush = Brushes.Gray; _preview.BorderThickness = new Thickness(1);
        DockPanel.SetDock(_preview, Dock.Left);
        _preview.Width = 120;
        // This preview must show the chosen color, so theming has to leave it alone.
        ThemeManager.SetSkipTheme(_preview, true);
        previewRow.Children.Add(_preview);
        var hexPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        hexPanel.Children.Add(new TextBlock { Text = "Hex (#RRGGBB or #AARRGGBB)", Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)), FontSize = 11 });
        hexPanel.Children.Add(_hex);
        previewRow.Children.Add(hexPanel);
        root.Children.Add(previewRow);

        root.Children.Add(SliderRow("Red", _red, _redText));
        root.Children.Add(SliderRow("Green", _green, _greenText));
        root.Children.Add(SliderRow("Blue", _blue, _blueText));
        root.Children.Add(SliderRow("Alpha", _alpha, _alphaText));

        var palette = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var swatches = new[]
        {
            Colors.White, Colors.Black, Colors.Red, Colors.Orange, Colors.Gold, Colors.Yellow, Colors.LimeGreen, Colors.Green,
            Colors.Teal, Colors.Cyan, Colors.DodgerBlue, Colors.Navy, Colors.Purple, Colors.Magenta, Colors.SlateGray, Colors.DimGray
        };
        foreach (var color in swatches)
        {
            var swatch = new Button
            {
                Width = 30, Height = 26, Margin = new Thickness(2), Background = new SolidColorBrush(color),
                BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1), ToolTip = color.ToString()
            };
            ThemeManager.SetSkipTheme(swatch, true);
            swatch.Click += (_, _) => SetHex($"#FF{color.R:X2}{color.G:X2}{color.B:X2}");
            palette.Children.Add(swatch);
        }
        root.Children.Add(palette);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var use = new Button { Content = "Use color", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        use.Click += (_, _) =>
        {
            if (!TryParse(_hex.Text, out _)) return;
            SelectedHex = Normalize(_hex.Text);
            DialogResult = true;
            Close();
        };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(use); buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;

        _hex.TextChanged += (_, _) => { if (_updating) return; if (TryParse(_hex.Text, out var color)) ApplyColor(color, syncHex: false); };
        _red.ValueChanged += (_, _) => SliderChanged();
        _green.ValueChanged += (_, _) => SliderChanged();
        _blue.ValueChanged += (_, _) => SliderChanged();
        _alpha.ValueChanged += (_, _) => SliderChanged();

        ThemeManager.Apply(this, PreferencesStore.Load().Theme);
        SetHex(initial);
        Loaded += (_, _) => { _hex.Focus(); _hex.SelectAll(); };
    }

    static FrameworkElement SliderRow(string label, Slider slider, TextBlock valueText)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        var name = new TextBlock { Text = label, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)) };
        valueText.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right;
        valueText.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
        Grid.SetColumn(name, 0); Grid.SetColumn(slider, 1); Grid.SetColumn(valueText, 2);
        grid.Children.Add(name); grid.Children.Add(slider); grid.Children.Add(valueText);
        return grid;
    }

    void SliderChanged()
    {
        if (_updating) return;
        var color = Color.FromArgb((byte)_alpha.Value, (byte)_red.Value, (byte)_green.Value, (byte)_blue.Value);
        ApplyColor(color, syncHex: true);
    }

    void ApplyColor(Color color, bool syncHex)
    {
        _updating = true;
        _red.Value = color.R; _green.Value = color.G; _blue.Value = color.B; _alpha.Value = color.A;
        _redText.Text = color.R.ToString(); _greenText.Text = color.G.ToString(); _blueText.Text = color.B.ToString(); _alphaText.Text = color.A.ToString();
        _preview.Background = new SolidColorBrush(color);
        if (syncHex) _hex.Text = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        _updating = false;
    }

    void SetHex(string value)
    {
        if (TryParse(value, out var color)) ApplyColor(color, syncHex: false);
        else ApplyColor(Colors.White, syncHex: false);
        _updating = true;
        _hex.Text = value;
        _updating = false;
    }

    static bool TryParse(string text, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(text.Trim())!;
            return true;
        }
        catch { color = Colors.White; return false; }
    }

    static string Normalize(string text) => text.Trim().ToUpperInvariant();

    /// <summary>Shows the picker and returns the chosen hex color, or null when cancelled.</summary>
    public static string? Pick(Window owner, string initial)
    {
        var dialog = new ColorPickerWindow(initial) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.SelectedHex : null;
    }
}
