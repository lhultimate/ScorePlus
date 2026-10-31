using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace Scoreboard.Core;

/// <summary>
/// Turns a studio layout into one self-contained web page: no external CSS, no fonts to fetch, no
/// scripts to host. Drop the file on a site (or in an iframe) and the graphic looks like it does on
/// air. Every text element carries its binding in a <c>data-token</c> attribute, and the page exposes
/// <c>ScorePlus.set("HOME_SCORE", "7")</c> so a site can push live values into it.
///
/// What carries over: position, size, opacity, rotation, fonts and their weight/style/decorations,
/// colours and team-palette slots, flat or gradient backgrounds, borders, shapes, logos (embedded as
/// data URIs) and the live-data icons. What cannot: video and camera feeds (the page draws a marked
/// placeholder box where they would be) and the studio's value-change animations.
/// </summary>
public static class ScorebugHtml
{
    static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".svg"];

    public static string Build(ScorebugLayout layout, GameState? state = null, string title = "ScorePlus")
    {
        var width = Math.Max(1, layout.Width);
        var height = Math.Max(1, layout.Height);
        var stageBackground = Css(layout.Background);
        var body = new StringBuilder();

        foreach (var element in layout.Elements ?? [])
        {
            if (!element.Visible) continue;
            body.AppendLine(ElementHtml(element, state));
        }

        var html = new StringBuilder();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.AppendLine($"<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.AppendLine($"<title>{WebUtility.HtmlEncode(title)}</title>");
        html.AppendLine("<!--");
        html.AppendLine("  Exported from ScorePlus — a single layout, ready for a web page or an iframe.");
        html.AppendLine("  The stage keeps its design size and is scaled to fit the window.");
        html.AppendLine("  Update a value from your own script with:");
        html.AppendLine("      ScorePlus.set('HOME_SCORE', '7');        // a token name, without the braces");
        html.AppendLine("      ScorePlus.set('HOME_TEAM', 'New England');");
        html.AppendLine("      ScorePlus.set('STAT:Turnovers', 3);");
        html.AppendLine("  Any element whose data-token contains the name is updated.");
        html.AppendLine("-->");
        html.AppendLine("<style>");
        html.AppendLine("html,body{margin:0;padding:0;height:100%;background:transparent;overflow:hidden;}");
        html.AppendLine("#sp-wrap{position:fixed;inset:0;display:block;}");
        html.AppendLine($"#sp-stage{{position:absolute;left:0;top:0;width:{width}px;height:{height}px;background:{stageBackground};transform-origin:top left;overflow:hidden;}}");
        html.AppendLine(".sp-el{position:absolute;box-sizing:border-box;overflow:hidden;}");
        html.AppendLine(".sp-text{display:flex;align-items:center;justify-content:center;text-align:center;width:100%;height:100%;white-space:pre-wrap;line-height:1.05;word-break:break-word;}");
        html.AppendLine(".sp-shape-circle{border-radius:50%;}");
        html.AppendLine(".sp-triangle{clip-path:polygon(50% 0%,100% 100%,0% 100%);}");
        html.AppendLine(".sp-logo{width:100%;height:100%;object-fit:contain;}");
        html.AppendLine(".sp-media{display:flex;align-items:center;justify-content:center;width:100%;height:100%;font:600 12px/1.2 'Segoe UI',Arial,sans-serif;letter-spacing:.08em;opacity:.55;}");
        html.AppendLine(".sp-bars{display:flex;align-items:center;justify-content:center;gap:6%;width:100%;height:100%;}");
        html.AppendLine(".sp-bar{height:14%;min-height:3px;flex:1 1 0;border-radius:2px;background:currentColor;}");
        html.AppendLine(".sp-dot{position:absolute;top:50%;width:12%;padding-top:12%;border-radius:50%;transform:translateY(-50%);background:currentColor;}");
        html.AppendLine(".sp-bases{position:absolute;inset:0;display:block;}");
        html.AppendLine(".sp-base{position:absolute;width:26%;padding-top:26%;border:2px solid currentColor;box-sizing:border-box;transform:rotate(45deg);}");
        html.AppendLine(".sp-base.on{background:currentColor;}");
        html.AppendLine("@keyframes sp-pulse{0%,100%{opacity:1}50%{opacity:.62}}");
        html.AppendLine("@keyframes sp-drift{0%,100%{transform:translate(0,0)}50%{transform:translate(14px,7px)}}");
        html.AppendLine(".sp-pulse{animation:sp-pulse 1s ease-in-out infinite;}");
        html.AppendLine(".sp-drift{animation:sp-drift 3.2s ease-in-out infinite;}");
        html.AppendLine("</style></head><body>");
        html.AppendLine("<div id=\"sp-wrap\"><div id=\"sp-stage\">");
        html.Append(body);
        html.AppendLine("</div></div>");
        html.AppendLine("<script>");
        html.AppendLine("(function(){");
        html.AppendLine($"  var W={width}, H={height};");
        html.AppendLine("  var stage=document.getElementById('sp-stage');");
        html.AppendLine("  function fit(){");
        html.AppendLine("    var s=Math.min(window.innerWidth/W, window.innerHeight/H);");
        html.AppendLine("    stage.style.transform='scale('+s+')';");
        html.AppendLine("    stage.style.left=((window.innerWidth-W*s)/2)+'px';");
        html.AppendLine("    stage.style.top=((window.innerHeight-H*s)/2)+'px';");
        html.AppendLine("  }");
        html.AppendLine("  window.addEventListener('resize', fit); fit();");
        html.AppendLine("  window.ScorePlus={");
        html.AppendLine("    set:function(token, value){");
        html.AppendLine("      if(token==null) return;");
        html.AppendLine("      var needle=String(token).replace(/[{}]/g,'');");
        html.AppendLine("      var nodes=document.querySelectorAll('[data-token]');");
        html.AppendLine("      for(var i=0;i<nodes.length;i++){");
        html.AppendLine("        if(nodes[i].getAttribute('data-token').indexOf(needle)>=0) nodes[i].textContent=String(value);");
        html.AppendLine("      }");
        html.AppendLine("    },");
        html.AppendLine("    fit: fit");
        html.AppendLine("  };");
        html.AppendLine("})();");
        html.AppendLine("</script></body></html>");
        return html.ToString();
    }

    static string ElementHtml(ScorebugElement item, GameState? state)
    {
        var style = new StringBuilder();
        style.Append(CultureInfo.InvariantCulture, $"left:{Num(item.X)}px;top:{Num(item.Y)}px;width:{Num(item.Width)}px;height:{Num(item.Height)}px;");
        style.Append(CultureInfo.InvariantCulture, $"opacity:{Num(Math.Clamp(item.Opacity, 0, 1))};");
        if (Math.Abs(item.Rotation) > 0.01) style.Append(CultureInfo.InvariantCulture, $"transform:rotate({Num(item.Rotation)}deg);");

        // The box behind the artwork: shapes and logos draw themselves, exactly like the studio.
        if (!item.TransparentBackground && item.Kind is not (ScorebugElementKind.Shape or ScorebugElementKind.Logo))
            style.Append($"background:{Background(item, state)};");
        if (item.BorderThickness > 0)
            style.Append(CultureInfo.InvariantCulture, $"border:{Num(item.BorderThickness)}px solid {ResolveBorder(item, state)};");
        if (item.Kind == ScorebugElementKind.Shape && item.Shape == ScorebugShape.Rectangle)
            style.Append("border-radius:8px;");

        var classes = new StringBuilder("sp-el");
        if (item.Kind == ScorebugElementKind.Shape && item.Shape == ScorebugShape.Circle) classes.Append(" sp-shape-circle");
        if (item.Kind == ScorebugElementKind.Shape && item.Shape == ScorebugShape.Triangle) classes.Append(" sp-triangle");
        if (item.Animation == ScorebugAnimation.Pulse) classes.Append(" sp-pulse");
        if (item.Animation == ScorebugAnimation.Drift) classes.Append(" sp-drift");

        var colour = ResolveForeground(item, state);
        var token = item.Kind == ScorebugElementKind.Text ? $" data-token=\"{WebUtility.HtmlEncode(item.Content ?? string.Empty)}\"" : string.Empty;

        var inner = new StringBuilder();
        switch (item.Kind)
        {
            case ScorebugElementKind.Shape:
                // A transparent shape is an outline only; the triangle and circle clip their own fill.
                inner.Append($"<div style=\"width:100%;height:100%;background:{(item.TransparentBackground ? "transparent" : Background(item, state))};{(item.Shape == ScorebugShape.Circle ? "border-radius:50%;" : string.Empty)}\"></div>");
                break;
            case ScorebugElementKind.Logo:
                inner.Append(LogoHtml(item, state));
                break;
            case ScorebugElementKind.Video:
                inner.Append($"<div class=\"sp-media\" style=\"color:{colour};background:rgba(255,255,255,.06);\">{WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(item.MediaTag) ? "VIDEO" : $"VIDEO · {item.MediaTag}")}</div>");
                break;
            default:
                inner.Append(TextHtml(item, state, colour));
                break;
        }
        inner.Append(IconHtml(item, state, colour));

        return $"<div class=\"{classes}\" style=\"{style}\"{token}>{inner}</div>";
    }

    static string TextHtml(ScorebugElement item, GameState? state, string colour)
    {
        var text = state is not null
            ? ScoreboardTemplates.Bind(item.Content ?? string.Empty, state, item.PeriodFormat)
            : item.Content ?? string.Empty;
        var font = FontFamily(item.FontFamily);
        var weight = item.Bold ? "font-weight:700;" : "font-weight:400;";
        var italic = item.Italic ? "font-style:italic;" : string.Empty;
        var decorations = new StringBuilder();
        if (item.Underline) decorations.Append("underline ");
        if (item.StrikeThrough) decorations.Append("line-through ");
        var decoration = decorations.Length > 0 ? $"text-decoration:{decorations.ToString().Trim()};" : string.Empty;
        return $"<div class=\"sp-text\" style=\"color:{colour};font-family:{font};font-size:{Num(item.FontSize)}px;{weight}{italic}{decoration}\">{WebUtility.HtmlEncode(text)}</div>";
    }

    static string LogoHtml(ScorebugElement item, GameState? state)
    {
        var path = item.LogoSide == ScorebugLogoSide.None ? item.ImagePath : state?.LogoFor(item.LogoSide) ?? item.ImagePath;
        var data = DataUri(path);
        if (data is not null)
            return $"<img class=\"sp-logo\" alt=\"\" src=\"{data}\">";
        var label = item.LogoSide == ScorebugLogoSide.None
            ? item.Content
            : $"{item.LogoSide.ToString().ToUpperInvariant()} LOGO";
        return $"<div class=\"sp-text\" style=\"color:{ResolveForeground(item, state)};font-family:{FontFamily(item.FontFamily)};font-size:{Num(item.FontSize)}px;\">{WebUtility.HtmlEncode(label ?? string.Empty)}</div>";
    }

    /// <summary>The live-data artwork: timeout bars, a possession dot, or the three baseball bases.</summary>
    static string IconHtml(ScorebugElement item, GameState? state, string colour)
    {
        if (item.Icon == ScorebugIcon.None || state is null) return string.Empty;
        switch (item.Icon)
        {
            case ScorebugIcon.TimeoutBars:
                var count = Math.Clamp(item.IconVisitors ? state.AwayTimeouts : state.HomeTimeouts, 0, 12);
                if (count == 0) return string.Empty;
                var bars = new StringBuilder($"<div class=\"sp-bars\" style=\"color:{colour};\">");
                for (var i = 0; i < count; i++) bars.Append("<span class=\"sp-bar\"></span>");
                bars.Append("</div>");
                return bars.ToString();
            case ScorebugIcon.PossessionDot:
                var onLeft = state.HomePossession ^ item.IconFlipped;
                var dotStyle = onLeft ? "left:2%;" : "right:2%;";
                return $"<span class=\"sp-dot\" style=\"{dotStyle}color:{colour};\"></span>";
            case ScorebugIcon.Bases:
                var first = SportValues.Read(state, "baseball.first") != 0;
                var second = SportValues.Read(state, "baseball.second") != 0;
                var third = SportValues.Read(state, "baseball.third") != 0;
                return $"<span class=\"sp-bases\" style=\"color:{colour};\">" +
                       $"<span class=\"sp-base{(second ? " on" : string.Empty)}\" style=\"left:37%;top:2%;\"></span>" +
                       $"<span class=\"sp-base{(third ? " on" : string.Empty)}\" style=\"left:68%;top:37%;\"></span>" +
                       $"<span class=\"sp-base{(first ? " on" : string.Empty)}\" style=\"left:6%;top:37%;\"></span>" +
                       "</span>";
            default:
                return string.Empty;
        }
    }

    static string ResolveForeground(ScorebugElement item, GameState? state) => Css(state?.ColorFor(item.ForegroundSource) ?? item.Foreground);

    static string ResolveBorder(ScorebugElement item, GameState? state) => Css(state?.ColorFor(item.BorderSource) ?? item.BorderColor);

    static string Background(ScorebugElement item, GameState? state)
    {
        var first = Css(state?.ColorFor(item.BackgroundSource) ?? item.Background);
        if (!item.BackgroundGradient) return first;
        var second = Css(state?.ColorFor(item.BackgroundSource2) ?? item.BackgroundColor2);
        // CSS measures gradients clockwise from "to top"; the studio measures from left to right.
        return $"linear-gradient({Num(item.GradientAngle + 90)}deg, {first}, {second})";
    }

    /// <summary>
    /// A WPF-style hex colour (#RGB, #ARGB, #RRGGBB, #AARRGGBB) as CSS rgba(), so the alpha the
    /// studio uses for its translucent boxes survives the export.
    /// </summary>
    static string Css(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length > 0 && text[0] == '#') text = text[1..];
        if (text.Length is not (3 or 4 or 6 or 8) || !text.All(Uri.IsHexDigit)) return "transparent";
        int a, r, g, b;
        if (text.Length <= 4)
        {
            a = text.Length == 4 ? Convert.ToInt32(new string(text[0], 2), 16) : 255;
            var start = text.Length == 4 ? 1 : 0;
            r = Convert.ToInt32(new string(text[start], 2), 16);
            g = Convert.ToInt32(new string(text[start + 1], 2), 16);
            b = Convert.ToInt32(new string(text[start + 2], 2), 16);
        }
        else
        {
            a = text.Length == 8 ? Convert.ToInt32(text[..2], 16) : 255;
            var start = text.Length == 8 ? 2 : 0;
            r = Convert.ToInt32(text.Substring(start, 2), 16);
            g = Convert.ToInt32(text.Substring(start + 2, 2), 16);
            b = Convert.ToInt32(text.Substring(start + 4, 2), 16);
        }
        return string.Create(CultureInfo.InvariantCulture, $"rgba({r},{g},{b},{(a / 255.0).ToString("0.###", CultureInfo.InvariantCulture)})");
    }

    /// <summary>The family name of a font choice, including an imported font file.</summary>
    static string FontFamily(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return "'Segoe UI',Arial,sans-serif";
        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || text.Contains('\\') || text.Contains('/'))
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(text);
                if (!string.IsNullOrWhiteSpace(name)) return $"'{name.Replace("'", string.Empty)}',Arial,sans-serif";
            }
            catch { }
            return "'Segoe UI',Arial,sans-serif";
        }
        return $"'{text.Replace("'", string.Empty)}',Arial,sans-serif";
    }

    /// <summary>An image file as a data URI, so the page has no loose files; null when it is missing.</summary>
    static string? DataUri(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var resolved = path;
            if (!Path.IsPathRooted(resolved)) resolved = AppPaths.Combine(resolved);
            if (!File.Exists(resolved)) resolved = path;
            if (!File.Exists(resolved)) return null;
            var extension = Path.GetExtension(resolved).ToLowerInvariant();
            if (!ImageExtensions.Contains(extension)) return null;
            var mime = extension switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                ".ico" => "image/x-icon",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream"
            };
            var bytes = File.ReadAllBytes(resolved);
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch { return null; }
    }

    static string Num(double value) => (double.IsFinite(value) ? value : 0).ToString("0.###", CultureInfo.InvariantCulture);
}
