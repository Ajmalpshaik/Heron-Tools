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
- Licensed under the Apache License 2.0 (`LICENSE`, `NOTICE`), like the Heron AI platform.

### Changed (compared with the AJ-Tools versions these tools came from)
- Every run is one named Undo step, rolled back completely on any failure; the commit result is checked.
- 64-bit element ids throughout (Revit 2024+).
- Many workflow fixes - see `docs/filter-pro-move-notes.md`, `docs/view-tools-move-notes.md` and
  `docs/panels-move-notes.md`.
- Reassign Level measures every element before and after and no longer re-makes spaces (they are skipped).
