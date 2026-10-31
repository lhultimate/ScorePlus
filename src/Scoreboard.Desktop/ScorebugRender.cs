using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>Shared rendering rules so the studio and the audience output always agree.</summary>
internal static class ScorebugRender
{
    public static Brush ParseBrush(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Brushes.Transparent;
        try { return (Brush)new BrushConverter().ConvertFromString(value)!; }
        catch { return Brushes.Transparent; }
    }

    /// <summary>The colour an element actually draws, after its team-palette slot is resolved.</summary>
    public static string ResolveForeground(ScorebugElement item, GameState? state) =>
        state?.ColorFor(item.ForegroundSource) ?? item.Foreground;

    public static string ResolveBackground(ScorebugElement item, GameState? state) =>
        state?.ColorFor(item.BackgroundSource) ?? item.Background;

    /// <summary>The gradient's second colour, after its team-palette slot is resolved.</summary>
    public static string ResolveBackground2(ScorebugElement item, GameState? state) =>
        state?.ColorFor(item.BackgroundSource2) ?? item.BackgroundColor2;

    /// <summary>
    /// What the background actually paints, as a comparable string: a colour, or both stops and the
    /// angle when the element is a gradient. Views cache on this so a brush is only rebuilt on change.
    /// </summary>
    public static string BackgroundSignature(ScorebugElement item, GameState? state) =>
        item.BackgroundGradient
            ? $"g|{ResolveBackground(item, state)}|{ResolveBackground2(item, state)}|{item.GradientAngle:0.###}"
            : ResolveBackground(item, state);

    /// <summary>
    /// The brush an element's box is painted with: a flat colour, or a two-stop gradient running in
    /// <see cref="ScorebugElement.GradientAngle"/> degrees (0 left→right, 90 top→bottom).
    /// </summary>
    public static Brush BackgroundBrush(ScorebugElement item, GameState? state = null)
    {
        if (!item.BackgroundGradient) return ParseBrush(ResolveBackground(item, state));
        var start = ParseBrush(ResolveBackground(item, state));
        var end = ParseBrush(ResolveBackground2(item, state));
        var angle = ((item.GradientAngle % 360) + 360) % 360 * Math.PI / 180.0;
        var dx = Math.Cos(angle) / 2;
        var dy = Math.Sin(angle) / 2;
        var brush = new LinearGradientBrush(ColorOf(start), ColorOf(end), new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
        brush.Freeze();
        return brush;
    }

    static Color ColorOf(Brush brush) => brush is SolidColorBrush solid ? solid.Color : Colors.Transparent;

    /// <summary>The colour of the element's outline, after its team-palette slot is resolved.</summary>
    public static string ResolveBorder(ScorebugElement item, GameState? state) =>
        state?.ColorFor(item.BorderSource) ?? item.BorderColor;

    /// <summary>The outline an element draws around its own box, or nothing when it is switched off.</summary>
    public static Brush HostBorder(ScorebugElement item, GameState? state = null) =>
        item.BorderThickness <= 0 ? Brushes.Transparent : ParseBrush(ResolveBorder(item, state));

    /// <summary>Background painted behind an element. Shapes/logo draw themselves, so their host stays clear.</summary>
    public static Brush HostBackground(ScorebugElement item, GameState? state = null) =>
        item.Kind is ScorebugElementKind.Shape or ScorebugElementKind.Logo || item.TransparentBackground
            ? Brushes.Transparent
            : BackgroundBrush(item, state);

    public static string? ResolveLogoPath(ScorebugElement item, GameState? state) =>
        item.LogoSide == ScorebugLogoSide.None ? item.ImagePath : state?.LogoFor(item.LogoSide) ?? item.ImagePath;

    public static FrameworkElement CreateContent(ScorebugElement item, Func<GameState>? state, MediaPlayback? playback = null, bool muteVideo = true, bool showVideoPlaceholder = false, bool onAirVideo = false)
    {
        if (item.Kind == ScorebugElementKind.Video) return new VideoSurface(item, playback, muteVideo, showVideoPlaceholder, onAirVideo);
        // HTML is drawn by a browser engine and captured as a frame, so every feed shows the same
        // picture at the element's design size.
        if (item.Kind == ScorebugElementKind.Html) return new HtmlSurface(item, showVideoPlaceholder);
        // Icons are drawn from live data rather than typed text: small bars per timeout, or a dot
        // showing which team has the ball.
        if (item.Kind == ScorebugElementKind.Text && item.Icon != ScorebugIcon.None) return new ScorebugIconView(item, state);
        // A logo view keeps its image slot whether or not a logo exists yet, so a team logo set
        // after the graphic was built appears immediately instead of waiting for a rebuild.
        if (item.Kind == ScorebugElementKind.Logo) return new ScorebugLogoView(item, state?.Invoke(), showVideoPlaceholder);
        if (item.Kind == ScorebugElementKind.Shape) return CreateShape(item, state?.Invoke());
        var text = state is null ? item.Content : ScoreboardTemplates.Bind(item.Content, state(), item.PeriodFormat);
        return MakeText(item, text, state?.Invoke());
    }

    /// <summary>Logos are copied into the app folder, but they still need a caching load.</summary>
    internal static BitmapImage LoadLogo(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static ScorebugTextBlock MakeText(ScorebugElement item, string text, GameState? state = null)
    {
        var block = new ScorebugTextBlock(item);
        // The constructor can only use the element's fixed colour. Studio, preview and output all
        // build text before any refresh happens, so a team-palette slot has to resolve right here
        // or the value draws in the wrong colour until something else repaints it.
        block.ApplyStyle(item, state);
        block.UpdateText(text, ScorebugTransition.Cut);
        return block;
    }

    public static Shape CreateShape(ScorebugElement item, GameState? state = null)
    {
        Shape shape = item.Shape switch
        {
            ScorebugShape.Circle => new Ellipse(),
            ScorebugShape.Triangle => new Polygon { Points = new PointCollection { new(0.5, 0), new(1, 1), new(0, 1) } },
            _ => new Rectangle { RadiusX = 8, RadiusY = 8 }
        };
        shape.Stretch = Stretch.Fill;
        // A transparent shape has no fill, only its outline — the old code always painted the
        // background, which is why a hollow shape could never be made.
        shape.Fill = item.TransparentBackground ? Brushes.Transparent : BackgroundBrush(item, state);
        shape.Stroke = HostBorder(item, state);
        shape.StrokeThickness = Math.Max(0, item.BorderThickness);
        shape.StrokeStartLineCap = PenLineCap.Round;
        shape.StrokeEndLineCap = PenLineCap.Round;
        return shape;
    }

    /// <summary>Applies text, colour, font and style to whatever content an element created.</summary>
    public static void ApplyToContent(FrameworkElement content, ScorebugElement item, GameState? state = null, string? text = null)
    {
        switch (content)
        {
            case ScorebugIconView icon:
                icon.Update(text);
                break;
            case ScorebugTextBlock block:
                block.ApplyStyle(item, state);
                block.UpdateText(text ?? item.Content, item.Transition, item.TransitionSeconds);
                break;
            case TextBlock plain:
                plain.Text = text ?? item.Content;
                break;
            case Shape shape:
                shape.Fill = item.TransparentBackground ? Brushes.Transparent : BackgroundBrush(item, state);
                shape.Stroke = HostBorder(item, state);
                shape.StrokeThickness = Math.Max(0, item.BorderThickness);
                break;
            case ScorebugLogoView logo:
                // Logos are set from the console: pick one up, drop one, all without a rebuild.
                logo.Update(item, state);
                break;
            case HtmlSurface htmlPage:
                htmlPage.Update(item);
                break;
            case Image image:
                var path = ResolveLogoPath(item, state);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    try { image.Source = LoadLogo(path); } catch { }
                }
                else
                {
                    // Removing a team logo must take it off the graphic now, not after a rebuild.
                    image.Source = null;
                }
                break;
        }
    }
}

/// <summary>
/// A logo element that can gain or lose its image while it is already on screen. Building the
/// picture only when a logo exists at creation time left an empty placeholder that a later team
/// logo could never fill, which is why a restored game showed its text but not its crest.
/// </summary>
public sealed class ScorebugLogoView : Grid
{
    readonly Image _image = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    readonly TextBlock? _placeholder;

    public ScorebugLogoView(ScorebugElement item, GameState? state, bool showPlaceholder)
    {
        if (showPlaceholder)
        {
            _placeholder = new TextBlock
            {
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Children.Add(_placeholder);
        }
        Children.Add(_image);
        Update(item, state);
    }

    public void Update(ScorebugElement item, GameState? state)
    {
        var path = ScorebugRender.ResolveLogoPath(item, state);
        var found = false;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                _image.Source = ScorebugRender.LoadLogo(path);
                found = true;
            }
            catch { _image.Source = null; }
        }
        else
        {
            // Removing a team logo clears the picture now, without rebuilding the graphic.
            _image.Source = null;
        }
        _image.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
        if (_placeholder is null) return;
        _placeholder.Text = item.LogoSide == ScorebugLogoSide.None ? item.Content : $"{item.LogoSide.ToString().ToUpperInvariant()} LOGO";
        _placeholder.FontFamily = FontCatalog.Resolve(item.FontFamily);
        _placeholder.FontSize = item.FontSize;
        _placeholder.FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal;
        _placeholder.FontStyle = item.Italic ? FontStyles.Italic : FontStyles.Normal;
        _placeholder.Foreground = ScorebugRender.ParseBrush(ScorebugRender.ResolveForeground(item, state));
        _placeholder.Visibility = found ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>
/// A text element that knows how it should cross from one value to the next. A score that merely
/// cuts feels like a spreadsheet; a wheel, a dissolve or a flip feels like television. Values that
/// change while an animation is still running snap to the newest one so the graphic is never stale.
/// </summary>
public sealed class ScorebugTextBlock : TextBlock
{
    readonly TranslateTransform _translate = new();
    readonly ScaleTransform _scale = new(1, 1);
    readonly RotateTransform _rotate = new();
    string _shown = "";
    bool _animating;
    // A transition is two phases with callbacks; a newer value (or a snap) bumps the generation,
    // so a stale out-phase can never put its old text back on the graphic.
    int _generation;
    ScorebugElement? _style;

    public ScorebugTextBlock(ScorebugElement item)
    {
        _style = item;
        TextAlignment = TextAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Center;
        TextWrapping = TextWrapping.Wrap;
        RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_rotate);
        group.Children.Add(_translate);
        RenderTransform = group;
        ApplyStyle(item, null);
    }

    public void ApplyStyle(ScorebugElement item, GameState? state)
    {
        FontFamily = FontCatalog.Resolve(item.FontFamily);
        FontSize = item.FontSize;
        FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal;
        FontStyle = item.Italic ? FontStyles.Italic : FontStyles.Normal;
        Foreground = ScorebugRender.ParseBrush(ScorebugRender.ResolveForeground(item, state));
        var decorations = new TextDecorationCollection();
        if (item.Underline) decorations.Add(System.Windows.TextDecorations.Underline[0]);
        if (item.StrikeThrough) decorations.Add(System.Windows.TextDecorations.Strikethrough[0]);
        TextDecorations = decorations.Count == 0 ? null : decorations;
        // Slanted text: rotation is part of the element's design, not part of the animation.
        _rotate.Angle = item.Rotation;
        _style = item;
    }

    /// <summary>Shows a new value, crossing to it with the element's chosen animation.</summary>
    /// <param name="seconds">How long the whole crossing takes: 0 snaps, 5 is a slow dissolve.</param>
    public void UpdateText(string text, ScorebugTransition transition, double seconds = 0.2)
    {
        text ??= "";
        var total = Math.Clamp(double.IsFinite(seconds) ? seconds : 0.2, 0, 5);
        if (_shown == text && !_animating) return;
        if (transition == ScorebugTransition.Cut || total < 0.02 || !IsLoaded || _animating)
        {
            Snap(text);
            return;
        }
        _animating = true;
        _shown = text;
        var generation = _generation;
        // The value leaves a little faster than it arrives, which reads as a deliberate cross
        // rather than a symmetric dissolve.
        var outDuration = TimeSpan.FromSeconds(total * 0.42);
        var inDuration = TimeSpan.FromSeconds(Math.Max(0.01, total - total * 0.42));
        PlayOut(transition, outDuration, () =>
        {
            if (generation != _generation) return;
            Text = text;
            PlayIn(transition, inDuration, () =>
            {
                if (generation == _generation) _animating = false;
            });
        });
    }

    void Snap(string text)
    {
        _generation++;
        ClearPhase();
        Text = text;
        _shown = text;
        _animating = false;
    }

    /// <summary>
    /// Ends a phase with the block at rest. Every animation holds its final value, then this clears
    /// it and resets the transforms, so a wheel or a zoom can never leave the new value offset or
    /// shrunken after the animation has finished.
    /// </summary>
    void ClearPhase()
    {
        BeginAnimation(OpacityProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _translate.BeginAnimation(TranslateTransform.XProperty, null);
        _translate.BeginAnimation(TranslateTransform.YProperty, null);
        Opacity = 1;
        _scale.ScaleX = _scale.ScaleY = 1;
        _translate.X = _translate.Y = 0;
    }

    double Distance => Math.Max(8, FontSize * 1.1);

    // ---------- out ----------

    void PlayOut(ScorebugTransition transition, TimeSpan duration, Action done)
    {
        switch (transition)
        {
            case ScorebugTransition.Fade:
                Animate(1, 0, duration, done);
                break;
            case ScorebugTransition.FadeScale:
                Animate(1, 0, duration, done, scaleFrom: 1, scaleTo: 0.94);
                break;
            case ScorebugTransition.SlideUp:
                Animate(1, 0.2, duration, done, yFrom: 0, yTo: -Distance * 0.75);
                break;
            case ScorebugTransition.SlideDown:
                Animate(1, 0.2, duration, done, yFrom: 0, yTo: Distance * 0.75);
                break;
            case ScorebugTransition.SlideLeft:
                Animate(1, 0.2, duration, done, xFrom: 0, xTo: -Distance * 0.75);
                break;
            case ScorebugTransition.SlideRight:
                Animate(1, 0.2, duration, done, xFrom: 0, xTo: Distance * 0.75);
                break;
            case ScorebugTransition.WheelUp:
                // No fade: the old value rolls out like a drum, and the new one rolls in behind it.
                Animate(1, 1, duration, done, yFrom: 0, yTo: -Distance);
                break;
            case ScorebugTransition.WheelDown:
                Animate(1, 1, duration, done, yFrom: 0, yTo: Distance);
                break;
            case ScorebugTransition.Flip:
                Animate(1, 0.15, duration, done, scaleYFrom: 1, scaleYTo: 0.05);
                break;
            case ScorebugTransition.Zoom:
                Animate(1, 0, duration, done, scaleFrom: 1, scaleTo: 0.6);
                break;
            case ScorebugTransition.Pop:
                Animate(1, 0.35, duration, done, scaleFrom: 1, scaleTo: 0.88);
                break;
            case ScorebugTransition.Drop:
                Animate(1, 0.15, duration, done, yFrom: 0, yTo: -Distance * 0.4);
                break;
            default:
                Animate(1, 1, duration, done);
                break;
        }
    }

    // ---------- in ----------

    void PlayIn(ScorebugTransition transition, TimeSpan duration, Action done)
    {
        switch (transition)
        {
            case ScorebugTransition.Fade:
                Animate(0, 1, duration, done);
                break;
            case ScorebugTransition.FadeScale:
                Animate(0, 1, duration, done, scaleFrom: 0.94, scaleTo: 1);
                break;
            case ScorebugTransition.SlideUp:
                Animate(0, 1, duration, done, yFrom: Distance * 0.7, yTo: 0);
                break;
            case ScorebugTransition.SlideDown:
                Animate(0, 1, duration, done, yFrom: -Distance * 0.7, yTo: 0);
                break;
            case ScorebugTransition.SlideLeft:
                Animate(0, 1, duration, done, xFrom: Distance * 0.7, xTo: 0);
                break;
            case ScorebugTransition.SlideRight:
                Animate(0, 1, duration, done, xFrom: -Distance * 0.7, xTo: 0);
                break;
            case ScorebugTransition.WheelUp:
                Animate(1, 1, duration, done, yFrom: Distance, yTo: 0);
                break;
            case ScorebugTransition.WheelDown:
                Animate(1, 1, duration, done, yFrom: -Distance, yTo: 0);
                break;
            case ScorebugTransition.Flip:
                Animate(1, 1, duration, done, scaleYFrom: 0.05, scaleYTo: 1);
                break;
            case ScorebugTransition.Zoom:
                Animate(0, 1, duration, done, scaleFrom: 0.6, scaleTo: 1);
                break;
            case ScorebugTransition.Pop:
                Animate(0, 1, duration, done, scaleFrom: 0.88, scaleTo: 1, overshoot: true);
                break;
            case ScorebugTransition.Drop:
                Animate(0, 1, duration, done, yFrom: Distance * 0.7, yTo: 0);
                break;
            default:
                Animate(1, 1, duration, done);
                break;
        }
    }

    /// <summary>
    /// Runs one phase with explicit from/to values. The opacity animation is always present, which
    /// gives a reliable completion callback even for a wheel that only moves. Holding the final
    /// value (instead of stopping) means the callback clears a settled block, not one that is about
    /// to snap back to a stale offset.
    /// </summary>
    void Animate(double fromOpacity, double toOpacity, TimeSpan duration, Action done,
        double? xFrom = null, double? xTo = null, double? yFrom = null, double? yTo = null,
        double? scaleFrom = null, double? scaleTo = null, double? scaleYFrom = null, double? scaleYTo = null,
        bool overshoot = false)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var finished = false;
        var fade = new DoubleAnimation(fromOpacity, toOpacity, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        fade.Completed += (_, _) =>
        {
            if (finished) return;
            finished = true;
            ClearPhase();
            done();
        };
        BeginAnimation(OpacityProperty, fade);

        if (xFrom is double || xTo is double)
            _translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(xFrom ?? 0, xTo ?? 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
        if (yFrom is double || yTo is double)
            _translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(yFrom ?? 0, yTo ?? 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });

        if (overshoot)
        {
            // A pop lands a touch too big and settles: the little bounce that reads as "new value".
            var start = scaleFrom ?? 1;
            var end = scaleTo ?? 1;
            AnimationTimeline Pop()
            {
                var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(start, KeyTime.FromPercent(0)));
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(end * 1.06, KeyTime.FromPercent(0.62), new CubicEase { EasingMode = EasingMode.EaseOut }));
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(end, KeyTime.FromPercent(1)));
                return animation;
            }
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, Pop());
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, Pop());
        }
        else
        {
            if (scaleFrom is double || scaleTo is double)
            {
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scaleFrom ?? 1, scaleTo ?? 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scaleFrom ?? 1, scaleTo ?? 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
            }
            if (scaleYFrom is double || scaleYTo is double)
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scaleYFrom ?? 1, scaleYTo ?? 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
        }
    }
}

/// <summary>
/// An element that draws live artwork instead of (or alongside) its text: one little bar per
/// timeout the team has left, or a dot on the side of the team with the ball.
/// </summary>
public sealed class ScorebugIconView : Grid
{
    readonly ScorebugElement _item;
    readonly Func<GameState>? _state;
    readonly ScorebugTextBlock? _text;
    readonly Ellipse? _dot;
    readonly StackPanel? _bars;
    readonly Canvas? _bases;
    string _appliedForeground = "";
    string _baseSignature = "";

    public ScorebugIconView(ScorebugElement item, Func<GameState>? state, string? text = null)
    {
        _item = item;
        _state = state;
        ClipToBounds = true;
        if (item.Icon == ScorebugIcon.Bases)
        {
            // Three bases in a diamond: a base a runner is standing on is filled, an empty one is
            // only a faint outline, so the graphic reads at a glance like the field does.
            _bases = new Canvas { ClipToBounds = true };
            Children.Add(_bases);
        }
        else if (item.Icon == ScorebugIcon.TimeoutBars)
        {
            // One short horizontal line per timeout, in a row from left to right.
            _bars = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Children.Add(_bars);
        }
        else
        {
            // The dot sits beside whatever the element says (usually {{POSSESSION}}).
            if (!string.IsNullOrWhiteSpace(item.Content))
            {
                _text = ScorebugRender.MakeText(item, text ?? item.Content);
                Children.Add(_text);
            }
            _dot = new Ellipse { Fill = ScorebugRender.ParseBrush(ScorebugRender.ResolveForeground(item, state?.Invoke())), VerticalAlignment = VerticalAlignment.Center };
            Children.Add(_dot);
        }
        Update(text);
    }

    /// <summary>Copies the current game into the icon: bar count, dot side, and the text beside it.</summary>
    public void Update(string? text = null)
    {
        var state = _state?.Invoke();
        _text?.ApplyStyle(_item, state);
        var foreground = ScorebugRender.ResolveForeground(_item, state);
        if (_bases is not null)
        {
            var first = state is not null && SportValues.Read(state, "baseball.first") != 0;
            var second = state is not null && SportValues.Read(state, "baseball.second") != 0;
            var third = state is not null && SportValues.Read(state, "baseball.third") != 0;
            var signature = $"{_item.Width:0.#}x{_item.Height:0.#}|{foreground}|{first}{second}{third}";
            if (signature != _baseSignature)
            {
                _baseSignature = signature;
                _bases.Children.Clear();
                var width = Math.Max(24, _item.Width);
                var height = Math.Max(24, _item.Height);
                var size = Math.Clamp(Math.Min(width, height) * 0.34, 9, 70);
                var brush = ScorebugRender.ParseBrush(foreground);
                var faint = (Brush)brush.Clone();
                faint.Opacity = 0.32;
                (double X, double Y, bool On)[] spots =
                [
                    (width * 0.30, height * 0.70, third),
                    (width * 0.50, height * 0.24, second),
                    (width * 0.70, height * 0.70, first)
                ];
                foreach (var (x, y, on) in spots)
                {
                    var square = new Rectangle
                    {
                        Width = size,
                        Height = size,
                        RadiusX = Math.Max(1, size * 0.10),
                        RadiusY = Math.Max(1, size * 0.10),
                        RenderTransformOrigin = new Point(0.5, 0.5),
                        RenderTransform = new RotateTransform(45),
                        Fill = on ? brush : Brushes.Transparent,
                        Stroke = on ? brush : faint,
                        StrokeThickness = Math.Max(1.5, size * 0.12)
                    };
                    Canvas.SetLeft(square, x - size / 2);
                    Canvas.SetTop(square, y - size / 2);
                    _bases.Children.Add(square);
                }
            }
            return;
        }
        if (_bars is not null)
        {
            var count = 0;
            if (state is not null) count = Math.Clamp(_item.IconVisitors ? state.AwayTimeouts : state.HomeTimeouts, 0, 10);
            if (_bars.Children.Count != count)
            {
                _bars.Children.Clear();
                var slot = _item.Width / Math.Max(1, count);
                var width = Math.Max(6, Math.Min(_item.Width * 0.22, slot * 0.62));
                var thickness = Math.Clamp(_item.Height / 8, 2, 6);
                var gap = Math.Max(2, width * 0.22);
                for (var i = 0; i < count; i++)
                    _bars.Children.Add(new Rectangle
                    {
                        Width = width,
                        Height = thickness,
                        RadiusX = thickness / 2,
                        RadiusY = thickness / 2,
                        Margin = new Thickness(gap / 2, 0, gap / 2, 0),
                        Fill = ScorebugRender.ParseBrush(foreground)
                    });
                _appliedForeground = foreground;
            }
            else if (foreground != _appliedForeground)
            {
                // A team colour change repaints the existing bars instead of redrawing them.
                var brush = ScorebugRender.ParseBrush(foreground);
                foreach (var child in _bars.Children.OfType<Rectangle>()) child.Fill = brush;
                _appliedForeground = foreground;
            }
            return;
        }
        if (_dot is not null)
        {
            var size = Math.Max(8, Math.Min(_item.Width, _item.Height) * 0.3);
            _dot.Width = size;
            _dot.Height = size;
            _dot.Margin = new Thickness(size * 0.35, 0, size * 0.35, 0);
            _dot.Fill = ScorebugRender.ParseBrush(foreground);
            // Visitors on the left, home on the right, like the console; the element can mirror it.
            var home = state?.HomePossession ?? true;
            var right = _item.IconFlipped ? !home : home;
            _dot.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
        if (_text is not null)
        {
            var value = text ?? (state is null ? _item.Content : ScoreboardTemplates.Bind(_item.Content, state, _item.PeriodFormat));
            _text.UpdateText(value, _item.Transition, _item.TransitionSeconds);
        }
    }
}
