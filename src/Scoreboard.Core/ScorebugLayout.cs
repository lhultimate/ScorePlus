using System.Text.Json;
using System.Text.Json.Nodes;

namespace Scoreboard.Core;

public enum ScorebugElementKind { Text, Logo, Shape, Video, Html }

/// <summary>
/// How one layout hands over to the next on the audience output when a display switches. The
/// duration lives beside it on the display, so a slow dissolve and a hard cut are one setting apart.
/// </summary>
public enum LayoutTransition { Cut, Fade, SlideLeft, SlideRight, SlideUp, SlideDown, Zoom }

/// <summary>Ambient motion that runs all the time, whatever the value is.</summary>
public enum ScorebugAnimation { None, Pulse, Drift }

/// <summary>
/// What happens to a text element when its value changes. Twelve ways to cross from the old
/// number to the new one, so the score never just cuts.
/// </summary>
public enum ScorebugTransition
{
    Cut,
    Fade,
    FadeScale,
    SlideUp,
    SlideDown,
    SlideLeft,
    SlideRight,
    WheelUp,
    WheelDown,
    Flip,
    Zoom,
    Pop,
    Drop
}

public enum ScorebugShape { Rectangle, Circle, Triangle }

/// <summary>Where an element's colour comes from: a fixed colour, or one of the team palette slots.</summary>
public enum ScorebugColorSource
{
    Custom,
    HomePrimary,
    HomeSecondary,
    AwayPrimary,
    AwaySecondary
}

/// <summary>Which team's logo a logo element shows; set in the control panel, not the studio.</summary>
public enum ScorebugLogoSide { None, Home, Away }

/// <summary>Artwork drawn by an element in addition to (or instead of) its bound text.</summary>
public enum ScorebugIcon
{
    None,
    /// <summary>One little bar per timeout the team has left.</summary>
    TimeoutBars,
    /// <summary>A dot on the left or right edge, on the side of the team with the ball.</summary>
    PossessionDot,
    /// <summary>The three baseball bases: filled when a runner is on, a faint outline when empty.</summary>
    Bases
}

public sealed class ScorebugElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Element";
    public ScorebugElementKind Kind { get; set; } = ScorebugElementKind.Text;
    public ScorebugShape Shape { get; set; } = ScorebugShape.Rectangle;
    public string Content { get; set; } = "TEXT";
    public string? ImagePath { get; set; }

    /// <summary>
    /// Set on a part copied out of another layout: every element sharing this id is the same
    /// part, so moving or restyling one updates all of them. Null on an independent element.
    /// </summary>
    public string? LinkId { get; set; }

    /// <summary>For video elements: only media assigned to this tag plays inside the element.</summary>
    public string MediaTag { get; set; } = "";

    /// <summary>
    /// For video elements: lock the box to the picture's aspect ratio, so the clip is never
    /// stretched or cropped. Off means the box can be any shape and the clip fills it.
    /// </summary>
    public bool PreserveVideoAspect { get; set; }

    /// <summary>The picture's width ÷ height while <see cref="PreserveVideoAspect"/> is on. 16:9 by default.</summary>
    public double VideoAspect { get; set; } = 16.0 / 9.0;

    /// <summary>
    /// For video elements: which speaker this element's clip plays through (a WASAPI render device
    /// id). Empty means the Windows default, which is what the audience output uses.
    /// </summary>
    public string? AudioDevice { get; set; }
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 70;
    public double FontSize { get; set; } = 32;

    /// <summary>Font family name, or a file URI for an imported font.</summary>
    public string FontFamily { get; set; } = "Segoe UI";

    public bool Bold { get; set; } = true;
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool StrikeThrough { get; set; }

    public string Foreground { get; set; } = "#FFFFFFFF";
    public string Background { get; set; } = "#CC141D28";

    /// <summary>Clockwise rotation of the element's content, in degrees. Mainly for slanted text.</summary>
    public double Rotation { get; set; }

    /// <summary>Outline drawn around the element. Zero hides the border entirely.</summary>
    public double BorderThickness { get; set; }

    /// <summary>The outline's fixed colour, used unless <see cref="BorderSource"/> names a team slot.</summary>
    public string BorderColor { get; set; } = "#FFFFFFFF";

    /// <summary>Where the border colour comes from. A team slot wins over <see cref="BorderColor"/>.</summary>
    public ScorebugColorSource BorderSource { get; set; } = ScorebugColorSource.Custom;

    /// <summary>Where the text colour comes from. A team slot wins over <see cref="Foreground"/>.</summary>
    public ScorebugColorSource ForegroundSource { get; set; } = ScorebugColorSource.Custom;

    /// <summary>Where the background comes from. A team slot wins over <see cref="Background"/>.</summary>
    public ScorebugColorSource BackgroundSource { get; set; } = ScorebugColorSource.Custom;

    /// <summary>Paint the background as a two-stop gradient instead of one flat colour.</summary>
    public bool BackgroundGradient { get; set; }

    /// <summary>The gradient's second colour, used unless <see cref="BackgroundSource2"/> names a team slot.</summary>
    public string BackgroundColor2 { get; set; } = "#CC101820";

    /// <summary>Where the gradient's second colour comes from. A team slot wins over <see cref="BackgroundColor2"/>.</summary>
    public ScorebugColorSource BackgroundSource2 { get; set; } = ScorebugColorSource.Custom;

    /// <summary>Gradient direction in degrees: 0 runs left to right, 90 top to bottom.</summary>
    public double GradientAngle { get; set; }

    public bool TransparentBackground { get; set; }
    public double Opacity { get; set; } = 1;
    public bool Visible { get; set; } = true;
    public ScorebugAnimation Animation { get; set; }

    /// <summary>How the value changes: dissolve, wheel, flip, and so on.</summary>
    public ScorebugTransition Transition { get; set; } = ScorebugTransition.Cut;

    /// <summary>How long a value change takes, in seconds: 0 snaps, 5 is a slow dissolve.</summary>
    public double TransitionSeconds { get; set; } = 0.2;

    /// <summary>Which team logo a logo element draws. Configured in the control panel.</summary>
    public ScorebugLogoSide LogoSide { get; set; } = ScorebugLogoSide.None;

    /// <summary>How this element prints a quarter: "Full", "Short", "Number", or null for the game default.</summary>
    public string? PeriodFormat { get; set; }

    /// <summary>Optional artwork from live data: timeout bars or a possession dot.</summary>
    public ScorebugIcon Icon { get; set; } = ScorebugIcon.None;

    /// <summary>For timeout bars: count the visitors' timeouts instead of the home team's.</summary>
    public bool IconVisitors { get; set; }

    /// <summary>For the possession dot: mirror it, putting the home side on the left.</summary>
    public bool IconFlipped { get; set; }
}

public sealed class ScorebugLayout
{
    public int Version { get; set; } = 4;

    /// <summary>Stable identity, so a layout can be the target of a link on another display.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Broadcast Classic";

    /// <summary>
    /// The display this layout sends to when it goes on air. A linked layout switches the other
    /// display to <see cref="LinkLayoutId"/> at the same time, so one click can change a main
    /// scorebug and its matching lower third together.
    /// </summary>
    public string? LinkDisplayId { get; set; }

    /// <summary>The layout id, on <see cref="LinkDisplayId"/>, that this layout switches to.</summary>
    public string? LinkLayoutId { get; set; }

    /// <summary>Design canvas size in pixels. Elements are positioned in this space and scaled to fit any output.</summary>
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;

    /// <summary>
    /// The colour behind this layout's artwork — the studio canvas, the previews and the audience
    /// output all paint it. Broadcast green (#FF00B140) turns the same layout into a chroma-key
    /// source; black, white or any other colour is set in the studio's canvas bar.
    /// </summary>
    public string Background { get; set; } = "#FF091018";

    /// <summary>
    /// How this layout arrives when the operator switches to it: cut, dissolve, swing, slide or
    /// zoom. The hand-over belongs to the incoming layout, so a main scorebug can snap on while a
    /// lower third dissolves in beside it.
    /// </summary>
    public LayoutTransition Transition { get; set; } = LayoutTransition.Fade;

    /// <summary>How long this layout's hand-over takes, in seconds: 0 cuts, 5 is a slow dissolve.</summary>
    public double TransitionSeconds { get; set; } = 0.5;

    public static readonly (string Name, int Width, int Height)[] SizePresets =
    [
        ("HD · 1280 × 720", 1280, 720),
        ("Full HD · 1920 × 1080", 1920, 1080),
        ("QHD · 2560 × 1440", 2560, 1440),
        ("4K UHD · 3840 × 2160", 3840, 2160),
        ("Vertical · 1080 × 1920", 1080, 1920),
        ("Square · 1080 × 1080", 1080, 1080)
    ];
    public List<ScorebugElement> Elements { get; set; } = [];

    public static ScorebugLayout Default() => new()
    {
        Elements =
        [
            new() { Name="Home team", Content="{{HOME_TEAM}}", X=74, Y=236, Width=300, Height=54, FontSize=36 },
            new() { Name="Home score", Content="{{HOME_SCORE}}", X=74, Y=300, Width=300, Height=126, FontSize=102, ForegroundSource=ScorebugColorSource.HomePrimary },
            new() { Name="Away team", Content="{{AWAY_TEAM}}", X=906, Y=236, Width=300, Height=54, FontSize=36 },
            new() { Name="Away score", Content="{{AWAY_SCORE}}", X=906, Y=300, Width=300, Height=126, FontSize=102, ForegroundSource=ScorebugColorSource.AwayPrimary },
            new() { Name="Period", Content="{{PERIOD}}", X=470, Y=225, Width=340, Height=52, FontSize=28 },
            new() { Name="Game clock", Content="{{GAME_CLOCK}}", X=430, Y=300, Width=420, Height=92, FontSize=72, Transition=ScorebugTransition.WheelUp },
            new() { Name="Home logo", Kind=ScorebugElementKind.Logo, LogoSide=ScorebugLogoSide.Home, Content="HOME LOGO", X=78, Y=160, Width=120, Height=64, FontSize=20 },
            new() { Name="Away logo", Kind=ScorebugElementKind.Logo, LogoSide=ScorebugLogoSide.Away, Content="AWAY LOGO", X=1082, Y=160, Width=120, Height=64, FontSize=20 }
        ]
    };

    /// <summary>A deep copy with brand new element ids, so a duplicated layout edits independently.</summary>
    public ScorebugLayout Copy(string? name = null)
    {
        var copy = JsonSerializer.Deserialize<ScorebugLayout>(JsonSerializer.Serialize(this))!;
        copy.Name = name ?? $"{Name} copy";
        // A duplicate is its own layout: it keeps the link settings but gets a fresh identity.
        copy.Id = Guid.NewGuid().ToString("N");
        foreach (var element in copy.Elements) element.Id = Guid.NewGuid().ToString("N");
        return copy;
    }
}

/// <summary>
/// One program feed. A show usually has a main scorebug display and a lower-thirds display, and
/// each one keeps its own set of layouts and its own choice of what is on air.
/// </summary>
public sealed class ScorebugDisplay
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Program";
    public List<ScorebugLayout> Layouts { get; set; } = [];

    /// <summary>The layout last open for editing on this display.</summary>
    public int ActiveIndex { get; set; }

    /// <summary>The layout this display is currently sending to its audience output.</summary>
    public int PublishedIndex { get; set; }
}

/// <summary>What is written to scorebug.json: every display, with its layouts and what is on air.</summary>
public sealed class ScorebugDocument
{
    public int Version { get; set; } = 5;
    public List<ScorebugDisplay> Displays { get; set; } = [];
    public int ActiveDisplayIndex { get; set; }

    /// <summary>A v3 file held one flat list of layouts for one screen; read so it still opens.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<ScorebugLayout>? Layouts { get; set; }
    public int ActiveIndex { get; set; }
    public int PublishedIndex { get; set; }
}

public sealed class ScorebugCatalog
{
    readonly string _path;

    /// <summary>Every program feed in this game. A new game starts with one display.</summary>
    public List<ScorebugDisplay> Displays { get; private set; } = [];

    /// <summary>The display whose layouts the studio is editing.</summary>
    public int ActiveDisplayIndex { get; private set; }

    public string? Warning { get; private set; }
    public event Action? Changed;

    public ScorebugCatalog(string? path = null)
    {
        _path = path ?? AppPaths.Combine("scorebug.json");
        try { Load(); }
        catch (Exception ex)
        {
            Displays = [DefaultDisplay()];
            Warning = $"Could not load scorebug layout: {ex.Message}";
        }
        Validate();
    }

    /// <summary>A display and the layout that has to change with it, in another display.</summary>
    public sealed record LayoutLink(string DisplayId, int LayoutIndex);

    static ScorebugDisplay DefaultDisplay() => new()
    {
        Name = "Program",
        Layouts = [ScorebugLayout.Default()]
    };

    static ScorebugDisplay BlankDisplay(int number) => new()
    {
        Name = $"Display {number}",
        Layouts = [new ScorebugLayout { Name = "Layout 1" }]
    };

    public ScorebugDisplay ActiveDisplay => Displays[Math.Clamp(ActiveDisplayIndex, 0, Displays.Count - 1)];

    /// <summary>The layouts of the display being edited.</summary>
    public IReadOnlyList<ScorebugLayout> Layouts => ActiveDisplay.Layouts;

    public int ActiveIndex => ActiveDisplay.ActiveIndex;

    public int PublishedIndex => ActiveDisplay.PublishedIndex;

    /// <summary>The layout being designed and previewed.</summary>
    public ScorebugLayout Layout => ActiveDisplay.Layouts[Math.Clamp(ActiveDisplay.ActiveIndex, 0, ActiveDisplay.Layouts.Count - 1)];

    /// <summary>The layout currently on air. It changes only when Update Output is pressed.</summary>
    public ScorebugLayout PublishedLayout => ActiveDisplay.Layouts[Math.Clamp(ActiveDisplay.PublishedIndex, 0, ActiveDisplay.Layouts.Count - 1)];

    // ---------- displays ----------

    public ScorebugDisplay? FindDisplay(string? id) =>
        id is null ? null : Displays.FirstOrDefault(display => string.Equals(display.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ScorebugLayout> LayoutsOf(string? displayId) =>
        (FindDisplay(displayId) ?? ActiveDisplay).Layouts;

    public ScorebugLayout? LayoutOf(string? displayId, int index)
    {
        var layouts = LayoutsOf(displayId);
        return layouts.Count == 0 ? null : layouts[Math.Clamp(index, 0, layouts.Count - 1)];
    }

    /// <summary>What a display is actually sending to its audience output.</summary>
    public ScorebugLayout PublishedLayoutOf(string? displayId)
    {
        var display = FindDisplay(displayId) ?? ActiveDisplay;
        return display.Layouts[Math.Clamp(display.PublishedIndex, 0, display.Layouts.Count - 1)];
    }

    public bool IsPublished(string? displayId, int layoutIndex)
    {
        var display = FindDisplay(displayId);
        return display is not null && display.PublishedIndex == Math.Clamp(layoutIndex, 0, display.Layouts.Count - 1);
    }

    /// <summary>
    /// Where a layout sends the other screen when it is chosen. Null when the layout has no link or
    /// the link no longer resolves (a deleted layout, or a link pointing at its own display).
    /// </summary>
    public LayoutLink? LinkTarget(string? displayId, int layoutIndex)
    {
        var source = FindDisplay(displayId);
        if (source is null || layoutIndex < 0 || layoutIndex >= source.Layouts.Count) return null;
        var layout = source.Layouts[layoutIndex];
        if (string.IsNullOrWhiteSpace(layout.LinkDisplayId) || string.IsNullOrWhiteSpace(layout.LinkLayoutId)) return null;
        var target = FindDisplay(layout.LinkDisplayId);
        if (target is null || string.Equals(target.Id, source.Id, StringComparison.OrdinalIgnoreCase)) return null;
        var index = target.Layouts.FindIndex(candidate => string.Equals(candidate.Id, layout.LinkLayoutId, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : new LayoutLink(target.Id, index);
    }

    /// <summary>Adds a second (or third) program feed and makes it the one being edited.</summary>
    public int AddDisplay(string? name = null)
    {
        var display = BlankDisplay(Displays.Count + 1);
        if (!string.IsNullOrWhiteSpace(name)) display.Name = name.Trim();
        Displays.Add(display);
        ActiveDisplayIndex = Displays.Count - 1;
        Save();
        Changed?.Invoke();
        return ActiveDisplayIndex;
    }

    public bool RemoveDisplay(int index)
    {
        if (Displays.Count <= 1 || index < 0 || index >= Displays.Count) return false;
        var removed = Displays[index];
        Displays.RemoveAt(index);
        // Nothing may keep a link (or an audience window) pointing at a display that is gone.
        foreach (var display in Displays)
            foreach (var layout in display.Layouts)
                if (string.Equals(layout.LinkDisplayId, removed.Id, StringComparison.OrdinalIgnoreCase))
                {
                    layout.LinkDisplayId = null;
                    layout.LinkLayoutId = null;
                }
        ActiveDisplayIndex = Math.Clamp(ActiveDisplayIndex, 0, Displays.Count - 1);
        Save();
        Changed?.Invoke();
        return true;
    }

    public void RenameDisplay(int index, string name)
    {
        if (index < 0 || index >= Displays.Count || string.IsNullOrWhiteSpace(name)) return;
        Displays[index].Name = name.Trim();
        Save();
        Changed?.Invoke();
    }

    /// <summary>Chooses which display the studio edits. Never changes what any display is on air with.</summary>
    public void SelectDisplay(int index)
    {
        var next = Math.Clamp(index, 0, Displays.Count - 1);
        if (next == ActiveDisplayIndex) return;
        ActiveDisplayIndex = next;
        Save();
        Changed?.Invoke();
    }

    void Load()
    {
        Warning = null;
        if (!File.Exists(_path))
        {
            Displays = [DefaultDisplay()];
            ActiveDisplayIndex = 0;
            return;
        }
        var text = File.ReadAllText(_path);
        var node = JsonNode.Parse(text);
        if (node is JsonObject root && root["Displays"] is not null)
        {
            var document = root.Deserialize<ScorebugDocument>() ?? new ScorebugDocument();
            Displays = document.Displays ?? [];
            ActiveDisplayIndex = document.ActiveDisplayIndex;
            // A v4 file kept one hand-over per display; every layout now carries its own. Each layout
            // inherits the display transition it was last saved with, so an existing game still
            // switches the way the operator set it up.
            if (document.Version < 5 && root["Displays"] is JsonArray storedDisplays)
            {
                for (var i = 0; i < Math.Min(storedDisplays.Count, Displays.Count); i++)
                {
                    if (storedDisplays[i] is not JsonObject stored) continue;
                    var transition = LayoutTransition.Fade;
                    var seconds = 0.5;
                    try
                    {
                        if (stored["Transition"] is JsonValue kind) transition = (LayoutTransition)kind.GetValue<int>();
                        if (stored["TransitionSeconds"] is JsonValue span) seconds = span.GetValue<double>();
                    }
                    catch
                    {
                        // A file written by hand may spell the transition out; the defaults are fine.
                    }
                    foreach (var layout in Displays[i].Layouts)
                    {
                        layout.Transition = transition;
                        layout.TransitionSeconds = seconds;
                    }
                }
            }
        }
        else if (node is JsonObject legacy && legacy["Layouts"] is not null)
        {
            // A v3 file held one flat list of layouts for a single screen. It becomes display 1.
            var document = legacy.Deserialize<ScorebugDocument>() ?? new ScorebugDocument();
            Displays =
            [
                new ScorebugDisplay
                {
                    Name = "Program",
                    Layouts = document.Layouts ?? [],
                    ActiveIndex = document.ActiveIndex,
                    PublishedIndex = document.PublishedIndex
                }
            ];
            ActiveDisplayIndex = 0;
        }
        else
        {
            // An even older file held one layout. It keeps its artwork and becomes layout 1.
            var single = node?.Deserialize<ScorebugLayout>() ?? ScorebugLayout.Default();
            Displays = [new ScorebugDisplay { Name = "Program", Layouts = [single] }];
            ActiveDisplayIndex = 0;
        }
        if (Displays.Count == 0) Displays = [DefaultDisplay()];
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var document = new ScorebugDocument
        {
            Displays = Displays,
            ActiveDisplayIndex = ActiveDisplayIndex
        };
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true);
    }

    /// <summary>Edits the layout being designed, then saves and notifies every view.</summary>
    public void Update(Action<ScorebugLayout> edit)
    {
        edit(Layout); Validate();
        Save(); Changed?.Invoke();
    }

    /// <summary>
    /// Copies a linked part's artwork onto every copy of it, in every layout of every display.
    /// Called from inside <see cref="Update"/> so one edit is still one save and one repaint.
    /// </summary>
    public void PropagateLink(ScorebugElement source)
    {
        if (string.IsNullOrWhiteSpace(source.LinkId)) return;
        foreach (var display in Displays)
            foreach (var layout in display.Layouts)
                foreach (var target in layout.Elements)
                    if (!ReferenceEquals(target, source)
                        && !string.Equals(target.Id, source.Id, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(target.LinkId, source.LinkId, StringComparison.OrdinalIgnoreCase))
                        CopyArtwork(source, target);
    }

    /// <summary>Every visual property of one element onto another, keeping the target's identity.</summary>
    static void CopyArtwork(ScorebugElement source, ScorebugElement target)
    {
        var id = target.Id;
        var link = target.LinkId;
        var copy = JsonSerializer.Deserialize<ScorebugElement>(JsonSerializer.Serialize(source));
        if (copy is null) return;
        foreach (var property in typeof(ScorebugElement).GetProperties())
        {
            if (!property.CanRead || !property.CanWrite) continue;
            if (property.Name is nameof(ScorebugElement.Id) or nameof(ScorebugElement.LinkId)) continue;
            property.SetValue(target, property.GetValue(copy));
        }
        target.Id = id;
        target.LinkId = link;
    }

    void Validate()
    {
        if (Displays.Count == 0) Displays = [DefaultDisplay()];
        var seenDisplays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenLayouts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in Displays)
        {
            display.Layouts ??= [];
            if (display.Layouts.Count == 0) display.Layouts.Add(new ScorebugLayout { Name = "Layout 1" });
            if (string.IsNullOrWhiteSpace(display.Name)) display.Name = "Display";
            if (string.IsNullOrWhiteSpace(display.Id) || !seenDisplays.Add(display.Id)) display.Id = Guid.NewGuid().ToString("N");
            seenDisplays.Add(display.Id);
            display.ActiveIndex = Math.Clamp(display.ActiveIndex, 0, display.Layouts.Count - 1);
            display.PublishedIndex = Math.Clamp(display.PublishedIndex, 0, display.Layouts.Count - 1);
            foreach (var layout in display.Layouts)
            {
                if (string.IsNullOrWhiteSpace(layout.Id) || !seenLayouts.Add(layout.Id))
                {
                    layout.Id = Guid.NewGuid().ToString("N");
                    seenLayouts.Add(layout.Id);
                }
                layout.Elements ??= [];
                if (layout.Elements.Count > 100) layout.Elements = layout.Elements.Take(100).ToList();
                layout.Width = (int)Math.Clamp(layout.Width, 320, 7680);
                layout.Height = (int)Math.Clamp(layout.Height, 240, 7680);
                if (string.IsNullOrWhiteSpace(layout.Name)) layout.Name = "Untitled layout";
                if (!IsColor(layout.Background)) layout.Background = "#FF091018";
                layout.TransitionSeconds = Math.Clamp(double.IsFinite(layout.TransitionSeconds) ? layout.TransitionSeconds : 0.5, 0, 5);
                if (!Enum.IsDefined(layout.Transition)) layout.Transition = LayoutTransition.Fade;
                foreach (var item in layout.Elements)
                {
                    item.X = Math.Clamp(double.IsFinite(item.X) ? item.X : 0, 0, layout.Width);
                    item.Y = Math.Clamp(double.IsFinite(item.Y) ? item.Y : 0, 0, layout.Height);
                    item.Width = Math.Clamp(double.IsFinite(item.Width) ? item.Width : 100, 24, layout.Width);
                    item.Height = Math.Clamp(double.IsFinite(item.Height) ? item.Height : 50, 24, layout.Height);
                    item.FontSize = Math.Clamp(double.IsFinite(item.FontSize) ? item.FontSize : 24, 8, 480);
                    item.Opacity = Math.Clamp(double.IsFinite(item.Opacity) ? item.Opacity : 1, 0, 1);
                    item.Rotation = Math.Clamp(double.IsFinite(item.Rotation) ? item.Rotation : 0, -360, 360);
                    item.BorderThickness = Math.Clamp(double.IsFinite(item.BorderThickness) ? item.BorderThickness : 0, 0, 40);
                    item.TransitionSeconds = Math.Clamp(double.IsFinite(item.TransitionSeconds) ? item.TransitionSeconds : 0.2, 0, 5);
                    item.VideoAspect = Math.Clamp(double.IsFinite(item.VideoAspect) && item.VideoAspect > 0.05 ? item.VideoAspect : 16.0 / 9.0, 0.1, 10);
                    item.GradientAngle = Math.Clamp(double.IsFinite(item.GradientAngle) ? item.GradientAngle : 0, -360, 360);
                    if (string.IsNullOrWhiteSpace(item.BorderColor)) item.BorderColor = "#FFFFFFFF";
                    if (string.IsNullOrWhiteSpace(item.BackgroundColor2)) item.BackgroundColor2 = "#CC101820";
                    if (item.PeriodFormat is not ("Full" or "Short" or "Number")) item.PeriodFormat = null;
                    if (string.IsNullOrWhiteSpace(item.FontFamily)) item.FontFamily = "Segoe UI";
                }
            }
        }
        ActiveDisplayIndex = Math.Clamp(ActiveDisplayIndex, 0, Displays.Count - 1);
        // A link that no longer resolves (a deleted layout, a display that is gone) is dropped rather
        // than left dangling; a layout can only link to another display.
        foreach (var display in Displays)
        {
            foreach (var layout in display.Layouts)
            {
                if (string.IsNullOrWhiteSpace(layout.LinkDisplayId) || string.IsNullOrWhiteSpace(layout.LinkLayoutId))
                {
                    layout.LinkDisplayId = null;
                    layout.LinkLayoutId = null;
                    continue;
                }
                var target = FindDisplay(layout.LinkDisplayId);
                var valid = target is not null
                    && !string.Equals(target.Id, display.Id, StringComparison.OrdinalIgnoreCase)
                    && target.Layouts.Any(candidate => string.Equals(candidate.Id, layout.LinkLayoutId, StringComparison.OrdinalIgnoreCase));
                if (!valid)
                {
                    layout.LinkDisplayId = null;
                    layout.LinkLayoutId = null;
                }
            }
        }
    }

    /// <summary>#RGB, #ARGB, #RRGGBB or #AARRGGBB, so a bad entry can never blank the canvas.</summary>
    static bool IsColor(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length < 4 || text[0] != '#') return false;
        return text[1..].Length is 3 or 4 or 6 or 8 && text[1..].All(Uri.IsHexDigit);
    }

    /// <summary>Re-sizes the layout being designed and scales every element with it, so a 720p design survives a move to 4K.</summary>
    public void Resize(int width, int height)
    {
        width = (int)Math.Clamp(width, 320, 7680);
        height = (int)Math.Clamp(height, 240, 7680);
        if (width == Layout.Width && height == Layout.Height) return;
        var scaleX = (double)width / Layout.Width;
        var scaleY = (double)height / Layout.Height;
        var scale = Math.Min(scaleX, scaleY);
        Update(layout =>
        {
            foreach (var item in layout.Elements)
            {
                item.X *= scaleX;
                item.Y *= scaleY;
                item.Width *= scaleX;
                item.Height *= scaleY;
                item.FontSize *= scale;
            }
            layout.Width = width;
            layout.Height = height;
        });
    }

    // ---------- layouts ----------

    /// <summary>Selects the layout being designed. Never touches what is on air until PublishLayout.</summary>
    public void SelectLayout(int index, bool publish = false)
    {
        var display = ActiveDisplay;
        var next = Math.Clamp(index, 0, display.Layouts.Count - 1);
        if (next == display.ActiveIndex && !publish) return;
        display.ActiveIndex = next;
        if (publish) display.PublishedIndex = next;
        Save(); Changed?.Invoke();
    }

    /// <summary>Sends the previewed layout to the audience output.</summary>
    public void PublishLayout() => PublishLayout(ActiveDisplay.Id, ActiveDisplay.ActiveIndex);

    /// <summary>
    /// Puts one display's chosen layout on air. Each display publishes on its own, so a lower-third
    /// display never has to show the same layout as the main scorebug.
    /// </summary>
    public void PublishLayout(string? displayId, int layoutIndex)
    {
        var display = FindDisplay(displayId) ?? ActiveDisplay;
        var next = Math.Clamp(layoutIndex, 0, display.Layouts.Count - 1);
        if (display.PublishedIndex == next) return;
        display.PublishedIndex = next;
        Save(); Changed?.Invoke();
    }

    public int AddLayout(string? name = null, bool copyActive = false)
    {
        var display = ActiveDisplay;
        var layout = copyActive ? Layout.Copy(name) : new ScorebugLayout { Name = string.IsNullOrWhiteSpace(name) ? $"Layout {display.Layouts.Count + 1}" : name.Trim() };
        if (string.IsNullOrWhiteSpace(layout.Name)) layout.Name = $"Layout {display.Layouts.Count + 1}";
        display.Layouts.Add(layout);
        display.ActiveIndex = display.Layouts.Count - 1;
        Save(); Changed?.Invoke();
        return display.ActiveIndex;
    }

    public bool RemoveLayout(int index)
    {
        var display = ActiveDisplay;
        if (display.Layouts.Count <= 1 || index < 0 || index >= display.Layouts.Count) return false;
        var removed = display.Layouts[index];
        display.Layouts.RemoveAt(index);
        if (display.ActiveIndex >= display.Layouts.Count) display.ActiveIndex = display.Layouts.Count - 1;
        if (display.PublishedIndex >= display.Layouts.Count) display.PublishedIndex = display.Layouts.Count - 1;
        if (display.ActiveIndex > index) display.ActiveIndex--;
        if (display.PublishedIndex > index) display.PublishedIndex--;
        // A link that pointed at the deleted layout is broken, not silently aimed somewhere else.
        foreach (var other in Displays)
            foreach (var layout in other.Layouts)
                if (string.Equals(layout.LinkLayoutId, removed.Id, StringComparison.OrdinalIgnoreCase))
                {
                    layout.LinkDisplayId = null;
                    layout.LinkLayoutId = null;
                }
        Save(); Changed?.Invoke();
        return true;
    }

    public void RenameLayout(int index, string name)
    {
        var layouts = ActiveDisplay.Layouts;
        if (index < 0 || index >= layouts.Count || string.IsNullOrWhiteSpace(name)) return;
        layouts[index].Name = name.Trim();
        Save(); Changed?.Invoke();
    }

    /// <summary>
    /// Moves a layout to another display, artwork and identity included. The display being edited
    /// becomes the target, so the layout stays on screen where the operator just sent it.
    /// </summary>
    public bool MoveLayoutToDisplay(int index, string targetDisplayId)
    {
        var source = ActiveDisplay;
        var target = FindDisplay(targetDisplayId);
        if (target is null || string.Equals(target.Id, source.Id, StringComparison.OrdinalIgnoreCase)) return false;
        // Every display keeps one layout; the source cannot be left empty.
        if (source.Layouts.Count <= 1 || index < 0 || index >= source.Layouts.Count) return false;
        var layout = source.Layouts[index];
        source.Layouts.RemoveAt(index);
        target.Layouts.Add(layout);
        if (source.ActiveIndex >= source.Layouts.Count) source.ActiveIndex = source.Layouts.Count - 1;
        if (source.PublishedIndex >= source.Layouts.Count) source.PublishedIndex = source.Layouts.Count - 1;
        if (source.ActiveIndex > index) source.ActiveIndex--;
        if (source.PublishedIndex > index) source.PublishedIndex--;
        target.ActiveIndex = target.Layouts.Count - 1;
        ActiveDisplayIndex = Displays.FindIndex(display => string.Equals(display.Id, target.Id, StringComparison.OrdinalIgnoreCase));
        Validate();
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Copies a layout onto another display. The copy is independent: new layout and element ids,
    /// so editing it there never touches the original.
    /// </summary>
    public int CopyLayoutToDisplay(int index, string targetDisplayId)
    {
        var source = ActiveDisplay;
        var target = FindDisplay(targetDisplayId);
        if (target is null || string.Equals(target.Id, source.Id, StringComparison.OrdinalIgnoreCase)) return -1;
        if (index < 0 || index >= source.Layouts.Count) return -1;
        target.Layouts.Add(source.Layouts[index].Copy());
        target.ActiveIndex = target.Layouts.Count - 1;
        Validate();
        Save();
        Changed?.Invoke();
        return target.Layouts.Count - 1;
    }

    /// <summary>Points this layout at a layout on another display, or clears the link.</summary>
    public void SetLink(string? displayId, int layoutIndex, string? linkDisplayId, string? linkLayoutId)
    {
        var display = FindDisplay(displayId) ?? ActiveDisplay;
        if (layoutIndex < 0 || layoutIndex >= display.Layouts.Count) return;
        var layout = display.Layouts[layoutIndex];
        layout.LinkDisplayId = string.IsNullOrWhiteSpace(linkDisplayId) ? null : linkDisplayId;
        layout.LinkLayoutId = string.IsNullOrWhiteSpace(linkLayoutId) ? null : linkLayoutId;
        Validate();
        Save(); Changed?.Invoke();
    }

    /// <summary>Sets how one layout crosses in on air. The hand-over travels with the layout.</summary>
    public void SetLayoutTransition(int displayIndex, int layoutIndex, LayoutTransition transition, double seconds)
    {
        if (displayIndex < 0 || displayIndex >= Displays.Count) return;
        var display = Displays[displayIndex];
        if (layoutIndex < 0 || layoutIndex >= display.Layouts.Count) return;
        var layout = display.Layouts[layoutIndex];
        var clamped = Math.Clamp(double.IsFinite(seconds) ? seconds : 0, 0, 5);
        if (layout.Transition == transition && Math.Abs(layout.TransitionSeconds - clamped) < 0.0001) return;
        layout.Transition = transition;
        layout.TransitionSeconds = clamped;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Sets the hand-over of the layout being designed.</summary>
    public void SetTransition(LayoutTransition transition, double seconds) =>
        SetLayoutTransition(ActiveDisplayIndex, ActiveDisplay.ActiveIndex, transition, seconds);

    /// <summary>A deep copy of one display, for writing an .spd file without touching the game.</summary>
    public ScorebugDisplay? SnapshotDisplay(int index) => index < 0 || index >= Displays.Count
        ? null
        : JsonSerializer.Deserialize<ScorebugDisplay>(JsonSerializer.Serialize(Displays[index]));

    /// <summary>A deep copy of one layout, for writing an .spl file.</summary>
    public ScorebugLayout? SnapshotLayout(int index)
    {
        var layouts = ActiveDisplay.Layouts;
        return index < 0 || index >= layouts.Count
            ? null
            : JsonSerializer.Deserialize<ScorebugLayout>(JsonSerializer.Serialize(layouts[index]));
    }

    /// <summary>
    /// Adds an imported layout to the display being edited. Its ids are fresh, so importing the same
    /// file twice leaves two independent layouts rather than two views of one.
    /// </summary>
    public int ImportLayout(ScorebugLayout source, string? name = null)
    {
        var layout = source.Copy(name ?? source.Name);
        if (string.IsNullOrWhiteSpace(layout.Name)) layout.Name = $"Layout {ActiveDisplay.Layouts.Count + 1}";
        ActiveDisplay.Layouts.Add(layout);
        ActiveDisplay.ActiveIndex = ActiveDisplay.Layouts.Count - 1;
        Validate();
        Save();
        Changed?.Invoke();
        return ActiveDisplay.ActiveIndex;
    }

    /// <summary>
    /// Adds a whole imported display — its layouts, their order and what is on air — and selects it.
    /// Layouts and elements get fresh ids, and links out of the file are dropped because the display
    /// they pointed at is not part of it.
    /// </summary>
    public int ImportDisplay(ScorebugDisplay source, string? name = null)
    {
        var copy = JsonSerializer.Deserialize<ScorebugDisplay>(JsonSerializer.Serialize(source)) ?? new ScorebugDisplay();
        copy.Id = Guid.NewGuid().ToString("N");
        if (!string.IsNullOrWhiteSpace(name)) copy.Name = name.Trim();
        if (copy.Layouts.Count == 0) copy.Layouts.Add(new ScorebugLayout { Name = "Layout 1" });
        foreach (var layout in copy.Layouts)
        {
            layout.Id = Guid.NewGuid().ToString("N");
            layout.LinkDisplayId = null;
            layout.LinkLayoutId = null;
            foreach (var element in layout.Elements) element.Id = Guid.NewGuid().ToString("N");
        }
        copy.ActiveIndex = Math.Clamp(copy.ActiveIndex, 0, copy.Layouts.Count - 1);
        copy.PublishedIndex = Math.Clamp(copy.PublishedIndex, 0, copy.Layouts.Count - 1);
        Displays.Add(copy);
        ActiveDisplayIndex = Displays.Count - 1;
        Validate();
        Save();
        Changed?.Invoke();
        return ActiveDisplayIndex;
    }

    /// <summary>
    /// Starts over with a single display and the factory layout. A new game is a blank slate: no
    /// layouts or displays from the previous game, and the same design every fresh install opens with.
    /// </summary>
    public void ResetToDefault()
    {
        Displays = [DefaultDisplay()];
        ActiveDisplayIndex = 0;
        Save(); Changed?.Invoke();
    }
}
