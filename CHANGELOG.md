# Changelog

All notable changes to Heron Tools. Dates are yyyy-mm-dd.

## [Unreleased]

### Added
- **Model Walk** (Coordination panel, 3D views): walk the live model in first person - W A S D and the mouse, five
  speeds, eased starts and stops, Fly, Pass-through, doors (E), Go to point, a low eye for ceiling voids, Measure (M:
  two points, distance in mm with horizontal and vertical parts) and Snapshot. The location line shows the level and
  the room. Right-click or Tab picks the mode: **Inspect** (what is under the crosshair, its ID, level, system, link,
  and how far), **Select** (adds to Revit's selection, kept after the walk), **Snag** (a picture, your comment typed on
  the spot and a red colour; written to `Snags.csv` in a folder next to the model - 64-bit and unique IDs, room,
  shared coordinates, camera, picture), **Hide** (Temporary Hide; Show hidden brings back only what the walk hid).
  Every element's own colours are given back at the end of the walk (or kept, if you set that) and always before Save
  and Sync with Central. Esc pauses and Revit is free to use; the pause menu lists the snags (click one to go back and
  select it) and the **viewpoints** - named, kept per model, played, or saved as a real Revit 3D view. **Settings**:
  mouse sensitivity, invert look, the five speeds, eye height, step-up height, keep snag colours. A second click on
  the button ends the walk and takes you back to your view. Rebuilt from AJ-Tools' Game Mode with every audit finding
  fixed - see [docs/model-walk-move-notes.md](docs/model-walk-move-notes.md) and the user guide
  [docs/model-walk-guide.md](docs/model-walk-guide.md). Not yet tested in Revit.

### Fixed
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
