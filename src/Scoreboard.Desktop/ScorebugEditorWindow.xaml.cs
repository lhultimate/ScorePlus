using System.Windows;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;
public partial class ScorebugEditorWindow : Window
{
    readonly ScorebugCatalog _catalog;
    readonly GameEngine _engine;
    readonly MediaPlayback? _playback;
    readonly string _theme;
    // Keyed by element id: a rebuild reuses the surface so a playing clip is never restarted.
    readonly Dictionary<string, VideoSurface> _videoSurfaces = [];
    // Property edits save through the catalog; the resulting Changed event must not rebuild the
    // whole canvas (and the video surfaces with it). RefreshAllVisuals repaints in place instead.
    bool _skipRebuild;
    readonly System.Windows.Threading.DispatcherTimer _wallClock = new() { Interval=TimeSpan.FromSeconds(1) };
    bool _canvasLoading;
    bool _layoutLoading;
    // Layout pictures for the strip above the canvas, keyed by layout: the signature is the
    // artwork they were drawn from, so a thumbnail is only re-rendered when that artwork changed.
    readonly Dictionary<string,(string Signature,ImageSource Image)> _layoutThumbs = [];
    bool _displayLoading;
    bool _linkLoading;
    // Set while the player-data pickers are being filled for an element, so the sync is not
    // mistaken for the operator choosing a different player or value.
    bool _bindingLoading;

    /// <summary>The side, player and stat an element prints. Null player = follow the desk's selection.</summary>
    sealed record PlayerSideOption(bool Home, string Label) { public override string ToString() => Label; }
    sealed record PlayerChoice(string? Id, string Label) { public override string ToString() => Label; }
    sealed record PlayerFieldOption(string Field, string Label) { public override string ToString() => Label; }

    static readonly Regex PlayerToken = new(@"\{\{PLAYER:(?<side>HOME|AWAY):(?<field>[^}]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // A marker in the Add sport data menu: not a token, it means "add the bases artwork".
    const string BasesMarker = "!bases";
    // Speakers are listed once per studio window; asking WASAPI per keystroke would be wasteful.
    readonly IReadOnlyList<AudioOption> _audioOptions;
    readonly System.Windows.Threading.DispatcherTimer _propertySave = new() { Interval=TimeSpan.FromMilliseconds(350) };
    readonly Dictionary<string,(Border Host, FrameworkElement Content, Border Handle, ScorebugElementKind Kind)> _visuals = [];
    readonly Dictionary<string,(ScorebugElementKind Kind, ScorebugShape Shape, string? Image, ScorebugAnimation Animation, double Opacity)> _applied = [];
    bool _loading;
    bool _syncingLayers;
    // Everything selected, in the order it was picked; the primary is the one the property panel
    // edits and the one whose resize handle is shown.
    readonly List<string> _selectedIds = [];
    string? _selectedId;
    ScorebugElement? _dragElement;
    // Where each selected element started, so dragging one object carries the whole selection.
    readonly Dictionary<string,(double X, double Y)> _dragOrigins = [];
    Point _dragStartPoint;
    bool _resizing;
    double _resizeStartX, _resizeStartY, _resizeStartWidth, _resizeStartHeight, _resizeStartFontSize;
    Point _listStart;

    sealed record PeriodOption(string? Key, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record ColorSourceOption(ScorebugColorSource Source, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record LogoOption(ScorebugLogoSide Side, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>A speaker offered in the element's audio picker.</summary>
    sealed record AudioOption(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record DisplayOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record LinkDisplayOption(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record LinkLayoutOption(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>How a layout change lands on air, offered on the display bar.</summary>
    sealed record LayoutTransitionOption(LayoutTransition Transition, string Label)
    {
        public override string ToString() => Label;
    }

    static readonly LayoutTransitionOption[] LayoutTransitions =
    [
        new(LayoutTransition.Cut, "Cut — instant"),
        new(LayoutTransition.Fade, "Dissolve"),
        new(LayoutTransition.SlideLeft, "Swing left"),
        new(LayoutTransition.SlideRight, "Swing right"),
        new(LayoutTransition.SlideUp, "Slide up"),
        new(LayoutTransition.SlideDown, "Slide down"),
        new(LayoutTransition.Zoom, "Zoom")
    ];

    // Per-element quarter style. "Follow game default" keeps the old behaviour for layouts that
    // never asked for anything else.
    static readonly PeriodOption[] PeriodOptions =
    [
        new(null, "Follow game default"),
        new("Full", "Quarter 1"),
        new("Short", "Q1"),
        new("Number", "1")
    ];

    static readonly ColorSourceOption[] ColorSources =
    [
        new(ScorebugColorSource.Custom, "Custom colour"),
        new(ScorebugColorSource.HomePrimary, "Home · primary"),
        new(ScorebugColorSource.HomeSecondary, "Home · secondary"),
        new(ScorebugColorSource.AwayPrimary, "Visitors · primary"),
        new(ScorebugColorSource.AwaySecondary, "Visitors · secondary")
    ];

    static readonly LogoOption[] Logos =
    [
        new(ScorebugLogoSide.Home, "Home team logo"),
        new(ScorebugLogoSide.Away, "Visitors team logo")
    ];

    public ScorebugEditorWindow(ScorebugCatalog catalog, GameEngine engine, MediaPlayback? playback=null, string theme="Dark")
    {
        InitializeComponent(); _catalog=catalog; _engine=engine; _playback=playback; _theme=theme;
        ThemeManager.Apply(this,theme);
        ApplyTipVisibility(PreferencesStore.Load().ShowHeadingHints);
        // The studio opens filling the desktop (windowed, so the title bar and taskbar stay), and it
        // can never be sized past the working area: a window that runs off the bottom of the screen
        // hides the lower edge of the artwork, which read as a canvas you could not drag into.
        var work=SystemParameters.WorkArea;
        MinWidth=Math.Min(MinWidth,Math.Max(720,work.Width-40));
        MinHeight=Math.Min(MinHeight,Math.Max(480,work.Height-40));
        MaxWidth=work.Width; MaxHeight=work.Height;
        Width=MaxWidth; Height=MaxHeight;
        WindowState=WindowState.Maximized;
        // Follow it onto a portrait or higher-DPI monitor without running off the screen.
        ScreenFit.Track(this);
        AnimationInput.ItemsSource=Enum.GetValues<ScorebugAnimation>();
        TransitionInput.ItemsSource=Enum.GetValues<ScorebugTransition>();
        TransitionBox.ItemsSource=LayoutTransitions;
        ShapeInput.ItemsSource=Enum.GetValues<ScorebugShape>();
        IconInput.ItemsSource=Enum.GetValues<ScorebugIcon>();
        PeriodFormatInput.ItemsSource=PeriodOptions;
        ForegroundSourceInput.ItemsSource=ColorSources;
        BackgroundSourceInput.ItemsSource=ColorSources;
        GradientSourceInput.ItemsSource=ColorSources;
        BorderSourceInput.ItemsSource=ColorSources;
        LogoInput.ItemsSource=Logos;
        FontInput.ItemsSource=FontCatalog.All;
        _audioOptions=new[]{new AudioOption(null,"Default output")}
            .Concat(AudioRouter.Devices().Select(device=>new AudioOption(device.Id,device.Name))).ToArray();
        AudioDeviceInput.ItemsSource=_audioOptions;
        CanvasPreset.ItemsSource=ScorebugLayout.SizePresets.Select(p=>p.Name).ToArray();
        _canvasLoading=true;
        CanvasPreset.SelectedIndex=PresetIndex(catalog.Layout.Width,catalog.Layout.Height);
        CanvasWidthInput.Text=catalog.Layout.Width.ToString();
        CanvasHeightInput.Text=catalog.Layout.Height.ToString();
        _canvasLoading=false;
        PlayerSideInput.ItemsSource=new[]{new PlayerSideOption(true,"Home"),new PlayerSideOption(false,"Visitors")};
        PlayerSideInput.SelectedIndex=0;
        PlayerPickInput.ItemsSource=new[]{new PlayerChoice(null,"Selected player — whoever is on the graphic")};
        PlayerFieldInput.ItemsSource=PlayerFields.All(null).Select(field=>new PlayerFieldOption(field,field)).ToArray();
        _propertySave.Tick+=(_,_)=>{_propertySave.Stop();SaveProperties();RefreshAllVisuals();};
        _catalog.Changed += Render;
        _engine.Changed += RefreshBoundText;
        _wallClock.Tick+=(_,_)=>{if(UsesWallClock())RefreshBoundText();};
        _wallClock.Start();
        // Every text field in the property panel commits on Enter. The window handler below also
        // covers Enter, but wiring the boxes themselves means a field always settles even when it
        // lives inside a template the window-level preview never sees.
        foreach(var box in Descendants<TextBox>(PropertyPanel))
            box.KeyDown+=(_,e)=>
            {
                if(e.Key!=Key.Enter)return;
                _propertySave.Stop(); SaveProperties(); RefreshAllVisuals();
                box.SelectAll();
                e.Handled=true;
            };
        Render();
    }

    /// <summary>A fresh copy of a linked part follows the original; a fresh copy of anything else
    /// starts its own link group so the two can be edited as one.</summary>
    static void LinkCopies(ScorebugElement original,ScorebugElement copy)
    {
        if(string.IsNullOrWhiteSpace(original.LinkId))original.LinkId=original.Id;
        copy.LinkId=original.LinkId;
    }

    static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is T match)yield return match;
            foreach(var nested in Descendants<T>(child))yield return nested;
        }
    }

    /// <summary>True when any element prints the date or the time of day.</summary>
    bool UsesWallClock()
    {
        foreach(var item in _catalog.Layout.Elements)
            if(item.Content.Contains("{{TIME}}",StringComparison.OrdinalIgnoreCase)||item.Content.Contains("{{DATE}}",StringComparison.OrdinalIgnoreCase))return true;
        return false;
    }

    ScorebugElement? Selected => _selectedId is null ? null : _catalog.Layout.Elements.FirstOrDefault(x=>x.Id==_selectedId);

    bool IsSelected(string id) => _selectedIds.Contains(id);

    /// <summary>Drops the selection entirely, for when the layout underneath it goes away.</summary>
    void ClearSelection()
    {
        _selectedIds.Clear();
        _selectedId=null;
    }

    /// <summary>
    /// Mirrors the canvas selection onto the layer list without treating it as the operator's click.
    /// The list runs in Extended mode, so Ctrl and Shift give range and toggle selection for free.
    /// </summary>
    void SyncLayersFromSelection()
    {
        _syncingLayers=true;
        try
        {
            Layers.SelectedItems.Clear();
            foreach(var item in Layers.Items.OfType<LayerItem>().Where(x=>_selectedIds.Contains(x.Element.Id)))
                Layers.SelectedItems.Add(item);
        }
        finally { _syncingLayers=false; }
    }

    void Render()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(Render);return;}
        if(_skipRebuild)return;
        if(_dragElement is not null)return; // never rebuild under an active gesture
        _selectedIds.RemoveAll(id=>_catalog.Layout.Elements.All(x=>x.Id!=id));
        if(_selectedId is not null && !_selectedIds.Contains(_selectedId))_selectedId=_selectedIds.LastOrDefault();
        _loading=true;
        _syncingLayers=true;
        // Top of the list is the front of the picture, the opposite of the canvas' back-to-front order.
        var ordered=_catalog.Layout.Elements.AsEnumerable().Reverse().Select(e=>new LayerItem(e)).ToArray();
        Layers.ItemsSource=ordered;
        foreach(var item in ordered.Where(x=>_selectedIds.Contains(x.Element.Id)))Layers.SelectedItems.Add(item);
        LayerCount.Text=ordered.Length==0?"no elements":$"{ordered.Length} element{(ordered.Length==1?"":"s")}";
        _syncingLayers=false;
        SyncLayoutPicker();
        SyncDisplayPicker();
        SyncLinkPicker();
        var previousVideos=new Dictionary<string,VideoSurface>(_videoSurfaces);
        var previousVisuals=new Dictionary<string,(Border Host, FrameworkElement Content, Border Handle, ScorebugElementKind Kind)>(_visuals);
        _videoSurfaces.Clear();
        DesignCanvas.Width=_catalog.Layout.Width; DesignCanvas.Height=_catalog.Layout.Height;
        // Elements (video especially) must never paint outside the artwork they belong to.
        DesignCanvas.ClipToBounds=true;
        CanvasInfo.Text=$"{_catalog.Layout.Name} · {_catalog.Layout.Width} × {_catalog.Layout.Height} · drag elements to reposition";
        // The artwork backdrop is the layout's own colour, and it is exactly what the preview and
        // every audience window paint behind the graphic.
        var backdrop=ScorebugRender.ParseBrush(_catalog.Layout.Background);
        // Only the artwork takes the backdrop: the letterbox around it stays the panel colour, so
        // the canvas edge is unmistakable at the top and the bottom.
        DesignCanvas.Background=backdrop;
        if(!_canvasLoading)
        {
            _canvasLoading=true;
            CanvasWidthInput.Text=_catalog.Layout.Width.ToString();
            CanvasHeightInput.Text=_catalog.Layout.Height.ToString();
            CanvasPreset.SelectedIndex=PresetIndex(_catalog.Layout.Width,_catalog.Layout.Height);
            CanvasBackgroundInput.Text=_catalog.Layout.Background;
            _canvasLoading=false;
            CanvasBackgroundSwatch.Background=backdrop;
            CanvasBackgroundSwatch.ToolTip=$"Backdrop colour: {_catalog.Layout.Background}";
        }
        // Reuse the visual for every element that still exists instead of clearing the canvas. Taking
        // a MediaElement out of the visual tree stops its decoder, so a full rebuild on each edit
        // restarted every video and left the dock's playhead behind it.
        _visuals.Clear(); _applied.Clear();
        var zIndex=0;
        foreach(var item in _catalog.Layout.Elements)
        {
            Border host;
            if(previousVisuals.TryGetValue(item.Id,out var prior)&&CanReuseVisual(item,prior))
            {
                host=prior.Host;
                if(prior.Content is VideoSurface video)video.SetElement(item);
                _visuals[item.Id]=(host,prior.Content,prior.Handle,item.Kind);
                if(prior.Content is VideoSurface kept)_videoSurfaces[item.Id]=kept;
            }
            else host=CreateVisual(item,previousVideos);
            // A new visual is added here; a reused one is already on the canvas and must stay put,
            // because removing it would take a playing video out of the tree.
            if(!DesignCanvas.Children.Contains(host))DesignCanvas.Children.Add(host);
            Panel.SetZIndex(host,zIndex++);
        }
        foreach(var child in DesignCanvas.Children.OfType<UIElement>().ToList())
            if(!_visuals.Values.Any(visual=>ReferenceEquals(visual.Host,child)))DesignCanvas.Children.Remove(child);
        foreach(var (id,surface) in previousVideos)
            if(!_videoSurfaces.ContainsKey(id))surface.Dispose();
        RefreshAllVisuals();
        _loading=false;
        LoadProperties();
    }

    /// <summary>
    /// Fills the layout strip: every layout on this display as a picture with its name beneath, so
    /// switching layouts is a click on the one the operator can see rather than a name in a list.
    /// </summary>
    void SyncLayoutPicker()
    {
        _layoutLoading=true;
        try
        {
            LayoutStrip.Children.Clear();
            var layouts=_catalog.Layouts;
            for(var index=0;index<layouts.Count;index++)
            {
                var layout=layouts[index];
                var active=index==_catalog.ActiveIndex;
                var tile=new Button
                {
                    Padding=new Thickness(4),
                    Margin=new Thickness(0,0,6,0),
                    MinWidth=0,
                    Background=Brushes.Transparent,
                    BorderThickness=new Thickness(active?2:1),
                    BorderBrush=new SolidColorBrush(active?Color.FromRgb(0x38,0xD7,0xA3):Color.FromRgb(0x2E,0x3B,0x4A)),
                    Cursor=Cursors.Hand,
                    ToolTip=active?$"{layout.Name} — being designed now":$"Switch the studio to {layout.Name}"
                };
                var image=new Image
                {
                    Source=ThumbnailFor(layout),
                    Width=122,
                    Height=68,
                    Stretch=Stretch.Uniform,
                    SnapsToDevicePixels=true
                };
                RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
                var label=new TextBlock
                {
                    Text=$"{index+1}. {layout.Name}",
                    FontSize=10,
                    Margin=new Thickness(2,3,2,0),
                    TextAlignment=TextAlignment.Center,
                    TextTrimming=TextTrimming.CharacterEllipsis,
                    MaxWidth=140,
                    Foreground=new SolidColorBrush(active?Color.FromRgb(0x38,0xD7,0xA3):Color.FromRgb(0x93,0xA0,0xB2))
                };
                var stack=new StackPanel();
                stack.Children.Add(image);
                stack.Children.Add(label);
                tile.Content=stack;
                // The theme pass repaints every button it meets; these tiles carry their own colours.
                ThemeManager.SetSkipTheme(tile,true);
                var target=index;
                tile.Click+=(_,_)=>SelectLayoutFromStrip(target);
                LayoutStrip.Children.Add(tile);
            }
            LayoutHintText.Text=_catalog.ActiveIndex==_catalog.PublishedIndex
                ? "This layout is on air."
                : "Preview only — press Update Output in the control panel to put it on air.";
        }
        finally { _layoutLoading=false; }
    }

    void SelectLayoutFromStrip(int index)
    {
        if(_layoutLoading||index<0||index>=_catalog.Layouts.Count||index==_catalog.ActiveIndex)return;
        ClearSelection();
        _catalog.SelectLayout(index);
    }

    /// <summary>A small picture of one layout, cached until its artwork changes.</summary>
    ImageSource ThumbnailFor(ScorebugLayout layout)
    {
        var signature=ThumbSignature(layout);
        if(_layoutThumbs.TryGetValue(layout.Id,out var cached)&&cached.Signature==signature)return cached.Image;
        ImageSource image;
        try
        {
            // A throwaway view of the same artwork, rendered once: no media plays in a thumbnail.
            var view=new ScorebugCanvasView(_catalog,_engine,stateProvider:()=>_engine.State,playback:null,layoutProvider:()=>layout)
            {
                Width=264,
                Height=148
            };
            view.Measure(new Size(264,148));
            view.Arrange(new Rect(0,0,264,148));
            view.UpdateLayout();
            var bitmap=new RenderTargetBitmap(264,148,96,96,PixelFormats.Pbgra32);
            bitmap.Render(view);
            bitmap.Freeze();
            view.Dispose();
            image=bitmap;
        }
        catch
        {
            image=new DrawingImage();
        }
        _layoutThumbs[layout.Id]=(signature,image);
        return image;
    }

    static string ThumbSignature(ScorebugLayout layout) =>
        $"{layout.Width}x{layout.Height}|{layout.Background}|"+
        string.Join(";",layout.Elements.Select(item=>$"{item.Id}:{item.Kind}:{item.X},{item.Y},{item.Width},{item.Height}:{item.Content}:{item.FontSize}:{item.Foreground}:{item.Background}:{item.TransparentBackground}"));

    /// <summary>Fills the display picker from the catalog without treating the sync as a choice.</summary>
    void SyncDisplayPicker()
    {
        _displayLoading=true;
        try
        {
            var options=_catalog.Displays.Select((display,index)=>new DisplayOption(display.Id,$"{index+1}. {display.Name}")).ToArray();
            if(DisplayBox.ItemsSource is not IEnumerable<DisplayOption> existing||!existing.Select(x=>x.Label).SequenceEqual(options.Select(x=>x.Label)))
                DisplayBox.ItemsSource=options;
            DisplayBox.SelectedIndex=_catalog.ActiveDisplayIndex;
            // The hand-over belongs to the layout being designed, so every layout can arrive
            // differently; the pickers follow both the display and the layout selection.
            var layout=_catalog.Layout;
            TransitionBox.SelectedItem=LayoutTransitions.FirstOrDefault(option=>option.Transition==layout.Transition)??LayoutTransitions[1];
            TransitionSecondsInput.Text=layout.TransitionSeconds.ToString("0.##");
            TransitionCaption.Text=$"  Saved with {layout.Name} — every layout can arrive differently.";
        }
        finally { _displayLoading=false; }
    }

    void Transition_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_displayLoading)return;
        if(TransitionBox.SelectedItem is not LayoutTransitionOption option)return;
        _catalog.SetTransition(option.Transition,_catalog.Layout.TransitionSeconds);
    }

    void TransitionSeconds_Changed(object sender,TextChangedEventArgs e)
    {
        if(_displayLoading)return;
        // Half-typed text ("", "0.") leaves the setting alone instead of snapping it to zero.
        if(!double.TryParse(TransitionSecondsInput.Text,out var seconds))return;
        _catalog.SetTransition(_catalog.Layout.Transition,seconds);
    }

    /// <summary>Shows this layout's link to another display, if it has one that still resolves.</summary>
    void SyncLinkPicker()
    {
        _linkLoading=true;
        try
        {
            var layout=_catalog.Layout;
            var targets=new List<LinkDisplayOption>{new(null,"No link")};
            foreach(var display in _catalog.Displays)
            {
                if(string.Equals(display.Id,_catalog.ActiveDisplay.Id,StringComparison.OrdinalIgnoreCase))continue;
                targets.Add(new LinkDisplayOption(display.Id,display.Name));
            }
            LinkDisplayBox.ItemsSource=targets;
            var selected=targets.FirstOrDefault(option=>string.Equals(option.Id,layout.LinkDisplayId,StringComparison.OrdinalIgnoreCase))??targets[0];
            LinkDisplayBox.SelectedItem=selected;
            FillLinkLayouts(selected.Id,layout.LinkLayoutId);
            // Ticked exactly when the layout on the other side already points back at this one.
            LinkBothWaysInput.IsChecked=HasReciprocal(layout);
        }
        finally { _linkLoading=false; }
    }

    /// <summary>True when the layout this one links to links straight back to it.</summary>
    bool HasReciprocal(ScorebugLayout layout)
    {
        if(string.IsNullOrWhiteSpace(layout.LinkDisplayId)||string.IsNullOrWhiteSpace(layout.LinkLayoutId))return false;
        var target=_catalog.FindDisplay(layout.LinkDisplayId);
        var counterpart=target?.Layouts.FirstOrDefault(x=>string.Equals(x.Id,layout.LinkLayoutId,StringComparison.OrdinalIgnoreCase));
        return counterpart is not null
            &&string.Equals(counterpart.LinkDisplayId,_catalog.ActiveDisplay.Id,StringComparison.OrdinalIgnoreCase)
            &&string.Equals(counterpart.LinkLayoutId,layout.Id,StringComparison.OrdinalIgnoreCase);
    }

    void LinkBothWays_Changed(object sender,RoutedEventArgs e)
    {
        if(_linkLoading)return;
        ApplyReciprocal(LinkBothWaysInput.IsChecked==true);
    }

    /// <summary>
    /// Writes (or removes) the link coming back from the other display's layout, so a link can work
    /// in both directions. Only ever touches the exact layout this one points at.
    /// </summary>
    void ApplyReciprocal(bool enabled)
    {
        var layout=_catalog.Layout;
        var display=_catalog.ActiveDisplay;
        if(string.IsNullOrWhiteSpace(layout.LinkDisplayId)||string.IsNullOrWhiteSpace(layout.LinkLayoutId))return;
        var target=_catalog.FindDisplay(layout.LinkDisplayId);
        if(target is null)return;
        var index=target.Layouts.FindIndex(x=>string.Equals(x.Id,layout.LinkLayoutId,StringComparison.OrdinalIgnoreCase));
        if(index<0)return;
        var back=target.Layouts[index];
        var pointsBack=string.Equals(back.LinkDisplayId,display.Id,StringComparison.OrdinalIgnoreCase)
            &&string.Equals(back.LinkLayoutId,layout.Id,StringComparison.OrdinalIgnoreCase);
        if(enabled&&!pointsBack)_catalog.SetLink(target.Id,index,display.Id,layout.Id);
        else if(!enabled&&pointsBack)_catalog.SetLink(target.Id,index,null,null);
    }

    void FillLinkLayouts(string? displayId,string? selectedLayoutId)
    {
        var target=_catalog.FindDisplay(displayId);
        var options=new List<LinkLayoutOption>{new(null,target is null?"—":"Choose a layout")};
        if(target is not null)
            options.AddRange(target.Layouts.Select((layout,index)=>new LinkLayoutOption(layout.Id,$"{index+1}. {layout.Name}")));
        LinkLayoutBox.ItemsSource=options;
        LinkLayoutBox.IsEnabled=target is not null;
        LinkLayoutBox.SelectedItem=options.FirstOrDefault(option=>string.Equals(option.Id,selectedLayoutId,StringComparison.OrdinalIgnoreCase))??options[0];
    }

    /// <summary>
    /// True when an existing visual still draws what this element now asks for. Live artwork (the
    /// timeout bars, the possession dot, the bases) is its own kind of content, so switching an
    /// element to an icon — or from one icon to another — has to build it instead of reusing the
    /// text block that was there before, which is why icons never appeared in the studio.
    /// </summary>
    static bool CanReuseVisual(ScorebugElement item,(Border Host, FrameworkElement Content, Border Handle, ScorebugElementKind Kind) prior)
    {
        if(prior.Kind!=item.Kind)return false;
        var wantsIcon=item.Kind==ScorebugElementKind.Text&&item.Icon!=ScorebugIcon.None;
        var hasIcon=prior.Content is ScorebugIconView;
        if(wantsIcon!=hasIcon)return false;
        return !hasIcon||((ScorebugIconView)prior.Content).Icon==item.Icon;
    }

    Border CreateVisual(ScorebugElement item,Dictionary<string,VideoSurface>? previous=null)
    {
        FrameworkElement content;
        if(previous is not null&&previous.TryGetValue(item.Id,out var kept))
        {
            // Detach from the old host and re-point at the edited element: the picture keeps running.
            if(kept.Parent is Border oldClip)oldClip.Child=null;
            kept.SetElement(item);
            content=kept;
        }
        else content=ScorebugRender.CreateContent(item,()=>_engine.State,_playback,true,showVideoPlaceholder:true);
        if(content is VideoSurface videoSurface)_videoSurfaces[item.Id]=videoSurface;
        var handle=new Border
        {
            Width=16, Height=16, Background=Brushes.DeepSkyBlue, BorderBrush=Brushes.White,
            BorderThickness=new Thickness(1), Cursor=Cursors.SizeNWSE, Tag=item,
            HorizontalAlignment=HorizontalAlignment.Right, VerticalAlignment=VerticalAlignment.Bottom,
            Margin=new Thickness(0,0,1,1),
            Visibility=IsSelected(item.Id)&&item.Id==_selectedId?Visibility.Visible:Visibility.Collapsed,
            ToolTip="Drag to resize"
        };
        handle.PreviewMouseLeftButtonDown+=Resize_MouseDown;
        handle.PreviewMouseMove+=Resize_MouseMove;
        handle.PreviewMouseLeftButtonUp+=Resize_MouseUp;
        // Clip the content to its own box so a playing video cannot bleed over neighbouring elements.
        var clipped=new Border{Child=content,ClipToBounds=true};
        var stack=new Grid();
        stack.Children.Add(clipped);
        stack.Children.Add(handle);
        var host=new Border
        {
            Width=item.Width, Height=item.Height, Background=ScorebugRender.HostBackground(item,_engine.State),
            ClipToBounds=true,
            Opacity=item.Opacity, Cursor=Cursors.SizeAll, Tag=item, Child=stack,
            BorderBrush=IsSelected(item.Id)?Brushes.DeepSkyBlue:ScorebugRender.HostBorder(item,_engine.State),
            BorderThickness=new Thickness(IsSelected(item.Id)?2:Math.Max(1,item.BorderThickness))
        };
        Canvas.SetLeft(host,item.X); Canvas.SetTop(host,item.Y); Panel.SetZIndex(host,_catalog.Layout.Elements.IndexOf(item));
        host.MouseLeftButtonDown+=Element_MouseDown; host.MouseMove+=Element_MouseMove; host.MouseLeftButtonUp+=Element_MouseUp;
        _visuals[item.Id]=(host,content,handle,item.Kind);
        ApplyAnimation(item,host);
        return host;
    }

    void ApplyAnimation(ScorebugElement item,FrameworkElement host)
    {
        if(_applied.TryGetValue(item.Id,out var applied)&&applied.Animation==item.Animation&&Math.Abs(applied.Opacity-item.Opacity)<0.0001)return;
        host.BeginAnimation(UIElement.OpacityProperty,null);
        if(host.RenderTransform is TranslateTransform previous){previous.BeginAnimation(TranslateTransform.XProperty,null);previous.BeginAnimation(TranslateTransform.YProperty,null);host.RenderTransform=Transform.Identity;}
        switch(item.Animation)
        {
            case ScorebugAnimation.Pulse:
                host.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(item.Opacity*0.72,item.Opacity,TimeSpan.FromSeconds(1.1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
                break;
            case ScorebugAnimation.Drift:
                // Animate the transform, not the host: the old call animated X on the Border itself,
                // which moved nothing, so Drift looked broken.
                var drift=new TranslateTransform();
                host.RenderTransform=drift;
                drift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,14,TimeSpan.FromSeconds(3.2)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
                drift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,7,TimeSpan.FromSeconds(2.1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
                break;
        }
        _applied[item.Id]=(item.Kind,item.Shape,item.ImagePath,item.Animation,item.Opacity);
    }

    /// <summary>Updates every visual in place. Used while typing so focus and selection are never lost.</summary>
    void RefreshAllVisuals()
    {
        foreach(var item in _catalog.Layout.Elements)
        {
            if(!_visuals.TryGetValue(item.Id,out var visual))continue;
            if(visual.Content is ScorebugTextBlock block)
                ScorebugRender.ApplyToContent(block,item,_engine.State,ScoreboardTemplates.Bind(item.Content,_engine.State,item.PeriodFormat));
            else if(visual.Content is ScorebugIconView iconView)
                iconView.Update();
            else if(visual.Content is not VideoSurface)
                ScorebugRender.ApplyToContent(visual.Content,item,_engine.State);
            visual.Host.Width=item.Width; visual.Host.Height=item.Height;
            visual.Host.Background=ScorebugRender.HostBackground(item,_engine.State);
            if(item.Animation!=ScorebugAnimation.Pulse)visual.Host.Opacity=item.Opacity;
            Canvas.SetLeft(visual.Host,item.X); Canvas.SetTop(visual.Host,item.Y);
            visual.Handle.Visibility=item.Id==_selectedId&&IsSelected(item.Id)?Visibility.Visible:Visibility.Collapsed;
            visual.Host.BorderBrush=IsSelected(item.Id)?Brushes.DeepSkyBlue:ScorebugRender.HostBorder(item,_engine.State);
            visual.Host.BorderThickness=new Thickness(IsSelected(item.Id)?2:Math.Max(1,item.BorderThickness));
            ApplyAnimation(item,visual.Host);
        }
        // The property panel's colour previews follow the typed colours in place, since an edit no
        // longer rebuilds the canvas and therefore no longer reloads the panel.
        RefreshColorSwatches();
    }

    void RefreshBoundText()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(RefreshBoundText);return;}
        if(_dragElement is not null)return;
        foreach(var item in _catalog.Layout.Elements)
        {
            if(!_visuals.TryGetValue(item.Id,out var visual))continue;
            if(visual.Content is ScorebugIconView iconView) iconView.Update();
            else if(visual.Content is ScorebugTextBlock block)
            {
                block.ApplyStyle(item,_engine.State);
                block.UpdateText(ScoreboardTemplates.Bind(item.Content,_engine.State,item.PeriodFormat),item.Transition,item.TransitionSeconds);
            }
            else if(visual.Content is Image or ScorebugLogoView or System.Windows.Shapes.Shape)
                ScorebugRender.ApplyToContent(visual.Content,item,_engine.State);
        }
        RefreshColorSwatches();
        if(Selected is {Kind:ScorebugElementKind.Text}) UpdatePlayerHint();
    }

    // ---------- player data ----------

    /// <summary>Reads the selected element's player token into the team / player / value pickers.</summary>
    void LoadPlayerBinding(ScorebugElement element)
    {
        _bindingLoading=true;
        try
        {
            var home=true; string? playerId=null; var field="Name";
            var match=PlayerToken.Match(element.Content);
            if(match.Success)
            {
                home=match.Groups["side"].Value.Equals("HOME",StringComparison.OrdinalIgnoreCase);
                var raw=match.Groups["field"].Value.Trim();
                if(raw.StartsWith("id:",StringComparison.OrdinalIgnoreCase))
                {
                    var rest=raw[3..];
                    var split=rest.IndexOf(':');
                    if(split>0){playerId=rest[..split].Trim();field=rest[(split+1)..].Trim();}
                }
                else field=raw;
            }
            PlayerSideInput.SelectedItem=(PlayerSideInput.ItemsSource as IEnumerable<PlayerSideOption>)?.FirstOrDefault(option=>option.Home==home);
            FillPlayerChoices(home,playerId);
            FillFieldChoices(home,field);
            UpdatePlayerHint();
        }
        finally { _bindingLoading=false; }
    }

    /// <summary>The roster one side of the game draws from, if it has one.</summary>
    Roster? RosterFor(bool home) =>
        RosterStore.Book.Find(home?_engine.State.HomeRosterId:_engine.State.AwayRosterId);

    /// <summary>Lists "selected player" first, then every player on that side's roster by name.</summary>
    void FillPlayerChoices(bool home,string? playerId)
    {
        var roster=RosterFor(home);
        var options=new List<PlayerChoice>{new(null,"Selected player — whoever is on the graphic")};
        if(roster is not null)
            options.AddRange(roster.Players.Select(player=>new PlayerChoice(player.Id,
                $"#{player.Number} {player.Name}{(player.Position.Length>0?$" · {player.Position}":"")}".Trim())));
        PlayerPickInput.ItemsSource=options;
        PlayerPickInput.SelectedItem=options.FirstOrDefault(option=>string.Equals(option.Id??"",playerId??"",StringComparison.OrdinalIgnoreCase))??options[0];
        PlayerPickInput.IsEnabled=roster is not null;
        PlayerPickInput.ToolTip=roster is null
            ? "No roster is assigned to this side yet — pick one in the Rosters tab"
            : $"{roster.Name} · choose one player to pin, or follow whoever the desk selects";
    }

    /// <summary>Lists name, number, position and every stat column that roster tracks.</summary>
    void FillFieldChoices(bool home,string field)
    {
        var options=PlayerFields.All(RosterFor(home)).Select(name=>new PlayerFieldOption(name,name)).ToArray();
        PlayerFieldInput.ItemsSource=options;
        PlayerFieldInput.SelectedItem=options.FirstOrDefault(option=>string.Equals(option.Field,field,StringComparison.OrdinalIgnoreCase))??options[0];
    }

    /// <summary>Writes the pickers back into the element's CONTENT as a {{PLAYER:…}} binding.</summary>
    void PlayerBinding_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_loading||_bindingLoading||Selected is null)return;
        if(sender==PlayerSideInput)
        {
            _bindingLoading=true;
            try
            {
                var home=(PlayerSideInput.SelectedItem as PlayerSideOption)?.Home??true;
                FillPlayerChoices(home,null);
                FillFieldChoices(home,(PlayerFieldInput.SelectedItem as PlayerFieldOption)?.Field??"Name");
            }
            finally { _bindingLoading=false; }
        }
        var side=(PlayerSideInput.SelectedItem as PlayerSideOption)?.Home??true;
        var player=(PlayerPickInput.SelectedItem as PlayerChoice)?.Id;
        var field=(PlayerFieldInput.SelectedItem as PlayerFieldOption)?.Field??"Name";
        var token=player is null
            ? $"{{{{PLAYER:{(side?"HOME":"AWAY")}:{field}}}}}"
            : $"{{{{PLAYER:{(side?"HOME":"AWAY")}:id:{player}:{field}}}}}";
        if(ContentInput.Text==token){UpdatePlayerHint();return;}
        ContentInput.Text=token;
        UpdatePlayerHint();
    }

    /// <summary>Shows what the binding prints right now, so the operator never has to guess.</summary>
    void UpdatePlayerHint()
    {
        if(PlayerBindingHint is null)return;
        if(ContentInput.Text.Length==0||!PlayerToken.IsMatch(ContentInput.Text))
        {
            PlayerBindingHint.Text="This text is not tied to a player yet. Pick a team, player and value above, or type any binding in CONTENT.";
            return;
        }
        var printed=ScoreboardTemplates.Bind(ContentInput.Text,_engine.State,Selected?.PeriodFormat);
        PlayerBindingHint.Text=printed.Length==0
            ? "Prints nothing: that player or value is empty right now."
            : $"Prints now: \"{printed}\"";
    }

    void Layers_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_loading||_syncingLayers)return;
        // Extended mode hands over the whole range: Ctrl adds rows one at a time, Shift takes every
        // row between the anchor and the click.
        var ids=Layers.SelectedItems.OfType<LayerItem>().Select(x=>x.Element.Id).ToArray();
        _selectedIds.Clear();
        _selectedIds.AddRange(ids);
        // Keep editing the element the panel already held while it is still selected, so adding a
        // row to the selection does not shuffle the property fields.
        _selectedId=_selectedId is not null&&_selectedIds.Contains(_selectedId) ? _selectedId : ids.LastOrDefault();
        RefreshAllVisuals();
        LoadProperties();
    }

    void LoadProperties()
    {
        _loading=true;
        var e=Selected; var has=e is not null;
        foreach(var control in new Control[]{NameInput,ContentInput,XInput,YInput,WidthInput,HeightInput,FontInput,FontInputSize,RotationInput,ColorInput,BackgroundInput,BorderWidthInput,BorderColorInput,BorderSourceInput,AnimationInput,TransitionInput,TransitionSpeedInput,ShapeInput,VisibleInput,TransparentBackgroundInput,MediaTagInput,PeriodFormatInput,IconInput,IconVisitorsInput,IconFlipInput,IconFillInput,IconFillSourceInput,ForegroundSourceInput,BackgroundSourceInput,LogoInput,BoldInput,ItalicInput,UnderlineInput,StrikeInput,PreserveAspectInput,AspectInput,AudioDeviceInput,GradientInput,GradientSourceInput,GradientColorInput,GradientAngleInput,OpacityInput})
            control.IsEnabled=has;
        if(has)
        {
            NameInput.Text=e!.Name; ContentInput.Text=e.Content; XInput.Text=e.X.ToString("0"); YInput.Text=e.Y.ToString("0");
            WidthInput.Text=e.Width.ToString("0"); HeightInput.Text=e.Height.ToString("0");
            FontInputSize.Text=e.FontSize.ToString("0");
            RotationInput.Text=e.Rotation.ToString("0");
            var font=FontCatalog.ChoiceFor(e.FontFamily);
            FontInput.SelectedItem=FontCatalog.All.FirstOrDefault(c=>string.Equals(c.Source,font.Source,StringComparison.OrdinalIgnoreCase))??font;
            BoldInput.IsChecked=e.Bold; ItalicInput.IsChecked=e.Italic;
            UnderlineInput.IsChecked=e.Underline; StrikeInput.IsChecked=e.StrikeThrough;
            ColorInput.Text=e.Foreground; BackgroundInput.Text=e.Background;
            BorderColorInput.Text=e.BorderColor; BorderWidthInput.Text=e.BorderThickness.ToString("0.#");
            TransitionSpeedInput.Text=e.TransitionSeconds.ToString("0.00");
            GradientInput.IsChecked=e.BackgroundGradient;
            GradientColorInput.Text=e.BackgroundColor2;
            GradientAngleInput.Text=e.GradientAngle.ToString("0");
            OpacityInput.Text=e.Opacity.ToString("0.00");
            ForegroundSourceInput.SelectedItem=ColorSources.FirstOrDefault(c=>c.Source==e.ForegroundSource)??ColorSources[0];
            BackgroundSourceInput.SelectedItem=ColorSources.FirstOrDefault(c=>c.Source==e.BackgroundSource)??ColorSources[0];
            GradientSourceInput.SelectedItem=ColorSources.FirstOrDefault(c=>c.Source==e.BackgroundSource2)??ColorSources[0];
            BorderSourceInput.SelectedItem=ColorSources.FirstOrDefault(c=>c.Source==e.BorderSource)??ColorSources[0];
            UpdateColorAvailability();
            AnimationInput.SelectedItem=e.Animation; TransitionInput.SelectedItem=e.Transition; ShapeInput.SelectedItem=e.Shape;
            VisibleInput.IsChecked=e.Visible; TransparentBackgroundInput.IsChecked=e.TransparentBackground;
            MediaTagInput.Text=e.MediaTag;
            PreserveAspectInput.IsChecked=e.PreserveVideoAspect;
            AspectInput.Text=e.VideoAspect.ToString("0.###");
            AudioDeviceInput.SelectedItem=_audioOptions.FirstOrDefault(option=>string.Equals(option.Id??"",e.AudioDevice??"",StringComparison.OrdinalIgnoreCase))??_audioOptions[0];
            LogoInput.SelectedItem=Logos.FirstOrDefault(l=>l.Side==e.LogoSide)??Logos[0];
            PeriodFormatInput.SelectedItem=PeriodOptions.FirstOrDefault(o=>o.Key==e.PeriodFormat)??PeriodOptions[0];
            IconInput.SelectedItem=e.Icon; IconVisitorsInput.IsChecked=e.IconVisitors; IconFlipInput.IsChecked=e.IconFlipped;
            IconFillInput.Text=e.IconFillColor;
            IconFillSourceInput.SelectedItem=ColorSources.FirstOrDefault(c=>c.Source==e.IconFillSource)??ColorSources[0];
        }
        if(e is not null&&e.Kind==ScorebugElementKind.Text) LoadPlayerBinding(e);
        _loading=false;
        UpdatePropertyVisibility();
        RefreshColorSwatches();
    }

    /// <summary>
    /// Paints the two colour previews in the property panel. A hex box on its own never showed
    /// what an element (or a team palette slot) actually draws; these swatches do.
    /// </summary>
    void RefreshColorSwatches()
    {
        var item=Selected;
        TextColorSwatch.Background=item is null?Brushes.Transparent:ScorebugRender.ParseBrush(ScorebugRender.ResolveForeground(item,_engine.State));
        BackgroundColorSwatch.Background=item is null||item.TransparentBackground?Brushes.Transparent:ScorebugRender.ParseBrush(ScorebugRender.ResolveBackground(item,_engine.State));
        GradientColorSwatch.Background=item is null||item.TransparentBackground||!item.BackgroundGradient?Brushes.Transparent:ScorebugRender.ParseBrush(_engine.State.ColorFor(item.BackgroundSource2)??item.BackgroundColor2);
        BorderColorSwatch.Background=item is null||item.BorderThickness<=0?Brushes.Transparent:ScorebugRender.ParseBrush(ScorebugRender.ResolveBorder(item,_engine.State));
        IconFillSwatch.Background=item is null||item.Icon!=ScorebugIcon.Bases?Brushes.Transparent:ScorebugRender.ParseBrush(ScorebugRender.ResolveIconFill(item,_engine.State));
        TextColorSwatch.ToolTip=item is null?null:$"Text colour: {ScorebugRender.ResolveForeground(item,_engine.State)}";
        BackgroundColorSwatch.ToolTip=item is null?null:item.TransparentBackground?"No background box":$"Background colour: {ScorebugRender.ResolveBackground(item,_engine.State)}";
        GradientColorSwatch.ToolTip=item is null||!item.BackgroundGradient?"No gradient":$"Second gradient colour: {_engine.State.ColorFor(item.BackgroundSource2)??item.BackgroundColor2}";
        BorderColorSwatch.ToolTip=item is null||item.BorderThickness<=0?"No border":$"Border colour: {ScorebugRender.ResolveBorder(item,_engine.State)}";
        IconFillSwatch.ToolTip=item is null||item.Icon!=ScorebugIcon.Bases?"No bases":$"Base fill: {ScorebugRender.ResolveIconFill(item,_engine.State)}";
    }

    /// <summary>A colour box only takes typing when the element uses a fixed colour.</summary>
    void UpdateColorAvailability()
    {
        ColorInput.IsEnabled=Selected is not null && (ForegroundSourceInput.SelectedItem as ColorSourceOption)?.Source==ScorebugColorSource.Custom;
        BackgroundInput.IsEnabled=Selected is not null && (BackgroundSourceInput.SelectedItem as ColorSourceOption)?.Source==ScorebugColorSource.Custom;
        var gradientOn=Selected is not null && GradientInput.IsChecked==true;
        GradientColorInput.IsEnabled=gradientOn && (GradientSourceInput.SelectedItem as ColorSourceOption)?.Source==ScorebugColorSource.Custom;
        GradientSourceInput.IsEnabled=gradientOn;
        GradientAngleInput.IsEnabled=gradientOn;
        BorderColorInput.IsEnabled=Selected is not null && (BorderSourceInput.SelectedItem as ColorSourceOption)?.Source==ScorebugColorSource.Custom;
        // The base fill only means something on a diamond, and only when it is not a palette slot.
        IconFillInput.IsEnabled=Selected is not null
            && IconInput.SelectedItem is ScorebugIcon.Bases
            && (IconFillSourceInput.SelectedItem as ColorSourceOption)?.Source==ScorebugColorSource.Custom;
    }

    /// <summary>Shows only the properties that mean something for this kind of element.</summary>
    void UpdatePropertyVisibility()
    {
        var e=Selected;
        var linked=e is not null&&!string.IsNullOrWhiteSpace(e.LinkId);
        LinkSection.Visibility=e is null?Visibility.Collapsed:Visibility.Visible;
        LinkHint.Text=linked
            ? "This part is linked: moving it, resizing it or changing anything here updates every copy of it on every layout."
            : "This element stands on its own. \"Add parts…\" makes a copy that stays linked to its original.";
        BreakLinkButton.Visibility=linked?Visibility.Visible:Visibility.Collapsed;
        var kind=e?.Kind;
        var text=kind==ScorebugElementKind.Text;
        var any=e is not null;
        // An HTML element has no printed value, but its markup is edited in the same content box.
        TextSection.Visibility=text||kind==ScorebugElementKind.Html?Visibility.Visible:Visibility.Collapsed;
        // Type is typography: a logo, shape or video has no text to set a face, weight or colour on.
        FontSection.Visibility=text?Visibility.Visible:Visibility.Collapsed;
        // Rotation is drawn inside the text element, so only text can use it.
        TransformSection.Visibility=text?Visibility.Visible:Visibility.Collapsed;
        LogoSection.Visibility=kind==ScorebugElementKind.Logo?Visibility.Visible:Visibility.Collapsed;
        ShapePanel.Visibility=kind==ScorebugElementKind.Shape?Visibility.Visible:Visibility.Collapsed;
        VideoPanel.Visibility=kind==ScorebugElementKind.Video?Visibility.Visible:Visibility.Collapsed;
        // The host paints the backdrop and the outline for every kind, shapes included.
        BackgroundSection.Visibility=any?Visibility.Visible:Visibility.Collapsed;
        BorderSection.Visibility=any?Visibility.Visible:Visibility.Collapsed;
        PeriodSection.Visibility=text&&e!.Content.Contains("{{PERIOD}}",StringComparison.OrdinalIgnoreCase)?Visibility.Visible:Visibility.Collapsed;
        // Player data only means something on text that actually prints a player value. On a stats
        // element the pickers would overwrite {{YARDS}} with a player binding, and on a plain label
        // they change nothing at all; unbound text becomes a player element through the studio's
        // "Add player data" button instead, so the pickers stay off every other element.
        PlayerDataSection.Visibility=text&&PlayerToken.IsMatch(e!.Content)?Visibility.Visible:Visibility.Collapsed;
        IconSection.Visibility=text?Visibility.Visible:Visibility.Collapsed;
        // The fill only appears for the diamond: bars and the possession dot have one colour.
        BaseFillPanel.Visibility=text&&IconInput.SelectedItem is ScorebugIcon.Bases?Visibility.Visible:Visibility.Collapsed;
        // A value transition needs a printed value to cross to.
        TransitionSection.Visibility=text?Visibility.Visible:Visibility.Collapsed;
        var icon=e?.Icon??ScorebugIcon.None;
        IconVisitorsInput.Visibility=icon==ScorebugIcon.TimeoutBars?Visibility.Visible:Visibility.Collapsed;
        IconFlipInput.Visibility=icon==ScorebugIcon.PossessionDot?Visibility.Visible:Visibility.Collapsed;
    }

    void Property_Changed(object sender,RoutedEventArgs e)=>ApplyProperties(sender);
    void Property_Changed(object sender,TextChangedEventArgs e)=>ApplyProperties(sender);
    void Property_Changed(object sender,SelectionChangedEventArgs e)=>ApplyProperties(sender);

    void ApplyProperties(object sender)
    {
        if(_loading||Selected is null)return;
        if(sender==ForegroundSourceInput||sender==BackgroundSourceInput||sender==GradientSourceInput||sender==IconFillSourceInput){UpdateColorAvailability();SaveProperties();RefreshAllVisuals();return;}
        if(sender is ComboBox)
        {
            // Shape/font/animation changes need fresh geometry or a new face.
            SaveProperties();
            // Choosing a different icon changes which properties exist (a diamond has a base fill)
            // and whether the fill colour can be typed, so the panel is repainted with it.
            if(sender==IconInput){UpdatePropertyVisibility();UpdateColorAvailability();RefreshColorSwatches();}
            Render(); return;
        }
        if(sender is CheckBox){UpdateColorAvailability();SaveProperties();RefreshAllVisuals();return;}
        if(sender==ColorInput||sender==BackgroundInput||sender==GradientColorInput){SaveProperties();RefreshAllVisuals();return;}
        if(sender==ContentInput) UpdatePropertyVisibility();
        _propertySave.Stop(); _propertySave.Start();
    }

    /// <summary>
    /// Writes the property panel onto the graphic. With several rows selected every visual property
    /// lands on all of them, so a background, border or colour is set once for the whole group; the
    /// name and the content stay with the primary element, because those are what makes it that
    /// element rather than another one.
    /// </summary>
    void SaveProperties()
    {
        if(_loading)return;
        var item=Selected; if(item is null)return;
        var ids=_selectedIds.Count>0?_selectedIds.ToArray():[item.Id];
        var name=NameInput.Text; var content=ContentInput.Text;
        var xOk=double.TryParse(XInput.Text,out var x); var yOk=double.TryParse(YInput.Text,out var y);
        var wOk=double.TryParse(WidthInput.Text,out var w); var hOk=double.TryParse(HeightInput.Text,out var h);
        var fOk=double.TryParse(FontInputSize.Text,out var f);
        var foregroundOk=TryColor(ColorInput.Text,out _); var backgroundOk=TryColor(BackgroundInput.Text,out _);
        var rotationOk=double.TryParse(RotationInput.Text,out var rotation);
        var borderWidthOk=double.TryParse(BorderWidthInput.Text,out var borderWidth);
        var borderColorOk=TryColor(BorderColorInput.Text,out _);
        var transitionSpeedOk=double.TryParse(TransitionSpeedInput.Text,out var transitionSpeed);
        var gradient=GradientInput.IsChecked==true;
        var gradientColorOk=TryColor(GradientColorInput.Text,out _);
        var gradientAngleOk=double.TryParse(GradientAngleInput.Text,out var gradientAngle);
        var opacityOk=double.TryParse(OpacityInput.Text,out var opacity);
        var gradientSource=(GradientSourceInput.SelectedItem as ColorSourceOption)?.Source;
        var borderSource=(BorderSourceInput.SelectedItem as ColorSourceOption)?.Source;
        var animation=AnimationInput.SelectedItem is ScorebugAnimation a?(ScorebugAnimation?)a:null;
        var transition=TransitionInput.SelectedItem is ScorebugTransition t?(ScorebugTransition?)t:null;
        var shape=ShapeInput.SelectedItem is ScorebugShape s?(ScorebugShape?)s:null;
        var visible=VisibleInput.IsChecked==true; var transparent=TransparentBackgroundInput.IsChecked==true;
        var mediaTag=MediaTagInput.Text.Trim();
        var preserveAspect=PreserveAspectInput.IsChecked==true;
        var aspectOk=double.TryParse(AspectInput.Text,out var aspect)&&aspect>0.05;
        var audioDevice=(AudioDeviceInput.SelectedItem as AudioOption)?.Id;
        var periodFormat=PeriodFormatInput.SelectedItem is PeriodOption period?(string?)period.Key:null;
        var icon=IconInput.SelectedItem is ScorebugIcon chosenIcon?(ScorebugIcon?)chosenIcon:null;
        var iconVisitors=IconVisitorsInput.IsChecked==true; var iconFlipped=IconFlipInput.IsChecked==true;
        var iconFillText=IconFillInput.Text.Trim();
        var iconFillOk=iconFillText.Length==0||TryColor(iconFillText,out _);
        var iconFillSource=(IconFillSourceInput.SelectedItem as ColorSourceOption)?.Source;
        var font=(FontInput.SelectedItem as FontChoice)?.Source;
        var bold=BoldInput.IsChecked==true; var italic=ItalicInput.IsChecked==true;
        var underline=UnderlineInput.IsChecked==true; var strike=StrikeInput.IsChecked==true;
        var foregroundSource=(ForegroundSourceInput.SelectedItem as ColorSourceOption)?.Source;
        var backgroundSource=(BackgroundSourceInput.SelectedItem as ColorSourceOption)?.Source;
        var logoSide=(LogoInput.SelectedItem as LogoOption)?.Side;
        // One element selected is the common case: leave the file alone when nothing actually changed.
        // With a group selected every field is written, because the group is allowed to differ.
        if(ids.Length==1&&item.Name==name&&item.Content==content&&(!xOk||item.X==x)&&(!yOk||item.Y==y)&&(!wOk||item.Width==w)&&(!hOk||item.Height==h)
            &&item.PreserveVideoAspect==preserveAspect&&(!aspectOk||Math.Abs(item.VideoAspect-aspect)<0.001)
            &&string.Equals(item.AudioDevice,audioDevice,StringComparison.Ordinal)
            &&(!fOk||item.FontSize==f)&&(!foregroundOk||item.Foreground==ColorInput.Text)&&(!backgroundOk||item.Background==BackgroundInput.Text)
            &&(animation is null||item.Animation==animation)&&(transition is null||item.Transition==transition)
            &&(shape is null||item.Shape==shape)&&item.Visible==visible&&item.TransparentBackground==transparent
            &&item.MediaTag==mediaTag&&(periodFormat is null||item.PeriodFormat==periodFormat)
            &&(icon is null||item.Icon==icon)&&item.IconVisitors==iconVisitors&&item.IconFlipped==iconFlipped
            &&(!iconFillOk||string.Equals(item.IconFillColor,iconFillText,StringComparison.OrdinalIgnoreCase))
            &&(iconFillSource is null||item.IconFillSource==iconFillSource)
            &&(font is null||item.FontFamily==font)&&item.Bold==bold&&item.Italic==italic&&item.Underline==underline&&item.StrikeThrough==strike
            &&(foregroundSource is null||item.ForegroundSource==foregroundSource)&&(backgroundSource is null||item.BackgroundSource==backgroundSource)
            &&(logoSide is null||item.LogoSide==logoSide)
            &&(!rotationOk||Math.Abs(item.Rotation-rotation)<0.001)&&(!borderWidthOk||Math.Abs(item.BorderThickness-borderWidth)<0.001)
            &&(!borderColorOk||item.BorderColor==BorderColorInput.Text)&&(borderSource is null||item.BorderSource==borderSource)
            &&item.BackgroundGradient==gradient&&(!gradientColorOk||item.BackgroundColor2==GradientColorInput.Text)
            &&(gradientSource is null||item.BackgroundSource2==gradientSource)&&(!gradientAngleOk||Math.Abs(item.GradientAngle-gradientAngle)<0.001)
            &&(!opacityOk||Math.Abs(item.Opacity-opacity)<0.001)
            &&(!transitionSpeedOk||Math.Abs(item.TransitionSeconds-transitionSpeed)<0.001))return;
        var id=item.Id;
        // Switching an element to live artwork changes what it draws, so that one edit has to build
        // the new content rather than paint over the old text.
        var artworkChanged=false;
        // The save raises Changed, which would rebuild the canvas and restart every video; this
        // edit paints in place through RefreshAllVisuals instead.
        _skipRebuild=true;
        try
        {
        _catalog.Update(layout=>{
            foreach(var target in layout.Elements.Where(z=>ids.Contains(z.Id)))
            {
                // Name and content belong to one element; everything else is the group's look.
                if(target.Id==id){target.Name=name;target.Content=content;}
                if(xOk)target.X=x; if(yOk)target.Y=y;
                var targetW=wOk?w:target.Width; var targetH=hOk?h:target.Height;
                // A preserved video box follows the picture's shape: moving one side moves the other.
                if(preserveAspect&&aspectOk)
                {
                    var widthChanged=Math.Abs(targetW-target.Width)>0.5;
                    var heightChanged=Math.Abs(targetH-target.Height)>0.5;
                    if(heightChanged&&!widthChanged) targetW=targetH*aspect;
                    else if(widthChanged) targetH=targetW/aspect;
                }
                target.Width=Math.Max(16,targetW); target.Height=Math.Max(16,targetH);
                if(fOk)target.FontSize=f;
                if(foregroundOk)target.Foreground=ColorInput.Text;
                if(backgroundOk)target.Background=BackgroundInput.Text;
                if(animation is not null)target.Animation=animation.Value;
                if(transition is not null)target.Transition=transition.Value;
                if(shape is not null)target.Shape=shape.Value;
                target.Visible=visible; target.TransparentBackground=transparent;
                if(target.Kind==ScorebugElementKind.Video)target.MediaTag=mediaTag;
                if(periodFormat is not null||target.PeriodFormat is not null)target.PeriodFormat=periodFormat;
                target.PreserveVideoAspect=preserveAspect;
                if(aspectOk)target.VideoAspect=Math.Clamp(aspect,0.1,10);
                target.AudioDevice=string.IsNullOrWhiteSpace(audioDevice)?null:audioDevice;
                if(icon is not null&&target.Icon!=icon.Value){target.Icon=icon.Value;artworkChanged=true;}
                target.IconVisitors=iconVisitors; target.IconFlipped=iconFlipped;
                if(iconFillOk)target.IconFillColor=iconFillText;
                if(iconFillSource is not null)target.IconFillSource=iconFillSource.Value;
                if(font is not null)target.FontFamily=font;
                target.Bold=bold; target.Italic=italic;
                target.Underline=underline; target.StrikeThrough=strike;
                if(foregroundSource is not null)target.ForegroundSource=foregroundSource.Value;
                if(backgroundSource is not null)target.BackgroundSource=backgroundSource.Value;
                target.BackgroundGradient=gradient;
                if(gradientSource is not null)target.BackgroundSource2=gradientSource.Value;
                if(gradientColorOk)target.BackgroundColor2=GradientColorInput.Text;
                if(gradientAngleOk)target.GradientAngle=Math.Clamp(gradientAngle,-360,360);
                if(opacityOk)target.Opacity=Math.Clamp(opacity,0,1);
                if(logoSide is not null)target.LogoSide=logoSide.Value;
                if(rotationOk)target.Rotation=Math.Clamp(rotation,-360,360);
                if(borderWidthOk)target.BorderThickness=Math.Clamp(borderWidth,0,40);
                if(borderColorOk)target.BorderColor=BorderColorInput.Text;
                if(borderSource is not null)target.BorderSource=borderSource.Value;
                if(transitionSpeedOk)target.TransitionSeconds=Math.Clamp(transitionSpeed,0,5);
                if(target.Kind==ScorebugElementKind.Logo&&target.LogoSide==ScorebugLogoSide.None)target.LogoSide=ScorebugLogoSide.Home;
                // A linked part carries the edit to every copy of it, on every layout and display.
                _catalog.PropagateLink(target);
            }
        });
        }
        finally { _skipRebuild=false; }
        if(artworkChanged)Render();
    }

    static bool TryColor(string value,out Color color){try{color=(Color)ColorConverter.ConvertFromString(value)!;return true;}catch{color=Colors.White;return false;}}

    // ---------- fonts ----------

    void ImportFont_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="Import a font",Filter="Fonts (*.ttf;*.otf;*.ttc)|*.ttf;*.otf;*.ttc|All files (*.*)|*.*"};
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var source=FontCatalog.Import(dialog.FileName);
            FontInput.ItemsSource=FontCatalog.All;
            var choice=FontCatalog.ChoiceFor(source);
            FontInput.SelectedItem=FontCatalog.All.FirstOrDefault(c=>string.Equals(c.Source,choice.Source,StringComparison.OrdinalIgnoreCase))??choice;
            SaveProperties();
            Render();
        }
        catch(Exception ex)
        {
            NoticeWindow.Tell(this,"That font could not be imported",ex.Message,warning:true);
        }
    }

    // ---------- displays ----------

    void DisplayBox_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_displayLoading||DisplayBox.SelectedItem is not DisplayOption option)return;
        var index=_catalog.Displays.FindIndex(display=>string.Equals(display.Id,option.Id,StringComparison.OrdinalIgnoreCase));
        if(index<0||index==_catalog.ActiveDisplayIndex)return;
        ClearSelection();
        _catalog.SelectDisplay(index);
    }

    void DisplayNew_Click(object sender,RoutedEventArgs e)
    {
        var name=PromptText("New display","Name this display. It starts with one blank layout and gets its own audience window.",$"Display {_catalog.Displays.Count+1}");
        if(string.IsNullOrWhiteSpace(name))return;
        ClearSelection();
        _catalog.AddDisplay(name);
        Render();
    }

    void DisplayRename_Click(object sender,RoutedEventArgs e)
    {
        var name=PromptText("Rename display","Display name",_catalog.ActiveDisplay.Name);
        if(string.IsNullOrWhiteSpace(name))return;
        _catalog.RenameDisplay(_catalog.ActiveDisplayIndex,name);
        Render();
    }

    void DisplayDelete_Click(object sender,RoutedEventArgs e)
    {
        if(_catalog.Displays.Count<=1)
        {
            NoticeWindow.Tell(this,"A game needs one display","A game needs at least one display, so the last one cannot be deleted.");
            return;
        }
        if(!ConfirmWindow.Ask(this,_theme,"DELETE DISPLAY",$"Delete the display \"{_catalog.ActiveDisplay.Name}\" and its {_catalog.Layouts.Count} layout(s)?","Delete display",destructive:true))return;
        ClearSelection();
        _catalog.RemoveDisplay(_catalog.ActiveDisplayIndex);
        Render();
    }

    /// <summary>Choosing another display only re-aims the link editor; it never writes a link yet.</summary>
    void LinkDisplay_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_linkLoading)return;
        var option=LinkDisplayBox.SelectedItem as LinkDisplayOption;
        _linkLoading=true;
        try { FillLinkLayouts(option?.Id,null); }
        finally { _linkLoading=false; }
        // Clearing the target clears the link, including the coming-back half; picking a layout
        // below sets it again.
        if(option?.Id is null)
        {
            ApplyReciprocal(false);
            _catalog.SetLink(_catalog.ActiveDisplay.Id,_catalog.ActiveIndex,null,null);
        }
    }

    void LinkLayout_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_linkLoading)return;
        if(LinkLayoutBox.SelectedItem is not LinkLayoutOption layout||layout.Id is null)return;
        if(LinkDisplayBox.SelectedItem is not LinkDisplayOption display||display.Id is null)return;
        // Read the tick first: clearing the old pair reloads the panel, which would reset it.
        var bothWays=LinkBothWaysInput.IsChecked==true;
        // A fresh target means any previous coming-back link belongs to the old pair.
        ApplyReciprocal(false);
        _catalog.SetLink(_catalog.ActiveDisplay.Id,_catalog.ActiveIndex,display.Id,layout.Id);
        if(bothWays)ApplyReciprocal(true);
    }

    void LinkClear_Click(object sender,RoutedEventArgs e)
    {
        ApplyReciprocal(false);
        _catalog.SetLink(_catalog.ActiveDisplay.Id,_catalog.ActiveIndex,null,null);
        Render();
    }

    /// <summary>A small themed name prompt, used by the display buttons.</summary>
    string? PromptText(string title,string hint,string initial)
    {
        var dialog=new Window{Title=title,Width=400,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false};
        var panel=new StackPanel{Margin=new Thickness(16)};
        panel.Children.Add(new TextBlock{Text=hint,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,0,2,8),Opacity=0.8});
        var input=new TextBox{Text=initial,Margin=new Thickness(2),FontSize=13};
        panel.Children.Add(input);
        var ok=new Button{Content="Save",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(2,10,2,0),MinHeight=34,Padding=new Thickness(14,6,14,6)};
        string? result=null;
        ok.Click+=(_,_)=>{result=input.Text.Trim();dialog.DialogResult=true;dialog.Close();};
        panel.Children.Add(ok);
        dialog.Content=panel;
        dialog.Loaded+=(_,_)=>{input.Focus();input.SelectAll();};
        ThemeManager.Apply(dialog,_theme);
        return dialog.ShowDialog()==true&&!string.IsNullOrWhiteSpace(result)?result:null;
    }

    // ---------- layouts ----------

    void LayoutNew_Click(object sender,RoutedEventArgs e)
    {
        _catalog.AddLayout($"Layout {_catalog.Layouts.Count+1}");
        ClearSelection();
        Render();
    }

    void LayoutDuplicate_Click(object sender,RoutedEventArgs e)
    {
        _catalog.AddLayout($"{_catalog.Layout.Name} copy",copyActive:true);
        ClearSelection();
        Render();
    }

    /// <summary>Sends the layout being edited to another display it should live on instead.</summary>
    void LayoutMove_Click(object sender,RoutedEventArgs e)
    {
        if(!PickLayoutTarget(copy:false)) return;
    }

    /// <summary>Puts a copy of the layout being edited on another display.</summary>
    void LayoutCopy_Click(object sender,RoutedEventArgs e)
    {
        if(!PickLayoutTarget(copy:true)) return;
    }

    bool PickLayoutTarget(bool copy)
    {
        if(_catalog.Displays.Count<=1)
        {
            LayoutHintText.Text="Add a second display to move or copy a layout onto it.";
            return false;
        }
        var index=_catalog.ActiveIndex;
        var target=DisplayPickerWindow.AskLayoutTarget(this,_catalog,_theme,copy);
        if(target is null)return false;
        var name=_catalog.FindDisplay(target)?.Name??"the other display";
        // Move takes the studio to the target so the layout stays in view; copy leaves it here.
        var done=copy
            ? _catalog.CopyLayoutToDisplay(index,target)>=0
            : _catalog.MoveLayoutToDisplay(index,target);
        ClearSelection();
        Render();
        LayoutHintText.Text=done
            ? (copy?$"Copied to {name}. This layout is unchanged.":$"Moved to {name}. This display keeps its other layouts.")
            : "Nothing changed — a display always keeps at least one layout.";
        return done;
    }

    void LayoutDelete_Click(object sender,RoutedEventArgs e)
    {
        if(_catalog.Layouts.Count<=1)
        {
            NoticeWindow.Tell(this,"A game needs one layout","A game needs at least one scorebug layout, so the last one cannot be deleted.");
            return;
        }
        if(!ConfirmWindow.Ask(this,_theme,"DELETE LAYOUT",$"Delete the layout \"{_catalog.Layout.Name}\"?","Delete layout",destructive:true))return;
        ClearSelection();
        _catalog.RemoveLayout(_catalog.ActiveIndex);
        Render();
    }

    void LayoutRename_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new Window{Title="Rename layout",Width=380,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(16)};
        panel.Children.Add(new TextBlock{Text="Layout name",Margin=new Thickness(4,0,4,6)});
        var input=new TextBox{Text=_catalog.Layout.Name,Margin=new Thickness(4)};
        panel.Children.Add(input);
        var ok=new Button{Content="Rename",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(4,10,4,0)};
        ok.Click+=(_,_)=>{_catalog.RenameLayout(_catalog.ActiveIndex,input.Text);dialog.DialogResult=true;dialog.Close();};
        panel.Children.Add(ok);
        dialog.Content=panel; ThemeManager.Apply(dialog,_theme);
        dialog.ShowDialog();
        Render();
    }

    // New artwork starts with no background box so it lands clean on the canvas; turn one on in the properties if needed.
    // ---------- files: layouts, displays and the web page ----------

    static string SafeFileName(string? name)
    {
        var text=new string((name??"").Where(ch=>!System.IO.Path.GetInvalidFileNameChars().Contains(ch)).ToArray()).Trim();
        return text.Length==0?"layout":text;
    }

    /// <summary>Writes the layout being designed to a ScorePlus Layout file, artwork and all.</summary>
    void LayoutExport_Click(object sender,RoutedEventArgs e)
    {
        var snapshot=_catalog.SnapshotLayout(_catalog.ActiveIndex);
        if(snapshot is null)return;
        var dialog=new SaveFileDialog
        {
            Title="Export layout",
            Filter=ScorebugFiles.LayoutFilter,
            DefaultExt=ScorebugFiles.LayoutExtension,
            FileName=SafeFileName(snapshot.Name)
        };
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            ScorebugFiles.SaveLayout(dialog.FileName,snapshot);
            NoticeWindow.Tell(this,"LAYOUT EXPORTED",$"\u201c{snapshot.Name}\u201d was written to {dialog.FileName}.");
        }
        catch(Exception ex) { NoticeWindow.Tell(this,"EXPORT FAILED",ex.Message,warning:true); }
    }

    /// <summary>Adds a ScorePlus Layout file to the display being edited, as its own independent layout.</summary>
    void LayoutImport_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="Import layout",Filter=ScorebugFiles.LayoutFilter,DefaultExt=ScorebugFiles.LayoutExtension};
        if(dialog.ShowDialog(this)!=true)return;
        var layout=ScorebugFiles.LoadLayout(dialog.FileName);
        if(layout is null)
        {
            NoticeWindow.Tell(this,"IMPORT FAILED","That file is not a ScorePlus Layout, so nothing was added.",warning:true);
            return;
        }
        var index=_catalog.ImportLayout(layout);
        NoticeWindow.Tell(this,"LAYOUT IMPORTED",$"\u201c{layout.Name}\u201d is now layout {index+1} on {_catalog.ActiveDisplay.Name}.");
    }

    /// <summary>Writes this display — every layout it holds — to a ScorePlus Display file.</summary>
    void DisplayExport_Click(object sender,RoutedEventArgs e)
    {
        var snapshot=_catalog.SnapshotDisplay(_catalog.ActiveDisplayIndex);
        if(snapshot is null)return;
        var dialog=new SaveFileDialog
        {
            Title="Export display",
            Filter=ScorebugFiles.DisplayFilter,
            DefaultExt=ScorebugFiles.DisplayExtension,
            FileName=SafeFileName(snapshot.Name)
        };
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            ScorebugFiles.SaveDisplay(dialog.FileName,snapshot);
            NoticeWindow.Tell(this,"DISPLAY EXPORTED",$"\u201c{snapshot.Name}\u201d with {snapshot.Layouts.Count} layout{(snapshot.Layouts.Count==1?"":"s")} was written to {dialog.FileName}.");
        }
        catch(Exception ex) { NoticeWindow.Tell(this,"EXPORT FAILED",ex.Message,warning:true); }
    }

    /// <summary>Adds every layout of a ScorePlus Display file as a new display, and edits it.</summary>
    void DisplayImport_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="Import display",Filter=ScorebugFiles.DisplayFilter,DefaultExt=ScorebugFiles.DisplayExtension};
        if(dialog.ShowDialog(this)!=true)return;
        var display=ScorebugFiles.LoadDisplay(dialog.FileName);
        if(display is null)
        {
            NoticeWindow.Tell(this,"IMPORT FAILED","That file is not a ScorePlus Display, so nothing was added.",warning:true);
            return;
        }
        var index=_catalog.ImportDisplay(display);
        NoticeWindow.Tell(this,"DISPLAY IMPORTED",$"\u201c{_catalog.Displays[index].Name}\u201d now has {_catalog.Displays[index].Layouts.Count} layout{(_catalog.Displays[index].Layouts.Count==1?"":"s")}.");
    }

    void AddText_Click(object sender,RoutedEventArgs e)=>Add(new ScorebugElement{Name="New text",Content="EDIT TEXT",X=480,Y=470,TransparentBackground=true});

    /// <summary>The baseball diamond as one element: three bases that light up with a runner on.</summary>
    void AddLogo_Click(object sender,RoutedEventArgs e)=>Add(new ScorebugElement{Name="New logo",Kind=ScorebugElementKind.Logo,LogoSide=ScorebugLogoSide.Home,Content="HOME LOGO",X=520,Y=470,Width=140,Height=90,TransparentBackground=true});

    /// <summary>
    /// Imports a still picture onto the graphic. The file is copied into the app's Images folder so
    /// the design keeps working after the original is moved, and the element starts at the picture's
    /// own shape instead of a guessed box.
    /// </summary>
    void AddImage_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog
        {
            Title="Add an image to the scorebug",
            Filter="Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files (*.*)|*.*"
        };
        if(dialog.ShowDialog()!=true)return;
        var path=dialog.FileName;
        try
        {
            var folder=AppPaths.Combine("Images");
            Directory.CreateDirectory(folder);
            var name=System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            var extension=System.IO.Path.GetExtension(dialog.FileName);
            var target=System.IO.Path.Combine(folder,$"{name}-{DateTime.Now:yyyyMMdd-HHmmss}{extension}");
            File.Copy(dialog.FileName,target,true);
            path=target;
        }
        catch
        {
            // A read-only source still draws from where it is; the design keeps a usable path.
        }
        var width=Math.Round(_catalog.Layout.Width*0.25);
        var height=width;
        try
        {
            var bitmap=new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.UriSource=new Uri(path); bitmap.EndInit();
            if(bitmap.PixelWidth>0&&bitmap.PixelHeight>0)height=Math.Max(16,Math.Round(width*(double)bitmap.PixelHeight/bitmap.PixelWidth));
        }
        catch
        {
            // An unreadable picture still adds the element so the box can be laid out now.
        }
        Add(new ScorebugElement
        {
            Name=System.IO.Path.GetFileNameWithoutExtension(path),
            Kind=ScorebugElementKind.Image,
            ImagePath=path,
            Content="",
            X=Math.Round((_catalog.Layout.Width-width)/2),
            Y=Math.Round((_catalog.Layout.Height-height)/2),
            Width=width,
            Height=height,
            TransparentBackground=true
        });
    }
    void AddShape_Click(object sender,RoutedEventArgs e)
    {
        var picker=new Window{Title="Add shape",Width=340,Height=190,ResizeMode=ResizeMode.NoResize,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(14)};
        panel.Children.Add(new TextBlock{Text="Shape",Margin=new Thickness(4)});
        var shape=new ComboBox{ItemsSource=Enum.GetValues<ScorebugShape>(),SelectedItem=ScorebugShape.Rectangle,Margin=new Thickness(4)};
        var add=new Button{Content="Add shape",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right};
        add.Click+=(_,_)=>{
            var kind=(ScorebugShape)(shape.SelectedItem??ScorebugShape.Rectangle);
            Add(new ScorebugElement{Name=$"New {kind.ToString().ToLowerInvariant()}",Kind=ScorebugElementKind.Shape,Shape=kind,Content="",X=450,Y=470,Width=kind==ScorebugShape.Rectangle?200:140,Height=kind==ScorebugShape.Rectangle?100:140,TransparentBackground=true,BorderThickness=2});
            picker.DialogResult=true; picker.Close();
        };
        panel.Children.Add(shape); panel.Children.Add(add); picker.Content=panel; ThemeManager.Apply(picker,_theme); picker.ShowDialog();
    }

    void AddData_Click(object sender,RoutedEventArgs e)
    {
        // Every token the binder understands, so no data is missing from the studio.
        var choices=new List<(string,string)>
        {
            ("Team · home name","{{HOME_TEAM}}"),("Team · visitors name","{{AWAY_TEAM}}"),
            ("Team · home abbreviation","{{HOME_ABBR}}"),("Team · visitors abbreviation","{{AWAY_ABBR}}"),
            ("Score · home","{{HOME_SCORE}}"),("Score · visitors","{{AWAY_SCORE}}"),
            ("Period (quarter)","{{PERIOD}}"),("Game clock","{{GAME_CLOCK}}"),("Play clock","{{PLAY_CLOCK}}"),
            ("Timeout clock","{{TIMEOUT_CLOCK}}"),("Timeouts left","{{TIMEOUTS}}"),("Possession","{{POSSESSION}}"),
            ("Sport name","{{SPORT}}"),("Date","{{DATE}}"),("Time of day","{{TIME}}")
        };
        // Only the sport being played puts its own words in this menu: a hockey game should never
        // offer "Football · down", and a baseball game should never offer soccer corners.
        void Sport(string id,params (string Label,string Token)[] items)
        {
            if(!string.Equals(_engine.State.SportId,id,StringComparison.OrdinalIgnoreCase))return;
            choices.AddRange(items);
        }
        Sport("football",
            ("Football · down","{{DOWN}}"),("Football · to go","{{TO_GO}}"),("Football · down & distance","{{DOWNS}}"),
            ("Football · yards to go","{{YARDS_TO_GO}}"),("Football · ball on (number)","{{BALL_ON}}"),
            ("Football · ball on side","{{BALL_SIDE}}"),("Football · ball on side + number","{{BALL_SIDE_SPOT}}"),
            ("Football · ball side team name","{{BALL_TEAM}}"),("Football · ball spot","{{BALL_SPOT}}"),
            ("Football · team yards","{{YARDS}}"),("Football · home yards","{{HOME_YARDS}}"),("Football · visitors yards","{{AWAY_YARDS}}"),
            ("Football · turnovers","{{TURNOVERS}}"));
        Sport("basketball",("Basketball · shot clock","{{SHOT_CLOCK}}"),("Basketball · fouls","{{BASKETBALL_FOULS}}"),("Basketball · fouls (both)","{{FOULS}}"));
        Sport("baseball",("Baseball · count & outs","{{BASEBALL_COUNT}}"),("Baseball · hits","{{HITS}}"),("Baseball · outs","{{BASEBALL_OUTS}}"),
            ("Baseball · bases (diamond)",BasesMarker));
        Sport("soccer",("Soccer · cards","{{SOCCER_CARDS}}"),("Soccer · cards (short)","{{CARDS}}"),("Soccer · shots","{{SHOTS}}"),("Soccer · corners","{{CORNERS}}"));
        // The sport's own values, first: bases, count, power play, overs and wickets all belong here.
        foreach(var control in _engine.State.Controls)
            choices.Add(($"{_engine.Sport.Name} · {control.Label}",$"{{{{SPORT:{control.Id}}}}}"));
        foreach(var stat in _engine.State.Stats)choices.Add(($"Custom stat · {stat.Name}",$"{{{{STAT:{stat.Name}}}}}"));
        choices.Add(("Custom stat · any name","{{STAT:Name}}"));
        var picker=new Window{Title="Add sport data",Width=380,Height=470,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var list=new ListBox{ItemsSource=choices.Select(x=>x.Item1).ToArray(),Margin=new Thickness(10)};
        var add=new Button{Content="Add selected data",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right};
        add.Click+=(_,_) =>
        {
            if(list.SelectedIndex<0)return;
            var token=choices[list.SelectedIndex].Item2;
            // The bases diamond is artwork rather than a printed value, so it is added as an
            // icon element instead of a text element holding a token.
            if(token==BasesMarker) Add(new ScorebugElement
            {
                Name="Bases",
                Content="",
                Icon=ScorebugIcon.Bases,
                X=420,
                Y=430,
                Width=220,
                Height=150,
                TransparentBackground=true
            });
            else Add(new ScorebugElement{Name=choices[list.SelectedIndex].Item1,Content=token,X=420,Y=470,TransparentBackground=true});
            picker.DialogResult=true;picker.Close();
        };
        picker.Content=new DockPanel{Children={add,list}}; DockPanel.SetDock(add,Dock.Bottom); ThemeManager.Apply(picker,_theme); picker.ShowDialog();
    }

    /// <summary>
    /// Player data: a value read from whoever is selected for a side in the Rosters tab. Every
    /// element added here follows the selection, so switching players reprints them all.
    /// </summary>
    void AddPlayerData_Click(object sender,RoutedEventArgs e)
    {
        var choices=new List<(string Label,string Token)>();
        void Side(bool home,string label)
        {
            var prefix=home?"HOME":"AWAY";
            var selected=home?_engine.State.HomePlayer:_engine.State.AwayPlayer;
            var roster=RosterStore.Book.Find(home?_engine.State.HomeRosterId:_engine.State.AwayRosterId);
            var who=selected is null?"none on the graphic"
                :selected.Number>0?$"#{selected.Number} {selected.Name}":selected.Name;
            foreach(var field in new[]{"Name","Number","Position"})
                choices.Add(($"{label} · {field}  ({who})",$"{{{{PLAYER:{prefix}:{field}}}}}"));
            var stats=(selected?.Stats.Select(s=>s.Name)??Enumerable.Empty<string>())
                .Concat(roster?.StatNames()??[])
                .Concat(RosterStore.Book.StatNames())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name=>name,StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach(var stat in stats)
                choices.Add(($"{label} · {stat}  ({who})",$"{{{{PLAYER:{prefix}:{stat}}}}}"));
        }
        Side(true,"Selected player · home");
        Side(false,"Selected player · visitors");

        var picker=new Window{Title="Add player data",Width=470,Height=540,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var list=new ListBox{ItemsSource=choices.Select(x=>x.Label).ToArray(),Margin=new Thickness(10,6,10,4)};
        // The first value is ready to add, so the button never sits there doing nothing.
        if(list.Items.Count>0)list.SelectedIndex=0;
        var add=new Button{Content="Add selected data",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,10,10)};
        (string Label,string Token)? chosen=null;
        add.Click+=(_,_)=>{if(list.SelectedIndex<0)return;chosen=choices[list.SelectedIndex];picker.DialogResult=true;picker.Close();};
        var hint=new TextBlock
        {
            Text="These follow the player selected for that side in the Rosters tab, so switching players reprints the element. To pin one exact player instead, add it and choose them under PLAYER DATA in the properties panel.",
            TextWrapping=TextWrapping.Wrap,FontSize=10,Opacity=0.7,Margin=new Thickness(10,0,10,4)
        };
        var note=new TextBlock
        {
            Text="Need a value that is not listed? Add it as a stat column in the ROSTERS tab, or as a custom stat on the console — every one of those appears here.",
            TextWrapping=TextWrapping.Wrap,FontSize=10,Opacity=0.7,Margin=new Thickness(10,0,10,8)
        };
        var intro=new TextBlock{Text="Pick the value this element prints. Name, Number and Position come with every roster; the rest are that team's stat columns.",TextWrapping=TextWrapping.Wrap,FontSize=11,Opacity=0.8,Margin=new Thickness(10,10,10,2)};
        var bottom=new StackPanel();
        bottom.Children.Add(hint); bottom.Children.Add(note); bottom.Children.Add(add);
        var dock=new DockPanel{LastChildFill=true};
        DockPanel.SetDock(bottom,Dock.Bottom);
        DockPanel.SetDock(intro,Dock.Top);
        dock.Children.Add(intro); dock.Children.Add(bottom); dock.Children.Add(list);
        picker.Content=dock; ThemeManager.Apply(picker,_theme); picker.ShowDialog();
        if(chosen is {} pick)
            Add(new ScorebugElement{Name=pick.Label,Content=pick.Token,X=420,Y=470,TransparentBackground=true});
    }

    /// <summary>A video box that only shows clips carrying its tag.</summary>
    /// <summary>A box the operator fills with HTML/CSS; a browser engine draws it on every screen.</summary>
    void AddHtml_Click(object sender,RoutedEventArgs e)=>Add(new ScorebugElement
    {
        Name="HTML widget",
        Kind=ScorebugElementKind.Html,
        Content="<div style=\"padding:14px 22px;font:600 34px Segoe UI,Arial;color:#38D7A3;background:rgba(9,16,24,.74);border-radius:12px\">HTML</div>",
        X=420,Y=520,Width=440,Height=150,TransparentBackground=true
    });

    void AddVideo_Click(object sender,RoutedEventArgs e)
    {
        var picker=new Window{Title="Add video element",Width=380,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(23,28,37)),Foreground=Brushes.White};
        var panel=new StackPanel{Margin=new Thickness(14)};
        panel.Children.Add(new TextBlock{Text="Which media tag should play inside this element?",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,0,4,8)});
        var tag=new ComboBox{IsEditable=true,Text=MediaLibrary.AnyTag,ItemsSource=MediaLibrary.SuggestedTags.ToArray(),Margin=new Thickness(4)};
        panel.Children.Add(tag);
        panel.Children.Add(new TextBlock{Text="ANY plays whatever clip is cued, whatever tag it carries. Name a tag (Ads, Highlights…) to show only that tag's clips.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(4,6,4,0)});
        var add=new Button{Content="Add video box",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(4,12,4,0)};
        add.Click+=(_,_)=>{
            var width=Math.Round(_catalog.Layout.Width*0.36); var height=Math.Round(width*9/16);
            Add(new ScorebugElement{
                Name=$"Video · {MediaLibrary.NormalizeTag(tag.Text)}",Kind=ScorebugElementKind.Video,Content="",
                MediaTag=MediaLibrary.NormalizeTag(tag.Text),
                X=Math.Round((_catalog.Layout.Width-width)/2),Y=Math.Round((_catalog.Layout.Height-height)/2),
                Width=width,Height=height,TransparentBackground=true});
            picker.DialogResult=true; picker.Close();
        };
        panel.Children.Add(add); picker.Content=panel; ThemeManager.Apply(picker,_theme); picker.ShowDialog();
    }

    static int PresetIndex(int width,int height)
    {
        var index=Array.FindIndex(ScorebugLayout.SizePresets,p=>p.Width==width&&p.Height==height);
        return index<0?0:index;
    }

    void CanvasPreset_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_canvasLoading||CanvasPreset.SelectedIndex<0)return;
        var preset=ScorebugLayout.SizePresets[CanvasPreset.SelectedIndex];
        ApplyCanvasSize(preset.Width,preset.Height);
    }

    void CanvasResize_Click(object sender,RoutedEventArgs e)
    {
        var width=int.TryParse(CanvasWidthInput.Text,out var w)?w:_catalog.Layout.Width;
        var height=int.TryParse(CanvasHeightInput.Text,out var h)?h:_catalog.Layout.Height;
        ApplyCanvasSize(width,height);
    }

    /// <summary>Changing the canvas size scales every element with it, so a 720p layout survives a move to 4K.</summary>
    void ApplyCanvasSize(int width,int height)
    {
        if(width<320||height<240)return;
        _catalog.Resize(width,height);
        Render();
    }

    void Add(ScorebugElement item)
    {
        _catalog.Update(layout=>layout.Elements.Add(item));
        ClearSelection();
        _selectedIds.Add(item.Id);
        _selectedId=item.Id;
        Render();
    }

    /// <summary>
    /// Copies elements from another layout into this one. Each copy gets a new identity and is
    /// nudged clear of the original position, so a score bar built once can be reused everywhere
    /// instead of rebuilding a dozen near-identical layouts.
    /// </summary>
    void AddMany(IEnumerable<ScorebugElement> items)
    {
        var source=items.ToArray();
        if(source.Length==0)return;
        _catalog.Update(layout=>
        {
            foreach(var item in source)
            {
                var copy=JsonSerializer.Deserialize<ScorebugElement>(JsonSerializer.Serialize(item))!;
                copy.Id=Guid.NewGuid().ToString("N");
                // The original and its copy become one part: move or restyle either and the
                // other follows, on any layout. Break the link in the properties to split them.
                LinkCopies(item,copy);
                var original=_catalog.Displays.SelectMany(d=>d.Layouts).SelectMany(l=>l.Elements)
                    .FirstOrDefault(x=>string.Equals(x.Id,item.Id,StringComparison.OrdinalIgnoreCase));
                if(original is not null)original.LinkId=item.LinkId;
                copy.Width=Math.Clamp(copy.Width,24,layout.Width);
                copy.Height=Math.Clamp(copy.Height,24,layout.Height);
                copy.X=Math.Clamp(copy.X+24,0,Math.Max(0,layout.Width-copy.Width));
                copy.Y=Math.Clamp(copy.Y+24,0,Math.Max(0,layout.Height-copy.Height));
                layout.Elements.Add(copy);
            }
        });
        ClearSelection();
        foreach(var element in _catalog.Layout.Elements.TakeLast(source.Length))
        {
            _selectedIds.Add(element.Id);
            _selectedId=element.Id;
        }
        Render();
    }

    /// <summary>Pick another layout, tick the parts worth reusing, and they land on this canvas.</summary>
    void ImportParts_Click(object sender,RoutedEventArgs e)
    {
        var current=_catalog.Layout;
        var sources=_catalog.Displays
            .SelectMany(display=>display.Layouts.Select(layout=>(Display:display.Name,Layout:layout)))
            .Where(entry=>!ReferenceEquals(entry.Layout,current)&&entry.Layout.Elements.Count>0)
            .ToArray();
        if(sources.Length==0)
        {
            NoticeWindow.Tell(this,"NOTHING TO COPY YET","This game has one layout, and it has no artwork to borrow from another. Add elements to this layout, or start a second layout, and its parts appear here.");
            return;
        }
        var dialog=new Window
        {
            Title="Add parts from another layout",Width=560,Height=560,MinWidth=460,MinHeight=420,
            Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.FromRgb(0x17,0x1C,0x25)),Foreground=Brushes.White,
            FontFamily=new FontFamily("Segoe UI")
        };
        var panel=new DockPanel{Margin=new Thickness(14)};
        var intro=new TextBlock
        {
            Text="Copy artwork from any layout in this game. The copies are independent — editing them here never touches the layout they came from.",
            TextWrapping=TextWrapping.Wrap,FontSize=11,Opacity=0.75,Margin=new Thickness(2,0,2,8)
        };
        var sourceBox=new ComboBox{ItemsSource=sources.Select(s=>$"{s.Display}  ·  {s.Layout.Name}").ToArray(),SelectedIndex=0,Margin=new Thickness(0,0,0,8)};
        var list=new ListBox{SelectionMode=SelectionMode.Extended,Margin=new Thickness(0,0,0,8)};
        var count=new TextBlock{FontSize=11,Opacity=0.6,Margin=new Thickness(2,0,2,0)};
        void Fill()
        {
            var chosen=sources[Math.Max(0,sourceBox.SelectedIndex)];
            list.ItemsSource=chosen.Layout.Elements.AsEnumerable().Reverse().Select(item=>new PartItem(item,$"{KindName(item)} · {item.Name}")).ToArray();
            count.Text=$"{chosen.Layout.Elements.Count} part{(chosen.Layout.Elements.Count==1?"":"s")} in {chosen.Layout.Name}. Ctrl-click to pick several.";
        }
        sourceBox.SelectionChanged+=(_,_)=>Fill();
        Fill();
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="Cancel",MinWidth=90,Margin=new Thickness(4,0,0,0)};
        cancel.Click+=(_,_)=>dialog.Close();
        var add=new Button{Content="Add selected parts",IsDefault=true,MinWidth=150,Margin=new Thickness(6,0,0,0),Background=new SolidColorBrush(Color.FromRgb(0x0B,0x6B,0x50)),BorderBrush=new SolidColorBrush(Color.FromRgb(0x14,0xA6,0x7C)),Foreground=Brushes.White};
        ThemeManager.SetSkipTheme(add,true);
        add.Click+=(_,_)=>
        {
            if(list.SelectedItems.Count==0)return;
            AddMany(list.SelectedItems.Cast<PartItem>().Select(part=>part.Element));
            dialog.DialogResult=true;
            dialog.Close();
        };
        actions.Children.Add(cancel);
        actions.Children.Add(add);
        DockPanel.SetDock(intro,Dock.Top);
        DockPanel.SetDock(sourceBox,Dock.Top);
        DockPanel.SetDock(actions,Dock.Bottom);
        DockPanel.SetDock(count,Dock.Bottom);
        panel.Children.Add(intro);
        panel.Children.Add(sourceBox);
        panel.Children.Add(actions);
        panel.Children.Add(count);
        panel.Children.Add(list);
        dialog.Content=panel;
        ThemeManager.Apply(dialog,_theme);
        dialog.ShowDialog();
    }

    static string KindName(ScorebugElement item)=>item.Kind switch
    {
        ScorebugElementKind.Logo=>"Logo",
        ScorebugElementKind.Shape=>"Shape",
        ScorebugElementKind.Video=>"Video",
        _=>"Text"
    };

    sealed record PartItem(ScorebugElement Element,string Text){ public override string ToString()=>Text; }

    /// <summary>Deletes every selected object at once — the Remove button and the Delete key.</summary>
    void RemoveSelected()
    {
        if(_selectedIds.Count==0)return;
        var ids=_selectedIds.ToArray();
        ClearSelection();
        _catalog.Update(layout=>layout.Elements.RemoveAll(x=>ids.Contains(x.Id)));
        Render();
    }

    void Remove_Click(object sender,RoutedEventArgs e) => RemoveSelected();

    /// <summary>Leaves this copy alone from now on; its linked siblings keep following each other.</summary>
    void BreakLink_Click(object sender,RoutedEventArgs e)
    {
        var item=Selected;
        if(item is null||string.IsNullOrWhiteSpace(item.LinkId))return;
        var id=item.Id;
        _catalog.Update(layout=>
        {
            var target=layout.Elements.FirstOrDefault(x=>x.Id==id);
            if(target is not null)target.LinkId=null;
        });
        LoadProperties();
    }

    void Element_MouseDown(object sender,MouseButtonEventArgs e)
    {
        if(_resizing||sender is not Border host||host.Tag is not ScorebugElement item)return;
        var id=item.Id;
        var ctrl=(Keyboard.Modifiers&ModifierKeys.Control)!=0;
        var shift=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;
        if(ctrl||shift)
        {
            // Ctrl-click adds this object to the selection, or takes it back out again; Shift-click
            // only ever adds, so a whole group can be gathered one object at a time.
            if(ctrl&&_selectedIds.Contains(id))
            {
                _selectedIds.Remove(id);
                if(_selectedId==id)_selectedId=_selectedIds.LastOrDefault();
            }
            else if(!_selectedIds.Contains(id))
            {
                _selectedIds.Add(id);
                _selectedId=id;
            }
        }
        else if(!_selectedIds.Contains(id))
        {
            _selectedIds.Clear();
            _selectedIds.Add(id);
            _selectedId=id;
        }
        else _selectedId=id; // dragging one of several keeps the whole group together
        SyncLayersFromSelection();
        RefreshAllVisuals();
        LoadProperties();
        if(_selectedIds.Count==0||_selectedId!=id)return;
        _dragElement=item;
        _dragStartPoint=e.GetPosition(DesignCanvas);
        _dragOrigins.Clear();
        foreach(var selectedId in _selectedIds)
        {
            var element=_catalog.Layout.Elements.FirstOrDefault(x=>x.Id==selectedId);
            if(element is not null)_dragOrigins[selectedId]=(element.X,element.Y);
        }
        host.CaptureMouse();
        e.Handled=true;
    }

    void Element_MouseMove(object sender,MouseEventArgs e)
    {
        if(_resizing||_dragElement is null||e.LeftButton!=MouseButtonState.Pressed)return;
        var p=e.GetPosition(DesignCanvas);
        var dx=p.X-_dragStartPoint.X;
        var dy=p.Y-_dragStartPoint.Y;
        // The whole selection moves by the same offset, each box still kept inside the artwork.
        foreach(var (selectedId,origin) in _dragOrigins)
        {
            var element=_catalog.Layout.Elements.FirstOrDefault(x=>x.Id==selectedId);
            if(element is null)continue;
            var x=Math.Clamp(origin.X+dx,0,Math.Max(0,_catalog.Layout.Width-element.Width));
            var y=Math.Clamp(origin.Y+dy,0,Math.Max(0,_catalog.Layout.Height-element.Height));
            element.X=x; element.Y=y;
            if(_visuals.TryGetValue(selectedId,out var visual)){Canvas.SetLeft(visual.Host,x);Canvas.SetTop(visual.Host,y);}
        }
        if(_dragElement is { } primary&&!string.IsNullOrEmpty(primary.Id))
        {
            var current=_catalog.Layout.Elements.FirstOrDefault(x=>x.Id==primary.Id);
            if(current is not null&&!_loading){_loading=true;XInput.Text=current.X.ToString("0");YInput.Text=current.Y.ToString("0");_loading=false;}
        }
    }

    void Element_MouseUp(object sender,MouseButtonEventArgs e)
    {
        if(_resizing)return;
        if(sender is Border host)host.ReleaseMouseCapture();
        SaveDrag();
        e.Handled=true;
    }

    /// <summary>Writes every dragged object's new position in one catalog edit.</summary>
    void SaveDrag()
    {
        if(_dragElement is null)return;
        _dragElement=null;
        var moved=_catalog.Layout.Elements
            .Where(x=>_dragOrigins.ContainsKey(x.Id))
            .ToDictionary(x=>x.Id,x=>(x.X,x.Y));
        _dragOrigins.Clear();
        if(moved.Count==0)return;
        _catalog.Update(layout=>
        {
            foreach(var (id,position) in moved)
            {
                var target=layout.Elements.FirstOrDefault(z=>z.Id==id);
                if(target is not null){target.X=position.X;target.Y=position.Y;_catalog.PropagateLink(target);}
            }
        });
    }

    void Canvas_MouseMove(object sender,MouseEventArgs e) { }

    void Canvas_MouseUp(object sender,MouseButtonEventArgs e)
    {
        if(_resizing)return;
        SaveDrag();
    }

    void Resize_MouseDown(object sender,MouseButtonEventArgs e)
    {
        if(sender is not Border handle||handle.Tag is not ScorebugElement item)return;
        _dragElement=item; _resizing=true;
        _selectedIds.Clear(); _selectedIds.Add(item.Id); _selectedId=item.Id;
        SyncLayersFromSelection();
        RefreshAllVisuals();
        LoadProperties();
        var origin=e.GetPosition(DesignCanvas);
        _resizeStartX=origin.X; _resizeStartY=origin.Y;
        _resizeStartWidth=item.Width; _resizeStartHeight=item.Height;
        _resizeStartFontSize=item.FontSize;
        handle.CaptureMouse();
        e.Handled=true;
    }

    void Resize_MouseMove(object sender,MouseEventArgs e)
    {
        if(_dragElement is null||!_resizing||e.LeftButton!=MouseButtonState.Pressed)return;
        var p=e.GetPosition(DesignCanvas);
        var maxWidth=Math.Max(24,_catalog.Layout.Width-_dragElement.X);
        var maxHeight=Math.Max(24,_catalog.Layout.Height-_dragElement.Y);
        var width=Math.Clamp(_resizeStartWidth+(p.X-_resizeStartX),24,maxWidth);
        var height=Math.Clamp(_resizeStartHeight+(p.Y-_resizeStartY),24,maxHeight);
        var ctrl=(Keyboard.Modifiers&ModifierKeys.Control)!=0;
        var shift=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;
        // A video box that preserves its aspect ratio can only be resized along one diagonal;
        // Shift asks for the same thing on any other element.
        var lockedAspect=_dragElement.PreserveVideoAspect&&_dragElement.VideoAspect>0.05
            ? _dragElement.VideoAspect
            : shift ? _resizeStartWidth/Math.Max(1,_resizeStartHeight) : 0;
        if(lockedAspect>0)
        {
            height=Math.Clamp(width/lockedAspect,24,maxHeight);
            width=Math.Clamp(height*lockedAspect,24,maxWidth);
            height=width/lockedAspect;
        }
        _dragElement.Width=width; _dragElement.Height=height;
        // Text and the possession dot keep their size as the box grows, so a value never jumps a
        // few points because its plate was nudged. Ctrl makes the type follow the box instead.
        if(ctrl&&_resizeStartHeight>0.5)
            _dragElement.FontSize=Math.Clamp(_resizeStartFontSize*(height/_resizeStartHeight),8,480);
        if(_visuals.TryGetValue(_dragElement.Id,out var visual))
        {
            visual.Host.Width=width; visual.Host.Height=height;
            visual.Content.Width=double.NaN; visual.Content.Height=double.NaN;
        }
        if(ctrl)RefreshAllVisuals();
        else if(_visuals.TryGetValue(_dragElement.Id,out var resized))
            ScorebugRender.ApplyToContent(resized.Content,_dragElement,_engine.State);
        if(!_loading)
        {
            _loading=true;
            WidthInput.Text=width.ToString("0"); HeightInput.Text=height.ToString("0");
            if(FindName("FontSizeInput") is TextBox fontSizeBox)fontSizeBox.Text=_dragElement.FontSize.ToString("0.#");
            _loading=false;
        }
        e.Handled=true;
    }

    void Resize_MouseUp(object sender,MouseButtonEventArgs e)
    {
        if(sender is Border handle)handle.ReleaseMouseCapture();
        if(_dragElement is { } item)
        {
            var id=item.Id; var w=item.Width; var h=item.Height;
            _dragElement=null; _resizing=false;
            _catalog.Update(layout=>{var target=layout.Elements.FirstOrDefault(z=>z.Id==id);if(target is not null){target.Width=Math.Clamp(w,24,layout.Width);target.Height=Math.Clamp(h,24,layout.Height);_catalog.PropagateLink(target);}});
        }
        _resizing=false;
        e.Handled=true;
    }

    void Layers_MouseDown(object sender,MouseButtonEventArgs e)=>_listStart=e.GetPosition(null);

    void Layers_MouseMove(object sender,MouseEventArgs e)
    {
        if(e.LeftButton!=MouseButtonState.Pressed||Layers.SelectedItem is not LayerItem item)return;
        var p=e.GetPosition(null);
        if(Math.Abs(p.X-_listStart.X)>8||Math.Abs(p.Y-_listStart.Y)>8)DragDrop.DoDragDrop(Layers,item.Element.Id,DragDropEffects.Move);
    }

    void Layers_Drop(object sender,DragEventArgs e)
    {
        if(e.Data.GetData(typeof(string)) is not string movedId)return;
        var p=e.GetPosition(Layers);
        var target=Layers.InputHitTest(p) as DependencyObject;
        while(target is not null&&target is not ListBoxItem)target=VisualTreeHelper.GetParent(target);
        if(target is not ListBoxItem row||row.Content is not LayerItem drop)return;
        var from=_catalog.Layout.Elements.FindIndex(x=>x.Id==movedId);
        var to=_catalog.Layout.Elements.FindIndex(x=>x.Id==drop.Element.Id);
        if(from<0||to<0||from==to)return;
        _catalog.Update(layout=>{var item=layout.Elements[from];layout.Elements.RemoveAt(from);layout.Elements.Insert(Math.Clamp(to,0,layout.Elements.Count),item);});
    }

    void ChoosePresetColor(TextBox target)
    {
        var picked=ColorPickerWindow.Pick(this,target.Text);
        if(picked is not null){target.Text=picked;SaveProperties();RefreshAllVisuals();}
    }

    /// <summary>
    /// Shows or hides the explanatory lines in the studio. Preferences turns them off by default,
    /// because a broadcast operator who knows the room does not need a paragraph beside every panel.
    /// </summary>
    void ApplyTipVisibility(bool show)
    {
        var visibility=show?Visibility.Visible:Visibility.Collapsed;
        FrameworkElement[] tips=
        [
            HeaderHint,DisplayHintText,TransitionCaption,LinkTipText,LayersHintText,
            CanvasBackgroundHintText,PlayerDataHintText,LogoHintText,VideoTagHintText,AudioHintText,
            CanvasFitTipText,ResizeTipText,AllValuesHintText
        ];
        foreach(var tip in tips)if(tip is not null)tip.Visibility=visibility;
    }

    // ---------- the artwork backdrop ----------

    /// <summary>Puts the backdrop back to the ScorePlus navy a layout starts with.</summary>
    void CanvasBackgroundNavy_Click(object sender,RoutedEventArgs e)=>SetCanvasBackground("#FF091018");
    void CanvasBackgroundBlack_Click(object sender,RoutedEventArgs e)=>SetCanvasBackground("#FF000000");
    void CanvasBackground_Click(object sender,RoutedEventArgs e)
    {
        var picked=ColorPickerWindow.Pick(this,_catalog.Layout.Background);
        if(picked is not null)SetCanvasBackground(picked);
    }

    /// <summary>Typing a valid colour repaints the backdrop as you go; half-typed hex is ignored.</summary>
    void CanvasBackgroundInput_Changed(object sender,TextChangedEventArgs e)
    {
        if(_canvasLoading)return;
        var text=CanvasBackgroundInput.Text.Trim();
        if(TryColor(text,out _))SetCanvasBackground(text);
    }

    /// <summary>Sets the backdrop on this layout only, for the studio, the preview and the audience.</summary>
    void SetCanvasBackground(string color)
    {
        if(string.Equals(_catalog.Layout.Background,color,StringComparison.OrdinalIgnoreCase))return;
        // The catalog's Changed event repaints the studio, so the backdrop appears without a reopen.
        _catalog.Update(layout=>layout.Background=color);
    }

    void TextColor_Click(object sender,RoutedEventArgs e)=>ChoosePresetColor(ColorInput);
    void BackgroundColor_Click(object sender,RoutedEventArgs e)=>ChoosePresetColor(BackgroundInput);
    void BorderColor_Click(object sender,RoutedEventArgs e)=>ChoosePresetColor(BorderColorInput);
    void GradientColor2_Click(object sender,RoutedEventArgs e)=>ChoosePresetColor(GradientColorInput);
    void IconFillColor_Click(object sender,RoutedEventArgs e)=>ChoosePresetColor(IconFillInput);
    void Done_Click(object sender,RoutedEventArgs e)=>Close();

    // Enter inside a property field commits that value. It must never close the studio.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if(e.Key==Key.Enter&&(Keyboard.FocusedElement is TextBox||e.OriginalSource is TextBox))
        {
            _propertySave.Stop(); SaveProperties(); RefreshAllVisuals();
            e.Handled=true;
            return;
        }
        // Escape drops the selection, so the operator never has to click empty canvas to get out.
        // It also lets go of a property box, which is why focus is cleared first.
        if(e.Key==Key.Escape)
        {
            if(_selectedIds.Count>0)
            {
                Keyboard.ClearFocus();
                ClearSelection();
                SyncLayersFromSelection();
                RefreshAllVisuals();
                LoadProperties();
                e.Handled=true;
                return;
            }
        }
        // Delete removes every selected object, but never while the operator is typing a value.
        if(e.Key==Key.Delete&&Keyboard.FocusedElement is not TextBox&&e.OriginalSource is not TextBox)
        {
            if(_selectedIds.Count>0)
            {
                RemoveSelected();
                e.Handled=true;
                return;
            }
        }
        base.OnPreviewKeyDown(e);
    }
    protected override void OnClosed(EventArgs e)
    {
        _propertySave.Stop(); SaveProperties();
        foreach(var surface in _videoSurfaces.Values) surface.Dispose();
        _videoSurfaces.Clear();
        _wallClock.Stop();
        _catalog.Changed-=Render; _engine.Changed-=RefreshBoundText;
        base.OnClosed(e);
    }
    /// <summary>Moves the selected element one step towards the front (+1) or back (-1) of the picture.</summary>
    void MoveSelectedLayer(int delta)
    {
        if(Selected is not { } item) return;
        var from=_catalog.Layout.Elements.IndexOf(item);
        if(from<0) return;
        var to=Math.Clamp(from+delta,0,_catalog.Layout.Elements.Count-1);
        if(from==to) return;
        _catalog.Update(layout=>{var moved=layout.Elements[from];layout.Elements.RemoveAt(from);layout.Elements.Insert(to,moved);});
    }

    void LayerFront_Click(object sender,RoutedEventArgs e)=>MoveSelectedLayer(1);
    void LayerBack_Click(object sender,RoutedEventArgs e)=>MoveSelectedLayer(-1);

    sealed class LayerItem
    {
        public LayerItem(ScorebugElement element)=>Element=element;
        public ScorebugElement Element { get; }
        public string Glyph => !Element.Visible ? "○" : Element.Kind switch
        {
            ScorebugElementKind.Logo => "▣",
            ScorebugElementKind.Shape => "◼",
            ScorebugElementKind.Video => "▶",
            ScorebugElementKind.Html => "◧",
            _ => "T"
        };
        public string Label => Element.Name;
        public string KindLabel => Element.Kind.ToString().ToUpperInvariant();
    }
}
