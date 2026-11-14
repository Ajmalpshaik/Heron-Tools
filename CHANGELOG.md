# Changelog

All notable changes to Heron Tools. Dates are yyyy-mm-dd.

## [1.0.0] - unreleased (draft, awaiting the Revit test)

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
  installs, replaces or removes Heron Tools per version, waits while Revit is open, keeps the replaced
  install, checks every file; `/install`, `/uninstall`, `/status` without a window. Made by
  `build/build-installer.ps1`, and by the Gates check on every change.
- A **Release** workflow (`.github/workflows/release.yml`): a version tag builds every Revit year, checks the tag
  matches the built version, and makes a draft GitHub release with `HeronToolsInstaller.exe` attached.
- Licensed under the Apache License 2.0 (`LICENSE`, `NOTICE`), like the Heron AI platform.

### Changed (compared with the AJ-Tools versions these tools came from)
- Every run is one named Undo step, rolled back completely on any failure; the commit result is checked.
- 64-bit element ids throughout (Revit 2024+).
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
