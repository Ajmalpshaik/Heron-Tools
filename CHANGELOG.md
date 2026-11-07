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
- Heron brand design for every window; Heron mark and app icon; brand ribbon icons.
- The Heron metadata standard on every source file, checked by `build/check-metadata.ps1`, and a GitHub
  `Gates` check that compiles every Revit year.
- `build/build.ps1` (build all years, install into each installed Revit) and `build/uninstall.ps1`.

### Changed (compared with the AJ-Tools versions these tools came from)
- Every run is one named Undo step, rolled back completely on any failure; the commit result is checked.
- 64-bit element ids throughout (Revit 2024+).
- Many workflow fixes - see `docs/filter-pro-move-notes.md`.
