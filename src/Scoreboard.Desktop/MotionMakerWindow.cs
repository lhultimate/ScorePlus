using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// The transition builder and the animation maker. One editor, two doors: a transition keys how a
/// graphic leaves and arrives (with colour keys that travel through the hand-over), while an
/// animation keys motion a value follows — a loop while it is up, or a flourish when it changes.
/// Nothing is applied from here: saving puts the named clip into the show's library, and the
/// studio's lists then offer it on any element or layout.
/// </summary>
public sealed class MotionMakerWindow : Window
{
    readonly MotionKind _kind;
    readonly ScorebugCatalog _catalog;
    readonly GameEngine _engine;
    readonly string _theme;
    MotionClip _clip;

    readonly ListBox _phaseList = new() { Height = 74 };
    readonly ListBox _trackList = new() { Height = 96 };
    readonly ListBox _keyList = new() { Height = 84 };
    readonly ListBox _colorList = new() { Height = 92 };
    readonly ListBox _libraryList = new() { Height = 132 };
    readonly ComboBox _propertyPicker = new() { MinHeight = 28, Width = 176 };
    readonly ComboBox _easePicker = new() { MinHeight = 28, Width = 110 };
    readonly ComboBox _colorRolePicker = new() { MinHeight = 28, Width = 106 };
    readonly ComboBox _phaseTriggerSide = new() { MinHeight = 28, Width = 108 };
    readonly TextBox _phaseTriggerAmount = Field("0");
    readonly ComboBox _pieceKindPicker = new() { MinHeight = 28, Width = 118 };
    readonly ComboBox _pieceLogoSide = new() { MinHeight = 28, Width = 118 };
    readonly ComboBox _pieceShapePicker = new() { MinHeight = 28, Width = 118 };
    readonly TextBox _pieceTriggerAmount = Field("0");
    readonly ComboBox _pieceTriggerSide = new() { MinHeight = 28, Width = 108 };
    readonly TextBox _staggerBox = Field("0.08");
    readonly TextBlock _timelineSummary = new() { FontSize = 10, Opacity = 0.68, TextWrapping = TextWrapping.Wrap };
    readonly ComboBox _presetPicker = new() { MinHeight = 28 };
    readonly ListBox _presetList = new() { Height = 260, Margin = new Thickness(0, 4, 0, 4) };
    readonly TextBlock _presetHint = new() { FontSize = 10, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
    readonly Slider _easyExit = new() { Minimum = 0.05, Maximum = 10, IsMoveToPointEnabled = true, Margin = new Thickness(0, 2, 0, 2) };
    readonly Slider _easyEnter = new() { Minimum = 0.05, Maximum = 10, IsMoveToPointEnabled = true, Margin = new Thickness(0, 2, 0, 2) };
    readonly Slider _easyDuration = new() { Minimum = 0.05, Maximum = 10, IsMoveToPointEnabled = true, Margin = new Thickness(0, 2, 0, 2) };
    readonly TextBlock _easyExitValue = new() { FontSize = 11, MinWidth = 48, TextAlignment = TextAlignment.Right };
    readonly TextBlock _easyEnterValue = new() { FontSize = 11, MinWidth = 48, TextAlignment = TextAlignment.Right };
    readonly TextBlock _easyDurationValue = new() { FontSize = 11, MinWidth = 48, TextAlignment = TextAlignment.Right };
    readonly TextBlock _easySummary = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = 0.78, Margin = new Thickness(0, 10, 0, 4) };
    readonly Expander _advancedExpander = new() { IsExpanded = false, Margin = new Thickness(0, 12, 0, 0) };
    bool _easySync;
    readonly ComboBox _exitLayoutPicker = new() { MinHeight = 28 };
    readonly ComboBox _enterLayoutPicker = new() { MinHeight = 28 };
    readonly ComboBox _motionLayoutPicker = new() { MinHeight = 28 };
    readonly TextBox _nameBox = Field();
    readonly TextBox _phaseName = Field();
    readonly TextBox _phaseSeconds = Field("0.3");
    readonly TextBox _keyTime = Field("0");
    readonly TextBox _keyValue = Field("0");
    readonly TextBox _colorTime = Field("0");
    readonly TextBox _colorValue = Field("#FFFFFFFF");
    readonly CheckBox _phaseOn = new() { Content = "Enabled", IsChecked = true, Margin = new Thickness(6, 0, 0, 0) };
    readonly CheckBox _loopBox = new() { Content = "Loop while the graphic is up", Margin = new Thickness(0, 2, 0, 2) };
    readonly CheckBox _pingBox = new() { Content = "Ping-pong instead of restarting", Margin = new Thickness(0, 2, 0, 2) };
    readonly CheckBox _valueBox = new() { Content = "Play when the value changes", Margin = new Thickness(0, 2, 0, 2) };
    readonly TextBlock _status = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    readonly ListBox _pieceList = new() { Height = 112 };
    readonly TextBlock _pieceHint = new() { FontSize = 10, Opacity = 0.68, TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _pieceEditor = new();
    readonly TextBox _pieceName = Field();
    readonly TextBox _pieceText = Field();
    readonly TextBox _pieceWidth = Field("240");
    readonly TextBox _pieceHeight = Field("90");
    readonly TextBox _pieceX = Field("0");
    readonly TextBox _pieceY = Field("0");
    readonly TextBox _pieceFill = Field("#FF0B6B50");
    readonly TextBox _pieceTextColor = Field("#FFFFFFFF");
    readonly TextBlock _timelinePiece = new() { FontSize = 10, FontWeight = FontWeights.Bold, Opacity = 0.85, Margin = new Thickness(0, 0, 0, 2) };
    string _pieceId = "";
    bool _pieceLoading;
    readonly Canvas _timeline = new() { Width = 560, Height = 104, Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x14)) };
    readonly Border _stage = new() { Height = 226, CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x14)) };
    readonly Grid _previewGrid = new() { ClipToBounds = true };
    readonly Border _plate;
    readonly TextBlock _plateText;
    readonly Viewbox _stageView;
    readonly Dictionary<string, FrameworkElement> _surfaces = [];
    readonly Line _playhead = new() { Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)), StrokeThickness = 2, Y1 = 0, Y2 = 104, IsHitTestVisible = false };
    readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(30) };
    readonly Stopwatch _clock = new();

    int _phaseIndex;
    int _trackIndex = -1;
    int _keyIndex = -1;
    int _colorIndex = -1;

    // Timeline dragging: the playhead scrubs the selected phase, and a key dot can be dragged to
    // retime it. Both use one capture on the ruler.
    PreviewTarget? _shown;
    TransitionPreview? _transitionPreview;
    readonly DispatcherTimer _previewTimer = new();
    readonly List<DispatcherTimer> _previewStaggers = [];
    Action? _previewNext;
    bool _scrubbing;
    double _scrubTime;
    MotionTrack? _dragTrack;
    MotionKey? _dragKey;
    Ellipse? _dragDot;
    ColorTrack? _dragColorTrack;
    ColorKey? _dragColorKey;
    Polygon? _dragDiamond;

    // Dragging an item's name reorders the layout timeline. The id survives redraws, and the
    // insert line is derived from the pointer so a drop always lands exactly where it is shown.
    string? _timelineDragId;
    int _timelineDragTarget = -1;
    double _timelineDragStartY;
    bool _timelineDragMoved;

    public MotionMakerWindow(MotionKind kind, ScorebugCatalog catalog, GameEngine engine, string theme, MotionClip? start = null, string? layoutId = null)
    {
        _kind = kind;
        _catalog = catalog;
        _engine = engine;
        _theme = theme;
        _clip = start?.Snapshot() ?? (kind == MotionKind.Transition ? MotionClip.NewTransition() : MotionClip.NewAnimation());
        // The maker edits the layout it was opened from. A clip that travelled with a copied layout
        // can still name the old one, which would list the wrong items for the whole session.
        if (!string.IsNullOrWhiteSpace(layoutId))
        {
            RepairClipLayout(layoutId);
            _clip.TimelineLayoutId = layoutId;
        }

        Title = kind == MotionKind.Transition ? "ScorePlus — Transition Builder" : "ScorePlus — Animation Maker";
        // Fit the working area: the window is a working surface, not something to drag around a
        // desktop it does not fit on.
        var work = SystemParameters.WorkArea;
        Width = Math.Min(1240, Math.Max(900, work.Width - 40));
        Height = Math.Min(800, Math.Max(600, work.Height - 40));
        MinWidth = Math.Min(980, Width);
        MinHeight = Math.Min(640, Height);
        MaxWidth = work.Width;
        MaxHeight = work.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");
        ScreenFit.Track(this);

        _plateText = new TextBlock
        {
            Text = kind == MotionKind.Transition ? "LOWER THIRD" : "21",
            FontSize = 54,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _plate = new Border
        {
            Width = 420,
            Height = 150,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xD7, 0xA3)),
            BorderThickness = new Thickness(2),
            Child = _plateText
        };
        _plate.RenderTransformOrigin = new Point(0.5, 0.5);
        _previewGrid.Children.Add(_plate);
        _previewGrid.Width = 420;
        _previewGrid.Height = 150;
        _stageView = new Viewbox { Child = _previewGrid, Stretch = Stretch.Uniform };

        Content = BuildTimelineWorkspace();
        WireEasyControls();
        // Enter in any maker field settles the typing and lets go of the box; it must not leave a
        // value box live, where the next keystroke would silently edit what was just entered.
        foreach (var box in new[]
        {
            _nameBox, _phaseName, _phaseSeconds, _keyTime, _keyValue, _colorTime, _colorValue,
            _pieceName, _pieceText, _pieceWidth, _pieceHeight, _pieceX, _pieceY, _pieceFill, _pieceTextColor
        })
        {
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter || box.AcceptsReturn) return;
                e.Handled = true;
                // Enter commits the field the operator just typed in. Without this, changing a
                // key's time or value and pressing Enter silently discarded the edit.
                if ((box == _keyTime || box == _keyValue) && SelectedKey is not null) UpdateKey();
                else if ((box == _colorTime || box == _colorValue) && SelectedColorRow is not null) UpdateColor();
                Keyboard.ClearFocus();
            };
        }
        ThemeManager.Apply(this, theme);
        Loaded += (_, _) =>
        {
            RefreshAll();
            RefreshLayoutChoices();
            BuildStage();
            DrawTimeline();
            if (start is null)
                _status.Text = "New here? Press Quick start in the top-right, or pick a style on the left. The three numbered steps are all you need.";
        };
        _ticker.Tick += (_, _) => TrackPlayhead();
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            var next = _previewNext;
            _previewNext = null;
            next?.Invoke();
        };
        _catalog.Changed += LibraryChanged;
        Closed += (_, _) =>
        {
            _ticker.Stop();
            _catalog.Changed -= LibraryChanged;
            StopPlayback();
        };
    }

    /// <summary>
    /// A layout copy made before clips were remapped keeps the source layout's keyframe ids. When
    /// that source still exists, re-home each track by the element order the copy preserved, so the
    /// keys show on the layout the operator actually opened instead of silently vanishing.
    /// </summary>
    void RepairClipLayout(string layoutId)
    {
        var target = _catalog.Displays.SelectMany(display => display.Layouts)
            .FirstOrDefault(layout => string.Equals(layout.Id, layoutId, StringComparison.OrdinalIgnoreCase));
        if (target is null) return;
        var sourceId = _clip.TimelineLayoutId;
        if (string.IsNullOrWhiteSpace(sourceId)
            || string.Equals(sourceId, target.Id, StringComparison.OrdinalIgnoreCase)) return;
        var source = _catalog.Displays.SelectMany(display => display.Layouts)
            .FirstOrDefault(layout => string.Equals(layout.Id, sourceId, StringComparison.OrdinalIgnoreCase));
        if (source is null || source.Elements.Count == 0) return;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < source.Elements.Count && i < target.Elements.Count; i++)
        {
            var from = source.Elements[i];
            var to = target.Elements[i];
            // A duplicate keeps its source's names and order. Requiring both avoids moving keys onto
            // unrelated artwork when the old layout was edited after the copy was made.
            if (string.Equals((from.Name ?? "").Trim(), (to.Name ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                map[from.Id] = to.Id;
        }
        if (map.Count == 0) return;
        string Map(string? id) => id is { Length: > 0 } key && map.TryGetValue(key, out var fresh) ? fresh : id ?? "";
        foreach (var phase in _clip.Phases)
        {
            foreach (var track in phase.Tracks) track.PieceId = Map(track.PieceId);
            foreach (var track in phase.Colors) track.PieceId = Map(track.PieceId);
        }
        _clip.PieceOrder = _clip.PieceOrder.Select(Map).ToList();
    }

    // ---------- layout ----------

    bool _timelineLoading;

    /// <summary>
    /// The rewritten builder: a layout-wide timeline in the centre, every real and temporary item
    /// as a track, and a focused inspector on the right. The old step-by-step layout is kept only
    /// as a fallback for older callers; this is the one the studio opens.
    /// </summary>
    UIElement BuildTimelineWorkspace()
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(ActionButton("How it works", ShowHelp));
        actions.Children.Add(ActionButton("Save clip", SaveToLibrary));
        actions.Children.Add(ActionButton("Import " + Extension(), Import));
        actions.Children.Add(ActionButton("Export " + Extension(), Export));
        actions.Children.Add(ActionButton("Close", Close));
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        var title = new StackPanel();
        title.Children.Add(new TextBlock
        {
            Text = _kind == MotionKind.Transition ? "TRANSITION TIMELINE" : "ANIMATION TIMELINE",
            FontSize = 20,
            FontWeight = FontWeights.Bold
        });
        title.Children.Add(new TextBlock
        {
            Text = _kind == MotionKind.Transition
                ? "Every item in the layout is a track. Order them, keyframe any visual property, and add temporary text, logos and shapes that vanish when the hand-over ends."
                : "Every item in the layout is a track. Move one item independently, or add a temporary pop-up that only appears for the score that was actually pressed.",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 850
        });
        header.Children.Add(title);
        root.Children.Add(header);

        var toolbar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x18, 0x24)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var bar = new WrapPanel();
        bar.Children.Add(ToolbarLabel("LAYOUT"));
        _timelineLayoutPicker.MinWidth = 240;
        _timelineLayoutPicker.MaxWidth = 360;
        _timelineLayoutPicker.SelectionChanged += (_, _) =>
        {
            if (_timelineLayoutPicker.SelectedItem is LayoutChoice choice && choice.Layout is not null)
                _clip.TimelineLayoutId = choice.Layout.Id;
            RefreshTimeline();
            RefreshPieces();
            BuildStage();
            DrawTimeline();
        };
        bar.Children.Add(_timelineLayoutPicker);
        bar.Children.Add(ToolbarLabel("  GAP"));
        _staggerBox.Width = 54;
        _staggerBox.ToolTip = "Seconds between ordered items on the timeline";
        bar.Children.Add(_staggerBox);
        bar.Children.Add(ActionButton("Apply gap", ApplyTimelineGap));
        bar.Children.Add(ActionButton("◀ Earlier", () => MoveTimeline(-1)));
        bar.Children.Add(ActionButton("Later ▶", () => MoveTimeline(1)));
        bar.Children.Add(ActionButton("🏠 Layout order", ResetTimeline));
        bar.Children.Add(ActionButton("▶  Play", Play));
        bar.Children.Add(ActionButton("■  Stop", () => { StopPlayback(); BuildStage(); }));
        toolbar.Child = bar;
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(258) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(382) });
        Grid.SetRow(body, 2);
        root.Children.Add(body);
        body.Children.Add(BuildTimelineLeftColumn());
        var centre = BuildTimelineCentre();
        Grid.SetColumn(centre, 1);
        body.Children.Add(centre);
        var inspector = BuildTimelineInspector();
        Grid.SetColumn(inspector, 2);
        body.Children.Add(inspector);

        var footer = new TextBlock
        {
            Text = "Click a track to select the item. Click or drag the ruler to scrub; double-click the ruler to add a key. Drag an item's name up or down to reorder it. A return key is just a key at the end of the phase carrying the item's designed value.",
            FontSize = 10,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        return root;
    }

    static TextBlock ToolbarLabel(string text) => new()
    {
        Text = text,
        FontSize = 9,
        FontWeight = FontWeights.Bold,
        Opacity = 0.62,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 5, 0)
    };

    UIElement BuildTimelineLeftColumn()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 10, 0) };
        var panel = new StackPanel();
        panel.Children.Add(Section("TIMELINE ITEMS"));
        panel.Children.Add(new TextBlock
        {
            Text = "Drag a row in the timeline, or use Move earlier/later, to set the order. Ordered rows start in that order, separated by the gap; rows left unordered run with the layout.",
            FontSize = 10,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });
        _timelineList.Height = 300;
        _timelineList.SelectionChanged += (_, _) => SelectTimelineItem();
        panel.Children.Add(_timelineList);
        var add = new WrapPanel { Margin = new Thickness(0, 4, 0, 2) };
        add.Children.Add(ActionButton("+ Text", () => AddTemporaryPiece(MotionPieceKind.Text)));
        add.Children.Add(ActionButton("+ Logo", () => AddTemporaryPiece(MotionPieceKind.Logo)));
        add.Children.Add(ActionButton("+ Shape", () => AddTemporaryPiece(MotionPieceKind.Shape)));
        panel.Children.Add(add);
        var order = new WrapPanel();
        order.Children.Add(ActionButton("Move earlier", () => MoveTimeline(-1)));
        order.Children.Add(ActionButton("Move later", () => MoveTimeline(1)));
        panel.Children.Add(order);

        panel.Children.Add(Section("PHASES / TAKES"));
        _phaseList.Height = 132;
        _phaseList.SelectionChanged += (_, _) =>
        {
            if (_phaseList.SelectedIndex >= 0 && _phaseList.SelectedIndex != _phaseIndex)
            {
                _phaseIndex = _phaseList.SelectedIndex;
                LoadPhase();
                BuildStage();
            }
        };
        panel.Children.Add(_phaseList);
        var phases = new WrapPanel { Margin = new Thickness(0, 4, 0, 2) };
        phases.Children.Add(ActionButton("+ Phase", AddPhase));
        phases.Children.Add(ActionButton("− Phase", RemovePhase));
        panel.Children.Add(phases);
        if (_kind == MotionKind.Animation)
        {
            var takes = new WrapPanel();
            takes.Children.Add(ActionButton("+1 take", () => AddValueVariant(1)));
            takes.Children.Add(ActionButton("+2 take", () => AddValueVariant(2)));
            takes.Children.Add(ActionButton("+3 take", () => AddValueVariant(3)));
            takes.Children.Add(ActionButton("+6 take", () => AddValueVariant(6)));
            panel.Children.Add(takes);
            panel.Children.Add(new TextBlock
            {
                Text = "A take with a trigger only plays when that score button is pressed. The untriggered phase is the fallback.",
                FontSize = 10,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        panel.Children.Add(Section(_kind == MotionKind.Transition ? "TRANSITION STYLES" : "ANIMATION STYLES"));
        _presetPicker.MinHeight = 28;
        _presetPicker.ItemsSource = _kind == MotionKind.Transition ? MotionClip.TransitionPresets : MotionClip.AnimationPresets;
        if (_presetPicker.SelectedIndex < 0) _presetPicker.SelectedIndex = 0;
        panel.Children.Add(_presetPicker);
        panel.Children.Add(ActionButton("Use style", UsePreset));
        panel.Children.Add(ActionButton("Blank clip", () =>
        {
            _clip = _kind == MotionKind.Transition ? MotionClip.NewTransition() : MotionClip.NewAnimation();
            _pieceId = "";
            _phaseIndex = 0;
            RefreshAll();
            BuildStage();
        }));
        panel.Children.Add(Section("SAVED CLIPS"));
        _libraryList.Height = 120;
        panel.Children.Add(_libraryList);
        var library = new WrapPanel();
        library.Children.Add(ActionButton("Load", LoadFromLibrary));
        library.Children.Add(ActionButton("Delete", DeleteFromLibrary));
        panel.Children.Add(library);
        scroll.Content = panel;
        return scroll;
    }

    UIElement BuildTimelineCentre()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 10, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(238) });

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var title = new StackPanel();
        title.Children.Add(Section("LAYOUT TIMELINE"));
        title.Children.Add(_timelineSummary);
        head.Children.Add(title);
        var tools = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        tools.Children.Add(ActionButton("+ Key at playhead", AddKeyAtPlayhead));
        tools.Children.Add(ActionButton("↩ Return all", AddReturnKeys));
        DockPanel.SetDock(tools, Dock.Right);
        head.Children.Add(tools);
        grid.Children.Add(head);

        var frame = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x08, 0x0D, 0x13)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x28, 0x32, 0x3F)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0),
            ClipToBounds = true
        };
        _timeline.Width = 1040;
        _timeline.Height = 330;
        _timeline.MouseLeftButtonDown += TimelineDown;
        _timeline.MouseMove += TimelineMove;
        _timeline.MouseLeftButtonUp += TimelineUp;
        _timeline.LostMouseCapture += (_, _) => EndDrag();
        frame.Child = new ScrollViewer
        {
            Content = _timeline,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(0x08, 0x0D, 0x13))
        };
        grid.Children.Add(frame);
        Grid.SetRow(frame, 1);

        var preview = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var previewHead = new DockPanel();
        previewHead.Children.Add(Section("LIVE PREVIEW"));
        var previewButtons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        previewButtons.Children.Add(ActionButton("▶  Play", Play));
        previewButtons.Children.Add(ActionButton("■  Stop", () => { StopPlayback(); BuildStage(); }));
        DockPanel.SetDock(previewButtons, Dock.Right);
        previewHead.Children.Add(previewButtons);
        preview.Children.Add(previewHead);
        _stage.Height = 184;
        preview.Children.Add(_stage);
        grid.Children.Add(preview);
        Grid.SetRow(preview, 2);
        return grid;
    }

    UIElement BuildTimelineInspector()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel();
        panel.Children.Add(Section("CLIP"));
        panel.Children.Add(FieldLabel("Name"));
        panel.Children.Add(_nameBox);
        var behaviour = new WrapPanel();
        behaviour.Children.Add(_valueBox);
        behaviour.Children.Add(_loopBox);
        behaviour.Children.Add(_pingBox);
        panel.Children.Add(behaviour);
        panel.Children.Add(new TextBlock
        {
            Text = "Item gap is set in the timeline toolbar above.",
            FontSize = 10,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(Section("PHASE / TAKE"));
        var phaseFields = new WrapPanel();
        _phaseName.Width = 112;
        _phaseSeconds.Width = 58;
        phaseFields.Children.Add(FieldLabel("Name"));
        phaseFields.Children.Add(_phaseName);
        phaseFields.Children.Add(FieldLabel("  Seconds"));
        phaseFields.Children.Add(_phaseSeconds);
        phaseFields.Children.Add(_phaseOn);
        panel.Children.Add(phaseFields);
        var triggerFields = new WrapPanel();
        _phaseTriggerAmount.Width = 54;
        triggerFields.Children.Add(FieldLabel("Only when score is"));
        triggerFields.Children.Add(_phaseTriggerAmount);
        _phaseTriggerSide.ItemsSource = SideOptions();
        _phaseTriggerSide.SelectedIndex = 0;
        triggerFields.Children.Add(_phaseTriggerSide);
        panel.Children.Add(triggerFields);
        panel.Children.Add(new TextBlock
        {
            Text = "0 = any score. A +3 take is skipped when +6 is pressed, so its temporary +3 never appears by mistake.",
            FontSize = 10,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(ActionButton("Apply phase", ApplyPhaseSettings));

        panel.Children.Add(Section("SELECTED ITEM"));
        panel.Children.Add(_pieceHint);
        BuildTimelinePieceEditor();
        panel.Children.Add(_pieceEditor);

        panel.Children.Add(Section("MOTION KEYS"));
        var trackTools = new WrapPanel();
        _propertyPicker.Width = 156;
        _propertyPicker.ItemsSource = Enum.GetValues<MotionTarget>().Select(target => new TargetOption(target)).ToArray();
        if (_propertyPicker.SelectedIndex < 0) _propertyPicker.SelectedIndex = 0;
        trackTools.Children.Add(_propertyPicker);
        trackTools.Children.Add(ActionButton("+ Track", AddTrack));
        trackTools.Children.Add(ActionButton("− Track", RemoveTrack));
        panel.Children.Add(trackTools);
        _trackList.Height = 100;
        _trackList.SelectionChanged += (_, _) => { if (_trackList.SelectedIndex >= 0) { _trackIndex = _trackList.SelectedIndex; LoadTrack(); } };
        panel.Children.Add(_trackList);
        var keyFields = new WrapPanel();
        _keyTime.Width = 54; _keyValue.Width = 64;
        keyFields.Children.Add(FieldLabel("Time")); keyFields.Children.Add(_keyTime);
        keyFields.Children.Add(FieldLabel("  Value")); keyFields.Children.Add(_keyValue);
        _easePicker.ItemsSource = Enum.GetValues<MotionEase>().Select(ease => new EaseOption(ease, EaseLabel(ease))).ToArray();
        SelectEase(MotionEase.Out);
        keyFields.Children.Add(_easePicker);
        panel.Children.Add(keyFields);
        var keyButtons = new WrapPanel();
        keyButtons.Children.Add(ActionButton("Add key", AddKey));
        keyButtons.Children.Add(ActionButton("Update key", UpdateKey));
        keyButtons.Children.Add(ActionButton("Remove key", RemoveKey));
        keyButtons.Children.Add(ActionButton("↩ Return key", AddReturnKey));
        panel.Children.Add(keyButtons);
        _keyList.Height = 82;
        _keyList.SelectionChanged += (_, _) => { if (_keyList.SelectedIndex >= 0) { _keyIndex = _keyList.SelectedIndex; LoadKey(); } };
        panel.Children.Add(_keyList);

        panel.Children.Add(Section("COLOR KEYS"));
        var colorFields = new WrapPanel();
        _colorRolePicker.ItemsSource = Enum.GetValues<MotionColorRole>().Select(role => new RoleOption(role, RoleLabel(role))).ToArray();
        SelectRole(MotionColorRole.Text);
        colorFields.Children.Add(_colorRolePicker);
        _colorTime.Width = 54; _colorValue.Width = 92;
        colorFields.Children.Add(FieldLabel("Time")); colorFields.Children.Add(_colorTime);
        colorFields.Children.Add(_colorValue);
        colorFields.Children.Add(ActionButton("Pick…", PickColor));
        panel.Children.Add(colorFields);
        var colorButtons = new WrapPanel();
        colorButtons.Children.Add(ActionButton("Add color", AddColor));
        colorButtons.Children.Add(ActionButton("Update color", UpdateColor));
        colorButtons.Children.Add(ActionButton("Remove color", RemoveColor));
        colorButtons.Children.Add(ActionButton("↩ Return color", AddReturnColor));
        panel.Children.Add(colorButtons);
        _colorList.Height = 92;
        _colorList.SelectionChanged += (_, _) => { if (_colorList.SelectedIndex >= 0) { _colorIndex = _colorList.SelectedIndex; LoadColor(); } };
        panel.Children.Add(_colorList);
        panel.Children.Add(_status);
        scroll.Content = panel;
        return scroll;
    }

    void BuildTimelinePieceEditor()
    {
        if (_pieceEditor.Children.Count > 0) return;
        _pieceName.Width = 150; _pieceText.Width = 200;
        _pieceWidth.Width = 56; _pieceHeight.Width = 56; _pieceX.Width = 56; _pieceY.Width = 56;
        _pieceFill.Width = 92; _pieceTextColor.Width = 92;
        _pieceEditor.Children.Add(FieldLabel("Name", 2));
        _pieceEditor.Children.Add(_pieceName);
        _pieceKindPicker.ItemsSource = new[] { "Text", "Logo", "Shape" };
        _pieceKindPicker.SelectedIndex = 0;
        _pieceEditor.Children.Add(FieldLabel("Kind", 4));
        _pieceEditor.Children.Add(_pieceKindPicker);
        _pieceEditor.Children.Add(FieldLabel("Text / template", 2));
        _pieceEditor.Children.Add(_pieceText);
        var geometry = new WrapPanel();
        geometry.Children.Add(FieldLabel("W")); geometry.Children.Add(_pieceWidth);
        geometry.Children.Add(FieldLabel("H")); geometry.Children.Add(_pieceHeight);
        geometry.Children.Add(FieldLabel("X")); geometry.Children.Add(_pieceX);
        geometry.Children.Add(FieldLabel("Y")); geometry.Children.Add(_pieceY);
        _pieceEditor.Children.Add(geometry);
        var paints = new WrapPanel();
        paints.Children.Add(FieldLabel("Plate")); paints.Children.Add(_pieceFill);
        paints.Children.Add(FieldLabel("Text")); paints.Children.Add(_pieceTextColor);
        _pieceEditor.Children.Add(paints);
        _pieceLogoSide.ItemsSource = new[] { "No logo side", "Home logo", "Away logo" };
        _pieceLogoSide.SelectedIndex = 0;
        _pieceEditor.Children.Add(FieldLabel("Logo side", 4));
        _pieceEditor.Children.Add(_pieceLogoSide);
        _pieceShapePicker.ItemsSource = Enum.GetValues<ScorebugShape>();
        _pieceShapePicker.SelectedIndex = 0;
        _pieceEditor.Children.Add(FieldLabel("Shape", 4));
        _pieceEditor.Children.Add(_pieceShapePicker);
        _pieceTriggerAmount.Width = 54;
        _pieceTriggerSide.ItemsSource = SideOptions();
        _pieceTriggerSide.SelectedIndex = 0;
        var triggers = new WrapPanel();
        triggers.Children.Add(FieldLabel("Only when score"));
        triggers.Children.Add(_pieceTriggerAmount);
        triggers.Children.Add(_pieceTriggerSide);
        _pieceEditor.Children.Add(triggers);
        _pieceEditor.Children.Add(ActionButton("Update selected piece", UpdateTemporaryPiece));
        _pieceEditor.Children.Add(ActionButton("Remove temporary piece", RemoveTemporaryPiece));
    }

    static SideOption[] SideOptions() =>
    [
        new(MotionScoreSide.Any, "Any side"),
        new(MotionScoreSide.Home, "Home"),
        new(MotionScoreSide.Away, "Away")
    ];

    UIElement BuildNew()
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(ActionButton("Quick start", QuickStart));
        actions.Children.Add(ActionButton("How to use", ShowHelp));
        actions.Children.Add(ActionButton("Save clip", SaveToLibrary));
        actions.Children.Add(ActionButton("Import " + Extension(), Import));
        actions.Children.Add(ActionButton("Export " + Extension(), Export));
        actions.Children.Add(ActionButton("Close", Close));
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        var title = new StackPanel();
        title.Children.Add(new TextBlock
        {
            Text = _kind == MotionKind.Transition ? "TRANSITION BUILDER" : "ANIMATION MAKER",
            FontSize = 20,
            FontWeight = FontWeights.Bold
        });
        title.Children.Add(new TextBlock
        {
            Text = _kind == MotionKind.Transition
                ? "Make a graphic leave and arrive. Pick a look, set the pace, watch it, then save it."
                : "Make a graphic move while it is up, or when its value changes. Pick a look, set the pace, watch it, then save it.",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 680
        });
        header.Children.Add(title);
        root.Children.Add(header);

        root.Children.Add(BuildStepStrip());
        Grid.SetRow((UIElement)root.Children[^1], 1);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        body.Children.Add(BuildStyleColumn());
        var adjust = BuildAdjustColumn();
        Grid.SetColumn(adjust, 1);
        body.Children.Add(adjust);
        var preview = BuildPreviewColumn();
        Grid.SetColumn(preview, 2);
        body.Children.Add(preview);

        _advancedExpander.Header = _kind == MotionKind.Transition
            ? "Advanced · exact phases, keyframes and colour keys (optional)"
            : "Advanced · exact keyframes, phases and colour keys (optional)";
        var advanced = (ScrollViewer)BuildEditorColumn();
        advanced.MaxHeight = 370;
        _advancedExpander.Content = advanced;
        Grid.SetRow(_advancedExpander, 3);
        root.Children.Add(_advancedExpander);

        return root;
    }

    UIElement BuildStepStrip()
    {
        var strip = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x18, 0x24)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var row = new WrapPanel();
        row.Children.Add(StepBadge("1", "Choose a style"));
        row.Children.Add(StepArrow());
        row.Children.Add(StepBadge("2", "Set the pace"));
        row.Children.Add(StepArrow());
        row.Children.Add(StepBadge("3", "Preview & save"));
        row.Children.Add(new TextBlock
        {
            Text = "  You only need these three steps. Open Advanced at the bottom if you want exact keyframes.",
            FontSize = 10,
            Opacity = 0.62,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        strip.Child = row;
        return strip;
    }

    static UIElement StepBadge(string number, string text)
    {
        var badge = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0xD7, 0xA3)),
            Child = new TextBlock
            {
                Text = number,
                Foreground = new SolidColorBrush(Color.FromRgb(0x09, 0x10, 0x18)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var label = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 2, 0)
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(badge);
        panel.Children.Add(label);
        return panel;
    }

    static TextBlock StepArrow() => new()
    {
        Text = "→",
        FontSize = 13,
        Opacity = 0.45,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(5, 0, 5, 0)
    };

    UIElement BuildStyleColumn()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        panel.Children.Add(StepTitle("1", "CHOOSE A STYLE", "Start with the closest look. Nothing is applied until you save."));
        FillPresetList();
        _presetList.SelectionChanged += (_, _) =>
        {
            if (_presetList.SelectedItem is ListBoxItem item && item.Tag is string name)
                _presetHint.Text = PresetDescription(_kind, name);
        };
        panel.Children.Add(_presetList);
        panel.Children.Add(_presetHint);
        panel.Children.Add(ActionButton("Use this style", UsePreset));
        panel.Children.Add(ActionButton("Start from a blank clip", () =>
        {
            _clip = _kind == MotionKind.Transition ? MotionClip.NewTransition() : MotionClip.NewAnimation();
            _pieceId = "";
            _phaseIndex = 0;
            RefreshAll();
            BuildStage();
            _status.Text = "Blank clip ready. Add exact keyframes below, or pick a style above.";
        }));

        panel.Children.Add(Section(_kind == MotionKind.Transition ? "SAVED TRANSITIONS" : "SAVED ANIMATIONS"));
        _libraryList.Height = 120;
        panel.Children.Add(_libraryList);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(ActionButton("Load", LoadFromLibrary));
        buttons.Children.Add(ActionButton("Delete", DeleteFromLibrary));
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock
        {
            Text = "Saved clips live with the show. A layout that uses one carries its own copy, so exporting can never lose it.",
            FontSize = 10,
            Opacity = 0.58,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        return panel;
    }

    UIElement BuildAdjustColumn()
    {
        var panel = new StackPanel { Margin = new Thickness(14, 0, 14, 0) };
        panel.Children.Add(StepTitle("2", "SET THE PACE", "Drag a duration and watch the preview. Short is crisp; long is dramatic."));

        if (_kind == MotionKind.Transition)
        {
            AddSliderRow(panel, "Leaving (exit)", _easyExit, _easyExitValue);
            AddSliderRow(panel, "Arriving (enter)", _easyEnter, _easyEnterValue);
        }
        else
        {
            AddSliderRow(panel, "How long the motion takes", _easyDuration, _easyDurationValue);
            panel.Children.Add(new TextBlock
            {
                Text = "OPTIONS",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Opacity = 0.6,
                Margin = new Thickness(0, 10, 0, 4)
            });
            panel.Children.Add(_loopBox);
            panel.Children.Add(_pingBox);
            panel.Children.Add(_valueBox);
        }

        panel.Children.Add(_easySummary);
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x1C, 0x26)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 8, 0, 0),
            Child = new TextBlock
            {
                Text = _kind == MotionKind.Transition
                    ? "A transition has two halves: the old graphic leaves, then the new one arrives. Both halves play in order."
                    : "A loop repeats while the graphic is up. A value-change clip plays once after the new number or name lands.",
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.68
            }
        });
        return panel;
    }

    void AddSliderRow(Panel panel, string label, Slider slider, TextBlock readout)
    {
        var head = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
        DockPanel.SetDock(readout, Dock.Right);
        head.Children.Add(readout);
        head.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(head);
        panel.Children.Add(slider);
    }

    UIElement BuildPreviewColumn()
    {
        var panel = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        panel.Children.Add(StepTitle("3", "PREVIEW & SAVE", "Play the clip on a real layout, then give it a name and save it."));
        _stage.Height = 250;
        panel.Children.Add(_stage);
        var playback = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        playback.Children.Add(ActionButton("▶  Play", Play));
        playback.Children.Add(ActionButton("■  Stop", () =>
        {
            StopPlayback();
            BuildStage();
        }));
        panel.Children.Add(playback);
        panel.Children.Add(new TextBlock
        {
            Text = "Tip: press Play, then drag either duration slider until the movement feels right.",
            FontSize = 10,
            Opacity = 0.62,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8)
        });
        panel.Children.Add(FieldLabel("Name this clip"));
        panel.Children.Add(_nameBox);
        panel.Children.Add(ActionButton("Save clip to library", SaveToLibrary));
        panel.Children.Add(_status);
        panel.Children.Add(new TextBlock
        {
            Text = _kind == MotionKind.Transition
                ? "Saving makes it available in the studio's Transition in, Value change and Motion preset lists."
                : "Saving makes it available in the studio's Motion preset and Value change lists.",
            FontSize = 10,
            Opacity = 0.58,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        return panel;
    }

    static StackPanel StepTitle(string number, string title, string detail)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0xD7, 0xA3)),
            Child = new TextBlock
            {
                Text = number,
                Foreground = new SolidColorBrush(Color.FromRgb(0x09, 0x10, 0x18)),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        head.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0)
        });
        panel.Children.Add(head);
        panel.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 10,
            Opacity = 0.62,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(29, 2, 0, 4)
        });
        return panel;
    }

    UIElement BuildLegacy()
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var fileRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        fileRow.Children.Add(ActionButton("Save clip", SaveToLibrary));
        fileRow.Children.Add(ActionButton("Import " + Extension(), Import));
        fileRow.Children.Add(ActionButton("Export " + Extension(), Export));
        fileRow.Children.Add(ActionButton("Close", Close));
        DockPanel.SetDock(fileRow, Dock.Right);
        header.Children.Add(fileRow);

        var title = new StackPanel();
        title.Children.Add(new TextBlock
        {
            Text = _kind == MotionKind.Transition ? "TRANSITION BUILDER" : "ANIMATION MAKER",
            FontSize = 20,
            FontWeight = FontWeights.Bold
        });
        title.Children.Add(new TextBlock
        {
            Text = _kind == MotionKind.Transition
                ? "Key how a graphic leaves and arrives. Save it, then pick it on any element or layout."
                : "Key motion a value follows. Save it, then pick it on any element.",
            FontSize = 11,
            Opacity = 0.7
        });
        header.Children.Add(title);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(252) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        body.Children.Add(BuildLibraryColumn());
        var editor = BuildEditorColumn();
        Grid.SetColumn(editor, 1);
        body.Children.Add(editor);

        var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        if (_kind == MotionKind.Transition)
        {
            right.Children.Add(Section("PREVIEW"));
            right.Children.Add(new TextBlock
            {
                Text = "The real layout, plus any temporary pieces. Play runs the exit and then the arrival.",
                FontSize = 10,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            right.Children.Add(_stage);
            var previewButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            previewButtons.Children.Add(ActionButton("▶  Play", Play));
            previewButtons.Children.Add(ActionButton("■  Stop", () =>
            {
                StopPlayback();
                BuildStage();
            }));
            right.Children.Add(previewButtons);
            // Every item on the layout, plus pieces that exist only for this hand-over. Selecting one
            // scopes the tracks and keys below to it, so the timeline edits that piece alone.
            right.Children.Add(Section("PIECES"));
            right.Children.Add(_pieceHint);
            _pieceList.SelectionChanged += (_, _) => SelectPiece();
            right.Children.Add(_pieceList);
            var pieceButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            pieceButtons.Children.Add(ActionButton("Add temporary piece", () => AddTemporaryPiece(MotionPieceKind.Text)));
            pieceButtons.Children.Add(ActionButton("Remove piece", RemoveTemporaryPiece));
            right.Children.Add(pieceButtons);
            _pieceName.Width = 150;
            _pieceText.Width = 150;
            _pieceWidth.Width = 58; _pieceHeight.Width = 58; _pieceX.Width = 58; _pieceY.Width = 58;
            _pieceFill.Width = 92; _pieceTextColor.Width = 92;
            _pieceEditor.Children.Add(FieldLabel("Name", 2));
            _pieceEditor.Children.Add(_pieceName);
            _pieceEditor.Children.Add(FieldLabel("Text", 2));
            _pieceEditor.Children.Add(_pieceText);
            var geometry = new WrapPanel();
            geometry.Children.Add(FieldLabel("W", 2)); geometry.Children.Add(_pieceWidth);
            geometry.Children.Add(FieldLabel("H", 2)); geometry.Children.Add(_pieceHeight);
            geometry.Children.Add(FieldLabel("X", 2)); geometry.Children.Add(_pieceX);
            geometry.Children.Add(FieldLabel("Y", 2)); geometry.Children.Add(_pieceY);
            _pieceEditor.Children.Add(geometry);
            var paints = new WrapPanel();
            paints.Children.Add(FieldLabel("Plate", 2)); paints.Children.Add(_pieceFill);
            paints.Children.Add(FieldLabel("Text color", 2)); paints.Children.Add(_pieceTextColor);
            _pieceEditor.Children.Add(paints);
            _pieceEditor.Children.Add(ActionButton("Update piece", UpdateTemporaryPiece));
            right.Children.Add(_pieceEditor);
        }
        else
        {
            _stage.Child = _stageView;
            right.Children.Add(Section("PREVIEW"));
            right.Children.Add(FieldLabel("Preview on"));
            right.Children.Add(_motionLayoutPicker);
            _motionLayoutPicker.SelectionChanged += (_, _) => BuildStage();
            right.Children.Add(_stage);
            var playback = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            playback.Children.Add(ActionButton("▶  Play", Play));
            playback.Children.Add(ActionButton("■  Stop", () =>
            {
                StopPlayback();
                BuildStage();
            }));
            right.Children.Add(playback);
        }

        right.Children.Add(Section("SAVED CLIPS"));
        right.Children.Add(new TextBlock
        {
            Text = _kind == MotionKind.Transition
                ? "Saving a clip puts it in the studio's Value change, Motion preset and Transition in lists. Nothing is applied from this window."
                : "Saving a clip puts it in the studio's Motion preset and Value change lists on every element. Nothing is applied from this window.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            Margin = new Thickness(0, 0, 0, 6)
        });
        right.Children.Add(_status);
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(rightScroll, 2);
        body.Children.Add(rightScroll);

        var footer = new TextBlock
        {
            Text = _kind == MotionKind.Transition
                ? "Keys are measured in seconds from the start of their phase. Click or drag the ruler to scrub; drag a key dot to retime it. Color keys repaint the text, plate or outline as the hand-over runs."
                : "A loop runs from its first key to its last for as long as the graphic is up. A value-change clip plays once, after the new value lands. Drag the ruler to scrub; drag a key dot to retime it.",
            FontSize = 10,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    UIElement BuildLibraryColumn()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        panel.Children.Add(Section("CLIP"));
        panel.Children.Add(FieldLabel("Name"));
        panel.Children.Add(_nameBox);
        panel.Children.Add(new TextBlock
        {
            Text = "The name is how the clip is listed in the studio. Saving adds it to the library.",
            FontSize = 10,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2)
        });
        panel.Children.Add(FieldLabel("Start from a preset", 8));
        _presetPicker.ItemsSource = _kind == MotionKind.Transition ? MotionClip.TransitionPresets : MotionClip.AnimationPresets;
        _presetPicker.SelectedIndex = 0;
        panel.Children.Add(_presetPicker);
        panel.Children.Add(ActionButton("Use this preset", UsePreset));
        panel.Children.Add(ActionButton("Start a blank clip", () =>
        {
            _clip = _kind == MotionKind.Transition ? MotionClip.NewTransition() : MotionClip.NewAnimation();
            _pieceId = "";
            _phaseIndex = 0;
            RefreshAll();
            BuildStage();
        }));

        panel.Children.Add(Section(_kind == MotionKind.Transition ? "SAVED TRANSITIONS" : "SAVED ANIMATIONS"));
        panel.Children.Add(_libraryList);
        var libraryButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        libraryButtons.Children.Add(ActionButton("Load", LoadFromLibrary));
        libraryButtons.Children.Add(ActionButton("Delete", DeleteFromLibrary));
        panel.Children.Add(libraryButtons);
        panel.Children.Add(new TextBlock
        {
            Text = "The library is saved with the show. Every clip also travels inside the layout that uses it, so an exported layout or display is never missing its motion.",
            FontSize = 10,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        return panel;
    }

    UIElement BuildEditorColumn()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(4, 0, 4, 0) };
        var panel = new StackPanel();

        if (_kind == MotionKind.Transition)
        {
            BuildTimelineSection(panel);
            BuildPiecesSection(panel);
        }

        panel.Children.Add(Section("PHASES"));
        var phaseRow = new WrapPanel();
        phaseRow.Children.Add(new TextBlock { Text = "Name", VerticalAlignment = VerticalAlignment.Center, FontSize = 10, Opacity = 0.7, Margin = new Thickness(0, 0, 4, 0) });
        _phaseName.Width = 96;
        phaseRow.Children.Add(_phaseName);
        phaseRow.Children.Add(new TextBlock { Text = "Seconds", VerticalAlignment = VerticalAlignment.Center, FontSize = 10, Opacity = 0.7, Margin = new Thickness(8, 0, 4, 0) });
        _phaseSeconds.Width = 62;
        phaseRow.Children.Add(_phaseSeconds);
        phaseRow.Children.Add(_phaseOn);
        phaseRow.Children.Add(ActionButton("Apply", ApplyPhaseSettings));
        panel.Children.Add(phaseRow);

        _phaseList.SelectionChanged += (_, _) => { if (_phaseList.SelectedIndex >= 0) { _phaseIndex = _phaseList.SelectedIndex; LoadPhase(); } };
        panel.Children.Add(_phaseList);
        var phaseButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        phaseButtons.Children.Add(ActionButton("Add phase", AddPhase));
        phaseButtons.Children.Add(ActionButton("Remove phase", RemovePhase));
        panel.Children.Add(phaseButtons);

        // The simple timing and behaviour controls live in step 2; this drawer is for exact
        // keyframes, so it stays focused once the operator deliberately opens it.
        panel.Children.Add(new TextBlock
        {
            Text = "This is the exact-key editing view. Most clips are finished in steps 1–3 above; use this only when you need a specific value or time.",
            FontSize = 10,
            Opacity = 0.62,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        panel.Children.Add(Section("TIMELINE"));
        panel.Children.Add(_timelinePiece);
        panel.Children.Add(new TextBlock
        {
            Text = "Click or drag the ruler to scrub the selected phase; double-click it to add a key. Drag a key dot to retime it.",
            FontSize = 10,
            Opacity = 0.65,
            Margin = new Thickness(0, 0, 0, 3)
        });
        _timeline.MouseLeftButtonDown += TimelineDown;
        _timeline.MouseMove += TimelineMove;
        _timeline.MouseLeftButtonUp += TimelineUp;
        _timeline.LostMouseCapture += (_, _) => EndDrag();
        panel.Children.Add(new Border
        {
            Child = new Viewbox { Child = _timeline, Stretch = Stretch.Uniform, MaxHeight = 150 },
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x28, 0x32, 0x3F)),
            Padding = new Thickness(0)
        });

        panel.Children.Add(Section("MOTION TRACKS"));
        var trackRow = new WrapPanel();
        _propertyPicker.ItemsSource = Enum.GetValues<MotionTarget>().Select(target => new TargetOption(target)).ToArray();
        _propertyPicker.SelectedIndex = 0;
        trackRow.Children.Add(_propertyPicker);
        trackRow.Children.Add(ActionButton("Add track", AddTrack));
        trackRow.Children.Add(ActionButton("Remove track", RemoveTrack));
        panel.Children.Add(trackRow);
        _trackList.SelectionChanged += (_, _) => { if (_trackList.SelectedIndex >= 0) { _trackIndex = _trackList.SelectedIndex; LoadTrack(); } };
        panel.Children.Add(_trackList);

        var keyRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        keyRow.Children.Add(new TextBlock { Text = "Time", FontSize = 10, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        _keyTime.Width = 58;
        keyRow.Children.Add(_keyTime);
        keyRow.Children.Add(new TextBlock { Text = "Value", FontSize = 10, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
        _keyValue.Width = 68;
        keyRow.Children.Add(_keyValue);
        // Labelled options, not the enum itself: "Out" beside a keyframe reads as a direction.
        _easePicker.ItemsSource = Enum.GetValues<MotionEase>()
            .Select(ease => new EaseOption(ease, EaseLabel(ease))).ToArray();
        _easePicker.ToolTip = "How the value approaches this key from the one before it";
        SelectEase(MotionEase.Out);
        keyRow.Children.Add(_easePicker);
        panel.Children.Add(keyRow);
        var keyButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        keyButtons.Children.Add(ActionButton("Add key", AddKey));
        keyButtons.Children.Add(ActionButton("Update key", UpdateKey));
        keyButtons.Children.Add(ActionButton("Remove key", RemoveKey));
        panel.Children.Add(keyButtons);
        _keyList.SelectionChanged += (_, _) => { if (_keyList.SelectedIndex >= 0) { _keyIndex = _keyList.SelectedIndex; LoadKey(); } };
        panel.Children.Add(_keyList);

        panel.Children.Add(Section("COLOR KEYS"));
        var colorRow = new WrapPanel();
        _colorRolePicker.ItemsSource = Enum.GetValues<MotionColorRole>()
            .Select(role => new RoleOption(role, RoleLabel(role))).ToArray();
        _colorRolePicker.ToolTip = "Which part of the item the colour key paints";
        SelectRole(MotionColorRole.Text);
        colorRow.Children.Add(_colorRolePicker);
        colorRow.Children.Add(new TextBlock { Text = "Time", FontSize = 10, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
        _colorTime.Width = 56;
        colorRow.Children.Add(_colorTime);
        _colorValue.Width = 96;
        colorRow.Children.Add(_colorValue);
        colorRow.Children.Add(ActionButton("Pick…", PickColor));
        panel.Children.Add(colorRow);
        var colorButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        colorButtons.Children.Add(ActionButton("Add color", AddColor));
        colorButtons.Children.Add(ActionButton("Update", UpdateColor));
        colorButtons.Children.Add(ActionButton("Remove", RemoveColor));
        panel.Children.Add(colorButtons);
        _colorList.SelectionChanged += (_, _) => { if (_colorList.SelectedIndex >= 0) { _colorIndex = _colorList.SelectedIndex; LoadColor(); } };
        panel.Children.Add(_colorList);

        scroll.Content = panel;
        return scroll;
    }

    // ---------- layout timeline ----------

    readonly ComboBox _timelineLayoutPicker = new();
    readonly ListBox _timelineList = new();
    List<TimelineEntry> _timelineItems = [];

    ScorebugLayout TimelineLayout =>
        (_timelineLayoutPicker.SelectedItem as LayoutChoice)?.Layout ?? _catalog.Layout;

    sealed record TimelineEntry(string Id, string Label, bool Temporary, MotionPiece? Piece);

    /// <summary>
    /// The timeline: pick the layout the hand-over is for, then put its items in the order they
    /// leave and arrive. It is the simplest way to make a transition feel designed rather than
    /// everything moving at once.
    /// </summary>
    void BuildTimelineSection(StackPanel panel)
    {
        panel.Children.Add(Section("TIMELINE"));
        panel.Children.Add(new TextBlock
        {
            Text = "Pick the layout this hand-over is for, then order its items. The top item moves first and the rest follow; items left out of the list still move, with the layout.",
            FontSize = 10,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });
        _timelineLayoutPicker.MinWidth = 230;
        _timelineLayoutPicker.SelectionChanged += (_, _) => RefreshTimeline();
        panel.Children.Add(_timelineLayoutPicker);
        _timelineList.Height = 124;
        _timelineList.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(_timelineList);
        var timelineButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        timelineButtons.Children.Add(ActionButton("Move up", () => MoveTimeline(-1)));
        timelineButtons.Children.Add(ActionButton("Move down", () => MoveTimeline(1)));
        timelineButtons.Children.Add(ActionButton("Layout order", ResetTimeline));
        panel.Children.Add(timelineButtons);
    }

    void RefreshTimeline()
    {
        if (_clip is null) return;
        var layout = TimelineLayout;
        if (string.IsNullOrWhiteSpace(_clip.TimelineLayoutId)) _clip.TimelineLayoutId = layout.Id;
        var ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _clip.PieceOrder ?? [])
            if (!ranks.ContainsKey(id)) ranks[id] = ranks.Count;
        var entries = new List<TimelineEntry> { new("", "Whole graphic", false, null) };
        entries.AddRange(layout.Elements.Select(item =>
            new TimelineEntry(item.Id, string.IsNullOrWhiteSpace(item.Name) ? "(unnamed item)" : item.Name, false, null)));
        entries.AddRange(_clip.Pieces.Select(piece =>
            new TimelineEntry(piece.Id, "Temporary · " + piece.Name, true, piece)));
        _timelineItems = entries
            .Select((entry, index) => (Entry: entry, Original: index))
            .OrderBy(pair => pair.Entry.Id.Length == 0 ? -1 : ranks.TryGetValue(pair.Entry.Id, out var rank) ? rank : int.MaxValue)
            .ThenBy(pair => pair.Original)
            .Select(pair => pair.Entry)
            .ToList();
        var selected = (_timelineList.SelectedItem as TimelineRow)?.Id;
        var targetId = !string.IsNullOrWhiteSpace(_pieceId) ? _pieceId : selected;
        _timelineLoading = true;
        try
        {
            _timelineList.ItemsSource = _timelineItems
                .Select((item, index) => new TimelineRow(index + 1, item.Id, item.Label, item.Temporary))
                .ToList();
            var index = _timelineItems.FindIndex(item => string.Equals(item.Id, targetId, StringComparison.OrdinalIgnoreCase));
            _timelineList.SelectedIndex = index >= 0 ? index : 0;
            _pieceId = _timelineItems.Count == 0 ? "" : _timelineItems[Math.Max(0, index)].Id;
        }
        finally { _timelineLoading = false; }
        LoadPiece();
        _timelineSummary.Text = $"{layout.Name} · {layout.Elements.Count} layout item{(layout.Elements.Count == 1 ? "" : "s")} · {_clip.Pieces.Count} temporary · {_clip.Phases.Count} phase{( _clip.Phases.Count == 1 ? "" : "s")} · {_clip.ItemStagger:0.##}s gap";
    }

    void SelectTimelineItem()
    {
        if (_timelineLoading || _timelineList.SelectedItem is not TimelineRow row) return;
        _pieceId = row.Id;
        _trackIndex = -1;
        _colorIndex = -1;
        LoadPiece();
        RefreshTracks();
        RefreshColors();
        DrawTimeline();
        BuildStage();
        _status.Text = row.Id.Length == 0
            ? "Whole-graphic keys move every item together. Pick a specific track for per-item motion."
            : $"Editing {row.Label}. Click the ruler to scrub, or double-click to add a key.";
    }

    void MoveTimeline(int delta)
    {
        if (_timelineList.SelectedItem is not TimelineRow row) return;
        var index = _timelineItems.FindIndex(item => string.Equals(item.Id, row.Id, StringComparison.OrdinalIgnoreCase));
        var target = index + delta;
        if (row.Id.Length == 0 || index < 0 || target <= 0 || target >= _timelineItems.Count) return;
        var moved = _timelineItems[index];
        _timelineItems.RemoveAt(index);
        _timelineItems.Insert(target, moved);
        _clip.PieceOrder = _timelineItems.Where(item => item.Id.Length > 0).Select(item => item.Id).ToList();
        RefreshTimeline();
        _timelineList.SelectedIndex = target;
        _pieceId = moved.Id;
        SelectTimelineItem();
        _status.Text = $"Timeline: {moved.Label} starts at slot {target + 1}.";
    }

    /// <summary>
    /// Drop a dragged row at an insert line. Row 0 is the whole graphic and never moves; every
    /// other row belongs to the editable order, and a drop at the bottom means "last".
    /// </summary>
    bool CommitTimelineDrop(string id, int insert)
    {
        var from = _timelineItems.FindIndex(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (from <= 0) return false;
        insert = Math.Clamp(insert, 1, _timelineItems.Count);
        if (insert == from || insert == from + 1) return false;
        var moved = _timelineItems[from];
        _timelineItems.RemoveAt(from);
        if (from < insert) insert--;
        insert = Math.Clamp(insert, 1, _timelineItems.Count);
        _timelineItems.Insert(insert, moved);
        _clip.PieceOrder = _timelineItems.Where(item => item.Id.Length > 0).Select(item => item.Id).ToList();
        RefreshTimeline();
        _timelineList.SelectedIndex = insert;
        _pieceId = moved.Id;
        SelectTimelineItem();
        DrawTimeline();
        _status.Text = $"Timeline: {moved.Label} now starts at slot {insert + 1}.";
        return true;
    }

    void ResetTimeline()
    {
        _clip.PieceOrder = [];
        RefreshTimeline();
        SelectTimelineItem();
        _status.Text = "Timeline cleared: every item starts together in layout order.";
    }

    void ApplyTimelineGap()
    {
        _clip.ItemStagger = Number(_staggerBox.Text, _clip.ItemStagger, 0, 1.5);
        _staggerBox.Text = _clip.ItemStagger.ToString("0.##", CultureInfo.CurrentCulture);
        DrawTimeline();
        BuildStage();
        _status.Text = $"Ordered items start {_clip.ItemStagger:0.##}s apart.";
    }

    sealed record TimelineRow(int Position, string Id, string Label, bool Temporary = false)
    {
        public override string ToString() => $"{Position}.  {Label}{(Temporary ? "  [extra]" : "")}";
    }

    void BuildPiecesSection(StackPanel panel)
    {
        panel.Children.Add(Section("PIECES"));
        panel.Children.Add(new TextBlock
        {
            Text = "Select the item you want the keyframes below to affect. Temporary pieces exist only for this transition.",
            FontSize = 10,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });
        _pieceList.SelectionChanged += (_, _) => SelectPiece();
        panel.Children.Add(_pieceHint);
        panel.Children.Add(_pieceList);
        var pieceButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        pieceButtons.Children.Add(ActionButton("Add temporary piece", () => AddTemporaryPiece(MotionPieceKind.Text)));
        pieceButtons.Children.Add(ActionButton("Remove piece", RemoveTemporaryPiece));
        panel.Children.Add(pieceButtons);

        _pieceName.Width = 150;
        _pieceText.Width = 150;
        _pieceWidth.Width = 58; _pieceHeight.Width = 58; _pieceX.Width = 58; _pieceY.Width = 58;
        _pieceFill.Width = 92; _pieceTextColor.Width = 92;
        _pieceEditor.Children.Add(FieldLabel("Name", 2));
        _pieceEditor.Children.Add(_pieceName);
        _pieceEditor.Children.Add(FieldLabel("Text", 2));
        _pieceEditor.Children.Add(_pieceText);
        var geometry = new WrapPanel();
        geometry.Children.Add(FieldLabel("W", 2)); geometry.Children.Add(_pieceWidth);
        geometry.Children.Add(FieldLabel("H", 2)); geometry.Children.Add(_pieceHeight);
        geometry.Children.Add(FieldLabel("X", 2)); geometry.Children.Add(_pieceX);
        geometry.Children.Add(FieldLabel("Y", 2)); geometry.Children.Add(_pieceY);
        _pieceEditor.Children.Add(geometry);
        var paints = new WrapPanel();
        paints.Children.Add(FieldLabel("Plate", 2)); paints.Children.Add(_pieceFill);
        paints.Children.Add(FieldLabel("Text color", 2)); paints.Children.Add(_pieceTextColor);
        _pieceEditor.Children.Add(paints);
        _pieceEditor.Children.Add(ActionButton("Update piece", UpdateTemporaryPiece));
        panel.Children.Add(_pieceEditor);
    }

    static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
        Margin = new Thickness(0, 12, 0, 4)
    };

    static TextBlock FieldLabel(string text, double top = 6) => new()
    {
        Text = text,
        FontSize = 10,
        Opacity = 0.7,
        Margin = new Thickness(0, top, 0, 2)
    };

    static TextBox Field(string text = "") => new()
    {
        Text = text,
        MinHeight = 26,
        Padding = new Thickness(6, 3, 6, 3),
        FontSize = 12,
        Margin = new Thickness(0, 2, 0, 2)
    };

    static Button ActionButton(string text, Action click)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 2, 4, 2),
            MinHeight = 28,
            FontSize = 11
        };
        button.Click += (_, _) => click();
        return button;
    }

    string Extension() => "." + MotionFiles.Extension(_kind);

    void WireEasyControls()
    {
        _easyExit.ValueChanged += EasyTimingChanged;
        _easyEnter.ValueChanged += EasyTimingChanged;
        _easyDuration.ValueChanged += EasyTimingChanged;
        _loopBox.Checked += EasyBehaviorChanged;
        _loopBox.Unchecked += EasyBehaviorChanged;
        _pingBox.Checked += EasyBehaviorChanged;
        _pingBox.Unchecked += EasyBehaviorChanged;
        _valueBox.Checked += EasyBehaviorChanged;
        _valueBox.Unchecked += EasyBehaviorChanged;
    }

    void FillPresetList()
    {
        _presetList.Items.Clear();
        var names = _kind == MotionKind.Transition ? MotionClip.TransitionPresets : MotionClip.AnimationPresets;
        foreach (var name in names)
        {
            var item = new ListBoxItem
            {
                Tag = name,
                Padding = new Thickness(8, 6, 8, 6),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = PresetDescription(_kind, name)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeights.SemiBold });
            stack.Children.Add(new TextBlock
            {
                Text = PresetDescription(_kind, name),
                FontSize = 10,
                Opacity = 0.64,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0)
            });
            item.Content = stack;
            _presetList.Items.Add(item);
        }
    }

    static string PresetDescription(MotionKind kind, string name) => kind == MotionKind.Transition
        ? name switch
        {
            "Dissolve" => "A soft cross-fade. Neutral and safe for any graphic.",
            "Wipe left" => "The old graphic slides left while the new one follows in.",
            "Wipe right" => "The old graphic slides right while the new one follows in.",
            "Push up" => "Both graphics travel upward, like a news ticker.",
            "Push down" => "Both graphics travel downward, like a drop-in panel.",
            "Zoom through" => "A punchy scale-through for replays and big moments.",
            "Flip card" => "A card-flip on the vertical axis.",
            "Swing" => "A quick swing out and a smooth return.",
            "Blur cut" => "A fast defocus cut; crisp and modern.",
            "Color wash" => "Fades through a colour while the graphic changes.",
            _ => "A saved motion preset."
        }
        : name switch
        {
            "Bob" => "A gentle up-and-down idle. Good for name straps.",
            "Sway" => "A small side-to-side drift with a touch of rotation.",
            "Pulse" => "A subtle breathing scale. Keeps a card alive without distraction.",
            "Spin" => "A full rotation. Save it for a logo or a celebration.",
            "Breathe" => "A slow, soft scale in and out.",
            "Shimmer" => "A quick highlight sweep for sponsor or premium graphics.",
            "Ticker roll" => "A continuous vertical roll for lists and tickers.",
            "Slide in" => "Arrives from the left with a clean settle.",
            "Pop in" => "A quick scale pop when the value first appears.",
            "Color cycle" => "A slow colour drift around the text or plate.",
            _ => "A saved motion preset."
        };

    void SyncPresetSelection()
    {
        var match = _presetList.Items.OfType<ListBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, _clip.Name, StringComparison.OrdinalIgnoreCase));
        _presetList.SelectedItem = match;
        if (match is null)
            _presetHint.Text = _clip.Phases.Count == 0 ? "This clip has no phases yet. Start blank only if you want to build from nothing." : "Tune the pace in step 2, or open Advanced for exact keyframes.";
        else _presetHint.Text = PresetDescription(_kind, (string)match.Tag);
    }

    void RefreshEasy()
    {
        if (_kind == MotionKind.Transition)
        {
            var exit = _clip.Phases.ElementAtOrDefault(0);
            var enter = _clip.Phases.ElementAtOrDefault(1);
            _easyExit.Maximum = Math.Max(10, exit?.Seconds ?? 0.3);
            _easyEnter.Maximum = Math.Max(10, enter?.Seconds ?? 0.3);
            _easySync = true;
            try
            {
                _easyExit.Value = Math.Clamp(exit?.Seconds ?? 0.3, _easyExit.Minimum, _easyExit.Maximum);
                _easyEnter.Value = Math.Clamp(enter?.Seconds ?? 0.3, _easyEnter.Minimum, _easyEnter.Maximum);
            }
            finally { _easySync = false; }
        }
        else
        {
            var phase = CurrentPhase ?? _clip.Phases.FirstOrDefault();
            _easyDuration.Maximum = Math.Max(10, phase?.Seconds ?? 1);
            _easySync = true;
            try { _easyDuration.Value = Math.Clamp(phase?.Seconds ?? 1, _easyDuration.Minimum, _easyDuration.Maximum); }
            finally { _easySync = false; }
        }
        _easySync = true;
        try
        {
            _loopBox.IsChecked = _clip.Loop;
            _pingBox.IsChecked = _clip.PingPong;
            _valueBox.IsChecked = _clip.UseOnValueChange;
        }
        finally { _easySync = false; }
        RefreshEasyReadouts();
    }

    void RefreshEasyReadouts()
    {
        _easyExitValue.Text = $"{_easyExit.Value:0.##}s";
        _easyEnterValue.Text = $"{_easyEnter.Value:0.##}s";
        _easyDurationValue.Text = $"{_easyDuration.Value:0.##}s";
        if (_kind == MotionKind.Transition)
        {
            var exit = _clip.Phases.ElementAtOrDefault(0);
            var enter = _clip.Phases.ElementAtOrDefault(1);
            var total = (exit?.Seconds ?? 0) + (enter?.Seconds ?? 0);
            _easySummary.Text = $"Exit {exit?.Seconds ?? 0:0.##}s + enter {enter?.Seconds ?? 0:0.##}s = {total:0.##}s. The old graphic leaves first, then the new one arrives.";
        }
        else
        {
            var phase = CurrentPhase ?? _clip.Phases.FirstOrDefault();
            var tracks = phase?.Tracks.Count ?? 0;
            var behaviour = _clip.UseOnValueChange ? "plays once when the value changes" : _clip.Loop ? "loops while the graphic is up" : "plays once";
            _easySummary.Text = $"{phase?.Seconds ?? 0:0.##}s · {tracks} keyed propert{(tracks == 1 ? "y" : "ies")} · {behaviour}.";
        }
    }

    void EasyTimingChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_easySync || _clip.Phases.Count == 0) return;
        var seconds = Math.Round(e.NewValue, 2);
        MotionPhase? phase;
        if (_kind == MotionKind.Transition)
            phase = sender == _easyExit ? _clip.Phases.ElementAtOrDefault(0) : _clip.Phases.ElementAtOrDefault(1);
        else
            phase = CurrentPhase ?? _clip.Phases.FirstOrDefault();
        if (phase is null) return;
        phase.Seconds = seconds;
        _clip.Normalize();
        DrawTimeline();
        RefreshEasyReadouts();
        _status.Text = $"{phase.Name} is now {seconds:0.##}s.";
    }

    void EasyBehaviorChanged(object sender, RoutedEventArgs e)
    {
        if (_easySync) return;
        _clip.Loop = _loopBox.IsChecked == true;
        _clip.PingPong = _pingBox.IsChecked == true;
        _clip.UseOnValueChange = _valueBox.IsChecked == true;
        RefreshEasyReadouts();
        _status.Text = _clip.UseOnValueChange ? "Plays once when the value changes." : _clip.Loop ? "Loops while the graphic is up." : "Plays once.";
    }

    // ---------- refresh ----------

    void RefreshAll()
    {
        _clip.Normalize();
        _nameBox.Text = _clip.Name;
        _staggerBox.Text = _clip.ItemStagger.ToString("0.##", CultureInfo.CurrentCulture);
        _phaseIndex = Math.Clamp(_phaseIndex, 0, Math.Max(0, _clip.Phases.Count - 1));
        _phaseList.ItemsSource = _clip.Phases.Select((phase, index) => new PhaseRow(phase, index)).ToArray();
        _phaseList.SelectedIndex = _clip.Phases.Count == 0 ? -1 : _phaseIndex;
        LoadPhase();
        RefreshLibrary();
        if (_kind == MotionKind.Transition) RefreshPieces();
        _loopBox.IsChecked = _clip.Loop;
        _pingBox.IsChecked = _clip.PingPong;
        _valueBox.IsChecked = _clip.UseOnValueChange;
        SyncPresetSelection();
        RefreshEasy();
        RefreshTimeline();
        DrawTimeline();
        RefreshStagePaint();
    }

    MotionPhase? CurrentPhase => _phaseIndex >= 0 && _phaseIndex < _clip.Phases.Count ? _clip.Phases[_phaseIndex] : null;

    void LoadPhase()
    {
        var phase = CurrentPhase;
        _phaseName.Text = phase?.Name ?? "";
        _phaseSeconds.Text = (phase?.Seconds ?? 0.3).ToString("0.##", CultureInfo.CurrentCulture);
        _phaseOn.IsChecked = phase?.Enabled ?? true;
        _phaseTriggerAmount.Text = (phase?.TriggerAmount ?? 0).ToString(CultureInfo.CurrentCulture);
        _phaseTriggerSide.SelectedItem = SideOptions().FirstOrDefault(option => option.Value == (phase?.TriggerSide ?? MotionScoreSide.Any));
        _trackIndex = -1;
        _colorIndex = -1;
        RefreshTracks();
        RefreshColors();
        DrawTimeline();
        RefreshEasy();
    }

    void RefreshTracks()
    {
        var tracks = PieceTracks();
        _trackIndex = tracks.Count == 0 ? -1 : Math.Clamp(_trackIndex < 0 ? 0 : _trackIndex, 0, tracks.Count - 1);
        _trackList.ItemsSource = tracks.Select(track => new TrackRow(track)).ToArray();
        _keyIndex = -1;
        if (_trackIndex >= 0)
        {
            _trackList.SelectedIndex = _trackIndex;
            LoadTrack();
        }
        else
        {
            _trackList.SelectedIndex = -1;
            _keyList.ItemsSource = null;
        }
        DrawTimeline();
    }

    void LoadTrack()
    {
        var track = SelectedTrack;
        if (track is null) { _keyList.ItemsSource = null; return; }
        _keyList.ItemsSource = track.Keys.OrderBy(key => key.Time).Select((key, index) => new KeyRow(key, index)).ToArray();
        _keyIndex = -1;
        DrawTimeline();
    }

    MotionTrack? SelectedTrack
    {
        get
        {
            var tracks = PieceTracks();
            return _trackIndex >= 0 && _trackIndex < tracks.Count ? tracks[_trackIndex] : null;
        }
    }

    MotionTarget PickedTarget =>
        _propertyPicker.SelectedItem is TargetOption option ? option.Target : MotionTarget.Opacity;

    string PieceLabel => _pieceId.Length == 0
        ? "the whole graphic"
        : _clip.Pieces.FirstOrDefault(piece => IsPiece(piece.Id))?.Name
          ?? TimelineLayout.Elements.FirstOrDefault(item => IsPiece(item.Id))?.Name
          ?? "this item";

    /// <summary>
    /// The picked property's track on the selected item, created on first use. Placing a key must
    /// never depend on a separate "add track" step: both the key button and the ruler call this.
    /// </summary>
    MotionTrack? EnsureTrack()
    {
        var phase = CurrentPhase;
        if (phase is null) { _status.Text = "Add a phase before keyframing."; return null; }
        var target = PickedTarget;
        var tracks = PieceTracks();
        var existing = tracks.FirstOrDefault(track => track.Target == target);
        if (existing is not null)
        {
            _trackIndex = tracks.IndexOf(existing);
            _trackList.SelectedIndex = _trackIndex;
            LoadTrack();
            return existing;
        }
        var value = DesignValue(target);
        var track = new MotionTrack
        {
            Target = target,
            PieceId = _pieceId,
            Keys = [new MotionKey(0, value), new MotionKey(Math.Max(0.1, phase.Seconds), value)]
        };
        phase.Tracks.Add(track);
        RefreshAll();
        var refreshed = PieceTracks();
        _trackIndex = Math.Max(0, refreshed.IndexOf(track));
        _trackList.SelectedIndex = _trackIndex;
        LoadTrack();
        DrawTimeline();
        return track;
    }

    MotionKey? SelectedKey => SelectedTrack is { } track && _keyIndex >= 0 && _keyIndex < track.Keys.Count
        ? track.Keys.OrderBy(key => key.Time).ElementAtOrDefault(_keyIndex)
        : null;

    ColorRow? SelectedColorRow => _colorList.SelectedItem as ColorRow;

    ColorTrack? SelectedColorTrack => SelectedColorRow?.Track;

    void RefreshColors()
    {
        var phase = CurrentPhase;
        var rows = new List<ColorRow>();
        if (phase is not null)
            foreach (var track in phase.Colors.Where(track => IsPiece(track.PieceId)))
                foreach (var key in track.Keys.OrderBy(key => key.Time))
                    rows.Add(new ColorRow(track, key));
        _colorList.ItemsSource = rows;
        _colorIndex = -1;
        _colorList.SelectedIndex = -1;
        DrawTimeline();
    }

    void LoadKey()
    {
        if (SelectedKey is not { } key) return;
        _keyTime.Text = key.Time.ToString("0.##", CultureInfo.CurrentCulture);
        _keyValue.Text = key.Value.ToString("0.###", CultureInfo.CurrentCulture);
        SelectEase(key.Ease);
        DrawTimeline();
    }

    void LoadColor()
    {
        if (SelectedColorRow is not { } row) return;
        SelectRole(row.Track.Role);
        _colorTime.Text = row.Key.Time.ToString("0.##", CultureInfo.CurrentCulture);
        _colorValue.Text = row.Key.Color;
    }

    void RefreshLibrary()
    {
        var clips = _kind == MotionKind.Transition ? _catalog.Transitions : _catalog.Animations;
        _libraryList.ItemsSource = clips.Select(clip => new LibraryRow(clip)).ToArray();
    }

    void LibraryChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(LibraryChanged); return; }
        // Any catalog edit can change a layout's artwork, so the previews are rebuilt next time.
        _surfaces.Clear();
        RefreshLibrary();
        RefreshLayoutChoices();
        BuildStage();
    }

    /// <summary>
    /// The pieces the operator can key: the whole graphic, every real item in the layout, then the
    /// temporary pieces stored inside this clip. Choosing one scopes the track and key editors.
    /// </summary>
    void RefreshPieces()
    {
        var layout = TimelineLayout;
        var choices = new List<PieceChoice> { new("", "Whole graphic", false, null) };
        foreach (var item in layout.Elements)
            choices.Add(new PieceChoice(item.Id, string.IsNullOrWhiteSpace(item.Name) ? "(unnamed item)" : item.Name, false, null));
        foreach (var piece in _clip.Pieces)
            choices.Add(new PieceChoice(piece.Id, "Temporary · " + piece.Name, true, piece));
        _pieceLoading = true;
        try
        {
            _pieceList.ItemsSource = choices;
            var selected = choices.FirstOrDefault(choice => string.Equals(choice.Id, _pieceId, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
            _pieceId = selected.Id;
            _pieceList.SelectedItem = selected;
        }
        finally { _pieceLoading = false; }
        LoadPiece();
        UpdateTimelineLabel();
        _pieceHint.Text = _pieceId.Length == 0
            ? "Keys on the whole graphic move every item together. Pick an item, or add a temporary piece, to key it on its own."
            : "These keys run on the selected piece only. Whole-graphic keys still play too, and a temporary piece exists only while this transition runs.";
    }

    void SelectPiece()
    {
        if (_pieceLoading || _pieceList.SelectedItem is not PieceChoice choice) return;
        _pieceId = choice.Id;
        _trackIndex = -1;
        _colorIndex = -1;
        LoadPiece();
        RefreshTracks();
        RefreshColors();
        DrawTimeline();
        UpdateTimelineLabel();
        _pieceHint.Text = choice.Temporary
            ? "This piece is temporary: it is saved in the clip and drawn only while the transition runs."
            : choice.Id.Length == 0
                ? "Keys on the whole graphic move every item together. Pick an item, or add a temporary piece, to key it on its own."
                : "These keys run on the selected piece only. Whole-graphic keys still play too.";
    }

    MotionPiece? SelectedPiece => _clip.Pieces.FirstOrDefault(piece => string.Equals(piece.Id, _pieceId, StringComparison.OrdinalIgnoreCase));

    void LoadPiece()
    {
        var piece = SelectedPiece;
        _pieceEditor.IsEnabled = piece is not null;
        if (piece is null)
        {
            _pieceName.Text = "";
            _pieceText.Text = "";
            return;
        }
        _pieceName.Text = piece.Name;
        _pieceText.Text = piece.Text;
        _pieceKindPicker.SelectedIndex = (int)piece.Kind;
        _pieceLogoSide.SelectedIndex = piece.Logo switch { ScorebugLogoSide.Home => 1, ScorebugLogoSide.Away => 2, _ => 0 };
        _pieceShapePicker.SelectedItem = piece.Shape;
        _pieceTriggerAmount.Text = piece.TriggerAmount.ToString(CultureInfo.CurrentCulture);
        _pieceTriggerSide.SelectedItem = SideOptions().FirstOrDefault(option => option.Value == piece.TriggerSide);
        _pieceWidth.Text = piece.Width.ToString("0.##", CultureInfo.CurrentCulture);
        _pieceHeight.Text = piece.Height.ToString("0.##", CultureInfo.CurrentCulture);
        _pieceX.Text = piece.X.ToString("0.##", CultureInfo.CurrentCulture);
        _pieceY.Text = piece.Y.ToString("0.##", CultureInfo.CurrentCulture);
        _pieceFill.Text = piece.Fill;
        _pieceTextColor.Text = piece.TextColor;
    }

    void AddTemporaryPiece(MotionPieceKind kind = MotionPieceKind.Text)
    {
        var layout = TimelineLayout;
        var label = kind switch { MotionPieceKind.Logo => "Logo", MotionPieceKind.Shape => "Shape", _ => "Text" };
        var triggerAmount = CurrentPhase?.TriggerAmount ?? 0;
        var triggerSide = CurrentPhase?.TriggerSide ?? MotionScoreSide.Any;
        var piece = new MotionPiece
        {
            Name = $"{label} {_clip.Pieces.Count + 1}",
            Kind = kind,
            X = Math.Max(0, layout.Width / 2 - 160),
            Y = Math.Max(0, layout.Height / 2 - 55),
            Width = 320,
            Height = 110,
            Text = kind == MotionPieceKind.Text ? "{{LAST_SCORE_CHANGE}}" : "",
            Logo = kind == MotionPieceKind.Logo ? ScorebugLogoSide.Home : ScorebugLogoSide.None,
            TriggerAmount = triggerAmount,
            TriggerSide = triggerSide
        };
        _clip.Pieces.Add(piece);
        _pieceId = piece.Id;
        RefreshPieces();
        RefreshTimeline();
        LoadPiece();
        RefreshTracks();
        RefreshColors();
        DrawTimeline();
        BuildStage();
        _status.Text = $"Added temporary {label.ToLowerInvariant()} “{piece.Name}”. It exists only while this clip runs; key it on the timeline.";
    }

    void UpdateTemporaryPiece()
    {
        if (SelectedPiece is not { } piece) { _status.Text = "Select a temporary piece first."; return; }
        piece.Name = string.IsNullOrWhiteSpace(_pieceName.Text) ? piece.Name : _pieceName.Text.Trim();
        piece.Text = _pieceText.Text;
        piece.Kind = (MotionPieceKind)Math.Clamp(_pieceKindPicker.SelectedIndex, 0, 2);
        piece.Logo = _pieceLogoSide.SelectedIndex switch { 1 => ScorebugLogoSide.Home, 2 => ScorebugLogoSide.Away, _ => ScorebugLogoSide.None };
        piece.Shape = _pieceShapePicker.SelectedItem is ScorebugShape shape ? shape : piece.Shape;
        piece.TriggerAmount = (int)Number(_pieceTriggerAmount.Text, piece.TriggerAmount, 0, 999);
        piece.TriggerSide = (_pieceTriggerSide.SelectedItem as SideOption)?.Value ?? MotionScoreSide.Any;
        piece.Width = Number(_pieceWidth.Text, piece.Width, 4, 7680);
        piece.Height = Number(_pieceHeight.Text, piece.Height, 4, 7680);
        piece.X = Number(_pieceX.Text, piece.X, -7680, 7680);
        piece.Y = Number(_pieceY.Text, piece.Y, -7680, 7680);
        piece.Fill = HexColor(_pieceFill.Text);
        piece.TextColor = HexColor(_pieceTextColor.Text);
        piece.Normalize();
        RefreshPieces();
        RefreshTimeline();
        LoadPiece();
        RefreshTracks();
        RefreshColors();
        DrawTimeline();
        BuildStage();
        _status.Text = $"Updated “{piece.Name}”. It is saved with the clip.";
    }

    void RemoveTemporaryPiece()
    {
        if (SelectedPiece is not { } piece) { _status.Text = "Only a temporary piece can be removed; layout items are edited in the studio."; return; }
        _clip.Pieces.Remove(piece);
        // Keys authored for a temporary piece have nothing left to drive once the piece is gone.
        foreach (var phase in _clip.Phases)
        {
            phase.Tracks.RemoveAll(track => string.Equals(track.PieceId, piece.Id, StringComparison.OrdinalIgnoreCase));
            phase.Colors.RemoveAll(track => string.Equals(track.PieceId, piece.Id, StringComparison.OrdinalIgnoreCase));
        }
        _pieceId = "";
        RefreshPieces();
        RefreshTimeline();
        LoadPiece();
        RefreshTracks();
        RefreshColors();
        DrawTimeline();
        BuildStage();
        _status.Text = $"Removed temporary piece “{piece.Name}”.";
    }

    void UpdateTimelineLabel()
    {
        if (_kind != MotionKind.Transition) { _timelinePiece.Text = ""; return; }
        var name = SelectedPiece?.Name;
        if (string.IsNullOrWhiteSpace(name) && _pieceId.Length > 0)
            name = TimelineLayout.Elements.FirstOrDefault(item => string.Equals(item.Id, _pieceId, StringComparison.OrdinalIgnoreCase))?.Name;
        _timelinePiece.Text = "Editing " + (string.IsNullOrWhiteSpace(name) ? "the whole graphic" : name);
    }

    bool IsPiece(string? id) => string.Equals(id ?? "", _pieceId, StringComparison.OrdinalIgnoreCase);

    List<MotionTrack> PieceTracks() => CurrentPhase?.Tracks.Where(track => IsPiece(track.PieceId)).ToList() ?? [];

    /// <summary>Every existing layout, by display and name, so the preview draws the real thing.</summary>
    void RefreshLayoutChoices()
    {
        var exitId = (_exitLayoutPicker.SelectedItem as LayoutChoice)?.Layout?.Id;
        var enterId = (_enterLayoutPicker.SelectedItem as LayoutChoice)?.Layout?.Id;
        var motionId = (_motionLayoutPicker.SelectedItem as LayoutChoice)?.Layout?.Id;
        var options = new List<LayoutChoice> { new(null, "Preview plate") };
        var layoutOptions = new List<LayoutChoice>();
        foreach (var display in _catalog.Displays)
            foreach (var layout in display.Layouts)
            {
                var choice = new LayoutChoice(layout, $"{display.Name} · {layout.Name}");
                options.Add(choice);
                layoutOptions.Add(choice);
            }
        SetChoices(_exitLayoutPicker, options, exitId);
        SetChoices(_enterLayoutPicker, options, enterId);
        SetChoices(_motionLayoutPicker, options, motionId);
        // The timeline always edits a real layout: the one it was opened on, then the clip's own
        // layout, then the studio's active layout. The stand-in plate cannot own item tracks, and
        // leaving the picker on it used to make the timeline list the wrong items.
        // The clip's own layout wins: loading a saved clip must reopen the layout it was authored
        // on, while the picker is only a fallback for a clip that has not chosen one yet.
        var timelineId = _clip.TimelineLayoutId;
        if (string.IsNullOrWhiteSpace(timelineId)) timelineId = (_timelineLayoutPicker.SelectedItem as LayoutChoice)?.Layout?.Id;
        if (string.IsNullOrWhiteSpace(timelineId)) timelineId = _catalog.Layout.Id;
        SetChoices(_timelineLayoutPicker, layoutOptions, timelineId);
        if ((_timelineLayoutPicker.SelectedItem as LayoutChoice)?.Layout is { } timeline)
            _clip.TimelineLayoutId = timeline.Id;
        RefreshTimeline();
        // First run: preview the layout being designed rather than the stand-in plate.
        if (exitId is null && enterId is null && motionId is null)
        {
            SelectLayout(_exitLayoutPicker, options, _catalog.Layout.Id);
            SelectLayout(_enterLayoutPicker, options, _catalog.Layout.Id);
            SelectLayout(_motionLayoutPicker, options, _catalog.Layout.Id);
        }
    }

    static void SetChoices(ComboBox box, List<LayoutChoice> options, string? id)
    {
        box.ItemsSource = options;
        box.SelectedItem = options.Count == 0
            ? null
            : options.FirstOrDefault(option => string.Equals(option.Layout?.Id, id, StringComparison.OrdinalIgnoreCase)) ?? options[0];
    }

    static void SelectLayout(ComboBox box, List<LayoutChoice> options, string? id) =>
        box.SelectedItem = options.FirstOrDefault(option => string.Equals(option.Layout?.Id, id, StringComparison.OrdinalIgnoreCase)) ?? options[0];

    // ---------- editing ----------

    void ShowHelp()
    {
        var transition = _kind == MotionKind.Transition;
        NoticeWindow.Tell(this,
            transition ? "How the Transition Builder works" : "How the Animation Maker works",
            transition
                ? "Use this maker when a graphic comes on or leaves the screen.\n\n" +
                  "1. Pick a style on the left — for example Wipe left or Dissolve.\n" +
                  "2. Drag the two pace sliders: one for the old graphic leaving, one for the new graphic arriving.\n" +
                  "3. Press Play to watch it on a real layout. Give it a name and press Save clip.\n\n" +
                  "The saved clip then appears in the studio's Transition in list. Advanced is optional; you never need it for a normal transition."
                : "Use this maker when a value moves while it is already on screen — for example a score popping, a clock pulsing or a lower third looping.\n\n" +
                  "1. Pick a style on the left — for example Pop in or Pulse.\n" +
                  "2. Drag the duration slider. Tick Loop for an idle motion, or Play when the value changes for a one-shot flourish.\n" +
                  "3. Press Play to watch it, name it and press Save clip.\n\n" +
                  "The saved clip then appears in the studio's Animation / Value change lists. Advanced is optional; you never need it for a normal animation.");
    }

    void QuickStart()
    {
        if (_presetList.Items.Count == 0) return;
        if (_presetList.SelectedIndex < 0) _presetList.SelectedIndex = 0;
        UsePreset();
        if (_kind == MotionKind.Transition)
        {
            _easyExit.Value = 0.3;
            _easyEnter.Value = 0.4;
        }
        else _easyDuration.Value = 0.8;
        BuildStage();
        Play();
        _status.Text = "Quick start is playing. If it feels right, give it a name and press Save clip. Use the pace sliders to change the speed.";
    }

    void UsePreset()
    {
        var name = (_presetList.SelectedItem as ListBoxItem)?.Tag as string
            ?? _presetPicker.SelectedItem as string;
        if (name is null)
        {
            _status.Text = "Pick a style first, then press Use this style.";
            return;
        }
        _clip = _kind == MotionKind.Transition ? MotionClip.TransitionPreset(name) : MotionClip.AnimationPreset(name);
        _clip.Name = name;
        _pieceId = "";
        _phaseIndex = 0;
        RefreshAll();
        BuildStage();
        _status.Text = $"Style loaded: {name}. Set the pace in step 2, then save it under your own name.";
    }

    void AddPhase()
    {
        _clip.Phases.Add(new MotionPhase(_kind == MotionKind.Transition ? "Phase" : "Motion", 0.4)
        {
            Tracks = [new MotionTrack(MotionTarget.Opacity, (0, 1), (0.4, 1))]
        });
        _phaseIndex = _clip.Phases.Count - 1;
        RefreshAll();
    }

    void RemovePhase()
    {
        if (_clip.Phases.Count <= 1 || CurrentPhase is null) return;
        _clip.Phases.RemoveAt(_phaseIndex);
        _phaseIndex = Math.Clamp(_phaseIndex, 0, _clip.Phases.Count - 1);
        RefreshAll();
    }

    void ApplyPhaseSettings()
    {
        var phase = CurrentPhase;
        if (phase is null) return;
        if (!string.IsNullOrWhiteSpace(_phaseName.Text)) phase.Name = _phaseName.Text.Trim();
        phase.Seconds = Number(_phaseSeconds.Text, phase.Seconds, 0, 30);
        phase.Enabled = _phaseOn.IsChecked == true;
        phase.TriggerAmount = (int)Number(_phaseTriggerAmount.Text, phase.TriggerAmount, 0, 999);
        phase.TriggerSide = (_phaseTriggerSide.SelectedItem as SideOption)?.Value ?? MotionScoreSide.Any;
        _clip.Loop = _loopBox.IsChecked == true;
        _clip.PingPong = _pingBox.IsChecked == true;
        _clip.UseOnValueChange = _valueBox.IsChecked == true;
        _clip.Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? _clip.Name : _nameBox.Text.Trim();
        phase.Normalize();
        if (phase.TriggerAmount > 0) { _clip.UseOnValueChange = true; _clip.Loop = false; }
        RefreshAll();
        BuildStage();
        _status.Text = $"Phase {phase.Name}: {phase.Seconds:0.##}s.";
    }

    void AddTrack()
    {
        if (EnsureTrack() is not { } track) return;
        _status.Text = $"{MotionClip.TargetLabel(track.Target)} track ready on {PieceLabel}.";
    }

    static double Default(MotionTarget target) => target switch
    {
        MotionTarget.Opacity or MotionTarget.Scale or MotionTarget.ScaleX or MotionTarget.ScaleY => 1,
        _ => 0
    };

    void RemoveTrack()
    {
        if (CurrentPhase is not { } phase || SelectedTrack is not { } track) return;
        phase.Tracks.Remove(track);
        _trackIndex = -1;
        RefreshAll();
    }

    void AddKey()
    {
        var track = SelectedTrack ?? EnsureTrack();
        if (track is null) return;
        var time = Number(_keyTime.Text, 0, 0, CurrentPhase?.Seconds ?? 5);
        var value = Number(_keyValue.Text, Default(track.Target), -100000, 100000);
        var ease = SelectedEase;
        track.Keys.RemoveAll(key => Math.Abs(key.Time - time) < 0.005);
        var added = new MotionKey(time, value, ease);
        track.Keys.Add(added);
        track.Normalize(CurrentPhase?.Seconds ?? 5);
        RefreshTracks();
        if (track.Keys.Contains(added))
        {
            SelectKeyForEditing(track, added);
            BuildStage();
            PreviewAt(added.Time);
            _status.Text = $"Keyed {MotionClip.TargetLabel(track.Target)} at {added.Time:0.##}s.";
        }
    }

    /// <summary>
    /// A key at the ruler's current position for the picked property. The track is created (and
    /// selected) on first use, so placing a key never depends on pressing "+ Track" beforehand.
    /// </summary>
    void AddKeyAtPlayhead()
    {
        var track = EnsureTrack();
        if (track is null) return;
        _scrubTime = Math.Clamp(_scrubTime, 0, CurrentPhase?.Seconds ?? 5);
        _keyTime.Text = _scrubTime.ToString("0.##", CultureInfo.CurrentCulture);
        _keyValue.Text = DesignValue(track.Target).ToString("0.###", CultureInfo.CurrentCulture);
        AddKey();
        _status.Text = $"Added {MotionClip.TargetLabel(track.Target)} on {PieceLabel} at {_scrubTime:0.##}s.";
    }

    void AddValueVariant(int amount)
    {
        var source = CurrentPhase ?? _clip.Phases.FirstOrDefault();
        if (source is null) return;
        var copy = source.Copy();
        copy.Name = string.IsNullOrWhiteSpace(source.Name) ? $"Value +{amount}" : $"{source.Name} +{amount}";
        copy.TriggerAmount = amount;
        copy.TriggerSide = (_phaseTriggerSide.SelectedItem as SideOption)?.Value ?? MotionScoreSide.Any;
        copy.Enabled = true;
        _clip.Phases.Add(copy);
        _clip.UseOnValueChange = true;
        _clip.Loop = false;
        _phaseIndex = _clip.Phases.Count - 1;
        RefreshAll();
        BuildStage();
        _status.Text = $"Added a +{amount} take. It only plays when that score is pressed; the untriggered phase stays the fallback.";
    }

    /// <summary>The value an item has in the studio, before any clip runs.</summary>
    double DesignValue(MotionTarget target)
    {
        var piece = SelectedPiece;
        var item = TimelineLayout.Elements.FirstOrDefault(candidate => string.Equals(candidate.Id, _pieceId, StringComparison.OrdinalIgnoreCase));
        return target switch
        {
            MotionTarget.X or MotionTarget.Y or MotionTarget.Rotate or MotionTarget.Blur or MotionTarget.SkewX or MotionTarget.SkewY => 0,
            MotionTarget.Scale or MotionTarget.ScaleX or MotionTarget.ScaleY => 1,
            MotionTarget.Opacity => item?.Opacity ?? piece?.Opacity ?? 1,
            MotionTarget.Width => item?.Width ?? piece?.Width ?? TimelineLayout.Width,
            MotionTarget.Height => item?.Height ?? piece?.Height ?? TimelineLayout.Height,
            MotionTarget.FontSize => item?.FontSize ?? piece?.FontSize ?? 32,
            MotionTarget.BorderThickness => item?.BorderThickness ?? piece?.BorderThickness ?? 0,
            MotionTarget.TextOpacity => item?.TextOpacity ?? 1,
            MotionTarget.BackgroundOpacity => item?.BackgroundOpacity ?? 1,
            MotionTarget.BorderOpacity => item?.BorderOpacity ?? 1,
            _ => 0
        };
    }

    string DesignColor(MotionColorRole role)
    {
        if (SelectedPiece is { } piece)
            return role switch
            {
                MotionColorRole.Background => piece.Fill,
                MotionColorRole.Border => piece.Border,
                _ => piece.TextColor
            };
        var item = TimelineLayout.Elements.FirstOrDefault(candidate => string.Equals(candidate.Id, _pieceId, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
            return role switch
            {
                MotionColorRole.Background => ScorebugRender.ResolveBackground(item, _engine.State),
                MotionColorRole.Border => ScorebugRender.ResolveBorder(item, _engine.State),
                _ => ScorebugRender.ResolveForeground(item, _engine.State)
            };
        return role == MotionColorRole.Background ? TimelineLayout.Background : "#FFFFFFFF";
    }

    /// <summary>Adds a return key for the selected property at the end of the current phase.</summary>
    void AddReturnKey()
    {
        var phase = CurrentPhase;
        if (phase is null) return;
        var track = SelectedTrack ?? EnsureTrack();
        if (track is null) return;
        var value = DesignValue(track.Target);
        track.Keys.RemoveAll(key => Math.Abs(key.Time - phase.Seconds) < 0.005);
        track.Keys.Add(new MotionKey(phase.Seconds, value, MotionEase.InOut));
        track.Normalize(phase.Seconds);
        RefreshTracks();
        _status.Text = $"Return key added for {MotionClip.TargetLabel(track.Target)} at {phase.Seconds:0.##}s — back to the designed value.";
        BuildStage();
    }

    /// <summary>Writes the designed value at the end of every keyed property on the selected item.</summary>
    void AddReturnKeys()
    {
        var phase = CurrentPhase;
        if (phase is null) return;
        var tracks = phase.Tracks.Where(track => IsPiece(track.PieceId)).ToList();
        var colors = phase.Colors.Where(track => IsPiece(track.PieceId)).ToList();
        foreach (var track in tracks)
        {
            track.Keys.RemoveAll(key => Math.Abs(key.Time - phase.Seconds) < 0.005);
            track.Keys.Add(new MotionKey(phase.Seconds, DesignValue(track.Target), MotionEase.InOut));
            track.Normalize(phase.Seconds);
        }
        foreach (var track in colors)
        {
            track.Keys.RemoveAll(key => Math.Abs(key.Time - phase.Seconds) < 0.005);
            track.Keys.Add(new ColorKey(phase.Seconds, DesignColor(track.Role)));
            track.Normalize(phase.Seconds);
        }
        RefreshTracks();
        RefreshColors();
        BuildStage();
        _status.Text = tracks.Count + colors.Count == 0
            ? "This item has no keys in the phase yet."
            : $"Added return keys to {tracks.Count + colors.Count} track{(tracks.Count + colors.Count == 1 ? "" : "s")} — the designed look returns at {phase.Seconds:0.##}s.";
    }

    void AddReturnColor()
    {
        var phase = CurrentPhase;
        if (phase is null) return;
        var role = SelectedRole;
        var track = phase.Color(role, _pieceId);
        track.Keys.RemoveAll(key => Math.Abs(key.Time - phase.Seconds) < 0.005);
        track.Keys.Add(new ColorKey(phase.Seconds, DesignColor(role)));
        track.Normalize(phase.Seconds);
        RefreshAll();
        BuildStage();
        PreviewAt(phase.Seconds);
        _status.Text = $"{MotionClip.RoleLabel(role)} returns to its designed color at {phase.Seconds:0.##}s.";
    }

    void UpdateKey()
    {
        if (SelectedKey is not { } key) { _status.Text = "Select a key to update it."; return; }
        if (SelectedTrack is not { } track) { _status.Text = "Select a track to update its key."; return; }
        key.Time = Number(_keyTime.Text, key.Time, 0, CurrentPhase?.Seconds ?? 5);
        key.Value = Number(_keyValue.Text, key.Value, -100000, 100000);
        key.Ease = SelectedEase;
        track.Normalize(CurrentPhase?.Seconds ?? 5);
        RefreshTracks();
        var kept = track.Keys.Contains(key)
            ? key
            : track.Keys.OrderBy(candidate => Math.Abs(candidate.Time - key.Time)).FirstOrDefault();
        if (kept is null) { _status.Text = "That key could not be updated."; return; }
        // Keep the edited key selected and put the preview on its frame: a value change that only
        // altered an invisible model looked like it had been ignored.
        SelectKeyForEditing(track, kept);
        BuildStage();
        PreviewAt(kept.Time);
        _status.Text = $"Updated {MotionClip.TargetLabel(track.Target)} at {kept.Time:0.##}s to {kept.Value:0.###}.";
    }

    void RemoveKey()
    {
        if (SelectedKey is not { } key) return;
        SelectedTrack?.Keys.Remove(key);
        _keyIndex = -1;
        RefreshTracks();
        BuildStage();
    }

    void PickColor()
    {
        if (ColorPickerWindow.Pick(this, _colorValue.Text) is string picked) _colorValue.Text = picked;
    }

    void AddColor()
    {
        var phase = CurrentPhase;
        if (phase is null) return;
        var role = SelectedRole;
        var track = phase.Color(role, _pieceId);
        var time = Number(_colorTime.Text, 0, 0, phase.Seconds);
        track.Keys.RemoveAll(key => Math.Abs(key.Time - time) < 0.005);
        track.Keys.Add(new ColorKey(time, HexColor(_colorValue.Text)));
        track.Normalize(phase.Seconds);
        RefreshAll();
        var added = _colorList.Items.OfType<ColorRow>()
            .FirstOrDefault(candidate => candidate.Track.Role == role && Math.Abs(candidate.Key.Time - time) < 0.006);
        if (added is not null) _colorList.SelectedItem = added;
        BuildStage();
        // Show the keyed frame at once. Rebuilding the stage alone leaves it at the design look,
        // which made a freshly added colour key appear to have done nothing.
        PreviewAt(added?.Key.Time ?? time);
        _status.Text = $"Added {MotionClip.RoleLabel(role)} at {time:0.##}s.";
    }

    void UpdateColor()
    {
        if (SelectedColorRow is not { } row) { _status.Text = "Select a color key to update it."; return; }
        row.Key.Time = Number(_colorTime.Text, row.Key.Time, 0, CurrentPhase?.Seconds ?? 5);
        row.Key.Color = HexColor(_colorValue.Text);
        row.Track.Normalize(CurrentPhase?.Seconds ?? 5);
        RefreshAll();
        var kept = _colorList.Items.OfType<ColorRow>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Key, row.Key))
            ?? _colorList.Items.OfType<ColorRow>()
                .OrderBy(candidate => Math.Abs(candidate.Key.Time - row.Key.Time)).FirstOrDefault();
        if (kept is not null) _colorList.SelectedItem = kept;
        BuildStage();
        PreviewAt(kept?.Key.Time ?? row.Key.Time);
        _status.Text = $"Updated {MotionClip.RoleLabel(row.Track.Role)} at {(kept?.Key.Time ?? row.Key.Time):0.##}s.";
    }

    void RemoveColor()
    {
        if (SelectedColorRow is not { } row) return;
        row.Track.Keys.Remove(row.Key);
        if (row.Track.Keys.Count == 0) CurrentPhase?.Colors.Remove(row.Track);
        RefreshAll();
        BuildStage();
    }

    static string HexColor(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length >= 4 && text[0] == '#' && text[1..].Length is 3 or 4 or 6 or 8 && text[1..].All(Uri.IsHexDigit)) return text;
        return "#FFFFFFFF";
    }

    static double Number(string? text, double fallback, double min, double max)
    {
        if (!double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            && !double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return fallback;
        return Math.Clamp(value, min, max);
    }

    // ---------- preview ----------

    readonly record struct PreviewTarget(FrameworkElement Element, double Width, double Height);

    sealed record TransitionPreview(FrameworkElement Root, Dictionary<string, FrameworkElement> Hosts,
        List<(FrameworkElement Host, MotionPiece Piece)> Temporary, double Width, double Height);

    PreviewTarget TargetFor(LayoutChoice? choice)
    {
        if (choice?.Layout is not { } layout) return new PreviewTarget(_plate, 420, 150);
        if (!_surfaces.TryGetValue(layout.Id, out var surface))
        {
            surface = BuildLayoutSurface(layout);
            _surfaces[layout.Id] = surface;
        }
        return new PreviewTarget(surface, layout.Width, layout.Height);
    }

    /// <summary>A still, scaled copy of a real layout — the same paint, fonts and spacing as the studio.</summary>
    FrameworkElement BuildLayoutSurface(ScorebugLayout layout)
    {
        var canvas = new Canvas
        {
            Width = layout.Width,
            Height = layout.Height,
            Background = ScorebugRender.ParseBrush(layout.Background),
            ClipToBounds = true
        };
        foreach (var item in layout.Elements)
            if (item.Visible) canvas.Children.Add(BuildElementHost(item));
        return new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
    }

    /// <summary>One element as the studio draws it: same paint, content and spacing.</summary>
    FrameworkElement BuildElementHost(ScorebugElement item)
    {
        var state = _engine.State;
        var content = ScorebugRender.CreateContent(item, () => _engine.State, null, true, showVideoPlaceholder: true);
        if (content is FrameworkElement element && content is not ScorebugTextBlock)
            element.Opacity = Math.Clamp(item.TextOpacity, 0, 1);
        var host = new Border
        {
            Width = item.Width,
            Height = item.Height,
            Child = content,
            Background = ScorebugRender.HostBackground(item, state),
            Opacity = Math.Clamp(item.Opacity, 0, 1),
            ClipToBounds = true
        };
        if (item.BorderThickness > 0)
        {
            host.BorderBrush = ScorebugRender.HostBorder(item, state);
            host.BorderThickness = new Thickness(item.BorderThickness);
        }
        MotionPlayback.SetDesignRotation(host, content is ScorebugTextBlock ? 0 : item.Rotation);
        Canvas.SetLeft(host, item.X);
        Canvas.SetTop(host, item.Y);
        return host;
    }

    void ShowTarget(PreviewTarget target)
    {
        _shown = target;
        _previewGrid.Children.Clear();
        _previewGrid.Children.Add(target.Element);
        _previewGrid.Width = target.Width;
        _previewGrid.Height = target.Height;
    }

    static void ResetPreview(FrameworkElement host, double opacity = 1)
    {
        MotionPlayback.Stop(host);
        host.RenderTransform = Transform.Identity;
        host.Opacity = Math.Clamp(opacity, 0, 1);
        host.Effect = null;
    }

    /// <summary>
    /// The real layout as a canvas of item hosts, plus the clip's temporary pieces, so a transition
    /// can be watched and scrubbed before it is saved. The hosts, not the canvas, receive a phase:
    /// each item follows the keys scoped to it.
    /// </summary>
    TransitionPreview BuildTransitionPreview()
    {
        var layout = TimelineLayout;
        var canvas = new Canvas
        {
            Width = layout.Width,
            Height = layout.Height,
            Background = ScorebugRender.ParseBrush(layout.Background),
            ClipToBounds = true
        };
        var hosts = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in layout.Elements)
        {
            if (!item.Visible) continue;
            var host = BuildElementHost(item);
            canvas.Children.Add(host);
            hosts[item.Id] = host;
        }
        var temporary = new List<(FrameworkElement Host, MotionPiece Piece)>();
        foreach (var piece in PreviewPieces())
        {
            var host = BuildTemporaryPieceHost(piece);
            canvas.Children.Add(host);
            temporary.Add((host, piece));
        }
        return new TransitionPreview(
            new Viewbox { Child = canvas, Stretch = Stretch.Uniform },
            hosts, temporary, layout.Width, layout.Height);
    }

    IEnumerable<MotionPiece> PreviewPieces()
    {
        if (_kind != MotionKind.Animation) return _clip.Pieces;
        var phase = CurrentPhase;
        if (phase is null || phase.TriggerAmount == 0) return _clip.Pieces;
        return _clip.Pieces.Where(piece =>
            (piece.TriggerAmount == 0 || piece.TriggerAmount == phase.TriggerAmount)
            && (phase.TriggerSide == MotionScoreSide.Any || piece.TriggerSide == MotionScoreSide.Any
                || piece.TriggerSide == phase.TriggerSide));
    }

    FrameworkElement BuildTemporaryPieceHost(MotionPiece piece)
    {
        var host = new Border
        {
            Width = piece.Width,
            Height = piece.Height,
            Background = piece.Kind == MotionPieceKind.Shape ? Brushes.Transparent : new SolidColorBrush(Paint.ColorOf(piece.Fill, Color.FromRgb(0x0B, 0x6B, 0x50))),
            BorderBrush = new SolidColorBrush(Paint.ColorOf(piece.Border, Color.FromRgb(0x38, 0xD7, 0xA3))),
            BorderThickness = new Thickness(piece.BorderThickness),
            Opacity = Math.Clamp(piece.Opacity, 0, 1),
            ClipToBounds = true,
            Child = ScorebugRender.CreateMotionPiece(piece, () => _engine.State, _catalog, _engine)
        };
        host.RenderTransformOrigin = new Point(0.5, 0.5);
        Canvas.SetLeft(host, piece.X);
        Canvas.SetTop(host, piece.Y);
        return host;
    }

    void ResetTransitionPreview(TransitionPreview preview)
    {
        foreach (var item in TimelineLayout.Elements)
            if (preview.Hosts.TryGetValue(item.Id, out var host))
                ResetPreview(host, item.Opacity);
        foreach (var (host, piece) in preview.Temporary)
        {
            host.Visibility = Visibility.Collapsed;
            ResetPreview(host, piece.Opacity);
        }
    }

    static void ShowTemporaryPieces(TransitionPreview preview)
    {
        foreach (var (host, _) in preview.Temporary) host.Visibility = Visibility.Visible;
    }

    void ApplyTransitionAt(TransitionPreview preview, MotionPhase phase, double time)
    {
        foreach (var item in TimelineLayout.Elements)
            if (preview.Hosts.TryGetValue(item.Id, out var host))
                MotionPlayback.ApplyAt(host, phase, time, item.Id);
        foreach (var (host, piece) in preview.Temporary)
            MotionPlayback.ApplyAt(host, phase, time, piece.Id);
    }

    void StartPreviewTimer(double seconds, Action next)
    {
        _previewTimer.Stop();
        _previewNext = next;
        _previewTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.05, seconds));
        _previewTimer.Start();
    }

    void PlayTransition()
    {
        if (_transitionPreview is not { } preview)
        {
            preview = BuildTransitionPreview();
            _transitionPreview = preview;
            _stage.Child = preview.Root;
        }
        var exit = _clip.ExitPhase;
        var enter = _clip.EnterPhase;
        void Enter()
        {
            ResetTransitionPreview(preview);
            ShowTemporaryPieces(preview);
            if (enter is not null) PlayPreviewPhase(preview, enter);
            _status.Text = $"Playing {_clip.Name} · {enter?.Name ?? "Enter"}";
            if (enter is { Enabled: true, Seconds: > 0.001 })
                StartPreviewTimer(PreviewDuration(enter), () =>
                {
                    ResetTransitionPreview(preview);
                    _status.Text = $"Played {_clip.Name}.";
                });
            else
            {
                ResetTransitionPreview(preview);
                _status.Text = $"Played {_clip.Name}.";
            }
        }
        ResetTransitionPreview(preview);
        ShowTemporaryPieces(preview);
        if (exit is { Enabled: true, IsEmpty: false } && exit.Seconds > 0.001)
        {
            PlayPreviewPhase(preview, exit);
            _status.Text = $"Playing {_clip.Name} · {exit.Name}";
            StartPreviewTimer(PreviewDuration(exit), Enter);
        }
        else Enter();
    }

    double PreviewDuration(MotionPhase phase) =>
        phase.Seconds + MaxOrderDelay + 0.12;

    void PlayPreviewPhase(TransitionPreview preview, MotionPhase phase)
    {
        foreach (var item in TimelineLayout.Elements)
            if (preview.Hosts.TryGetValue(item.Id, out var host))
                PlayPreviewHost(host, phase, item.Opacity, item.Id);
        foreach (var (host, piece) in preview.Temporary)
            PlayPreviewHost(host, phase, piece.Opacity, piece.Id);
    }

    void PlayPreviewHost(FrameworkElement host, MotionPhase phase, double opacity, string pieceId)
    {
        var delay = DelayFor(pieceId);
        if (delay <= 0.001)
        {
            MotionPlayback.PlayPhase(host, phase, Math.Clamp(opacity, 0, 1), pieceId: pieceId);
            return;
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _previewStaggers.Remove(timer);
            MotionPlayback.PlayPhase(host, phase, Math.Clamp(opacity, 0, 1), pieceId: pieceId);
        };
        _previewStaggers.Add(timer);
        timer.Start();
    }

    void ClearPreviewStaggers()
    {
        foreach (var timer in _previewStaggers) timer.Stop();
        _previewStaggers.Clear();
    }

    LayoutChoice? ExitChoice => _exitLayoutPicker.SelectedItem as LayoutChoice;
    LayoutChoice? EnterChoice => _enterLayoutPicker.SelectedItem as LayoutChoice;
    LayoutChoice? MotionChoice => _motionLayoutPicker.SelectedItem as LayoutChoice;

    void BuildStage()
    {
        StopPlayback();
        _transitionPreview = BuildTransitionPreview();
        _stage.Child = _transitionPreview.Root;
        _shown = null;
        RefreshStagePaint();
        DrawTimeline();
    }

    void RefreshStagePaint()
    {
        // The stand-in plate follows the clip's first colour key so the paint is visible before play.
        // A real layout keeps its own paint; its colour keys run during playback.
        var phase = CurrentPhase ?? _clip.EnterPhase;
        var textColor = phase?.Colors.FirstOrDefault(track => track.Role == MotionColorRole.Text)?.Keys.FirstOrDefault()?.Color;
        var plateColor = phase?.Colors.FirstOrDefault(track => track.Role == MotionColorRole.Background)?.Keys.FirstOrDefault()?.Color;
        var borderColor = phase?.Colors.FirstOrDefault(track => track.Role == MotionColorRole.Border)?.Keys.FirstOrDefault()?.Color;
        _plateText.Foreground = new SolidColorBrush(Paint.ColorOf(textColor, Colors.White));
        _plate.Background = new SolidColorBrush(Paint.ColorOf(plateColor, Color.FromRgb(0x1B, 0x24, 0x30)));
        _plate.BorderBrush = new SolidColorBrush(Paint.ColorOf(borderColor, Color.FromRgb(0x38, 0xD7, 0xA3)));
        _plateText.Text = _kind == MotionKind.Transition ? "LOWER THIRD" : "21";
    }

    void Play()
    {
        StopPlayback();
        _clip.Normalize();
        _status.Text = $"Playing {_clip.Name} · {_clip.TotalSeconds:0.##}s";
        if (_kind == MotionKind.Transition)
        {
            PlayTransition();
        }
        else
        {
            var phase = CurrentPhase ?? _clip.Phases.FirstOrDefault();
            var preview = _transitionPreview ?? BuildTransitionPreview();
            _transitionPreview = preview;
            _stage.Child = preview.Root;
            if (phase is not null)
            {
                ResetTransitionPreview(preview);
                ShowTemporaryPieces(preview);
                PlayPreviewPhase(preview, phase);
                _status.Text = $"Playing {_clip.Name} · {phase.Name}";
                StartPreviewTimer(PreviewDuration(phase), () =>
                {
                    ResetTransitionPreview(preview);
                    _status.Text = $"Played {_clip.Name}.";
                });
            }
        }
        _clock.Restart();
        _ticker.Start();
    }

    void TrackPlayhead()
    {
        Canvas.SetLeft(_playhead, XForGlobal(_clock.Elapsed.TotalSeconds));
        if (_clock.Elapsed.TotalSeconds > _clip.TotalSeconds + 0.15) _ticker.Stop();
    }

    void StopPlayback()
    {
        _clock.Reset();
        _ticker.Stop();
        _previewTimer.Stop();
        _previewNext = null;
        ClearPreviewStaggers();
        if (_transitionPreview is { } preview) ResetTransitionPreview(preview);
        if (_shown is { } shown) MotionPlayback.Stop(shown.Element);
        MotionPlayback.Stop(_plate);
        _scrubbing = false;
    }

    // ---------- timeline ----------

    const double TimelineGutter = 172;
    const double TimelineHeader = 34;
    const double TimelineRowHeight = 34;

    double TimelineTrackWidth => Math.Max(20, _timeline.Width - TimelineGutter);

    /// <summary>
    /// The longest start delay on the timeline. Delayed items keep running after their phase's
    /// nominal end, so the ruler has to include that tail; otherwise every late key lands on the
    /// right edge and looks pinned to the switch break.
    /// </summary>
    double MaxOrderDelay => _timelineItems.Select(item => DelayFor(item.Id)).DefaultIfEmpty(0).Max();

    double TimelineTotalSeconds => Math.Max(0.1, _clip.TotalSeconds + MaxOrderDelay);

    double XForGlobal(double time) =>
        TimelineGutter + Math.Clamp(time / TimelineTotalSeconds, 0, 1) * TimelineTrackWidth;

    /// <summary>
    /// The item's zero-based slot in the authored order, or -1 when it runs with the layout. Only
    /// explicitly ordered items are delayed, so “Layout order” really does put everything together.
    /// </summary>
    int OrderOf(string id)
    {
        if (id.Length == 0) return -1;
        return _clip.PieceOrder?.FindIndex(candidate =>
            string.Equals(candidate, id, StringComparison.OrdinalIgnoreCase)) ?? -1;
    }

    double DelayFor(string id)
    {
        var order = OrderOf(id);
        return order <= 0 ? 0 : order * Math.Max(0, _clip.ItemStagger);
    }

    double PhaseStartFor(int phaseIndex) =>
        _clip.Phases.Take(Math.Max(0, phaseIndex)).Sum(phase => phase.Seconds);

    int PhaseIndexAt(double x)
    {
        var total = TimelineTotalSeconds;
        var global = Math.Clamp((x - TimelineGutter) / TimelineTrackWidth, 0, 1) * total;
        var cursor = 0.0;
        for (var i = 0; i < _clip.Phases.Count; i++)
        {
            cursor += _clip.Phases[i].Seconds;
            if (global <= cursor + 0.0001) return i;
        }
        return Math.Max(0, _clip.Phases.Count - 1);
    }

    void DrawTimeline()
    {
        _timeline.Children.Clear();
        var total = TimelineTotalSeconds;
        var rows = _timelineItems;
        var height = TimelineHeader + rows.Count * TimelineRowHeight + 14;
        _timeline.Height = Math.Max(330, height);
        var colors = new[]
        {
            Color.FromRgb(0x2A, 0x3A, 0x4A), Color.FromRgb(0x24, 0x4A, 0x3E),
            Color.FromRgb(0x3A, 0x2E, 0x4A), Color.FromRgb(0x4A, 0x3A, 0x24)
        };

        _timeline.Children.Add(new Rectangle
        {
            Width = Math.Max(1, _timeline.Width), Height = _timeline.Height,
            Fill = new SolidColorBrush(Color.FromRgb(0x0F, 0x15, 0x1C))
        });
        _timeline.Children.Add(new Rectangle
        {
            Width = TimelineGutter - 1, Height = _timeline.Height,
            Fill = new SolidColorBrush(Color.FromRgb(0x14, 0x1C, 0x25))
        });

        // Phase ruler across the top. Each block is a real take; triggered takes are outlined so
        // a +3 and the normal fallback are visually distinct.
        var start = 0.0;
        for (var i = 0; i < _clip.Phases.Count; i++)
        {
            var phase = _clip.Phases[i];
            var left = XForGlobal(start);
            var right = XForGlobal(start + phase.Seconds);
            var width = Math.Max(2, right - left);
            var selected = i == _phaseIndex;
            var block = new Rectangle
            {
                Width = width, Height = TimelineHeader - 5,
                Fill = new SolidColorBrush(phase.Enabled ? colors[i % colors.Length] : Color.FromRgb(0x1A, 0x20, 0x28)),
                Stroke = selected ? new SolidColorBrush(Color.FromRgb(0x38, 0xD7, 0xA3))
                    : phase.TriggerAmount > 0 ? new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA)) : Brushes.Transparent,
                StrokeThickness = selected ? 2 : 1,
                ToolTip = $"{phase.Name} · {phase.Seconds:0.##}s · {phase.TriggerLabel}"
            };
            Canvas.SetLeft(block, left);
            Canvas.SetTop(block, 2);
            _timeline.Children.Add(block);
            var label = new TextBlock
            {
                Text = $"{phase.Name} · {phase.Seconds:0.##}s",
                FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                Opacity = phase.Enabled ? 0.92 : 0.45, TextTrimming = TextTrimming.CharacterEllipsis,
                Width = Math.Max(2, width - 8)
            };
            Canvas.SetLeft(label, left + 4);
            Canvas.SetTop(label, 4);
            _timeline.Children.Add(label);
            if (phase.TriggerAmount > 0)
            {
                var trigger = new TextBlock
                {
                    Text = phase.TriggerLabel, FontSize = 8, Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0xB5, 0xFD)),
                    Width = Math.Max(2, width - 8), TextTrimming = TextTrimming.CharacterEllipsis
                };
                Canvas.SetLeft(trigger, left + 4);
                Canvas.SetTop(trigger, 16);
                _timeline.Children.Add(trigger);
            }
            start += phase.Seconds;
        }

        for (var tick = 0.0; tick <= total + 0.0001; tick += 0.1)
        {
            var strong = Math.Abs(tick * 2 - Math.Round(tick * 2)) < 0.01;
            var x = XForGlobal(tick);
            _timeline.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = TimelineHeader, Y2 = _timeline.Height,
                Stroke = new SolidColorBrush(strong ? Color.FromRgb(0x36, 0x43, 0x52) : Color.FromRgb(0x20, 0x29, 0x34)),
                StrokeThickness = 1
            });
        }

        var y = TimelineHeader;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var selected = string.Equals(row.Id, _pieceId, StringComparison.OrdinalIgnoreCase);
            var dragging = _timelineDragId is { Length: > 0 }
                && string.Equals(row.Id, _timelineDragId, StringComparison.OrdinalIgnoreCase);
            _timeline.Children.Add(new Rectangle
            {
                Width = Math.Max(1, _timeline.Width), Height = TimelineRowHeight,
                Fill = new SolidColorBrush(dragging ? Color.FromRgb(0x1E, 0x35, 0x31)
                    : selected ? Color.FromRgb(0x17, 0x2B, 0x2A)
                    : rowIndex % 2 == 0 ? Color.FromRgb(0x12, 0x19, 0x21) : Color.FromRgb(0x0F, 0x15, 0x1C))
            });
            Canvas.SetTop(_timeline.Children[^1], y);
            if (selected)
            {
                var accent = new Rectangle { Width = 3, Height = TimelineRowHeight, Fill = new SolidColorBrush(Color.FromRgb(0x38, 0xD7, 0xA3)) };
                Canvas.SetTop(accent, y);
                _timeline.Children.Add(accent);
            }
            if (row.Id.Length > 0)
            {
                var grip = new TextBlock
                {
                    Text = "⋮⋮",
                    FontSize = 9,
                    Opacity = dragging ? 1 : 0.42,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x98, 0xA8)),
                    Cursor = Cursors.SizeAll,
                    ToolTip = "Drag up or down to change when this item starts"
                };
                Canvas.SetLeft(grip, 4);
                Canvas.SetTop(grip, y + 9);
                _timeline.Children.Add(grip);
            }
            var name = new TextBlock
            {
                Text = row.Label, FontSize = 10, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = selected ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xB8, 0xC4, 0xD0)),
                Width = TimelineGutter - 36, TextTrimming = TextTrimming.CharacterEllipsis,
                Cursor = row.Id.Length == 0 ? Cursors.Arrow : Cursors.SizeAll,
                ToolTip = row.Id.Length == 0
                    ? "Whole-graphic keys move every item together"
                    : row.Temporary ? "Temporary piece — drag to reorder" : "Layout item — drag to reorder"
            };
            Canvas.SetLeft(name, row.Id.Length == 0 ? 12 : 22);
            Canvas.SetTop(name, y + 8);
            _timeline.Children.Add(name);
            var delay = DelayFor(row.Id);
            start = 0;
            for (var phaseIndex = 0; phaseIndex < _clip.Phases.Count; phaseIndex++)
            {
                var phase = _clip.Phases[phaseIndex];
                var left = XForGlobal(start + delay);
                var right = XForGlobal(start + delay + phase.Seconds);
                var bar = new Rectangle
                {
                    Width = Math.Max(2, right - left), Height = 16,
                    RadiusX = 4, RadiusY = 4,
                    Fill = new SolidColorBrush(selected ? Color.FromArgb(0x55, 0x38, 0xD7, 0xA3) : Color.FromArgb(0x28, 0x8B, 0xA8, 0xC4)),
                    ToolTip = $"{row.Label} · {phase.Name} starts {delay:0.##}s after the phase"
                };
                Canvas.SetLeft(bar, left);
                Canvas.SetTop(bar, y + 10);
                _timeline.Children.Add(bar);

                foreach (var track in phase.Tracks.Where(candidate => string.Equals(candidate.PieceId ?? "", row.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var key in track.Keys)
                    {
                        // The dot's time is the phase time plus the item's start delay. It must not
                        // be clamped to the phase end: a late item is still moving when the next
                        // phase begins, and pinning its keys made them impossible to retime.
                        var keyX = XForGlobal(start + delay + key.Time);
                        var dot = new Ellipse
                        {
                            Width = selected ? 11 : 8, Height = selected ? 11 : 8,
                            Fill = new SolidColorBrush(selected && ReferenceEquals(track, SelectedTrack) ? Color.FromRgb(0xF9, 0xE7, 0x9B) : Color.FromRgb(0x38, 0xD7, 0xA3)),
                            Stroke = selected ? new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x14)) : null,
                            StrokeThickness = selected ? 1.5 : 0,
                            Cursor = Cursors.SizeWE,
                            IsHitTestVisible = false,
                            Tag = key,
                            ToolTip = $"{row.Label} · {MotionClip.TargetLabel(track.Target)} = {key.Value:0.###} at {key.Time:0.##}s"
                        };
                        Canvas.SetLeft(dot, keyX - dot.Width / 2);
                        Canvas.SetTop(dot, y + 12.5);
                        _timeline.Children.Add(dot);
                    }
                }
                foreach (var colorTrack in phase.Colors.Where(candidate => string.Equals(candidate.PieceId ?? "", row.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var key in colorTrack.Keys)
                    {
                        var keyX = XForGlobal(start + delay + key.Time);
                        var diamond = new Polygon
                        {
                            Points = new PointCollection { new(0, 4), new(4, 0), new(8, 4), new(4, 8) },
                            Fill = new SolidColorBrush(Paint.ColorOf(key.Color, Colors.White)),
                            IsHitTestVisible = false,
                            Tag = key,
                            Cursor = Cursors.SizeWE,
                            ToolTip = $"{row.Label} · {MotionClip.RoleLabel(colorTrack.Role)} at {key.Time:0.##}s"
                        };
                        Canvas.SetLeft(diamond, keyX - 4);
                        Canvas.SetTop(diamond, y + 23);
                        _timeline.Children.Add(diamond);
                    }
                }
                start += phase.Seconds;
            }
            y += TimelineRowHeight;
        }

        if (rows.Count == 0)
        {
            var empty = new TextBlock { Text = "This layout has no items yet.", Foreground = Brushes.White, Opacity = 0.6, FontSize = 11 };
            Canvas.SetLeft(empty, TimelineGutter + 14);
            Canvas.SetTop(empty, TimelineHeader + 12);
            _timeline.Children.Add(empty);
        }
        Canvas.SetTop(_playhead, TimelineHeader - 3);
        Canvas.SetLeft(_playhead, XForGlobal(PhaseStart + DelayFor(_pieceId) + _scrubTime));
        _playhead.Y2 = _timeline.Height;
        _timeline.Children.Add(_playhead);

        // The insert line is where the row will land, not where the pointer is: it jumps between
        // rows, so the drop is predictable even on a fast drag.
        if (_timelineDragId is not null && _timelineDragTarget >= 0)
        {
            var dropY = Math.Clamp(TimelineHeader + _timelineDragTarget * TimelineRowHeight, TimelineHeader + 1, _timeline.Height - 1);
            _timeline.Children.Add(new Line
            {
                X1 = TimelineGutter - 8, X2 = _timeline.Width - 4, Y1 = dropY, Y2 = dropY,
                Stroke = new SolidColorBrush(Color.FromRgb(0xF9, 0xE7, 0x9B)), StrokeThickness = 3,
                IsHitTestVisible = false
            });
            var cap = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(0xF9, 0xE7, 0x9B)), IsHitTestVisible = false };
            Canvas.SetLeft(cap, TimelineGutter - 12);
            Canvas.SetTop(cap, dropY - 4);
            _timeline.Children.Add(cap);
        }
        _timelineSummary.Text = $"{TimelineLayout.Name} · {TimelineLayout.Elements.Count} items · {_clip.Pieces.Count} temporary · {_clip.Phases.Count} takes · {_clip.ItemStagger:0.##}s gap";
    }

    /// <summary>Where the selected phase begins on the whole clip's ruler.</summary>
    double PhaseStart => PhaseStartFor(_phaseIndex);

    void TimelineDown(object sender, MouseButtonEventArgs e)
    {
        if (CurrentPhase is null) return;
        _ticker.Stop();
        var point = e.GetPosition(_timeline);
        if (point.Y < TimelineHeader)
        {
            var phaseIndex = PhaseIndexAt(point.X);
            if (phaseIndex != _phaseIndex)
            {
                _phaseIndex = phaseIndex;
                _phaseList.SelectedIndex = phaseIndex;
                LoadPhase();
            }
            _scrubTime = TimeAt(point);
            Canvas.SetLeft(_playhead, XForGlobal(PhaseStart + DelayFor(_pieceId) + _scrubTime));
            DrawTimeline();
            // The ruler is the fast path: a double-click keys the picked property for the current
            // item, creating its track on first use.
            if (e.ClickCount == 2) AddKeyAtPlayhead();
            return;
        }
        var row = (int)((point.Y - TimelineHeader) / TimelineRowHeight);
        var onRow = row >= 0 && row < _timelineItems.Count;
        if (point.X < TimelineGutter)
        {
            if (onRow)
            {
                _timelineList.SelectedIndex = row;
                SelectTimelineItem();
                var item = _timelineItems[row];
                // The whole-graphic row is the layout itself and stays first. Every other row can
                // be dragged by its name; a plain click still just selects it.
                if (item.Id.Length > 0)
                {
                    _timelineDragId = item.Id;
                    _timelineDragTarget = -1;
                    _timelineDragStartY = point.Y;
                    _timelineDragMoved = false;
                    _timeline.CaptureMouse();
                }
            }
            return;
        }
        // Clicking a lane also picks that item, so a key always lands on the row under the pointer.
        if (onRow && !string.Equals(_timelineItems[row].Id, _pieceId, StringComparison.OrdinalIgnoreCase))
        {
            _timelineList.SelectedIndex = row;
            SelectTimelineItem();
        }
        // A key dot wins over scrubbing. Hit-testing happens here, on the canvas, rather than on
        // each tiny dot, so a key can be grabbed even when the playhead or another lane covers it.
        if (TryStartKeyDrag(point)) return;
        var atPhase = PhaseIndexAt(point.X);
        if (atPhase != _phaseIndex)
        {
            _phaseIndex = atPhase;
            _phaseList.SelectedIndex = atPhase;
            LoadPhase();
        }
        _scrubTime = TimeAt(point);
        var time = _scrubTime;
        _keyTime.Text = time.ToString("0.##", CultureInfo.CurrentCulture);
        Canvas.SetLeft(_playhead, XForGlobal(PhaseStart + DelayFor(_pieceId) + time));
        Canvas.SetTop(_playhead, TimelineHeader - 3);
        _playhead.Y2 = _timeline.Height;
        if (e.ClickCount == 2)
        {
            AddKeyAtPlayhead();
            return;
        }
        _scrubbing = true;
        _timeline.CaptureMouse();
        ScrubTo(point);
    }

    void TimelineMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
        var point = e.GetPosition(_timeline);
        if (_timelineDragId is not null)
        {
            if (!_timelineDragMoved && Math.Abs(point.Y - _timelineDragStartY) >= 4) _timelineDragMoved = true;
            if (_timelineDragMoved)
            {
                var insert = Math.Clamp((int)Math.Round((point.Y - TimelineHeader) / TimelineRowHeight), 1, _timelineItems.Count);
                if (insert != _timelineDragTarget)
                {
                    _timelineDragTarget = insert;
                    DrawTimeline();
                }
                var label = _timelineItems.FirstOrDefault(item => string.Equals(item.Id, _timelineDragId, StringComparison.OrdinalIgnoreCase))?.Label ?? "item";
                _status.Text = $"Moving {label} to position {insert + 1}. Release to drop.";
            }
            return;
        }
        if (_dragKey is not null && _dragTrack is not null)
        {
            RetimeKey(_dragTrack, _dragKey, point);
            return;
        }
        if (_dragColorKey is not null && _dragColorTrack is not null)
        {
            RetimeColorKey(_dragColorTrack, _dragColorKey, point);
            return;
        }
        if (_scrubbing) ScrubTo(point);
    }

    void TimelineUp(object sender, MouseButtonEventArgs e)
    {
        if (_timelineDragId is not null)
        {
            var id = _timelineDragId;
            var target = _timelineDragTarget;
            var moved = _timelineDragMoved;
            _timelineDragId = null;
            _timelineDragTarget = -1;
            _timelineDragMoved = false;
            _timeline.ReleaseMouseCapture();
            if (moved && target >= 0) CommitTimelineDrop(id, target);
            else DrawTimeline();
            return;
        }
        if (_dragColorKey is not null && _dragColorTrack is not null)
        {
            var track = _dragColorTrack;
            var dragged = _dragColorKey;
            track.Normalize(CurrentPhase?.Seconds ?? 5);
            _dragColorTrack = null;
            _dragColorKey = null;
            _dragDiamond = null;
            _timeline.ReleaseMouseCapture();
            var kept = track.Keys.Contains(dragged)
                ? dragged
                : track.Keys.OrderBy(key => Math.Abs(key.Time - dragged.Time)).FirstOrDefault();
            if (kept is not null)
            {
                SelectColorForEditing(track, kept);
                BuildStage();
                PreviewAt(kept.Time);
                _status.Text = $"Retimed {MotionClip.RoleLabel(track.Role)} to {kept.Time:0.##}s.";
            }
            return;
        }
        if (_dragKey is not null && _dragTrack is not null)
        {
            var track = _dragTrack;
            var dragged = _dragKey;
            track.Normalize(CurrentPhase?.Seconds ?? 5);
            _dragTrack = null;
            _dragKey = null;
            _dragDot = null;
            _timeline.ReleaseMouseCapture();
            // Normalize can merge a key dropped exactly onto another one. Keep a real key
            // selected either way, so the inspector never goes blank after a retime.
            var kept = track.Keys.Contains(dragged)
                ? dragged
                : track.Keys.OrderBy(key => Math.Abs(key.Time - dragged.Time)).FirstOrDefault();
            if (kept is not null)
            {
                SelectKeyForEditing(track, kept);
                BuildStage();
                PreviewAt(kept.Time);
                _status.Text = $"Retimed {MotionClip.TargetLabel(track.Target)} to {kept.Time:0.##}s.";
            }
            return;
        }
        EndDrag();
    }

    void EndDrag()
    {
        if (_timeline.IsMouseCaptured) _timeline.ReleaseMouseCapture();
        if (_scrubbing)
        {
            _scrubbing = false;
            BuildStage();
        }
        _dragKey = null;
        _dragTrack = null;
        _dragDot = null;
        _dragColorKey = null;
        _dragColorTrack = null;
        _dragDiamond = null;
        if (_timelineDragId is not null)
        {
            _timelineDragId = null;
            _timelineDragTarget = -1;
            _timelineDragMoved = false;
            DrawTimeline();
        }
    }

    /// <summary>
    /// Finds a numeric or colour key under the pointer on any row and phase, selects it, and starts
    /// a retime drag. Keys are hit-tested against their drawn centres so a delayed item's keys are
    /// just as grabbable as one that starts at zero.
    /// </summary>
    bool TryStartKeyDrag(Point point)
    {
        if (CurrentPhase is null) return false;
        var rowIndex = (int)((point.Y - TimelineHeader) / TimelineRowHeight);
        if (rowIndex < 0 || rowIndex >= _timelineItems.Count) return false;
        var row = _timelineItems[rowIndex];
        var rowTop = TimelineHeader + rowIndex * TimelineRowHeight;
        MotionPhase? hitPhase = null;
        MotionTrack? hitTrack = null;
        MotionKey? hitKey = null;
        ColorTrack? hitColorTrack = null;
        ColorKey? hitColorKey = null;
        var hitDistance = double.MaxValue;
        for (var phaseIndex = 0; phaseIndex < _clip.Phases.Count; phaseIndex++)
        {
            var phase = _clip.Phases[phaseIndex];
            var start = PhaseStartFor(phaseIndex);
            foreach (var track in phase.Tracks.Where(candidate =>
                string.Equals(candidate.PieceId ?? "", row.Id, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var key in track.Keys)
                {
                    var dx = Math.Abs(point.X - XForGlobal(start + DelayFor(row.Id) + key.Time));
                    var dy = Math.Abs(point.Y - (rowTop + 18));
                    var distance = dx + dy;
                    if (dx > 10 || dy > 10 || distance >= hitDistance) continue;
                    hitPhase = phase;
                    hitTrack = track;
                    hitKey = key;
                    hitColorTrack = null;
                    hitColorKey = null;
                    hitDistance = distance;
                }
            }
            foreach (var track in phase.Colors.Where(candidate =>
                string.Equals(candidate.PieceId ?? "", row.Id, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var key in track.Keys)
                {
                    var dx = Math.Abs(point.X - XForGlobal(start + DelayFor(row.Id) + key.Time));
                    var dy = Math.Abs(point.Y - (rowTop + 27));
                    var distance = dx + dy;
                    if (dx > 10 || dy > 10 || distance >= hitDistance) continue;
                    hitPhase = phase;
                    hitColorTrack = track;
                    hitColorKey = key;
                    hitTrack = null;
                    hitKey = null;
                    hitDistance = distance;
                }
            }
        }
        if (hitPhase is null) return false;
        var phaseAt = _clip.Phases.IndexOf(hitPhase);
        if (phaseAt >= 0 && phaseAt != _phaseIndex)
        {
            _phaseIndex = phaseAt;
            _phaseList.SelectedIndex = phaseAt;
            LoadPhase();
        }
        if (hitTrack is not null && hitKey is not null) BeginKeyDrag(hitTrack, hitKey);
        else if (hitColorTrack is not null && hitColorKey is not null) BeginColorKeyDrag(hitColorTrack, hitColorKey);
        else return false;
        return true;
    }

    void BeginKeyDrag(MotionTrack track, MotionKey key)
    {
        StopPlayback();
        _ticker.Stop();
        SelectKeyForEditing(track, key);
        _dragTrack = track;
        _dragKey = key;
        _dragDot = KeyDotFor(key);
        _scrubTime = key.Time;
        _timeline.CaptureMouse();
        _keyTime.Text = key.Time.ToString("0.##", CultureInfo.CurrentCulture);
        Canvas.SetLeft(_playhead, XForGlobal(PhaseStart + DelayFor(track.PieceId) + key.Time));
        _status.Text = $"Retiming {MotionClip.TargetLabel(track.Target)} — drag left or right.";
    }

    void BeginColorKeyDrag(ColorTrack track, ColorKey key)
    {
        StopPlayback();
        _ticker.Stop();
        SelectColorForEditing(track, key);
        _dragColorTrack = track;
        _dragColorKey = key;
        _dragDiamond = ColorDiamondFor(key);
        _scrubTime = key.Time;
        _timeline.CaptureMouse();
        _colorTime.Text = key.Time.ToString("0.##", CultureInfo.CurrentCulture);
        Canvas.SetLeft(_playhead, XForGlobal(PhaseStart + DelayFor(track.PieceId) + key.Time));
        _status.Text = $"Retiming {MotionClip.RoleLabel(track.Role)} — drag left or right.";
    }

    Ellipse? KeyDotFor(MotionKey key) =>
        _timeline.Children.OfType<Ellipse>().FirstOrDefault(dot => ReferenceEquals(dot.Tag, key));

    Polygon? ColorDiamondFor(ColorKey key) =>
        _timeline.Children.OfType<Polygon>().FirstOrDefault(diamond => ReferenceEquals(diamond.Tag, key));

    /// <summary>Selects a track and key in the inspector without losing the edit context.</summary>
    void SelectKeyForEditing(MotionTrack track, MotionKey key)
    {
        var tracks = PieceTracks();
        var trackIndex = tracks.IndexOf(track);
        if (trackIndex < 0) return;
        _trackIndex = trackIndex;
        _trackList.SelectedIndex = trackIndex;
        LoadTrack();
        var ordered = track.Keys.OrderBy(candidate => candidate.Time).ToList();
        var keyIndex = ordered.IndexOf(key);
        _keyIndex = keyIndex;
        _keyList.SelectedIndex = keyIndex;
        LoadKey();
    }

    void RetimeKey(MotionTrack track, MotionKey key, Point point)
    {
        var time = TimeAt(point, track.PieceId);
        key.Time = time;
        _scrubTime = time;
        _keyTime.Text = time.ToString("0.##", CultureInfo.CurrentCulture);
        var dot = _dragDot ?? KeyDotFor(key);
        if (dot is not null) Canvas.SetLeft(dot, XForGlobal(PhaseStart + DelayFor(track.PieceId) + time) - dot.Width / 2);
        Canvas.SetLeft(_playhead, XForGlobal(PhaseStart + DelayFor(track.PieceId) + time));
        _status.Text = $"Retiming {MotionClip.TargetLabel(track.Target)} to {time:0.##}s.";
    }

    void RetimeColorKey(ColorTrack track, ColorKey key, Point point)
    {
        var time = TimeAt(point, track.PieceId);
        key.Time = time;
        _scrubTime = time;
        _colorTime.Text = time.ToString("0.##", CultureInfo.CurrentCulture);
        var diamond = _dragDiamond ?? ColorDiamondFor(key);
        if (diamond is not null) Canvas.SetLeft(diamond, XForGlobal(PhaseStart + DelayFor(track.PieceId) + time) - 4);
        Canvas.SetLeft(_playhead, XForGlobal(PhaseStart + DelayFor(track.PieceId) + time));
        _status.Text = $"Retiming {MotionClip.RoleLabel(track.Role)} to {time:0.##}s.";
    }

    /// <summary>Selects a colour track and key in the inspector without losing the edit context.</summary>
    void SelectColorForEditing(ColorTrack track, ColorKey key)
    {
        RefreshColors();
        var rows = _colorList.Items.OfType<ColorRow>().ToList();
        var index = rows.FindIndex(row => ReferenceEquals(row.Key, key));
        _colorIndex = index;
        _colorList.SelectedIndex = index;
        LoadColor();
    }

    void ScrubTo(Point point)
    {
        var phaseIndex = PhaseIndexAt(point.X);
        if (phaseIndex != _phaseIndex)
        {
            _phaseIndex = phaseIndex;
            _phaseList.SelectedIndex = phaseIndex;
            LoadPhase();
        }
        var phase = CurrentPhase;
        if (phase is null) return;
        var time = Math.Clamp(TimeAt(point), 0, phase.Seconds);
        _scrubTime = time;
        Canvas.SetLeft(_playhead, XForGlobal(PhaseStart + DelayFor(_pieceId) + time));
        Canvas.SetTop(_playhead, TimelineHeader - 3);
        _playhead.Y2 = _timeline.Height;
        _keyTime.Text = time.ToString("0.##", CultureInfo.CurrentCulture);
        ShowPhaseSurface();
        if (_transitionPreview is { } preview)
        {
            ShowTemporaryPieces(preview);
            ApplyTransitionAt(preview, phase, time);
        }
        _status.Text = $"{phase.Name}: {time:0.##}s of {phase.Seconds:0.##}s.";
    }

    /// <summary>Puts the preview at a moment in the selected phase, so an edit is visible at once.</summary>
    void PreviewAt(double time)
    {
        var phase = CurrentPhase;
        if (phase is null) return;
        _scrubTime = Math.Clamp(time, 0, phase.Seconds);
        if (_transitionPreview is null) BuildStage();
        if (_transitionPreview is { } preview)
        {
            ShowTemporaryPieces(preview);
            ApplyTransitionAt(preview, phase, _scrubTime);
        }
        Canvas.SetLeft(_playhead, XForGlobal(PhaseStart + DelayFor(_pieceId) + _scrubTime));
        Canvas.SetTop(_playhead, TimelineHeader - 3);
        _playhead.Y2 = _timeline.Height;
    }

    /// <summary>While scrubbing, show the surface the selected phase belongs to.</summary>
    void ShowPhaseSurface()
    {
        if (_kind == MotionKind.Transition)
        {
            if (_transitionPreview is null) BuildStage();
            return;
        }
        if (_transitionPreview is null) BuildStage();
    }

    /// <summary>
    /// The ruler position as a time inside the selected item's own phase. The item's start delay is
    /// removed as well as the phase start, so a key at 0s sits at the beginning of its row and a
    /// drag maps back to the same key time it shows on screen.
    /// </summary>
    double TimeAt(Point point, string? pieceId = null)
    {
        var global = Math.Clamp((point.X - TimelineGutter) / TimelineTrackWidth, 0, 1) * TimelineTotalSeconds;
        return Math.Clamp(global - PhaseStart - DelayFor(pieceId ?? _pieceId), 0, CurrentPhase?.Seconds ?? 5);
    }

    // ---------- files and library ----------

    void SaveToLibrary()
    {
        _clip.Normalize();
        _clip.Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? _clip.Name : _nameBox.Text.Trim();
        if (_kind == MotionKind.Transition) _catalog.SaveTransition(_clip);
        else _catalog.SaveAnimation(_clip);
        RefreshAll();
        BuildStage();
        _status.Text = _kind == MotionKind.Transition
            ? $"Saved “{_clip.Name}”. It is now in the studio's Value change, Motion preset and Transition in lists."
            : $"Saved “{_clip.Name}”. It is now in the studio's Motion preset and Value change lists.";
    }

    void LoadFromLibrary()
    {
        if (_libraryList.SelectedItem is not LibraryRow row) { _status.Text = "Pick a saved clip first."; return; }
        _clip = row.Clip.Snapshot();
        _phaseIndex = 0;
        RefreshAll();
        // Rebuild the layout choices around the loaded clip, so the timeline edits the layout it
        // was authored on rather than whichever layout happened to be open before.
        RefreshLayoutChoices();
        RefreshPieces();
        BuildStage();
        DrawTimeline();
        _status.Text = $"Loaded “{row.Clip.Name}”.";
    }

    void DeleteFromLibrary()
    {
        if (_libraryList.SelectedItem is not LibraryRow row) return;
        if (_kind == MotionKind.Transition) _catalog.RemoveTransition(row.Clip.Id);
        else _catalog.RemoveAnimation(row.Clip.Id);
        _status.Text = $"Removed “{row.Clip.Name}” from the library. Layouts already using it keep their copy.";
    }

    void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = _kind == MotionKind.Transition ? "Export transition" : "Export animation",
            Filter = MotionFiles.Filter(_kind),
            FileName = SafeName(_clip.Name) + "." + MotionFiles.Extension(_kind)
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _clip.Normalize();
            MotionFiles.Save(dialog.FileName, _clip);
            _status.Text = $"Exported {System.IO.Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex) { _status.Text = $"Could not export: {ex.Message}"; }
    }

    void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = _kind == MotionKind.Transition ? "Import a transition" : "Import an animation",
            Filter = MotionFiles.Filter(_kind)
        };
        if (dialog.ShowDialog(this) != true) return;
        var clip = MotionFiles.Load(dialog.FileName, _kind);
        if (clip is null) { _status.Text = "That file is not a readable ScorePlus clip."; return; }
        _clip = clip;
        _phaseIndex = 0;
        RefreshAll();
        RefreshLayoutChoices();
        RefreshPieces();
        BuildStage();
        DrawTimeline();
        _status.Text = $"Imported {System.IO.Path.GetFileName(dialog.FileName)}. Save it to add it to the library.";
    }

    static string SafeName(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return "clip";
        foreach (var bad in System.IO.Path.GetInvalidFileNameChars()) trimmed = trimmed.Replace(bad, '-');
        return trimmed;
    }

    // ---------- row types ----------

    sealed class PhaseRow(MotionPhase phase, int index)
    {
        public override string ToString() =>
            $"{index + 1}. {phase.Name} · {phase.Seconds:0.##}s{(phase.Enabled ? "" : " · off")} · {phase.Tracks.Count} tracks · {phase.Colors.Sum(track => track.Keys.Count)} colors";
    }

    sealed class TrackRow(MotionTrack track)
    {
        public override string ToString() => $"{MotionClip.TargetLabel(track.Target)} · {track.Keys.Count} key{(track.Keys.Count == 1 ? "" : "s")}";
    }

    sealed class KeyRow(MotionKey key, int index)
    {
        public override string ToString() => $"{index + 1}.  {key.Time:0.##}s  →  {key.Value:0.###}  ·  {EaseLabel(key.Ease)}";
    }

    sealed class ColorRow(ColorTrack track, ColorKey key)
    {
        public ColorTrack Track => track;
        public ColorKey Key => key;
        public override string ToString() => $"{MotionClip.RoleLabel(track.Role)} · {key.Time:0.##}s · {key.Color}";
    }

    sealed class LibraryRow(MotionClip clip)
    {
        public MotionClip Clip => clip;
        public override string ToString() => $"{clip.Name}  —  {clip.Summary}";
    }

    /// <summary>The ease's own wording: "Out" alone says nothing on a keyframe row.</summary>
    static string EaseLabel(MotionEase ease) => ease switch
    {
        MotionEase.Linear => "Linear",
        MotionEase.In => "Ease in",
        MotionEase.Out => "Ease out",
        MotionEase.InOut => "Ease in-out",
        MotionEase.Back => "Back (overshoot)",
        MotionEase.Bounce => "Bounce",
        _ => "Elastic"
    };

    static string RoleLabel(MotionColorRole role) => role switch
    {
        MotionColorRole.Background => "Plate",
        MotionColorRole.Border => "Border",
        _ => "Text colour"
    };

    MotionEase SelectedEase => (_easePicker.SelectedItem as EaseOption)?.Value ?? MotionEase.Out;

    MotionColorRole SelectedRole => (_colorRolePicker.SelectedItem as RoleOption)?.Value ?? MotionColorRole.Text;

    void SelectEase(MotionEase ease) => _easePicker.SelectedItem = _easePicker.Items.OfType<EaseOption>()
        .FirstOrDefault(option => option.Value == ease) ?? _easePicker.Items.OfType<EaseOption>().FirstOrDefault();

    void SelectRole(MotionColorRole role) => _colorRolePicker.SelectedItem = _colorRolePicker.Items.OfType<RoleOption>()
        .FirstOrDefault(option => option.Value == role) ?? _colorRolePicker.Items.OfType<RoleOption>().FirstOrDefault();

    sealed record EaseOption(MotionEase Value, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record RoleOption(MotionColorRole Value, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record SideOption(MotionScoreSide Value, string Label)
    {
        public override string ToString() => Label;
    }

    sealed class TargetOption(MotionTarget target)
    {
        public MotionTarget Target => target;
        public override string ToString() => MotionClip.TargetLabel(target);
    }

    sealed record LayoutChoice(ScorebugLayout? Layout, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record PieceChoice(string Id, string Label, bool Temporary, MotionPiece? Piece)
    {
        public override string ToString() => Label;
    }
}
