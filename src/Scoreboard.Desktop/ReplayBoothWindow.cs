using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Scoreboard.Desktop;

/// <summary>
/// The referee's review room: every camera live in a wall, a selected camera in a large replay, and
/// the game/play/stoppage clocks belonging to that exact recording position. Arrow keys move the
/// selection, Enter enlarges a camera, and Escape returns to the wall.
/// </summary>
public sealed class ReplayBoothWindow : Window
{
    readonly ReplayBoothController _booth;
    readonly UserPreferences _preferences;
    readonly UniformGrid _wallTiles = new() { Margin = new Thickness(6) };
    readonly Grid _wall = new() { Background = Brushes.Black, ClipToBounds = true };
    readonly Grid _bigView = new() { Background = Brushes.Black };
    readonly Image _bigImage = new() { Stretch = Stretch.Uniform };
    readonly Border _bigNamePlate = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        Background = new SolidColorBrush(Color.FromArgb(0xD8, 0, 0, 0)),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 5, 10, 5),
        Margin = new Thickness(14)
    };
    readonly TextBlock _bigName = new() { FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
    readonly TextBlock _bigMeta = new() { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xC4, 0xD2)), Margin = new Thickness(0, 2, 0, 0) };
    readonly TextBlock _bigHint = new()
    {
        Text = "Waiting for the camera to record its first replay window…",
        FontSize = 13,
        Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA8, 0xB8)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(28)
    };
    readonly Slider _scrub = new() { Name = "BigScrub", Minimum = 0, Maximum = 120, Margin = new Thickness(0, 2, 10, 2) };
    // The wall page carries its own scrubber on the same shared playhead, so a replay can be pulled
    // back without enlarging a camera first. Two sliders, one playhead: neither writes back over the
    // other while it is being dragged.
    readonly Slider _wallScrub = new() { Name = "WallScrub", Minimum = 0, Maximum = 120, Margin = new Thickness(0, 2, 10, 2) };
    readonly TextBlock _wallTime = new() { Text = "LIVE EDGE", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x66)), VerticalAlignment = VerticalAlignment.Center, MinWidth = 104 };
    readonly TextBlock _wallWindow = new() { Text = "2 MIN", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB4)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    bool _updatingWallScrub;
    readonly TextBlock _timeLabel = new() { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x66)), VerticalAlignment = VerticalAlignment.Center, MinWidth = 104 };
    readonly TextBlock _windowLabel = new() { Text = "2 MIN", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB4)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    readonly StackPanel _stoppagePanel = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
    readonly TextBlock _stoppageValue = new() { Text = "0:00", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x66)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0), MinWidth = 38 };
    readonly Button _stoppageToggle = new() { Content = "Start", MinWidth = 58, Margin = new Thickness(0, 0, 4, 0) };
    readonly Button _stoppageReset = new() { Content = "Reset", MinWidth = 58, Margin = new Thickness(0, 0, 4, 0) };
    readonly Button _stoppageMinus = new() { Content = "−30s", MinWidth = 52, Margin = new Thickness(0, 0, 4, 0) };
    readonly Button _stoppagePlus = new() { Content = "+30s", MinWidth = 52, Margin = new Thickness(0, 0, 4, 0) };
    readonly Slider _speedSlider = new() { Minimum = 0.1, Maximum = 2.0, Value = 1, Width = 150, Margin = new Thickness(8, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _speedText = new() { Text = "1.0×", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x66)), VerticalAlignment = VerticalAlignment.Center, MinWidth = 40 };
    readonly StackPanel _wallCameraList = new();
    readonly ComboBox _wallColumnsBox = new() { Width = 130, Margin = new Thickness(0, 0, 0, 6) };
    bool _updatingLayout;
    int _wallColumns = 2;
    readonly Button _playButton = new() { Content = "Play", MinWidth = 70 };
    readonly Button _backButton = new() { Content = "◀ Wall", MinWidth = 76, Margin = new Thickness(0, 0, 6, 0), Visibility = Visibility.Collapsed };
    readonly CheckBox _labelsCheck = new() { Content = "Show camera names on the bottom corner", IsChecked = true };
    readonly Border _editor = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(10, 7, 10, 7) };
    readonly Border _transportBar = new() { Padding = new Thickness(12, 8, 12, 10) };
    readonly TextBlock _status = new() { FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA8, 0xB8)), TextWrapping = TextWrapping.Wrap };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly Dictionary<int, Image> _tileImages = [];
    readonly Dictionary<int, Border> _tileBorders = [];
    bool _expanded;
    bool _editing;
    bool _updatingScrub;
    string _cameraSignature = "";

    public ReplayBoothWindow(ReplayBoothController booth, UserPreferences preferences)
    {
        _booth = booth;
        _preferences = preferences;
        Title = "ScorePlus — Replay Booth";
        Width = 1340;
        Height = 860;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;
        Foreground = Brushes.White;
        ScreenFit.Track(this);

        _booth.Activate();
        try
        {
            _scrub.Style = (Style)Application.Current.FindResource("ReplayTransportSlider");
            _scrub.Maximum = Math.Max(1, CameraFeed.ReplayWindowSeconds);
            _wallScrub.Style = (Style)Application.Current.FindResource("ReplayTransportSlider");
            _wallScrub.Maximum = Math.Max(1, CameraFeed.ReplayWindowSeconds);
        }
        catch { }
        _windowLabel.Text = WindowLabel();
        _wallWindow.Text = WindowLabel();
        _labelsCheck.IsChecked = _booth.ShowLabels;
        _labelsCheck.Checked += (_, _) => SetLabels(true);
        _labelsCheck.Unchecked += (_, _) => SetLabels(false);
        _speedSlider.Value = _booth.Speed;
        _speedText.Text = $"{_booth.Speed:0.0}×";
        _speedSlider.ValueChanged += (_, e) =>
        {
            _booth.Speed = e.NewValue;
            _speedText.Text = $"{_booth.Speed:0.0}×";
        };
        Content = BuildChrome();
        BuildBigView();
        _booth.RefreshCameras();
        RenderWall();
        ThemeManager.Apply(this, preferences.Theme);
        Palette();

        _tick.Tick += (_, _) => RefreshUi();
        _tick.Start();
        Closed += (_, _) => _tick.Stop();
        Loaded += (_, _) =>
        {
            Palette();
            Focus();
            RefreshUi();
        };
    }

    UIElement BuildChrome()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new DockPanel { Margin = new Thickness(14, 11, 14, 7), LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var edit = new Button { Content = "✎ Edit", MinWidth = 72, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Edit the booth's labels and wall options" };
        edit.Click += (_, _) => ToggleEditing();
        var refresh = new Button { Content = "Refresh recordings", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Pick up the newest recorded camera segments now" };
        refresh.Click += (_, _) =>
        {
            if (_booth.RefreshBuffer()) { _status.Text = "RECORDINGS REFRESHED · " + _booth.SelectedLabel; RefreshUi(); }
            else _status.Text = "STILL RECORDING · no finished camera clip yet";
        };
        _backButton.Click += (_, _) => ToggleExpanded(false);
        actions.Children.Add(_backButton);
        actions.Children.Add(edit);
        actions.Children.Add(refresh);
        actions.Children.Add(_stoppagePanel);
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "REPLAY BOOTH", FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(new TextBlock
        {
            Text = "  ·  ARROWS SELECT  ·  ENTER ENLARGE  ·  ESC WALL",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB4)),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(title);

        var editorPanel = new DockPanel { LastChildFill = true };
        _editor.Child = editorPanel;
        var done = new Button { Content = "✓ Done", MinWidth = 70, Margin = new Thickness(8, 0, 0, 0) };
        done.Click += (_, _) => ToggleEditing();
        DockPanel.SetDock(done, Dock.Right);
        editorPanel.Children.Add(done);
        // The booth owns this layout, separately from the multiview wall: its own column count,
        // its own camera order, and its own set of hidden cameras.
        var editorStack = new StackPanel();
        editorStack.Children.Add(_labelsCheck);
        var columnsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
        columnsRow.Children.Add(new TextBlock
        {
            Text = "WALL COLUMNS",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB4)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        columnsRow.Children.Add(_wallColumnsBox);
        editorStack.Children.Add(columnsRow);
        editorStack.Children.Add(new TextBlock
        {
            Text = "CAMERAS ON THIS WALL · ↑ ↓ ORDER",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB4)),
            Margin = new Thickness(0, 2, 0, 4)
        });
        var cameraScroll = new ScrollViewer
        {
            MaxHeight = 132,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _wallCameraList
        };
        editorStack.Children.Add(cameraScroll);
        editorPanel.Children.Add(editorStack);
        _wallColumnsBox.ItemsSource = new[] { "Auto", "1 column", "2 columns", "3 columns", "4 columns" };
        _wallColumnsBox.SelectedIndex = Math.Clamp(_booth.Columns, 0, 4);
        _wallColumnsBox.SelectionChanged += (_, _) => ApplyWallEditor();
        DockPanel.SetDock(_editor, Dock.Top);
        root.Children.Add(header);
        Grid.SetRow(_editor, 1);
        root.Children.Add(_editor);

        _wall.Children.Add(_wallTiles);
        _wall.Focusable = true;
        // The wall page is the booth's main page, so its transport sits under the tiles: the shared
        // playhead is scrubbable there without enlarging a camera.
        var wallRow = new Grid();
        wallRow.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        wallRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        wallRow.Children.Add(_wall);
        var wallTransport = new DockPanel { LastChildFill = true, Margin = new Thickness(14, 2, 14, 8) };
        wallTransport.Children.Add(new TextBlock
        {
            Text = "LIVE",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xD7, 0xA3)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        wallTransport.Children.Add(_wallWindow);
        DockPanel.SetDock(_wallTime, Dock.Right);
        wallTransport.Children.Add(_wallTime);
        wallTransport.Children.Add(_wallScrub);
        Grid.SetRow(wallTransport, 1);
        wallRow.Children.Add(wallTransport);
        _wallScrub.ValueChanged += (_, e) =>
        {
            if (_updatingWallScrub || _expanded) return;
            if (Math.Abs(e.NewValue - _booth.Back) > 0.05) _booth.Seek(e.NewValue);
        };
        Grid.SetRow(wallRow, 2);
        root.Children.Add(wallRow);
        Grid.SetRow(_bigView, 2);
        root.Children.Add(_bigView);

        DockPanel.SetDock(_status, Dock.Bottom);
        Grid.SetRow(_status, 3);
        // The status sits under the picture without taking height from it while it is empty.
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _status.Margin = new Thickness(14, 0, 14, 8);
        root.Children.Add(_status);
        return root;
    }

    void BuildBigView()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var picture = new Grid { Margin = new Thickness(8, 0, 8, 0) };
        picture.Children.Add(_bigImage);
        picture.Children.Add(_bigHint);
        _bigNamePlate.Child = _bigName;
        ThemeManager.SetSkipTheme(_bigNamePlate, true);
        picture.Children.Add(_bigNamePlate);
        grid.Children.Add(picture);

        var controls = _transportBar;
        var stack = new StackPanel();
        var clockRow = new DockPanel { LastChildFill = true };
        var metaHost = new StackPanel();
        metaHost.Children.Add(_bigMeta);
        clockRow.Children.Add(_timeLabel);
        DockPanel.SetDock(_timeLabel, Dock.Right);
        clockRow.Children.Add(metaHost);
        stack.Children.Add(clockRow);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
        var back15 = new Button { Content = "−15s", MinWidth = 56, Margin = new Thickness(0, 0, 5, 0) };
        var back5 = new Button { Content = "−5s", MinWidth = 52, Margin = new Thickness(0, 0, 5, 0) };
        var forward5 = new Button { Content = "+5s", MinWidth = 52, Margin = new Thickness(5, 0, 5, 0) };
        var forward15 = new Button { Content = "+15s", MinWidth = 56, Margin = new Thickness(0, 0, 5, 0) };
        var live = new Button { Content = "Live edge", MinWidth = 76 };
        back15.Click += (_, _) => { _booth.RefreshBuffer(); _booth.Step(15); RefreshUi(); };
        back5.Click += (_, _) => { _booth.RefreshBuffer(); _booth.Step(5); RefreshUi(); };
        forward5.Click += (_, _) => { _booth.Step(-5); RefreshUi(); };
        forward15.Click += (_, _) => { _booth.Step(-15); RefreshUi(); };
        live.Click += (_, _) => { _booth.Seek(0); _booth.Pause(); RefreshUi(); };
        _playButton.Click += (_, _) => { _booth.TogglePlay(); RefreshUi(); };
        buttons.Children.Add(back15);
        buttons.Children.Add(back5);
        buttons.Children.Add(_playButton);
        buttons.Children.Add(forward5);
        buttons.Children.Add(forward15);
        buttons.Children.Add(live);
        stack.Children.Add(buttons);
        var scrubRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 4, 0, 0) };
        scrubRow.Children.Add(new TextBlock { Text = "LIVE", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xD7, 0xA3)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        scrubRow.Children.Add(_windowLabel);
        scrubRow.Children.Add(_scrub);
        stack.Children.Add(scrubRow);
        var speedRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
        speedRow.Children.Add(new TextBlock
        {
            Text = "SPEED",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB4)),
            VerticalAlignment = VerticalAlignment.Center
        });
        speedRow.Children.Add(_speedSlider);
        speedRow.Children.Add(_speedText);
        stack.Children.Add(speedRow);

        // A compact added-time stopwatch beside the transport. It drives the live game engine, so
        // starting it here is exactly the same as pressing Stoppage on the console.
        _stoppageToggle.Click += (_, _) => { _booth.ToggleStoppage(); RefreshBigView(); };
        _stoppageReset.Click += (_, _) => { _booth.ResetStoppage(); RefreshBigView(); };
        _stoppagePlus.Click += (_, _) => { _booth.AdjustStoppage(30); RefreshBigView(); };
        _stoppageMinus.Click += (_, _) => { _booth.AdjustStoppage(-30); RefreshBigView(); };
        _stoppagePanel.Children.Add(new TextBlock
        {
            Text = "⏱ STOPPAGE",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB4)),
            VerticalAlignment = VerticalAlignment.Center
        });
        _stoppagePanel.Children.Add(_stoppageValue);
        _stoppagePanel.Children.Add(_stoppageMinus);
        _stoppagePanel.Children.Add(_stoppagePlus);
        _stoppagePanel.Children.Add(_stoppageToggle);
        _stoppagePanel.Children.Add(_stoppageReset);
        controls.Child = stack;
        Grid.SetRow(controls, 1);
        grid.Children.Add(controls);
        _bigView.Children.Add(grid);
        _bigView.Visibility = Visibility.Collapsed;
        _scrub.ValueChanged += (_, e) =>
        {
            if (_updatingScrub || _editing) return;
            if (Math.Abs(e.NewValue - _booth.Back) > 0.05) _booth.Seek(e.NewValue);
        };
    }

    void Palette()
    {
        // The edit strip and transport bar are operator chrome, not artwork: follow the window's
        // theme instead of the old hard-coded navy so the booth matches the rest of ScorePlus.
        _editor.SetResourceReference(Border.BackgroundProperty, "OperatorPanelBrush");
        _transportBar.SetResourceReference(Border.BackgroundProperty, "OperatorPanelBrush");
        _labelsCheck.SetResourceReference(Control.ForegroundProperty, "OperatorTextBrush");
        _bigName.Foreground = Brushes.White;
        _bigHint.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA8, 0xB8));
        _timeLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x66));
        _windowLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB4));
        _wallWindow.Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB4));
        _wallTime.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x66));
        _stoppageValue.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x66));
        _status.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA8, 0xB8));
        _bigMeta.Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xC4, 0xD2));
    }

    void ToggleEditing()
    {
        _editing = !_editing;
        _editor.Visibility = _editing ? Visibility.Visible : Visibility.Collapsed;
        if (_editing)
        {
            BuildWallEditor();
            _labelsCheck.Focus();
            _status.Text = "EDIT · wall columns, camera order and names on the booth's own layout";
        }
        else
        {
            RenderWall();
            _wall.Focus();
            _status.Text = "Wall ready · arrows select · Enter enlarge";
        }
    }

    void SetLabels(bool show)
    {
        _booth.ShowLabels = show;
        foreach (var border in _tileBorders.Values)
        {
            if (border.Child is Grid grid && grid.Children.OfType<Border>().LastOrDefault() is { Tag: "label" } label)
                label.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        _bigNamePlate.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    void RenderWall()
    {
        var signature = string.Join("|", _booth.Cameras.Select(camera => $"{camera.Index}:{camera.Label}"));
        if (signature == _cameraSignature && _tileImages.Count == _booth.Cameras.Count) return;
        _cameraSignature = signature;
        _wallTiles.Children.Clear();
        _tileImages.Clear();
        _tileBorders.Clear();
        if (_booth.Cameras.Count == 0)
        {
            _wallTiles.Children.Add(new TextBlock
            {
                Text = "No cameras in the media library.\nAdd a camera in the Media tab, then reopen the Replay Booth.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA8, 0xB8)),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24)
            });
            return;
        }
        _wallColumns = _booth.Columns > 0 ? _booth.Columns : _booth.Cameras.Count <= 4 ? 2 : 3;
        _wallTiles.Columns = _wallColumns;
        foreach (var camera in _booth.Cameras)
        {
            var picture = new Image { Stretch = Stretch.Uniform };
            var body = new Grid();
            body.Children.Add(picture);
            var plate = new Border
            {
                Tag = "label",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Color.FromArgb(0xD8, 0, 0, 0)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(8),
                Child = new TextBlock
                {
                    Text = camera.Label,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 260
                },
                Visibility = _booth.ShowLabels ? Visibility.Visible : Visibility.Collapsed
            };
            ThemeManager.SetSkipTheme(plate, true);
            body.Children.Add(plate);
            var border = new Border
            {
                Margin = new Thickness(3),
                Background = Brushes.Black,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Child = body,
                Tag = camera,
                Focusable = true
            };
            border.MouseLeftButtonDown += (_, _) => SelectCamera(camera, expand: false);
            border.MouseRightButtonDown += (_, _) => SelectCamera(camera, expand: true);
            _tileImages[camera.Index] = picture;
            _tileBorders[camera.Index] = border;
            _wallTiles.Children.Add(border);
        }
        UpdateSelection();
    }

    void SelectCamera(ReplayBoothCamera camera, bool expand)
    {
        _booth.SelectCamera(camera.Index);
        if (expand) ToggleExpanded(true);
        else
        {
            // The tile is a mouse target, not a focusable control, so a click used to leave keyboard
            // focus on the last toolbar button. Enter then activated that button instead of enlarging
            // the camera. Focusing the tile makes the wall own the keyboard after a click.
            _tileBorders.TryGetValue(camera.Index, out var border);
            if (border is null || !border.Focus()) _wall.Focus();
            UpdateSelection();
            // Do not repaint the wall here. The 120 ms timer already feeds each tile; touching every
            // Image on selection made the whole wall look as though it had dropped its picture.
        }
        _status.Text = $"CAMERA · {camera.Label} · {(expand ? "replay" : "selected")}";
    }

    void UpdateSelection()
    {
        var selected = _booth.SelectedCamera?.Index ?? -1;
        foreach (var (index, border) in _tileBorders)
            border.BorderBrush = index == selected
                ? new SolidColorBrush(Color.FromRgb(0x38, 0xD7, 0xA3))
                : new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38));
    }

    void ToggleExpanded(bool expanded)
    {
        _expanded = expanded && _booth.SelectedCamera is not null;
        _wall.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
        _bigView.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        _backButton.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        if (_expanded)
        {
            _booth.RefreshBuffer();
            Focus();
            _status.Text = $"REPLAY · {_booth.SelectedLabel} · arrows change camera · Escape returns to the wall";
        }
        else
        {
            _status.Text = "Wall ready · arrows select · Enter enlarge";
            _wall.Focus();
        }
        RefreshUi();
    }

    void RefreshWallFrames()
    {
        // Every tile draws at the one shared playhead, so the wall is the same instant from every
        // angle: scrub once and the whole wall moves, whether or not a camera is expanded.
        foreach (var (index, image) in _tileImages)
            if (_booth.FrameFor(index) is { } frame && !ReferenceEquals(image.Source, frame))
                image.Source = frame;
        RefreshWallScrub();
    }

    /// <summary>Keeps the wall's scrubber and its label on the shared playhead.</summary>
    void RefreshWallScrub()
    {
        _wallWindow.Text = WindowLabel();
        _wallTime.Text = !_booth.HasReplay ? "LIVE" : _booth.Back < 0.15 ? "LIVE EDGE" : $"{Math.Round(_booth.Back)}s BACK";
        _updatingWallScrub = true;
        try
        {
            _wallScrub.Maximum = Math.Max(1, Math.Min(CameraFeed.ReplayWindowSeconds, Math.Max(1, _booth.Depth)));
            _wallScrub.Value = Math.Clamp(_booth.Back, 0, _wallScrub.Maximum);
        }
        finally { _updatingWallScrub = false; }
    }

    void RefreshStoppage()
    {
        // The stoppage clock is part of the booth's top bar: it is there before a soccer match
        // has any added time and on every sport, so the operator can always reach it.
        _stoppagePanel.Visibility = Visibility.Visible;
        _stoppageValue.Text = _booth.StoppageText;
        _stoppageToggle.Content = _booth.StoppageRunning ? "Stop" : "Start";
    }

    /// <summary>Rebuilds the booth's own layout editor from the saved wall.</summary>
    void BuildWallEditor()
    {
        if (_wallCameraList is null) return;
        _updatingLayout = true;
        try
        {
            _wallColumnsBox.SelectedIndex = Math.Clamp(_booth.Columns, 0, 4);
            _wallCameraList.Children.Clear();
            var order = _booth.WallOrder.ToList();
            var hidden = new HashSet<string>(_booth.WallHidden, StringComparer.OrdinalIgnoreCase);
            var cameras = _booth.AllCameras
                .OrderBy(camera =>
                {
                    var index = order.FindIndex(path => string.Equals(path, camera.Path, StringComparison.OrdinalIgnoreCase));
                    return index < 0 ? int.MaxValue : index;
                })
                .ToList();
            foreach (var camera in cameras)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3), Tag = camera.Path };
                var up = new Button { Content = "↑", Width = 26, MinHeight = 22, Padding = new Thickness(0), Margin = new Thickness(3, 0, 0, 0) };
                var down = new Button { Content = "↓", Width = 26, MinHeight = 22, Padding = new Thickness(0), Margin = new Thickness(3, 0, 0, 0) };
                up.Click += (_, _) => MoveWallCamera(row, -1);
                down.Click += (_, _) => MoveWallCamera(row, 1);
                DockPanel.SetDock(up, Dock.Right);
                DockPanel.SetDock(down, Dock.Right);
                row.Children.Add(up);
                row.Children.Add(down);
                var check = new CheckBox
                {
                    Content = camera.Label,
                    IsChecked = !hidden.Contains(camera.Path),
                    Tag = camera.Path,
                    FontSize = 11,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Foreground = _labelsCheck.Foreground
                };
                check.Checked += (_, _) => ApplyWallEditor();
                check.Unchecked += (_, _) => ApplyWallEditor();
                row.Children.Add(check);
                _wallCameraList.Children.Add(row);
            }
        }
        finally { _updatingLayout = false; }
    }

    void MoveWallCamera(DockPanel row, int delta)
    {
        if (_updatingLayout) return;
        var index = _wallCameraList.Children.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _wallCameraList.Children.Count) return;
        _updatingLayout = true;
        try
        {
            _wallCameraList.Children.RemoveAt(index);
            _wallCameraList.Children.Insert(target, row);
        }
        finally { _updatingLayout = false; }
        ApplyWallEditor();
    }

    /// <summary>Saves the booth's wall from the editor: order is the row order, unchecked rows are hidden.</summary>
    void ApplyWallEditor()
    {
        if (_updatingLayout) return;
        var order = new List<string>();
        var hidden = new List<string>();
        foreach (var child in _wallCameraList.Children)
        {
            if (child is not DockPanel row) continue;
            var path = row.Tag as string ?? "";
            if (path.Length == 0) continue;
            var check = row.Children.OfType<CheckBox>().FirstOrDefault();
            if (check?.IsChecked == true) order.Add(path); else hidden.Add(path);
        }
        _booth.SetWallLayout(order, hidden, _wallColumnsBox.SelectedIndex);
        // The column count and camera set are part of the wall's shape, so force the next render
        // to rebuild the tiles instead of returning on an unchanged camera signature.
        _cameraSignature = "";
        RenderWall();
        _status.Text = $"BOOTH LAYOUT · {order.Count} camera{(order.Count == 1 ? "" : "s")} · {(_booth.Columns == 0 ? "auto columns" : $"{_booth.Columns} column{(_booth.Columns == 1 ? "" : "s")}")}";
    }


    void RefreshUi()
    {
        if (!IsVisible && !_expanded) return;
        RefreshStoppage();
        RenderWall();
        UpdateSelection();
        if (_expanded) RefreshBigView();
        else RefreshWallFrames();
    }

    string WindowLabel()
    {
        var seconds = Math.Max(1, (int)Math.Round(CameraFeed.ReplayWindowSeconds));
        return seconds % 60 == 0 ? $"{seconds / 60} MIN" : $"{seconds} SEC";
    }

    void RefreshBigView()
    {
        _bigName.Text = _booth.SelectedLabel;
        _bigNamePlate.Visibility = _booth.ShowLabels ? Visibility.Visible : Visibility.Collapsed;
        RefreshStoppage();
        _windowLabel.Text = WindowLabel();
        var frame = _booth.DisplayFrame;
        _bigImage.Source = frame;
        _bigHint.Visibility = frame is null ? Visibility.Visible : Visibility.Collapsed;
        if (_booth.HasReplay)
        {
            _bigHint.Text = $"Waiting for the camera to record its first {WindowLabel().ToLowerInvariant()}…";
            var clock = _booth.ClockAtPlayhead;
            var parts = new List<string>();
            if (clock is not null)
            {
                parts.Add($"GAME {clock.GameClock}");
                parts.Add($"PLAY {clock.PlayClock}");
                if (_booth.ShowStoppage) parts.Add($"STOPPAGE {clock.Stoppage}");
                parts.Add(clock.Period.ToUpperInvariant());
            }
            _bigMeta.Text = string.Join("   ·   ", parts);
            _timeLabel.Text = _booth.Back < 0.15 ? "LIVE EDGE" : $"{Math.Round(_booth.Back)}s BACK";
            _playButton.Content = _booth.IsPlaying ? "Pause" : "Play";
            _updatingScrub = true;
            try
            {
                _scrub.Maximum = Math.Max(1, Math.Min(CameraFeed.ReplayWindowSeconds, _booth.Depth));
                _scrub.Value = Math.Clamp(_booth.Back, 0, _scrub.Maximum);
            }
            finally { _updatingScrub = false; }
        }
        else
        {
            _bigMeta.Text = "Waiting for a finished recording clip";
            _timeLabel.Text = "LIVE";
            _playButton.Content = "Play";
            _updatingScrub = true;
            try { _scrub.Value = 0; } finally { _updatingScrub = false; }
        }
    }

    /// <summary>Moves in the wall's real grid instead of treating Up as Left. The selected tile
    /// stays in its column, so Up and Down feel vertical even when the last row is incomplete.</summary>
    void MoveSelection(int dx, int dy)
    {
        var count = _booth.Cameras.Count;
        if (count == 0) return;
        var index = Math.Max(0, _booth.SelectedIndex);
        var columns = Math.Max(1, _wallColumns);
        if (dy != 0)
        {
            var row = Math.Clamp(index / columns + dy, 0, (count - 1) / columns);
            index = Math.Min(row * columns + index % columns, count - 1);
        }
        if (dx != 0)
        {
            var target = index + dx;
            if (target < 0 || target >= count) return;
            index = target;
        }
        _booth.Select(index);
        if (_expanded) ToggleExpanded(true);
        else UpdateSelection();
    }

    /// <summary>Runs a camera or replay action bound in Preferences → Keybinds.</summary>
    bool TryReplayAction(string actionId)
    {
        switch (actionId)
        {
            case "camera.left": MoveSelection(-1, 0); return true;
            case "camera.right": MoveSelection(1, 0); return true;
            case "camera.up": MoveSelection(0, -1); return true;
            case "camera.down": MoveSelection(0, 1); return true;
            case "camera.enter":
                if (_booth.SelectedCamera is null)
                {
                    // Enter with nothing selected is a useful gesture: take the first camera.
                    if (_booth.Cameras.Count > 0) _booth.Select(0);
                    UpdateSelection();
                    return true;
                }
                ToggleExpanded(!_expanded);
                return true;
            case "camera.back":
                if (_expanded) ToggleExpanded(false);
                return true;
            case "replay.back":
                _booth.RefreshBuffer();
                _booth.Step(2);
                RefreshUi();
                return true;
            case "replay.forward":
                _booth.Step(-2);
                RefreshUi();
                return true;
            case "replay.live":
                _booth.Seek(0);
                _booth.Pause();
                RefreshUi();
                return true;
            case "replay.start":
                _booth.RefreshBuffer();
                _booth.Seek(_booth.Depth);
                _booth.Pause();
                RefreshUi();
                return true;
            case "replay.playpause":
                _booth.TogglePlay();
                RefreshUi();
                return true;
            default:
                return false;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Enter finishes the edit strip and returns to the wall. Previously the edit guard swallowed
        // it, so pressing Enter in that state felt as though the booth's keyboard had stopped working.
        if (_editing)
        {
            if (key is Key.Enter or Key.Escape)
            {
                ToggleEditing();
                e.Handled = true;
                return;
            }
            base.OnPreviewKeyDown(e);
            return;
        }
        // Every arrow and the replay transport can be rebound. A binding wins over the built-in
        // gesture; the A/D/W/S and arrow fallbacks keep the booth usable before anything is bound.
        var bound = KeybindStore.Match(KeybindStore.Load(), key, Keyboard.Modifiers);
        if (bound is not null && TryReplayAction(bound))
        {
            e.Handled = true;
            return;
        }
        // A bound console action is not this window's to consume. Falling through keeps Enter and
        // the arrow fallbacks working when a global binding happens to use one of those keys.
        switch (key)
        {
            case Key.Enter:
                if (_booth.SelectedCamera is null && _booth.Cameras.Count > 0)
                {
                    _booth.Select(0);
                    UpdateSelection();
                    e.Handled = true;
                    return;
                }
                if (_booth.SelectedCamera is not null)
                {
                    ToggleExpanded(!_expanded);
                    e.Handled = true;
                }
                return;
            case Key.Left: MoveSelection(-1, 0); e.Handled = true; return;
            case Key.Right: MoveSelection(1, 0); e.Handled = true; return;
            case Key.Up: MoveSelection(0, -1); e.Handled = true; return;
            case Key.Down: MoveSelection(0, 1); e.Handled = true; return;
            case Key.Escape:
                if (_expanded)
                {
                    ToggleExpanded(false);
                    e.Handled = true;
                }
                else if (_booth.HasSelection)
                {
                    // On the wall Escape is "stop pointing at a camera": the green outline goes and
                    // the pictures stay exactly where they are.
                    _booth.ClearSelection();
                    UpdateSelection();
                    _status.Text = "Wall ready · no camera selected · arrows pick one";
                    e.Handled = true;
                }
                return;
            case Key.A: if (TryReplayAction("replay.back")) e.Handled = true; return;
            case Key.D: if (TryReplayAction("replay.forward")) e.Handled = true; return;
            case Key.W: if (TryReplayAction("replay.live")) e.Handled = true; return;
            case Key.S: if (TryReplayAction("replay.start")) e.Handled = true; return;
        }
        base.OnPreviewKeyDown(e);
    }
}
