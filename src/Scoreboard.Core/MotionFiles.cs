using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scoreboard.Core;

/// <summary>
/// The two files the makers trade with other shows and other machines: a ScorePlus Transition
/// (<c>.spt</c>) holding one hand-over, and a ScorePlus Animation (<c>.spa</c>) holding one piece of
/// motion. Both are plain, indented JSON with every keyframe, colour key and phase in them, so a
/// layout, a display or a saved game carries its motion without needing anything else. A clip can
/// also be written and read on its own, which is what an export from a maker does.
/// </summary>
public static class MotionFiles
{
    public const string TransitionExtension = "spt";
    public const string AnimationExtension = "spa";
    public const int CurrentVersion = 1;

    public const string TransitionFilter = "ScorePlus Transition (*.spt)|*.spt|All files (*.*)|*.*";
    public const string AnimationFilter = "ScorePlus Animation (*.spa)|*.spa|All files (*.*)|*.*";

    static readonly JsonSerializerOptions Write = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    static readonly JsonSerializerOptions Read = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>What a .spt or .spa file holds: one clip, plus enough header to say what wrote it.</summary>
    public sealed class ClipFile
    {
        public string Format { get; set; } = "ScorePlus Transition";
        public int Version { get; set; } = CurrentVersion;
        public string Product { get; set; } = "ScorePlus";
        public string SavedBy { get; set; } = ProductVersion();
        public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
        public MotionClip? Clip { get; set; }
    }

    static string ProductVersion()
    {
        try { return typeof(MotionFiles).Assembly.GetName().Version?.ToString() ?? "1.0.0"; }
        catch { return "1.0.0"; }
    }

    public static string Filter(MotionKind kind) => kind == MotionKind.Transition ? TransitionFilter : AnimationFilter;

    public static string Extension(MotionKind kind) => kind == MotionKind.Transition ? TransitionExtension : AnimationExtension;

    public static string Serialize(MotionClip clip) => JsonSerializer.Serialize(new ClipFile
    {
        Format = clip.Kind == MotionKind.Transition ? "ScorePlus Transition" : "ScorePlus Animation",
        Clip = clip
    }, Write);

    /// <summary>
    /// Reads a clip file. A bare <see cref="MotionClip"/> — what a hand-written file looks like — is
    /// accepted too, so nothing has to be wrapped by hand.
    /// </summary>
    public static MotionClip? Parse(string json, MotionKind? expected = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        MotionClip? clip = null;
        try
        {
            var file = JsonSerializer.Deserialize<ClipFile>(json, Read);
            clip = file?.Clip;
        }
        catch { }
        if (clip is null)
        {
            try { clip = JsonSerializer.Deserialize<MotionClip>(json, Read); }
            catch { return null; }
        }
        if (clip?.Phases is null) return null;
        // A file saved as the other kind is refused rather than applied silently: a transition played
        // as an animation loops a hand-over, which is never what the operator meant.
        if (expected is MotionKind kind && clip.Kind != kind) clip.Kind = kind;
        clip.Normalize();
        clip.Id = Guid.NewGuid().ToString("N");
        return clip;
    }

    public static void Save(string path, MotionClip clip)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
        WriteAtomically(path, Serialize(clip));
    }

    public static MotionClip? Load(string path, MotionKind? expected = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            return Parse(File.ReadAllText(path), expected);
        }
        catch { return null; }
    }

    /// <summary>Writes beside the target first, so an interrupted save never leaves half a file.</summary>
    static void WriteAtomically(string path, string content)
    {
        var full = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        var temp = full + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, full, true);
    }
}
