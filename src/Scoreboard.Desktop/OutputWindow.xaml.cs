using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using Scoreboard.Core;
namespace Scoreboard.Desktop;
public partial class OutputWindow : Window
{
    readonly GameEngine _engine;
    readonly ScorebugCatalog _catalog;
    GameState _displayState;
    readonly ScorebugCanvasView _scorebugView;
    // Which display this window is the audience output for. Only that display's published layout
    // is ever drawn here, so two screens can be on air with completely different designs.
    readonly string _displayId;
    string _broadcastTheme;
    WindowStyle _style; WindowState _state; bool _full;
    double _left, _top, _width, _height;
    bool _geometrySaved;
    public OutputWindow(GameEngine engine, ScorebugCatalog catalog,string broadcastTheme="Dark",MediaPlayback? playback=null,string? displayId=null)
    {
        InitializeComponent(); _broadcastTheme=broadcastTheme; _engine = engine; _catalog=catalog; _displayState=engine.CloneState();
        _displayId=(catalog.FindDisplay(displayId)??catalog.Displays[0]).Id;
        // The audience only ever sees the layout that was published, never the one being designed.
        _scorebugView=new ScorebugCanvasView(catalog,engine,()=>_displayState,playback,muteVideo:false,layoutProvider:()=>_catalog.PublishedLayoutOf(_displayId),displayId:_displayId);
        ScorebugHost.Children.Add(_scorebugView); _engine.Changed+=Update; _engine.Changed+=_scorebugView.Refresh; _catalog.Changed+=Publish; Update();
    }

    /// <summary>The display this window shows.</summary>
    public string DisplayId => _displayId;

    /// <summary>The display's name, for the window title and the chooser.</summary>
    public string DisplayName => _catalog.FindDisplay(_displayId)?.Name ?? "Program";
    public void Publish(){_displayState=_engine.CloneState();Update();_scorebugView.Refresh();}
    /// <summary>Re-creates the on-air visuals; used when a new game replaces everything.</summary>
    public void Rebuild(){Update();_scorebugView.Rebuild();}
    public void UpdateLiveScoreClock(GameState source)
    {
        _displayState.HomeScore=source.HomeScore;_displayState.AwayScore=source.AwayScore;
        _displayState.HomeTeam=source.HomeTeam;_displayState.AwayTeam=source.AwayTeam;
        _displayState.HomeAbbreviation=source.HomeAbbreviation;_displayState.AwayAbbreviation=source.AwayAbbreviation;
        _displayState.ClockSeconds=source.ClockSeconds;_displayState.ClockRunning=source.ClockRunning;_displayState.ClockCountsUp=source.ClockCountsUp;
        _displayState.ShotClockSeconds=source.ShotClockSeconds;_displayState.ShotClockRunning=source.ShotClockRunning;
        _displayState.PlayClockSeconds=source.PlayClockSeconds;_displayState.PlayClockRunning=source.PlayClockRunning;
        _displayState.TimeoutClockSeconds=source.TimeoutClockSeconds;_displayState.TimeoutClockRunning=source.TimeoutClockRunning;
        // Player data goes live with the score, so a substitution prints on air immediately.
        _displayState.HomePlayerCleared=source.HomePlayerCleared;_displayState.AwayPlayerCleared=source.AwayPlayerCleared;
        _displayState.HomePlayer=source.HomePlayer;_displayState.AwayPlayer=source.AwayPlayer;
        Update();_scorebugView.Refresh();
    }
    public void RefreshDisplay(){Update();_scorebugView.Refresh();}
    /// <summary>Prints the newest values at once, used when the value switch delay changes.</summary>
    public void RefreshGraphic(){Update();_scorebugView.ApplyNow();}
    // The audience window is intentionally never restyled by ThemeManager: a theme change must not recolor on-air graphics.
    public void SetBroadcastTheme(string theme){_broadcastTheme=theme;Update();}
    void Window_Loaded(object sender,RoutedEventArgs e){Update();_scorebugView.Refresh();}
    void Update()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Update); return; }
        var s = _displayState;
        Background=ColorBrush(ThemeManager.Resolve(_broadcastTheme)=="Light"?"#FFF3F5F8":"#FF000000");
        HomeLabel.Text = s.HomeTeam; AwayLabel.Text = s.AwayTeam;
        HomeScore.Text = s.HomeScore.ToString(); AwayScore.Text = s.AwayScore.ToString();
        Period.Text = PeriodDisplay.Format(s).ToUpperInvariant(); Clock.Text = GameEngine.FormatClock(s.ClockSeconds);
        HomeExtraLabel.Text = AwayExtraLabel.Text = "TIMEOUTS";
        HomeExtraPanel.Visibility = AwayExtraPanel.Visibility = Visibility.Visible;
        HomeTimeouts.Text = Dots(s.HomeTimeouts); AwayTimeouts.Text = Dots(s.AwayTimeouts);
        Possession.Visibility = s.SportId is "basketball" or "football" ? Visibility.Visible : Visibility.Collapsed;
        Possession.Text = $"●  {(s.HomePossession ? s.HomeTeam : s.AwayTeam)} POSSESSION";
        if(s.SportId=="football")
        {
            Period.Text=$"{PeriodDisplay.Format(s).ToUpperInvariant()} · {s.Down}{OrdinalSuffix(s.Down)} & {s.YardsToGo}";
            Possession.Text=$"●  {(s.HomePossession?s.HomeTeam:s.AwayTeam)} POSSESSION   ·   PLAY CLOCK {s.PlayClockSeconds}   ·   TIMEOUT CLOCK {GameEngine.FormatClock(s.TimeoutClockSeconds)}";
        }
        if(s.SportId=="soccer")
        {
            HomeExtraLabel.Text = AwayExtraLabel.Text = "CARDS · SHOTS · CORNERS";
            HomeTimeouts.Text=$"🟨 {s.HomeYellowCards}   🟥 {s.HomeRedCards}   ⚽ {s.HomeShots}   ◆ {s.HomeCorners}";
            AwayTimeouts.Text=$"🟨 {s.AwayYellowCards}   🟥 {s.AwayRedCards}   ⚽ {s.AwayShots}   ◆ {s.AwayCorners}";
            Possession.Visibility=Visibility.Collapsed;
        }
        else if(s.SportId=="basketball")
        {
            HomeExtraLabel.Text=AwayExtraLabel.Text="FOULS";HomeTimeouts.Text=$"{s.HomeFouls} fouls";AwayTimeouts.Text=$"{s.AwayFouls} fouls";
            Possession.Text=$"●  {(s.HomePossession?s.HomeTeam:s.AwayTeam)} POSSESSION   ·   SHOT CLOCK {s.ShotClockSeconds}";
        }
        else if(s.SportId=="baseball")
        {
            HomeExtraLabel.Text=AwayExtraLabel.Text="HITS · COUNT";HomeTimeouts.Text=$"{s.HomeHits} hits   ·   {s.Balls} balls / {s.Strikes} strikes";AwayTimeouts.Text=$"{s.AwayHits} hits   ·   {s.Outs} outs";Possession.Visibility=Visibility.Collapsed;
        }
        else if(s.SportId=="football")
        {
            HomeExtraLabel.Text=AwayExtraLabel.Text="YARDS · TURNOVERS";HomeTimeouts.Text=$"{s.HomeYards} yards   ·   {s.HomeTurnovers} turnovers";AwayTimeouts.Text=$"{s.AwayYards} yards   ·   {s.AwayTurnovers} turnovers";
        }
    }
    static System.Windows.Media.Brush ColorBrush(string value)=>(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(value)!;
    static string Dots(int count) => string.Join(" ", Enumerable.Repeat("●", count));
    static string OrdinalSuffix(int value)=>value switch{1=>"st",2=>"nd",3=>"rd",_=>"th"};
    /// <summary>True while this window is filling its screen.</summary>
    public bool IsFullscreen => _full;

    public void ToggleFullscreen()
    {
        if (_full) ExitFullscreen();
        else EnterFullscreen();
    }

    void EnterFullscreen()
    {
        _full = true;
        _style = WindowStyle;
        _state = WindowState;
        _left = Left;
        _top = Top;
        _width = Width;
        _height = Height;
        _geometrySaved = true;
        // Drop back to a normal window before the chrome goes: maximizing while the style is being
        // changed makes WPF size the window to the old work area, which is what left a strip down
        // each side of a 16:9 graphic (the "black borders" in fullscreen).
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        FillMonitor();
        Topmost = true;
    }

    void ExitFullscreen()
    {
        _full = false;
        Topmost = false;
        WindowStyle = _style;
        ResizeMode = ResizeMode.CanResize;
        if (_geometrySaved)
        {
            Left = _left;
            Top = _top;
            Width = _width;
            Height = _height;
            _geometrySaved = false;
        }
        WindowState = _state;
    }

    /// <summary>Takes the window to the full bounds of the monitor it is on, taskbar included.</summary>
    void FillMonitor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;
        // GetMonitorInfo is in physical pixels; WPF's Left/Top/Width/Height are device-independent.
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = info.Monitor.Left / dpi.DpiScaleX;
        Top = info.Monitor.Top / dpi.DpiScaleY;
        Width = (info.Monitor.Right - info.Monitor.Left) / dpi.DpiScaleX;
        Height = (info.Monitor.Bottom - info.Monitor.Top) / dpi.DpiScaleY;
    }

    const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    /// <summary>
    /// F11 on the audience window is the same as Broadcast Fullscreen from the main menu: the
    /// graphic fills the screen. Escape gives the window back.
    /// </summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        if (e.Key == System.Windows.Input.Key.Escape && _full)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }
    protected override void OnClosed(EventArgs e) { _engine.Changed-=Update; _engine.Changed-=_scorebugView.Refresh; _catalog.Changed-=Publish; _scorebugView.Dispose(); base.OnClosed(e); }
}
