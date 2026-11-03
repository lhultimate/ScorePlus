namespace Scoreboard.Core;

/// <summary>One stop in a gradient: where it sits (0–1) and the colour it holds.</summary>
public sealed class GradientStopModel
{
    public double Offset { get; set; }
    public string Color { get; set; } = "#FFFFFFFF";

    public GradientStopModel() { }

    public GradientStopModel(double offset, string color)
    {
        Offset = offset;
        Color = color;
    }

    public GradientStopModel Copy() => new(Offset, Color);
}

/// <summary>
/// A gradient with any number of stops, running in a straight line or out from a centre. Used for
/// a background plate and for the text itself, so a score can be filled as well as a box.
/// </summary>
public sealed class ScorebugGradient
{
    public bool Enabled { get; set; }
    /// <summary>Out from the centre rather than along a line.</summary>
    public bool Radial { get; set; }
    /// <summary>Direction in degrees: 0 runs left to right, 90 top to bottom.</summary>
    public double Angle { get; set; } = 90;
    public double CenterX { get; set; } = 0.5;
    public double CenterY { get; set; } = 0.5;
    public double RadiusX { get; set; } = 0.5;
    public double RadiusY { get; set; } = 0.5;
    public List<GradientStopModel> Stops { get; set; } = [];

    public static ScorebugGradient TwoStop(string from, string to, double angle = 90) => new()
    {
        Enabled = true,
        Angle = angle,
        Stops = [new(0, from), new(1, to)]
    };

    public ScorebugGradient Copy()
    {
        var copy = new ScorebugGradient
        {
            Enabled = Enabled,
            Radial = Radial,
            Angle = Angle,
            CenterX = CenterX,
            CenterY = CenterY,
            RadiusX = RadiusX,
            RadiusY = RadiusY,
            Stops = Stops.Select(stop => stop.Copy()).ToList()
        };
        copy.Normalize();
        return copy;
    }

    public bool IsUsable => Enabled && Stops.Count >= 2;

    /// <summary>A comparable description, so a view only rebuilds a brush when the paint changed.</summary>
    public string Signature() => !IsUsable
        ? "off"
        : $"g|{(Radial ? "r" : "l")}|{Angle:0.###}|{CenterX:0.###}|{CenterY:0.###}|{RadiusX:0.###}|{RadiusY:0.###}|" +
          string.Join("|", Stops.Select(stop => $"{stop.Offset:0.###}:{stop.Color}"));

    public void Normalize()
    {
        Stops ??= [];
        Stops = Stops.Take(12).Where(stop => stop is not null).ToList();
        foreach (var stop in Stops)
        {
            stop.Offset = Math.Clamp(double.IsFinite(stop.Offset) ? stop.Offset : 0, 0, 1);
            if (!IsColor(stop.Color)) stop.Color = "#FFFFFFFF";
        }
        Stops = Stops.OrderBy(stop => stop.Offset).ToList();
        // A gradient needs two ends to be a gradient at all.
        if (Stops.Count == 0) Stops = [new(0, "#FFFFFFFF"), new(1, "#FF38D7A3")];
        if (Stops.Count == 1) Stops.Add(new GradientStopModel(1, Stops[0].Color));
        Angle = Math.Clamp(double.IsFinite(Angle) ? Angle : 90, -360, 360);
        CenterX = Math.Clamp(double.IsFinite(CenterX) ? CenterX : 0.5, -1, 2);
        CenterY = Math.Clamp(double.IsFinite(CenterY) ? CenterY : 0.5, -1, 2);
        RadiusX = Math.Clamp(double.IsFinite(RadiusX) ? RadiusX : 0.5, 0.05, 3);
        RadiusY = Math.Clamp(double.IsFinite(RadiusY) ? RadiusY : 0.5, 0.05, 3);
    }

    internal static bool IsColor(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length < 4 || text[0] != '#') return false;
        return text[1..].Length is 3 or 4 or 6 or 8 && text[1..].All(Uri.IsHexDigit);
    }
}

/// <summary>
/// A surface finish laid over an element's paint. WPF has no shader pipeline here, so the knobs
/// build a material from layered gradients, a tiled grain and tint: enough to make a plate read as
/// brushed metal, glass, fabric or stone, and enough to keep a score's own paint lively.
/// </summary>
public sealed class ScorebugTexture
{
    public bool Enabled { get; set; }

    /// <summary>A starting point that sets the knobs below; every knob stays editable.</summary>
    public string Kind { get; set; } = "Matte";

    /// <summary>How matte the surface is: softens highlights and lifts the mid-tones.</summary>
    public double Roughness { get; set; } = 0.45;
    /// <summary>How broad and bright the reflected highlight is.</summary>
    public double Glossiness { get; set; } = 0.3;
    /// <summary>The colour the surface picks up from its surroundings.</summary>
    public double Ambience { get; set; } = 0.35;
    /// <summary>A narrow specular streak across the surface.</summary>
    public double Shine { get; set; } = 0.25;
    /// <summary>Turns the surface metal-like: harder contrast, tinted highlights.</summary>
    public double Metallic { get; set; }
    /// <summary>Visible speckle, from a fine grain to coarse noise.</summary>
    public double Grain { get; set; } = 0.1;
    /// <summary>A raised relief edge, like stamped foil.</summary>
    public double Emboss { get; set; } = 0.05;
    /// <summary>A chamfered edge catching light on one side and shadow on the other.</summary>
    public double Bevel { get; set; } = 0.08;
    /// <summary>A broad, soft sheen with no hard edge, like satin.</summary>
    public double Satin { get; set; } = 0.1;
    /// <summary>A colour shift across the surface, like an oil slick or hologram.</summary>
    public double Iridescence { get; set; }
    /// <summary>How much of the finish is laid over the paint at all.</summary>
    public double Opacity { get; set; } = 0.5;
    /// <summary>Size of the grain and banding pattern.</summary>
    public double Scale { get; set; } = 1;
    /// <summary>Overall contrast of the finish.</summary>
    public double Contrast { get; set; } = 0.1;
    /// <summary>Direction the light comes from, in degrees.</summary>
    public double Angle { get; set; } = 135;
    /// <summary>An optional colour cast over the surface.</summary>
    public string Tint { get; set; } = "#FFFFFFFF";
    public double TintAmount { get; set; }

    public static IReadOnlyList<string> Kinds =>
    [
        "Matte", "Metal", "Chrome", "Glass", "Frost", "Fabric", "Velvet",
        "Stone", "Concrete", "Paper", "Hologram"
    ];

    public static ScorebugTexture ForKind(string kind)
    {
        var texture = new ScorebugTexture { Enabled = true, Kind = kind };
        switch (kind)
        {
            case "Metal":
                texture.Roughness = 0.28; texture.Glossiness = 0.62; texture.Ambience = 0.2;
                texture.Shine = 0.55; texture.Metallic = 0.8; texture.Grain = 0.05;
                texture.Emboss = 0.18; texture.Bevel = 0.35; texture.Satin = 0.05;
                texture.Contrast = 0.35; texture.Angle = 90; texture.Opacity = 0.62;
                break;
            case "Chrome":
                texture.Roughness = 0.1; texture.Glossiness = 0.85; texture.Ambience = 0.1;
                texture.Shine = 0.8; texture.Metallic = 1; texture.Grain = 0.02;
                texture.Emboss = 0.1; texture.Bevel = 0.5; texture.Satin = 0; texture.Iridescence = 0.15;
                texture.Contrast = 0.55; texture.Angle = 100; texture.Opacity = 0.7;
                break;
            case "Glass":
                texture.Roughness = 0.15; texture.Glossiness = 0.7; texture.Ambience = 0.55;
                texture.Shine = 0.45; texture.Metallic = 0.1; texture.Grain = 0.02;
                texture.Emboss = 0; texture.Bevel = 0.2; texture.Satin = 0.2;
                texture.Contrast = 0.12; texture.Angle = 120; texture.Opacity = 0.4;
                break;
            case "Frost":
                texture.Roughness = 0.9; texture.Glossiness = 0.12; texture.Ambience = 0.7;
                texture.Shine = 0.06; texture.Grain = 0.35; texture.Satin = 0.4;
                texture.Contrast = 0.05; texture.Scale = 0.6; texture.Opacity = 0.5;
                break;
            case "Fabric":
                texture.Roughness = 0.85; texture.Glossiness = 0.08; texture.Ambience = 0.45;
                texture.Shine = 0.04; texture.Grain = 0.45; texture.Satin = 0.55;
                texture.Emboss = 0.12; texture.Scale = 2.4; texture.Opacity = 0.55;
                break;
            case "Velvet":
                texture.Roughness = 0.95; texture.Glossiness = 0.05; texture.Ambience = 0.3;
                texture.Shine = 0.02; texture.Grain = 0.2; texture.Satin = 0.75;
                texture.Scale = 3; texture.Contrast = 0.15; texture.Opacity = 0.55;
                break;
            case "Stone":
                texture.Roughness = 0.92; texture.Glossiness = 0.15; texture.Ambience = 0.25;
                texture.Grain = 0.6; texture.Emboss = 0.28; texture.Bevel = 0.25;
                texture.Scale = 1.4; texture.Contrast = 0.25; texture.Opacity = 0.62;
                break;
            case "Concrete":
                texture.Roughness = 1; texture.Glossiness = 0.03; texture.Ambience = 0.2;
                texture.Grain = 0.8; texture.Emboss = 0.35; texture.Scale = 0.8;
                texture.Contrast = 0.2; texture.Opacity = 0.65;
                break;
            case "Paper":
                texture.Roughness = 0.88; texture.Glossiness = 0.05; texture.Ambience = 0.6;
                texture.Grain = 0.25; texture.Emboss = 0.08; texture.Satin = 0.15;
                texture.Scale = 1.8; texture.Opacity = 0.35;
                break;
            case "Hologram":
                texture.Roughness = 0.3; texture.Glossiness = 0.6; texture.Ambience = 0.5;
                texture.Shine = 0.4; texture.Metallic = 0.35; texture.Iridescence = 0.85;
                texture.Grain = 0.06; texture.Contrast = 0.3; texture.Angle = 45;
                texture.Opacity = 0.55;
                break;
            default: // Matte
                texture.Roughness = 0.6; texture.Glossiness = 0.1; texture.Ambience = 0.4;
                texture.Shine = 0.12; texture.Grain = 0.08; texture.Contrast = 0.08;
                texture.Opacity = 0.45;
                break;
        }
        return texture;
    }

    public ScorebugTexture Copy() => new()
    {
        Enabled = Enabled, Kind = Kind, Roughness = Roughness, Glossiness = Glossiness,
        Ambience = Ambience, Shine = Shine, Metallic = Metallic, Grain = Grain,
        Emboss = Emboss, Bevel = Bevel, Satin = Satin, Iridescence = Iridescence,
        Opacity = Opacity, Scale = Scale, Contrast = Contrast, Angle = Angle,
        Tint = Tint, TintAmount = TintAmount
    };

    /// <summary>A comparable description, so a view only rebuilds a brush when the finish changed.</summary>
    public string Signature() => !Enabled
        ? "off"
        : $"{Kind}|{Roughness:0.###}|{Glossiness:0.###}|{Ambience:0.###}|{Shine:0.###}|{Metallic:0.###}|" +
          $"{Grain:0.###}|{Emboss:0.###}|{Bevel:0.###}|{Satin:0.###}|{Iridescence:0.###}|{Opacity:0.###}|" +
          $"{Scale:0.###}|{Contrast:0.###}|{Angle:0.###}|{Tint}|{TintAmount:0.###}";

    public void Normalize()
    {
        Roughness = Clamp(Roughness, 0, 1, 0.45);
        Glossiness = Clamp(Glossiness, 0, 1, 0.3);
        Ambience = Clamp(Ambience, 0, 1, 0.35);
        Shine = Clamp(Shine, 0, 1, 0.25);
        Metallic = Clamp(Metallic, 0, 1, 0);
        Grain = Clamp(Grain, 0, 1, 0.1);
        Emboss = Clamp(Emboss, 0, 1, 0.05);
        Bevel = Clamp(Bevel, 0, 1, 0.08);
        Satin = Clamp(Satin, 0, 1, 0.1);
        Iridescence = Clamp(Iridescence, 0, 1, 0);
        Opacity = Clamp(Opacity, 0, 1, 0.5);
        Scale = Clamp(Scale, 0.1, 12, 1);
        Contrast = Clamp(Contrast, 0, 1, 0.1);
        Angle = Clamp(Angle, -360, 360, 135);
        TintAmount = Clamp(TintAmount, 0, 1, 0);
        if (!ScorebugGradient.IsColor(Tint)) Tint = "#FFFFFFFF";
        if (string.IsNullOrWhiteSpace(Kind) || !Kinds.Contains(Kind)) Kind = "Matte";
    }

    static double Clamp(double value, double min, double max, double fallback) =>
        Math.Clamp(double.IsFinite(value) ? value : fallback, min, max);
}
