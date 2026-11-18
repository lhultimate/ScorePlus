using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;
using Scoreboard.Core;
using Size = OpenCvSharp.Size;

namespace Scoreboard.Desktop;

/// <summary>One recorded camera clip on disk. Replay scrubs across a rolling list of these.</summary>
public sealed record CameraSegment(string Path, DateTimeOffset StartedAt, double Seconds)
{
    public DateTimeOffset EndsAt => StartedAt.AddSeconds(Seconds);
}

/// <summary>A capture device as Windows lists it. The index is the one OpenCV/DirectShow opens.</summary>
public sealed record CameraDevice(int Index, string Name)
{
    public string Label => $"{Name}   ·   camera {Index + 1}";
}

/// <summary>
/// A live camera feed, shared by every scorebug video element watching it. Frames are captured on
/// a background thread and handed to the UI already frozen, so a 1080p preview never fights the
/// dispatcher. While the feed runs it records short clips to disk; <see cref="Snapshot"/> hands back
/// the last couple of minutes, which is what Replay scrubs through and what a saved replay is cut from.
/// </summary>
public sealed class CameraFeed : IDisposable, ILiveSource
{
    // One capture per device index: opening the same webcam twice fails on most drivers, and two
    // scorebug elements showing the camera should share the one feed.
    static readonly Dictionary<int, CameraFeed> Feeds = [];

    // The capture device's own default resolution is used on purpose: forcing 720p on a webcam
    // that only delivers 5 fps at that size makes the scorebug look frozen. The rate also matches
    // the recorder, so an instant replay plays back at real speed rather than fast-forward.
    const double FramesPerSecond = 15;
    static readonly TimeSpan SegmentLength = TimeSpan.FromSeconds(120);

    /// <summary>The replay window in Preferences defaults to two minutes.</summary>
    public const double DefaultReplaySeconds = 120;

    /// <summary>How far back Replay can reach, in seconds. Driven by the saved preference.</summary>
    public static double ReplayWindowSeconds { get; set; } = DefaultReplaySeconds;

    readonly int _index;
    readonly Dispatcher _dispatcher;
    readonly object _gate = new();
    Thread? _thread;
    volatile bool _wanted;
    volatile bool _framePending;
    long _frameVersion;
    VideoWriter? _writer;
    string? _segmentPath;
    DateTime _segmentStarted;
    bool _recordingFailed;
    // Clips that have been closed and can be read back. Newest last. A replay scrubs across these.
    readonly List<CameraSegment> _segments = [];

    CameraFeed(int index, Dispatcher dispatcher)
    {
        _index = index;
        _dispatcher = dispatcher;
    }

    public static CameraFeed Get(int index)
    {
        var key = Math.Max(0, index);
        if (Feeds.TryGetValue(key, out var existing)) return existing;
        var feed = new CameraFeed(key, Dispatcher.CurrentDispatcher);
        Feeds[key] = feed;
        return feed;
    }

    /// <summary>Raised on the UI dispatcher for every captured frame.</summary>
    public event Action<BitmapSource>? FrameReady;

    /// <summary>The last capture error, shown in the dock instead of a silent black box.</summary>
    public string? Error { get; private set; }

    /// <summary>The newest frozen picture, shared with the Replay Booth and its browser wall.</summary>
    public BitmapSource? LastFrame { get; private set; }

    /// <summary>Increases with every published frame. The browser wall uses it as the image cache
    /// key, so selecting a camera does not make every tile download a fresh JPEG.</summary>
    public long FrameVersion => Interlocked.Read(ref _frameVersion);

    public bool IsLive => _wanted;

    /// <summary>Which capture device this feed opens.</summary>
    public int CameraIndex => _index;

    public bool CanRecord => !_recordingFailed && _segmentPath is not null;


    public void Start()
    {
        if (_wanted) return;
        _wanted = true;
        Error = null;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = $"Camera {_index + 1}" };
        _thread.Start();
    }

    public void Stop()
    {
        _wanted = false;
        var thread = _thread;
        _thread = null;
        try { thread?.Join(1500); } catch { }
        lock (_gate) CloseWriter();
    }

    /// <summary>
    /// Hands back the clips recorded so far, newest last, covering the configured replay window. The
    /// clip being written is closed first: it has to be finalised before anything can read it back,
    /// and the feed opens a fresh segment on its next frame. Empty means nothing was recorded.
    /// </summary>
    public IReadOnlyList<CameraSegment> Snapshot()
    {
        lock (_gate)
        {
            CloseWriter();
            if (_segments.Count == 0) return [];
            var newest = _segments[^1].EndsAt;
            var earliest = newest.AddSeconds(-Math.Max(1, ReplayWindowSeconds));
            return _segments.Where(segment => segment.EndsAt > earliest && File.Exists(segment.Path)).ToArray();
        }
    }

    void CloseWriter()
    {
        var path = _segmentPath;
        var seconds = (DateTime.UtcNow - _segmentStarted).TotalSeconds;
        try { _writer?.Release(); } catch { }
        try { _writer?.Dispose(); } catch { }
        _writer = null;
        _segmentPath = null;
        // A clip shorter than half a second is not a replay; a finished one joins the buffer.
        if (path is not null && seconds >= 0.5 && File.Exists(path))
            _segments.Add(new CameraSegment(path, new DateTimeOffset(_segmentStarted, TimeSpan.Zero), seconds));
        PruneSegments();
    }

    /// <summary>Keeps the replay window bounded: old clips beyond the configured window are deleted.</summary>
    void PruneSegments()
    {
        while (_segments.Count > 1 && _segments[^1].EndsAt - _segments[0].EndsAt > TimeSpan.FromSeconds(Math.Max(1, ReplayWindowSeconds) + 20))
        {
            var oldest = _segments[0];
            _segments.RemoveAt(0);
            try { File.Delete(oldest.Path); } catch { }
        }
    }

    /// <summary>
    /// Writes a clip covering [from, to] across the recorded segments. Runs off the UI thread; the
    /// frames are re-encoded, so a two-minute save takes a few seconds rather than being instant.
    /// </summary>
    public static bool Export(IReadOnlyList<CameraSegment> segments, DateTimeOffset from, DateTimeOffset to, string outputPath, out string? error)
    {
        error = null;
        VideoWriter? writer = null;
        try
        {
            foreach (var segment in segments)
            {
                if (segment.EndsAt <= from || segment.StartedAt >= to) continue;
                using var capture = new VideoCapture(segment.Path);
                if (!capture.IsOpened()) continue;
                using var frame = new Mat();
                var fps = Math.Max(1, (int)Math.Round(FramesPerSecond));
                var startOffset = Math.Max(0, (from - segment.StartedAt).TotalSeconds);
                var endOffset = Math.Min(segment.Seconds, (to - segment.StartedAt).TotalSeconds);
                var firstFrame = (int)Math.Round(startOffset * fps);
                var lastFrame = (int)Math.Round(endOffset * fps);
                if (lastFrame <= firstFrame) continue;
                capture.Set(VideoCaptureProperties.PosFrames, firstFrame);
                for (var index = firstFrame; index < lastFrame; index++)
                {
                    if (!capture.Read(frame) || frame.Empty()) break;
                    if (writer is null)
                    {
                        writer = new VideoWriter(outputPath, FourCC.MJPG, FramesPerSecond,
                            new Size(Math.Max(2, frame.Width), Math.Max(2, frame.Height)));
                        if (!writer.IsOpened()) { error = "The clip file could not be written."; return false; }
                    }
                    writer.Write(frame);
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try { writer?.Release(); } catch { }
            try { writer?.Dispose(); } catch { }
        }
        if (writer is null) error = "There was nothing recorded in that range.";
        return writer is not null;
    }

    /// <summary>
    /// The capture devices that can actually be opened, for the camera routing menu. Probed once
    /// and cached: opening a camera is slow, and a menu must not do it every time.
    /// </summary>
    public static IReadOnlyList<int> AvailableCameras()
    {
        if (AvailableCamerasCache is { } cached) return cached;
        var found = new List<int>();
        for (var index = 0; index < 6; index++)
        {
            VideoCapture? capture = null;
            try
            {
                capture = Open(index);
                if (capture is { } opened && opened.IsOpened()) found.Add(index);
            }
            catch { }
            finally { try { capture?.Release(); } catch { } try { capture?.Dispose(); } catch { } }
        }
        AvailableCamerasCache = found;
        return found;
    }

    static IReadOnlyList<int>? AvailableCamerasCache;

    static IReadOnlyList<CameraDevice>? _namedDevices;

    /// <summary>The Windows name of one capture device, for a camera item that has no name of its own.</summary>
    public static string CameraName(int index) =>
        NamedDevices().FirstOrDefault(device => device.Index == index)?.Name ?? $"Camera {index + 1}";

    /// <summary>
    /// Every capture device Windows knows, with its real name ("ICSprIng Camera"), in the same
    /// order DirectShow hands them out — which is the order the device index opens.
    /// </summary>
    public static IReadOnlyList<CameraDevice> NamedDevices()
    {
        if (_namedDevices is { } cached) return cached;
        List<CameraDevice> devices = [];
        try { devices = EnumerateVideoInputs().Select((name, index) => new CameraDevice(index, name)).ToList(); }
        catch { devices = []; }
        if (devices.Count == 0)
        {
            // Enumeration is unavailable here; fall back to the devices that actually open, so the
            // menu is never empty, and call them by number.
            devices = AvailableCameras().Select(index => new CameraDevice(index, $"Camera {index + 1}")).ToList();
        }
        _namedDevices = devices;
        return devices;
    }

    // DirectShow's device enumerator: the same one OpenCV uses, which is why its order and the
    // device index line up. Only the friendly name is read from each moniker.
    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator(ref Guid deviceClass, out IEnumMoniker? enumMoniker, int flags);
    }

    [ComImport, Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86")]
    class SystemDeviceEnum
    {
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyBag
    {
        [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string propertyName, out object? value, IntPtr errorLog);
    }

    static List<string> EnumerateVideoInputs()
    {
        var names = new List<string>();
        var category = new Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86");
        ICreateDevEnum? deviceEnumerator = null;
        try
        {
            deviceEnumerator = (ICreateDevEnum)new SystemDeviceEnum();
            // S_FALSE (1) means the category exists but holds nothing.
            if (deviceEnumerator.CreateClassEnumerator(ref category, out var monikers, 0) != 0 || monikers is null) return names;
            try
            {
                var buffer = new IMoniker[1];
                while (monikers.Next(1, buffer, IntPtr.Zero) == 0)
                {
                    var moniker = buffer[0];
                    object? bag = null;
                    try
                    {
                        var bagId = typeof(IPropertyBag).GUID;
                        moniker.BindToStorage(null!, null!, ref bagId, out bag);
                        var name = bag is IPropertyBag properties && properties.Read("FriendlyName", out var value, IntPtr.Zero) >= 0
                            ? value as string
                            : null;
                        names.Add(string.IsNullOrWhiteSpace(name) ? $"Camera {names.Count + 1}" : name.Trim());
                    }
                    catch { names.Add($"Camera {names.Count + 1}"); }
                    finally
                    {
                        if (bag is not null) Marshal.ReleaseComObject(bag);
                        if (moniker is not null) Marshal.ReleaseComObject(moniker);
                    }
                }
            }
            finally { Marshal.ReleaseComObject(monikers); }
        }
        finally
        {
            if (deviceEnumerator is not null) Marshal.ReleaseComObject(deviceEnumerator);
        }
        return names;
    }

    void CaptureLoop()
    {
        VideoCapture? capture = null;
        try
        {
            capture = Open(_index);
            if (capture is null || !capture.IsOpened())
            {
                Error = $"Camera {_index + 1} is not available.";
                return;
            }
            using var frame = new Mat();
            var interval = (int)(1000 / FramesPerSecond);
            while (_wanted)
            {
                var started = Environment.TickCount64;
                if (!capture.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(60);
                    continue;
                }
                RecordFrame(frame);
                PublishFrame(frame);
                var spent = (int)(Environment.TickCount64 - started);
                var wait = interval - spent;
                if (wait > 0) Thread.Sleep(wait);
            }
        }
        catch (Exception ex)
        {
            Error = $"Camera {_index + 1}: {ex.Message}";
        }
        finally
        {
            lock (_gate) CloseWriter();
            try { capture?.Release(); capture?.Dispose(); } catch { }
            _wanted = false;
        }
    }

    static VideoCapture? Open(int index)
    {
        // DirectShow starts in well under a second, where Media Foundation can take several, and
        // its 15 fps at the camera's native size matches the recorder. Media Foundation is the
        // fallback for drivers that refuse DirectShow.
        try
        {
            var directShow = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
            if (directShow.IsOpened()) return directShow;
            directShow.Dispose();
        }
        catch { }
        try { return new VideoCapture(index); } catch { return null; }
    }

    void RecordFrame(Mat frame)
    {
        if (_recordingFailed) return;
        lock (_gate)
        {
            if (_writer is null)
            {
                try
                {
                    var folder = AppPaths.Combine("CameraClips");
                    Directory.CreateDirectory(folder);
                    PruneFolder(folder);
                    var path = Path.Combine(folder, $"camera-{DateTime.Now:yyyyMMdd-HHmmss}.avi");
                    var writer = new VideoWriter(path, FourCC.MJPG, FramesPerSecond,
                        new Size(Math.Max(2, frame.Width), Math.Max(2, frame.Height)));
                    if (!writer.IsOpened())
                    {
                        writer.Dispose();
                        _recordingFailed = true;
                        return;
                    }
                    _writer = writer;
                    _segmentPath = path;
                    _segmentStarted = DateTime.UtcNow;
                }
                catch
                {
                    _recordingFailed = true;
                    return;
                }
            }
            try
            {
                _writer!.Write(frame);
                // Long shows would otherwise fill the disk with one endless file.
                if (DateTime.UtcNow - _segmentStarted > SegmentLength) CloseWriter();
            }
            catch
            {
                CloseWriter();
                _recordingFailed = true;
            }
        }
    }

    /// <summary>Keeps the newest camera clips on disk so replays never grow without limit.</summary>
    static void PruneFolder(string folder)
    {
        try
        {
            // One segment may be 120s; keep enough finished clips for the chosen window plus spares.
            var keep = (int)Math.Ceiling(Math.Max(1, ReplayWindowSeconds) / SegmentLength.TotalSeconds) + 3;
            foreach (var stale in new DirectoryInfo(folder).GetFiles("camera-*.avi")
                         .OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep))
                stale.Delete();
        }
        catch { }
    }

    void PublishFrame(Mat frame)
    {
        if (_framePending) return;
        BitmapSource? bitmap = null;
        try
        {
            // Copy the raw BGR frame straight into a bitmap. Encoding every frame as JPEG and
            // decoding it again was the bottleneck: it capped the preview at about 12 fps.
            var stride = (int)frame.Step();
            var image = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr24, null,
                frame.Data, stride * frame.Height, stride);
            image.Freeze();
            bitmap = image;
        }
        catch { return; }
        var picture = bitmap;
        _framePending = true;
        _dispatcher.BeginInvoke(() =>
        {
            _framePending = false;
            LastFrame = picture;
            Interlocked.Increment(ref _frameVersion);
            try { FrameReady?.Invoke(picture); } catch { }
        });
    }

    public void Dispose()
    {
        Stop();
        Feeds.Remove(_index);
    }
}
