# Model Walk: moved from AJ-Tools Game Mode (2026-11-21)

AJ-Tools' **Game Mode** (`src/GameMode/`, 16 files, 5,369 lines, AJ-Tools `master` `435cd6f`) was rebuilt in
Heron Tools as **Model Walk**, a coordination tool: walk the model, point at things, select them, record snags,
hand over a report. The requirements were [game-mode-audit.md](game-mode-audit.md) (44 findings, GM-A01 to GM-A44)
and [game-mode-spec.md](game-mode-spec.md) (KEEP / CHANGE / DROP / ADD and the names). No code is shared with
AJ-Tools or Heron AI; AJ-Tools was read only, never changed.

**Nothing here has run in a real Revit yet.** It builds clean for Revit 2020-2027; every behaviour below
still **needs a real Revit test**. Stage 4 (polish, the spec's ADD items) is at the end of this file, with a
step-by-step Revit test script. The user guide is [model-walk-guide.md](model-walk-guide.md).

## File map

| AJ-Tools `src/GameMode/` | Heron Tools `src/HeronTools/ModelWalk/` |
|---|---|
| `CmdGameMode.cs` | `CmdModelWalk.cs` (command + `CmdModelWalkAvailability`) |
| `GameSession.cs` (tuning, input, HUD state, session) | `WalkSession.cs` (`WalkTuning`, `WalkInput`, `WalkHudState`, `WalkSession`, `WalkViewpoint`) |
| `GameMotionEngine.cs` | `WalkEngine.cs` (frame, camera, status, clean end) |
| `GameMotionEngine.Movement.cs` | `WalkEngine.Movement.cs` (collision, gravity, stairs, landing checks) |
| `GameMotionEngine.Extras.cs` | `WalkEngine.Actions.cs` (go to point, door, viewpoints, Select / Snag / Hide) |
| `GameCollisionService.cs` | `WalkCollision.cs` (near box, links, ray budget, readout text) |
| snag lines inside the engine | `WalkReport.cs` (report, folder, shared coordinates) |
| - (J wiped every override) | `SnagColourMemory.cs` (remember and give back each element's own override) |
| `GameKeyBindings.cs` | `WalkKeyBindings.cs` |
| `GamePrefs.cs` (professional mode) | dropped |
| `GameHudWindow.xaml` / `.xaml.cs` | `ModelWalkHudWindow.xaml` / `.xaml.cs` (lifetime, Guard, pause, placement) |
| `GameHudWindow.Controls.cs` | `ModelWalkHudWindow.Input.cs` |
| `GameHudWindow.Render.cs` | `ModelWalkHudWindow.Render.cs` |
| `GameHudWindow.Photo.cs` | `ModelWalkHudWindow.Snapshot.cs` (GDI + WPF, no Windows Forms) |
| `GameHudWindow.Weapons.cs` | dropped (gun, pictures, flash, sound, holster) |
| nested `GameNative` | `WalkNative.cs` |
| `GameKeySettingsWindow.xaml` / `.xaml.cs` | `ModelWalkKeysWindow.xaml` / `.xaml.cs` |
| - (stage 4) | `WalkSettings.cs` + `ModelWalkSettingsWindow.xaml` / `.xaml.cs` (Settings), `WalkViewpointStore.cs` (viewpoints per model), `WalkEngine.Rooms.cs` (room in the location line), `ModelWalkHudWindow.Entry.cs` (comment / name box), `ModelWalkHudWindow.Pause.cs` (pause menu) |
| `Resources/GameMode.png`, `GameGun.png`, `GameRifle.png`, `GameSnagGun.png` | `Resources/Icons/ModelWalk16.png`, `ModelWalk32.png` (new, house style, embedded); gun pictures dropped |
| `RibbonManager.BuildGamePanel` | `App/HeronToolsApp.cs`, Coordination panel, button "Model Walk" |

Also changed outside the folder: `Helpers/ElementIdHelper.cs` (see GM-A03), `Helpers/ElementIdExtensions.cs`,
`Helpers/SelectionFilters.cs`, `Helpers/ElementIdIntegerComparer.cs` (comments), `UI/HeronStyles.xaml` (one new
see-through Ink shade, `InputCatcher`, #010F0F10 - Ink at 1/255, for the overlay that catches the mouse).

## Names

| Old | New |
|---|---|
| Game Mode | **Model Walk** (Coordination panel, toggle) |
| "AJ Game View" | **"Heron Model Walk"**; in a workshared model **"Heron Model Walk - <username>"** (one per person). An old "AJ Game View" is left alone. |
| Weapons: laser, selector, snag, cleaner | **Modes: Inspect, Select, Snag, Hide** (right-click or Tab; Shift+Tab back) |
| Ghost | Pass-through |
| Respawn | Back to start |
| Teleport | Go to point (a ring at the crosshair, no arc) |
| Saved positions / Tour | Viewpoints / Play viewpoints |
| Photo | Snapshot |
| Crouch (hold C) | Low eye (C toggles 1000 mm in Walk; C held = down in Fly) |
| Undo names "AJ Tools - ..." | "Model Walk: walk view", "Model Walk: snag N", "Model Walk: clear snag colours", "Model Walk: give back snag colours", "Model Walk: hide" / "show hidden" (only if Revit asks for a transaction) |
| `%AppData%\AJTools\ajgame-keys.txt` | `%AppData%\HeronTools\ModelWalkKeys.txt` (old file not read - AJ-Tools keeps its own) |
| `Documents\AJ Game Snags`, `Pictures\AJ Game Photos` | one folder per walk next to the model, `<model folder>\Model Walk - <model>\<date time>\` (Snags.csv, snag pictures, snapshots); `Documents\Heron Model Walk\<model>\<date time>\` when the model is not saved or the folder cannot be written |
| - | `%AppData%\HeronTools\ModelWalkSettings.txt` (Settings), `%AppData%\HeronTools\ModelWalk Viewpoints\` (viewpoints per model) |

## Dropped (spec DROP list)

Gun and its pictures, muzzle flash, shot sound and M; jump and climbing through windows (Space is "up" in Fly
only); flashlight (V); professional mode (N - always professional now); J reset-all; minimap; section box tool.
No gun / weapon / shoot wording is left anywhere a modeller sees.

## Audit findings: fixed or not

| ID | Sev | What was wrong | Now |
|---|---|---|---|
| GM-A01 | Critical | J wiped every override in the view | **J made safe.** J now means "Clear my snag colours" - it gives back only what Model Walk coloured (owner request, 2026-11-21: same key as AJ-Tools, safe meaning). Reset Graphics stays the tool for a full reset. |
| GM-A02 | High | Snag overwrote the element's own override, never given back | **Fixed.** `SnagColourMemory` copies each element's own override before colouring; given back on J, at the end of the walk (one Undo step) and before every Save / Save As / Sync with Central. An element whose colour the modeller changed after the snag is left as they set it. Stage 4 added the setting "Keep snag colours after the walk" (off). |
| GM-A03 | High | Ids cut to 32-bit; Heron's helper did the same | **Fixed.** ElementIds compared with `==`, printed with `LongValue()`, categories with `ElementIdHelper.IsCategory`. Heron's `ElementIdHelper.GetIntegerValue` now throws instead of truncating; `IntValue()` removed; its two callers (`SelectionFilters`) use full-value helpers - same results for every real id. All 8 years build. |
| GM-A04 | High | Levels by `Level.Elevation` (follows Elevation Base) | **Fixed.** `Level.ProjectElevation` for the location line. The start point no longer comes from a plan's level (Model Walk starts from a 3D view). NEEDS REAL REVIT with a moved project base point. |
| GM-A05 | High | ~19 full-model rays a frame, links on, every frame | **Fixed (measure in Revit).** Collision tests host elements only in a 15 m box round the eye; links only when their box is near (a box filter is checked in the link's own coordinates, per the API note, so it cannot be used on links); readout ray 10 Hz, clear height 4 Hz; no ray while standing still; no `SetOrientation` / redraw when the camera did not change; frames raised only when something can change (4 a second when idle); ray budget 24 a frame - past it a move is refused, not guessed. F12 shows frame ms, average and rays. NEEDS REAL REVIT on a big federated model. |
| GM-A06 | High | No try/catch in WPF handlers | **Fixed.** Timer tick and every key / mouse handler run in `Guard()`: any exception frees the mouse, ends the walk (report written) and closes the overlay; a second failure just closes. The engine frame is guarded as before. |
| GM-A07 | High | Dozens of non-Heron colours | **Fixed.** Every window brush from `HeronStyles.xaml`; one orange thing (active mode chip; Save in Key Settings). Snag colour is Error #FF5A4E as a model colour. Checked by parsing both XAML files against the theme. |
| GM-A08 | High | Overlay `Topmost` over Revit dialogs and other apps | **Fixed.** Not Topmost; owned by Revit's main window. NEEDS REAL REVIT that the overlay still stays over the view. |
| GM-A09 | Medium | Commit results unchecked; "no undo entries" claim false | **Fixed.** Every `Commit()` checked; refused = reported as refused. One Undo step per action (walk view, each snag, clear, give back). Tooltip and notes say which actions are Undo steps. |
| GM-A10 | Medium | U ended the modeller's own Temporary Hide/Isolate; hides stayed after exit | **Fixed.** Hide is refused while the modeller's own Temporary Hide/Isolate is on (it cannot be undone apart). Show hidden (U) ends only Model Walk's own; the end of the walk does the same and says how many came back. Revit's own Reset is noticed. Limit: if the modeller adds their own temporary hide on top of Model Walk's during a pause, U also shows theirs (Revit has no "unhide one"). NEEDS REAL REVIT: whether a transaction is needed (both ways handled, checked). |
| GM-A11 | Medium | Locked view / temporary camera | **Fixed:** locked walk view checked before start and on every camera move, plain message. `SaveOrientation` not used (it locks the view). Carrying on after the model is reopened uses the view's saved camera - NEEDS REAL REVIT. |
| GM-A12 | Medium | Document checked by title | **Fixed.** The `Document` is kept and compared with `Equals`; `IsValidObject` checked. |
| GM-A13 | Medium | Ribbon toggle stuck if `Show()` threw; toggle after view check | **Fixed.** `Active` set only after `Show()` succeeds; a failed show closes everything; the toggle runs first, and the button is enabled from any view while a walk runs. |
| GM-A14 | Medium | Pause pill and minimum size in raw pixels | **Fixed.** Pill measured from its text and scaled by the window's DPI; minimum size DPI-scaled. NEEDS REAL REVIT at 150 / 200 % and on a second monitor. |
| GM-A15 | Medium | Walking under a pipe or table popped you on top | **Fixed.** Snap up only within the 350 mm step; higher = the step is refused. Body rays now at four heights. |
| GM-A16 | Medium | Three thin rays; slid into walls; 300-350 mm stairs blocked | **Fixed.** Two side rays at the body edge on the main move; shin ray just above step height. |
| GM-A17 | Medium | Name taken by a non-perspective view = new unnamed view every run | **Fixed.** First free "name (2)", "(3)"... is used, found again next time, and the walk says the name it made. |
| GM-A18 | Medium | One shared game view for the whole team | **Fixed (owner to confirm).** One walk view per person in workshared models; snag colour refused (recorded only) when the view is owned by someone else. NEEDS REAL REVIT on a central model with two users. |
| GM-A19 | Medium | Pictures loaded from disk | **Fixed.** Gun pictures dropped; the ribbon icon is embedded like every Heron icon. |
| GM-A20 | Medium | System.Drawing / Windows Forms / SoundPlayer | **Fixed.** Snapshot copies the screen with GDI (user32/gdi32) and saves with WPF's PNG encoder; no sound. The csproj is unchanged (WPF only). |
| GM-A21 | Medium | Left in the game view after exit; raw exception text | **Fixed.** Ending the walk (Esc, ribbon, locked view) takes you back to the view you started from (`RequestViewChange`); not when you left the walk view yourself. Messages are plain words with Revit's text as detail. |
| GM-A22 | Medium | Internal coordinates shown and reported | **Fixed.** The HUD shows level and facing, no XYZ. The report gives shared coordinates (E / N / Z, mm) via the active project location, plus unique id and level. NEEDS REAL REVIT that the numbers match Spot Coordinates. |
| GM-A23 | Low | Speed limits disagreed | Fixed: five fixed steps in `WalkTuning` (0.8 / 1.5 / 3.5 / 5.5 / 8 m/s), +/- or the wheel; shown as "Speed 3/5". |
| GM-A24 | Low | Settings showed raw key names | Fixed: Key Settings uses the same labels as the HUD. |
| GM-A25 | Low | Right Shift did nothing | Fixed: left and right Shift / Ctrl / Alt are one key. |
| GM-A26 | Low | Any number / unknown key name accepted on load | Fixed: real, allowed keys only; anything else keeps its standard key and is said once at start. |
| GM-A27 | Low | A moved key left the old action keyless | Fixed: after load, a keyless action gets its standard key if free, else a warning at start. |
| GM-A28 | Low | No reset, no swap | Fixed: "Reset to standard keys"; a key another action has is swapped, and the window says so. |
| GM-A29 | Low | Two loose files in AJTools | Fixed: one file via `AppDataConfigStore`, saved through a temporary file. |
| GM-A30 | Low | Photo name to the second; HUD could be caught | Fixed: milliseconds in the name; capture after a render pass plus 80 ms. NEEDS REAL REVIT that no HUD is caught. |
| GM-A31 | Low | New SoundPlayer per shot | Gone with the sound. |
| GM-A32 | Low | Levels read once | Fixed: refreshed every 4 s with the rays. |
| GM-A33 | Low | "N" was project north | Fixed: says "Facing NE (project)". True north not added. |
| GM-A34 | Low | F in Ghost went to Walk | Fixed: F turns Fly on from any mode. |
| GM-A35 | Low | E door step not checked | Fixed: the far side is checked (floor, head room, body clear); refused with a reason. |
| GM-A36 | Low | Teleport into a ceiling void | Fixed: Go to point lands on the floor below the aimed point and needs head room and body room; refused with a reason. |
| GM-A37 | Low | `Raise()` result ignored | Fixed: Denied / TimedOut for more than 5 s ends the walk with a plain message. |
| GM-A38 | Low | Toggle after view validation | Fixed with GM-A13. |
| GM-A39 | Low | `MM_TO_FEET` rounded | Fixed in Model Walk (`1 / 304.8` from `FEET_TO_MM`). Heron's shared `Constants.MM_TO_FEET` left as is - changing it touches every tool (owner decision). |
| GM-A40 | Low | Frame ms shown to everyone | Fixed: hidden behind F12. |
| GM-A41 | Low | First 3D view type, no template | Kept; now said in the tooltip. |
| GM-A42 | Low | AJ credit lines and names | Fixed: Heron names; no credit lines (About Heron covers it). |
| GM-A43 | Low | Windows never blocked while airborne | Gone with jumping: windows always block. |
| GM-A44 | Low | Mouse speed fixed, no invert | **Fixed in stage 4**: Settings window - mouse sensitivity (25-250 %) and invert look. |

## The 19 must-keep behaviours

All kept: API only in the `ExternalEvent` handler (1); every frame guarded (2); ends by itself on close / other
model / other view / view deleted / window closed (3); no Undo entry for camera moves (4); only what the view shows
collides, links included and resolved to the linked element (5); dt 0.005-0.25 s and fall speed capped (6); landing
checked from the pre-drop eye (7); no-floor hover and the absolute safety net (8); pointer re-centred when the view
rectangle moves, centred before look is armed, 400 px backstop (9); `ZoomToFit` on start, resume and rectangle
change (10); pause on Esc, focus loss, capture loss - keys cleared, mouse freed, pill, resume re-fits (11); report
written once on every end, failure shown (12); swapped key maps load, hints follow keys, Cancel restores (13);
ribbon toggle, one walk at a time (14); families refused, new view crop off / far clip off, existing view reused
untouched (15); linked elements refused by Select / Hide (Snag records them without colour, per spec) (16); readout
with category, family : type, size, system, level, 64-bit ID, link (17); selection kept after the walk (18); all
distances in mm in `WalkTuning` (19).

## Owner decisions (answered before stage 4)

1. Button enabled in **any 3D view** - kept.
2. Snag colours **given back at the end** by default; a setting "Keep snag colours after the walk" (off) - added in
   stage 4, with Clear snag colours still giving them back.
3. **One walk view per person** in workshared models - kept.
4. **J** clears snag colours - kept.
5. Heron's shared `Constants.MM_TO_FEET` (0.00328084) - **not changed** (other tools use it). Follow-up: make it
   exactly `1 / 304.8` for every tool in its own change, with each tool re-tested. Model Walk already uses
   `1 / Constants.FEET_TO_MM`.
6. Removing Game Mode from AJ-Tools - only on Ajmal's typed "yes", after the Revit test.

## Results

| Check | State |
|---|---|
| Build Revit 2020, 2021, 2022, 2023, 2024, 2025, 2026, 2027 (Linux, `-p:EnableWindowsTargeting=true`) | PASS - 0 warnings, 0 errors each |
| Metadata check (rules of `build/check-metadata.ps1`, run in Python - no pwsh here) | PASS - 196 files |
| XAML: both windows well-formed, every resource key in the theme, no raw colours | PASS |
| XAML rendered to a picture | NOT RUN - WPF needs Windows; this container is Linux |
| Every walk behaviour, frame rate on a big model, DPI, two monitors, worksharing, locked view, Save/Sync give-back, Temporary Hide with/without a transaction, shared coordinates in the report, snapshot without HUD | NEEDS REAL REVIT |

## Still needs a real Revit test

Start from a perspective and an orthographic 3D view; walk stairs and ramps (300-350 mm risers); walk under a
1,300 mm pipe (blocked, not climbed); door E with and without room on the far side; Go to point onto a floor, a
ceiling, a duct; Fly / Pass-through; viewpoints and Play viewpoints; Select keeps the selection and the camera does
not whip; Snag colours a host element, records a linked one, X gives colours back, a colour the modeller changes is
kept, Save and Sync give colours back; Hide / U with and without the modeller's own Temporary Hide; Esc, ribbon and
closing the view all end cleanly and write the report; back to the start view; locked view message; frame timing
(F12) on a large federated model; 100 / 150 / 200 % scaling and a second monitor; a Revit dialog appearing during
the walk is not covered by the overlay; Key Settings save, swap, reset, Cancel; each Revit year 2020-2027.

---

# Stage 4: polish - the spec's ADD items (2026-11-21)

Built on branch `claude/game-mode-heron`, one commit per item, each built for Revit 2020-2027 before the next.
**Builds are not Revit tests** - everything below still NEEDS REAL REVIT.

| Item | State | What it is |
|---|---|---|
| a. Settings window | DONE | Pause, S (or the Settings button). Mouse sensitivity (25-250 %), invert look (GM-A44), five walk speeds (m/s), eye height (1000-2000 mm), step-up height (100-450 mm), keep snag colours. Saved per user in `%AppData%\HeronTools\ModelWalkSettings.txt`; mm and m/s in the window, feet only inside (`WalkSettings.*Ft`). A damaged line keeps its standard value and is said once at start. Save applies at once and finds the floor again at the new eye height. Speeds are in m/s, not mm/s - easier to read; distances are mm as asked. |
| b. Snag record | DONE | Snag click: the element is recorded, coloured (one Undo step), a picture taken with the HUD hidden, then a comment box (Enter saves, Esc skips, a click saves). `Snags.csv` (UTF-8 with BOM, Excel-safe quoting, a leading `= + - @` made text) and the pictures in one folder per walk next to the model, or under Documents. Columns: No., Time, Author, Comment, Category, Family : Type, Size, Element ID (64-bit), Unique ID, Level, System, Link, Room, East / North / Elevation (mm, shared; "internal" in the header if the shared coordinates could not be read), Camera, Picture, Snag colour. The CSV is rewritten after each snag, comment and picture (through a temporary file) and once more at the end; the plain-text report is gone. Snapshots (K) go in the same folder. |
| c. Open snag | DONE | The pause menu lists the snags (newest first). A click puts the camera back where the snag was recorded (Fly again if it was recorded flying), resumes the walk and adds the element to Revit's selection (owner's decision: add, never replace). A linked element is said, not selected; a deleted one is said. |
| d. Viewpoints | DONE | B saves a viewpoint and asks for a name (Esc keeps "Viewpoint N - <level - room>"). Kept per model, per user, in `%AppData%\HeronTools\ModelWalk Viewpoints\<model> <hash>.txt` (a workshared model by its central path, so every local copy finds them; an unsaved model by its title). Not in the Revit file. Pause menu: go, Save as 3D view, Delete, Play viewpoints. **Save as 3D view**: one transaction "Model Walk: save viewpoint as 3D view" - `Duplicate` of the walk view (its VG, filters, section box), a unique name "Model Walk - <name>" (characters Revit refuses replaced), the camera set and `SaveOrientation` (does not lock). Snag colours are not carried: every snagged element gets its own overrides in the copy, in the same transaction. Runs in the ExternalEvent while paused. |
| e. Measure | DONE | M at point 1, M at point 2: distance, horizontal and vertical parts in mm, text only - no line on screen. One ray per press; the live "mm to the crosshair" uses the readout's own 10 Hz ray. A finished measure fades after 20 s. |
| f. Room name | DONE | Location line "Level 03  -  Room 3.12 Plant Room"; the snag's Room column (where you stood). `Document.GetRoomAtPoint` 300 mm above the floor, in the walk view's phase; at most 4 times a second and only after a 300 mm move. **Linked rooms: included**, because it is cheap and read-only - only when this model has no room there, only loaded links whose box holds the point (at most 4), in the link's coordinates and its last phase (a link's phases cannot be matched to this view's), marked "(link name)". |
| g. Eased moves | DONE | Walk / Fly / Pass-through speed up and slow down (time constant 0.12 s, by real elapsed time); look stays direct. Same rays per frame; a blocked part of a move is dropped from the speed; every jump starts from standing. Play viewpoints eases its speed and turns smoothly. |
| h. Field of view | LEFT OUT | The API has no field-of-view property; it can only be changed through the perspective crop box. That needs Crop View on in the walk view and a transaction (an Undo step and a model change) per change, and it changes the picture that the ZoomToFit aim fix (must-keep 10) keeps under the crosshair. That cannot be shown safe without Revit, and the spec says not to promise it before a Revit test. |

Finish pass: HUD - location line (level + room), facing, move mode, speed step; mode chips say what a click does;
key hints Help, Measure, Viewpoint, Pause; the help card lists every key in plain words. Pause is now a small menu
(the one orange thing is "Carry on walking"); clicking empty space on it no longer resumes (Enter or the button does),
so scrolling the snag list never throws you back into the walk. Ribbon tooltip: a one-line "how to start". Key
Settings and Settings footers match the other Heron windows (Save, then Cancel). Wording checked: no gun, shoot,
weapon, game, respawn, teleport, ghost or laser anywhere a modeller sees.

## Must-keeps after stage 4

All 19 still hold. What changed around them:

- (1) API only in the ExternalEvent: the new API work - Save as 3D view, Open snag's selection, rooms, Measure's ray,
  Clear snag colours from the pause menu - all runs in `WalkEngine`. The HUD and both windows touch no Revit API.
  A frame raised **while paused** runs only the queued pause-menu actions; it does not move the camera and does not
  end the walk because Revit shows another view (the modeller may be using Revit) - the walk still ends on resume,
  as before, and still ends at once if the model closes or the walk view is deleted.
- (2) Guarded: `PausedFrame` runs inside the same try/catch; every new HUD button runs in `Guard()`.
- (9) While the comment box is open the pointer stays parked on the centre and the view does not turn.
- (11) Esc while typing a comment skips the comment (a second Esc pauses). Pause keeps what was typed.
- (12) The report is still written once at the end with its message, and now also after every change.
- (16) Open snag does not select a linked element.
- (18) Open snag adds the snag's element to the selection; nothing already selected is dropped.
- (19) New distances (eased stop, Play viewpoints, room probe) are in `WalkTuning`; the modeller's own in `WalkSettings`.
- No new transaction outside the ExternalEvent; no int ids (ids are `ElementId` or `LongValue()`); no override lost
  (Save as 3D view gives each snagged element its own overrides in the copy; kept colours are still given back by
  Clear snag colours and before every Save and Sync).
- Performance: no new rays per frame. Measure: one ray per key press. Rooms: no rays, throttled. Easing: the same
  rays per frame, a few more frames while a move eases out.

## Stage 4 results

| Check | State |
|---|---|
| Build Revit 2020, 2021, 2022, 2023, 2024, 2025, 2026, 2027 (Linux, `-p:EnableWindowsTargeting=true`) | PASS - 0 warnings, 0 errors each, after every item |
| Metadata check (rules of `build/check-metadata.ps1`, run in Python - no pwsh here) | PASS - 203 files |
| XAML: all three Model Walk windows well-formed, every resource key in the theme (XAML and code), no raw colours | PASS |
| XAML rendered to a picture | NOT RUN - WPF needs Windows; this container is Linux |
| Every behaviour in the test script below | NEEDS REAL REVIT |

## Owner decisions after stage 4 (answered)

1. **Keep snag colours + Save:** keep the red until the save. With the setting on, the red stays after the walk and
   is given back before every Save and Sync, so no snag colour reaches the file. No change needed.
2. **Open snag adds to the selection** (it no longer replaces it). Changed.
3. **Speeds in m/s** in Settings - kept.

## Still needs a real Revit test (stage 4)

Snag picture shows the element red and no HUD; comment box takes typing (also non-English keyboards and IME);
CSV opens in Excel with the right columns and accents; report folder next to a local file, a central's local copy,
a read-only folder (falls back to Documents) and a cloud model; Open snag camera and selection; viewpoints come back
after closing and reopening the model, and in a second local copy of the same central; Save as 3D view keeps its
camera after reopening, has no snag red, and has the walk view's VG / section box; rooms in host and linked models,
and with phases; eased stop near a wall (no push into it); Settings save and apply (eye height, step-up on 300-450 mm
stairs, invert look); pause menu size at 100 / 150 / 200 % scaling; Keep snag colours on, then Save (colours given
back).

## Revit test script for the owner (about 30 minutes)

Use a copy of a real project with MEP, rooms and at least one link. Revit 2024 first, then 2020 and 2027.

1. **Start.** Open a 3D view, click Model Walk. Expect: the view "Heron Model Walk" opens where your view looked, the
   help card shows on the left. Walk with W A S D, look with the mouse. Let go of W - the walk eases to a stop.
2. **Location.** Walk into a room. Expect the top-left line "Level xx  -  Room nn Name". Walk into a room that is
   only in a linked model - expect "(link name)" after it.
3. **Speed and eye.** Press + and - (or the wheel): "Speed n/5". Press C: low eye. Walk up a stair.
4. **Measure.** Press M on the floor, then M on the duct above. Expect "Measured ... mm" with a vertical part that
   matches Revit's own measure.
5. **Snag.** Right-click to Snag. Click a duct. Expect: it turns red, a moment later a box asks for a comment. Type
   "Clash with tray", Enter. Snag a linked element too (recorded, not red).
6. **Viewpoint.** Press B, type "Plant room", Enter. Walk away, press 1 - you are back.
7. **Pause menu.** Press Esc. Expect a small menu at the top of the view: Carry on walking, Settings, Keys, Clear snag
   colours, Open report folder, End walk, your snags and your viewpoint. Use Revit (change VG) - it works.
8. **Open snag.** Click snag 1 in the list. Expect: the walk resumes looking at the duct, and the duct is selected.
9. **Save as 3D view.** Esc, then Save as 3D view on "Plant room". Expect "Saved the 3D view ...". Check the Project
   Browser: "Model Walk - Plant room" - open it later: same camera, no red duct, one Undo step.
10. **Settings.** Esc, S. Set sensitivity 50 %, tick invert look, eye height 1600, Save. Carry on: turning is slower,
    mouse forward looks down, the eye is lower. Set them back.
11. **Report.** Esc, Open report folder. Expect `Snags.csv`, `Snag 001.png`, `Snag 002.png`. Open the CSV in Excel:
    comment, IDs, room, East/North/Elevation match Spot Coordinates at the duct, picture names.
12. **End.** Esc, End walk. Expect: back in your 3D view, the duct is its own colour again, the end message names the
    report. The Undo list shows "Model Walk: ..." steps only for the view, the snags and the colours.
13. **Keep colours.** Start again, Settings, tick Keep snag colours, snag something, end the walk: still red. Save the
    model: the red goes before saving.
14. **Viewpoints kept.** Close and reopen the model, start Model Walk, Esc: "Plant room" is still in the list.

Report each step as PASS / FAIL (with what you saw) and the Revit year.

---

# Final review (2026-11-21)

A fresh read of the whole branch (every file in `src/HeronTools/ModelWalk/`, the helper changes, the ribbon entry,
the theme key) against `AGENTS.md`, `CONTRIBUTING.md` and the Revit API notes of the `Nice3point.Revit.Api`
packages (2020, 2021). The docs were read after the code and checked against it.

## Findings

| # | Severity | Finding | State |
|---|---|---|---|
| R1 | Medium | `WalkEngine.EndWithoutRevit` - run on the overlay's own events (overlay closed, Revit busy for 3 s, an overlay fault, Revit's main window closing) - called `Document.IsValidObject`, `GetElement` and `SnagColourMemory.Find` outside Revit's API context. The notes said the HUD touches no Revit API; this path did. | **Fixed** (`949ff3f`). It now reads only the colour memory's plain .NET count, found on Revit's thread (first frame, each snag colour). |
| R2 | Medium | The snag colour memory was dropped on `DocumentClosing`. A close can still be cancelled after that event (the save question, another add-in): the model stays open with the red on and no way back, and the next Save writes the red into the file. Most likely with "Keep snag colours" on. | **Fixed** (`8c69db2`). Dropped only on `DocumentClosed` with status Succeeded, matched by `DocumentId`. |
| R3 | Low | The start-up warning for an action left without a key said "Pause (Esc) and press S" - S opens Settings; Keys is K. | **Fixed** (`124d24f`). |
| R4 | Low | A snapshot / snag picture asked for before the view's place on screen is known was refused as "a picture is already being taken". | **Fixed** (`f20953d`). |
| R5 | Low (doc) | The guide said Hide changes nothing and left Hide / Show hidden out of the Undo list; when Revit asks for a transaction for Temporary Hide they are Undo steps. | **Fixed** in `model-walk-guide.md`. |
| R6 | Low | `Snags.csv` uses commas. In a Windows region whose list separator is `;` (decimal-comma countries) Excel opens it in one column. Numbers in the file are culture-free (invariant, whole mm), so nothing is misread - only the columns. | **Not changed** - owner decision. A `sep=,` first line is the usual fix, but how Excel treats it together with the UTF-8 mark that keeps accents right needs a test on a real Excel, not a guess. Workaround: Excel, Data, From Text/CSV, comma. |
| R7 | Low | Revit's own Undo of "Model Walk: give back snag colours" after the walk brings the red back with nothing left to give it back again. | **Not changed** - Revit's Undo raises no event an add-in can safely act on. Same limit as Quick Clash. Told here so the owner knows. |
| R8 | Nit | `BeginEndSequence` frees the mouse before it marks the walk as ending, so the lost-capture handler runs the pause code inside the same call. Nothing shows: the end card replaces it before WPF draws. | Not changed - no visible effect, and the order is part of the tested-on-paper end sequence. |
| R9 | Nit | `ElementIdHelper` has a doubled `<summary>` above `FromLong` / `FromInt`. | Not changed - it is on `main` already, not part of this change. |

## Checked and found right

- **Revit API only on Revit's thread.** Every transaction is in the command (walk view, give back leftovers) or the
  `ExternalEvent` handler (snag, clear, give back, save as 3D view, temporary hide) or Revit's own Save / Sync
  events (allowed to change the model, per the API note). Each is `using`, started in `try`, rolled back on a Revit
  exception, and its `Commit()` checked. No transaction is left open on any path.
- **Camera.** `View3D.SetOrientation` is a temporary orientation "not saved in the document" (API note) - no
  transaction, no Undo entry, as the notes claim.
- **Overrides, hide, selection.** Each element's own override is copied before the red; given back at the end, on X,
  and before Save / Save As / Sync; an element the modeller recoloured is left alone. Hide refuses to mix with the
  modeller's own Temporary Hide/Isolate. The selection is changed only by Select mode and Open snag, as documented.
- **Element ids.** No `int` id anywhere in Model Walk. The `ElementIdHelper` change: `GetIntegerValue` now has no
  caller outside the helper; `IntValue()` had only the two `SelectionFilters` callers, which now use
  `TryGetBuiltInCategory` / `IsCategory` - the same answer for every real id in 2020-2023, and no wrong match from a
  large id in 2024-2027.
- **Mouse, timer, overlay.** Every handler runs in `Guard()`; every end path frees capture, shows the pointer, stops
  the timer and disposes the `ExternalEvent`. Not Topmost, owned by Revit's main window, and focus loss pauses.
- **Per frame.** No file write in a frame; rays counted with a budget; no `SetOrientation` / redraw while the camera
  is still; idle frames 4 a second.
- **Files.** Model names cleaned of characters Windows refuses; unsaved and cloud models and read-only or too-long
  folders fall back to Documents; CSV quoting handles commas, quotes and new lines in comments, and a leading
  `= + - @` is made text; numbers invariant.
- **UI.** Only `HeronStyles.xaml` keys (every key used in XAML and code exists); one orange button per window; no
  gun, shoot, weapon or game wording a modeller sees.

## Final review results

| Check | State |
|---|---|
| Build Revit 2020, 2021, 2022, 2023, 2024, 2025, 2026, 2027 (`dotnet build ... -p:EnableWindowsTargeting=true`, Linux) | PASS - 0 warnings, 0 errors each |
| Metadata check (rules of `build/check-metadata.ps1`, run in Python - no pwsh here) | PASS - 203 files |
| XAML resource keys (XAML and `FindResource` / `SetResourceReference` in code) all in the theme | PASS |
| XAML rendered to a picture | NOT RUN - WPF needs Windows |
| R1 and R2 in Revit (close the overlay mid-walk; close the model with kept colours and press Cancel, then Save - the red must go) | NEEDS REAL REVIT - add to the test script below |
| Everything in the owner's test script | NEEDS REAL REVIT |

Add to the owner's test script: **15. Cancelled close.** Settings, tick Keep snag colours, snag something, end the
walk. Close the model, press **Cancel** at Revit's question. Save. Expect: the red goes before saving.
