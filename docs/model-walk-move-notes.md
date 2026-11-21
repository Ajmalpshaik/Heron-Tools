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
| GM-A39 | Low | `MM_TO_FEET` rounded | Fixed in Model Walk (`1 / 304.8` from `FEET_TO_MM`). Heron's shared `Constants.MM_TO_FEET` made exact too, in its own change (issue #40, `19b2a12`); only View Crop uses it, moving by less than 0.001 mm. |
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
5. Heron's shared `Constants.MM_TO_FEET` - made exactly `1 / 304.8` in its own change (issue #40). View Crop is its
   only user; its margins and offsets move by less than 0.001 mm. View Crop still needs a Revit look.
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

Add to the owner's test script: **15. Cancelled close.** With Keep snag colours on (standard), snag something, end the
walk. Close the model, press **Cancel** at Revit's question. Save. Expect: the red goes before saving.

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

Add to the owner's test script: **16. Held Esc.** Walk, hold Esc for two seconds: paused, not ended. **17. Alt+F4.**
Hide something, Alt+F4 on the walk: the walk ends, the element shows again, back in your 3D view. **18. Kept red,
snag again.** With colours kept from an earlier walk, snag the same element: a new report row, still red.

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

## Extra Revit checks (steps 21-29, from the audit)

The audit fixes below are in the code but can only be proven in Revit. Do them after steps 1-20. Write PASS / FAIL
next to each, with the Revit year.

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
| Camera | 3000 mm behind and 500 mm right of a pivot 1900 mm above the feet (along the full look direction, so looking down lifts the camera). **One** cast (`CastNear`, host + near links - up to 2 rays, the near box round the eye) from the pivot to the camera; a hit pulls the camera in to 250 mm before it, never closer than 300 mm - unless the surface is closer than 550 mm, then half way to it (always in front of it; stage 6 review). Cast only when the pivot or direction changed and both rays fit in the 24-ray budget; otherwise the last answer is kept (or the 300 mm camera if there is none) - never a guess. No ray in Pass-through. |
| Character | Was: about 17 shaded boxes in the Heron colours. **Replaced** by the real character - see "The real character (2026-11-21, stage 8)" at the end. Drawn only in the walk view of the walk's model (`ElementId` **and** `Document` matched - ids repeat across models). No transaction, no element, no Undo step. |
| Player vs camera | Player: collision, gravity, step-up, door hint and E, floor and clear height, room, viewpoints, the snag's camera / "where you stood", Open snag. Camera: the crosshair ray - readout, Select, Snag, Hide, Measure, Go to point - so clicks land on the crosshair; distances shown (readout, "Moved ... mm") are from the player. Clicks use the previous frame's camera, which is the picture the modeller aimed at. |
| Rays | Third person adds at most **1** cast (2 rays with near links) a frame (the wall slide), only while the camera moves; the readout ray just starts at the camera. Measure with F12 on a big model (step 40). |
| Redraws | The camera moving already redraws. With no camera move, the view is refreshed only when the character appears or goes, or while it walks, runs or blends on the spot (frames are asked for until it stands). |
| Snag pictures | The character is hidden from the snag click until its picture is saved (`SnagAwaitingPicture`, then `SnagPictureBusy` from the HUD) - snag pictures show the model only. **Snapshots (K) show the character** (they are what you see); Q first for one without it. |
| Viewpoints, Save as 3D view | Save the player's eye and look, never the third-person camera offset. "Save as 3D view" therefore looks from the player's eyes. When a walk ends in third person on Revit's thread, `End` puts the walk view's camera back on the player's eye (stage 6 review: the next walk starts from that camera). Only when the walk ends without Revit's thread (Revit busy, overlay fault) does it stay 3 m back. |

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
| R1 | Important | A walk ended in third person left the walk view's camera 3 m behind and 0.5 m right of the player. `CmdModelWalk` starts the next walk from the walk view's camera, so that walk began 3 m back - possibly in the next room, inside a wall or outside the building. | **Fixed** - `WalkEngine.End` puts the walk view's camera back on the player's eye (same no-transaction camera move as every frame). Left: an end without Revit's thread (Revit busy 3 s, overlay fault) cannot touch the view; that rare case still starts 3 m back. |
| R2 | Medium | Wall slide: with a surface closer than 550 mm along the camera ray (a duct or slab just above the head while looking down), the camera was still put 300 mm out - past the surface, so the view looked from inside or behind it. | **Fixed** - the camera stops half way to such a surface, always in front of it. |
| R3 | Minor | `CastNear` is up to 2 rays (model + near links) but the slide only checked "under 24", so a frame could reach 25 rays. | **Fixed** - cast only when both rays fit in the budget. |
| R4 | Checked, no defect | `RemoveServer` / `AddServer` / `SetActiveServers`: identical in all eight years' XML; `RemoveServer` throws `ArgumentException` for a server used in an open model and `InvalidOperationException` while the service draws - both caught. Every end path (End walk, Esc Esc, ribbon, Alt+F4, view switch, model switch, model closing, `Guard` fault, Revit busy, Revit closing) reaches `SwitchOff` or `SwitchOffSoon`; a second walk re-uses the registered server and cancels a pending switch-off. `CanExecute` matches view id and document; `GetBoundingBox` and `RenderScene` cannot throw. No transaction, no `int` element id, no new colour, one orange button per window, every new file has its 5-field header. | - |
| R5 | Checked, no defect | Quick snags: the "One moment" refusal, the picture hand-off (`SnagAwaitingPicture` cleared and `SnagPictureBusy` set in the same UI-thread call, so the figure cannot come back between them), `Snags.csv` rewritten through the temp file with the #41 separator and quoting for commas, quotes and line breaks, comment-box keys (Enter / Esc / repeats), Open still adds to the selection, the setting on = the old box. | - |
| R6 | Note, not changed | A click uses the previous frame's camera with this frame's look direction; turning fast while clicking in third person can shift the hit by a few centimetres. First person has the same one-frame lag. A snag recorded and then paused within 0.1 s keeps no picture (as before this change). The "One moment" message also shows while a Snapshot (K) is being taken. | Left - no new behaviour asked for. |

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
| S10 | Note, not changed | The animator is kept for the Revit session, so a new walk (or Q again) starts from the clip it was in last time and blends 0.2 s into Idle - a short settle, not a snap. | Left - cosmetic, no new behaviour asked for. |

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
