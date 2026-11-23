# Datums panel

[Tool catalogue](README.md) > Datums

Tools to tidy and dimension grids and levels in a view. Three buttons stacked: **Reset Datums to 3D** (drop-down),
**Level Extents** (drop-down) and **Flip Bubbles**; beside them the large **Auto Dimension** drop-down.

---

## Reset Datums to 3D

**What it does:** puts grid and/or level ends in the active view back to their 3D extents.

**Buttons in the drop-down:** **Grids**, **Levels**, **Grids and Levels** - each resets every visible datum of
that kind in this view.

**When to use it:** after datum ends were dragged in a view (2D ends) and you want them to follow the model
again.

**How to use it:** open the plan, section or elevation, then **Reset Datums to 3D >** the button you want.

**What it changes in the model:** the 2D extents of the grids and levels visible in this view.

**Undo:** one Undo step - **Ctrl+Z**.

**Limits:** run it in a normal project view. Ends that cannot change in this view are reported; datums already
on their 3D extents are counted as "nothing to reset".

---

## Level Extents

### Match Level Extents

**What it does:** gives other levels the same 3D extents as one level.

**How to use it:**

1. **Level Extents > Match Level Extents.**
2. Pick the **source** level.
3. Pick the **target** levels one by one. **Esc** to finish.

### Levels to Section Box

**What it does:** stretches every level's 3D extents to the section box of the active 3D view.

**How to use it:** open a 3D view with its **section box** on, then **Level Extents > Levels to Section Box**.

**What these change in the model:** the 3D extents of levels. Level ends made 2D in a view stay 2D.

**Undo:** the whole run is one Undo step - **Ctrl+Z**.

---

## Flip Bubbles

**What it does:** swaps which end of a grid or level shows its bubble.

**How to use it:**

1. Open a plan, section, elevation or detail view. Click **Flip Bubbles**.
2. Pick datums one at a time (each flips at once), or window-select batches. **Esc** to finish.

**What it changes in the model:** the bubble ends of the datums in this view. Each flip is read back, so a datum
that a view template controls is not counted as flipped.

**Undo:** the whole run is one Undo step - **Ctrl+Z**.

**Limits:** plan, section, elevation and detail views only.

---

## Auto Dimension

**What it does:** places dimension rows across the grids and levels of the active view - for each set of parallel
grids (or levels) a **chain** (every one in turn) and an **overall** (first to last), **just inside their heads**, the
way you would place them by hand.

**Buttons in the drop-down:**

| Button | Plans | Sections, elevations, their callouts |
|---|---|---|
| **Grids** | the grids | the grids |
| **Levels** | (says levels need a section) | the levels |
| **Grids and Levels** | the grids | the grids and the levels |
| **Settings** | opens the settings window - changes nothing in the model | |

**How to use it:**

1. Open the plan, section or elevation. To dimension only some grids or levels, **select them first**.
2. **Auto Dimension >** the button you want. The rows appear; when everything was placed, nothing pops up.

**Where the rows sit:** measured in from the outermost grid (or level) head on the chosen end: the chain at the
first gap, the overall one gap further in. The heads are read as drawn in this view, so grids cut short in the view or by
a scope box are followed. A row never goes outside the crop region. Gaps are sheet millimetres (8 and 6 by default),
so the look is the same at 1:50 and 1:200.

**Settings** (saved for you, in every project, in `%AppData%\HeronTools\AutoDimension.config`):

| Setting | Choices | Default |
|---|---|---|
| Rows | Chain; Overall; Leave the overall out when there are only two (it would repeat the chain) | all on |
| Where the rows sit | Bubble end (the end where most heads show a bubble) / Other end / Both ends | Bubble end |
| Heads to first row, Between rows | sheet mm, 0 to 200 | 8, 6 |
| Dimension types | Chain and Overall: this project's linear types, or the project default | project default |
| Grids from, Levels from | Do not dimension / This model / Linked models / This model and linked models | This model |
| Only levels ticked Building Story | on / off | off |
| Leave out these names | whole names separated by `;`, `*` for any text, `?` for one character (`1` leaves out grid 1 only, not 10) | empty |
| Skip a row already in the view | a dimension on exactly the same grids or levels, made before this run, is not made again | on |
| Tell me what was left out | a short report after a run that left something out | on |

The window shows a small sketch of the rows as you change the choices. A dimension type saved in another project is
kept in the list; a project without it uses its default type, and the report says so.

**What it changes in the model:** adds dimensions to the active view. Nothing else.

**Undo:** the whole run is one Undo step - **Ctrl+Z** (named *Auto Dimension Grids*, *Levels* or *Grids and Levels*).
A row Revit refuses is left out on its own and named in the report; the other rows stay.

**Limits:**

- Plans, ceiling plans, structural and area plans, sections, elevations and callouts of them. Not 3D views, drafting
  views or legends; the buttons are greyed there.
- Straight grids only: curved grids are left out and counted. A multi-segment grid counts once per direction (its
  longest straight segment).
- Grids at an angle get their own rows, measured square to the grids. Grids must be parallel within about half a
  degree to share a row.
- Linked grids and levels: only links shown in the view, limited to the crop region (with no crop, to the span of this
  model's own datums). A dimension to a linked datum goes if the link is unloaded - that is Revit's behaviour.
- With grids or levels selected, only this model's selected datums are used (no linked ones).
- Adding a grid and running again makes a new chain and overall (the old ones are not the same set of grids, so they are
  kept): delete the old rows first.

---

## Revit versions

All Datums tools: 2020 to 2027.
