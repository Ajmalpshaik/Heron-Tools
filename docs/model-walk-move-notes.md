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
| `GameCharacterServer.cs`, `GameMotionEngine.ThirdPerson.cs` (AJ-Tools v1.60.0, `a5eb30b`, added after the first move; v1.61.0, `ab98e2e`) | `WalkCharacterServer.cs` (the character, DirectContext3D, switch on / off), `WalkEngine.ThirdPerson.cs` (camera, place, speed) - see "Third-person view" and "The real character" at the end |
| `GameCharacterModel.cs`, `GameCharacterAnimator.cs`, `Resources/GameCharacter.gltf` (AJ-Tools v1.61.0, `ab98e2e`) | `WalkCharacterModel.cs` + `WalkJson.cs` (reader), `WalkCharacterAnimator.cs`, `Resources/ModelWalk/WalkCharacter.gltf` (embedded in the DLL) |
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
| GM-A02 | High | Snag overwrote the element's own override, never given back | **Fixed.** `SnagColourMemory` copies each element's own override before colouring; given back on J, at the end of the walk (one Undo step) and before every Save / Save As / Sync with Central. An element whose colour the modeller changed after the snag is left as they set it. Stage 4 added the setting "Keep snag colours after the walk"; on by default since 2026-11-21 (owner). |
| GM-A03 | High | Ids cut to 32-bit; Heron's helper did the same | **Fixed.** ElementIds compared with `==`, printed with `LongValue()`, categories with `ElementIdHelper.IsCategory`. Heron's `ElementIdHelper.GetIntegerValue` now throws instead of truncating; `IntValue()` removed; its two callers (`SelectionFilters`) use full-value helpers - same results for every real id. All 8 years build. |
| GM-A04 | High | Levels by `Level.Elevation` (follows Elevation Base) | **Fixed.** `Level.ProjectElevation` for the location line. The start point no longer comes from a plan's level (Model Walk starts from a 3D view). NEEDS REAL REVIT with a moved project base point. |
| GM-A05 | High | ~19 full-model rays a frame, links on, every frame | **Fixed (measure in Revit).** Collision tests host elements only in a 15 m box round the eye; links only when their box is near (a box filter is checked in the link's own coordinates, per the API note, so it cannot be used on links); readout ray 10 Hz, clear height 4 Hz; no ray while standing still; no `SetOrientation` / redraw when the camera did not change; frames raised only when something can change (4 a second when idle); a move budget of 24 rays a frame (2 kept for the floor under you) - past it a move is refused, not guessed; the readout, door hint and landing checks come on top, so F12 can show about 30-40 on a busy frame (#82). F12 shows frame ms, average and rays. NEEDS REAL REVIT on a big federated model. |
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
| GM-A39 | Low | `MM_TO_FEET` rounded | Fixed in Model Walk (`1 / 304.8` from `FEET_TO_MM`). Heron's shared `Constants.MM_TO_FEET` made exact too, in its own change (issue #40, `21e571c`). View Crop is the only tool whose numbers moved (by less than 0.001 mm); Quick Clash and 3D Snapshot use it too, but already worked with 1 / 304.8. |
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
2. Snag colours: a setting "Keep snag colours after the walk", added in stage 4. **On by default** (Ajmal,
   2026-11-21: "Keep snag colours after walk"); Clear snag colours, Save and Sync still give them back.
3. **One walk view per person** in workshared models - kept.
4. **J** clears snag colours - kept.
5. Heron's shared `Constants.MM_TO_FEET` - made exactly `1 / 304.8` in its own change (issue #40). View Crop is the
   only tool whose numbers moved: its margins and offsets by less than 0.001 mm. View Crop still needs a Revit look.
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
not whip; Snag colours a host element, records a linked one, J gives colours back, a colour the modeller changes is
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
12. **End.** Esc, End walk. Expect: back in your 3D view, the duct still red (Keep snag colours is on as standard), the end message names the
    report. The Undo list shows "Model Walk: ..." steps only for the view, the snags and the colours.
13. **Keep colours.** Keep snag colours is on as standard: snag something, end the walk: still red. Save the
    model: the red goes before saving. Untick it in Settings, snag, end the walk: the red goes at once.
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
- **Overrides, hide, selection.** Each element's own override is copied before the red; given back at the end, on J,
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

Added to the owner's test script:

15. **Cancelled close.** With Keep snag colours on (standard), snag something, end the walk. Close the model, press
    **Cancel** at Revit's question. Save. Expect: the red goes before saving.

## Second review round (2026-11-21, after the owner's answers)

Owner answers: **Keep snag colours on by default** (the red stays after the walk; J, Save and Sync still give the
elements their own colours back) and **any 3D view** - `9c7a546`. `main` merged in (`b9d7d12`, corrected spec from #43).

Seven more defects found by the review session; all checked against the code; all still **NEED REAL REVIT**.

| # | Problem | Fix |
|---|---|---|
| 1 | Holding Esc paused, then its key repeat ended the walk; a held Enter / S / K repeated too. | Key repeats ignored while paused and on the end card (`8a4b0c4`). |
| 2 | Alt+F4 on the overlay closed it without the clean-up: what the walk hid stayed hidden, the start view was not restored. | A close request while walking ends the walk the normal way first, then the end sequence closes the overlay. Revit shutting down destroys the owned window without a close request, so it is never held up (`0b44fa6`). |
| 3 | Key Settings: a failed save still counted as saved, so Cancel kept the unsaved keys. | Marked saved only when the save worked (`11688b8`). |
| 4 | The viewpoint file name started with the title, which differs per local copy of a central - a second local copy found nothing. | The readable part comes from the same key as the hash (the central's name) (`ce43cf4`). |
| 5 | An element still red from an earlier walk (kept colours) or after Ctrl+Z of a snag said "Already a snag" and got no report row. | Only a second snag of the same element in THIS walk is refused; a refused colour no longer drops an original remembered earlier (`723522c`). |
| 6 | Save as 3D view gave the copy every remembered original, even where the modeller had recoloured the element since. | Same test as the put-back: only an element still showing the snag colour gets its original (`b569516`). |
| 7 | The guide said U shows "only what this walk hid"; Revit can only end the whole Temporary Hide/Isolate. A reset plus a hide of the modeller's own during a pause was taken for the walk's. | Guide says plainly what U ends. A reset-then-own-hide is now noticed (an element the walk hid is visible again) and left alone; an own isolate that hides all of the walk's cannot be told apart - documented (`6606f62`). |

Issues opened by the review session for later work: #40, #41, #42.

Added to the owner's test script:

16. **Held Esc.** Walk, hold Esc for two seconds: paused, not ended.
17. **Alt+F4.** Hide something, Alt+F4 on the walk: the walk ends, the element shows again, back in your 3D view.
18. **Kept red, snag again.** With colours kept from an earlier walk, snag the same element: a new report row, still
    red.

## Revit test results (issue #42)

Fill in after the owner's Revit test: PASS / FAIL, and what was seen for a FAIL. When every row passes, the Model Walk
files go from `Heron-Status: DRAFT` to `PROVEN`. Only after that, and on the owner's typed "yes", is Game Mode removed
from AJ-Tools.

| Step | Revit 2024 | Revit 2020 | Revit 2027 | What was seen |
|---|---|---|---|---|
| 1-14 (owner's test script) | | | | |
| 15 Cancelled close | | | | |
| 16 Held Esc | | | | |
| 17 Alt+F4 | | | | |
| 18 Kept red, snag again | | | | |
| 19 Snags.csv on a ";" Windows region opens in columns (issue #41) | | | | |
| 20 View Crop margin and annotation offset look the same as before (issue #40) | | | | |
| 21 Big model speed (GM-A05) | | | | |
| 22 Levels with survey elevation (GM-A04) | | | | |
| 23 Revit dialogs stay on top (GM-A08) | | | | |
| 24 Screen scaling and two monitors (GM-A14) | | | | |
| 25 Workshared, two users (GM-A18) | | | | |
| 26 Locked view and reopen (GM-A11) | | | | |
| 27 Shared coordinates (GM-A22) | | | | |
| 28 Snapshot is clean (GM-A30) | | | | |
| 29 J clears only snag colours | | | | |
| 30 Quick snags | | | | |
| 31 Comments in the pause menu | | | | |
| 32 Change and remove a comment | | | | |
| 33 Ask for a comment at each snag (on) | | | | |
| 34 Third person on / off | | | | |
| 35 Camera slides in at a wall | | | | |
| 36 Clicks land on the crosshair | | | | |
| 37 Character gone on every end | | | | |
| 38 Model closed while paused in third person | | | | |
| 39 Pictures, viewpoints, Save as 3D view | | | | |
| 40 Speed in third person (F12) | | | | |
| 41 Next walk starts where you stood (after third person) | | | | |
| 42 Camera under a low duct, looking down | | | | |
| 43 The real character: stand, walk, run | | | | |
| 44 Feet at speed, and turning | | | | |
| 45 Standing still costs no redraws | | | | |
| 46 Big model speed with the character (F12) | | | | |
| 47 Colours, walls and pictures with the character | | | | |
| 48 Long walk, many redraws (buffer reuse) | | | | |
| 49 Character starts standing | | | | |
| 50 Keys left without a key | | | | |
| 51 First step is smooth | | | | |
| 52 Model closed while walking or paused | | | | |
| 53 Hide the floor | | | | |
| 54 Door while looking down | | | | |
| 55 Odd user name | | | | |
| 56 Key Settings (Cancel, Save, labels) | | | | |
| 57 Save messages while paused | | | | |
| 58 Esc right after a snag keeps its picture | | | | |
| 59 Snag click during a Snapshot (K) | | | | |
| 60 No pause / end card in a picture | | | | |
| 61 Busy end in third person | | | | |
| 62 Click while turning (third person) | | | | |
| 67 Slide past a door frame near a link | | | | |
| 68 Walk view redrawn after switching view (tiled) | | | | |
| 63 Viewpoint named "#..." kept | | | | |
| 64 Third-person camera after Pass-through / Hide | | | | |

## Extra Revit checks (steps 19-29)

Steps 19 and 20 check issues #41 and #40; steps 21-29 the audit fixes that only Revit can prove. Do them after steps
1-18. Write PASS / FAIL next to each, with the Revit year. **Every numbered step (1 to 64 now) has one row in the results table
above**; steps 30 and later are in the sections that added them, below.

19. **Snags.csv on a ";" Windows region (issue #41).** In Windows Settings, Region, pick a format whose list
    separator is ";" (for example German), restart Revit. Walk, snag two elements (one with a comment holding a comma),
    end. Open `Snags.csv` by double-click in Excel. Expect one value per column, the comment whole in its column,
    accents right. Set the region back.
20. **View Crop unchanged (issue #40).** On a plan with a crop region, run View Crop with a margin (for example 300 mm)
    and an annotation offset, as before the `MM_TO_FEET` change. Expect the crop and annotation crop in the same place
    as before (the change is under 0.001 mm).

21. **Big model speed (GM-A05).** Open the largest federated model you have (MEP + links). Start Model Walk, press F12,
    walk for one minute. Expect the frame time to stay under about 100 ms and Revit to stay usable. Write down the
    average ms and the rays count shown.
22. **Levels with survey elevation (GM-A04).** In a project where the levels' Elevation Base is Survey Point, or the
    project base point is moved, walk onto a floor. Expect the location line to name the right level.
23. **Revit dialogs stay on top (GM-A08).** During a walk, press Esc, then open Visibility/Graphics (VV). Expect the
    dialog to show above the walk and to be clickable. Close it; carry on walking - the walk still covers the view.
24. **Screen scaling and two monitors (GM-A14).** Set Windows display scaling to 150 %, then 200 %. Pause the walk:
    the pause menu is the right size and sharp. Move Revit to a second monitor and repeat.
25. **Workshared, two users (GM-A18).** On a central model, two people start Model Walk. Expect one walk view each
    ("Heron Model Walk - <user>"). If one tries to snag in a view the other owns, the snag is recorded but not
    coloured, with a plain message.
26. **Locked view and reopen (GM-A11).** Lock the walk view (Save Orientation and Lock View), start Model Walk.
    Expect a plain message, no error box. Unlock, walk somewhere, end, close and reopen the model, start again:
    note where the camera starts.
27. **Shared coordinates (GM-A22).** Snag a duct end. Place a Spot Coordinate on the same point. Expect E / N / Z in
    `Snags.csv` to match the spot coordinate (mm).
28. **Snapshot is clean (GM-A30).** Take a snag and a Snapshot (K). Open both pictures: no HUD text, crosshair or
    menu in them.
29. **J clears only snag colours.** Give a pipe your own colour override first. Snag two other elements, then
    change one of them by hand in Override Graphics in View. Press J. Expect: the one you changed keeps your change,
    the other snag gets its own colours back, and the pipe you coloured first is untouched.

---

# Quick snags and third-person view (2026-11-21)

Two owner requests after the merge. Nothing here has run in Revit yet: builds only.

## Quick snags

The owner wants to click snag after snag without a box popping up, and write the comments later.

| What | How |
|---|---|
| Setting | **Ask for a comment at each snag** (Settings, Snags card), `AskSnagComment` in `ModelWalkSettings.txt`, **off** by default. On = the first behaviour (comment box after each snag's picture). A file without the line reads as off. |
| Snag click, setting off | Same record, colour (one Undo step) and picture (HUD hidden) as before; the engine's toast ends "- add a comment in the pause menu (Esc)"; the HUD opens no box, takes no focus, the walk carries on at once. |
| Two snags in a row | One picture at a time. A Snag click while the last snag's picture is still waiting or being taken (about 0.1 s) is refused with "One moment - the last snag's picture is being taken. Click again." - before, the second number overwrote the first in `SnagAwaitingPicture` and the first snag lost its picture. The check runs in the HUD before the click is queued; the HUD and the ExternalEvent share Revit's UI thread, so there is no race. |
| Pause menu snag list | Each row: number, category, family : type, and the comment - or **No comment yet** in `AccentWarning` (Warning = needs attention). The header says how many have no comment yet. Click a row = one comment box under the list (title "Snag n - ..."). **Open** (right of each row) = the old Open snag: camera back, element added to Revit's selection. |
| Saving | Enter or **Save comment** writes the comment, rewrites `Snags.csv` through the temp file (same `WalkReport.Save`, so the Windows list separator from #41 is kept) and moves to the next snag (newest first) with no comment. An emptied box clears the comment. Carry on walking, Open, End walk and the end sequence save what was typed (`HidePauseMenu`). Esc drops it. |
| Keys while typing | While the comment box has the keys, the paused key handler gives them to the box first: Enter saves, Esc closes the box only, every other key types - S and K do not open Settings / Keys, repeats are allowed (Backspace held). Key-up is ignored for the box too. Once the box is closed the old rules come back: repeats ignored while paused (held-Esc fix), Esc ends the walk, Enter carries on. |
| Help, ribbon tooltip, Snag mode message | Say "click snag after snag, write the comments in the pause menu" when the setting is off. The ribbon tooltip also no longer says the colours come back when the walk ends (they stay by default since the owner's decision; J, Save and Sync give them back). |

**Owner decision:** "Esc ... closes the box first, then resumes" was read as: the first Esc closes the comment box,
the next Esc does what Esc does in the pause menu today (ends the walk). If the owner wants the second Esc to carry on
walking instead, that is a one-line change in `OnWalkKeyDown`.

### Revit test steps (quick snags)

30. **Quick snags.** Settings: "Ask for a comment at each snag" is off. Snag mode, click five elements quickly. Expect
    five red elements, five messages "Snag n recorded ... - add a comment in the pause menu (Esc)", no box, the walk
    never stops. Click twice very fast: the second may say "One moment"; no snag may be left without a picture.
31. **Comments in the pause menu.** Esc. Expect five rows, each "No comment yet", "5 with no comment yet". Click the
    newest, type "Clash with tray" (try S, K, W in the text), Enter: saved, the box moves to the next one. Esc closes the
    box; a second Esc ends the walk. Open the report folder: `Snags.csv` has the comment, 5 pictures.
32. **Change and remove.** Pause, click a snag with a comment, change it, click Carry on walking: kept. Pause again,
    empty another one, Enter: "comment removed", the row says "No comment yet" again.
33. **Old way still there.** Tick "Ask for a comment at each snag", Save. Snag: the comment box opens as before.

## Third-person view

Ported from AJ-Tools Game Mode v1.60.0 (`a5eb30b`: `GameCharacterServer.cs`, `GameMotionEngine.ThirdPerson.cs` and
the diffs in the HUD, keys and session). AJ-Tools was read only, never changed.

| What | How |
|---|---|
| Switch | Key **Q** (new remappable action `ThirdPerson`, "First / third person (show a character)"; Q was free) and the setting **Third-person view (show a character)** (Settings, Look card; `ThirdPerson` in `ModelWalkSettings.txt`, **off** by default) - the view each walk starts in. Saving Settings with the box changed switches the running walk too. The status line ends "First person" / "Third person"; the help card lists the key. |
| Camera | 3000 mm behind and 500 mm right of a pivot 1900 mm above the feet (along the full look direction, so looking down lifts the camera). **One** cast (`CastNear`, host + near links - up to 2 rays, the near box round the eye) from the pivot to the camera; a hit pulls the camera in to 250 mm before it, never closer than 300 mm - unless the surface is closer than 550 mm, then half way to it (always in front of it; stage 6 review). Cast only when the pivot or direction changed and both rays fit in the 24-ray budget; otherwise the camera stays where it was for that frame (on the pivot if there is none yet) - never an old distance from a new place (camera session, C4). No ray in Pass-through. |
| Character | Was: about 17 shaded boxes in the Heron colours. **Replaced** by the real character - see "The real character (2026-11-21, stage 8)" at the end. Drawn only in the walk view of the walk's model (`ElementId` **and** `Document` matched - ids repeat across models). No transaction, no element, no Undo step. |
| Player vs camera | Player: collision, gravity, step-up, door hint and E, floor and clear height, room, viewpoints, the snag's camera / "where you stood", Open snag. Camera: the crosshair ray - readout, Select, Snag, Hide, Measure, Go to point - so clicks land on the crosshair; distances shown (readout, "Moved ... mm") are from the player. Clicks use the previous frame's camera **and the look it was sent with**, which is the picture the modeller aimed at (camera session). |
| Rays | Third person adds at most **1** cast (2 rays with near links) a frame (the wall slide), only while the camera moves; the readout ray just starts at the camera. Measure with F12 on a big model (step 40). |
| Redraws | The camera moving already redraws. With no camera move, the view is refreshed only when the character appears or goes, or while it walks, runs or blends on the spot (frames are asked for until it stands). |
| Snag pictures | The character is hidden from the snag click until its picture is saved (`SnagAwaitingPicture`, then `SnagPictureBusy` from the HUD) - snag pictures show the model only. **Snapshots (K) show the character** (they are what you see); Q first for one without it. |
| Viewpoints, Save as 3D view | Save the player's eye and look, never the third-person camera offset. "Save as 3D view" therefore looks from the player's eyes. When a walk ends in third person on Revit's thread, `End` puts the walk view's camera back on the player's eye (stage 6 review: the next walk starts from that camera). When the walk ends without Revit's thread (Revit busy, overlay fault) the view cannot be touched; the next walk in that model starts from the player instead, if the camera was not moved since (camera session). |

### Switching the character off - what Revit allows

The Revit API notes for `ExternalService.RemoveServer` (identical in the 2020, 2021, 2022, 2023, 2024, 2025, 2026 and
2027 `Nice3point.Revit.Api` XML) say a server that **has drawn in a model that is still open cannot be removed**
(`ArgumentException`). So the character is never left drawing, but the server object can stay registered, inactive,
until the model closes. AJ-Tools leaves its server registered and active for the whole Revit session; Model Walk does
this instead:

| When | What happens |
|---|---|
| Walk starts | `WalkCharacterServer.Prepare` (inside the command): makes the switch-off `ExternalEvent` and watches models closing, once per Revit session. Nothing is registered yet. |
| First third-person frame | `SwitchOn` (inside the frame `ExternalEvent`): `AddServer` the first time, then put on the active list. A refusal turns third person off with a plain message. |
| Every walk end on Revit's thread - End walk, Esc Esc, the ribbon button, model closed, view switched, walk view deleted, locked view, a fault in the frame | `WalkEngine.End` calls `SwitchOff` **first**: hidden (`CanExecute` false), off the active list, `RemoveServer` tried; the view is refreshed so the character goes at once. |
| Alt+F4 / overlay closed, Revit busy for 3 s, an overlay fault (`Guard`) | `EndWithoutRevit` - no Revit API there: `SwitchOffSoon` hides the character at once (plain .NET flag) and raises the switch-off event, which runs `SwitchOff` and refreshes the view on Revit's next idle moment (skipped if a new walk switched it on meanwhile). Alt+F4 normally goes the Revit-thread way first (it ends the walk the normal way). |
| A model closing | `DocumentClosing`: if it is the walk's model, `SwitchOff` (also covers a close while paused). `DocumentClosed`: `RemoveServer` tried again when no walk uses it - this is when Revit lets it go. |
| Revit closing | `HeronToolsApp.OnShutdown` calls `SwitchOff`. |

No transaction anywhere for the character. Every member used (`ExternalServiceRegistry.GetService`,
`MultiServerService.AddServer / RemoveServer / IsRegisteredServerId / GetActiveServerIds / SetActiveServers`, the
`IDirectContext3DServer` members, `DrawContext.IsAvailable / FlushBuffer`, `VertexBuffer`, `IndexBuffer`,
`EffectInstance`) has the same signature in all eight years' API notes, so there is no version-specific code.

### Results (both changes)

| Check | State |
|---|---|
| Build Revit 2020, 2021, 2022, 2023, 2024, 2025, 2026, 2027 (`dotnet build ... -p:EnableWindowsTargeting=true`, Linux) | PASS - 0 warnings, 0 errors each, after each change |
| Metadata check (rules of `build/check-metadata.ps1`, run in Python - no pwsh here) | PASS - 211 files |
| XAML: HUD and Settings well-formed, every resource key (XAML, `SetResourceReference`) in the theme, no raw colours | PASS |
| XAML rendered to a picture | NOT RUN - WPF needs Windows |
| The figure (now the character) draws, hides behind walls; the camera slides; switch-off on every end path | NEEDS REAL REVIT |
| Quick snags, the pause menu comment box and its keys | NEEDS REAL REVIT |

**Owner decisions:** (1) the second Esc after closing the comment box ends the walk (as Esc in the pause menu does
today) - say if it should carry on walking instead; (2) the figure's colours - superseded: the blocky figure is gone, the character keeps its
own colours (stage 8); (3) snapshots (K) show the character - say if they should hide it like snag
pictures.

### Revit test steps (third person)

34. **Third person on / off.** Start a walk, press Q. Expect: the camera jumps back about 3 m and a little right, a
    site worker stands where you were, the status line says "Third person". Walk: he walks; stop: he stands. Shift: he
    runs. Q again: "First person", he is gone. Tick the setting, start a new
    walk: it starts in third person.
35. **Camera slides in at a wall.** In third person, walk backwards towards a wall (look away from it). Expect the
    camera to come closer instead of looking through the wall, and to go back out as you step away. Walk behind a
    column: the character is hidden by it.
36. **Clicks land on the crosshair.** In third person: Inspect a duct (the readout names what is under the crosshair,
    the distance is from the character), Select it, Snag another, Hide a third, Measure two points, Go to point on a floor.
    Each acts on what is under the crosshair, not on something in front of the character.
37. **Character gone on every end.** In third person, end the walk with: End walk; Esc Esc; the ribbon button; Alt+F4 on
    the overlay; switching to another view. Each time open the walk view afterwards: no character. Then save and reopen the
    model: no character, nothing in the model, no "Model Walk" Undo step for it.
38. **Model closed while paused.** In third person press Esc, then close the model (Save: No). Expect no error; open
    another model, start Model Walk in it: no leftover character in the new model. Close Revit with a walk paused in third
    person: Revit closes normally.
39. **Pictures, viewpoints, Save as 3D view.** In third person: a snag's picture has no character; a Snapshot (K) has him.
    Press B, walk away, press 1: you stand where you saved, camera behind you again. Save that viewpoint as a 3D view:
    it looks from your eyes, no character in it.
40. **Speed in third person (F12).** On the big model of step 21, press F12, walk for a minute in first person, then in
    third person. Write down both averages and the rays count: third person may add 1 ray a frame, the frame time should
    stay close to first person.

## Stage 6 review (2026-11-21)

A fresh read of the whole change (`git diff origin/main...HEAD`), the files it touches, and the Revit API notes in
`Nice3point.Revit.Api` 2020-2027 for every graphics-service member used. Builds only - nothing here has run in Revit.

| # | Severity | Finding | State |
|---|---|---|---|
| R1 | Important | A walk ended in third person left the walk view's camera 3 m behind and 0.5 m right of the player. `CmdModelWalk` starts the next walk from the walk view's camera, so that walk began 3 m back - possibly in the next room, inside a wall or outside the building. | **Fixed** - `WalkEngine.End` puts the walk view's camera back on the player's eye (same no-transaction camera move as every frame). An end without Revit's thread (Revit busy 3 s, overlay fault) cannot touch the view - fixed later at the next walk's start (camera session, C1). |
| R2 | Medium | Wall slide: with a surface closer than 550 mm along the camera ray (a duct or slab just above the head while looking down), the camera was still put 300 mm out - past the surface, so the view looked from inside or behind it. | **Fixed** - the camera stops half way to such a surface, always in front of it. |
| R3 | Minor | `CastNear` is up to 2 rays (model + near links) but the slide only checked "under 24", so a frame could reach 25 rays. | **Fixed** - cast only when both rays fit in the budget. (The 24 is the move budget, not a hard frame cap - #82.) |
| R4 | Checked, no defect | `RemoveServer` / `AddServer` / `SetActiveServers`: identical in all eight years' XML; `RemoveServer` throws `ArgumentException` for a server used in an open model and `InvalidOperationException` while the service draws - both caught. Every end path (End walk, Esc Esc, ribbon, Alt+F4, view switch, model switch, model closing, `Guard` fault, Revit busy, Revit closing) reaches `SwitchOff` or `SwitchOffSoon`; a second walk re-uses the registered server and cancels a pending switch-off. `CanExecute` matches view id and document; `GetBoundingBox` and `RenderScene` cannot throw. No transaction, no `int` element id, no new colour, one orange button per window, every new file has its 5-field header. | - |
| R5 | Checked, no defect | Quick snags: the "One moment" refusal, the picture hand-off (`SnagAwaitingPicture` cleared and `SnagPictureBusy` set in the same UI-thread call, so the figure cannot come back between them), `Snags.csv` rewritten through the temp file with the #41 separator and quoting for commas, quotes and line breaks, comment-box keys (Enter / Esc / repeats), Open still adds to the selection, the setting on = the old box. | - |
| R6 | Note, not changed | A click uses the previous frame's camera with this frame's look direction; turning fast while clicking in third person can shift the hit by a few centimetres. First person has the same one-frame lag. A snag recorded and then paused within 0.1 s keeps no picture (as before this change). The "One moment" message also shows while a Snapshot (K) is being taken. | Click offset fixed later (camera session, C2); the rest left - no new behaviour asked for. |

Extra Revit test steps:

41. **Next walk starts where you stood.** In third person, walk next to a wall with a room behind you, end the walk
    (End walk). Click Model Walk again: you start where the character stood, not 3 m back.
42. **Camera under a low duct.** In third person, stand under a duct or beam about 200 mm above your head and look
    down. Expect the camera close above the character, never inside or above the duct.

| Check (stage 6) | State |
|---|---|
| Build Revit 2020-2027 after the fixes (Linux, `-p:EnableWindowsTargeting=true`) | PASS - 0 warnings, 0 errors each |
| Metadata rules (Python copy of `build/check-metadata.ps1`) | PASS - 211 files |
| XAML resource keys used by the change all in the theme | PASS |
| R1-R3 in Revit (steps 35, 41, 42) | NEEDS REAL REVIT |

## The real character (2026-11-21, stage 8)

The owner's request: "remove all the character and keep this character" - the blocky code-built figure goes, the real
character from AJ-Tools PR #51 (Game Mode v1.61.0, `ab98e2e`, merged) comes in. AJ-Tools was read only, never changed.
Builds and an outside-Revit check only - nothing here has run in Revit yet.

| What | How |
|---|---|
| The character | A site worker: yellow hard hat, safety vest, shirt, trousers; 1850 mm to the top of the hat. glTF 2.0 with its data inside the file: one skinned mesh (6 colour parts, 4,620 vertices, 2,524 triangles), 23 bones, clips Idle (4.17 s), Walk (1.25 s), Run (0.88 s). |
| Where it lives | `src/HeronTools/Resources/ModelWalk/WalkCharacter.gltf` (469 KB), an **embedded resource** in `HeronTools.dll` (`HeronTools.ModelWalk.WalkCharacter.gltf`) - not a file beside the DLL, so the installer needs nothing new and it cannot go missing. The only change to the AJ-Tools file: its `generator` text says Heron Tools. |
| Reading it | `WalkCharacterModel` (from `GameCharacterModel.cs`). AJ-Tools reads the JSON with Newtonsoft.Json; Heron Tools ships no JSON library and .NET Framework 4.7.2 / 4.8 (Revit 2020-2024) has no `System.Text.Json`, so `WalkJson.cs` is a small plain-.NET JSON reader (invariant culture for numbers - a decimal-comma Windows region reads 0.5 right). Every index and data block is checked against the file before use; a broken file gives a plain reason, never an exception. Read once per Revit session, the first time third person is used (about 40 ms). |
| Animation | `WalkCharacterAnimator` (from `GameCharacterAnimator.cs`): linear-blend skinning, 0.2 s cross-fade between clips, Walk / Run keep the step phase. Speed below 180 mm/s = Idle. The clips' own speeds, measured from the feet: **Walk 982 mm/s, Run 1725 mm/s**. Rate = player speed / clip speed, capped at 2.2x (walk) and 2.4x (run). So Walk plays true to about 2.2 m/s and Run to about 4.1 m/s; above that the feet slide (speed steps 3.5, 5.5 and 8 m/s, and Shift x3). Kept on purpose: legs that still read as walking. Heron change: the scratch matrices are fields - **0 bytes allocated per frame**. |
| Speed hand-over | The engine passes the player's **horizontal** speed (distance the feet moved this frame / frame time - going straight up stairs or flying up is not walking), smoothed over about 0.12 s so one late frame does not flick the legs, and **Shift** (`input.Run`) for Run. A jump (viewpoint, go to point, door, back to start - over 1800 mm in one frame) is not a step. |
| Drawing | `WalkCharacterServer` (was `WalkFigureServer`, same server id): one shaded triangle list per colour part. File axes (Y up, facing +Z, +X = its left) become the walk's (Z up, facing the yaw, +Y = its left) - a cyclic swap, so triangles keep their winding; soles on the floor under the eye. Revit's buffers are **kept between redraws**: triangles and colours made once, the vertices rebuilt only when the pose moved on (a new walk frame), and anything Revit says is no longer valid (`IsValid()`, as the API notes ask) is made again. A replaced buffer is disposed at once (`VertexBuffer`, `IndexBuffer`, `EffectInstance` and `VertexFormat` are `IDisposable` in all eight years - read from the reference assemblies). |
| Redraws | Walking or running with no camera move (pushing into a wall, viewpoint play) refreshes the view each frame, as the limb swing did. Standing: once the blend into Idle is done, no extra refresh - the idle pose moves only on redraws Revit does anyway (looking around). Standing still with the mouse still costs nothing. |
| No fallback | The blocky figure is **removed** (code, colours, docs). If the character cannot be read, third person still works - camera behind, nothing drawn, the graphics server not even switched on - and the walk says once: "Third person without the character: *reason*. The camera is behind you; nothing else changes." |
| Colours | The file's own plain colours (linear light turned into screen colour), its black skin and white eyes swapped for a skin tone and dark eyes, as in AJ-Tools. These are **model-view graphics**, like Colorize's palette - CONTRIBUTING rule 6 and `HeronStyles.xaml` govern **window** colours, so the yellow hat stays. No window colour was added. |
| Licence | The character is CC0 1.0 (public domain, no credit required). Recorded in `NOTICE` next to the Manrope font entry, as the repo does for third-party assets. AJ-Tools did not record the original author or download page; the owner may want to add it (owner decision 2 below). |
| Kept from stage 6 | Server registered only while walking in third person, switched off on every end path; `RemoveServer` only when Revit allows (model closed, Revit closing); the camera put back on the player's eye at End; wall slide stops in front of a surface; 24 rays a frame; snag pictures hide the character, snapshots show it; aim rays from the camera. None of that code changed - only names. |

API check (`Nice3point.Revit.Api` XML 2020, 2021, 2022, 2023, 2024, 2025, 2026, 2027): `VertexBuffer(int)`, `Map`,
`Unmap`, `IsValid`, `GetVertexStreamPositionNormal`, `VertexStreamPositionNormal.AddVertex`, `VertexPositionNormal(XYZ,
XYZ)`, `GetSizeInFloats`, `IndexBuffer(int)` ("measured in short integers"), `GetIndexStreamTriangle`, `AddTriangle`,
`IndexTriangle(int,int,int)`, `GetSizeInShortInts`, `EffectInstance(VertexFormatBits)` and its `Set...Color`,
`IsValid`, `VertexFormat(VertexFormatBits)`, `IsValid`, `DrawContext.IsAvailable`, `FlushBuffer` (9 arguments) - the
same in every year. No version-specific code. The .NET side uses only `System`, `System.IO`, `System.Text`,
`System.Globalization`, `System.Reflection` members that exist in net472 (it builds for 2020).

**Owner decisions:** (1) standing still, the character holds his idle pose until something redraws the view (he does not
breathe on his own) - chosen so a big model is not redrawn 30 times a second for him; say if he should breathe anyway;
(2) the character's original source (author / page) is not recorded in AJ-Tools - CC0 needs no credit, but name it if
you know it and it can go in `NOTICE`; (3) at the faster speed steps the feet slide (above about 2.2 m/s walking,
4.1 m/s running) - raising the caps makes the legs blur instead.

### Revit test steps (the real character)

43. **Stand, walk, run.** Start a walk, press Q. Expect a site worker in a yellow hard hat where you stand, facing where
    you look, feet on the floor. Stand: idle pose. W at the second speed step (1.5 m/s): he walks, legs in step. Hold
    Shift: he runs. Let go of everything: he slows and stands, with no snap between the three.
44. **Feet at speed, and turning.** At the 3.5 m/s step and with Shift, expect the legs to stay readable and the feet to
    slide a little (expected). Turn on the spot with the mouse: he turns with you. Walk up a stair: he walks and rises
    with you (his feet do not fit each tread - there is no foot placement).
45. **Standing still costs no redraws.** In third person, stand still and do not touch the mouse for 10 s with F12 on:
    the frame count stays at about four a second and Revit stays idle (Task Manager CPU low).
46. **Big model speed with the character (F12).** On the model of step 21: F12, walk one minute in first person, then in
    third person. Write down both averages. The character's own cost was 0.26 ms a frame outside Revit; the redraw cost
    is only measurable here.
47. **Colours, walls and pictures.** He is shaded (hat yellow, vest, skin tone, dark eyes), lit from the view like the
    model, in Shaded and Realistic. Walk behind a wall: he is hidden. A snag's picture has no character; a Snapshot (K)
    has him. End the walk in every way of step 37: he is gone each time.

### Results (stage 8)

| Check | State |
|---|---|
| Build Revit 2020, 2021, 2022, 2023, 2024, 2025, 2026, 2027 (`dotnet build ... -p:EnableWindowsTargeting=true`, Linux) | PASS - 0 warnings, 0 errors each |
| DLL size | 1.44 MB to 1.93 MB in every year (+488 KB: the character file) |
| Metadata rules (Python copy of `build/check-metadata.ps1` - no pwsh here) | PASS - 214 files |
| Settings XAML well-formed; only text changed, no new resource key | PASS |
| Character resource inside the built DLL (2020 and 2027, read from the DLL's resource table) | PASS |
| Reader + animator outside Revit (throwaway .NET test, not committed): file read through `TryLoad` from an embedded resource, 46 ms; standing 1850 mm, soles at 0 mm, face to the front; Walk at 1.5 m/s rate 1.53, at 3.5 m/s rate 2.20 (cap); Run at 4.5 and 10.5 m/s rate 2.40 (cap); back to Idle after stopping; no NaN; broken files give a plain reason | PASS |
| Cost per frame of posing + skinning (same test, 3,000 frames) | PASS - 0.26 ms, 0 bytes allocated |
| Turning the pose into Revit's buffers (one `XYZ` pair per vertex, about 4,600 a rebuilt frame) | NEEDS REAL REVIT (F12, step 46) |
| The character draws, animates, hides behind walls; snag / snapshot pictures; switch-off on every end path | NEEDS REAL REVIT (steps 43-47, 37) |
| Install into Revit on the build PC | NOT RUN - Linux build machine, no Revit |

## Stage 8 review (2026-11-21)

A fresh read of the whole change (`git diff origin/main...HEAD`), every Model Walk file it touches, `NOTICE`, and the
`Nice3point.Revit.Api` XML for the DirectContext3D members. The reader and animator were run outside Revit in a
throwaway .NET test (not committed) in en-US, **de-DE** and fr-FR, with 18 broken files. Builds only - nothing here
has run in Revit.

| # | Severity | Finding | State |
|---|---|---|---|
| S1 | Important | `WalkJson`: a file nested very deep (100,000 `[`) ran the reader out of stack. A stack overflow cannot be caught - it ends Revit, not just the character. | **Fixed** (`8ad405d`) - deeper than 64 levels is refused with a plain reason ("nested too deep"). The glTF file nests about six. |
| S2 | Medium | Animator: one NaN speed went into the clip time and stayed there - every later frame was NaN (character gone or garbage) for the rest of the walk. | **Fixed** (`d1efd9b`) - a NaN or negative speed counts as standing. An infinite speed was already capped. |
| S3 | Medium | glTF reader: a damaged `count` (2,000,000,000) made its array first and checked the buffer after - 7.4 GB allocated inside Revit's process before the refusal. | **Fixed** (`c26f1f7`) - count, view and buffer range checked before the array is made; one block capped at 4 million values (the shipped file's biggest: 10,368). Now refused with 2 MB used. |
| S4 | Low | Animator: `w <= 0.0` lets a NaN bone weight through, so one damaged weight makes its vertex NaN. | **Fixed** (`daf09b4`) - guard only, the shipped file has none. |
| S5 | Checked, no defect | JSON numbers: invariant culture, exponents (`1.5e2`, `-0.25E-1`) right in de-DE; `\u` escapes incl. surrogate pairs; base64 `data:` buffers; component types byte / ubyte / short / ushort / uint / float and `normalized`; accessor + view offsets, stride, bounds (long maths, no overflow); triangle indices and skin joints checked; bone loops cannot hang (`BuildOrder`); every failure is caught in `TryLoad` / `EnsureCharacter` / `Advance` with a plain reason. | - |
| S6 | Checked, no defect | Animator: parents before children, inverse bind matrices column-major to row-major right, weights re-normalised when they do not sum to 1, clip time wraps, dt NaN / negative / huge clamped; **0 bytes allocated per frame** (measured, 300 frames walk / run / blend), 0.17-0.23 ms a frame. The posed character stays inside `GetBoundingBox` (widest 675 mm of 1200, highest 1870 mm of 2000). Stops into Idle and `IsMoving` goes false. | - |
| S7 | Checked, no defect | Server: vertex format, effect and stream are all PositionNormal; index count = triangles x 3 shorts; parts over 65,535 vertices skipped (16-bit indices); every buffer tested with `IsValid()` before use; `CanExecute` needs Visible + view id + document; `RenderScene` and `CanExecute` cannot throw; nothing drawn while hidden (snag pictures). Switch on / off, `Prepare`, `TryRemove` and the end paths are line-for-line the stage 6 code (diffed against `main`). Camera back on the eye at End, wall slide and the 24-ray budget unchanged. | - |
| S8 | Checked, no defect | The embedded resource `HeronTools.ModelWalk.WalkCharacter.gltf` is in every year's DLL (net472, net48 x4, net8 x2, net10), full 469,422 bytes - read from each DLL's resource table. No `int` element id, no transaction, no window colour, no model change; every new file has its 5-field header; `NOTICE` CC0 entry sits with the Manrope entry and says what the Apache grant does not cover. | - |
| S9 | Note, not changed | A replaced Revit buffer is disposed on the next redraw after it was drawn. The API notes say nothing about disposing a buffer that was already drawn, so this is not proven. | NEEDS REAL REVIT - step 48. |
| S10 | Note | The animator is kept for the Revit session, so a new walk (or Q again) starts from the clip it was in last time and blends 0.2 s into Idle - a short settle, not a snap. | **Fixed** in "Character polish" below - he starts standing. |

Extra Revit test step:

48. **Long walk, many redraws.** In third person, walk and run for five minutes in one model, end, open a second model
    and do the same (F12 on). Expect no flicker, no missing parts and no slow-down over time; Revit's memory (Task
    Manager) climbs no more than a few MB across the two walks.

| Check (stage 8 review) | State |
|---|---|
| Build Revit 2020-2027 after the fixes (Linux, `-p:EnableWindowsTargeting=true`) | PASS - 0 warnings, 0 errors each |
| Metadata rules (Python copy of `build/check-metadata.ps1`) | PASS - 214 files |
| Character resource in all eight DLLs | PASS |
| Reader + animator outside Revit, en-US / de-DE / fr-FR, 18 broken files, NaN / infinite input | PASS (S1-S3 failed before their fixes) |
| Buffer reuse and dispose over a long walk and two models (step 48) | NEEDS REAL REVIT |

## Third-person camera session (2026-11-22)

Builds only - nothing here has run in Revit. Area: the camera (offset, wall slide, ray budget, aim rays, Q, end paths),
not the character drawing.

| # | Finding | State |
|---|---|---|
| C1 | A walk ended in third person **without** Revit's thread (Revit busy 3 s, overlay fault) left the walk view's camera 3 m back, and the next walk started there (stage 6 R1, the case left). | **Fixed** - `EndWithoutRevit` (no Revit API) remembers the model, the walk view, the camera it left and the player's eye and look. `CmdModelWalk` starts the next walk in that model and view from the player when the walk view's camera is still within 5 mm of where it was left; a camera the modeller moved since is kept. A walk in another model keeps the note for its own model. |
| C2 | In third person a click (handled before this frame's camera move) used the previous camera with this frame's newer look - a few cm off when turning fast (stage 6 R6). | **Fixed** - clicks, Measure and Go to point use the look that camera was sent with (`AimAlong`), so they go through the crosshair of the picture on screen. The readout already used this frame's camera and look. First person unchanged. |
| C3 | Q mid-move and during Play viewpoints: the first third-person frame always casts the wall slide (`_slideAllowedFt` reset to -1 by first person); Play viewpoints moves the player, the camera follows with the slide cast when it moved (Play uses no movement rays). Every end path on Revit's thread puts the camera back (`End`), the rest now C1. | Checked, no defect |
| C4 | Past the 24-ray budget with the camera moved, the old slide distance was used from the new pivot and direction - for that frame the camera could go through a wall (turning or walking backwards into it on a frame that used all its rays). | **Fixed** - the camera stays where it was for that frame (on the pivot above the head if there is none yet); the slide is cast the next frame. |

Extra Revit test steps (camera session):

61. **Busy end in third person.** In third person, stand near a wall with a room behind you. Start a Revit
    command that keeps Revit busy for more than 3 s (or close the overlay with Alt+F4 while a modal dialog is open) so
    the walk ends with "Revit was busy". Click Model Walk again: you start where the character stood, not 3 m back.
    Repeat, but orbit the walk view by hand before starting again: the walk starts from your orbited camera.
62. **Click while turning.** In third person, aim at the edge of a thin pipe, then turn the mouse fast and click while
    turning, in Select mode. The pipe that was under the crosshair at the click is selected.

| Check (camera session) | State |
|---|---|
| Steps 61-62 | NEEDS REAL REVIT |
| Build Revit 2020-2027 (Linux, `-p:EnableWindowsTargeting=true`) | PASS - 0 warnings, 0 errors each |
| Metadata rules (Python copy of `build/check-metadata.ps1`) | PASS - 214 files |

## Character polish (2026-11-22)

Two small fixes in the character drawing, from the stage 8 review's notes. Builds and an outside-Revit check only.

| # | What was wrong | Fix |
|---|---|---|
| P1 | (S10) The animator lives for the Revit session, so a new walk or Q back into third person started from the clip he was in last time (walking or running) and settled 0.2 s into Idle. | `WalkCharacterAnimator.Reset()` - start of Idle, no blend, pose made again (no allocation). `WalkCharacterServer.ResetPose()` calls it on the first third-person frame of a walk and after every Q into third person. |
| P2 | The frame the blend into Idle finishes asked for no redraw (`IsMoving` had just gone false), so with no camera move he was left on the last blend frame - a few per cent short of standing - until something else redrew the view. | That last frame is redrawn too (the engine remembers he was moving one frame before). Still no redraws once he stands. |

Revit test step:

49. **Starts standing.** In third person, run (Shift + W) and press Q mid-run, wait, press Q again: he appears standing
    at once, no left-over running pose. Run again, end the walk mid-run, start a new walk in third person: standing at
    once. Walk, stop, keep the mouse still: he ends fully in the standing pose.

| Check (character polish) | State |
|---|---|
| Build Revit 2020-2027 (Linux, `-p:EnableWindowsTargeting=true`) | PASS - 0 warnings, 0 errors each |
| Metadata rules (Python copy of `build/check-metadata.ps1`) | PASS |
| `Reset()` outside Revit (throwaway test): after running, back to Idle, not moving, 1850 mm, the same pose as the first load, 0 bytes allocated | PASS |
| Step 49 in Revit | NEEDS REAL REVIT |

## File readers review (2026-11-22)

A read of every Model Walk file reader and writer: `ModelWalkSettings.txt` (`WalkSettings`), `ModelWalkKeys.txt`
(`WalkKeyBindings`), the viewpoint store (`WalkViewpointStore`), and the character reader (`WalkJson`,
`WalkCharacterModel`, the embedded resource - see "Stage 8 review" above). Run outside Revit in a throwaway .NET test
(not committed - the repo has no test project) in **de-DE**, with missing, damaged and hand-edited files.

| # | Severity | Finding | State |
|---|---|---|---|
| F1 | Medium | Keys: `Save` writes `Action=None` for an action left without a key (its standard key taken by another). The next walk read that as a damaged line - "had a key that cannot be used (None)" - and the HUD shows only the first note, so the true one ("has no key: its standard key is used by another action, press K") was hidden. | **Fixed** (`2171dfe`) - `None` is skipped as "no key saved": the action gets its standard key if free, else the true note. |
| F2 | Low | Settings: the five speeds were saved rounded to 3 decimals. Two speeds the window accepts (1.0001 and 1.0004 m/s) saved as the same number, the next walk found them not increasing, and all five went back to standard. | **Fixed** (`df6ae57`) - saved exactly (`R`, invariant culture). |
| F3 | Checked, no defect | All three files: numbers written and read in the invariant culture (de-DE writes `1650.5`, a hand-typed `1,7` is refused, not read as 17); NaN, `1e999`, out-of-range and unknown lines keep the standard value with one plain note; missing file = standard, no note; read refusal (locked, no access) caught; every save goes through a `.tmp` file then `File.Replace` / `File.Move`, and an I/O or access error comes back as a plain message, never an exception. Viewpoints round-trip exactly (`R`), names with tabs / line breaks cleaned, empty names become "Viewpoint", pitch over 90 degrees refused. Swapped keys (W / S) round-trip with no note. | - |
| F4 | Note, not changed | A viewpoint file name is the model name + a 16-character hash. On Revit 2020-2024 (.NET Framework, 260-character paths) a model name over about 170 characters makes the path too long: the viewpoints cannot be saved, and the walk says so plainly ("could not be saved (... too long ...)"). Shortening the name would move existing viewpoints of long-named models to a new file, so it is left. Revit 2025+ has no such limit. | Left - told here. |

Revit test step:

50. **Keys left without a key.** Close Revit. In `%AppData%\HeronTools\ModelWalkKeys.txt` leave one line, `MoveForward=S`. Start a walk: the note says "Walk back has no key ... press K". Pause, K, Save, end the walk, start again: the same note, not "cannot be used (None)". Delete the file afterwards (standard keys come back).

| Check (file readers) | State |
|---|---|
| Build Revit 2020-2027 after the fixes | PASS - 0 warnings, 0 errors each |
| Metadata rules (Python copy of `build/check-metadata.ps1`) | PASS - 214 files |
| Settings / keys / viewpoints outside Revit, de-DE, missing + damaged files (F1, F2 failed before their fixes) | PASS |
| Step 50 in Revit | NEEDS REAL REVIT |

## Walk engine review (2026-11-22)

A read of the core walk - movement, gravity, collision, doors, the frame loop and every way a walk ends. Six fixes,
each built for Revit 2020-2027. **Nothing here has run in Revit.**

| # | What was wrong | Now |
|---|---|---|
| E1 | The first step after standing still jumped about 0.8 m. Standing still, the overlay asks for only four frames a second, so the first moving frame saw up to 250 ms gone and the eased move used almost full speed for all of it. | After a still frame, the first moving frame counts as one normal frame (33 ms). Slow frames while moving are still clamped at 250 ms as before. |
| E2 | Closing the walk's model (Close, closing its last view, or closing Revit) did not end the walk at once: the end waited for a frame, and Revit sends none while it closes a model - on exit, never. The report then relied on the overlay's own close. | The walk ends the moment its model starts to close (`DocumentClosing`, watched once per Revit session): the report is written, the character switched off, and the overlay shows the end message. No transaction while the model closes; if the close is cancelled, the message says the snag colours and the hidden elements are still on and how to get them back, and a walk that ended in third person starts the next walk where the player stood (the camera session's C-fix, used here too). |
| E3 | Hide (click), Show hidden (U) and Revit's own Reset Temporary Hide/Isolate did not look for the floor again: hide the floor you stand on and you stayed in the air until you moved. | Each of them looks for the floor again on the next frame. |
| E4 | E (door) measured the door's distance along the slanted look. Looking down at a door, the step landed further past it than meant. | The distance is measured across the floor; you land 900 mm past the door face as meant. |
| E5 | The walk view found again as "Heron Model Walk (10)" before "(2)" (sorted as text). | Sorted by number: "(2)" comes first. |
| E6 | In a workshared model the walk view is named after the Revit user name. A user name with a character Revit refuses in a view name (`\`, `:`, `;` ...) meant the walk view could never be made. | Those characters become "-" in the view name. A name with one could never be made before, so no existing walk view is lost. |

Checked and left as is: ids are compared as `ElementId` or `LongValue()` (no `int`); levels by `ProjectElevation`;
every transaction is named and its `Commit()` checked; no transaction outside the frame's ExternalEvent or the command;
Revit refusing frames for 5 s still ends the walk; Alt+F4 still ends the walk the normal way first.

### Revit test steps (walk engine review)

51. **First step is smooth.** Stand still for 2 s, then press W once briefly. Expect a short, smooth step - no jump.
    Then hold W: speeds up as before.
52. **Model closed while walking or paused.** (a) Start a walk, Esc (pause), close the model with the X on its last
    view, answer No to saving. Expect the overlay to say "The model is closing, so Model Walk ended." and the walk
    folder to have Snags.csv. (b) Same, but answer Cancel to the save question: the model stays open, the message also
    says the snag colours / hidden elements are still on; start Model Walk again - it starts normally. (c) With a walk
    running, close Revit: no error, and Snags.csv is in the walk folder.
53. **Hide the floor.** In Hide mode, look down and click the floor you stand on. Expect to drop to whatever is below
    (or "no floor below"). Press U: back on the floor.
54. **Door while looking down.** Stand 1 m from a door, look down at about 30 degrees at it, press E. Expect to land
    about 900 mm past the door, the same as looking straight at it.
55. **Odd user name.** In Options > General set the user name to `test:user`, open a workshared model, start Model Walk.
    Expect a walk view "Heron Model Walk - test-user" and a normal walk. Put the user name back.

### Results (walk engine review)

| Check | State |
|---|---|
| Build Revit 2020-2027 (Linux, `-p:EnableWindowsTargeting=true`) | PASS - 0 warnings, 0 errors each |
| Metadata rules (Python copy of `build/check-metadata.ps1`) | PASS - 214 files |
| Steps 51-55 | NEEDS REAL REVIT |

## Windows and words review (2026-11-22)

Every key and setting in the HUD help card, pause menu, Settings window, Key Settings window, ribbon tooltip and
`model-walk-guide.md` checked against `WalkKeyBindings`, `WalkSettings` and the engine. What did not match:

| # | What was wrong | Fix |
|---|---|---|
| W1 | Key Settings said "Changes apply now; Save keeps them", but Cancel or the close box put the old keys back - nothing applied without Save. | Footer: "Save uses the new keys now and keeps them for your next walks. Cancel puts the old keys back." |
| W2 | Key Settings: a key file that could not be written kept the window open, while the message said "They still work until the walk ends" - and Cancel then took them away. | As in Settings: Save always uses the new keys for this walk; a save problem shows as a message in the walk. A good save says "Keys saved". |
| W3 | Key Settings' fixed-key line left out Enter (it is fixed in the code). | Enter added; the line also says a taken key is swapped. |
| W4 | Help card and guide did not say Space / C rise and sink in Pass-through too, and Fly speed (x2) applies to Pass-through. Help said Pass-through "walls and slabs do not stop you" - nothing stops you. Fly did not say walls still stop you. | Help card, guide key table, guide settings table and Settings window speed note fixed. |
| W5 | Settings window named "+ / -", "Shift" and "C" as if fixed; they can be remapped. | "Faster / Slower keys (standard + and -)", "Run (standard Shift)", "standard key C". |
| W6 | Pause menu Settings tooltip listed only mouse, speeds, eye height, step-up, snag colours. | Adds third person and snag comments. |
| W7 | The guide did not say how to change a key, or what Reset / Cancel do in either window. | A short "Change a key" paragraph and a line under the Settings table. |
| W8 | Ribbon tooltip: "Pause (Esc) for snags, viewpoints and settings". | "... settings and keys". |
| W9 | Key Settings XAML header comment said "press S"; it is K. | Fixed. |
| W10 | (#79 item 1, PR review) "Settings saved", "Keys saved" or why they could not be saved, and the "colours given back" line after a Save / Sync while paused, were toasts - but while paused the walk layer is hidden and no HUD is drawn, so nobody saw them. | While paused they go in the pause card's status line (as a snag comment does); in the walk they stay toasts. |
| W11 | (#79 item 2) The 5 s "Revit stopped taking frames" clock was only reset by an accepted frame: one refusal before a pause and one more on resume could end the walk at once. | The clock starts again at every pause and every resume. |
| W12 | (#79 item 3) The pause menu's "Clear snag colours (J)" kept the old key after Key Settings. | Set again when Key Settings closes. |
| W13 | (#79 item 4) The Settings window named + / -, Shift, C and J even when they were remapped (W5 only said "standard"). | The window gets the key map and names the keys actually bound. |
| W14 | (#79 item 6) The ribbon tooltip did not say snag colours stay after the walk (Keep snag colours is on by default). | "Snag colours stay in the walk view after the walk (standard; a setting) until J, or the next Save or Sync." |

Checked and right: the five speeds, the start step (3), eye / step-up / sensitivity limits, low eye
(min of 1000 mm and eye - 300 mm), third-person offsets (3000 mm back, 500 mm right), J, F12, M, B / 1-9 / O, Q, quick
snags and "Ask for a comment at each snag", the pause-menu keys (Enter, S, K, Esc). One orange button per window
(Carry on walking, Save, Save). No colour outside `HeronStyles.xaml`. No game wording.

Revit test step:

56. **Key Settings.** Pause, K, give Measure the key N, Cancel: M still measures. K again: N for Measure, and
    Clear snag colours the key L, Save: the pause card says "Keys saved." and its button says "Clear snag colours (L)".
    Carry on: N measures, the help card (H) and the bottom-right hint say N. Pause, S: the Snags note says "(L, or the
    pause menu)". Next walk: N still measures. Reset to standard keys, Save.
57. **Save messages while paused.** Pause, S, Save: the pause card says "Settings saved.". With a snag coloured and
    the walk paused, press Ctrl+S in Revit: the pause card says the snag colours were given back. Make
    `%AppData%\HeronTools\ModelWalkKeys.txt` read-only, pause, K, change a key, Save: the pause card says the keys could
    not be saved, and the new key still works in this walk. Clear read-only.

| Check (windows and words) | State |
|---|---|
| Build Revit 2020-2027 (Linux, `-p:EnableWindowsTargeting=true`) | PASS - 0 warnings, 0 errors each |
| Metadata rules (Python copy of `build/check-metadata.ps1`) | PASS (214 files) |
| The three Model Walk XAML files parse; every StaticResource / DynamicResource and every key named in code exists in `UI/*.xaml` | PASS |
| Render the windows outside Revit | NOT RUN - WPF cannot render on this Linux build machine |
| Steps 56-57 in Revit | NEEDS REAL REVIT |

---

# Full audit after quick snags, third person and the character (2026-11-22, final-review session)

A fresh read of every line of `src/HeronTools/ModelWalk/` on `main` (after #72 and #73) and the Model Walk wiring in
`App/HeronToolsApp.cs`, against the 44 audit findings (GM-A01 to GM-A44), the 19 must-keeps and `AGENTS.md`. Builds and
reading only - nothing here has run in Revit.

**No important defect and no regression of a GM-A fix or a must-keep was found.** Every GM-A fix listed above still
holds in the code (A01-A04, A06, A07, A09-A18, A20-A22, A24-A30, A32, A34-A37, A39, A40, A44 read again line by line);
all 19 must-keeps hold, including through third person (snag pictures hide the character, "where you stood" and Open
snag use the player's eye) and quick snags (the report is still written once on every end path).

Small defects found - each checked against the code, each in code another session owns, so each is a GitHub issue:

| Issue | Area | Findings |
|---|---|---|
| #79 | HUD, Settings, Key Settings windows | Settings save failure shown as a toast while paused (never seen); an old refused frame can end the walk at once; pause-menu "Clear snag colours (J)" keeps the old key; Settings window names fixed keys; Key Settings text contradicts itself; ribbon tooltip silent on kept colours - **all fixed in #87** |
| #81 | Viewpoint file | A viewpoint named "#..." is lost on the next walk (the keyless-action warning found at the same time was fixed meanwhile in #78, F1) |
| #82 | Engine, command | Wall slide refused near a linked model (ray budget); clean-up after a failed overlay open is cancelled; 24 rays is a move budget, not a frame cap; end paths redraw only the active view; two wording / silent-failure points |
| #83 | Third-person camera | Stale wall-slide answer when over budget (camera in the wall for a frame) and after a mode / world change - **the over-budget case fixed in #88**; the mode / world change still open |
| #84 | Snags | The same linked element can be snagged twice; the CSV "Snag colour" column after a give-back |
| #85 | Character server | A second walk does not cancel a pending switch-off; a failed buffer remake leaves a disposed object |
| PR #74 comment | Snag pictures | A picture taken after a forced pause, during a Snapshot + Esc, or at the end can show the pause / end card - **fixed in #74** (step 60) |
| #77 | Spec item 31 | The optional "one saved 3D view per snag" is not built (owner decision) |

Spec: `docs/game-mode-spec.md` now has section F, "What was built", item by item against the code; issue #44 updated.

| Check (full audit) | State |
|---|---|
| Line-by-line read of all 29 Model Walk files + ribbon wiring | PASS |
| Build Revit 2020-2027 on `main` (Linux, `-p:EnableWindowsTargeting=true`) | PASS - 0 warnings, 0 errors each |
| Metadata rules (Python copy of `build/check-metadata.ps1`) | PASS - 214 files |
| Revit API XML cross-check of new findings | NOT RUN - the `Nice3point.Revit.Api` packages were not in this container's NuGet cache |
| The defects above, once fixed | NEEDS REAL REVIT - each fix adds its own test step |
---

# Snag pictures and pause (2026-11-22, quick snags session)

The two notes the stage 6 / stage 8 reviews left (R6 above).

| What was wrong | Now |
|---|---|
| Quick snagging, then Esc within about 0.1 s: the pause cleared `SnagAwaitingPicture`, so that snag kept **no picture**; if the picture was already being taken, the pause card could land in it. | Esc and the Pause key now go through `PauseWhenAsked`: while a snag's picture is waiting or being taken, held keys stop at once and the pause comes as soon as the picture is saved (at most 2 s, then it pauses anyway). The comment box (setting on) does not open in between. A pause Revit forces (the overlay loses focus or the mouse) still pauses at once; if that drops a waiting picture, the pause menu now says "Snag n was recorded without a picture". |
| A picture already being taken (the short wait before the screen copy) when the walk paused or ended - a Snapshot (K) then Esc, or a forced pause / end right after a snag - still copied the screen, with the pause or end card in it (final-review comment on #74). | Esc / Pause also wait for a Snapshot. If the walk pauses or ends anyway, `CaptureNow` takes no picture; the snag says "Snag n was recorded without a picture (the walk paused before it was taken)" in the pause menu's status line, a snapshot "Snapshot not saved (...)". |
| A Snag click during a Snapshot (K) said "the last snag's picture is being taken". | It says "One moment - the snapshot is being taken. Click again." (and "Pausing..." in the moment before a pause). |

### Revit test steps

58. **Esc right after a snag keeps its picture.** Quick snagging on. Snag a duct and press Esc at once (same moment).
    Expect the walk to pause a blink later, and `Snag nnn.png` to show the red duct with no pause card in it. Then
    snag, and Alt+Tab away at once: the pause menu says "Snag n was recorded without a picture".
59. **Snag click during a Snapshot (K).** Press K and click a snag at once. Expect "One moment - the snapshot is being
    taken. Click again."; click again: recorded with its picture.
60. **No card in a picture.** Press K and Esc at once: the walk pauses a blink later and the snapshot has no pause card.
    Snag, then make Revit take the focus at once (Alt+Tab): no snag picture with the pause card in it; the pause menu
    says the snag was recorded without a picture.

## Last read of the test script (2026-11-22, final-review session)

After #74, #75, #76, #78, #80, #87 and #88 merged, the Revit test script was made one numbered list: **steps 1 to
62, each number once, no gaps, and one row each in "Revit test results (issue #42)"**.

| Was | Now |
|---|---|
| Steps 15-18 written inline in a paragraph | A numbered list (same words) |
| Steps 19 and 20 had a results row but no step text | Written out under "Extra Revit checks (steps 19-29)" (issues #41 and #40) |
| Step 50 written inside a results-table cell | A numbered step above the table; the cell says "Step 50 in Revit" |
| Two steps numbered 56 and 57 (#87's and #74's), plus #74's 58 | #87 keeps 56-57; #74's are now 58-60 |
| "Camera-1" / "Camera-2" (no number) | Steps 61 and 62 |
| Results rows missing for 48-57, 61, 62 | Added |

| Check (last read) | State |
|---|---|
| Steps 1-62 numbered once, no gap, each with a results row (checked with a script over the file) | PASS |
| Build Revit 2020-2027 on `main` after the last merges | PASS - 0 warnings, 0 errors each |
| Metadata rules (Python copy of `build/check-metadata.ps1`) | PASS |
| Every step | NEEDS REAL REVIT - the owner's test, issue #42 |

# Viewpoint named "#..." (2026-11-22, issue #81)

| What was wrong | Now |
|---|---|
| The viewpoints file skipped every line starting with `#` as its header comment, but a viewpoint name may start with `#`. A viewpoint named "#2 riser" worked in the walk, then was gone on the next walk, not even counted as skipped. | Only a `#` line with no tab is a comment (the header has none). A viewpoint line always has tabs, so "#2 riser" is read back. A file saved before this fix still has the line, so the viewpoint comes back too. |

### Revit test step

63. **Viewpoint named "#...".** Save a viewpoint and name it "#2 riser". End the walk and start it again: "#2 riser"
    is still in the viewpoint list and takes you to the same place.

| Check (#81) | State |
|---|---|
| Build Revit 2020-2027 | PASS - 0 warnings, 0 errors each |
| Metadata rules (Python copy of `build/check-metadata.ps1`) | PASS |
| Step 63 | NEEDS REAL REVIT |

# Third-person camera after a mode or world change (2026-11-22, issue #83 item 2)

Item 1 of #83 (the camera's old wall distance used from a new place when the frame had no rays left) was fixed by #88.

| What was wrong | Now |
|---|---|
| The camera's wall check is cast only when the pivot or the look direction moves. Pass-through stores the full 3 m distance (nothing blocks), so leaving Pass-through while standing still with a wall behind kept the camera behind the wall until the mouse moved. The same after hiding the wall behind you (the camera stayed short) or showing it again (the camera stayed inside it). | The stored distance is forgotten when Pass-through goes on or off, when Model Walk hides an element or shows hidden ones, when Revit's own Reset Temporary Hide/Isolate is noticed, and on the world refresh every 4 s (links, Visibility/Graphics). The next frame casts the check again. Cost: one cast (1-2 rays) after each of those, nothing while standing still otherwise. |

### Revit test step

64. **Third-person camera after Pass-through / Hide.** Third person, Walk. Stand with your back 1 m from a wall. Press
    Pass-through: the camera goes 3 m back, through the wall. Without moving the mouse or keys, press Pass-through
    again: within a blink the camera comes back in front of the wall. Then turn round, Hide that wall (Hide mode, click
    it) and turn back: the camera is 3 m back. Standing still, press Show hidden: the wall comes back and the camera
    comes back in front of it at once, not inside it.

| Check (#83 item 2) | State |
|---|---|
| Build Revit 2020-2027 | PASS - 0 warnings, 0 errors each |
| Metadata rules (Python copy of `build/check-metadata.ps1`) | PASS |
| Step 64 | NEEDS REAL REVIT |

# Engine: slide near links, failed open, redraw at the end (2026-11-22, issue #82)

| What was wrong | Now |
|---|---|
| Near a linked model each near cast is 2 rays. A diagonal step caught only by a side ray (a door jamb, a column corner) used 12 rays, the X slide 8 more, the Y slide hit 24; the floor check then found no ray left and threw away the X slide already found free. Walking diagonally past a door frame in an MEP model with architecture linked stopped dead. | The move keeps the last 2 rays of its 24 for the floor under you (`FloorRayReserve`): a slide axis that no longer fits is skipped, the one already found is kept. If nothing is near below and the long floor ray does not fit, the floor is checked next frame instead of "no floor". |
| If the overlay failed to open, its `Close()` was taken for Alt+F4 and cancelled; the frame event was then disposed under a live window. | `AbortOpen()` marks the overlay as failing first, so the close goes through. |
| "Ray budget 24 a frame" read like a hard cap; the door hint, readout, floor and landing checks are not counted against it. | Called the **move budget** in the notes and the code; F12 shows the real count (about 30-40 on a busy frame). |
| After "you switched to another view / model", only the now-active view was redrawn: a walk view still on screen (tiled views, a second monitor) kept its last picture with the character. | The end and the late switch-off redraw every open view of the walk's model (`UIDocument.UpdateAllOpenViews`, in all eight years' API). Not while the model is closing. |
| "Model Walk could not start, and nothing was changed" - false when the walk view had just been made. Snag colours left from an earlier walk that could not be given back at the start said nothing. | The message names the view that was made and says it stays. A give-back that fails at the start is shown when the walk starts. |

Noted, not defects (NEEDS REAL REVIT, already in the script): a transaction inside `DocumentSaving` /
`DocumentSynchronizingWithCentral` (the give-back before writing), and `SetOrientation` on a walk view that is no longer
active, at the end.

### Revit test steps

67. **Slide past a door frame near a link.** MEP model with the architecture linked. Walk diagonally into a door frame
    (jamb) or a column corner: you slide along it, not stop dead. F12: the rays stay at or under about 40.
68. **Walk view redrawn after switching view.** Third person. Tile two views (View tab > Tile), the walk view and a plan.
    Click into the plan: the walk ends ("you switched to another view") and the character is gone from the walk view too.

| Check (#82) | State |
|---|---|
| Revit API: `UIDocument.UpdateAllOpenViews` and `new UIDocument(Document)` in `Nice3point.Revit.Api` 2020-2027 XML | PASS - present in all eight |
| Build Revit 2020-2027 | PASS - 0 warnings, 0 errors each |
| Metadata rules (Python copy of `build/check-metadata.ps1`) | PASS |
| Steps 67-68; a failed overlay open (cannot be forced in a test) | NEEDS REAL REVIT |
