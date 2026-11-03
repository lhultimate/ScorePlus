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
            }
        }
        foreach (var track in colorTracks)
        {
            if (track.ColorAt(time) is not { } hex) continue;
            var color = Paint.ColorOf(hex, Colors.White);
            foreach (var brush in Targets(host, track.Role)) brush.Color = color;
        }
    }

    /// <summary>Plays an animation clip's motion, either once or for as long as the graphic is up.</summary>
    public static void PlayClip(FrameworkElement host, MotionClip? clip, double baseOpacity, bool ambient, Action? done = null)
    {
        if (clip is null || clip.IsEmpty) { done?.Invoke(); return; }
        var phase = ambient ? clip.Phases.FirstOrDefault(candidate => candidate.Enabled && !candidate.IsEmpty) : clip.EnterPhase;
        if (phase is null) { done?.Invoke(); return; }
        PlayPhase(host, phase, baseOpacity, done, 1, repeat: ambient && clip.Loop, pingPong: ambient && clip.PingPong);
    }

    /// <summary>Stops every animation this player put on an element and leaves it at rest.</summary>
    public static void Stop(FrameworkElement host)
    {
        host.BeginAnimation(UIElement.OpacityProperty, null);
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
            foreach (var brush in Targets(host, track.Role))
                brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
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
