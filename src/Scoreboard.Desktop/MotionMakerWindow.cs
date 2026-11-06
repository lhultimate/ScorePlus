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
    readonly Line _playhead = new() { Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)), StrokeThickness = 2, Y1 = 0, Y2 = 104 };
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
    Action? _previewNext;
    bool _scrubbing;
    MotionTrack? _dragTrack;
    MotionKey? _dragKey;
    Ellipse? _dragDot;

    public MotionMakerWindow(MotionKind kind, ScorebugCatalog catalog, GameEngine engine, string theme, MotionClip? start = null)
    {
        _kind = kind;
        _catalog = catalog;
        _engine = engine;
        _theme = theme;
        _clip = start?.Snapshot() ?? (kind == MotionKind.Transition ? MotionClip.NewTransition() : MotionClip.NewAnimation());

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

        Content = BuildNew();
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

    // ---------- layout ----------

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
            pieceButtons.Children.Add(ActionButton("Add temporary piece", AddTemporaryPiece));
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
    List<ScorebugElement> _timelineItems = [];

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
        var layout = (_timelineLayoutPicker.SelectedItem as LayoutChoice)?.Layout ?? _catalog.Layout;
        var ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _clip.PieceOrder ?? [])
            if (!ranks.ContainsKey(id)) ranks[id] = ranks.Count;
        _timelineItems = layout.Elements
            .OrderBy(item => ranks.TryGetValue(item.Id, out var rank) ? rank : int.MaxValue)
            .ToList();
        var selected = (_timelineList.SelectedItem as TimelineRow)?.Id;
        _timelineList.ItemsSource = _timelineItems
            .Select((item, index) => new TimelineRow(index + 1, item.Id, string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name))
            .ToList();
        _timelineList.SelectedIndex = selected is null ? -1
            : Math.Max(0, _timelineItems.FindIndex(item => string.Equals(item.Id, selected, StringComparison.OrdinalIgnoreCase)));
    }

    void MoveTimeline(int delta)
    {
        if (_timelineList.SelectedItem is not TimelineRow row) return;
        var index = _timelineItems.FindIndex(item => string.Equals(item.Id, row.Id, StringComparison.OrdinalIgnoreCase));
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _timelineItems.Count) return;
        var moved = _timelineItems[index];
        _timelineItems.RemoveAt(index);
        _timelineItems.Insert(target, moved);
        _clip.PieceOrder = _timelineItems.Select(item => item.Id).ToList();
        RefreshTimeline();
        _timelineList.SelectedIndex = target;
        _status.Text = $"Timeline: {moved.Name} is {target + 1} of {_timelineItems.Count}.";
    }

    void ResetTimeline()
    {
        _clip.PieceOrder = [];
        RefreshTimeline();
        _status.Text = "Timeline cleared: every item moves together.";
    }

    sealed record TimelineRow(int Position, string Id, string Label)
    {
        public override string ToString() => $"{Position}.  {Label}";
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
        pieceButtons.Children.Add(ActionButton("Add temporary piece", AddTemporaryPiece));
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
        _trackList.ItemsSource = tracks.Select(track => new TrackRow(track)).ToArray();
        if (_trackIndex >= 0 && _trackIndex < tracks.Count) _trackList.SelectedIndex = _trackIndex;
        _keyIndex = -1;
        _keyList.ItemsSource = null;
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
        var layout = _catalog.Layout;
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
        _pieceWidth.Text = piece.Width.ToString("0.##", CultureInfo.CurrentCulture);
        _pieceHeight.Text = piece.Height.ToString("0.##", CultureInfo.CurrentCulture);
        _pieceX.Text = piece.X.ToString("0.##", CultureInfo.CurrentCulture);
        _pieceY.Text = piece.Y.ToString("0.##", CultureInfo.CurrentCulture);
        _pieceFill.Text = piece.Fill;
        _pieceTextColor.Text = piece.TextColor;
    }

    void AddTemporaryPiece()
    {
        var layout = _catalog.Layout;
        var piece = new MotionPiece
        {
            Name = $"Piece {_clip.Pieces.Count + 1}",
            X = Math.Max(0, layout.Width / 2 - 160),
            Y = Math.Max(0, layout.Height / 2 - 55),
            Width = 320,
            Height = 110,
            Text = ""
        };
        _clip.Pieces.Add(piece);
        _pieceId = piece.Id;
        RefreshPieces();
        RefreshTracks();
        RefreshColors();
        DrawTimeline();
        BuildStage();
        _status.Text = $"Added temporary piece “{piece.Name}”. Key it on the timeline, then save the clip.";
    }

    void UpdateTemporaryPiece()
    {
        if (SelectedPiece is not { } piece) { _status.Text = "Select a temporary piece first."; return; }
        piece.Name = string.IsNullOrWhiteSpace(_pieceName.Text) ? piece.Name : _pieceName.Text.Trim();
        piece.Text = _pieceText.Text;
        piece.Width = Number(_pieceWidth.Text, piece.Width, 4, 7680);
        piece.Height = Number(_pieceHeight.Text, piece.Height, 4, 7680);
        piece.X = Number(_pieceX.Text, piece.X, -7680, 7680);
        piece.Y = Number(_pieceY.Text, piece.Y, -7680, 7680);
        piece.Fill = HexColor(_pieceFill.Text);
        piece.TextColor = HexColor(_pieceTextColor.Text);
        piece.Normalize();
        RefreshPieces();
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
            name = _catalog.Layout.Elements.FirstOrDefault(item => string.Equals(item.Id, _pieceId, StringComparison.OrdinalIgnoreCase))?.Name;
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
        foreach (var display in _catalog.Displays)
            foreach (var layout in display.Layouts)
                options.Add(new LayoutChoice(layout, $"{display.Name} · {layout.Name}"));
        SetChoices(_exitLayoutPicker, options, exitId);
        SetChoices(_enterLayoutPicker, options, enterId);
        SetChoices(_motionLayoutPicker, options, motionId);
        SetChoices(_timelineLayoutPicker, options, (_timelineLayoutPicker.SelectedItem as LayoutChoice)?.Layout?.Id ?? enterId);
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
        box.SelectedItem = options.FirstOrDefault(option => string.Equals(option.Layout?.Id, id, StringComparison.OrdinalIgnoreCase)) ?? options[0];
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
        _clip.Loop = _loopBox.IsChecked == true;
        _clip.PingPong = _pingBox.IsChecked == true;
        _clip.UseOnValueChange = _valueBox.IsChecked == true;
        _clip.Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? _clip.Name : _nameBox.Text.Trim();
        phase.Normalize();
        RefreshAll();
        BuildStage();
        _status.Text = $"Phase {phase.Name}: {phase.Seconds:0.##}s.";
    }

    void AddTrack()
    {
        var phase = CurrentPhase;
        if (phase is null) return;
        var target = _propertyPicker.SelectedItem is TargetOption option ? option.Target : MotionTarget.Opacity;
        if (PieceTracks().Any(track => track.Target == target))
        {
            _status.Text = $"{MotionClip.TargetLabel(target)} is already keyed on this piece in this phase.";
            return;
        }
        var track = new MotionTrack
        {
            Target = target,
            PieceId = _pieceId,
            Keys = [new MotionKey(0, Default(target)), new MotionKey(Math.Max(0.1, phase.Seconds), Default(target))]
        };
        phase.Tracks.Add(track);
        var addedIndex = PieceTracks().Count - 1;
        RefreshAll();
        _trackIndex = addedIndex;
        RefreshTracks();
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
        var track = SelectedTrack;
        if (track is null) { _status.Text = "Pick a track first, then add a key to it."; return; }
        var time = Number(_keyTime.Text, 0, 0, CurrentPhase?.Seconds ?? 5);
        var value = Number(_keyValue.Text, Default(track.Target), -100000, 100000);
        var ease = SelectedEase;
        track.Keys.RemoveAll(key => Math.Abs(key.Time - time) < 0.005);
        track.Keys.Add(new MotionKey(time, value, ease));
        track.Normalize(CurrentPhase?.Seconds ?? 5);
        RefreshTracks();
        var ordered = track.Keys.OrderBy(key => key.Time).ToList();
        _keyIndex = Math.Max(0, ordered.FindIndex(key => Math.Abs(key.Time - time) < 0.006));
        _keyList.SelectedIndex = _keyIndex;
        LoadKey();
        BuildStage();
    }

    void UpdateKey()
    {
        if (SelectedKey is not { } key) { _status.Text = "Select a key to update it."; return; }
        key.Time = Number(_keyTime.Text, key.Time, 0, CurrentPhase?.Seconds ?? 5);
        key.Value = Number(_keyValue.Text, key.Value, -100000, 100000);
        key.Ease = SelectedEase;
        SelectedTrack?.Normalize(CurrentPhase?.Seconds ?? 5);
        RefreshTracks();
        BuildStage();
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
    }

    void UpdateColor()
    {
        if (SelectedColorRow is not { } row) { _status.Text = "Select a color key to update it."; return; }
        row.Key.Time = Number(_colorTime.Text, row.Key.Time, 0, CurrentPhase?.Seconds ?? 5);
        row.Key.Color = HexColor(_colorValue.Text);
        row.Track.Normalize(CurrentPhase?.Seconds ?? 5);
        RefreshAll();
        BuildStage();
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
        var layout = _catalog.Layout;
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
        foreach (var piece in _clip.Pieces ?? [])
        {
            var host = BuildTemporaryPieceHost(piece);
            canvas.Children.Add(host);
            temporary.Add((host, piece));
        }
        return new TransitionPreview(
            new Viewbox { Child = canvas, Stretch = Stretch.Uniform },
            hosts, temporary, layout.Width, layout.Height);
    }

    FrameworkElement BuildTemporaryPieceHost(MotionPiece piece)
    {
        var text = new TextBlock
        {
            Text = piece.Text,
            FontSize = piece.FontSize,
            Foreground = new SolidColorBrush(Paint.ColorOf(piece.TextColor, Colors.White)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var host = new Border
        {
            Width = piece.Width,
            Height = piece.Height,
            Background = new SolidColorBrush(Paint.ColorOf(piece.Fill, Color.FromRgb(0x0B, 0x6B, 0x50))),
            BorderBrush = new SolidColorBrush(Paint.ColorOf(piece.Border, Color.FromRgb(0x38, 0xD7, 0xA3))),
            BorderThickness = new Thickness(piece.BorderThickness),
            Opacity = Math.Clamp(piece.Opacity, 0, 1),
            ClipToBounds = true,
            Child = text
        };
        host.RenderTransformOrigin = new Point(0.5, 0.5);
        Canvas.SetLeft(host, piece.X);
        Canvas.SetTop(host, piece.Y);
        return host;
    }

    void ResetTransitionPreview(TransitionPreview preview)
    {
        foreach (var item in _catalog.Layout.Elements)
            if (preview.Hosts.TryGetValue(item.Id, out var host))
                ResetPreview(host, item.Opacity);
        foreach (var (host, piece) in preview.Temporary)
            ResetPreview(host, piece.Opacity);
    }

    void ApplyTransitionAt(TransitionPreview preview, MotionPhase phase, double time)
    {
        foreach (var item in _catalog.Layout.Elements)
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
            foreach (var item in _catalog.Layout.Elements)
                if (preview.Hosts.TryGetValue(item.Id, out var host))
                    MotionPlayback.PlayPhase(host, enter, Math.Clamp(item.Opacity, 0, 1), pieceId: item.Id);
            foreach (var (host, piece) in preview.Temporary)
                MotionPlayback.PlayPhase(host, enter, Math.Clamp(piece.Opacity, 0, 1), pieceId: piece.Id);
            _status.Text = $"Playing {_clip.Name} · {enter?.Name ?? "Enter"}";
            if (enter is { Enabled: true, Seconds: > 0.001 })
                StartPreviewTimer(enter.Seconds, () =>
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
        if (exit is { Enabled: true, IsEmpty: false } && exit.Seconds > 0.001)
        {
            foreach (var item in _catalog.Layout.Elements)
                if (preview.Hosts.TryGetValue(item.Id, out var host))
                    MotionPlayback.PlayPhase(host, exit, Math.Clamp(item.Opacity, 0, 1), pieceId: item.Id);
            foreach (var (host, piece) in preview.Temporary)
                MotionPlayback.PlayPhase(host, exit, Math.Clamp(piece.Opacity, 0, 1), pieceId: piece.Id);
            _status.Text = $"Playing {_clip.Name} · {exit.Name}";
            StartPreviewTimer(exit.Seconds, Enter);
        }
        else Enter();
    }

    LayoutChoice? ExitChoice => _exitLayoutPicker.SelectedItem as LayoutChoice;
    LayoutChoice? EnterChoice => _enterLayoutPicker.SelectedItem as LayoutChoice;
    LayoutChoice? MotionChoice => _motionLayoutPicker.SelectedItem as LayoutChoice;

    void BuildStage()
    {
        StopPlayback();
        if (_kind == MotionKind.Transition)
        {
            _transitionPreview = BuildTransitionPreview();
            _stage.Child = _transitionPreview.Root;
            _shown = null;
            return;
        }
        var target = TargetFor(MotionChoice);
        ResetPreview(target.Element);
        ShowTarget(target);
        RefreshStagePaint();
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
            var target = TargetFor(MotionChoice);
            ResetPreview(target.Element);
            ShowTarget(target);
            MotionPlayback.PlayPhase(target.Element, phase, 1, () => _status.Text = $"Played {_clip.Name}.",
                repeat: _clip.Loop, pingPong: _clip.PingPong);
        }
        _clock.Restart();
        _ticker.Start();
    }

    void TrackPlayhead()
    {
        var total = Math.Max(0.1, _clip.TotalSeconds);
        var x = Math.Clamp(_clock.Elapsed.TotalSeconds / total, 0, 1) * _timeline.Width;
        Canvas.SetLeft(_playhead, x);
        if (_clock.Elapsed.TotalSeconds > total + 0.15) _ticker.Stop();
    }

    void StopPlayback()
    {
        _clock.Reset();
        _ticker.Stop();
        _previewTimer.Stop();
        _previewNext = null;
        if (_transitionPreview is { } preview) ResetTransitionPreview(preview);
        if (_shown is { } shown) MotionPlayback.Stop(shown.Element);
        MotionPlayback.Stop(_plate);
        _scrubbing = false;
    }

    // ---------- timeline ----------

    void DrawTimeline()
    {
        _timeline.Children.Clear();
        var total = Math.Max(0.1, _clip.TotalSeconds);
        double x = 0;
        var colors = new[]
        {
            Color.FromRgb(0x2A, 0x3A, 0x4A), Color.FromRgb(0x24, 0x4A, 0x3E),
            Color.FromRgb(0x3A, 0x2E, 0x4A), Color.FromRgb(0x4A, 0x3A, 0x24)
        };
        var index = 0;
        foreach (var phase in _clip.Phases)
        {
            var width = Math.Max(1, phase.Seconds / total * _timeline.Width);
            var block = new Rectangle
            {
                Width = width,
                Height = 44,
                Fill = new SolidColorBrush(phase.Enabled ? colors[index % colors.Length] : Color.FromRgb(0x1A, 0x20, 0x28))
            };
            Canvas.SetLeft(block, x);
            Canvas.SetTop(block, 0);
            _timeline.Children.Add(block);
            var label = new TextBlock
            {
                Text = $"{phase.Name} · {phase.Seconds:0.##}s",
                FontSize = 10,
                Foreground = Brushes.White,
                Opacity = phase.Enabled ? 0.9 : 0.5
            };
            Canvas.SetLeft(label, x + 5);
            Canvas.SetTop(label, 4);
            _timeline.Children.Add(label);
            x += width;
            index++;
        }
        // Ticks every tenth of a second, with a stronger line each half second.
        for (var tick = 0.0; tick <= total; tick += 0.1)
        {
            var strong = Math.Abs(tick * 2 - Math.Round(tick * 2)) < 0.01;
            _timeline.Children.Add(new Line
            {
                X1 = tick / total * _timeline.Width,
                X2 = tick / total * _timeline.Width,
                Y1 = 45,
                Y2 = strong ? 62 : 54,
                Stroke = new SolidColorBrush(strong ? Color.FromRgb(0x4A, 0x58, 0x6A) : Color.FromRgb(0x28, 0x32, 0x3F)),
                StrokeThickness = 1
            });
        }
        // The selected track's keys sit under the ruler; each dot can be dragged to retime its key.
        var track = SelectedTrack;
        var start = PhaseStart;
        if (track is not null)
        {
            foreach (var key in track.Keys.OrderBy(candidate => candidate.Time))
            {
                var dot = new Ellipse
                {
                    Width = 11,
                    Height = 11,
                    Fill = new SolidColorBrush(Color.FromRgb(0x38, 0xD7, 0xA3)),
                    Cursor = Cursors.SizeWE,
                    ToolTip = $"{MotionClip.TargetLabel(track.Target)} = {key.Value:0.###} at {key.Time:0.##}s — drag to retime"
                };
                Canvas.SetLeft(dot, Math.Clamp((start + key.Time) / total, 0, 1) * _timeline.Width - 5.5);
                Canvas.SetTop(dot, 67);
                var keyRef = key;
                dot.MouseLeftButtonDown += (_, e) => { BeginKeyDrag(track, keyRef, dot); e.Handled = true; };
                _timeline.Children.Add(dot);
            }
        }
        // Color keys are diamonds under the motion keys.
        var phaseNow = CurrentPhase;
        if (phaseNow is not null)
        {
            foreach (var colorTrack in phaseNow.Colors.Where(track => IsPiece(track.PieceId)))
            {
                foreach (var key in colorTrack.Keys)
                {
                    var diamond = new Polygon
                    {
                        Points = new PointCollection { new(4, 0), new(8, 4), new(4, 8), new(0, 4) },
                        Fill = new SolidColorBrush(Paint.ColorOf(key.Color, Colors.White)),
                        ToolTip = $"{MotionClip.RoleLabel(colorTrack.Role)} at {key.Time:0.##}s"
                    };
                    Canvas.SetLeft(diamond, Math.Clamp((start + key.Time) / total, 0, 1) * _timeline.Width - 4);
                    Canvas.SetTop(diamond, 82);
                    _timeline.Children.Add(diamond);
                }
            }
        }
        Canvas.SetTop(_playhead, 0);
        Canvas.SetLeft(_playhead, 0);
        _playhead.Y2 = 104;
        _timeline.Children.Add(_playhead);
    }

    /// <summary>Where the selected phase begins on the whole clip's ruler.</summary>
    double PhaseStart => _clip.Phases.Take(Math.Max(0, _phaseIndex)).Sum(phase => phase.Seconds);

    void TimelineDown(object sender, MouseButtonEventArgs e)
    {
        if (CurrentPhase is null) return;
        _ticker.Stop();
        var point = e.GetPosition(_timeline);
        var time = TimeAt(point);
        _keyTime.Text = time.ToString("0.##", CultureInfo.CurrentCulture);
        Canvas.SetLeft(_playhead, Math.Clamp(point.X, 0, _timeline.Width));
        if (e.ClickCount == 2)
        {
            if (SelectedTrack is not null) AddKey();
            else _status.Text = "Select a track first, then double-click the ruler to add a key.";
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
        if (_dragKey is not null && _dragTrack is not null)
        {
            var time = TimeAt(point);
            _dragKey.Time = time;
            _keyTime.Text = time.ToString("0.##", CultureInfo.CurrentCulture);
            if (_dragDot is not null) Canvas.SetLeft(_dragDot, Math.Clamp(point.X, 0, _timeline.Width) - 5.5);
            _status.Text = $"Retiming {MotionClip.TargetLabel(_dragTrack.Target)} to {time:0.##}s.";
            return;
        }
        if (_scrubbing) ScrubTo(point);
    }

    void TimelineUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragKey is not null && _dragTrack is not null)
        {
            var track = _dragTrack;
            var dragged = _dragKey;
            track.Normalize(CurrentPhase?.Seconds ?? 5);
            _dragTrack = null;
            _dragKey = null;
            _dragDot = null;
            _timeline.ReleaseMouseCapture();
            RefreshTracks();
            var ordered = track.Keys.OrderBy(key => key.Time).ToList();
            _keyIndex = ordered.IndexOf(dragged);
            _keyList.SelectedIndex = _keyIndex;
            LoadKey();
            BuildStage();
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
    }

    void BeginKeyDrag(MotionTrack track, MotionKey key, Ellipse dot)
    {
        StopPlayback();
        _ticker.Stop();
        _dragTrack = track;
        _dragKey = key;
        _dragDot = dot;
        _timeline.CaptureMouse();
        _keyTime.Text = key.Time.ToString("0.##", CultureInfo.CurrentCulture);
    }

    void ScrubTo(Point point)
    {
        var phase = CurrentPhase;
        if (phase is null) return;
        var total = Math.Max(0.1, _clip.TotalSeconds);
        var global = Math.Clamp(point.X / _timeline.Width, 0, 1) * total;
        var time = Math.Clamp(global - PhaseStart, 0, phase.Seconds);
        Canvas.SetLeft(_playhead, Math.Clamp(point.X, 0, _timeline.Width));
        _keyTime.Text = time.ToString("0.##", CultureInfo.CurrentCulture);
        ShowPhaseSurface();
        if (_kind == MotionKind.Transition)
        {
            if (_transitionPreview is { } preview) ApplyTransitionAt(preview, phase, time);
        }
        else if (_shown is { } shown) MotionPlayback.ApplyAt(shown.Element, phase, time);
        _status.Text = $"{phase.Name}: {time:0.##}s of {phase.Seconds:0.##}s.";
    }

    /// <summary>While scrubbing, show the surface the selected phase belongs to.</summary>
    void ShowPhaseSurface()
    {
        if (_kind == MotionKind.Transition)
        {
            if (_transitionPreview is null) BuildStage();
            return;
        }
        var target = TargetFor(MotionChoice);
        if (_shown?.Element != target.Element || Math.Abs((_shown?.Width ?? 0) - target.Width) > 0.5)
        {
            ResetPreview(target.Element);
            ShowTarget(target);
        }
    }

    /// <summary>The ruler position as a time inside the selected phase.</summary>
    double TimeAt(Point point)
    {
        var total = Math.Max(0.1, _clip.TotalSeconds);
        var global = Math.Clamp(point.X / _timeline.Width, 0, 1) * total;
        return Math.Clamp(global - PhaseStart, 0, CurrentPhase?.Seconds ?? 5);
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
        BuildStage();
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
        BuildStage();
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
