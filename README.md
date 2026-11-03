# ScorePlus

**A native Windows scoreboard and broadcast graphics system for live production — one operator console, unlimited displays, and a scorebug you design yourself.**

> **Alpha software.** ScorePlus is under active development and the alpha builds are updated often. Expect rough edges; don't run it as the only copy of a show you care about. Release labels read like `a1.2`, `a1.3`, `a1.4`.

Everything runs locally on one Windows PC. No browser, no server, no internet connection, and no separately installed .NET runtime. The clock, the score and the graphics are all in one program, and the audience output updates the moment a value changes — there is never a "refresh" step mid-game.

The app carries its own **SP+** mark: the transparent one sits in the title bar and the launch box, the taskbar, Start Menu and shortcuts carry it, and the phone remote serves and shows it too.

Linux builds are maintained separately at [MysteriousJose/ScorePlus-Linux](https://github.com/MysteriousJose/ScorePlus-Linux) and are not officially supported.

---

## Contents

- [What ScorePlus Is](#what-scoreplus-is)
- [Install and Run](#install-and-run)
- [Your First Game — A Seven-Step Tutorial](#your-first-game--a-seven-step-tutorial)
- [Tutorial: Design a Scorebug](#tutorial-design-a-scorebug)
- [Tutorial: Two Screens with Displays and Layouts](#tutorial-two-screens-with-displays-and-layouts)
- [Tutorial: Video, Camera and Replay](#tutorial-video-camera-and-replay)
- [Tutorial: Rosters and Player Stats](#tutorial-rosters-and-player-stats)
- [Tutorial: Team Identity and Colors](#tutorial-team-identity-and-colors)
- [Tutorial: Keybinds](#tutorial-keybinds)
- [Tutorial: The Phone Remote](#tutorial-the-phone-remote)
- [Tutorial: Sport Profiles](#tutorial-sport-profiles)
- [Reference: The Control Panel](#reference-the-control-panel)
- [Reference: The Scorebug Studio](#reference-the-scorebug-studio)
- [Reference: Data Tokens](#reference-data-tokens)
- [Reference: Preferences](#reference-preferences)
- [Reference: File Types](#reference-file-types)
- [Where Files Live](#where-files-live)
- [Building and Releasing](#building-and-releasing)
- [Updates](#updates)
- [Project Layout and Tests](#project-layout-and-tests)
- [Troubleshooting](#troubleshooting)
- [What's Next](#whats-next)

---

## What ScorePlus Is

ScorePlus is two programs in one window: an **operator console** that a scorekeeper drives during a game, and an **audience output** that a projector, capture card or streaming encoder sees. They talk to each other instantly, so pressing `+1` on a score changes the on-air graphic before your finger leaves the button.

It was built around four ideas:

**1. One console, no scrolling.** The control panel puts both teams, the game clock, the play clock, the shot clock, down & distance, the ball spot, possession, timeouts, the timeout clock and your custom stats on a single page. There is nothing to hunt for and no tabs to flip through mid-play.

**2. Live data, staged data.** Scores, clocks, team names and the selected player's data are *live* — they hit the audience the instant they change. Everything else is *staged*: down and distance, possession, timeouts, custom stats and the chosen layout only go on air when you press **Update Output**. That gives you a rehearsal layer between your fingers and the broadcast.

**3. The graphic is yours.** A built-in studio lets you design the scorebug from scratch with text, logos, shapes, video boxes and live HTML — on a canvas that can be HD, Full HD, QHD, 4K or vertical. Every element is typed, colored, bordered, rotated and animated independently, and any element can read any value the game holds.

**4. It scales up without getting more complicated.** Start with one scorebug on one screen. When the show grows, add a lower thirds display, link displays to each other, put the console on a phone, and hand parts of one layout to another so you are not maintaining five near-identical scorebugs.

### At a Glance

| | |
| --- | --- |
| **Platform** | Windows 10/11, 64-bit. Self-contained single-file executables. |
| **Sports** | American Football, Australian Football, Baseball, Basketball, Cricket, Hockey, Lacrosse, Rugby, Soccer, Tennis, Volleyball, Wrestling, and a blank Custom preset |
| **Displays** | Unlimited, each holding unlimited layouts, each layout with its own transition |
| **Inputs** | Keyboard, mouse, phone/tablet remote, live cameras, microphones |
| **Files** | `.spg` game, `.spl` layout, `.spd` display, `.spr` roster, `.spp` player, `.spf` media folder |
| **Saving** | Portable save/load, 100-step undo, rolling autosaves every 5 seconds |

---

## Install and Run

There are two ways to get ScorePlus running, and they behave differently on purpose.

### The Installer (Recommended)

Download **`ScorePlusSetup.exe`** from the releases page and run it. You do not need administrator rights: it installs for the current user into

```
%LOCALAPPDATA%\Programs\ScorePlus
```

and adds a Start Menu shortcut, an optional desktop shortcut and an entry in Windows' installed-apps list. `Uninstall.exe` in that folder removes it cleanly.

An installed copy is the only kind that **checks for updates** and can replace itself.

The setup also accepts `/SILENT`, `/DIR=<path>`, `/NOICONS` and `/NOREG` for scripted or unattended installs.

### The Portable Build

Download **`ScorePlusPortable.exe`** and run it from anywhere — a USB stick, a desktop folder, a network share. It needs nothing else: one file, no loose DLLs, no runtime install.

A portable copy never checks for updates and never touches the registry, so it is the safe choice for testing a new alpha beside the version that runs your show.

### First Launch

Two windows open together: the compact **launch box** with the ScorePlus mark and version, and the **main window** behind it, dimmed and locked. The console is visible so you know where the game will land, but nothing on it can be changed until you create or open a game.

The launch box offers:

- **New Game** — a truly blank slate. It asks which sport the game is for and then gives you one factory scorebug on one display: clean teams, 0–0, the sport's own opening clock.
- **Open Game** — a saved `.spg` file.
- **Autosaves** — the recovery points listed down the side, each labelled with its time. Click one to load it.
- **Delete all** — clears every autosave. It arms for a few seconds and needs a second click, so a stray click can never throw away a season. Saved `.spg` files are never touched.

> **What a new game keeps and what it clears.** A new game resets the teams, logo, score, clocks, sport values, custom stats, rosters-in-play, media folders and playlists, and any extra layouts or displays — it goes back to one scorebug. It keeps the things that describe *you*, not the game: imported media files, sport profiles, rosters and their stat columns, keybinds, fonts, preferences and your accent colors.

---

## Your First Game — A Seven-Step Tutorial

This walkthrough takes you from a cold launch to a scorebug on a second screen. Budget five minutes.

**Step 1 — Start a new game.** Launch ScorePlus and click **New Game**. Pick the sport from the list. The launch box dismisses and the console unlocks.

**Step 2 — Name the teams.** Click the **…** button beside **Visitors** and beside **Home**. The team box that pops up holds the name, the abbreviation (used in tight spots on the graphic), the logo, and the team's primary and secondary colors. Type the real names, pick their colors, and close it. The graphic already shows the changes — team names are live.

**Step 3 — Set the clocks.** In the **GAME CLOCK** card, click the value itself and type `12:00`, then press **Enter**. Typing always sets an exact value; the `−1:00`, `−0:01`, `+0:01` and `+1:00` buttons nudge it. **Start** runs the clock and turns into **Stop**; the play clock and shot clock work the same way.

**Step 4 — Put a picture on screen.** In the top-row icon bar, press **▶** (Audience Output). If the game has one display, the window opens immediately; when there are several, you are asked which display it is for. Press **F11** to fill the screen — exactly like the old *Broadcast Fullscreen* button, now on the window itself.

**Step 5 — Score something.** Click `+6` on the home side. The scoreboard on the audience screen reads 6 before the button finishes clicking. `−` buttons step back down, and **Undo** takes back the last 100 changes.

**Step 6 — Publish the staged values.** Set the down, the distance, who has the ball and how many timeouts are left. Nothing appears until you press **Update Output** — then all of it lands at once. The **Live: ON** toggle pauses that live feed for scores, clocks and names (useful when you're correcting a typo and don't want it on air); click it again to resume.

**Step 7 — Save.** Press **Save Game** and choose a folder. You get a `.spg` file you can double-click next week, and it carries every display and layout with it — reopening the game returns to the same screens and the same layout on air. If you forget, autosaves run every five seconds and the newest eight points are kept on the launch screen.

That's a working show. Everything from here is refinement.

---

## Tutorial: Design a Scorebug

Open **Scorebug Studio** from the second row of the console. The studio opens maximized and fills the desktop: toolbar on top, layers on the left, property panel on the right, and the scorebug canvas in the middle framed with a border marking exactly where the artwork ends.

### Add Your First Three Elements

1. Press **TEXT +** in the toolbar. A text element appears on the canvas with placeholder content.
2. In the property panel, set **NAME** to something you'll recognise later (`Home score`), then set **TEXT** to `{{HOME_SCORE}}`. The canvas immediately prints the current value — the token is bound to the game, not to a typed number.
3. Press **SHAPE +**, then set **Shape type** to *Rectangle*, drag it behind the text, and give it a background color. The layer list on the left controls the stacking order — the top row is the front of the picture.
4. Press **LOGO +** and choose which team it shows. Logos themselves are configured in the console's **…** team box, not here, so one imported logo serves every layout.

### Positioning, Resizing and the Modifier Keys

Drag an element to move it, or type exact **X**, **Y**, **Width** and **Height** values in the property panel and press **Enter**. The canvas is scaled by the panel, so all four edges stay reachable no matter how big the window is.

Hold a modifier while resizing for precise control:

| Hold | While resizing |
| --- | --- |
| `Ctrl` | The box and **all of its text** scale together, instead of text keeping its point size |
| `Shift` | Aspect ratio is locked — drag one edge and the other follows proportionally |
| `Ctrl` + `Shift` | Both at once |

### Selecting, Layering and Deleting

| Action | How |
| --- | --- |
| Select one element | Click it |
| Add or remove an element from the selection | `Ctrl` + click |
| Add a range of elements | `Shift` + click |
| Deselect everything | `Escape` (or click empty canvas) |
| Select layers | `Ctrl` + click rows in the layer list, `Shift` + click for a contiguous range |
| Delete the selection | `Delete` |
| Change many at once | Change any property — box, border, color, gradient, opacity, size — with several selected |

Every property change with a multi-selection lands on all of them, so restyling a row of stat boxes is one color pick rather than six.

### Styling

The property panel only shows sections the selected element can actually use: text gets typography, rotation, transitions and player data; a logo gets a team picker; a video box gets a media tag and an audio output; a shape gets a shape type. Anything irrelevant stays hidden.

Options available on almost everything:

- **Border** — thickness and color, fixed or from a team palette slot. Zero thickness hides it entirely, and a shape with a transparent background becomes a clean outline.
- **Gradient background** — a second color and a direction in degrees, and both colors can be team slots.
- **Opacity** — per element, so a stat box can sit at 60% over live video.
- **Color sources** — anywhere a color is offered, choose *Custom* for a hex/alpha picker, or **Home Primary**, **Home Secondary**, **Away Primary**, **Away Secondary** so the graphic recolors itself when you change a team's identity.

### Type and Motion

Every text element picks its own font from 25+ installed families, or imports a `.ttf`/`.otf`/`.ttc` of its own (kept in the app's `Fonts` folder so the layout travels with it). Each one can be **bold**, *italic*, <u>underlined</u> or ~~struck through~~, rotated to any angle, and animated.

Two separate animation systems, easy to confuse:

- **Value transition** — what happens when the number/word changes. Thirteen choices:
  `Cut`, `Fade`, `FadeScale`, `Slide Up`, `Slide Down`, `Slide Left`, `Slide Right`, `Wheel Up`, `Wheel Down`, `Flip`, `Zoom`, `Pop`, `Drop`
  Set the crossing time from **0 to 5 seconds**. `0` is an instant snap.
- **Ambient motion** — `None`, `Pulse` or `Drift`, which runs all the time regardless of the value.

There is also a show-wide **value switch delay** in Preferences (0 by default): how long a graphic deliberately holds the old value before printing the new one. Use it when a fast-changing score needs a beat to be readable — say, 0.3 seconds.

### Live Artwork Instead of Numbers

Three elements can draw artwork from game state instead of (or alongside) typed text, chosen from the **ICON** section:

| Icon | What it draws | Follows |
| --- | --- | --- |
| **Timeout bars** | A row of short horizontal lines, one per timeout the team has left | Home or visitors timeouts |
| **Possession dot** | A dot on the left edge for the visitors, on the right for home (mirrorable) | Possession |
| **Bases** | Three diamond bases, filled when a runner is on, faint outline when empty | The runner on each base |

They update themselves with no studio edit, and **every icon animates** with the element's own value transition: a base fades and pops in as a runner arrives and shrinks away when they leave, new timeout bars slide or dissolve into the row, and the possession dot slides across the plate when the ball changes hands. The possession dot and the text are exclusive: turning on the dot removes the text rather than sitting under it. Small icons keep their size when you resize the box, unless you hold `Ctrl`, exactly like text.

A baseball base has two colors of its own: the **outline** follows the element's text color, while **Base fill** sets the inside independently — a fixed color, or one of the team palette slots, so a lit base can wear the scoring team's color.

### Canvas Size and Background

Beyond the canvas's own bar you choose the size: **HD (1280×720)**, **Full HD (1920×1080)**, **QHD (2560×1440)**, **4K UHD (3840×2160)**, **Vertical (1080×1920)** or **Square (1080×1080)**. Changing it scales the layout and is remembered with it.

**Canvas background** belongs to the layout — green screen, navy, black, any color, or a typed hex — and the studio, the program preview and the audience all paint the same thing. The **Reset** button puts it back to the ScorePlus navy; it sits beside the green-screen button so a chroma key and a clean backdrop are one click apart.

### Multi-Select, Duplicate, and the Finer Points

- Layers list runs **front of the picture at the top**.
- Every element flies forward/back with the layer buttons, or you can drag rows.
- The canvas is a real drag surface: no element can be pushed outside it, and an element can sit flush with any edge.
- The studio itself follows the app's theme, so a light theme gives you a light studio — but the artwork never changes color because of a theme. The graphic keeps the colors you designed.

---

## Tutorial: Two Screens with Displays and Layouts

A **display** is one audience screen. A **layout** is one design that a display can show. A display can hold many layouts; a layout belongs to exactly one display.

### Add a Lower Thirds Screen

1. In the console, use the display dropdown directly above the program preview. Choose **+ New display** (or open the studio, where the **DISPLAY** bar has the same controls).
2. The new display starts blank — one empty layout, nothing on air — because it is meant for something other than the scorebug.
3. In the studio, switch the **DISPLAY** bar to the new display, then build the lower third on its canvas: a shape, `{{PLAYER:HOME:Name}}`, `{{PLAYER:HOME:Position}}`, a team logo.
4. Back in the console, select that display in the dropdown and come back to the scorebug display when you're done.

To change the display inside the studio, use the **DISPLAY** bar at the top. It sits above the **LAYOUT** strip on purpose: pick the screen first, then pick what it shows.

### Layouts as Pictures, Not a Dropdown

Every layout on the chosen display appears as a strip of **thumbnails with the layout's name underneath**. Click one to edit it. In the console, the program preview has **◀ / ▶** arrows beside it that step through the displays' layouts; step to the one you want and press **Update Output** to send it. It's a slideshow of scorebugs — every layout still reads the same live game data, so nothing needs re-entering.

### Switching Transitions

When a display switches layouts, the hand-over belongs to the layout **arriving**. Each layout carries its own **TRANSITION IN** — `Cut`, `Fade`, `Slide Left`, `Slide Right`, `Slide Up`, `Slide Down` or `Zoom` — over anything from **0 to 5 seconds**. Set it on the layout bar. One scorebug can snap on while the next dissolves in slowly, and the choice saves in the game, the `.spl` file and the `.spd` file.

### Building the Hand-Over (Transition Maker)

**Transition Maker** sits beside the studio in the header. A clip has two legs — **Exit** and **Enter** — and each leg is keyed on its own ruler. The **PIECES** list is the heart of it: choose **Whole graphic**, one of the layout's real items, or a **temporary piece** you create here. Tracks and keys are scoped to the selected piece, so a score can swing out while its plate stays put, and several items can move on their own timings inside the same leg. The preview plays the real layout, with temporary pieces in place, through the exit and the arrival, and the timeline always shows the selected piece's keys.

**Add temporary piece** creates artwork that is saved inside the clip and drawn only while the transition runs — a flash card, a strap, a wipe bar. It has its own name, text, size, position, plate and text color, and it is keyed exactly like a real item. **Remove piece** deletes it again; layout items are edited in the studio. Add a track for any of ten properties: move X, move Y, uniform scale, scale X, scale Y, rotate, opacity, blur, skew X and skew Y. Each key takes a time in seconds, a value and a curve (`Linear`, `In`, `Out`, `InOut`, `Back`, `Bounce`, `Elastic`), so a score can swing out on a back curve and settle in on a bounce. Click the ruler to place the playhead, double-click to add a key there, and drag a key dot to retime it.

**Color keys** are the second half. For the text, the plate or the outline, add color keys at points through either leg: the graphic can wash to the team color as it leaves and back to white as it arrives. Color keys are scoped to the selected piece too. **Save clip** puts the named clip in the show's library and in the studio's transition dropdowns; nothing is applied from this window. On air, a per-piece exit crops each item out of the outgoing frame and moves it on its own, while a per-piece arrival runs on each item's own box.

Clips live in the show's library and are written on their own as **`.spt`** files. Because the clip is copied onto the layout or element that uses it, an exported `.spl`, `.spd` or saved game always carries its motion with it — nothing points at a library entry that might move.

### Motion a Value Follows (Animation Maker)

**Animation Maker** keys motion rather than a hand-over. A loop runs from its first key to its last for as long as the graphic is up — a bob, a sway, a breathe, a spin, a color cycle — and **ping-pong** runs it back the other way instead of snapping to the start. Tick **Play when the value changes** and the same clip becomes the flourish a new number plays: a pop, a ticker roll, a slide-in.

Apply it to an element and it replaces the two motion presets for that element; the value-change form runs after the new value lands. Animations are written as **`.spa`** files and, like transitions, are copied onto the element that uses them so they save with the layout, the display and the game.

### Per-Window Layout Choice

The staged layout and display are chosen **per window**. Open a second console with **New Panel** (the ⧉ icon) and it can be lining up a lower third on display 2 while the first window works the scorebug on display 1. Nothing you stage moves until **Update Output**. A detached panel carries its own **sound level meter** and show line under the preview, so the operator at that screen sees the output level and what is on air without turning back to the main window.

Below the staged preview sits a second, smaller thumbnail: the **audience view**. It mirrors exactly what the chosen display is really showing on air, and it follows Update Output, not your staging. It's a monitor, not another preview — you can point it at any display to check what that screen looks like.

Under the preview is a **sound level meter** with the peak level the machine is putting out, next to the on-air display and layout, so you can see at a glance what is live and whether audio is actually flowing.

Above the meter sit **eight quick-layout buttons**, navy in every theme. Each one can point at any layout on **any display** — a scorebug, a lower third, a sponsor board — and every detached panel carries the same eight. Right-click a button to reassign it, or to **clear** it so the button does nothing until it is assigned again. By default a press only **stages** the layout, so Update Output still sends it; **Preferences → Quick layout buttons also update output** makes the same press go straight to air. The assignment and that switch are stored in preferences, so they follow the operator, not the game.

### Linking Displays Together

A layout can be **linked** to a layout on another display. Choose it under **LINK THIS LAYOUT TO**, and tick **both ways** if the link should work in reverse too — then choosing layout A on display 1 also stages layout F on display 2, and choosing F on display 2 stages A on display 1. One **Update Output** publishes both.

A link that points at another display can't survive a single-display export (there's no second display in the file), so it is cleared on import. That's deliberate: a file never silently rewires your show.

### Copying and Moving Layouts

- **Copy to…** clones a layout — plus its artwork — onto another display as an independent copy. Editing one does not touch the other.
- **Move to…** hands the layout over entirely, artwork and identity included, and follows it to the target display so you stay where you were working.

A display never gives away its last layout; there is always something on that screen.

### Sharing Parts Between Layouts

If two layouts need the same score bar, don't build it twice. In the studio press **PARTS +** and pick elements from another layout on any display in the game. The copies land nudged clear of the original and stay **linked**: move one, resize one, restyle one, and every copy follows on every layout. That's how you avoid a dozen nearly identical scorebugs.

When one copy should stop following, open its properties and press **Break link** — that copy becomes independent while the others stay together.

### Adding Embedded HTML

**HTML +** drops a live HTML/CSS widget on the graphic. It is drawn by a real browser engine, so the studio, both previews and the audience output show the same picture, and it can carry a full CSS layout — a sponsor ticker, a cleaned-up lower third, a countdown strip. Anything inside it can still read live data through `data-token` attributes and the small `window.ScorePlus.set(token, value)` helper, so the HTML element animates and updates like any other.

---

## Tutorial: Video, Camera and Replay

Everything audiovisual lives in the **MEDIA** tab. It is a full-size pane, not a dock: buttons are the same size as the console's, and it stays usable in a **New Panel** window parked on the media machine.

### Import Clips and Organise Them

1. Switch to **MEDIA** and press **Import**. Pick audio or video files. Each becomes a thumbnail tile in the browser.
2. Press **+ Folder** to make a folder, then drag a clip onto the folder card to file it. **Double-click** any folder card to open it, and folders can sit inside folders. **← Back** and the path bar walk you out again.
3. Press **+ Playlist** and drag clips onto it — or open the playlist and use **Add clip**. A playlist opens into a full ordered list rather than hiding behind a dropdown.
4. The **search box** looks through every clip, folder and playlist, and deliberately ignores which folder you're standing in. A clip never disappears just because somebody filed it away.
5. **Export folder…** writes a whole folder — everything nested inside it and its media — as one **ScorePlus Folder (`.spf`)**; **Import folder…** brings it back whole on another machine.

### Tags Are What Connect Media to the Graphic

A tag is a folder that the scorebug can watch. If a clip is tagged **Ads**, it plays inside a video element watching **Ads**, and a video element watching **Highlights** shows nothing while that ad runs. Tags keep two video boxes independent without any routing step.

There is one special tag: **ANY**. A video element tagged ANY plays whatever clip is currently cued, regardless of its tag — the "just show me what I loaded" option.

To tag a clip: select it, pick the tag from the dropdown, press **Set tag**. Suggested tags include General, Ads, Highlights, Music, Anthem, Sponsors, Camera and Microphone, and you can type your own.

### Volume, Speaker, Loop

- The scrub bar seeks anywhere in a clip; you don't have to sit through it.
- **Play/▶**, **Stop/■** and the transport bar drive the selected clip. Stop clears the last frame from the preview and takes the overlay down.
- **Volume** is per clip, so an ad can be quiet and a highlight loud.
- **Speaker** is per clip *and* per element: pick the Windows audio device this clip goes out through. In the studio, a video element also names its own audio output, so two clips on two overlays can go to two different speakers. Routing happens only when an element is actually on an audience screen, so several preview windows never open the same speaker twice.
- **Loop this clip** repeats one video indefinitely — the stadium-screen behaviour.
- **Loop playlist** repeats the whole ordered run. Without it, a playlist plays its clips one after another until it ends or you press **Stop**.

### Live Cameras

**Add camera** puts a capture device (webcam, HDMI capture, SDI card) into the library as a live item. It routes by tag like anything else.

- **Route…** points a camera item at a different device, and can rename it — the item keeps whatever name you gave it, with the device name shown beside it, so "ICS Spring Camera" is identifiable in a list of three.
- **Switching the device while it is live changes the shot immediately.**
- **Add microphone** adds an audio capture device the same way, for a commentary or ambient feed.

### Replay, End to End

The camera keeps rolling a two-minute buffer while it is live. Replay is staged, not fired blindly.

1. Something good happens. Press **Replay**. The replay opens **in the program preview only** — the audience keeps watching live camera — frozen on the newest frame, and the scrub bar becomes a replay timeline.
2. Drag back to where the play started. You can go back as far as two minutes.
3. Press **Start replay** to cut it to air exactly as it stands. A paused replay goes out as a freeze frame rather than suddenly jumping into motion; press **▶ Play** to roll it.
4. Pause and resume freely while it is on air.
5. Press **Save replay…** to write everything from where you are through to the moment you pressed it as a video clip on your computer.
6. Press **Live** to jump back to the present and pick up the camera again — instantly, with no black frame in between.

If you never press **Start replay**, nothing on air changes. The audience screen and the audience view thumbnail both keep feeding live until you do.

**Clear** empties the media side and blanks the screen — the way out of a camera that is holding on its last frame.

> **Why replay is staged.** A live camera has no "rewind" that the audience can see. Staging the replay means you can find the start of the play, confirm the frame, and only then make the cut — and if the moment passes, you just press **Live** and nothing ever went out.

---

## Tutorial: Rosters and Player Stats

The **ROSTERS** tab is a player desk, not another console.

### Build a Roster

1. Switch to **ROSTERS**. Use the visitors/home switch at the top to pick the side you're editing.
2. Pick or create a roster for that side. A roster is a team's list of players and the stat columns it tracks.
3. Add a player and fill in number, name, position and any stat values. Each sport ships with **20+ stat columns** already present (27 for American football; 22 for basketball, baseball, soccer and the rest), so you never have to name a stat before typing a value. Columns are the roster's, not the player's — every player in the roster gets the same ones.
4. **Select player** makes someone the *selected player* for that side. **No player** takes them off the graphic entirely, leaving player elements blank until somebody is selected again. That choice survives a roster edit and a save.

### Put a Player on the Graphic

In the studio, add a text element and press **PLAYER DATA +** (or use the PLAYER DATA section in the property panel). Pick the team and the value — `Name`, `Number`, `Position`, `Yards`, whatever the roster tracks — and the element binds to

```
{{PLAYER:HOME:Name}}
{{PLAYER:HOME:Number}}
{{PLAYER:AWAY:Yards}}
```

One element now shows **whoever is selected** for that side. Switch players in the roster tab and every player element reprints at once — on the preview, every monitor and the audience output, without an Update Output.

If you'd rather an element keep printing one specific player — a "player of the game" strip, say — open its properties and **pin** that player from the roster list. It stops following the selection and holds that player while somebody else is on the graphic.

### Import and Export

**Import…** and **Export…** in the ROSTERS header exchange:

- a whole roster as a **ScorePlus Roster (`.spr`)**,
- a single player as a **ScorePlus Player (`.spp`)**.

Both carry the team — numbers, names, positions and which stat columns it tracks — and neither carries a stat value. A shared or imported roster starts with blank numbers, so nobody's totals travel with it and a season starts clean. Imported entries always get fresh ids, so nothing collides with an existing roster.

Rosters live with the app and survive a new game. Which roster each side uses, who is selected and their stat values travel in the `.spg`.

---

## Tutorial: Team Identity and Colors

Team identity lives in the console, not the studio.

1. Click the **…** beside **Visitors** (or **Home**). The team box opens.
2. Set the **name**, the **abbreviation** and the **logo**. The logo is imported once here and is then available to logo elements on every layout, every display.
3. Pick the **primary** and **secondary** colors. A live swatch in the three-dot menu shows the current pair whenever you're not editing them, so you can read a team's palette at a glance.
4. Change any of it and the graphic follows: team names are live, and every element pointed at a team color slot recolors itself.

That last part is the real trick. In the studio, an element's color can be *Custom* — a fixed hex — or one of four **palette slots**:

`Home Primary` · `Home Secondary` · `Away Primary` · `Away Secondary`

Point the scorebox at `Home Primary` and the graphic will be correct for whoever is home tonight, without opening the studio. It works for foreground, background, the gradient's second color and borders.

The abbreviation is what fits where a full name won't, and it also feeds tokens like `{{HOME_ABBR}}`, `{{BALL_SIDE}}` and `{{POSSESSION}}`.

---

## Tutorial: Keybinds

Nothing is bound by default — every key is yours to assign.

1. Press **⌨** in the top row, or **Keybinds…** from the menu.
2. The list shows only the **selected sport's** bindings plus the show-wide ones, so a baseball game never shows you hockey's power-play key. Switch sports and the list changes with it.
3. Click a control, press the key combination you want. Bindings persist between sessions and travel with your preferences.
4. Things worth binding first: **Update Output**, the **Live: ON/OFF** toggle, the ball spot's side, the clocks' start/stop, and each scoring button you press most.

Keybinds are global to the app while a game is open, so a second control panel doesn't need its own shortcuts — the one keyboard drives whichever window has focus.

---

## Tutorial: The Phone Remote

Preferences can serve the whole control panel to a phone or tablet on the same Wi-Fi.

1. Open **Preferences → Remote** and switch it on. Set a **port** (8080 by default) and a **password**.
2. Press **Open remote**. The URL it prints is your machine's **network address**, not localhost — e.g. `http://192.168.1.5:8080` — so the address bar shows a URL a phone can actually use.
3. On the phone, open that address, sign in with the password, and the console appears in a thumb-sized layout: both teams above and below the clock, so nothing clips on a narrow screen.
4. Type into any value and press **Enter**. Every action runs exactly the same code as the on-screen button.

The remote carries the full panel: both teams (name, abbreviation, score, color, timeouts, timeout clock), the game clock, period, play clock, shot clock, down & distance, the ball spot and its side, possession, the sport's own values, custom stats, layout switching, **Update Output**, **Live**, **Undo** and **New panel**.

It is off until you enable it, requires the password for everything (hashed, constant-time compared, login throttled), and serves nothing without a signed-in session. Windows may ask once whether ScorePlus can accept connections — allow it on **private** networks and leave it blocked on public ones.

> Anyone on your Wi-Fi with the password can change the score. Don't enable the remote on a network you don't trust, and change the password if it leaves the building.

---

## Tutorial: Sport Profiles

**Sport Profiles** edits how a sport behaves — not how it looks.

You can change the period label, the number of periods, the period length, timeouts per team, the scoring buttons, whether the clock counts up, and whether the sport has a shot clock. You can also edit that sport's **SPORT VALUES** list — the balls, strikes and bases for baseball, power play for hockey, overs and wickets for cricket, sets for tennis — including the starting value every new game opens with.

- **Add value** creates a new number to step or an on/off switch for something a preset doesn't have.
- Any value can be removed.
- **New profile** starts a custom sport from scratch, not a copy.
- Built-in presets can be edited too; your edits are stored as overrides you can **Reset** back to the shipped preset.
- Custom sports can be duplicated, renamed and deleted.

Values edited here are copied onto the game when it starts, so a profile change applies to the next new game — not to a game already in progress.

Every sport ships with its own console values. This is what a game gives you beyond score and clock:

| Sport | Period | Sport controls |
| --- | --- | --- |
| American Football | Quarter | Down & distance, ball spot, play clock, new series |
| Australian Football | Quarter | Behinds, tackles, inside 50s, free kicks |
| Baseball | Inning | Half (top/bottom), balls, strikes, outs, three bases, hits, errors |
| Basketball | Quarter | Team fouls, bonus, points in the paint, shot clock |
| Cricket | Innings | Overs, wickets, extras, target, day/night, innings break |
| Hockey | Period | Power play, shots, penalty minutes, empty net |
| Lacrosse | Quarter | Shots, saves, faceoff wins, power play, penalty minutes |
| Rugby | Half | Tries, conversions, penalty goals, cards |
| Soccer | Half | Shots, corners, saves, offsides, cards |
| Tennis | Set | Sets, games, serve, tiebreak |
| Volleyball | Set | Sets, serve, set point |
| Wrestling | Period | Near fall, riding time, blood time, overtime |
| Custom | Period | Whatever you add |

Flags print their words on the graphic — `PP`, `BOTTOM`, `BONUS`, `TOP` — instead of 1 and 0. **Add sport data** in the studio only ever lists the sport you are playing: an American football game never offers soccer corners.

---

## Reference: The Control Panel

The main window is one page, top to bottom.

### Top Row

| Control | What it does |
| --- | --- |
| **⧉ New panel** | Opens a second, synced control panel in its own window |
| **⌨ Keybinds** | Assign key combinations to controls |
| **⚙ Preferences** | Theme, accents, hints, remote, updates, value switch delay |
| **▶ Audience output** | Choose a display and open its on-air window (F11 fills the screen) |
| **☰ Event log** | The game's event history in its own movable window |
| **▦ Game report** | Export a printable HTML summary of the game |
| **Save Game** / **Open Game** | `.spg` save and load |
| **Scorebug Studio** | Design the on-air graphic |
| **Sport Profiles** | Edit how the sport behaves |
| **New Game** | Start a blank slate |

### The Console Body

- **Visitors** on the left, **Home** on the right, each with a **…** team box (name, abbreviation, logo, primary/secondary colors), a score stepper, and timeouts.
- **Game clock** — typed as `MM:SS` or a plain number of seconds, with ±1s and ±1m buttons and Start/Stop.
- **Play clock** and **shot clock** — presets (40/25, 24/14), ±1, start/stop. Clocks count the whole way down and show `0` rather than stopping a second early.
- **Down & distance**, the **ball spot** with its own side (`Home 37`, `Visitors 25` — independent of possession, capped at 50) and possession (`HOME ▶` / `◀ AWAY`).
- **Timeout clock** — one-minute clock with a reset and ±1s/±10s.
- **Sport controls** — that sport's own values as compact `− value +` strips, typed over by clicking the number.
- **Custom stats** — any number *or* any text: `3rd`, `N/A`, `TBD`. **＋ Add** creates one, and each can be shown on the graphic with `{{STAT:Name}}` and in the report.
- **Program preview** with the display dropdown, the **◀ ▶** layout arrows, the **sound level meter** and the on-air/layout readout, and the smaller **audience view** below it.
- **Live: ON / OFF** and **Update Output** at the bottom of the preview column.

Every value reads the same way: add buttons above, subtract below, and click the number to type an exact value. `Enter` commits a typed value and leaves the field without closing the window it's in.

### Tabs

**CONTROL PANEL**, **MEDIA** and **ROSTERS**, each filling the pane at console-sized buttons. The active tab carries the accent-colored outline (also on first launch, before you've clicked anything).

Every notice and confirmation in ScorePlus is the app's own themed window — closing a game with unsaved changes brings up **Keep editing / Exit without saving / Save & exit**, and deleting a roster or clearing autosaves asks in the same style. There are no Windows message boxes anywhere in the app.

---

## Reference: The Scorebug Studio

Layout: toolbar across the top, layers down the left, canvas in the middle, properties on the right, and the display/layout bars above the canvas.

### Toolbar

Every button is a capitalised label with a plus: **TEXT +**, **LOGO +**, **IMAGE +**, **SHAPE +**, **SPORT DATA +**, **PLAYER DATA +**, **VIDEO +**, **HTML +**, **PARTS +**.

### Element Types

| Type | What it draws | Notable properties |
| --- | --- | --- |
| **Text** | Any bound or typed string | Font, size, bold/italic/underline/strike, rotation, color or text gradient, texture, value transition, icon, quarter style |
| **Logo** | One team's logo | Which side (Home/Away); the artwork itself comes from the console |
| **Image** | A still picture imported with **IMAGE +** | The file is copied into the app, so the design survives the original being moved |
| **Shape** | Rectangle, circle or triangle | Shape type, fill or gradient, border, opacity |
| **Video** | Live clip from one tag | Media tag (or **ANY**), preserve aspect ratio, audio output, border, radius |
| **HTML** | A live web widget | Free HTML/CSS/JS, `data-token` bindings |

### Properties Worth Knowing

- **Quarter style** is per element (`Quarter 1`, `Q1` or `1`), so two graphics can print the period differently in the same game.
- **Preserve video aspect ratio** locks the box to the clip's shape — nothing is stretched or cropped. Turn it off and the box can be any shape, with the picture filling it.
- **Media tag** decides which clips can appear. **ANY** accepts every clip.
- **Audio output** is per video element, independent of the clip's own speaker, so two overlays can feed two destinations.
- **Border** and **gradient** each take a fixed color or a team palette slot.
- **Paint & texture** opens the finish editor: a gradient on the text itself as well as the plate, with any number of stops, either linear at an angle or radial out from a centre. The same editor lays a material over the paint — `Matte`, `Metal`, `Chrome`, `Glass`, `Frost`, `Fabric`, `Velvet`, `Stone`, `Concrete`, `Paper` or `Hologram` — with fourteen knobs of its own: roughness, glossiness, ambience, shine, metallic, grain, emboss, bevel, satin, iridescence, opacity, scale, contrast and light angle, plus a tint.
- **Opacity** is four sliders, not one: the whole element, the printed value, the background plate and the outline each fade on their own. A score can dissolve away while its plate stays solid, or a border can fade without touching the text.
- **Motion** has two routes: the built-in `None / Pulse / Drift` presets, or a clip from the animation maker that replaces them.
- **Rotation** is in degrees; negative angles are fine.
- **Transition** and its **duration** (0–5s) live with the element, next to the font controls.

### Keyboard and Mouse

| Input | Result |
| --- | --- |
| Click | Select one element |
| `Ctrl` + click | Add or remove from selection |
| `Shift` + click | Add to selection |
| `Ctrl` + click layer row | Select an extra layer |
| `Shift` + click layer row | Select every layer between the two clicks |
| `Delete` | Remove the selected elements |
| `Escape` | Deselect everything |
| `Ctrl` + resize | Scale the box and its text together |
| `Shift` + resize | Lock the box's aspect ratio |
| `Ctrl` + `Shift` + resize | Both |
| `Enter` in a value field | Commit and leave the field |

### Display and Layout Bars

- **DISPLAY** — which screen you're editing; also where displays are added, renamed and removed.
- **TRANSITION IN** — the arriving layout's hand-over and its duration.
- **LINK THIS LAYOUT TO** — the layout on another display that moves with this one, with a both-ways checkbox.
- **LAYOUT** — the display's layouts as thumbnails with their names underneath, plus **Import .spl… / Export .spl…** and **Import .spd… / Export .spd…**, **Copy to… / Move to…** and **PARTS +**.
- **CANVAS BACKGROUND** — green screen, navy reset, black, custom color.
- **Size presets** — HD, Full HD, QHD, 4K UHD, vertical, square.

### Safety in the Studio

The studio is a real editor: a maximized window that never sizes past the working area, a canvas that scales with the panel and keeps all four edges reachable, and layer ordering that reads front-to-back. Multi-select plus one property change applies to the whole selection, so a row of stat boxes is never edited six times.

---

## Reference: Data Tokens

Bind live data by typing a token into a text element's content, or by using the toolbar buttons which insert them for you.

### Game and Clock

| Token | Prints |
| --- | --- |
| `{{HOME_TEAM}}` / `{{AWAY_TEAM}}` | The team's full name, exactly as typed |
| `{{HOME_ABBR}}` / `{{AWAY_ABBR}}` | The abbreviation |
| `{{HOME_SCORE}}` / `{{AWAY_SCORE}}` | The score |
| `{{PERIOD}}` | The period, in the element's chosen style |
| `{{GAME_CLOCK}}` | Game clock as `MM:SS` |
| `{{PLAY_CLOCK}}` | Play clock in seconds |
| `{{SHOT_CLOCK}}` | Shot clock in seconds |
| `{{TIMEOUT_CLOCK}}` | Timeout clock as `MM:SS` |
| `{{TIMEOUTS}}` | Both teams' remaining timeouts |
| `{{POSSESSION}}` | The abbreviation of the team with the ball |
| `{{DOWN}}` / `{{TO_GO}}` / `{{DOWNS}}` | Down, distance, and both (`3rd & 7`) |
| `{{BALL_ON}}` | The yard line as a number |
| `{{BALL_SIDE}}` / `{{BALL_TEAM}}` | Which side the spot is on |
| `{{BALL_SIDE_SPOT}}` | Both together (`HOME 37`) |
| `{{SPORT}}` | The sport id, upper case |

### Sport Values

| Token | Prints |
| --- | --- |
| `{{SPORT:baseball.balls}}` | Any sport control value |
| `{{SPORT:hockey.powerplay}}` | Flags print their words (`PP`) |
| `{{BALLS}}` / `{{STRIKES}}` / `{{OUTS}}` | Baseball count pieces |
| `{{BASEBALL_COUNT}}` | `2-1 · 1 OUT` |
| `{{BASEBALL_OUTS}}`, `{{HITS}}` | Baseball figures |
| `{{BASES}}` | Which bases are occupied (`1B 3B`) |
| `{{INNING_HALF}}` | `TOP` or `BOTTOM` |
| `{{POWERPLAY}}` | `PP` or blank |
| `{{SHOTS}}`, `{{CORNERS}}`, `{{CARDS}}` | Shared sport figures |

### Player, Stats and Time

| Token | Prints |
| --- | --- |
| `{{PLAYER:HOME:Name}}` | A value from the selected player |
| `{{PLAYER:AWAY:Number}}` | Same, other side |
| `{{PLAYER:HOME:Yards}}` | Any stat column the roster tracks |
| `{{STAT:Turnovers}}` | A custom stat by name |
| `{{DATE}}` / `{{TIME}}` | Today's date, the current time |

Tokens are resolved at draw time, so an element holding `{{HOME_SCORE}}` always shows the current score — including during a value transition, where the old and new values are both known.

---

## Reference: Preferences

| Setting | Default | Notes |
| --- | --- | --- |
| **Theme** | **System** | Follows Windows' light/dark. Pin **Dark** or **Light** to override. Applies to the operator windows only, never to the artwork. |
| **Heading hints** | **Off** | The little explanatory lines next to headings ("scoring, timing, actions"). Turning them on shows them everywhere, including the studio tips. |
| **Accent color** | ScorePlus green | Tab outlines, selected rows, live badges |
| **Secondary accent** | Dark green | Filled actions — Update Output, confirms, on toggles |
| **Value switch delay** | **0 s** | How long a graphic holds an old value before printing the new one. Type any number up to 5 seconds. |
| **Remote** | **Off** | Server, port (8080) and password for the phone control panel |
| **Check for updates** | **On** | Installed copies only |
| **Update feed** | GitHub | Optional override for a self-hosted manifest |
| **Show heading hints** | Off | See above |

Both accents accept a typed hex value or a picker, and **Reset** brings back the ScorePlus green pair. Everything here saves immediately; pressing `Enter` in a value field commits it without closing Preferences.

---

## Reference: File Types

| Extension | Name | Holds |
| --- | --- | --- |
| `.spg` | **ScorePlus Game** | Everything: teams, score, clocks, sport values, custom stats, every display and layout with its transitions and animations, the motion library, what is on air, rosters in use, selected players and their stats |
| `.spl` | **ScorePlus Layout** | One layout, with all of its elements, artwork and transition |
| `.spd` | **ScorePlus Display** | One display and every layout on it, their order, and what is on air — with each layout's transition clip |
| `.spt` | **ScorePlus Transition** | One keyframed hand-over: its exit and enter legs, every property key and every color key |
| `.spa` | **ScorePlus Animation** | One keyframed animation: loop or value-change motion, with its own color keys |
| `.spr` | **ScorePlus Roster** | A team roster: numbers, names, positions and its stat columns — **no stat values** |
| `.spp` | **ScorePlus Player** | One player, same rules — no stat values |
| `.spf` | **ScorePlus Folder** | A folder, everything nested inside it, and its media |
| `.html` | Game report | A printable, self-contained summary of the finished game |

Exporting a layout or display keeps the artwork and settings, including the team palette slots (they re-read the game's colors, so the graphic still matches whoever is playing). A cross-display link is cleared on import, because a single file cannot contain the display it pointed at.

The game report is a complete record: every value the game holds, the sport's own figures, the event timeline, all custom stats, and a table for **every roster with every player and every stat column — zeros included**, so a report never hides the players who didn't touch the ball.

---

## Where Files Live

| What | Location |
| --- | --- |
| Game files | Anywhere you choose — portable `.spg` files |
| Autosaves | `%LOCALAPPDATA%\ScorePlus\autosaves` — the newest eight, listed on the launch screen |
| Preferences | `%LOCALAPPDATA%\ScorePlus\preferences.json` |
| Sport profiles | `%LOCALAPPDATA%\ScorePlus\sports.json` |
| Scorebug displays and layouts | `%LOCALAPPDATA%\ScorePlus\scorebug.json` |
| Rosters and stat columns | `%LOCALAPPDATA%\ScorePlus\rosters.json` |
| Keybinds | `%LOCALAPPDATA%\ScorePlus\keybinds.json` |
| Imported fonts | `%LOCALAPPDATA%\ScorePlus\Fonts` |
| Team logos | `%LOCALAPPDATA%\ScorePlus\TeamLogos` |
| Diagnostics | `%LOCALAPPDATA%\ScorePlus\logs` |
| Program (installed) | `%LOCALAPPDATA%\Programs\ScorePlus` |

Autosaves roll: a new one is written every five seconds, at most one per minute is kept as a recovery point, and anything past the newest eight is deleted automatically — including at startup. **Delete all** in the launch box or Preferences clears them on demand, behind a second click.

If you had an older installation, the files in `%LOCALAPPDATA%\Scoreboard` are copied into `%LOCALAPPDATA%\ScorePlus` on first run. Copied, not moved, so an older build still works.

---

## Building and Releasing

Building needs the **.NET 10 SDK** with the Windows desktop targeting pack.

```sh
./scripts/build-release.sh a1.4
```

This publishes a self-contained single-file build into its own folder:

```
dist/ScorePlus-win-x64-a1.4/
├── ScorePlusPortable.exe   run it straight from the folder
├── ScorePlusSetup.exe      install it
├── README.md               this document, copied in for reference
└── UPDATES.md              how the update feed works
```

Exactly two **executables** ship — there is no bare `ScorePlus.exe` left in the folder to confuse the upload, and the docs beside them are just notes. Each version gets its own folder, so a new build never replaces the one you are still testing; only a rebuild of the same version refreshes its own folder.

To ship an update, use the publish script. It builds, hashes the setup exe, writes the update manifest beside it, and — with `--release` — creates the GitHub release with both assets through the GitHub CLI:

```sh
./scripts/publish-release.sh a1.4 --release --notes "Adds X, fixes Y"
```

Asset names matter: the release must carry **`ScorePlusSetup.exe`** and **`ScorePlusPortable.exe`**. Only `ScorePlusSetup` is used for updating; the portable exe is never self-replacing.

`--prerelease` marks it as an alpha. Drafts are ignored by the updater, because their assets aren't served.

---

## Updates

An installed copy checks for a new version at launch, and it already knows where to look: the app carries this project's GitHub releases feed, so **publishing a release is the whole update**.

The flow:

1. The app reads the newest release's tag and compares it with its own build number.
2. If the tag is newer, it offers the update.
3. On your confirmation it downloads `ScorePlusSetup.exe`, verifies the SHA-256 that GitHub computed for the asset, and runs it. The installer replaces the app in place, and your preferences, rosters, sport profiles, layouts and keybinds are untouched.

Things worth knowing:

- **Only installed copies update.** A build run out of a folder has no install marker, so it never checks and never replaces itself. That is deliberate: your test copy can't be swapped out from under you.
- **The check runs at startup**, plus a manual **Check now** in Preferences → Updates. If a release goes live while the app is open, it won't notice until the next launch.
- **Release numbers must increase.** `a1.3` reads as 1.3.0, and the highest published number wins — not the most recent upload. Watch out for `a1.03`: it parses to the same 1.03 → 1.3.0 as `a1.3`, so it isn't "newer" to an a1.3 install.
- **Prefer a custom host?** Preferences → Updates can point at a JSON manifest elsewhere. See [docs/updates.md](docs/updates.md).

---

## Project Layout and Tests

```
src/Scoreboard.Core       Game state, engine, sport catalog, scorebug model, tokens, files, reports
src/Scoreboard.Desktop    WPF application: console, media, rosters, studio, output, remote
src/ScorePlus.Setup       The installer: payload, shortcuts, registry entry, uninstall
scripts/build-release.sh  Publishes the app and builds the setup exe into dist/
scripts/publish-release.sh  Builds, writes the manifest, publishes a GitHub release
docs/updates.md           How the built-in update feed works, and hosting your own
tests/Scoreboard.Core.Tests  Headless checks: scoring, clocks, stats, profiles, files, reports
tests/UiSmoke                Loads every window and exercises the operator flows
tests/HtmlRender             Proves an HTML element draws a real frame in a real event loop
```

Run them with:

```sh
dotnet run --project tests/Scoreboard.Core.Tests/Scoreboard.Core.Tests.csproj -c Release
dotnet run --project tests/UiSmoke/UiSmoke.csproj -c Release
dotnet run --project tests/HtmlRender/HtmlRender.csproj -c Release
```

`HtmlRender` is separate because the browser engine refuses to start unless the app's own event loop is running; the smoke harness drives the dispatcher itself.

Between them the suites cover scoring, undo, clock behaviour, period formats, sport profiles and overrides, custom stats and tokens, live-versus-staged publishing, multiple layouts and displays, linked and copied parts, element typography and transitions, icon artwork, media tags, volume and speaker routing, camera and replay staging, roster files, layout and display files, media folder files, report output, the remote page, the installer's update path, and the studio's keyboard and canvas behaviour.

---

## Troubleshooting

**The audience screen is black, or a video disappeared.**
Check which display the window is showing and press **Update Output**. A display that has never been published shows nothing. If a *video* box is empty, its tag doesn't match the clip that's cued — check the clip's tag, or set the element to **ANY**.

**The score updates but the audience doesn't.**
**Live: ON** is off. Click it back on. (Down, distance, possession, timeouts and layout changes still need **Update Output** by design.)

**A keybind doesn't fire.**
Keybinds only list the sport you're playing. If the binding belongs to another sport's controls, switch sports or rebind. Also check the console window has focus — a phone remote session doesn't own the keyboard.

**The phone remote won't load.**
It is off until Preferences turns it on. Use the network URL (e.g. `http://192.168.1.5:8080`), not localhost, and allow ScorePlus through Windows Firewall on private networks. Phones must be on the same Wi-Fi.

**Replay shows a black box.**
Replay is staged in the preview until **Start replay**. The audience and the audience view thumbnail keep the live camera until then. If the preview itself is black, the camera may have been cleared or routed away — check **Route…**.

**Video restarts, or the scrub bar drifts.**
Each clip's position is preserved while the studio is edited. If a clip was re-imported or replaced, it starts from the beginning. Seek with the bar rather than waiting through a clip.

**The update prompt never appears.**
Only installed copies check, and the release must be published (not a draft), tagged with a higher number, and carry an asset named `ScorePlusSetup.exe`. Portable builds never update.

**A new game opened an old game.**
It shouldn't — New Game wipes to a blank slate. If you see stale data, check you're on the latest build; the launch box's autosave list is the most common mix-up, since those load a game you chose.

**Something is clipped or the window doesn't fit.**
The console and studio reflow onto portrait monitors and high-DPI scaling. The studio canvas fills its panel and re-fits when the window resizes, so drag space always matches the framed artwork area.

---

## What's Next

ScorePlus is an evolving production tool, not yet a full broadcast package. Still ahead:

- Network and hardware control (not for a bit)
- Multi-game communication
- Full remote control support
- Professional/commercial use

Also worth knowing: video sound plays through the audience output window while the preview and studio stay muted, so you never hear a cue twice — and a video element with its own audio output is only routed when it is on an audience screen, so several preview windows never open the same speaker more than once.

Several live sources can be on air together: each camera and each microphone keeps its own capture, its own level and its own output, so a wide shot, a tight shot and two commentary mics can run at once, each on its own scorebug element (by tag) or its own speaker. Cueing a live item adds it; it does not take another feed down. **Add microphone** creates a mic slot without asking which device — the **Route** button on that item chooses it — so setting up a four-mic desk is four clicks.

---

<sub>ScorePlus · alpha · bugs are expected and reports are welcome.</sub>
