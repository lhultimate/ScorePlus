using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// The picture inside a scorebug video element. It watches <see cref="MediaPlayback"/> and only
/// shows the clip when the clip's tag matches the element's tag, so an "Ads" element stays empty
/// while a highlight reel is cued and vice versa. While cued it loops like a stadium screen, and it
/// freezes on the current frame whenever the operator pauses the transport.
/// </summary>
public sealed class VideoSurface : Grid
{
    ScorebugElement _item;
    readonly MediaPlayback? _playback;
    readonly MediaElement _element;
    readonly TextBlock? _placeholder = null;
    readonly RectangleGeometry _clip = new();
    // Live camera frames are drawn on this image instead of the MediaElement.
    readonly Image _live = new() { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
    // Decoded replay frames are drawn here for the same reason: no media player is involved, so a
    // recorded clip can never come out black or stuck on its first frame.
    readonly Image _replay = new() { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
    ReplayFeed? _replayFeed;
    readonly bool _mute;
    // True for a surface that is showing what the audience gets, even when it is muted. The
    // audience-view monitor is one: silent, but it must never show a replay the audience cannot.
    readonly bool _onAir;
    CameraFeed? _feed;
    int _cameraIndex = -1;
    string? _loadedPath;
    bool _playing;
    int _appliedSeek;
    // True once the decoder has opened the file, so a re-attached surface knows it already has
    // media loaded and should resume rather than treat itself as brand new.
    bool _opened;
    // A seek while playing pauses the picture and resumes it on the next dispatcher turn; the
    // queued resume owns Play() so this method does not start it inside the same tick.
    bool _resumeQueued;
    // Identifies the cue a queued freeze belongs to, so a timer left over from the previous clip
    // can never pause the new one.
    int _freezeToken;
    // Per-element audio. When this element names a speaker, its clip plays through that output
    // here, so two video boxes can carry two clips to two different speakers.
    AudioRouter? _router;
    string? _routedPath;
    string? _routedDevice;

    /// <param name="showPlaceholder">True only in the studio. The audience output and the preview
    /// stay blank while nothing is cued, so an off-air "VIDEO · GENERAL" label can never go out.</param>
    public VideoSurface(ScorebugElement item, MediaPlayback? playback, bool mute, bool showPlaceholder = true, bool onAir = false)
    {
        _item = item;
        _playback = playback;
        _mute = mute;
        _onAir = onAir;
        ClipToBounds = true;
        // MediaElement's video texture can paint past its layout slot when the aspect ratios differ,
        // so the picture is clipped to the element box explicitly instead of relying on layout only.
        Clip = _clip;
        SizeChanged += (_, _) => SyncClip();
        _element = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = item.PreserveVideoAspect ? Stretch.Uniform : Stretch.UniformToFill,
            IsMuted = mute,
            Volume = mute ? 0 : 1,
            // Needed so a seek while paused still draws the new frame on the scorebug.
            ScrubbingEnabled = true,
            ClipToBounds = true
        };
        // Re-apply once the MediaElement is in the tree so Play() is honoured. A surface that is
        // re-attached (the studio rebuilt its canvas) picks the picture up where the transport is
        // instead of clearing its source and restarting from frame one.
        _element.Loaded += (_, _) =>
        {
            if (_opened && _playback is { LivePosition: > 0.05 } transport)
            {
                try { _element.Position = TimeSpan.FromSeconds(transport.LivePosition); } catch { }
                _appliedSeek = _playback.SeekVersion;
                _playing = false;
            }
            Apply();
        };
        // A newly opened clip starts from the top; a paused cue holds its very first frame.
        _element.MediaOpened += (_, _) =>
        {
            _opened = true;
            // Open where the clip actually is: a rebuilt scorebug resumes the picture instead of
            // restarting it, and a fresh cue still starts from the operator's last seek.
            var resume = _playback?.LivePosition ?? 0;
            if (!double.IsFinite(resume) || resume <= 0.05) resume = _playback?.Position ?? 0;
            // Never ask for a point past the end of the clip: seeking beyond a short replay clip
            // left the box black until the transport moved the playhead again.
            if (_element.NaturalDuration.HasTimeSpan && _element.NaturalDuration.TimeSpan.TotalSeconds > 0)
                resume = Math.Min(resume, Math.Max(0, _element.NaturalDuration.TimeSpan.TotalSeconds - 0.1));
            try { _element.Position = TimeSpan.FromSeconds(Math.Max(0, resume)); } catch { }
            _appliedSeek = _playback?.SeekVersion ?? 0;
            if (_playback is { IsPlaying: false } && !_playing)
            {
                // Start decoding so a paused cue still shows its frame. Pausing on the very next
                // dispatcher turn froze some decoders before they had composited anything, which
                // is what left a black box in the preview until the clip was started. The freeze
                // now waits for the first frames to land.
                var token = ++_freezeToken;
                try { _element.Play(); } catch { }
                var freeze = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
                freeze.Tick += (_, _) =>
                {
                    freeze.Stop();
                    if (token != _freezeToken) return;
                    if (_live.Source is not null) _live.Visibility = Visibility.Collapsed;
                    // Start replay may have been pressed while the decoder was opening; in that
                    // case keep rolling instead of freezing on the first frame.
                    if (_playback is { IsPlaying: true }) { try { _element.Play(); } catch { } _playing = true; return; }
                    try { _element.Pause(); } catch { }
                    _playing = false;
                };
                freeze.Start();
                return;
            }
            // The live frame only covered the decoder while it opened the file; the clip is
            // drawing now, so put it away so nothing stale shows through.
            if (_live.Source is not null) _live.Visibility = Visibility.Collapsed;
            Apply();
        };
        _element.MediaEnded += (_, _) => Loop();
        // A decoder that fails mid-seek raises MediaFailed; unhandled, it takes the whole process
        // down. Catch it and blank the element instead, so the show keeps running.
        _element.MediaFailed += (_, _) => Fail();
        Children.Add(_element);
        Children.Add(_live);
        Children.Add(_replay);

        if (showPlaceholder)
        {
            _placeholder = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(item.MediaTag) ? "VIDEO" : $"VIDEO · {item.MediaTag.ToUpperInvariant()}",
                Foreground = Brushes.White,
                FontSize = Math.Max(9, item.FontSize * 0.6),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.55,
                IsHitTestVisible = false
            };
            Children.Add(_placeholder);
        }

        if (_playback is not null) _playback.Changed += Apply;
        Apply();
    }

    /// <summary>
    /// Points an existing surface at a freshly edited element. Reusing the surface is what keeps a
    /// playing clip running when anything else in the studio changes; rebuilding it restarted the
    /// picture from frame one.
    /// </summary>
    public void SetElement(ScorebugElement item)
    {
        _item = item;
        // Preserving the aspect ratio letterboxes the picture inside its box instead of cropping it.
        var stretch = item.PreserveVideoAspect ? Stretch.Uniform : Stretch.UniformToFill;
        _element.Stretch = stretch;
        _live.Stretch = stretch;
        _replay.Stretch = stretch;
        if (_placeholder is not null)
            _placeholder.Text = string.IsNullOrWhiteSpace(item.MediaTag) ? "VIDEO" : $"VIDEO · {item.MediaTag.ToUpperInvariant()}";
        Apply();
    }

    void SyncClip() => _clip.Rect = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));

    /// <summary>
    /// The clip this surface should be showing. A muted surface is a preview (studio, program
    /// thumbnail), and only previews see a replay that has been opened but not started.
    /// </summary>
    MediaEntry? Cue() => _mute && !_onAir
        ? _playback?.PreviewFor(_item.MediaTag)
        : _playback?.ActiveFor(_item.MediaTag);

    /// <summary>The clip this surface resolves right now, exposed so a check can prove a monitor and
    /// a preview disagree about a staged replay.</summary>
    public MediaEntry? CurrentCue => Cue();

    /// <summary>
    /// A clip that loops behaves like a stadium screen: rewind and keep rolling. A clip with its
    /// loop switched off holds its last frame and lets the session move to the next playlist item.
    /// </summary>
    void Loop()
    {
        if (_loadedPath is null) { _playing = false; return; }
        var entry = Cue();
        if (entry is null || !entry.Loop || _playback is { IsPlaying: false }) { _playing = false; return; }
        try
        {
            _element.Position = TimeSpan.Zero;
            _element.Play();
            _playing = true;
        }
        catch { _playing = false; }
    }

    void Apply()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Apply); return; }
        SyncClip();
        var entry = Cue();

        // A replay is decoded to frames by the session, not opened as a media file: the recorded
        // clips are MJPG AVI, which a MediaElement shows as one still frame on a machine without an
        // MJPEG decoder. Every surface shows the session's decoded picture, exactly like a camera.
        if (ShowReplayFrames(entry)) return;

        // A live camera has no file to open: its frames replace the MediaElement until the
        // operator cuts to an instant replay or another clip.
        if (entry is { IsCamera: true })
        {
            // A live feed has no file for the router to open. Stop the MediaElement even when the
            // same camera index is already selected: a surface that previously held a file must
            // never keep its soundtrack playing underneath the camera.
            StopRouting();
            try { _element.Stop(); _element.Source = null; } catch { }
            _loadedPath = null;
            _opened = false;
            _playing = false;
            ShowLiveCamera(entry.CameraIndex);
            if (_placeholder is not null) _placeholder.Visibility = Visibility.Collapsed;
            return;
        }
        HideLiveCamera();

        var matches = entry is not null && File.Exists(entry.Path);

        if (!matches)
        {
            StopRouting();
            _live.Visibility = Visibility.Collapsed;
            if (_loadedPath is not null)
            {
                // Stop, Close and then clear the source. A MediaElement that is only stopped can
                // keep painting its last frame — which is exactly what stayed on the program
                // preview after Stop — so it is also taken out of the picture entirely.
                try { _element.Stop(); _element.Close(); _element.Source = null; } catch { }
                _loadedPath = null;
                _opened = false;
            }
            _playing = false;
            // Nothing is cued, so the box shows the artwork behind it, not the last video frame.
            _element.Visibility = Visibility.Collapsed;
            if (_placeholder is not null) _placeholder.Visibility = Visibility.Visible;
            return;
        }

        _element.Visibility = Visibility.Visible;
        if (_placeholder is not null) _placeholder.Visibility = Visibility.Collapsed;

        // Each clip carries its own level, and a clip routed to a chosen speaker (on the clip or on
        // this element) is muted here so its sound never comes out of two places at once.
        try
        {
            _element.IsMuted = _mute || !string.IsNullOrWhiteSpace(_item.AudioDevice) || (_playback?.RoutedToDevice ?? false);
            // The clip's own level, through the bus that carries its tag: a speaker switched off
            // takes its clips silent with it instead of leaving them playing on another output.
            // A surface with no transport behind it has no on-air sound, and a clip whose bus is
            // switched off is silent rather than falling back to full level on the default output.
            var level = (_playback?.Volume ?? 1) * (_playback?.SpeakerGain ?? 0);
            _element.Volume = (float)Math.Clamp(level, 0, 1);
        }
        catch { }
        SyncRouting(entry!);

        if (_loadedPath != entry!.Path)
        {
            // A queued freeze from the previous cue must not touch this one.
            _freezeToken++;
            // Opening a file takes a few frames, during which the MediaElement is black. Hold the
            // last live picture over it so the scorebug never blinks out between live and replay.
            if (_live.Source is not null) _live.Visibility = Visibility.Visible;
            try
            {
                _element.Source = new Uri(entry.Path);
                _loadedPath = entry.Path;
                _playing = false;
                _appliedSeek = _playback?.SeekVersion ?? 0;
                if (_playback is null || _playback.IsPlaying) { _element.Play(); _playing = true; }
            }
            catch
            {
                _loadedPath = null;
                _playing = false;
                if (_placeholder is not null) _placeholder.Visibility = Visibility.Visible;
            }
            return;
        }

        // Same clip already loaded: follow the transport instead of restarting the picture.
        // ScrubbingEnabled draws the frame at the new position even while paused, so the element
        // needs no Play/Pause nudge here — which also keeps scrubbing smooth.
        var isTransportClip = _playback?.NowPlaying is { } transport
            && string.Equals(transport.Path, entry.Path, StringComparison.OrdinalIgnoreCase);
        if (isTransportClip && _playback is not null && _playback.SeekVersion != _appliedSeek)
        {
            _appliedSeek = _playback.SeekVersion;
            _router?.Seek(_playback.Position);
            var resume = _playing && _playback.IsPlaying;
            try
            {
                if (resume) { _element.Pause(); _playing = false; }
                _element.Position = TimeSpan.FromSeconds(Math.Max(0, _playback.Position));
                if (resume)
                {
                    // Resume on the next dispatcher turn. Calling Play() in the same tick as
                    // Position is what some decoders do not survive (the seek that closed the app).
                    _resumeQueued = true;
                    Dispatcher.BeginInvoke(() =>
                    {
                        _resumeQueued = false;
                        if (_playback is not { IsPlaying: true }) return;
                        try { _element.Play(); _playing = true; } catch { }
                    });
                    return;
                }
            }
            catch { }
        }

        var desired = _playback?.IsPlaying ?? true;
        if (desired == _playing || _resumeQueued) return;
        try
        {
            if (desired)
            {
                _element.Play();
                _playing = true;
                // The clip is drawing again; the last live frame was only a cover while it opened.
                if (_live.Source is not null) _live.Visibility = Visibility.Collapsed;
            }
            else
            {
                _element.Pause();
                _playing = false;
            }
        }
        catch { }
        if (desired) _router?.Resume(); else _router?.Pause();
    }

    /// <summary>
    /// Plays (or pauses, seeks) this element's clip on its own speaker. Doing nothing when the
    /// element has no speaker is deliberate: that case keeps the MediaElement's sound.
    /// </summary>
    void SyncRouting(MediaEntry entry)
    {
        // Only a surface that is actually on an audience output plays sound. Otherwise the studio
        // and every preview would open their own copy of the same speaker and triple the audio.
        if (_mute)
        {
            StopRouting();
            return;
        }
        var device = _item.AudioDevice;
        if (string.IsNullOrWhiteSpace(device))
        {
            StopRouting();
            return;
        }
        _router ??= new AudioRouter();
        if (!string.Equals(_routedPath, entry.Path, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_routedDevice, device, StringComparison.OrdinalIgnoreCase))
        {
            // Start at the transport's point so joining a running clip stays in sync.
            var start = _playback?.Position ?? 0;
            _router.Play(entry.Path, device, Math.Clamp((_playback?.Volume ?? entry.Volume) * (_playback?.SpeakerGain ?? 0), 0, 1), start);
            _routedPath = entry.Path;
            _routedDevice = device;
        }
        if (_playback is { IsPlaying: false }) _router.Pause(); else _router.Resume();
        if (_playback is not null) _router.SetVolume(Math.Clamp(_playback.Volume * _playback.SpeakerGain, 0, 1));
    }

    void StopRouting()
    {
        if (_router is null) return;
        _router.Stop();
        _routedPath = null;
        _routedDevice = null;
    }

    void ShowLiveCamera(int index)
    {
        if (_cameraIndex != index)
        {
            UnsubscribeLive();
            _cameraIndex = index;
            if (_loadedPath is not null)
            {
                try { _element.Stop(); _element.Source = null; } catch { }
                _loadedPath = null;
                _opened = false;
                _playing = false;
            }
            _feed = CameraFeed.Get(index);
            _feed.FrameReady += OnCameraFrame;
            _feed.Start();
        }
        // The last frame stays in the image: cutting back from a replay shows the camera instantly
        // instead of a blank box until the next frame arrives a fraction of a second later.
        _live.Visibility = Visibility.Visible;
        _element.Visibility = Visibility.Collapsed;
    }

    /// <summary>Stops listening to the feed but keeps its last frame on screen.</summary>
    void UnsubscribeLive()
    {
        if (_feed is not null) _feed.FrameReady -= OnCameraFrame;
        _feed = null;
        _cameraIndex = -1;
        _live.Visibility = Visibility.Collapsed;
        _element.Visibility = Visibility.Visible;
    }

    void OnCameraFrame(ImageSource frame) => _live.Source = frame;

    void HideLiveCamera()
    {
        if (_cameraIndex < 0) return;
        UnsubscribeLive();
    }

    /// <summary>Blanks the element after a decode failure instead of letting it crash the app.</summary>
    void Fail()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Fail); return; }
        _playing = false;
        _opened = false;
        _loadedPath = null;
        try { _element.Stop(); _element.Close(); _element.Source = null; } catch { }
        _element.Visibility = Visibility.Collapsed;
        _live.Visibility = Visibility.Collapsed;
        if (_placeholder is not null) _placeholder.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Draws a replay's decoded frames and reports false for every other cue, which also releases
    /// the subscription so a surface never keeps a picture from a clip it no longer shows.
    /// </summary>
    bool ShowReplayFrames(MediaEntry? entry)
    {
        var feed = _playback?.ReplayFrames;
        if (entry is null || feed is null || !feed.Contains(entry.Path))
        {
            StopReplayFrames();
            return false;
        }
        HideLiveCamera();
        StopRouting();
        if (!ReferenceEquals(_replayFeed, feed))
        {
            StopReplayFrames();
            _replayFeed = feed;
            feed.FrameReady += OnReplayFrame;
        }
        if (_loadedPath is not null)
        {
            // The file is never opened here, so nothing must stay on the MediaElement underneath.
            try { _element.Stop(); _element.Close(); _element.Source = null; } catch { }
            _loadedPath = null;
            _opened = false;
        }
        _playing = false;
        _element.Visibility = Visibility.Collapsed;
        if (_placeholder is not null) _placeholder.Visibility = Visibility.Collapsed;
        if (_replay.Source is null && feed.Frame is { } picture) _replay.Source = picture;
        _replay.Visibility = Visibility.Visible;
        return true;
    }

    void StopReplayFrames()
    {
        if (_replayFeed is not null) _replayFeed.FrameReady -= OnReplayFrame;
        _replayFeed = null;
        if (_replay.Visibility != Visibility.Collapsed) _replay.Visibility = Visibility.Collapsed;
        _replay.Source = null;
    }

    void OnReplayFrame(System.Windows.Media.Imaging.BitmapSource frame)
    {
        if (_replay.Visibility == Visibility.Visible) _replay.Source = frame;
    }

    public void Dispose()
    {
        if (_playback is not null) _playback.Changed -= Apply;
        StopReplayFrames();
        StopRouting();
        _router?.Dispose();
        _router = null;
        HideLiveCamera();
        _playing = false;
        _opened = false;
        try { _element.Stop(); _element.Source = null; } catch { }
    }
}
