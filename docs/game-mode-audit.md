# Game Mode audit (AJ-Tools to Heron Tools move)

Audit only. No code was changed in either repo.

- **Source read:** AJ-Tools `main` at `435cd6f` (2026-11-08), `src/GameMode/` (16 files, 5,369 lines), plus
  `src/Helpers/ElementIdHelper.cs`, `src/Helpers/ValidationHelper.cs`, `src/Helpers/Constants.cs`,
  `src/Core/RibbonManager.cs` (Game panel), `src/Resources/Game*.png`, `src/AJ Tools.csproj`,
  `CHANGELOG.md` and `.claude/knowledge/debug-log.md` (Game Mode entries).
- **API checked against:** `Nice3point.Revit.Api.RevitAPI` / `RevitAPIUI` XML docs, 2020.2.90 and 2027.4.0.
- **Not done:** no build, no Revit run. Anything marked **NEEDS REAL REVIT** is a reading of the code
  and the API docs only.

All line numbers are AJ-Tools file lines. "The game view" = the `AJ Game View` perspective 3D view.

## Counts

| Severity | Count |
|---|---|
| Critical | 1 |
| High | 7 |
| Medium | 14 |
| Low | 22 |
| **Total** | **44** |

---

## Critical

### GM-A01 - J wipes every graphic override in the game view, with no way back but Undo
- **Where:** `src/GameMode/GameMotionEngine.Extras.cs:198-204` and `:254-286`; key `J` in
  `GameKeyBindings.cs:104`.
- **What goes wrong:** one key press runs `SetElementOverrides(id, new OverrideGraphicSettings())` on
  **every** element visible in the game view - not only the snag marks. Any colour, halftone, line weight
  or pattern the modeller set by hand in that view is gone. J sits next to H (help) and K (photo), so it is
  easy to hit by mistake. There is no confirm and no memory of the old overrides. The only way back is
  Revit Undo after leaving the game (it is one transaction), which a modeller will not know. On a big
  view it is also one giant transaction over thousands of elements inside a single game frame (Revit
  freezes for seconds). This breaks AGENTS.md "Never overwrite a modeller's graphic overrides without a
  way back" and CONTRIBUTING rule 2.
- **Fix:** J clears **only the marks this tool made** (keep a memory of each element's original
  override when it is snag-marked, like `Services/QuickClash/ClashColourMemory.cs` /
  `Services/Colorize/ColorizeMemory.cs`), and puts the remembered override back. If a "reset all" is still
  wanted, leave it to Heron's existing Reset Graphics tool (it has `ResetGraphicsMemory`).

## High

### GM-A02 - Snag marker overwrites the element's existing override and never puts it back
- **Where:** `GameMotionEngine.Extras.cs:209-247` (line 238 `SetElementOverrides`).
- **What goes wrong:** the red mark replaces whatever override the element had (it does not read
  `GetElementOverrides` first). The marks stay after the game ends; the only "clear" is J (GM-A01), which
  destroys everything else. Snag marks from older sessions cannot be told apart from the modeller's own
  red overrides.
- **Fix:** read and store the original override per element before marking; offer "Clear my snag marks"
  that restores exactly those. Ask the owner whether marks should stay after exit (report is the real
  output) - if not, restore them on exit in one named transaction.

### GM-A03 - Element ids compared and printed as 32-bit int (Revit 2024+ ids are 64-bit)
- **Where:** `CmdGameMode.cs:289`, `:377`; `GameMotionEngine.cs:199`, `:438`;
  `GameCollisionService.cs:162-164`, `:236`. The helper `src/Helpers/ElementIdHelper.cs:67-77` returns
  `(int)id.Value` from 2024 (silent truncation). Heron's own `Helpers/ElementIdHelper.cs:24-34` has the
  same truncation.
- **What goes wrong:** on a model with ids above 2,147,483,647 two different views can compare equal
  (the game keeps running in the wrong view, or picks the wrong `UIView` rectangle), and the Id printed
  in the laser line, the toasts and the **snag report** is wrong - a modeller searching "Select by ID"
  finds nothing or the wrong element. Breaks AGENTS.md "Never compare element ids as int".
- **Fix:** compare `ElementId` objects directly (`==`/`Equals`); print with `LongValue()`
  (`Helpers/ElementIdExtensions.cs:30`). Category checks: compare `BuiltInCategory` via a helper, not
  `(int)`.

### GM-A04 - Start height and level name use `Level.Elevation`, which depends on the level's Elevation Base
- **Where:** `CmdGameMode.cs:312` (start Z in a plan view); `GameMotionEngine.cs:488`, `:500-507`
  (compass level name).
- **What goes wrong:** API docs (2020 and 2027): `Level.Elevation` is relative to the **shared** origin
  when the level type's Elevation Base is "Survey Point". The camera works in internal coordinates. On a
  project with real-world levels (e.g. FFL +10.500 SSL - common on Qatar jobs) the player starts metres
  above or below the floor (falls through the building or hovers with "no floor found"), and the status
  card shows the wrong level name.
- **Fix:** use the level's internal Z (`Level.ProjectElevation` is documented as "relative to project
  origin, no matter the Elevation Base" - **NEEDS REAL REVIT** to confirm it equals internal Z when the
  project base point is moved; otherwise read the level plane's origin).

### GM-A05 - Up to ~19 full-model ray casts every frame, with links on - large models will freeze
- **Where:** `GameMotionEngine.cs:293` (look ray), `:351` (aim ray), `:361-369` (two 500 m clear-height
  rays), `GameMotionEngine.Movement.cs:69-86` (3 rays per try, up to 4 tries = 12), `:102`, `:146`, `:165`;
  `GameCollisionService.cs:99-102` (`FindReferencesInRevitLinks = true`, no element filter); plus
  `RefreshActiveView()` every frame (`GameMotionEngine.cs:343`).
- **What goes wrong:** `ReferenceIntersector.FindNearest` has no distance limit and tests every visible
  element in the host and all links. The "0.1 ms per ray" note is from one test model on Revit 2020. On a
  hospital-size federated model each ray can take tens of ms; at 30 frames/s the add-in can hold Revit's
  UI thread most of the time (laggy walk, Revit feels hung). **NEEDS REAL REVIT** on a big model.
- **Fix:** measure first (the HUD already shows frame ms). Then: a `BoundingBoxIntersectsFilter` around
  the player for collision rays; throttle clear height and compass to ~4 Hz; cast the aim ray only on
  shots / when laser or teleport is active; skip `RefreshActiveView` if `SetOrientation` already redraws
  (**NEEDS REAL REVIT**).

### GM-A06 - No try/catch in the WPF handlers - one exception can take Revit down
- **Where:** `GameHudWindow.xaml.cs:385-454` (`OnTick`, incl. `RenderHud`), `GameHudWindow.Controls.cs:59-111`,
  `:113-175`, `:223-380`, `:382-429`; `GameHudWindow.Render.cs:27-198`.
- **What goes wrong:** the engine frame is guarded, but the timer tick, render and every key/mouse handler
  are not. An unhandled exception on Revit's UI thread in a modeless window can crash Revit, with the
  cursor hidden and captured. **NEEDS REAL REVIT** (how Revit 2020-2027 treat dispatcher exceptions).
- **Fix:** wrap `OnTick` and each input handler; on any exception call `StopGame("...")` so the cursor is
  released and the overlay closes cleanly.

### GM-A07 - Window colours are not from the Heron palette (dozens of hard-coded colours)
- **Where:** `GameHudWindow.xaml` (whole file: `#00C8FF`, `#101722`, `#3CE24A`, `#FFC53D`, `#FF6A3D` ...),
  `GameHudWindow.Weapons.cs:256-403`, `GameHudWindow.Render.cs:121-122`, `:162`,
  `GameKeySettingsWindow.xaml:6` and its buttons, `GameKeySettingsWindow.xaml.cs:103`, `:117-119`, `:144`,
  `:186`, `:200`.
- **What goes wrong:** breaks CONTRIBUTING rule 6 / AGENTS.md "never add a window colour that is not in
  HeronStyles.xaml". Neon blue is AJ-Tools' accent; Heron's one accent is orange `#FD641A`. The Key Settings
  window's cyan "Save and Close" is a second accent colour.
- **Fix:** Key Settings window: use `HeronStyles.xaml` brushes only, one Accent button. HUD: map every colour
  to a palette key or a see-through shade of one (Mist/Ink/Accent/Success/Warning/Error); the five tool
  colours need an owner decision (palette has only Accent/Success/Warning/Error/Mist).

### GM-A08 - The overlay is `Topmost` and covers other apps and Revit's own dialogs
- **Where:** `GameHudWindow.xaml:9` (`Topmost="True"`), full-view window with 1/255 alpha (`:19`).
- **What goes wrong:** while playing, a Revit warning or worksharing dialog can open **under** the
  invisible overlay (the overlay eats the clicks; Revit looks frozen). While paused, the pill floats on top
  of Outlook/Teams/other apps after Alt+Tab. **NEEDS REAL REVIT** (deactivation should pause first, but the
  overlay keeps Topmost).
- **Fix:** do not use `Topmost`; rely on the owner = Revit main window (already set, `CmdGameMode.cs:162-163`).
  If z-order needs help, set Topmost only while the overlay is active and drop it on `Deactivated`.

## Medium

### GM-A09 - "No undo entries / no model changes" claim is false; `Commit()` results unchecked
- **Where:** transactions at `GameMotionEngine.Extras.cs:235` (each snag shot), `:260` (J), `:397` and
  `:427` (hide/unhide fallback), `CmdGameMode.cs:204-258` (view creation, `:257` unchecked). The claims:
  `CmdGameMode.cs:26-28`, `GameMotionEngine.cs:26-29`, CHANGELOG line ~1384.
- **What goes wrong:** every snag shot is its own Undo step ("AJ Tools - Snag Mark" x 40 in the Undo
  list). A refused commit is reported as success (toast says marked; view "created"). Breaks CONTRIBUTING
  rule 1 (check `Commit()`), and the tooltip/docs mislead the modeller.
- **Fix:** check `Commit() == TransactionStatus.Committed` everywhere; consider one TransactionGroup per
  game for all marks (assimilate on exit = one Undo step "Heron Tools - Game Mode marks"). Fix the docs.

### GM-A10 - Cleaner/U: clears the modeller's own Temporary Hide/Isolate, and hidden elements stay hidden after exit
- **Where:** `GameMotionEngine.Extras.cs:385-414` (hide), `:416-444` (`DisableTemporaryViewMode(TemporaryHideIsolate)`),
  `:111-123`.
- **What goes wrong:** U turns off the whole temporary hide/isolate of the view, including what the modeller
  had isolated before starting. When the game ends, cleaner-hidden elements stay temporarily hidden (and so
  stay "not there" for collision next game) with no message.
- **Fix:** remember whether the view already had a temporary mode on start; on U restore only the ids the
  cleaner hid (re-apply the user's isolate if needed), and on exit tell the modeller "N elements still
  temporarily hidden" or restore them. Confirm in Revit whether `HideElementsTemporary` ever needs a
  transaction (**NEEDS REAL REVIT** - API docs do not say).

### GM-A11 - `SetOrientation` is documented as temporary; resume and locked views not handled
- **Where:** `CmdGameMode.cs:115-121` (resume from `GetOrientation`), `GameMotionEngine.cs:333-342`.
- **What goes wrong:** API docs 2020 and 2027: "Sets the temporary orientation ... not saved in the
  document" and throws `InvalidOperationException` when "View is locked". The header says camera writes
  "persist" (verified on 2020 only). After closing and reopening the model, "continue from where you were"
  may jump back to the view's saved camera. On a locked 3D view the game starts and dies on frame 1 with
  "Revit refused the camera move (raw exception text)". **NEEDS REAL REVIT** on 2021-2027.
- **Fix:** check `gameView.IsLocked` at start and tell the modeller in plain words; if the last position
  must survive a reopen, call `SaveOrientation()` once on exit inside one named transaction (owner decision).

### GM-A12 - Document checked by Title only
- **Where:** `GameMotionEngine.cs:184-188`, `GameSession.cs:260` (stores the title, not the document).
- **What goes wrong:** two open models with the same file name (e.g. a central-local and a detached copy, or
  the same name in two folders) look identical; the engine then reads `GameViewId` in the wrong document.
- **Fix:** keep the `Document` reference and compare with `uidoc.Document.Equals(sessionDoc)` (or `PathName`
  plus title), and check `IsValidObject`.

### GM-A13 - The ribbon toggle can get stuck "running" for the rest of the Revit session
- **Where:** `CmdGameMode.cs:86-97`, `:168-169` (`ActiveHud` set **before** `hud.Show()`).
- **What goes wrong:** if `Show()` throws, `ActiveHud` stays set, the timer never starts and the window never
  closes - every later click only calls `StopGame` on a dead window. Game Mode cannot start again until
  Revit restarts. Also the toggle runs only after `ValidateUIDocumentAndView`, so in some views the
  modeller cannot stop the game from the ribbon.
- **Fix:** set `ActiveHud` after a successful `Show()` (or reset it in the catch); handle the toggle
  before view validation.

### GM-A14 - Pause pill and minimum window size are raw pixels, not DPI-scaled
- **Where:** `GameHudWindow.xaml.cs:169-170` (400 x 160 px), `:511-522`, `:573` (min 200 x 150 px).
- **What goes wrong:** WPF content is laid out in DIPs; at 150-200 % display scaling the pill's text
  (sized for 400 x 160 DIPs) is clipped in a 400 x 160 pixel window - "click to continue" may be cut off.
  Mixed-DPI multi-monitor and floating Revit view windows are unproven. **NEEDS REAL REVIT** at 100/150/200 %
  and on a second monitor.
- **Fix:** multiply the pill size by the window's current DPI scale (`VisualTreeHelper.GetDpi`), or size the
  pill to its content.

### GM-A15 - Walking under a pipe, table or handrail pops the player on top of it
- **Where:** `GameMotionEngine.Movement.cs:102-131` (grounded snap; `:130-131` "floor pushed up: snap up").
- **What goes wrong:** the floor ray goes straight down from the eye and treats the first thing it meets as
  the floor. Horizontal body rays sit only at eye, ~800 mm and 300 mm above the floor, so a pipe at
  1,300 mm or a table edge between them is walked into; then the down ray hits it and the player is snapped
  up onto it in one frame. In MEP-heavy plant rooms this happens often. **NEEDS REAL REVIT** to see how often.
- **Fix:** only snap up within `StepUpFt`; above that, treat it as a blocked move (undo the step) instead of
  teleporting up.

### GM-A16 - Body is three thin rays: player can slide into walls and see through them
- **Where:** `GameMotionEngine.Movement.cs:52-88`.
- **What goes wrong:** rays go only along the movement direction from the body centre. A wall at a shallow
  angle, a column corner or a door frame beside the path is missed, so the eye ends up inside or right at a
  face (camera clips into the wall). Also, risers between 300 and 350 mm are blocked by the 300 mm shin ray
  although `StepUpMm` is 350.
- **Fix:** add two side rays at +/- body radius (or a short ring of rays); set the shin ray just above
  `StepUp`.

### GM-A17 - If "AJ Game View" is taken by a non-perspective view, a new unnamed 3D view is made on every run
- **Where:** `CmdGameMode.cs:183-191` (finds perspective views only), `:210-217` (rename failure swallowed).
- **What goes wrong:** the next run does not find the default-named view, so it makes another one: "3D View
  1", "3D View 2"... piling up in the Project Browser with no message.
- **Fix:** if the name is taken, use a numbered free name ("Heron Game View 2") and tell the modeller;
  or find the view by a stored id/extensible-storage marker.

### GM-A18 - Worksharing: one shared game view for the whole team (NEEDS REAL REVIT)
- **Where:** `CmdGameMode.cs:114-128`, `:204-258`.
- **What goes wrong:** the game view is a model element. In a workshared model the first user creates and
  borrows it; teammates reuse the same view. Snag marks / J / hide on a view owned by someone else will be
  refused (toast only), and marks one person makes appear in everyone's game view after sync. Not tested.
- **Fix:** test on a central model with two users. Likely: one game view per user (name with the user
  name), and refuse snag/J clearly when the view is not editable (`WorksharingUtils.GetCheckoutStatus`).

### GM-A19 - Assets are loaded from a `Resources` folder beside the DLL; 2.7 MB of pictures
- **Where:** `GameHudWindow.Weapons.cs:36-46`, `:98-106`, `:149-157`; `src/AJ Tools.csproj:57-60`
  (Content copied to output). Files: `GameGun.png` 1.35 MB, `GameSnagGun.png` 0.78 MB, `GameRifle.png`
  0.56 MB, `GameMode.png` icon.
- **What goes wrong:** Heron Tools embeds its icons (`HeronTools.csproj:39`, `Helpers/BrandImages.cs`), so a
  straight copy finds no files and silently falls back to the vector pistol. The pictures' origin/licence is
  not recorded (comments say "Ajmal's own picture", one from `Y:\Ajmal Ps\icon\`).
- **Fix:** embed as resources (or drop the pictures - see the wording list); record their source in NOTICE.

### GM-A20 - Screenshot and sound need Windows Forms / System.Drawing - Heron's project does not reference them
- **Where:** `GameHudWindow.Photo.cs:52-59` (`System.Drawing.Bitmap`, `CopyFromScreen`),
  `GameHudWindow.Weapons.cs:499-523` (`System.Media.SoundPlayer`). AJ-Tools has `UseWindowsForms` +
  `System.Drawing` (`src/AJ Tools.csproj:23`, `:54`); `src/HeronTools/HeronTools.csproj` has only `UseWPF`.
- **What goes wrong:** the port will not compile for 2025-2027 (net8/net10) and likely 2020-2024 without
  new references. **NOT RUN** (no build here).
- **Fix:** photo: capture with WPF (`RenderTargetBitmap` cannot see Revit's view, so use `CopyFromScreen`
  behind a `REVIT` helper or add `UseWindowsForms`). Sound: drop it or add the reference. Build all years.

### GM-A21 - Exit leaves the modeller in the game view, and raw exception text in messages
- **Where:** `CmdGameMode.cs:140`, `:144`, `:178`; `GameMotionEngine.cs:340`, `:378`.
- **What goes wrong:** after exit the modeller stays in "AJ Game View" (their working view is a tab behind);
  errors show `ex.Message` only ("Game error: Object reference not set..."), against CONTRIBUTING "never a raw
  exception alone".
- **Fix:** remember the view the modeller started from and offer/return to it on exit; plain-words messages
  with Revit's text as detail.

### GM-A22 - HUD position, saved positions and snag report use internal coordinates
- **Where:** `GameMotionEngine.cs:426-430`, `GameHudWindow.Render.cs:164-170`, `GameMotionEngine.Extras.cs:141-147`.
- **What goes wrong:** the X/Y/Z mm shown and written to the snag report do not match Revit's Spot
  Coordinates / project coordinates, so a coordinator cannot use them to find the snag.
- **Fix:** convert through the project base point or shared coordinates (`ProjectLocation`) and say which
  ("Shared coordinates, mm"); or report the level + nearest grid instead.

## Low

| ID | Where | What goes wrong | Fix |
|---|---|---|---|
| GM-A23 | `GameHudWindow.Controls.cs:355`, `:362` vs `GameMotionEngine.cs:270`, `GameSession.cs:188` | Speed limits disagree: HUD 0.4-3.0, engine clamp 0.2-4.0, comment 0.4-3.0. | One constant in `GameTuning`. |
| GM-A24 | `GameKeySettingsWindow.xaml.cs:112`, `:185`, `:199` vs `GameKeyBindings.cs:138-163` | Settings buttons show raw names ("LeftShift", "OemPlus"); the HUD shows "Shift", "+". | Use `Label()` in the window. |
| GM-A25 | `GameKeyBindings.cs:94`; `Controls.cs:296`, `:412` | Run is bound to Left Shift only; Right Shift does nothing. Session comment says "C or Ctrl" crouch - Ctrl is not bound (`GameSession.cs:153`). | Treat Left/Right modifiers as one key; fix the comment. |
| GM-A26 | `GameKeyBindings.cs:243` | `Enum.TryParse` accepts numbers ("999") and unknown values - an edited file can bind a key that never fires. | Also check `Enum.IsDefined`, and reserved keys. |
| GM-A27 | `GameKeyBindings.cs:244-255` | Loading a file that moves a key to another action leaves the old holder with "(no key)" - no fallback. | After load, give any keyless action its default if free, else warn. |
| GM-A28 | `GameKeySettingsWindow.xaml.cs:171-181` | No "Reset to defaults" and no swap: to swap W and S you must go via a spare key. | Add Reset defaults; offer swap on conflict. |
| GM-A29 | `GamePrefs.cs:57-63`; `GameKeyBindings.cs:287-292` | Two loose files in `%AppData%\AJTools\`; prefs save rewrites the whole file with one line. | Use Heron's `Helpers/AppDataConfigStore.cs` (`%AppData%\HeronTools\`); one settings file. |
| GM-A30 | `GameHudWindow.Photo.cs:35-44` | File name to the second - two photos in one second overwrite each other. HUD may still be on screen when captured (one Background dispatch). **NEEDS REAL REVIT**. | Add milliseconds / counter; wait for a render pass before capture. |
| GM-A31 | `GameHudWindow.Weapons.cs:515` | New `SoundPlayer` + stream per shot (7-8 per second on auto fire), never disposed. | One player, reuse. |
| GM-A32 | `GameMotionEngine.cs:480-492` | Levels cached once per game - levels added during pause are ignored. | Refresh with the intersector (every 4 s). |
| GM-A33 | `GameMotionEngine.cs:494-497` | Compass "N" is project north (+Y), not true north. | Say "Project N", or use `ProjectLocation` angle. |
| GM-A34 | `GameMotionEngine.cs:387-400` | F pressed while in Ghost goes to Walk, not Fly. | Toggle Fly from any mode. |
| GM-A35 | `GameMotionEngine.cs:297-301` | E (door) moves 900 mm past the door with no collision check - can land inside a wall, furniture or shaft. | Check the landing with the same rays; refuse if blocked. |
| GM-A36 | `GameMotionEngine.Extras.cs:34-49` | Teleport can land above a ceiling or inside a ceiling void. | Drop the landing to the floor below / refuse if no floor. |
| GM-A37 | `GameHudWindow.xaml.cs:427`, `:489` | `ExternalEvent.Raise()` result (Denied / TimedOut) ignored. | Count denials; stop with a plain message. |
| GM-A38 | `CmdGameMode.cs:79-97` | The stop toggle sits after view validation (see GM-A13). | Move the toggle first. |
| GM-A39 | `src/Helpers/Constants.cs:48` | `MM_TO_FEET = 0.00328084` (rounded; exact is 1/304.8). Error is tiny but reports drift by ~0.3 mm per 10 km. | Use `/ 304.8` (Heron already has `FEET_TO_MM = 304.8`). |
| GM-A40 | `GameHudWindow.Render.cs:31` | "frame 33 ms" debug line shown to every modeller. | Hide behind a debug key or remove. |
| GM-A41 | `CmdGameMode.cs:195-198` | Takes the first 3D view type; new view starts with no template and default VG. | Fine, but say so in the tooltip. |
| GM-A42 | `GameHudWindow.xaml:276-278`, `:293-294`; `GameKeySettingsWindow.xaml` footer | "Created & All Rights Reserved @ Ajmal P.S." credit line and "AJ" names everywhere. | Use Heron branding (About window covers credit). |
| GM-A43 | `GameMotionEngine.Movement.cs:79-82` | Windows never block while airborne - jumping anywhere passes through any window family, even high or fixed ones. By design, but undocumented. | Keep, but state it in the help card. |
| GM-A44 | `GameHudWindow.Controls.cs:104-105` | Mouse sensitivity fixed (0.15 deg/px), no invert-Y setting. | Add to settings later (owner decision). |

---

## Wording that may look unprofessional for a BIM tool (listed, not decided)

From the HUD, help card, toasts, tooltip (`RibbonManager.cs:365-378`) and key names:

- "Game Mode", "GAME ENDED", "Thanks for playing", "Click here to continue playing", "respawn".
- "gun", "pistol", "rifle", "blaster", "weapon", "Weapon: away", "holster", "draw".
- "shoot", "shot", "fire", "automatic fire", "gunfire", "hold left click to fire", "bullet", "tracer",
  "muzzle flash", "recoil", "impact sparks", synthesized "gunshot" sound.
- "CLEANER - shot hides the element", "SNAG MARKER - shot paints red", "SELECTOR - shot selects",
  "Hidden: ...", "victim" (variable name only, `Extras.cs:90`, `:129`, `:162`).
- "GHOST (through walls)", "Professional mode - no gun shown", "guns are back", "NO GUN".
- Gun pictures (`GameGun.png`, `GameRifle.png`, `GameSnagGun.png`) and the vector pistol drawing.
- Possible neutral set for the owner to consider: Walkthrough / Pointer / Measure / Hide / Mark issue /
  Select; "Presentation mode" as the default.

## Port dependency list (AJ-Tools types Game Mode needs)

Game Mode uses **no Heron AI code**. These are the AJ-Tools pieces it needs; each has a Heron Tools
equivalent or must be replaced:

| AJ-Tools type / file | Used for | Heron Tools status |
|---|---|---|
| `AJTools.Utils.ElementIdHelper.GetIntegerValue` (`src/Helpers/ElementIdHelper.cs`) | view id compare, category check, printed Id | Exists in `Helpers/ElementIdHelper.cs` with the **same int truncation** - use `ElementId` compare + `LongValue()` instead (GM-A03) |
| `AJTools.Utils.ValidationHelper.ValidateUIDocumentAndView` | command start check | Exists (`Helpers/ValidationHelper.cs:74`) |
| `AJTools.Utils.DialogHelper.ShowError` | start errors | Exists (`Helpers/DialogHelper.cs:32`, Heron message window) |
| `AJTools.Utils.Constants.MM_TO_FEET` | all distances | Exists (`Helpers/Constants.cs:20`); prefer `FEET_TO_MM` |
| `AJTools.Helpers.WindowMotionHelper` (Attach entrance/exit) | Key Settings window | Exists (`Helpers/WindowMotionHelper.cs:51`, `:149`) |
| `AJTools.Core.RibbonManager` (`BuildGamePanel`, `AddGameModeTool`) | ribbon button + tooltip | Re-do in `App/HeronToolsApp.cs` (join Heron tab, no availability class today - add one: project document only) |
| `src/Resources/GameMode.png` (ribbon icon) | ribbon | Needs a Heron-style icon, embedded (`Resources/Icons`) |
| `src/Resources/GameGun.png`, `GameRifle.png`, `GameSnagGun.png` | HUD pictures, from disk | Embed or drop (GM-A19) |
| `System.Drawing` / Windows Forms (`CopyFromScreen`) | photo (K) | Not referenced by `HeronTools.csproj` (GM-A20) |
| `System.Media.SoundPlayer` | shot sound | Not referenced by `HeronTools.csproj` (GM-A20) |
| `%AppData%\AJTools\ajgame-keys.txt`, `ajgame-prefs.txt` | key map, professional mode | Move to `Helpers/AppDataConfigStore.cs` (`%AppData%\HeronTools\`); decide whether to migrate old files |
| Namespaces `AJTools.Commands.GameMode`, `AJTools.Services.GameMode`, `AJTools.UI.GameMode` | everything | Rename to `HeronTools.*`; `CmdGameMode.ActiveHud` is referenced from `GameHudWindow.xaml.cs:375-378` |
| File headers (AJ-Tools `#region Metadata` blocks) | - | Replace with the five Heron fields (`docs/metadata-standard.md`); the checker refuses files without them |
| View name "AJ Game View", transaction names "AJ Tools - ..." | model content | Owner decision: new name ("Heron Game View")? Models that already have "AJ Game View" would get a second view unless both names are searched |
| User32 `GetCursorPos` / `SetCursorPos` / `SetWindowPos` (nested `GameNative`) | mouse look, overlay | Self-contained, copy as is |

## Must-keep behaviours (they work and must survive the move)

1. **All Revit API work runs in the `ExternalEvent` handler** (`GameMotionEngine : IExternalEventHandler`);
   the WPF timer only raises the event and never touches the model. View creation is in the command.
2. **Every engine frame is guarded**: any exception ends the game cleanly with a reason (`GameMotionEngine.cs:376-379`).
3. **Game ends by itself** when the document closes, another project/view becomes active, the game view is
   deleted or its window closes (`GameMotionEngine.cs:177-203`, `:474`).
4. **Camera moves create no Undo entries** (`SetOrientation` without a transaction) - only creating the view,
   and the mark/reset/hide features, change the model.
5. **Collision uses only what is visible in the game view** - VG, filters, hide-in-view and the **section box**
   shape the world (API docs confirm ReferenceIntersector skips hidden elements and geometry outside the
   section box). Linked models collide too, and linked hits are resolved to the real linked element.
6. **dt clamped to 0.005-0.25 s** and fall speed capped (`GameMotionEngine.cs:212-215`, `Movement.cs:136-139`).
7. **Landing check measures from the pre-step eye** so a fast fall cannot pass a slab (fixed 2026-10-12, `Movement.cs:156-171`).
8. **No-floor hover** instead of falling forever in floorless (MEP-only) models; absolute respawn safety net.
9. **Mouse-look re-centres the cursor whenever the view rectangle moves** - the SELECTOR "camera whip" fix
   (debug-log 2026-08-05, `GameHudWindow.xaml.cs:542-574`), cursor centred **before** look is armed
   (`Controls.cs:27-36`), and the 400 px step backstop (`Controls.cs:88-93`).
10. **ZoomToFit on start, resume and every rectangle change** so the crosshair matches the real aim
    (aim/display sync fix, `GameMotionEngine.cs:450-466`). Do not remove it.
11. **Pause on Esc, on losing focus, on losing mouse capture**: held keys cleared, cursor released, overlay
    shrinks to a pill, Revit fully usable; resume re-fits the view.
12. **Snag report written exactly once on every exit path** (Esc, ribbon toggle, overlay closed, engine stop),
    and a failed write is shown, not swallowed (fixed 2026-10-12).
13. **Key-map load accepts swapped layouts** (fixed 2026-10-12, `GameKeyBindings.cs:241-256`); hints, help
    card and toasts show the key actually bound; Key Settings Cancel restores the old map.
14. **Ribbon button toggles** (second click stops the game); one game at a time.
15. **Family documents refused**; new game view made with Crop View and crop boundary off, far clip off;
    an existing game view is reused untouched.
16. **Linked elements are refused** by hide/mark/select with a plain toast (host elements only).
17. Laser identity line: category, family : type, size, system, level, Id, "(linked)".
18. Selection made with the SELECTOR stays after exit (no transaction).
19. Every distance tuned in mm in one place (`GameTuning`).

## Result states

| Check | State |
|---|---|
| Line-by-line read of all 16 Game Mode files + helpers + ribbon wiring | PASS |
| API facts (SetOrientation temporary/locked, ReferenceIntersector visibility + section box, Level.Elevation base, ElementId.IntegerValue gone in 2027) against Nice3point XML 2020 + 2027 | PASS |
| Build of AJ-Tools or a Heron port | NOT RUN (audit only; no Windows build here) |
| Performance, DPI, worksharing, locked view, Topmost, crash on handler exception, resume after reopen | NEEDS REAL REVIT |
