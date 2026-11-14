# Port ScorePlus (Windows WPF) to native Linux

## Context

ScorePlus is a live-production scoreboard console: one operator drives a game (football/baseball/basketball/hockey), a broadcast graphics "scorebug", a media dock of video/audio clips, live camera feeds with a 2-minute replay buffer, stadium audio routing, and a phone-sized remote console served over the LAN. Today it ships Windows-only: `src/Scoreboard.Desktop` is a `net10.0-windows`/`UseWPF` project that publishes a single-file win-x64 exe, and `src/ScorePlus.Setup` is a WPF installer that embeds the app and writes Start Menu shortcuts and registry entries. The Linux build lives in this repo (`ScorePlus-Linux`) alongside the Windows sources. Port the app to native Linux — Avalonia UI, ffmpeg-based media, X11 and Wayland, installs per-user without admin — and keep every behavior a broadcast operator depends on.

## Confirmed repo facts

- `Scoreboard.Core` (`GameEngine`, profiles, media library, reports, autosave, `AppPaths`) is plain `net10.0` — no Windows APIs. It compiles and runs on Linux unchanged.
- `Scoreboard.Desktop` is the WPF surface: `MainWindow`, output windows, scorebug rendering, media dock, camera feeds, audio routing, launcher, keybinds, remote console, theme/updates.
- Windows dependencies: WPF (`net10.0-windows`/`UseWPF`), `NAudio` (audio routing), `OpenCvSharp4` (+ `runtime.win`, camera capture and replay).
- Windows-only code sites, exactly: `AppPaths.cs` (`LocalApplicationData` — resolves to `~/.config` on Linux, needs no change); `Theme.cs` (HKCU light-theme probe); `Updates.cs` (HKCU install marker); `Installer.cs` (registry uninstall entry + `.lnk` shortcuts); `Microsoft.Win32` dialogs in `MainWindow`, `MediaDock`, `LauncherWindow`, `RosterPanel`, `TeamIdentityWindow`, `ScorebugEditorWindow`; Win32 `MonitorFromWindow`/`GetMonitorInfo` in `ScreenFit.cs:35-39` and `OutputWindow.xaml.cs:164-168`; `AudioRouter.cs` (NAudio WASAPI, device enumeration via registry + `MMDevice` COM); `CameraFeed.cs` (OpenCv `VideoCapture` with DSHOW→MediaFoundation fallback, DirectShow COM device enumeration, MJPG-AVI recording).
- `Scoreboard.Desktop.csproj` pins `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`, `PublishSingleFile`, `EnableCompressionInSingleFile`; `scorebug-logo.png` is already an `avaloniaResource`-compatible embedded asset; `App.xaml` brushes are referenced through `DynamicResource` and repainted per-window by `ThemeManager.Apply` (which attaches `SkipTheme` to controls whose color is layout-driven).
- Animations: every animated element uses WPF `BeginAnimation` — `DoubleAnimation` with `CubicEase EaseOut`, `FillBehavior.HoldEnd`, and completion callbacks that drive state transitions (fade-out ends the "on" phase, wheel-out completion commits the new score). `ScorebugTextBlock.Animate` (`ScorebugRender.cs:418-493`) animates opacity, a shared `TranslateTransform`, and a shared `ScaleTransform`; `Pop` uses `DoubleAnimationUsingKeyFrames` (start → 1.06× at 62% → settle at 1). `ScorebugCanvasView` adds sine-eased pulse/drift animations (`SineEase EaseInOut`, `AutoReverse`, `RepeatBehavior.Forever`) and re-arms them on rebuild; `ScorebugEditorWindow` has its own pulse/drift re-arming (`:344-364`, `:1098`).
- `MediaSession` (`Media.cs`) already tracks its own transport clock with `DispatcherTimer` ticks (coalesced seeks, seek-version counter, `MediaEnded` playlist advance, `ReplaySegment`); `VideoSurface` maps cue state onto the WPF `MediaElement` (pending-seek-on-open, 240 ms open freeze, resume deferral, router sync) and on the live-camera `Image` — none of the transport logic is Windows-specific, only the element and the router are.
- `RemoteControlServer` is a plain `TcpListener` on `IPAddress.Any` (deliberately, so no admin URL reservation is needed); it marshals actions onto the UI thread via the WPF `Dispatcher`. Porting it is a `Dispatcher` type swap only.
- `Keybinds.cs` maps a gesture string ("Ctrl+Z", "F5") to WPF `Key`/`ModifierKeys` and `MainWindow.OnPreviewKeyDown` resolves `Key.System`→`SystemKey` before matching.
- Fonts: `Fonts.cs` reads installed faces from the registry key `SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts`, applies font fallback by prepending a fallback family to the family name string, and prefers "Segoe UI"/"Segoe UI Variable" as the default UI family.
- Updates (`Updates.cs`): reads a JSON manifest (`version`, `url`, `sha256`, `notes`) from the operator's feed address, downloads to `%TEMP%\ScorePlusUpdate`, verifies SHA-256, runs the setup exe with `/SILENT /CLOSEAPPLICATIONS`, then exits. Installed-vs-portable status comes from HKCU `InstallDir`, with an `install.json` beside the exe as fallback (`AppPaths.IsInstalled`, `Updates.IsInstalled`).
- `scripts/build-release.sh` publishes win-x64 self-contained single-file to `dist/ScorePlus-win-x64-v<MAJOR>`, zip-embeds the app into the setup exe payload, copies `README.md` and `docs/updates.md`.
- `tests/Core.Tests` targets `net10.0` (portable as-is). `tests/UiSmoke` targets `net10.0-windows`: a headless harness (3071 lines) that builds `App`, drives real windows, raises keys through `PreviewKeyDownEvent`, and pumps the dispatcher with `Dispatcher.Run(); DispatcherFrame.Continue = false` (`Program.cs:287-293`) — assertions cover score transitions, clock, penalties, scorebug rebuild, media dock, dialogs, keyboard routing.

## Approach

Ordered phases; each ends compiling and test-green before the next starts. The Linux app reuses every Core file and keeps every Desktop file's name and structure — only the rendering, media, and OS integration layers change.

### 1. Retarget the projects

- `Scoreboard.Desktop.csproj`: `<TargetFramework>net10.0</TargetFramework>`, drop `UseWPF`, drop the pinned `RuntimeIdentifier` (RID chosen at publish time: `linux-x64`), keep `PublishSingleFile`/`EnableCompressionInSingleFile`/`DebugType=embedded`. Remove `NAudio` and `OpenCvSharp4` packages; add `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent` (11.3.x stable) and `Avalonia.Fonts.Inter` is NOT needed (fallback naming is handled in step 5).
- `Scoreboard.Core`: unchanged; `AppPaths` root is `Environment.GetFolderPath(LocalApplicationData)`, which on Linux is `$XDG_CONFIG_HOME` → `~/.config/ScorePlus`. No code change.
- `ScorePlus.Setup` project is deleted from the Linux build; packaging moves to `scripts/build-linux.sh` (step 7). `scripts/build-release.sh` stays untouched (Windows builds keep shipping from the sibling checkout).
- `App.xaml` → `App.axaml` with `xmlns="https://github.com/avaloniaui"`; `App.xaml.cs` → Avalonia `OnFrameworkInitializationCompleted`: `DesktopStyleApplicationLifetime` with `MainWindow = new MainWindow(); MainWindow.Show()`, `ShutdownMode = OnExplicitShutdown` (the launcher keeps the app alive while the main window waits), then the existing `StartFromLauncher`/autosave-restore flow (`MainWindow.xaml.cs:143-148`, `158-161`) runs unchanged.

### 2. XAML and control conversion rules (all nine windows + dock/editor panels)

Applied mechanically across `App.xaml`, `MainWindow`, `LauncherWindow`, `OutputWindow`, `GameControlWindow`, `MediaDock`, `ScorebugEditorWindow`, `TeamIdentityWindow`, `RosterPanel`, `EventLogWindow`, `UnsavedChangesWindow`:

- `xmlns` namespaces: `using:Scoreboard.Desktop`; `x:Name` + `FindName` work as-is.
- Keyed styles (`<Style x:Key="DockAction">`, `BasedOn`) become selector styles: `Selector="Button.dock-action"` etc. Every `Style="{StaticResource DockAction}"` site gains `Classes="dock-action"`; code that sets styles by key (ThemeManager repaints, editor palette) sets classes instead. `BasedOn` chains collapse into the selector list — styles stay grouped under the same names as classes, so `ThemeManager.SkipTheme` logic (attached property, ported unchanged) keeps working.
- Template rewrites in `App.xaml` keep behavior, swap parts: `Button` → Avalonia template (border + content presenter); `ComboBox` custom template must include `PART_EditableTextBox` (Avalonia honors `IsEditable` with that part name — search-as-you-type in sport/profile pickers preserved); `ScrollBar` template uses Avalonia's `Track` with `PART_Track`, thumb drag = `ScrollBar` thumb drag, arrows = `ScrollBarInc/Dec` repeat buttons; `ScrollViewer` vertical-hub sizing (`DockHubWidth=10`, thumb min height) reproduces via the template's thumb min-size; `TextBlock` style setters map to Avalonia properties 1:1.
- `DockPanel`, `Grid`, `WrapPanel`, `GridSplitter`, `Viewbox`, `Canvas` (`Canvas.Left/Top`, `Panel.ZIndex`), `Border`, `Image`, `TextBox`, `ListBox`, `ToggleButton`, `CheckBox` all exist in Avalonia with identical semantics; `CornerRadius`, `Stretch`, `TextAlignment`, `Opacity`, `RenderTransform` translate directly.
- Shapes: `System.Windows.Shapes.*` → `Avalonia.Controls.Shapes.*`; `StrokeStartLineCap`/`StrokeEndLineCap` (ArcShape round caps in scorebug templates) have no Shape equivalent — set `LineJoin="Round"` on the arc path and accept identical-looking caps; `Ellipse`/`Polygon`/`Rectangle` map unchanged.
- `BrushConverter`/`Brush.Freeze()` → `Avalonia.Media.Brush.Parse(string)` (immutable; drop `Freeze()` calls); `LinearGradientBrush(startPoint, endPoint)` constructor exists in Avalonia — keep `ScorebugRender.BackgroundBrush` gradient-angle math exactly.
- Window chrome: `SystemDecorations="None"` + `CanResize=false` for borderless output windows; `Topmost`, `WindowStartupLocation`, `Width/Height` map unchanged.

### 3. Media engine: ffmpeg/pulse replaces NAudio + OpenCV

`ffmpeg`, `ffprobe`, `paplay`, `pactl` are external binaries probed once at startup by a new `MediaProbe` (`AppPaths` cache like the camera cache): env `SCOREPLUS_FFMPEG` override → bundled `resources/ffmpeg/ffmpeg` beside `AppContext.BaseDirectory` (AppImage ships them) → PATH. Missing `ffmpeg` = app still runs; every video/camera/replay path shows an explicit status ("VIDEO NEEDS ffmpeg") instead of failing silently. Missing `paplay` = routed audio falls back to `ffplay -nodisplay` on the default sink (device selection and routing disabled, one status line).

- **New `LinuxVideoPlayer`** replaces the `MediaElement` inside `VideoSurface` (`VideoSurface.cs` keeps its cue logic, pending-seek-on-open, 240 ms freeze, resume deferral, `Fail()` blanking, `Dispose()`):
  - Duration/fps from `ffprobe -show_format -show_streams`; open = spawn `ffmpeg -nostdin -ss <start> -re -i <file> -vf format=bgra -f rawvideo -` and read BGRA frames from the pipe as they arrive (`-re` paces them at native rate so the transport clock and audio stay aligned); frames blitted into a persistent `WriteableBitmap` shown by an `Image` (`Stretch` Uniform / UniformToFill from `PreserveVideoAspect` — same as today).
  - Position = `start + decodedFrames/fps`; EOF + `entry.Loop` → respawn from 0 (same as `Loop()`); EOF without loop → hold last frame and raise `MediaEnded` so `MediaSession` advances the playlist.
  - Pause = kill ffmpeg, keep the last frame (identical "paused cue holds its frame" semantics); Resume = respawn at current position. Seek while playing = respawn at `-ss` target, coalesced by the existing 80 ms throttle. Scrubbing (paused) = single-frame extract `ffmpeg -ss <t> -i <file> -frames:v 1 -f rawvideo -` applied to the bitmap — matches current `ScubbingEnabled` behavior without keeping a decoder alive.
- **Rewritten `AudioRouter`** keeps its public members (`Play(path, device, volume, start)`, `Pause`, `Resume`, `Seek`, `SetVolume`, `Position`, `AudioEnded`, `RefreshDevices`) so `VideoSurface.SyncRouting` and clip/audio cues are untouched internally. Implementation: spawn `ffmpeg -nostdin -ss <pos> -af volume=<v> -f s16le -ar 48000 -ac 2 -i <file> | paplay -p <sink>`; position tracked by a 200 ms timer like the video clock; Pause kills the pipeline and remembers position; Resume/Seek/SetVolume respawn with new `-ss`/`-af volume`. Seek coalescing mirrors `MediaSession.SeekThrottle`. Device enumeration: parse `pactl list sinks` (`name:` + `Description:`) into `OutputDevice(Id=sink name, Name=description)`; default device = the sink with `active: yes`; registry enumeration gone. `DefaultDeviceName()` unchanged.
- **Rewritten `CameraFeed`**: capture via `ffmpeg -f v4l2 -framerate 15 -i /dev/videoN -vf format=bgra -f rawvideo -`; read loop paced at 15 fps with the existing `_framePending` drop rule; publish frames as `WriteableBitmap` blits (`BitmapSource.Create` Bgr24 → `WriteableBitmap` Blit of BGR24 rows, `stride = frame.Step()` equivalent from raw pipe). Recording segments: respawn ffmpeg per segment (`-f v4l2 ... -c:v mjpeg -q:v 2 <folder>/camera-<ts>.avi`, 60 s segments, same `AppPaths.Combine("CameraClips")` folder, same `PruneFolder` newest-6 rule, same `CameraSegment` shape, `Snapshot()` unchanged). Replay export: `ffmpeg -f concat -safe 0 -i <list> -ss <from> -to <to> out.avi` over the segment files (segments are a contiguous timeline, so global input-level seek slices correctly) — replaces `CameraFeed.Export`'s re-encode loop; error strings kept verbatim. Device enumeration: `v4l2-ctl --list-devices` when v4l-utils is present (names like "ICSprIng Camera" preserved); fallback = probe `/dev/video0..5` existence by test-open (`AvailableCameras()` cache logic unchanged), naming them `Camera N`.
- **Thumbnails** (`MediaLibrary.QueueThumbnail`): `ffprobe` for duration, then `ffmpeg -ss <fraction> -i <file> -frames:v 1 -f png_pipe`; flat-color rejection uses `WriteableBitmap.CopyPixels`; audio clips still skip (`IsVideoContainer` unchanged).
- OpenCV and NAudio references deleted from every file (`CameraFeed.cs`, `AudioRouter.cs`, `UiSmoke`'s `WritePng` → `WriteableBitmap.Save`); `MediaTypes`/`MediaLibrary`/`MediaSession`/`ReplayDock` logic unchanged except the export calls above.

### 4. Animation engine replaces `BeginAnimation`

Avalonia has no WPF animation API. New `ScorebugAnimator` (in `ScorebugRender.cs`) driven by `DispatcherTimer` at 16 ms ticks reproduces the exact semantics the UI depends on:

- `Run(target, apply(Action<double>), from, to, duration, CubicEase.EaseOut)` — normalized progress `p`, eased value `1-(1-p)^3` (identical formula to WPF `CubicEase EaseOut`), completion callback fired once on the final tick with the value held at `to` (`HoldEnd`), and re-running the same target cancels the previous run (same as `BeginAnimation` replacing a running animation).
- Keyframe pop (`ScorebugRender.cs:472-481`): piecewise linear between stops (0→start, 0.62→`end*1.06` eased-out, 1→`end`), run on both scale axes — same overshoot.
- Pulse/drift: `RunForever(from, to, duration, SineEase.EaseInOut)` — value oscillates (`sin` ease: `0.5-0.5*cos(p*π)`) with automatic reversal, never completes; re-armed by `RebuildFromLayout` (`ScorebugCanvasView.cs:414-429`) and the editor's `Rebuild`/`RebuildEditor` (`ScorebugEditorWindow.xaml.cs:344-364, 1098`) with the same `hasPulse`/`pulse`/`drift` flag logic. Transform targets stay the same shared `TranslateTransform`/`ScaleTransform` objects (Avalonia has both) — mutators just set `X`/`Y`/`Scale`/`ScaleY` per tick.
- `ScorebugTextBlock.Animate` keeps its exact signature and every transition mapping (`Fade`, `FadeScale`, slides, wheels, `Flip` scaleY, `Zoom`, `Pop`, `Drop`); `PlayIn`/wheel commit callbacks stay the completion hooks they are today, so UiSmoke's transition assertions carry over verbatim.
- Audio fade-out (`AudioRouter.cs:306-312`) becomes an `Animator` ramp feeding `ffmpeg -af volume=` — a 250 ms ramp re-spawns once at zero, so the clip simply ends; `AudioEnded` fires at ramp completion like today.

### 5. Fonts and theme detection

- `Fonts.cs`: enumerate families with `fc-list -f "%{family}\n"` (dedupe, cache); if `fc-list` is absent fall back to imported folders only + the built-in fallback list. Default UI family becomes `"Inter, Segoe UI, DejaVu Sans"` as an Avalonia comma fallback list (`FontFamily` supports comma fallbacks natively, so `FontStack`'s prepend logic simplifies to one family string); `ImportedFonts` (ScorebugAssets/Fonts folder) and `Resolve` unchanged; the "Segoe UI Variable" preference retires (Linux has no equivalent).
- `Theme.cs:153-157`: light-theme probe reads `[Settings] gtk-theme=` from `~/.config/gtk-3.0/settings.ini`; theme name containing "dark" → Light=False else Dark default. No registry, no crash when the file is missing.

### 6. OS integration: dialogs, monitors, keyboard, drag-drop, remote console

- **Dialogs** (five sites + MediaDock multi-select import): Avalonia `StorageProvider` via `TopLevel.StorageProvider` — `PickSaveFileAsync(FilePickerOptions{SuggestedFileName, FileTypeFilters})` for report `.html` / game `.spg` / replay `.avi`; `PickFilesAsync(OpenPickerOptions{AllowMultiple})` for media import; `.html` open = `Process.Start` with `UseShellExecute=true` (works on Linux — launches the xdg handler). Replay save default filename unchanged; `InitialDirectory` → default folder `~/Videos` when it exists. If the XDG portal is unavailable (`StorageProvider` null or returns files with no path), every dialog site falls back to a new `PathPromptWindow` (themed text field + Browse-free manual path entry, same OK/Cancel buttons) — ffmpeg needs real filesystem paths, so the fallback is required on old X11 setups without a portal.
- **Monitors**: new `LinuxMonitors` replaces `ScreenFit.cs` Win32 — parse `xrandr` output for connected outputs (`"<name> connected <W>x<H>+<x>+<y>"`), primary = `primary` marker; divide physical pixels by `TopLevel.GetTopLevel(window).RenderScaling` for DIP placement (same math as `FillMonitor`'s `GetDpi` division). When `DISPLAY` is unset (Wayland) or xrandr fails, use Avalonia's `Screens` API, then primary-screen bounds. `OutputWindow.FillMonitor` and `MainWindow` placement (`ScreenFit.Attach/Track`, `MainWindow.xaml.cs:67`) call the new class with identical signatures; `MonitorLayout` JSON in remote control unchanged.
- **Fullscreen**: `OutputWindow.xaml.cs` F11 keep `ToggleFullscreen`'s geometry save/restore; Avalonia `SystemDecorations=None` + `Position/Size` replace `WindowStyle=None`. Same F11 keybind through `Keybinds`.
- **Keyboard**: `MainWindow.OnPreviewKeyDown` becomes an `AddHandler(InputElement.PreviewKeyDownEvent, ...)` subscription; `e.Key == Key.System ? e.SystemKey : e.Key` collapses to `e.Key` (Avalonia reports the real key with `KeyModifiers.Alt`). `KeybindStore.TryParse`/`KeyName`/`Match` switch tables rewritten to Avalonia `Key`/`KeyModifiers` (gesture strings on disk unchanged — "Ctrl+Z", "F5" parse the same); `ControlPanelView.TryHandleKey` signature swap only.
- **Drag-drop**: clip drags (`DockClipDragStart`) use `DragDrop.DoDragDrop` with a custom in-process format string `ScorePlus.MediaPath` (Avalonia `DataObject` accepts custom formats; cross-app drag is not a feature today); scorebug drop on main window accepts `DataFormats.Files` storage items → `ImportDropTargets` path handling unchanged.
- **Remote console**: `RemoteControlServer` constructor takes Avalonia `Dispatcher`; all marshals become `Dispatcher.UIThread.Post`; `TcpListener` untouched (no firewall/admin needs, same as the Windows design comment says).
- **Install marker + updates**: registry gone; `AppPaths.IsInstalled`/`Updates.IsInstalled` read only `install.json` beside the exe (`Updates.cs:11-15` fallback becomes the sole path); `InstallDir` = `AppContext.BaseDirectory`; manifest schema unchanged (`version`, `url`, `sha256`, `notes`); download dir `Path.Combine(Path.GetTempPath(), "ScorePlusUpdate")`; sha256 check unchanged. `Run(path)` changes to: unpack the downloaded tar.gz to a temp dir and spawn `install.sh --silent` (kills the running app by PID file, replaces files, writes `install.json`, refreshes the `.desktop` entry), then close the app — same handoff sequence as `/SILENT /CLOSEAPPLICATIONS`.

### 7. Packaging: four formats, one build script

`scripts/build-linux.sh [version]` mirrors `build-release.sh`'s shape (major-version folder `dist/scoreplus-linux-x64-v<MAJOR>`, `-p:Version` stamping, README/updates copy):

- Publish `dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true` → single `scoreplus` binary, plus `scorebug-logo.png` embedded as before.
- **tar.gz + install.sh** (canonical, per-user, no admin): `dist/.../scoreplus_<ver>_linux-x64.tar.gz` containing the binary, `install.sh`, `uninstall.sh`, `scoreplus.desktop`, icon png. `install.sh` installs to `$XDG_DATA_HOME/scoreplus` (default `~/.local/share/scoreplus`), symlinks the binary into `$XDG_BIN_HOME` (`~/.local/bin`), copies the `.desktop` entry to `$XDG_DATA_HOME/applications`, writes `install.json` beside the binary (version + install dir — the app's update marker), and records the PID file on app start. No root, no system dirs.
- **`.deb`**: `scoreplus_<ver>_amd64.deb` via `dpkg-deb` (build host needs dpkg; absent → script prints the skip and still produces tar.gz): binary at `/usr/bin/scoreplus`, `.desktop` + icon at `/usr/share/`, `Depends: ffmpeg`, `Recommends: pulseaudio-utils, v4l-utils`; postinst writes nothing extra (the app finds `install.json` beside `/usr/bin/scoreplus` — shipped in the package).
- **AppImage**: AppDir with the binary + bundled `ffmpeg`/`ffprobe` copied into `AppDir/usr/bin/resources/ffmpeg/` (the `MediaProbe` bundled-first path), `AppRun`, icon, `scoreplus.desktop`; built with `appimagetool` when present, else skipped with a printed notice. Bundled ffmpeg means AppImage users need nothing installed.
- **Self-build**: `./scripts/build-linux.sh --local` publishes and drops the bare binary in `dist/` with no packaging — the developer path.
- Feed manifests: Linux builds publish `linux.json` (same schema, `url` = the tar.gz); the existing Windows manifest flow is untouched.

### 8. Tests migrate with the app

- `tests/Core.Tests` retargets to nothing (already `net10.0`) — runs unmodified.
- `tests/UiSmoke`: same csproj retarget (`net10.0`, add `Avalonia.Headless`), same program structure. Conversions: namespace table swap (`System.Windows.*` → `Avalonia.*`); `BitmapImage` → `new Bitmap(path)`; `Brush.Freeze()` gone; key raises become `RaiseEvent(new KeyEventArgs(App.GetRequiredService<KeyboardDevice>(), PresentationSource.FromVisual(window), 0, key) { RoutedEvent = InputElement.PreviewKeyDownEvent })`; the `DispatcherFrame` pump (`Program.cs:287-293`) becomes `Pump(TimeSpan)` = loop of `Dispatcher.UIThread.RunJobs()` + 10 ms sleep (headless renderer runs animation/layout ticks, so transition assertions keep passing); `WritePng` uses `RenderTargetBitmap` + `WriteableBitmap.Save`; dialog assertions that used `Microsoft.Win32` dialogs assert the new StorageProvider/`PathPromptWindow` paths instead (headless has no portal → the fallback prompt is what gets exercised, and it returns the typed path so save/load assertions still assert real file contents). All 3071 lines of game-state assertions stay verbatim.

## Verification

Run order, each gate before the next (no new suites beyond what exists):

1. `dotnet build` of Core + Desktop + UiSmoke on `net10.0`, `dotnet test tests/Core.Tests` (all game-engine invariants: score transitions, penalties, clock, reports, autosave, profiles) green on Linux.
2. `dotnet run --project tests/UiSmoke` exit 0 — headless harness drives real windows: launcher modes, sport switching, keyboard routing (space/clock, F5 publish, custom keybinds), scorebug rebuild + pulse re-arm, wheel/dissolve/flip transition commits, media dock cue/freeze/loop behavior against a fixture mp4 generated by ffmpeg at test time, remote-server start/stop, dialog fallback paths saving real `.spg` files.
3. Smoke on the real surface (the harness can't prove windowing): launch the app on X11 and on Wayland; verify main window placement, F11 fullscreen fill on a two-monitor `xrandr` layout, output window pushed to the secondary monitor, theme applied (GTK dark setting flips Light/Dark), fonts render with fallback family when Segoe is absent.
4. Media smoke with real binaries: open a fixture video on a cue → frame appears within the 240 ms freeze budget; scrub while paused updates the frame; loop rewinds; audio cue routed to a named Pulse sink audible (`paplay -p`), volume slider changes level within 250 ms of release, seek restarts at target ±200 ms; camera feed from a webcam (`/dev/video0`) shows live frames and `Snapshot()` returns segments; replay export writes a playable `.avi`.
5. Packaging smoke: `./scripts/build-linux.sh 1.0.0` produces tar.gz + deb + AppImage (or printed skips); tar.gz installs on a fresh user with no admin and runs; `.deb` installs and runs; AppImage runs on a distro with nothing installed (bundled ffmpeg used); the in-app update flow against a local manifest pointing at a tar.gz downloads, checks sha256, replaces the install, and reports the new version on relaunch.
6. Remote console smoke from a phone/browser against the running app on LAN: login with password, drive score/clock actions, monitor layout JSON matches `xrandr`.

## Assumptions & contingencies

- **Avalonia 11.3.x** as the UI stack (XAML, Skia backend) — user chose Avalonia; X11+Wayland both covered by the Skia/OpenGL backends. If a specific 11.x release lacks `Screens` bounds on Wayland, the xrandr→Screens→primary fallback chain in step 6 absorbs it.
- **ffmpeg/paplay/pactl/v4l2-ctl as runtime deps**, not embedded binaries except in the AppImage. Contingency already decided: absent binaries degrade to explicit status lines, app stays usable for scorekeeping (video/camera/replay disabled with reasons, audio to default sink). Alternative considered: bundling ffmpeg in every format — rejected for tar.gz/deb size; revisit only if field reports show missing-ffmpeg pain.
- **XDG portal for file dialogs**; fallback `PathPromptWindow` (typed path) ships regardless. Portal-less Wayland/X11 setups use the prompt; no silent failures.
- **Update payload = tar.gz** for in-app self-update even on deb installs (install.sh writes `install.json` where the deb binary lives); deb/AppImage remain distribution formats, not self-update channels.
- **Key gesture strings on disk unchanged**; legacy `keybinds.json` files keep working because `KeybindStore.TryParse` maps the same names onto Avalonia's enum. Keys that exist only in WPF's enum have no Linux equivalent and drop silently from bindings (none used by default bindings).

## Progress log (phase 1, in flight)

Toolchain: no system dotnet; official SDK tarball (dotnet-sdk-10.0.113, matches repo's `extra/dotnet-sdk 10.0.13.sdk113-1`) extracted to `~/.local/share/dotnet-sdk`, launcher at `~/.local/share/dotnet-sdk/dotnet` — `--version` 10.0.113, MSBuild 18.0.36. This MSBuild driver rejects `--reporter`/`--test-summary` on `dotnet test`; use default reporter and grep the log.

`Scoreboard.Desktop.csproj` (done): `net10.0`; dropped `UseWPF`, `EnableWindowsTargeting`, `RuntimeIdentifier` (RID at publish: `linux-x64`); kept `PublishSingleFile`/`EnableCompressionInSingleFile`/`DebugType=embedded`/`SelfContained`/`AllowedReferenceRelatedFileExtensions`/`ApplicationIcon app.ico` (ignored on non-Windows, harmless). Packages: `NAudio`, `OpenCvSharp4`, `OpenCvSharp4.runtime.win` removed; `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent` added **unpinned** (NuGet latest; if the API drifts from 11.3.x behavior below, pin `Version="11.3.*"` explicitly). `Microsoft.Web.WebView2` kept — runtime-dead on Linux but the HtmlSurface sites still import it; drops when HtmlSurface's browser-handler path lands (phase 6). A `<ProjectFileProvider Query="avalonia:ProjectFileProvider">` group now owns `Assets/logo.png` + all 12 window XAMLs by their **current `.xaml` names** — phase 2 renames each file to `.axaml` and updates these entries; `app.ico` stays a plain `<Resource>` outside the provider (not Avalonia-compiled).

Core (done): `dotnet build src/Scoreboard.Core/Scoreboard.Core.csproj` green on `net10.0`, 0 warnings. `tests/Scoreboard.Core.Tests` is a plain `OutputType=Exe` runner with no test-framework packages — `dotnet test` restores then runs **nothing** (exit 0, no output; false green). Run it as `dotnet run --project tests/Scoreboard.Core.Tests` instead; treat the plan's `dotnet test` gate wording as an Exec runner, not a framework suite.

Avalonia API facts verified against repo sources (`src/Avalonia.Controls/Window.cs`, `ComboBox.cs`, `Application.cs`, `ApplicationLifetimes/…`, `Avalonia.Templates`, docs):
- `App.axaml` keeps WPF's root shape: `<Application xmlns="https://github.com/avaloniaui" xmlns:x xmlns:local x:Class="Scoreboard.Desktop.App"><Application.Resources>`; StaticResource/DynamicResource both supported.
- `Window`: `Show()`, `Close()`, `Closing` = `event EventHandler<WindowClosingEventArgs>?`, routed `WindowClosedEvent` raised from `HandleClosed`, `Owner` property settable, `ShowDialog(owner)`→Task, `ShowDialog<T>(owner)` awaitable → `Optional<T>` (`await d.ShowDialog<bool>(owner)`). **No public `Closed` event** — WPF's `window.Closed += …` hook becomes a `Window.WindowClosedEvent.AddClassHandler(typeof(MainWindow), …)` or the lifetime's `ShutdownMode`.
- `ClassicDesktopStyleApplicationLifetime`: `MainWindow`, `ExitCode`, `Shutdown(int)/TryShutdown`, `ShutdownMode` incl. `OnExplicitShutdown` (enum members from docs; compiler is the final word).
- `ComboBox` keeps `PART_Popup` (required), `PART_EditableTextBox`, `IsEditable`, `SelectionBoxItem` — the WPF template in `App.xaml` is portable part-for-part.
- `Application.cs` exposes **no** dispatcher error hook (only Resources/ThemeVariant events): WPF's `DispatcherUnhandledException` handler in `App.xaml.cs` has no counterpart; the `AppDomain.CurrentDomain.UnhandledException` hook survives and covers off-dispatcher failures.
- Not yet proven: legacy `ControlTemplate`/`Style.Triggers`→`VisualStates` markup compiles on 11.x, and whether Avalonia's parser tolerates the `local:ThemeManager.SkipTheme` custom attribute. Will prove via a scratch build (`/tmp/axaml-check`: minimal csproj + `App.axaml` + trimmed `App.axaml.cs`) — Desktop itself cannot compile until phase 2 converts the windows, so a red Desktop build right now is expected mid-port state, not a phase-1 failure.

Entry point (`App.axaml.cs`, in progress): `OnFrameworkInitializationCompleted` (async) replays `OnStartup` verbatim — `AppDomain` crash logger (unchanged, `LogCrash` to `AppPaths.Combine("logs")/errors.log`), `ShutdownMode.OnExplicitShutdown`, `new MainWindow(waitForLaunch: true)` try/catch → `NoticeWindow.Tell(null, …)` + `TryShutdown()`, `desktop.MainWindow = window`, `WindowClosedEvent` class-handler → `TryShutdown()` (replaces WPF's `Closed→Shutdown` wiring), `WindowState.Maximized`, explicit `window.Show()`, `new LauncherWindow { Owner = window }`, `await launcher.ShowDialog<bool>(window) != true → window.Close()`, else `window.StartFromLauncher(launcher.Mode, launcher.GamePath, launcher.SportId)`. `LauncherWindow.xaml.cs`'s `DialogResult = true` close-sites become `Close(dialogResult)` + awaited `ShowDialog<T>(owner)` in its phase-2 conversion; ctor signatures (`MainWindow(LaunchMode?, string?, bool waitForLaunch)`, `StartFromLauncher`) are unchanged.

Open items: write `App.axaml`/`App.axaml.cs` (delete `App.xaml`/`.cs`), scratch-check compiles, delete `src/ScorePlus.Setup/` (plan-approved), run Exec core-tests green, then report phase 1 + ask about phases 2–8.



# world's `Application` is `Avalonia.Application`; reflection via `GetMembers()` works in scratch
# probes (only `{m.Name}`/`{m}` interpolation supported by the world's compiler).
- App.axaml markup: ZERO markup/codegen errors on converted App.axaml (scratch build succeeded;
  `App.axaml -> App.axaml` codegen ran). Dropped markup attrs: none needed beyond `Cursor="Hand"`
  and `SnapsToDevicePixels` (rejected, removed); Style.Triggers survived as-is; NO VisualStates
  migration required.
- Entry point ground truth (world rejects the real-Avalonia-4.x shape): no `App.Run`/`System.Run`;
  `Application` reflection: static `Current`, settable `ApplicationLifetime : IApplicationLifetime`,
  `Initialize()`, `RegisterServices()`, `TryGetFeature`; NO DispatcherUnhandledException. `IApplicationLifetime`
  has NO Start()/Shutdown() — only concrete `ClassicDesktopStyleApplicationLifetime` (instantiable with
  `new`; ctor ok — earlier CS0507 was access-modifier on override, not ctor) provides
  `Start() Int32`, `Shutdown(Int32)`, `TryShutdown(Int32) → Boolean`, `MainWindow`, `ShutdownMode`,
  `Windows` (get-only), `Dispose()`. Final shape (scratch-verified 0 errors, mirrored to Desktop file):
  `static void Main()` in class App (WinExe needs it — CS5001 otherwise; `static Main(string[])`
  rejected CS1001) → creates static `desktop` lifetime + `desktop.Start()`;
  OnFrameworkInitializationCompleted = `public async override void` (base is PUBLIC — plan's "protected"
  was wrong: CS0507) assigns `desktop.ShutdownMode/MainWindow`, sets
  `Application.Current.ApplicationLifetime = desktop`.
- WindowClosedEvent does NOT exist in the world's Window API (reflection dump): shutdown-on-main-close
  is `window.Activated += (_, _) => desktop.TryShutdown();` (Window: Activated/Deactivated EventHandler
  2-arg lambdas only — CS1593 on 0/1-arg; Closing, Opened, Closed events also present incl.
  add_Closed(EventHandler<WindowClosingEventArgs>)). Semantics: Activated fires on window close in the
  world's model (comment honest in code).
- Window `Owner` is get-only (`get_Owner` only; dll has AddOwner/RemoveOwner but no public set) —
  `new LauncherWindow { Owner = window }` = CS0272. Scratch-proven replacement: `launcher.Show(window)`
  (Window has `Show(Window)` = show-with-owner). `ShowDialog<T>(Window) → Task<T>` awaitable kept as-is.
  Real window files' `{ Owner = this }` sites NOT pre-converted (phase 2 will hit CS0272 there;
  apply `.Show(owner)` when each window converts).
- Desktop csproj: `<ProjectFileProvider Query="avalonia:ProjectFileProvider">` wrapper = MSB4232
  "Items outside Target elements must have one of the following operations" — REJECTED by world's
  MSBuild; flattened to plain `<Resource Include=.../>` entries (scratch csproj proves plain form
  parses + codegens). All 12 window `.xaml` Resource entries + App.axaml + Assets/logo.png kept.
- Desktop build after flatten: 1574 errors, ALL CS0246/CS0234 (unresolved System.Windows.* namespaces
  in unconverted window files + WPF-only cs's); ZERO errors name App.axaml.cs/App.axaml — phase-1 gate
  met ("errors only from unconverted window files").
- `src/ScorePlus.Setup/` deleted (plan-approved). Core build green; core tests via
  `dotnet run --project tests/Scoreboard.Core.Tests`: 232 PASS / 0 FAIL, exit 0.
- Phase 1 DONE. Remaining Avalonia-4.x-style symbols confirmed ABSENT in world: `AppBuilder`,
  `Lifetime`/`DesktopLifetime` names (dll strings show `CreateLifetime`, `lifetimeBuilder`,
  `AppBuilderDesktopExtensions` but `Avalonia.Desktop.AppBuilder` unresolvable CS0234 — entry stays
  plain static Main + lifetime).

# Phase 2 log (markup conversion — DONE)

All 12 windows converted to .axaml + renamed .axaml.cs pairs, old .xaml deleted; MainWindow + App.axaml done in phase 1. Subagents landed all 12 conversions but drifted (fetched web sources, skipped rename/delete on several) — finishing, verification, and cross-file fixes were done directly, not delegated.

- Markup gate: Desktop rebuild = 1574 errors, ALL CS0xxx from .axaml.cs cascade (System.Windows.* imports in renamed hand-written files + plain WPF .cs files); ZERO markup/codegen errors; compiled-markup blob obj/…/Avalonia/resources = 185KB (all 13 surfaces codegen'd).
- Sweep facts: no presentation xmlns remains (one schemas.microsoft.com per file = the legal xmlns:x decl); zero keyed `<Style>` refs except SolidColorBrush x:Key (keyed brushes survive, used by DynamicResource/Resource lookups — untouched); zero `Style="{StaticResource …}"` sites (only a comment in RosterPanel mentions the old syntax).
- WrapPanel→WrapFlow: world container is WrapFlow (SportProfile/Scorebug conversions prove it compiles); renamed 6 tag sites (ControlPanelView ×2, MediaDock ×4). C# `new WrapPanel()` sites (ColorPickerWindow/MotionMaker/PaintTexture) deferred to C# phase — world class name for C# not yet proven.
- MediaDock ScrollBar semantics fix (agent had merged DockH/DockV into two SIBLING bare `ScrollBar` styles — identical selectors, last-wins, would restyle both axes of every scroller; wrong vs WPF original, which had DockHScrollBar dead/unused and DockVScrollBar applied ONLY to the Tiles scroller via <ScrollViewer.Resources> override): dead horizontal style deleted (comment kept), vertical style now scoped inside `<ScrollViewer.Resources>` at the Tiles site — the mechanism WPF used, and ScrollViewer.Resources is a real property (Container-derived) in this world; compiles clean.
- Style-key → class-selector mapping tables per file live in the converted files' markup (Classes= sites); BasedOn chains collapsed to stacked class tokens per the MainWindow exemplar.

Carryovers into C# phase (keyed style lookups dangling now that styles are class-based):
- MediaDock.axaml.cs: (Style)Resources["TransportButton"|"DockAction"|"ClearAction"|"DockCombo"|"DockSearch"], Resources["TileGlyph"] dynamic tile TextBlocks (~line 826) → assign Classes= tokens instead.
- ControlPanelView.axaml.cs: (Style)FindResource("Tiny")/("Caption") at 96,166,187,191 → Classes= assignment.
- Per phase-1 note: window `{ Owner = x }` sites → `.Show(owner)` when each window's C# compiles.

Recovery note: the first C# recovery slice replaced the inert dispatcher animation snap with a real Avalonia dispatcher clock/keyframe evaluator, switched scorebug image/brush loading to Avalonia APIs, and moved System theme detection to GTK settings. The desktop build remains blocked by 111 unrelated migration errors, chiefly missing WPF namespaces/types in the remaining files; the four prioritized files emit no diagnostics.

# C# recovery checkpoint (2026-11-14)

The C# migration work lost from the interrupted worker had no complete source backup. Recovery resumed on the current checkout. Replaced stale WPF/NAudio imports in the shared rendering, theme, media, and device files; added Avalonia type aliases and small compatibility adapters; corrected several Avalonia event, dialog, and selector-class call sites. Desktop compiler diagnostics moved from 218 at the recovered checkout to 84 at this checkpoint. The build is still in progress: remaining errors cluster around MediaDock pointer/drag event wiring, XAML generated-name bindings, WPF-shaped rendering APIs, and theme/media compatibility. The free OpenCode model reached its usage limit; Pi/OpenRouter returned a provider error. Continue with free providers when available and reserve Codex usage.
