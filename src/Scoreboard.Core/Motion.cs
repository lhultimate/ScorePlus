using System.Globalization;
using System.Text.Json;

namespace Scoreboard.Core;

/// <summary>What a clip is for: a hand-over between graphics, or motion a value follows.</summary>
public enum MotionKind { Transition, Animation }

/// <summary>
/// What a keyframe moves. X and Y are design pixels, ScaleX/ScaleY and Scale are factors,
/// Rotate and SkewX/SkewY are degrees, Opacity is 0–1 and Blur is a pixel radius. The extra
/// targets let a timeline change an item's box, text size or outline as well as its transform.
/// </summary>
public enum MotionTarget
{
    X, Y, Scale, ScaleX, ScaleY, Rotate, Opacity, Blur, SkewX, SkewY,
    Width, Height, FontSize, BorderThickness, TextOpacity, BackgroundOpacity, BorderOpacity
}

public enum MotionEase { Linear, In, Out, InOut, Back, Bounce, Elastic }

/// <summary>Which part of an element a colour keyframe paints.</summary>
public enum MotionColorRole { Text, Background, Border }

/// <summary>Which side a score-triggered take belongs to.</summary>
public enum MotionScoreSide { Any, Home, Away }

/// <summary>What kind of temporary art a timeline piece draws.</summary>
public enum MotionPieceKind { Text, Logo, Shape }

/// <summary>One keyframe on a numeric track: at <see cref="Time"/> seconds, the value is <see cref="Value"/>.</summary>
public sealed class MotionKey
{
    public double Time { get; set; }
    public double Value { get; set; }
    /// <summary>How the value approaches this key from the one before it.</summary>
    public MotionEase Ease { get; set; } = MotionEase.Out;

    public MotionKey() { }

    public MotionKey(double time, double value, MotionEase ease = MotionEase.Out)
    {
        Time = time;
        Value = value;
        Ease = ease;
    }

    public MotionKey Copy() => new(Time, Value, Ease);
}

/// <summary>Every keyframe for one property, in time order.</summary>
public sealed class MotionTrack
{
    public MotionTarget Target { get; set; }
    /// <summary>
    /// The piece this track drives: empty for the whole graphic, otherwise an element id or the id
    /// of a temporary piece that exists only while the transition runs.
    /// </summary>
    public string PieceId { get; set; } = "";
    public List<MotionKey> Keys { get; set; } = [];

    public MotionTrack() { }

    public MotionTrack(MotionTarget target, params (double Time, double Value)[] keys)
    {
        Target = target;
        foreach (var (time, value) in keys) Keys.Add(new MotionKey(time, value));
    }

    public double End => Keys.Count == 0 ? 0 : Keys.Max(key => key.Time);

    public MotionTrack Copy() => new()
    {
        Target = Target,
        PieceId = PieceId,
        Keys = Keys.Select(key => key.Copy()).ToList()
    };

    /// <summary>The value at a moment, so the editor can print what the preview is doing.</summary>
    public double? ValueAt(double time)
    {
        if (Keys.Count == 0) return null;
        var keys = Keys.OrderBy(key => key.Time).ToList();
        if (time <= keys[0].Time) return keys[0].Value;
        if (time >= keys[^1].Time) return keys[^1].Value;
        for (var index = 1; index < keys.Count; index++)
        {
            var to = keys[index];
            if (time > to.Time) continue;
            var from = keys[index - 1];
            var span = Math.Max(0.0001, to.Time - from.Time);
            var amount = (time - from.Time) / span;
            // The key's own easing shapes the approach into it, which is what the preview shows.
            amount = MotionCurves.Apply(amount, to.Ease);
            return from.Value + (to.Value - from.Value) * amount;
        }
        return keys[^1].Value;
    }

    public void Normalize(double seconds)
    {
        Keys ??= [];
        Keys = Keys.Take(64).Select(key => key ?? new MotionKey()).ToList();
        foreach (var key in Keys)
        {
            key.Time = Math.Clamp(Finite(key.Time), 0, Math.Max(0, seconds));
            key.Value = Math.Clamp(Finite(key.Value), -100000, 100000);
            if (!Enum.IsDefined(key.Ease)) key.Ease = MotionEase.Out;
        }
        // Two keys at the same instant make an impossible ramp; the later one wins.
        Keys = Keys.OrderBy(key => key.Time).GroupBy(key => Math.Round(key.Time, 3)).Select(group => group.Last()).ToList();
        if (Keys.Count == 0) Keys.Add(new MotionKey(0, 0));
    }

    static double Finite(double value) => double.IsFinite(value) ? value : 0;
}

/// <summary>A colour keyframe: at <see cref="Time"/> seconds, the role reaches <see cref="Color"/>.</summary>
public sealed class ColorKey
{
    public double Time { get; set; }
    public string Color { get; set; } = "#FFFFFFFF";

    public ColorKey() { }

    public ColorKey(double time, string color)
    {
        Time = time;
        Color = color;
    }

    public ColorKey Copy() => new(Time, Color);
}

/// <summary>Every colour keyframe for one role of an element, in time order.</summary>
public sealed class ColorTrack
{
    public MotionColorRole Role { get; set; }
    /// <summary>The piece this colour track paints; empty means the whole graphic.</summary>
    public string PieceId { get; set; } = "";
    public List<ColorKey> Keys { get; set; } = [];

    public ColorTrack() { }

    public ColorTrack(MotionColorRole role, params (double Time, string Color)[] keys)
    {
        Role = role;
        foreach (var (time, color) in keys) Keys.Add(new ColorKey(time, color));
    }

    public ColorTrack Copy() => new()
    {
        Role = Role,
        PieceId = PieceId,
        Keys = Keys.Select(key => key.Copy()).ToList()
    };

    /// <summary>The blended colour at a moment, as #AARRGGBB, or null when the track has no keys.</summary>
    public string? ColorAt(double time)
    {
        if (Keys.Count == 0) return null;
        var keys = Keys.OrderBy(key => key.Time).ToList();
        if (time <= keys[0].Time) return keys[0].Color;
        if (time >= keys[^1].Time) return keys[^1].Color;
        for (var index = 1; index < keys.Count; index++)
        {
            var to = keys[index];
            if (time > to.Time) continue;
            var from = keys[index - 1];
            var span = Math.Max(0.0001, to.Time - from.Time);
            var amount = Math.Clamp((time - from.Time) / span, 0, 1);
            return MotionColors.Mix(from.Color, to.Color, amount);
        }
        return keys[^1].Color;
    }

    public void Normalize(double seconds)
    {
        Keys ??= [];
        Keys = Keys.Take(64).Select(key => key ?? new ColorKey()).ToList();
        foreach (var key in Keys)
        {
            key.Time = Math.Clamp(double.IsFinite(key.Time) ? key.Time : 0, 0, Math.Max(0, seconds));
            if (!IsColor(key.Color)) key.Color = "#FFFFFFFF";
        }
        Keys = Keys.OrderBy(key => key.Time).GroupBy(key => Math.Round(key.Time, 3)).Select(group => group.Last()).ToList();
    }

    static bool IsColor(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length < 4 || text[0] != '#') return false;
        return text[1..].Length is 3 or 4 or 6 or 8 && text[1..].All(Uri.IsHexDigit);
    }
}

/// <summary>
/// A piece that exists only for the length of a hand-over — a flash card, a strap, a wipe bar.
/// It is stored inside the clip, not on the layout, and is drawn only while the transition runs.
/// </summary>
public sealed class MotionPiece
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Piece";
    public MotionPieceKind Kind { get; set; } = MotionPieceKind.Text;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 90;
    public string Fill { get; set; } = "#FF0B6B50";
    public string Border { get; set; } = "#FF38D7A3";
    public double BorderThickness { get; set; } = 2;
    public string Text { get; set; } = "";
    public string TextColor { get; set; } = "#FFFFFFFF";
    public double FontSize { get; set; } = 28;
    public double Opacity { get; set; } = 1;
    /// <summary>A logo piece follows this side's logo from the game, exactly like a logo element.</summary>
    public ScorebugLogoSide Logo { get; set; } = ScorebugLogoSide.None;
    /// <summary>A shape piece's silhouette.</summary>
    public ScorebugShape Shape { get; set; } = ScorebugShape.Rectangle;
    /// <summary>Only show this piece for a score of this size. 0 means every score.</summary>
    public int TriggerAmount { get; set; }
    public MotionScoreSide TriggerSide { get; set; }

    public MotionPiece Copy() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Fill = Fill,
        Border = Border,
        BorderThickness = BorderThickness,
        Text = Text,
        TextColor = TextColor,
        FontSize = FontSize,
        Opacity = Opacity,
        Logo = Logo,
        Shape = Shape,
        TriggerAmount = TriggerAmount,
        TriggerSide = TriggerSide
    };

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(Name)) Name = "Piece";
        if (!Enum.IsDefined(Kind)) Kind = MotionPieceKind.Text;
        if (!Enum.IsDefined(Logo)) Logo = ScorebugLogoSide.None;
        if (!Enum.IsDefined(Shape)) Shape = ScorebugShape.Rectangle;
        X = Math.Clamp(double.IsFinite(X) ? X : 0, -7680, 7680);
        Y = Math.Clamp(double.IsFinite(Y) ? Y : 0, -7680, 7680);
        Width = Math.Clamp(double.IsFinite(Width) ? Width : 240, 4, 7680);
        Height = Math.Clamp(double.IsFinite(Height) ? Height : 90, 4, 7680);
        BorderThickness = Math.Clamp(double.IsFinite(BorderThickness) ? BorderThickness : 2, 0, 40);
        FontSize = Math.Clamp(double.IsFinite(FontSize) ? FontSize : 28, 6, 480);
        Opacity = Math.Clamp(double.IsFinite(Opacity) ? Opacity : 1, 0, 1);
        Fill = string.IsNullOrWhiteSpace(Fill) ? "#FF0B6B50" : Fill;
        Border = string.IsNullOrWhiteSpace(Border) ? "#FF38D7A3" : Border;
        TextColor = string.IsNullOrWhiteSpace(TextColor) ? "#FFFFFFFF" : TextColor;
        TriggerAmount = Math.Clamp(TriggerAmount, 0, 999);
        if (!Enum.IsDefined(TriggerSide)) TriggerSide = MotionScoreSide.Any;
        Name = Name.Trim();
    }

    public bool MatchesTrigger(int amount, bool home) =>
        (TriggerAmount == 0 || TriggerAmount == Math.Abs(amount))
        && (TriggerSide == MotionScoreSide.Any || TriggerSide == (home ? MotionScoreSide.Home : MotionScoreSide.Away));
}

/// <summary>
/// One leg of a clip: the way a graphic leaves ("Exit"), the way it arrives ("Enter"), the loop it
/// runs while it is up ("Motion"), or the flourish a new value plays ("Value").
/// </summary>
public sealed class MotionPhase
{
    public string Name { get; set; } = "Enter";
    public bool Enabled { get; set; } = true;
    public double Seconds { get; set; } = 0.4;
    /// <summary>
    /// Animations only: this take plays when a score of this size is pressed. 0 is the normal take.
    /// A clip can carry a +3 take, a +6 take and a normal fallback in one file.
    /// </summary>
    public int TriggerAmount { get; set; }
    public MotionScoreSide TriggerSide { get; set; }
    public List<MotionTrack> Tracks { get; set; } = [];
    public List<ColorTrack> Colors { get; set; } = [];

    public MotionPhase() { }

    public MotionPhase(string name, double seconds)
    {
        Name = name;
        Seconds = seconds;
    }

    public bool IsEmpty => Tracks.Count == 0 && Colors.Count == 0;

    public bool IsTriggered => TriggerAmount > 0 || TriggerSide != MotionScoreSide.Any;

    public bool MatchesTrigger(int amount, bool home) =>
        (TriggerAmount == 0 || TriggerAmount == Math.Abs(amount))
        && (TriggerSide == MotionScoreSide.Any || TriggerSide == (home ? MotionScoreSide.Home : MotionScoreSide.Away));

    public string TriggerLabel => TriggerAmount > 0
        ? $"{TriggerSide switch { MotionScoreSide.Home => "Home ", MotionScoreSide.Away => "Away ", _ => "" }}+{TriggerAmount}"
        : TriggerSide == MotionScoreSide.Any ? "Any score" : $"{TriggerSide} score";

    public MotionPhase Copy() => new()
    {
        Name = Name,
        Enabled = Enabled,
        Seconds = Seconds,
        TriggerAmount = TriggerAmount,
        TriggerSide = TriggerSide,
        Tracks = Tracks.Select(track => track.Copy()).ToList(),
        Colors = Colors.Select(track => track.Copy()).ToList()
    };

    /// <summary>Adds a track for a property on one piece, or returns the one that already exists.</summary>
    public MotionTrack Track(MotionTarget target, string pieceId = "")
    {
        var track = Tracks.FirstOrDefault(candidate => candidate.Target == target
            && string.Equals(candidate.PieceId ?? "", pieceId ?? "", StringComparison.OrdinalIgnoreCase));
        if (track is not null) return track;
        track = new MotionTrack { Target = target, PieceId = pieceId ?? "" };
        Tracks.Add(track);
        return track;
    }

    public ColorTrack Color(MotionColorRole role, string pieceId = "")
    {
        var track = Colors.FirstOrDefault(candidate => candidate.Role == role
            && string.Equals(candidate.PieceId ?? "", pieceId ?? "", StringComparison.OrdinalIgnoreCase));
        if (track is not null) return track;
        track = new ColorTrack { Role = role, PieceId = pieceId ?? "" };
        Colors.Add(track);
        return track;
    }

    public void Normalize()
    {
        Tracks ??= [];
        Colors ??= [];
        Seconds = Math.Clamp(double.IsFinite(Seconds) ? Seconds : 0.4, 0, 30);
        TriggerAmount = Math.Clamp(TriggerAmount, 0, 999);
        if (!Enum.IsDefined(TriggerSide)) TriggerSide = MotionScoreSide.Any;
        if (string.IsNullOrWhiteSpace(Name)) Name = "Phase";
        Tracks = Tracks.Take(24).Where(track => track is not null).ToList();
        foreach (var track in Tracks)
        {
            track.PieceId = (track.PieceId ?? "").Trim();
            if (!Enum.IsDefined(track.Target)) track.Target = MotionTarget.Opacity;
            track.Normalize(Seconds);
        }
        // One track per property on each piece: two would fight over the same transform.
        Tracks = Tracks.GroupBy(track => (track.Target, track.PieceId)).Select(group => group.First()).ToList();
        Colors = Colors.Take(12).Where(track => track is not null).ToList();
        foreach (var track in Colors)
        {
            track.PieceId = (track.PieceId ?? "").Trim();
            if (!Enum.IsDefined(track.Role)) track.Role = MotionColorRole.Text;
            track.Normalize(Seconds);
        }
        Colors = Colors.GroupBy(track => (track.Role, track.PieceId)).Select(group => group.First()).ToList();
    }
}

/// <summary>
/// A saved transition (<c>.spt</c>) or animation (<c>.spa</c>): the phases a graphic runs through,
/// the keyframes on every property in each phase, and the colour keys that travel with them.
/// It is stored on the element or layout that uses it, so saving a layout or a game saves it too,
/// and it is written on its own when the operator exports it from a maker.
/// </summary>
public sealed class MotionClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled";
    public MotionKind Kind { get; set; } = MotionKind.Transition;
    public string Notes { get; set; } = "";
    /// <summary>Animations: keep running for as long as the graphic is up.</summary>
    public bool Loop { get; set; }
    /// <summary>Animations: run back the other way instead of jumping to the first key.</summary>
    public bool PingPong { get; set; }
    /// <summary>Animations: play this when the element's value changes, rather than all the time.</summary>
    public bool UseOnValueChange { get; set; }
    /// <summary>The layout this timeline was authored against; the editor reopens on it.</summary>
    public string TimelineLayoutId { get; set; } = "";
    /// <summary>How far apart ordered items start inside one leg of the timeline, in seconds.</summary>
    public double ItemStagger { get; set; } = 0.08;
    public List<MotionPhase> Phases { get; set; } = [];

    /// <summary>Text, logos and shapes that exist only while this clip runs.</summary>
    public List<MotionPiece> Pieces { get; set; } = [];

    /// <summary>
    /// Transitions: the order the layout's items leave and arrive in. Item ids not listed here run
    /// with the phase, so a clip saved before the timeline existed plays exactly as it used to.
    /// </summary>
    public List<string> PieceOrder { get; set; } = [];

    /// <summary>A short line for lists: how many phases, tracks and colour keys the clip holds.</summary>
    public string Summary
    {
        get
        {
            var tracks = Phases.Sum(phase => phase.Tracks.Count);
            var colours = Phases.Sum(phase => phase.Colors.Sum(track => track.Keys.Count));
            var pieces = Pieces.Count > 0 ? " · " + Pieces.Count + (Pieces.Count == 1 ? " piece" : " pieces") : "";
            return $"{Phases.Count} phase{(Phases.Count == 1 ? "" : "s")} · {tracks} track{(tracks == 1 ? "" : "s")} · {colours} color key{(colours == 1 ? "" : "s")} · {TotalSeconds:0.##}s{pieces}";
        }
    }

    public double TotalSeconds => Phases.Where(phase => phase.Enabled).Sum(phase => Math.Max(0, phase.Seconds));

    public MotionPhase? Phase(string name) =>
        Phases.FirstOrDefault(phase => string.Equals(phase.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The phase that plays as the graphic arrives, or the first enabled one.</summary>
    public MotionPhase? EnterPhase => Phase("Enter") ?? Phases.FirstOrDefault(phase => phase.Enabled);

    /// <summary>The phase that plays as the graphic leaves; transitions only.</summary>
    public MotionPhase? ExitPhase => Phase("Exit");

    /// <summary>
    /// The take a value change should play. A triggered take wins for the exact score and side;
    /// otherwise the untriggered take is the fallback. This is what keeps a +3 pop-up off a +6.
    /// </summary>
    public MotionPhase? ValuePhaseFor(int amount, bool home)
    {
        var takes = Phases.Where(phase => phase.Enabled && !phase.IsEmpty).ToList();
        if (amount != 0)
        {
            var exact = takes.FirstOrDefault(phase => phase.TriggerAmount > 0
                && phase.TriggerAmount == Math.Abs(amount) && phase.MatchesTrigger(amount, home));
            if (exact is not null) return exact;
        }
        return takes.FirstOrDefault(phase => !phase.IsTriggered) ?? takes.FirstOrDefault();
    }

    public MotionPhase? ValuePhaseFor(GameState? state) =>
        ValuePhaseFor(state?.LastScoreAmount ?? 0, state?.LastScoreHome ?? true);

    /// <summary>Pieces that belong in a score-triggered take. Untriggered pieces appear in every take.</summary>
    public List<MotionPiece> PiecesForTrigger(int amount, bool home) =>
        Pieces.Where(piece => piece.MatchesTrigger(amount, home)).ToList();

    public bool IsEmpty => (Phases.Count == 0 || Phases.All(phase => phase.IsEmpty)) && Pieces.Count == 0;

    public MotionClip Copy(string? name = null)
    {
        var json = JsonSerializer.Serialize(this);
        var copy = JsonSerializer.Deserialize<MotionClip>(json) ?? new MotionClip();
        copy.Id = Guid.NewGuid().ToString("N");
        if (!string.IsNullOrWhiteSpace(name)) copy.Name = name.Trim();
        copy.Normalize();
        return copy;
    }

    /// <summary>
    /// A deep copy that keeps this clip's identity. A library clip and the copies embedded on a
    /// layout or element share an id, so a dropdown recognises the clip that is in use and saving
    /// it again updates the same entry instead of adding a duplicate.
    /// </summary>
    public MotionClip Snapshot()
    {
        var copy = Copy();
        copy.Id = Id;
        return copy;
    }

    public void Normalize()
    {
        Phases ??= [];
        Phases = Phases.Take(8).Where(phase => phase is not null).ToList();
        foreach (var phase in Phases) phase.Normalize();
        if (string.IsNullOrWhiteSpace(Name)) Name = Kind == MotionKind.Transition ? "Untitled transition" : "Untitled animation";
        if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N");
        if (!Enum.IsDefined(Kind)) Kind = MotionKind.Transition;
        Notes ??= "";
        TimelineLayoutId = (TimelineLayoutId ?? "").Trim();
        ItemStagger = Math.Clamp(double.IsFinite(ItemStagger) ? ItemStagger : 0.08, 0, 1.5);
        PieceOrder ??= [];
        PieceOrder = PieceOrder.Take(60).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        Pieces ??= [];
        Pieces = Pieces.Take(24).Where(piece => piece is not null).ToList();
        var seenPieces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var piece in Pieces)
        {
            if (string.IsNullOrWhiteSpace(piece.Id) || !seenPieces.Add(piece.Id))
            {
                piece.Id = Guid.NewGuid().ToString("N");
                seenPieces.Add(piece.Id);
            }
            piece.Normalize();
        }
        if (Kind == MotionKind.Transition)
        {
            // A hand-over is always an exit and an arrival, even if one leg is empty.
            if (Phase("Exit") is null) Phases.Insert(0, new MotionPhase("Exit", 0.25));
            if (Phase("Enter") is null) Phases.Add(new MotionPhase("Enter", 0.35));
        }
        else if (Phases.Count == 0)
        {
            Phases.Add(new MotionPhase(UseOnValueChange ? "Value" : "Motion", 0.8));
        }
    }

    public static string TargetLabel(MotionTarget target) => target switch
    {
        MotionTarget.X => "Move X (px)",
        MotionTarget.Y => "Move Y (px)",
        MotionTarget.Scale => "Scale (factor)",
        MotionTarget.ScaleX => "Scale X (factor)",
        MotionTarget.ScaleY => "Scale Y (factor)",
        MotionTarget.Rotate => "Rotate (degrees)",
        MotionTarget.Opacity => "Opacity (0–1)",
        MotionTarget.Blur => "Blur (px)",
        MotionTarget.SkewX => "Skew X (degrees)",
        MotionTarget.SkewY => "Skew Y (degrees)",
        MotionTarget.Width => "Width (px)",
        MotionTarget.Height => "Height (px)",
        MotionTarget.FontSize => "Text size (px)",
        MotionTarget.BorderThickness => "Border width (px)",
        MotionTarget.TextOpacity => "Text opacity (0–1)",
        MotionTarget.BackgroundOpacity => "Background opacity (0–1)",
        MotionTarget.BorderOpacity => "Border opacity (0–1)",
        _ => target.ToString()
    };

    public static string RoleLabel(MotionColorRole role) => role switch
    {
        MotionColorRole.Text => "Text color",
        MotionColorRole.Background => "Background color",
        MotionColorRole.Border => "Border color",
        _ => role.ToString()
    };

    // ---------- starting points ----------

    public static MotionClip NewTransition(string name = "New transition") => new()
    {
        Name = name,
        Kind = MotionKind.Transition,
        Phases =
        [
            new MotionPhase("Exit", 0.22)
            {
                Tracks = [new MotionTrack(MotionTarget.Opacity, (0, 1), (0.22, 0))],
                Colors = []
            },
            new MotionPhase("Enter", 0.35)
            {
                Tracks = [new MotionTrack(MotionTarget.Opacity, (0, 0), (0.35, 1))],
                Colors = []
            }
        ]
    };

    public static MotionClip NewAnimation(string name = "New animation", bool onValueChange = false) => new()
    {
        Name = name,
        Kind = MotionKind.Animation,
        UseOnValueChange = onValueChange,
        Loop = !onValueChange,
        Phases =
        [
            new MotionPhase(onValueChange ? "Value" : "Motion", 0.9)
            {
                Tracks = [new MotionTrack(MotionTarget.Scale, (0, 1), (0.45, 1.08), (0.9, 1))],
                Colors = []
            }
        ]
    };

    public static IReadOnlyList<string> TransitionPresets =>
    [
        "Dissolve", "Wipe left", "Wipe right", "Push up", "Push down",
        "Zoom through", "Flip card", "Swing", "Blur cut", "Color wash"
    ];

    public static IReadOnlyList<string> AnimationPresets =>
    [
        "Bob", "Sway", "Pulse", "Spin", "Breathe", "Shimmer",
        "Ticker roll", "Slide in", "Pop in", "Color cycle"
    ];

    public static MotionClip TransitionPreset(string name)
    {
        var clip = NewTransition(name);
        var exit = clip.Phase("Exit")!;
        var enter = clip.Phase("Enter")!;
        switch (name)
        {
            case "Wipe left":
                exit.Seconds = 0.28;
                exit.Tracks = [new MotionTrack(MotionTarget.X, (0, 0), (0.28, -420)), new MotionTrack(MotionTarget.Opacity, (0, 1), (0.28, 0.2))];
                enter.Seconds = 0.36;
                enter.Tracks = [new MotionTrack(MotionTarget.X, (0, 420), (0.36, 0)), new MotionTrack(MotionTarget.Opacity, (0, 0.2), (0.36, 1))];
                break;
            case "Wipe right":
                exit.Seconds = 0.28;
                exit.Tracks = [new MotionTrack(MotionTarget.X, (0, 0), (0.28, 420)), new MotionTrack(MotionTarget.Opacity, (0, 1), (0.28, 0.2))];
                enter.Seconds = 0.36;
                enter.Tracks = [new MotionTrack(MotionTarget.X, (0, -420), (0.36, 0)), new MotionTrack(MotionTarget.Opacity, (0, 0.2), (0.36, 1))];
                break;
            case "Push up":
                exit.Seconds = 0.26;
                exit.Tracks = [new MotionTrack(MotionTarget.Y, (0, 0), (0.26, -180))];
                enter.Seconds = 0.34;
                enter.Tracks = [new MotionTrack(MotionTarget.Y, (0, 180), (0.34, 0))];
                break;
            case "Push down":
                exit.Seconds = 0.26;
                exit.Tracks = [new MotionTrack(MotionTarget.Y, (0, 0), (0.26, 180))];
                enter.Seconds = 0.34;
                enter.Tracks = [new MotionTrack(MotionTarget.Y, (0, -180), (0.34, 0))];
                break;
            case "Zoom through":
                exit.Seconds = 0.3;
                exit.Tracks = [new MotionTrack(MotionTarget.Scale, (0, 1), (0.3, 1.35)), new MotionTrack(MotionTarget.Opacity, (0, 1), (0.3, 0))];
                enter.Seconds = 0.4;
                enter.Tracks = [new MotionTrack(MotionTarget.Scale, (0, 0.72), (0.4, 1)), new MotionTrack(MotionTarget.Opacity, (0, 0), (0.4, 1))];
                break;
            case "Flip card":
                exit.Seconds = 0.24;
                exit.Tracks = [new MotionTrack(MotionTarget.ScaleY, (0, 1), (0.24, 0.02)), new MotionTrack(MotionTarget.Opacity, (0, 1), (0.24, 0.05))];
                enter.Seconds = 0.34;
                enter.Tracks = [new MotionTrack(MotionTarget.ScaleY, (0, 0.02), (0.34, 1)), new MotionTrack(MotionTarget.Opacity, (0, 0.05), (0.34, 1))];
                break;
            case "Swing":
                exit.Seconds = 0.3;
                exit.Tracks = [new MotionTrack(MotionTarget.Rotate, (0, 0), (0.3, -8)), new MotionTrack(MotionTarget.X, (0, 0), (0.3, -220)), new MotionTrack(MotionTarget.Opacity, (0, 1), (0.3, 0))];
                enter.Seconds = 0.42;
                enter.Tracks = [new MotionTrack(MotionTarget.Rotate, (0, 10), (0.42, 0)), new MotionTrack(MotionTarget.X, (0, 260), (0.42, 0)), new MotionTrack(MotionTarget.Opacity, (0, 0), (0.42, 1))];
                break;
            case "Blur cut":
                exit.Seconds = 0.18;
                exit.Tracks = [new MotionTrack(MotionTarget.Blur, (0, 0), (0.18, 22)), new MotionTrack(MotionTarget.Opacity, (0, 1), (0.18, 0))];
                enter.Seconds = 0.3;
                enter.Tracks = [new MotionTrack(MotionTarget.Blur, (0, 26), (0.3, 0)), new MotionTrack(MotionTarget.Opacity, (0, 0), (0.3, 1))];
                break;
            case "Color wash":
                exit.Seconds = 0.28;
                exit.Tracks = [new MotionTrack(MotionTarget.Opacity, (0, 1), (0.28, 0))];
                exit.Colors = [new ColorTrack(MotionColorRole.Background, (0, "#FF141D28"), (0.28, "#FF38D7A3"))];
                enter.Seconds = 0.46;
                enter.Tracks = [new MotionTrack(MotionTarget.Opacity, (0, 0), (0.46, 1))];
                enter.Colors =
                [
                    new ColorTrack(MotionColorRole.Background, (0, "#FF38D7A3"), (0.46, "#FF141D28")),
                    new ColorTrack(MotionColorRole.Text, (0, "#FF0B1A16"), (0.46, "#FFFFFFFF"))
                ];
                break;
            default: // Dissolve
                exit.Seconds = 0.26;
                exit.Tracks = [new MotionTrack(MotionTarget.Opacity, (0, 1), (0.26, 0)), new MotionTrack(MotionTarget.Scale, (0, 1), (0.26, 0.97))];
                enter.Seconds = 0.4;
                enter.Tracks = [new MotionTrack(MotionTarget.Opacity, (0, 0), (0.4, 1)), new MotionTrack(MotionTarget.Scale, (0, 1.03), (0.4, 1))];
                break;
        }
        clip.Normalize();
        return clip;
    }

    public static MotionClip AnimationPreset(string name)
    {
        var clip = NewAnimation(name);
        var phase = clip.Phases[0];
        switch (name)
        {
            case "Bob":
                phase.Seconds = 1.8;
                phase.Tracks = [new MotionTrack(MotionTarget.Y, (0, 0), (0.9, -9), (1.8, 0))];
                break;
            case "Sway":
                phase.Seconds = 2.4;
                phase.Tracks = [new MotionTrack(MotionTarget.X, (0, 0), (1.2, 8), (2.4, 0)), new MotionTrack(MotionTarget.Rotate, (0, 0), (1.2, 1.2), (2.4, 0))];
                break;
            case "Pulse":
                phase.Seconds = 1.4;
                phase.Tracks = [new MotionTrack(MotionTarget.Scale, (0, 1), (0.7, 1.06), (1.4, 1))];
                break;
            case "Spin":
                clip.Loop = false;
                phase.Seconds = 2.4;
                phase.Tracks = [new MotionTrack(MotionTarget.Rotate, (0, 0), (2.4, 360))];
                break;
            case "Breathe":
                phase.Seconds = 3.2;
                phase.Tracks = [new MotionTrack(MotionTarget.Opacity, (0, 1), (1.6, 0.82), (3.2, 1)), new MotionTrack(MotionTarget.Scale, (0, 1), (1.6, 1.015), (3.2, 1))];
                break;
            case "Shimmer":
                phase.Seconds = 2.2;
                phase.Colors =
                [
                    new ColorTrack(MotionColorRole.Text, (0, "#FFFFFFFF"), (1.1, "#FF38D7A3"), (2.2, "#FFFFFFFF")),
                    new ColorTrack(MotionColorRole.Border, (0, "#FFFFFFFF"), (1.1, "#FFE8B23A"), (2.2, "#FFFFFFFF"))
                ];
                break;
            case "Ticker roll":
                phase.Seconds = 0.46;
                phase.Tracks = [new MotionTrack(MotionTarget.Y, (0, 16), (0.24, 0)), new MotionTrack(MotionTarget.Opacity, (0, 0), (0.2, 1))];
                break;
            case "Slide in":
                phase.Seconds = 0.42;
                phase.Tracks = [new MotionTrack(MotionTarget.X, (0, -42), (0.42, 0)), new MotionTrack(MotionTarget.Opacity, (0, 0), (0.3, 1))];
                break;
            case "Pop in":
                phase.Seconds = 0.36;
                phase.Tracks = [new MotionTrack(MotionTarget.Scale, (0, 0.8), (0.24, 1.045), (0.36, 1)), new MotionTrack(MotionTarget.Opacity, (0, 0), (0.18, 1))];
                break;
            case "Color cycle":
                phase.Seconds = 4;
                phase.Colors =
                [
                    new ColorTrack(MotionColorRole.Background, (0, "#FF141D28"), (2, "#FF0B6B50"), (4, "#FF141D28")),
                    new ColorTrack(MotionColorRole.Text, (0, "#FFFFFFFF"), (2, "#FF38D7A3"), (4, "#FFFFFFFF"))
                ];
                break;
            default: // Pop value
                phase.Seconds = 0.9;
                phase.Tracks = [new MotionTrack(MotionTarget.Scale, (0, 1), (0.45, 1.08), (0.9, 1))];
                break;
        }
        clip.Normalize();
        return clip;
    }
}

/// <summary>Shapes a phase's progress with the curve the operator chose for each key.</summary>
public static class MotionCurves
{
    public static double Apply(double amount, MotionEase ease)
    {
        amount = Math.Clamp(amount, 0, 1);
        return ease switch
        {
            MotionEase.Linear => amount,
            MotionEase.In => amount * amount * amount,
            MotionEase.Out => 1 - Math.Pow(1 - amount, 3),
            MotionEase.InOut => amount < 0.5 ? 4 * amount * amount * amount : 1 - Math.Pow(-2 * amount + 2, 3) / 2,
            MotionEase.Back => Back(amount),
            MotionEase.Bounce => Bounce(amount),
            MotionEase.Elastic => Elastic(amount),
            _ => amount
        };
    }

    static double Back(double amount)
    {
        const double c = 1.70158;
        const double c3 = c + 1;
        return c3 * amount * amount * amount - c * amount * amount;
    }

    static double Bounce(double amount)
    {
        const double n = 7.5625;
        const double d = 2.75;
        if (amount < 1 / d) return n * amount * amount;
        if (amount < 2 / d) { amount -= 1.5 / d; return n * amount * amount + 0.75; }
        if (amount < 2.5 / d) { amount -= 2.25 / d; return n * amount * amount + 0.9375; }
        amount -= 2.625 / d;
        return n * amount * amount + 0.984375;
    }

    static double Elastic(double amount)
    {
        if (amount is 0 or 1) return amount;
        const double c = (2 * Math.PI) / 3;
        return Math.Pow(2, -10 * amount) * Math.Sin((amount * 10 - 0.75) * c) + 1;
    }
}

/// <summary>Colour arithmetic for keyframes, kept in Core so the makers and the renderer agree.</summary>
public static class MotionColors
{
    public static (byte A, byte R, byte G, byte B) Parse(string? value)
    {
        var fallback = ((byte)255, (byte)255, (byte)255, (byte)255);
        var text = (value ?? "").Trim();
        if (text.Length < 4 || text[0] != '#') return fallback;
        var digits = text[1..];
        try
        {
            return digits.Length switch
            {
                3 => (255, (byte)(Convert.ToByte($"{digits[0]}{digits[0]}", 16)), (byte)(Convert.ToByte($"{digits[1]}{digits[1]}", 16)), (byte)(Convert.ToByte($"{digits[2]}{digits[2]}", 16))),
                4 => (Nibble(digits[0]), Nibble(digits[1]), Nibble(digits[2]), Nibble(digits[3])),
                6 => (255, Convert.ToByte(digits[..2], 16), Convert.ToByte(digits[2..4], 16), Convert.ToByte(digits[4..6], 16)),
                8 => (Convert.ToByte(digits[..2], 16), Convert.ToByte(digits[2..4], 16), Convert.ToByte(digits[4..6], 16), Convert.ToByte(digits[6..8], 16)),
                _ => fallback
            };
        }
        catch { return fallback; }
    }

    static byte Nibble(char digit) => (byte)(Convert.ToByte(digit.ToString(), 16) * 17);

    public static string Hex(byte a, byte r, byte g, byte b) => $"#{a:X2}{r:X2}{g:X2}{b:X2}";

    public static string Mix(string? from, string? to, double amount)
    {
        var (aa, ar, ag, ab) = Parse(from);
        var (ba, br, bg, bb) = Parse(to);
        amount = Math.Clamp(amount, 0, 1);
        byte M(byte x, byte y) => (byte)Math.Round(x * (1 - amount) + y * amount);
        return Hex(M(aa, ba), M(ar, br), M(ag, bg), M(ab, bb));
    }

    /// <summary>Brightens (positive) or darkens (negative) a colour, keeping its alpha.</summary>
    public static string Shift(string? value, double amount)
    {
        var (a, r, g, b) = Parse(value);
        amount = Math.Clamp(amount, -1, 1);
        byte S(byte channel)
        {
            var target = amount >= 0 ? 255 : 0;
            return (byte)Math.Round(channel * (1 - Math.Abs(amount)) + target * Math.Abs(amount));
        }
        return Hex(a, S(r), S(g), S(b));
    }

    /// <summary>Moves a colour towards a hue by rotating its channels; a stylised iridescence.</summary>
    public static string Hue(string? value, double turn)
    {
        var (a, r, g, b) = Parse(value);
        var angle = ((turn % 1) + 1) % 1 * Math.PI * 2;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        // A compact YIQ-style rotation, enough to shift a tint without washing it grey.
        var rr = Math.Clamp(r * (0.213 + cos * 0.787 - sin * 0.213) + g * (0.715 - cos * 0.715 - sin * 0.715) + b * (0.072 - cos * 0.072 + sin * 0.928), 0, 255);
        var gg = Math.Clamp(r * (0.213 - cos * 0.213 + sin * 0.143) + g * (0.715 + cos * 0.285 + sin * 0.140) + b * (0.072 - cos * 0.072 - sin * 0.283), 0, 255);
        var bb = Math.Clamp(r * (0.213 - cos * 0.213 - sin * 0.787) + g * (0.715 - cos * 0.715 + sin * 0.715) + b * (0.072 + cos * 0.928 + sin * 0.072), 0, 255);
        return Hex(a, (byte)rr, (byte)gg, (byte)bb);
    }

    public static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);
}
