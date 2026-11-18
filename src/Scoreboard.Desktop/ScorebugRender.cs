using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
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

    public static string ResolveBackground(ScorebugElement item, GameState? state)
    {
        var slot = state?.ColorFor(item.BackgroundSource);
        return string.IsNullOrWhiteSpace(slot) ? item.Background : slot;
    }

    /// <summary>The gradient's second colour, after its team-palette slot is resolved.</summary>
    public static string ResolveBackground2(ScorebugElement item, GameState? state) =>
        state?.ColorFor(item.BackgroundSource2) ?? item.BackgroundColor2;

    /// <summary>
    /// What the background actually paints, as a comparable string: a colour, a gradient, or a
    /// gradient carrying a material finish. Views cache on this so a brush is only rebuilt on change.
    /// </summary>
    public static string BackgroundSignature(ScorebugElement item, GameState? state) => Paint.Signature(item, state);

    /// <summary>
    /// The brush an element's box is painted with: a flat colour, a multi-stop or radial gradient,
    /// or the same paint under a texture, faded by the plate's own opacity.
    /// </summary>
    public static Brush BackgroundBrush(ScorebugElement item, GameState? state = null) => Paint.Background(item, state);

    /// <summary>The colour of the element's outline, after its team-palette slot is resolved.</summary>
    public static string ResolveBorder(ScorebugElement item, GameState? state) =>
        state?.ColorFor(item.BorderSource) ?? item.BorderColor;

    /// <summary>The outline an element draws around its own box, or nothing when it is switched off.</summary>
    public static Brush HostBorder(ScorebugElement item, GameState? state = null) => Paint.Border(item, state);

    /// <summary>True when the element carries a plate paint beyond its flat default: a gradient or
    /// a material finish. Such a paint is explicit artwork, so it shows even on a logo or shape
    /// whose default plate is transparent.</summary>
    public static bool HasPlatePaint(ScorebugElement item) =>
        item.BackgroundTexture is { Enabled: true }
        || item.BackgroundGradientPaint is { IsUsable: true }
        || item.BackgroundGradient;

    /// <summary>Background painted behind an element. Text always has its plate; shapes and logos
    /// keep their old clear host until a background is actually chosen for them. A gradient or
    /// texture counts as choosing a background, which is what lets a logo be painted and textured
    /// instead of only showing its studio placeholder.</summary>
    public static Brush HostBackground(ScorebugElement item, GameState? state = null)
    {
        var platePaint = HasPlatePaint(item);
        if (item.TransparentBackground && !platePaint) return Brushes.Transparent;
        if (item.Kind is ScorebugElementKind.Shape or ScorebugElementKind.Logo
            && item.BackgroundSource == ScorebugColorSource.Custom
            && string.Equals(item.Background, "#CC141D28", StringComparison.OrdinalIgnoreCase)
            && !platePaint)
            return Brushes.Transparent;
        return BackgroundBrush(item, state);
    }

    /// <summary>
    /// The colour the inside of a baseball base is filled with. Empty follows the element's text
    /// colour, so an existing diamond keeps its look; a palette slot follows that team.
    /// </summary>
    public static string ResolveIconFill(ScorebugElement item, GameState? state) =>
        item.IconFillSource != ScorebugColorSource.Custom
            ? state?.ColorFor(item.IconFillSource) ?? item.IconFillColor
            : string.IsNullOrWhiteSpace(item.IconFillColor) ? ResolveForeground(item, state) : item.IconFillColor;

    /// <summary>The colour the icon actually draws with: its own colour, a made icon's resolved
    /// state colour, or the element's text colour when neither one is set.</summary>
    public static string ResolveIconColor(ScorebugElement item, GameState? state)
    {
        if (!string.IsNullOrWhiteSpace(item.IconColor)) return item.IconColor;
        if (item.Icon == ScorebugIcon.Custom)
        {
            var icon = CustomIconStore.Find(item.CustomIconId);
            if (icon is not null) return icon.Resolve(state).Color;
        }
        return ResolveForeground(item, state);
    }

    public static string? ResolveLogoPath(ScorebugElement item, GameState? state)
    {
        if (item.LogoSide == ScorebugLogoSide.None) return item.ImagePath;
        var path = state?.LogoFor(item.LogoSide);
        return string.IsNullOrWhiteSpace(path) ? item.ImagePath : path;
    }

    /// <summary>An image element's file, including a portrait that follows the selected player.</summary>
    public static string? ResolveImagePath(ScorebugElement item, GameState? state)
    {
        if (item.PlayerPhotoSide != ScorebugPlayerPhotoSide.None)
        {
            var player = item.PlayerPhotoSide == ScorebugPlayerPhotoSide.Home ? state?.HomePlayer : state?.AwayPlayer;
            if (!string.IsNullOrWhiteSpace(player?.PhotoPath)) return player.PhotoPath;
        }
        return item.ImagePath;
    }

    public static FrameworkElement CreateContent(ScorebugElement item, Func<GameState>? state, MediaPlayback? playback = null, bool muteVideo = true, bool showVideoPlaceholder = false, bool onAirVideo = false,
        ScorebugCatalog? catalog = null, GameEngine? engine = null, string? displayId = null, IReadOnlySet<string>? layoutChain = null)
    {
        // A live-layout element is a real scorebug feed inside this box: the source layout keeps
        // its own artwork and values, and redraws whenever either changes.
        if (item.Kind == ScorebugElementKind.Layout)
        {
            if (catalog is null || engine is null) return LayoutPlaceholder("LIVE LAYOUT");
            return new ScorebugLayoutFeed(catalog, engine, state, playback, muteVideo, item, displayId, layoutChain, onAirVideo);
        }
        if (item.Kind == ScorebugElementKind.Video) return new VideoSurface(item, playback, muteVideo, showVideoPlaceholder, onAirVideo);
        // A text element can be a bottom-screen ticker: it travels continuously instead of sitting
        // still, with its items separated by the same dot used by the league ticker.
        if (item.Kind == ScorebugElementKind.Text && item.Ticker) return new TickerView(item, state);
        // HTML is drawn by a browser engine and captured as a frame, so every feed shows the same
        // picture at the element's design size.
        if (item.Kind == ScorebugElementKind.Html) return new HtmlSurface(item, showVideoPlaceholder);
        // A still image imported into the studio. It keeps its picture slot while the file is there,
        // and goes blank (rather than stale) if the file is moved away.
        if (item.Kind == ScorebugElementKind.Image)
        {
            var picture = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            ApplyToContent(picture, item, state?.Invoke());
            return picture;
        }
        // Icons are drawn from live data rather than typed text: small bars per timeout, or a dot
        // showing which team has the ball.
        if (item.Kind == ScorebugElementKind.Text && item.Icon != ScorebugIcon.None) return new ScorebugIconView(item, state);
        // A logo view keeps its image slot whether or not a logo exists yet, so a team logo set
        // after the graphic was built appears immediately instead of waiting for a rebuild.
        if (item.Kind == ScorebugElementKind.Logo) return new ScorebugLogoView(item, state?.Invoke(), showVideoPlaceholder);
        if (item.Kind == ScorebugElementKind.Shape) return CreateShape(item, state?.Invoke());
        var text = state is null ? item.Content : ScoreboardTemplates.Bind(item.Content, state(), item.PeriodFormat, item.DownCaps, item.PlayClockColon, item.GameClockLeadingZero, item.ClockDecimals);
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
            // A true parallelogram: the diagonals are geometry, not a scaled rectangle, and WPF
            // anti-aliases the sloped edges instead of leaving a stepped "broken" border.
            ScorebugShape.Parallelogram => new Polygon
            {
                Points = new PointCollection { new(0.24, 0), new(1, 0), new(0.76, 1), new(0, 1) },
                StrokeLineJoin = PenLineJoin.Round
            },
            _ => new Rectangle
            {
                RadiusX = Math.Max(0, item.CornerRadius),
                RadiusY = Math.Max(0, item.CornerRadius)
            }
        };
        shape.Stretch = Stretch.Fill;
        // A transparent shape has no flat fill, only its outline — unless an explicit gradient or
        // material paint was chosen, in which case that artwork is the fill.
        shape.Fill = HostBackground(item, state);
        shape.Stroke = HostBorder(item, state);
        shape.StrokeThickness = Math.Max(0, item.BorderThickness);
        shape.StrokeStartLineCap = PenLineCap.Round;
        shape.StrokeEndLineCap = PenLineCap.Round;
        // Smooth, full-strength diagonals on every render scale; snapping to device pixels made a
        // sloped edge look chipped when the scorebug was scaled into an audience window.
        shape.SnapsToDevicePixels = false;
        shape.UseLayoutRounding = false;
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
            case TickerView ticker:
                ticker.Update(item, state);
                break;
            case TextBlock plain:
                plain.Text = text ?? item.Content;
                break;
            case Shape shape:
                if (shape is Rectangle rounded)
                {
                    rounded.RadiusX = Math.Max(0, item.CornerRadius);
                    rounded.RadiusY = Math.Max(0, item.CornerRadius);
                }
                shape.Fill = HostBackground(item, state);
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
            case ScorebugLayoutFeed layoutFeed:
                layoutFeed.Update(item);
                break;
            case Image image:
                var path = ResolveImagePath(item, state);
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

    internal static FrameworkElement LayoutPlaceholder(string text) => new Border
    {
        Background = ParseBrush("#66101822"),
        BorderBrush = ParseBrush("#6638D7A3"),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(10),
        Child = new TextBlock
        {
            Text = text,
            Foreground = ParseBrush("#8FA0B4"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }
    };

    /// <summary>
    /// Draws a timeline piece. Text pieces bind the same tokens as normal elements, so a pop-up can
    /// read <c>{{LAST_SCORE_CHANGE}}</c> and always show the score that was actually pressed. A
    /// logo piece follows the home/away logo, and a shape piece uses the same silhouettes as a
    /// designed shape element.
    /// </summary>
    public static FrameworkElement CreateMotionPiece(MotionPiece piece, Func<GameState>? state, ScorebugCatalog? catalog = null, GameEngine? engine = null)
    {
        var game = state?.Invoke();
        if (piece.Kind == MotionPieceKind.Logo)
        {
            var element = new ScorebugElement
            {
                Name = piece.Name,
                Kind = ScorebugElementKind.Logo,
                LogoSide = piece.Logo,
                Content = piece.Name,
                Width = piece.Width,
                Height = piece.Height,
                Foreground = piece.TextColor,
                Background = piece.Fill,
                BorderColor = piece.Border,
                BorderThickness = piece.BorderThickness
            };
            return CreateContent(element, state, showVideoPlaceholder: false, catalog: catalog, engine: engine);
        }
        if (piece.Kind == MotionPieceKind.Shape)
        {
            var element = new ScorebugElement
            {
                Name = piece.Name,
                Kind = ScorebugElementKind.Shape,
                Shape = piece.Shape,
                Width = piece.Width,
                Height = piece.Height,
                Background = piece.Fill,
                BorderColor = piece.Border,
                BorderThickness = piece.BorderThickness
            };
            return CreateShape(element, game);
        }
        var text = game is null ? piece.Text : ScoreboardTemplates.Bind(piece.Text, game);
        return new TextBlock
        {
            Text = text,
            FontSize = piece.FontSize,
            Foreground = new SolidColorBrush(Paint.ColorOf(piece.TextColor, Colors.White)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
    }
}

/// <summary>
/// A text element that scrolls across its box like a lower-screen ticker. The text is duplicated
/// once with a gap, then the pair travels exactly one period so the loop has no seam. Every value
/// change keeps the same animation unless the words, style or speed actually changed, which is what
/// makes the ticker immune to the editor's normal once-a-second repaint.
/// </summary>
public sealed class TickerView : Grid, IDisposable
{
    readonly StackPanel _track = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly System.Windows.Threading.DispatcherTimer _rotationTimer = new();
    readonly Queue<TickerItem> _alerts = new();
    // Previous league scores let the ticker notice a score in another game without treating the
    // first feed as a burst of alerts.
    readonly Dictionary<string, (int Home, int Away)> _leagueScores = [];
    ScorebugElement _item;
    string _signature = "";
    string _rotationKey = "";
    string _rotationStyle = "";
    string _rotationTiming = "";
    List<TickerItem> _rotationItems = [];
    TextBlock? _rotationText;
    int _rotationIndex;
    int _transitionGeneration;
    int _scrollGeneration;
    long _seenScoreSequence;
    bool _scoreSequenceSeen;
    bool _loaded;
    bool _disposed;
    bool _rotationActive;
    bool _showingAlert;
    bool _pendingRotationEntrance;
    double _dwellSeconds = 7;
    double _transitionSeconds = 0.4;
    double _period;

    public TickerView(ScorebugElement item, Func<GameState>? state)
    {
        _item = item;
        ClipToBounds = true;
        IsHitTestVisible = false;
        Background = Brushes.Transparent;
        Children.Add(_track);
        _rotationTimer.Tick += (_, _) => AdvanceRotation();
        Update(item, state);
        Loaded += (_, _) =>
        {
            _loaded = true;
            if (_rotationActive)
            {
                if (_pendingRotationEntrance)
                {
                    _pendingRotationEntrance = false;
                    PlayRotationEntrance();
                }
                StartRotationClock();
            }
            else Start();
        };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            StopRotationClock();
            Stop();
        };
    }

    public void Update(ScorebugElement item, GameState? state)
    {
        if (_disposed) return;
        UpdateCore(item, state is null ? null : () => state);
    }

    public void Update(ScorebugElement item, Func<GameState>? state)
    {
        if (_disposed) return;
        UpdateCore(item, state);
    }

    void UpdateCore(ScorebugElement item, Func<GameState>? state)
    {
        if (_disposed) return;
        _item = item;
        var game = state?.Invoke();
        if (item.Ticker && game is not null && TickerRotation.IsBound(item.Content) && game.TickerAutoRotate)
        {
            UpdateRotation(item, game);
            return;
        }
        if (_rotationActive) StopRotation();
        var raw = game is null ? item.Content : ScoreboardTemplates.Bind(item.Content, game, item.PeriodFormat, item.DownCaps, item.PlayClockColon, item.GameClockLeadingZero, item.ClockDecimals);
        raw = System.Text.RegularExpressions.Regex.Replace(raw ?? "", @"\s+", " ").Trim();
        var display = raw.Length == 0 ? " " : raw + "   \u2022   ";
        var foreground = Paint.Text(item, game);
        var color = foreground is SolidColorBrush solid ? solid.Color.ToString() : foreground.ToString();
        var signature = string.Join('|',
            display, item.FontFamily, item.FontSize.ToString("0.###"), item.Bold, item.Italic,
            item.TextOpacity.ToString("0.###"), item.TickerSpeed.ToString("0.###"), item.TickerReverse, color);
        if (string.Equals(signature, _signature, StringComparison.Ordinal)) return;
        _signature = signature;
        Build(display, item, game);
    }

    void UpdateRotation(ScorebugElement item, GameState game)
    {
        _rotationActive = true;
        var style = string.Join('|',
            item.FontFamily, item.FontSize.ToString("0.###"), item.Bold, item.Italic,
            item.TextOpacity.ToString("0.###"), item.Width.ToString("0.###"),
            Paint.TextSignature(item, game));
        if (_rotationText is null || !string.Equals(style, _rotationStyle, StringComparison.Ordinal))
        {
            _rotationStyle = style;
            BuildRotationText(item, game);
        }

        _dwellSeconds = Math.Clamp(game.TickerDwellSeconds, 1, 60);
        _transitionSeconds = Math.Clamp(game.TickerTransitionSeconds, 0, 3);
        var timing = $"{_dwellSeconds:0.###}|{_transitionSeconds:0.###}";
        if (!string.Equals(timing, _rotationTiming, StringComparison.Ordinal))
        {
            _rotationTiming = timing;
            if (_rotationTimer.IsEnabled && !_showingAlert)
                _rotationTimer.Interval = Dwell;
        }

        var items = TickerRotation.Build(game);
        var key = string.Join('|', items.Select(entry => entry.Key));
        var listChanged = !string.Equals(key, _rotationKey, StringComparison.Ordinal);
        _rotationItems = items.ToList();
        _rotationKey = key;
        if (listChanged || _rotationIndex >= _rotationItems.Count) _rotationIndex = 0;
        if (_rotationItems.Count == 0) return;

        DetectScoreAlerts(game);
        if (_showingAlert) return;
        var current = _rotationItems[Math.Clamp(_rotationIndex, 0, _rotationItems.Count - 1)];
        if (_rotationText is not null && !string.Equals(_rotationText.Text, current.Text, StringComparison.Ordinal))
            ShowRotationText(current.Text, animate: _loaded);
        if (_loaded && !_rotationTimer.IsEnabled) StartRotationClock();
    }

    void BuildRotationText(ScorebugElement item, GameState game)
    {
        Stop();
        _transitionGeneration++;
        _track.BeginAnimation(OpacityProperty, null);
        if (_track.RenderTransform is TranslateTransform previous)
            previous.BeginAnimation(TranslateTransform.XProperty, null);
        _rotationTimer.Stop();
        _track.Children.Clear();
        var typeface = new Typeface(FontCatalog.Resolve(item.FontFamily),
            item.Italic ? FontStyles.Italic : FontStyles.Normal,
            item.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        _rotationText = new TextBlock
        {
            Text = " ",
            FontFamily = typeface.FontFamily,
            FontSize = Math.Max(1, item.FontSize),
            FontWeight = typeface.Weight,
            FontStyle = typeface.Style,
            Foreground = Paint.Text(item, game),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.None,
            Opacity = Math.Clamp(item.TextOpacity, 0, 1),
            IsHitTestVisible = false
        };
        _track.Children.Add(_rotationText);
        _track.RenderTransform = new TranslateTransform(0, 0);
        _track.RenderTransformOrigin = new Point(0, 0.5);
        _track.Opacity = 1;
    }

    void DetectScoreAlerts(GameState game)
    {
        if (!_scoreSequenceSeen)
        {
            _seenScoreSequence = game.LastScoreSequence;
            _scoreSequenceSeen = true;
        }
        else if (game.LastScoreSequence != _seenScoreSequence)
        {
            _seenScoreSequence = game.LastScoreSequence;
            var alert = TickerRotation.LocalScoreAlert(game);
            if (alert.Length > 0) _alerts.Enqueue(new TickerItem("alert:local", alert));
        }

        var selected = TickerRotation.SelectedGames(game)
            .Select(entry => entry.GameId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var liveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var league in game.LeagueGames ?? [])
        {
            if (league is null) continue;
            var key = string.IsNullOrWhiteSpace(league.GameId) ? league.Label : league.GameId;
            liveIds.Add(key);
            var score = (league.HomeScore, league.AwayScore);
            if (_leagueScores.TryGetValue(key, out var previous)
                && previous != score
                && game.TickerScoreAlerts
                && selected.Contains(league.GameId))
                _alerts.Enqueue(new TickerItem($"alert:{key}:{league.HomeScore}:{league.AwayScore}", TickerRotation.LeagueScoreAlert(league)));
            _leagueScores[key] = score;
        }
        foreach (var stale in _leagueScores.Keys.Where(key => !liveIds.Contains(key)).ToList())
            _leagueScores.Remove(stale);

        if (_loaded && !_showingAlert && _alerts.Count > 0) StartAlert();
    }

    TimeSpan Dwell => TimeSpan.FromSeconds(_dwellSeconds);

    void StartRotationClock()
    {
        if (_disposed || !_loaded || !_rotationActive) return;
        if (_showingAlert) return;
        if (_alerts.Count > 0)
        {
            StartAlert();
            return;
        }
        if (_rotationItems.Count == 0) return;
        _rotationTimer.Interval = Dwell;
        _rotationTimer.Start();
    }

    void StartAlert()
    {
        if (_alerts.Count == 0 || _rotationText is null) return;
        _showingAlert = true;
        var alert = _alerts.Dequeue();
        _rotationTimer.Stop();
        _rotationTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(Math.Max(4, _dwellSeconds), 4, 60));
        ShowRotationText(alert.Text, animate: true);
        _rotationTimer.Start();
    }

    void AdvanceRotation()
    {
        if (_disposed || !_rotationActive) return;
        if (_showingAlert)
        {
            _showingAlert = false;
            if (_alerts.Count > 0)
            {
                StartAlert();
                return;
            }
            if (_rotationItems.Count > 0)
            {
                _rotationIndex = Math.Clamp(_rotationIndex, 0, _rotationItems.Count - 1);
                ShowRotationText(_rotationItems[_rotationIndex].Text, animate: true);
            }
            _rotationTimer.Interval = Dwell;
            return;
        }
        if (_alerts.Count > 0)
        {
            StartAlert();
            return;
        }
        if (_rotationItems.Count == 0) return;
        if (_rotationItems.Count > 1)
            _rotationIndex = _item.TickerReverse
                ? (_rotationIndex - 1 + _rotationItems.Count) % _rotationItems.Count
                : (_rotationIndex + 1) % _rotationItems.Count;
        ShowRotationText(_rotationItems[_rotationIndex].Text, animate: true);
        _rotationTimer.Interval = Dwell;
    }

    void ShowRotationText(string text, bool animate)
    {
        if (_rotationText is null) return;
        var transition = _transitionSeconds;
        if (!animate || !_loaded || transition <= 0.01)
        {
            _transitionGeneration++;
            _rotationText.Text = text;
            _track.BeginAnimation(OpacityProperty, null);
            if (_track.RenderTransform is TranslateTransform still) { still.BeginAnimation(TranslateTransform.XProperty, null); still.X = 0; }
            _track.Opacity = 1;
            _pendingRotationEntrance = !_loaded && transition > 0.01;
            return;
        }
        var generation = ++_transitionGeneration;
        var half = TimeSpan.FromSeconds(transition / 2);
        var distance = Math.Max(1, ActualWidth > 1 ? ActualWidth : _item.Width);
        // Normal cards leave to the left and the next card comes in from the right. Reverse does
        // the opposite, so the direction is visible instead of the text changing in place.
        var outgoing = _item.TickerReverse ? distance : -distance;
        var incoming = -outgoing;
        var transform = _track.RenderTransform as TranslateTransform;
        if (transform is not null)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 0;
        }
        var fadeOut = new DoubleAnimation(1, 0, half);
        fadeOut.Completed += (_, _) =>
        {
            if (_disposed || generation != _transitionGeneration || _rotationText is null) return;
            _rotationText.Text = text;
            if (transform is not null)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = incoming;
                var slideIn = new DoubleAnimation(incoming, 0, half) { FillBehavior = FillBehavior.Stop };
                slideIn.Completed += (_, _) =>
                {
                    if (_disposed || generation != _transitionGeneration) return;
                    transform.BeginAnimation(TranslateTransform.XProperty, null);
                    transform.X = 0;
                };
                transform.BeginAnimation(TranslateTransform.XProperty, slideIn, HandoffBehavior.SnapshotAndReplace);
            }
            var fadeIn = new DoubleAnimation(0, 1, half);
            _track.BeginAnimation(OpacityProperty, fadeIn);
        };
        if (transform is not null)
        {
            var slideOut = new DoubleAnimation(0, outgoing, half)
            {
                FillBehavior = FillBehavior.Stop
            };
            transform.BeginAnimation(TranslateTransform.XProperty, slideOut, HandoffBehavior.SnapshotAndReplace);
        }
        _track.BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>
    /// The first card is set before the view is loaded, so it has nowhere to animate from yet.
    /// Play its entrance once the view has a width, otherwise the first card simply appears.
    /// </summary>
    void PlayRotationEntrance()
    {
        if (_rotationText is null || _disposed || _transitionSeconds <= 0.01) return;
        var transform = _track.RenderTransform as TranslateTransform;
        if (transform is null) return;
        var generation = ++_transitionGeneration;
        var half = TimeSpan.FromSeconds(_transitionSeconds / 2);
        var distance = Math.Max(1, ActualWidth > 1 ? ActualWidth : _item.Width);
        var start = _item.TickerReverse ? -distance : distance;
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = start;
        _track.BeginAnimation(OpacityProperty, null);
        _track.Opacity = 0;
        var slide = new DoubleAnimation(start, 0, half) { FillBehavior = FillBehavior.Stop };
        slide.Completed += (_, _) =>
        {
            if (_disposed || generation != _transitionGeneration) return;
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 0;
        };
        transform.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);
        _track.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, half));
    }

    void StopRotation()
    {
        _rotationActive = false;
        _showingAlert = false;
        _transitionGeneration++;
        _alerts.Clear();
        _rotationItems.Clear();
        _rotationKey = "";
        _rotationStyle = "";
        _pendingRotationEntrance = false;
        StopRotationClock();
        _track.BeginAnimation(OpacityProperty, null);
        if (_track.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 0;
        }
        _track.Opacity = 1;
    }

    void StopRotationClock() => _rotationTimer.Stop();

    void Build(string display, ScorebugElement item, GameState? game)
    {
        Stop();
        _track.Children.Clear();
        var typeface = new Typeface(FontCatalog.Resolve(item.FontFamily),
            item.Italic ? FontStyles.Italic : FontStyles.Normal,
            item.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        var brush = Paint.Text(item, game);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double width;
        try
        {
            var formatted = new FormattedText(display, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, typeface, Math.Max(1, item.FontSize), brush, dpi)
            {
                MaxTextWidth = 100000,
                Trimming = TextTrimming.None
            };
            width = Math.Max(1, formatted.WidthIncludingTrailingWhitespace);
        }
        catch { width = Math.Max(1, item.Width * 0.8); }
        var gap = Math.Max(20, item.FontSize * 1.5);
        _period = width + gap;
        for (var copy = 0; copy < 2; copy++)
        {
            var block = new TextBlock
            {
                Text = display,
                FontFamily = typeface.FontFamily,
                FontSize = Math.Max(1, item.FontSize),
                FontWeight = typeface.Weight,
                FontStyle = typeface.Style,
                Foreground = brush,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                Opacity = Math.Clamp(item.TextOpacity, 0, 1),
                Margin = new Thickness(0, 0, copy == 0 ? gap : 0, 0),
                IsHitTestVisible = false
            };
            _track.Children.Add(block);
        }
        _track.RenderTransform = new TranslateTransform(0, 0);
        _track.RenderTransformOrigin = new Point(0, 0.5);
        if (_loaded) Start();
    }

    void Start()
    {
        if (_disposed || _period <= 0 || _track.RenderTransform is not TranslateTransform transform) return;
        var speed = Math.Clamp(_item.TickerSpeed, 10, 600);
        var generation = ++_scrollGeneration;
        var reverse = _item.TickerReverse;
        var loopStart = reverse ? -_period : 0;
        var loopEnd = reverse ? 0 : -_period;
        // The first pass starts outside the box so the words visibly enter instead of appearing
        // already on screen. The repeating pass then starts exactly where the entrance ended, so
        // the two copies hand over without a visible seam.
        var viewport = ActualWidth > 1 ? ActualWidth : Math.Max(1, _item.Width);
        var entrance = reverse ? -_period * 2 : Math.Max(_period, viewport);
        if (Math.Abs(entrance - loopStart) < 0.5)
        {
            StartScrollLoop(transform, loopStart, loopEnd, speed, generation);
            return;
        }
        var entranceSeconds = Math.Max(0.25, Math.Abs(loopStart - entrance) / speed);
        var approach = new DoubleAnimation(entrance, loopStart, new Duration(TimeSpan.FromSeconds(entranceSeconds)))
        {
            FillBehavior = FillBehavior.Stop
        };
        approach.Completed += (_, _) => StartScrollLoop(transform, loopStart, loopEnd, speed, generation);
        transform.BeginAnimation(TranslateTransform.XProperty, approach, HandoffBehavior.SnapshotAndReplace);
    }

    void StartScrollLoop(TranslateTransform transform, double from, double to, double speed, int generation)
    {
        if (_disposed || generation != _scrollGeneration) return;
        var seconds = Math.Max(0.25, Math.Abs(to - from) / speed);
        var animation = new DoubleAnimation(from, to, new Duration(TimeSpan.FromSeconds(seconds)))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        transform.BeginAnimation(TranslateTransform.XProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    void Stop()
    {
        _scrollGeneration++;
        if (_track.RenderTransform is TranslateTransform transform)
            transform.BeginAnimation(TranslateTransform.XProperty, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _transitionGeneration++;
        _rotationTimer.Stop();
        _alerts.Clear();
        Stop();
        Children.Clear();
    }
}

/// <summary>
/// A resizeable live feed of another layout. It deliberately hosts the same canvas view the studio
/// and the audience use, so game values, media and value changes keep moving inside the box instead
/// of becoming a picture. The feed owns that view and disposes it when it is removed.
/// </summary>
public sealed class ScorebugLayoutFeed : Grid, IDisposable
{
    readonly ScorebugCatalog _catalog;
    readonly GameEngine _engine;
    readonly Func<GameState>? _state;
    readonly MediaPlayback? _playback;
    readonly bool _muteVideo;
    readonly string? _baseDisplayId;
    readonly IReadOnlySet<string> _chain;
    readonly bool _onAirVideo;
    ScorebugElement _item;
    ScorebugCanvasView? _view;
    string? _viewLayoutId;
    bool _disposed;

    public ScorebugLayoutFeed(ScorebugCatalog catalog, GameEngine engine, Func<GameState>? state,
        MediaPlayback? playback, bool muteVideo, ScorebugElement item, string? displayId,
        IReadOnlySet<string>? layoutChain, bool onAirVideo)
    {
        _catalog = catalog;
        _engine = engine;
        _state = state;
        _playback = playback;
        _muteVideo = muteVideo;
        _baseDisplayId = displayId;
        _chain = layoutChain is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(layoutChain, StringComparer.OrdinalIgnoreCase);
        _onAirVideo = onAirVideo;
        _item = item;
        IsHitTestVisible = false;
        Update(item);
    }

    /// <summary>The layout this feed is drawing, so the studio can verify and serialise the choice.</summary>
    public string? SourceLayoutId => _item.NestedLayoutId;

    public void Update(ScorebugElement item)
    {
        if(_disposed)return;
        _item = item;
        var target = _catalog.FindLayout(item.NestedLayoutId);
        if (target is null)
        {
            ShowPlaceholder(string.IsNullOrWhiteSpace(item.NestedLayoutId) ? "LIVE LAYOUT" : "LAYOUT NOT FOUND");
            return;
        }
        if (_chain.Contains(target.Id))
        {
            ShowPlaceholder("LAYOUT LOOP");
            return;
        }
        if (_view is null || !string.Equals(_viewLayoutId, target.Id, StringComparison.OrdinalIgnoreCase))
        {
            ShowView(target);
        }
        _view!.SetFill(!item.NestedPreserveAspect);
        ToolTip = $"Live layout: {target.Name} · {target.Width} × {target.Height}";
    }

    void ShowView(ScorebugLayout source)
    {
        DisposeView();
        Children.Clear();
        var chain = new HashSet<string>(_chain, StringComparer.OrdinalIgnoreCase) { source.Id };
        var display = _catalog.FindDisplayOfLayout(source.Id);
        _view = new ScorebugCanvasView(_catalog, _engine, _state, _playback, _muteVideo,
            layoutProvider: () => _catalog.FindLayout(source.Id) ?? source,
            onAirVideo: _onAirVideo,
            displayId: display?.Id ?? _baseDisplayId,
            layoutChain: chain,
            liveValues: true,
            fillBox: !_item.NestedPreserveAspect);
        _viewLayoutId = source.Id;
        Children.Add(_view);
    }

    void ShowPlaceholder(string text)
    {
        DisposeView();
        if (Children.Count == 1 && Children[0] is Border existing && existing.Child is TextBlock label && label.Text == text)
            return;
        Children.Clear();
        Children.Add(ScorebugRender.LayoutPlaceholder(text));
        ToolTip = string.IsNullOrWhiteSpace(_item.NestedLayoutId)
            ? "Choose a source layout in the properties panel"
            : "The live layout source is missing or would loop back into this layout";
    }

    void DisposeView()
    {
        if (_view is null) return;
        _view.Dispose();
        _view = null;
        _viewLayoutId = null;
    }

    public void Dispose()
    {
        if(_disposed)return;
        _disposed=true;
        DisposeView();
        Children.Clear();
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
    // These washes sit over the crest and use the crest's own alpha as their opacity mask, so a
    // material slider changes the artwork itself instead of disappearing behind an opaque logo.
    readonly Rectangle _textFinish = new() { IsHitTestVisible = false, Stretch = Stretch.Fill };
    readonly Rectangle _plateFinish = new() { IsHitTestVisible = false, Stretch = Stretch.Fill };
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
        Children.Add(_textFinish);
        Children.Add(_plateFinish);
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
        // A material on either side of the Paint & Texture window follows the logo's silhouette.
        // Keep the fallback colour neutral: a transparent logo has no plate colour of its own.
        ApplyFinish(_textFinish, found ? Paint.TextureWash(item.TextTexture, Color.FromRgb(0xC8, 0xC8, 0xC8)) : null, found, item.TextTexture);
        ApplyFinish(_plateFinish, found ? Paint.TextureWash(item.BackgroundTexture, Color.FromRgb(0xC8, 0xC8, 0xC8)) : null, found, item.BackgroundTexture);
        if (_placeholder is null) return;
        _placeholder.Text = item.LogoSide == ScorebugLogoSide.None ? item.Content : $"{item.LogoSide.ToString().ToUpperInvariant()} LOGO";
        _placeholder.FontFamily = FontCatalog.Resolve(item.FontFamily);
        _placeholder.FontSize = item.FontSize;
        _placeholder.FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal;
        _placeholder.FontStyle = item.Italic ? FontStyles.Italic : FontStyles.Normal;
        _placeholder.Foreground = Paint.Text(item, state);
        _placeholder.Opacity = Math.Clamp(item.TextOpacity, 0, 1);
        // A painted or textured logo plate is the artwork the operator made; the studio's
        // "HOME LOGO" label would cover it, so the plate takes the placeholder's place. With no
        // team logo and no plate paint the label still identifies the empty slot.
        _placeholder.Visibility = found || ScorebugRender.HasPlatePaint(item) ? Visibility.Collapsed : Visibility.Visible;
    }

    void ApplyFinish(Rectangle finish, Brush? brush, bool hasImage, ScorebugTexture? texture)
    {
        finish.Fill = brush;
        finish.Visibility = brush is null || !hasImage ? Visibility.Collapsed : Visibility.Visible;
        finish.Opacity = texture is { Enabled: true }
            ? Math.Clamp(0.35 + texture.Opacity * 0.5, 0.25, 0.85)
            : 1;
        finish.OpacityMask = hasImage && _image.Source is not null
            ? new ImageBrush(_image.Source) { Stretch = Stretch.Uniform }
            : null;
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
    readonly TransformGroup _rest = new();
    DispatcherTimer? _countTimer;
    // JumpFlip owns a fallback timer as well: a hosted animation can be stopped before its
    // Completed event runs, and the text must never stay locked in the animating state.
    DispatcherTimer? _finishTimer;
    string _shown = "";
    bool _animating;
    // A value that arrives while a crossing is running waits for that crossing to finish, then
    // crosses from there. Replacing the running one made the printed value jump mid-animation.
    string? _queuedText;
    ScorebugTransition _queuedTransition = ScorebugTransition.Cut;
    double _queuedSeconds = 0.2;
    // A transition is two phases with callbacks; a newer value (or a snap) bumps the generation,
    // so a stale out-phase can never put its old text back on the graphic.
    int _generation;
    ScorebugElement? _style;

    /// <summary>The live game, used to choose a score-triggered take for a value animation.</summary>
    public Func<GameState>? StateProvider { get; set; }

    /// <summary>Lets the canvas show the clip's temporary pieces while this value changes.</summary>
    public Action<MotionClip, string, string>? ValueMotionPlayed { get; set; }

    public ScorebugTextBlock(ScorebugElement item)
    {
        _style = item;
        TextAlignment = TextAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Center;
        TextWrapping = TextWrapping.Wrap;
        RenderTransformOrigin = new Point(0.5, 0.5);
        _rest.Children.Add(_scale);
        _rest.Children.Add(_rotate);
        _rest.Children.Add(_translate);
        RenderTransform = _rest;
        ApplyStyle(item, null);
    }

    public void ApplyStyle(ScorebugElement item, GameState? state)
    {
        FontFamily = FontCatalog.Resolve(item.FontFamily);
        FontSize = item.FontSize;
        FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal;
        FontStyle = item.Italic ? FontStyles.Italic : FontStyles.Normal;
        // A gradient or a textured paint fills the letters themselves.
        Foreground = Paint.Text(item, state);
        Opacity = Math.Clamp(item.TextOpacity, 0, 1);
        var decorations = new TextDecorationCollection();
        if (item.Underline) decorations.Add(System.Windows.TextDecorations.Underline[0]);
        if (item.StrikeThrough) decorations.Add(System.Windows.TextDecorations.Strikethrough[0]);
        TextDecorations = decorations.Count == 0 ? null : decorations;
        // Text turns its own glyphs; the host only positions the box. This also lets standalone
        // text (the studio preview, a paint sample and a plain canvas) rotate without a host.
        _rotate.Angle = double.IsFinite(item.Rotation) ? item.Rotation : 0;
        // Left and right sit against the box's own edges, which needs the block to fill it; the
        // centred default keeps the block sized to its text exactly as before.
        TextAlignment = item.TextAlign switch
        {
            ScorebugTextAlign.Left => System.Windows.TextAlignment.Left,
            ScorebugTextAlign.Right => System.Windows.TextAlignment.Right,
            _ => System.Windows.TextAlignment.Center
        };
        HorizontalAlignment = item.TextAlign == ScorebugTextAlign.Center
            ? System.Windows.HorizontalAlignment.Center
            : System.Windows.HorizontalAlignment.Stretch;
        // A drop shadow lifts lettering off a photograph: a soft one reads as a glow, a hard one as
        // the broadcast drop shadow over a crowd shot.
        if (item.TextShadow)
        {
            var shadowBrush = ScorebugRender.ParseBrush(item.TextShadowColor);
            var shadowColor = shadowBrush is SolidColorBrush solid ? solid.Color : Colors.Black;
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = shadowColor,
                ShadowDepth = Math.Clamp(item.TextShadowOffset, 0, 40),
                // WPF measures the direction anticlockwise from the right; the setting is the
                // familiar clockwise bearing (0 right, 90 down), so 45 keeps the old down-right drop.
                Direction = (360 - Math.Clamp(double.IsFinite(item.TextShadowAngle) ? item.TextShadowAngle : 45, 0, 360)) % 360,
                BlurRadius = Math.Clamp(item.TextShadowSoftness, 0, 60),
                Opacity = shadowColor.A / 255.0
            };
        }
        else Effect = null;
        _style = item;
    }

    /// <summary>Shows a new value, crossing to it with the element's chosen animation.</summary>
    /// <param name="seconds">How long the whole crossing takes: 0 snaps, 5 is a slow dissolve.</param>
    public void UpdateText(string text, ScorebugTransition transition, double seconds = 0.2)
    {
        text ??= "";
        var total = Math.Clamp(double.IsFinite(seconds) ? seconds : 0.2, 0, 5);
        // A value that falls can cross differently from one that rises: a score ticking down can
        // drop away while a score ticking up rises in. Plain numeric values only, so a name change
        // is never mistaken for a fall.
        if (_style?.TransitionDown is { } down
            && double.TryParse(_shown.Trim(), out var before)
            && double.TryParse(text.Trim(), out var after)
            && after < before)
        {
            transition = down;
            total = Math.Clamp(_style.TransitionDownSeconds ?? total, 0, 5);
        }
        // The target is already recorded when a transition starts. A refresh that prints the same
        // value must leave that transition alone, and it also cancels a value queued behind it:
        // the latest state is the value already crossing in, not an older detour.
        if (_shown == text) { _queuedText = null; return; }
        // A different value that arrives mid-crossing waits its turn. A cut still snaps, because
        // a cut is supposed to land immediately.
        if (_animating && transition != ScorebugTransition.Cut && total >= 0.02)
        {
            _queuedText = text;
            _queuedTransition = transition;
            _queuedSeconds = total;
            return;
        }
        // A hand-over built in the transition maker wins over the preset: the old value leaves on
        // the clip's exit keys and the new one arrives on its enter keys, colour keys and all.
        if (_style?.MotionTransition is { } clip && IsLoaded && clip.Phases.Any(phase => phase.Enabled && !phase.IsEmpty))
        {
            PlayMotionTransition(text, clip);
            return;
        }
        var valueMotion = _style?.MotionAnimation is { UseOnValueChange: true, IsEmpty: false } motion ? motion : null;
        var beforeValue = _shown;
        if (transition == ScorebugTransition.Cut || total < 0.02 || !IsLoaded)
        {
            Snap(text);
            PlayValueMotion(valueMotion, beforeValue, text);
            return;
        }
        // The two special presets need more than an out/in pair: one counts through the values while
        // it is small, the other flips through a full turn while it is lifted. Both are numeric
        // values, so a text change that is not a number still falls back to the plain crossing.
        if (transition == ScorebugTransition.ShrinkWheel && TryPlayShrinkWheel(text, beforeValue, total, valueMotion)) return;
        if (transition == ScorebugTransition.JumpFlip && TryPlayJumpFlip(text, beforeValue, total, valueMotion)) return;
        // A clock is a value with stable character positions. Wheeling the whole block makes every
        // digit move when only one second changed, so a clock rolls only its changed characters.
        if (transition is ScorebugTransition.WheelUp or ScorebugTransition.WheelDown
            && IsClockValue && TryPlayClockWheel(text, transition, total, valueMotion, beforeValue)) return;
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
                if (generation != _generation) return;
                _animating = false;
                // A value that arrived while the crossing ran starts its own crossing now, from
                // the value that just landed. Nothing snaps into place between the two.
                if (_queuedText is not null) { PlayQueued(); return; }
                PlayValueMotion(valueMotion, beforeValue, text);
            });
        });
    }

    void PlayValueMotion(MotionClip? clip, string before, string after)
    {
        if (clip is null) return;
        var state = StateProvider?.Invoke();
        var phase = clip.ValuePhaseFor(state);
        ValueMotionPlayed?.Invoke(clip, before, after);
        MotionPlayback.PlayClip(this, clip, Opacity, ambient: false, phase: phase);
    }

    void PlayQueued()
    {
        if (_queuedText is not { } next) return;
        var transition = _queuedTransition;
        var seconds = _queuedSeconds;
        _queuedText = null;
        if (string.Equals(next, Text, StringComparison.Ordinal)) return;
        UpdateText(next, transition, seconds);
    }

    void Snap(string text)
    {
        _generation++;
        _queuedText = null;
        ClearPhase();
        Text = text;
        _shown = text;
        _animating = false;
    }

    /// <summary>
    /// Prints a new clock digit at once. The decimal column is refreshed many times a second, and a
    /// value crossing for every tenth would keep the digits animating and read as lag.
    /// </summary>
    public void UpdateClockText(string text)
    {
        text ??= "";
        if (string.Equals(_shown, text, StringComparison.Ordinal) && string.Equals(Text, text, StringComparison.Ordinal)) return;
        Snap(text);
    }

    /// <summary>
    /// Lands the newest value immediately. A score celebration animates the item host and can
    /// cancel the value's in/out animations before their completion callbacks run; without this
    /// the block would stay marked as animating and queue every later score forever.
    /// </summary>
    internal void Settle()
    {
        var target = _queuedText ?? _shown;
        _generation++;
        _countTimer?.Stop();
        _countTimer = null;
        _finishTimer?.Stop();
        _finishTimer = null;
        ClearPhase();
        Text = target;
        _shown = target;
        _queuedText = null;
        _animating = false;
    }

    /// <summary>True when this element prints a game or play clock rather than a plain score.</summary>
    bool IsClockValue => _style?.Content?.Contains("{{GAME_CLOCK}}", StringComparison.OrdinalIgnoreCase) == true
        || _style?.Content?.Contains("{{PLAY_CLOCK}}", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Wheels only the characters that changed on a clock, instead of sliding the whole value. A
    /// 13:24 to 13:23 change rolls the final 4 into a 3; a 10 second or whole minute change rolls
    /// exactly the digits that differ. The old run leaves, the text is swapped, and the new run
    /// arrives from the other side, all inside one TextBlock.
    /// </summary>
    bool TryPlayClockWheel(string text, ScorebugTransition transition, double total, MotionClip? valueMotion, string beforeValue)
    {
        if (!IsLoaded || total < 0.02 || string.Equals(_shown, text, StringComparison.Ordinal)) return false;
        var oldValue = _shown;
        if (oldValue.Length == 0 || text.Length == 0) return false;
        _animating = true;
        _shown = text;
        var generation = _generation;
        ClearPhase();
        WheelText(oldValue, text, transition == ScorebugTransition.WheelUp,
            TimeSpan.FromSeconds(total), () => FinishCrossing(generation, valueMotion, beforeValue, text));
        return true;
    }

    /// <summary>
    /// Rolls only the characters that differ, from the old value to the new one, using the same
    /// eased motion as WheelUp. Keeping the unchanged prefix and suffix still means the drum reads
    /// as one movement instead of the whole block jittering.
    /// </summary>
    void WheelText(string oldValue, string nextValue, bool up, TimeSpan duration, Action done)
    {
        var generation = _generation;
        if (string.Equals(oldValue, nextValue, StringComparison.Ordinal))
        {
            Text = nextValue;
            done();
            return;
        }
        // A roll moves a glyph half a line out of its own box. Without clipping, the digit that is
        // leaving is still visible when the text swaps, so a slow roll reads as a blink; clipped to
        // the line box it is a real drum, and slow steps stay smooth.
        ClipToBounds = true;
        var prefix = 0;
        while (prefix < oldValue.Length && prefix < nextValue.Length && oldValue[prefix] == nextValue[prefix]) prefix++;
        var suffix = 0;
        while (suffix < oldValue.Length - prefix && suffix < nextValue.Length - prefix
            && oldValue[oldValue.Length - 1 - suffix] == nextValue[nextValue.Length - 1 - suffix]) suffix++;
        var oldCount = oldValue.Length - prefix - suffix;
        var total = Math.Max(0.01, duration.TotalSeconds);
        var outDuration = TimeSpan.FromSeconds(total * 0.45);
        var inDuration = TimeSpan.FromSeconds(Math.Max(0.01, total * 0.55));
        var distance = Math.Max(6, FontSize * 0.5);
        // The two halves of a roll are one movement: the digit that leaves picks up speed and the one
        // that arrives sets straight out at that speed, so a slow roll never stops halfway and
        // restarts. An ease-in-out on both halves put a standstill at every swap.
        var outEase = new CubicEase { EasingMode = EasingMode.EaseIn };
        var inEase = new CubicEase { EasingMode = EasingMode.EaseOut };

        void Arrive()
        {
            if (generation != _generation) return;
            Text = nextValue;
            TextEffects.Clear();
            var newPrefix = 0;
            while (newPrefix < oldValue.Length && newPrefix < nextValue.Length && oldValue[newPrefix] == nextValue[newPrefix]) newPrefix++;
            var newSuffix = 0;
            while (newSuffix < oldValue.Length - newPrefix && newSuffix < nextValue.Length - newPrefix
                && oldValue[oldValue.Length - 1 - newSuffix] == nextValue[nextValue.Length - 1 - newSuffix]) newSuffix++;
            var newCount = nextValue.Length - newPrefix - newSuffix;
            if (newCount <= 0)
            {
                ClipToBounds = false;
                done();
                return;
            }
            var inShift = new TranslateTransform(0, up ? distance : -distance);
            TextEffects.Add(new TextEffect
            {
                PositionStart = newPrefix,
                PositionCount = newCount,
                Transform = inShift
            });
            var inRoll = new DoubleAnimation(up ? distance : -distance, 0, inDuration)
            {
                EasingFunction = inEase,
                FillBehavior = FillBehavior.HoldEnd
            };
            inRoll.Completed += (_, _) =>
            {
                if (generation != _generation) return;
                TextEffects.Clear();
                ClipToBounds = false;
                done();
            };
            inShift.BeginAnimation(TranslateTransform.YProperty, inRoll);
        }

        TextEffects.Clear();
        if (oldCount <= 0)
        {
            Arrive();
            return;
        }
        var outShift = new TranslateTransform();
        TextEffects.Add(new TextEffect
        {
            PositionStart = prefix,
            PositionCount = oldCount,
            Transform = outShift
        });
        var outRoll = new DoubleAnimation(0, up ? -distance : distance, outDuration)
        {
            EasingFunction = outEase,
            FillBehavior = FillBehavior.HoldEnd
        };
        outRoll.Completed += (_, _) => Arrive();
        outShift.BeginAnimation(TranslateTransform.YProperty, outRoll);
    }

    /// <summary>Shrinks, wheels the old value through every integer like WheelUp, then grows back.
    /// Each step is a real glyph roll, so the count reads as a drum rather than a text swap.</summary>
    bool TryPlayShrinkWheel(string text, string beforeValue, double total, MotionClip? valueMotion)
    {
        if (!IsLoaded || total < 0.02 || !int.TryParse(beforeValue.Trim(), out var before)
            || !int.TryParse(text.Trim(), out var after) || before == after) return false;
        _animating = true;
        _shown = text;
        var generation = _generation;
        ClearPhase();
        var shrink = Math.Clamp(_style?.TransitionShrink ?? 0.82, 0.2, 1);
        var shrinkTime = TimeSpan.FromSeconds(total * 0.28);
        var countSeconds = Math.Max(0.01, total * 0.44);
        var growTime = TimeSpan.FromSeconds(Math.Max(0.01, total * 0.28));
        var grow = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var shrinkEase = new CubicEase { EasingMode = EasingMode.EaseInOut };
        void GrowBack()
        {
            if (generation != _generation) return;
            TextEffects.Clear();
            Text = text;
            // The shrink animation is still holding the block small. Hand that value to the base
            // property before the grow starts, or the first frame paints at full size and the block
            // pops back up for one frame — the worst artefact on a slow transition.
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _scale.ScaleX = _scale.ScaleY = shrink;
            var growAnim = new DoubleAnimation(shrink, 1, growTime) { EasingFunction = grow, FillBehavior = FillBehavior.HoldEnd };
            growAnim.Completed += (_, _) => FinishCrossing(generation, valueMotion, beforeValue, text);
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, growAnim);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(shrink, 1, growTime) { EasingFunction = grow, FillBehavior = FillBehavior.HoldEnd });
        }
        var shrinkAnim = new DoubleAnimation(1, shrink, shrinkTime) { EasingFunction = shrinkEase, FillBehavior = FillBehavior.HoldEnd };
        shrinkAnim.Completed += (_, _) =>
        {
            if (generation != _generation) return;
            var steps = Math.Abs(after - before);
            if (steps == 0 || countSeconds < 0.01)
            {
                Text = text;
                GrowBack();
                return;
            }
            // Count every integer through the same glyph roll WheelUp uses. A timer that only
            // changed Text made the value jump from one number to the next; rolling each step
            // keeps the small, wheeled digits moving while the block is shrunk. The per-step time
            // comes from the element's transition time, so a longer transition really is slower.
            var direction = Math.Sign(after - before);
            // A wheel can only draw as many numbers as it has frames. Stepping every integer of a
            // long change squeezed a dozen glyph rolls into one frame, which read as a chop; step
            // by whatever fits in ~50 ms instead, so each roll is actually seen on screen.
            const double minRoll = 0.05;
            var maxSteps = Math.Max(1, (int)Math.Floor(countSeconds / minRoll));
            var stepSize = Math.Max(1, (int)Math.Ceiling(steps / (double)maxSteps));
            var stepCount = Math.Max(1, (int)Math.Ceiling(steps / (double)stepSize));
            var stepDuration = TimeSpan.FromSeconds(Math.Max(minRoll, countSeconds / stepCount));
            var current = before;
            void WheelStep()
            {
                if (generation != _generation) return;
                if (current == after)
                {
                    GrowBack();
                    return;
                }
                var next = direction > 0 ? Math.Min(after, current + stepSize) : Math.Max(after, current - stepSize);
                WheelText(current.ToString(), next.ToString(), direction > 0, stepDuration, () =>
                {
                    if (generation != _generation) return;
                    current = next;
                    WheelStep();
                });
            }
            WheelStep();
        };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrinkAnim);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, shrink, shrinkTime)
        {
            EasingFunction = shrinkEase,
            FillBehavior = FillBehavior.HoldEnd
        });
        return true;
    }

    /// <summary>Lifts, turns through a full 360° on the chosen axis while the value changes at
    /// the far side, then drops back.</summary>
    bool TryPlayJumpFlip(string text, string beforeValue, double total, MotionClip? valueMotion)
    {
        if (!IsLoaded || total < 0.02 || string.Equals(_shown, text, StringComparison.Ordinal)) return false;
        _animating = true;
        _shown = text;
        var generation = _generation;
        ClearPhase();
        var jump = Math.Clamp(_style?.TransitionJumpHeight ?? 40, 0, 300);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var animationDuration = TimeSpan.FromSeconds(total);
        var lift = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd, Duration = animationDuration };
        lift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        lift.KeyFrames.Add(new EasingDoubleKeyFrame(-jump, KeyTime.FromPercent(0.30), new CubicEase { EasingMode = EasingMode.EaseOut }));
        lift.KeyFrames.Add(new EasingDoubleKeyFrame(-jump, KeyTime.FromPercent(0.75)));
        lift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseIn }));
        _translate.Y = 0;
        _translate.BeginAnimation(TranslateTransform.YProperty, lift);
        // A side-to-side turn is a flip across X; a top-to-bottom turn is a flip across Y. The
        // value is edge-on at the 180° point, which is where the text is swapped.
        var direction = _style?.TransitionJumpDirection ?? ScorebugJumpDirection.LeftToRight;
        var horizontal = direction is ScorebugJumpDirection.LeftToRight or ScorebugJumpDirection.RightToLeft;
        var flip = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd, Duration = animationDuration };
        flip.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        flip.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.12)));
        flip.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.30), ease));
        flip.KeyFrames.Add(new EasingDoubleKeyFrame(-1, KeyTime.FromPercent(0.46), ease));
        flip.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.62), ease));
        flip.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.78), ease));
        flip.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1)));
        // The travel skew makes the two directions read differently while the card is edge-on.
        var travel = direction is ScorebugJumpDirection.RightToLeft or ScorebugJumpDirection.DownToUp ? -1.0 : 1.0;
        var drift = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd, Duration = animationDuration };
        drift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        drift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.30)));
        drift.KeyFrames.Add(new EasingDoubleKeyFrame(8 * travel, KeyTime.FromPercent(0.46), ease));
        drift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.62), ease));
        drift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        // The text changes at the back of the turn, when it is edge-on and the old value is hidden.
        var swapAt = Math.Max(0.01, total * 0.45);
        _countTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(swapAt) };
        _countTimer.Tick += (_, _) =>
        {
            if (_countTimer is null) return;
            _countTimer.Stop();
            _countTimer = null;
            if (generation == _generation) Text = text;
        };
        _countTimer.Start();
        // Attach the completion before starting the animation. A completed callback must never
        // be missed because the clock started first, and a fallback timer settles the value if
        // another host animation replaces this one before WPF reports its completion.
        flip.Completed += (_, _) => FinishCrossing(generation, valueMotion, beforeValue, text);
        var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(total + 0.25) };
        _finishTimer = guard;
        guard.Tick += (_, _) =>
        {
            guard.Stop();
            if (ReferenceEquals(_finishTimer, guard)) _finishTimer = null;
            FinishCrossing(generation, valueMotion, beforeValue, text);
        };
        guard.Start();
        if (horizontal) _scale.BeginAnimation(ScaleTransform.ScaleXProperty, flip);
        else _scale.BeginAnimation(ScaleTransform.ScaleYProperty, flip);
        // The lift owns Y, so the directional drift always rides X.
        _translate.BeginAnimation(TranslateTransform.XProperty, drift);
        return true;
    }

    void FinishCrossing(int generation, MotionClip? valueMotion, string before, string after)
    {
        if (generation != _generation || !_animating) return;
        // The completion can arrive before the mid-point swap on a clipped or unusually short
        // timeline; always land the new value rather than leaving the old one on screen.
        Text = after;
        _countTimer?.Stop();
        _countTimer = null;
        ClearPhase();
        _animating = false;
        if (_queuedText is not null) { PlayQueued(); return; }
        PlayValueMotion(valueMotion, before, after);
    }

    /// <summary>
    /// The value leaves on the clip's exit leg and arrives on its enter leg. The generation guard
    /// means a value that changes again mid-flight wins, and a stale leg never restores old text.
    /// </summary>
    void PlayMotionTransition(string text, MotionClip clip)
    {
        _generation++;
        _queuedText = null;
        var generation = _generation;
        ClearPhase();
        _animating = true;
        _shown = text;
        var level = Math.Clamp(Opacity, 0, 1);
        var exit = clip.ExitPhase;
        var enter = clip.EnterPhase;
        void Arrive()
        {
            if (generation != _generation) return;
            Text = text;
            if (enter is null || enter.IsEmpty)
            {
                ClearPhase();
                _animating = false;
                if (_queuedText is not null) PlayQueued();
                return;
            }
            MotionPlayback.PlayPhase(this, enter, level, () =>
            {
                if (generation != _generation) return;
                ClearPhase();
                _animating = false;
                if (_queuedText is not null) PlayQueued();
            });
        }
        if (exit is not null && !exit.IsEmpty) MotionPlayback.PlayPhase(this, exit, level, Arrive);
        else Arrive();
    }

    /// <summary>
    /// Ends a phase with the block at rest. Every animation holds its final value, then this clears
    /// it and resets the transforms, so a wheel or a zoom can never leave the new value offset or
    /// shrunken after the animation has finished.
    /// </summary>
    void ClearPhase()
    {
        _countTimer?.Stop();
        _countTimer = null;
        _finishTimer?.Stop();
        _finishTimer = null;
        TextEffects.Clear();
        ClipToBounds = false;
        MotionPlayback.Stop(this);
        BeginAnimation(OpacityProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _translate.BeginAnimation(TranslateTransform.XProperty, null);
        _translate.BeginAnimation(TranslateTransform.YProperty, null);
        // The motion player replaces the render transform while it runs; the design group goes back
        // afterwards, so rotation and the value-change offsets keep working.
        RenderTransform = _rest;
        Opacity = Math.Clamp(_style?.TextOpacity ?? 1, 0, 1);
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
            case ScorebugTransition.ShrinkWheel:
                Animate(1, 1, duration, done, scaleFrom: 1, scaleTo: Math.Clamp(_style?.TransitionShrink ?? 0.82, 0.2, 1));
                break;
            case ScorebugTransition.JumpFlip:
                Animate(1, 1, duration, done, yFrom: 0, yTo: -Math.Clamp(_style?.TransitionJumpHeight ?? 40, 0, 300), scaleYFrom: 1, scaleYTo: -1);
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
            case ScorebugTransition.ShrinkWheel:
                Animate(1, 1, duration, done, scaleFrom: Math.Clamp(_style?.TransitionShrink ?? 0.82, 0.2, 1), scaleTo: 1);
                break;
            case ScorebugTransition.JumpFlip:
                Animate(1, 1, duration, done, yFrom: -Math.Clamp(_style?.TransitionJumpHeight ?? 40, 0, 300), yTo: 0, scaleYFrom: -1, scaleYTo: 1);
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
    // The icon's own chrome sits behind its artwork, inside the element's plate and outline. Keeping
    // it separate is what lets an arrow or a diamond have a fill and outline of its own without
    // changing the box around the printed value.
    readonly Border _chrome = new() { IsHitTestVisible = false };
    readonly Grid _art = new();
    RotateTransform? _artTurn;
    // A mark that hugs an edge of its box turns on its own centre; turning the whole box would
    // swing it out of the element and leave an empty plate.
    readonly RotateTransform _arrowTurn = new();
    readonly TranslateTransform _arrowSlide = new();
    readonly TransformGroup _arrowTransforms = new();
    RotateTransform? _dotTurn;
    readonly Func<GameState>? _state;
    readonly Ellipse? _dot;
    readonly Polygon? _arrow;
    Polygon? _flagCloth;
    Rectangle? _flagPole;
    readonly StackPanel? _bars;
    readonly Canvas? _bases;
    readonly List<Rectangle> _baseSquares = [];
    readonly List<ScaleTransform> _baseScales = [];
    readonly bool[] _baseLit = new bool[3];
    Brush _baseBrush = Brushes.White;
    Brush _baseFill = Brushes.White;
    Brush _baseFaint = Brushes.White;
    string _appliedForeground = "";
    string _baseSignature = "";
    // Which side the dot was last drawn on, so a possession change can animate from the old side.
    bool? _dotRight;
    // Whether the arrow was showing last time it was refreshed, so its arrival and departure animate.
    bool? _arrowShown;
    // A referee's yellow or red card, drawn as the angled rectangle the official holds up.
    Rectangle? _card;
    // An icon made in the Icon Creator: a glyph on its plate.
    Border? _customHost;
    TextBlock? _customText;
    Image? _customImage;
    string _customImagePath = "";

    /// <summary>
    /// Which live artwork this view was built for, so the studio can tell one icon from another.
    /// It is captured at construction rather than read from the element: the element is edited in
    /// place, so reading it would let a view that draws bars claim it is already a dot.
    /// </summary>
    public ScorebugIcon Icon { get; }

    public ScorebugIconView(ScorebugElement item, Func<GameState>? state, string? text = null)
    {
        _item = item;
        _state = state;
        Icon = item.Icon;
        Children.Add(_chrome);
        Children.Add(_art);
        // Bars and bases are artwork that must stay inside its plate; the dot is a small mark that
        // is allowed to breathe over the edge rather than arrive chopped in half.
        ClipToBounds = Icon is not (ScorebugIcon.PossessionDot or ScorebugIcon.PossessionArrow);
        if (Icon == ScorebugIcon.Bases)
        {
            // Three bases in a diamond: a base a runner is standing on is filled, an empty one is
            // only a faint outline, so the graphic reads at a glance like the field does.
            _bases = new Canvas { ClipToBounds = true };
            _art.Children.Add(_bases);
        }
        else if (Icon == ScorebugIcon.PossessionArrow)
        {
            // A solid triangle that leans into the plate of the team with the ball. Movement and
            // sizing happen in Update, so the mark follows possession without being rebuilt.
            _arrow = new Polygon
            {
                Fill = ScorebugRender.ParseBrush(ScorebugRender.ResolveForeground(item, state?.Invoke())),
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Fill,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            _arrowTransforms.Children.Add(_arrowTurn);
            _arrowTransforms.Children.Add(_arrowSlide);
            _arrow.RenderTransform = _arrowTransforms;
            _art.Children.Add(_arrow);
        }
        else if (Icon == ScorebugIcon.PenaltyFlag)
        {
            // A referee's flag: a slim pole with a yellow pennant at the top.
            _flagPole = new Rectangle
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = ScorebugRender.ParseBrush(ScorebugRender.ResolveForeground(item, state?.Invoke()))
            };
            _flagCloth = new Polygon
            {
                Points = new PointCollection { new Point(0, 0), new Point(1, 0.5), new Point(0, 1) },
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            _art.Children.Add(_flagPole);
            _art.Children.Add(_flagCloth);
        }
        else if (Icon is ScorebugIcon.YellowCard or ScorebugIcon.RedCard)
        {
            // A card is its own colour, not the plate's: a yellow card has to read yellow from the
            // far side of the arena. It is drawn slightly taller than wide, like the real thing.
            _card = new Rectangle
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(-8),
                Fill = new SolidColorBrush(Icon == ScorebugIcon.RedCard ? Color.FromRgb(0xE2, 0x3B, 0x3B) : Color.FromRgb(0xF2, 0xC2, 0x30))
            };
            _art.Children.Add(_card);
        }
        else if (Icon is ScorebugIcon.TimeoutBars or ScorebugIcon.TimeoutDots)
        {
            // One short horizontal line per timeout, in a row from left to right.
            _bars = new StackPanel
            {
                Orientation = item.IconVertical && Icon == ScorebugIcon.TimeoutBars
                    ? Orientation.Vertical : Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _art.Children.Add(_bars);
        }
        else if (Icon == ScorebugIcon.Custom)
        {
            // The maker's icon draws a glyph on its own plate, exactly as it looked while it was made.
            _customText = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                FontWeight = FontWeights.Bold
            };
            _customImage = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            var customContent = new Grid();
            customContent.Children.Add(_customText);
            customContent.Children.Add(_customImage);
            _customHost = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = customContent
            };
            _art.Children.Add(_customHost);
        }
        else
        {
            // The dot is the whole icon: it marks the side with the ball, on the side that has it.
            // Printing the value beside it read as a second, stale possession next to the mark.
            _dot = new Ellipse { Fill = ScorebugRender.ParseBrush(ScorebugRender.ResolveForeground(item, state?.Invoke())), VerticalAlignment = VerticalAlignment.Center };
            _art.Children.Add(_dot);
        }
        Update(text);
    }

    /// <summary>Draws a made icon: its glyph, colour and plate, sized to the element's own box.</summary>
    void UpdateCustom()
    {
        if (_customText is null || _customHost is null) return;
        var icon = Scoreboard.Core.CustomIconStore.Find(_item.CustomIconId);
        if (icon is null)
        {
            _customText.Text = string.Empty;
            _customImagePath = "";
            if (_customImage is not null) { _customImage.Source = null; _customImage.Visibility = Visibility.Collapsed; }
            _customHost.Background = Brushes.Transparent;
            return;
        }
        var width = Math.Max(12, _item.Width);
        var height = Math.Max(12, _item.Height);
        var look = icon.Resolve(_state?.Invoke());
        var imagePath = (look.ImagePath ?? "").Trim();
        var hasImage = imagePath.Length > 0 && File.Exists(imagePath);
        if (_customImage is not null)
        {
            if (hasImage)
            {
                if (!string.Equals(_customImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
                {
                    try { _customImage.Source = ScorebugRender.LoadLogo(imagePath); _customImagePath = imagePath; }
                    catch { _customImage.Source = null; _customImagePath = ""; hasImage = false; }
                }
                _customImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                _customImage.Source = null;
                _customImagePath = "";
                _customImage.Visibility = Visibility.Collapsed;
            }
        }
        _customText.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
        _customText.Text = look.Glyph;
        _customText.FontSize = Math.Clamp(Math.Min(width, height) * 0.62, 8, 260);
        _customText.Foreground = ScorebugRender.ParseBrush(string.IsNullOrWhiteSpace(_item.IconColor) ? look.Color : _item.IconColor);
        _customHost.Background = ScorebugRender.ParseBrush(look.Plate);
        _customHost.Width = width;
        _customHost.Height = height;
        _customHost.CornerRadius = new CornerRadius(look.Round ? Math.Min(width, height) / 2 : Math.Min(width, height) * 0.16);
        _customHost.Opacity = Math.Clamp(look.Opacity, 0, 1);
        _customHost.RenderTransformOrigin = new Point(0.5, 0.5);
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(look.Scale, look.Scale));
        transform.Children.Add(new RotateTransform(look.Rotation));
        transform.Children.Add(new TranslateTransform(look.OffsetX, look.OffsetY));
        _customHost.RenderTransform = transform;
    }

    /// <summary>Paints the icon's own outline, fill and opacity, inside the element's box.</summary>
    void UpdateChrome()
    {
        var inset = Math.Max(0, _item.BorderThickness) + 1;
        _chrome.Margin = new Thickness(inset);
        _chrome.Background = string.IsNullOrWhiteSpace(_item.IconBackground)
            ? Brushes.Transparent
            : ScorebugRender.ParseBrush(_item.IconBackground);
        var opacity = Math.Clamp(_item.IconOpacity, 0, 1);
        _chrome.Opacity = opacity;
        _art.Opacity = opacity;
        // Any icon can be turned. A possession arrow pointing down is simply the arrow turned 90°,
        // so there is one mark instead of a sideways one and a downward one.
        var turn = Math.Clamp(_item.IconRotation, -360, 360);
        if (_arrow is not null)
        {
            // The arrow sits against one edge of its box, so the box itself must not turn: turning
            // the mark keeps it exactly where possession put it instead of swinging it out of the
            // plate and out of sight.
            _arrowTurn.Angle = turn;
            _art.RenderTransform = null;
        }
        else if (_dot is not null)
        {
            // The dot hugs an edge for the same reason, so it turns on its own centre too.
            if (Math.Abs(turn) < 0.01)
                _dot.RenderTransform = null;
            else
            {
                _dotTurn ??= new RotateTransform();
                _dotTurn.Angle = turn;
                _dot.RenderTransformOrigin = new Point(0.5, 0.5);
                if (!ReferenceEquals(_dot.RenderTransform, _dotTurn)) _dot.RenderTransform = _dotTurn;
            }
            _art.RenderTransform = null;
        }
        else if (Math.Abs(turn) < 0.01)
            _art.RenderTransform = null;
        else
        {
            _artTurn ??= new RotateTransform();
            _artTurn.Angle = turn;
            _art.RenderTransformOrigin = new Point(0.5, 0.5);
            if (!ReferenceEquals(_art.RenderTransform, _artTurn)) _art.RenderTransform = _artTurn;
        }
    }

    /// <summary>
    /// The icon outline belongs to the artwork, not to one rectangle around the whole box. Every
    /// dot, bar, base, arrow, flag part and card gets its own outline, so a possession dot has a
    /// ring around the dot and a timeout row has a border around each individual bar or dot.
    /// </summary>
    void ApplyArtworkBorders()
    {
        var thickness = Math.Clamp(_item.IconBorderThickness, 0, 40);
        var brush = ScorebugRender.ParseBrush(string.IsNullOrWhiteSpace(_item.IconBorderColor) ? "#FFFFFFFF" : _item.IconBorderColor);
        void Outline(Shape shape)
        {
            shape.Stroke = thickness > 0 ? brush : null;
            shape.StrokeThickness = thickness;
            if (thickness > 0) shape.StrokeLineJoin = PenLineJoin.Round;
        }
        if (_dot is not null) Outline(_dot);
        if (_arrow is not null) Outline(_arrow);
        if (_flagPole is not null) Outline(_flagPole);
        if (_flagCloth is not null) Outline(_flagCloth);
        if (_card is not null) Outline(_card);
        if (_bars is not null)
            foreach (var bar in _bars.Children.OfType<Shape>()) Outline(bar);
        // A base already has a faint outline of its own when the icon border is turned off; the
        // element's outline width and colour replace that on each base when it is turned on.
        for (var index = 0; index < _baseSquares.Count; index++)
        {
            var square = _baseSquares[index];
            if (thickness > 0)
            {
                square.Stroke = brush;
                square.StrokeThickness = thickness;
                square.StrokeLineJoin = PenLineJoin.Round;
            }
            else
            {
                square.Stroke = _baseLit[index] ? _baseBrush : _baseFaint;
                square.StrokeThickness = Math.Max(1.5, square.Width * 0.12);
            }
        }
        if (_customHost is not null)
        {
            _customHost.BorderBrush = thickness > 0 ? brush : null;
            _customHost.BorderThickness = new Thickness(thickness);
        }
    }

    /// <summary>Moves the icon off its box's centre, so one element can carry a mark elsewhere.</summary>
    void ApplyIconPlacement()
    {
        RenderTransform = Math.Abs(_item.IconOffsetX) < 0.01 && Math.Abs(_item.IconOffsetY) < 0.01
            ? null
            : new TranslateTransform(_item.IconOffsetX, _item.IconOffsetY);
    }

    /// <summary>Copies the current game into the icon: bar count, dot side, and the text beside it.</summary>
    public void Update(string? text = null)
    {
        // A value-bound icon is on the graphic only while its value says so: a flag icon with no flag
        // in the game must not sit there pointing at nothing, and it comes back on its own when the
        // value is set again.
        if (!string.IsNullOrWhiteSpace(_item.IconToken))
        {
            var bound = _state is null ? string.Empty : ScoreboardTemplates.Bind(_item.IconToken, _state(), _item.PeriodFormat, _item.DownCaps, _item.PlayClockColon, _item.GameClockLeadingZero, _item.ClockDecimals);
            var showing = !string.IsNullOrWhiteSpace(bound)
                && !string.Equals(bound.Trim(), "0", StringComparison.Ordinal)
                && !bound.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)
                && !bound.Trim().Equals("no", StringComparison.OrdinalIgnoreCase);
            Visibility = showing ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (!showing) return;
        }
        else if (Visibility != System.Windows.Visibility.Visible) Visibility = System.Windows.Visibility.Visible;
        ApplyIconPlacement();
        UpdateChrome();
        if (_customText is not null)
        {
            UpdateCustom();
            ApplyArtworkBorders();
            return;
        }
        var state = _state?.Invoke();
        // The icon can carry its own colour, which is what makes a flag icon read as a flag rather
        // than as part of the score text it happens to share a box with.
        var foreground = string.IsNullOrWhiteSpace(_item.IconColor)
            ? ScorebugRender.ResolveForeground(_item, state)
            : _item.IconColor;
        if (_bases is not null)
        {
            var first = state is not null && SportValues.Read(state, "baseball.first") != 0;
            var second = state is not null && SportValues.Read(state, "baseball.second") != 0;
            var third = state is not null && SportValues.Read(state, "baseball.third") != 0;
            var width = Math.Max(24, _item.Width);
            var height = Math.Max(24, _item.Height);
            // The three bases are built once for a given size and colour, then only lit and cleared.
            // Rebuilding them on every runner (what this used to do) threw away the squares mid-change,
            // so a value-change animation had nothing to animate.
            var lit = new[] { third, second, first };
            var fill = ScorebugRender.ResolveIconFill(_item, state);
            var geometry = $"{width:0.#}x{height:0.#}|{foreground}|{fill}";
            if (geometry != _baseSignature)
            {
                _baseSignature = geometry;
                _bases.Children.Clear();
                _baseSquares.Clear();
                _baseScales.Clear();
                var size = Math.Clamp(Math.Min(width, height) * 0.34, 9, 70);
                // The outline and the inside are separate brushes: a base can be a red diamond with a
                // white inside, or follow either team's palette independently of the element's text.
                _baseBrush = ScorebugRender.ParseBrush(foreground);
                _baseFill = ScorebugRender.ParseBrush(fill);
                var faint = (Brush)_baseBrush.Clone();
                faint.Opacity = 0.32;
                _baseFaint = faint;
                (double X, double Y)[] spots =
                [
                    (width * 0.30, height * 0.70),
                    (width * 0.50, height * 0.24),
                    (width * 0.70, height * 0.70)
                ];
                for (var index = 0; index < spots.Length; index++)
                {
                    var scale = new ScaleTransform(1, 1);
                    var square = new Rectangle
                    {
                        Width = size,
                        Height = size,
                        RadiusX = Math.Max(1, size * 0.10),
                        RadiusY = Math.Max(1, size * 0.10),
                        RenderTransformOrigin = new Point(0.5, 0.5),
                        RenderTransform = new TransformGroup { Children = { new RotateTransform(45), scale } },
                        StrokeThickness = Math.Max(1.5, size * 0.12)
                    };
                    Canvas.SetLeft(square, spots[index].X - size / 2);
                    Canvas.SetTop(square, spots[index].Y - size / 2);
                    _baseSquares.Add(square);
                    _baseScales.Add(scale);
                    _bases.Children.Add(square);
                }
                // A fresh set of squares starts from the game as it stands, painted instantly: the
                // first paint and every thumbnail are stills, not animated changes.
                for (var index = 0; index < _baseSquares.Count; index++)
                {
                    _baseLit[index] = lit[index];
                    PaintBase(index, lit[index], null);
                }
            }
            var seconds = Math.Clamp(_item.TransitionSeconds, 0, 5);
            // Only a real value-change animation is honoured, and only once the view is on screen:
            // thumbnails and the studio's first paint land instantly.
            var animate = _item.Transition != ScorebugTransition.Cut && seconds > 0.02 && IsLoaded;
            for (var index = 0; index < lit.Length; index++)
            {
                if (_baseLit[index] == lit[index]) continue;
                _baseLit[index] = lit[index];
                PaintBase(index, lit[index], animate ? TimeSpan.FromSeconds(seconds) : null);
            }
            ApplyArtworkBorders();
            return;
        }
        if (_bars is not null)
        {
            var remaining = 0;
            var limit = 0;
            if (state is not null)
            {
                remaining = Math.Clamp(_item.IconVisitors ? state.AwayTimeouts : state.HomeTimeouts, 0, 12);
                limit = _item.IconVisitors ? state.AwayTimeoutLimit : state.HomeTimeoutLimit;
            }
            var count = Math.Clamp(Math.Max(remaining, limit), 0, 12);
            var vertical = Icon == ScorebugIcon.TimeoutBars && _item.IconVertical;
            _bars.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            if (count == 0)
            {
                _bars.Children.Clear();
                _appliedForeground = foreground;
                ApplyArtworkBorders();
                return;
            }
            var scale = Math.Clamp(_item.IconScale, 0.25, 3);
            // The slot is the whole side divided by the count. A bar fills nearly the whole slot, so
            // resizing the element vertically makes a vertical stack taller instead of leaving the
            // old 18 px cap behind. Horizontal bars take their thickness from the element's height.
            var slot = (vertical ? Math.Max(1, _item.Height) : Math.Max(1, _item.Width)) / Math.Max(1, count);
            var gap = Math.Max(1, slot * 0.16);
            var width = vertical
                ? Math.Clamp(Math.Max(1, _item.Width) * 0.62 * scale, 2, Math.Max(2, _item.Width))
                : Math.Clamp(slot - gap, 2, 400);
            var thickness = vertical
                ? Math.Clamp(slot - gap, 2, 400)
                : Math.Clamp(Math.Max(1, _item.Height) * 0.28 * scale, 2, Math.Max(2, _item.Height));
            var usedBrush = ScorebugRender.ParseBrush(string.IsNullOrWhiteSpace(_item.IconUsedColor) ? "#FF9AA5B2" : _item.IconUsedColor);
            if (_bars.Children.Count != count)
            {
                _bars.Children.Clear();
                var animate = Animates;
                for (var i = 0; i < count; i++)
                {
                    var fill = i < remaining ? ScorebugRender.ParseBrush(foreground) : usedBrush;
                    // Bars are rounded lines; the dots variant is the same row of round pips.
                    Shape bar = Icon == ScorebugIcon.TimeoutDots
                        ? new Ellipse { Width = width, Height = width, Fill = fill }
                        : new Rectangle
                        {
                            Width = width,
                            Height = thickness,
                            RadiusX = thickness / 2,
                            RadiusY = thickness / 2,
                            Fill = fill
                        };
                    bar.Margin = vertical
                        ? new Thickness(0, gap / 2, 0, gap / 2)
                        : new Thickness(gap / 2, 0, gap / 2, 0);
                    _bars.Children.Add(bar);
                    // A timeout being used keeps its place but turns grey, so the row still reads as
                    // the full allotment instead of shrinking every time a timeout is taken.
                    if (animate) AnimateIn(bar);
                }
                _appliedForeground = foreground;
            }
            else
            {
                // A size, colour or used-timeout change updates the existing bars in place instead of
                // rebuilding them, so the row never flickers while the slider moves.
                var brush = ScorebugRender.ParseBrush(foreground);
                var index = 0;
                foreach (var child in _bars.Children.OfType<Shape>())
                {
                    child.Width = width;
                    child.Height = Icon == ScorebugIcon.TimeoutDots ? width : thickness;
                    if (child is Rectangle rounded) { rounded.RadiusX = thickness / 2; rounded.RadiusY = thickness / 2; }
                    child.Margin = vertical
                        ? new Thickness(0, gap / 2, 0, gap / 2)
                        : new Thickness(gap / 2, 0, gap / 2, 0);
                    child.Fill = index++ < remaining ? brush : usedBrush;
                }
                _appliedForeground = foreground;
            }
            ApplyArtworkBorders();
            return;
        }
        if (_arrow is not null)
        {
            _arrow.Fill = ScorebugRender.ParseBrush(foreground);
            // The arrow belongs to one plate and only exists while that team has the ball. It leans
            // inward from the plate's edge: left for home, right for away, mirrored by the flip.
            // One arrow follows the ball rather than belonging to one plate, so a possession change
            // never leaves the graphic with no mark at all. Flipped mirrors the field when the
            // home team is drawn on the right.
            var home = state?.HomePossession ?? true;
            // Home normally sits on the west side: the arrow hugs that edge and faces east into
            // the plate. A mirrored field (home drawn on the right) flips the edge and the arrow.
            var homeSide = _item.IconFlipped ? !home : home;
            var pointsEast = homeSide;
            // The turn plays whenever the ball changes hands: the mark slides and fades in from the
            // side it is leaving, so possession never blinks from one edge to the other.
            // The first paint is a still: rebuilding a feed for another layout must not replay the
            // mark's arrival when possession has not changed.
            if (_arrowShown is null) _arrowShown = pointsEast;
            if (_arrowShown != pointsEast)
            {
                _arrowShown = pointsEast;
                _arrow.Visibility = Visibility.Visible;
                // A held fade from the previous switch would keep the mark invisible; drop it first.
                _arrow.BeginAnimation(UIElement.OpacityProperty, null);
                _arrowSlide.BeginAnimation(TranslateTransform.XProperty, null);
                // The turn always animates, even on a Cut element: the ball changing hands should be
                // visible rather than the arrow blinking to the other side.
                var reveal = TimeSpan.FromSeconds(Math.Clamp(_item.TransitionSeconds, 0.15, 0.5));
                _arrow.Opacity = 0;
                _arrow.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, reveal));
                _arrowSlide.X = pointsEast ? -14 : 14;
                _arrowSlide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(pointsEast ? -14 : 14, 0, reveal));
            }
            _arrow.Visibility = Visibility.Visible;
            var height = Math.Clamp(Math.Min(_item.Height * 0.62, _item.FontSize * 1.05), 6, 200);
            var width = height * 0.58;
            var gap = Math.Max(0, _item.Width * 0.02);
            // Possession moves the mark from one edge to the other; it never turns the glyph around.
            // The arrow keeps the direction the designer gave it (Flipped mirrors it once, like every
            // other icon), so the ball changing hands slides the arrow instead of flipping it.
            _arrow.Points = _item.IconFlipped
                ? new PointCollection { new Point(width, 0), new Point(0, height / 2), new Point(width, height) }
                : new PointCollection { new Point(0, 0), new Point(width, height / 2), new Point(0, height) };
            _arrow.Width = width;
            _arrow.Height = height;
            _arrow.Margin = new Thickness(gap, 0, gap, 0);
            _arrow.HorizontalAlignment = homeSide ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            _arrow.Fill = ScorebugRender.ParseBrush(foreground);
            ApplyArtworkBorders();
            return;
        }
        if (_flagPole is not null)
        {
            // A flag is sized from the element's type like a card, then capped by its own box.
            var height = Math.Clamp(Math.Min(_item.Height, _item.FontSize * 1.7), 8, 400);
            var width = height * 0.7;
            var pole = Math.Max(1.5, width * 0.1);
            _flagPole.Width = pole;
            _flagPole.Height = height;
            _flagCloth!.Width = width;
            _flagCloth.Height = height * 0.52;
            _flagCloth.Margin = new Thickness(pole, 0, 0, height * 0.48);
            _flagCloth.Fill = ScorebugRender.ParseBrush(string.IsNullOrWhiteSpace(_item.IconColor) ? "#FFF2C230" : _item.IconColor);
            _flagPole.Fill = ScorebugRender.ParseBrush(foreground);
            ApplyArtworkBorders();
            return;
        }
        if (_card is not null)
        {
            // The card is sized from the element's type size and then capped by its own box, so a
            // small plate shows a small card instead of a cropped one.
            var height = Math.Clamp(Math.Min(_item.Height, _item.FontSize * 1.7), 8, 400);
            var width = height * 0.68;
            var cardRoom = DotBox();
            if (cardRoom.Width > 0) width = Math.Min(width, cardRoom.Width * 0.92);
            if (cardRoom.Height > 0) height = Math.Min(height, cardRoom.Height * 0.92);
            _card.Width = width;
            _card.Height = height;
            _card.RadiusX = _card.RadiusY = Math.Max(2, width * 0.08);
            ApplyArtworkBorders();
            return;
        }
        if (_dot is not null)
        {
            // The mark keeps its size when the box is resized, so a wider plate does not inflate the
            // dot. Ctrl-resizing in the studio scales the element's type size, and the dot follows,
            // so the mark can still be made bigger on purpose. A box shorter than the mark wins,
            // though: a dot that is bigger than its plate would be cut by the plate's own clip.
            var size = Math.Clamp(_item.FontSize * 1.15, 6, 240);
            var room = DotBox();
            if (room.Height > 0) size = Math.Min(size, room.Height);
            // The gap off the plate's edge only exists when the box can afford it, so a tight box
            // never squeezes the mark into an oval against its own border.
            var gap = room.Width <= 0 || room.Width < size * 1.9 ? 0 : size * 0.22;
            _dot.Width = size;
            _dot.Height = size;
            _dot.Margin = new Thickness(gap, 0, gap, 0);
            _dot.Fill = ScorebugRender.ParseBrush(foreground);
            // Visitors on the left, home on the right, like the console; the element can mirror it.
            var home = state?.HomePossession ?? true;
            var right = _item.IconFlipped ? !home : home;
            _dot.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            // Possession changing moves the mark to the other side of the plate. It slides across
            // the plate, from the side the ball left, so a viewer sees the ball change hands rather
            // than a dot teleport — and the slide happens inside the box, never past its clip.
            // Like the arrow, the dot's first paint lands where it belongs without an entrance.
            if (_dotRight is null) _dotRight = right;
            if (_dotRight != right)
            {
                _dotRight = right;
                // Whatever was running belongs to the old side; drop it before starting again.
                _dot.BeginAnimation(UIElement.OpacityProperty, null);
                if (_dot.RenderTransform is TranslateTransform running) running.BeginAnimation(TranslateTransform.XProperty, null);
                if (Animates)
                {
                    var travel = Math.Max(0, room.Width - size - gap * 2);
                    var from = right ? -travel : travel;
                    var shift = new TranslateTransform(from, 0);
                    _dot.RenderTransform = shift;
                    var duration = MotionTime;
                    var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
                    shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, 0, duration) { EasingFunction = easing });
                    _dot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.35, 1, duration) { EasingFunction = easing });
                }
                else
                {
                    _dot.RenderTransform = null;
                    _dot.Opacity = 1;
                }
            }
        }
        ApplyArtworkBorders();
    }

    /// <summary>
    /// The room the dot has to live in: the laid-out box when it has one, otherwise the element's
    /// own design size, so a slide is measured before the first arrange instead of guessing.
    /// </summary>
    (double Width, double Height) DotBox()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (double.IsNaN(width) || width <= 0) width = _item.Width;
        if (double.IsNaN(height) || height <= 0) height = _item.Height;
        return (double.IsNaN(width) ? 0 : width, double.IsNaN(height) ? 0 : height);
    }

    /// <summary>True when this icon should animate its changes rather than snap.</summary>
    bool Animates => _item.Transition != ScorebugTransition.Cut
        && Math.Clamp(_item.TransitionSeconds, 0, 5) > 0.02 && IsLoaded;

    /// <summary>
    /// Brings a piece of live artwork in the way the element says it should arrive: a saved
    /// value-change clip if the studio attached one, otherwise the chosen preset transition.
    /// </summary>
    void AnimateIn(FrameworkElement element)
    {
        if (_item.MotionTransition is { IsEmpty: false } clip && IsLoaded)
        {
            element.Opacity = 1;
            MotionPlayback.PlayClip(element, clip, 1, ambient: false);
            return;
        }
        if (Animates) PlayIn(element);
        else element.Opacity = 1;
    }

    /// <summary>Fades a mark out before it is collapsed, so possession never just disappears.</summary>
    void AnimateOut(FrameworkElement element, Action done)
    {
        if (!Animates || _item.MotionTransition is { IsEmpty: false })
        {
            element.Opacity = 0;
            done();
            return;
        }
        var fade = new DoubleAnimation(0, MotionTime) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        fade.Completed += (_, _) => done();
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    TimeSpan MotionTime => TimeSpan.FromSeconds(Math.Clamp(_item.TransitionSeconds, 0, 5));

    /// <summary>
    /// Brings a freshly built piece of the icon in with the element's chosen value-change motion:
    /// a dissolve, a slide from the edge, or a pop/zoom, so bars and the dot move like the text does.
    /// </summary>
    void PlayIn(FrameworkElement element)
    {
        var duration = MotionTime;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        switch (_item.Transition)
        {
            case ScorebugTransition.Cut:
                return;
            case ScorebugTransition.Fade:
                element.Opacity = 0;
                element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
                return;
            case ScorebugTransition.SlideLeft or ScorebugTransition.SlideRight
                or ScorebugTransition.SlideUp or ScorebugTransition.SlideDown
                or ScorebugTransition.WheelUp or ScorebugTransition.WheelDown:
                var (fromX, fromY) = _item.Transition switch
                {
                    ScorebugTransition.SlideLeft => (22.0, 0.0),
                    ScorebugTransition.SlideRight => (-22.0, 0.0),
                    ScorebugTransition.SlideUp => (0.0, 12.0),
                    ScorebugTransition.SlideDown => (0.0, -12.0),
                    ScorebugTransition.WheelUp => (0.0, 12.0),
                    _ => (0.0, -12.0)
                };
                var translate = new TranslateTransform(fromX, fromY);
                element.RenderTransform = translate;
                translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, duration) { EasingFunction = easing });
                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, duration) { EasingFunction = easing });
                element.Opacity = 0;
                element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
                return;
            default:
                // Flip, zoom, pop, drop and the remaining fades: a small scale up with a dissolve.
                element.RenderTransformOrigin = new Point(0.5, 0.5);
                var from = _item.Transition == ScorebugTransition.Zoom ? 0.2 : 0.45;
                var scale = new ScaleTransform(from, from);
                element.RenderTransform = scale;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, 1, duration) { EasingFunction = easing });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(from, 1, duration) { EasingFunction = easing });
                element.Opacity = 0;
                element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
                return;
        }
    }

    /// <summary>
    /// Lights or clears one base. A runner arriving fades and pops the base in over the element's own
    /// value-change time, so the diamond moves the way the rest of the graphic does; clearing a base
    /// is a quiet change and lands at once.
    /// </summary>
    void PaintBase(int index, bool on, TimeSpan? duration)
    {
        if (index < 0 || index >= _baseSquares.Count) return;
        var square = _baseSquares[index];
        var scale = _baseScales[index];
        square.BeginAnimation(UIElement.OpacityProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (duration is null)
        {
            ApplyBaseLook(square, on);
            square.Opacity = 1;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            return;
        }
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (!on)
        {
            // A runner leaving is a quiet change: the base goes back to its faint outline immediately,
            // rather than staying lit for the length of an exit animation.
            ApplyBaseLook(square, false);
            square.Opacity = 1;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            return;
        }
        ApplyBaseLook(square, true);
        square.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration.Value) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.55, 1, duration.Value) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.55, 1, duration.Value) { EasingFunction = easing });
    }

    /// <summary>Lit: the inside takes the base fill, the outline the text colour. Empty: a faint outline only.</summary>
    void ApplyBaseLook(Rectangle square, bool on)
    {
        square.Fill = on ? _baseFill : Brushes.Transparent;
        square.Stroke = on ? _baseBrush : _baseFaint;
    }
}
