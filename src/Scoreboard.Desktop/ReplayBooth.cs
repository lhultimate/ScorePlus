using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>One camera as the Replay Booth presents it to the operator and the browser.</summary>
public sealed record ReplayBoothCamera(int Index, string Label, string Path = "");

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
/// The camera wall and configurable-length replay engine shared by the desktop Replay Booth and the
/// browser pages. It keeps every library camera recording while active and decodes every one of
/// them, so the whole wall can be scrubbed as one: a seek or a play moves all the cameras to the
/// same instant, and selecting a different camera only changes which one fills the big view. That
/// is what a referee needs — the same moment from several angles — and it means the scrubber is
/// honest: the frame on the wall really is the same instant as the frame in the big picture.
/// </summary>
public sealed class ReplayBoothController : IDisposable
{
    readonly MediaSession _session;
    readonly GameEngine _engine;
    readonly UserPreferences _preferences;
    readonly Dispatcher _ui;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly List<ReplayBoothCamera> _cameras = [];
    readonly List<ReplayBoothCamera> _allCameras = [];
    // One decoder per camera. They are opened together and seeked together, which is the whole
    // point of a shared playhead.
    readonly Dictionary<int, ReplayFeed> _feeds = [];
    readonly Dictionary<int, List<CameraSegment>> _segments = [];
    readonly Dictionary<int, (BitmapSource Frame, byte[] Data)> _liveJpegCache = [];
    int _selected;
    bool _active;
    bool _prepared;
    bool _playing;
    double _back;
    double _depth;
    DateTime _lastSnapshot = DateTime.MinValue;
    DateTime? _playStartedAt;
    double _playStartedBack;

    public ReplayBoothController(MediaSession session, GameEngine engine, UserPreferences preferences)
    {
        _session = session;
        _engine = engine;
        _preferences = preferences;
        _ui = Dispatcher.CurrentDispatcher;
        _timer.Tick += (_, _) => Refresh();
    }

    public IReadOnlyList<ReplayBoothCamera> Cameras => _cameras;
    /// <summary>Every camera, including the ones the booth's layout hides, so the editor can list them.</summary>
    public IReadOnlyList<ReplayBoothCamera> AllCameras => _allCameras;
    public int SelectedIndex => _selected;
    public ReplayBoothCamera? SelectedCamera => _selected >= 0 && _selected < _cameras.Count ? _cameras[_selected] : null;
    public bool IsPlaying => _playing;
    public bool IsPrepared => _prepared;
    public double Depth => _depth;
    public double Back => _back;
    public bool HasReplay => _prepared && _segments.Values.Any(list => list.Count > 0);
    public bool HasSelection => _selected >= 0 && SelectedCamera is not null;
    public bool ShowStoppage => _engine.State.SportId == "soccer" || (ClockAtPlayhead?.StoppageSeconds ?? 0) > 0;
    public bool StoppageRunning => _engine.State.StoppageRunning;
    public string StoppageText => GameEngine.FormatClock(_engine.State.StoppageSeconds);
    public string SelectedLabel => SelectedCamera?.Label ?? "No camera";

    /// <summary>The booth wall's own column count, or zero for an automatic grid.</summary>
    public int Columns => Math.Clamp(_preferences.ReplayBoothColumns, 0, 6);

    /// <summary>Replay playback rate, 0.1x to 2.0x, shared with the media rail.</summary>
    public double Speed
    {
        get => Math.Clamp(_preferences.ReplaySpeed, 0.1, 2.0);
        set
        {
            var clamped = Math.Clamp(double.IsFinite(value) ? value : 1, 0.1, 2.0);
            if (Math.Abs(_preferences.ReplaySpeed - clamped) < 0.0001) return;
            _preferences.ReplaySpeed = clamped;
            PreferencesStore.Save(_preferences);
            foreach (var feed in _feeds.Values) feed.Speed = clamped;
        }
    }

    /// <summary>
    /// The booth's own wall layout: which cameras are on it, in what order, and how many columns.
    /// It is stored separately from the multiview wall, so editing one never rearranges the other.
    /// </summary>
    public void SetWallLayout(IEnumerable<string> order, IEnumerable<string> hidden, int columns)
    {
        _preferences.ReplayBoothOrder = order.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        _preferences.ReplayBoothHidden = hidden.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        _preferences.ReplayBoothColumns = Math.Clamp(columns, 0, 6);
        PreferencesStore.Save(_preferences);
        // The cached camera list has to be rebuilt even if the library did not change.
        _cameras.Clear();
        RefreshCameras();
    }

    public IReadOnlyList<string> WallOrder => _preferences.ReplayBoothOrder;
    public IReadOnlyList<string> WallHidden => _preferences.ReplayBoothHidden;

    /// <summary>The live stoppage clock. It is a game clock, not a replay clock: starting it accrues
    /// added time in the same engine the console and the scorebug read.</summary>
    public void ToggleStoppage() => _engine.ToggleStoppageClock();
    public void ResetStoppage() => _engine.ResetStoppageClock();
    public void AdjustStoppage(int seconds) => _engine.AdjustStoppageClock(seconds);

    /// <summary>The frame shown in the big view: the selected camera, at the shared playhead.</summary>
    public BitmapSource? DisplayFrame => SelectedCamera is { } camera ? FrameFor(camera.Index) : null;

    /// <summary>
    /// The frame one camera shows at the shared playhead. Every tile draws through this, so the
    /// wall and the big picture can never disagree about the moment being reviewed.
    /// </summary>
    public BitmapSource? FrameFor(int cameraIndex)
    {
        if (_prepared && _feeds.TryGetValue(cameraIndex, out var feed) && feed.Frame is { } picture)
            return picture;
        return LiveFrame(cameraIndex);
    }

    /// <summary>The clock reading at the playhead (or the live clock when nothing is open).</summary>
    public ReplayBoothClock? ClockAtPlayhead
    {
        get
        {
            var newest = NewestEdge();
            if (newest is not { } edge) return ReplayBoothHistory.At(DateTimeOffset.UtcNow);
            var at = edge.AddSeconds(-Math.Clamp(_back, 0, _depth));
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

    /// <summary>Starts recording and decoding every library camera. Safe to call twice.</summary>
    public void Activate()
    {
        RefreshCameras();
        if (_active) return;
        _active = true;
        foreach (var camera in _cameras)
        {
            try { CameraFeed.Get(camera.Index).Start(); } catch { }
        }
        if (_selected < 0 && _cameras.Count > 0) _selected = 0;
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
            next.Add(new ReplayBoothCamera(entry.CameraIndex, entry.Label, entry.Path));
        }
        _allCameras.Clear();
        _allCameras.AddRange(next);
        // The booth's own order comes first; any camera added since the layout was saved follows
        // in library order rather than disappearing from the wall.
        var order = _preferences.ReplayBoothOrder;
        if (order.Count > 0)
        {
            var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < order.Count; index++) rank[order[index]] = index;
            next = next.OrderBy(camera => rank.TryGetValue(camera.Path, out var position) ? position : int.MaxValue).ToList();
        }
        var hidden = new HashSet<string>(_preferences.ReplayBoothHidden, StringComparer.OrdinalIgnoreCase);
        if (hidden.Count > 0) next.RemoveAll(camera => hidden.Contains(camera.Path));
        var changed = next.Count != _cameras.Count
            || next.Where((camera, index) => camera.Index != _cameras[index].Index || camera.Label != _cameras[index].Label).Any();
        if (!changed) return;
        _cameras.Clear();
        _cameras.AddRange(next);
        _selected = previousIndex >= 0 ? _cameras.FindIndex(camera => camera.Index == previousIndex) : -1;
        if (_selected < 0) _selected = _cameras.Count > 0 ? 0 : -1;
        // A camera that left the library takes its decoder with it.
        foreach (var stale in _feeds.Keys.Where(index => _cameras.All(camera => camera.Index != index)).ToArray())
        {
            _feeds[stale].Dispose();
            _feeds.Remove(stale);
            _segments.Remove(stale);
        }
        if (_active)
        {
            foreach (var camera in _cameras)
                try { CameraFeed.Get(camera.Index).Start(); } catch { }
        }
    }

    /// <summary>Selecting a camera never moves the playhead: the wall is one moment, several angles.</summary>
    public void Select(int index)
    {
        if (_cameras.Count == 0) { _selected = -1; return; }
        _selected = Math.Clamp(index, 0, _cameras.Count - 1);
        if (SelectedCamera is { } camera)
            try { CameraFeed.Get(camera.Index).Start(); } catch { }
    }

    public void SelectDelta(int delta) => Select(_selected + delta);

    public void SelectCamera(int cameraIndex)
    {
        var index = _cameras.FindIndex(camera => camera.Index == cameraIndex);
        if (index >= 0) Select(index);
    }

    /// <summary>Escape on the wall: no camera is outlined, and nothing is paused by it.</summary>
    public void ClearSelection() => _selected = -1;

    /// <summary>Refreshes the wall and keeps every camera recording.</summary>
    public void Refresh()
    {
        if (!_active) return;
        RefreshCameras();
        foreach (var camera in _cameras)
            try { CameraFeed.Get(camera.Index).Start(); } catch { }
        // The first finished clip on any camera is enough to make the scrubber useful, so the booth
        // does not wait for every camera to roll a full segment.
        if (!_prepared && (DateTime.UtcNow - _lastSnapshot).TotalSeconds >= 2)
            SnapshotAndOpen(reset: true);
        TrackPlaying();
        UpdateDepth();
    }

    void UpdateDepth()
    {
        if (NewestEdge() is not { } newest) return;
        var oldest = OldestEdge() ?? newest;
        _depth = Math.Min(CameraFeed.ReplayWindowSeconds, Math.Max(0, (newest - oldest).TotalSeconds));
        _back = Math.Clamp(_back, 0, Math.Max(0, _depth));
    }

    /// <summary>Moves the shared playhead back from the live edge. Zero is the newest recorded frame.</summary>
    public bool Seek(double secondsBack)
    {
        if (!EnsureBuffer()) return false;
        _playing = false;
        _playStartedAt = null;
        _back = Math.Clamp(double.IsFinite(secondsBack) ? secondsBack : 0, 0, Math.Max(0, _depth));
        OpenAll(_back, playing: false);
        return true;
    }

    /// <summary>Moves forward (negative delta) or backward (positive delta) on every camera.</summary>
    public bool Step(double delta) => Seek(_back + delta);

    public bool Play()
    {
        if (!EnsureBuffer()) return false;
        // Pressing Play at the live edge is the moment to pick up what has been recorded since the
        // booth opened; otherwise the replay would start behind the actual edge.
        if (_back <= 0.15) RefreshBuffer();
        if (_back <= 0.15 && _depth > 1) _back = Math.Min(5, _depth);
        _playing = true;
        _playStartedAt = DateTime.UtcNow;
        _playStartedBack = _back;
        OpenAll(_back, playing: true);
        return true;
    }

    public void Pause()
    {
        _playing = false;
        _playStartedAt = null;
        foreach (var feed in _feeds.Values) feed.Pause();
    }

    public bool TogglePlay() => _playing ? Play_Stop() : Play();

    bool Play_Stop()
    {
        Pause();
        return false;
    }

    /// <summary>Moves the shared playhead while the cameras are playing.</summary>
    void TrackPlaying()
    {
        if (!_playing || _playStartedAt is not { } started) return;
        var elapsed = (DateTime.UtcNow - started).TotalSeconds;
        var back = _playStartedBack - Math.Max(0, elapsed);
        if (back <= 0)
        {
            // The playhead reached the live edge. Hold the last frame rather than wrapping.
            _back = 0;
            Pause();
            return;
        }
        _back = back;
    }

    /// <summary>Forces a fresh snapshot of every camera so a replay opened late reaches the live edge.</summary>
    public bool RefreshBuffer()
    {
        if (!EnsureBuffers()) return false;
        _prepared = true;
        UpdateDepth();
        OpenAll(_back, playing: _playing);
        return true;
    }

    bool EnsureBuffer() => _prepared && _segments.Values.Any(list => list.Count > 0) || SnapshotAndOpen(reset: true);

    /// <summary>Takes one clip snapshot per camera. It is the only expensive step, and it happens on
    /// opening a replay or an explicit refresh, never on a seek.</summary>
    bool EnsureBuffers()
    {
        var any = false;
        foreach (var camera in _cameras)
        {
            try { CameraFeed.Get(camera.Index).Start(); } catch { }
            var segments = CameraFeed.Get(camera.Index).Snapshot();
            _segments[camera.Index] = segments.ToList();
            if (segments.Count > 0) any = true;
        }
        _lastSnapshot = DateTime.UtcNow;
        return any;
    }

    bool SnapshotAndOpen(bool reset)
    {
        if (_cameras.Count == 0) return false;
        if (!EnsureBuffers())
        {
            _prepared = false;
            _depth = 0;
            _back = 0;
            return false;
        }
        if (reset)
        {
            _back = 0;
            _playing = false;
            _playStartedAt = null;
        }
        _prepared = true;
        UpdateDepth();
        OpenAll(_back, playing: _playing);
        return true;
    }

    /// <summary>
    /// Points every camera at the same instant and opens its decoder there. Each camera has its own
    /// recordings, so the instant is resolved against that camera's own segments; a camera with a
    /// shorter window simply holds its oldest frame instead of jumping out of step.
    /// </summary>
    void OpenAll(double secondsBack, bool playing)
    {
        foreach (var camera in _cameras)
        {
            var feed = FeedFor(camera.Index);
            var segments = SegmentsFor(camera.Index);
            if (segments.Count == 0)
            {
                feed.Close();
                continue;
            }
            var target = segments[^1].EndsAt.AddSeconds(-Math.Clamp(secondsBack, 0, Math.Max(0, _depth)));
            var index = -1;
            for (var candidate = 0; candidate < segments.Count; candidate++)
                if (target < segments[candidate].EndsAt) { index = candidate; break; }
            if (index < 0) index = segments.Count - 1;
            var segment = segments[index];
            var offset = Math.Clamp((target - segment.StartedAt).TotalSeconds, 0, Math.Max(0, segment.Seconds - 0.05));
            feed.Open(segment.Path, offset, playing);
        }
    }

    ReplayFeed FeedFor(int cameraIndex)
    {
        if (_feeds.TryGetValue(cameraIndex, out var feed)) return feed;
        feed = new ReplayFeed(_ui) { Speed = Speed };
        _feeds[cameraIndex] = feed;
        return feed;
    }

    IReadOnlyList<CameraSegment> SegmentsFor(int cameraIndex) =>
        _segments.TryGetValue(cameraIndex, out var list) ? list : [];

    DateTimeOffset? NewestEdge()
    {
        // The playhead is measured from the camera the operator is watching, so the scrubber's
        // length matches the picture in the big view.
        var preferred = SelectedCamera is { } camera ? SegmentsFor(camera.Index) : [];
        if (preferred.Count > 0) return preferred[^1].EndsAt;
        var newest = _segments.Values.Where(list => list.Count > 0).Select(list => list[^1].EndsAt).ToArray();
        return newest.Length == 0 ? null : newest.Max();
    }

    DateTimeOffset? OldestEdge()
    {
        var oldest = _segments.Values.Where(list => list.Count > 0).Select(list => list[0].StartedAt).ToArray();
        return oldest.Length == 0 ? null : oldest.Min();
    }

    public BitmapSource? LiveFrame(int cameraIndex)
    {
        if (cameraIndex < 0) return null;
        try { return CameraFeed.Get(cameraIndex).LastFrame; } catch { return null; }
    }

    /// <summary>A JPEG snapshot for the browser wall, encoded from the same frame the desk sees.</summary>
    public byte[]? FrameJpeg(int cameraIndex, bool replay)
    {
        var frame = replay && _prepared ? FrameFor(cameraIndex) : LiveFrame(cameraIndex);
        if (frame is null) return null;
        // Several wall tiles can ask for the same camera between two captured frames. Reuse the
        // encoded bytes instead of recompressing the same picture for every tile and every client.
        if (_liveJpegCache.TryGetValue(cameraIndex, out var cached) && ReferenceEquals(cached.Frame, frame))
            return cached.Data;
        var data = EncodeJpeg(frame);
        if (data is not null) _liveJpegCache[cameraIndex] = (frame, data);
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
            case "clear":
                ClearSelection();
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
        foreach (var feed in _feeds.Values) feed.Dispose();
        _feeds.Clear();
        _segments.Clear();
    }
}
