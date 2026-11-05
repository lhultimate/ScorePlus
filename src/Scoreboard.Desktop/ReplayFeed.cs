using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;

namespace Scoreboard.Desktop;

/// <summary>
/// Plays a recorded camera clip as decoded frames instead of handing the file to a media player.
/// The clips are MJPG AVI written by <see cref="CameraFeed"/>; a WPF MediaElement renders those as
/// one still frame or nothing at all on a machine without an MJPEG decoder, which is exactly what a
/// frozen replay looked like. OpenCV is the decoder that wrote the file, so it always reads it back,
/// and every surface shares the one decoded picture — the same way live camera frames are shared.
/// </summary>
public sealed class ReplayFeed : IDisposable
{
    const double FallbackFps = 15;
    readonly Dispatcher _ui;
    readonly object _gate = new();
    Thread? _thread;
    int _generation;
    bool _run;
    bool _playing;
    bool _restart;
    double _seekTo;
    string? _path;
    double _position;
    double _duration;
    bool _failed;
    bool _framePending;

    public ReplayFeed(Dispatcher ui) => _ui = ui;

    /// <summary>Raised on the UI thread as the picture advances, in seconds into the clip.</summary>
    public event Action<double>? PositionChanged;

    /// <summary>Raised on the UI thread with each decoded frame, already frozen.</summary>
    public event Action<BitmapSource>? FrameReady;

    /// <summary>Raised on the UI thread when the clip reaches its last frame while playing.</summary>
    public event Action? Ended;

    /// <summary>The clip being decoded, or null when no replay is open.</summary>
    public string? Path { get; private set; }

    public bool IsPlaying => _playing;

    public double Position => Volatile.Read(ref _position);

    public double Duration => _duration;

    public bool HasFailed => _failed;

    /// <summary>The frame last decoded, so a surface that attaches late still has a picture.</summary>
    public BitmapSource? Frame { get; private set; }

    /// <summary>True when this is the clip currently being decoded, so a surface can claim it.</summary>
    public bool Contains(string? path) => path is not null && string.Equals(Path, path, StringComparison.OrdinalIgnoreCase);

    /// <summary>Opens a clip at a point inside it. The same clip keeps its decoder and just seeks.</summary>
    public void Open(string path, double seconds, bool playing)
    {
        lock (_gate)
        {
            Path = path;
            _seekTo = Math.Max(0, seconds);
            _position = _seekTo;
            _restart = true;
            _playing = playing;
            _failed = false;
            _run = true;
        }
        StartThread();
    }

    public void Play()
    {
        lock (_gate)
        {
            _playing = true;
            _run = true;
        }
        StartThread();
    }

    public void Pause()
    {
        lock (_gate) _playing = false;
    }

    /// <summary>Moves the playhead. A paused clip draws the frame at the new point.</summary>
    public void Seek(double seconds)
    {
        lock (_gate)
        {
            _seekTo = Math.Max(0, seconds);
            _position = _seekTo;
            _restart = true;
        }
    }

    /// <summary>Stops the decoder and forgets the clip, so the next replay opens cleanly.</summary>
    public void Close()
    {
        Thread? thread;
        lock (_gate)
        {
            _run = false;
            _playing = false;
            _restart = false;
            Path = null;
            _generation++;
            thread = _thread;
        }
        _thread = null;
        if (thread is not null && thread != Thread.CurrentThread)
            try { thread.Join(250); } catch { }
        Frame = null;
        Volatile.Write(ref _position, 0);
        _duration = 0;
    }

    void StartThread()
    {
        Thread? existing;
        lock (_gate) existing = _thread;
        if (existing is { IsAlive: true }) return;
        int generation;
        lock (_gate) generation = ++_generation;
        var thread = new Thread(() => Loop(generation)) { IsBackground = true, Name = "Replay clip" };
        _thread = thread;
        thread.Start();
    }

    /// <summary>
    /// Decodes the clip on its own thread, one frame at a time, at the rate it was recorded. Frames
    /// are handed to the UI already frozen, so a 1080p replay never fights the dispatcher.
    /// </summary>
    void Loop(int generation)
    {
        VideoCapture? capture = null;
        string? opened = null;
        using var frame = new Mat();
        var fps = FallbackFps;
        long index = 0;
        var clock = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                string? path;
                bool playing;
                bool restart;
                double seekTo;
                lock (_gate)
                {
                    if (!_run || generation != _generation) break;
                    path = Path;
                    playing = _playing;
                    restart = _restart;
                    seekTo = _seekTo;
                    _restart = false;
                }
                if (path is null) break;

                if (opened is null || !string.Equals(opened, path, StringComparison.Ordinal))
                {
                    try { capture?.Release(); capture?.Dispose(); } catch { }
                    capture = new VideoCapture(path);
                    if (!capture.IsOpened())
                    {
                        Fail();
                        break;
                    }
                    opened = path;
                    fps = SafeFps(capture);
                    _duration = SafeDuration(capture, fps);
                    restart = true;
                }

                if (restart)
                {
                    index = (long)Math.Round(Math.Max(0, seekTo) * fps);
                    try { capture.Set(VideoCaptureProperties.PosFrames, index); } catch { }
                    // Draw the frame at the new point even while paused: a scrub has to show.
                    if (capture.Read(frame) && !frame.Empty()) Publish(frame);
                    Report(index / fps);
                    index++;
                    clock.Restart();
                    continue;
                }

                if (!playing)
                {
                    Thread.Sleep(15);
                    clock.Restart();
                    continue;
                }

                var started = clock.Elapsed.TotalMilliseconds;
                if (!capture.Read(frame) || frame.Empty())
                {
                    // End of the clip: hold the last frame and let the session decide whether the
                    // next recorded clip runs. The thread parks here instead of exiting, so the
                    // next clip can take over without waiting for a thread to die.
                    var ended = false;
                    lock (_gate)
                    {
                        if (_playing && generation == _generation) { _playing = false; ended = true; }
                    }
                    if (ended) _ui.BeginInvoke(() => { try { Ended?.Invoke(); } catch { } });
                    continue;
                }
                Publish(frame);
                Report(index / fps);
                index++;
                var spent = clock.Elapsed.TotalMilliseconds - started;
                var wait = (1000.0 / fps) - spent;
                if (wait > 1) Thread.Sleep((int)Math.Round(wait));
            }
        }
        catch { Fail(); }
        finally
        {
            try { capture?.Release(); capture?.Dispose(); } catch { }
            lock (_gate) _run = false;
        }
    }

    void Publish(Mat image)
    {
        // One frame in flight at a time: a replay drops a picture rather than queueing the UI.
        if (_framePending) return;
        BitmapSource? bitmap;
        try
        {
            var stride = (int)image.Step();
            var picture = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgr24, null,
                image.Data, stride * image.Height, stride);
            picture.Freeze();
            bitmap = picture;
        }
        catch { return; }
        var ready = bitmap;
        _framePending = true;
        _ui.BeginInvoke(() =>
        {
            _framePending = false;
            Frame = ready;
            try { FrameReady?.Invoke(ready); } catch { }
        });
    }

    void Report(double seconds)
    {
        Volatile.Write(ref _position, Math.Max(0, seconds));
        var at = _position;
        _ui.BeginInvoke(() => { try { PositionChanged?.Invoke(at); } catch { } });
    }

    void Fail()
    {
        _failed = true;
        lock (_gate) _playing = false;
    }

    static double SafeFps(VideoCapture capture)
    {
        try
        {
            var fps = capture.Fps;
            if (double.IsFinite(fps) && fps > 1 && fps < 121) return fps;
        }
        catch { }
        return FallbackFps;
    }

    static double SafeDuration(VideoCapture capture, double fps)
    {
        try
        {
            var frames = capture.FrameCount;
            if (frames > 0 && frames < int.MaxValue) return frames / fps;
        }
        catch { }
        return 0;
    }

    public void Dispose() => Close();
}
