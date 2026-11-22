# Changelog

All notable changes to Heron Tools. Dates are yyyy-mm-dd.

## [Unreleased]

### Added - Auto Dimension (Datums panel) (NEEDS A REAL REVIT TEST)
- **Auto Dimension** drop-down: **Grids**, **Levels**, **Grids and Levels**, **Settings**. Moved from AJ-Tools'
  Automatic Dimension and rebuilt: each set of parallel grids or levels gets an overall and a chain, **just inside the
  grid and level heads** (and always inside the crop), at the bubble end, the other end or both. One Undo step per run;
  a run that places everything ends quietly. Notes on what was fixed: [docs/auto-dimension-move-notes.md](docs/auto-dimension-move-notes.md).
- Grids at an angle to the screen get their own rows; callouts of sections are allowed; select grids or levels first
  to dimension only those; "leave out" names are whole names (`1` no longer leaves out 10 and 21), with `*` and `?`.

## [1.2.1] - 2026-11-22

Model Walk fixes from the owner's first test on a big linked model (issue #104), plus jump. Release notes:
[docs/releases/1.2.1.md](docs/releases/1.2.1.md). Published as a pre-release: every item below still needs a real Revit test.

### Fixed - Model Walk (NEEDS A REAL REVIT TEST)
- **Third-person character missing in a big model far from the origin:** where he stands is now set before Revit's
  drawing service is switched on (Revit could take his box at the internal origin, off screen). When Revit's own
  near cut in a very big model is farther than the character, the walk says so once instead of showing nothing.
- **Building cut away:** a walk that starts outside the walk view's section box switches that section box off (the walk
  view only, one Undo step, and the walk says so); far clipping is switched off. A box you start inside stays.
- **No annotation in the walk view:** levels, grids, scope boxes, reference planes, marks, tags, dimensions and text are
  hidden in the walk view (only), and a view template is taken off it - in the same one Undo step.
- **Doors in linked models** (and doors whose panel is a nested family): the door hint shows and E goes through.
- **Snags on linked elements** get a red box on screen (and in the snag picture), since Revit cannot colour a single
  element inside a link. Hide on a linked element says so plainly; Select works inside a link on Revit 2023 and later.
- **Jump is back:** Space in Walk jumps (Settings: Jump height, standard 500 mm, 0 = off); it stops under a slab or duct,
  never through it, and lands on steps. Space still rises in Fly and Pass-through.
- **Start on a roof:** the location line says "on a roof (outside)" and the walk says once how to get inside.
- **"Something Revit could not name"** in the readout now names the link it is in (and the ID when Revit has one).
- Long messages stay on screen long enough to read (up to 9 s).
- **F12** shows a second line for the character: drawn or why not, Revit's near cut, the section box, and where his feet
  are (internal and shared).

## [1.2.0] - 2026-11-22

In short: the new **Model Walk** tool (first and third person with a real character, quick snags, viewpoints), a full
code review of every other tool (issues #18-#37 fixed), and exact mm-to-feet. Release notes:
[docs/releases/1.2.0.md](docs/releases/1.2.0.md).

**Model Walk is DRAFT: it compiles for every Revit year but has not yet been tested in Revit.** Every fix below also
still needs a real Revit test.

### Added - Docs
- **Tool catalogue** (`docs/tools/`): one page per ribbon panel - what each tool does, when to use it, step by
  step, what it changes in the model, how to undo it, and its limits - with a picture of the ribbon.

### Changed - Docs
- **README** rewritten for a modeller first: what Heron Tools is, who it is for, a picture of the ribbon, every tool in
  one line with a link to its catalogue page, install in four steps, where to get help.
- **Install guide** (`docs/install.md`): download, install, check it loaded, where the files go, update, remove,
  first time in Revit, office install without a window, common problems, and building from source.
- **Release notes** for each version in `docs/releases/`; the release page shows them, with `SHA256SUMS.txt`
  to check the download.

### Added - Model Walk (NEEDS A REAL REVIT TEST)
- **Save a snag as a 3D view:** each snag row in the pause menu has **Save as 3D view** - a real Revit 3D view
  "Model Walk - Snag 003 - <comment>" looking from where you stood, with the walk view's Visibility/Graphics, without
  the snag red. One Undo step (#77).

### Changed - Model Walk (NEEDS A REAL REVIT TEST)
- **Model Walk docs:** the spec (`docs/game-mode-spec.md`) now says what was built, item by item; the Revit test
  script in `docs/model-walk-move-notes.md` is one numbered list, each step with a results row.
- **Snag pictures and pause:** Esc right after a quick snag now waits for that snag's picture (about 0.1 s) instead of
  dropping it; a pause Revit forces says in the pause menu if a snag was left without its picture. A snag click during
  a Snapshot (K) now says the snapshot is being taken. Esc waits for a Snapshot too, and a picture is never taken with
  the pause or end card in it (it is skipped and said instead).
- **Third-person camera:** a walk that ended while Revit was busy no longer starts the next walk 3 m behind you, and
  a click while turning fast now acts on what was under the crosshair.
- **Key Settings**: Save now always uses the new keys for this walk and says "Keys saved" (a file that cannot be
  written shows a message, as Settings does); the window, help card, Settings window, guide and ribbon tooltip now
  describe every key and setting as the code does (Pass-through rises and sinks like Fly, remapped keys, Enter fixed).
- **Model Walk messages while paused** ("Settings saved", "Keys saved", why a save failed, snag colours given back on
  Save / Sync) now show in the pause card - before they were hidden until you walked on. A Revit refusal before a pause
  no longer counts towards ending the walk (#79).
- **Third person now shows a real character** (from AJ-Tools Game Mode v1.61.0): a site worker in a yellow hard hat,
  1850 mm tall, in place of the blocky figure, which is removed. He stands when you stop, walks when you move and runs
  with Shift, blending smoothly between them; his legs speed up with you up to a limit, so at the faster speed steps
  his feet slide a little. Drawn in the walk view only, walls still hide him, nothing is added to the model. The
  character is built into HeronTools.dll (CC0, see NOTICE). If it cannot be read, third person still works without
  him and the walk says so once. The setting and the key now say "show a character".
- **Third person starts standing**: a new walk or Q again shows the character standing at once, not finishing the
  run he was in last time; when he stops he always ends fully in the standing pose.
- **Quick snags** (standard): in Snag mode click snag after snag - each one is recorded, coloured red and pictured,
  with a short message and no comment box, so the walk carries on at once. Press Esc and write each snag's comment in
  the pause menu's snag list (one comment box; Enter saves and goes to the next snag with no comment; "No comment yet"
  shows which are missing). `Snags.csv` is saved again after each comment. The old box after each snag is the new
  setting **Ask for a comment at each snag** (off by default). Open on a row still goes back to the snag.
- **Third-person view** (from AJ-Tools Game Mode v1.60.0): Q (remappable) switches between first and third person; the
  setting **Third-person view (show a character)** (off by default) starts the walk in it. The camera sits 3000 mm behind
  and 500 mm right of you and slides in when a wall is behind; the character (above) is drawn in the walk view with
  Revit's own graphics service (nothing added to the model, no Undo step).
  Collision, steps and doors stay with you; clicks, Measure and the readout aim from the camera so they land on the
  crosshair. The status line says First person / Third person. Snag pictures hide the character; snapshots show him.

### Fixed - Model Walk files (NEEDS A REAL REVIT TEST)
- **Keys:** an action left without a key no longer comes back next walk as "had a key that cannot be used (None)" -
  that wrong note hid the real one ("has no key: its standard key is used by another action").
- **Settings:** the five walk speeds are saved exactly. Two close speeds (1.0001 and 1.0004 m/s) used to save as the
  same number, so the next walk put all five back to standard.
- **Character file:** the reader refuses a file nested too deep or with an impossible size before it uses memory, and a
  bad speed or bone weight can no longer break the animation for the rest of the walk.

### Fixed - Model Walk walk engine (NEEDS A REAL REVIT TEST)
- Third person: switching the character off just before a walk ends can no longer switch it off in the next walk;
  if Revit drops the character's drawing buffers they are made again (#85).
- Walking along a wall near a link: a slide that is free is no longer thrown away; a walk that fails to open cleans
  up after itself; ending the walk redraws every open view of the model (#82).
- Third person: after Pass-through on/off, hiding or showing an element, or Revit's own Reset Temporary Hide, the
  camera checks the wall behind you again at once, even standing still (#83).
- Viewpoints: a viewpoint whose name starts with "#" (for example "#2 riser") is no longer lost on the next walk (#81).
- Snag mode: an element in a link is snagged once per link instance ("Already snag N"), not again on each click;
  the `Snags.csv` colour column now says "coloured when recorded", true even after J, Save or Sync (#84).
- The first step after standing still no longer jumps about 0.8 m forward.
- Closing the walk's model, or closing Revit, now ends the walk at once and writes Snags.csv.
- Hiding the floor you stand on (or showing it again) now finds the floor again straight away.
- E (door) while looking down at the door lands 900 mm past it, as when looking straight at it.
- The walk view is found again by number ("(2)" before "(10)").
- In a workshared model, a Revit user name with a character Revit refuses in view names no longer stops the walk view being made.

### Fixed (from a full code review - every item still NEEDS A REAL REVIT TEST)
- Housekeeping: unused code removed and old file headers replaced by the metadata standard (no behaviour change).
- **Flip Bubbles** (window select): a grid Revit refused half-way through a flip is now put back whole in its own
  sub-transaction, never left with no bubble at either end while the rest commit.
- **Reset Graphics, Pin / Unpin, Link Workset**: if the window stops on an error after a change was made, the change is
  kept (Revit undoes a command's work when it does not return Succeeded) and the message says so. Link Workset now
  returns Cancelled when nothing was moved.
- **Crop View by Elements**: grids and reference planes in a linked model now count when "Levels, grids and reference
  planes" is ticked (they were dropped as non-model categories - the same fix the host path already had).
- **Quick Menu**: the wheel now closes if focus left it during the opening animation, instead of staying up over Revit.
- **Every window**: a window whose minimum size is bigger than the screen is now shrunk to fit, so the bottom buttons
  are always reachable (Quick Menu Settings on a 1080p laptop at 150%).
- **Quick Clash**: "the link is no longer loaded" now stays on the status line instead of being overwritten.
- **Filter Pro**: clearing the categories or parameter now also clears the remembered values, so the value search box
  cannot bring back an old parameter's values.
- **Match Elevation** selection filters compare 64-bit category ids (no `int` cut-down).
- No bare `catch` left anywhere (CONTRIBUTING rule 5): each now names `System.Exception`. Behaviour is unchanged.
- Docs: README transaction / modeless-window lines and the uninstall rule match the code; the metadata doc names the
  `installer/` folder; the bug report form lists every tool on the ribbon.

### Fixed (issues #18-#37, one pull request each - every item still NEEDS A REAL REVIT TEST)
- **Colorize** (#18): Restore no longer wipes an override set by hand after an earlier Restore; every element is checked,
  not the first 500 (Highlight too); the memory follows the model through Save As.
- **Quick Clash** (#19): after Reset view, Save / Sync / Close / the next Run never wipe overrides set by hand - "still
  coloured" needs the full Quick Clash override; Save As keeps Reset view working.
- **Filter Pro** (#20): Shuffle colours has a way back - **Restore colours** puts each filter's own overrides back.
- **Filter Pro** (#21): Update existing filters never rewrites a same-named filter that tests another parameter or
  other categories; it is skipped with a reason.
- **Filter Pro** (#22): decimal Equals uses the shown rounding; parameters on only a few families are listed; Design
  Option values are kept apart; unplaced types are not listed; a load error during restore is shown.
- **Quick Clash** (#23): clicking a clash no longer replaces the Selection A used by the next Run.
- **Quick Clash** (#24): with Hard and Clearance on, a small overlap is a 0 mm Clearance clash; Hard depth is measured
  on the real overlap, not its box (diagonal runs).
- **Quick Clash** (#25): the linked overlay frees its memory, skips repeat redraws and is dropped when the link moves
  or is unloaded; switching 3D views is lighter.
- **Quick Clash** (#26): "nothing was changed" is true after an error; isolating a second clash works; Esc is read
  reliably; very detailed families are capped for the clearance check.
- **Reassign Level** (#27): an element is not reassigned if that would move or delete a connected duct or pipe.
- **Match Level Extents / Levels to Section Box** (#28): levels or views Revit refused are reported, "partly matched"
  is said, no pop-up on full success.
- **Commands** (#29): after the tool's own message Revit adds no second error box; no raw exception text; Crop View
  skips shaped crops with a reason; Match Elevation checks the model is editable before the picks.
- **Pin / Unpin** (#30): elements changed or deleted in central are skipped with "Reload Latest"; the first refusal
  reason is shown; Restore Last Reset, Colorize Restore and Highlight count what Revit refused.
- **Reset Graphics** (#31): the views list counts once after ticking stops, not on every tick (no freeze).
- **3D Views by Workset / 3D Snapshot** (#32): name checks only against 3D views; failure reasons are kept; two views
  with the same name are reported instead of guessed.
- **Quick Menu Settings** (#33): the body scrolls on small screens; dragging the scrollbar no longer drags a tool; a
  damaged layout file is set aside and Save works; custom sizes are kept; the real save error is shown; the wheel
  opens inside the screen under the pointer.
- **Brand** (#34): the wheel uses the built-in Manrope font and colours from HeronStyles.xaml; installer Install button
  follows the palette.
- **Installer** (#35): finds Revit on any drive; a leftover add-in file can be removed; a failed first install leaves
  nothing behind; the installer version follows the add-in version.
- **Build gates** (#36): a compiler warning now fails the build; the metadata check enforces the standard; a manual
  Release run with no tag publishes nothing.
- **Code rules** (#37): version-specific code moved into Helpers; a Yes/No question is never asked twice; Find by
  Element ID tells copies of the same link apart.

### Added
- **Model Walk** (Coordination panel, 3D views): walk the live model in first person - W A S D and the mouse, five
  speeds, eased starts and stops, Fly, Pass-through, doors (E), Go to point, a low eye for ceiling voids, Measure (M:
  two points, distance in mm with horizontal and vertical parts) and Snapshot. The location line shows the level and
  the room. Right-click or Tab picks the mode: **Inspect** (what is under the crosshair, its ID, level, system, link,
  and how far), **Select** (adds to Revit's selection, kept after the walk), **Snag** (a picture, your comment typed on
  the spot and a red colour; written to `Snags.csv` in a folder next to the model - 64-bit and unique IDs, room,
  shared coordinates, camera, picture), **Hide** (Temporary Hide; Show hidden brings back only what the walk hid).
  Snag colours stay after the walk (a setting; on as standard); every element's own colours are remembered and given
  back by Clear snag colours (J) and always before Save and Sync with Central. Esc pauses and Revit is free to use; the pause menu lists the snags (click one to go back and
  select it) and the **viewpoints** - named, kept per model, played, or saved as a real Revit 3D view. **Settings**:
  mouse sensitivity, invert look, the five speeds, eye height, step-up height, keep snag colours. A second click on
  the button ends the walk and takes you back to your view. Rebuilt from AJ-Tools' Game Mode with every audit finding
  fixed - see [docs/model-walk-move-notes.md](docs/model-walk-move-notes.md) and the user guide
  [docs/model-walk-guide.md](docs/model-walk-guide.md). Not yet tested in Revit.

### Fixed
- **Model Walk** `Snags.csv` opens in columns on Windows set to a ";" list separator (most decimal-comma countries):
  the file now uses the Windows list separator (#41).
- `Constants.MM_TO_FEET` is exactly 1 / 304.8 (was 0.00328084); View Crop values move by less than 0.001 mm (#40).
- Quick Clash, 3D Snapshot and Model Walk use the shared `Constants` mm / feet values instead of their own copies of
  304.8 (same numbers; one source).
- `ElementIdHelper` no longer cuts a 64-bit element id (Revit 2024+) to 32 bits; the two selection filters that used
  it compare the full value.

## [1.1.0] - 2026-11-16

### Added
- **Quick Clash - Isolate, Show all, Select** (Results tab, like Navisworks): pick one or several clashes (Ctrl / Shift click),
  or none for every clash. Clicking clashes zooms to all of them and selects both elements of each in Revit.
  **Isolate** shows only their elements (Revit's own Temporary Hide/Isolate, with insulation; a linked clash keeps its link
  visible and the overlay draws only the picked linked elements); **Show all** brings the rest back; **Select** makes them
  Revit's selection. Linked elements are selected too in Revit 2023 and later (2020-2022 cannot select inside a link).
  Run and Reset view end Quick Clash's isolate first; a Temporary Hide/Isolate of your own is never replaced without asking.

## [1.0.0] - 2026-11-15

### Added
- Heron Tools as a stand-alone add-in for Revit 2020-2027, joining the shared **Heron** ribbon tab with
  its own **View** panel.
- **Filter Pro** - one view filter per value, named with its rule, added to the active or picked views.
- **Colorize** - colour elements by category or parameter value straight onto the elements; Restore
  original puts back their own overrides.
- **View Crop** - Crop View by Elements, and Annotation Crop by View Crop (offset on paper).
- **Unhide All**, **Toggle Links**, **Highlight Selection** (second click puts the view back).
- **Quick** panel: the **Quick Menu** wheel and its settings.
- **Datums** panel: Reset Datums to 3D, Match Level Extents, Levels to Section Box, Flip Bubbles.
- **Modify** panel: Match Elevation (centre/top/bottom), Reassign Level, Pin / Unpin, Smart Selection.
- **Coordination** panel: ID of a Picked Element, Find by Element ID, 3D Views by Workset, Link Workset.
- **About Heron** - one About for the Heron family: version, Revit and .NET, links to both products.
- **Reset Graphics** (View panel): Reset Selected (one click, no window), Reset Graphics... (element and/or category
  overrides, this view or picked views, counted first), and Restore Last Reset - every reset keeps a copy first.
- **3D Snapshot** (View panel): one click fits Revit's own 3D view (`{3D - <username>}` / `{3D}`, the one its 3D button opens) to what you are
  working on - selected rooms, spaces, zones or their tags - linked rooms too (level to upper limit), a selected section mark or the section you
  are in (same cut, same depth, camera facing the same way), or a window dragged in a plan (view range bottom to top).
  300 mm extra on every side; you stay in your view; one Undo step.
- **Quick Clash** (Coordination panel, 3D views only): a Navisworks-style clash test in a window that stays open beside the model.
  Selection A (categories of this model, or the current selection) against Selection B (categories of this model or one placed link);
  Hard (tolerance) and/or Clearance (distance) in mm; boxes first, then shapes, so big views stay quick; Esc stops a run.
  Clash colours red / green, the rest grey and see-through (all changeable); linked clash elements drawn on top. Click a clash to zoom.
  Every element's own overrides are remembered (and kept on disk in case Revit closes): Reset view puts them back exactly, and the
  view is put back by itself before Save and Sync with Central. Run and Reset are each one Undo step.
- **Every window remembers your last choices**, also after Revit restarts (`%AppData%\HeronTools\memory-<tool>.txt`):
  remembered by name, applied only where the model has them.
- **Every window stays fully on screen**, bottom buttons included: it is moved inside the screen it opens on
  (taskbar excluded) and, on a small or scaled laptop screen, held to the screen height with its middle scrolling.
- **Every pop-up message is a Heron window** (errors, warnings, notes, Yes/No questions, the View Crop report):
  same dark theme, orange button and Manrope font as the tool windows, a status mark that says what kind of
  message it is, "Show details" for long lists, and "Copy message" for support. Falls back to Revit's own
  box only if the Heron window cannot open. (Not the start-up failure box - that stays Revit's own.)
- **Flip Bubbles** opens a Heron chooser instead of Revit's grey box: two picture cards, Pick one by one or
  Window select; keys 1 / 2, Enter takes the last-used one (remembered), Esc cancels.
- Heron brand design for every window; Heron mark and app icon; brand ribbon icons; the Manrope
  brand font built into the DLL (SIL Open Font License 1.1, `Resources/Fonts/OFL.txt`).
- The Heron metadata standard on every source file, checked by `build/check-metadata.ps1`, and a GitHub
  `Gates` check that compiles every Revit year.
- `build/build.ps1` (build all years, install into each installed Revit) and `build/uninstall.ps1`, with
  the Heron AI installer's safeguards: DLL .NET check per year, deps.json check, ManifestSettings check,
  open/unknown Revit refusal, kept previous install with `-Rollback`, byte-for-byte check.
- **`HeronToolsInstaller.exe`** - one-file setup window in the Heron AI installer's look: finds Revit 2020-2027,
  installs or replaces Heron Tools in the ticked versions, waits while Revit is open, keeps the replaced
  install, checks every file; `/install`, `/uninstall`, `/status` without a window. Like the Heron AI
  installer since 2026-11-08: no tick box on the one product, so Install never removes, and a quiet
  **Remove Heron Tools** button (shown only while it is installed) takes it out of every Revit after a
  Yes/No question with No first. Made by
  `build/build-installer.ps1`, and by the Gates check on every change.
- A **Release** workflow (`.github/workflows/release.yml`): a version tag builds every Revit year, checks the tag
  matches the built version, and makes a draft GitHub release with `HeronToolsInstaller.exe` attached.
- Licensed under the Apache License 2.0 (`LICENSE`, `NOTICE`), like the Heron AI platform.

### Changed (compared with the AJ-Tools versions these tools came from)
- Every run is one named Undo step, rolled back completely on any failure; the commit result is checked.
- 64-bit element ids throughout (Revit 2024+).
- **No "done" pop-ups.** A tool that did its job ends quietly - the result is on screen. Pop-ups are kept for
  errors, for anything left undone or refused, and for "nothing changed, because...". Removed: Toggle Links'
  "links hidden one by one" note, Reassign Level's "Complete" box, 3D Views by Workset's summary.
- Many workflow fixes - see `docs/filter-pro-move-notes.md`, `docs/view-tools-move-notes.md` and
  `docs/panels-move-notes.md`.
- **Quick Menu wheel looks and motion** (chosen with Ajmal on a 23-design page): six designs - Heron Ring,
  Slim Ring, Double Ring, Orbit Buttons, Rim Lights, HUD Notched - with softly rounded slot corners; tool names
  on the wheel or icons only (the name then shows large in the centre); four openings - Unfold, Bloom, Spin in,
  Iris Pop - each starting with a small centre button that grows before the tools come out. The pointed slot
  pops, lights and (by design) steps out, with an arc, needle or chevron gliding to it; the wheel drops back
  as it closes. All three are set in Quick Menu Settings, which also gets a Preview of the real wheel and
  lifts the picked slot in its own drawing. Motion is off when Windows "Show animations" is off.
- **Quick Menu Settings drawing follows the chosen Style and Names** (same shapes as the real wheel, from one
  shared design table); new **Extra small (380)** wheel size; tool names on the wheel shrink a little to fit
  their slot instead of being cut off with "..."; the pointed slot always sits in front of its neighbours.
- Window polish after a full review of every window: typing fields are the same 34 px height as the drop-downs
  (the text was set 16 px in and the box was 41 px tall); search hints no longer cut off in Filter Pro and Colorize;
  the Annotation Crop offset label fits; the Pick views column headings sit over their columns; Reset Graphics'
  Restore Last Reset link lines up with the buttons.
- Reassign Level measures every element before and after and no longer re-makes spaces (they are skipped).
