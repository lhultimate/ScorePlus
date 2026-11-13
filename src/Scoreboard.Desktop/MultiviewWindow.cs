using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public enum MultiviewSourceKind { ProgramPreview, AudienceOutput, Camera, Replay, Blank }

/// <summary>One saved multiview tile. The source name is a string so old preference files stay readable.</summary>
public sealed class MultiviewTileBinding
{
    public string Source { get; set; } = "preview";
    public string DisplayId { get; set; } = "";
    public int CameraIndex { get; set; } = -1;
}

/// <summary>
/// A custom multiview wall. Every tile chooses its own source: a staged program preview for any
/// display, that display's published audience picture, any live camera, the replay monitor, or a
/// blank placeholder. The tile list belongs to the operator's preferences, so a show can leave a
/// preview beside the program and still see every camera.
/// </summary>
public sealed class MultiviewWindow : Window
{
    sealed class TileConfig
    {
        public MultiviewSourceKind Kind { get; set; }
        public string DisplayId { get; set; } = "";
        public int CameraIndex { get; set; } = -1;
    }

    sealed record SourceChoice(MultiviewSourceKind Kind, string DisplayId, int CameraIndex, string Label)
    {
        public override string ToString() => Label;
    }

    readonly MediaSession _session;
    readonly UserPreferences _preferences;
    readonly ScorebugCatalog _catalog;
    readonly GameEngine _engine;
    readonly GameEngine _publishedEngine;
    readonly ProgramSelection _program;
    readonly AutomationRuntime _automation;
    readonly MediaPlayback _playback;

    readonly WrapPanel _panel = new() { Margin = new Thickness(8) };
    readonly TextBlock _title = new()
    {
        Text = "MULTIVIEW",
        FontSize = 15,
        FontWeight = FontWeights.Bold,
        VerticalAlignment = VerticalAlignment.Center
    };
    readonly TextBlock _status = new()
    {
        FontSize = 11,
        Margin = new Thickness(14, 2, 14, 8),
        TextWrapping = TextWrapping.Wrap,
        Text = "Choose a source in each tile. Add or remove tiles to build the wall you need."
    };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly List<TileConfig> _tiles = [];
    readonly List<ScorebugCanvasView> _canvasViews = [];
    readonly List<(CameraFeed Feed, Action<BitmapSource> Handler)> _cameraHandlers = [];
    string _signature = "";
    bool _rendering;

    // Replay is a single view that can be moved between tiles; recreating it mid-replay dropped
    // the decoded frame and made the preview go black.
    readonly TextBlock _replayTitle = new()
    {
        Text = "REPLAY",
        FontSize = 9,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(6, 3, 6, 3)
    };
    readonly Image _replayMirror = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
    readonly Image _replayPicture = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
    readonly TextBlock _replayHint = new()
    {
        Text = "Press Replay in the media dock to review a camera.",
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12)
    };
    readonly Grid _replayBody = new();
    int _mirrorIndex = -1;
    CameraFeed? _mirrorFeed;
    Action<BitmapSource>? _mirrorHandler;

    Brush _windowBrush = Brushes.Black;
    Brush _surfaceBrush = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
    Brush _borderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38));
    Brush _textBrush = Brushes.White;
    Brush _mutedBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));

    public MultiviewWindow(MediaSession session, UserPreferences preferences, ScorebugCatalog catalog,
        GameEngine engine, GameEngine publishedEngine, ProgramSelection program,
        AutomationRuntime automation, MediaPlayback playback)
    {
        _session = session;
        _preferences = preferences;
        _catalog = catalog;
        _engine = engine;
        _publishedEngine = publishedEngine;
        _program = program;
        _automation = automation;
        _playback = playback;

        Title = "ScorePlus — Multiview";
        Width = 1320;
        Height = 820;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildChrome();
        ApplyPalette(preferences.Theme);
        BuildReplayBody();
        LoadTiles();
        _signature = BuildSignature();

        if (session.Playback.ReplayFrames is { } frames) frames.FrameReady += OnReplayFrame;
        _program.Changed += ProgramChanged;
        RenderTiles();
        _tick.Tick += (_, _) => Refresh();
        _tick.Start();
        Closed += (_, _) => Shutdown();
        ThemeManager.Apply(this, preferences.Theme);
        ApplyPalette(preferences.Theme);
        // The window-wide theme pass runs on Loaded and paints every TextBlock; re-apply the few
        // bespoke pieces afterwards so the title and hint never wash out into the black wall.
        Loaded += (_, _) => ApplyPalette(preferences.Theme);
    }

    UIElement BuildChrome()
    {
        var root = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(14, 11, 14, 0), LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var add = new Button
        {
            Content = "+ Add tile",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Add another preview, audience, camera or replay tile"
        };
        add.Click += (_, _) => AddTile();
        var reset = new Button
        {
            Content = "Reset wall",
            Padding = new Thickness(12, 6, 12, 6),
            ToolTip = "Restore the default preview, audience, replay and camera tiles"
        };
        reset.Click += (_, _) => ResetTiles();
        actions.Children.Add(add);
        actions.Children.Add(reset);
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(_title);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);
        root.Children.Add(new ScrollViewer
        {
            Content = _panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        return root;
    }

    void ApplyPalette(string theme)
    {
        var light = ThemeManager.Resolve(theme) == "Light";
        _windowBrush = Solid(light ? "#F3F5F8" : "#0B0B0B");
        _surfaceBrush = Solid(light ? "#FFFFFF" : "#141414");
        _borderBrush = Solid(light ? "#C9D2DC" : "#383838");
        _textBrush = Solid(light ? "#18202B" : "#F4F7FB");
        _mutedBrush = Solid(light ? "#586576" : "#B0B0B0");
        Background = _windowBrush;
        Foreground = _textBrush;
        _title.Foreground = _textBrush;
        _status.Foreground = _mutedBrush;
        _replayTitle.Foreground = _mutedBrush;
        _replayHint.Foreground = _mutedBrush;
    }

    static Brush Solid(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    void LoadTiles()
    {
        _tiles.Clear();
        var saved = _preferences.MultiviewTiles ?? [];
        var hasReplay = false;
        foreach (var binding in saved)
        {
            var kind = ParseSource(binding.Source);
            if (kind == MultiviewSourceKind.Replay && hasReplay) kind = MultiviewSourceKind.Blank;
            if (kind == MultiviewSourceKind.Replay) hasReplay = true;
            var display = ResolveDisplay(binding.DisplayId);
            var camera = ResolveCamera(binding.CameraIndex);
            if (kind == MultiviewSourceKind.Camera && camera is null) kind = MultiviewSourceKind.Blank;
            if ((kind is MultiviewSourceKind.ProgramPreview or MultiviewSourceKind.AudienceOutput) && display is null)
                kind = MultiviewSourceKind.Blank;
            _tiles.Add(new TileConfig
            {
                Kind = kind,
                DisplayId = kind is MultiviewSourceKind.ProgramPreview or MultiviewSourceKind.AudienceOutput
                    ? display?.Id ?? ""
                    : "",
                CameraIndex = kind == MultiviewSourceKind.Camera ? camera!.Value : -1
            });
        }
        if (_tiles.Count == 0) DefaultTiles();
    }

    void DefaultTiles()
    {
        _tiles.Clear();
        var display = ResolveDisplay(_program.DisplayId) ?? _catalog.Displays.FirstOrDefault();
        if (display is not null)
        {
            _tiles.Add(new TileConfig { Kind = MultiviewSourceKind.ProgramPreview, DisplayId = display.Id });
            _tiles.Add(new TileConfig { Kind = MultiviewSourceKind.AudienceOutput, DisplayId = display.Id });
        }
        _tiles.Add(new TileConfig { Kind = MultiviewSourceKind.Replay });
        foreach (var index in _session.Library.Where(entry => entry.IsCamera).Select(entry => entry.CameraIndex).Distinct())
            _tiles.Add(new TileConfig { Kind = MultiviewSourceKind.Camera, CameraIndex = index });
        if (_tiles.Count == 0) _tiles.Add(new TileConfig { Kind = MultiviewSourceKind.Blank });
    }

    void AddTile()
    {
        var display = ResolveDisplay(_program.DisplayId);
        _tiles.Add(new TileConfig
        {
            Kind = display is null ? MultiviewSourceKind.Blank : MultiviewSourceKind.ProgramPreview,
            DisplayId = display?.Id ?? ""
        });
        SaveTiles();
        RenderTiles();
    }

    void ResetTiles()
    {
        DefaultTiles();
        SaveTiles();
        RenderTiles();
    }

    void CloseTile(TileConfig tile)
    {
        _tiles.Remove(tile);
        if (_tiles.Count == 0) _tiles.Add(new TileConfig { Kind = MultiviewSourceKind.Blank });
        SaveTiles();
        RenderTiles();
    }

    void SaveTiles()
    {
        _preferences.MultiviewTiles = _tiles.Select(tile => new MultiviewTileBinding
        {
            Source = SourceName(tile.Kind),
            DisplayId = tile.DisplayId,
            CameraIndex = tile.CameraIndex
        }).ToList();
        PreferencesStore.Save(_preferences);
    }

    static string SourceName(MultiviewSourceKind kind) => kind switch
    {
        MultiviewSourceKind.ProgramPreview => "preview",
        MultiviewSourceKind.AudienceOutput => "audience",
        MultiviewSourceKind.Camera => "camera",
        MultiviewSourceKind.Replay => "replay",
        _ => "blank"
    };

    static MultiviewSourceKind ParseSource(string? source) => (source ?? "").Trim().ToLowerInvariant() switch
    {
        "preview" or "program" => MultiviewSourceKind.ProgramPreview,
        "audience" or "output" => MultiviewSourceKind.AudienceOutput,
        "camera" => MultiviewSourceKind.Camera,
        "replay" => MultiviewSourceKind.Replay,
        _ => MultiviewSourceKind.Blank
    };

    ScorebugDisplay? ResolveDisplay(string? id) => _catalog.FindDisplay(id) ?? _catalog.Displays.FirstOrDefault();

    int? ResolveCamera(int index) => _session.Library.Any(entry => entry.IsCamera && entry.CameraIndex == index)
        ? index : null;

    string BuildSignature()
    {
        var displays = string.Join("|", _catalog.Displays.Select(display => $"{display.Id}:{display.Name}"));
        var cameras = string.Join("|", _session.Library.Where(entry => entry.IsCamera)
            .OrderBy(entry => entry.CameraIndex)
            .Select(entry => $"{entry.CameraIndex}:{entry.Label}"));
        return displays + "||" + cameras;
    }

    void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var signature = BuildSignature();
        if (!string.Equals(signature, _signature, StringComparison.Ordinal))
        {
            _signature = signature;
            RenderTiles();
        }
        SyncReplay();
    }

    void ProgramChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(ProgramChanged); return; }
        foreach (var view in _canvasViews) view.Rebuild();
    }

    /// <summary>Rebuilds the wall's chrome. Canvas views are disposed, camera listeners removed first.</summary>
    void RenderTiles()
    {
        if (_rendering) return;
        _rendering = true;
        try
        {
            foreach (var (feed, handler) in _cameraHandlers) feed.FrameReady -= handler;
            _cameraHandlers.Clear();
            foreach (var view in _canvasViews) view.Dispose();
            _canvasViews.Clear();
            // The replay view is moved from one tile to the next. Clear its old parent before the
            // tile is rebuilt or WPF refuses to add it to the new one.
            DetachFromParent(_replayBody);
            _panel.Children.Clear();

            foreach (var tile in _tiles.ToList()) _panel.Children.Add(BuildTile(tile));
            _status.Text = $"{_tiles.Count} tile{(_tiles.Count == 1 ? "" : "s")} · "
                + "choose a source in each tile, or remove it with ×.";
        }
        finally { _rendering = false; }
        SyncReplay();
    }

    Border BuildTile(TileConfig tile)
    {
        var source = new ComboBox
        {
            FontSize = 11,
            MinHeight = 26,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var choices = SourceChoices(tile);
        source.ItemsSource = choices;
        var selected = choices.FindIndex(choice => Matches(choice, tile));
        source.SelectedIndex = selected >= 0 ? selected : 0;
        source.SelectionChanged += (_, _) =>
        {
            if (_rendering || source.SelectedItem is not SourceChoice choice) return;
            ApplyChoice(tile, choice);
            SaveTiles();
            // Let the ComboBox close its drop-down before the wall is rebuilt underneath it.
            Dispatcher.BeginInvoke(new Action(RenderTiles));
        };

        var close = new Button
        {
            Content = "×",
            Width = 26,
            Height = 26,
            MinHeight = 26,
            Padding = new Thickness(0),
            FontSize = 15,
            ToolTip = "Remove this tile"
        };
        close.Click += (_, _) => CloseTile(tile);
        DockPanel.SetDock(close, Dock.Right);

        var head = new DockPanel { Margin = new Thickness(7, 5, 5, 3) };
        head.Children.Add(close);
        head.Children.Add(source);

        var body = BuildBody(tile);
        var bodyBorder = new Border
        {
            Height = 230,
            Background = Solid("#000000"),
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = body,
            ClipToBounds = true
        };
        var stack = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        stack.Children.Add(head);
        stack.Children.Add(bodyBorder);
        var border = new Border
        {
            Width = 420,
            Margin = new Thickness(6),
            Background = _surfaceBrush,
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = stack
        };
        return border;
    }

    void ApplyChoice(TileConfig tile, SourceChoice choice)
    {
        tile.Kind = choice.Kind;
        tile.DisplayId = choice.Kind is MultiviewSourceKind.ProgramPreview or MultiviewSourceKind.AudienceOutput
            ? choice.DisplayId : "";
        tile.CameraIndex = choice.Kind == MultiviewSourceKind.Camera ? choice.CameraIndex : -1;
    }

    static bool Matches(SourceChoice choice, TileConfig tile) =>
        choice.Kind == tile.Kind
        && (choice.Kind is not (MultiviewSourceKind.ProgramPreview or MultiviewSourceKind.AudienceOutput)
            || string.Equals(choice.DisplayId, tile.DisplayId, StringComparison.OrdinalIgnoreCase))
        && (choice.Kind != MultiviewSourceKind.Camera || choice.CameraIndex == tile.CameraIndex);

    List<SourceChoice> SourceChoices(TileConfig tile)
    {
        var choices = new List<SourceChoice>();
        foreach (var display in _catalog.Displays)
            choices.Add(new SourceChoice(MultiviewSourceKind.ProgramPreview, display.Id, -1, $"PROGRAM PREVIEW · {display.Name}"));
        foreach (var display in _catalog.Displays)
            choices.Add(new SourceChoice(MultiviewSourceKind.AudienceOutput, display.Id, -1, $"AUDIENCE OUTPUT · {display.Name}"));
        if (tile.Kind == MultiviewSourceKind.Replay || !_tiles.Any(other => other.Kind == MultiviewSourceKind.Replay))
            choices.Add(new SourceChoice(MultiviewSourceKind.Replay, "", -1, "REPLAY · live / recorded preview"));
        foreach (var camera in _session.Library.Where(entry => entry.IsCamera)
                     .GroupBy(entry => entry.CameraIndex).Select(group => group.First()).OrderBy(entry => entry.CameraIndex))
            choices.Add(new SourceChoice(MultiviewSourceKind.Camera, "", camera.CameraIndex, $"CAMERA · {camera.Label}"));
        choices.Add(new SourceChoice(MultiviewSourceKind.Blank, "", -1, "BLANK · spacer"));
        return choices;
    }

    static void DetachFromParent(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case ContentControl content: content.Content = null; break;
            case Decorator decorator: decorator.Child = null; break;
        }
    }

    UIElement BuildBody(TileConfig tile)
    {
        switch (tile.Kind)
        {
            case MultiviewSourceKind.ProgramPreview:
                return ProgramPreview(tile.DisplayId);
            case MultiviewSourceKind.AudienceOutput:
                return AudienceOutput(tile.DisplayId);
            case MultiviewSourceKind.Camera:
                return CameraTile(tile.CameraIndex);
            case MultiviewSourceKind.Replay:
                return _replayBody;
            default:
                return Hint("BLANK TILE", "Choose a source above, or press × to remove this tile.");
        }
    }

    UIElement ProgramPreview(string displayId)
    {
        var id = ResolveDisplay(displayId)?.Id ?? _program.DisplayId;
        var view = new ScorebugCanvasView(_catalog, _engine,
            stateProvider: () => _engine.State,
            playback: _playback,
            layoutProvider: () => _program.LayoutFor(id) ?? _catalog.PublishedCompositeOf(id),
            displayId: id,
            automation: _automation);
        _canvasViews.Add(view);
        return view;
    }

    UIElement AudienceOutput(string displayId)
    {
        var id = ResolveDisplay(displayId)?.Id ?? _program.DisplayId;
        var view = new ScorebugCanvasView(_catalog, _publishedEngine,
            playback: _playback,
            muteVideo: true,
            layoutProvider: () => _catalog.PublishedCompositeOf(id),
            onAirVideo: true,
            displayId: id,
            automation: _automation);
        _canvasViews.Add(view);
        return view;
    }

    UIElement CameraTile(int cameraIndex)
    {
        var entry = _session.Library.FirstOrDefault(candidate => candidate.IsCamera && candidate.CameraIndex == cameraIndex);
        if (entry is null) return Hint("CAMERA NOT FOUND", "Add the camera in the media dock, then choose it again.");
        var picture = new Image { Stretch = Stretch.Uniform };
        var feed = CameraFeed.Get(cameraIndex);
        var lastShown = DateTime.MinValue;
        // Frames arrive at capture rate; the mosaic only repaints ten times a second.
        Action<BitmapSource> handler = frame =>
        {
            var now = DateTime.UtcNow;
            if ((now - lastShown).TotalMilliseconds < 100) return;
            lastShown = now;
            Dispatcher.BeginInvoke(new Action(() => picture.Source = frame));
        };
        feed.FrameReady += handler;
        feed.Start();
        _cameraHandlers.Add((feed, handler));
        return picture;
    }

    UIElement Hint(string heading, string detail)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = _mutedBrush,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 11,
            Foreground = _mutedBrush,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        });
        return panel;
    }

    void BuildReplayBody()
    {
        _replayBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _replayBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _replayTitle.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetRow(_replayTitle, 0);
        _replayBody.Children.Add(_replayTitle);
        var pictures = new Grid();
        pictures.Children.Add(_replayMirror);
        pictures.Children.Add(_replayPicture);
        pictures.Children.Add(_replayHint);
        Grid.SetRow(pictures, 1);
        _replayBody.Children.Add(pictures);
    }

    /// <summary>Points the replay tile at the replay while one is open, and at the cued camera otherwise.</summary>
    void SyncReplay()
    {
        var frames = _session.Playback.ReplayFrames;
        if (_session.IsReplaying && _session.Current is { IsVideo: true } replay && frames is not null && frames.Contains(replay.Path))
        {
            _replayTitle.Text = $"REPLAY · {_session.CameraEntry?.Label ?? "camera"}"
                + (_session.ReplayOnAir ? " · ON AIR" : " · PREVIEW")
                + (_session.IsPlaying ? " · playing" : " · paused");
            StopMirror();
            if (_replayPicture.Source is null && frames.Frame is { } picture) _replayPicture.Source = picture;
            _replayHint.Visibility = _replayPicture.Source is null ? Visibility.Visible : Visibility.Collapsed;
            _replayPicture.Visibility = Visibility.Visible;
            _replayMirror.Visibility = Visibility.Collapsed;
            return;
        }

        _replayPicture.Visibility = Visibility.Collapsed;
        MirrorCamera();
    }

    void OnReplayFrame(BitmapSource frame)
    {
        if (_replayPicture.Visibility != Visibility.Visible) return;
        _replayPicture.Source = frame;
        _replayHint.Visibility = Visibility.Collapsed;
    }

    void MirrorCamera()
    {
        var entry = _session.CameraEntry;
        if (entry is null)
        {
            _replayTitle.Text = "REPLAY · nothing cued";
            _replayHint.Text = "Press Replay in the media dock to review a camera.";
            _replayHint.Visibility = Visibility.Visible;
            _replayMirror.Visibility = Visibility.Collapsed;
            StopMirror();
            return;
        }

        if (_mirrorIndex != entry.CameraIndex || _mirrorFeed is null)
        {
            StopMirror();
            var feed = CameraFeed.Get(entry.CameraIndex);
            var lastShown = DateTime.MinValue;
            Action<BitmapSource> handler = frame =>
            {
                var now = DateTime.UtcNow;
                if ((now - lastShown).TotalMilliseconds < 100) return;
                lastShown = now;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_replayMirror.Visibility != Visibility.Visible) return;
                    _replayMirror.Source = frame;
                }));
            };
            feed.FrameReady += handler;
            feed.Start();
            _mirrorFeed = feed;
            _mirrorHandler = handler;
            _mirrorIndex = entry.CameraIndex;
        }

        _replayTitle.Text = $"REPLAY · {entry.Label} · live";
        _replayMirror.Visibility = Visibility.Visible;
        _replayHint.Visibility = _replayMirror.Source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    void StopMirror()
    {
        if (_mirrorFeed is not null && _mirrorHandler is not null) _mirrorFeed.FrameReady -= _mirrorHandler;
        _mirrorFeed = null;
        _mirrorHandler = null;
        _mirrorIndex = -1;
    }

    void Shutdown()
    {
        _tick.Stop();
        _program.Changed -= ProgramChanged;
        foreach (var (feed, handler) in _cameraHandlers) feed.FrameReady -= handler;
        _cameraHandlers.Clear();
        StopMirror();
        foreach (var view in _canvasViews) view.Dispose();
        _canvasViews.Clear();
        if (_session.Playback.ReplayFrames is { } frames) frames.FrameReady -= OnReplayFrame;
    }
}
