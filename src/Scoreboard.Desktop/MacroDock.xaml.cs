using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// The Macros tab: a board of one-press buttons on the left, and the ordered step editor for the
/// selected button on the right. Macros live in the game state, so opening a saved game brings the
/// show's buttons back with it.
/// </summary>
public partial class MacroDock : UserControl
{
    // A firm threshold keeps a click on the edit handle from becoming a drag just because the
    // pointer moved a few pixels while the button went down. Dragging the whole card still works.
    const double MacroDragThreshold = 10;
    static readonly string[] MacroColors =
    [
        "#38D7A3", "#4C9AFF", "#FF6B6B", "#FFB86B",
        "#B98CFF", "#F1C40F", "#2ECC71", "#95A5A6"
    ];

    readonly GameEngine _engine;
    readonly MediaSession _session;
    readonly MacroRunner _runner;
    List<SavedMacro> _macros = [];
    SavedMacro? _draft;
    SavedMacro? _dragMacro;
    bool _dragging;
    Point _dragOrigin;
    bool _syncing;
    bool _dirty;

    sealed record KindChoice(MacroActionKind Kind, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record PlayerChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record CommandChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record SportValueChoice(string Id, string Label, bool Flag)
    {
        public override string ToString() => Label;
    }

    sealed record CustomValueChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    public event Action<string>? Status;

    /// <summary>Repaints the board after a theme or accent change.</summary>
    public void ApplyTheme(string theme)
    {
        var light = ThemeManager.Resolve(theme) == "Light";
        // Neutral operator shades, not the old blue-tinted navy, so the macro editor belongs to
        // the rest of the workspace in dark mode as well as light.
        Set("MacroPanelBrush", light ? "#FFFFFF" : "#080808");
        Set("MacroSurfaceBrush", light ? "#E5EAF0" : "#141414");
        Set("MacroInputBrush", light ? "#FFFFFF" : "#050505");
        Set("MacroTextBrush", light ? "#18202B" : "#F4F7FB");
        Set("MacroMutedBrush", light ? "#586576" : "#B0B0B0");
        Set("MacroBorderBrush", light ? "#B9C3CE" : "#383838");
        Set("MacroHoverBrush", light ? "#D9E2EC" : "#2A2A2A");

        var accent = UiPalette.Accent(PreferencesStore.Load());
        var soft = light ? UiPalette.Mix(accent, Colors.White, 0.78) : UiPalette.Soft(accent);
        Set("MacroAccentBrush", accent);
        Set("MacroAccentSoftBrush", soft);
        // Text sits on the soft fill, not on the bright accent itself. Reading contrast from the
        // accent made dark-theme buttons print dark-on-dark.
        Set("MacroAccentTextBrush", Readable(soft));
        Set("MacroSelectionBrush", soft);
        // The selection tint is never the same value as the box behind it, so the ordinary text
        // colour is the reliable readable choice here in both themes.
        Set("MacroSelectionTextBrush", light ? "#18202B" : "#F4F7FB");
        RenderMacros();
        UpdateColorPreview();
        UpdateColorChoiceHighlight();
    }

    void Set(string key, string hex) => Set(key, (Color)ColorConverter.ConvertFromString(hex));

    void Set(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Resources[key] = brush;
    }

    static Color Readable(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255;
        return luminance > 0.6 ? Color.FromRgb(24, 32, 43) : Colors.White;
    }

    public MacroDock(GameEngine engine, MediaSession session, string? theme = null, Action<string>? executeControl = null)
    {
        InitializeComponent();
        _engine = engine;
        _session = session;
        _runner = new MacroRunner(engine, session, executeControl);
        // The board paints itself from its own palette so generated cards, the step list and the
        // editor all follow the active theme. Opting out keeps the window-wide theme pass from
        // flattening the card hierarchy and its accent.
        ThemeManager.SetSkipTheme(this, true);
        BuildColorChoices();
        ApplyTheme(theme ?? PreferencesStore.Load().Theme);
        KindBox.ItemsSource = new[]
        {
            new KindChoice(MacroActionKind.Score, "Score — add points to a side"),
            new KindChoice(MacroActionKind.SetDown, "Down — set the current down"),
            new KindChoice(MacroActionKind.SetTimeouts, "Timeouts — set a side's remaining count"),
            new KindChoice(MacroActionKind.SetGameClock, "Game clock — set the clock"),
            new KindChoice(MacroActionKind.SetPlayClock, "Play clock — set the clock"),
            new KindChoice(MacroActionKind.SetTimeoutClock, "Timeout clock — set the clock"),
            new KindChoice(MacroActionKind.SetPeriod, "Period — set the current period"),
            new KindChoice(MacroActionKind.PlayVideo, "Video — play a clip"),
            new KindChoice(MacroActionKind.RosterStat, "Roster stat — change a player's number"),
            new KindChoice(MacroActionKind.Control, "Console button — run any control action"),
            new KindChoice(MacroActionKind.SportValue, "Sport value — set any counter or flag"),
            new KindChoice(MacroActionKind.SetFootballDistance, "Football — set distance to go"),
            new KindChoice(MacroActionKind.SetFootballBallSpot, "Football — set ball spot"),
            new KindChoice(MacroActionKind.SetPossession, "Possession — set the team with the ball"),
            new KindChoice(MacroActionKind.CustomValue, "Custom value — set a value on the console")
        };
        KindBox.SelectedIndex = 0;
        SideBox.ItemsSource = new[] { "Home", "Visitors" };
        SideBox.SelectedIndex = 0;
        RefreshView();
    }

    /// <summary>
    /// Commits a macro field being typed when the game is saved or the operator leaves the tab, so
    /// the board in the file is the one they can see rather than only the last explicit Save macro.
    /// </summary>
    public void CommitPendingEdits()
    {
        if (_dirty) SaveDraft(false);
    }

    /// <summary>Reloads the saved buttons and refreshes the editor when it is safe to do so.</summary>
    public void RefreshView(bool force = false)
    {
        if (force) { _draft = null; _dirty = false; }
        var selectedId = _draft?.Id;
        _macros = _engine.State.Macros.Select(macro => macro.Copy()).ToList();
        if (_draft is null || !_dirty)
        {
            var selected = _macros.FirstOrDefault(macro => macro.Id == selectedId) ?? _macros.FirstOrDefault();
            _draft = selected?.Copy();
        }
        RenderMacros();
        LoadEditor();
    }

    void RenderMacros()
    {
        MacroButtons.Children.Clear();
        EmptyText.Visibility = _macros.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var macro in _macros)
        {
            var customColor = ParseColor(macro.Color);
            var card = new Border
            {
                Width = 226,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(8),
                Background = (Brush)Resources["MacroSurfaceBrush"],
                BorderBrush = customColor is Color cardColor ? ColorBrush(cardColor) : (Brush)Resources["MacroBorderBrush"],
                BorderThickness = new Thickness(customColor is null ? 1 : 2),
                CornerRadius = new CornerRadius(9),
                Tag = macro
            };
            card.PreviewMouseLeftButtonDown += MacroCard_MouseDown;
            // Use the bubbling event here, not the preview event: the header's MouseUp needs to
            // arrive before the card clears the press, otherwise a header click never edits.
            card.MouseLeftButtonUp += MacroCard_MouseUp;
            var stack = new StackPanel();
            // The handle is a real, always-visible drag surface. The old card-only preview handler
            // was swallowed by the run Button, which is why dragging a macro did nothing.
            var header = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(4, 3, 4, 5),
                MinHeight = 20,
                Cursor = Cursors.SizeAll,
                Tag = macro,
                ToolTip = "Drag to reorder · click or double-click to edit · right-click for Edit / Delete"
            };
            header.Child = new TextBlock
            {
                Text = "⠿  DRAG · CLICK TO EDIT",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Resources["MacroMutedBrush"]
            };
            header.MouseLeftButtonDown += MacroHeader_MouseDown;
            header.MouseLeftButtonUp += MacroHeader_MouseUp;
            stack.Children.Add(header);
            var run = new Button
            {
                Style = (Style)Resources["MacroAccent"],
                Content = new TextBlock
                {
                    Text = $"{macro.Label}\n{macro.ActionCount} step{(macro.ActionCount == 1 ? "" : "s")}",
                    TextWrapping = TextWrapping.Wrap
                },
                Tag = macro,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                ToolTip = string.Join(Environment.NewLine, macro.Actions.Select(action => action.Summary))
            };
            if (customColor is Color fill)
            {
                // A custom colour owns the button itself, not just a tiny border stripe, so a
                // board full of macros can be read at a glance.
                run.Background = ColorBrush(fill);
                run.BorderBrush = ColorBrush(UiPalette.Mix(fill, Colors.Black, 0.25));
                run.Foreground = ColorBrush(Readable(fill));
            }
            run.Click += RunMacro_Click;
            stack.Children.Add(run);
            card.Child = stack;
            AttachMacroMenu(card, macro);
            AttachMacroMenu(header, macro);
            AttachMacroMenu(run, macro);
            MacroButtons.Children.Add(card);
        }
    }

    void LoadEditor()
    {
        _syncing = true;
        try
        {
            NameBox.Text = _draft?.Label ?? "";
            ColorBox.Text = _draft?.Color ?? "";
            StepList.ItemsSource = _draft?.Actions;
            SaveButton.IsEnabled = DeleteButton.IsEnabled = _draft is not null;
            EditStatus.Text = _draft is null ? "NEW" : _dirty ? "UNSAVED" : "SAVED";
            if (_draft is not null && StepList.Items.Count > 0) StepList.SelectedIndex = 0;
            UpdateFields();
            UpdateColorPreview();
            UpdateColorChoiceHighlight();
        }
        finally { _syncing = false; }
    }

    MacroActionKind SelectedKind => KindBox.SelectedItem is KindChoice choice ? choice.Kind : MacroActionKind.Score;

    void UpdateFields()
    {
        var kind = SelectedKind;
        SidePanel.Visibility = kind is MacroActionKind.Score or MacroActionKind.SetTimeouts
            or MacroActionKind.RosterStat or MacroActionKind.SetPossession
            ? Visibility.Visible : Visibility.Collapsed;
        AmountPanel.Visibility = kind == MacroActionKind.Score ? Visibility.Visible : Visibility.Collapsed;
        ValuePanel.Visibility = kind is MacroActionKind.SetDown or MacroActionKind.SetTimeouts
            or MacroActionKind.SetGameClock or MacroActionKind.SetPlayClock
            or MacroActionKind.SetTimeoutClock or MacroActionKind.SetPeriod
            or MacroActionKind.RosterStat or MacroActionKind.SportValue or MacroActionKind.CustomValue
            or MacroActionKind.SetFootballDistance or MacroActionKind.SetFootballBallSpot
            ? Visibility.Visible : Visibility.Collapsed;
        RosterPanel.Visibility = kind == MacroActionKind.RosterStat ? Visibility.Visible : Visibility.Collapsed;
        VideoPanel.Visibility = kind == MacroActionKind.PlayVideo ? Visibility.Visible : Visibility.Collapsed;
        CommandPanel.Visibility = kind == MacroActionKind.Control ? Visibility.Visible : Visibility.Collapsed;
        SportValuePanel.Visibility = kind == MacroActionKind.SportValue ? Visibility.Visible : Visibility.Collapsed;
        CustomValuePanel.Visibility = kind == MacroActionKind.CustomValue ? Visibility.Visible : Visibility.Collapsed;
        ValueLabel.Text = kind switch
        {
            MacroActionKind.SetDown => "DOWN (1–4)",
            MacroActionKind.SetTimeouts => "TIMEOUTS REMAINING",
            MacroActionKind.SetGameClock => "SECONDS",
            MacroActionKind.SetPlayClock => "SECONDS",
            MacroActionKind.SetTimeoutClock => "SECONDS",
            MacroActionKind.SetPeriod => "PERIOD",
            MacroActionKind.RosterStat => "VALUE (OR AMOUNT TO ADD)",
            MacroActionKind.SportValue => "VALUE (OR AMOUNT TO ADD)",
            MacroActionKind.SetFootballDistance => "YARDS TO GO (1–99)",
            MacroActionKind.SetFootballBallSpot => "BALL SPOT (0–50)",
            MacroActionKind.CustomValue => "VALUE (OR AMOUNT TO ADD)",
            _ => "VALUE"
        };
        if (kind is MacroActionKind.RosterStat) SyncRoster();
        if (kind is MacroActionKind.Control) RefreshCommandChoices();
        if (kind is MacroActionKind.SportValue) RefreshSportValueChoices();
        if (kind is MacroActionKind.CustomValue) RefreshCustomValueChoices();
    }

    void SyncRoster()
    {
        var home = SideBox.SelectedIndex != 1;
        var rosterId = home ? _engine.State.HomeRosterId : _engine.State.AwayRosterId;
        var roster = RosterStore.Book.Find(rosterId);
        var previous = (PlayerBox.SelectedItem as PlayerChoice)?.Id;
        var choices = (roster?.Players ?? []).Select(player => new PlayerChoice(player.Id, player.Record)).ToList();
        PlayerBox.ItemsSource = choices;
        if (previous is not null) PlayerBox.SelectedItem = choices.FirstOrDefault(choice => choice.Id == previous);
        if (PlayerBox.SelectedItem is null && choices.Count > 0) PlayerBox.SelectedIndex = 0;
        var statName = StatBox.Text;
        var names = roster?.StatNames() ?? [];
        StatBox.ItemsSource = names;
        if (!string.IsNullOrWhiteSpace(statName)) StatBox.Text = statName;
        PlayerBox.IsEnabled = choices.Count > 0;
        StatBox.IsEnabled = names.Count > 0;
    }

    /// <summary>
    /// Every button the current sport's console can press, so a macro is not limited to the
    /// handful of fields that happen to have a dedicated step editor.
    /// </summary>
    void RefreshCommandChoices()
    {
        var selected = (CommandBox.SelectedItem as CommandChoice)?.Id;
        var choices = ControlActions.ForSport(_engine.State.SportId)
            // A .set action opens a dialog on the console; a macro must never stop halfway through
            // waiting for a window. Exact values have their own step kinds instead.
            .Where(action => !action.Id.EndsWith(".set", StringComparison.OrdinalIgnoreCase))
            .Select(action => new CommandChoice(action.Id, $"{action.Group} · {action.Label}"))
            .ToList();
        CommandBox.ItemsSource = choices;
        var match = choices.FirstOrDefault(choice => string.Equals(choice.Id, selected, StringComparison.OrdinalIgnoreCase));
        CommandBox.SelectedItem = match ?? choices.FirstOrDefault();
    }

    void RefreshCustomValueChoices()
    {
        var selected = (CustomValueBox.SelectedItem as CustomValueChoice)?.Id;
        var choices = _engine.State.Stats
            .Select(stat => new CustomValueChoice(stat.Id, stat.Name))
            .ToList();
        CustomValueBox.ItemsSource = choices;
        var match = choices.FirstOrDefault(choice => string.Equals(choice.Id, selected, StringComparison.OrdinalIgnoreCase));
        CustomValueBox.SelectedItem = match ?? choices.FirstOrDefault();
    }

    void RefreshSportValueChoices()
    {
        var selected = (SportValueBox.SelectedItem as SportValueChoice)?.Id;
        var choices = _engine.State.Controls
            .Select(control => new SportValueChoice(control.Id, control.Label, control.Kind == SportControlKind.Flag))
            .ToList();
        SportValueBox.ItemsSource = choices;
        var match = choices.FirstOrDefault(choice => string.Equals(choice.Id, selected, StringComparison.OrdinalIgnoreCase));
        SportValueBox.SelectedItem = match ?? choices.FirstOrDefault();
    }

    void RunMacro_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SavedMacro macro }) return;
        if (_draft is not null && _draft.Id == macro.Id && _dirty) SaveDraft(false);
        var current = _macros.FirstOrDefault(item => item.Id == macro.Id);
        if (current is null) return;
        var message = _runner.Run(current);
        EditStatus.Text = "RAN";
        Status?.Invoke(message);
    }

    void EditMacroCard(SavedMacro macro)
    {
        if (_draft is not null && _draft.Id == macro.Id) return;
        SaveDraft(false);
        _draft = macro.Copy();
        _dirty = false;
        LoadEditor();
    }

    void DeleteMacroCard(SavedMacro macro)
    {
        if (_draft?.Id == macro.Id) { _draft = null; _dirty = false; }
        _macros.RemoveAll(item => item.Id == macro.Id);
        _engine.StoreMacros(_macros);
        if (_draft is null) _draft = _macros.FirstOrDefault()?.Copy();
        RenderMacros();
        LoadEditor();
        Status?.Invoke($"MACRO · {macro.Label} deleted");
    }

    /// <summary>Right-click menu on a card: the edit and delete actions no longer take a row.</summary>
    void AttachMacroMenu(FrameworkElement element, SavedMacro macro)
    {
        var menu = new ContextMenu
        {
            Background = (Brush)Resources["MacroSurfaceBrush"],
            Foreground = (Brush)Resources["MacroTextBrush"],
            BorderBrush = (Brush)Resources["MacroBorderBrush"]
        };
        var edit = new MenuItem { Header = "Edit macro", Foreground = (Brush)Resources["MacroTextBrush"] };
        edit.Click += (_, _) => EditMacroCard(macro);
        var delete = new MenuItem { Header = "Delete macro", Foreground = (Brush)Resources["MacroTextBrush"] };
        delete.Click += (_, _) => DeleteMacroCard(macro);
        menu.Items.Add(edit);
        menu.Items.Add(delete);
        element.ContextMenu = menu;
        // Open it explicitly as well as relying on ContextMenuService. A captured mouse from a
        // macro card can keep the service from seeing the right-click, which made Edit / Delete
        // appear to be missing.
        element.PreviewMouseRightButtonDown += (_, e) =>
        {
            menu.PlacementTarget = element;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        };
    }

    // ----- macro board ordering: drag the header onto another card to move it there -----

    void MacroHeader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is not FrameworkElement { Tag: SavedMacro macro } handle) return;
        if (e.ClickCount >= 2)
        {
            handle.ReleaseMouseCapture();
            _dragMacro = null;
            EditMacroCard(macro);
            e.Handled = true;
            return;
        }
        _dragMacro = macro;
        _dragOrigin = e.GetPosition(this);
        handle.CaptureMouse();
        e.Handled = true;
    }

    bool IsDragDistance(Point position) =>
        Math.Abs(position.X - _dragOrigin.X) >= MacroDragThreshold
        || Math.Abs(position.Y - _dragOrigin.Y) >= MacroDragThreshold;

    void MacroHeader_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragMacro is null) return;
        var macro = _dragMacro;
        _dragMacro = null;
        if (sender is UIElement handle) handle.ReleaseMouseCapture();
        // A click (as opposed to a drag, which clears _dragMacro at the dock level) selects this
        // card for editing. It also means the operator never has to discover the header's
        // double-click gesture before they can change a macro.
        if (e.ChangedButton == MouseButton.Left) EditMacroCard(macro);
    }

    /// <summary>
    /// Last-resort drag start for the whole dock. The header and card already start a drag, but a
    /// button inside the card can capture the mouse before the card sees the movement; this preview
    /// handler sits above every one of those controls and cannot be swallowed by them.
    /// </summary>
    void MacroDock_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragMacro is null || e.LeftButton != MouseButtonState.Pressed) return;
        if (!IsDragDistance(e.GetPosition(this))) return;
        if (_dragging) return;
        // Track the drag with a plain mouse capture instead of the OLE drag loop. Cards live in a
        // ScrollViewer and contain a Button, and the OLE loop was unreliable there: a drag could
        // start and then end with no drop. Capturing on the board makes the release deterministic.
        _dragging = true;
        if (Mouse.Captured is UIElement captured) captured.ReleaseMouseCapture();
        CaptureMouse();
    }

    void MacroDock_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging || _dragMacro is null) return;
        var macro = _dragMacro;
        var (target, inBoard) = MacroDropAt(e.GetPosition(this));
        _dragging = false;
        _dragMacro = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (target is not null) MoveMacro(macro.Id, target.Id);
        else if (inBoard) MoveMacro(macro.Id, null);
        e.Handled = true;
    }

    (SavedMacro? Target, bool InBoard) MacroDropAt(Point point)
    {
        var inBoard = false;
        for (var node = InputHitTest(point) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, MacroButtons)) inBoard = true;
            if (node is FrameworkElement { Tag: SavedMacro target }) return (target, true);
        }
        return (null, inBoard);
    }

    void NewMacro_Click(object sender, RoutedEventArgs e)
    {
        SaveDraft(false);
        var macro = new SavedMacro { Label = $"Macro {_macros.Count + 1}" };
        _macros.Add(macro);
        _engine.StoreMacros(_macros);
        _draft = macro.Copy();
        _dirty = false;
        RenderMacros();
        LoadEditor();
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null) return;
        _macros.RemoveAll(item => item.Id == _draft.Id);
        _engine.StoreMacros(_macros);
        _draft = _macros.FirstOrDefault()?.Copy();
        _dirty = false;
        RenderMacros();
        LoadEditor();
    }

    void Save_Click(object sender, RoutedEventArgs e) => SaveDraft(true);

    void SaveDraft(bool announce)
    {
        if (_draft is null) return;
        _draft.Label = string.IsNullOrWhiteSpace(NameBox.Text) ? "Macro" : NameBox.Text.Trim();
        var existing = _macros.FirstOrDefault(item => item.Id == _draft.Id);
        if (existing is null) _macros.Add(_draft.Copy());
        else
        {
            var index = _macros.IndexOf(existing);
            _macros[index] = _draft.Copy();
        }
        _engine.StoreMacros(_macros);
        _dirty = false;
        if (NameBox.Text != _draft.Label)
        {
            _syncing = true;
            try { NameBox.Text = _draft.Label; }
            finally { _syncing = false; }
        }
        if (ColorBox.Text != _draft.Color)
        {
            _syncing = true;
            try { ColorBox.Text = _draft.Color; }
            finally { _syncing = false; }
        }
        RenderMacros();
        UpdateColorPreview();
        UpdateColorChoiceHighlight();
        EditStatus.Text = "SAVED";
        if (announce) Status?.Invoke($"MACRO · {_draft.Label} saved");
    }

    void Name_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _draft is null) return;
        _draft.Label = NameBox.Text;
        _dirty = true;
        EditStatus.Text = "UNSAVED";
    }

    void BuildColorChoices()
    {
        ColorChoices.Children.Clear();
        AddColorChoice("", "Theme accent", 54);
        foreach (var color in MacroColors) AddColorChoice(color, color, 26);
    }

    void AddColorChoice(string value, string tip, double width)
    {
        var swatch = new Button
        {
            Width = width,
            Height = 26,
            MinHeight = 26,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 4),
            Tag = value,
            ToolTip = tip
        };
        if (value.Length == 0)
        {
            swatch.Content = "THEME";
            swatch.FontSize = 9;
            swatch.Style = (Style)Resources["MacroAction"];
        }
        else if (ParseColor(value) is Color color)
        {
            swatch.Background = ColorBrush(color);
            swatch.BorderBrush = (Brush)Resources["MacroBorderBrush"];
        }
        swatch.Click += ColorChoice_Click;
        ColorChoices.Children.Add(swatch);
    }

    void ColorChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color }) SetMacroColor(color);
    }

    void Color_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _draft is null) return;
        var text = ColorBox.Text.Trim();
        if (text.Length == 0)
        {
            _draft.Color = "";
            _dirty = true;
            EditStatus.Text = "UNSAVED";
            UpdateColorPreview();
            UpdateColorChoiceHighlight();
            return;
        }
        if (!TryParseColor(text, out var color))
        {
            EditStatus.Text = "COLOR?";
            UpdateColorPreview();
            return;
        }
        _draft.Color = NormalizeColor(color);
        _dirty = true;
        EditStatus.Text = "UNSAVED";
        UpdateColorPreview();
        UpdateColorChoiceHighlight();
    }

    void Color_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_draft is null) return;
        _syncing = true;
        try { ColorBox.Text = _draft.Color; }
        finally { _syncing = false; }
        UpdateColorPreview();
        UpdateColorChoiceHighlight();
    }

    void SetMacroColor(string value)
    {
        if (_draft is null) return;
        if (value.Length == 0) _draft.Color = "";
        else if (ParseColor(value) is Color chosen) _draft.Color = NormalizeColor(chosen);
        else return;
        _dirty = true;
        _syncing = true;
        try { ColorBox.Text = _draft.Color; }
        finally { _syncing = false; }
        EditStatus.Text = "UNSAVED";
        UpdateColorPreview();
        UpdateColorChoiceHighlight();
    }

    void UpdateColorPreview()
    {
        var color = _draft is not null && ParseColor(_draft.Color) is Color custom
            ? custom
            : (Resources["MacroAccentSoftBrush"] as SolidColorBrush)?.Color ?? Color.FromRgb(0x12, 0x3C, 0x33);
        ColorPreview.Background = ColorBrush(color);
        ColorPreview.ToolTip = _draft is not null && ParseColor(_draft.Color) is not null
            ? _draft.Color
            : "Theme accent";
    }

    void UpdateColorChoiceHighlight()
    {
        var selected = _draft?.Color ?? "";
        foreach (var swatch in ColorChoices.Children.OfType<Button>())
        {
            var isSelected = string.Equals(swatch.Tag as string ?? "", selected, StringComparison.OrdinalIgnoreCase);
            swatch.BorderThickness = new Thickness(isSelected ? 3 : 1);
            swatch.BorderBrush = isSelected
                ? (Brush)Resources["MacroTextBrush"]
                : (Brush)Resources["MacroBorderBrush"];
        }
    }

    static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text) || text[0] != '#') return false;
        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(text)!;
            color = Color.FromRgb(parsed.R, parsed.G, parsed.B);
            return true;
        }
        catch { return false; }
    }

    static Color? ParseColor(string? text) => TryParseColor(text, out var color) ? color : null;

    static string NormalizeColor(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    static Brush ColorBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    void Kind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing) UpdateFields();
    }

    void Side_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && SelectedKind == MacroActionKind.RosterStat) SyncRoster();
    }

    void BrowseVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a video for this macro step",
            Filter = "Video clips|*.mp4;*.mov;*.wmv;*.avi;*.mkv;*.webm;*.m4v|All files|*.*"
        };
        if (dialog.ShowDialog() == true) VideoPathBox.Text = dialog.FileName;
    }

    MacroAction ReadActionFromFields()
    {
        var kind = SelectedKind;
        var command = CommandBox.SelectedItem as CommandChoice;
        var sportValue = SportValueBox.SelectedItem as SportValueChoice;
        var customValue = CustomValueBox.SelectedItem as CustomValueChoice;
        return new MacroAction
        {
            Kind = kind,
            Home = SideBox.SelectedIndex != 1,
            Amount = int.TryParse(AmountBox.Text, out var amount) ? amount : 1,
            Value = int.TryParse(ValueBox.Text, out var value) ? value : 0,
            Path = kind switch
            {
                MacroActionKind.PlayVideo => VideoPathBox.Text.Trim(),
                MacroActionKind.Control => command?.Id ?? "",
                MacroActionKind.SportValue => sportValue?.Id ?? "",
                MacroActionKind.CustomValue => customValue?.Id ?? "",
                _ => ""
            },
            Label = kind == MacroActionKind.Control ? command?.Label ?? ""
                : kind == MacroActionKind.SportValue ? sportValue?.Label ?? ""
                : kind == MacroActionKind.CustomValue ? customValue?.Label ?? ""
                : "",
            PlayerId = (PlayerBox.SelectedItem as PlayerChoice)?.Id ?? "",
            PlayerName = (PlayerBox.SelectedItem as PlayerChoice)?.Label ?? "",
            StatName = StatBox.Text.Trim(),
            Delta = kind == MacroActionKind.SportValue
                ? SportDeltaBox.IsChecked == true
                : kind == MacroActionKind.CustomValue
                    ? CustomDeltaBox.IsChecked == true
                    : DeltaBox.IsChecked == true
        };
    }

    void AddStep_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null) return;
        _draft.Actions.Add(ReadActionFromFields());
        _dirty = true;
        StepList.Items.Refresh();
        StepList.SelectedIndex = _draft.Actions.Count - 1;
        EditStatus.Text = "UNSAVED";
    }

    void UpdateStep_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null || StepList.SelectedItem is not MacroAction action) return;
        var replacement = ReadActionFromFields();
        action.Kind = replacement.Kind;
        action.Home = replacement.Home;
        action.Amount = replacement.Amount;
        action.Value = replacement.Value;
        action.Path = replacement.Path;
        action.Label = replacement.Label;
        action.PlayerId = replacement.PlayerId;
        action.PlayerName = replacement.PlayerName;
        action.StatName = replacement.StatName;
        action.Delta = replacement.Delta;
        _dirty = true;
        StepList.Items.Refresh();
        EditStatus.Text = "UNSAVED";
    }

    void Step_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || StepList.SelectedItem is not MacroAction action) return;
        _syncing = true;
        try
        {
            KindBox.SelectedItem = KindBox.Items.OfType<KindChoice>().FirstOrDefault(choice => choice.Kind == action.Kind);
            SideBox.SelectedIndex = action.Home ? 0 : 1;
            AmountBox.Text = action.Amount.ToString();
            ValueBox.Text = action.Value.ToString();
            VideoPathBox.Text = action.Path;
            DeltaBox.IsChecked = action.Delta;
            if (action.Kind == MacroActionKind.Control)
            {
                RefreshCommandChoices();
                CommandBox.SelectedItem = CommandBox.Items.OfType<CommandChoice>()
                    .FirstOrDefault(choice => string.Equals(choice.Id, action.Path, StringComparison.OrdinalIgnoreCase));
            }
            if (action.Kind == MacroActionKind.SportValue)
            {
                RefreshSportValueChoices();
                SportValueBox.SelectedItem = SportValueBox.Items.OfType<SportValueChoice>()
                    .FirstOrDefault(choice => string.Equals(choice.Id, action.Path, StringComparison.OrdinalIgnoreCase));
                SportDeltaBox.IsChecked = action.Delta;
            }
            if (action.Kind == MacroActionKind.CustomValue)
            {
                RefreshCustomValueChoices();
                CustomValueBox.SelectedItem = CustomValueBox.Items.OfType<CustomValueChoice>()
                    .FirstOrDefault(choice => string.Equals(choice.Id, action.Path, StringComparison.OrdinalIgnoreCase));
                CustomDeltaBox.IsChecked = action.Delta;
            }
            if (action.Kind == MacroActionKind.RosterStat)
            {
                SyncRoster();
                PlayerBox.SelectedItem = PlayerBox.Items.OfType<PlayerChoice>().FirstOrDefault(choice => choice.Id == action.PlayerId);
                StatBox.Text = action.StatName;
            }
            UpdateFields();
        }
        finally { _syncing = false; }
    }

    void StepUp_Click(object sender, RoutedEventArgs e) => MoveStep(-1);

    void StepDown_Click(object sender, RoutedEventArgs e) => MoveStep(1);

    void MoveStep(int delta)
    {
        if (_draft is null || StepList.SelectedItem is not MacroAction action) return;
        var index = _draft.Actions.IndexOf(action);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _draft.Actions.Count) return;
        _draft.Actions.RemoveAt(index);
        _draft.Actions.Insert(target, action);
        _dirty = true;
        StepList.Items.Refresh();
        StepList.SelectedIndex = target;
        EditStatus.Text = "UNSAVED";
    }

        void StepRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null || StepList.SelectedItem is not MacroAction action) return;
        var index = _draft.Actions.IndexOf(action);
        if (index < 0) return;
        _draft.Actions.RemoveAt(index);
        _dirty = true;
        StepList.Items.Refresh();
        if (_draft.Actions.Count > 0) StepList.SelectedIndex = Math.Min(index, _draft.Actions.Count - 1);
        EditStatus.Text = "UNSAVED";
    }

    // ----- macro board ordering: drag a card onto another card to move it there -----

    void MacroCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is Border { Tag: SavedMacro macro })
        {
            _dragMacro = macro;
            _dragOrigin = e.GetPosition(this);
        }
    }

    void MacroCard_MouseUp(object sender, MouseButtonEventArgs e) => _dragMacro = null;

    void MoveMacro(string id, string? beforeId)
    {
        var from = _macros.FindIndex(macro => macro.Id == id);
        if (from < 0) return;
        var target = beforeId is null ? _macros.Count : _macros.FindIndex(macro => macro.Id == beforeId);
        if (target < 0 || target == from) return;
        var moved = _macros[from];
        _macros.RemoveAt(from);
        // A drop uses the target's original slot: dragging the first card onto the second puts it
        // in the second position, while dragging the second onto the first puts it before it.
        target = Math.Clamp(target, 0, _macros.Count);
        _macros.Insert(target, moved);
        _engine.StoreMacros(_macros);
        RenderMacros();
        Status?.Invoke($"MACRO · {moved.Label} moved");
    }
}
