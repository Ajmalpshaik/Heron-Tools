# Model Walk: moved from AJ-Tools Game Mode (2026-11-21)

AJ-Tools' **Game Mode** (`src/GameMode/`, 16 files, 5,369 lines, AJ-Tools `master` `435cd6f`) was rebuilt in
Heron Tools as **Model Walk**, a coordination tool: walk the model, point at things, select them, record snags,
hand over a report. The requirements were [game-mode-audit.md](game-mode-audit.md) (44 findings, GM-A01 to GM-A44)
and [game-mode-spec.md](game-mode-spec.md) (KEEP / CHANGE / DROP / ADD and the names). No code is shared with
AJ-Tools or Heron AI; AJ-Tools was read only, never changed.

**Nothing here has run in a real Revit yet.** It builds clean for Revit 2020-2027; every behaviour below
still **needs a real Revit test**.

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
| `Documents\AJ Game Snags`, `Pictures\AJ Game Photos` | `Documents\Heron Model Walk\<model>\` (report and snapshots together) |

## Dropped (spec DROP list)

Gun and its pictures, muzzle flash, shot sound and M; jump and climbing through windows (Space is "up" in Fly
only); flashlight (V); professional mode (N - always professional now); J reset-all; minimap; section box tool.
No gun / weapon / shoot wording is left anywhere a modeller sees.

## Audit findings: fixed or not

| ID | Sev | What was wrong | Now |
|---|---|---|---|
| GM-A01 | Critical | J wiped every override in the view | **J dropped.** "Clear my snag colours" (X) gives back only what Model Walk coloured. Reset Graphics stays the tool for a full reset. |
| GM-A02 | High | Snag overwrote the element's own override, never given back | **Fixed.** `SnagColourMemory` copies each element's own override before colouring; given back on X, at the end of the walk (one Undo step) and before every Save / Save As / Sync with Central. An element whose colour the modeller changed after the snag is left as they set it. Owner decision below (keep marks?). |
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
| GM-A44 | Low | Mouse speed fixed, no invert | **Left for stage 4** (spec ADD 34, Settings window). |

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

## Spec ADD items left for stage 4

- Snag record with a typed comment, per-snag snapshot, CSV (spec 30) - today a plain-text report.
- Open a snag back in Revit / a saved 3D view per snag (31).
- Room name in the location line (32).
- Measure (33).
- Settings window: mouse sensitivity, invert look, speed, eye height, step-up (34, GM-A44).
- Smooth camera ease (35); field of view (36, only after a Revit test).
- Named viewpoints kept per model and "Save as Revit 3D view" (14) - today viewpoints live for one walk, named
  "Viewpoint N - <level>".
- Option to keep snag colours after the walk (22).

## Owner decisions needed

1. **Ribbon availability.** The brief said "3D perspective view". The button is enabled in **any 3D view**
   (perspective or not): the {3D} view is orthographic, so a model with no camera view could otherwise never start.
   Plans are not enabled (the old tool could start from a plan).
2. **Snag colours at the end.** Given back at the end of the walk by default (the report is the record). Keep them?
3. **One walk view per person** in workshared models ("Heron Model Walk - <username>").
4. **Clear snag colours key = X.** J is not reused, so an old Game Mode habit cannot clear anything by surprise.
5. Heron's shared `Constants.MM_TO_FEET` (0.00328084) - make it exactly `1 / 304.8` for every tool?
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
