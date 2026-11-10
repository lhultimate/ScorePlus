using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// Plays a <see cref="MotionClip"/> on any element: a hand-over's exit and enter legs, a looping
/// ambient motion, or the flourish a new value plays. Every property the maker can key ends up on a
/// transform or an effect here, and colour keys repaint the text, plate and outline as they travel.
/// One player for the studio preview, the makers and the audience output, so what the operator
/// builds is exactly what goes to air.
/// </summary>
internal static class MotionPlayback
{
    /// <summary>The element's design rotation, composed with every clip played on its host.</summary>
    public static readonly DependencyProperty BaseRotationProperty = DependencyProperty.RegisterAttached(
        "BaseRotation", typeof(double), typeof(MotionPlayback), new PropertyMetadata(0.0));

    public static void SetBaseRotation(FrameworkElement host, double angle) =>
        host.SetValue(BaseRotationProperty, double.IsFinite(angle) ? angle : 0);

    public static double GetBaseRotation(FrameworkElement host) =>
        host.GetValue(BaseRotationProperty) is double angle && double.IsFinite(angle) ? angle : 0;

    /// <summary>The design colour of a brush a clip is about to repaint.</summary>
    static readonly DependencyProperty OriginalColourProperty = DependencyProperty.RegisterAttached(
        "OriginalColour", typeof(Color?), typeof(MotionPlayback), new PropertyMetadata(null));

    /// <summary>The design opacity of a brush a clip is about to fade.</summary>
    static readonly DependencyProperty OriginalBrushOpacityProperty = DependencyProperty.RegisterAttached(
        "OriginalBrushOpacity", typeof(double?), typeof(MotionPlayback), new PropertyMetadata(null));

    // A colour key on a gradient or textured paint cannot animate that brush's single Colour
    // property. The brush is swapped for a plain brush while the key runs, and these properties
    // remember exactly what to put back when the phase ends.
    static readonly DependencyProperty OriginalForegroundBrushProperty = DependencyProperty.RegisterAttached(
        "OriginalForegroundBrush", typeof(BrushSnapshot), typeof(MotionPlayback), new PropertyMetadata(null));
    static readonly DependencyProperty OriginalBackgroundBrushProperty = DependencyProperty.RegisterAttached(
        "OriginalBackgroundBrush", typeof(BrushSnapshot), typeof(MotionPlayback), new PropertyMetadata(null));
    static readonly DependencyProperty OriginalBorderBrushProperty = DependencyProperty.RegisterAttached(
        "OriginalBorderBrush", typeof(BrushSnapshot), typeof(MotionPlayback), new PropertyMetadata(null));

    /// <summary>Puts a host at its design angle when no motion is running.</summary>
    public static void SetDesignRotation(FrameworkElement host, double angle)
    {
        SetBaseRotation(host, angle);
        host.RenderTransformOrigin = new Point(0.5, 0.5);
        host.RenderTransform = Math.Abs(angle) > 0.001 ? new RotateTransform(angle) : Transform.Identity;
    }

    /// <summary>
    /// Runs one phase from its first keyframe to its last. The host's own opacity is treated as a
    /// ceiling, so a clip that fades to 0 hides a half-transparent element rather than brightening it.
    /// </summary>
    public static void PlayPhase(FrameworkElement host, MotionPhase? phase, double baseOpacity, Action? done = null, double speed = 1, bool repeat = false, bool pingPong = false, string? pieceId = null)
    {
        if (phase is null || !phase.Enabled || phase.Seconds <= 0.001 || phase.IsEmpty)
        {
            done?.Invoke();
            return;
        }
        // A whole-graphic play (pieceId null) only runs the tracks that belong to no piece, so a
        // scoped key never lands on the canvas instead of on the item it was authored for.
        var tracks = phase.Tracks.Where(track => Matches(track.PieceId, pieceId)).ToList();
        var colorTracks = phase.Colors.Where(track => Matches(track.PieceId, pieceId)).ToList();
        if (tracks.Count == 0 && colorTracks.Count == 0) { done?.Invoke(); return; }
        Stop(host);
        var seconds = Math.Max(0.05, phase.Seconds / Math.Clamp(speed <= 0 ? 1 : speed, 0.1, 8));
        var duration = TimeSpan.FromSeconds(seconds);
        var repeatBehaviour = repeat ? RepeatBehavior.Forever : new RepeatBehavior(1);

        var group = new TransformGroup();
        var scale = new ScaleTransform(1, 1);
        var rotate = new RotateTransform();
        var skew = new SkewTransform();
        var translate = new TranslateTransform();
        group.Children.Add(scale);
        group.Children.Add(rotate);
        group.Children.Add(skew);
        group.Children.Add(translate);
        // The design angle is part of the element, not part of the clip: keep it under every
        // transform the clip animates.
        group.Children.Add(new RotateTransform(GetBaseRotation(host)));
        host.RenderTransformOrigin = new Point(0.5, 0.5);
        host.RenderTransform = group;

        var finished = false;
        void Finish()
        {
            if (finished) return;
            finished = true;
            if (!repeat)
            {
                Reset(host, baseOpacity);
                done?.Invoke();
            }
        }

        // Opacity always has a track so there is a reliable completion callback even for a clip that
        // only moves. A clip with no opacity keys holds the element at its normal level.
        var opacityTrack = tracks.FirstOrDefault(track => track.Target == MotionTarget.Opacity);
        var opacity = BuildDouble(opacityTrack, seconds, 0, 1, baseOpacity);
        opacity.RepeatBehavior = repeatBehaviour;
        opacity.AutoReverse = pingPong;
        opacity.Completed += (_, _) => Finish();
        host.BeginAnimation(UIElement.OpacityProperty, opacity);

        foreach (var track in tracks)
        {
            if (track.Target == MotionTarget.Opacity) continue;
            switch (track.Target)
            {
                case MotionTarget.X:
                    AnimateTransform(translate, TranslateTransform.XProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.Y:
                    AnimateTransform(translate, TranslateTransform.YProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.Scale:
                    AnimateTransform(scale, ScaleTransform.ScaleXProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    AnimateTransform(scale, ScaleTransform.ScaleYProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.ScaleX:
                    AnimateTransform(scale, ScaleTransform.ScaleXProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.ScaleY:
                    AnimateTransform(scale, ScaleTransform.ScaleYProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.Rotate:
                    AnimateTransform(rotate, RotateTransform.AngleProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.SkewX:
                    AnimateTransform(skew, SkewTransform.AngleXProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.SkewY:
                    AnimateTransform(skew, SkewTransform.AngleYProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.Blur:
                    AnimateBlur(host, track, seconds, repeatBehaviour, pingPong);
                    break;
                case MotionTarget.Width:
                    AnimateElement(host, FrameworkElement.WidthProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.Height:
                    AnimateElement(host, FrameworkElement.HeightProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.TextOpacity:
                    foreach (var text in Descendants(host).OfType<TextBlock>())
                        AnimateElement(text, UIElement.OpacityProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.BackgroundOpacity:
                    AnimateBrushOpacity(host, MotionColorRole.Background, track, seconds, repeatBehaviour, pingPong);
                    break;
                case MotionTarget.BorderOpacity:
                    AnimateBrushOpacity(host, MotionColorRole.Border, track, seconds, repeatBehaviour, pingPong);
                    break;
                case MotionTarget.FontSize:
                    foreach (var text in Descendants(host).OfType<TextBlock>())
                        AnimateElement(text, TextBlock.FontSizeProperty, track, seconds, repeatBehaviour, pingPong, 1);
                    break;
                case MotionTarget.BorderThickness:
                    AnimateBorderThickness(host, track, seconds, repeatBehaviour, pingPong);
                    break;
            }
        }

        // Colour keys travel with the phase, relative to the same clock.
        AnimateColours(host, colorTracks, seconds, repeatBehaviour, pingPong);
    }

    /// <summary>
    /// True when a track belongs to the piece being played. An empty piece id is the whole graphic,
    /// so an element's play still receives the clip's global keys as well as its own.
    /// </summary>
    static bool Matches(string? trackPiece, string? pieceId)
    {
        if (string.IsNullOrWhiteSpace(trackPiece)) return true;
        return pieceId is not null && string.Equals(trackPiece, pieceId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Puts a phase at an instant, without animating: the scrub preview. Values are read straight
    /// from the tracks at <paramref name="time"/>, so dragging the ruler shows the frame it lands on.
    /// </summary>
    public static void ApplyAt(FrameworkElement host, MotionPhase? phase, double time, string? pieceId = null)
    {
        if (phase is null) return;
        var tracks = phase.Tracks.Where(track => Matches(track.PieceId, pieceId)).ToList();
        var colorTracks = phase.Colors.Where(track => Matches(track.PieceId, pieceId)).ToList();
        Stop(host);
        host.Opacity = 1;
        host.Effect = null;
        var group = new TransformGroup();
        var scale = new ScaleTransform(1, 1);
        var rotate = new RotateTransform();
        var skew = new SkewTransform();
        var translate = new TranslateTransform();
        group.Children.Add(scale);
        group.Children.Add(rotate);
        group.Children.Add(skew);
        group.Children.Add(translate);
        group.Children.Add(new RotateTransform(GetBaseRotation(host)));
        host.RenderTransformOrigin = new Point(0.5, 0.5);
        host.RenderTransform = group;
        foreach (var track in tracks)
        {
            if (track.ValueAt(time) is not { } value) continue;
            switch (track.Target)
            {
                case MotionTarget.X: translate.X = value; break;
                case MotionTarget.Y: translate.Y = value; break;
                case MotionTarget.Scale: scale.ScaleX = value; scale.ScaleY = value; break;
                case MotionTarget.ScaleX: scale.ScaleX = value; break;
                case MotionTarget.ScaleY: scale.ScaleY = value; break;
                case MotionTarget.Rotate: rotate.Angle = value; break;
                case MotionTarget.SkewX: skew.AngleX = value; break;
                case MotionTarget.SkewY: skew.AngleY = value; break;
                case MotionTarget.Opacity: host.Opacity = Math.Clamp(value, 0, 1); break;
                case MotionTarget.Blur: host.Effect = new BlurEffect { Radius = Math.Max(0, value), KernelType = KernelType.Gaussian }; break;
                case MotionTarget.Width: host.Width = Math.Max(0, value); break;
                case MotionTarget.Height: host.Height = Math.Max(0, value); break;
                case MotionTarget.TextOpacity:
                    foreach (var text in Descendants(host).OfType<TextBlock>()) text.Opacity = Math.Clamp(value, 0, 1);
                    break;
                case MotionTarget.BackgroundOpacity:
                    foreach (var brush in Targets(host, MotionColorRole.Background)) brush.Opacity = Math.Clamp(value, 0, 1);
                    break;
                case MotionTarget.BorderOpacity:
                    foreach (var brush in Targets(host, MotionColorRole.Border)) brush.Opacity = Math.Clamp(value, 0, 1);
                    break;
                case MotionTarget.FontSize:
                    foreach (var text in Descendants(host).OfType<TextBlock>()) text.FontSize = Math.Clamp(value, 1, 1000);
                    break;
                case MotionTarget.BorderThickness:
                    foreach (var border in Descendants(host).OfType<Border>()) border.BorderThickness = new Thickness(Math.Clamp(value, 0, 100));
                    foreach (var shape in Descendants(host).OfType<Shape>()) shape.StrokeThickness = Math.Clamp(value, 0, 100);
                    break;
            }
        }
        foreach (var track in colorTracks)
        {
            if (track.ColorAt(time) is not { } hex) continue;
            var color = Paint.ColorOf(hex, Colors.White);
            foreach (var target in ColourTargets(host, track.Role)) target.Set(color);
        }
    }

    /// <summary>Plays an animation clip's motion, either once or for as long as the graphic is up.</summary>
    public static void PlayClip(FrameworkElement host, MotionClip? clip, double baseOpacity, bool ambient, Action? done = null, MotionPhase? phase = null)
    {
        if (clip is null || clip.IsEmpty) { done?.Invoke(); return; }
        phase ??= ambient
            ? clip.Phases.FirstOrDefault(candidate => candidate.Enabled && !candidate.IsEmpty && !candidate.IsTriggered)
              ?? clip.Phases.FirstOrDefault(candidate => candidate.Enabled && !candidate.IsEmpty)
            : clip.EnterPhase;
        if (phase is null) { done?.Invoke(); return; }
        PlayPhase(host, phase, baseOpacity, done, 1, repeat: ambient && clip.Loop, pingPong: ambient && clip.PingPong);
    }

    /// <summary>Stops every animation this player put on an element and leaves it at rest.</summary>
    public static void Stop(FrameworkElement host)
    {
        host.BeginAnimation(UIElement.OpacityProperty, null);
        host.BeginAnimation(FrameworkElement.WidthProperty, null);
        host.BeginAnimation(FrameworkElement.HeightProperty, null);
        switch (host.RenderTransform)
        {
            case ScaleTransform scale:
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                break;
            case RotateTransform rotate:
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                break;
            case TranslateTransform translate:
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                break;
            case SkewTransform skew:
                skew.BeginAnimation(SkewTransform.AngleXProperty, null);
                skew.BeginAnimation(SkewTransform.AngleYProperty, null);
                break;
            case TransformGroup group:
                foreach (var child in group.Children) StopTransform(child);
                break;
        }
        if (host.Effect is BlurEffect blur) blur.BeginAnimation(BlurEffect.RadiusProperty, null);
        // A clip never leaves a property behind: box size, text size, outline and every brush
        // return to the values the designer actually saved.
        foreach (var text in Descendants(host).OfType<TextBlock>())
        {
            text.BeginAnimation(UIElement.OpacityProperty, null);
            text.BeginAnimation(TextBlock.FontSizeProperty, null);
        }
        foreach (var border in Descendants(host).OfType<Border>())
            border.BeginAnimation(Border.BorderThicknessProperty, null);
        foreach (var shape in Descendants(host).OfType<Shape>())
            shape.BeginAnimation(Shape.StrokeThicknessProperty, null);
        RestoreBrushes(host);
    }

    static void StopTransform(Transform transform)
    {
        switch (transform)
        {
            case ScaleTransform scale:
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                break;
            case RotateTransform rotate:
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                break;
            case TranslateTransform translate:
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                break;
            case SkewTransform skew:
                skew.BeginAnimation(SkewTransform.AngleXProperty, null);
                skew.BeginAnimation(SkewTransform.AngleYProperty, null);
                break;
        }
    }

    /// <summary>Puts an element back at rest at its design opacity.</summary>
    static void Reset(FrameworkElement host, double baseOpacity)
    {
        Stop(host);
        host.Opacity = Math.Clamp(baseOpacity, 0, 1);
        var angle = GetBaseRotation(host);
        host.RenderTransform = Math.Abs(angle) > 0.001 ? new RotateTransform(angle) : Transform.Identity;
        host.Effect = null;
    }

    static void AnimateTransform(Animatable transform, DependencyProperty property, MotionTrack track, double seconds, RepeatBehavior repeat, bool pingPong, double fallback)
    {
        var animation = BuildDouble(track, seconds, 0, fallback, 1);
        animation.RepeatBehavior = repeat;
        animation.AutoReverse = pingPong;
        transform.BeginAnimation(property, animation);
    }

    static void AnimateBlur(FrameworkElement host, MotionTrack track, double seconds, RepeatBehavior repeat, bool pingPong)
    {
        var blur = host.Effect as BlurEffect ?? new BlurEffect { Radius = 0, KernelType = KernelType.Gaussian };
        host.Effect = blur;
        var animation = BuildDouble(track, seconds, 0, 0, 1);
        animation.RepeatBehavior = repeat;
        animation.AutoReverse = pingPong;
        blur.BeginAnimation(BlurEffect.RadiusProperty, animation);
    }

    static void AnimateElement(IAnimatable element, DependencyProperty property, MotionTrack track, double seconds, RepeatBehavior repeat, bool pingPong, double fallback)
    {
        var animation = BuildDouble(track, seconds, 0, fallback, 1);
        animation.RepeatBehavior = repeat;
        animation.AutoReverse = pingPong;
        element.BeginAnimation(property, animation);
    }

    static void AnimateBrushOpacity(FrameworkElement host, MotionColorRole role, MotionTrack track, double seconds, RepeatBehavior repeat, bool pingPong)
    {
        foreach (var brush in Targets(host, role))
        {
            if (brush.GetValue(OriginalBrushOpacityProperty) is not double)
                brush.SetValue(OriginalBrushOpacityProperty, brush.Opacity);
            var animation = BuildDouble(track, seconds, 1, 1, 1);
            animation.RepeatBehavior = repeat;
            animation.AutoReverse = pingPong;
            brush.BeginAnimation(Brush.OpacityProperty, animation);
        }
    }

    static void AnimateBorderThickness(FrameworkElement host, MotionTrack track, double seconds, RepeatBehavior repeat, bool pingPong)
    {
        var keys = track.Keys.OrderBy(key => key.Time).ToList();
        if (keys.Count == 0) return;
        var animation = new ThicknessAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd, RepeatBehavior = repeat, AutoReverse = pingPong };
        if (keys[0].Time > 0.0001)
            animation.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(Math.Max(0, keys[0].Value)), KeyTime.FromTimeSpan(TimeSpan.Zero)));
        foreach (var key in keys)
        {
            var fraction = seconds <= 0.0001 ? 1 : Math.Clamp(key.Time / seconds, 0, 1);
            animation.KeyFrames.Add(new EasingThicknessKeyFrame(new Thickness(Math.Clamp(key.Value, 0, 100)), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(fraction * seconds)))
            {
                EasingFunction = Ease(key.Ease)
            });
        }
        foreach (var border in Descendants(host).OfType<Border>())
            border.BeginAnimation(Border.BorderThicknessProperty, animation);
        foreach (var shape in Descendants(host).OfType<Shape>())
            shape.BeginAnimation(Shape.StrokeThicknessProperty, BuildDouble(track, seconds, 0, 0, 1));
    }

    /// <summary>
    /// Turns one track into keyframes. A key's easing shapes the approach into it, which is why the
    /// curve belongs to the key rather than to the whole clip. Opacity is multiplied by the host's
    /// own level so the element never becomes brighter than it was designed to be.
    /// </summary>
    static DoubleAnimationUsingKeyFrames BuildDouble(MotionTrack? track, double seconds, double defaultValue, double fallback, double ceiling)
    {
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
        var keys = track?.Keys.OrderBy(key => key.Time).ToList() ?? [];
        if (keys.Count == 0)
        {
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(fallback, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(fallback, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds))));
            return animation;
        }
        double Limit(double value) => Math.Clamp(value, -100000, 100000) * (ceiling == 1 && defaultValue == 0 ? 1 : 1);
        if (keys[0].Time > 0.0001)
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(Limit(keys[0].Value), KeyTime.FromTimeSpan(TimeSpan.Zero)));
        foreach (var key in keys)
        {
            var fraction = seconds <= 0.0001 ? 1 : Math.Clamp(key.Time / seconds, 0, 1);
            var value = Limit(key.Value);
            if (ceiling != 1) value = Math.Clamp(value, 0, 1) * ceiling;
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(fraction * seconds)))
            {
                EasingFunction = Ease(key.Ease)
            });
        }
        return animation;
    }

    static IEasingFunction Ease(MotionEase ease) => ease switch
    {
        MotionEase.Linear => new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        MotionEase.In => new CubicEase { EasingMode = EasingMode.EaseIn },
        MotionEase.Out => new CubicEase { EasingMode = EasingMode.EaseOut },
        MotionEase.InOut => new CubicEase { EasingMode = EasingMode.EaseInOut },
        MotionEase.Back => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 },
        MotionEase.Bounce => new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 2, Bounciness = 2 },
        MotionEase.Elastic => new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 6 },
        _ => new QuadraticEase { EasingMode = EasingMode.EaseInOut }
    };

    // ---------- colour keys ----------

    /// <summary>
    /// Repaints the text, plate and outline through a phase's colour keys. Every brush is replaced
    /// with an unfrozen copy first, because an animated frozen brush throws.
    /// </summary>
    public static void AnimateColours(FrameworkElement host, IEnumerable<ColorTrack> tracks, double seconds, RepeatBehavior repeat, bool pingPong)
    {
        foreach (var track in tracks)
        {
            if (track.Keys.Count == 0) continue;
            var animation = BuildColour(track, seconds);
            animation.RepeatBehavior = repeat;
            animation.AutoReverse = pingPong;
            foreach (var target in ColourTargets(host, track.Role)) target.Animate(animation);
        }
    }

    /// <summary>Restores every brush a clip has touched, including colours held by an animation.</summary>
    static void RestoreBrushes(DependencyObject root)
    {
        foreach (var element in Descendants(root))
        {
            RestoreOwnerBrush(element, OriginalForegroundBrushProperty, brush =>
            {
                if (element is TextBlock text) text.Foreground = brush;
                else if (element is TextBox box) box.Foreground = brush;
            });
            RestoreOwnerBrush(element, OriginalBackgroundBrushProperty, brush =>
            {
                if (element is Border border) border.Background = brush;
                else if (element is Panel panel) panel.Background = brush;
                else if (element is Shape shape) shape.Fill = brush;
            });
            RestoreOwnerBrush(element, OriginalBorderBrushProperty, brush =>
            {
                if (element is Border border) border.BorderBrush = brush;
                else if (element is Shape shape) shape.Stroke = brush;
            });
        }
        foreach (var brush in Brushes(root))
        {
            // A frozen brush is shared, immutable paint. It was never animated (colour animation
            // gets a live copy), and WPF refuses BeginAnimation on it, so leave it alone.
            if (brush.IsFrozen) continue;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.BeginAnimation(Brush.OpacityProperty, null);
            if (brush.GetValue(OriginalColourProperty) is Color colour) brush.Color = colour;
            if (brush.GetValue(OriginalBrushOpacityProperty) is double opacity) brush.Opacity = opacity;
            brush.ClearValue(OriginalColourProperty);
            brush.ClearValue(OriginalBrushOpacityProperty);
        }
    }

    static void RestoreOwnerBrush(DependencyObject owner, DependencyProperty property, Action<Brush?> assign)
    {
        if (owner.GetValue(property) is not BrushSnapshot snapshot) return;
        assign(snapshot.Brush);
        owner.ClearValue(property);
    }

    static IEnumerable<SolidColorBrush> Brushes(DependencyObject root)
    {
        foreach (var element in Descendants(root))
        {
            switch (element)
            {
                case TextBlock { Foreground: SolidColorBrush text }:
                    yield return text;
                    break;
                case TextBox { Foreground: SolidColorBrush box }:
                    yield return box;
                    break;
                case Border border:
                    if (border.Background is SolidColorBrush plate) yield return plate;
                    if (border.BorderBrush is SolidColorBrush outline) yield return outline;
                    break;
                case Panel panel when panel.Background is SolidColorBrush panelBrush:
                    yield return panelBrush;
                    break;
                case Shape shape:
                    if (shape.Fill is SolidColorBrush fill) yield return fill;
                    if (shape.Stroke is SolidColorBrush stroke) yield return stroke;
                    break;
            }
        }
    }

    static ColorAnimationUsingKeyFrames BuildColour(ColorTrack track, double seconds)
    {
        var animation = new ColorAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
        var keys = track.Keys.OrderBy(key => key.Time).ToList();
        if (keys.Count == 0) return animation;
        Color Of(string? value) => Paint.ColorOf(value, Colors.White);
        if (keys[0].Time > 0.0001)
            animation.KeyFrames.Add(new LinearColorKeyFrame(Of(keys[0].Color), KeyTime.FromTimeSpan(TimeSpan.Zero)));
        foreach (var key in keys)
        {
            var fraction = seconds <= 0.0001 ? 1 : Math.Clamp(key.Time / seconds, 0, 1);
            animation.KeyFrames.Add(new EasingColorKeyFrame(Of(key.Color), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(fraction * seconds)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });
        }
        return animation;
    }

    /// <summary>Every solid brush on a host that paints one of the colour roles.</summary>
    static IEnumerable<SolidColorBrush> Targets(DependencyObject root, MotionColorRole role)
    {
        foreach (var element in Descendants(root))
        {
            switch (role)
            {
                case MotionColorRole.Text:
                    if (element is TextBlock text && text.Foreground is SolidColorBrush textBrush)
                        yield return Live(text, ref textBrush, brush => text.Foreground = brush);
                    if (element is TextBox box && box.Foreground is SolidColorBrush boxBrush)
                        yield return Live(box, ref boxBrush, brush => box.Foreground = brush);
                    break;
                case MotionColorRole.Background:
                    if (element is Border border && border.Background is SolidColorBrush borderBrush)
                        yield return Live(border, ref borderBrush, brush => border.Background = brush);
                    if (element is Panel panel && panel.Background is SolidColorBrush panelBrush)
                        yield return Live(panel, ref panelBrush, brush => panel.Background = brush);
                    if (element is Shape { Fill: SolidColorBrush fill } shape)
                        yield return Live(shape, ref fill, brush => shape.Fill = brush);
                    break;
                case MotionColorRole.Border:
                    if (element is Border { BorderBrush: SolidColorBrush edge } outlined)
                        yield return Live(outlined, ref edge, brush => outlined.BorderBrush = brush);
                    if (element is Shape { Stroke: SolidColorBrush stroke } stroked)
                        yield return Live(stroked, ref stroke, brush => stroked.Stroke = brush);
                    break;
            }
        }
    }

    /// <summary>
    /// Every painted property a colour role owns, including gradient and textured brushes. A
    /// colour key is one colour, so those paints are temporarily replaced by a flat brush of that
    /// colour; the original brush is restored when the phase ends.
    /// </summary>
    static IEnumerable<ColourTarget> ColourTargets(DependencyObject root, MotionColorRole role)
    {
        foreach (var element in Descendants(root))
        {
            switch (role)
            {
                case MotionColorRole.Text:
                    if (element is TextBlock text)
                        yield return new ColourTarget(text, () => text.Foreground, brush => text.Foreground = brush, OriginalForegroundBrushProperty);
                    if (element is TextBox box)
                        yield return new ColourTarget(box, () => box.Foreground, brush => box.Foreground = brush, OriginalForegroundBrushProperty);
                    break;
                case MotionColorRole.Background:
                    if (element is Border border)
                        yield return new ColourTarget(border, () => border.Background, brush => border.Background = brush, OriginalBackgroundBrushProperty);
                    if (element is Panel panel)
                        yield return new ColourTarget(panel, () => panel.Background, brush => panel.Background = brush, OriginalBackgroundBrushProperty);
                    if (element is Shape shape)
                        yield return new ColourTarget(shape, () => shape.Fill, brush => shape.Fill = brush, OriginalBackgroundBrushProperty);
                    break;
                case MotionColorRole.Border:
                    if (element is Border outlined)
                        yield return new ColourTarget(outlined, () => outlined.BorderBrush, brush => outlined.BorderBrush = brush, OriginalBorderBrushProperty);
                    if (element is Shape stroked)
                        yield return new ColourTarget(stroked, () => stroked.Stroke, brush => stroked.Stroke = brush, OriginalBorderBrushProperty);
                    break;
            }
        }
    }

    /// <summary>A painted property that can hold any brush, not just a solid one.</summary>
    sealed class ColourTarget
    {
        readonly DependencyObject _owner;
        readonly Func<Brush?> _read;
        readonly Action<Brush?> _assign;
        readonly DependencyProperty _originalProperty;

        public ColourTarget(DependencyObject owner, Func<Brush?> read, Action<Brush?> assign, DependencyProperty originalProperty)
        {
            _owner = owner;
            _read = read;
            _assign = assign;
            _originalProperty = originalProperty;
        }

        public void Set(Color color)
        {
            if (_read() is SolidColorBrush solid)
            {
                if (solid.IsFrozen)
                {
                    solid = new SolidColorBrush(solid.Color);
                    _assign(solid);
                }
                solid.Color = color;
                return;
            }
            RememberOriginal();
            _assign(new SolidColorBrush(color));
        }

        public void Animate(ColorAnimationUsingKeyFrames animation)
        {
            if (_read() is SolidColorBrush solid)
            {
                if (solid.IsFrozen)
                {
                    solid = new SolidColorBrush(solid.Color);
                    _assign(solid);
                }
                if (solid.GetValue(OriginalColourProperty) is not Color)
                    solid.SetValue(OriginalColourProperty, solid.Color);
                solid.BeginAnimation(SolidColorBrush.ColorProperty, animation);
                return;
            }
            RememberOriginal();
            var brush = new SolidColorBrush(FirstColour(_read()));
            brush.SetValue(OriginalColourProperty, brush.Color);
            _assign(brush);
            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        void RememberOriginal()
        {
            if (_owner.GetValue(_originalProperty) is null)
                _owner.SetValue(_originalProperty, new BrushSnapshot(_read()));
        }

        static Color FirstColour(Brush? brush) => brush switch
        {
            SolidColorBrush solid => solid.Color,
            GradientBrush gradient when gradient.GradientStops.Count > 0 => gradient.GradientStops[0].Color,
            _ => Colors.White
        };
    }

    sealed class BrushSnapshot
    {
        public Brush? Brush { get; }
        public BrushSnapshot(Brush? brush) => Brush = brush;
    }

    /// <summary>An unfrozen copy of a brush, already assigned, ready to animate.</summary>
    static SolidColorBrush Live(DependencyObject owner, ref SolidColorBrush current, Action<SolidColorBrush> assign)
    {
        var copy = current.IsFrozen ? new SolidColorBrush(current.Color) : current;
        if (copy.IsFrozen) copy = new SolidColorBrush(current.Color);
        assign(copy);
        return copy;
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }
}
