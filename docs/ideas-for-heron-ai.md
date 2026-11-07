# Ideas Heron AI could take from Heron Tools

Research note, 2026-11-07. While moving seven tools from AJ-Tools into Heron Tools, each was checked against
the Heron AI platform's proven building blocks (`brain/fragments/` in the Heron AI repository), and Heron
Tools learned from Heron AI. This is the other direction: what Heron AI could learn from these tools.

**Nothing in Heron AI was changed.** Each idea is a suggestion for the owner to accept or refuse; anything
accepted would be built Heron AI's own way, never copied across.

| # | Idea | Where Heron AI stands today (checked) | What Heron Tools learned |
|---|---|---|---|
| 1 | **A way back after highlighting.** Remember each element's own graphic overrides before painting, and restore them on request. | `highlight-vs-rest` paints red/grey and keeps no originals; the only way back is `reset-graphic-overrides`, which writes an empty override and so also wipes overrides the modeller set on purpose. | Highlight Selection saves the originals per view and the second click puts them back exactly; if the highlight was undone in between, the memory is dropped instead of "restoring" a clean view. |
| 2 | **The same way back for colour by parameter.** | `color-by-parameter` already starts from the element's own overrides (good - other settings are kept), but there is no restore of the colours it wrote. | Colorize remembers the first original only (a second shuffle never overwrites the true original) and offers Restore original as its own Undo step. |
| 3 | **Annotation crop offset is a paper distance.** | `set-view-crop` / `set-crop-box-settings` can switch the annotation crop on; no fragment sets its offset. | Revit's API notes: "a non-negative length in view units", default 1", minimum 1/8". Treating it as a model distance put the annotation crop 10 m out at 1:100. Clamp to 1/96 ft. |
| 4 | **Fit a crop to the elements a view shows** (a capability Heron AI does not have). | `set-view-crop` sets a crop to a given box or shape. | Measure with the crop **off** (a view collector only returns what is inside the current crop, so measuring with it on can only shrink it); Revit 2024+ can list the **linked** elements a view shows (`new FilteredElementCollector(doc, viewId, linkId)`), so a big linked site no longer widens every plan; skip a non-rectangular crop (setting `CropBox` on it silently does nothing); read the crop back to prove Revit took it. |
| 5 | **Distinct colours.** | `color-by-parameter` spreads hues evenly around the colour wheel (already distinct). | For a fixed palette, deal from a shuffled deck per run instead of random picks (random picks repeated a colour about 4 times in 10 with 5 values). Only relevant if Heron AI ever uses a fixed palette. |
| 6 | **The rule belongs in a filter's name.** | Worth checking `create-view-filters-by-rule` naming. | "Has value" and "Has no value" (or "Equals A" / "Does not equal A") once got the same name, and a later run reused the opposite filter. Names now carry the rule: "Not 300", "Has value". |
| 7 | **Decimal "equals" from the spread behind one displayed value.** | Worth checking any fragment that builds an Equals rule from a value picked by its displayed text. | Values listed as "1500" can differ in a far decimal place; the rule now covers min..max of the values that show that text. |
| 8 | **A template blocks filters only if it controls them.** | Worth checking `apply-view-filter`. | `template.GetNonControlledTemplateParameterIds()` containing `VIS_GRAPHICS_FILTERS` means the view's filters are free to change even though it has a template. |
| 9 | **Unhide all, fast and partial-safe.** | `show-elements` shows given elements. A "show everything hidden in this view" capability does not exist. | Collect only model elements and the view's own annotation (`ElementOwnerViewFilter` OR invalid owner) instead of asking every element in the model; if `UnhideElements` refuses one id, retry one by one so the rest still show; no empty Undo step when nothing is hidden. |
| 10 | **Toggle the RVT Links category, and say what it cannot show.** | `set-category-visibility` can switch any category. | After switching links on, report link instances still hidden one by one - otherwise the click looks as if nothing happened. |

## Not taken from AJ-Tools (left behind on purpose)

- AJ-Tools' "Reset Graphics" style of clearing overrides to blank - replaced by remember-and-restore above.
- Borderless custom-title-bar windows - Heron Tools uses the standard Windows title bar, as Heron AI's
  windows do.
