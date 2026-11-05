using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Scoreboard.Desktop;

/// <summary>
/// A broadcast-style multiview: a replay preview that is always on screen, plus a live tile for
/// every camera in the library. The replay tile shows the same decoded frames the program is
/// showing while a replay is open, and mirrors the live camera the rest of the time, so there is
/// never a black hole where the preview should be.
/// </summary>
public sealed class MultiviewWindow : Window
{
    readonly MediaSession _session;
    readonly WrapPanel _panel = new() { Margin = new Thickness(10) };
    readonly TextBlock _status = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
        FontSize = 11,
        Margin = new Thickness(14, 6, 14, 2),
        Text = "The replay preview follows Replay, the scrubber and Start replay. Every camera below is live."
    };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(400) };
    // Keyed by camera device index. The feed itself belongs to the session: this window only
    // listens to its frames, so closing the multiview never stops what is on air.
    readonly Dictionary<int, (Image Picture, CameraFeed Feed, Action<BitmapSource> Handler, TextBlock Title, MediaEntry Entry)> _live = [];
    // The replay tile is built once and never replaced: rebuilding it dropped the picture, which is
    // why the preview went black or froze instead of following the replay.
    readonly TextBlock _replayTitle = new()
    {
        Text = "REPLAY",
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
        Margin = new Thickness(6, 4, 6, 4)
    };
    readonly Image _replayMirror = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
    readonly Image _replayPicture = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
    readonly TextBlock _replayHint = new()
    {
        Text = "Press Replay in the media dock to review the camera.",
        Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12)
    };
    Border? _replayTile;
    string _signature = "";
    int _mirrorIndex = -1;
    CameraFeed? _mirrorFeed;
    Action<BitmapSource>? _mirrorHandler;

    public MultiviewWindow(MediaSession session, string theme)
    {
        _session = session;
        Title = "ScorePlus — Multiview";
        Width = 1280;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x18));
        Foreground = Brushes.White;

        var header = new TextBlock
        {
            Text = "MULTIVIEW",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(14, 12, 14, 0)
        };
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        // The session decodes the replay once; this tile and every scorebug video element draw that
        // same picture, so they can never disagree about what the replay is showing.
        if (session.Playback.ReplayFrames is { } frames) frames.FrameReady += OnReplayFrame;

        BuildReplayTile();
        _tick.Tick += (_, _) => Refresh();
        _tick.Start();
        Closed += (_, _) => Shutdown();
        Refresh();
        ThemeManager.Apply(this, theme);
    }

    void BuildReplayTile()
    {
        var stack = new Grid();
        stack.Children.Add(_replayMirror);
        stack.Children.Add(_replayPicture);
        stack.Children.Add(_replayHint);
        _replayTile = Tile(_replayTitle, stack);
    }

    /// <summary>Rebuilds the camera tiles only when the camera set changes, and keeps the replay in step.</summary>
    void Refresh()
    {
        if (_session is null) return;
        var cameras = _session.Library.Where(entry => entry.IsCamera).ToArray();
        var signature = string.Join("|", cameras.Select(camera => $"{camera.CameraIndex}:{camera.Label}"));
        if (!string.Equals(signature, _signature, StringComparison.Ordinal))
        {
            _signature = signature;
            Build(cameras);
        }
        // On air markers change without rebuilding a single tile: a rebuild restarted every picture.
        foreach (var (_, tile) in _live)
            tile.Title.Text = $"{tile.Entry.Label} · CAM {tile.Entry.CameraIndex + 1}"
                + (_session.IsCameraOnAir(tile.Entry) ? " · ON AIR" : "");
        SyncReplay();
    }

    void Build(MediaEntry[] cameras)
    {
        foreach (var (_, feed, handler, _, _) in _live.Values) feed.FrameReady -= handler;
        _live.Clear();
        _panel.Children.Clear();
        // The replay preview goes first, so it is always in the same place and never scrolls away.
        if (_replayTile is not null) _panel.Children.Add(_replayTile);

        foreach (var camera in cameras)
        {
            var picture = new Image { Stretch = Stretch.Uniform };
            var feed = CameraFeed.Get(camera.CameraIndex);
            // Frames arrive at capture rate; the tile only repaints ten times a second, so the
            // dispatcher is not asked to upload a new bitmap for every single frame.
            var lastShown = DateTime.MinValue;
            Action<BitmapSource> handler = frame =>
            {
                var now = DateTime.UtcNow;
                if ((now - lastShown).TotalMilliseconds < 100) return;
                lastShown = now;
                Dispatcher.BeginInvoke(() => picture.Source = frame);
            };
            feed.FrameReady += handler;
            feed.Start();
            var title = new TextBlock
            {
                Text = $"{camera.Label} · CAM {camera.CameraIndex + 1}",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
                Margin = new Thickness(6, 4, 6, 4)
            };
            _live[camera.CameraIndex] = (picture, feed, handler, title, camera);
            _panel.Children.Add(Tile(title, picture));
        }

        if (cameras.Length == 0)
            _panel.Children.Add(Tile("NO CAMERAS YET", new TextBlock
            {
                Text = "Add a camera in the media dock, then cue it to see it here.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10)
            }));

        StopMirror();
    }

    /// <summary>Points the permanent replay tile at the replay while one is open, and at the live camera otherwise.</summary>
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

    /// <summary>Mirrors the live camera into the replay tile when no replay is open.</summary>
    void MirrorCamera()
    {
        var entry = _session.CameraEntry;
        if (entry is null)
        {
            _replayTitle.Text = "REPLAY · nothing cued";
            _replayHint.Text = "Press Replay in the media dock to review the camera.";
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
                Dispatcher.BeginInvoke(() =>
                {
                    if (_replayMirror.Visibility != Visibility.Visible) return;
                    _replayMirror.Source = frame;
                });
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

    Border Tile(string title, UIElement content) => Tile(new TextBlock
    {
        Text = title,
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)),
        Margin = new Thickness(6, 4, 6, 4)
    }, content);

    Border Tile(TextBlock head, UIElement content)
    {
        var body = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x09, 0x10, 0x18)),
            Child = content,
            Height = 230
        };
        var stack = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        stack.Children.Add(head);
        stack.Children.Add(body);
        return new Border
        {
            Width = 420,
            Margin = new Thickness(6),
            Background = new SolidColorBrush(Color.FromRgb(0x17, 0x1C, 0x25)),
            CornerRadius = new CornerRadius(8),
            Child = stack
        };
    }

    void Shutdown()
    {
        _tick.Stop();
        foreach (var (_, feed, handler, _, _) in _live.Values) feed.FrameReady -= handler;
        _live.Clear();
        StopMirror();
        if (_session.Playback.ReplayFrames is { } frames) frames.FrameReady -= OnReplayFrame;
    }
}
