# Model Walk: target spec (Game Mode moved from AJ-Tools)

Status: **research spec, no code yet.** Written 2026-11-21 from AJ-Tools `master` `435cd6f`
(`src/GameMode/`, 16 files, about 5,400 lines) and the Revit API notes in the `Nice3point.Revit.Api`
2024 packages. Nothing here is proven in Revit; every "needs real Revit" line below is still owed.

The goal: the AJ-Tools Game Mode becomes a tool a BIM coordinator can open in a client meeting or a
coordination meeting without apology. Walk the model, point at things, mark issues, hand over a report.
Nothing that looks like a video game.

---

## A. What Game Mode does today (inventory)

**How it works.** One perspective 3D view ("AJ Game View") is created once and reused. A transparent
window sits exactly over the Revit view. A 33 ms timer (about 30 frames a second) reads keys and mouse
and raises an `ExternalEvent`; the handler moves the camera with `View3D.SetOrientation` (no
transaction, no undo entry) and calls `RefreshActiveView`. Collision, floor finding and aiming all use
one `ReferenceIntersector` on the game view, with links on - so only what is **visible** in that view
blocks you. The intersector is rebuilt every few seconds to pick up VG changes. The game ends by itself
if you leave the view, close it or change document.

**Movement (tuning in `GameSession.cs`)**

| Item | Today |
|---|---|
| Walk | W A S D, mouse look (0.15 degree per pixel, pitch held to 85 degrees), 3.5 m/s |
| Run | Shift held, 3 x speed |
| Speed | + / - live speed factor |
| Jump | Space, about 1.15 m - used to climb through windows |
| Crouch | C held, eye 1000 mm (normal eye 1700 mm) |
| Gravity and stairs | Gravity on in walk mode; steps up to 350 mm walked up and down; body radius 350 mm |
| Fly | F on/off, 7 m/s, Space up, C down |
| Ghost | G on/off - walk through everything |
| Doors | E within 1.6 m of a door steps you through it |
| Respawn | R back to the start point |
| Teleport | T held shows an aimed arc, release to jump there |
| Saved positions | B saves (unlimited), number keys jump back, list on the left |
| Tour | O flies smoothly through every saved position |

**"Weapons" (right-click cycles)**

| Weapon | Today | Model change |
|---|---|---|
| Gun | Picture of a gun, automatic fire, muzzle flash, shot sound. Visual only | none |
| Laser | Live distance in mm, plus element category, family/type, id, system, level (links resolved) | none |
| Cleaner | Temporarily hides the element hit; U brings all back | Temporary Hide/Isolate |
| Snag | Paints the element red and adds it to a punch list | element override, one transaction each |
| Selector | Adds or removes the element in Revit's real selection, kept after the game | none |

**Other keys:** K clean screenshot (HUD hidden one frame, raw screen pixels) to `Pictures\AJ Game Photos`;
V flashlight "night mode" (HUD dark overlay); J resets **every** element override in the view;
N "professional mode" (hides the gun, all tools still work); M sound on/off; H help card; P pause;
Esc pause (overlay shrinks to a pill so Revit is usable), Esc again exits; Esc then S opens Key Settings
(every action remappable, saved to `%AppData%\AJTools\ajgame-keys.txt`).

**HUD today:** crosshair, weapon picture, laser readout, compass and level line, saved-position list,
toasts, snag count, help card. Colours are hard-coded (for example `#9FD0FF`), not a brand palette.

**On exit:** a plain-text snag list to `Documents\AJ Game Snags\Snags_<date>.txt` (project, date, one
line per snag from the laser description).

**Already tried and removed by the owner** (do not bring back without asking): a follow-me section box
("no need"), and screen-projected measuring ("not working properly").

---

## B. KEEP / CHANGE / DROP / ADD

Strict rule used: keep what helps someone find, show or record a coordination problem; cut what is there
for fun.

| # | Item | Verdict | Reason |
|---|---|---|---|
| 1 | Real Revit perspective view, VG/filters/section box shape the walk | **KEEP** | The best idea in the tool: the coordinator already knows how to set up a view. |
| 2 | Collision only against visible elements (links included) | **KEEP** | Hide a category, walk through it - easy and honest. |
| 3 | No transaction, no undo entry for camera moves | **KEEP** | `SetOrientation` is "temporary, not saved in the document" (API note) - nothing to undo. |
| 4 | W A S D + mouse look, Shift run | **KEEP** | The standard walk control in every serious walkthrough viewer. |
| 5 | + / - speed | **CHANGE** | Fixed steps (for example 5 steps, 0.5 to 8 m/s) shown in the HUD; the mouse wheel also steps. |
| 6 | Gravity, 350 mm step-up, body radius | **KEEP** | Needed to walk stairs and ramps like a person. Make step-up and eye height settings. |
| 7 | Crouch (C, 1000 mm) | **CHANGE** | Becomes "Eye height": a setting (1000-2000 mm, default 1700) plus C to toggle a low eye for ceiling-void checks. |
| 8 | Fly (F) | **KEEP** | Needed for ceiling voids, risers and roofs. Space up, C down. |
| 9 | Ghost (G) | **KEEP** | Rename "Pass-through". Needed to get past a locked room or a clash. |
| 10 | Doors with E | **KEEP** | Real doors are often closed in the model; walking through them must be easy. |
| 11 | Jump (Space) and climbing windows | **DROP** | Game behaviour. Space stays "up" in fly mode only. |
| 12 | Respawn (R) | **CHANGE** | Rename "Back to start". Same key. |
| 13 | Teleport arc (T) | **CHANGE** | Keep the action, drop the arc look: "Go to point" - aim at a floor, a ring shows the landing spot, release to go. Very useful in big plant rooms. |
| 14 | Saved positions (B, numbers) | **CHANGE** | Rename "Viewpoints". Give each a name, keep them per model (not per session), and allow "Save as Revit 3D view" for a meeting. |
| 15 | Tour (O) | **CHANGE** | Rename "Play viewpoints". Smooth move between viewpoints, pause at each, any key stops. Good for a client walk. |
| 16 | Gun weapon | **DROP** | No coordination value; a gun on a client's screen is unprofessional. |
| 17 | Gun pictures, muzzle flash, shot sound, M (sound) | **DROP** | Same reason. No sound at all. |
| 18 | Professional mode (N) | **DROP** | The tool is always professional - nothing left to hide. |
| 19 | Flashlight night mode (V) | **DROP** | Fake darkness tells nothing about the model. |
| 20 | Laser | **CHANGE** | Becomes the default mode "Inspect": live readout of what is under the crosshair (category, family : type, id, level, system, link name) and distance from the eye. |
| 21 | Cleaner + U | **CHANGE** | Rename "Hide" / "Show hidden". Keep Temporary Hide (never permanent). Remember what this session hid, so "Show hidden" brings back only those, not the modeller's own temporary hide. |
| 22 | Snag (red override) | **CHANGE** | Rename "Snag". Before colouring, remember the element's own override and give it back on "Clear snags" and on exit (option to keep). One Undo step per snag. Never paint a linked element (it cannot be overridden alone) - record it only. |
| 23 | J resets every override in the view | **DROP** | Breaks the rule "never overwrite a modeller's overrides without a way back". "Clear snags" (item 22) replaces it. |
| 24 | Selector | **KEEP** | Rename "Select". Carries elements straight back into Revit for editing - strong for coordination. |
| 25 | Snag report (plain .txt) | **CHANGE** | See item 30. |
| 26 | Photo (K) | **CHANGE** | Rename "Snapshot". Same clean capture, saved next to the report in a folder named for the model and date. |
| 27 | Key settings (Esc then S) | **KEEP** | Office keyboards differ. Move to `%AppData%\HeronTools`, rebuilt in the Heron window style. |
| 28 | Help card (H), pause (P / Esc) | **KEEP** | Pause keeps Revit usable mid-walk - change VG, then carry on. |
| 29 | Compass + level line | **KEEP** | Becomes the HUD's location block (section D). |
| 30 | Snag record | **ADD** | Each snag stores: number, comment (typed in a small box on the spot, Enter to save, Esc to skip), element id (64-bit), unique id, category, family : type, level, system, link name, eye position and direction, snapshot file, time, author. Saved as CSV (opens in a spreadsheet) plus one picture per snag. |
| 31 | Snag back to Revit | **ADD** | "Open snag": from the snag list, put the camera back and select the element. Optional: one saved 3D view per snag for the issue meeting. |
| 32 | Room in the location block | **ADD** | "Level 03 - Room 3.12 Plant Room" tells the client where they are. Needs a real Revit test for rooms inside links. |
| 33 | Measure | **ADD** | Point-to-point by two hits of the ray (distance from the two hit points, plus vertical and horizontal parts), text only in the HUD - no line drawn on screen. This avoids the screen-projection problem that made the old measure fail. Clearance check above a duct is a daily coordination ask. |
| 34 | Mouse sensitivity, invert look, speed, eye height, step-up | **ADD** | One small Settings tab; saved per user. Helps people who feel sick with fast turning. |
| 35 | Smooth camera | **ADD** | Ease the start and stop of moves and turns over a few frames; keep look itself direct (no lag). |
| 36 | Field of view setting | **ADD - only if a real Revit test proves it** | The API has no field-of-view property; it is set through the perspective crop box size. Test before promising it. |
| 37 | Minimap (plan drawn in the corner) | **DROP for now** | Drawing a plan every frame is heavy and the location block gives most of the value. Revisit later. |
| 38 | Section box tool | **DROP** | The owner removed it before; the view's own section box already works with the walk. |

---

## C. Names (one recommendation)

| Today | Heron Tools |
|---|---|
| Game Mode (ribbon button) | **Model Walk** - Coordination panel, button text "Model\nWalk", toggle (click again to stop) |
| "AJ Game View" | **"Heron Model Walk"** perspective view (an old "AJ Game View" is left alone, never reused or renamed) |
| Weapons | **Modes** (right-click or Tab to change, see note) |
| Laser | **Inspect** (default mode) |
| Selector | **Select** |
| Snag | **Snag** |
| Cleaner | **Hide** |
| Saved positions | **Viewpoints** |
| Tour | **Play viewpoints** |
| Teleport | **Go to point** |
| Respawn | **Back to start** |
| Ghost | **Pass-through** |
| Photo | **Snapshot** |
| Punch list / snag report | **Snag report** |

Note: today the number keys jump to saved positions. Keep that, and change modes with right-click and
Tab (Shift+Tab back) - so a meeting presenter never jumps by mistake.

"Model Walk" is chosen over "Walkthrough" because Revit already has its own Walkthrough command
(a camera path), and two things with one name confuse modellers.

---

## D. HUD layout (HeronStyles colours only)

Rule: the model is the star; the HUD is small, quiet and at the edges. One orange thing only: the
active mode chip. Text is Mist on a see-through Ink plate so it reads on any model colour.

```
+--------------------------------------------------------------------------+
| [LOCATION]                                              [MODE CHIPS]     |
|  Level 03 - Room 3.12 Plant Room                    Inspect Select Snag Hide
|  Facing NE  -  Walk  -  Speed 2/5                                        |
|                                                                          |
| [VIEWPOINTS]                                                             |
|  1 Entrance                         +                                    |
|  2 Plant room                  (crosshair)                               |
|  3 Riser A                                                               |
|                                                                          |
|                        [INSPECT READOUT]                     [SNAGS  4]  |
|          Ducts - Rectangular Duct : Supply  -  ID 1234567                |
|          Level 03  -  SA-03  -  Link: MEP.rvt      4 250 mm              |
|                    [toast: Snag 4 saved]           H Help  Esc Pause     |
+--------------------------------------------------------------------------+
```

| Element | Brush keys (`HeronStyles.xaml`) |
|---|---|
| Plates behind every text block | `OverlayBackground` (Ink 80 %), edge `BorderSoft` |
| Main text (location, readout line 1) | `TextPrimary` (Mist 92 %) |
| Second lines, viewpoint list, key hints | `TextSecondary` (Mist 72 %), hints `TextDisabled` (Mist 55 %) |
| Crosshair | `TextPrimary` with a 1 px `OverlayBackground` outline so it shows on white walls |
| Active mode chip | `Accent` fill, `TextOnAccent` text - the only orange on screen |
| Other mode chips | `AccentGray` fill, `TextSecondary` text |
| Distance number | `TextPrimary`, larger size, brand font |
| Snag counter | `AccentWarning` (needs attention) |
| Toast "saved" / "report written" | `AccentSuccess`; failure toast `AccentDanger` |
| Snag comment box | `CardBackground` (Surface) with `BorderStrong` edge |
| Paused pill | `CardBackground`, `TextPrimary` "Paused - click to continue" |
| Snag colour on the element | `AccentDanger` (Error #FF5A4E) as the override colour - a model colour, not a window colour, but still from the palette |

Font: the brand font (`BrandFont`). No pictures of tools, no animation beyond a short fade of toasts.
A "Clean screen" key (default Z) hides everything except the crosshair; Snapshot always hides the HUD for its one frame.

---

## E. Top risks

| # | Risk | What to do |
|---|---|---|
| 1 | **Frame rate in big models.** Each frame is a `SetOrientation` + `RefreshActiveView` + several rays. Redraw time grows with the model; 30 frames a second may not hold. | Raise the `ExternalEvent` only when the camera must move (no raise while standing still); count rays per frame; show frames per second in a debug key; test on a real large MEP model. NEEDS REAL REVIT. |
| 2 | **`ExternalEvent` waits when Revit is busy.** The API note says events run only when no command or edit mode is active, at Revit's next idle cycle. | Treat a late frame as normal: move by real elapsed time, never by frame count, so the speed stays right. Never call the API from the timer itself. |
| 3 | **Camera position is not saved.** `SetOrientation` is temporary. `SaveOrientation` exists but **locks** the view. | Store the last camera and the viewpoints in Heron Tools' own user file (per model), not by locking the view. |
| 4 | **Overriding the modeller's graphics.** Old Snag + J could wipe their colours. | Remember and give back (item 22), the same way Highlight and Quick Clash do; reset before Save and Sync like Quick Clash. |
| 5 | **Element ids.** Report and selection must use 64-bit ids (Revit 2024 and later) and unique ids; linked elements need link instance + linked id. | Use the existing Heron id helpers; never `int`. |
| 6 | **Overlay window and mouse capture.** Moving the pointer back to the centre each frame (`SetCursorPos`) and a top-most window can trap the user, fight Alt+Tab, or sit wrong on mixed-DPI screens. | Esc always frees the mouse; losing focus pauses at once; place by device pixels (today's approach works); test on two screens with different scaling. NEEDS REAL REVIT. |
| 7 | **Field of view** has no direct API. | Do not promise it until a real test changes the crop box and the picture still lines up with the crosshair. |
| 8 | **Both add-ins installed.** AJ-Tools Game Mode and Heron Model Walk could run at once in one Revit. | Different view name, different settings folder; Model Walk refuses to start if another walk overlay owns the view. Removing Game Mode from AJ-Tools needs the owner's typed "yes". |
| 9 | **Workshared models.** Creating the walk view is a model change that others see after sync. | Say so the first time; one Undo step "Heron Tools - Model Walk view". |
| 10 | **Rays through links on 2020-2022.** Link handling differs by year (Quick Clash already found this). | Test Inspect, collision and Snag on a linked model in 2020 and 2024+. |

**State today:** inventory and API notes - PASS (read from source and the API XML notes).
Every behaviour in section B - NOT RUN (no code yet). Frame rate, mouse capture, field of view,
rooms in links - NEEDS REAL REVIT.
