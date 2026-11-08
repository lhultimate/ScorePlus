using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Scoreboard.Core;
namespace Scoreboard.Desktop;
public partial class OutputWindow : Window
{
    readonly GameEngine _engine;
    readonly ScorebugCatalog _catalog;
    GameState _displayState;
    readonly ScorebugCanvasView _scorebugView;
    // Which display this window is the audience output for. Only that display's published layout
    // is ever drawn here, so two screens can be on air with completely different designs.
    readonly string _displayId;
    readonly AnnotationBoard _annotationBoard;
    string _broadcastTheme;
    // Annotation tools live above the graphic, so a pen stroke never edits or moves the layout.
    Grid? _annotationLayer;
    InkCanvas? _ink;
    // The ink sits in the scorebug's design space inside its own uniform Viewbox, so it lines up
    // with the artwork instead of filling the whole window and spilling into the letterbox bars.
    Viewbox? _annotationViewbox;
    Grid? _annotationSurface;
    Border? _annotationToolbar;
    Border? _annotationColorSwatch;
    TextBlock? _annotationStatus;
    TextBox? _annotationWidthBox;
    ComboBox? _annotationFadePicker;
    DispatcherTimer? _annotationFadeTimer;
    bool _highlighter;
    bool _eraser;
    Color _annotationColor = Color.FromRgb(255, 230, 0);
    double _annotationWidth = 3;
    bool _annotationFading;
    bool _settingAnnotationWidth;
    bool _settingAnnotationFade;
    bool _drawingStroke;
    Stroke? _activeStroke;
    Point _lastAnnotationPoint;
    const double AnnotationFadeMilliseconds = 180;
    public OutputWindow(GameEngine engine, ScorebugCatalog catalog,string broadcastTheme="Dark",MediaPlayback? playback=null,string? displayId=null, AutomationRuntime? automation=null, bool annotations=false, AnnotationBoard? annotationBoard=null)
    {
        InitializeComponent(); _broadcastTheme=broadcastTheme; _engine = engine; _catalog=catalog; _displayState=engine.CloneState();
        _displayId=(catalog.FindDisplay(displayId)??catalog.Displays[0]).Id;
        var saved = PreferencesStore.Load();
        _annotationWidth = Math.Clamp(double.IsFinite(saved.AnnotationWidth) ? saved.AnnotationWidth : 3, 1, 24);
        _annotationBoard = annotationBoard ?? new AnnotationBoard(AnnotationHoldFromSeconds(saved.AnnotationFadeSeconds));
        Title = $"ScorePlus — {DisplayName} Audience Output";
        // The audience only ever sees the layout that was published, never the one being designed.
        _scorebugView=new ScorebugCanvasView(catalog,engine,()=>_displayState,playback,muteVideo:false,layoutProvider:()=>_catalog.PublishedCompositeOf(_displayId),displayId:_displayId,automation:automation);
        ScorebugHost.Children.Add(_scorebugView); _engine.Changed+=Update; _engine.Changed+=_scorebugView.Refresh; _catalog.Changed+=Publish; Update();
        BuildAnnotationLayer();
        if(annotations)OpenAnnotationTools();
    }

    /// <summary>The display this window shows.</summary>
    public string DisplayId => _displayId;

    /// <summary>True while the pen/highlighter toolbar is available on this window.</summary>
    public bool AnnotationToolsOpen => _annotationToolbar?.Visibility == Visibility.Visible;

    /// <summary>Shows the annotation toolbar over the live audience picture.</summary>
    public void OpenAnnotationTools()
    {
        BuildAnnotationLayer();
        if (_annotationLayer is null) return;
        _annotationBoard.Open();
        _annotationLayer.Visibility = Visibility.Visible;
        if (_annotationToolbar is not null) _annotationToolbar.Visibility = Visibility.Visible;
        SetAnnotationTool(highlighter: false);
        SyncAnnotationWidthBox();
        SyncAnnotationFadePicker();
        UpdateAnnotationTitle();
    }

    /// <summary>
    /// Shows the same ink on an audience window without putting the drawing toolbar on the projector.
    /// MainWindow calls this for every other output on the display when the operator starts drawing.
    /// </summary>
    public void ShowAudienceInk()
    {
        BuildAnnotationLayer();
        if (_annotationLayer is null) return;
        _annotationLayer.Visibility = Visibility.Visible;
        if (_annotationToolbar is not null) _annotationToolbar.Visibility = Visibility.Collapsed;
        UpdateAnnotationTitle();
    }

    public void CloseAnnotationTools()
    {
        FinishLiveStroke();
        _annotationBoard.Dismiss();
        HideAnnotationLayer();
    }

    void HideAnnotationLayer()
    {
        if (_annotationLayer is not null) _annotationLayer.Visibility = Visibility.Collapsed;
        if (_annotationToolbar is not null) _annotationToolbar.Visibility = Visibility.Collapsed;
        ClearAnnotations();
        UpdateAnnotationTitle();
    }

    /// <summary>The pen width shown in the toolbar; also lets the checks verify typing a width.</summary>
    public double AnnotationWidth => _annotationWidth;

    /// <summary>The design space the pen draws in; it follows the on-air layout.</summary>
    public Size AnnotationSurfaceSize => _annotationSurface is null
        ? Size.Empty
        : new Size(_annotationSurface.Width, _annotationSurface.Height);

    /// <summary>How long ink stays before the quick fade; zero means permanent.</summary>
    public TimeSpan AnnotationFadeDelay => _annotationBoard.Hold;

    void BuildAnnotationLayer()
    {
        if (_annotationLayer is not null || Content is not Grid root) return;
        _annotationLayer = new Grid { Visibility = Visibility.Collapsed };
        // The ink is arranged in the scorebug's design space and stretched by a Viewbox of its own,
        // so a stroke lands on the graphic on every output however the window is shaped. A
        // window-sized canvas drew the pen in the black bars above the artwork on a tall monitor.
        _annotationSurface = new Grid { Width = 1280, Height = 720 };
        _annotationViewbox = new Viewbox { Stretch = Stretch.Uniform, Child = _annotationSurface };
        _annotationViewbox.SizeChanged += (_, _) => ApplyAnnotationDrawingAttributes();
        _annotationLayer.Children.Add(_annotationViewbox);
        // Pen and highlighter are drawn by this window rather than by InkCanvas's private dynamic
        // layer. The growing Stroke is added to the shared collection on mouse-down, so every
        // audience window sees each point immediately instead of waiting for mouse-up.
        _ink = new InkCanvas
        {
            Background = Brushes.Transparent,
            EditingMode = InkCanvasEditingMode.None,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _annotationSurface.Children.Add(_ink);
        // The board owns the stroke collection, so every audience window for this display draws the
        // same ink instead of each window having its own invisible copy.
        _ink.Strokes = _annotationBoard.Strokes;
        _ink.StrokeCollected += (_, _) => RestartAnnotationHold();
        _ink.Strokes.StrokesChanged += (_, _) =>
        {
            // A stroke under the hand is not finished: its hold starts only on mouse-up.
            if (!_annotationFading && !_annotationBoard.IsDrawing) RestartAnnotationHold();
        };
        _ink.PreviewMouseDown += (_, _) => CancelAnnotationFade();
        _ink.PreviewStylusDown += (_, _) => CancelAnnotationFade();
        // handledEventsToo: InkCanvas still sees the pointer even with its own editing mode off, and
        // must not be able to swallow the press before the live stroke starts.
        _ink.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(Ink_PreviewMouseLeftButtonDown), handledEventsToo: true);
        _ink.AddHandler(UIElement.PreviewMouseMoveEvent,
            new MouseEventHandler(Ink_PreviewMouseMove), handledEventsToo: true);
        _ink.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(Ink_PreviewMouseLeftButtonUp), handledEventsToo: true);
        _ink.LostMouseCapture += (_, _) => FinishLiveStroke();
        _annotationBoard.HoldChanged += AnnotationBoard_HoldChanged;
        _annotationBoard.Dismissed += HideAnnotationLayer;
        _annotationBoard.StrokeStarted += CancelAnnotationFade;
        _annotationBoard.StrokeFinished += RestartAnnotationHold;

        _annotationFadeTimer = new DispatcherTimer { Interval = _annotationBoard.Hold };
        _annotationFadeTimer.Tick += (_, _) => BeginAnnotationFade();

        var bar = new Border
        {
            // A fresh audience window has no toolbar until the operator asks for one. Without this,
            // its default Visible state made every output report AnnotationToolsOpen = true.
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(232, 15, 24, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(58, 69, 82)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 5, 8, 5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12, 12, 12, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal }
        };
        _annotationToolbar = bar;
        var panel = (StackPanel)bar.Child;
        panel.Children.Add(ToolbarButton("Pen", (_, _) => SetAnnotationTool(highlighter: false)));
        panel.Children.Add(ToolbarButton("Highlighter", (_, _) => SetAnnotationTool(highlighter: true)));
        panel.Children.Add(ToolbarButton("Eraser", (_, _) => SetAnnotationEraser()));
        panel.Children.Add(ToolbarSeparator());

        var colour = ToolbarButton("Colour", (_, _) => ChooseAnnotationColour());
        _annotationColorSwatch = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(_annotationColor),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 3, 0)
        };
        colour.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _annotationColorSwatch, new TextBlock { Text = "Colour", VerticalAlignment = VerticalAlignment.Center } }
        };
        panel.Children.Add(colour);
        panel.Children.Add(ToolbarButton("−", (_, _) => ChangeAnnotationWidth(-1)));
        _annotationWidthBox = new TextBox
        {
            Text = _annotationWidth.ToString("0.#", CultureInfo.InvariantCulture),
            Width = 44, MinWidth = 44, MaxWidth = 44, Height = 24,
            FontSize = 11, Padding = new Thickness(2, 0, 2, 0),
            Margin = new Thickness(2, 0, 2, 0), TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(11, 17, 24)),
            Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(58, 69, 82)),
            ToolTip = "Pen width — type 1–24 or use − / +"
        };
        _annotationWidthBox.TextChanged += (_, _) =>
        {
            if (_settingAnnotationWidth) return;
            if (!double.TryParse(_annotationWidthBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var width)
                || width < 1 || width > 24) return;
            _annotationWidth = width;
            ApplyAnnotationDrawingAttributes();
            RefreshAnnotationStatus();
            SaveAnnotationPreferences();
        };
        _annotationWidthBox.LostFocus += (_, _) => CommitAnnotationWidth();
        _annotationWidthBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            CommitAnnotationWidth();
            e.Handled = true;
        };
        panel.Children.Add(_annotationWidthBox);
        panel.Children.Add(ToolbarButton("+", (_, _) => ChangeAnnotationWidth(1)));
        panel.Children.Add(ToolbarSeparator());
        panel.Children.Add(new TextBlock
        {
            Text = "Fade", Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)),
            FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0)
        });
        _annotationFadePicker = new ComboBox
        {
            Width = 82, Height = 24, MinHeight = 24, FontSize = 10,
            Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(2, 0, 2, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ItemsSource = new[] { "0.5 s", "1 s", "1.5 s", "2 s", "3 s", "5 s", "10 s", "Permanent" },
            SelectedItem = FadeLabel(_annotationBoard.Hold),
            ToolTip = "How long ink stays before it fades; Permanent keeps it until Clear"
        };
        _annotationFadePicker.SelectionChanged += (_, _) =>
        {
            if (_settingAnnotationFade || _annotationFadePicker.SelectedItem is not string label) return;
            var hold = label == "Permanent"
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(double.TryParse(label.Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                    ? seconds : 1);
            _annotationBoard.SetHold(hold);
            SaveAnnotationPreferences();
        };
        panel.Children.Add(_annotationFadePicker);
        panel.Children.Add(ToolbarSeparator());
        panel.Children.Add(ToolbarButton("Undo", (_, _) => UndoAnnotation()));
        panel.Children.Add(ToolbarButton("Clear", (_, _) => ClearAnnotations()));
        panel.Children.Add(ToolbarButton("Done", (_, _) => CloseAnnotationTools()));
        _annotationStatus = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)),
            FontSize = 10, Margin = new Thickness(10, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(_annotationStatus);
        RefreshAnnotationStatus();
        _annotationLayer.Children.Add(bar);
        SyncAnnotationSurface();
        root.Children.Add(_annotationLayer);
    }

    Button ToolbarButton(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, FontSize = 11, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(2, 0, 2, 0) };
        button.Click += click;
        return button;
    }

    Border ToolbarSeparator() => new()
    {
        Width = 1, Background = new SolidColorBrush(Color.FromRgb(58, 69, 82)),
        Margin = new Thickness(4, 2, 4, 2)
    };

    void SetAnnotationTool(bool highlighter, bool eraser = false)
    {
        if (_ink is null) return;
        if (_drawingStroke) FinishLiveStroke();
        _highlighter = highlighter;
        _eraser = eraser;
        // Custom strokes keep the shared collection live. The eraser still uses InkCanvas hit-testing.
        _ink.EditingMode = eraser ? InkCanvasEditingMode.EraseByStroke : InkCanvasEditingMode.None;
        if (!eraser) ApplyAnnotationDrawingAttributes();
        RefreshAnnotationStatus();
    }

    void Ink_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!AnnotationToolsOpen || _eraser || _ink is null) return;
        var point = e.GetPosition(_ink);
        if (point.X < 0 || point.Y < 0 || point.X > _ink.ActualWidth || point.Y > _ink.ActualHeight) return;
        if (_drawingStroke) FinishLiveStroke();
        _annotationBoard.BeginStroke();
        CancelAnnotationFade();
        var stroke = new Stroke(new StylusPointCollection { new StylusPoint(point.X, point.Y) },
            _ink.DefaultDrawingAttributes.Clone());
        _activeStroke = stroke;
        _lastAnnotationPoint = point;
        _drawingStroke = true;
        _ink.Strokes.Add(stroke);
        _ink.CaptureMouse();
        e.Handled = true;
    }

    void Ink_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_drawingStroke || _activeStroke is null || _ink is null) return;
        var point = e.GetPosition(_ink);
        if ((point - _lastAnnotationPoint).Length < 0.75) return;
        _activeStroke.StylusPoints.Add(new StylusPoint(point.X, point.Y));
        _lastAnnotationPoint = point;
        e.Handled = true;
    }

    void Ink_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawingStroke) return;
        FinishLiveStroke();
        e.Handled = true;
    }

    void FinishLiveStroke()
    {
        if (!_drawingStroke) return;
        _drawingStroke = false;
        _activeStroke = null;
        if (_ink?.IsMouseCaptured == true) _ink.ReleaseMouseCapture();
        _annotationBoard.EndStroke();
        RestartAnnotationHold();
    }

    void ApplyAnnotationDrawingAttributes()
    {
        if (_ink is null) return;
        var scale = AnnotationScale;
        var pen = _annotationWidth / scale;
        var colour = _highlighter ? Color.FromArgb(0x66, _annotationColor.R, _annotationColor.G, _annotationColor.B) : _annotationColor;
        _ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = colour,
            Width = _highlighter ? Math.Max(12 / scale, pen * 5) : pen,
            Height = _highlighter ? Math.Max(12 / scale, pen * 5) : pen,
            StylusTip = StylusTip.Ellipse,
            FitToCurve = true,
            IgnorePressure = _highlighter
        };
    }

    /// <summary>
    /// Keeps the pen on the scorebug's design space, so a tall window that letterboxes the graphic
    /// moves the pen with it instead of leaving it above the artwork.
    /// </summary>
    void SyncAnnotationSurface()
    {
        if (_annotationSurface is null) return;
        var layout = _scorebugView.Layout;
        var width = Math.Max(1, layout.Width);
        var height = Math.Max(1, layout.Height);
        if (Math.Abs(_annotationSurface.Width - width) < 0.5
            && Math.Abs(_annotationSurface.Height - height) < 0.5) return;
        _annotationSurface.Width = width;
        _annotationSurface.Height = height;
        ApplyAnnotationDrawingAttributes();
    }

    /// <summary>How many screen pixels one design unit covers in this window.</summary>
    double AnnotationScale
    {
        get
        {
            if (_annotationViewbox is null || _annotationSurface is null) return 1;
            var scale = Math.Min(
                _annotationViewbox.ActualWidth / Math.Max(1, _annotationSurface.Width),
                _annotationViewbox.ActualHeight / Math.Max(1, _annotationSurface.Height));
            return double.IsFinite(scale) && scale > 0.01 ? scale : 1;
        }
    }

    void RefreshAnnotationStatus()
    {
        if (_annotationStatus is null) return;
        _annotationStatus.Text = _eraser
            ? "ERASER · click a stroke"
            : _highlighter
                ? $"HIGHLIGHTER · {_annotationWidth:0.#} px · {AnnotationFadeLabel}"
                : $"PEN · {_annotationWidth:0.#} px · {AnnotationFadeLabel}";
    }

    string AnnotationFadeLabel => _annotationBoard.Hold <= TimeSpan.Zero
        ? "permanent"
        : $"{_annotationBoard.Hold.TotalSeconds:0.#} s fade";

    static TimeSpan AnnotationHoldFromSeconds(double seconds) =>
        !double.IsFinite(seconds) || seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Clamp(seconds, 0.5, 10));

    static string FadeLabel(TimeSpan hold) => hold <= TimeSpan.Zero
        ? "Permanent"
        : $"{hold.TotalSeconds:0.#} s";

    void SyncAnnotationFadePicker()
    {
        if (_annotationFadePicker is null) return;
        _settingAnnotationFade = true;
        try { _annotationFadePicker.SelectedItem = FadeLabel(_annotationBoard.Hold); }
        finally { _settingAnnotationFade = false; }
    }

    void AnnotationBoard_HoldChanged()
    {
        if (_annotationFadeTimer is not null) _annotationFadeTimer.Interval = _annotationBoard.Hold;
        SyncAnnotationFadePicker();
        RestartAnnotationHold();
        RefreshAnnotationStatus();
    }

    void SaveAnnotationPreferences()
    {
        try
        {
            var preferences = PreferencesStore.Load();
            preferences.AnnotationWidth = _annotationWidth;
            preferences.AnnotationFadeSeconds = _annotationBoard.Hold <= TimeSpan.Zero
                ? 0
                : _annotationBoard.Hold.TotalSeconds;
            PreferencesStore.Save(preferences);
        }
        catch { /* the drawing toolbar must keep working even if preferences cannot be written */ }
    }

    void SetAnnotationEraser() => SetAnnotationTool(highlighter: false, eraser: true);

    void ChooseAnnotationColour()
    {
        var chosen = ColorPickerWindow.Pick(this, $"#{_annotationColor.A:X2}{_annotationColor.R:X2}{_annotationColor.G:X2}{_annotationColor.B:X2}");
        if (chosen is null) return;
        try
        {
            if (ColorConverter.ConvertFromString(chosen) is Color colour)
            {
                _annotationColor = colour;
                if (_annotationColorSwatch is not null) _annotationColorSwatch.Background = new SolidColorBrush(colour);
                SetAnnotationTool(_highlighter);
            }
        }
        catch { }
    }

    void ChangeAnnotationWidth(double delta)
    {
        _annotationWidth = Math.Clamp(_annotationWidth + delta, 1, 24);
        SyncAnnotationWidthBox();
        ApplyAnnotationDrawingAttributes();
        RefreshAnnotationStatus();
        SaveAnnotationPreferences();
    }

    void CommitAnnotationWidth()
    {
        if (_annotationWidthBox is null) return;
        if (double.TryParse(_annotationWidthBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var width))
            _annotationWidth = Math.Clamp(width, 1, 24);
        SyncAnnotationWidthBox();
        ApplyAnnotationDrawingAttributes();
        RefreshAnnotationStatus();
        SaveAnnotationPreferences();
    }

    void SyncAnnotationWidthBox()
    {
        if (_annotationWidthBox is null) return;
        _settingAnnotationWidth = true;
        try { _annotationWidthBox.Text = _annotationWidth.ToString("0.#", CultureInfo.InvariantCulture); }
        finally { _settingAnnotationWidth = false; }
    }

    void ClearAnnotations()
    {
        _annotationFadeTimer?.Stop();
        _annotationFading = true;
        try { _ink?.Strokes.Clear(); }
        finally { _annotationFading = false; }
        if (_ink is not null)
        {
            _ink.BeginAnimation(OpacityProperty, null);
            _ink.Opacity = 1;
        }
    }

    void CancelAnnotationFade()
    {
        _annotationFadeTimer?.Stop();
        if (_ink is null) return;
        _ink.BeginAnimation(OpacityProperty, null);
        _ink.Opacity = 1;
        _annotationFading = false;
    }

    void RestartAnnotationHold()
    {
        if (_ink is null || _ink.Strokes.Count == 0)
        {
            _annotationFadeTimer?.Stop();
            return;
        }
        if (_annotationBoard.Hold <= TimeSpan.Zero)
        {
            _annotationFadeTimer?.Stop();
            _ink.BeginAnimation(OpacityProperty, null);
            _ink.Opacity = 1;
            return;
        }
        if (_annotationFadeTimer is null) return;
        _annotationFadeTimer.Stop();
        _annotationFadeTimer.Interval = _annotationBoard.Hold;
        _annotationFadeTimer.Start();
    }

    void BeginAnnotationFade()
    {
        _annotationFadeTimer?.Stop();
        if (_ink is null || _ink.Strokes.Count == 0) return;
        _annotationFading = true;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(AnnotationFadeMilliseconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        fade.Completed += (_, _) =>
        {
            if (_ink is null) { _annotationFading = false; return; }
            _annotationFading = true;
            try { _ink.Strokes.Clear(); }
            finally { _annotationFading = false; }
            _ink.BeginAnimation(OpacityProperty, null);
            _ink.Opacity = 1;
        };
        _ink.BeginAnimation(OpacityProperty, fade);
    }

    void UndoAnnotation()
    {
        if (_ink is null || _ink.Strokes.Count == 0) return;
        _ink.Strokes.RemoveAt(_ink.Strokes.Count - 1);
        RestartAnnotationHold();
    }

    /// <summary>The display's name, for the window title and the chooser.</summary>
    public string DisplayName => _catalog.FindDisplay(_displayId)?.Name ?? "Program";

    void UpdateAnnotationTitle() => Title = AnnotationToolsOpen
        ? $"Operator Draw Window — {DisplayName}"
        : $"ScorePlus — {DisplayName} Audience Output";
    public void Publish(){_displayState=_engine.CloneState();Update();_scorebugView.Refresh();}
    /// <summary>Re-creates the on-air visuals; used when a new game replaces everything.</summary>
    public void Rebuild(){Update();_scorebugView.Rebuild();}
    /// <summary>Copies controller-owned game values to the audience without staging them.</summary>
    public void UpdateLiveController(GameState source)
    {
        _displayState.HomeScore=source.HomeScore;_displayState.AwayScore=source.AwayScore;
        _displayState.ClockSeconds=source.ClockSeconds;_displayState.ClockRunning=source.ClockRunning;
        _displayState.Period=source.Period;_displayState.HomeTimeouts=source.HomeTimeouts;
        _displayState.AwayTimeouts=source.AwayTimeouts;_displayState.HomePossession=source.HomePossession;
        _displayState.Down=source.Down;_displayState.YardsToGo=source.YardsToGo;_displayState.DownPending=source.DownPending;
        _displayState.FootballDownMode=source.FootballDownMode;_displayState.ClockMessage=source.ClockMessage;
        // Team marks are part of the live picture: without these the audience kept the old crest.
        _displayState.HomeLogoPath=source.HomeLogoPath;_displayState.AwayLogoPath=source.AwayLogoPath;
        _displayState.HomeMascot=source.HomeMascot;_displayState.AwayMascot=source.AwayMascot;
        _displayState.HomeRecord=source.HomeRecord;_displayState.AwayRecord=source.AwayRecord;
        _displayState.HomeDivisionRecord=source.HomeDivisionRecord;_displayState.AwayDivisionRecord=source.AwayDivisionRecord;
        _displayState.HomeRanking=source.HomeRanking;_displayState.AwayRanking=source.AwayRanking;
        _displayState.HomePrimaryColor=source.HomePrimaryColor;_displayState.AwayPrimaryColor=source.AwayPrimaryColor;
        _displayState.HomeSecondaryColor=source.HomeSecondaryColor;_displayState.AwaySecondaryColor=source.AwaySecondaryColor;
        _displayState.YardsToGoal=source.YardsToGoal;_displayState.BallOnHomeSide=source.BallOnHomeSide;
        _displayState.ShotClockSeconds=source.ShotClockSeconds;_displayState.ShotClockRunning=source.ShotClockRunning;
        _displayState.StoppageSeconds=source.StoppageSeconds;_displayState.StoppageRunning=source.StoppageRunning;
        _displayState.PlayClockSeconds=source.PlayClockSeconds;_displayState.PlayClockRunning=source.PlayClockRunning;
        _displayState.TimeoutClockSeconds=source.TimeoutClockSeconds;_displayState.TimeoutClockRunning=source.TimeoutClockRunning;
        _displayState.HomeFouls=source.HomeFouls;_displayState.AwayFouls=source.AwayFouls;
        _displayState.Balls=source.Balls;_displayState.Strikes=source.Strikes;_displayState.Outs=source.Outs;
        Update();_scorebugView.Refresh();
    }

    public void UpdateLiveScoreClock(GameState source)
    {
        _displayState.HomeScore=source.HomeScore;_displayState.AwayScore=source.AwayScore;
        _displayState.HomeTeam=source.HomeTeam;_displayState.AwayTeam=source.AwayTeam;
        _displayState.HomeAbbreviation=source.HomeAbbreviation;_displayState.AwayAbbreviation=source.AwayAbbreviation;
        _displayState.HomeLogoPath=source.HomeLogoPath;_displayState.AwayLogoPath=source.AwayLogoPath;
        _displayState.HomeMascot=source.HomeMascot;_displayState.AwayMascot=source.AwayMascot;
        _displayState.HomeRecord=source.HomeRecord;_displayState.AwayRecord=source.AwayRecord;
        _displayState.HomeDivisionRecord=source.HomeDivisionRecord;_displayState.AwayDivisionRecord=source.AwayDivisionRecord;
        _displayState.HomeRanking=source.HomeRanking;_displayState.AwayRanking=source.AwayRanking;
        _displayState.HomePrimaryColor=source.HomePrimaryColor;_displayState.AwayPrimaryColor=source.AwayPrimaryColor;
        _displayState.HomeSecondaryColor=source.HomeSecondaryColor;_displayState.AwaySecondaryColor=source.AwaySecondaryColor;
        // The new down goes up as soon as it changes, but the distance stays as it was until the
        // operator publishes. Keep the pending flag on air until Publish() reloads the whole state,
        // so typing the distance does not put a half-updated "2nd & 10" on screen.
        if(source.DownPending){_displayState.Down=source.Down;_displayState.DownPending=true;}
        // The Yards/Goal/PAT choice belongs to the staged down-and-distance line, not the live down.
        _displayState.ClockMessage=source.ClockMessage;
        _displayState.ClockSeconds=source.ClockSeconds;_displayState.ClockRunning=source.ClockRunning;_displayState.ClockCountsUp=source.ClockCountsUp;
        _displayState.ShotClockSeconds=source.ShotClockSeconds;_displayState.ShotClockRunning=source.ShotClockRunning;
        _displayState.StoppageSeconds=source.StoppageSeconds;_displayState.StoppageRunning=source.StoppageRunning;
        _displayState.PlayClockSeconds=source.PlayClockSeconds;_displayState.PlayClockRunning=source.PlayClockRunning;
        _displayState.TimeoutClockSeconds=source.TimeoutClockSeconds;_displayState.TimeoutClockRunning=source.TimeoutClockRunning;
        // Player data goes live with the score, so a substitution prints on air immediately.
        _displayState.HomePlayerCleared=source.HomePlayerCleared;_displayState.AwayPlayerCleared=source.AwayPlayerCleared;
        _displayState.HomePlayer=source.HomePlayer;_displayState.AwayPlayer=source.AwayPlayer;
        Update();_scorebugView.Refresh();
    }
    public void RefreshDisplay(){Update();_scorebugView.Refresh();}
    /// <summary>Prints the newest values at once, used when the value switch delay changes.</summary>
    public void RefreshGraphic(){Update();_scorebugView.ApplyNow();}
    // The audience window is intentionally never restyled by ThemeManager: a theme change must not recolor on-air graphics.
    public void SetBroadcastTheme(string theme){_broadcastTheme=theme;Update();}
    void Window_Loaded(object sender,RoutedEventArgs e){Update();_scorebugView.Refresh();}
    void Update()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Update); return; }
        SyncAnnotationSurface();
        var s = _displayState;
        Background=ColorBrush(ThemeManager.Resolve(_broadcastTheme)=="Light"?"#FFF3F5F8":"#FF000000");
        HomeLabel.Text = s.HomeTeam; AwayLabel.Text = s.AwayTeam;
        HomeScore.Text = s.HomeScore.ToString(); AwayScore.Text = s.AwayScore.ToString();
        Period.Text = PeriodDisplay.Format(s).ToUpperInvariant(); Clock.Text = s.GameClockText;
        HomeExtraLabel.Text = AwayExtraLabel.Text = "TIMEOUTS";
        HomeExtraPanel.Visibility = AwayExtraPanel.Visibility = Visibility.Visible;
        HomeTimeouts.Text = Dots(s.HomeTimeouts); AwayTimeouts.Text = Dots(s.AwayTimeouts);
        Possession.Visibility = s.SportId is "basketball" or "football" ? Visibility.Visible : Visibility.Collapsed;
        Possession.Text = $"●  {(s.HomePossession ? s.HomeTeam : s.AwayTeam)} POSSESSION";
        if(s.SportId=="football")
        {
            var down=s.DownDistanceDisplay;
            Period.Text=string.IsNullOrWhiteSpace(down)
                ? PeriodDisplay.Format(s).ToUpperInvariant()
                : $"{PeriodDisplay.Format(s).ToUpperInvariant()} · {down}";
            Possession.Text=$"●  {(s.HomePossession?s.HomeTeam:s.AwayTeam)} POSSESSION   ·   PLAY CLOCK {s.PlayClockSeconds}   ·   TIMEOUT CLOCK {GameEngine.FormatClock(s.TimeoutClockSeconds)}";
        }
        if(s.SportId=="soccer")
        {
            HomeExtraLabel.Text = AwayExtraLabel.Text = "CARDS · SHOTS · CORNERS";
            HomeTimeouts.Text=$"🟨 {s.HomeYellowCards}   🟥 {s.HomeRedCards}   ⚽ {s.HomeShots}   ◆ {s.HomeCorners}";
            AwayTimeouts.Text=$"🟨 {s.AwayYellowCards}   🟥 {s.AwayRedCards}   ⚽ {s.AwayShots}   ◆ {s.AwayCorners}";
            Possession.Visibility=Visibility.Collapsed;
        }
        else if(s.SportId=="basketball")
        {
            HomeExtraLabel.Text=AwayExtraLabel.Text="FOULS";HomeTimeouts.Text=$"{s.HomeFouls} fouls";AwayTimeouts.Text=$"{s.AwayFouls} fouls";
            Possession.Text=$"●  {(s.HomePossession?s.HomeTeam:s.AwayTeam)} POSSESSION   ·   SHOT CLOCK {s.ShotClockSeconds}";
        }
        else if(s.SportId=="baseball")
        {
            HomeExtraLabel.Text=AwayExtraLabel.Text="HITS · COUNT";HomeTimeouts.Text=$"{s.HomeHits} hits   ·   {s.Balls} balls / {s.Strikes} strikes";AwayTimeouts.Text=$"{s.AwayHits} hits   ·   {s.Outs} outs";Possession.Visibility=Visibility.Collapsed;
        }
        else if(s.SportId=="football")
        {
            HomeExtraLabel.Text=AwayExtraLabel.Text="YARDS · TURNOVERS";HomeTimeouts.Text=$"{s.HomeYards} yards   ·   {s.HomeTurnovers} turnovers";AwayTimeouts.Text=$"{s.AwayYards} yards   ·   {s.AwayTurnovers} turnovers";
        }
    }
    static System.Windows.Media.Brush ColorBrush(string value)=>(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(value)!;
    static string Dots(int count) => string.Join(" ", Enumerable.Repeat("●", count));
    static string OrdinalSuffix(int value)=>value switch{1=>"st",2=>"nd",3=>"rd",_=>"th"};
    /// <summary>True while this window is filling its screen.</summary>
    public bool IsFullscreen => WindowFullscreen.IsFullscreen(this);

    public void ToggleFullscreen() => WindowFullscreen.Toggle(this);

    /// <summary>
    /// F11 on the audience window is the same as Broadcast Fullscreen from the main menu: the
    /// graphic fills the screen. Escape gives the window back.
    /// </summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        if (e.Key == System.Windows.Input.Key.Escape && AnnotationToolsOpen)
        {
            CloseAnnotationTools();
            e.Handled = true;
            return;
        }
        if (e.Key == System.Windows.Input.Key.Escape && IsFullscreen)
        {
            WindowFullscreen.Exit(this);
            e.Handled = true;
            return;
        }
        if (AnnotationToolsOpen && Keyboard.FocusedElement is not TextBox && Keyboard.FocusedElement is not ComboBox)
        {
            if (e.Key == System.Windows.Input.Key.P) { SetAnnotationTool(false); e.Handled = true; return; }
            if (e.Key == System.Windows.Input.Key.H) { SetAnnotationTool(true); e.Handled = true; return; }
            if (e.Key == System.Windows.Input.Key.E) { SetAnnotationEraser(); e.Handled = true; return; }
        }
        base.OnPreviewKeyDown(e);
    }
    protected override void OnClosed(EventArgs e)
    {
        FinishLiveStroke();
        _annotationFadeTimer?.Stop();
        if (AnnotationToolsOpen) _annotationBoard.Dismiss();
        _annotationBoard.HoldChanged -= AnnotationBoard_HoldChanged;
        _annotationBoard.Dismissed -= HideAnnotationLayer;
        _annotationBoard.StrokeStarted -= CancelAnnotationFade;
        _annotationBoard.StrokeFinished -= RestartAnnotationHold;
        _ink?.BeginAnimation(OpacityProperty,null);
        _engine.Changed-=Update; _engine.Changed-=_scorebugView.Refresh; _catalog.Changed-=Publish;
        _scorebugView.Dispose();
        base.OnClosed(e);
    }
}
