# View tools: moved from AJ-Tools (2026-11-07)

Colorize, View Crop (Crop View by Elements, Annotation Crop by View Crop), Unhide All, Toggle Links and
Highlight Selection were copied from AJ-Tools' View panel (`D:\06_Development\01_AJ-Tools\Revit Addins`,
master `c3117eb`), then checked line by line - against Heron AI's proven building blocks, by an independent
audit, and by a second review of the fixes. About 50 files; the Section Mark Visibility tool was **not**
needed (a false link in the first file count).

## Ribbon (Heron tab > View panel)

`Filter Pro` · `Colorize` · `View Crop` (pulldown: Crop View by Elements, Annotation Crop by View Crop) ·
a stack of `Unhide All` / `Toggle Links` / `Highlight`. New brand icons for every button.

## Defects found and fixed

| Tool | Problem in the AJ-Tools version | Fix |
|---|---|---|
| Highlight Selection | Painted every element in the view red/grey, **overwriting the modeller's own overrides**, and relied on a "Reset Graphics" tool Heron Tools does not have. | Remembers each element's overrides; the second click puts the view back exactly. If the highlight was undone in between, it highlights again instead. |
| Colorize | Same overwrite with no way back. | Remembers the first original of each element; **Restore original** puts them back (one Undo step). |
| Colorize | Random colours repeated. | Shuffled deck per run - no repeat until all 20 colours are used. |
| Colorize | Model fill patterns offered; overrides refuse them, so every element failed. | Drafting patterns only; the drafting solid fill by default. |
| Colorize | Decimal values: one sample +/- 1e-6 ft missed elements that show the same value. | Rule covers the spread behind the displayed value. |
| Colorize | Validation pop-ups; the view list offered sheets and legends; messages said "active view" in picked-views mode. | Messages in the window; only views that take overrides listed; each skip names its view. Redesigned on the Filter Pro layout. |
| View Crop | Setting `CropBox` on a **non-rectangular crop** silently does nothing, yet "Updated crop successfully" was reported. | Such views are skipped with the reason (the shape is not lost); every crop write is read back to prove Revit took it. |
| View Crop | Measured "visible elements" with the crop **on**, so the crop could only ever shrink. | Crop switched off just while measuring, then back on, inside the view's transaction. |
| View Crop | Linked models measured whole, for every view (a linked site widened every plan; 40 views = 40 full scans). | Revit 2024+: only the linked elements the view shows. 2020-2023 cannot ask that and still measure the whole link. |
| View Crop | `IsWorksetVisible` called in non-workshared models. | Only in workshared models. |
| Annotation crop | Offset treated as a **model** distance - Revit uses a **paper** distance (API: "length in view units", default 1", minimum 1/8"); the default 100 mm at 1:100 put it 10 m out. | Labelled "Offset on paper (mm)", default 25 mm (Revit's 1"), never below 3.2 mm. |
| Annotation crop | Silently switched on an old, forgotten crop when Crop View was off. | Skipped with "turn on Crop View first". |
| View Crop settings | A damaged settings file could give a NaN or negative margin. | Rejected on load. |
| Unhide All | Asked every element in the model (slow); one refused id stopped the whole run; an empty Undo step when nothing was hidden; "No hidden elements" while categories stayed hidden. | Only model elements and the view's own annotation; one-by-one fallback; nothing done = no transaction; the report says categories/filters in Visibility/Graphics were not changed. |
| Toggle Links | Links hidden one by one stayed hidden on "show", with no word about it. | Says how many are still hidden and how to show them. |
| All | A change Revit refused at commit (view owned by another user) was reported as success. | Commit result checked everywhere. |
| All | 32-bit element ids (`(int)id.Value`, `HashSet<int>`) in about a dozen places. | 64-bit throughout. |
| All | Override builder switched OFF the elements' own fill patterns whenever no pattern was picked. | A pattern's Visible switch is only touched when that pattern is set. |
| All windows | Borderless custom-title-bar windows in the old theme, "AJ Tools" wording. | Heron design (standard title bar, brand theme, heron mark, app icon, one orange button, messages in the window). |

## Still owed

- **Real Revit test** of every tool (see the pull request checklist).
- Highlight's and Colorize's memory lasts for the Revit session only; after a restart, Undo or Revit's
  Override Graphics in View > Reset are the way back (said in the Disclaimer).
- Linked models in View Crop on Revit 2020-2023 are still measured whole.
- Removing these tools from AJ-Tools - after the Revit test, on the owner's word.
