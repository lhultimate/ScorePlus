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
    public OutputWindow(GameEngine engine, ScorebugCatalog catalog,string broadcastTheme="Dark",MediaPlayback? playback=null,string? displayId=null)
    {
        InitializeComponent(); _broadcastTheme=broadcastTheme; _engine = engine; _catalog=catalog; _displayState=engine.CloneState();
        _displayId=(catalog.FindDisplay(displayId)??catalog.Displays[0]).Id;
        // The audience only ever sees the layout that was published, never the one being designed.
        _scorebugView=new ScorebugCanvasView(catalog,engine,()=>_displayState,playback,muteVideo:false,layoutProvider:()=>_catalog.PublishedCompositeOf(_displayId),displayId:_displayId);
        ScorebugHost.Children.Add(_scorebugView); _engine.Changed+=Update; _engine.Changed+=_scorebugView.Refresh; _catalog.Changed+=Publish; Update();
    }

    /// <summary>The display this window shows.</summary>
    public string DisplayId => _displayId;

    /// <summary>The display's name, for the window title and the chooser.</summary>
    public string DisplayName => _catalog.FindDisplay(_displayId)?.Name ?? "Program";
    public void Publish(){_displayState=_engine.CloneState();Update();_scorebugView.Refresh();}
    /// <summary>Re-creates the on-air visuals; used when a new game replaces everything.</summary>
    public void Rebuild(){Update();_scorebugView.Rebuild();}
    /// <summary>Copies controller-owned game values to the audience without staging them.</summary>
    public void UpdateLiveController(GameState source)
    {
        _displayState.HomeScore=source.HomeScore;_displayState.AwayScore=source.AwayScore;
        _displayState.ClockSeconds=source.ClockSeconds;_displayState.ClockRunning=source.ClockRunning;
        _displayState.Period=source.Period;_displayState.HomeTimeouts=source.HomeTimeouts;
        _displayState.AwayTimeouts=source.AwayTimeouts;_displayState.HomePossession=source.HomePossession;
        _displayState.Down=source.Down;_displayState.YardsToGo=source.YardsToGo;
        _displayState.YardsToGoal=source.YardsToGoal;_displayState.BallOnHomeSide=source.BallOnHomeSide;
        _displayState.ShotClockSeconds=source.ShotClockSeconds;_displayState.ShotClockRunning=source.ShotClockRunning;
        _displayState.PlayClockSeconds=source.PlayClockSeconds;_displayState.PlayClockRunning=source.PlayClockRunning;
        _displayState.TimeoutClockSeconds=source.TimeoutClockSeconds;_displayState.TimeoutClockRunning=source.TimeoutClockRunning;
        _displayState.HomeFouls=source.HomeFouls;_displayState.AwayFouls=source.AwayFouls;
        _displayState.Balls=source.Balls;_displayState.Strikes=source.Strikes;_displayState.Outs=source.Outs;
        Update();_scorebugView.Refresh();
    }

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
    public bool IsFullscreen => WindowFullscreen.IsFullscreen(this);

    public void ToggleFullscreen() => WindowFullscreen.Toggle(this);

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
        if (e.Key == System.Windows.Input.Key.Escape && IsFullscreen)
        {
            WindowFullscreen.Exit(this);
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }
    protected override void OnClosed(EventArgs e) { _engine.Changed-=Update; _engine.Changed-=_scorebugView.Refresh; _catalog.Changed-=Publish; _scorebugView.Dispose(); base.OnClosed(e); }
}
