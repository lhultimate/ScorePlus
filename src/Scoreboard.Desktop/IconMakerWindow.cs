using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// The Icon Creator. An icon is a glyph on its own plate, and each data state can carry a completely
/// different design: the away-possession mark can be red, smaller and on the left, while the
/// home-possession mark is green, larger and on the right. The first matching state wins; the
/// fallback design is used when no state matches.
/// </summary>
public sealed class IconMakerWindow : Window
{
    readonly string _theme;
    readonly ListBox _list = new();
    readonly ListBox _states = new() { Height = 92, DisplayMemberPath = "Label" };
    readonly TextBox _name = new() { MinWidth = 220 };
    readonly ComboBox _designFor = new() { MinHeight = 30, Margin = new Thickness(0, 3, 0, 0) };
    readonly TextBox _glyph = new() { MinWidth = 64, MaxWidth = 100 };
    readonly TextBox _color = new() { Name = "IconColor", MinWidth = 100 };
    readonly Border _colorSwatch = new()
    {
        Name = "IconColorSwatch", Width = 24, Height = 24, CornerRadius = new CornerRadius(4),
        BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x45, 0x52)),
        VerticalAlignment = VerticalAlignment.Center, ToolTip = "Glyph colour"
    };
    readonly TextBox _plate = new() { MinWidth = 100 };
    readonly CheckBox _round = new() { Content = "Round plate" };
    readonly TextBox _offsetX = new() { MinWidth = 64 };
    readonly TextBox _offsetY = new() { MinWidth = 64 };
    readonly TextBox _scale = new() { MinWidth = 64 };
    readonly TextBox _opacity = new() { MinWidth = 64 };
    readonly TextBox _rotation = new() { MinWidth = 64 };
    readonly TextBox _stateName = new() { MinWidth = 130 };
    readonly ComboBox _stateField = new() { MinHeight = 28, MinWidth = 150 };
    readonly ComboBox _stateOperator = new() { MinHeight = 28, MinWidth = 140, Margin = new Thickness(6, 0, 0, 0) };
    readonly TextBox _stateValue = new() { MinWidth = 110, Margin = new Thickness(6, 0, 0, 0) };
    readonly Button _removeState;
    readonly Border _previewStage = new()
    {
        Height = 150,
        CornerRadius = new CornerRadius(8),
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x11, 0x18)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x37, 0x45)),
        BorderThickness = new Thickness(1),
        Margin = new Thickness(0, 4, 0, 0),
        ClipToBounds = false
    };
    readonly Border _preview = new()
    {
        Width = 108,
        Height = 108,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
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
    CustomIconState? _designTarget;
    bool _filling;

    /// <summary>The library icon open in the maker, if it has been saved.</summary>
    public string? SelectedIconId => CustomIconStore.Find(_editing.Id)?.Id;

    public IconMakerWindow(string theme)
    {
        _theme = theme;
        Title = "ScorePlus — Icon Creator";
        Width = 880;
        Height = 710;
        MinWidth = 760;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        ScreenFit.Track(this);

        _preview.Child = _previewGlyph;
        _preview.RenderTransformOrigin = new Point(0.5, 0.5);
        _previewStage.Child = _preview;
        ThemeManager.SetSkipTheme(_previewStage, true);
        ThemeManager.SetSkipTheme(_colorSwatch, true);

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
            Text = "ICON CREATOR",
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(titleRow);
        header.Children.Add(new TextBlock
        {
            Text = "Design a fallback look, then add a data state for each game value — possession is away, possession is home, and so on. Every state keeps its own glyph, plate, position, size, rotation and opacity.",
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
            Content = "Close", IsCancel = true, MinHeight = 36, MinWidth = 96,
            Padding = new Thickness(14, 7, 14, 7)
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);

        var save = new Button
        {
            Content = "Save icon", IsDefault = true, MinHeight = 36, MinWidth = 120,
            Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0)
        };
        save.Click += (_, _) => SaveIcon();
        DockPanel.SetDock(save, Dock.Right);
        footer.Children.Add(save);

        _delete = new Button
        {
            Content = "Delete…", MinHeight = 36, MinWidth = 96,
            Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0)
        };
        _delete.Click += (_, _) => DeleteIcon();
        DockPanel.SetDock(_delete, Dock.Right);
        footer.Children.Add(_delete);

        footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        // ---- library and editor side by side ----
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var library = new DockPanel();
        library.Children.Add(new TextBlock
        {
            Text = "ICON LIBRARY", FontSize = 10, FontWeight = FontWeights.Bold,
            Opacity = 0.8, Margin = new Thickness(0, 0, 0, 6)
        });
        DockPanel.SetDock(library.Children[0], Dock.Top);
        var libraryActions = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var newButton = new Button { Content = "New icon", MinHeight = 32 };
        newButton.Click += (_, _) => StartNew();
        libraryActions.Children.Add(newButton);
        var importButton = new Button
        {
            Content = "Import .spi…", MinHeight = 32, Margin = new Thickness(0, 6, 0, 0),
            ToolTip = "Add one or more ScorePlus Icon (.spi) files to the library"
        };
        importButton.Click += (_, _) => ImportIcons();
        libraryActions.Children.Add(importButton);
        var exportButton = new Button
        {
            Content = "Export .spi…", MinHeight = 32, Margin = new Thickness(0, 6, 0, 0),
            ToolTip = "Write the icon in this editor as a ScorePlus Icon (.spi) file"
        };
        exportButton.Click += (_, _) => ExportIcon();
        libraryActions.Children.Add(exportButton);
        DockPanel.SetDock(libraryActions, Dock.Bottom);
        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is CustomIcon icon) LoadIcon(icon); };
        library.Children.Add(libraryActions);
        library.Children.Add(_list);
        Grid.SetColumn(library, 0);
        columns.Children.Add(library);

        var editor = new StackPanel();
        editor.Children.Add(SectionLabel("ICON NAME"));
        editor.Children.Add(_name);

        editor.Children.Add(SectionLabel("DESIGN FOR"));
        _designFor.DisplayMemberPath = "Label";
        _designFor.SelectionChanged += (_, _) =>
        {
            if (_filling || _designFor.SelectedItem is not DesignChoice choice) return;
            CommitDesign();
            _designTarget = choice.State;
            LoadDesign();
        };
        editor.Children.Add(_designFor);

        var glyphRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        glyphRow.Children.Add(Field("GLYPH", _glyph));
        glyphRow.Children.Add(Field("COLOUR", _color, _colorSwatch));
        glyphRow.Children.Add(Field("PLATE", _plate));
        editor.Children.Add(glyphRow);
        editor.Children.Add(new TextBlock
        {
            Text = "The glyph is one to three characters — ★ ● ▲ ⚑ ⬤ ✚ or a letter. Colours take #RRGGBB or #AARRGGBB; a plate starting #00 is transparent.",
            FontSize = 10, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0)
        });
        _round.Margin = new Thickness(0, 8, 0, 0);
        _round.Checked += (_, _) => UpdatePreview();
        _round.Unchecked += (_, _) => UpdatePreview();
        editor.Children.Add(_round);

        var positionRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        positionRow.Children.Add(Field("OFFSET X", _offsetX));
        positionRow.Children.Add(Field("OFFSET Y", _offsetY));
        positionRow.Children.Add(Field("SCALE", _scale));
        positionRow.Children.Add(Field("OPACITY 0–1", _opacity));
        positionRow.Children.Add(Field("ROTATION°", _rotation));
        editor.Children.Add(positionRow);
        editor.Children.Add(new TextBlock
        {
            Text = "Offsets are design pixels inside the scorebug box. Scale 1 is natural size; opacity 1 is solid.",
            FontSize = 10, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0)
        });

        editor.Children.Add(SectionLabel("PREVIEW", 14));
        editor.Children.Add(_previewStage);
        editor.Children.Add(new TextBlock
        {
            Text = "The preview shows the selected design exactly as it will draw. The icon switches designs when the game value matches a state.",
            FontSize = 10, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        });

        editor.Children.Add(SectionLabel("DATA STATES", 14));
        editor.Children.Add(_states);
        var stateButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        var addState = new Button { Content = "Add data state", MinHeight = 30, Padding = new Thickness(10, 3, 10, 3) };
        addState.Click += (_, _) => AddState();
        stateButtons.Children.Add(addState);
        _removeState = new Button
        {
            Content = "Remove state", MinHeight = 30, Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(6, 0, 0, 0), IsEnabled = false
        };
        _removeState.Click += (_, _) => RemoveState();
        stateButtons.Children.Add(_removeState);
        editor.Children.Add(stateButtons);

        editor.Children.Add(new TextBlock
        {
            Text = "STATE NAME (OPTIONAL)", FontSize = 10, FontWeight = FontWeights.Bold,
            Opacity = 0.8, Margin = new Thickness(0, 12, 0, 4)
        });
        _stateName.ToolTip = "A label for this design in the list; it does not affect when the design applies.";
        editor.Children.Add(_stateName);
        editor.Children.Add(new TextBlock
        {
            Text = "WHEN THIS DATA IS TRUE", FontSize = 10, FontWeight = FontWeights.Bold,
            Opacity = 0.8, Margin = new Thickness(0, 10, 0, 4)
        });
        var conditionRow = new WrapPanel();
        conditionRow.Children.Add(_stateField);
        conditionRow.Children.Add(_stateOperator);
        conditionRow.Children.Add(_stateValue);
        editor.Children.Add(conditionRow);
        editor.Children.Add(new TextBlock
        {
            Text = "The field, comparison and value below decide when this design replaces the fallback.",
            FontSize = 10, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0)
        });

        var scroll = new ScrollViewer
        {
            Content = editor,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetColumn(scroll, 2);
        columns.Children.Add(scroll);
        root.Children.Add(columns);

        foreach (var box in new[] { _name, _glyph, _color, _plate, _offsetX, _offsetY, _scale, _opacity, _rotation })
            box.TextChanged += (_, _) => UpdatePreview();
        _stateName.TextChanged += (_, _) =>
        {
            if (_filling || _designTarget is null) return;
            _designTarget.Name = _stateName.Text.Trim();
            RefreshStateLabels();
        };
        _stateField.SelectionChanged += (_, _) =>
        {
            if (_filling || _designTarget is null || _stateField.SelectedItem is not FieldChoice choice) return;
            var condition = _designTarget.Condition;
            condition.Field = choice.Field;
            var allowed = AutomationRules.OperatorsFor(condition.Field);
            if (!allowed.Any(option => option.Operator == condition.Operator)) condition.Operator = allowed[0].Operator;
            FillConditionControls();
            RefreshStateLabels();
        };
        _stateOperator.SelectionChanged += (_, _) =>
        {
            if (_filling || _designTarget is null || _stateOperator.SelectedItem is not OperatorChoice choice) return;
            _designTarget.Condition.Operator = choice.Operator;
            RefreshStateLabels();
        };
        _stateValue.TextChanged += (_, _) =>
        {
            if (_filling || _designTarget is null) return;
            var condition = _designTarget.Condition;
            if (AutomationRules.IsTextField(condition.Field)) condition.Text = _stateValue.Text.Trim();
            else if (AutomationRules.IsNumericField(condition.Field) && double.TryParse(_stateValue.Text, out var number))
                condition.Value = number;
            RefreshStateLabels();
        };
        _states.SelectionChanged += (_, _) =>
        {
            if (_filling || _states.SelectedItem is not CustomIconState state) return;
            CommitDesign();
            _designTarget = state;
            LoadDesign();
        };

        Content = root;
        ThemeManager.Apply(this, _theme);
        StartNew();
        RefreshLibrary();
    }

    static TextBlock SectionLabel(string text, double top = 12) => new()
    {
        Text = text, FontSize = 10, FontWeight = FontWeights.Bold,
        Opacity = 0.8, Margin = new Thickness(0, top, 0, 4)
    };

    static TextBlock FieldLabel(string text) => new()
    {
        Text = text, FontSize = 10, FontWeight = FontWeights.Bold,
        Opacity = 0.8, Margin = new Thickness(0, 0, 0, 4)
    };

    static StackPanel Field(string label, TextBox box, Border? swatch = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        panel.Children.Add(FieldLabel(label));
        if (swatch is null)
        {
            panel.Children.Add(box);
            return panel;
        }
        var row = new DockPanel();
        swatch.Margin = new Thickness(4, 0, 0, 0);
        DockPanel.SetDock(swatch, Dock.Right);
        row.Children.Add(swatch);
        row.Children.Add(box);
        panel.Children.Add(row);
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
        _filling = true;
        try
        {
            _name.Text = icon.Name;
            _states.ItemsSource = null;
            _states.ItemsSource = icon.States;
            _designTarget = null;
            RebuildDesignChoices();
            LoadDesign();
        }
        finally { _filling = false; }
        UpdatePreview();
    }

    void RebuildDesignChoices(CustomIconState? select = null)
    {
        var choices = new List<DesignChoice> { new(null) };
        choices.AddRange(_editing.States.Select(state => new DesignChoice(state)));
        _designFor.ItemsSource = choices;
        if (select is not null) _designTarget = select;
        _designFor.SelectedItem = choices.FirstOrDefault(choice => ReferenceEquals(choice.State, _designTarget)) ?? choices[0];
        _states.SelectedItem = select;
        _removeState.IsEnabled = select is not null;
    }

    void LoadDesign()
    {
        _filling = true;
        try
        {
            var design = _designTarget;
            _glyph.Text = design?.Glyph ?? _editing.Glyph;
            _color.Text = design?.Color ?? _editing.Color;
            _plate.Text = design?.Plate ?? _editing.Plate;
            _round.IsChecked = design?.Round ?? _editing.Round;
            _offsetX.Text = (design?.OffsetX ?? _editing.OffsetX).ToString("0.##");
            _offsetY.Text = (design?.OffsetY ?? _editing.OffsetY).ToString("0.##");
            _scale.Text = (design?.Scale ?? _editing.Scale).ToString("0.##");
            _opacity.Text = (design?.Opacity ?? _editing.Opacity).ToString("0.##");
            _rotation.Text = (design?.Rotation ?? _editing.Rotation).ToString("0.##");
            _stateName.Text = design?.Name ?? "";
            _stateName.IsEnabled = design is not null;
            _stateField.IsEnabled = design is not null;
            _stateOperator.IsEnabled = design is not null;
            _stateValue.IsEnabled = design is not null;
            _removeState.IsEnabled = design is not null;
            if (design is not null) FillConditionControls();
        }
        finally { _filling = false; }
        UpdatePreview();
    }

    void FillConditionControls()
    {
        if (_designTarget is null) return;
        var condition = _designTarget.Condition;
        var fieldChoices = AutomationRules.Fields.Select(option => new FieldChoice(option.Field, option.Label)).ToArray();
        var operatorChoices = AutomationRules.OperatorsFor(condition.Field)
            .Select(option => new OperatorChoice(option.Operator, option.Label)).ToArray();
        var previous = _filling;
        _filling = true;
        try
        {
            _stateField.ItemsSource = fieldChoices;
            _stateField.DisplayMemberPath = "Label";
            _stateField.SelectedItem = fieldChoices.FirstOrDefault(option => option.Field == condition.Field) ?? fieldChoices[0];
            _stateOperator.ItemsSource = operatorChoices;
            _stateOperator.DisplayMemberPath = "Label";
            _stateOperator.SelectedItem = operatorChoices.FirstOrDefault(option => option.Operator == condition.Operator) ?? operatorChoices[0];
            if (AutomationRules.IsBooleanField(condition.Field))
            {
                _stateValue.Text = "";
                _stateValue.IsEnabled = false;
            }
            else
            {
                _stateValue.Text = AutomationRules.IsTextField(condition.Field)
                    ? condition.Text
                    : condition.Value.ToString("0.##");
                _stateValue.IsEnabled = true;
            }
        }
        finally { _filling = previous; }
    }

    void CommitDesign()
    {
        if (_filling) return;
        var design = _designTarget;
        if (design is null)
        {
            _editing.Glyph = _glyph.Text.Trim();
            _editing.Color = _color.Text.Trim();
            _editing.Plate = _plate.Text.Trim();
            _editing.Round = _round.IsChecked == true;
            if (double.TryParse(_offsetX.Text, out var x)) _editing.OffsetX = x;
            if (double.TryParse(_offsetY.Text, out var y)) _editing.OffsetY = y;
            if (double.TryParse(_scale.Text, out var scale)) _editing.Scale = scale;
            if (double.TryParse(_opacity.Text, out var opacity)) _editing.Opacity = opacity;
            if (double.TryParse(_rotation.Text, out var rotation)) _editing.Rotation = rotation;
        }
        else
        {
            design.Glyph = _glyph.Text.Trim();
            design.Color = _color.Text.Trim();
            design.Plate = _plate.Text.Trim();
            design.Round = _round.IsChecked == true;
            if (double.TryParse(_offsetX.Text, out var x)) design.OffsetX = x;
            if (double.TryParse(_offsetY.Text, out var y)) design.OffsetY = y;
            if (double.TryParse(_scale.Text, out var scale)) design.Scale = scale;
            if (double.TryParse(_opacity.Text, out var opacity)) design.Opacity = opacity;
            if (double.TryParse(_rotation.Text, out var rotation)) design.Rotation = rotation;
            design.Name = _stateName.Text.Trim();
        }
    }

    void RefreshStateLabels()
    {
        _states.Items.Refresh();
        _designFor.Items.Refresh();
    }

    void AddState()
    {
        CommitDesign();
        var state = new CustomIconState
        {
            Glyph = _editing.Glyph,
            Color = _editing.Color,
            Plate = _editing.Plate,
            Round = _editing.Round,
            OffsetX = _editing.OffsetX,
            OffsetY = _editing.OffsetY,
            Scale = _editing.Scale,
            Opacity = _editing.Opacity,
            Rotation = _editing.Rotation,
            Name = ""
        };
        _editing.States.Add(state);
        _filling = true;
        try { _states.ItemsSource = _editing.States; }
        finally { _filling = false; }
        RebuildDesignChoices(state);
        LoadDesign();
        _stateName.Focus();
        _stateName.SelectAll();
        _status.Text = "New data state. Choose the value it follows, then design its look.";
    }

    void RemoveState()
    {
        if (_designTarget is null) return;
        var removing = _designTarget;
        _filling = true;
        try
        {
            _editing.States.Remove(removing);
            _designTarget = null;
            _states.ItemsSource = _editing.States;
        }
        finally { _filling = false; }
        RebuildDesignChoices();
        LoadDesign();
        _status.Text = "Data state removed. The fallback look is used when no state matches.";
    }

    void UpdatePreview()
    {
        if (_designTarget is not null) CommitDesign();
        var glyph = _glyph.Text.Trim();
        var colour = _color.Text.Trim();
        var plate = _plate.Text.Trim();
        _previewGlyph.Text = string.IsNullOrWhiteSpace(glyph) ? "★" : glyph;
        var glyphBrush = ScorebugRender.ParseBrush(string.IsNullOrWhiteSpace(colour) ? "#FFFFFF" : colour);
        _previewGlyph.Foreground = glyphBrush;
        _colorSwatch.Background = glyphBrush;
        _colorSwatch.ToolTip = string.IsNullOrWhiteSpace(colour) ? "Glyph colour: default white" : $"Glyph colour: {colour}";
        _preview.Background = ScorebugRender.ParseBrush(plate);
        _preview.CornerRadius = new CornerRadius(_round.IsChecked == true ? 54 : 16);
        _preview.Opacity = double.TryParse(_opacity.Text, out var opacity) ? Math.Clamp(opacity, 0, 1) : 1;
        var scale = double.TryParse(_scale.Text, out var s) ? Math.Clamp(s, 0.05, 8) : 1;
        var rotation = double.TryParse(_rotation.Text, out var r) ? r : 0;
        var offsetX = double.TryParse(_offsetX.Text, out var x) ? x : 0;
        var offsetY = double.TryParse(_offsetY.Text, out var y) ? y : 0;
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(scale * 0.72, scale * 0.72));
        transform.Children.Add(new RotateTransform(rotation));
        transform.Children.Add(new TranslateTransform(offsetX * 0.72, offsetY * 0.72));
        _preview.RenderTransform = transform;
    }

    void SaveIcon()
    {
        CommitDesign();
        var icon = _editing.Copy();
        icon.Name = _name.Text.Trim();
        icon.Normalize();
        _editing = CustomIconStore.Save(icon);
        Fill(_editing);
        RefreshLibrary();
        _status.Text = $"Saved “{_editing.Name}”. The studio's Made icon list now offers it.";
    }

    void ExportIcon()
    {
        CommitDesign();
        var icon = _editing.Copy();
        icon.Name = string.IsNullOrWhiteSpace(_name.Text) ? "Icon" : _name.Text.Trim();
        var dialog = new SaveFileDialog
        {
            Title = "Export ScorePlus icon",
            Filter = IconFiles.Filter,
            DefaultExt = IconFiles.Extension,
            AddExtension = true,
            FileName = SafeName(icon.Name)
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            IconFiles.Save(dialog.FileName, icon);
            _status.Text = $"Exported “{icon.Name}” to {System.IO.Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not export: {ex.Message}";
        }
    }

    void ImportIcons()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import ScorePlus icons",
            Filter = IconFiles.Filter,
            DefaultExt = IconFiles.Extension,
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        var imported = new List<CustomIcon>();
        foreach (var path in dialog.FileNames)
        {
            var icon = IconFiles.Load(path);
            if (icon is null) continue;
            imported.Add(CustomIconStore.Save(icon));
        }
        if (imported.Count == 0)
        {
            _status.Text = "That file is not a readable ScorePlus Icon (.spi).";
            return;
        }
        _editing = imported[^1];
        Fill(_editing);
        RefreshLibrary();
        var skipped = dialog.FileNames.Length - imported.Count;
        var skippedText = skipped > 0
            ? $" {skipped} file{(skipped == 1 ? "" : "s")} could not be read."
            : "";
        _status.Text = imported.Count == 1
            ? $"Imported “{_editing.Name}” from {System.IO.Path.GetFileName(dialog.FileNames[0])}.{skippedText}"
            : $"Imported {imported.Count} icons; the last is “{_editing.Name}”.{skippedText}";
    }

    static string SafeName(string name)
    {
        var trimmed = (name ?? "").Trim();
        foreach (var bad in System.IO.Path.GetInvalidFileNameChars()) trimmed = trimmed.Replace(bad, '-');
        return trimmed.Length == 0 ? "icon" : trimmed;
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

    sealed class DesignChoice
    {
        public DesignChoice(CustomIconState? state) => State = state;
        public CustomIconState? State { get; }
        public string Label => State is null ? "Fallback look (no data state matches)" : State.Label;
        public override string ToString() => Label;
    }

    sealed record FieldChoice(AutomationField Field, string Label) { public override string ToString() => Label; }
    sealed record OperatorChoice(AutomationOperator Operator, string Label) { public override string ToString() => Label; }
}
