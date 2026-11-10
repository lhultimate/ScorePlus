using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    // The layouts above this feed, so a live-layout element cannot contain itself.
    readonly HashSet<string> _layoutChain;
    readonly Canvas _canvas = new() { Background=new SolidColorBrush(Color.FromRgb(9,16,24)), ClipToBounds=true };
    // Everything the view draws sits on this stage, so a layout hand-over can hold the outgoing
    // artwork on top of the incoming one while it slides or dissolves away.
    readonly Grid _stage = new() { ClipToBounds = true };
    // Which display this view is a feed of; null means the display being edited.
    readonly string? _displayId;
    string? _drawnLayoutId;
    ScorebugLayout? _drawnLayout;
    // What the last audience build drew, so a studio-only change cannot replay its entrance
    // animations (the possession arrow slide in particular) on air.
    string? _drawnSignature;
    FrameworkElement? _swapOverlay;
    // Pieces that exist only while a hand-over runs, and the timer that ends an exit or a scoped
    // arrival. Both belong to the swap, not to the layout, so FinishSwap takes them down together.
    Canvas? _swapPieces;
    DispatcherTimer? _swapTimer;
    // Temporary pieces from a value-change animation. They live above the layout for the length
    // of that flourish and are removed on the next value change, rebuild or stop.
    Canvas? _valuePieces;
    DispatcherTimer? _valueTimer;
    // The hand-over video is drawn through a video brush so its chroma shader reaches the frames.
    MediaPlayer? _swapPlayer;
    // A staged layout's hand-over video is opened before Update Output, so the first frame is
    // already available when the cut actually starts.
    MediaPlayer? _preparedTransitionPlayer;
    string _preparedTransitionPath = "";
    // Hosts whose position is travelling from an outgoing layout's spot to the incoming spot.
    readonly List<(ScorebugElement Item,FrameworkElement Host,Transform Original)> _swapMoves = [];
    readonly MediaPlayback? _playback;
    // Optional node-rule runtime. When present, this view draws the runtime's temporary copy of
    // the layout (matched actions only); the saved design is never changed.
    readonly AutomationRuntime? _automation;
    readonly bool _muteVideo;
    // A silent monitor of the audience picture (not a preview), so it follows the on-air cue.
    readonly bool _onAirVideo;
    // A nested feed must follow every game value, not just the clocks a preview copies.
    readonly bool _liveValues;
    readonly Viewbox _viewbox;
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
    bool _disposed;
    // Time-of-day text is not driven by the game clock, so it gets its own one-second pulse;
    // otherwise a layout with {{TIME}} freezes whenever no clock is running.
    readonly System.Windows.Threading.DispatcherTimer _wallClock = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <param name="publishedLayout">True for the audience output: it follows the layout that was
    /// actually published, not the one being designed in the preview.</param>
    /// <param name="publishedLayout">True for the audience output: it follows the layout that was
    /// actually published, not the one being designed in the preview.</param>
    /// <param name="layoutProvider">Draws exactly the layout this view belongs to (a specific
    /// display's on-air layout, or a window's staged one). Overrides <paramref name="publishedLayout"/>.</param>
    public ScorebugCanvasView(ScorebugCatalog catalog, GameEngine engine, Func<GameState>? stateProvider=null, MediaPlayback? playback=null, bool muteVideo=true, bool publishedLayout=false, Func<ScorebugLayout>? layoutProvider=null, bool onAirVideo=false, string? displayId=null, AutomationRuntime? automation=null,
        IReadOnlySet<string>? layoutChain=null, bool liveValues=false, bool fillBox=false)
    {
        _catalog=catalog;_engine=engine;_live=stateProvider is null;_automation=automation;
        _liveValues=liveValues;
        _layoutChain=layoutChain is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(layoutChain,StringComparer.OrdinalIgnoreCase);
        _displayId=displayId;
        _layoutProvider = layoutProvider ?? (publishedLayout ? () => _catalog.PublishedLayout : () => _catalog.Layout);
        _stateProvider=stateProvider??(()=>_engine.State);
        _playback=playback;_muteVideo=muteVideo;_onAirVideo=onAirVideo;
        _stage.Children.Add(_canvas);
        _viewbox=new Viewbox { Stretch=fillBox?Stretch.Fill:Stretch.Uniform, Child=_stage };
        Children.Add(_viewbox);
        _catalog.Changed+=Rebuild;
        if(_automation is not null)_automation.Changed+=Rebuild;
        // A roster edit changes what a specific-player binding prints, without touching the game.
        RosterStore.Changed+=UpdateValues;
        if(stateProvider is null||liveValues)_engine.Changed+=UpdateValues;else _engine.Changed+=UpdateClockValues;
        _wallClock.Tick+=(_,_)=>{if(UsesWallClock())UpdateValues();};
        _wallClock.Start();
        _layoutChain.Add(Layout.Id);
        Rebuild();
    }

    /// <summary>The artwork this view is drawing: the design being previewed, or the published one.</summary>
    public ScorebugLayout Layout => _automation?.Effective(_displayId,_layoutProvider()) ?? _layoutProvider();

    /// <summary>
    /// Opens this layout's hand-over video ahead of time. Preloading takes the media stack's startup
    /// cost out of the cut, and the player prepared here is consumed by the next video swap.
    /// </summary>
    public void PrepareTransition(ScorebugLayout layout)
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(()=>PrepareTransition(layout));return;}
        PrepareTransition(layout.TransitionVideoPath);
    }

    void PrepareTransition(string? path)
    {
        path=(path??"").Trim();
        if(string.Equals(path,_preparedTransitionPath,StringComparison.OrdinalIgnoreCase)&&_preparedTransitionPlayer is not null)return;
        ReleasePreparedTransition();
        if(path.Length==0||!System.IO.File.Exists(path))return;
        var extension=System.IO.Path.GetExtension(path).ToLowerInvariant();
        if(extension is not (".mp4" or ".m4v" or ".wmv" or ".avi" or ".mov" or ".mkv" or ".webm"))return;
        var player=new MediaPlayer{IsMuted=true,ScrubbingEnabled=true};
        _preparedTransitionPlayer=player;
        _preparedTransitionPath=path;
        player.MediaOpened+=(_,_)=>{
            if(!ReferenceEquals(_preparedTransitionPlayer,player))return;
            // Put the first frame at the head of the clip while the layout waits on stage. The
            // swap must not seek again: that seek is what kept the video cold until after the cut.
            try{player.Position=TimeSpan.Zero;player.Pause();}catch{}
        };
        player.MediaFailed+=(_,_)=>{
            if(ReferenceEquals(_preparedTransitionPlayer,player))ReleasePreparedTransition();
        };
        try{player.Open(new Uri(path));}catch{ReleasePreparedTransition();}
    }

    MediaPlayer? TakePreparedTransition(string path)
    {
        if(_preparedTransitionPlayer is null||!string.Equals(_preparedTransitionPath,path,StringComparison.OrdinalIgnoreCase))return null;
        var player=_preparedTransitionPlayer;
        _preparedTransitionPlayer=null;
        _preparedTransitionPath="";
        return player;
    }

    void ReleasePreparedTransition()
    {
        if(_preparedTransitionPlayer is not { } player)return;
        _preparedTransitionPlayer=null;
        _preparedTransitionPath="";
        try{player.Stop();player.Close();}catch{}
    }

    public void Rebuild()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(Rebuild);return;}
        if(_disposed)return;
        // A different layout on the same feed is a hand-over: keep what is currently on screen as a
        // still, so the new layout can be revealed through it rather than snapping into place. Any
        // other rebuild (an edit, a palette change) must never trigger the animation.
        var incoming = Layout;
        // Staging another layout or opening the studio raises catalog.Changed even when the artwork
        // this view draws has not moved. The audience views compare what they would draw and skip
        // the rebuild, so a preview-only change never replays an entrance animation on air.
        if (_displayId is not null)
        {
            var signature = JsonSerializer.Serialize(incoming);
            if (_drawnLayout is not null && string.Equals(_drawnSignature, signature, StringComparison.Ordinal))
                return;
            _drawnSignature = signature;
        }
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
        ClearValuePieces();
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
            else content=ScorebugRender.CreateContent(item,_stateProvider,_playback,_muteVideo,showVideoPlaceholder:false,onAirVideo:_onAirVideo,catalog:_catalog,engine:_engine,displayId:_displayId,layoutChain:_layoutChain);
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
            // Text rotates its own glyphs; a border or plate is not turned with it.
            MotionPlayback.SetDesignRotation(host,item.Kind==ScorebugElementKind.Text?0:item.Rotation);
            Canvas.SetLeft(host,item.X);Canvas.SetTop(host,item.Y);Panel.SetZIndex(host,zIndex++);
            _visuals[item.Id]=(host,content);
            // A rebuilt view starts from what it draws right now, so a delay never blanks the graphic.
            if(content is ScorebugTextBlock shown)
            {
                _shownText[item.Id]=shown.Text;
                shown.StateProvider=_stateProvider;
                var elementId=item.Id;
                shown.ValueMotionPlayed=(clip,before,after)=>PlayValuePieces(elementId,clip,before,after);
            }
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
        foreach(var (id,prior) in previousVisuals)
            if((!_visuals.TryGetValue(id,out var current)||!ReferenceEquals(current.Content,prior.Content))
                &&prior.Content is ScorebugLayoutFeed oldFeed)
                oldFeed.Dispose();
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
        // Position-morph: an item that exists on both layouts (a duplicate keeps its element names)
        // travels from its old spot to its new spot instead of every changed piece entering from
        // the edge. This is the hand-over for moving artwork between two similar layouts.
        if(incoming.Transition==LayoutTransition.Move&&outgoingLayout is not null)
        {
            PlayMoveSwap(outgoingLayout,incoming);
            return;
        }
        // The smart mode keeps every unchanged item exactly where it is and animates only the
        // items that were added, removed or edited between the two layouts.
        if(incoming.OnlyAnimateChanges&&outgoingLayout is not null)
        {
            PlaySmartSwap(outgoing,outgoingLayout,incoming);
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
    /// Animates a preset transition item by item. Unchanged artwork is left alone: adding a stat
    /// window therefore brings in only that window, while removing one lets only the removed
    /// artwork leave. The outgoing still is cropped to the changed items so unchanged pieces are
    /// never painted twice or covered by a moving background.
    /// </summary>
    void PlaySmartSwap(RenderTargetBitmap outgoing,ScorebugLayout outgoingLayout,ScorebugLayout incoming)
    {
        var changes=ChangedElements(outgoingLayout,incoming);
        var transition=incoming.Transition;
        var seconds=Math.Clamp(incoming.TransitionSeconds,0,5);
        FinishSwap();
        if(transition==LayoutTransition.Cut||seconds<=0)return;
        if(changes.Incoming.Count==0&&changes.Outgoing.Count==0)return;
        var width=Math.Max(1,_canvas.Width);
        var height=Math.Max(1,_canvas.Height);
        var duration=TimeSpan.FromSeconds(seconds);
        var easing=new CubicEase{EasingMode=EasingMode.EaseInOut};

        // The changed parts of the outgoing picture, with each crop at its old position.
        var overlay=new Canvas{Width=width,Height=height,IsHitTestVisible=false};
        var pixelWidth=Math.Max(1,outgoing.PixelWidth);
        var pixelHeight=Math.Max(1,outgoing.PixelHeight);
        foreach(var item in outgoingLayout.Elements.Where(element=>element.Visible&&changes.Outgoing.Contains(element.Id)))
        {
            var x=(int)Math.Clamp(item.X,0,Math.Max(0,pixelWidth-1));
            var y=(int)Math.Clamp(item.Y,0,Math.Max(0,pixelHeight-1));
            var w=(int)Math.Clamp(item.Width,1,pixelWidth-x);
            var h=(int)Math.Clamp(item.Height,1,pixelHeight-y);
            if(w<1||h<1)continue;
            var crop=new CroppedBitmap(outgoing,new Int32Rect(x,y,w,h));
            crop.Freeze();
            var image=new Image{Source=crop,Width=w,Height=h,Stretch=Stretch.Fill,IsHitTestVisible=false,Opacity=Math.Clamp(item.Opacity,0,1)};
            Canvas.SetLeft(image,item.X);Canvas.SetTop(image,item.Y);
            overlay.Children.Add(image);
        }
        var hasOutgoing=overlay.Children.Count>0;
        if(hasOutgoing){_stage.Children.Add(overlay);_swapOverlay=overlay;}

        var moving=new List<(ScorebugElement Item,FrameworkElement Host,Transform Original,double OriginalOpacity)>();
        foreach(var item in incoming.Elements.Where(element=>changes.Incoming.Contains(element.Id)))
        {
            if(!_visuals.TryGetValue(item.Id,out var visual))continue;
            var host=visual.Host;
            var original=host.RenderTransform??Transform.Identity;
            var originalOpacity=host.Opacity;
            moving.Add((item,host,original,originalOpacity));
            if(transition==LayoutTransition.Fade)
            {
                // A fade keeps the incoming change invisible until its turn, then brings it up with
                // the same clock and easing as the outgoing crop, rather than popping in at the end.
                host.Opacity=0;
                host.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,originalOpacity,duration){EasingFunction=easing});
            }
            else if(transition is LayoutTransition.SlideLeft or LayoutTransition.SlideRight or LayoutTransition.SlideUp or LayoutTransition.SlideDown)
            {
                var fromX=transition==LayoutTransition.SlideLeft?width:transition==LayoutTransition.SlideRight?-width:0;
                var fromY=transition==LayoutTransition.SlideUp?height:transition==LayoutTransition.SlideDown?-height:0;
                AttachTranslate(host,original,fromX,fromY,0,0,duration,easing);
            }
            else if(transition==LayoutTransition.Zoom)
            {
                AttachScale(host,original,0.92,1,duration,easing);
            }
        }

        if(hasOutgoing)
        {
            if(transition is LayoutTransition.SlideLeft or LayoutTransition.SlideRight or LayoutTransition.SlideUp or LayoutTransition.SlideDown)
            {
                var leaving=new TranslateTransform();
                overlay.RenderTransform=leaving;
                var toX=transition==LayoutTransition.SlideLeft?-width:transition==LayoutTransition.SlideRight?width:0;
                var toY=transition==LayoutTransition.SlideUp?-height:transition==LayoutTransition.SlideDown?height:0;
                leaving.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,toX,duration){EasingFunction=easing});
                leaving.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,toY,duration){EasingFunction=easing});
            }
            else if(transition==LayoutTransition.Zoom)
            {
                overlay.RenderTransformOrigin=new Point(0.5,0.5);
                var scale=new ScaleTransform(1,1);
                overlay.RenderTransform=scale;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,1.14,duration){EasingFunction=easing});
                scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,1.14,duration){EasingFunction=easing});
            }
            overlay.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,0,duration){EasingFunction=easing});
        }

        if(moving.Count==0&&!hasOutgoing)return;
        StartSwapTimer(seconds,() =>
        {
            if(!ReferenceEquals(_swapOverlay,overlay)&&hasOutgoing)return;
            if(hasOutgoing)
            {
                _stage.Children.Remove(overlay);
                _swapOverlay=null;
            }
            foreach(var (item,host,original,originalOpacity) in moving)
            {
                host.BeginAnimation(UIElement.OpacityProperty,null);
                host.Opacity=originalOpacity;
                host.RenderTransform=original;
                // The fade replaced any ambient pulse; let the normal painter put it back.
                _animations.Remove(item.Id);
                ApplyAnimation(item,host);
            }
        });
    }

    /// <summary>
    /// Slides every item that exists on both layouts from its old position to its new one. Items
    /// are paired by id, then by link, then by name: a duplicated layout gets fresh element ids but
    /// keeps the names, so moving a plate on the copy reads as that plate travelling.
    /// </summary>
    void PlayMoveSwap(ScorebugLayout outgoingLayout,ScorebugLayout incoming)
    {
        FinishSwap();
        var seconds=Math.Clamp(incoming.TransitionSeconds,0,5);
        if(seconds<=0)return;
        var matches=MatchElements(outgoingLayout,incoming);
        if(matches.Count==0)return;
        var duration=TimeSpan.FromSeconds(seconds);
        var easing=new CubicEase{EasingMode=EasingMode.EaseInOut};
        foreach(var item in incoming.Elements)
        {
            if(!item.Visible||!_visuals.TryGetValue(item.Id,out var visual))continue;
            if(!matches.TryGetValue(item.Id,out var previous)||!previous.Visible)continue;
            var dx=previous.X-item.X; var dy=previous.Y-item.Y;
            if(Math.Abs(dx)<0.5&&Math.Abs(dy)<0.5)continue;
            var original=visual.Host.RenderTransform??Transform.Identity;
            AttachTranslate(visual.Host,original,dx,dy,0,0,duration,easing);
            _swapMoves.Add((item,visual.Host,original));
        }
        if(_swapMoves.Count==0)return;
        StartSwapTimer(seconds,RestoreMovePieces);
    }

    /// <summary>
    /// Pairs the items of two layouts one-for-one. Id is the direct match; LinkId survives copies
    /// that keep a linked part; the element name is what a normal layout duplicate retains.
    /// </summary>
    static Dictionary<string,ScorebugElement> MatchElements(ScorebugLayout outgoing,ScorebugLayout incoming)
    {
        var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches=new Dictionary<string,ScorebugElement>(StringComparer.OrdinalIgnoreCase);
        void Pair(ScorebugElement item,Func<ScorebugElement,bool> match)
        {
            if(matches.ContainsKey(item.Id))return;
            var previous=outgoing.Elements.FirstOrDefault(candidate=>!used.Contains(candidate.Id)&&match(candidate));
            if(previous is null)return;
            used.Add(previous.Id);
            matches[item.Id]=previous;
        }
        foreach(var item in incoming.Elements)
            Pair(item,candidate=>string.Equals(candidate.Id,item.Id,StringComparison.OrdinalIgnoreCase));
        foreach(var item in incoming.Elements.Where(item=>!string.IsNullOrWhiteSpace(item.LinkId)))
            Pair(item,candidate=>!string.IsNullOrWhiteSpace(candidate.LinkId)
                &&string.Equals(candidate.LinkId,item.LinkId,StringComparison.OrdinalIgnoreCase));
        foreach(var item in incoming.Elements.Where(item=>!string.IsNullOrWhiteSpace(item.Name)))
            Pair(item,candidate=>!string.IsNullOrWhiteSpace(candidate.Name)
                &&string.Equals(candidate.Name,item.Name,StringComparison.OrdinalIgnoreCase));
        return matches;
    }

    void RestoreMovePieces()
    {
        foreach(var (_,host,original) in _swapMoves)host.RenderTransform=original;
        _swapMoves.Clear();
    }

    /// <summary>Wraps an element's own transform with a translation that settles at zero.</summary>
    static void AttachTranslate(FrameworkElement host,Transform original,double fromX,double fromY,double toX,double toY,TimeSpan duration,IEasingFunction easing)
    {
        var translate=new TranslateTransform(fromX,fromY);
        var group=new TransformGroup();
        if(original is not null&&original!=Transform.Identity)group.Children.Add(original);
        group.Children.Add(translate);
        host.RenderTransform=group;
        translate.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(fromX,toX,duration){EasingFunction=easing});
        translate.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(fromY,toY,duration){EasingFunction=easing});
    }

    /// <summary>Wraps an element's own transform with a scale that settles at one.</summary>
    static void AttachScale(FrameworkElement host,Transform original,double from,double to,TimeSpan duration,IEasingFunction easing)
    {
        host.RenderTransformOrigin=new Point(0.5,0.5);
        var scale=new ScaleTransform(from,from);
        var group=new TransformGroup();
        if(original is not null&&original!=Transform.Identity)group.Children.Add(original);
        group.Children.Add(scale);
        host.RenderTransform=group;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(from,to,duration){EasingFunction=easing});
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(from,to,duration){EasingFunction=easing});
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
        // Smart mode applies to custom clips too: the exit and arrival only run on the items that
        // differ between the two layouts, not on the whole canvas.
        var smart=incoming.OnlyAnimateChanges&&outgoingLayout is not null;
        var changes=smart?ChangedElements(outgoingLayout!,incoming):null;
        if(smart&&changes!.Incoming.Count==0&&changes.Outgoing.Count==0)return;
        // Items the operator unchecked in the transition maker stay put, and any key scoped to a
        // piece means the arrival runs on each item's own box rather than on the whole canvas.
        var stationary=incoming.Handover?.Where(entry=>!entry.Arrives).Select(entry=>entry.ElementId).ToHashSet(StringComparer.OrdinalIgnoreCase)??[];
        var pieceKeys=clip.Phases.Any(phase=>phase.Tracks.Any(track=>!string.IsNullOrWhiteSpace(track.PieceId))
            ||phase.Colors.Any(track=>!string.IsNullOrWhiteSpace(track.PieceId)));
        var exitScoped=outgoingLayout is not null&&exit is not null
            &&(exit.Tracks.Any(track=>!string.IsNullOrWhiteSpace(track.PieceId))
               ||exit.Colors.Any(track=>!string.IsNullOrWhiteSpace(track.PieceId)));
        var useCrops=exitScoped||smart;

        // The outgoing frame: the whole still, or one cropped still per item when the exit itself is
        // keyed piece by piece. The crops are exactly what was on screen, so a piece moves on its own.
        FrameworkElement outgoingHost;
        // One step per item: the timeline below decides the order they run in.
        var exitSteps=new List<(string Id,Action Play)>();
        var enterSteps=new List<(string Id,Action Play)>();
        if(useCrops)
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
            foreach(var item in outgoingLayout!.Elements.Where(element=>element.Visible&&(!smart||changes!.Outgoing.Contains(element.Id))))
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
                var step=image;var id=PieceIdFor(item);
                exitSteps.Add((id,()=>MotionPlayback.PlayPhase(step,exit,Math.Clamp(item.Opacity,0,1),pieceId:id)));
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
            }
            _stage.Children.Add(pieces);
            _swapPieces=pieces;
        }
        // Extra pieces are part of the same timeline as the layout: they take their slot in the
        // exit and the arrival instead of all appearing on the first beat.
        if(exit is {Enabled:true,IsEmpty:false})
            foreach(var (host,piece) in temporary)
                exitSteps.Add((piece.Id,()=>MotionPlayback.PlayPhase(host,exit,Math.Clamp(piece.Opacity,0,1),pieceId:piece.Id)));
        PlayStaggered(exitSteps,exit?.Seconds??0,clip);

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
            var perItem=smart||pieceKeys||stationary.Count>0||clip.Pieces.Count>0;
            if(!perItem)
            {
                MotionPlayback.PlayPhase(_canvas,enter,1,FinishSwap);
                return;
            }
            foreach(var item in incoming.Elements)
            {
                if(smart&&!changes!.Incoming.Contains(item.Id))continue;
                if(stationary.Contains(item.Id))continue;
                if(!_visuals.TryGetValue(item.Id,out var visual))continue;
                var host=visual.Host;var id=item.Id;var opacity=Math.Clamp(item.Opacity,0,1);
                enterSteps.Add((id,()=>MotionPlayback.PlayPhase(host,enter,opacity,pieceId:id)));
            }
            foreach(var (host,piece) in temporary)
                enterSteps.Add((piece.Id,()=>MotionPlayback.PlayPhase(host,enter,Math.Clamp(piece.Opacity,0,1),pieceId:piece.Id)));
            PlayStaggered(enterSteps,enter.Seconds,clip);
            // Scoped pieces finish individually; one timer ends the hand-over once the leg is over,
            // including the time the timeline spends spreading its items out.
            StartSwapTimer(enter.Seconds+StaggerFor(enterSteps,clip),FinishSwap);
        }
        if(exit is {Enabled:true,IsEmpty:false}&&exit.Seconds>0.001) StartSwapTimer(exit.Seconds+StaggerFor(exitSteps,clip),Arrive);
        else Arrive();
    }

    /// <summary>Which items are new or edited between two layouts, matched by exact visual state.</summary>
    sealed record LayoutChanges(HashSet<string> Incoming, HashSet<string> Outgoing);

    /// <summary>
    /// Compares the two layouts by what each item draws, independent of its id and name. Matching
    /// equal items as unchanged is what lets a duplicated layout add one stat window without every
    /// other element sliding. Duplicate identical items are paired one-for-one.
    /// </summary>
    static LayoutChanges ChangedElements(ScorebugLayout outgoing,ScorebugLayout incoming)
    {
        var incomingChanged=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outgoingChanged=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(outgoing.Width!=incoming.Width||outgoing.Height!=incoming.Height)
        {
            foreach(var item in incoming.Elements)incomingChanged.Add(item.Id);
            foreach(var item in outgoing.Elements)outgoingChanged.Add(item.Id);
            return new(incomingChanged,outgoingChanged);
        }
        var pool=new Dictionary<string,List<ScorebugElement>>(StringComparer.Ordinal);
        foreach(var item in outgoing.Elements)
        {
            var signature=VisualSignature(item);
            if(!pool.TryGetValue(signature,out var list))pool[signature]=list=[];
            list.Add(item);
        }
        foreach(var item in incoming.Elements)
        {
            var signature=VisualSignature(item);
            if(pool.TryGetValue(signature,out var list)&&list.Count>0)list.RemoveAt(list.Count-1);
            else incomingChanged.Add(item.Id);
        }
        foreach(var remaining in pool.Values.SelectMany(list=>list))outgoingChanged.Add(remaining.Id);
        return new(incomingChanged,outgoingChanged);
    }

    /// <summary>The visual properties of an item, with identity and labels removed.</summary>
    static string VisualSignature(ScorebugElement item)
    {
        var node=JsonSerializer.SerializeToNode(item)?.AsObject();
        if(node is null)return string.Empty;
        node.Remove(nameof(ScorebugElement.Id));
        node.Remove(nameof(ScorebugElement.Name));
        return node.ToJsonString();
    }

    FrameworkElement BuildTemporaryPiece(MotionPiece piece)
    {
        var host=new Border
        {
            Width=piece.Width,
            Height=piece.Height,
            Background=piece.Kind==MotionPieceKind.Shape?Brushes.Transparent:new SolidColorBrush(Paint.ColorOf(piece.Fill,Color.FromRgb(0x0B,0x6B,0x50))),
            BorderBrush=new SolidColorBrush(Paint.ColorOf(piece.Border,Color.FromRgb(0x38,0xD7,0xA3))),
            BorderThickness=new Thickness(piece.BorderThickness),
            Opacity=Math.Clamp(piece.Opacity,0,1),
            ClipToBounds=true,
            Child=ScorebugRender.CreateMotionPiece(piece,_stateProvider,_catalog,_engine)
        };
        host.RenderTransformOrigin=new Point(0.5,0.5);
        Canvas.SetLeft(host,piece.X);Canvas.SetTop(host,piece.Y);
        return host;
    }

    /// <summary>
    /// Runs each item's own part of a leg in timeline order: the top item starts at once and every
    /// later item follows, sharing half the leg between them, so the order is visible but the leg
    /// still finishes on time. A clip with no order keeps its single, simultaneous beat.
    /// </summary>
    void PlayStaggered(List<(string Id,Action Play)> steps,double leg,MotionClip clip)
    {
        if(steps.Count==0)return;
        var ranks=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        if(clip.PieceOrder is not null)
            foreach(var id in clip.PieceOrder)
                if(!ranks.ContainsKey(id))ranks[id]=ranks.Count;
        var ordered=steps
            .Select((step,index)=>(step.Id,step.Play,index,Rank:ranks.TryGetValue(step.Id,out var rank)?rank:int.MaxValue))
            .OrderBy(step=>step.Rank).ThenBy(step=>step.index)
            .ToList();
        var gap=StaggerFor(steps,clip)/(ordered.Count>1?ordered.Count-1:1);
        for(var index=0;index<ordered.Count;index++)
        {
            var play=ordered[index].Play;
            if(index==0||gap<=0.001){play();continue;}
            var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(gap*index)};
            timer.Tick+=(_,_)=>{timer.Stop();play();};
            timer.Start();
        }
    }

    /// <summary>How long the timeline spreads a leg out, or zero when the clip has no order.</summary>
    static double StaggerFor(List<(string Id,Action Play)> steps,MotionClip clip)
    {
        if(steps.Count<2||clip.PieceOrder is null||clip.PieceOrder.Count<2)return 0;
        var ordered=steps.Count(step=>clip.PieceOrder.Contains(step.Id,StringComparer.OrdinalIgnoreCase));
        return ordered<2?0:Math.Clamp(Math.Clamp(clip.ItemStagger,0,1.5)*(ordered-1),0.01,2.5);
    }

    /// <summary>
    /// Value animations can carry temporary pieces of their own. They sit above the layout, are
    /// filtered by the score that was pressed, and disappear when the clip's take ends.
    /// </summary>
    void PlayValuePieces(string elementId,MotionClip clip,string before,string after)
    {
        var state=_stateProvider();
        var amount=state.LastScoreAmount;
        if(amount==0
            &&double.TryParse(before.Trim(),out var oldValue)
            &&double.TryParse(after.Trim(),out var newValue))
            amount=(int)Math.Round(newValue-oldValue);
        var pieces=clip.PiecesForTrigger(amount,state.LastScoreHome);
        if(pieces.Count==0)return;
        var phase=clip.ValuePhaseFor(amount,state.LastScoreHome);
        if(phase is null||!phase.Enabled)return;
        ClearValuePieces();
        var canvas=new Canvas{Width=Math.Max(1,_canvas.Width),Height=Math.Max(1,_canvas.Height),IsHitTestVisible=false};
        Panel.SetZIndex(canvas,10000);
        var steps=new List<(string Id,Action Play)>();
        foreach(var piece in pieces)
        {
            var host=BuildTemporaryPiece(piece);
            canvas.Children.Add(host);
            var pieceId=piece.Id;
            var opacity=Math.Clamp(piece.Opacity,0,1);
            steps.Add((pieceId,()=>MotionPlayback.PlayPhase(host,phase,opacity,pieceId:pieceId)));
        }
        PlayStaggered(steps,phase.Seconds,clip);
        _stage.Children.Add(canvas);
        _valuePieces=canvas;
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(Math.Max(0.05,phase.Seconds+StaggerFor(steps,clip)))};
        _valueTimer=timer;
        timer.Tick+=(_,_) => { timer.Stop(); if(ReferenceEquals(_valueTimer,timer)) ClearValuePieces(); };
        timer.Start();
    }

    void ClearValuePieces()
    {
        _valueTimer?.Stop();
        _valueTimer=null;
        if(_valuePieces is null)return;
        foreach(var host in _valuePieces.Children.OfType<FrameworkElement>()) MotionPlayback.Stop(host);
        _stage.Children.Remove(_valuePieces);
        _valuePieces=null;
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
        var prepared=TakePreparedTransition(path);
        var player=prepared??new MediaPlayer{IsMuted=true,ScrubbingEnabled=false};
        var drawing=new VideoDrawing{Player=player,Rect=new Rect(0,0,1,1)};
        var brush=new DrawingBrush(drawing){Stretch=Stretch.Fill};
        var video=new Rectangle{Width=width,Height=height,Fill=brush,IsHitTestVisible=false};
        // The hand-over video can be shot against a green screen just like a camera or a replay.
        // The shader is the same one used by video elements, so the key colour reaches the pixels.
        var keyed=TryParseColor(incoming.TransitionVideoChromaKeyColor,out var keyColour);
        if(keyed) video.Effect=new ChromaKeyEffect{KeyColor=keyColour,Tolerance=incoming.TransitionVideoChromaKeyTolerance};
        // The incoming layout is already underneath. Hold the outgoing still over it until the
        // video's first frame is ready, then until the operator's chosen cut beat. A keyed clip
        // needs this too: otherwise its transparent pixels reveal the new layout while the media
        // pipeline is still opening, which is the three-second early cut.
        var cutSeconds=Math.Clamp(incoming.TransitionVideoCutSeconds,0,30);
        stack.Children.Add(still);
        stack.Children.Add(video);
        _stage.Children.Add(stack);
        _swapOverlay=stack;
        _swapPlayer=player;
        DispatcherTimer? cutTimer=null;
        DispatcherTimer? paintTimer=null;
        EventHandler? paintHandler=null;
        var cutDone=false;
        void StopPaintWait()
        {
            if(paintHandler is not null){CompositionTarget.Rendering-=paintHandler;paintHandler=null;}
            if(paintTimer is not null){paintTimer.Stop();paintTimer=null;}
        }
        void Cut()
        {
            if(cutDone)return;
            cutDone=true;
            cutTimer?.Stop();
            cutTimer=null;
            if(stack.Children.Contains(still))stack.Children.Remove(still);
        }
        void StartCut()
        {
            if(cutDone)return;
            StopPaintWait();
            // The fixed fallback that removes a stuck clip must not fire before a deliberately
            // late cut. Honor the chosen beat and the clip's own duration, with a ceiling that
            // still clears a hung media pipeline.
            var guardSeconds=cutSeconds;
            if(player.NaturalDuration.HasTimeSpan)
                guardSeconds=Math.Max(guardSeconds,player.NaturalDuration.TimeSpan.TotalSeconds);
            StartSwapTimer(Math.Clamp(guardSeconds+1,12,65),Finish);
            if(cutSeconds<=0.001){Cut();return;}
            cutTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(cutSeconds)};
            cutTimer.Tick+=(_,_) => {
                cutTimer?.Stop();
                cutTimer=null;
                if(ReferenceEquals(_swapOverlay,stack))Cut();
            };
            cutTimer.Start();
        }
        void WaitForFirstFrames()
        {
            if(cutDone||paintHandler is not null||!ReferenceEquals(_swapOverlay,stack))return;
            var frames=0;
            paintHandler=(_,_)=>{
                if(cutDone||!ReferenceEquals(_swapOverlay,stack)){StopPaintWait();return;}
                // Two composed frames prove the video brush is painting, not merely that the
                // media clock has opened. MediaOpened happens before the first visible frame.
                if(++frames<2||player.Position<=TimeSpan.Zero)return;
                StopPaintWait();
                StartCut();
            };
            CompositionTarget.Rendering+=paintHandler;
            // Never let a decoder that opens but never paints pin the old scoreboard forever.
            paintTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
            paintTimer.Tick+=(_,_)=>{
                StopPaintWait();
                if(!cutDone&&ReferenceEquals(_swapOverlay,stack))StartCut();
            };
            paintTimer.Start();
        }
        void BeginPlayback()
        {
            if(cutDone)return;
            try{player.Play();}catch{Finish();return;}
            WaitForFirstFrames();
        }
        void Finish()
        {
            if(!ReferenceEquals(_swapOverlay,stack))return;
            cutDone=true;
            StopPaintWait();
            cutTimer?.Stop();
            cutTimer=null;
            cutDone=true;
            try{player.Stop();player.Close();}catch{}
            if(ReferenceEquals(_swapPlayer,player))_swapPlayer=null;
            _stage.Children.Remove(stack);
            _swapOverlay=null;
        }
        player.MediaEnded+=(_,_)=>Finish();
        // A clip that cannot open on this machine must not leave the old picture over the new layout.
        player.MediaFailed+=(_,_)=>Finish();
        // A video that never opens must not leave the old still over the new layout. This is
        // replaced by the clip-aware guard once playback opens.
        StartSwapTimer(30,Finish);
        if(prepared is null)
        {
            try{player.Open(new Uri(path));}
            catch{Finish();return true;}
        }
        else player.ScrubbingEnabled=false;
        // A prepared player is already parked on its first frame at position zero. Play it without
        // seeking again, then hold the outgoing still until composed video frames actually land.
        BeginPlayback();
        return true;
    }

    /// <summary>Stops a hand-over in progress and leaves the new layout alone on the stage.</summary>
    void FinishSwap()
    {
        _swapTimer?.Stop();
        _swapTimer=null;
        RestoreMovePieces();
        if(_swapPlayer is { } player)
        {
            try{player.Stop();player.Close();}catch{}
            _swapPlayer=null;
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
        var hostRotation=item.Kind==ScorebugElementKind.Text?0:item.Rotation;
        MotionPlayback.SetBaseRotation(host,hostRotation);
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
        MotionPlayback.SetDesignRotation(host,hostRotation);
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
                if(Math.Abs(hostRotation)>0.001)
                {
                    var group=new TransformGroup();
                    group.Children.Add(drift);
                    group.Children.Add(new RotateTransform(hostRotation));
                    host.RenderTransform=group;
                }
                else host.RenderTransform=drift;
                drift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,14,TimeSpan.FromSeconds(3.2)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
                drift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,7,TimeSpan.FromSeconds(2.1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
                break;
        }
        _animations[item.Id]=(item.Animation,item.Opacity,motion,item.Rotation);
    }

    static bool TryParseColor(string? text,out Color colour)
    {
        colour=default;
        var value=(text??"").Trim();
        if(value.Length<4||value[0]!='#')return false;
        try{return ColorConverter.ConvertFromString(value) is Color parsed&&(colour=parsed).A>0;}catch{return false;}
    }

    /// <summary>True when any element prints the date or the time of day.</summary>
    bool UsesWallClock()
    {
        foreach(var item in Layout.Elements)
            if(item.Content.Contains("{{TIME}}",StringComparison.OrdinalIgnoreCase)||item.Content.Contains("{{DATE}}",StringComparison.OrdinalIgnoreCase))return true;
        return false;
    }

    public void Refresh()=>UpdateValues();

    /// <summary>Switches a nested feed between filling its box and keeping the source's shape.</summary>
    public void SetFill(bool fill)=>_viewbox.Stretch=fill?Stretch.Fill:Stretch.Uniform;

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
        if(_disposed)return;
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
            else if(visual.Content is VideoSurface videoSurface)
            {
                // A video keeps its decoder and playhead; only its box, tag and chroma key change.
                videoSurface.SetElement(item);
            }
            else
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
        if(_disposed)return;
        _disposed=true;
        ClearValuePieces();
        _wallClock.Stop();
        foreach(var (id,_) in _pendingText.ToArray()) CancelPending(id);
        _catalog.Changed-=Rebuild;
        if(_automation is not null)_automation.Changed-=Rebuild;
        RosterStore.Changed-=UpdateValues;
        if(_live||_liveValues)_engine.Changed-=UpdateValues;else _engine.Changed-=UpdateClockValues;
        foreach(var visual in _visuals.Values)
            if(visual.Content is ScorebugLayoutFeed feed)feed.Dispose();
        foreach(var surface in _videoSurfaces.Values) surface.Dispose();
        _videoSurfaces.Clear();
        foreach(var page in _htmlSurfaces.Values) page.Dispose();
        _htmlSurfaces.Clear();
        ReleasePreparedTransition();
    }
}
