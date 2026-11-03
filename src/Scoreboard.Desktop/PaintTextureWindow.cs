using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// Paint and finish for one element: gradients with any number of stops on the text and the plate,
/// a radial option for both, and the material knobs — roughness, glossiness, ambience, shine,
/// metallic, grain, emboss, bevel, satin, iridescence, contrast, scale, opacity and light angle.
/// Edits apply live to the element, so the studio canvas, the preview and the output all update.
/// </summary>
public sealed class PaintTextureWindow : Window
{
    readonly ScorebugCatalog _catalog;
    readonly GameEngine _engine;
    readonly string _theme;
    readonly ScorebugElement _draft;
    readonly Border _preview = new() { Height = 156, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 8) };
    readonly TextBlock _status = new() { FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    readonly ListBox _textStops = new() { Height = 78 };
    readonly ListBox _plateStops = new() { Height = 78 };
    readonly TextBox _textOffset = Field("0");
    readonly TextBox _textColour = Field("#FFFFFFFF");
    readonly TextBox _plateOffset = Field("0");
    readonly TextBox _plateColour = Field("#FFFFFFFF");
    readonly System.Windows.Threading.DispatcherTimer _apply = new() { Interval = TimeSpan.FromMilliseconds(180) };

    PaintTextureWindow(ScorebugCatalog catalog, GameEngine engine, ScorebugElement element, string theme)
    {
        _catalog = catalog;
        _engine = engine;
        _theme = theme;
        _draft = JsonSerializer.Deserialize<ScorebugElement>(JsonSerializer.Serialize(element)) ?? element;

        Title = "Paint & texture — " + element.Name;
        Width = 900;
        Height = 780;
        MinWidth = 760;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "PAINT & TEXTURE", FontSize = 19, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock
        {
            Text = "Paint the text and the plate with gradients that have as many stops as you like, then lay a finish over them. Every change is live on the graphic.",
            FontSize = 11,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(header);

        var columns = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetRow(columns, 1);
        root.Children.Add(columns);
        columns.Children.Add(BuildPaintColumn(true));
        var plate = BuildPaintColumn(false);
        Grid.SetColumn(plate, 1);
        columns.Children.Add(plate);

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var done = new Button { Content = "Done", IsDefault = true, MinHeight = 34, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(6, 0, 0, 0) };
        done.Click += (_, _) => Close();
        var reset = new Button { Content = "Reset paint", MinHeight = 34, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6, 0, 0, 0) };
        reset.Click += (_, _) =>
        {
            _draft.TextGradientPaint = null; _draft.TextTexture = null;
            _draft.BackgroundGradientPaint = null; _draft.BackgroundTexture = null;
            RefreshAll();
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(reset);
        actions.Children.Add(done);
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);
        footer.Children.Add(_status);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        ThemeManager.Apply(this, theme);
        _apply.Tick += (_, _) => { _apply.Stop(); Commit(); };
        Loaded += (_, _) => RefreshAll();
        Closed += (_, _) => { _apply.Stop(); Commit(); };
    }

    static TextBox Field(string text) => new()
    {
        Text = text,
        MinHeight = 26,
        Padding = new Thickness(6, 3, 6, 3),
        FontSize = 12,
        Margin = new Thickness(0, 2, 0, 2)
    };

    static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
        Margin = new Thickness(0, 10, 0, 4)
    };

    UIElement BuildPaintColumn(bool text)
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(text ? 0 : 8, 0, text ? 8 : 0, 0)
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = text ? "TEXT PAINT" : "PLATE PAINT", FontSize = 13, FontWeight = FontWeights.Bold });
        // One live sample, beside the plate controls: a single UI element cannot sit in two columns.
        if (!text) panel.Children.Add(_preview);

        var gradient = text ? _draft.TextGradientPaint : _draft.BackgroundGradientPaint;
        var enabled = new CheckBox { Content = "Paint a gradient", IsChecked = gradient is { IsUsable: true }, Margin = new Thickness(0, 2, 0, 2) };
        enabled.Checked += (_, _) => SetGradient(text, Existing(text) ?? ScorebugGradient.TwoStop(
            text ? _draft.Foreground : _draft.Background,
            text ? "#FF38D7A3" : _draft.BackgroundColor2));
        enabled.Unchecked += (_, _) => SetGradient(text, null);
        panel.Children.Add(enabled);

        var radial = new CheckBox { Content = "Radial (out from a centre)", IsChecked = gradient?.Radial == true, Margin = new Thickness(0, 2, 0, 2) };
        radial.Checked += (_, _) => { var paint = Existing(text); if (paint is null) return; paint.Radial = true; Touch(); };
        radial.Unchecked += (_, _) => { var paint = Existing(text); if (paint is null) return; paint.Radial = false; Touch(); };
        panel.Children.Add(radial);

        var angleRow = new WrapPanel();
        angleRow.Children.Add(new TextBlock { Text = "Angle", FontSize = 10, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var angle = Field((gradient?.Angle ?? 90).ToString("0.##", CultureInfo.CurrentCulture));
        angle.Width = 60;
        angle.TextChanged += (_, _) =>
        {
            var paint = Existing(text);
            if (paint is null || !double.TryParse(angle.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)) return;
            paint.Angle = value;
            Touch();
        };
        angleRow.Children.Add(angle);
        if (gradient?.Radial == true)
        {
            angleRow.Children.Add(new TextBlock { Text = "Centre X", FontSize = 10, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
            angleRow.Children.Add(NumberBox(gradient.CenterX, 0.5, 0, 1, value => { var paint = Existing(text); if (paint is null) return; paint.CenterX = value; Touch(); }));
            angleRow.Children.Add(new TextBlock { Text = "Y", FontSize = 10, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
            angleRow.Children.Add(NumberBox(gradient.CenterY, 0.5, 0, 1, value => { var paint = Existing(text); if (paint is null) return; paint.CenterY = value; Touch(); }));
        }
        panel.Children.Add(angleRow);

        panel.Children.Add(Section("GRADIENT STOPS"));
        var list = text ? _textStops : _plateStops;
        list.SelectionChanged += (_, _) => LoadStop(text);
        panel.Children.Add(list);
        var stopRow = new WrapPanel();
        var offset = text ? _textOffset : _plateOffset;
        var colour = text ? _textColour : _plateColour;
        stopRow.Children.Add(new TextBlock { Text = "At", FontSize = 10, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        offset.Width = 52;
        stopRow.Children.Add(offset);
        colour.Width = 92;
        stopRow.Children.Add(colour);
        var pick = new Button { Content = "Pick…", MinHeight = 26, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(4, 2, 0, 2) };
        pick.Click += (_, _) => { if (ColorPickerWindow.Pick(this, colour.Text) is string picked) { colour.Text = picked; UpdateStop(text); } };
        stopRow.Children.Add(pick);
        panel.Children.Add(stopRow);
        var stopButtons = new StackPanel { Orientation = Orientation.Horizontal };
        stopButtons.Children.Add(Action("Add stop", () => AddStop(text)));
        stopButtons.Children.Add(Action("Update stop", () => UpdateStop(text)));
        stopButtons.Children.Add(Action("Remove stop", () => RemoveStop(text)));
        panel.Children.Add(stopButtons);

        var finishHeader = text ? "TEXT FINISH" : "PLATE FINISH";
        panel.Children.Add(Section(finishHeader));
        var finish = text ? _draft.TextTexture : _draft.BackgroundTexture;
        var finishOn = new CheckBox { Content = "Lay a material over this paint", IsChecked = finish?.Enabled == true, Margin = new Thickness(0, 2, 0, 4) };
        finishOn.Checked += (_, _) => SetTexture(text, ExistingTexture(text) ?? ScorebugTexture.ForKind("Matte"));
        finishOn.Unchecked += (_, _) => SetTexture(text, null);
        panel.Children.Add(finishOn);

        var kindRow = new WrapPanel();
        kindRow.Children.Add(new TextBlock { Text = "Material", FontSize = 10, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var kind = new ComboBox { MinWidth = 128, MinHeight = 26, ItemsSource = ScorebugTexture.Kinds, Margin = new Thickness(0, 2, 0, 2) };
        if (finish is not null && ScorebugTexture.Kinds.Contains(finish.Kind)) kind.SelectedItem = finish.Kind;
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedItem is not string chosen) return;
            var texture = ExistingTexture(text);
            if (texture is null) return;
            var preset = ScorebugTexture.ForKind(chosen);
            texture.Kind = chosen;
            texture.Roughness = preset.Roughness; texture.Glossiness = preset.Glossiness;
            texture.Ambience = preset.Ambience; texture.Shine = preset.Shine;
            texture.Metallic = preset.Metallic; texture.Grain = preset.Grain;
            texture.Emboss = preset.Emboss; texture.Bevel = preset.Bevel;
            texture.Satin = preset.Satin; texture.Iridescence = preset.Iridescence;
            texture.Opacity = preset.Opacity; texture.Scale = preset.Scale;
            texture.Contrast = preset.Contrast; texture.Angle = preset.Angle;
            Rebuild(text);
        };
        kindRow.Children.Add(kind);
        panel.Children.Add(kindRow);

        (string Label, string Key, Func<ScorebugTexture, double> Get, Action<ScorebugTexture, double> Set)[] knobs =
        [
            ("Roughness", "roughness", t => t.Roughness, (t, v) => t.Roughness = v),
            ("Glossiness", "glossiness", t => t.Glossiness, (t, v) => t.Glossiness = v),
            ("Ambience", "ambience", t => t.Ambience, (t, v) => t.Ambience = v),
            ("Shine", "shine", t => t.Shine, (t, v) => t.Shine = v),
            ("Metallic", "metallic", t => t.Metallic, (t, v) => t.Metallic = v),
            ("Grain", "grain", t => t.Grain, (t, v) => t.Grain = v),
            ("Emboss", "emboss", t => t.Emboss, (t, v) => t.Emboss = v),
            ("Bevel", "bevel", t => t.Bevel, (t, v) => t.Bevel = v),
            ("Satin", "satin", t => t.Satin, (t, v) => t.Satin = v),
            ("Iridescence", "iridescence", t => t.Iridescence, (t, v) => t.Iridescence = v),
            ("Opacity", "opacity", t => t.Opacity, (t, v) => t.Opacity = v),
            ("Scale", "scale", t => t.Scale, (t, v) => t.Scale = v),
            ("Contrast", "contrast", t => t.Contrast, (t, v) => t.Contrast = v),
            ("Light angle", "angle", t => t.Angle, (t, v) => t.Angle = v)
        ];
        foreach (var knob in knobs)
        {
            var current = ExistingTexture(text);
            var max = knob.Key is "scale" ? 12 : knob.Key is "angle" ? 360 : 1;
            var min = knob.Key is "angle" ? -360 : 0;
            var value = current is null ? 0 : knob.Get(current);
            if (knob.Key == "scale" && current is null) value = 1;
            panel.Children.Add(Slider(knob.Label, min, max, value, updated =>
            {
                var texture = ExistingTexture(text);
                if (texture is null) return;
                knob.Set(texture, updated);
                Touch();
            }));
        }

        var tintRow = new WrapPanel();
        tintRow.Children.Add(new TextBlock { Text = "Tint", FontSize = 10, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var tint = Field(finish?.Tint ?? "#FFFFFFFF");
        tint.Width = 92;
        tint.TextChanged += (_, _) =>
        {
            var texture = ExistingTexture(text);
            if (texture is null || string.IsNullOrWhiteSpace(tint.Text)) return;
            texture.Tint = tint.Text.Trim();
            Touch();
        };
        tintRow.Children.Add(tint);
        var tintPick = new Button { Content = "Pick…", MinHeight = 26, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(4, 2, 0, 2) };
        tintPick.Click += (_, _) =>
        {
            if (ColorPickerWindow.Pick(this, tint.Text) is not string picked) return;
            tint.Text = picked;
            var texture = ExistingTexture(text);
            if (texture is null) return;
            texture.Tint = picked;
            Touch();
        };
        tintRow.Children.Add(tintPick);
        panel.Children.Add(tintRow);
        panel.Children.Add(Slider("Tint amount", 0, 1, finish?.TintAmount ?? 0, updated =>
        {
            var texture = ExistingTexture(text);
            if (texture is null) return;
            texture.TintAmount = updated;
            Touch();
        }));

        scroll.Content = panel;
        return scroll;
    }

    static Button Action(string text, Action click, bool _ = false)
    {
        var button = new Button { Content = text, MinHeight = 26, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 2, 4, 2), FontSize = 11 };
        button.Click += (_, _) => click();
        return button;
    }

    FrameworkElement Slider(string label, double min, double max, double value, Action<double> set)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
        var caption = new TextBlock { Text = label, Width = 82, FontSize = 10, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };
        var readout = new TextBlock { Text = value.ToString("0.##", CultureInfo.CurrentCulture), Width = 38, FontSize = 10, Opacity = 0.8, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new System.Windows.Controls.Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            SmallChange = (max - min) / 100,
            LargeChange = (max - min) / 10,
            MinHeight = 22,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(caption, Dock.Left);
        DockPanel.SetDock(readout, Dock.Right);
        panel.Children.Add(caption);
        panel.Children.Add(readout);
        panel.Children.Add(slider);
        slider.ValueChanged += (_, e) =>
        {
            readout.Text = e.NewValue.ToString("0.##", CultureInfo.CurrentCulture);
            set(e.NewValue);
        };
        return panel;
    }

    TextBox NumberBox(double value, double fallback, double min, double max, Action<double> set)
    {
        var box = Field(value.ToString("0.##", CultureInfo.CurrentCulture));
        box.Width = 48;
        box.TextChanged += (_, _) =>
        {
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)) return;
            set(Math.Clamp(parsed, min, max));
        };
        return box;
    }

    ScorebugGradient? Existing(bool text) => text ? _draft.TextGradientPaint : _draft.BackgroundGradientPaint;

    ScorebugTexture? ExistingTexture(bool text) => text ? _draft.TextTexture : _draft.BackgroundTexture;

    void SetGradient(bool text, ScorebugGradient? paint)
    {
        if (paint is not null) paint.Normalize();
        if (text) _draft.TextGradientPaint = paint; else _draft.BackgroundGradientPaint = paint;
        Rebuild(text);
    }

    void SetTexture(bool text, ScorebugTexture? texture)
    {
        if (texture is not null) texture.Normalize();
        if (text) _draft.TextTexture = texture; else _draft.BackgroundTexture = texture;
        Rebuild(text);
    }

    /// <summary>A whole section is rebuilt for changes that alter its shape (a new gradient, a preset).</summary>
    void Rebuild(bool text)
    {
        if (Content is Grid grid && grid.Children.Count > 1 && grid.Children[1] is Grid columns)
        {
            // The live sample, the two stop lists and the stop fields are shared by every rebuild.
            // Detach them from the old panels first: WPF will not parent the same control twice, and
            // that is exactly what the material checkbox used to trigger. Some of them sit inside a
            // nested stop row, so clearing only the column panel would leave those parents in place.
            foreach (var shared in new FrameworkElement[]
            {
                _preview, _textStops, _plateStops, _textOffset, _textColour, _plateOffset, _plateColour
            })
            {
                if (shared.Parent is Panel parent) parent.Children.Remove(shared);
                else if (shared.Parent is ContentControl content && ReferenceEquals(content.Content, shared)) content.Content = null;
                else if (shared.Parent is Decorator decorator && ReferenceEquals(decorator.Child, shared)) decorator.Child = null;
            }
            foreach (var old in columns.Children.OfType<ScrollViewer>())
            {
                if (old.Content is Panel panel) panel.Children.Clear();
                old.Content = null;
            }
            var fresh = BuildPaintColumn(text);
            var other = BuildPaintColumn(!text);
            var textColumn = text ? fresh : other;
            var plateColumn = text ? other : fresh;
            columns.Children.Clear();
            columns.Children.Add(textColumn);
            Grid.SetColumn(plateColumn, 1);
            columns.Children.Add(plateColumn);
        }
        Touch();
    }

    void AddStop(bool text)
    {
        var paint = Existing(text);
        if (paint is null) return;
        var offset = Number(text ? _textOffset.Text : _plateOffset.Text, 0.5, 0, 1);
        var colour = Colour(text ? _textColour.Text : _plateColour.Text);
        paint.Stops.Add(new GradientStopModel(offset, colour));
        paint.Normalize();
        RefreshStops(text);
        Touch();
    }

    void UpdateStop(bool text)
    {
        var paint = Existing(text);
        var list = text ? _textStops : _plateStops;
        if (paint is null || list.SelectedIndex < 0 || list.SelectedIndex >= paint.Stops.Count) return;
        paint.Stops[list.SelectedIndex].Offset = Number(text ? _textOffset.Text : _plateOffset.Text, paint.Stops[list.SelectedIndex].Offset, 0, 1);
        paint.Stops[list.SelectedIndex].Color = Colour(text ? _textColour.Text : _plateColour.Text);
        paint.Normalize();
        RefreshStops(text);
        Touch();
    }

    void RemoveStop(bool text)
    {
        var paint = Existing(text);
        var list = text ? _textStops : _plateStops;
        if (paint is null || paint.Stops.Count <= 2 || list.SelectedIndex < 0 || list.SelectedIndex >= paint.Stops.Count) return;
        paint.Stops.RemoveAt(list.SelectedIndex);
        paint.Normalize();
        RefreshStops(text);
        Touch();
    }

    void LoadStop(bool text)
    {
        var paint = Existing(text);
        var list = text ? _textStops : _plateStops;
        if (paint is null || list.SelectedIndex < 0 || list.SelectedIndex >= paint.Stops.Count) return;
        var stop = paint.Stops[list.SelectedIndex];
        (text ? _textOffset : _plateOffset).Text = stop.Offset.ToString("0.##", CultureInfo.CurrentCulture);
        (text ? _textColour : _plateColour).Text = stop.Color;
    }

    void RefreshStops(bool text)
    {
        var paint = Existing(text);
        var list = text ? _textStops : _plateStops;
        list.ItemsSource = paint?.Stops.Select((stop, index) => $"{index + 1}.  {stop.Offset:0.##}  ·  {stop.Color}").ToArray();
        if (list.Items.Count > 0) list.SelectedIndex = 0;
    }

    void RefreshAll()
    {
        RefreshStops(true);
        RefreshStops(false);
        RefreshPreview();
    }

    /// <summary>The live sample: the element's own text painted with the draft's fill and plate.</summary>
    void RefreshPreview()
    {
        var state = _engine.State;
        var plate = new Border
        {
            Width = 380,
            Height = 142,
            CornerRadius = new CornerRadius(8),
            Background = _draft.TransparentBackground ? Brushes.Transparent : Paint.Background(_draft, state),
            BorderBrush = Paint.Border(_draft, state),
            BorderThickness = new Thickness(Math.Max(0, _draft.BorderThickness))
        };
        var text = new ScorebugTextBlock(_draft)
        {
            Text = string.IsNullOrWhiteSpace(_draft.Content) || _draft.Content.StartsWith("{{", StringComparison.Ordinal) ? "SCORE 21" : _draft.Content,
            FontSize = Math.Clamp(_draft.FontSize, 14, 64),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        plate.Child = text;
        _preview.Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x14));
        _preview.Child = new Viewbox { Child = plate, Stretch = Stretch.Uniform, Margin = new Thickness(6) };
        _status.Text = "Click Done to keep these changes; they are already on the graphic.";
    }

    void Touch()
    {
        RefreshPreview();
        _apply.Stop();
        _apply.Start();
    }

    void Commit()
    {
        _draft.TextGradientPaint?.Normalize();
        _draft.BackgroundGradientPaint?.Normalize();
        _draft.TextTexture?.Normalize();
        _draft.BackgroundTexture?.Normalize();
        _catalog.Update(layout =>
        {
            var target = layout.Elements.FirstOrDefault(item => string.Equals(item.Id, _draft.Id, StringComparison.OrdinalIgnoreCase));
            if (target is null) return;
            target.TextGradientPaint = _draft.TextGradientPaint?.Copy();
            target.BackgroundGradientPaint = _draft.BackgroundGradientPaint?.Copy();
            target.TextTexture = _draft.TextTexture?.Copy();
            target.BackgroundTexture = _draft.BackgroundTexture?.Copy();
        });
    }

    static double Number(string? text, double fallback, double min, double max)
    {
        if (!double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var value)) return fallback;
        return Math.Clamp(value, min, max);
    }

    static string Colour(string? text)
    {
        var value = (text ?? "").Trim();
        if (value.Length >= 4 && value[0] == '#' && value[1..].Length is 3 or 4 or 6 or 8 && value[1..].All(Uri.IsHexDigit)) return value;
        return "#FFFFFFFF";
    }

    /// <summary>Opens the editor for one element, applying every change to the live layout.</summary>
    public static void Edit(Window owner, ScorebugCatalog catalog, GameEngine engine, ScorebugElement element, string theme)
    {
        var window = new PaintTextureWindow(catalog, engine, element, theme) { Owner = owner };
        window.ShowDialog();
    }
}
