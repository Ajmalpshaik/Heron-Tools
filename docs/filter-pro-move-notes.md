# Filter Pro: moved from AJ-Tools (2026-11-07)

Filter Pro was copied from AJ-Tools (`D:\06_Development\01_AJ-Tools\Revit Addins`, master `c3117eb`)
into Heron Tools, rebranded, and checked against the view-filter code Heron AI has proven on real
Revit 2020/2024/2026/2027. This note is the record of what changed and what is still owed.

## What was copied

| From AJ-Tools `src/` | To Heron Tools `src/HeronTools/` |
|---|---|
| `Commands/CmdFilterPro.cs`, `CmdFilterProAvailability.cs` | `Commands/` |
| `Services/FilterPro/*.cs` (7 files) | `Services/FilterPro/` |
| `UI/FilterProWindow.xaml(.cs)` | `UI/` |
| 11 model files (`FilterCategoryItem` ... `ColorPalette`) | `Models/` |
| `FilterRuleCompat`, `ElementIdExtensions`, `ElementIdHelper`, `ElementIdIntegerComparer`, `Constants`, `WindowMotionHelper`, `TabMotionHelper` | `Helpers/` |
| `UI/ModernStyles.xaml` (templates and motion only) | `UI/HeronStyles.xaml` (all colours replaced by the Heron brand) |

Namespace `AJTools` became `HeronTools`. The ribbon icon was redrawn in brand Accent.

## Brand

Colours come only from the Heron brand palette (source of truth: Heron AI `docs/brand/README.md`,
from the designer's kit). Every hex value in `HeronStyles.xaml` was listed and checked: all are brand
colours or White/Shadow at an opacity, which the brand allows.

- Main button (Apply To View), selection, focus: Accent `#FD641A`. Only one Accent button per window.
- Create Filters and Close were Success-green and Danger-red buttons. The brand keeps green for "done"
  and red for "failed", so both are now quiet buttons.
- Windows' default GroupBox drew a white double line; it now has its own rounded Surface card template.
- Font: `Manrope, Segoe UI`. Manrope is **not installed** on this PC, so Segoe UI shows (the brand's own
  stated fallback) - the same as Heron AI today.
- The colours Filter Pro writes into the **model** (the 20-colour filter palette) are model colours,
  not UI, and stay as they were.
- Logo: the Heron mark from the designer's kit (`D:_Development\Heron grafics update\Heron`) - the
  same artwork Heron AI shows - is in `docs/brand/`, in the README header, drawn in Mist in the window
  header, and the orange app icon is on the window's title bar. The full wordmark reads "Heron AI", so
  it is not used for this product.
- The kit also has the static Manrope files (Regular, Medium, SemiBold, ExtraBold), which WPF can
  embed. Not embedded yet: the font's licence file (SIL OFL) must ship with it and is not in the kit.
- Seen while checking: Heron AI's own Revit windows still use its old blue `#2F9BE3`
  (`revit/Heron.Revit.Addin/HeronWindowStyle.cs`), not the brand. Nothing in Heron AI was changed.

## Defects fixed (found by comparing with Heron AI)

| # | Problem in the old code | Fix |
|---|---|---|
| 1 | A failure part-way through a run was noted as "skipped" and the transaction still committed - half a set of filters. | Revit failures now throw; each button runs as one transaction that is rolled back on any failure, and `Commit()`'s result is checked. The modeller sees "Nothing was changed". |
| 2 | Reorder removes every filter and re-adds them, swallowing errors - a failure could lose filters and their overrides. | No swallowing in the reorder; a failure rolls the whole run back. |
| 3 | Reorder trusted a remembered order over the live one, undoing a manual reorder in Visibility/Graphics; the memory was keyed by view number only. | Revit 2021+: reads `View.GetOrderedFilters()`. Revit 2020 (no such call): memory kept, keyed by document + view. |
| 4 | No Revit pre-check before making a filter; Family Name / Type Name forced into the parameter list even when Revit cannot filter them for the picked categories. | `ElementFilterIsAcceptableForParameterFilterElement` runs first; refused rules are reported, nothing is made. Nothing is forced into the list. |
| 5 | Name clash check ignored selection filters, so Revit could refuse the name. | All filter names (rule-based and selection) are checked. |
| 6 | Name cleaning missed the backtick and tilde. | Added; `NamingUtils.IsValidName` is asked as the final word. |
| 7 | Updating an existing filter: categories changed before old rules were cleared (can throw); `SetElementFilter` result ignored. | `ClearRules` - `SetCategories` - `SetElementFilter`, result checked. |
| 8 | "Family and Type": every rule silently became Equals; Has Value sent a fake parameter to Revit; a missing family name was replaced by the category name (filter matched nothing). | Only Equals is offered for it, with a clear message otherwise; the fake id never reaches Revit; no category-name stand-in. |
| 9 | Solid fill: took any solid pattern (Heron uses the drafting one); with none, set an invisible pattern colour silently. | Drafting solid fill only; if there is none, says so and still applies line colours. |
| 10 | Several reads/writes in apply and reorder swallowed failures. | They throw (rolled back) or are reported. |
| 11 | The view list offered sheets, legends and drafting views, which then failed. | The list uses the same "can this view have filters" test as the ribbon button. |
| 14 | Element ids cut to 32 bits for comparisons (Revit 2024+ ids can be larger, so two filters could look like one). | Full 64-bit value everywhere; a large id can no longer be mistaken for a built-in parameter. |
| 15 | Decimal values: listed by the text Revit shows, but the rule used one sample value +/- 1e-6 ft, so elements showing the same "1500" could be missed. | The rule covers the real spread of values behind that text (Equals, Not Equals, Greater, Less...). |
| - | A text rule (Contains...) on a number parameter silently became Equals. | Refused with a plain message. |
| - | Error boxes showed raw text titled "Error"/"Validation". | Plain-language messages titled "Filter Pro". |

## Defects fixed after the independent code review

| Problem | Fix |
|---|---|
| Reorder re-adds every filter; a filter that was already on the view AND touched by this run (Shuffle, or Apply on an existing filter) came back with blank overrides - line weights, patterns, transparency lost - and a hidden one came back visible. (Present in the AJ-Tools version too.) | Every filter that was on the view gets its own overrides and visibility back first; new colours go on top; only a filter new to the view is switched visible. |
| Window events that load lists run as `async void`; an error there could reach Revit's message loop. | Each one, the name preview and the restore-last-picks step now catch and show the reason in the status line. |
| A rollback that itself failed could hide the first reason. | Rollback only when the transaction is still open; a rollback problem is added to the message. |
| "Applied: N" and an Undo entry even when nothing changed (all views template-controlled, or filters already in place). | A reused filter counts only if a view really changed; an empty run is rolled back and says "Nothing needed changing". |
| Two picked values that clean up to the same name merged into one filter silently. | Reported as skipped; only the first is used. |
| Revit 2020's remembered order was saved even when the run was rolled back. | Saved only after a successful commit. |
| A quick second click while a list was loading was ignored. | The list loads once more, so it matches the current picks. |
| Build/uninstall scripts could empty a linked folder's target, or stop half-way on a locked file. | A linked HeronTools folder is skipped; a locked file gives a FAIL row and the other years continue. |
| `TryGetBuiltInParameter` accepted -1 (invalid). | -1 is refused. |

## Still owed

- **Real Revit tests** (see the PR): tab alone / with Heron AI, every button, Undo in one step, a
  failure rolls back, 2020 vs 2021+ reorder, decimal Equals.
- **Suspected, not changed:** a view whose template does *not* control V/G filters is still skipped
  as "uses a view template" (Heron writes and then reads back instead). Needs a decision.
- **Low, not changed:** with Has Value / Has No Value, picking several values makes one identical
  filter per value (only the names differ). Could collapse to one filter.
- **Remove Filter Pro from AJ-Tools** - only after the Heron Tools version passes in Revit, and only on
  Ajmal's word. Careful: AJ-Tools' **Colorize** tool reuses part of Filter Pro, so these must STAY in
  AJ-Tools: `FilterProDataProvider` (ColorizeWindow), `FilterCreator` and `FilterApplier`
  (ColorizeApplier), and the models and helpers they need. Safe to remove there: the ribbon button
  (`RibbonManager.AddFilterProTool`), the Quick Menu entry, `CmdFilterPro`, `CmdFilterProAvailability`,
  `FilterProWindow`, `FilterProHelper`, `FilterReorderer`, `FilterProStateTracker`,
  `FilterValueKeyMatcher` and the `FilterPro.png` icon - each to be re-checked with a full AJ-Tools
  build before deleting.
