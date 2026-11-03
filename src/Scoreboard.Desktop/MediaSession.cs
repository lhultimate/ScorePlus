using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// One media library and one transport for the whole session. The main window and every detached
/// control panel host their own <see cref="MediaDock"/> against this same object, so a clip cued on
/// one panel is exactly what the other panels and the scorebug see.
/// Clips live in folders, route to the scorebug by tag, loop individually, and can be collected
/// into playlists that run one after another.
/// </summary>
public sealed class MediaSession
{
    readonly UserPreferences _preferences;
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    // ScrubbingEnabled is what makes Position stick while the clip is paused; without it the
    // transport can only be watched from the beginning.
    readonly MediaPlayer _player = new() { ScrubbingEnabled = true };
    readonly DispatcherTimer _transport = new() { Interval = TimeSpan.FromMilliseconds(200) };
    // Scrub bursts are coalesced into a single seek: pause/seek/play on every pixel of a drag is
    // what made seeking feel laggy.
    readonly DispatcherTimer _seekApply = new() { Interval = TimeSpan.FromMilliseconds(80) };
    // A clip sent to a chosen speaker plays its sound here, on that device, instead of in the
    // MediaElement (which can only ever use the Windows default output).
    readonly AudioRouter _audio = new();
    CameraFeed? _camera;
    // The live microphone, if one is cued. Like the camera it owns a capture device while on air.
    MicrophoneFeed? _microphone;
    // Every live source that is on air at once. A show can run a wide camera, a tight camera and
    // two commentary microphones together; each keeps its own capture and its own output.
    readonly List<MediaEntry> _liveCameras = [];
    readonly List<MediaEntry> _liveMicrophones = [];
    // The recorded clips a replay is travelling through, newest last, and the point inside them.
    readonly List<CameraSegment> _replayBuffer = [];
    int _replayIndex = -1;
    // False while the replay is only in the preview; true once Start replay put it on air.
    bool _replayOnAir;
    readonly Dictionary<string, ImageSource> _thumbs = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _pendingThumbs = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, int> _thumbAttempts = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _folders = [];

    bool _playing;
    bool _scrubbing;
    double _duration;
    // A seek asked for before the decoder opened is replayed as soon as it does.
    double? _pendingSeek;
    double _seekTarget;
    MediaPlaylist? _playlist;
    int _playlistIndex = -1;

    public MediaSession(UserPreferences preferences)
    {
        _preferences = preferences;
        Playback = new MediaPlayback();
        foreach (var entry in preferences.Media)
            if (!string.IsNullOrWhiteSpace(entry.Path) && !Library.Any(m => string.Equals(m.Path, entry.Path, StringComparison.OrdinalIgnoreCase)))
                Library.Add(entry with { Tag = MediaLibrary.NormalizeTag(entry.Tag), Folder = MediaLibrary.NormalizeFolder(entry.Folder) });
        foreach (var playlist in preferences.Playlists)
            if (!string.IsNullOrWhiteSpace(playlist.Name))
                Playlists.Add(new MediaPlaylist { Name = playlist.Name.Trim(), Loop = playlist.Loop, Paths = playlist.Paths.ToList() });
        foreach (var folder in preferences.MediaFolders)
        {
            var normalized = MediaLibrary.NormalizeFolder(folder);
            if (normalized.Length > 0 && !_folders.Contains(normalized, StringComparer.OrdinalIgnoreCase)) _folders.Add(normalized);
        }


        _player.MediaOpened += (_, _) =>
        {
            _duration = _player.NaturalDuration.HasTimeSpan ? Math.Max(0, _player.NaturalDuration.TimeSpan.TotalSeconds) : 0;
            if (_pendingSeek is double pending)
            {
                _pendingSeek = null;
                try { _player.Position = TimeSpan.FromSeconds(Math.Clamp(pending, 0, Math.Max(0, _duration))); } catch { }
            }
            TransportChanged?.Invoke();
            if (_playing) _player.Play();
        };
        _player.MediaEnded += (_, _) => HandleClipEnded();
        _player.MediaFailed += (_, e) =>
        {
            SetTransportPlaying(false);
            Status?.Invoke($"MEDIA ERROR · {e.ErrorException?.Message ?? "this machine cannot decode that file"}");
        };
        _transport.Tick += (_, _) =>
        {
            // Keep the shared playhead current so a rebuilt scorebug surface picks the clip up
            // where it is instead of restarting it from the first frame.
            if (Current is { IsCamera: false } && _player.Source is not null)
                Playback.ReportPosition(_audio.IsPlaying ? _audio.Position : Position);
            if (IsReplaying) TrackReplay();
            if (_scrubbing) return;
            TransportChanged?.Invoke();
        };
        _transport.Start();
        _seekApply.Tick += (_, _) => { _seekApply.Stop(); ApplySeek(); };
    }

    /// <summary>Clips in the library, in import order. Shared by every dock.</summary>
    public ObservableCollection<MediaEntry> Library { get; } = [];

    /// <summary>Saved playlists, shared by every dock.</summary>
    public ObservableCollection<MediaPlaylist> Playlists { get; } = [];

    /// <summary>The clip that scorebug video elements watch, including its paused/running state.</summary>
    public MediaPlayback Playback { get; }

    public MediaEntry? Selected { get; private set; }

    public MediaEntry? Current { get; private set; }

    /// <summary>The camera entry behind the live feed, so Replay can go back to it.</summary>
    public MediaEntry? CameraEntry { get; private set; }

    /// <summary>The microphone item currently cued, if any.</summary>
    public MediaEntry? MicrophoneEntry { get; private set; }

    /// <summary>True while an instant replay of a camera clip is open.</summary>
    public bool IsReplaying { get; private set; }

    /// <summary>True once Start replay put that replay on air; false while it is preview only.</summary>
    public bool ReplayOnAir => IsReplaying && _replayOnAir;

    /// <summary>True while a live camera is cued.</summary>
    public bool IsCameraLive => Current?.IsCamera == true && _camera is not null;

    /// <summary>True while a live microphone is cued and being monitored.</summary>
    public bool IsMicrophoneLive => Current?.IsMicrophone == true && _microphone?.IsRunning == true;

    /// <summary>Every camera currently on air, in the order they were cued.</summary>
    public IReadOnlyList<MediaEntry> LiveCameras => _liveCameras;

    /// <summary>Every microphone currently on air.</summary>
    public IReadOnlyList<MediaEntry> LiveMicrophones => _liveMicrophones;

    public bool IsCameraOnAir(MediaEntry entry) =>
        _liveCameras.Any(camera => string.Equals(camera.Path, entry.Path, StringComparison.OrdinalIgnoreCase));

    public bool IsMicrophoneOnAir(MediaEntry entry) =>
        _liveMicrophones.Any(microphone => string.Equals(microphone.Path, entry.Path, StringComparison.OrdinalIgnoreCase));

    /// <summary>The capture device's last error, for the dock's status line.</summary>
    public string? Error => Current?.IsMicrophone == true ? _microphone?.Error : _camera?.Error;

    /// <summary>How far behind live the replay playhead is, in seconds. 0 is the live edge.</summary>
    public double ReplayBack { get; private set; }

    /// <summary>How much recorded material the scrubber covers, in seconds (up to two minutes).</summary>
    public double ReplayDepth { get; private set; }

    /// <summary>The playlist currently running, or null when a clip was cued on its own.</summary>
    public MediaPlaylist? ActivePlaylist => _playlist;

    public int PlaylistPosition => _playlistIndex;

    /// <summary>Folder shown in the docks; null means every folder. Shared so all panels agree.</summary>
    public string? FolderFilter { get; private set; }

    /// <summary>The clips the dock should show, honouring the folder filter.</summary>
    public IReadOnlyList<MediaEntry> VisibleLibrary =>
        FolderFilter is null
            ? Library.ToArray()
            : Library.Where(e => string.Equals(MediaLibrary.NormalizeFolder(e.Folder), FolderFilter, StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>Every folder on the shelf: ones that hold clips plus ones created empty.</summary>
    public IReadOnlyList<string> Folders => _folders.Concat(MediaLibrary.Folders(Library))
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool IsPlaying => _playing;

    public double Position
    {
        get
        {
            try { return Math.Max(0, _player.Position.TotalSeconds); } catch { return 0; }
        }
    }

    public double Duration => _duration;

    /// <summary>Raised when the library or the selection changes.</summary>
    public event Action? StateChanged;

    /// <summary>Raised as the playhead moves, so only the transport redraws on every tick.</summary>
    public event Action? TransportChanged;

    /// <summary>Raised when a thumbnail becomes available for the given path.</summary>
    public event Action<string>? ThumbnailReady;

    /// <summary>Status-bar style message for whichever dock is active.</summary>
    public event Action<string>? Status;

    /// <summary>Lets a dock post a message without raising the event itself.</summary>
    public void Report(string message) => Status?.Invoke(message);

    public ImageSource? Thumbnail(MediaEntry entry)
    {
        if (_thumbs.TryGetValue(entry.Path, out var cached)) return cached;
        QueueThumbnail(entry);
        return null;
    }

    public bool HasThumbnail(MediaEntry entry) => _thumbs.ContainsKey(entry.Path);

    public void BeginScrub() => _scrubbing = true;

    public void EndScrub() => _scrubbing = false;

    /// <summary>Adds a folder to the shelf even before anything is filed in it.</summary>
    public string CreateFolder(string? folder)
    {
        var normalized = MediaLibrary.NormalizeFolder(folder);
        if (normalized.Length == 0) return "";
        if (_folders.Contains(normalized, StringComparer.OrdinalIgnoreCase)) return normalized;
        _folders.Add(normalized);
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"FOLDER CREATED · {normalized}");
        return normalized;
    }

    /// <summary>
    /// Clears the organisation for a new game: no folders, no playlists, every clip unfiled. The
    /// imported media itself is kept — a new game is a blank slate for the production, not a reason
    /// to throw away the operator's asset library.
    /// </summary>
    public void ResetOrganization()
    {
        Stop(keepStatus: true);
        Playlists.Clear();
        _folders.Clear();
        for (var index = 0; index < Library.Count; index++)
            if (Library[index].Folder.Length > 0) Library[index] = Library[index] with { Folder = "" };
        FolderFilter = null;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke("MEDIA · new game · folders and playlists cleared");
    }

    public void SetFolderFilter(string? folder)
    {
        var normalized = folder is null ? null : MediaLibrary.NormalizeFolder(folder);
        if (string.Equals(FolderFilter, normalized, StringComparison.OrdinalIgnoreCase)) return;
        FolderFilter = normalized;
        StateChanged?.Invoke();
    }

    public void Seek(double seconds)
    {
        var target = Math.Max(0, seconds);            if (_duration > 0) target = Math.Min(target, _duration);
        _seekTarget = target;
        _seekApply.Stop();
        _seekApply.Start();
        TransportChanged?.Invoke();
    }

    /// <summary>
    /// Notes the last media operation in <c>logs\media-last-action.txt</c>. A decoder that dies
    /// natively leaves no managed traceback; this file says exactly which step it died on.
    /// </summary>
    static void Trace(string message)
    {
        try
        {
            var folder = AppPaths.Combine("logs");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "media-last-action.txt"), $"{DateTimeOffset.Now:O} {message}");
        }
        catch { /* tracing must never interfere with playback */ }
    }

    /// <summary>Applies the latest requested point once the drag has settled.</summary>
    void ApplySeek()
    {
        var target = _seekTarget;
        _pendingSeek = target;
        Trace($"seek {target:0.00}s · {Current?.Label ?? "no clip"} · transport start");
        if (_player.Source is not null)
        {
            try
            {
                // A seek is applied most reliably while paused, so the playing clip is briefly held.
                var resume = _playing;
                if (resume) _player.Pause();
                _player.Position = TimeSpan.FromSeconds(target);
                _pendingSeek = null;
                // Resume one turn later: Play() in the same tick as Position can kill the decoder.
                if (resume) _ui.BeginInvoke(() => { try { _player.Play(); } catch { } });
                Trace($"seek {target:0.00}s · transport moved");
            }
            catch { }
        }
        _audio.Seek(target);
        // The picture the operator watches is the scorebug video, not this muted transport player.
        Trace($"seek {target:0.00}s · publishing to the scorebug ({Playback.NowPlaying?.Label ?? "nothing"})");
        Playback.Seek(target);
        TransportChanged?.Invoke();
        Trace($"seek {target:0.00}s · done");
    }

    public void Import(IEnumerable<string> paths, string tag, string? folder = null)
    {
        var normalized = MediaLibrary.NormalizeTag(tag);
        var folderName = MediaLibrary.NormalizeFolder(folder);
        var added = 0;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (Library.Any(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            // A video overlay loops like a stadium screen until the operator says otherwise.
            Library.Add(new MediaEntry(path, normalized, folderName, Loop: MediaLibrary.IsVideoFile(path)));
            added++;
        }
        Save();
        StateChanged?.Invoke();
        Status?.Invoke(added == 0
            ? "MEDIA · those clips are already in the library"
            : $"MEDIA · {Library.Count} clip{(Library.Count == 1 ? "" : "s")} · {added} new in {normalized}{(folderName.Length == 0 ? "" : $" / {folderName}")}");
    }

    /// <summary>
    /// Empties the media side of the show: the library, folders and playlists, and whatever is on
    /// air. Used by the dock's Clear button, which also blanks the last camera frame.
    /// </summary>
    public void ClearAll()
    {
        Stop(keepStatus: true);
        Playlists.Clear();
        _folders.Clear();
        Library.Clear();
        FolderFilter = null;
        Selected = null;
        Current = null;
        CameraEntry = null;
        IsReplaying = false;
        _replayOnAir = false;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke("MEDIA CLEARED · library, folders, playlists and the screen are empty");
    }

    public void Remove(MediaEntry entry)
    {
        Library.Remove(entry);
        foreach (var playlist in Playlists) playlist.Paths.RemoveAll(p => string.Equals(p, entry.Path, StringComparison.OrdinalIgnoreCase));
        if (ReferenceEquals(Selected, entry)) Selected = null;
        if (ReferenceEquals(Current, entry)) Stop();
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"REMOVED · {entry.Label}");
    }

    public void Retag(MediaEntry entry, string? tag)
    {
        var normalized = MediaLibrary.NormalizeTag(tag);
        if (entry.Tag == normalized)
        {
            Selected = entry;
            StateChanged?.Invoke();
            return;
        }
        var updated = MediaLibrary.Retag(Library, entry, normalized);
        if (updated is null) return;
        ReplaceCurrent(entry, updated);
        Selected = updated;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"TAG · {updated.Label} → {normalized}");
    }

    /// <summary>Moves the selected clip into a folder (or the unfiled bucket with an empty name).</summary>
    public void MoveToFolder(MediaEntry entry, string? folder)
    {
        var normalized = MediaLibrary.NormalizeFolder(folder);
        if (MediaLibrary.NormalizeFolder(entry.Folder) == normalized)
        {
            Selected = entry;
            StateChanged?.Invoke();
            return;
        }
        var updated = MediaLibrary.MoveToFolder(Library, entry, normalized);
        if (updated is null) return;
        ReplaceCurrent(entry, updated);
        Selected = updated;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"FOLDER · {updated.Label} → {updated.FolderLabel}");
    }

    // ---------- a folder as a file ----------

    /// <summary>True when a folder path is this folder or lives somewhere under it.</summary>
    static bool InFolder(string path, string folder) =>
        folder.Length == 0
            ? path.Length == 0
            : string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
              || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder the operator would export right now, for the dock's button state.</summary>
    public bool CanExportFolder(string? folder)
    {
        var normalized = MediaLibrary.NormalizeFolder(folder);
        return Library.Any(entry => InFolder(MediaLibrary.NormalizeFolder(entry.Folder), normalized))
            || _folders.Any(name => InFolder(name, normalized));
    }

    /// <summary>
    /// Writes a folder, everything under it and the clips inside it to one ScorePlus Folder
    /// (.spf). It is a zip rather than a bare manifest, so the folder really carries its media with
    /// it and round-trips on another machine: nested folders, tags, loops, levels, speakers, cameras.
    /// </summary>
    public bool ExportFolder(string? folderPath, string destination)
    {
        try
        {
            var folder = MediaLibrary.NormalizeFolder(folderPath);
            var manifest = new MediaFolderManifest { Folder = folder };
            foreach (var name in Folders.Where(candidate => InFolder(candidate, folder)))
                if (!manifest.Folders.Contains(name, StringComparer.OrdinalIgnoreCase)) manifest.Folders.Add(name);
            if (folder.Length > 0 && !manifest.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) manifest.Folders.Insert(0, folder);

            using (var stream = File.Create(destination))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var index = 0;
                foreach (var entry in Library.Where(item => InFolder(MediaLibrary.NormalizeFolder(item.Folder), folder)).ToArray())
                {
                    var clip = new MediaFolderClip
                    {
                        Path = entry.IsCamera || entry.IsMicrophone ? entry.Path : "",
                        Tag = entry.Tag,
                        Folder = MediaLibrary.NormalizeFolder(entry.Folder),
                        Loop = entry.Loop,
                        Volume = entry.Volume,
                        AudioDevice = entry.AudioDevice,
                        Name = entry.Name,
                        CameraDevice = entry.CameraDevice,
                        MicrophoneDevice = entry.MicrophoneDevice
                    };
                    if (!entry.IsCamera && !entry.IsMicrophone && File.Exists(entry.Path))
                    {
                        clip.File = $"files/{index:000}-{Path.GetFileName(entry.Path)}";
                        var item = archive.CreateEntry(clip.File, CompressionLevel.Optimal);
                        using var target = item.Open();
                        using var source = File.OpenRead(entry.Path);
                        source.CopyTo(target);
                        index++;
                    }
                    manifest.Clips.Add(clip);
                }

                var json = archive.CreateEntry(MediaFolderManifest.FileName, CompressionLevel.Optimal);
                using var writer = new StreamWriter(json.Open());
                writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }

            Status?.Invoke($"FOLDER EXPORTED · {(folder.Length == 0 ? "unfiled" : folder)} · {manifest.Clips.Count} clip{(manifest.Clips.Count == 1 ? "" : "s")} → {Path.GetFileName(destination)}");
            return true;
        }
        catch (Exception ex)
        {
            Status?.Invoke($"FOLDER EXPORT FAILED · {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Brings a ScorePlus Folder (.spf) back in. Its clips are unpacked into the library's own
    /// media folder, never over an existing file, and every folder in the manifest is recreated —
    /// nested ones included — before the clips are filed into them.
    /// </summary>
    public bool ImportFolder(string source)
    {
        try
        {
            using var archive = ZipFile.OpenRead(source);
            var manifestEntry = archive.GetEntry(MediaFolderManifest.FileName);
            if (manifestEntry is null)
            {
                Status?.Invoke("FOLDER IMPORT · that file is not a ScorePlus Folder");
                return false;
            }

            MediaFolderManifest? manifest;
            using (var reader = new StreamReader(manifestEntry.Open()))
                manifest = JsonSerializer.Deserialize<MediaFolderManifest>(reader.ReadToEnd());
            if (manifest is null)
            {
                Status?.Invoke("FOLDER IMPORT · its manifest could not be read");
                return false;
            }

            var landing = AppPaths.Combine("media");
            Directory.CreateDirectory(landing);
            foreach (var name in manifest.Folders.Select(MediaLibrary.NormalizeFolder).Where(name => name.Length > 0))
                if (!_folders.Contains(name, StringComparer.OrdinalIgnoreCase)) _folders.Add(name);

            var added = 0;
            foreach (var clip in manifest.Clips)
            {
                var folder = MediaLibrary.NormalizeFolder(clip.Folder);
                if (folder.Length > 0 && !_folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) _folders.Add(folder);

                if (MediaLibrary.CameraIndex(clip.Path) is not null || MediaLibrary.MicrophoneIndex(clip.Path) is not null)
                {
                    if (Library.Any(entry => string.Equals(entry.Path, clip.Path, StringComparison.OrdinalIgnoreCase))) continue;
                    Library.Add(new MediaEntry(clip.Path, MediaLibrary.NormalizeTag(clip.Tag), folder, clip.Loop,
                        Math.Clamp(clip.Volume, 0, 1), clip.AudioDevice, clip.Name, clip.CameraDevice, clip.MicrophoneDevice));
                    added++;
                    continue;
                }

                var zipEntry = string.IsNullOrWhiteSpace(clip.File) ? null : archive.GetEntry(clip.File);
                if (zipEntry is null) continue;
                var name = Path.GetFileName(zipEntry.FullName);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var target = UniqueFile(landing, name);
                zipEntry.ExtractToFile(target, true);
                Library.Add(new MediaEntry(target, MediaLibrary.NormalizeTag(clip.Tag), folder, clip.Loop,
                    Math.Clamp(clip.Volume, 0, 1), clip.AudioDevice, clip.Name, clip.CameraDevice));
                added++;
            }

            var landed = MediaLibrary.NormalizeFolder(manifest.Folder);
            if (landed.Length > 0) FolderFilter = landed;
            Save();
            StateChanged?.Invoke();
            Status?.Invoke(added == 0
                ? "FOLDER IMPORT · that folder is already in the library"
                : $"FOLDER IMPORTED · {(landed.Length == 0 ? "unfiled" : landed)} · {added} clip{(added == 1 ? "" : "s")}");
            return true;
        }
        catch (Exception ex)
        {
            Status?.Invoke($"FOLDER IMPORT FAILED · {ex.Message}");
            return false;
        }
    }

    /// <summary>A name in the media folder nothing else uses, so an import never overwrites a clip.</summary>
    static string UniqueFile(string directory, string name)
    {
        var target = Path.Combine(directory, name);
        if (!File.Exists(target)) return target;
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var attempt = 1; attempt < 1000; attempt++)
        {
            target = Path.Combine(directory, $"{stem}-{attempt}{extension}");
            if (!File.Exists(target)) return target;
        }
        return Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
    }

    /// <summary>Turns this one clip's own loop on or off.</summary>
    public void SetClipLoop(MediaEntry entry, bool loop)
    {
        var updated = MediaLibrary.SetLoop(Library, entry, loop);
        if (updated is null) return;
        ReplaceCurrent(entry, updated);
        Selected = updated;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"LOOP {(loop ? "ON" : "OFF")} · {updated.Label}");
    }

    /// <summary>
    /// The library's current record for this clip. A tile or a second panel can hold a stale copy
    /// after an edit; editing by path means the change always lands on the live record instead of
    /// silently doing nothing (which is what made a chosen speaker snap back to Default).
    /// </summary>
    MediaEntry Resolve(MediaEntry entry) =>
        Library.FirstOrDefault(m => string.Equals(m.Path, entry.Path, StringComparison.OrdinalIgnoreCase)) ?? entry;

    static bool SameClip(MediaEntry? a, MediaEntry b) =>
        a is not null && string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);

    /// <summary>Turns this clip's own output level, in place, without restarting the picture.</summary>
    public void SetClipVolume(MediaEntry entry, double volume)
    {
        entry = Resolve(entry);
        var updated = MediaLibrary.SetVolume(Library, entry, volume);
        if (updated is null) return;
        // A level change must never move the playhead, so the record is swapped in place.
        if (SameClip(Current, updated)) Current = updated;
        if (SameClip(Selected, updated)) Selected = updated;
        Playback.UpdateEntry(updated);
        Save();
        if (SameClip(Current, updated))
        {
            // A live microphone is monitored by its feed, so its own level is set there.
            if (updated.IsMicrophone) _microphone?.SetVolume(updated.Volume);
            _player.Volume = updated.IsVideo && string.IsNullOrWhiteSpace(updated.AudioDevice) ? 0 : (float)updated.Volume;
            if (updated.IsVideo && !string.IsNullOrWhiteSpace(updated.AudioDevice)) _audio.SetVolume(updated.Volume);
        }
        Playback.RefreshAudio();
        StateChanged?.Invoke();
        Status?.Invoke($"VOLUME {updated.VolumePercent}% · {updated.Label}");
    }

    /// <summary>Sends this clip's sound to a chosen speaker, or back to the Windows default.</summary>
    public void SetClipAudioDevice(MediaEntry entry, string? deviceId)
    {
        entry = Resolve(entry);
        var updated = MediaLibrary.SetAudioDevice(Library, entry, deviceId);
        if (updated is null) return;
        if (SameClip(Current, updated)) Current = updated;
        if (SameClip(Selected, updated)) Selected = updated;
        Playback.UpdateEntry(updated);
        Save();
        if (SameClip(Current, updated) && updated.IsMicrophone)
        {
            // Moving a live mic to another output re-opens its monitor; the capture stays live.
            _microphone?.SetOutput(updated.AudioDevice);
            Status?.Invoke($"OUTPUT · {updated.Label} → {updated.AudioLabel}");
            return;
        }
        if (SameClip(Current, updated) && updated.IsVideo)
        {
            _audio.Stop();
            var routed = !string.IsNullOrWhiteSpace(updated.AudioDevice);
            if (routed) _audio.Play(updated.Path, updated.AudioDevice, updated.Volume, Position);
            // A speaker that cannot be opened must not silence the clip: the scorebug element takes
            // the sound back over. A video's sound never comes from this transport player.
            Playback.ReportRouting(routed && _audio.LastError is not null);
            _player.Volume = 0;
        }
        Playback.RefreshAudio();
        StateChanged?.Invoke();
        Status?.Invoke(_audio.LastError is { } failure
            ? $"OUTPUT ERROR · {failure}"
            : $"OUTPUT · {updated.Label} → {updated.AudioLabel}");
    }

    // ---------- live camera ----------

    /// <summary>Adds a capture device to the library once, tagged so a scorebug element can show it.</summary>
    public MediaEntry AddCamera(int index, string? tag = null)
    {
        var path = MediaLibrary.CameraPath(index);
        var existing = Library.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Selected = existing;
            StateChanged?.Invoke();
            Status?.Invoke($"{existing.Label} is already in the library · tag {existing.Tag}");
            return existing;
        }
        var entry = new MediaEntry(path, MediaLibrary.NormalizeTag(string.IsNullOrWhiteSpace(tag) ? "Camera" : tag));
        Library.Add(entry);
        Selected = entry;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"CAMERA ADDED · {entry.Label} · tag {entry.Tag}");
        return entry;
    }

    /// <summary>
    /// Adds a microphone to the library. It behaves like a camera item — one entry, cued from the
    /// dock, with its own level and output — but it carries sound only, so no video box ever fills.
    /// </summary>
    public MediaEntry AddMicrophone(int index, string? tag = null)
    {
        var path = MediaLibrary.MicrophonePath(index);
        var existing = Library.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Selected = existing;
            StateChanged?.Invoke();
            Status?.Invoke($"{existing.Label} is already in the library");
            return existing;
        }
        Library.Add(new MediaEntry(path, MediaLibrary.NormalizeTag(string.IsNullOrWhiteSpace(tag) ? "Microphone" : tag)));
        var entry = Library[^1];
        Selected = entry;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"MICROPHONE ADDED · {entry.Label}");
        return entry;
    }

    /// <summary>Every capture device the operator can add as a microphone, by Windows' name.</summary>
    public IReadOnlyList<MicrophoneDevice> MicrophoneDevices()
    {
        var found = MicrophoneFeed.NamedDevices().ToList();
        // A microphone that is already open cannot be probed, so the live one is always offered.
        if (_microphone is not null && !found.Any(device => device.Index == _microphone.Index))
            found.Add(new MicrophoneDevice(_microphone.Index, $"Microphone {_microphone.Index + 1}"));
        foreach (var entry in Library.Where(entry => entry.IsMicrophone))
            if (!found.Any(device => device.Index == entry.MicrophoneIndex))
                found.Add(new MicrophoneDevice(entry.MicrophoneIndex, $"Microphone {entry.MicrophoneIndex + 1}"));
        return found.DistinctBy(device => device.Index).OrderBy(device => device.Index).ToArray();
    }

    /// <summary>The capture devices a camera item can be routed to, with the names Windows gives
    /// them, for the routing menu.</summary>
    public IReadOnlyList<CameraDevice> CameraDevices()
    {
        var found = CameraFeed.NamedDevices().ToList();
        // A camera that is already open cannot be probed, so the live one is always offered.
        if (_camera is not null && !found.Any(device => device.Index == _camera.CameraIndex))
            found.Add(new CameraDevice(_camera.CameraIndex, $"Camera {_camera.CameraIndex + 1}"));
        foreach (var entry in Library.Where(entry => entry.IsCamera))
            if (!found.Any(device => device.Index == entry.CameraIndex))
                found.Add(new CameraDevice(entry.CameraIndex, $"Camera {entry.CameraIndex + 1}"));
        return found.DistinctBy(device => device.Index).OrderBy(device => device.Index).ToArray();
    }

    /// <summary>Renames a camera item; routing is by tag, so the name is purely for the operator.</summary>
    public void RenameCamera(MediaEntry entry, string? name)
    {
        entry = Resolve(entry);
        var updated = MediaLibrary.SetName(Library, entry, name);
        if (updated is null) return;
        if (string.Equals(Current?.Path, updated.Path, StringComparison.OrdinalIgnoreCase)) Current = updated;
        if (string.Equals(CameraEntry?.Path, updated.Path, StringComparison.OrdinalIgnoreCase)) CameraEntry = updated;
        Selected = updated;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"CAMERA · {updated.Label}");
    }

    /// <summary>
    /// Sends a camera item to a different capture device. If that item is the one on air, the old
    /// device is released and the new one goes live immediately, so the scorebug keeps showing.
    /// </summary>
    public void RouteCamera(MediaEntry entry, int device)
    {
        entry = Resolve(entry);
        var wasLive = Current?.IsCamera == true && string.Equals(Current.Path, entry.Path, StringComparison.OrdinalIgnoreCase);
        var updated = MediaLibrary.SetCameraDevice(Library, entry, device);
        if (updated is null) return;
        Selected = updated;
        Save();
        if (wasLive)
        {
            StopCamera();
            PlayCamera(updated, null, -1);
        }
        else
        {
            StateChanged?.Invoke();
            Status?.Invoke($"CAMERA · {updated.Label} → device {device + 1}");
        }
    }

    /// <summary>Renames a microphone item without changing which capture device it listens to.</summary>
    public void RenameMicrophone(MediaEntry entry, string? name)
    {
        entry = Resolve(entry);
        var updated = MediaLibrary.SetName(Library, entry, name);
        if (updated is null) return;
        if (SameClip(Current, updated)) Current = updated;
        if (SameClip(MicrophoneEntry, updated)) MicrophoneEntry = updated;
        Selected = updated;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"MICROPHONE · {updated.Label}");
    }

    /// <summary>
    /// Sends a microphone item to a different capture device. If that item is on air, the old device
    /// is released and the new one is opened immediately, so changing a commentary mic keeps it live.
    /// </summary>
    public void RouteMicrophone(MediaEntry entry, int device)
    {
        entry = Resolve(entry);
        var wasLive = Current?.IsMicrophone == true && SameClip(Current, entry);
        var updated = MediaLibrary.SetMicrophoneDevice(Library, entry, device);
        if (updated is null) return;
        Selected = updated;
        Save();
        if (wasLive)
        {
            StopMicrophone();
            PlayMicrophone(updated);
        }
        else
        {
            StateChanged?.Invoke();
            Status?.Invoke($"MICROPHONE · {updated.Label} → device {device + 1}");
        }
    }

    /// <summary>
    /// Cues a live camera. There is no file and no duration: the feed paints every scorebug video
    /// element watching this tag, and keeps recording the clips Replay scrubs through.
    /// </summary>
    void PlayCamera(MediaEntry entry, MediaPlaylist? playlist, int index)
    {
        // Other live sources stay on air. A camera cue adds this feed rather than replacing the
        // others, so several cameras and microphones can run together.
        try { _player.Stop(); _player.Close(); } catch { }
        _seekApply.Stop();
        _seekTarget = 0;
        _playlist = playlist;
        _playlistIndex = index;
        // The library selection is the operator's, not the cue's: taking it over made the tag,
        // loop and speaker in the dock jump back to defaults every time the camera went live.
        Current = entry;
        CameraEntry = entry;
        IsReplaying = false;
        _replayOnAir = false;
        _replayBuffer.Clear();
        _replayIndex = -1;
        ReplayBack = 0;
        ReplayDepth = 0;
        _duration = 0;
        _pendingSeek = null;
        _audio.Stop();
        _camera = CameraFeed.Get(entry.CameraIndex);
        _camera.Start();
        // A camera already on air is not added twice; a second entry on another device joins it.
        if (!IsCameraOnAir(entry)) _liveCameras.Add(entry);
        Playback.SetLiveCameras(_liveCameras);
        SetTransportPlaying(true);
        Playback.Start(entry);
        StateChanged?.Invoke();
        Status?.Invoke($"LIVE · {entry.Label} · tag {entry.Tag}");
    }

    /// <summary>
    /// Cuts to the camera's recording and opens a scrubber over the last two minutes. It starts a
    /// few seconds back, runs like any other clip (play, pause, scrub) and stays until Live is
    /// pressed or it catches up with the live edge.
    /// </summary>
    public bool BeginReplay()
    {
        if (_camera is null || CameraEntry is null)
        {
            Status?.Invoke("REPLAY · go live on a camera first");
            return false;
        }
        var segments = _camera.Snapshot();
        if (segments.Count == 0)
        {
            Status?.Invoke("REPLAY · nothing recorded yet — give the camera a few seconds");
            return false;
        }
        _replayBuffer.Clear();
        _replayBuffer.AddRange(segments);
        IsReplaying = true;
        _replayIndex = -1;
        _playlist = null;
        _playlistIndex = -1;
        ReplayDepth = Math.Min(CameraFeed.MaxReplaySeconds, (_replayBuffer[^1].EndsAt - _replayBuffer[0].StartedAt).TotalSeconds);
        // Replay opens frozen on the newest frame and stays paused: the operator drags back to the
        // start of the play, then presses Start replay to run it. Nothing runs on its own, and the
        // audience keeps watching the live camera until it is started.
        _playing = false;
        _replayOnAir = false;
        SeekReplay(0);
        StateChanged?.Invoke();
        Status?.Invoke($"REPLAY READY · in the preview only · drag the bar, then Start replay to go to air");
        return true;
    }

    /// <summary>Runs the replay from wherever the operator has scrubbed to.</summary>
    public bool PlayReplay()
    {
        if (!IsReplaying)
        {
            Status?.Invoke("START REPLAY · press Replay first");
            return false;
        }
        // Start replay puts the replay on air and does nothing else. A paused replay goes out as a
        // freeze frame and stays paused until the operator presses Play, so a scrub is never lost.
        if (!_replayOnAir)
        {
            _replayOnAir = true;
            Playback.PromoteStage();
        }
        StateChanged?.Invoke();
        Status?.Invoke(_playing
            ? $"REPLAY ON AIR · playing · {Math.Round(ReplayBack)}s behind live"
            : $"REPLAY ON AIR · paused · press Play to roll it · {Math.Round(ReplayBack)}s behind live");
        return true;
    }

    /// <summary>Moves the replay playhead to a point measured back from the live edge.</summary>
    public void SeekReplay(double secondsBack)
    {
        if (!IsReplaying || _replayBuffer.Count == 0) return;
        var back = Math.Clamp(double.IsFinite(secondsBack) ? secondsBack : 0, 0, ReplayDepth);
        var target = _replayBuffer[^1].EndsAt.AddSeconds(-back);
        var index = _replayBuffer.FindIndex(segment => target < segment.EndsAt);
        if (index < 0) index = _replayBuffer.Count - 1;
        var segment = _replayBuffer[index];
        var offset = Math.Clamp((target - segment.StartedAt).TotalSeconds, 0, Math.Max(0, segment.Seconds - 0.05));
        ReplayBack = back;
        OpenReplaySegment(index, offset, _playing);
        TransportChanged?.Invoke();
    }

    /// <summary>Opens one recorded clip at a point inside it and points the scorebug at it.</summary>
    void OpenReplaySegment(int index, double offset, bool play)
    {
        var segment = _replayBuffer[Math.Clamp(index, 0, _replayBuffer.Count - 1)];
        _replayIndex = index;
        var entry = new MediaEntry(segment.Path, CameraEntry?.Tag ?? MediaLibrary.DefaultTag);
        Current = entry;
        // The library selection stays where the operator left it: the replay is a synthetic cue,
        // and taking the selection over reset the tag, loop and speaker shown in the dock.
        _duration = segment.Seconds;
        _seekApply.Stop();
        _audio.Stop();
        _pendingSeek = offset;
        try
        {
            _player.Close();
            // A camera clip is recorded without sound, so the transport stays muted.
            _player.Volume = 0;
            _player.Open(new Uri(segment.Path));
            if (play) _player.Play();
        }
        catch { }
        // Before Start replay the cue is staged: only the previews pick it up.
        if (_replayOnAir) Playback.Start(entry);
        else Playback.Stage(entry);
        Playback.Seek(offset);
        // After the cue, so a paused scrub stays paused on the scorebug: Start() marks a new cue
        // as playing, which would otherwise restart the picture the operator just paused.
        SetTransportPlaying(play);
    }

    /// <summary>Moves to the next recorded clip as a replay runs out, or back to live at the end.</summary>
    void AdvanceReplay()
    {
        if (_replayIndex + 1 < _replayBuffer.Count)
        {
            OpenReplaySegment(_replayIndex + 1, 0, _playing);
            ReplayBack = Math.Max(0, (_replayBuffer[^1].EndsAt - _replayBuffer[_replayIndex].StartedAt).TotalSeconds);
        }
        else
        {
            ReturnToLive();
        }
    }

    /// <summary>Keeps the replay playhead honest as the clip plays, so the bar follows it.</summary>
    void TrackReplay()
    {
        if (!IsReplaying || _replayIndex < 0 || _replayIndex >= _replayBuffer.Count) return;
        var segment = _replayBuffer[_replayIndex];
        var at = segment.StartedAt.AddSeconds(Math.Clamp(Position, 0, segment.Seconds));
        ReplayBack = Math.Clamp((_replayBuffer[^1].EndsAt - at).TotalSeconds, 0, Math.Max(0, ReplayDepth));
    }

    /// <summary>
    /// Saves the replay from where the operator has scrubbed to, through to the live edge at the
    /// moment the button was pressed. The frames are re-encoded on a background thread.
    /// </summary>
    public bool SaveReplayClip(string path)
    {
        if (!IsReplaying || _replayBuffer.Count == 0)
        {
            Status?.Invoke("SAVE REPLAY · open Replay first");
            return false;
        }
        var live = _replayBuffer[^1].EndsAt;
        var from = live.AddSeconds(-ReplayBack);
        var segments = _replayBuffer.ToArray();
        var name = System.IO.Path.GetFileName(path);
        Status?.Invoke($"SAVING REPLAY · last {Stamp(ReplayBack)} → {name}");
        Task.Run(() =>
        {
            var ok = CameraFeed.Export(segments, from, live, path, out var error);
            _ui.BeginInvoke(() => Status?.Invoke(ok ? $"REPLAY SAVED · {name}" : $"REPLAY NOT SAVED · {error}"));
        });
        return true;
    }

    /// <summary>Cuts back to the live camera, at the live edge.</summary>
    public bool ReturnToLive()
    {
        if (CameraEntry is null) return false;
        var camera = CameraEntry;
        IsReplaying = false;
        _replayOnAir = false;
        _replayBuffer.Clear();
        _replayIndex = -1;
        ReplayBack = 0;
        ReplayDepth = 0;
        PlayCamera(camera, null, -1);
        StateChanged?.Invoke();
        Status?.Invoke($"LIVE · {camera.Label}");
        return true;
    }

    static string Stamp(double seconds) => $"{(int)(seconds / 60)}:{Math.Abs((int)seconds % 60):00}";

    void StopCamera()
    {
        if (_camera is null) return;
        // Only this camera leaves the air; a second feed keeps running for its own element.
        _liveCameras.RemoveAll(camera => camera.CameraIndex == _camera.CameraIndex);
        _camera.Stop();
        _camera = null;
        Playback.SetLiveCameras(_liveCameras);
    }

    /// <summary>The camera items on air, so the dock can show one as live rather than merely selected.</summary>
    public IReadOnlyList<MediaEntry> CamerasOnAir => _liveCameras;

    /// <summary>The microphone items on air, one per capture device.</summary>
    public IReadOnlyList<MediaEntry> MicrophonesOnAir => _liveMicrophones;

    /// <summary>
    /// Takes every live camera and microphone off air at once. Cueing a file or stopping the show
    /// uses this; a single camera or mic is released by cueing its own item again or by Stop.
    /// </summary>
    public void StopLiveSources()
    {
        foreach (var camera in _liveCameras.Select(entry => entry.CameraIndex).Distinct().ToArray())
            try { CameraFeed.Get(camera).Stop(); } catch { }
        _liveCameras.Clear();
        _camera = null;
        foreach (var microphone in _liveMicrophones.Select(entry => entry.MicrophoneIndex).Distinct().ToArray())
            try { MicrophoneFeed.Get(microphone).Stop(); } catch { }
        _liveMicrophones.Clear();
        _microphone = null;
        MicrophoneEntry = null;
        Playback.SetLiveCameras(_liveCameras);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Cues a live microphone: the device is opened and its sound is monitored on the item's own
    /// output. There is no file, no picture and no timeline — the mic simply goes live until the
    /// operator stops it or cues something else, and the transport player is closed so a previous
    /// clip cannot keep playing underneath it.
    /// </summary>
    void PlayMicrophone(MediaEntry entry)
    {
        try { _player.Stop(); _player.Close(); } catch { }
        _seekApply.Stop();
        _seekTarget = 0;
        _playlist = null;
        _playlistIndex = -1;
        IsReplaying = false;
        _replayOnAir = false;
        _replayBuffer.Clear();
        _replayIndex = -1;
        ReplayBack = 0;
        ReplayDepth = 0;
        _duration = 0;
        _pendingSeek = null;
        _audio.Stop();
        Current = entry;
        MicrophoneEntry = entry;
        // Each microphone is its own capture and its own speaker, so adding one never takes another
        // off air. The same device cued twice is the same feed, not a second capture.
        _microphone = MicrophoneFeed.Get(entry.MicrophoneIndex);
        _microphone.SetVolume(entry.Volume);
        _microphone.Start(entry.AudioDevice);
        if (!IsMicrophoneOnAir(entry)) _liveMicrophones.Add(entry);
        // A microphone sends no picture; live cameras keep rendering, and the transport clears only
        // when there is no camera to show.
        if (_liveCameras.Count == 0) Playback.Stop(); else Playback.SetLiveCameras(_liveCameras);
        SetTransportPlaying(true);
        StateChanged?.Invoke();
        Status?.Invoke(_microphone.Error is { } failure
            ? $"MICROPHONE ERROR · {failure}"
            : $"LIVE · {entry.Label} → {entry.AudioLabel}");
    }

    void StopMicrophone()
    {
        if (_microphone is null) return;
        // Other microphones stay on air; each entry owns its own capture and speaker.
        _liveMicrophones.RemoveAll(entry => entry.MicrophoneIndex == _microphone.Index);
        _microphone.Stop();
        _microphone = null;
        MicrophoneEntry = null;
        StateChanged?.Invoke();
    }

    /// <summary>Keeps the cued clip pointing at the updated record after a tag/folder/loop edit.</summary>
    void ReplaceCurrent(MediaEntry before, MediaEntry after)
    {
        if (!ReferenceEquals(Current, before)) return;
        Current = after;
        if (!after.IsVideo) return;
        // Retagging or re-filing the clip that is on air must not restart it from frame one.
        var wasPlaying = Playback.IsPlaying;
        var position = Playback.Position;
        Playback.Start(after);
        if (position > 0) Playback.Seek(position);
        if (!wasPlaying) Playback.SetPlaying(false);
    }

    public void Select(MediaEntry entry)
    {
        Selected = entry;
        StateChanged?.Invoke();
        Status?.Invoke($"{entry.KindLabel} · {entry.Label} · tag {entry.Tag} · {entry.FolderLabel}");
    }

    /// <summary>Cues a clip: opens it, plays it and tells every video element on the scorebug.</summary>
    public void Play(MediaEntry entry) => PlayClip(entry, null, -1);

    void PlayClip(MediaEntry entry, MediaPlaylist? playlist, int index)
    {
        if (entry.IsCamera) { PlayCamera(entry, playlist, index); return; }
        if (entry.IsMicrophone) { PlayMicrophone(entry); return; }
        entry = Resolve(entry);
        IsReplaying = false;
        _replayBuffer.Clear();
        _replayIndex = -1;
        ReplayBack = 0;
        ReplayDepth = 0;
        if (!File.Exists(entry.Path))
        {
            Status?.Invoke("MEDIA FILE IS MISSING");
            return;
        }
        IsReplaying = false;
        _replayOnAir = false;
        // A clip sent to a speaker must not keep playing after the next clip is cued.
        _audio.Stop();
        _playlist = playlist;
        _playlistIndex = index;
        try { _player.Close(); } catch { }
        // A seek queued for the previous clip must not land on this one.
        _seekApply.Stop();
        _seekTarget = 0;
        Selected = entry;
        Current = entry;
        _duration = 0;
        _pendingSeek = null;
        // Video sound lives on the audience output; muting here avoids hearing it twice. A clip
        // routed to a chosen speaker is played by the audio router instead.
        _player.Volume = entry.IsVideo ? 0 : (float)Math.Clamp(entry.Volume, 0, 1);
        _player.Open(new Uri(entry.Path));
        _player.Play();
        SetTransportPlaying(true);
        // Audio-only clips must not fill a video overlay; videos cue the matching scorebug elements.
        if (entry.IsVideo) Playback.Start(entry); else Playback.Stop();
        // Only video is routed per speaker; an audio-only clip keeps the transport player. Routing
        // comes after Start() so its success/failure is not reset by the new cue.
        if (entry.IsVideo && !string.IsNullOrWhiteSpace(entry.AudioDevice))
        {
            _audio.Play(entry.Path, entry.AudioDevice, entry.Volume);
            Playback.ReportRouting(_audio.LastError is not null);
        }
        StateChanged?.Invoke();
        var where = playlist is null ? "" : $" · {playlist.Name} {index + 1}/{playlist.Count}";
        Status?.Invoke($"PLAYING · {(entry.IsVideo ? "VIDEO" : "AUDIO")} · {entry.Label} · tag {entry.Tag}{where}");
    }

    public void TogglePlayPause()
    {
        // A live microphone has no playhead: pause stops listening, play opens the device again.
        if (Current?.IsMicrophone == true)
        {
            if (_playing)
            {
                _microphone?.Stop();
                SetTransportPlaying(false);
                Status?.Invoke($"MICROPHONE PAUSED · {Current.Label}");
            }
            else
            {
                _microphone ??= MicrophoneFeed.Get(Current.MicrophoneIndex);
                _microphone.SetVolume(Current.Volume);
                _microphone.Start(Current.AudioDevice);
                SetTransportPlaying(true);
                Status?.Invoke($"LIVE · {Current.Label} → {Current.AudioLabel}");
            }
            return;
        }
        // A replay is paused and resumed like any clip, but never cued again from the top.
        if (IsReplaying)
        {
            if (_playing)
            {
                try { _player.Pause(); } catch { }
                SetTransportPlaying(false);
                Status?.Invoke($"REPLAY PAUSED · {Math.Round(ReplayBack)}s behind live");
            }
            // Play resumes a paused replay where it was scrubbed to. If it has not gone to air
            // yet this also promotes it, so one press both starts it and rolls it.
            else
            {
                if (!_replayOnAir) PlayReplay();
                try { _player.Play(); } catch { }
                SetTransportPlaying(true);
                Status?.Invoke($"REPLAY ON AIR · playing · {Math.Round(ReplayBack)}s behind live");
            }
            return;
        }
        if (_playing)
        {
            try { _player.Pause(); } catch { }
            _audio.Pause();
            SetTransportPlaying(false);
            Status?.Invoke($"PAUSED · {Current?.Label ?? "clip"} · scorebug video holds its frame");
            return;
        }
        if (Selected is not null && !ReferenceEquals(Selected, Current)) { Play(Selected); return; }
        if (Current is null) { Status?.Invoke("SELECT A CLIP FIRST"); return; }
        // A video's sound comes from the scorebug element, never from this muted transport player.
        _player.Volume = Current.IsVideo ? 0 : (float)Math.Clamp(Current.Volume, 0, 1);
        try { _player.Play(); } catch { }
        _audio.Resume();
        SetTransportPlaying(true);
        if (Current.IsVideo) Playback.Start(Current);
        Status?.Invoke($"PLAYING · {(Current.IsVideo ? "VIDEO" : "AUDIO")} · {Current.Label}");
    }

    void SetTransportPlaying(bool playing)
    {
        _playing = playing;
        Playback.SetPlaying(playing);
        StateChanged?.Invoke();
        TransportChanged?.Invoke();
    }

    /// <summary>What happens when the current clip reaches its end.</summary>
    void HandleClipEnded()
    {
        if (Current is null)
        {
            SetTransportPlaying(false);
            return;
        }
        // A replay walks forward through the recorded clips and only cuts back to live at the end,
        // so a paused replay is never yanked away from the operator.
        if (IsReplaying)
        {
            AdvanceReplay();
            return;
        }
        // A playlist owns the order: clips run one after another whether or not they loop alone.
        if (_playlist is not null)
        {
            var next = _playlistIndex + 1;
            if (next < _playlist.Count) { PlayPlaylistIndex(next); return; }
            if (_playlist.Loop) { PlayPlaylistIndex(0); return; }
            Status?.Invoke($"PLAYLIST ENDED · {_playlist.Name}");
            Stop(keepStatus: true);
            return;
        }
        if (Current.Loop)
        {
            try
            {
                _player.Position = TimeSpan.Zero;
                _player.Play();
                // A routed clip loops its speaker output as well as its picture.
                if (!string.IsNullOrWhiteSpace(Current.AudioDevice))
                    _audio.Play(Current.Path, Current.AudioDevice, Current.Volume);
            }
            catch { SetTransportPlaying(false); return; }
            Playback.Seek(0);
            SetTransportPlaying(true);
            return;
        }
        Status?.Invoke($"CLIP ENDED · {Current.Label}");
        Stop(keepStatus: true);
    }

    void PlayPlaylistIndex(int index)
    {
        if (_playlist is null || _playlist.Count == 0) { Stop(); return; }
        // Skip over clips that have been removed from the library.
        for (var step = 0; step < _playlist.Count; step++)
        {
            var probe = (index + step) % _playlist.Count;
            var path = _playlist.Paths[probe];
            var entry = Library.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
            if (entry is not null) { PlayClip(entry, _playlist, probe); return; }
        }
        Status?.Invoke($"PLAYLIST IS EMPTY · {_playlist.Name}");
        Stop(keepStatus: true);
    }

    // ---------- playlists ----------

    public MediaPlaylist CreatePlaylist(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) trimmed = $"Playlist {Playlists.Count + 1}";
        var existing = Playlists.FirstOrDefault(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Status?.Invoke($"PLAYLIST EXISTS · {existing.Name}");
            return existing;
        }
        var playlist = new MediaPlaylist { Name = trimmed };
        Playlists.Add(playlist);
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"PLAYLIST CREATED · {playlist.Name}");
        return playlist;
    }

    public void DeletePlaylist(MediaPlaylist playlist)
    {
        if (ReferenceEquals(_playlist, playlist)) Stop(keepStatus: true);
        Playlists.Remove(playlist);
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"PLAYLIST REMOVED · {playlist.Name}");
    }

    public void AddToPlaylist(MediaPlaylist playlist, MediaEntry entry)
    {
        if (playlist.Paths.Any(p => string.Equals(p, entry.Path, StringComparison.OrdinalIgnoreCase)))
        {
            Status?.Invoke($"ALREADY IN {playlist.Name.ToUpperInvariant()} · {entry.Label}");
            return;
        }
        playlist.Paths.Add(entry.Path);
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"{playlist.Name.ToUpperInvariant()} · + {entry.Label} ({playlist.Count})");
    }

    public void RemoveFromPlaylist(MediaPlaylist playlist, MediaEntry entry)
    {
        var removed = playlist.Paths.RemoveAll(p => string.Equals(p, entry.Path, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"{playlist.Name.ToUpperInvariant()} · − {entry.Label}");
    }

    /// <summary>The playlist's clips in play order, skipping files that have left the library.</summary>
    public IReadOnlyList<MediaEntry> PlaylistEntries(MediaPlaylist playlist) =>
        playlist.Paths.Select(path => Library.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase)))
            .Where(entry => entry is not null).Select(entry => entry!).ToArray();

    public void ClearPlaylist(MediaPlaylist playlist)
    {
        playlist.Paths.Clear();
        Save();
        StateChanged?.Invoke();
    }

    public void SetPlaylistLoop(MediaPlaylist playlist, bool loop)
    {
        playlist.Loop = loop;
        Save();
        StateChanged?.Invoke();
        Status?.Invoke($"{playlist.Name.ToUpperInvariant()} · LOOP {(loop ? "ON" : "OFF")}");
    }

    /// <summary>Starts the playlist at one of its clips, used by double-clicking in an open list.</summary>
    public void PlayPlaylistFrom(MediaPlaylist playlist, int index)
    {
        if (playlist.Count == 0)
        {
            Status?.Invoke($"PLAYLIST IS EMPTY · {playlist.Name}");
            return;
        }
        _playlist = playlist;
        PlayPlaylistIndex(Math.Clamp(index, 0, playlist.Count - 1));
    }

    /// <summary>Starts the playlist from its first clip.</summary>
    public void PlayPlaylist(MediaPlaylist playlist)
    {
        if (playlist.Count == 0)
        {
            Status?.Invoke($"PLAYLIST IS EMPTY · {playlist.Name}");
            return;
        }
        _playlist = playlist;
        PlayPlaylistIndex(0);
    }

    /// <summary>Skips to the next clip, inside a playlist or through the library.</summary>
    public void NextClip()
    {
        if (_playlist is not null) { PlayPlaylistIndex((_playlistIndex + 1) % _playlist.Count); return; }
        var items = VisibleLibrary;
        if (items.Count == 0) return;
        var index = Current is null ? -1 : items.ToList().FindIndex(m => string.Equals(m.Path, Current.Path, StringComparison.OrdinalIgnoreCase));
        Play(items[(index + 1 + items.Count) % items.Count]);
    }

    public    void Stop(bool keepStatus = false)
    {
        try { _player.Stop(); _player.Close(); } catch { }
        StopCamera();
        StopMicrophone();
        _audio.Stop();
        IsReplaying = false;
        _replayOnAir = false;
        _replayBuffer.Clear();
        _replayIndex = -1;
        ReplayBack = 0;
        ReplayDepth = 0;
        _seekApply.Stop();
        _seekTarget = 0;
        Current = null;
        _duration = 0;
        _pendingSeek = null;
        _playlist = null;
        _playlistIndex = -1;
        SetTransportPlaying(false);
        _player.Volume = 1;
        Playback.Stop();
        StateChanged?.Invoke();
        if (!keepStatus) Status?.Invoke("MEDIA STOPPED");
    }

    public void Save()
    {
        _preferences.Media = Library.ToList();
        _preferences.Playlists = Playlists.Select(p => new MediaPlaylist { Name = p.Name, Loop = p.Loop, Paths = p.Paths.ToList() }).ToList();
        _preferences.MediaFolders = _folders.ToList();
        try { PreferencesStore.Save(_preferences); } catch { }
    }

    // ---------- thumbnails ----------

    /// <summary>
    /// Grabs a frame a moment after the decoder opens the file. A MediaPlayer has no picture at
    /// MediaOpened yet, which is why a naive grab produced blank tiles; this seeks in, plays briefly,
    /// then renders. Failures (missing codec) fall back to the tile glyph.
    /// </summary>
    void QueueThumbnail(MediaEntry entry)
    {
        if (!entry.IsVideo || _thumbs.ContainsKey(entry.Path) || _pendingThumbs.Contains(entry.Path)) return;
        if (!File.Exists(entry.Path)) return;
        var attempt = _thumbAttempts.TryGetValue(entry.Path, out var count) ? count : 0;
        if (attempt >= 2) return;
        _thumbAttempts[entry.Path] = attempt + 1;
        _pendingThumbs.Add(entry.Path);

        var player = new MediaPlayer { Volume = 0, ScrubbingEnabled = true };
        var finished = false;
        var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };

        void Finish(bool captured)
        {
            if (finished) return;
            finished = true;
            delay.Stop();
            guard.Stop();
            try
            {
                if (captured)
                {
                    const int width = 240, height = 135;
                    var visual = new DrawingVisual();
                    using (var context = visual.RenderOpen()) context.DrawVideo(player, new Rect(0, 0, width, height));
                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    bitmap.Freeze();
                    if (IsUsable(bitmap))
                    {
                        _thumbs[entry.Path] = bitmap;
                        ThumbnailReady?.Invoke(entry.Path);
                    }
                    else if (attempt + 1 < 2 && !_thumbs.ContainsKey(entry.Path))
                    {
                        _pendingThumbs.Remove(entry.Path);
                        QueueThumbnail(entry);
                    }
                }
            }
            catch { }
            finally
            {
                _pendingThumbs.Remove(entry.Path);
                try { player.Stop(); player.Close(); } catch { }
            }
        }

        player.MediaOpened += (_, _) =>
        {
            try
            {
                var total = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan : TimeSpan.FromSeconds(3);
                var at = total > TimeSpan.FromSeconds(1.5) ? TimeSpan.FromSeconds(Math.Min(2, total.TotalSeconds / 3)) : TimeSpan.Zero;
                player.Position = at;
                player.Play();
                player.Pause();
                player.Play();
                delay.Start();
            }
            catch { Finish(false); }
        };
        delay.Tick += (_, _) => { delay.Stop(); player.Pause(); Finish(true); };
        guard.Tick += (_, _) => Finish(false);
        player.MediaFailed += (_, _) => Finish(false);

        guard.Start();
        try { player.Open(new Uri(entry.Path)); } catch { Finish(false); }
    }

    /// <summary>A frame that is one flat colour (usually black) means the decoder had nothing yet.</summary>
    static bool IsUsable(BitmapSource bitmap)
    {
        try
        {
            const int sample = 16;
            var stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);
            var first = (pixels[0], pixels[1], pixels[2]);
            for (var y = 0; y < bitmap.PixelHeight; y += bitmap.PixelHeight / 4 + 1)
                for (var x = 0; x < bitmap.PixelWidth; x += sample)
                {
                    var i = y * stride + x * 4;
                    if (Math.Abs(pixels[i] - first.Item1) > 8 || Math.Abs(pixels[i + 1] - first.Item2) > 8 || Math.Abs(pixels[i + 2] - first.Item3) > 8)
                        return true;
                }
            return false;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        _transport.Stop();
        _seekApply.Stop();
        StopCamera();
        StopMicrophone();
        StopLiveSources();
        _audio.Dispose();
        try { _player.Close(); } catch { }
    }
}
