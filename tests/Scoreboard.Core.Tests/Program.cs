using System.Text.Json;
using Scoreboard.Core;
// Keep the suite's default paths (sports.json, scorebug.json, autosaves) in a throwaway folder.
var appDataPath=Path.Combine(Path.GetTempPath(),$"core-appdata-{Guid.NewGuid():N}");
AppPaths.UseRoot(appDataPath);
AppDomain.CurrentDomain.ProcessExit+=(_,_)=>{try{Directory.Delete(appDataPath,true);}catch{}};
static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
var engine=new GameEngine();
Check(SportDefinition.BuiltIns.Select(x=>x.Id).Distinct().Count()>=9,"all built-in sports have unique identifiers");
// Every sport the operator expects by name is present, plus a blank "Custom" starting point.
foreach(var sport in new[]{"football","baseball","basketball","cricket","hockey","lacrosse","rugby","soccer","tennis","volleyball","wrestling","australian-football","generic"})
    Check(SportDefinition.BuiltIns.Any(s=>s.Id==sport),$"the {sport} sport profile is built in");
Check(SportDefinition.BuiltIns.Last().Name=="Custom"&&SportDefinition.BuiltIns.Last().Id=="generic","the last built-in profile is the editable Custom preset");
// The American preset keeps its own scoring buttons: touchdown, extra point and two-point.
var americanPreset=SportDefinition.BuiltIns.Single(s=>s.Id=="football");
var australianPreset=SportDefinition.BuiltIns.Single(s=>s.Id=="australian-football");
Check(americanPreset.ScoringOptions.SequenceEqual([1,2,3,6,7,8]),"American football offers its own scoring buttons (1, 2, 3, 6, 7, 8)");
Check(australianPreset.ScoringOptions.SequenceEqual([1,6])&&australianPreset.Timeouts==0&&australianPreset.PeriodSeconds==1200,"Australian football keeps goal and behind scoring, no timeouts and 20-minute quarters");
Check(SportDefinition.BuiltIns.All(s=>s.Periods>=1&&s.ScoringOptions.Length>0&&s.Timeouts>=0),"every built-in sport has playable period and scoring settings");
engine.NewGame("basketball"); engine.SetTeams("Eagles","Tigers");
engine.Score(true,3); engine.Score(false,2);
Check(engine.State.HomeScore==3&&engine.State.AwayScore==2,"home and away scoring operations");
Check(engine.Undo()&&engine.State.AwayScore==0,"undo reverses last scoring action");
engine.SetClock(3); engine.State.ClockRunning=true; Thread.Sleep(1200); engine.Tick();
Check(engine.State.ClockSeconds<=2&&engine.State.ClockSeconds>=1,"countdown clock uses elapsed monotonic time");
engine.State.ClockRunning=false; engine.SetClock(0); engine.State.ClockRunning=true; Thread.Sleep(1100); engine.Tick();
Check(engine.State.ClockSeconds==0&&!engine.State.ClockRunning,"clock stops safely at zero");
engine.NextPeriod(); Check(engine.State.Period==2&&engine.State.ClockSeconds==720,"period transition restores sport clock preset");
var file=Path.Combine(Path.GetTempPath(),$"scoreboard-{Guid.NewGuid():N}.scoreboard"); engine.Save(file); var other=new GameEngine(); other.Load(file);
Check(other.State.HomeTeam=="Eagles"&&other.State.Period==2&&other.State.Events.Count>0,"portable project saves game, teams, and event history"); File.Delete(file);
// A new console field must not be forgotten by the save. This checks every writable GameState
// property, not just the values the rest of the suite happens to set, and confirms it reads back.
var gameShapePath=Path.Combine(Path.GetTempPath(),$"game-shape-{Guid.NewGuid():N}.spg");
try
{
    var shapeState=new GameState();
    var shapeJson=GameFiles.Serialize(shapeState,null);
    File.WriteAllText(gameShapePath,shapeJson);
    using var shapeDocument=JsonDocument.Parse(shapeJson);
    var writtenFields=shapeDocument.RootElement.EnumerateObject().Select(property=>property.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var gameFields=typeof(GameState).GetProperties()
        .Where(property=>property.CanRead&&property.CanWrite)
        .Where(property=>property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute),true).Length==0)
        .ToArray();
    var missingFields=gameFields.Where(property=>!writtenFields.Contains(property.Name)).Select(property=>property.Name).ToArray();
    Check(missingFields.Length==0,$"every writable game field is written to a .spg file ({gameFields.Length} fields checked"
        +(missingFields.Length==0?")":$"; missing: {string.Join(", ",missingFields)})"));
    var (shapeLoaded,_)=GameFiles.Read(gameShapePath);
    var lostFields=gameFields.Where(property=>
        JsonSerializer.Serialize(property.GetValue(shapeState))!=JsonSerializer.Serialize(property.GetValue(shapeLoaded)))
        .Select(property=>property.Name).ToArray();
    Check(lostFields.Length==0,$"every writable game field reads back from a .spg file"
        +(lostFields.Length==0?"":$" (lost: {string.Join(", ",lostFields)})"));
}
finally { if(File.Exists(gameShapePath))File.Delete(gameShapePath); }
var catalogPath=Path.Combine(Path.GetTempPath(),$"sports-{Guid.NewGuid():N}.json");
try
{
    var catalog=new SportCatalog(catalogPath); catalog.Duplicate("basketball","Community Basketball");
    var custom=catalog.Custom.Single(); custom=catalog.AddOrUpdate(custom with { Periods=2, PeriodSeconds=600, Unit="Half", CountUp=true, ScoringOptions=[1,4] });
    var reloaded=new SportCatalog(catalogPath); Check(reloaded.Find(custom.Id)?.ScoringOptions.SequenceEqual([1,4])==true,"custom sport persists configurable scoring and game rules");
    var customGame=new GameEngine(reloaded); customGame.NewGame(custom.Id);
    Check(customGame.State.ClockSeconds==600&&customGame.State.ClockCountsUp&&customGame.State.TotalPeriods==2&&customGame.State.PeriodLabel=="Half","custom sport settings initialize a new game's rules");
    Check(!reloaded.Delete("basketball")&&reloaded.Delete(custom.Id),"built-in presets are protected while custom sports can be deleted");
}
finally { if(File.Exists(catalogPath))File.Delete(catalogPath); }
var templateEngine=new GameEngine();templateEngine.NewGame("football");templateEngine.SetFootballDistance(3);templateEngine.AddFootballYards(true,20);var downBug=ScoreboardTemplates.Bind("{{DOWNS}} / {{YARDS}}",templateEngine.State);Check(downBug.Contains("1st & 3")&&downBug.Contains("20–0"),"scorebug template binding exposes football down and yard stats");
var liveSource=new GameEngine();var liveOutput=new GameEngine();liveSource.NewGame("football");liveOutput.LoadFromState(liveSource.CloneState());liveSource.SetFootballDistance(2,31);liveSource.Score(true,6);liveOutput.UpdateLiveScoreClock(liveSource.State);Check(liveOutput.State.HomeScore==6&&liveOutput.State.YardsToGoal==35,"live output updates scores without publishing operator game-state edits");liveOutput.LoadFromState(liveSource.CloneState());liveSource.SetFootballDistance(1,20);liveOutput.UpdateLiveScoreClock(liveSource.State);Check(liveOutput.State.YardsToGoal==31,"live output retains explicit-publish sport-specific data");
var modeSource=new GameEngine();modeSource.NewGame("football");var modeOutput=new GameEngine();modeOutput.LoadFromState(modeSource.CloneState());modeSource.SetFootballDownMode(FootballDownMode.Goal);Check(GameEngine.StagedSignature(modeSource.State)!=GameEngine.StagedSignature(modeOutput.State),"the Yards/Goal/PAT choice waits for Update Output");modeOutput.UpdateLiveScoreClock(modeSource.State);Check(modeOutput.State.FootballDownMode==FootballDownMode.Normal,"a live output update does not take the staged down display mode");
var previewWas=GameEngine.DownPreviewEnabled;GameEngine.DownPreviewEnabled=true;try{var liveDown=new GameEngine();liveDown.NewGame("football");liveDown.SetFootballDistance(3,41);var liveDownTarget=new GameEngine();liveDownTarget.LoadFromState(liveDown.CloneState());liveDown.SetDown(2);liveDownTarget.UpdateLiveScoreClock(liveDown.State);Check(liveDownTarget.State.Down==2&&liveDownTarget.State.YardsToGo==3,"the down number publishes live while the distance and display mode wait");}finally{GameEngine.DownPreviewEnabled=previewWas;}
var iconPath=Path.Combine(Path.GetTempPath(),$"icons-{Guid.NewGuid():N}.json");try{var iconCatalog=new ScorebugCatalog(iconPath);iconCatalog.Update(layout=>{layout.Elements.Clear();layout.Elements.Add(new ScorebugElement{Name="Timeouts",Content="",Icon=ScorebugIcon.TimeoutBars});layout.Elements.Add(new ScorebugElement{Name="Ball",Content="",Icon=ScorebugIcon.PossessionDot,IconFlipped=true});});var iconReloaded=new ScorebugCatalog(iconPath);Check(iconReloaded.Layout.Elements[0].Icon==ScorebugIcon.TimeoutBars&&iconReloaded.Layout.Elements[1].Icon==ScorebugIcon.PossessionDot&&iconReloaded.Layout.Elements[1].IconFlipped,"timeout-bar and possession-dot icons persist in the scorebug layout");}finally{if(File.Exists(iconPath))File.Delete(iconPath);}
var layoutPath=Path.Combine(Path.GetTempPath(),$"scorebug-{Guid.NewGuid():N}.json");
try
{
    var catalog=new ScorebugCatalog(layoutPath);Check(catalog.Layout.Elements.Count>=8,"default scorebug has editable team, score, clock and logo elements");
    var logo=new ScorebugElement{Name="Sponsor bug",Kind=ScorebugElementKind.Logo,Content="SPONSOR",Animation=ScorebugAnimation.Pulse,X=1400,Y=-20,Opacity=1.5};
    catalog.Update(layout=>layout.Elements.Add(logo));
    var loaded=new ScorebugCatalog(layoutPath);var saved=loaded.Layout.Elements.Single(x=>x.Name=="Sponsor bug");
    Check(saved.Kind==ScorebugElementKind.Logo&&saved.Animation==ScorebugAnimation.Pulse&&saved.X==1280&&saved.Y==0&&saved.Opacity==1,"scorebug customization persists and clamps unsafe geometry");
    loaded.Update(layout=>layout.Elements.RemoveAll(x=>x.Id==logo.Id));Check(loaded.Layout.Elements.All(x=>x.Id!=logo.Id),"scorebug elements can be removed");
}
finally{if(File.Exists(layoutPath))File.Delete(layoutPath);}
engine.NewGame("football");engine.AdvanceDown();Check(engine.State.Down==2&&engine.State.YardsToGo==10,"football advances from first to second down");engine.AdvanceDown();engine.AdvanceDown();engine.AdvanceDown();Check(engine.State.Down==1,"football fourth down starts a new series");engine.SetFootballDistance(4,22);Check(engine.State.YardsToGo==4&&engine.State.YardsToGoal==22,"football yardage and distance are editable");
engine.SetDown(1);engine.SetFootballDistance(6);engine.SetFootballDownMode(FootballDownMode.Goal);Check(engine.State.DownDistanceDisplay=="1st & Goal","first and goal prints 1st & Goal");engine.SetFootballDownMode(FootballDownMode.Pat);Check(engine.State.DownDisplay=="PAT"&&engine.State.ToGoDisplay==""&&engine.State.DownDistanceDisplay=="PAT","the down line can become PAT after a touchdown");engine.SetFootballDownMode(FootballDownMode.TwoPoint);Check(engine.State.DownDistanceDisplay=="2 PT ATT","the down line can become 2 PT ATT after a touchdown");engine.SetFootballDownMode(FootballDownMode.None);Check(engine.State.DownDisplay==""&&engine.State.ToGoDisplay==""&&engine.State.DownDistanceDisplay==""&&ScoreboardTemplates.Bind("{{DOWNS}}",engine.State)=="","the down and distance can be blank");engine.SetFootballDownMode(FootballDownMode.Normal);Check(engine.State.DownDistanceDisplay=="1st & 6","normal down display comes back after a special mode");
engine.NewGame("football");engine.SetClockMessage(GameClockMessage.Final);Check(engine.State.GameClockText=="FINAL"&&ScoreboardTemplates.Bind("{{GAME_CLOCK}}",engine.State)=="FINAL","the game clock can read Final");engine.SetClockMessage(GameClockMessage.EndOfRegulation);Check(engine.State.GameClockText=="END OF REG."&&ScoreboardTemplates.Bind("{{GAME_CLOCK}}",engine.State)=="END OF REG.","the game clock can read End of Reg.");engine.SetClockMessage(GameClockMessage.Clock);Check(engine.State.GameClockText==GameEngine.FormatClock(engine.State.ClockSeconds),"the game clock returns to the running time");
engine.NewGame("football");engine.SetPeriod(5);Check(PeriodDisplay.Format(engine.State)=="OT","a fifth football period prints OT");engine.SetPeriod(6);Check(PeriodDisplay.Format(engine.State)=="2OT","a sixth football period prints 2OT");
engine.NewGame("soccer");engine.AddCard(true);engine.AddCard(false,true);Check(engine.State.HomeYellowCards==1&&engine.State.AwayRedCards==1,"soccer yellow and red cards track per side");Check(engine.Undo()&&engine.State.AwayRedCards==0,"soccer card can be undone");
Check(!GameEngine.UppercaseDownWordsEnabled,"the full-caps down line is off by default");
engine.NewGame("football");engine.SetFootballDownMode(FootballDownMode.Goal);
Check(engine.State.DownDistanceDisplay=="1st & Goal"&&engine.State.ToGoDisplay=="Goal",
    "the down line prints mixed case by default");
try
{
    GameEngine.UppercaseDownWordsEnabled=true;
    Check(engine.State.DownDistanceDisplay=="1st & GOAL"&&engine.State.ToGoDisplay=="GOAL",
        "the full-caps option prints 1st & GOAL");
    engine.SetFootballDownMode(FootballDownMode.Normal);engine.State.Down=2;engine.State.DownPending=true;
    Check(engine.State.DownDistanceDisplay=="2nd DOWN"&&ScoreboardTemplates.Bind("{{DOWNS}}",engine.State)=="2nd DOWN",
        "the full-caps option prints 2nd DOWN on the graphic");
}
finally
{
    GameEngine.UppercaseDownWordsEnabled=false;
    engine.State.DownPending=false;
}
Check(engine.State.DownDistanceDisplay=="2nd & 10","the down line returns to mixed case when the option is off");
var sportsGame=Path.Combine(Path.GetTempPath(),$"sports-state-{Guid.NewGuid():N}.scoreboard");engine.NewGame("football");engine.SetFootballDistance(3,17);engine.AdvanceDown();engine.Save(sportsGame);var restored=new GameEngine();restored.Load(sportsGame);Check(restored.State.Down==2&&restored.State.YardsToGo==3&&restored.State.YardsToGoal==17,"sport-specific state persists in saved game");File.Delete(sportsGame);
engine.NewGame("baseball");engine.BaseballBall();engine.BaseballBall();engine.BaseballBall();engine.BaseballBall();Check(engine.State.Balls==0&&engine.State.Strikes==0,"four baseball balls reset the count");
engine.BaseballStrike();engine.BaseballStrike();engine.BaseballStrike(true);Check(engine.State.Strikes==2&&engine.State.Outs==0,"baseball foul at two strikes does not record a third strike");
engine.BaseballStrike();Check(engine.State.Strikes==0&&engine.State.Balls==0&&engine.State.Outs==1,"third strike records an out and resets the count");
engine.BaseballHit(true);Check(engine.State.HomeHits==1&&engine.State.Balls==0&&engine.State.Strikes==0,"baseball hit is credited and resets the count");Check(engine.Undo()&&engine.State.HomeHits==0,"baseball hit supports undo");
var baseballSave=Path.Combine(Path.GetTempPath(),$"baseball-{Guid.NewGuid():N}.scoreboard");engine.BaseballHit(false);engine.Save(baseballSave);var baseballReload=new GameEngine();baseballReload.Load(baseballSave);Check(baseballReload.State.AwayHits==1&&baseballReload.State.Outs==1,"baseball counts and hits persist in game saves");File.Delete(baseballSave);
engine.NewGame("basketball");engine.AddFoul(true);engine.SetShotClock(24);engine.ToggleShotClock();Thread.Sleep(1100);engine.Tick();Check(engine.State.HomeFouls==1&&engine.State.ShotClockSeconds<=23&&engine.State.ShotClockRunning,"basketball tracks fouls and runs an independent shot clock");engine.ResetShotClock();Check(engine.State.ShotClockSeconds==24&&!engine.State.ShotClockRunning,"basketball shot clock resets to 24 and stops");Check(engine.Undo()&&engine.State.ShotClockRunning,"basketball shot clock action can be undone");
engine.NewGame("football");engine.AddFootballYards(true,25);engine.AddTurnover(false);Check(engine.State.HomeYards==25&&engine.State.AwayTurnovers==1,"football tracks team yards and turnovers");Check(engine.Undo()&&engine.State.AwayTurnovers==0,"football turnover supports undo");
engine.NewGame("soccer");engine.AddSoccerShot(true);engine.AddSoccerCorner(false);Check(engine.State.HomeShots==1&&engine.State.AwayCorners==1,"soccer tracks shots and corners by team");
engine.NewGame("soccer");engine.SetTeams("<Home & Co>","Visitors");engine.AddSoccerShot(true);var report=GameReport.Build(engine.State);Check(report.Contains("&lt;Home &amp; Co&gt;")&&report.Contains("Event timeline")&&report.Contains("Shots: 1 — 0"),"game report escapes names and summarizes stats and timeline");
engine.NewGame("football");engine.SetTeams("<Home & Co>","Visitors");engine.SetFootballDistance(2,33);engine.AdvanceDown();engine.AddFootballYards(true,14);engine.AddTurnover(false);var footballReport=GameReport.Build(engine.State);Check(footballReport.Contains("2nd &amp; 2")&&footballReport.Contains("Total yards: &lt;Home &amp; Co&gt; 14")&&footballReport.Contains("Turnovers: 0 — 1"),"football report includes down, yardage, and turnovers");
engine.NewGame("basketball");engine.AddFoul(true);var basketballReport=GameReport.Build(engine.State);Check(basketballReport.Contains("Fouls: 1 — 0")&&basketballReport.Contains("Shot clock: 30s"),"basketball report includes fouls and shot clock");
engine.NewGame("baseball");engine.SetTeams("<Home & Co>","Visitors");engine.BaseballHit(true);var baseballReport=GameReport.Build(engine.State);Check(baseballReport.Contains("Hits: &lt;Home &amp; Co&gt; 1")&&baseballReport.Contains("Count: 0 balls"),"baseball report includes hits and count");
engine.NewGame("football");engine.SetFootballDistance(4,18);engine.PreviousDown();Check(engine.State.Down==1&&engine.State.YardsToGo==4,"previous down steps back from first down safely");engine.AdvanceDown();engine.SetFootballDistance(7,42);engine.NewSeries();Check(engine.State.Down==1&&engine.State.YardsToGo==10,"new series resets the down to first and ten");Check(engine.Undo()&&engine.State.YardsToGo==7,"new series can be undone");
engine.NewGame("football");engine.SetFootballDistance(3,41);engine.MarkPublished();engine.AdvanceDown();Check(engine.State.Down==2&&engine.State.YardsToGo==3&&engine.State.YardsToGoal==41,"next down keeps the distance and spot");Check(!engine.CanRecallLastDown,"recall waits for the new down to be published");engine.MarkPublished();engine.RecallLastDown();Check(engine.State.Down==1&&engine.State.YardsToGo==3&&engine.State.YardsToGoal==41,"recall puts back the down that was on air before");engine.SetFootballDistance(2,18);engine.MarkPublished();engine.RecallLastDown();Check(engine.State.YardsToGo==3&&engine.State.YardsToGoal==41,"recall restores the previously published spot");
var hardwareDowns=new GameEngine();hardwareDowns.NewGame("football");hardwareDowns.ApplyHardwareSnapshot(new DeviceSnapshot(Down:1,YardsToGo:10,BallOn:35));hardwareDowns.MarkPublished();hardwareDowns.ApplyHardwareSnapshot(new DeviceSnapshot(Down:2,YardsToGo:7,BallOn:40));Check(hardwareDowns.State.Down==2&&hardwareDowns.State.YardsToGo==7,"a console frame moves the down");Check(!hardwareDowns.CanRecallLastDown,"recall waits for the console's new down to be published");hardwareDowns.MarkPublished();hardwareDowns.RecallLastDown();Check(hardwareDowns.State.Down==1&&hardwareDowns.State.YardsToGo==10&&hardwareDowns.State.YardsToGoal==35,"a recall restores the down that was on air before");hardwareDowns.ApplyHardwareSnapshot(new DeviceSnapshot(Down:2,YardsToGo:7,BallOn:40));hardwareDowns.ApplyHardwareSnapshot(new DeviceSnapshot(Down:2,YardsToGo:7,BallOn:40));hardwareDowns.MarkPublished();hardwareDowns.RecallLastDown();Check(hardwareDowns.State.Down==1&&hardwareDowns.State.YardsToGo==10,"a repeated controller frame still recalls the down before it");
engine.NewGame("football");Check(PeriodDisplay.Format(engine.State)=="Quarter 1","default period label is Quarter 1");engine.SetPeriodFormat("Short");Check(PeriodDisplay.Format(engine.State)=="Q1"&&ScoreboardTemplates.Bind("{{PERIOD}}",engine.State)=="Q1","short period label is Q1");Check(ScoreboardTemplates.Bind("{{PERIOD}}",engine.State,"Full")=="QUARTER 1"&&ScoreboardTemplates.Bind("{{PERIOD}}",engine.State,"Number")=="1","a studio element can choose its own quarter style");engine.SetPeriodFormat("Number");Check(PeriodDisplay.Format(engine.State)=="1","numeric period label is a bare number");engine.SetPeriodFormat("Ordinal");Check(PeriodDisplay.Format(engine.State)=="1st","ordinal period label is 1st");var periodReport=GameReport.Build(engine.State);Check(periodReport.Contains("1 of 4"),"game report honors the selected period format");engine.NewGame("soccer");engine.SetPeriodFormat("Short");Check(PeriodDisplay.Format(engine.State)=="Half 1","period format only applies to quarter-based sports");
var ordinalPath=Path.Combine(Path.GetTempPath(),$"ordinal-{Guid.NewGuid():N}.json");try{var ordinalCatalog=new ScorebugCatalog(ordinalPath);ordinalCatalog.Update(layout=>layout.Elements.Add(new ScorebugElement{Name="Ordinal",Content="{{PERIOD}}",PeriodFormat="Ordinal"}));Check(new ScorebugCatalog(ordinalPath).Layout.Elements[^1].PeriodFormat=="Ordinal","an element's ordinal quarter style survives a save");}finally{if(File.Exists(ordinalPath))File.Delete(ordinalPath);}
var shapeLayout=new ScorebugElement{Kind=ScorebugElementKind.Shape,Shape=ScorebugShape.Parallelogram};var shapeCatalogPath=Path.Combine(Path.GetTempPath(),$"shape-{Guid.NewGuid():N}.json");try{var catalog=new ScorebugCatalog(shapeCatalogPath);catalog.Update(layout=>layout.Elements.Add(shapeLayout));var reloaded=new ScorebugCatalog(shapeCatalogPath);Check(reloaded.Layout.Elements[^1].Shape==ScorebugShape.Parallelogram,"scorebug shapes persist in the studio layout, including the parallelogram");}finally{if(File.Exists(shapeCatalogPath))File.Delete(shapeCatalogPath);}
Check(ClockFormat.TryParse("10:30",out var parsedClock)&&parsedClock==630,"MM:SS clock entry parses minutes and seconds");Check(ClockFormat.TryParse("1:05",out var shortClock)&&shortClock==65,"single-digit minutes parse correctly");Check(ClockFormat.TryParse("720",out var rawClock)&&rawClock==720,"plain seconds still parse");Check(!ClockFormat.TryParse("10:75",out _)&&!ClockFormat.TryParse("abc",out _),"invalid clock entry is rejected");Check(ClockFormat.Format(630)=="10:30"&&ClockFormat.Format(0)=="00:00","clock formatting round trips");
engine.NewGame("football");Check(engine.State.HomeTimeouts==3&&engine.State.AwayTimeouts==3,"new games take the timeout count from the sport profile");var timeoutCatalogPath=Path.Combine(Path.GetTempPath(),$"timeouts-{Guid.NewGuid():N}.json");try{var timeouts=new SportCatalog(timeoutCatalogPath);timeouts.SaveProfile(timeouts.Find("football")! with { Timeouts=7 });var reloaded=new SportCatalog(timeoutCatalogPath);Check(reloaded.Find("football")!.Timeouts==3,"editing a football profile clamps its timeout override to three");var overrideGame=new GameEngine(reloaded);overrideGame.NewGame("football");Check(overrideGame.State.HomeTimeouts==3&&overrideGame.State.HomeTimeoutLimit==3,"football timeouts never exceed three in a new game");Check(reloaded.ResetOverride("football")&&reloaded.Find("football")!.Timeouts==3,"resetting an override restores the preset");}finally{if(File.Exists(timeoutCatalogPath))File.Delete(timeoutCatalogPath);}
engine.NewGame("soccer");var stat=engine.AddStat("Turnovers");engine.AdjustStat(stat.Id,2);engine.AdjustStat(stat.Id,1);Check(engine.State.Stats.Single().Value==3,"custom stats track a single value");Check(ScoreboardTemplates.Bind("{{STAT:Turnovers}}",engine.State)=="3"&&ScoreboardTemplates.Bind("{{stat:turnovers}}",engine.State)=="3","custom stat tokens display the value");Check(ScoreboardTemplates.Bind("{{STAT:Missing}}",engine.State)=="","missing custom stat tokens render empty");Check(engine.Undo()&&engine.State.Stats.Single().Value==2,"custom stat changes are undoable");var statsReport=GameReport.Build(engine.State);Check(statsReport.Contains("Custom stats")&&statsReport.Contains("Turnovers: 2"),"game report lists custom stats");engine.RemoveStat(stat.Id);Check(engine.State.Stats.Count==0,"custom stats can be removed");
engine.NewGame("football");var exact=engine.AddStat("Penalties");engine.SetStat(exact.Id,4);Check(engine.State.Stats.Single().Value==4,"typed custom stat values set the exact count");engine.SetStat(exact.Id,1);Check(engine.Undo()&&engine.State.Stats.Single().Value==4,"typed custom stat values are undoable");var stagedBefore=GameEngine.StagedSignature(engine.State);engine.Score(true,6);engine.SetClock(42);engine.SetPlayClock(12);Check(GameEngine.StagedSignature(engine.State)==stagedBefore,"score and clocks are published live instead of staged");engine.SetTeams("Home","Away");Check(GameEngine.StagedSignature(engine.State)==stagedBefore,"team name changes publish live like scores");engine.AdvanceDown();Check(GameEngine.StagedSignature(engine.State)!=stagedBefore,"down and distance wait for an explicit output update");
var liveNames=new GameEngine();liveNames.NewGame("football");var nameTarget=new GameEngine();nameTarget.LoadFromState(liveNames.CloneState());liveNames.SetTeams("Home Team","Away Team");nameTarget.UpdateLiveScoreClock(liveNames.State);Check(nameTarget.State.HomeTeam=="Home Team"&&nameTarget.State.HomeAbbreviation=="HT","live publishing carries renamed teams to the output");
var typedNames=new GameEngine();typedNames.NewGame("football");var typedTarget=new GameEngine();typedTarget.LoadFromState(typedNames.CloneState());typedNames.SetTeams("New England","ny giants");Check(ScoreboardTemplates.Bind("{{HOME_TEAM}} vs {{AWAY_TEAM}}",typedNames.State)=="New England vs ny giants","scorebug renders the full team name in the case the operator typed");Check(ScoreboardTemplates.Bind("{{HOME_ABBR}}/{{AWAY_ABBR}}",typedNames.State)=="NE/NG","abbreviation tokens still render initials for compact labels");typedTarget.UpdateLiveScoreClock(typedNames.State);Check(ScoreboardTemplates.Bind("{{HOME_TEAM}} vs {{AWAY_TEAM}}",typedTarget.State)=="New England vs ny giants","live publishing carries the typed team name to the on-air graphic");
engine.NewGame("football");engine.SetFootballDistance(3,74);Check(engine.State.YardsToGoal==50,"the ball spot stops at the 50-yard line");engine.SetPossession(false);Check(engine.State.YardsToGoal==50&&ScoreboardTemplates.Bind("{{BALL_ON}}",engine.State)=="50","the ball spot is not tied to possession");engine.SetFootballDistance(3,-6);Check(engine.State.YardsToGoal==0,"the ball spot stops at the goal line");
engine.NewGame("football");engine.SetTeams("Home Team","Away Team");engine.SetFootballDistance(2,37);Check(engine.State.BallOnHomeSide&&ScoreboardTemplates.Bind("{{BALL_SIDE}}",engine.State)=="HT","the ball spot starts on the home half of the field");engine.SetPossession(false);Check(ScoreboardTemplates.Bind("{{BALL_SIDE}}",engine.State)=="HT"&&engine.State.YardsToGoal==37,"possession does not decide which half the ball is on");var beforeSide=GameEngine.StagedSignature(engine.State);engine.SetBallSide(false);Check(ScoreboardTemplates.Bind("{{BALL_SIDE}}",engine.State)=="AT"&&ScoreboardTemplates.Bind("{{BALL_SIDE_SPOT}}",engine.State)=="AT 37","the ball spot can sit on the visitors' half");Check(GameEngine.StagedSignature(engine.State)!=beforeSide,"a ball-side change waits for Update Output");Check(engine.Undo()&&engine.State.BallOnHomeSide,"moving the ball to the other half can be undone");
var sameCatalogPath=Path.Combine(Path.GetTempPath(),$"same-{Guid.NewGuid():N}.json");try{var liveCatalog=new SportCatalog(sameCatalogPath);var liveEngine=new GameEngine(liveCatalog);liveEngine.NewGame("football");liveCatalog.SaveProfile(liveCatalog.Find("football")! with { Timeouts=2, ScoringOptions=[1,2,3,6] });liveEngine.NewGame("football");Check(liveEngine.State.HomeTimeouts==2&&liveEngine.State.ScoringOptions.Contains(6),"a sport profile edited while the app runs reaches the next new game without a relaunch");}finally{if(File.Exists(sameCatalogPath))File.Delete(sameCatalogPath);}
var autosaveFolder=Path.Combine(Path.GetTempPath(),$"autosaves-{Guid.NewGuid():N}");AutosaveStore.UseFolder(autosaveFolder);try{var auto=new GameEngine();auto.NewGame("football");auto.SetTeams("Home Squad","Away Squad");auto.Score(true,7);AutosaveStore.Save(auto);AutosaveStore.Save(auto);var points=AutosaveStore.List();Check(points.Count==1&&points[0].HomeScore==7&&points[0].HomeTeam=="Home Squad","autosaves keep one recoverable point inside the same minute");Check(points[0].Path.EndsWith(".spg",StringComparison.OrdinalIgnoreCase),"autosaves are stored as .spg game files");auto.NewGame("football");var afterNewGame=AutosaveStore.Save(auto,startNewPoint:true);Check(afterNewGame.Count==2&&afterNewGame[0].HomeScore==0&&afterNewGame[1].HomeScore==7,"a new game starts its own recoverable point and leaves the previous game recoverable");for(var i=0;i<AutosaveStore.Keep+3;i++)AutosaveStore.Save(auto,startNewPoint:true);Check(AutosaveStore.List().Count==AutosaveStore.Keep,$"autosaves are pruned to the newest {AutosaveStore.Keep} points");
// Delete all clears the folder, including a file that is too damaged to read as a game.
File.WriteAllText(Path.Combine(autosaveFolder,"autosave-broken.spg"),"not a game");
var deleted=AutosaveStore.DeleteAll();
Check(deleted>=AutosaveStore.Keep+1&&AutosaveStore.List().Count==0&&Directory.GetFiles(autosaveFolder).All(path=>!path.EndsWith(".spg",StringComparison.OrdinalIgnoreCase)),$"deleting all autosaves empties the folder ({deleted} removed)");
Check(AutosaveStore.DeleteAll()==0,"deleting all autosaves twice is harmless");
var savedGame=Path.Combine(Path.GetTempPath(),$"kept-game-{Guid.NewGuid():N}.spg");
auto.Save(savedGame);
AutosaveStore.Save(auto,startNewPoint:true);
AutosaveStore.DeleteAll();
Check(File.Exists(savedGame)&&AutosaveStore.List().Count==0,"deleting autosaves leaves a saved .spg game alone");
try{File.Delete(savedGame);}catch{}}finally{AutosaveStore.UseFolder(null);try{Directory.Delete(autosaveFolder,true);}catch{}}
var liveTick=new GameEngine();liveTick.NewGame("basketball");var tickNotified=0;liveTick.Changed+=()=>tickNotified++;Thread.Sleep(1100);liveTick.Tick();Check(tickNotified==0,"idle ticks do not notify the UI when no clock is running");

// ---------- team identity: name, abbreviation, logo and palette ----------
var identityEngine=new GameEngine();identityEngine.NewGame("football");
identityEngine.SetTeamIdentity(false,"Rovers FC","RFC",@"C:\logos\rovers.png","#FFAA1122","#FF223344",null,"4-2","21","2-2");
Check(identityEngine.State.AwayRecordDisplay=="4-2 (2-2)"&&identityEngine.State.AwayRanking=="#21"&&ScoreboardTemplates.Bind("{{AWAY_RECORD}} / {{AWAY_RANKING}}",identityEngine.State)=="4-2 (2-2) / #21","team details print a division record in brackets and a ranking with a hash");
Check(identityEngine.State.AwayTeam=="Rovers FC"&&identityEngine.State.AwayAbbreviation=="RFC"&&identityEngine.State.AwayLogoPath!.EndsWith("rovers.png",StringComparison.OrdinalIgnoreCase)&&identityEngine.State.ColorFor(ScorebugColorSource.AwayPrimary)=="#FFAA1122","team details save the name, abbreviation, logo and colours");
var identityTarget=new GameEngine();identityTarget.LoadFromState(identityEngine.CloneState());
identityEngine.SetTeamIdentity(true,"Home Town","HT",null,"#FF010203","#FF040506");
identityTarget.UpdateLiveScoreClock(identityEngine.State);
Check(identityTarget.State.HomePrimaryColor=="#FF010203"&&identityTarget.State.AwayTeam=="Rovers FC"&&identityTarget.State.AwayLogoPath is not null,"team details publish live like the score and team names");
identityEngine.SetTeamIdentity(true,"Home Town","HT",null,"not-a-colour","#FF040506");
Check(identityEngine.State.HomePrimaryColor.StartsWith('#')&&identityEngine.State.HomePrimaryColor.Length is 7 or 9,"an invalid team colour never reaches the graphic");
Check(identityEngine.State.ColorFor(ScorebugColorSource.Custom) is null&&identityEngine.State.LogoFor(ScorebugLogoSide.None) is null,"a custom colour and no logo resolve to the element's own values");
identityEngine.SetDivisionGame(true);Check(identityEngine.State.DivisionGame,"a game can be marked as a division game");
var photoRoster=Roster.Create("Photos");var photoPlayer=photoRoster.AddPlayer();photoPlayer.Name="Alex Smith";photoPlayer.PhotoPath=@"C:\photos\alex.png";var photoSelection=SelectedPlayer.From(photoRoster,photoPlayer);Check(photoSelection.PhotoPath.EndsWith("alex.png",StringComparison.OrdinalIgnoreCase),"selecting a player carries their uploaded portrait to the graphic");
var photoLayoutPath=Path.Combine(Path.GetTempPath(),$"photo-layout-{Guid.NewGuid():N}.json");try{var photoCatalog=new ScorebugCatalog(photoLayoutPath);photoCatalog.Update(layout=>layout.Elements.Add(new ScorebugElement{Name="Portrait",Kind=ScorebugElementKind.Image,PlayerPhotoSide=ScorebugPlayerPhotoSide.Home}));Check(new ScorebugCatalog(photoLayoutPath).Layout.Elements[^1].PlayerPhotoSide==ScorebugPlayerPhotoSide.Home,"a player-photo element survives a studio save");}finally{if(File.Exists(photoLayoutPath))File.Delete(photoLayoutPath);}

// ---------- layouts: several scorebugs in one game, one on air at a time ----------
var layoutsPath=Path.Combine(Path.GetTempPath(),$"layouts-{Guid.NewGuid():N}.json");
try
{
    var layouts=new ScorebugCatalog(layoutsPath);
    Check(layouts.Layouts.Count==1&&layouts.Layout.Elements.Count>=8,"a fresh catalog opens with the default layout");
    var second=layouts.AddLayout("Slideshow");
    layouts.Update(layout=>{layout.Elements.Clear();layout.Elements.Add(new ScorebugElement{Name="Slide",Content="SECOND LAYOUT",X=20,Y=30,Width=600,Height=90});});
    Check(layouts.Layout.Name=="Slideshow"&&layouts.Layout.Elements.Count==1,"a new layout holds its own artwork");
    layouts.PublishLayout();
    layouts.SelectLayout(0);
    Check(layouts.ActiveIndex==0&&layouts.PublishedIndex==second,"switching the preview leaves the on-air layout alone until it is published");
    var reloadedLayouts=new ScorebugCatalog(layoutsPath);
    Check(reloadedLayouts.Layouts.Count==2&&reloadedLayouts.Layouts[1].Name=="Slideshow"&&reloadedLayouts.PublishedIndex==1,"layouts and the published choice persist in scorebug.json");
    var copy=reloadedLayouts.AddLayout("Copy",copyActive:true);
    Check(reloadedLayouts.Layouts[copy].Elements[0].Id!=reloadedLayouts.Layouts[1].Elements[0].Id,"a duplicated layout edits independently of the original");
    Check(reloadedLayouts.RemoveLayout(copy)&&reloadedLayouts.Layouts.Count==2,"a layout can be deleted");
}
finally{if(File.Exists(layoutsPath))File.Delete(layoutsPath);}
var lastLayoutPath=Path.Combine(Path.GetTempPath(),$"last-layout-{Guid.NewGuid():N}.json");
try{var single=new ScorebugCatalog(lastLayoutPath);Check(!single.RemoveLayout(0),"the last layout cannot be deleted");}finally{if(File.Exists(lastLayoutPath))File.Delete(lastLayoutPath);}
var legacyLayoutPath=Path.Combine(Path.GetTempPath(),$"legacy-layout-{Guid.NewGuid():N}.json");
try
{
    File.WriteAllText(legacyLayoutPath,JsonSerializer.Serialize(ScorebugLayout.Default()));
    var migrated=new ScorebugCatalog(legacyLayoutPath);
    Check(migrated.Layouts.Count==1&&migrated.Layouts[0].Elements.Count>=8,"an older single-layout file becomes the first layout");
}
finally{if(File.Exists(legacyLayoutPath))File.Delete(legacyLayoutPath);}

// ---------- displays: independent screens, each with its own layouts and a link between them ----------
var displaysPath=Path.Combine(Path.GetTempPath(),$"displays-{Guid.NewGuid():N}.json");
try
{
    var displays=new ScorebugCatalog(displaysPath);
    var program=displays.Displays[0];
    Check(displays.Displays.Count==1&&displays.Displays[0].Layouts.Count==1,"a fresh game starts with one display and one layout");
    var lowerIndex=displays.AddDisplay("Lower thirds");
    var lower=displays.Displays[lowerIndex];
    Check(displays.ActiveDisplayIndex==lowerIndex&&lower.Layouts.Count==1&&lower.Layouts[0].Elements.Count==0,"a new display starts blank and becomes the one being designed");
    displays.AddLayout("Interview");
    var interview=displays.Layout;
    displays.SelectDisplay(0);
    displays.PublishLayout(program.Id,0);
    displays.PublishLayout(lower.Id,1);
    Check(displays.IsPublished(lower.Id,1)&&displays.IsPublished(program.Id,0),"each display publishes its own layout");
    Check(displays.PublishedLayoutOf(lower.Id).Name=="Interview"&&displays.PublishedLayoutOf(program.Id).Name=="Broadcast Classic","two displays can be on air with different layouts");
    displays.SetLink(program.Id,0,lower.Id,interview.Id);
    Check(displays.LinkTarget(program.Id,0) is { } link&&link.DisplayId==lower.Id&&link.LayoutIndex==1,"a layout can link to a layout on another display");
    displays.SetLink(program.Id,0,program.Id,interview.Id);
    Check(displays.LinkTarget(program.Id,0) is null,"a layout cannot link to its own display");
    displays.SetLink(program.Id,0,lower.Id,interview.Id);
    var reopened=new ScorebugCatalog(displaysPath);
    Check(reopened.Displays.Count==2&&reopened.LinkTarget(reopened.Displays[0].Id,0) is not null,"displays and layout links persist in scorebug.json");
    reopened.SelectDisplay(0);
    Check(reopened.RemoveLayout(0)==false&&reopened.Layouts.Count==1,"the last layout on a display cannot be deleted");
    // Layouts can move between displays, or be copied with an independent identity.
    var lowerId=reopened.Displays[1].Id;
    var sourceIds=reopened.Layouts[0].Elements.Select(element=>element.Id).ToArray();
    var copiedIndex=reopened.CopyLayoutToDisplay(0,lowerId);
    var copiedLayout=reopened.Displays[1].Layouts[copiedIndex];
    Check(copiedIndex>=0&&copiedLayout.Name.EndsWith("copy")&&copiedLayout.Elements.Select(element=>element.Id).SequenceEqual(sourceIds)==false,"copying a layout to another display makes an independent copy");
    Check(reopened.Layouts.Count==1,"copying a layout leaves the display it came from untouched");
    reopened.SelectDisplay(1);
    var movedCountBefore=reopened.Layouts.Count;
    Check(reopened.MoveLayoutToDisplay(copiedIndex,reopened.Displays[0].Id),"a layout can be moved to another display");
    Check(reopened.Layouts.Count==movedCountBefore-1&&reopened.ActiveDisplayIndex==0&&reopened.Layouts.Count==2&&reopened.Layouts[1].Id==copiedLayout.Id,"the moved layout joins the target display and stays in view");
    // A move keeps the display being edited on the target, so select the source again each time.
    while(reopened.Displays[1].Layouts.Count>1)
    {
        reopened.SelectDisplay(1);
        reopened.MoveLayoutToDisplay(0,reopened.Displays[0].Id);
    }
    reopened.SelectDisplay(1);
    Check(!reopened.MoveLayoutToDisplay(0,reopened.Displays[0].Id),"a display cannot give away its last layout");
    reopened.RemoveDisplay(1);
    Check(reopened.Displays.Count==1&&reopened.Displays[0].Layouts[0].LinkDisplayId is null,"removing a display clears the links that pointed at it");
    reopened.ResetToDefault();
    Check(reopened.Displays.Count==1&&reopened.Displays[0].Layouts.Count==1,"a new game resets back to one display");
}
finally{if(File.Exists(displaysPath))File.Delete(displaysPath);}

// ---------- video elements: preserved aspect, own audio output ----------
var videoPath=Path.Combine(Path.GetTempPath(),$"video-element-{Guid.NewGuid():N}.json");
try
{
    var video=new ScorebugCatalog(videoPath);
    video.Update(layout=>layout.Elements.Add(new ScorebugElement
    {
        Name="Ad",Kind=ScorebugElementKind.Video,MediaTag="Ads",PreserveVideoAspect=true,
        VideoAspect=4.0/3.0,Width=800,Height=600,AudioDevice="speaker-two"
    }));
    var reopenedVideo=new ScorebugCatalog(videoPath);
    var ad=reopenedVideo.Layout.Elements.First(x=>x.Kind==ScorebugElementKind.Video);
    Check(ad.PreserveVideoAspect&&Math.Abs(ad.VideoAspect-4.0/3.0)<0.001&&ad.AudioDevice=="speaker-two","a video element keeps its preserved aspect ratio and its own audio output");
}
finally{if(File.Exists(videoPath))File.Delete(videoPath);}

// ---------- typography, text transitions and team colour slots ----------
var styledPath=Path.Combine(Path.GetTempPath(),$"styled-{Guid.NewGuid():N}.json");
try
{
    var styledCatalog=new ScorebugCatalog(styledPath);
    styledCatalog.Update(layout=>
    {
        layout.Elements.Clear();
        layout.Elements.Add(new ScorebugElement
        {
            Name="Styled",FontFamily="Georgia",Bold=false,Italic=true,Underline=true,StrikeThrough=true,
            Transition=ScorebugTransition.WheelUp,ForegroundSource=ScorebugColorSource.AwayPrimary,BackgroundSource=ScorebugColorSource.HomeSecondary
        });
    });
    var styledReloaded=new ScorebugCatalog(styledPath);
    var savedStyle=styledReloaded.Layout.Elements.Single();
    Check(savedStyle.FontFamily=="Georgia"&&!savedStyle.Bold&&savedStyle.Italic&&savedStyle.Underline&&savedStyle.StrikeThrough,"font family, bold, italic, underline and strike persist in the layout");
    Check(savedStyle.Transition==ScorebugTransition.WheelUp&&savedStyle.ForegroundSource==ScorebugColorSource.AwayPrimary&&savedStyle.BackgroundSource==ScorebugColorSource.HomeSecondary,"the text transition and team colour slots persist in the layout");
}
finally{if(File.Exists(styledPath))File.Delete(styledPath);}
Check(Enum.GetValues<ScorebugTransition>().Length>=10&&Enum.GetValues<ScorebugColorSource>().Length==5,"there are at least ten value animations and five colour sources");

// ---------- rotation, borders and a configurable transition speed ----------
var decorPath=Path.Combine(Path.GetTempPath(),$"decor-{Guid.NewGuid():N}.json");
try
{
    var decor=new ScorebugCatalog(decorPath);
    decor.Update(layout=>layout.Elements.Add(new ScorebugElement
    {
        Name="Slanted",Rotation=-7.5,BorderThickness=3,BorderColor="#FF00FF00",
        BorderSource=ScorebugColorSource.AwaySecondary,TransitionSeconds=2.4
    }));
    var reloadedDecor=new ScorebugCatalog(decorPath).Layout.Elements[^1];
    Check(reloadedDecor.Rotation==-7.5&&reloadedDecor.BorderThickness==3&&reloadedDecor.BorderColor=="#FF00FF00"&&reloadedDecor.BorderSource==ScorebugColorSource.AwaySecondary&&reloadedDecor.TransitionSeconds==2.4,"rotation, border and transition speed persist in the layout");
    decor.Update(layout=>{layout.Elements[^1].Rotation=900;layout.Elements[^1].TransitionSeconds=9;layout.Elements[^1].BorderThickness=90;});
    var clampedDecor=new ScorebugCatalog(decorPath).Layout.Elements[^1];
    Check(clampedDecor.Rotation==360&&clampedDecor.TransitionSeconds==5&&clampedDecor.BorderThickness==40,"rotation, transition speed and border width are clamped to safe ranges");
}
finally{if(File.Exists(decorPath))File.Delete(decorPath);}

// ---------- play clock reaches and prints zero ----------
var playClock=new GameEngine();playClock.NewGame("football");
playClock.SetPlayClock(2);playClock.TogglePlayClock();Thread.Sleep(1150);playClock.Tick();
Check(playClock.State.PlayClockSeconds<=1&&playClock.State.PlayClockRunning,"the play clock counts down while it runs");
Thread.Sleep(1250);playClock.Tick();
Check(playClock.State.PlayClockSeconds==0&&!playClock.State.PlayClockRunning,"the play clock reaches zero and stops there");
Check(ScoreboardTemplates.Bind("{{PLAY_CLOCK}}",playClock.State)=="0","the graphic prints zero when the play clock expires");
var pauseClock=new GameEngine();pauseClock.NewGame("football");
pauseClock.SetClock(44);pauseClock.SetPlayClock(40);
pauseClock.ToggleClock();pauseClock.TogglePlayClock();
Thread.Sleep(1100);
pauseClock.ToggleClock();pauseClock.TogglePlayClock();
Check(pauseClock.State.ClockSeconds==44&&!pauseClock.State.ClockRunning
    &&pauseClock.State.PlayClockSeconds==40&&!pauseClock.State.PlayClockRunning,
    "stopping the game clock or play clock preserves the displayed value instead of consuming the elapsed fraction");
var carryClock=new GameEngine();carryClock.NewGame("football");
carryClock.SetClock(44);carryClock.SetPlayClock(40);
carryClock.ToggleClock();carryClock.TogglePlayClock();
Thread.Sleep(300);carryClock.Tick();
carryClock.ToggleClock();carryClock.TogglePlayClock();
Check(carryClock.State.ClockSeconds==44&&carryClock.State.PlayClockSeconds==40,
    "a paused clock keeps its displayed value while holding the elapsed fraction");
Thread.Sleep(350);
carryClock.ToggleClock();carryClock.TogglePlayClock();
Thread.Sleep(850);carryClock.Tick();
Check(carryClock.State.ClockSeconds==43&&carryClock.State.PlayClockSeconds==39,
    "resuming a clock keeps its sub-second time so the next change happens when it is due");

// ---------- rosters, player stats and the selected player on the graphic ----------
var rosterPath=Path.Combine(Path.GetTempPath(),$"rosters-{Guid.NewGuid():N}.json");
RosterStore.UsePath(rosterPath);
try
{
    var book=RosterStore.Book;
    var home=RosterStore.Book.Add("Home squad");
    var away=RosterStore.Book.Add("Visitors squad");
    var qb=home.AddPlayer(); qb.Number=12; qb.Name="Smith"; qb.Position="QB";
    var rb=home.AddPlayer(); rb.Number=28; rb.Name="Jones"; rb.Position="RB";
    foreach(var player in home.Players){player.SetStat("Yards",0);player.SetStat("Touchdowns",0);}
    qb.SetStat("Yards",184); qb.SetStat("Touchdowns",2);
    rb.SetStat("Yards",96); rb.SetStat("Touchdowns",1);
    var wr=away.AddPlayer(); wr.Number=7; wr.Name="Brown"; wr.Position="WR"; wr.SetStat("Yards",64);
    RosterStore.Save();
    var savedBook=new RosterBook();
    RosterStore.Reload();
    Check(RosterStore.All().Count==2&&RosterStore.Book.Find(home.Id)?.Players.Count==2&&RosterStore.Book.Find(home.Id)!.Find(qb.Id)!.Stat("yards")==184,"a roster library saves players, numbers, positions and stats to disk");
    Check(RosterStore.Book.StatNames().SequenceEqual(["Touchdowns","Yards"]),"the library collects every stat name for the studio");

    var rosterEngine=new GameEngine();
    rosterEngine.NewGame("football");
    rosterEngine.AssignRoster(true,home.Id);
    rosterEngine.AssignRoster(false,away.Id);
    Check(rosterEngine.State.HomePlayer is null&&rosterEngine.State.AwayPlayer is null,"assigning a roster leaves the graphic empty until a player is selected");
    rosterEngine.SelectPlayer(true,qb.Id);
    rosterEngine.SelectPlayer(false,wr.Id);
    Check(rosterEngine.State.HomePlayer?.Number==12&&rosterEngine.State.AwayPlayer?.Name=="Brown","selecting a player puts them on the graphic");
    Check(ScoreboardTemplates.Bind("{{PLAYER:HOME:Name}} #{{PLAYER:HOME:NUMBER}} {{PLAYER:HOME:Position}}",rosterEngine.State)=="Smith #12 QB","player tokens print the selected player's name, number and position");
    Check(ScoreboardTemplates.Bind("{{PLAYER:HOME:Yards}} yds · {{PLAYER:HOME:Touchdowns}} TD",rosterEngine.State)=="184 yds · 2 TD","player stat tokens print the selected player's stats");
    Check(ScoreboardTemplates.Bind("{{PLAYER:AWAY:stat:yards}}",rosterEngine.State)=="64","player stat tokens accept the Stat: prefix and any case");
    Check(ScoreboardTemplates.Bind("{{PLAYER:HOME:Missing}}",rosterEngine.State)==""&&ScoreboardTemplates.Bind("{{PLAYER:AWAY:Name}}",new GameEngine().State)=="","a missing stat or a side with no player renders empty");

    var beforeSelect=GameEngine.StagedSignature(rosterEngine.State);
    rosterEngine.SelectPlayer(true,rb.Id);
    Check(ScoreboardTemplates.Bind("{{PLAYER:HOME:Name}} {{PLAYER:HOME:Yards}}",rosterEngine.State)=="Jones 96","selecting another player switches every selected-player binding");
    Check(GameEngine.StagedSignature(rosterEngine.State)==beforeSelect,"a player switch does not wait for Update Output: player data publishes live");
    var livePlayerTarget=new GameEngine();
    livePlayerTarget.LoadFromState(rosterEngine.CloneState());
    rosterEngine.SelectPlayer(true,qb.Id);
    Check(livePlayerTarget.UpdateLiveScoreClock(rosterEngine.State)&&livePlayerTarget.State.HomePlayer?.Name=="Smith","live publishing carries a player switch straight to the graphic");
    rosterEngine.SelectPlayer(true,rb.Id);
    Check(rosterEngine.Undo()&&ScoreboardTemplates.Bind("{{PLAYER:HOME:Name}}",rosterEngine.State)=="Smith","a player switch can be undone");

    var liveNamesTarget=new GameEngine();
    liveNamesTarget.LoadFromState(rosterEngine.CloneState());
    rosterEngine.SetTeams("Home Squad","Visitors Squad");
    liveNamesTarget.UpdateLiveScoreClock(rosterEngine.State);
    Check(liveNamesTarget.State.HomePlayer?.Name=="Smith","renaming teams live leaves the selected players alone");

    // Editing the roster entry reaches the graphic without reselecting.
    var savedAgain=RosterStore.Book.Find(home.Id)!.Find(qb.Id)!;
    savedAgain.Name="Smithson"; savedAgain.SetStat("Yards",201);
    rosterEngine.SelectPlayer(true,qb.Id);
    rosterEngine.ReloadSelectedPlayer(true);
    Check(ScoreboardTemplates.Bind("{{PLAYER:HOME:Name}} {{PLAYER:HOME:Yards}}",rosterEngine.State)=="Smithson 201","editing a player updates the graphic without reselecting them");

    var rosterGame=Path.Combine(Path.GetTempPath(),$"roster-game-{Guid.NewGuid():N}.spg");
    rosterEngine.Save(rosterGame);
    var rosterLoaded=new GameEngine();
    rosterLoaded.Load(rosterGame);
    Check(rosterLoaded.State.HomeRosterId==home.Id&&rosterLoaded.State.AwayPlayer?.Name=="Brown"&&ScoreboardTemplates.Bind("{{PLAYER:HOME:Name}}",rosterLoaded.State)=="Smithson","a saved game keeps its rosters and selected players");
    File.Delete(rosterGame);

    rosterLoaded.NewGame("football");
    Check(rosterLoaded.State.HomePlayer is null&&rosterLoaded.State.HomeRosterId is null&&RosterStore.All().Count==2,"a new game clears the selections but keeps the roster library");
}
finally { RosterStore.UsePath(null); try { if(File.Exists(rosterPath)) File.Delete(rosterPath); } catch { } }

// ---------- custom stats can hold any text ----------
var textStatEngine=new GameEngine();textStatEngine.NewGame("football");
var textStat=textStatEngine.AddStat("Penalties");
var textBefore=GameEngine.StagedSignature(textStatEngine.State);
textStatEngine.SetStatText(textStat.Id,"3rd");
Check(textStatEngine.State.Stats.Single().Display=="3rd"&&textStatEngine.State.Stats.Single().Text=="3rd","a custom stat keeps free text instead of only a number");
Check(ScoreboardTemplates.Bind("{{STAT:Penalties}}",textStatEngine.State)=="3rd","a text stat prints on the graphic exactly as typed");
Check(GameReport.Build(textStatEngine.State).Contains("Penalties: 3rd"),"a text stat appears in the game report");
Check(GameEngine.StagedSignature(textStatEngine.State)!=textBefore,"a text stat waits for Update Output like any other value");
textStatEngine.SetStatText(textStat.Id,"412");
Check(textStatEngine.State.Stats.Single().Value==412&&textStatEngine.State.Stats.Single().Text is null&&textStatEngine.State.Stats.Single().Display=="412","numeric text becomes the stat's number again");
textStatEngine.AdjustStat(textStat.Id,1);
Check(textStatEngine.State.Stats.Single().Value==413&&textStatEngine.State.Stats.Single().IsNumber,"stepping a stat works after it held text");
var textStatGame=Path.Combine(Path.GetTempPath(),$"text-stat-{Guid.NewGuid():N}.spg");
textStatEngine.SetStatText(textStat.Id,"N/A");textStatEngine.Save(textStatGame);
var textStatLoaded=new GameEngine();textStatLoaded.Load(textStatGame);
Check(ScoreboardTemplates.Bind("{{STAT:Penalties}}",textStatLoaded.State)=="N/A","a text stat persists in a saved game");
File.Delete(textStatGame);

// ---------- gradient and opacity on every studio element ----------
var gradientPath=Path.Combine(Path.GetTempPath(),$"gradient-{Guid.NewGuid():N}.json");
try
{
    var gradientCatalog=new ScorebugCatalog(gradientPath);
    gradientCatalog.Update(layout=>layout.Elements.Add(new ScorebugElement
    {
        Name="Gradient plate",BackgroundGradient=true,Background="#FF102030",BackgroundColor2="#FF405060",
        BackgroundSource2=ScorebugColorSource.AwayPrimary,GradientAngle=45,Opacity=0.6
    }));
    var gradientReloaded=new ScorebugCatalog(gradientPath).Layout.Elements[^1];
    Check(gradientReloaded.BackgroundGradient&&gradientReloaded.BackgroundColor2=="#FF405060"
        &&gradientReloaded.BackgroundSource2==ScorebugColorSource.AwayPrimary
        &&Math.Abs(gradientReloaded.GradientAngle-45)<0.001&&Math.Abs(gradientReloaded.Opacity-0.6)<0.001,
        "a gradient background and element opacity persist in the layout");
    gradientCatalog.Update(layout=>{layout.Elements[^1].GradientAngle=900;layout.Elements[^1].Opacity=3;});
    var clampedGradient=new ScorebugCatalog(gradientPath).Layout.Elements[^1];
    Check(clampedGradient.GradientAngle==360&&clampedGradient.Opacity==1,"gradient angle and opacity are clamped to safe ranges");
}
finally{if(File.Exists(gradientPath))File.Delete(gradientPath);}

// ---------- player stat columns arrive with the player ----------
var presetRoster=Roster.Create("Preset check");
var presetPlayer=presetRoster.AddPlayer();
var presetColumns=PlayerStatPresets.For("football");
Check(presetColumns.Contains("Passing yards")&&presetColumns.Contains("Tackles"),"each sport ships with its own player stat columns");
Check(presetRoster.EnsureColumns(presetColumns),"giving a roster its columns reports the columns it added");
Check(presetPlayer.Stats.Select(s=>s.Name).SequenceEqual(presetColumns),"a new player starts with every stat column already in place");
Check(!presetRoster.EnsureColumns(presetColumns),"adding the same columns twice changes nothing");
presetPlayer.SetStat("Tackles",7);
presetRoster.EnsureColumns(PlayerStatPresets.For("basketball"));
Check(presetPlayer.Stat("Tackles")==7&&presetPlayer.Stat("Rebounds")==0,"switching sports adds columns without losing a value");

// ---------- .spr / .spp roster and player files ----------
var exportPath=Path.Combine(Path.GetTempPath(),$"export-{Guid.NewGuid():N}.spr");
var playerPath=Path.Combine(Path.GetTempPath(),$"export-{Guid.NewGuid():N}.spp");
try
{
    var shared=Roster.Create("Shared squad");
    var one=shared.AddPlayer(); one.Number=9; one.Name="Ada"; one.Position="QB"; one.SetStat("Yards",250);
    var two=shared.AddPlayer(); two.Number=4; two.Name="Bea"; two.Position="WR"; two.SetStat("Yards",80);
    RosterFiles.SaveRoster(exportPath,shared);
    var readBack=RosterFiles.LoadRoster(exportPath);
    Check(readBack is not null&&readBack.Name=="Shared squad"&&readBack.Players.Count==2
        &&readBack.Players[0].Name=="Ada"&&readBack.Players[0].Position=="QB"&&readBack.Players[0].Number==9,
        "a .spr file round trips the roster and its players");
    Check(readBack!.Id!=shared.Id&&readBack.Players[0].Id!=one.Id,"an imported roster and its players get fresh ids");
    // A shared roster says who is on the team, not what they have done: columns travel, values do not.
    Check(readBack.Players.All(p=>p.Stats.All(s=>s.Value==0&&s.Text is null)),
        "a .spr file carries no stat values");
    Check(readBack.Players[0].Stats.Any(s=>s.Name=="Yards"),"a .spr file keeps the team's stat columns");
    RosterFiles.SavePlayer(playerPath,two,"Shared squad");
    var readPlayer=RosterFiles.LoadPlayer(playerPath);
    Check(readPlayer is not null&&readPlayer.Name=="Bea"&&readPlayer.Number==4&&readPlayer.Stat("Yards")==0,
        "a .spp file round trips one player with a blank stat sheet");
    Check(RosterFiles.LoadRoster(playerPath) is null,"a player file is not mistaken for a roster file");
    File.WriteAllText(playerPath,"not a roster");
    Check(RosterFiles.LoadPlayer(playerPath) is null,"a damaged file loads as nothing instead of throwing");
}
finally
{
    if(File.Exists(exportPath))File.Delete(exportPath);
    if(File.Exists(playerPath))File.Delete(playerPath);
}

// ---------- every sport arrives with a full stat sheet ----------
foreach(var sport in SportDefinition.BuiltIns)
{
    var columns=PlayerStatPresets.For(sport.Id);
    Check(columns.Count>=20,"a sport ships with at least twenty player stat columns ("+sport.Id+" has "+columns.Count+")");
    Check(columns.Distinct(StringComparer.OrdinalIgnoreCase).Count()==columns.Count,"a sport's stat columns are unique ("+sport.Id+")");
}

// ---------- sport-specific data, and a game that carries its screens ----------
var sportData=new GameEngine();sportData.NewGame("baseball");
Check(!sportData.HasSportSpecificData(),"a fresh game carries no sport-specific data");
sportData.BaseballBall();
Check(sportData.HasSportSpecificData(),"a ball in the count counts as sport-specific data worth warning about");

var designPath=Path.Combine(Path.GetTempPath(),$"design-{Guid.NewGuid():N}.json");
var reopenedPath=Path.Combine(Path.GetTempPath(),$"design-{Guid.NewGuid():N}.json");
var gamePath=Path.Combine(Path.GetTempPath(),$"design-game-{Guid.NewGuid():N}.spg");
var legacyPath=Path.Combine(Path.GetTempPath(),$"legacy-{Guid.NewGuid():N}.spg");
try
{
    var designCatalog=new ScorebugCatalog(designPath);
    designCatalog.AddDisplay("Lower thirds");
    designCatalog.SetTransition(LayoutTransition.Zoom,1.5);
    var designEngine=new GameEngine();designEngine.NewGame("football");
    designEngine.Save(gamePath,designCatalog.ExportDocument());
    var reopened=new GameEngine();var design=reopened.Load(gamePath);
    Check(design is {Displays.Count:2},"a saved game carries every display and its layouts");
    var reopenedCatalog=new ScorebugCatalog(reopenedPath);
    reopenedCatalog.ApplyDocument(design);
    Check(reopenedCatalog.Displays.Count==2&&reopenedCatalog.Displays[1].Name=="Lower thirds"
        &&reopenedCatalog.Displays[1].Layouts[0].Transition==LayoutTransition.Zoom,"opening a game restores its layouts and their hand-over");
    designEngine.Save(legacyPath);
    Check(new GameEngine().Load(legacyPath) is null,"a game written without a design still loads, with no design to apply");
}
finally
{
    foreach(var path in new[]{gamePath,designPath,reopenedPath,legacyPath})
    {
        try{if(File.Exists(path))File.Delete(path);}catch{}
    }
}

// ---------- no player on the graphic is a choice ----------
var clearEngine=new GameEngine();clearEngine.NewGame("football");
var clearRoster=RosterStore.Book.Add("Clear check");
clearEngine.AssignRoster(true,clearRoster.Id);
var someone=clearRoster.AddPlayer();someone.Number=3;someone.Name="Someone";
clearEngine.SelectPlayer(true,someone.Id);
Check(clearEngine.State.HomePlayer?.Name=="Someone","selecting a player puts them on the graphic");
clearEngine.ClearSelectedPlayer(true);
Check(clearEngine.State.HomePlayer is null&&clearEngine.State.HomePlayerCleared,"clearing takes the player off the graphic");
clearEngine.ReloadSelectedPlayer(true);
Check(clearEngine.State.HomePlayer is null,"a cleared player stays cleared when the roster is reloaded");
clearEngine.SelectPlayer(true,someone.Id);
Check(clearEngine.State.HomePlayer?.Name=="Someone"&&!clearEngine.State.HomePlayerCleared,"selecting again puts a player back");

// ---------- layout hand-over between layouts, .spl / .spd files, and the web export ----------
var transitionPath=Path.Combine(Path.GetTempPath(),$"transition-{Guid.NewGuid():N}.json");
try
{
    var transitions=new ScorebugCatalog(transitionPath);
    Check(transitions.Layout.Transition==LayoutTransition.Cut&&transitions.Layout.TransitionSeconds==0,"a layout starts with a cut onto air");
    transitions.SetTransition(LayoutTransition.Zoom,9);
    Check(transitions.Layout.Transition==LayoutTransition.Zoom&&Math.Abs(transitions.Layout.TransitionSeconds-5)<0.001,"a layout hand-over is capped at five seconds");
    var second=transitions.AddLayout("Lower third");
    transitions.SetTransition(LayoutTransition.SlideLeft,1.2);
    Check(transitions.Layouts[second].Transition==LayoutTransition.SlideLeft,"each layout carries its own hand-over");
    Check(transitions.Layouts[0].Transition==LayoutTransition.Zoom&&Math.Abs(transitions.Layouts[0].TransitionSeconds-5)<0.001,"setting one layout's hand-over leaves the others alone");
    var reloadedTransitions=new ScorebugCatalog(transitionPath);
    Check(reloadedTransitions.Layouts[0].Transition==LayoutTransition.Zoom&&Math.Abs(reloadedTransitions.Layouts[0].TransitionSeconds-5)<0.001&&reloadedTransitions.Layouts[second].Transition==LayoutTransition.SlideLeft,"every layout keeps its own hand-over in the saved game");
    Check(Enum.GetValues<LayoutTransition>().Contains(LayoutTransition.Move),"a layout can slide moved items to their new positions");
    reloadedTransitions.SetTransition(LayoutTransition.Move,0.8);
    Check(new ScorebugCatalog(transitionPath).Layout.Transition==LayoutTransition.Move,"the move hand-over persists in the layout file");
    Check(Enum.GetValues<LayoutTransition>().Length>=5,"at least five layout hand-overs are offered");
}
finally { if(File.Exists(transitionPath))File.Delete(transitionPath); }

var exportLayoutPath=Path.Combine(Path.GetTempPath(),$"layout-{Guid.NewGuid():N}.spl");
var displayPath=Path.Combine(Path.GetTempPath(),$"display-{Guid.NewGuid():N}.spd");
try
{
    var source=new ScorebugLayout{Name="Lower third"};
    source.Elements.Add(new ScorebugElement{Name="Widget",Kind=ScorebugElementKind.Html,Content="<b>Hi</b>",Rotation=12,BackgroundGradient=true,BackgroundColor2="#FF102030",TransitionSeconds=1.4,TransparentBackground=true});
    ScorebugFiles.SaveLayout(exportLayoutPath,source);
    var loadedLayout=ScorebugFiles.LoadLayout(exportLayoutPath)!;
    Check(loadedLayout.Name=="Lower third"&&loadedLayout.Elements.Single().BackgroundGradient&&loadedLayout.Elements.Single().Kind==ScorebugElementKind.Html&&loadedLayout.Elements[0].Content=="<b>Hi</b>"&&Math.Abs(loadedLayout.Elements[0].Rotation-12)<0.001,"a ScorePlus Layout file carries the artwork exactly");
    var freshLayout=ScorebugFiles.FreshCopy(loadedLayout);
    Check(freshLayout.Id!=loadedLayout.Id&&freshLayout.Elements[0].Id!=loadedLayout.Elements[0].Id,"an imported layout gets its own identity");

    var importPath=Path.Combine(Path.GetTempPath(),$"import-{Guid.NewGuid():N}.json");
    try
    {
        var importCatalog=new ScorebugCatalog(importPath);
        var before=importCatalog.ActiveDisplay.Layouts.Count;
        importCatalog.ImportLayout(loadedLayout);
        Check(importCatalog.ActiveDisplay.Layouts.Count==before+1&&importCatalog.ActiveDisplay.ActiveIndex==before,"an imported layout is added to the display and opened");
        importCatalog.ImportLayout(loadedLayout);
        Check(importCatalog.ActiveDisplay.Layouts.Select(l=>l.Id).Distinct().Count()==before+2,"importing the same layout twice leaves two independent layouts");
    }
    finally { if(File.Exists(importPath))File.Delete(importPath); }

    var display=new ScorebugDisplay{Name="Lower thirds"};
    display.Layouts.Add(source);
    display.Layouts.Add(new ScorebugLayout{Name="Ticker"});
    display.PublishedIndex=1;
    ScorebugFiles.SaveDisplay(displayPath,display);
    var loadedDisplay=ScorebugFiles.LoadDisplay(displayPath)!;
    Check(loadedDisplay.Name=="Lower thirds"&&loadedDisplay.Layouts.Count==2&&loadedDisplay.PublishedIndex==1,"a ScorePlus Display file carries every layout and its order");
    var freshDisplay=ScorebugFiles.FreshDisplay(loadedDisplay);
    Check(freshDisplay.Id!=loadedDisplay.Id&&freshDisplay.Layouts.All(l=>loadedDisplay.Layouts.All(o=>o.Id!=l.Id)),"an imported display gets fresh ids");

    var webGame=new GameEngine();webGame.NewGame("football");webGame.Score(true,7);
    var page=ScorebugHtml.Build(ScorebugLayout.Default(),webGame.State,"Broadcast");
    Check(page.Contains("data-token")&&page.Contains("ScorePlus")&&page.Contains("<html"),"the web export keeps the tokens a site can update");
    Check(page.Contains(">7<")||page.Contains("7"),"the web export prints the bound values onto the page");

    var arrowGame=new GameEngine();arrowGame.NewGame("football");arrowGame.SetPossession(true);
    var arrowLayout=new ScorebugLayout{Elements=[
        new ScorebugElement{Name="Home arrow",Content="",Icon=ScorebugIcon.PossessionArrow,Width=80,Height=40},
        new ScorebugElement{Name="Away arrow",Content="",Icon=ScorebugIcon.PossessionArrow,IconVisitors=true,Width=80,Height=40}]};
    var homeArrowPage=ScorebugHtml.Build(arrowLayout,arrowGame.State,"Possession");
    Check(homeArrowPage.Contains("class=\"sp-arrow in")&&!homeArrowPage.Contains("class=\"sp-arrow out"),"the possession arrow only marks the team with the ball");
    arrowGame.SetPossession(false);
    Check(ScorebugHtml.Build(arrowLayout,arrowGame.State,"Possession").Contains("class=\"sp-arrow out flip"),"the possession arrow follows the ball to the other plate");
    var freshItem=new ScorebugElement{Name="Fresh",Content="X"};
    Check(freshItem.TextAlign==ScorebugTextAlign.Center,"a new element centres its text");
    Check(Math.Abs(freshItem.TextShadowAngle-45)<0.001,"a new element's shadow falls down and to the right");
    var uncutLayout=new ScorebugLayout{Name="Fresh layout"};
    Check(uncutLayout.Transition==LayoutTransition.Cut&&uncutLayout.TransitionSeconds==0,"a new layout cuts instead of dissolving");
    var shadowItem=new ScorebugElement{Name="Shadowed",Content="TEXAS",TextShadow=true,TextShadowColor="#CC000000",TextShadowOffset=5,TextShadowSoftness=0};
    Check(ScorebugHtml.Build(new ScorebugLayout{Elements=[shadowItem]},arrowGame.State,"Shadow").Contains("text-shadow:"),"a text shadow is written into the web export");

    var reportRoster=Roster.Create("Home Roster");
    var bench=reportRoster.AddPlayer();bench.Name="Bench";bench.SetStat("Yards",0);
    var fullReport=GameReport.Build(webGame.State,[reportRoster]);
    Check(fullReport.Contains("Home Roster")&&fullReport.Contains("Bench"),"the game report lists every player, including a zero stat line");

    // ---------- per-piece hand-over keys and temporary pieces ----------
    var pieceClip=new MotionClip{Name="Piece test",Kind=MotionKind.Transition};
    var pieceEnter=new MotionPhase("Enter",0.5);
    pieceEnter.Tracks.Add(new MotionTrack(MotionTarget.X,(0,0),(0.5,120)){PieceId="element-a"});
    pieceEnter.Tracks.Add(new MotionTrack(MotionTarget.Opacity,(0,0),(0.5,1)));
    pieceClip.Phases.Add(pieceEnter);
    pieceClip.Pieces.Add(new MotionPiece{Name="Flash",Text="GO",Width=320,Height=120});
    pieceClip.Normalize();
    Check(pieceClip.Phase("Enter")!.Tracks.Count==2
        &&pieceClip.Phase("Enter")!.Tracks.Any(track=>track.PieceId=="element-a")
        &&pieceClip.Phase("Enter")!.Tracks.Any(track=>track.PieceId==""),"a transition keeps whole-graphic and per-piece tracks side by side");
    var pieceCopy=pieceClip.Snapshot();
    Check(pieceCopy.Pieces.Count==1&&pieceCopy.Pieces[0].Name=="Flash"
        &&pieceCopy.Phase("Enter")!.Tracks.Any(track=>track.PieceId=="element-a"),"a saved clip carries its temporary pieces and piece-scoped keys");

    // ---------- Daktronics All Sport 5000 RTD decoding ----------
    // Build one RTD packet exactly as the console does: SYN SOH header STX text EOT checksum ETX.
    static byte[] DaktronicsFrame(int offset,string text)
    {
        var body=new List<byte>{0x01};
        body.AddRange(System.Text.Encoding.ASCII.GetBytes("004210"+offset.ToString("D4")));
        body.Add(0x02);
        body.AddRange(System.Text.Encoding.ASCII.GetBytes(text));
        body.Add(0x04);
        var checksum=body.Aggregate(0,static (sum,value)=>(sum+value)&0xFF);
        var frame=new List<byte>{0x16};
        frame.AddRange(body);
        frame.AddRange(System.Text.Encoding.ASCII.GetBytes(checksum.ToString("X2")));
        frame.Add(0x17);
        return frame.ToArray();
    }

    var rtd=new DaktronicsRtdDecoder("football");
    DeviceSnapshot? rtdState=null;
    foreach(var (offset,text) in new[]{
        (1,"16:09"),(108,"  12"),(112,"   7"),(142," 2"),(122," 3"),(130," 1"),
        (201," 0:25.0z"),(220,"35"),(222,"1ST"),(225,"10")})
        rtdState=rtd.Feed(DaktronicsFrame(offset,text)) ?? rtdState;
    Check(rtdState?.ClockSeconds==969&&rtdState.HomeScore==12&&rtdState.AwayScore==7&&rtdState.Period==2,
        "Daktronics RTD packets splice clock, score and period fields into the persistent buffer");
    Check(rtdState.HomeTimeouts==3&&rtdState.AwayTimeouts==1&&rtdState.PlayClockSeconds==25,
        "Daktronics RTD decodes timeouts and the football play clock");
    Check(rtdState.Down==1&&rtdState.YardsToGo==10&&rtdState.BallOn==35,
        "Daktronics RTD decodes down, distance and ball spot");
    var hardwareGame=new GameEngine();hardwareGame.NewGame("football");hardwareGame.HardwareControlled=true;
    Check(hardwareGame.ApplyHardwareSnapshot(rtdState!),
        "a device snapshot changes the live game");
    Check(hardwareGame.State.HomeScore==12&&hardwareGame.State.AwayScore==7&&hardwareGame.State.ClockSeconds==969
        &&hardwareGame.State.Period==2&&hardwareGame.State.Down==1&&hardwareGame.State.YardsToGo==10,
        "hardware values reach the console without an operator undo point");
    var hardwareOutput=new GameEngine();hardwareOutput.LoadFromState(hardwareGame.CloneState());
    Check(hardwareOutput.UpdateLiveController(hardwareGame.State)==false,
        "a controller update is idempotent for the published copy");
    hardwareGame.SetFootballDistance(3,42);hardwareGame.SetPeriod(3);
    Check(hardwareOutput.UpdateLiveController(hardwareGame.State)
        &&hardwareOutput.State.YardsToGo==3&&hardwareOutput.State.YardsToGoal==42&&hardwareOutput.State.Period==3,
        "controller updates carry down, distance and period to the audience copy");
    hardwareGame.HardwareControlled=false;

    var basketballRtd=new DaktronicsRtdDecoder("basketball");
    DeviceSnapshot? basketballState=null;
    foreach(var (offset,text) in new[]{
        (1," 0:44"),(108,"  21"),(112,"  19"),(201," 0:18.4z"),(236," 4"),(238," 2")})
        basketballState=basketballRtd.Feed(DaktronicsFrame(offset,text)) ?? basketballState;
    Check(basketballState?.ShotClockSeconds==18&&basketballState.HomeFouls==4&&basketballState.AwayFouls==2,
        "Daktronics RTD uses the basketball shot-clock and foul fields");

    // ---------- every clock button reaches the software ----------
    // RTD sends clock values but no running flag, so each clock is judged from its own readings:
    // a value that decreases is running, one that holds still was stopped.
    var runTrack=new ClockRunTracker(1000);
    Check(runTrack.Track(600,0)==null,"one clock reading does not guess whether the clock runs");
    Check(runTrack.Track(599,500)==true&&runTrack.Track(598,1000)==true,"a decreasing clock is running");
    Check(runTrack.Track(598,2600)==false,"a clock that holds still is stopped");
    Check(runTrack.Track(540,2700)==true&&runTrack.Track(540,3800)==false,
        "a clock set back reads as running until it holds still again");
    runTrack.Reset();
    Check(runTrack.Track(24,4000)==null,"a tracker that was reset forgets the previous game");
    var shotTrack=new ClockRunTracker(1000);
    // The shot and play clocks move independently of the game clock.
    shotTrack.Track(24,0);shotTrack.Track(23,500);
    var playTrack=new ClockRunTracker(1000);
    playTrack.Track(25,0);playTrack.Track(25,3000);
    Check(shotTrack.Track(22,1000)==true&&playTrack.Track(25,3500)==false,
        "the shot clock and the play clock are tracked separately");

    // ---------- b1.0: clock shapes, team records and a sport switch that keeps the teams ----------
    Check(GameEngine.FormatClock(600,ClockStyle.Padded)=="10:00"
        &&GameEngine.FormatClock(599,ClockStyle.Padded)=="09:59"
        &&GameEngine.FormatClock(59,ClockStyle.Padded)=="0:59",
        "the padded clock prints 10:00, 09:59 and 0:59");
    Check(GameEngine.FormatClock(600,ClockStyle.Compact)=="10:00"
        &&GameEngine.FormatClock(599,ClockStyle.Compact)=="9:59"
        &&GameEngine.FormatClock(59,ClockStyle.Compact)==":59",
        "the compact clock drops the leading zero and prints :59 under a minute");
    var identityGame=new GameEngine();identityGame.NewGame("football");
    identityGame.SetTeamIdentity(true,"Eagles","EAG",null,"#FF112233","#FF445566","Birds","8-2");
    Check(ScoreboardTemplates.Bind("{{HOME_RECORD}}",identityGame.State)=="8-2",
        "a team record is saved in team details and prints through its binding");
    identityGame.NewGame("soccer",keepTeamIdentity:true);
    Check(identityGame.State.HomeTeam=="Eagles"&&identityGame.State.HomeRecord=="8-2"&&identityGame.State.SportId=="soccer",
        "switching sport keeps the teams and their records");
    var identityReloaded=new GameEngine();identityReloaded.LoadFromState(identityGame.State);
    Check(identityReloaded.State.HomeRecord=="8-2","a team record survives a state reload");

    // ---------- b1.0: automatic results and quick buttons in the game save ----------
    Check(GameEngine.ApplyResult("4-0",1)=="5-0"&&GameEngine.ApplyResult("4-0",-1)=="4-1",
        "a win or loss adds to a two-part record without inventing a tie column");
    Check(GameEngine.ApplyResult("4-0",0)=="4-0-1"&&GameEngine.ApplyResult("5-7-1",1)=="6-7-1",
        "a tie becomes win-loss-tie and an existing tie column is kept");
    Check(GameEngine.ApplyResult("",1)==""&&GameEngine.ApplyResult("not a record",1)=="not a record",
        "an empty or malformed record is never changed by a result");
    var autoRecord=new GameEngine();autoRecord.NewGame("basketball");
    autoRecord.State.Period=autoRecord.State.TotalPeriods;autoRecord.State.ClockSeconds=1;
    autoRecord.State.HomeRecord="4-0";autoRecord.State.AwayRecord="5-7-1";autoRecord.Score(true,2);
    GameEngine.AutoUpdateRecordEnabled=true;autoRecord.State.ClockRunning=true;Thread.Sleep(1100);autoRecord.Tick();
    GameEngine.AutoUpdateRecordEnabled=false;
    Check(autoRecord.State.HomeRecord=="5-0"&&autoRecord.State.AwayRecord=="5-8-1"
        &&autoRecord.State.GameEnded&&autoRecord.State.RecordSettled,
        "the final whistle marks the game final and adds a win and a loss to both team records");
    var endedGame=new GameEngine();endedGame.NewGame("baseball");
    endedGame.State.HomeRecord="4-0";endedGame.State.AwayRecord="";endedGame.Score(true,1);
    Check(endedGame.EndGame()&&endedGame.State.GameEnded&&endedGame.State.RecordSettled
        &&endedGame.State.HomeRecord=="5-0"&&endedGame.State.AwayRecord=="",
        "End Game stops the game and records the result even when automatic updates are off");
    Check(!endedGame.EndGame()&&endedGame.State.HomeRecord=="5-0",
        "an already ended game is not counted twice");
    Check(endedGame.Undo()&&!endedGame.State.GameEnded&&!endedGame.State.RecordSettled
        &&endedGame.State.HomeRecord=="4-0",
        "End Game and its record update can be undone");
    var restartGame=new GameEngine();restartGame.NewGame("football");
    restartGame.SetTeamIdentity(true,"Restart Home","RST",null,"#FF112233","#FF445566",mascot:"Eagles",record:"2-0",ranking:"#7",divisionRecord:"1-0");
    restartGame.State.HomeRosterId="roster-1";
    restartGame.Score(true,7);restartGame.Score(false,3);
    restartGame.State.HomeTimeouts=1;restartGame.State.Period=3;restartGame.State.ClockSeconds=87;
    restartGame.EndGame();
    var settledRecord=restartGame.State.HomeRecord;
    Check(restartGame.RestartGame()&&!restartGame.State.GameEnded&&!restartGame.State.RecordSettled
        &&settledRecord=="3-0"
        &&restartGame.State.HomeScore==0&&restartGame.State.AwayScore==0
        &&restartGame.State.Period==1&&restartGame.State.ClockSeconds==restartGame.State.PeriodSeconds
        &&restartGame.State.HomeTimeouts==restartGame.State.HomeTimeoutLimit,
        "Restart Game clears the final, score and clocks");
    Check(restartGame.State.HomeTeam=="Restart Home"&&restartGame.State.HomeAbbreviation=="RST"
        &&restartGame.State.HomePrimaryColor=="#FF112233"&&restartGame.State.HomeMascot=="Eagles"
        &&restartGame.State.HomeRanking=="#7"&&restartGame.State.HomeDivisionRecord=="1-0"
        &&restartGame.State.HomeRecord==settledRecord&&restartGame.State.HomeRosterId=="roster-1",
        "Restart Game keeps the team properties and the settled record");
    Check(!restartGame.RestartGame(),"a running game is not restarted by the End Game button");
    var quickSave=Path.Combine(Path.GetTempPath(),$"quick-buttons-{Guid.NewGuid():N}.spg");
    engine.NewGame("basketball");
    engine.State.QuickLayouts=[new SavedQuickLayout{Label="Flag Layout",DisplayId="main",LayoutId="flag"}];
    engine.State.QuickMedia=[new SavedQuickMedia{Label="Replay",Path="C:\\clips\\replay.mp4"}];
    engine.State.QuickButtonsSaved=true;engine.Save(quickSave);
    var quickReload=new GameEngine();quickReload.Load(quickSave);
    Check(quickReload.State.QuickButtonsSaved&&quickReload.State.QuickLayouts.Single().Label=="Flag Layout"
        &&quickReload.State.QuickMedia.Single().Path=="C:\\clips\\replay.mp4",
        "quick layout and media buttons travel inside the game save");
    File.Delete(quickSave);

    // ---------- node rules and hand-over chroma keying ----------
    var automationPath=Path.Combine(Path.GetTempPath(),$"automation-{Guid.NewGuid():N}.json");
    var chromaPath=Path.Combine(Path.GetTempPath(),$"transition-chroma-{Guid.NewGuid():N}.json");
    try
    {
        var automationCatalog=new ScorebugCatalog(automationPath);
        var automationDisplay=automationCatalog.ActiveDisplay;
        var target=automationCatalog.Layout.Elements.First(element=>element.Name=="Home team");
        var rule=new AutomationRule{Name="Play clock warning"};
        rule.Conditions.Add(new AutomationCondition{Field=AutomationField.PlayClock,Operator=AutomationOperator.Less,Value=5});
        rule.Actions.Add(new AutomationAction{Kind=AutomationActionKind.SetTextColor,LayoutId=automationDisplay.Layouts[0].Id,ElementId=target.Id,Value="#FFFF3B30",ColorSource=ScorebugColorSource.HomePrimary});
        automationCatalog.UpdateAutomations(automationDisplay.Id,[rule]);
        var automationReloaded=new ScorebugCatalog(automationPath);
        var savedRule=automationReloaded.ActiveDisplay.Automations.Single();
        Check(savedRule.Name=="Play clock warning"&&savedRule.Conditions.Single().Field==AutomationField.PlayClock
            &&savedRule.Actions.Single().Kind==AutomationActionKind.SetTextColor&&savedRule.Actions.Single().Value=="#FFFF3B30"
            &&savedRule.Actions.Single().ColorSource==ScorebugColorSource.HomePrimary,
            "conditionals, conditions, actions and team-palette colours persist with their display");
        var paletteState=new GameState{HomePrimaryColor="#FF123456",AwaySecondaryColor="#FF654321"};
        Check(AutomationRules.ResolveColor(savedRule.Actions.Single(),paletteState)=="#FF123456",
            "a conditional colour can resolve against the home team's primary colour");
        Check(new AutomationCondition{Field=AutomationField.PossessionSide,Operator=AutomationOperator.Equal,Text="Away"}.Matches(new GameState{HomePossession=false}),
            "possession is available as a readable Home/Away condition");
        var ruleEngine=new GameEngine();ruleEngine.NewGame("football");ruleEngine.SetPlayClock(3);
        Check(savedRule.Matches(ruleEngine.State),"a play-clock condition matches while the clock is below its threshold");
        ruleEngine.SetPlayClock(8);
        Check(!savedRule.Matches(ruleEngine.State),"a play-clock condition stops matching above its threshold");

        // Rule bundles carry more than one rule, and the expanded graph can see flags, timeouts,
        // cards and team figures without making one element per value.
        var secondRule=new AutomationRule{Name="Flag and yards",BypassOutputUpdate=true};
        secondRule.Conditions.Add(new AutomationCondition{Field=AutomationField.PenaltyActive,Operator=AutomationOperator.IsTrue});
        secondRule.Conditions.Add(new AutomationCondition{Field=AutomationField.PenaltyType,Operator=AutomationOperator.Equal,Text="Holding"});
        secondRule.Conditions.Add(new AutomationCondition{Field=AutomationField.HomeYards,Operator=AutomationOperator.Greater,Value=300});
        secondRule.Conditions.Add(new AutomationCondition{Field=AutomationField.HomeTimeouts,Operator=AutomationOperator.Equal,Value=1});
        var ruleBundle=Path.Combine(Path.GetTempPath(),$"rules-{Guid.NewGuid():N}.spr");
        try
        {
            RuleFiles.Save(ruleBundle,new AutomationRule?[]{rule,secondRule});
            var importedRules=RuleFiles.Load(ruleBundle);
            Check(importedRules?.Count==2&&importedRules[1].Conditions.Count==4
                &&importedRules[1].Conditions[1].Field==AutomationField.PenaltyType
                &&importedRules[1].BypassOutputUpdate,
                "a ScorePlus Rule .spr bundle exports and imports multiple rules at once, including the live-bypass choice");
            var flagState=new GameState{PenaltyActive=true,PenaltyType="Holding",HomeYards=347,HomeTimeouts=1,
                HomeYellowCards=2,AwayRedCards=1,BallOnHomeSide=false,HomeTimeoutLimit=3,TimeoutClockSeconds=42};
            Check(secondRule.Matches(flagState),"flag, flag type, yards and timeouts all evaluate from the game state");
            flagState.PenaltyType="Offside";
            Check(!secondRule.Matches(flagState),"the flag-type condition rejects another penalty");
            Check(new AutomationCondition{Field=AutomationField.HomeYellowCards,Operator=AutomationOperator.GreaterOrEqual,Value=2}.Matches(flagState),
                "a card condition reads the home team's yellow cards");
            Check(new AutomationCondition{Field=AutomationField.HomeTimeoutLimit,Operator=AutomationOperator.Equal,Value=3}.Matches(flagState),
                "the timeout limit is available as a rule condition");
            var numericLabels=AutomationRules.OperatorsFor(AutomationField.PlayClock).Select(option=>option.Label).ToArray();
            Check(new[]{"greater than","less than","less than or equal to","greater than or equal to","equal to"}
                    .All(label=>numericLabels.Contains(label)),
                "numeric conditions offer the requested comparisons");
            var waitRule=new AutomationRule{Name="Flag layout and back"};
            waitRule.Conditions.Add(new AutomationCondition{Field=AutomationField.PenaltyActive,Operator=AutomationOperator.IsTrue});
            waitRule.Conditions.Add(new AutomationCondition{Field=AutomationField.PenaltyActive,Operator=AutomationOperator.IsFalse,WaitUntil=true});
            waitRule.Actions.Add(new AutomationAction{Kind=AutomationActionKind.SwitchLayout,LayoutId=automationDisplay.Layouts[0].Id,Phase=AutomationActionPhase.OnMatch});
            waitRule.Actions.Add(new AutomationAction{Kind=AutomationActionKind.SwitchLayout,LayoutId=automationDisplay.Layouts[0].Id,Phase=AutomationActionPhase.AfterWait});
            waitRule.Actions.Add(new AutomationAction{Kind=AutomationActionKind.SetText,ElementId=target.Id,Value="NO FLAG",Phase=AutomationActionPhase.Else});
            RuleFiles.Save(ruleBundle,new AutomationRule?[]{waitRule});
            var waitImport=RuleFiles.Load(ruleBundle)?.SingleOrDefault();
            Check(waitImport is not null&&waitImport.Conditions.Count==2&&waitImport.Conditions[1].WaitUntil
                &&waitImport.Actions[1].Phase==AutomationActionPhase.AfterWait
                &&waitImport.Actions[2].Phase==AutomationActionPhase.Else,
                "wait-until conditions and action phases travel in a ScorePlus Rule bundle");
            var waitState=new GameState{PenaltyActive=true};
            Check(waitImport!.Matches(waitState)&&!waitImport.MatchesWait(waitState),
                "a flag-down trigger matches while its wait-until condition does not");
            waitState.PenaltyActive=false;
            Check(!waitImport.Matches(waitState)&&waitImport.MatchesWait(waitState),
                "the wait-until condition becomes true when the flag goes away");
        }
        finally { if(File.Exists(ruleBundle)) File.Delete(ruleBundle); }

        var chromaCatalog=new ScorebugCatalog(chromaPath);
        chromaCatalog.Update(layout=>{layout.TransitionVideoPath=@"C:\\clips\\transition.mp4";layout.TransitionVideoChromaKeyColor="#FF00B140";layout.TransitionVideoChromaKeyTolerance=0.23;layout.TransitionVideoCutSeconds=2.4;});
        var chromaReloaded=new ScorebugCatalog(chromaPath);
        Check(chromaReloaded.Layout.TransitionVideoChromaKeyColor=="#FF00B140"&&Math.Abs(chromaReloaded.Layout.TransitionVideoChromaKeyTolerance-0.23)<0.001,
            "a layout hand-over video keeps its chroma key colour and tolerance across a save");
        Check(Math.Abs(chromaReloaded.Layout.TransitionVideoCutSeconds-2.4)<0.001,
            "a layout hand-over video keeps the beat when the incoming layout cuts in");
        chromaReloaded.Update(layout=>layout.TransitionVideoCutSeconds=90);
        Check(new ScorebugCatalog(chromaPath).Layout.TransitionVideoCutSeconds<=30,
            "a hand-over cut beat is clamped to a safe range");
    }
    finally
    {
        if(File.Exists(automationPath))File.Delete(automationPath);
        if(File.Exists(chromaPath))File.Delete(chromaPath);
    }

    // Made icons can carry a different design for each game value, with its own colour, position,
    // size, rotation and opacity. These are the values the studio and exported HTML both read.
    var iconStatePath=Path.Combine(Path.GetTempPath(),$"icons-{Guid.NewGuid():N}.json");
    try
    {
        CustomIconStore.UsePath(iconStatePath);
        var made=new CustomIcon{Name="Possession mark",Glyph="●",Color="#FFFFFFFF",Plate="#00000000"};
        made.States.Add(new CustomIconState
        {
            Condition=new AutomationCondition{Field=AutomationField.PossessionSide,Operator=AutomationOperator.Equal,Text="Away"},
            Glyph="A",Color="#FF112233",Plate="#FF445566",Round=false,OffsetX=8,OffsetY=-4,Scale=1.4,Opacity=0.6,Rotation=20
        });
        made.States.Add(new CustomIconState
        {
            Condition=new AutomationCondition{Field=AutomationField.PossessionSide,Operator=AutomationOperator.Equal,Text="Home"},
            Glyph="H",Color="#FF778899",OffsetX=-6,Scale=0.8
        });
        var stored=CustomIconStore.Save(made.Copy());
        var away=stored.Resolve(new GameState{HomePossession=false});
        var home=stored.Resolve(new GameState{HomePossession=true});
        Check(stored.States.Count==2&&away.Glyph=="A"&&home.Glyph=="H"
            &&away.Color=="#FF112233"&&away.OffsetX==8&&Math.Abs(away.Scale-1.4)<0.001&&Math.Abs(away.Opacity-0.6)<0.001,
            "a made icon chooses the first matching data state, including its colour, location, scale and opacity");
        Check(home.Color=="#FF778899"&&home.Round,"each icon state keeps its own plate and roundness");
        var fallback=new CustomIcon{Glyph="F",Color="#FFFFFFFF"};
        fallback.States.Add(new CustomIconState{Condition=new AutomationCondition{Field=AutomationField.PossessionSide,Operator=AutomationOperator.Equal,Text="Neither"},Glyph="X"});
        Check(fallback.Resolve(new GameState{HomePossession=false}).Glyph=="F","the fallback icon design draws when no data state matches");
        var copied=stored.Copy();
        Check(!ReferenceEquals(copied.States[0],stored.States[0])&&copied.States[0].Glyph=="A","copying an icon deep-copies its data states");
        var reloadedIcon=CustomIconStore.Find(stored.Id);
        Check(reloadedIcon is not null&&reloadedIcon.States.Count==2&&reloadedIcon.Resolve(new GameState{HomePossession=true}).Glyph=="H",
            "icon data states survive a save and reload");
        // A ScorePlus Icon file carries one icon and its data states between machines, and imports
        // as a new library entry rather than reusing the file's ids.
        var iconFile=Path.Combine(Path.GetTempPath(),$"icon-{Guid.NewGuid():N}.spi");
        try
        {
            var shared=new CustomIcon{Name="Shared icon",Glyph="◆",Color="#FF102030",Plate="#FF405060",Round=false,OffsetX=3,Scale=1.2,Opacity=0.8,Rotation=15};
            shared.States.Add(new CustomIconState{Condition=new AutomationCondition{Field=AutomationField.PossessionSide,Operator=AutomationOperator.Equal,Text="Home"},Glyph="H",Color="#FF00FF00",OffsetY=-2});
            IconFiles.Save(iconFile,shared);
            var imported=IconFiles.Load(iconFile);
            Check(imported is not null&&imported.Name=="Shared icon"&&imported.Glyph=="◆"
                &&imported.States.Count==1&&imported.States[0].Glyph=="H"&&imported.States[0].Condition.Text=="Home"
                &&imported.Id!=shared.Id&&imported.States[0].Id!=shared.States[0].Id,
                "a .spi file carries an icon and its data states and imports as an independent copy");
            File.WriteAllText(iconFile,"not a ScorePlus icon");
            Check(IconFiles.Load(iconFile) is null,"a damaged .spi file is refused");
        }
        finally { if(File.Exists(iconFile))File.Delete(iconFile); }
    }
    finally
    {
        CustomIconStore.UsePath(null);
        if(File.Exists(iconStatePath))File.Delete(iconStatePath);
    }
}
finally
{
    if(File.Exists(exportLayoutPath))File.Delete(exportLayoutPath);
    if(File.Exists(displayPath))File.Delete(displayPath);
}
