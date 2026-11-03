using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// Builds every brush the graphic paints with: flat colours, multi-stop gradients, radial gradients,
/// the material finish on top of them, and the per-layer opacity that lets a plate, its outline and
/// its text fade independently. The studio, the previews and the audience output all come through
/// here, so a finish looks the same on every screen.
/// </summary>
internal static class Paint
{
    public static Color ColorOf(string? value, Color fallback = default)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(value)!;
            return parsed;
        }
        catch { return fallback; }
    }

    public static Color Fade(Color color, double factor)
    {
        factor = Math.Clamp(double.IsFinite(factor) ? factor : 1, 0, 1);
        return Color.FromArgb((byte)Math.Round(color.A * factor), color.R, color.G, color.B);
    }

    public static Brush WithAlpha(Brush brush, double factor)
    {
        factor = Math.Clamp(double.IsFinite(factor) ? factor : 1, 0, 1);
        if (factor >= 0.999) return brush;
        switch (brush)
        {
            case SolidColorBrush solid:
                var faded = new SolidColorBrush(Fade(solid.Color, factor));
                faded.Freeze();
                return faded;
            case GradientBrush gradient:
                var copy = gradient.Clone();
                foreach (var stop in copy.GradientStops) stop.Color = Fade(stop.Color, factor);
                copy.Freeze();
                return copy;
            case ImageBrush image:
                var fadedImage = image.Clone();
                fadedImage.Opacity = image.Opacity * factor;
                fadedImage.Freeze();
                return fadedImage;
            case DrawingBrush drawing:
                var fadedDrawing = drawing.Clone();
                fadedDrawing.Opacity = drawing.Opacity * factor;
                fadedDrawing.Freeze();
                return fadedDrawing;
            default:
                var translucent = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * factor), 255, 255, 255));
                translucent.Freeze();
                return translucent;
        }
    }

    /// <summary>
    /// The text fill: a flat colour, a gradient, or either of those carrying a material finish.
    /// The layer's own opacity is applied by the text block (so it fades exactly once), which is
    /// why the brushes here are built at full alpha.
    /// </summary>
    public static Brush Text(ScorebugElement item, GameState? state)
    {
        var flat = ColorOf(state?.ColorFor(item.ForegroundSource) ?? item.Foreground, Colors.White);
        var paint = item.TextGradientPaint;
        var brush = paint is { IsUsable: true }
            ? Gradient(paint, flat, 1)
            : item.TextTexture is { Enabled: true }
                ? Linear([(0, flat), (1, flat)], 90, 1)
                : Solid(flat, 1);
        if (item.TextTexture is { Enabled: true } texture) return ApplyTextureToBrush(brush, texture, flat);
        return brush;
    }

    /// <summary>The background: flat, multi-stop or radial, carrying its finish and its own opacity.</summary>
    public static Brush Background(ScorebugElement item, GameState? state)
    {
        var flat = ColorOf(state?.ColorFor(item.BackgroundSource) ?? item.Background, Colors.Transparent);
        var opacity = item.BackgroundOpacity;
        var paint = item.BackgroundGradientPaint;
        Brush brush;
        if (paint is { IsUsable: true })
            brush = Gradient(paint, flat, opacity);
        else if (item.BackgroundGradient)
        {
            // A layout written before multi-stop gradients: two colours and an angle.
            var start = ColorOf(state?.ColorFor(item.BackgroundSource) ?? item.Background, Colors.Transparent);
            var end = ColorOf(state?.ColorFor(item.BackgroundSource2) ?? item.BackgroundColor2, Colors.Transparent);
            brush = Linear([(0, start), (1, end)], item.GradientAngle, opacity);
        }
        else
            brush = Solid(flat, opacity);
        if (item.BackgroundTexture is { Enabled: true } texture) return TexturedBackground(brush, texture, flat);
        return brush;
    }

    /// <summary>The outline, faded by its own opacity rather than the element's.</summary>
    public static Brush Border(ScorebugElement item, GameState? state)
    {
        if (item.BorderThickness <= 0) return Brushes.Transparent;
        var colour = ColorOf(state?.ColorFor(item.BorderSource) ?? item.BorderColor, Colors.White);
        return Solid(colour, item.BorderOpacity);
    }

    /// <summary>A comparable description so views only rebuild a brush when the paint changed.</summary>
    public static string Signature(ScorebugElement item, GameState? state) =>
        $"{item.BackgroundOpacity:0.###}|{item.BackgroundTexture?.Signature() ?? "off"}|" +
        (item.BackgroundGradientPaint is { IsUsable: true } paint
            ? paint.Signature()
            : item.BackgroundGradient
                ? $"legacy|{state?.ColorFor(item.BackgroundSource) ?? item.Background}|{state?.ColorFor(item.BackgroundSource2) ?? item.BackgroundColor2}|{item.GradientAngle:0.###}"
                : state?.ColorFor(item.BackgroundSource) ?? item.Background);

    /// <summary>A comparable description of the text fill, for the same reason.</summary>
    public static string TextSignature(ScorebugElement item, GameState? state) =>
        $"{item.TextOpacity:0.###}|{item.TextTexture?.Signature() ?? "off"}|" +
        (item.TextGradientPaint is { IsUsable: true } paint
            ? paint.Signature()
            : state?.ColorFor(item.ForegroundSource) ?? item.Foreground);

    static Brush Solid(Color colour, double opacity)
    {
        var brush = new SolidColorBrush(Fade(colour, opacity));
        brush.Freeze();
        return brush;
    }

    static Brush Gradient(ScorebugGradient paint, Color fallback, double opacity)
    {
        var stops = paint.Stops
            .Where(stop => stop is not null)
            .Select(stop => (stop.Offset, ColorOf(stop.Color, fallback)))
            .ToList();
        if (stops.Count < 2) return Solid(fallback, opacity);
        if (!paint.Radial) return Linear(stops, paint.Angle, opacity);
        var brush = new RadialGradientBrush
        {
            Center = new Point(paint.CenterX, paint.CenterY),
            GradientOrigin = new Point(paint.CenterX, paint.CenterY),
            RadiusX = paint.RadiusX,
            RadiusY = paint.RadiusY
        };
        foreach (var (offset, colour) in stops)
            brush.GradientStops.Add(new GradientStop(Fade(colour, opacity), Math.Clamp(offset, 0, 1)));
        brush.Freeze();
        return brush;
    }

    static Brush Linear(IReadOnlyList<(double Offset, Color Colour)> stops, double angle, double opacity)
    {
        var radians = (((angle % 360) + 360) % 360) * Math.PI / 180.0;
        var dx = Math.Cos(radians) / 2;
        var dy = Math.Sin(radians) / 2;
        var brush = new LinearGradientBrush(
            new GradientStopCollection(stops.Select(stop => new GradientStop(Fade(stop.Colour, opacity), Math.Clamp(stop.Offset, 0, 1)))),
            new Point(0.5 - dx, 0.5 - dy),
            new Point(0.5 + dx, 0.5 + dy));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// The material finish on a plate: a highlight band, an ambient wash, a satin sheen, a metallic
    /// edge, an iridescent shift and a tiled grain. WPF has no shading pipeline here, so these layer
    /// into a drawing that reads as a surface rather than a flat rectangle.
    /// </summary>
    static Brush TexturedBackground(Brush baseBrush, ScorebugTexture texture, Color flat)
    {
        var textureColour = ColorOf(texture.Tint, flat);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(baseBrush, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));

        // The broad bands: a lit edge, a shadowed body, a highlight and a reflected ambience.
        var bands = new GradientStopCollection();
        void Band(double offset, double light)
        {
            var colour = MotionColors.Shift(ToHex(Mix(textureColour, flat, texture.TintAmount)), light);
            var alpha = (byte)Math.Round(255 * Math.Clamp(texture.Opacity * (0.35 + Math.Abs(light) * 0.65), 0, 1));
            bands.Add(new GradientStop(Color.FromArgb(alpha, ColorOf(colour).R, ColorOf(colour).G, ColorOf(colour).B), Math.Clamp(offset, 0, 1)));
        }
        var satin = Math.Clamp(texture.Satin, 0, 1);
        var shine = Math.Clamp(texture.Shine + texture.Glossiness * 0.5, 0, 1);
        bands.Add(new GradientStop(Colors.Transparent, 0));
        Band(0.14, 0.35 + shine * 0.45);
        Band(0.34 + satin * 0.08, -(0.2 + texture.Roughness * 0.45));
        Band(0.52, 0.15 + texture.Ambience * 0.35 + satin * 0.25);
        Band(0.72, -(0.1 + texture.Roughness * 0.25));
        Band(0.92, 0.25 + shine * 0.55 + texture.Metallic * 0.2);
        bands.Add(new GradientStop(Colors.Transparent, 1));
        var radians = (((texture.Angle % 360) + 360) % 360) * Math.PI / 180.0;
        var dx = Math.Cos(radians) / 2;
        var dy = Math.Sin(radians) / 2;
        var bandBrush = new LinearGradientBrush(bands, new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
        bandBrush.Freeze();
        group.Children.Add(new GeometryDrawing(bandBrush, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));

        // Emboss / bevel: a thin lit edge on one side and a shadow on the other.
        var relief = Math.Clamp(texture.Emboss + texture.Bevel * 0.6, 0, 1);
        if (relief > 0.01)
        {
            var light = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * relief * 0.55), 255, 255, 255));
            var shadow = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * relief * 0.6), 0, 0, 0));
            light.Freeze(); shadow.Freeze();
            var edge = Math.Max(0.004, relief * 0.035);
            group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 0, 1, edge))));
            group.Children.Add(new GeometryDrawing(shadow, null, new RectangleGeometry(new Rect(0, 1 - edge, 1, edge))));
        }

        // Iridescence: a hue-shifted wash across the surface.
        if (texture.Iridescence > 0.01)
        {
            var iridescent = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            var baseHex = ToHex(flat);
            iridescent.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            iridescent.GradientStops.Add(new GradientStop(ColorOf(MotionColors.Hue(baseHex, 0.18), flat) is var h1
                ? Color.FromArgb((byte)Math.Round(255 * texture.Iridescence * 0.55), h1.R, h1.G, h1.B) : Colors.Transparent, 0.38));
            iridescent.GradientStops.Add(new GradientStop(ColorOf(MotionColors.Hue(baseHex, 0.55), flat) is var h2
                ? Color.FromArgb((byte)Math.Round(255 * texture.Iridescence * 0.5), h2.R, h2.G, h2.B) : Colors.Transparent, 0.68));
            iridescent.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            iridescent.Freeze();
            group.Children.Add(new GeometryDrawing(iridescent, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        }

        // Grain: a stable speckle, so the same finish looks the same every time it is drawn.
        if (texture.Grain > 0.01)
        {
            var dots = new GeometryGroup();
            var random = new Random(texture.Signature().GetHashCode(StringComparison.Ordinal));
            var count = (int)Math.Round(30 + texture.Grain * 220);
            var size = 0.0016 * Math.Max(0.4, texture.Scale);
            for (var index = 0; index < count; index++)
            {
                var x = random.NextDouble();
                var y = random.NextDouble();
                dots.Children.Add(new EllipseGeometry(new Point(x, y), size * (1 + random.NextDouble()), size * (1 + random.NextDouble())));
            }
            var grainColour = texture.Metallic > 0.4 ? Colors.White : Colors.Black;
            var grain = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(texture.Grain * 0.45, 0, 1)), grainColour.R, grainColour.G, grainColour.B));
            grain.Freeze();
            group.Children.Add(new GeometryDrawing(grain, null, dots));
        }

        // Contrast: a soft black wash at the foot of the plate.
        if (texture.Contrast > 0.01)
        {
            var wash = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            wash.GradientStops.Add(new GradientStop(Colors.Transparent, 0.45));
            wash.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(255 * texture.Contrast * 0.55), 0, 0, 0), 1));
            wash.Freeze();
            group.Children.Add(new GeometryDrawing(wash, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        }

        group.Freeze();
        var brush = new DrawingBrush(group)
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.None,
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = new Rect(0, 0, 1, 1)
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Text keeps its shape, so its finish is folded into the gradient's stops.</summary>
    static Brush ApplyTextureToBrush(Brush brush, ScorebugTexture texture, Color flat)
    {
        if (brush is not GradientBrush gradient) return brush;
        var copy = gradient.Clone();
        copy.GradientStops.Clear();
        var baseHex = ToHex(flat);
        var textureColour = ColorOf(texture.Tint, flat);
        void Add(double offset, string colour, double amount)
        {
            var blended = MotionColors.Mix(baseHex, ToHex(textureColour), texture.TintAmount);
            var shifted = ColorOf(MotionColors.Shift(blended, amount), flat);
            copy.GradientStops.Add(new GradientStop(Color.FromArgb(shifted.A, shifted.R, shifted.G, shifted.B), Math.Clamp(offset, 0, 1)));
        }
        Add(0, baseHex, 0.1 * texture.Ambience);
        Add(0.18, baseHex, (texture.Shine + texture.Glossiness * 0.5) * 0.8);
        Add(0.44, baseHex, -(texture.Roughness * 0.55));
        Add(0.58, baseHex, (texture.Satin + texture.Ambience) * 0.4);
        Add(0.8, baseHex, (texture.Metallic * 0.6 + texture.Shine * 0.4));
        Add(1, baseHex, 0.1 * texture.Ambience);
        if (texture.Iridescence > 0.01)
            foreach (var stop in copy.GradientStops.ToList())
                stop.Color = ColorOf(MotionColors.Hue(ToHex(stop.Color), texture.Iridescence * 0.3), stop.Color);
        copy.Opacity = Math.Clamp(texture.Opacity + 0.45, 0, 1);
        copy.Freeze();
        return copy;
    }

    static Color Mix(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(a.A * (1 - amount) + b.A * amount),
            (byte)Math.Round(a.R * (1 - amount) + b.R * amount),
            (byte)Math.Round(a.G * (1 - amount) + b.G * amount),
            (byte)Math.Round(a.B * (1 - amount) + b.B * amount));
    }

    public static string ToHex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>The image for a still or logo, loaded without holding the file open.</summary>
    public static BitmapImage? TryImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}
