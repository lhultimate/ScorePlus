using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>One camera as the Replay Booth presents it to the operator and the browser.</summary>
public sealed record ReplayBoothCamera(int Index, string Label);

/// <summary>What the game clocks read at a point in the camera recording.</summary>
public sealed record ReplayBoothClock(
    DateTimeOffset At,
    string GameClock,
    int PlayClockSeconds,
    int StoppageSeconds,
    string Period)
{
    public string PlayClock => $":{Math.Clamp(PlayClockSeconds, 0, 99):00}";
    public string Stoppage => GameEngine.FormatClock(StoppageSeconds);
}

/// <summary>
/// A rolling history of the game clocks. Camera clips are stamped in UTC, so a replay can ask what
/// the game clock, play clock and stoppage clock read at the exact frame the referee is reviewing.
/// The sample is taken by the main window whenever the game changes, which covers every clock tick
/// and every operator correction; it survives opening the booth late in a period.
/// </summary>
public static class ReplayBoothHistory
{
    // Clocks must reach back as far as the longest replay the operator can configure.
    const double KeepSeconds = 900;
    static readonly object Gate = new();
    static readonly List<ReplayBoothClock> Samples = [];

    public static void Sample(GameState state)
    {
        var at = DateTimeOffset.UtcNow;
        var sample = new ReplayBoothClock(
            at,
            state.GameClockText,
            state.PlayClockSeconds,
            state.StoppageSeconds,
            PeriodDisplay.Format(state));
        lock (Gate)
        {
            // A clock tick is once a second. Collapse duplicate frames so a busy score change cannot
            // fill the whole window with samples from the same instant.
            if (Samples.Count > 0)
            {
                var last = Samples[^1];
                if ((at - last.At).TotalMilliseconds < 350
                    && last.GameClock == sample.GameClock
                    && last.PlayClockSeconds == sample.PlayClockSeconds
                    && last.StoppageSeconds == sample.StoppageSeconds
                    && last.Period == sample.Period)
                    return;
            }
            Samples.Add(sample);
            var cutoff = at.AddSeconds(-KeepSeconds);
            var remove = 0;
            while (remove < Samples.Count - 1 && Samples[remove].At < cutoff) remove++;
            if (remove > 0) Samples.RemoveRange(0, remove);
        }
    }

    /// <summary>The last reading at or before the requested recording time.</summary>
    public static ReplayBoothClock? At(DateTimeOffset at)
    {
        lock (Gate)
        {
            ReplayBoothClock? found = null;
            foreach (var sample in Samples)
            {
                if (sample.At > at) break;
                found = sample;
            }
            return found ?? Samples.FirstOrDefault();
        }
    }

    public static void Reset()
    {
        lock (Gate) Samples.Clear();
    }
}

/// <summary>
/// The camera wall and configurable-length replay engine shared by the desktop Replay Booth and the browser
/// pages. It owns one decoder, keeps every library camera recording while active, and maps a
/// playhead back to the recorded game clocks. The main window owns one instance for the life of a
/// show, so the browser can still replay when the desktop booth is closed.
/// </summary>
public sealed class ReplayBoothController : IDisposable
{
    readonly MediaSession _session;
    readonly GameEngine _engine;
    readonly UserPreferences _preferences;
    readonly Dispatcher _ui;
    readonly ReplayFeed _feed;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly List<ReplayBoothCamera> _cameras = [];
    readonly List<CameraSegment> _segments = [];
    readonly Dictionary<int, (BitmapSource Frame, byte[] Data)> _liveJpegCache = [];
    int _selected;
    int _segmentIndex = -1;
    CameraSegment? _currentSegment;
    bool _active;
    bool _prepared;
    bool _playing;
    double _back;
    double _depth;
    DateTime _lastSnapshot = DateTime.MinValue;

    public ReplayBoothController(MediaSession session, GameEngine engine, UserPreferences preferences)
    {
        _session = session;
        _engine = engine;
        _preferences = preferences;
        _ui = Dispatcher.CurrentDispatcher;
        _feed = new ReplayFeed(_ui);
        _feed.PositionChanged += TrackPosition;
        _feed.Ended += AdvanceReplay;
        _timer.Tick += (_, _) => Refresh();
    }

    public IReadOnlyList<ReplayBoothCamera> Cameras => _cameras;
    public int SelectedIndex => _selected;
    public ReplayBoothCamera? SelectedCamera => _selected >= 0 && _selected < _cameras.Count ? _cameras[_selected] : null;
    public bool IsPlaying => _playing;
    public bool IsPrepared => _prepared;
    public double Depth => _depth;
    public double Back => _back;
    public bool HasReplay => _prepared && _segments.Count > 0;
    public bool ShowStoppage => _engine.State.SportId == "soccer" || (ClockAtPlayhead?.StoppageSeconds ?? 0) > 0;
    public bool StoppageRunning => _engine.State.StoppageRunning;
    public string StoppageText => GameEngine.FormatClock(_engine.State.StoppageSeconds);
    public string SelectedLabel => SelectedCamera?.Label ?? "No camera";

    /// <summary>The live stoppage clock. It is a game clock, not a replay clock: starting it accrues
    /// added time in the same engine the console and the scorebug read.</summary>
    public void ToggleStoppage() => _engine.ToggleStoppageClock();
    public void ResetStoppage() => _engine.ResetStoppageClock();
    public void AdjustStoppage(int seconds) => _engine.AdjustStoppageClock(seconds);

    /// <summary>The frame shown in the big view: a decoded replay frame, or the live camera.</summary>
    public BitmapSource? DisplayFrame => _prepared && _feed.Frame is { } picture ? picture : LiveFrame(SelectedCamera?.Index ?? -1);

    /// <summary>The clock reading at the playhead (or the live clock when nothing is open).</summary>
    public ReplayBoothClock? ClockAtPlayhead
    {
        get
        {
            if (_segments.Count == 0) return ReplayBoothHistory.At(DateTimeOffset.UtcNow);
            var at = _segments[^1].EndsAt.AddSeconds(-Math.Clamp(_back, 0, _depth));
            return ReplayBoothHistory.At(at) ?? ReplayBoothHistory.At(DateTimeOffset.UtcNow);
        }
    }

    public string LabelsPreferenceName => "replay";
    public bool ShowLabels
    {
        get => _preferences.ReplayBoothShowLabels;
        set
        {
            if (_preferences.ReplayBoothShowLabels == value) return;
            _preferences.ReplayBoothShowLabels = value;
            PreferencesStore.Save(_preferences);
        }
    }

    /// <summary>Starts recording every library camera and opens the first one. Safe to call twice.</summary>
    public void Activate()
    {
        RefreshCameras();
        if (_active) return;
        _active = true;
        foreach (var camera in _cameras)
        {
            try { CameraFeed.Get(camera.Index).Start(); } catch { }
        }
        Select(_selected);
        _timer.Start();
    }

    public void RefreshCameras()
    {
        var previousIndex = SelectedCamera?.Index ?? -1;
        var next = new List<ReplayBoothCamera>();
        var seen = new HashSet<int>();
        foreach (var entry in _session.Library.Where(entry => entry.IsCamera).OrderBy(entry => entry.CameraIndex))
        {
            if (!seen.Add(entry.CameraIndex)) continue;
            next.Add(new ReplayBoothCamera(entry.CameraIndex, entry.Label));
        }
        var changed = next.Count != _cameras.Count
            || next.Where((camera, index) => camera.Index != _cameras[index].Index || camera.Label != _cameras[index].Label).Any();
        if (!changed) return;
        _cameras.Clear();
        _cameras.AddRange(next);
        _selected = previousIndex >= 0 ? _cameras.FindIndex(camera => camera.Index == previousIndex) : -1;
        if (_selected < 0) _selected = _cameras.Count > 0 ? 0 : -1;
        if (_active)
        {
            foreach (var camera in _cameras)
                try { CameraFeed.Get(camera.Index).Start(); } catch { }
        }
    }

    public void Select(int index)
    {
        if (_cameras.Count == 0) { _selected = -1; return; }
        _selected = Math.Clamp(index, 0, _cameras.Count - 1);
        SelectCurrent();
    }

    public void SelectDelta(int delta) => Select(_selected + delta);

    public void SelectCamera(int cameraIndex)
    {
        var index = _cameras.FindIndex(camera => camera.Index == cameraIndex);
        if (index >= 0) Select(index);
    }

    void SelectCurrent()
    {
        _feed.Close();
        _segments.Clear();
        _segmentIndex = -1;
        _currentSegment = null;
        _prepared = false;
        _playing = false;
        _back = 0;
        _depth = 0;
        _lastSnapshot = DateTime.MinValue;
        if (SelectedCamera is { } camera)
        {
            try { CameraFeed.Get(camera.Index).Start(); } catch { }
        }
        // Preparing a replay closes the selected camera's writer, which can block the UI while a
        // clip is finalised. Selecting a camera on the wall should be instant, so leave the snapshot
        // to Refresh a couple of seconds later or to the first explicit replay action.
        _lastSnapshot = DateTime.UtcNow;
    }

    /// <summary>Refreshes the wall and keeps the selected camera recording.</summary>
    public void Refresh()
    {
        if (!_active) return;
        RefreshCameras();
        if (SelectedCamera is not { } camera) return;
        try { CameraFeed.Get(camera.Index).Start(); } catch { }
        // The selected camera may have just started. Once it has a finished clip, open it so the
        // scrubber is useful without the operator pressing anything.
        if (!_prepared && (DateTime.UtcNow - _lastSnapshot).TotalSeconds >= 2)
            SnapshotAndOpen(reset: true);
        if (_segments.Count > 0)
        {
            var newest = _segments[^1].EndsAt;
            _depth = Math.Min(CameraFeed.ReplayWindowSeconds, (newest - _segments[0].StartedAt).TotalSeconds);
            _back = Math.Clamp(_back, 0, Math.Max(0, _depth));
        }
    }

    /// <summary>Moves the playhead back from the live edge. Zero is the newest recorded frame.</summary>
    public bool Seek(double secondsBack)
    {
        if (!EnsureBuffer()) return false;
        _back = Math.Clamp(double.IsFinite(secondsBack) ? secondsBack : 0, 0, Math.Max(0, _depth));
        OpenAt(_back);
        return true;
    }

    /// <summary>Moves forward (negative delta) or backward (positive delta) in the recording.</summary>
    public bool Step(double delta) => Seek(_back + delta);

    public bool Play()
    {
        if (!EnsureBuffer()) return false;
        // Pressing Play at the live edge is the moment to pick up the segment recorded since the
        // booth opened; otherwise the replay would start several seconds behind the actual edge.
        if (_back <= 0.15) RefreshBuffer();
        if (_back <= 0.15 && _depth > 1) _back = Math.Min(5, _depth);
        OpenAt(_back, forcePlaying: true);
        _playing = true;
        _feed.Play();
        return true;
    }

    public void Pause()
    {
        _playing = false;
        _feed.Pause();
    }

    public bool TogglePlay()
    {
        if (_playing) { Pause(); return false; }
        return Play();
    }

    /// <summary>Forces a fresh snapshot so a replay opened long after the wall started reaches the live edge.</summary>
    public bool RefreshBuffer()
    {
        if (SelectedCamera is not { } camera) return false;
        try { CameraFeed.Get(camera.Index).Start(); } catch { }
        var segments = CameraFeed.Get(camera.Index).Snapshot();
        if (segments.Count == 0) return false;
        _segments.Clear();
        _segments.AddRange(segments);
        _depth = Math.Min(CameraFeed.ReplayWindowSeconds, (_segments[^1].EndsAt - _segments[0].StartedAt).TotalSeconds);
        _prepared = true;
        _lastSnapshot = DateTime.UtcNow;
        return true;
    }

    bool EnsureBuffer()
    {
        if (SelectedCamera is null) return false;
        if (_prepared && _segments.Count > 0) return true;
        return SnapshotAndOpen(reset: true);
    }

    bool SnapshotAndOpen(bool reset)
    {
        if (SelectedCamera is not { } camera) return false;
        try { CameraFeed.Get(camera.Index).Start(); } catch { }
        var segments = CameraFeed.Get(camera.Index).Snapshot();
        _lastSnapshot = DateTime.UtcNow;
        if (segments.Count == 0)
        {
            _segments.Clear();
            _prepared = false;
            _depth = 0;
            _back = 0;
            return false;
        }
        _segments.Clear();
        _segments.AddRange(segments);
        _depth = Math.Min(CameraFeed.ReplayWindowSeconds, (_segments[^1].EndsAt - _segments[0].StartedAt).TotalSeconds);
        if (reset)
        {
            _back = 0;
            _playing = false;
        }
        _prepared = true;
        OpenAt(_back, forcePlaying: false);
        return true;
    }

    void OpenAt(double secondsBack, bool? forcePlaying = null)
    {
        if (_segments.Count == 0) return;
        var back = Math.Clamp(secondsBack, 0, Math.Max(0, _depth));
        var target = _segments[^1].EndsAt.AddSeconds(-back);
        var index = _segments.FindIndex(segment => target < segment.EndsAt);
        if (index < 0) index = _segments.Count - 1;
        var segment = _segments[index];
        var offset = Math.Clamp((target - segment.StartedAt).TotalSeconds, 0, Math.Max(0, segment.Seconds - 0.05));
        _segmentIndex = index;
        _currentSegment = segment;
        _back = back;
        _feed.Open(segment.Path, offset, forcePlaying ?? _playing);
    }

    void TrackPosition(double seconds)
    {
        if (_segments.Count == 0 || _currentSegment is null) return;
        // An old decoder can still report a position while the new camera is opening.
        if (!string.Equals(_feed.Path, _currentSegment.Path, StringComparison.OrdinalIgnoreCase)) return;
        var at = _currentSegment.StartedAt.AddSeconds(Math.Clamp(seconds, 0, _currentSegment.Seconds));
        _back = Math.Clamp((_segments[^1].EndsAt - at).TotalSeconds, 0, Math.Max(0, _depth));
    }

    void AdvanceReplay()
    {
        if (_segmentIndex >= 0 && _segmentIndex + 1 < _segments.Count)
        {
            var next = _segmentIndex + 1;
            _segmentIndex = next;
            _currentSegment = _segments[next];
            _back = Math.Clamp((_segments[^1].EndsAt - _currentSegment.StartedAt).TotalSeconds, 0, _depth);
            _feed.Open(_currentSegment.Path, 0, _playing);
            return;
        }
        // Hold the last recorded frame at the live edge. The operator can scrub back again.
        _playing = false;
        _back = 0;
        _feed.Pause();
    }

    public BitmapSource? LiveFrame(int cameraIndex)
    {
        if (cameraIndex < 0) return null;
        try { return CameraFeed.Get(cameraIndex).LastFrame; } catch { return null; }
    }

    /// <summary>A JPEG snapshot for the browser wall, encoded from the same frozen frame the desk sees.</summary>
    public byte[]? FrameJpeg(int cameraIndex, bool replay)
    {
        BitmapSource? frame;
        if (replay && cameraIndex == SelectedCamera?.Index && _prepared)
            frame = _feed.Frame ?? LiveFrame(cameraIndex);
        else
            frame = LiveFrame(cameraIndex);
        if (frame is null) return null;
        // Several wall tiles can ask for the same camera between two captured frames. Reuse the
        // encoded bytes instead of recompressing the same picture for every tile and every client.
        if (!replay && _liveJpegCache.TryGetValue(cameraIndex, out var cached)
            && ReferenceEquals(cached.Frame, frame))
            return cached.Data;
        var data = EncodeJpeg(frame);
        if (data is not null && !replay) _liveJpegCache[cameraIndex] = (frame, data);
        return data;
    }

    static byte[]? EncodeJpeg(BitmapSource frame)
    {
        try
        {
            // The wall is a monitor, not the program output. Encoding a full 4K camera for a small
            // tile was the main cost behind the browser wall's lag; 960px keeps the picture sharp
            // while cutting the JPEG work by an order of magnitude on typical capture cards.
            BitmapSource source = frame;
            if (frame.PixelWidth > 960)
            {
                var scale = 960d / frame.PixelWidth;
                var resized = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                resized.Freeze();
                source = resized;
            }
            using var stream = new MemoryStream();
            var encoder = new JpegBitmapEncoder { QualityLevel = 72 };
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch { return null; }
    }

    public string StateJson(string mode)
    {
        Activate();
        RefreshCameras();
        var selected = SelectedCamera;
        var clock = ClockAtPlayhead;
        var showLabels = string.Equals(mode, "multiview", StringComparison.OrdinalIgnoreCase)
            ? _preferences.MultiviewShowLabels
            : _preferences.ReplayBoothShowLabels;
        var state = new
        {
            ok = true,
            mode,
            cameras = _cameras.Select((camera, index) => new
            {
                index = camera.Index,
                label = camera.Label,
                selected = index == _selected,
                frame = CameraFeed.Get(camera.Index).FrameVersion
            }),
            selected = selected?.Index ?? -1,
            selectedLabel = selected?.Label ?? "",
            depth = Math.Round(_depth, 2),
            back = Math.Round(_back, 2),
            playing = _playing,
            hasReplay = HasReplay,
            windowSeconds = Math.Round(Math.Max(1, CameraFeed.ReplayWindowSeconds)),
            showLabels,
            stoppageRunning = StoppageRunning,
            bindings = KeybindStore.Load().Bindings
                .Where(pair => pair.Key.StartsWith("replay.", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.StartsWith("camera.", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            gameClock = clock?.GameClock ?? GameEngine.FormatClock(_engine.State.ClockSeconds),
            playClock = clock?.PlayClock ?? $":{Math.Clamp(_engine.State.PlayClockSeconds, 0, 99):00}",
            stoppage = clock?.Stoppage ?? GameEngine.FormatClock(_engine.State.StoppageSeconds),
            hasStoppage = _engine.State.SportId == "soccer" || (clock?.StoppageSeconds ?? 0) > 0,
            period = clock?.Period ?? PeriodDisplay.Format(_engine.State)
        };
        return JsonSerializer.Serialize(state);
    }

    /// <summary>Handles the browser's small command surface. Every command is a UI-thread call.</summary>
    public bool HandleAction(string mode, IReadOnlyDictionary<string, string> form)
    {
        Activate();
        var action = form.TryGetValue("a", out var raw) ? raw.Trim().ToLowerInvariant() : "";
        int Number(string key, int fallback) => int.TryParse(form.TryGetValue(key, out var value) ? value : "", out var parsed) ? parsed : fallback;
        double Decimal(string key, double fallback) => double.TryParse(form.TryGetValue(key, out var value) ? value : "",
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed)
            ? parsed : fallback;
        switch (action)
        {
            case "select":
                if (form.ContainsKey("index")) SelectCamera(Number("index", SelectedCamera?.Index ?? -1));
                else SelectDelta(Number("delta", 0));
                return true;
            case "seek":
                return Seek(Decimal("back", _back));
            case "step":
                return Step(Decimal("delta", 0));
            case "play":
                return Play();
            case "pause":
                Pause();
                return true;
            case "toggle":
                TogglePlay();
                return true;
            case "stoppage":
                var stoppage = form.TryGetValue("value", out var stoppageValue) ? stoppageValue : "toggle";
                if (string.Equals(stoppage, "reset", StringComparison.OrdinalIgnoreCase)) ResetStoppage();
                else if (string.Equals(stoppage, "plus30", StringComparison.OrdinalIgnoreCase)) AdjustStoppage(30);
                else if (string.Equals(stoppage, "minus30", StringComparison.OrdinalIgnoreCase)) AdjustStoppage(-30);
                else ToggleStoppage();
                return true;
            case "labels":
                var value = form.TryGetValue("value", out var labelValue) && string.Equals(labelValue, "1", StringComparison.Ordinal);
                if (string.Equals(mode, "multiview", StringComparison.OrdinalIgnoreCase))
                {
                    _preferences.MultiviewShowLabels = value;
                    PreferencesStore.Save(_preferences);
                }
                else ShowLabels = value;
                return true;
            default:
                return false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _feed.PositionChanged -= TrackPosition;
        _feed.Ended -= AdvanceReplay;
        _feed.Dispose();
    }
}
