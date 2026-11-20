using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public enum MultiviewSourceKind { ProgramPreview, AudienceOutput, Camera, Replay, Blank }

/// <summary>A flattened tile for the browser multiview. It carries the saved source, not the
/// desktop window's live controls, so a browser page can render the same wall without one open.</summary>
public sealed record MultiviewBrowserTile(
    string Kind, string Label, string DisplayId, string LayoutId, int CameraIndex);

/// <summary>One saved multiview tile. The source name is a string so old preference files stay readable.</summary>
public sealed class MultiviewTileBinding
{
    public string Source { get; set; } = "preview";
    public string DisplayId { get; set; } = "";
    public int CameraIndex { get; set; } = -1;
    /// <summary>A specific layout to show, even when it is not the one staged or on air. Empty follows the desk.</summary>
    public string LayoutId { get; set; } = "";
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
        public string LayoutId { get; set; } = "";
    }

    sealed record SourceChoice(MultiviewSourceKind Kind, string DisplayId, int CameraIndex, string Label, string LayoutId = "")
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
    // The wall itself: one UniformGrid stretching every camera to fill the window with no chrome.
    readonly Grid _wall = new() { Background = Brushes.Black, ClipToBounds = true };
    readonly UniformGrid _wallTiles = new();
    readonly DockPanel _editorChrome = new() { Visibility = Visibility.Collapsed };
    readonly Button _editButton = new()
    {
        Content = "✎",
        Width = 38,
        Height = 34,
        Padding = new Thickness(0),
        FontSize = 16,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(8),
        ToolTip = "Edit multiview tiles"
    };
    bool _editing;
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
    readonly CheckBox _labelsToggle = new()
    {
        Content = "Show source names on tiles",
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 10, 0)
    };
    readonly CheckBox _soundToggle = new()
    {
        Content = "Sound bars on cameras with a microphone",
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 10, 0),
        ToolTip = "Draw a live level bar beside a camera tile whose camera names a microphone in the media dock"
    };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(400) };
    // The wall repaints two and a half times a second, which is plenty for pictures and far too
    // coarse for a level meter: the bars get their own faster clock so they track a voice.
    readonly DispatcherTimer _meterTick = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly List<TileConfig> _tiles = [];
    readonly List<ScorebugCanvasView> _canvasViews = [];
    readonly List<(CameraFeed Feed, Action<BitmapSource> Handler)> _cameraHandlers = [];
    string _signature = "";
    bool _rendering;
    bool _showLabels;
    int _focusedIndex;
    bool _maximized;
    /// <summary>Off after Escape, so the wall is a clean picture with no green outline on it.</summary>
    bool _showSelection = true;
    // Sound bars beside camera tiles. Each entry is a camera item and the fill it drives.
    readonly List<(MediaEntry Camera, Border Fill)> _meters = [];

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
    ReplayFeed? _replayFeed;

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
        Focusable = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _showLabels = preferences.MultiviewShowLabels;
        _labelsToggle.IsChecked = _showLabels;
        _labelsToggle.Checked += (_, _) => SetLabels(true);
        _labelsToggle.Unchecked += (_, _) => SetLabels(false);
        _soundToggle.IsChecked = preferences.MultiviewCameraSoundBars;
        _soundToggle.Checked += (_, _) => SetSoundBars(true);
        _soundToggle.Unchecked += (_, _) => SetSoundBars(false);
        Content = BuildChrome();
        _wall.SizeChanged += (_, _) => UpdateWallColumns();
        ApplyPalette(preferences.Theme);
        BuildReplayBody();
        LoadTiles();
        _signature = BuildSignature();

        SyncReplayFeed();
        _program.Changed += ProgramChanged;
        RenderTiles();
        _tick.Tick += (_, _) => Refresh();
        _tick.Start();
        _meterTick.Tick += (_, _) => UpdateMeters();
        _meterTick.Start();
        Closed += (_, _) => Shutdown();
        ThemeManager.Apply(this, preferences.Theme);
        ApplyPalette(preferences.Theme);
        // The window-wide theme pass runs on Loaded and paints every TextBlock; re-apply the few
        // bespoke pieces afterwards so the title and hint never wash out into the black wall.
        Loaded += (_, _) => { ApplyPalette(preferences.Theme); Focus(); };
    }

    UIElement BuildChrome()
    {
        var root = new Grid();
        _wall.Children.Add(_wallTiles);
        root.Children.Add(_wall);
        var header = new DockPanel { Margin = new Thickness(14, 11, 14, 0), LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var done = new Button
        {
            Content = "✓ Done",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Close the edit menu and show the camera wall"
        };
        done.Click += (_, _) => ToggleEditing();
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
        actions.Children.Add(done);
        actions.Children.Add(add);
        actions.Children.Add(reset);
        actions.Children.Add(_labelsToggle);
        actions.Children.Add(_soundToggle);
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(_title);

        // Layout files get their own row. Keeping them beside Add/Reset was what clipped the
        // save control into the reset button on a narrow or scaled window.
        var layoutActions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(14, 0, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var saveLayout = new Button
        {
            Content = "Save .spml",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Save every tile, display and camera choice as a ScorePlus Multiview Layout"
        };
        saveLayout.Click += (_, _) => SaveLayoutFile();
        var importLayout = new Button
        {
            Content = "Import .spml",
            Padding = new Thickness(12, 6, 12, 6),
            ToolTip = "Replace this wall with a saved ScorePlus Multiview Layout"
        };
        importLayout.Click += (_, _) => ImportLayoutFile();
        layoutActions.Children.Add(saveLayout);
        layoutActions.Children.Add(importLayout);

        var top = new StackPanel();
        top.Children.Add(header);
        top.Children.Add(layoutActions);
        DockPanel.SetDock(top, Dock.Top);
        _editorChrome.Children.Add(top);

        DockPanel.SetDock(_status, Dock.Top);
        _editorChrome.Children.Add(_status);
        _editorChrome.Children.Add(new ScrollViewer
        {
            Content = _panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        root.Children.Add(_editorChrome);
        _editButton.Click += (_, _) => ToggleEditing();
        Panel.SetZIndex(_editButton, 20);
        root.Children.Add(_editButton);
        return root;
    }

    void ToggleEditing()
    {
        _editing = !_editing;
        _editorChrome.Visibility = _editing ? Visibility.Visible : Visibility.Collapsed;
        _wall.Visibility = _editing ? Visibility.Collapsed : Visibility.Visible;
        // The edit menu has its own Done button. Leaving the floating checkmark over Reset while
        // editing made the two controls collide on a narrow window.
        _editButton.Visibility = _editing ? Visibility.Collapsed : Visibility.Visible;
        _editButton.Content = "✎";
        _editButton.ToolTip = "Edit multiview tiles";
        RenderTiles();
    }

    void SetSoundBars(bool show)
    {
        _preferences.MultiviewCameraSoundBars = show;
        // Remember that the choice was the operator's, so the migration in PreferencesStore leaves
        // it alone the next time the show is opened.
        _preferences.MultiviewSoundBarsChosen = true;
        PreferencesStore.Save(_preferences);
        RenderTiles();
    }

    void SetLabels(bool show)
    {
        if (_showLabels == show) return;
        _showLabels = show;
        _preferences.MultiviewShowLabels = show;
        PreferencesStore.Save(_preferences);
        RenderTiles();
    }

    void UpdateWallColumns()
    {
        var count = Math.Max(1, _maximized ? 1 : _tiles.Count);
        var width = Math.Max(1, _wall.ActualWidth);
        var height = Math.Max(1, _wall.ActualHeight);
        // Pick the grid with the largest usable cell, preferring the screen's shape. This is what
        // turns the wall into as many tiles as the window can physically show.
        var bestColumns = 1;
        var bestArea = -1d;
        for (var columns = 1; columns <= count; columns++)
        {
            var rows = (int)Math.Ceiling(count / (double)columns);
            var cellWidth = width / columns;
            var cellHeight = height / rows;
            if (cellWidth < 24 || cellHeight < 24) continue;
            var area = Math.Min(cellWidth / 16d, cellHeight / 9d);
            if (area <= bestArea) continue;
            bestArea = area;
            bestColumns = columns;
        }
        _wallTiles.Columns = bestColumns;
        _wallTiles.Rows = 0;
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
        _wall.Background = Brushes.Black;
        _editorChrome.Background = _windowBrush;
        _title.Foreground = _textBrush;
        _status.Foreground = _mutedBrush;
        _labelsToggle.Foreground = _textBrush;
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
                CameraIndex = kind == MultiviewSourceKind.Camera ? camera!.Value : -1,
                LayoutId = kind == MultiviewSourceKind.ProgramPreview ? binding.LayoutId ?? "" : ""
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

    /// <summary>The saved wall as data for the browser: layout tiles, audience outputs, cameras,
    /// the replay monitor and blanks. No desktop window has to be open.</summary>
    public static IReadOnlyList<MultiviewBrowserTile> BrowserTiles(UserPreferences preferences,
        ScorebugCatalog catalog, MediaSession session, ProgramSelection program)
    {
        var fallbackDisplay = catalog.FindDisplay(program.DisplayId) ?? catalog.Displays.FirstOrDefault();
        ScorebugDisplay? DisplayFor(string? id) => !string.IsNullOrWhiteSpace(id)
            ? catalog.FindDisplay(id!) ?? fallbackDisplay : fallbackDisplay;
        bool CameraExists(int index) => session.Library.Any(entry => entry.IsCamera && entry.CameraIndex == index);
        var tiles = new List<MultiviewBrowserTile>();
        var hasReplay = false;
        foreach (var binding in preferences.MultiviewTiles ?? [])
        {
            var kind = ParseSource(binding.Source);
            if (kind == MultiviewSourceKind.Replay && hasReplay) kind = MultiviewSourceKind.Blank;
            if (kind == MultiviewSourceKind.Replay) hasReplay = true;
            if (kind is MultiviewSourceKind.Camera && !CameraExists(binding.CameraIndex)) kind = MultiviewSourceKind.Blank;
            var display = kind is MultiviewSourceKind.ProgramPreview or MultiviewSourceKind.AudienceOutput
                ? DisplayFor(binding.DisplayId) : null;
            if (kind is MultiviewSourceKind.ProgramPreview or MultiviewSourceKind.AudienceOutput && display is null)
                kind = MultiviewSourceKind.Blank;
            var layoutId = kind == MultiviewSourceKind.ProgramPreview ? binding.LayoutId ?? "" : "";
            var layout = !string.IsNullOrWhiteSpace(layoutId) ? catalog.FindLayout(layoutId) : null;
            var camera = kind == MultiviewSourceKind.Camera
                ? session.Library.FirstOrDefault(entry => entry.IsCamera && entry.CameraIndex == binding.CameraIndex)
                : null;
            var label = kind switch
            {
                MultiviewSourceKind.ProgramPreview => layout is not null
                    ? $"PROGRAM · {layout.Name}" : $"PROGRAM · {display?.Name ?? "preview"}",
                MultiviewSourceKind.AudienceOutput => $"AUDIENCE · {display?.Name ?? "output"}",
                MultiviewSourceKind.Camera => camera?.Label ?? CameraFeed.CameraName(binding.CameraIndex),
                MultiviewSourceKind.Replay => "REPLAY · live / recorded preview",
                _ => ""
            };
            tiles.Add(new MultiviewBrowserTile(SourceName(kind), label,
                kind is MultiviewSourceKind.ProgramPreview or MultiviewSourceKind.AudienceOutput ? display?.Id ?? "" : "",
                layoutId, kind == MultiviewSourceKind.Camera ? binding.CameraIndex : -1));
        }
        if (tiles.Count == 0)
        {
            if (fallbackDisplay is not null)
            {
                tiles.Add(new MultiviewBrowserTile("preview", $"PROGRAM · {fallbackDisplay.Name}", fallbackDisplay.Id, "", -1));
                tiles.Add(new MultiviewBrowserTile("audience", $"AUDIENCE · {fallbackDisplay.Name}", fallbackDisplay.Id, "", -1));
            }
            tiles.Add(new MultiviewBrowserTile("replay", "REPLAY · live / recorded preview", "", "", -1));
            foreach (var entry in session.Library.Where(entry => entry.IsCamera).GroupBy(entry => entry.CameraIndex).Select(group => group.First()))
                tiles.Add(new MultiviewBrowserTile("camera", entry.Label, "", "", entry.CameraIndex));
            if (tiles.Count == 0) tiles.Add(new MultiviewBrowserTile("blank", "", "", "", -1));
        }
        return tiles;
    }

    /// <summary>Writes the wall's tile order, displays, layouts and cameras to a .spml file.</summary>
    void SaveLayoutFile()
    {
        if (!_editing) return;
        var dialog = new SaveFileDialog
        {
            Title = "Save multiview layout",
            Filter = MultiviewLayoutFiles.Filter,
            DefaultExt = "spml",
            AddExtension = true,
            FileName = $"multiview-{DateTime.Now:yyyyMMdd-HHmm}"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var document = new MultiviewLayoutDocument
            {
                Name = Path.GetFileNameWithoutExtension(dialog.FileName),
                Tiles = _tiles.Select(tile =>
                {
                    var display = tile.Kind is MultiviewSourceKind.ProgramPreview or MultiviewSourceKind.AudienceOutput
                        ? ResolveDisplay(tile.DisplayId)
                        : null;
                    var layout = tile.Kind == MultiviewSourceKind.ProgramPreview && !string.IsNullOrWhiteSpace(tile.LayoutId)
                        ? _catalog.FindLayout(tile.LayoutId)
                        : null;
                    var camera = tile.Kind == MultiviewSourceKind.Camera
                        ? _session.Library.FirstOrDefault(entry => entry.IsCamera && entry.CameraIndex == tile.CameraIndex)
                        : null;
                    return new MultiviewLayoutTile
                    {
                        Source = SourceName(tile.Kind),
                        DisplayId = display?.Id ?? "",
                        DisplayName = display?.Name ?? "",
                        LayoutId = layout?.Id ?? "",
                        LayoutName = layout?.Name ?? "",
                        CameraIndex = camera?.CameraIndex ?? -1,
                        CameraName = camera?.Label ?? ""
                    };
                }).ToList()
            };
            MultiviewLayoutFiles.Save(dialog.FileName, document);
            _status.Text = $"LAYOUT SAVED · {Path.GetFileName(dialog.FileName)} · {_tiles.Count} tiles";
        }
        catch (Exception ex)
        {
            _status.Text = $"COULD NOT SAVE LAYOUT · {ex.Message}";
        }
    }

    /// <summary>Replaces the wall with a .spml file, matching displays and cameras by id then name.</summary>
    void ImportLayoutFile()
    {
        if (!_editing) return;
        var dialog = new OpenFileDialog
        {
            Title = "Import multiview layout",
            Filter = MultiviewLayoutFiles.Filter,
            DefaultExt = "spml",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        var document = MultiviewLayoutFiles.Load(dialog.FileName);
        if (document is null)
        {
            _status.Text = "COULD NOT IMPORT LAYOUT · not a ScorePlus Multiview Layout";
            return;
        }

        var imported = new List<TileConfig>();
        var hasReplay = false;
        foreach (var source in document.Tiles)
        {
            var kind = ParseSource(source.Source);
            if (kind == MultiviewSourceKind.Replay && hasReplay) kind = MultiviewSourceKind.Blank;
            if (kind == MultiviewSourceKind.Replay) hasReplay = true;
            var display = ResolveDisplay(source.DisplayId, source.DisplayName);
            var camera = ResolveCamera(source.CameraIndex, source.CameraName);
            if (kind == MultiviewSourceKind.Camera && camera is null) kind = MultiviewSourceKind.Blank;
            if (kind is MultiviewSourceKind.ProgramPreview or MultiviewSourceKind.AudienceOutput && display is null)
                kind = MultiviewSourceKind.Blank;
            imported.Add(new TileConfig
            {
                Kind = kind,
                DisplayId = kind is MultiviewSourceKind.ProgramPreview or MultiviewSourceKind.AudienceOutput
                    ? display?.Id ?? ""
                    : "",
                CameraIndex = kind == MultiviewSourceKind.Camera ? camera!.Value : -1,
                LayoutId = kind == MultiviewSourceKind.ProgramPreview
                    ? ResolveLayoutId(display, source.LayoutId, source.LayoutName)
                    : ""
            });
        }
        if (imported.Count == 0) imported.Add(new TileConfig { Kind = MultiviewSourceKind.Blank });
        _tiles.Clear();
        _tiles.AddRange(imported);
        SaveTiles();
        RenderTiles();
        _status.Text = $"LAYOUT IMPORTED · {Path.GetFileName(dialog.FileName)} · {_tiles.Count} tiles";
    }

    string ResolveLayoutId(ScorebugDisplay? display, string? id, string? name)
    {
        if (display is null) return "";
        if (!string.IsNullOrWhiteSpace(id) && display.Layouts.Any(layout => string.Equals(layout.Id, id, StringComparison.OrdinalIgnoreCase)))
            return id;
        return display.Layouts.FirstOrDefault(layout =>
            string.Equals(layout.Name, name, StringComparison.OrdinalIgnoreCase))?.Id ?? "";
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
            CameraIndex = tile.CameraIndex,
            LayoutId = tile.LayoutId
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

    ScorebugDisplay? ResolveDisplay(string? id, string? name = null)
    {
        var byId = _catalog.FindDisplay(id);
        if (byId is not null) return byId;
        if (!string.IsNullOrWhiteSpace(name))
        {
            var byName = _catalog.Displays.FirstOrDefault(display =>
                string.Equals(display.Name, name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName;
        }
        return _catalog.Displays.FirstOrDefault();
    }

    int? ResolveCamera(int index, string? name = null)
    {
        if (_session.Library.Any(entry => entry.IsCamera && entry.CameraIndex == index)) return index;
        if (!string.IsNullOrWhiteSpace(name))
        {
            var byName = _session.Library.FirstOrDefault(entry => entry.IsCamera
                && string.Equals(entry.Label, name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName.CameraIndex;
        }
        return null;
    }

    string BuildSignature()
    {
        var displays = string.Join("|", _catalog.Displays.Select(display =>
            $"{display.Id}:{display.Name}:" + string.Join(",", display.Layouts.Select(layout => $"{layout.Id}={layout.Name}"))));
        var cameras = string.Join("|", _session.Library.Where(entry => entry.IsCamera)
            .OrderBy(entry => entry.CameraIndex)
            .Select(entry => $"{entry.CameraIndex}:{entry.Label}"));
        // A microphone added to the library has to reach the tile's picker, so the wall is rebuilt
        // when the microphones change too. The link itself is left out: choosing one rebuilds
        // nothing, which is what keeps the picker from closing under the operator's cursor.
        var microphones = string.Join("|", _session.Library.Where(entry => entry.IsMicrophone)
            .Select(entry => entry.Path));
        return displays + "||" + cameras + "||" + microphones;
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
            _meters.Clear();
            _panel.Children.Clear();
            _wallTiles.Children.Clear();

            if (_editing)
            {
                foreach (var tile in _tiles.ToList()) _panel.Children.Add(BuildTile(tile, editing: true));
                _status.Text = $"{_tiles.Count} tile{(_tiles.Count == 1 ? "" : "s")} · "
                    + "choose a source in each tile, or remove it with ×.";
            }
            else
            {
                if (_tiles.Count == 0) _tiles.Add(new TileConfig { Kind = MultiviewSourceKind.Blank });
                _focusedIndex = Math.Clamp(_focusedIndex, 0, _tiles.Count - 1);
                IEnumerable<TileConfig> visible = _maximized ? [_tiles[_focusedIndex]] : _tiles;
                foreach (var tile in visible.ToList()) _wallTiles.Children.Add(BuildTile(tile, editing: false));
                UpdateWallColumns();
                _status.Text = _maximized
                    ? $"{TileLabel(_tiles[_focusedIndex])} · arrows change the camera · Enter or Esc returns to the wall"
                    : "ARROWS SELECT · ENTER ENLARGE · EDIT FOR SOURCE NAMES";
            }
        }
        finally { _rendering = false; }
        SyncReplay();
    }

    Border BuildTile(TileConfig tile, bool editing)
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
        var bodyContent = new Grid();
        bodyContent.Children.Add(body);
        // The name belongs to the picture, not the editor chrome: a director can hide it while
        // editing and still see which camera or layout is under the cursor.
        if (_showLabels && !string.IsNullOrWhiteSpace(TileLabel(tile)))
            bodyContent.Children.Add(LabelPlate(TileLabel(tile)));
        var bodyBorder = new Border
        {
            Height = editing ? 230 : double.NaN,
            Background = Solid("#000000"),
            BorderBrush = _borderBrush,
            BorderThickness = editing ? new Thickness(0, 1, 0, 0) : new Thickness(0),
            Child = bodyContent,
            ClipToBounds = true
        };
        var stack = new DockPanel();
        if (editing)
        {
            DockPanel.SetDock(head, Dock.Top);
            stack.Children.Add(head);
            // A camera carries no sound of its own, so its tile is where the director says which
            // microphone that picture listens to — the same link the wall's sound bar reads.
            if (MicrophoneRow(tile) is { } microphoneRow)
            {
                DockPanel.SetDock(microphoneRow, Dock.Top);
                stack.Children.Add(microphoneRow);
            }
        }
        stack.Children.Add(bodyBorder);
        var border = new Border
        {
            Width = editing ? 420 : double.NaN,
            Margin = editing ? new Thickness(6) : new Thickness(1),
            Background = editing ? _surfaceBrush : Brushes.Black,
            BorderBrush = editing ? _borderBrush : IsFocused(tile) ? Solid("#38D7A3") : Brushes.Transparent,
            // Keep the border thickness constant. Toggling it on selection shifted the picture by
            // two pixels and forced WPF to lay the whole tile out again.
            BorderThickness = editing ? new Thickness(1) : new Thickness(2),
            CornerRadius = editing ? new CornerRadius(8) : new CornerRadius(0),
            Child = stack,
            Tag = tile
        };
        if (!editing)
        {
            border.MouseLeftButtonDown += (_, _) => SelectTile(tile, maximize: false);
            border.MouseRightButtonDown += (_, _) => SelectTile(tile, maximize: true);
        }
        return border;
    }

    /// <summary>
    /// The microphone picker for one camera tile: every microphone in the library, plus the choice
    /// to leave the camera silent. It writes the camera's microphone link, so the tile's bar and the
    /// dock's camera picker always agree. Null for tiles that are not a known camera.
    /// </summary>
    FrameworkElement? MicrophoneRow(TileConfig tile)
    {
        if (tile.Kind != MultiviewSourceKind.Camera) return null;
        var camera = _session.Library.FirstOrDefault(entry => entry.IsCamera && entry.CameraIndex == tile.CameraIndex);
        if (camera is null) return null;
        var microphones = _session.Library.Where(entry => entry.IsMicrophone).ToArray();
        var picker = new ComboBox
        {
            FontSize = 11,
            MinHeight = 26,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        picker.ItemsSource = new[] { "no microphone" }.Concat(microphones.Select(mic => mic.Label)).ToArray();
        var linked = Array.FindIndex(microphones, mic =>
            string.Equals(mic.Path, camera.MicrophoneLink, StringComparison.OrdinalIgnoreCase));
        picker.SelectedIndex = linked < 0 ? 0 : linked + 1;
        picker.ToolTip = $"Which microphone {camera.Label} listens to. Its sound bar in the wall reads this microphone";
        picker.SelectionChanged += (_, _) =>
        {
            if (_rendering) return;
            var index = picker.SelectedIndex - 1;
            var path = index >= 0 && index < microphones.Length ? microphones[index].Path : null;
            if (string.Equals(path ?? "", camera.MicrophoneLink ?? "", StringComparison.OrdinalIgnoreCase)) return;
            _session.SetCameraMicrophone(camera, path);
        };
        var label = new TextBlock
        {
            Text = "MIC",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = _mutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var row = new DockPanel { Margin = new Thickness(7, 0, 5, 4) };
        row.Children.Add(label);
        row.Children.Add(picker);
        return row;
    }

    static Border LabelPlate(string text)
    {
        var plate = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0, 0, 0)),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(8),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 320
            }
        };
        // The name sits on the black picture, not on the operator theme, so keep it white in both
        // themes instead of letting the window-wide theme pass repaint it dark.
        ThemeManager.SetSkipTheme(plate, true);
        return plate;
    }

    string TileLabel(TileConfig tile)
    {
        switch (tile.Kind)
        {
            case MultiviewSourceKind.ProgramPreview:
                var display = ResolveDisplay(tile.DisplayId);
                var layout = !string.IsNullOrWhiteSpace(tile.LayoutId) ? _catalog.FindLayout(tile.LayoutId) : null;
                return layout is not null ? $"PROGRAM · {layout.Name}" : $"PROGRAM · {display?.Name ?? "preview"}";
            case MultiviewSourceKind.AudienceOutput:
                return $"AUDIENCE · {ResolveDisplay(tile.DisplayId)?.Name ?? "output"}";
            case MultiviewSourceKind.Camera:
                return _session.Library.FirstOrDefault(entry => entry.IsCamera && entry.CameraIndex == tile.CameraIndex)?.Label
                    ?? CameraFeed.CameraName(tile.CameraIndex);
            case MultiviewSourceKind.Replay:
                return "REPLAY · live / recorded preview";
            default:
                return "";
        }
    }

    bool IsFocused(TileConfig tile) =>
        _showSelection && _tiles.Count > 0
        && ReferenceEquals(tile, _tiles[Math.Clamp(_focusedIndex, 0, _tiles.Count - 1)]);

    /// <summary>Escape clears the green outline: the wall then reads as a clean picture.</summary>
    void ClearSelection()
    {
        _showSelection = false;
        UpdateSelectionBorders();
        Focus();
    }

    void UpdateSelectionBorders()
    {
        if (_editing) return;
        foreach (var border in _wallTiles.Children.OfType<Border>())
            if (border.Tag is TileConfig tile)
                border.BorderBrush = IsFocused(tile) ? Solid("#38D7A3") : Brushes.Transparent;
    }

    void SelectTile(TileConfig tile, bool maximize)
    {
        var index = _tiles.IndexOf(tile);
        if (index < 0) return;
        _focusedIndex = index;
        _showSelection = true;
        if (maximize)
        {
            _maximized = true;
            RenderTiles();
        }
        else UpdateSelectionBorders();
        Focus();
    }

    bool TryCameraAction(string actionId)
    {
        switch (actionId)
        {
            case "camera.left": MoveFocus(-1, 0); return true;
            case "camera.right": MoveFocus(1, 0); return true;
            case "camera.up": MoveFocus(0, -1); return true;
            case "camera.down": MoveFocus(0, 1); return true;
            case "camera.enter":
                if (_tiles[_focusedIndex].Kind == MultiviewSourceKind.Blank) return true;
                _maximized = !_maximized;
                RenderTiles();
                Focus();
                return true;
            case "camera.back":
                if (!_maximized) return false;
                _maximized = false;
                RenderTiles();
                Focus();
                return true;
            default:
                return false;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_editing && _tiles.Count > 0)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var bound = KeybindStore.Match(KeybindStore.Load(), key, Keyboard.Modifiers);
            if (bound is not null && TryCameraAction(bound))
            {
                e.Handled = true;
                return;
            }
            // A bound action that belongs to the console must not make Enter or the arrow keys inert.
            switch (key)
            {
                case Key.Left: MoveFocus(-1, 0); e.Handled = true; return;
                case Key.Right: MoveFocus(1, 0); e.Handled = true; return;
                case Key.Up: MoveFocus(0, -1); e.Handled = true; return;
                case Key.Down: MoveFocus(0, 1); e.Handled = true; return;
                case Key.Enter:
                    if (_tiles[_focusedIndex].Kind != MultiviewSourceKind.Blank)
                    {
                        _maximized = !_maximized;
                        RenderTiles();
                        Focus();
                        e.Handled = true;
                    }
                    return;
                case Key.Escape:
                    if (_maximized)
                    {
                        _maximized = false;
                        RenderTiles();
                        Focus();
                        e.Handled = true;
                    }
                    else if (_showSelection)
                    {
                        // Esc is the wall's "show me the picture" key: the green outline goes.
                        ClearSelection();
                        e.Handled = true;
                    }
                    return;
            }
        }
        base.OnPreviewKeyDown(e);
    }

    /// <summary>Moves through the wall's actual grid. Up and Down change rows; Left and Right
    /// change columns. In the enlarged single-tile view the same arrows step through cameras.</summary>
    void MoveFocus(int dx, int dy)
    {
        if (_tiles.Count == 0) return;
        var index = Math.Clamp(_focusedIndex, 0, _tiles.Count - 1);
        var columns = Math.Max(1, _maximized ? 1 : _wallTiles.Columns);
        if (dy != 0)
        {
            var row = Math.Clamp(index / columns + dy, 0, (_tiles.Count - 1) / columns);
            index = Math.Min(row * columns + index % columns, _tiles.Count - 1);
        }
        if (dx != 0)
        {
            var target = index + dx;
            if (target < 0 || target >= _tiles.Count) return;
            index = target;
        }
        _focusedIndex = index;
        _showSelection = true;
        // Moving the selection is only a border change on the full wall. Rebuilding the tiles made
        // every camera flash black because each Image lost its Source until the next frame arrived.
        if (_maximized) RenderTiles();
        else UpdateSelectionBorders();
        Focus();
    }

    void ApplyChoice(TileConfig tile, SourceChoice choice)
    {
        tile.Kind = choice.Kind;
        tile.DisplayId = choice.Kind is MultiviewSourceKind.ProgramPreview or MultiviewSourceKind.AudienceOutput
            ? choice.DisplayId : "";
        tile.CameraIndex = choice.Kind == MultiviewSourceKind.Camera ? choice.CameraIndex : -1;
        tile.LayoutId = choice.Kind == MultiviewSourceKind.ProgramPreview ? choice.LayoutId : "";
    }

    static bool Matches(SourceChoice choice, TileConfig tile) =>
        choice.Kind == tile.Kind
        && (choice.Kind is not (MultiviewSourceKind.ProgramPreview or MultiviewSourceKind.AudienceOutput)
            || string.Equals(choice.DisplayId, tile.DisplayId, StringComparison.OrdinalIgnoreCase))
        && (choice.Kind != MultiviewSourceKind.ProgramPreview
            || string.Equals(choice.LayoutId, tile.LayoutId, StringComparison.OrdinalIgnoreCase))
        && (choice.Kind != MultiviewSourceKind.Camera || choice.CameraIndex == tile.CameraIndex);

    List<SourceChoice> SourceChoices(TileConfig tile)
    {
        var choices = new List<SourceChoice>();
        foreach (var display in _catalog.Displays)
            choices.Add(new SourceChoice(MultiviewSourceKind.ProgramPreview, display.Id, -1, $"PROGRAM PREVIEW · {display.Name}"));
        // Any layout, on air or not. This lets a director check a lower third or an alternate look
        // without staging it on the desk and risking it going out by accident.
        foreach (var display in _catalog.Displays)
            foreach (var layout in display.Layouts)
                choices.Add(new SourceChoice(MultiviewSourceKind.ProgramPreview, display.Id, -1,
                    $"LAYOUT · {display.Name} / {layout.Name}", layout.Id));
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
                return ProgramPreview(tile.DisplayId, tile.LayoutId);
            case MultiviewSourceKind.AudienceOutput:
                return AudienceOutput(tile.DisplayId);
            case MultiviewSourceKind.Camera:
                return CameraTile(tile.CameraIndex);
            case MultiviewSourceKind.Replay:
                return _replayBody;
            default:
                // A spacer is a deliberate black hole in the wall, not a card with instructions.
                return new Border { Background = Brushes.Black };
        }
    }

    UIElement ProgramPreview(string displayId, string layoutId)
    {
        var id = ResolveDisplay(displayId)?.Id ?? _program.DisplayId;
        var view = new ScorebugCanvasView(_catalog, _engine,
            stateProvider: () => _engine.State,
            playback: _playback,
            layoutProvider: () => !string.IsNullOrWhiteSpace(layoutId)
                ? _catalog.FindLayout(layoutId) ?? _program.LayoutFor(id) ?? _catalog.PublishedCompositeOf(id)
                : _program.LayoutFor(id) ?? _catalog.PublishedCompositeOf(id),
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
        // Every camera tile carries a level bar while the setting is on. One that names a
        // microphone reads that microphone; the others read the show's master mix, so the wall
        // always shows where the sound is instead of silently drawing nothing.
        if (_preferences.MultiviewCameraSoundBars)
        {
            var host = new Grid();
            host.Children.Add(picture);
            host.Children.Add(SoundBar(entry));
            RegisterCameraFeed(entry.CameraIndex, picture);
            return host;
        }
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

    /// <summary>Subscribes a tile's picture to its camera without adding any chrome around it.</summary>
    void RegisterCameraFeed(int cameraIndex, Image picture)
    {
        var feed = CameraFeed.Get(cameraIndex);
        var lastShown = DateTime.MinValue;
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
    }

    /// <summary>A thin vertical meter pinned to a camera tile's right edge.</summary>
    Border SoundBar(MediaEntry camera)
    {
        var fill = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Solid("#38D7A3"),
            Height = 0,
            CornerRadius = new CornerRadius(2)
        };
        ThemeManager.SetSkipTheme(fill, true);
        var track = new Border
        {
            Width = 12,
            Margin = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)),
            // An edge keeps the meter readable against a bright camera picture even at silence.
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(2),
            Child = fill,
            ToolTip = _session.MicrophoneFor(camera) is { } microphone
                ? $"{camera.Label} · {microphone.Label}"
                : $"{camera.Label} · no microphone linked · master mix"
        };
        ThemeManager.SetSkipTheme(track, true);
        _meters.Add((camera, fill));
        return track;
    }

    /// <summary>Moves every camera sound bar to the level of the microphone behind it.</summary>
    void UpdateMeters()
    {
        foreach (var (camera, fill) in _meters)
        {
            var microphone = _session.MicrophoneFor(camera);
            var level = microphone is null ? SoundLevelMeter.ReadMaster() : _session.LevelFor(microphone);
            // The bar's parent is the track; its height comes from the available space.
            var height = fill.Parent is FrameworkElement track && track.ActualHeight > 4
                ? Math.Max(0, (track.ActualHeight - 4) * Math.Max(0, level))
                : 0;
            // At twenty reads a second, setting the same height or a fresh brush every time would
            // invalidate layout and allocate for nothing. Move them only when they actually change.
            if (Math.Abs(fill.Height - height) > 0.5) fill.Height = height;
            var colour = level > 0.92 ? "#FF6B6B" : level > 0.78 ? "#E8B23A" : "#38D7A3";
            if (fill.Tag as string != colour)
            {
                fill.Tag = colour;
                fill.Background = Solid(colour);
            }
        }
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
        SyncReplayFeed();
        var frames = _replayFeed;
        // The replay tile follows the decoder the preview is looking at. While a second replay is
        // waiting, that is the new replay, so the operator can verify it before Start replay.
        var replay = _session.Current;
        if (_session.IsReplaying && replay is { IsVideo: true } && frames is not null && frames.Contains(replay.Path))
        {
            var phase = _session.HasReplayPreview ? "PREVIEW · NEXT"
                : _session.ReplayOnAir ? "ON AIR" : "PREVIEW";
            _replayTitle.Text = $"REPLAY · {_session.ReplayCameraEntry?.Label ?? "camera"} · {phase}"
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

    void SyncReplayFeed()
    {
        // The tile is a monitor for the operator, so it follows the prepared decoder when one is
        // staged. With nothing staged this is the on-air replay decoder.
        var frames = _session.Playback.ReplayFramesFor(preview: true);
        if (ReferenceEquals(_replayFeed, frames)) return;
        if (_replayFeed is not null) _replayFeed.FrameReady -= OnReplayFrame;
        _replayFeed = frames;
        if (_replayFeed is not null) _replayFeed.FrameReady += OnReplayFrame;
        _replayPicture.Source = null;
    }

    void OnReplayFrame(BitmapSource frame)
    {
        // A prepared replay is paused, so its first frame may be the only one that arrives. Show
        // it even if the tile had not switched to the replay yet; the old guard dropped it and the
        // operator had to scrub before the program preview filled in.
        if (_replayFeed is null) return;
        _replayPicture.Source = frame;
        _replayPicture.Visibility = Visibility.Visible;
        _replayHint.Visibility = Visibility.Collapsed;
        _replayMirror.Visibility = Visibility.Collapsed;
        StopMirror();
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
        _meterTick.Stop();
        _program.Changed -= ProgramChanged;
        foreach (var (feed, handler) in _cameraHandlers) feed.FrameReady -= handler;
        _cameraHandlers.Clear();
        StopMirror();
        foreach (var view in _canvasViews) view.Dispose();
        _canvasViews.Clear();
        if (_replayFeed is not null) _replayFeed.FrameReady -= OnReplayFrame;
    }
}
