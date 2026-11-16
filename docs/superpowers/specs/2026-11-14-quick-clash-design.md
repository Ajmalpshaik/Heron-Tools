# Quick Clash - design (2026-11-14)

## Intent

Ajmal PS (MEP BIM Coordinator) wants a quick clash check like Navisworks, inside Revit, so he can catch
MEP-against-structure clashes without exporting to Navisworks. It runs in the active 3D view, shows the
clashes in colour like Highlight, and is fully temporary: **Reset puts the view back exactly as it was.**

## Goals

- A **Quick Clash** button on the Heron tab, **Coordination** panel, enabled only when the active view is
  a 3D view (not a template) in a project. Greyed everywhere else.
- A **modeless** Heron-brand window (one orange button) that talks to Revit only through an `ExternalEvent`.
- Selection A = categories of **this model**, or the **current Revit selection**.
- Selection B = categories of **this model or one linked model** (picked from a list of loaded links).
- Clash types (either or both): **Hard** with a tolerance in mm; **Clearance** with a distance in mm.
- Only elements **visible in the active 3D view** are tested (section box, hidden elements and V/G respected).
- Colours: clash A red, clash B green, rest of the view grey and see-through (70 %). All three changeable,
  with a "Default colours" link. Settings and colours remembered on this PC.
- A results list in the window (Results tab): number, type, mm, element A, element B. Click = zoom to it.
- Run and Reset are each **one named Undo step**, rolled back fully on failure.

## Non-goals

- No saving of results: no file, no export, nothing written to the model (Ajmal: "temporary only").
- No Navisworks "Hard (conservative)" or "Duplicates" types. No clash grouping, statuses or comments.
- No linked model on side A; no link-against-link.
- No worksets filter (Ajmal chose categories + selection).

## Anti-goals (a failure even if it works)

- Any graphic override the modeller set by hand is lost or changed after Reset.
- Revit freezes for minutes on a big model with no warning.
- A run left half-coloured after an error.

## Constraints

- Revit 2020-2027; 64-bit ids from 2024 (`LongValue()`, never `int`). Version-specific code only in
  `src/HeronTools/Helpers/`.
- Five Heron metadata lines on every file; brand colours only from `HeronStyles.xaml` for the window
  chrome (the three clash colours are model colours, shown as swatches, like Colorize's swatches).
- No "done" pop-ups: results go to the status line and the list. Pop-ups only for errors.
- Revit API facts checked in the `Nice3point.Revit.Api` notes:
  - `DirectContext3D` (overlay drawing) exists from 2020.
  - `FilteredElementCollector(doc, viewId, linkInstanceId)` - the elements of a link visible in a host
    view - exists **only from 2023**.
  - `DocumentChangedEventArgs.Operation` (to see an Undo) exists from 2020.

## Approaches considered

1. **Overrides + overlay (chosen by Ajmal).** This model's elements get element overrides (saved first,
   exactly like Highlight). Linked clash elements, which Revit cannot override one by one, are drawn on
   top by a `DirectContext3D` overlay. Looks like Navisworks: clashes stand out in a faded model.
2. Overlay only. Touches no override and needs no Undo, but the rest of the model cannot fade, so clashes
   are hard to see in a busy MEP model. Rejected by Ajmal.
3. Temporary Hide/Isolate. Rejected: it replaces any isolate the modeller set, and cannot isolate single
   elements inside a link.

## How it works

### Window (as rendered and shown to Ajmal)

Header (mark, "Quick Clash", active view chip). Two tabs:
- **Test**: Selection A card (Categories / Current selection (n), search, ticked category list with counts),
  Selection B card (model drop-down: "This model" + every loaded link, search, ticked list with counts),
  Clash type card (Hard + Tolerance mm, Clearance + Distance mm), Colours card (three swatches, see-through %,
  Default colours link).
- **Results (n)**: "7 clashes · 5 hard · 2 clearance", list rows `#  Type  mm  Element A  Element B`.
- Footer: status line, **Run** (orange), **Reset view**, **Close**.

Category lists show only model categories that have elements visible in the view (A: this model;
B: the chosen source). Non-physical categories (Rooms, Spaces, Areas, HVAC Zones, Revit Links, lines,
cameras) are left out.

### Threading

One `ExternalEvent` with a small request object (Refresh lists, Run, Reset, Zoom to clash n). The window
never touches the Revit API; it raises the event and the handler posts results back through the window's
Dispatcher. Buttons are disabled while a request is running.

### Collecting the elements

- Side A: `FilteredElementCollector(doc, view.Id)` + the ticked categories, or the current selection
  (kept only if visible in the view).
- Side B, this model: the same as A.
- Side B, a link: Revit 2023+ uses the link-in-view collector (true visibility). Revit 2020-2022 have no
  such collector, so it takes the link's elements of the ticked categories whose box (moved by the link's
  transform) is inside the view's section box. A helper in `Helpers/` hides this difference. Elements hidden
  inside the link by V/G are therefore still tested in 2020-2022 - said in the README.

### Testing (speed)

1. Each element's solids are read once per run (fine detail, instances flattened with their transform;
   link elements moved by the link instance's total transform). Elements with no solid are skipped.
2. **Box prefilter:** every element's box, grown by the clearance, is sorted along X; only boxes that
   overlap (sweep-and-prune) go on as pairs. This keeps big views to thousands of pair checks, not millions.
3. Pairs skipped: the same element; elements **connected to each other through MEP connectors** (a duct and
   its own fitting always touch - Navisworks would report them; it is noise).
4. **Hard:** `BooleanOperationsUtils` intersect. Overlap depth = the smallest side of the intersection
   solid's box. Reported when the depth is more than the tolerance.
5. **Clearance** (only for pairs without a hard clash): the shortest gap is measured from the triangle
   corners of each solid's faces projected onto the other solid's faces (`Face.Project`). Reported when the
   gap is above 0 and below the distance. Accurate to a few mm; a long flat face against a long flat face is
   the weak case and is said in the help text.
   Two elements that overlap by less than the tolerance are ignored even when Clearance is on (the tolerance
   decides overlaps; Clearance only judges elements that do not overlap). With Hard off, any overlap is a
   Clearance clash of 0 mm. Touching (0 mm gap) counts as a Clearance clash.
6. A Boolean that Revit refuses (bad geometry) is listed as "could not check", not silently dropped.
7. Before Run, the status line shows "Checks 780 elements against 1,560". After Run: count and seconds.
   A run stops at **500 clashes** and says so (Navisworks-style safety); tighten the section box to see more.

### Colouring (Run)

One transaction **"Quick Clash"**:
- Saves each element-in-view's own overrides (as Highlight does), keyed by document + view.
- Rest of the view: grey projection lines, grey solid surface pattern, surface transparency (70 %).
  Every link instance in the view gets the same grey see-through override (whole link).
- Clash A elements red; clash B elements of this model green (solid fill, the drafting solid pattern Highlight uses).
- Linked clash B elements: the overlay server draws their solids green in that view only.
- Run again while coloured: in the same transaction, first put the saved originals back, then colour anew
  (the saved originals stay the true originals - never the colours of the last run).
- If `Commit()` fails, nothing is kept, the overlay is removed, and the status line says so.

### Reset

One transaction **"Quick Clash - Reset"**: puts every saved override back, removes the overlay, clears the
list. Elements deleted since the run are passed over. A saved copy is used only while its colours are still on
the view: a Run on a view that is back to its own colours (after Reset, Undo, or hand edits) copies the
overrides afresh, so newer hand-set overrides are never replaced by an old copy (found in code review).

### Undo and Redo (builder's call, after the builder check)

The saved originals are **never dropped because of an Undo**. They are the true originals for that view
until a Reset commits. After every Undo or Redo (`DocumentChanged`), the tool looks at what is really on the
view, like Highlight's `StillHighlighted`: if the clash colours are there, the overlay is shown and Reset
is offered; if not, the overlay is hidden. So undoing a second run, redoing a run, or undoing a Reset can
never leave the view coloured with no way back. A copy of the originals is also written to
`%LocalAppData%\HeronTools\QuickClash\` so a Revit crash does not lose them: the next Quick Clash window in
that view offers "Put back the view from the last Quick Clash".

### Saving and Sync with Central

**Ajmal's choice: put the view back first.** `DocumentSaving`, `DocumentSavingAs` and
`DocumentSynchronizingWithCentral` allow changes to the model (checked in the API notes), so just before the
file is written the tool runs Reset (its own Undo step). The clash list stays in the window; one click on Run
colours it again. Nothing coloured ever reaches the file or central.

### Long runs (builder's call)

The run works on Revit's thread, so the window cannot repaint during it. Before Run, the status line shows
the number of elements on each side. If the box prefilter finds more than 50,000 pairs, a Heron question asks
"This may take a few minutes - run anyway?" (a real decision, so a pop-up is allowed). **Esc** stops a run
(the key is read between pairs); a stopped run changes nothing.

### Links placed more than once (builder's call)

The B list names each placed copy ("4355-STR.rvt : Location 2") when a link is placed more than once; one
copy is tested. Every link instance in the view is part of "the rest" and goes grey and see-through, and its
own override is saved and put back like any element.

### Zoom

Click a row: the view must still be the coloured view (if the modeller switched views, the status line says
"Go back to {3D - ajmal} to zoom"). `UIView.ZoomAndCenterRectangle` on the clash box grown by 1 m; element A
is selected. The section box is not changed.

### Isolate, Show all, Select (added 2026-11-16, Ajmal: "same as Navisworks")

- The results list takes several clashes (Ctrl / Shift click). Clicking zooms to all picked clashes and selects
  both elements of each in Revit (as Navisworks selects a clash's two items). No clash picked = the buttons use every clash;
  "Pick none" un-picks.
- **Isolate** uses Revit's own Temporary Hide/Isolate (`IsolateElementsTemporary`, one Undo step "Quick Clash - Isolate"),
  so Revit's sunglasses button also ends it. Each element brings its insulation and lining. Revit cannot isolate one
  element inside a link, so a linked clash keeps its link instance visible (grey) and the overlay draws only the picked
  clashes' linked elements. If the view already has a Temporary Hide/Isolate the modeller set, a Heron Yes/No asks first.
- **Show all** ends Quick Clash's isolate ("Quick Clash - Show all") and the overlay draws every linked clash element again.
  Run ends it first (the test must see the whole view); Reset view ends it in the same Undo step as the put-back.
- **Select** makes the picked clashes' elements Revit's selection. Linked elements: `Selection.SetReferences` exists only
  from 2023, so 2020-2022 select this model's elements and the status line says so (`Helpers/LinkSelection.cs`).
- Whether the isolate is Quick Clash's own (found in code review): Revit raises no event for its own "Reset Temporary
  Hide/Isolate", so the session remembers which view it isolated, the elements it isolated and a sample (200) of the
  elements it hid. The isolate is taken as Quick Clash's only while `View.IsElementVisibleInTemporaryViewMode` still shows
  the first and hides the second (elements drawn since do not matter); otherwise it is the modeller's and is never ended
  or replaced without asking. A Run in another view ends Quick Clash's isolate where it is. Run stays one Undo step:
  a TransactionGroup "Quick Clash" folds the isolate's end and the colouring together.
- Revit may refuse an id it cannot isolate (insulation of a hidden category): Isolate then tries the clash elements alone;
  views that cannot use temporary modes (locked by a template) say so. Run ends the isolate before it checks its input
  (the test must see the whole view), so a Run that then stops still leaves the view un-isolated.

### Closing

Close (or closing the model or the view) runs Reset first, so the view is never left coloured. The
overlay server is unregistered. The window remembers its size and place (`WindowMemory`).

## Files (expected)

- `Commands/CmdQuickClash.cs`, `Commands/CmdQuickClashAvailability.cs`
- `Services/QuickClash/` - request handler, element collector, clash engine, colour memory, overlay server
- `Models/QuickClash/` - clash result, category item, settings
- `UI/QuickClashWindow.xaml` + `.xaml.cs`
- `Helpers/LinkElementsInViewCompat.cs` (2023+ collector vs 2020-2022 section box filter)
- `Resources/Icons/QuickClash16.png`, `QuickClash32.png`
- `HeronToolsApp.cs` (Coordination panel), README row, CHANGELOG line

## Left to the builder

- Exact names of classes and the shape of the request object.
- The icon drawing (same style as the other Heron icons).
- How the overlay buffers are built (vertex/index buffers, one per colour).

## Needs a real Revit test (cannot be proven by a compile)

Overlay draws in the right place for a moved/rotated link; Reset restores hand-set overrides exactly;
Undo of a run clears the overlay; link instance grey override; speed on a real 4355 model; 2020-2022
link fallback; connector-pair skipping.
