using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

public sealed class ScorebugCanvasView : Grid
{
    readonly ScorebugCatalog _catalog;
    readonly GameEngine _engine;
    readonly Func<GameState> _stateProvider;
    readonly bool _live;
    // Which artwork this view draws. The audience output resolves it from its own display; the
    // window preview resolves it from that window's staged selection.
    readonly Func<ScorebugLayout> _layoutProvider;
    readonly Canvas _canvas = new() { Background=new SolidColorBrush(Color.FromRgb(9,16,24)), ClipToBounds=true };
    // Everything the view draws sits on this stage, so a layout hand-over can hold the outgoing
    // artwork on top of the incoming one while it slides or dissolves away.
    readonly Grid _stage = new() { ClipToBounds = true };
    // Which display this view is a feed of; null means the display being edited.
    readonly string? _displayId;
    string? _drawnLayoutId;
    ScorebugLayout? _drawnLayout;
    FrameworkElement? _swapOverlay;
    // Pieces that exist only while a hand-over runs, and the timer that ends an exit or a scoped
    // arrival. Both belong to the swap, not to the layout, so FinishSwap takes them down together.
    Canvas? _swapPieces;
    DispatcherTimer? _swapTimer;
    readonly MediaPlayback? _playback;
    readonly bool _muteVideo;
    // A silent monitor of the audience picture (not a preview), so it follows the on-air cue.
    readonly bool _onAirVideo;
    readonly Dictionary<string, (FrameworkElement Host, FrameworkElement Content)> _visuals = [];
    // Keyed by element id so a rebuild can keep the surface (and the running picture) alive.
    readonly Dictionary<string, VideoSurface> _videoSurfaces = [];
    readonly Dictionary<string, HtmlSurface> _htmlSurfaces = [];
    readonly Dictionary<string, (ScorebugAnimation Animation, double Opacity, string Motion, double Rotation)> _animations = [];
    readonly Dictionary<string,string> _backgrounds = [];
    readonly Dictionary<string,bool> _transparentStates = [];
    readonly Dictionary<string,string> _borders = [];
    // Value switch delay: what each element is currently printing, and the wait before a change
    // lands. The operator sets the delay in Preferences; 0 prints instantly.
    readonly Dictionary<string,string> _shownText = [];
    readonly Dictionary<string,(string Target, System.Windows.Threading.DispatcherTimer Timer)> _pendingText = [];
    bool _flushDelay;
    // Time-of-day text is not driven by the game clock, so it gets its own one-second pulse;
    // otherwise a layout with {{TIME}} freezes whenever no clock is running.
    readonly System.Windows.Threading.DispatcherTimer _wallClock = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <param name="publishedLayout">True for the audience output: it follows the layout that was
    /// actually published, not the one being designed in the preview.</param>
    /// <param name="publishedLayout">True for the audience output: it follows the layout that was
    /// actually published, not the one being designed in the preview.</param>
    /// <param name="layoutProvider">Draws exactly the layout this view belongs to (a specific
    /// display's on-air layout, or a window's staged one). Overrides <paramref name="publishedLayout"/>.</param>
    public ScorebugCanvasView(ScorebugCatalog catalog, GameEngine engine, Func<GameState>? stateProvider=null, MediaPlayback? playback=null, bool muteVideo=true, bool publishedLayout=false, Func<ScorebugLayout>? layoutProvider=null, bool onAirVideo=false, string? displayId=null)
    {
        _catalog=catalog;_engine=engine;_live=stateProvider is null;
        _displayId=displayId;
        _layoutProvider = layoutProvider ?? (publishedLayout ? () => _catalog.PublishedLayout : () => _catalog.Layout);
        _stateProvider=stateProvider??(()=>_engine.State);
        _playback=playback;_muteVideo=muteVideo;_onAirVideo=onAirVideo;
        _stage.Children.Add(_canvas);
        Children.Add(new Viewbox { Stretch=Stretch.Uniform, Child=_stage });
        _catalog.Changed+=Rebuild;
        // A roster edit changes what a specific-player binding prints, without touching the game.
        RosterStore.Changed+=UpdateValues;
        if(stateProvider is null)_engine.Changed+=UpdateValues;else _engine.Changed+=UpdateClockValues;
        _wallClock.Tick+=(_,_)=>{if(UsesWallClock())UpdateValues();};
        _wallClock.Start();
        Rebuild();
    }

    /// <summary>The artwork this view is drawing: the design being previewed, or the published one.</summary>
    public ScorebugLayout Layout => _layoutProvider();

    public void Rebuild()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(Rebuild);return;}
        // A different layout on the same feed is a hand-over: keep what is currently on screen as a
        // still, so the new layout can be revealed through it rather than snapping into place. Any
        // other rebuild (an edit, a palette change) must never trigger the animation.
        var incoming = Layout;
        var swapping = _drawnLayoutId is not null && !string.Equals(_drawnLayoutId, incoming.Id, StringComparison.OrdinalIgnoreCase);
        var outgoing = swapping ? CaptureCanvas() : null;
        // The layout the still was taken from, so a per-piece exit can be cropped out of it.
        var outgoingLayout = swapping ? _drawnLayout : null;
        var previousVideos=new Dictionary<string,VideoSurface>(_videoSurfaces);
        var previousHtml=new Dictionary<string,HtmlSurface>(_htmlSurfaces);
        var previousVisuals=new Dictionary<string,(FrameworkElement Host, FrameworkElement Content)>(_visuals);
        foreach(var (id,_) in _pendingText) CancelPending(id);
        _videoSurfaces.Clear();_htmlSurfaces.Clear();_visuals.Clear();_animations.Clear();_backgrounds.Clear();_transparentStates.Clear();_borders.Clear();_shownText.Clear();
        var layout = incoming;
        _stage.Width=layout.Width;_stage.Height=layout.Height;
        _canvas.Width=layout.Width;_canvas.Height=layout.Height;
        // The artwork backdrop belongs to the layout, so a green-screen design and the studio agree.
        _canvas.Background=ScorebugRender.ParseBrush(layout.Background);
        var live=new List<UIElement>();
        var zIndex=0;
        foreach(var item in layout.Elements)
        {
            // The preview and the audience screen stay empty when nothing is cued; the studio is
            // the only place the tag placeholder is drawn.
            // A video box that still exists keeps its live surface. Rebuilding it stopped the
            // decoder and re-opened the file, which is why editing the studio restarted playback.
            FrameworkElement content;
            if(previousVideos.TryGetValue(item.Id,out var surface)){surface.SetElement(item);content=surface;}
            else if(previousHtml.TryGetValue(item.Id,out var page)){page.Update(item);content=page;}
            else content=ScorebugRender.CreateContent(item,_stateProvider,_playback,_muteVideo,showVideoPlaceholder:false,onAirVideo:_onAirVideo);
            if(content is VideoSurface video) _videoSurfaces[item.Id]=video;
            // An HTML element keeps its browser and its last frame across a rebuild, so editing the
            // graphic never blanks or restarts it.
            if(content is HtmlSurface htmlPage) _htmlSurfaces[item.Id]=htmlPage;
            // The printed value fades on its own; text sets this on itself, other artwork follows here.
            if(content is not ScorebugTextBlock) content.Opacity=Math.Clamp(item.TextOpacity,0,1);
            // The host border is reused as well. Moving a MediaElement to a new parent takes it out
            // of the visual tree, which stops the decoder and restarts the picture from frame one:
            // that is what made a video jump back to the start on every studio edit.
            var reused=previousVisuals.TryGetValue(item.Id,out var prior)&&ReferenceEquals(prior.Content,content);
            var host=reused?(Border)prior.Host:new Border{ClipToBounds=true,Child=content};
            if(!reused)host.Background=ScorebugRender.HostBackground(item,_stateProvider());
            host.Width=item.Width;host.Height=item.Height;host.Opacity=item.Opacity;
            host.Visibility=item.Visible?Visibility.Visible:Visibility.Collapsed;
            MotionPlayback.SetDesignRotation(host,item.Rotation);
            Canvas.SetLeft(host,item.X);Canvas.SetTop(host,item.Y);Panel.SetZIndex(host,zIndex++);
            _visuals[item.Id]=(host,content);
            // A rebuilt view starts from what it draws right now, so a delay never blanks the graphic.
            if(content is ScorebugTextBlock shown) _shownText[item.Id]=shown.Text;
            if(!_canvas.Children.Contains(host))_canvas.Children.Add(host);
            live.Add(host);
            ApplyAnimation(item,host);
        }
        foreach(var child in _canvas.Children.OfType<UIElement>().ToList())
            if(!live.Contains(child))_canvas.Children.Remove(child);
        foreach(var (id,orphan) in previousVideos)
            if(!_videoSurfaces.ContainsKey(id))orphan.Dispose();
        foreach(var (id,orphan) in previousHtml)
            if(!_htmlSurfaces.ContainsKey(id))orphan.Dispose();
        UpdateValues();
        _drawnLayoutId=layout.Id;
        _drawnLayout=layout;
        if(outgoing is not null) PlaySwap(outgoing,outgoingLayout,incoming);
    }

    /// <summary>A still of exactly what this feed is showing right now, used as the outgoing frame.</summary>
    RenderTargetBitmap? CaptureCanvas()
    {
        try
        {
            var width=Math.Max(1,(int)Math.Round(_canvas.Width));
            var height=Math.Max(1,(int)Math.Round(_canvas.Height));
            // Already laid out inside its Viewbox; rendering the tree as it stands avoids kicking
            // layout in the middle of a live feed.
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
            bitmap.Render(_canvas);
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    /// <summary>
    /// Runs the incoming layout's own hand-over: the outgoing still dissolves, slides or swings away
    /// while the new artwork comes in from the other side. Duration 0 or Cut lands instantly.
    /// </summary>
    void PlaySwap(RenderTargetBitmap outgoing,ScorebugLayout? outgoingLayout,ScorebugLayout incoming)
    {
        // A video file chosen for this layout is the hand-over: it plays full-frame over the
        // outgoing still and ends with the new layout showing underneath.
        if(TryPlayVideoSwap(outgoing,incoming))return;
        // A hand-over built in the transition maker replaces the seven presets for this layout:
        // its exit keys run on the outgoing still and its enter keys (and colour keys) on the new one.
        if(incoming.TransitionClip is { Kind:MotionKind.Transition } clip && !clip.IsEmpty)
        {
            PlayClipSwap(outgoing,outgoingLayout,incoming,clip);
            return;
        }
        var transition=incoming.Transition;
        var seconds=Math.Clamp(incoming.TransitionSeconds,0,5);
        FinishSwap();
        if(transition==LayoutTransition.Cut||seconds<=0)return;
        var width=Math.Max(1,_canvas.Width);
        var height=Math.Max(1,_canvas.Height);
        var duration=TimeSpan.FromSeconds(seconds);
        var easing=new CubicEase{EasingMode=EasingMode.EaseInOut};
        var overlay=new Image{Source=outgoing,Width=width,Height=height,Stretch=Stretch.Fill,IsHitTestVisible=false};
        var leaving=new TranslateTransform();
        var arriving=new TranslateTransform();
        overlay.RenderTransform=leaving;
        _canvas.RenderTransform=arriving;
        _stage.Children.Add(overlay);
        _swapOverlay=overlay;
        // A newer hand-over removes this overlay and starts its own; this callback must not then
        // tear that newer one down when its animation finishes in the background.
        void Finish(){ if(ReferenceEquals(_swapOverlay,overlay)) FinishSwap(); }
        // The new artwork moves in from the side the old one moves towards, so the two never overlap
        // in the same place for longer than a frame.
        var arrive=new DoubleAnimation(0,0,duration){EasingFunction=easing};
        switch(transition)
        {
            case LayoutTransition.SlideLeft:
                arrive=new DoubleAnimation(width,0,duration){EasingFunction=easing};
                leaving.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,-width,duration){EasingFunction=easing});
                break;
            case LayoutTransition.SlideRight:
                arrive=new DoubleAnimation(-width,0,duration){EasingFunction=easing};
                leaving.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,width,duration){EasingFunction=easing});
                break;
            case LayoutTransition.SlideUp:
                arrive=new DoubleAnimation(height,0,duration){EasingFunction=easing};
                leaving.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,-height,duration){EasingFunction=easing});
                break;
            case LayoutTransition.SlideDown:
                arrive=new DoubleAnimation(-height,0,duration){EasingFunction=easing};
                leaving.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,height,duration){EasingFunction=easing});
                break;
            case LayoutTransition.Zoom:
            {
                overlay.RenderTransformOrigin=new Point(0.5,0.5);
                _canvas.RenderTransformOrigin=new Point(0.5,0.5);
                var scaleOut=new ScaleTransform(1,1);
                var scaleIn=new ScaleTransform(0.92,0.92);
                overlay.RenderTransform=scaleOut;
                _canvas.RenderTransform=scaleIn;
                scaleOut.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,1.14,duration){EasingFunction=easing});
                scaleOut.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,1.14,duration){EasingFunction=easing});
                scaleIn.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(0.92,1,duration){EasingFunction=easing});
                scaleIn.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(0.92,1,duration){EasingFunction=easing});
                break;
            }
        }
        arriving.BeginAnimation(TranslateTransform.XProperty,arrive);
        var fade=new DoubleAnimation(1,0,duration){EasingFunction=easing};
        fade.Completed+=(_,_)=>Finish();
        overlay.BeginAnimation(UIElement.OpacityProperty,fade);
    }

    /// <summary>
    /// Runs a keyframed hand-over: the outgoing frame follows the clip's exit leg and the incoming
    /// artwork follows its enter leg, with the clip's colour keys repainting the new graphic as it
    /// arrives. The outgoing frame is a still, so only its movement and opacity are animated.
    /// </summary>
    void PlayClipSwap(RenderTargetBitmap outgoing, ScorebugLayout? outgoingLayout, ScorebugLayout incoming, MotionClip clip)
    {
        FinishSwap();
        var width=Math.Max(1,_canvas.Width);
        var height=Math.Max(1,_canvas.Height);
        var exit=clip.ExitPhase;
        var enter=clip.EnterPhase;
        // Items the operator unchecked in the transition maker stay put, and any key scoped to a
        // piece means the arrival runs on each item's own box rather than on the whole canvas.
        var stationary=incoming.Handover?.Where(entry=>!entry.Arrives).Select(entry=>entry.ElementId).ToHashSet(StringComparer.OrdinalIgnoreCase)??[];
        var pieceKeys=clip.Phases.Any(phase=>phase.Tracks.Any(track=>!string.IsNullOrWhiteSpace(track.PieceId))
            ||phase.Colors.Any(track=>!string.IsNullOrWhiteSpace(track.PieceId)));
        var exitScoped=outgoingLayout is not null&&exit is not null
            &&(exit.Tracks.Any(track=>!string.IsNullOrWhiteSpace(track.PieceId))
               ||exit.Colors.Any(track=>!string.IsNullOrWhiteSpace(track.PieceId)));

        // The outgoing frame: the whole still, or one cropped still per item when the exit itself is
        // keyed piece by piece. The crops are exactly what was on screen, so a piece moves on its own.
        FrameworkElement outgoingHost;
        if(exitScoped)
        {
            var crops=new Canvas{Width=width,Height=height,IsHitTestVisible=false};
            // Crop against the outgoing still's own pixels: the two layouts may be different sizes.
            var pixelWidth=Math.Max(1,outgoing.PixelWidth);
            var pixelHeight=Math.Max(1,outgoing.PixelHeight);
            // Pieces are authored on the arriving layout. When the layout that is leaving has the
            // same named item, its exit keys belong to that item too, so match by name as a fallback.
            string PieceIdFor(ScorebugElement leaving)
            {
                if(clip.Phases.Any(phase=>phase.Tracks.Any(track=>string.Equals(track.PieceId,leaving.Id,StringComparison.OrdinalIgnoreCase))
                    ||phase.Colors.Any(track=>string.Equals(track.PieceId,leaving.Id,StringComparison.OrdinalIgnoreCase))))
                    return leaving.Id;
                var match=incoming.Elements.FirstOrDefault(candidate=>!string.IsNullOrWhiteSpace(candidate.Name)
                    &&string.Equals(candidate.Name,leaving.Name,StringComparison.OrdinalIgnoreCase));
                return match?.Id??leaving.Id;
            }
            foreach(var item in outgoingLayout!.Elements.Where(element=>element.Visible))
            {
                var x=(int)Math.Clamp(item.X,0,Math.Max(0,pixelWidth-1));
                var y=(int)Math.Clamp(item.Y,0,Math.Max(0,pixelHeight-1));
                var w=(int)Math.Clamp(item.Width,1,pixelWidth-x);
                var h=(int)Math.Clamp(item.Height,1,pixelHeight-y);
                if(w<1||h<1)continue;
                var crop=new CroppedBitmap(outgoing,new Int32Rect(x,y,w,h));
                crop.Freeze();
                var image=new Image{Source=crop,Width=w,Height=h,Stretch=Stretch.Fill,IsHitTestVisible=false};
                Canvas.SetLeft(image,item.X);Canvas.SetTop(image,item.Y);
                crops.Children.Add(image);
                MotionPlayback.PlayPhase(image,exit,Math.Clamp(item.Opacity,0,1),pieceId:PieceIdFor(item));
            }
            outgoingHost=crops;
        }
        else
        {
            outgoingHost=new Image{Source=outgoing,Width=width,Height=height,Stretch=Stretch.Fill,IsHitTestVisible=false};
            if(exit is not null&&!exit.IsEmpty) MotionPlayback.PlayPhase(outgoingHost,exit,1);
        }
        _stage.Children.Add(outgoingHost);
        _swapOverlay=outgoingHost;

        // Temporary pieces live for the hand-over only; they are drawn above both layouts.
        var temporary=new List<(FrameworkElement Host, MotionPiece Piece)>();
        Canvas? pieces=null;
        if(clip.Pieces.Count>0)
        {
            pieces=new Canvas{Width=width,Height=height,IsHitTestVisible=false};
            foreach(var piece in clip.Pieces)
            {
                var host=BuildTemporaryPiece(piece);
                pieces.Children.Add(host);
                temporary.Add((host,piece));
                if(exit is {Enabled:true,IsEmpty:false}) MotionPlayback.PlayPhase(host,exit,Math.Clamp(piece.Opacity,0,1),pieceId:piece.Id);
            }
            _stage.Children.Add(pieces);
            _swapPieces=pieces;
        }

        bool Active()=>ReferenceEquals(_swapOverlay,outgoingHost)||(pieces is not null&&ReferenceEquals(_swapPieces,pieces));
        void ClearOutgoing()
        {
            if(!ReferenceEquals(_swapOverlay,outgoingHost))return;
            _stage.Children.Remove(outgoingHost);
            _swapOverlay=null;
            if(outgoingHost is Panel panel)
                foreach(var child in panel.Children.OfType<FrameworkElement>()) MotionPlayback.Stop(child);
            else MotionPlayback.Stop(outgoingHost);
        }
        void Arrive()
        {
            if(!Active())return;
            ClearOutgoing();
            if(enter is null||enter.IsEmpty){FinishSwap();return;}
            var perItem=pieceKeys||stationary.Count>0||clip.Pieces.Count>0;
            if(!perItem)
            {
                MotionPlayback.PlayPhase(_canvas,enter,1,FinishSwap);
                return;
            }
            foreach(var item in incoming.Elements)
            {
                if(stationary.Contains(item.Id))continue;
                if(!_visuals.TryGetValue(item.Id,out var visual))continue;
                MotionPlayback.PlayPhase(visual.Host,enter,Math.Clamp(item.Opacity,0,1),pieceId:item.Id);
            }
            foreach(var (host,piece) in temporary)
                MotionPlayback.PlayPhase(host,enter,Math.Clamp(piece.Opacity,0,1),pieceId:piece.Id);
            // Scoped pieces finish individually; one timer ends the hand-over once the leg is over.
            StartSwapTimer(enter.Seconds,FinishSwap);
        }
        if(exit is {Enabled:true,IsEmpty:false}&&exit.Seconds>0.001) StartSwapTimer(exit.Seconds,Arrive);
        else Arrive();
    }

    FrameworkElement BuildTemporaryPiece(MotionPiece piece)
    {
        var text=new TextBlock
        {
            Text=piece.Text,
            FontSize=piece.FontSize,
            Foreground=new SolidColorBrush(Paint.ColorOf(piece.TextColor,Colors.White)),
            HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Center,
            TextAlignment=TextAlignment.Center,
            TextWrapping=TextWrapping.Wrap
        };
        var host=new Border
        {
            Width=piece.Width,
            Height=piece.Height,
            Background=new SolidColorBrush(Paint.ColorOf(piece.Fill,Color.FromRgb(0x0B,0x6B,0x50))),
            BorderBrush=new SolidColorBrush(Paint.ColorOf(piece.Border,Color.FromRgb(0x38,0xD7,0xA3))),
            BorderThickness=new Thickness(piece.BorderThickness),
            Opacity=Math.Clamp(piece.Opacity,0,1),
            ClipToBounds=true,
            Child=text
        };
        host.RenderTransformOrigin=new Point(0.5,0.5);
        Canvas.SetLeft(host,piece.X);Canvas.SetTop(host,piece.Y);
        return host;
    }

    void StartSwapTimer(double seconds,Action done)
    {
        _swapTimer?.Stop();
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(Math.Max(0.05,seconds))};
        _swapTimer=timer;
        timer.Tick+=(_,_)=>{timer.Stop();if(ReferenceEquals(_swapTimer,timer))_swapTimer=null;done();};
        timer.Start();
    }

    /// <summary>
    /// Plays a video file as this layout's hand-over. False when the layout has no usable clip, so
    /// the ordinary preset runs instead.
    /// </summary>
    bool TryPlayVideoSwap(RenderTargetBitmap outgoing,ScorebugLayout incoming)
    {
        var path=incoming.TransitionVideoPath;
        if(string.IsNullOrWhiteSpace(path)||!System.IO.File.Exists(path))return false;
        var extension=System.IO.Path.GetExtension(path).ToLowerInvariant();
        if(extension is not (".mp4" or ".m4v" or ".wmv" or ".avi" or ".mov" or ".mkv" or ".webm"))return false;
        FinishSwap();
        var width=Math.Max(1,_canvas.Width);
        var height=Math.Max(1,_canvas.Height);
        var stack=new Grid{Width=width,Height=height,IsHitTestVisible=false};
        var still=new Image{Source=outgoing,Width=width,Height=height,Stretch=Stretch.Fill};
        var video=new MediaElement{Width=width,Height=height,Stretch=Stretch.Fill,IsMuted=true,
            LoadedBehavior=MediaState.Play,UnloadedBehavior=MediaState.Manual};
        try{video.Source=new Uri(path);}catch{return false;}
        stack.Children.Add(still);
        stack.Children.Add(video);
        _stage.Children.Add(stack);
        _swapOverlay=stack;
        void Finish()
        {
            if(!ReferenceEquals(_swapOverlay,stack))return;
            try{video.Stop();video.Close();}catch{}
            _stage.Children.Remove(stack);
            _swapOverlay=null;
        }
        video.MediaEnded+=(_,_)=>Finish();
        // A clip that cannot open on this machine must not leave the old picture over the new layout.
        video.MediaFailed+=(_,_)=>Finish();
        StartSwapTimer(12,Finish);
        return true;
    }

    /// <summary>Stops a hand-over in progress and leaves the new layout alone on the stage.</summary>
    void FinishSwap()
    {
        _swapTimer?.Stop();
        _swapTimer=null;
        if(_swapOverlay is Panel panel)
            foreach(var media in panel.Children.OfType<MediaElement>())
            {
                try{media.Stop();media.Close();}catch{}
            }
        if(_swapOverlay is not null)
        {
            _stage.Children.Remove(_swapOverlay);
            _swapOverlay=null;
        }
        if(_swapPieces is not null)
        {
            foreach(var host in _swapPieces.Children.OfType<FrameworkElement>()) MotionPlayback.Stop(host);
            _stage.Children.Remove(_swapPieces);
            _swapPieces=null;
        }
        MotionPlayback.Stop(_canvas);
        _canvas.BeginAnimation(UIElement.OpacityProperty,null);
        _canvas.RenderTransform=Transform.Identity;
        _canvas.RenderTransformOrigin=new Point(0.5,0.5);
    }

    void ApplyAnimation(ScorebugElement item,FrameworkElement host)
    {
        var motion=item.MotionAnimation is { UseOnValueChange:false, IsEmpty:false } clip?clip.Summary:"";
        if(_animations.TryGetValue(item.Id,out var applied)&&applied.Animation==item.Animation&&Math.Abs(applied.Opacity-item.Opacity)<0.0001&&applied.Motion==motion&&Math.Abs(applied.Rotation-item.Rotation)<0.001)return;
        MotionPlayback.SetBaseRotation(host,item.Rotation);
        // A clip from the animation maker owns the element's motion instead of the two presets.
        if(item.MotionAnimation is { UseOnValueChange:false, IsEmpty:false } loop)
        {
            MotionPlayback.Stop(host);
            host.Opacity=Math.Clamp(item.Opacity,0,1);
            MotionPlayback.PlayClip(host,loop,item.Opacity,ambient:true);
            _animations[item.Id]=(item.Animation,item.Opacity,motion,item.Rotation);
            return;
        }
        MotionPlayback.Stop(host);
        host.BeginAnimation(UIElement.OpacityProperty,null);
        host.Opacity=Math.Clamp(item.Opacity,0,1);
        if(host.RenderTransform is TranslateTransform previous)
        {
            previous.BeginAnimation(TranslateTransform.XProperty,null);
            previous.BeginAnimation(TranslateTransform.YProperty,null);
            host.RenderTransform=Transform.Identity;
        }
        MotionPlayback.SetDesignRotation(host,item.Rotation);
        switch(item.Animation)
        {
            case ScorebugAnimation.Pulse:
                host.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(item.Opacity*0.68,item.Opacity,TimeSpan.FromSeconds(1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
                break;
            case ScorebugAnimation.Drift:
                // The old code animated X on the host instead of on its transform, so drift did
                // nothing at all. Animate the transform itself, on two out-of-phase axes, and keep
                // the design angle around the whole motion.
                var drift=new TranslateTransform();
                var group=new TransformGroup();
                group.Children.Add(drift);
                group.Children.Add(new RotateTransform(item.Rotation));
                host.RenderTransform=group;
                drift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,14,TimeSpan.FromSeconds(3.2)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
                drift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,7,TimeSpan.FromSeconds(2.1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
                break;
        }
        _animations[item.Id]=(item.Animation,item.Opacity,motion,item.Rotation);
    }

    /// <summary>True when any element prints the date or the time of day.</summary>
    bool UsesWallClock()
    {
        foreach(var item in Layout.Elements)
            if(item.Content.Contains("{{TIME}}",StringComparison.OrdinalIgnoreCase)||item.Content.Contains("{{DATE}}",StringComparison.OrdinalIgnoreCase))return true;
        return false;
    }

    public void Refresh()=>UpdateValues();

    /// <summary>
    /// Prints the newest values at once, skipping the switch delay. Used when the delay itself is
    /// changed, so the new timing starts from what is actually on screen.
    /// </summary>
    public void ApplyNow()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(ApplyNow);return;}
        foreach(var id in _pendingText.Keys.ToArray()) CancelPending(id);
        _flushDelay=true;
        try { UpdateValues(); } finally { _flushDelay=false; }
    }

    void UpdateClockValues()
    {
        var state=_stateProvider();
        state.ClockSeconds=_engine.State.ClockSeconds;
        state.ShotClockSeconds=_engine.State.ShotClockSeconds;
        state.ClockRunning=_engine.State.ClockRunning;
        state.ShotClockRunning=_engine.State.ShotClockRunning;
        state.PlayClockSeconds=_engine.State.PlayClockSeconds;
        state.PlayClockRunning=_engine.State.PlayClockRunning;
        state.TimeoutClockSeconds=_engine.State.TimeoutClockSeconds;
        state.TimeoutClockRunning=_engine.State.TimeoutClockRunning;
        UpdateValues();
    }

    void UpdateValues()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(UpdateValues);return;}
        var state=_stateProvider();
        var layout=Layout;
        foreach(var item in layout.Elements)
        {
            if(!_visuals.TryGetValue(item.Id,out var visual))continue;
            var visibility=item.Visible?Visibility.Visible:Visibility.Collapsed;
            if(visual.Host.Visibility!=visibility)visual.Host.Visibility=visibility;
            if(item.Animation!=ScorebugAnimation.Pulse&&item.MotionAnimation is not { UseOnValueChange:false }&&Math.Abs(visual.Host.Opacity-item.Opacity)>0.0001)visual.Host.Opacity=item.Opacity;
            if(visual.Host is Border hostBorder)
            {
                // Only touch the brush when the design or the palette changed; rebuilding brushes
                // every tick made the output stutter.
                var resolved=ScorebugRender.BackgroundSignature(item,state);
                if(!_backgrounds.TryGetValue(item.Id,out var applied)||applied!=resolved||_transparentStates.GetValueOrDefault(item.Id)!=item.TransparentBackground)
                {
                    hostBorder.Background=ScorebugRender.HostBackground(item,state);
                    _backgrounds[item.Id]=resolved;
                    _transparentStates[item.Id]=item.TransparentBackground;
                }
                // An outline is part of the artwork, so the audience sees exactly what the studio
                // designed; shape borders are drawn by the shape itself, its host stays clear.
                if(item.Kind!=ScorebugElementKind.Shape)
                {
                    var thickness=Math.Max(0,item.BorderThickness);
                    // The outline has its own opacity, so its signature includes it and the brush is
                    // built through the same paint path as everything else.
                    var resolvedBorder=$"{ScorebugRender.ResolveBorder(item,state)}|{item.BorderOpacity:0.###}";
                    if(!_borders.TryGetValue(item.Id,out var appliedBorder)||appliedBorder!=resolvedBorder)
                    {
                        hostBorder.BorderBrush=ScorebugRender.HostBorder(item,state);
                        _borders[item.Id]=resolvedBorder;
                    }
                    if(Math.Abs(hostBorder.BorderThickness.Left-thickness)>0.01)hostBorder.BorderThickness=new Thickness(thickness);
                }
            }
            if(visual.Content is ScorebugTextBlock block)
            {
                var text=ScoreboardTemplates.Bind(item.Content,state,item.PeriodFormat);
                block.ApplyStyle(item,state);
                ShowValue(item,block,text);
            }
            else if(visual.Content is ScorebugIconView icon)
            {
                icon.Update();
            }
            else if(visual.Content is not VideoSurface)
            {
                ScorebugRender.ApplyToContent(visual.Content,item,state);
            }
            ApplyAnimation(item,visual.Host);
        }
    }

    /// <summary>
    /// Prints a value, or holds it for the show's switch delay first. While a value waits, newer
    /// values replace the queued one, so the graphic always lands on the newest value in one step
    /// rather than replaying a burst of stale numbers.
    /// </summary>
    void ShowValue(ScorebugElement item,ScorebugTextBlock block,string text)
    {
        var delay=DisplayTiming.ValueSwitchDelay;
        if(_flushDelay||delay<=0||!double.IsFinite(delay))
        {
            CancelPending(item.Id);
            block.UpdateText(text,item.Transition,item.TransitionSeconds);
            _shownText[item.Id]=text;
            return;
        }
        var shown=_shownText.GetValueOrDefault(item.Id,"");
        if(text==shown){CancelPending(item.Id);return;}
        if(_pendingText.TryGetValue(item.Id,out var pending))
        {
            // Already waiting: keep the newest target, leave the timer alone.
            _pendingText[item.Id]=(text,pending.Timer);
            return;
        }
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(Math.Clamp(delay,0.05,10))};
        timer.Tick+=(_,_)=>{
            timer.Stop();
            var latest=_pendingText.TryGetValue(item.Id,out var queued)?queued.Target:text;
            _pendingText.Remove(item.Id);
            block.UpdateText(latest,item.Transition,item.TransitionSeconds);
            _shownText[item.Id]=latest;
        };
        _pendingText[item.Id]=(text,timer);
        timer.Start();
    }

    void CancelPending(string id)
    {
        if(_pendingText.Remove(id,out var pending))pending.Timer.Stop();
    }

    public void Dispose()
    {
        _wallClock.Stop();
        foreach(var (id,_) in _pendingText.ToArray()) CancelPending(id);
        _catalog.Changed-=Rebuild;
        RosterStore.Changed-=UpdateValues;
        if(_live)_engine.Changed-=UpdateValues;else _engine.Changed-=UpdateClockValues;
        foreach(var surface in _videoSurfaces.Values) surface.Dispose();
        _videoSurfaces.Clear();
        foreach(var page in _htmlSurfaces.Values) page.Dispose();
        _htmlSurfaces.Clear();
    }
}
