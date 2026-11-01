using System.Windows;
using System.Windows.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// The roster desk's colours. It takes the operator theme's own page, surface, input wells and
/// borders — the same shades the media desk uses — so the desk looks like the rest of the program
/// in either theme instead of carrying a navy set of its own. Both themes are painted from here, and
/// the panels pick the brushes up through DynamicResource so rows built later are themed too.
/// </summary>
internal static class RosterPalette
{
    public static void Apply(string theme, ResourceDictionary resources)
    {
        var light = ThemeManager.Resolve(theme) == "Light";
        // The page is the theme's own background — near-black in Dark, the light page in Light —
        // not a navy panel, so the desk joins the console instead of reading as a different program.
        Set(resources, "RosterPanelBrush", light ? "#F3F5F8" : "#080808");
        Set(resources, "RosterSurfaceBrush", light ? "#FFFFFF" : "#141414");
        Set(resources, "RosterSurfaceAltBrush", light ? "#E5EAF0" : "#1C1C1C");
        Set(resources, "RosterInputBrush", light ? "#FFFFFF" : "#050505");
        Set(resources, "RosterBorderBrush", light ? "#B9C3CE" : "#383838");
        Set(resources, "RosterTextBrush", light ? "#18202B" : "#F4F7FB");
        Set(resources, "RosterMutedBrush", light ? "#586576" : "#93A0B2");
        // The title bar is part of the surface now, not a green band of its own, with the accent
        // kept for the desk's actions so it still reads as the same app.
        Set(resources, "RosterHeaderBrush", light ? "#FFFFFF" : "#141414");
        Set(resources, "RosterHeaderTextBrush", light ? "#18202B" : "#F4F7FB");
        Set(resources, "RosterPillBrush", light ? "#EEF2F7" : "#1C1C1C");
        Set(resources, "RosterPillBorderBrush", light ? "#B9C3CE" : "#383838");
        Set(resources, "RosterTabBrush", light ? "#F3F5F8" : "#080808");
        var accent = UiPalette.Accent(PreferencesStore.Load());
        var accentText = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255 > 0.6 ? "#0C1814" : "#FFFFFF";
        Set(resources, "RosterAccentBrush", accent);
        Set(resources, "RosterAccentTextBrush", accentText);
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
        // Readability first: light team colours get dark text.
        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        return luminance > 0.6
            ? (background, new SolidColorBrush(Color.FromRgb(12, 24, 20)))
            : (background, Brushes.White);
    }
}
