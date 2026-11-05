using System.IO;

namespace Scoreboard.Desktop;

/// <summary>
/// One clip in the operator's media library. <see cref="Tag"/> is the scorebug routing bucket
/// (a video element only shows clips carrying its tag) and <see cref="Folder"/> is purely for
/// organising the library. <see cref="Loop"/> restarts the clip when it ends.
/// <see cref="Volume"/> and <see cref="AudioDevice"/> are per-clip audio, so an anthem can play
/// quietly on the arena speakers while an ad plays at full level on the broadcast feed.
/// A live camera is simply another media entry (its path is <c>camera:0</c>), so it routes by tag
/// and shows in a scorebug video element exactly like a file does.
/// </summary>
public sealed record MediaEntry(
    string Path,
    string Tag = MediaLibrary.DefaultTag,
    string Folder = "",
    bool Loop = false,
    double Volume = 1,
    string? AudioDevice = null,
    string? Name = null,
    int? CameraDevice = null,
    int? MicrophoneDevice = null,
    bool SpeakerMuted = false)
{
    /// <summary>A camera or microphone with no name of its own is labelled with the device it uses,
    /// so the dock says "icspring camera" rather than "Live camera 1".</summary>
    public string Label => !string.IsNullOrWhiteSpace(Name)
        ? Name.Trim()
        : IsCamera ? CameraFeed.CameraName(CameraIndex)
        : IsMicrophone ? MicrophoneFeed.MicrophoneName(MicrophoneIndex)
        : IsSpeaker ? $"Speaker {(MediaLibrary.SpeakerIndex(Path) ?? 0) + 1}"
        : System.IO.Path.GetFileName(Path);
    public bool IsCamera => MediaLibrary.CameraIndex(Path) is not null;
    /// <summary>A live microphone item: captured from a device, with no file and no picture.</summary>
    public bool IsMicrophone => MediaLibrary.MicrophoneIndex(Path) is not null;
    /// <summary>An output-bus item: it names a speaker and carries that bus's level.</summary>
    public bool IsSpeaker => MediaLibrary.SpeakerIndex(Path) is not null;
    /// <summary>Which capture device this camera item routes to; an override beats the path's index.</summary>
    public int CameraIndex => CameraDevice ?? MediaLibrary.CameraIndex(Path) ?? 0;
    /// <summary>Which microphone this item listens to.</summary>
    public int MicrophoneIndex => MicrophoneDevice ?? MediaLibrary.MicrophoneIndex(Path) ?? 0;
    public bool IsVideo => IsCamera || (!IsMicrophone && MediaLibrary.IsVideoFile(Path));
    public string KindLabel => IsCamera ? "LIVE" : IsMicrophone ? "MIC" : IsSpeaker ? "SPEAKER" : IsVideo ? "VIDEO" : "AUDIO";
    public string FolderLabel => string.IsNullOrWhiteSpace(Folder) ? MediaLibrary.RootFolderLabel : Folder;
    public string TagAndFolder => string.IsNullOrWhiteSpace(Folder) ? Tag : $"{Tag} · {Folder}";

    /// <summary>0–100, as shown on the dock's knob.</summary>
    public int VolumePercent => (int)Math.Round(Math.Clamp(Volume, 0, 1) * 100);

    public string AudioLabel => string.IsNullOrWhiteSpace(AudioDevice) ? "Default output" : AudioDevice;

    /// <summary>A speaker bus is on air until it is muted from a quick-media button or its own bus control.</summary>
    public bool IsSpeakerOnAir => IsSpeaker && !SpeakerMuted;
}

/// <summary>A named, ordered set of clips that plays one after another.</summary>
public sealed class MediaPlaylist
{
    public string Name { get; set; } = "Playlist";
    public List<string> Paths { get; set; } = [];

    /// <summary>Start again at the first clip when the last one ends.</summary>
    public bool Loop { get; set; }

    public int Count => Paths.Count;
}

public static class MediaLibrary
{
    public const string DefaultTag = "General";

    /// <summary>A video element tagged ANY plays whatever is cued, whatever tag it carries.</summary>
    public const string AnyTag = "ANY";

    /// <summary>Clips with no folder still need a name in the filter list.</summary>
    public const string RootFolderLabel = "Unfiled";

    /// <summary>Where a camera source's path lives. <c>camera:0</c> is the first capture device.</summary>
    public const string CameraPrefix = "camera:";

    /// <summary>Where a microphone source's path lives. <c>mic:0</c> is the first capture device.</summary>
    public const string MicrophonePrefix = "mic:";

    /// <summary>Where an output-bus item's path lives. <c>speaker:0</c> is the first render device.</summary>
    public const string SpeakerPrefix = "speaker:";

    public static readonly string[] SuggestedTags = [AnyTag, "General", "Ads", "Highlights", "Music", "Anthem", "Sponsors", "Camera", "Microphone"];

    public static bool IsVideoFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" or ".m4v";

    public static string CameraPath(int index) => $"{CameraPrefix}{Math.Max(0, index)}";

    /// <summary>The capture device number behind a camera path, or null for a real file.</summary>
    public static int? CameraIndex(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(CameraPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        return int.TryParse(path[CameraPrefix.Length..], out var index) ? index : 0;
    }

    public static string MicrophonePath(int index) => $"{MicrophonePrefix}{Math.Max(0, index)}";

    public static string SpeakerPath(int index) => $"{SpeakerPrefix}{Math.Max(0, index)}";

    /// <summary>The render-device number behind a speaker path, or null for another media item.</summary>
    public static int? SpeakerIndex(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(SpeakerPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        return int.TryParse(path[SpeakerPrefix.Length..], out var index) ? index : 0;
    }

    /// <summary>The capture device number behind a microphone path, or null for a real file.</summary>
    public static int? MicrophoneIndex(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(MicrophonePrefix, StringComparison.OrdinalIgnoreCase)) return null;
        return int.TryParse(path[MicrophonePrefix.Length..], out var index) ? index : 0;
    }

    public static string NormalizeTag(string? tag)
    {
        var trimmed = (tag ?? "").Trim();
        return trimmed.Length == 0 ? DefaultTag : trimmed;
    }

    /// <summary>An empty folder means "unfiled"; anything else is a path like Ads/2026.</summary>
    public static string NormalizeFolder(string? folder) => (folder ?? "").Trim().Replace('\\', '/').Trim('/');

    /// <summary>Every folder in use, sorted, with the unfiled bucket last.</summary>
    public static IReadOnlyList<string> Folders(IEnumerable<MediaEntry> entries) =>
        entries.Select(e => NormalizeFolder(e.Folder)).Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Moves a clip into a tag, in place, and returns the updated entry.</summary>
    public static MediaEntry? Retag(IList<MediaEntry> library, MediaEntry selected, string? tag)
    {
        var index = library.IndexOf(selected);
        if (index < 0) return null;
        var updated = selected with { Tag = NormalizeTag(tag) };
        library[index] = updated;
        return updated;
    }

    /// <summary>Moves a clip into a folder, in place, and returns the updated entry.</summary>
    public static MediaEntry? MoveToFolder(IList<MediaEntry> library, MediaEntry selected, string? folder)
    {
        var index = library.IndexOf(selected);
        if (index < 0) return null;
        var updated = selected with { Folder = NormalizeFolder(folder) };
        library[index] = updated;
        return updated;
    }

    /// <summary>Renames a camera item, so a show can call it "Tight" or "Wide" rather than Camera 2.</summary>
    public static MediaEntry? SetName(IList<MediaEntry> library, MediaEntry selected, string? name)
    {
        var index = library.IndexOf(selected);
        if (index < 0) return null;
        var trimmed = (name ?? "").Trim();
        var updated = selected with { Name = trimmed.Length == 0 ? null : trimmed };
        library[index] = updated;
        return updated;
    }

    /// <summary>Points a camera item at a different capture device.</summary>
    public static MediaEntry? SetMicrophoneDevice(IList<MediaEntry> library, MediaEntry selected, int device)
    {
        var index = library.IndexOf(selected);
        if (index < 0) return null;
        var updated = selected with { MicrophoneDevice = Math.Max(0, device) };
        library[index] = updated;
        return updated;
    }

    public static MediaEntry? SetCameraDevice(IList<MediaEntry> library, MediaEntry selected, int device)
    {
        var index = library.IndexOf(selected);
        if (index < 0) return null;
        var updated = selected with { CameraDevice = Math.Max(0, device) };
        library[index] = updated;
        return updated;
    }

    /// <summary>Flips the clip's own loop flag, in place.</summary>
    public static MediaEntry? SetLoop(IList<MediaEntry> library, MediaEntry selected, bool loop)
    {
        var index = library.IndexOf(selected);
        if (index < 0) return null;
        var updated = selected with { Loop = loop };
        library[index] = updated;
        return updated;
    }

    /// <summary>Sets this clip's own output level, in place. 0–1.</summary>
    public static MediaEntry? SetVolume(IList<MediaEntry> library, MediaEntry selected, double volume)
    {
        var index = library.IndexOf(selected);
        if (index < 0) return null;
        var updated = selected with { Volume = Math.Clamp(double.IsFinite(volume) ? volume : 1, 0, 1) };
        library[index] = updated;
        return updated;
    }

    /// <summary>Sends this clip to one speaker (a WASAPI render device id), or back to the default.</summary>
    public static MediaEntry? SetAudioDevice(IList<MediaEntry> library, MediaEntry selected, string? device)
    {
        var index = library.IndexOf(selected);
        if (index < 0) return null;
        var updated = selected with { AudioDevice = string.IsNullOrWhiteSpace(device) ? null : device.Trim() };
        library[index] = updated;
        return updated;
    }

    /// <summary>Turns a speaker bus on or off without losing its level.</summary>
    public static MediaEntry? SetSpeakerMuted(IList<MediaEntry> library, MediaEntry selected, bool muted)
    {
        var index = library.IndexOf(selected);
        if (index < 0) return null;
        var updated = selected with { SpeakerMuted = muted };
        library[index] = updated;
        return updated;
    }

    /// <summary>True when a scorebug element with this tag should show this clip. ANY and blank match everything.</summary>
    public static bool TagMatches(string? elementTag, string clipTag)
    {
        if (string.IsNullOrWhiteSpace(elementTag)) return true;
        if (string.Equals(elementTag.Trim(), AnyTag, StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(NormalizeTag(clipTag), NormalizeTag(elementTag), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The clip currently cued to air. Scorebug video elements subscribe and only show it when their
/// own tag matches, so an ad element never displays the highlight reel and vice versa.
/// </summary>
public sealed class MediaPlayback
{
    public MediaEntry? NowPlaying { get; private set; }

    /// <summary>
    /// The decoded pictures of the clip being replayed, shared by every surface that shows it. A
    /// replay is frames, not a media file: the recorded clips are MJPG AVI, which a MediaElement
    /// renders as one still frame. Null only before the session attaches its decoder.
    /// </summary>
    public ReplayFeed? ReplayFrames { get; private set; }

    public void AttachReplay(ReplayFeed feed) => ReplayFrames = feed;

    // A show can carry several live cameras at once — wide, tight, bench — and each scorebug video
    // element picks the one whose tag it watches. Files still travel through NowPlaying.
    readonly List<MediaEntry> _liveCameras = [];

    public IReadOnlyList<MediaEntry> LiveCameras => _liveCameras;

    /// <summary>The live cameras on air, so a video element tagged for one still finds it.</summary>
    public void SetLiveCameras(IEnumerable<MediaEntry> cameras)
    {
        _liveCameras.Clear();
        _liveCameras.AddRange(cameras.Where(camera => camera.IsCamera));
        Changed?.Invoke();
    }

    // A replay the operator has opened but not started. Only previews see it; the audience keeps
    // the live cue until Start replay promotes it.
    MediaEntry? _staged;

    /// <summary>True while a replay is waiting in the preview, not yet on air.</summary>
    public bool HasStaged => _staged is not null;

    /// <summary>True once Start replay has promoted the staged replay. Only then does a file cue
    /// outrank a live camera; ordinary clips and several cameras can still coexist by tag.</summary>
    public bool ReplayOnAir { get; private set; }

    /// <summary>False while the operator has the clip paused, so scorebug video elements freeze with it.</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>Where the cued clip has been scrubbed to, in seconds. Video elements follow it.</summary>
    public double Position { get; private set; }

    /// <summary>Bumped on every explicit seek so an element can tell a seek from a play/pause.</summary>
    public int SeekVersion { get; private set; }

    /// <summary>The cued clip's own output level (0–1), so every screen plays it at the same volume.</summary>
    public double Volume => NowPlaying?.Volume ?? 1;

    /// <summary>
    /// The output this clip's sound is going to when it is not the Windows default: the clip's own
    /// speaker, or the speaker bus whose tag carries it. Empty means the element keeps the sound.
    /// </summary>
    public string? RouteDevice { get; private set; }

    /// <summary>
    /// True when the clip's audio is being sent to a chosen speaker instead of the element. A
    /// speaker that could not be opened reports false again, so the element keeps the sound.
    /// </summary>
    public bool RoutedToDevice => !string.IsNullOrWhiteSpace(RouteDevice) && !RoutingFailed;

    /// <summary>
    /// The level of the speaker bus that carries the cued clip, 0–1. Zero means no bus with this
    /// clip's tag is on air, so the clip is silent until that speaker is switched on.
    /// </summary>
    public double SpeakerGain { get; private set; } = 1;

    /// <summary>Re-reads where the cued clip plays and how loud, after a bus was switched or moved.</summary>
    public void ReportAudio(string? device, double gain)
    {
        gain = Math.Clamp(double.IsFinite(gain) ? gain : 1, 0, 1);
        var route = string.IsNullOrWhiteSpace(device) ? null : device;
        if (string.Equals(RouteDevice, route, StringComparison.OrdinalIgnoreCase) && Math.Abs(SpeakerGain - gain) < 0.0005) return;
        RouteDevice = route;
        SpeakerGain = gain;
        Changed?.Invoke();
    }

    /// <summary>True when the chosen speaker could not be opened and the clip fell back to the default.</summary>
    public bool RoutingFailed { get; private set; }

    /// <summary>The transport reports whether routing worked, so no clip ever goes silent.</summary>
    public void ReportRouting(bool failed)
    {
        if (RoutingFailed == failed) return;
        RoutingFailed = failed;
        Changed?.Invoke();
    }

    /// <summary>True while the cued source is a live camera rather than a file.</summary>
    public bool IsCamera => NowPlaying?.IsCamera == true;

    public event Action? Changed;

    public void Start(MediaEntry? entry)
    {
        // Any new cue replaces whatever replay was waiting in the preview.
        _staged = null;
        NowPlaying = entry;
        ReplayOnAir = false;
        IsPlaying = entry is not null;
        // A cue starts on its own speaker until the session says a tagged bus carries it.
        RouteDevice = string.IsNullOrWhiteSpace(entry?.AudioDevice) ? null : entry.AudioDevice;
        SpeakerGain = 1;
        Position = 0;
        // A surface that opens this cue must not resume at the last clip's position.
        LivePosition = 0;
        RoutingFailed = false;
        SeekVersion++;
        Changed?.Invoke();
    }

    /// <summary>Cues a replay for the preview only, leaving the audience on the live cue.</summary>
    public void Stage(MediaEntry? entry)
    {
        _staged = entry;
        ReplayOnAir = false;
        // The staged clip starts from the top until the operator seeks it; keeping the last clip's
        // playhead here would open the new file past its end.
        Position = 0;
        LivePosition = 0;
        Changed?.Invoke();
    }

    /// <summary>Puts the waiting replay on air, keeping the position the operator scrubbed to.</summary>
    public bool PromoteStage()
    {
        if (_staged is null) return false;
        NowPlaying = _staged;
        _staged = null;
        ReplayOnAir = true;
        RoutingFailed = false;
        // A replay is a recorded camera clip: it carries no library speaker of its own.
        RouteDevice = null;
        SpeakerGain = 1;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Moves every scorebug video element showing this clip to the same point.</summary>
    public void Seek(double seconds)
    {
        Position = Math.Max(0, seconds);
        SeekVersion++;
        Changed?.Invoke();
    }

    /// <summary>
    /// Where playback has actually reached, tracked by the transport. Deliberately separate from
    /// <see cref="Position"/> (the last explicit seek) so a rebuilt video surface can pick the clip
    /// up where it is without turning every tick into a seek.
    /// </summary>
    public double LivePosition { get; private set; }

    /// <summary>Notes where the transport actually is; never counts as a seek.</summary>
    public void ReportPosition(double seconds) => LivePosition = Math.Max(0, seconds);

    /// <summary>Re-reads the cued entry after its volume or speaker changed, without restarting it.</summary>
    public void RefreshAudio()
    {
        if (NowPlaying is null) return;
        Changed?.Invoke();
    }

    /// <summary>Swaps in an edited copy of the cued clip, keeping its position and play state.</summary>
    public void UpdateEntry(MediaEntry entry)
    {
        if (NowPlaying is null || !string.Equals(NowPlaying.Path, entry.Path, StringComparison.OrdinalIgnoreCase)) return;
        NowPlaying = entry;
        Changed?.Invoke();
    }

    public void Stop() => Start(null);

    /// <summary>Pauses or resumes every scorebug video element showing the cued clip.</summary>
    public void SetPlaying(bool playing)
    {
        if (NowPlaying is null || IsPlaying == playing) return;
        IsPlaying = playing;
        Changed?.Invoke();
    }

    public bool Matches(MediaEntry entry, string? elementTag) => MediaLibrary.TagMatches(elementTag, entry.Tag);

    /// <summary>The video this element should be showing right now, or null. Audio cues never fill a video box.</summary>
    /// <remarks>
    /// A live camera on air wins over the transport clip, so two video elements watching two tags can
    /// show two cameras at the same time. With no camera for this tag, the cued file is used.
    /// </remarks>
    public MediaEntry? ActiveFor(string? elementTag)
    {
        // A promoted replay must outrank the camera it was cut from, or Start replay leaves the live
        // picture on screen. Ordinary files keep the old order so several camera boxes can coexist.
        if (ReplayOnAir && NowPlaying is { IsVideo: true, IsCamera: false } replay && Matches(replay, elementTag))
            return replay;
        return Resolve(_liveCameras.FirstOrDefault(camera => Matches(camera, elementTag)) ?? NowPlaying, elementTag);
    }

    /// <summary>
    /// What a preview should show: a replay waiting to go on air takes precedence, so the operator
    /// can scrub it while the audience keeps watching the live camera.
    /// </summary>
    public MediaEntry? PreviewFor(string? elementTag)
    {
        if (_staged is not null) return Resolve(_staged, elementTag);
        // Once Start replay promotes the file, the preview must show it too; otherwise the operator
        // starts a replay and the thumbnail still shows the live camera, which reads as a dead button.
        if (ReplayOnAir && NowPlaying is { IsVideo: true, IsCamera: false } replay && Matches(replay, elementTag))
            return replay;
        return Resolve(_liveCameras.FirstOrDefault(camera => Matches(camera, elementTag)) ?? NowPlaying, elementTag);
    }

    MediaEntry? Resolve(MediaEntry? entry, string? elementTag)
    {
        if (entry is null || !entry.IsVideo) return null;
        return Matches(entry, elementTag) ? entry : null;
    }
}

/// <summary>
/// The manifest inside a ScorePlus Folder (.spf). A folder and everything under it leaves the
/// library as one zip: this manifest plus the clips themselves, so the folder can be handed to
/// another machine and come back exactly as it was — nested folders, tags, loops, levels, speakers
/// and camera items included.
/// </summary>
public sealed class MediaFolderManifest
{
    public const string FileName = "folder.json";

    public int Version { get; set; } = 1;

    /// <summary>The folder that was exported; empty for the unfiled bucket.</summary>
    public string Folder { get; set; } = "";

    /// <summary>Every folder in the export, parents included, so empty folders survive the trip.</summary>
    public List<string> Folders { get; set; } = [];

    public List<MediaFolderClip> Clips { get; set; } = [];
}

/// <summary>One clip in a folder export. <see cref="File"/> is its name inside the zip; a camera
/// item carries its <c>camera:N</c> path and no file at all.</summary>
public sealed class MediaFolderClip
{
    public string File { get; set; } = "";
    public string Path { get; set; } = "";
    public string Tag { get; set; } = MediaLibrary.DefaultTag;
    public string Folder { get; set; } = "";
    public bool Loop { get; set; }
    public double Volume { get; set; } = 1;
    public string? AudioDevice { get; set; }
    public string? Name { get; set; }
    public int? CameraDevice { get; set; }
    public int? MicrophoneDevice { get; set; }
}
