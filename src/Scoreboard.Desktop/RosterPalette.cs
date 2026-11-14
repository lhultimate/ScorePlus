using System.Windows;
using System.Windows.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// The roster desk's colours. In Dark it is built from neutral blacks and greys, not the blue-tinted
/// navy used by older panels, so the tab reads as part of the black operator surface. Both themes
/// are painted from here, and the panels pick the brushes up through DynamicResource so rows built
/// later are themed too.
/// </summary>
internal static class RosterPalette
{
    public static void Apply(string theme, ResourceDictionary resources)
    {
        var light = ThemeManager.Resolve(theme) == "Light";
        // Dark is a true black page with neutral cards, wells and borders. Text stays near-white
        // and muted labels stay above 4.5:1 on every one of those surfaces.
        Set(resources, "RosterPanelBrush", light ? "#F3F5F8" : "#000000");
        Set(resources, "RosterSurfaceBrush", light ? "#FFFFFF" : "#0A0A0A");
        Set(resources, "RosterSurfaceAltBrush", light ? "#E5EAF0" : "#161616");
        Set(resources, "RosterInputBrush", light ? "#F3F5F8" : "#050505");
        Set(resources, "RosterBorderBrush", light ? "#B9C3CE" : "#383838");
        Set(resources, "RosterTextBrush", light ? "#18202B" : "#F7F7F7");
        Set(resources, "RosterMutedBrush", light ? "#586576" : "#B8B8B8");
        // The title bar is part of the surface, not a green band of its own, with the accent kept
        // for the desk's actions so it still reads as the same app.
        Set(resources, "RosterHeaderBrush", light ? "#FFFFFF" : "#0A0A0A");
        Set(resources, "RosterHeaderTextBrush", light ? "#18202B" : "#F7F7F7");
        Set(resources, "RosterPillBrush", light ? "#EEF2F7" : "#1A1A1A");
        Set(resources, "RosterPillBorderBrush", light ? "#B9C3CE" : "#383838");
        Set(resources, "RosterTabBrush", light ? "#F3F5F8" : "#050505");
        var accent = UiPalette.Accent(PreferencesStore.Load());
        // Pick the text that actually contrasts with the operator's accent. A simple brightness
        // threshold made bright green accents print white-on-green, which was unreadable.
        Set(resources, "RosterAccentBrush", accent);
        Set(resources, "RosterAccentTextBrush", ReadableText(accent));
        Set(resources, "RosterDangerBrush", light ? "#C0392B" : "#FF6B6B");
    }

    static void Set(ResourceDictionary resources, string key, Color colour) =>
        Set(resources, key, $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}");

    static void Set(ResourceDictionary resources, string key, string hex)
    {
        // A solid brush is what the desk's DynamicResource keys expect; gradient/pattern entries
        // would be wrong for an input well or a row background.

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        resources[key] = brush;
    }

    /// <summary>
    /// The highlight for the row that is on the graphic: the team's primary colour, so the desk
    /// belongs to that team. Returns the background and the text colour that reads on it.
    /// </summary>
    public static (Brush Background, Brush Foreground) Highlight(string? teamColor, ResourceDictionary resources)
    {
        var fallback = resources["RosterAccentBrush"] as Brush ?? Brushes.LimeGreen;
        Brush background = fallback;
        try
        {
            if (!string.IsNullOrWhiteSpace(teamColor))
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(teamColor));
                brush.Freeze();
                background = brush;
            }
        }
        catch { }
        var color = background is SolidColorBrush solid ? solid.Color : Colors.LimeGreen;
        return (background, new SolidColorBrush(ReadableText(color)));
    }

    /// <summary>Returns the darker of white or the desk ink that has the higher WCAG contrast.</summary>
    static Color ReadableText(Color background)
    {
        var dark = Color.FromRgb(12, 24, 20);
        return Contrast(background, dark) >= Contrast(background, Colors.White) ? dark : Colors.White;
    }

    static double Contrast(Color one, Color two)
    {
        static double Channel(byte value)
        {
            var v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        var first = 0.2126 * Channel(one.R) + 0.7152 * Channel(one.G) + 0.0722 * Channel(one.B);
        var second = 0.2126 * Channel(two.R) + 0.7152 * Channel(two.G) + 0.0722 * Channel(two.B);
        var light = Math.Max(first, second);
        var dark = Math.Min(first, second);
        return (light + 0.05) / (dark + 0.05);
    }
}
