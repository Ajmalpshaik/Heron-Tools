# Datums panel

[Tool catalogue](README.md) > Datums

Tools to tidy grid and level ends in a view. Three buttons stacked: **Reset Datums to 3D** (drop-down),
**Level Extents** (drop-down) and **Flip Bubbles**.

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

## Revit versions

All Datums tools: 2020 to 2027.
