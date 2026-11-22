# Modify panel

[Tool catalogue](README.md) > Modify

Tools that move, pin and select elements: **Match Elevation** (drop-down), **Reassign Level**, **Pin / Unpin**
and **Smart Selection**.

---

## Match Elevation

**What it does:** gives other elements the same centre, top or bottom elevation as a source MEP element.

**Buttons in the drop-down:** **Match Centre**, **Match Top**, **Match Bottom**.

**When to use it:** to line up ducts, pipes, trays or equipment at the same height as another element.

**How to use it:**

1. **Match Elevation >** Centre, Top or Bottom.
2. Pick the **source** element (the height to copy).
3. Pick the **target** elements one by one. **Esc** to finish.

**What it changes in the model:** the elevation of the target elements.

**Undo:** one Undo step - **Ctrl+Z**.

**Limits:** pipe top and bottom use the pipe's **outside diameter** (the nominal size only when the outside
diameter cannot be read), the same as Revit's own Top / Bottom Elevation. Elements already at that height are
reported as "nothing to move".

---

## Reassign Level

**What it does:** moves ducts, pipes, cable trays, conduits and free-standing families to another reference
level **without moving them in space**.

**When to use it:** elements were modelled on the wrong level and schedules or views by level are wrong.

**How to use it:**

1. Click **Reassign Level**.
2. Choose **Whole project** (every element on the **From** level) or **Only my selection**.
3. Pick the **To** level and press **Reassign**.

**What it changes in the model:** each element's reference level and its offset, so that it stays exactly where
it was. Each element is measured before and after; any that would move is put back as it was and reported.

**Undo:** one Undo step - **Ctrl+Z**.

**Limits:** hosted families (on a wall, ceiling or face) are skipped. Spaces, rooms and zones are never touched.

Screenshot needed: the Reassign Level window.

---

## Pin / Unpin

**What it does:** pins or unpins whole groups of elements in one go.

**Groups:**

- **Sheet groups:** title blocks, placed views, legends, schedules (revision schedules excluded) - on this sheet
  or on all sheets.
- **Model groups:** duct system, pipe system, cable tray system, air terminals, links, generic models,
  mechanical equipment, plumbing fixtures, electrical equipment, grids, levels.

**How to use it:** click **Pin / Unpin**, tick the groups, choose **This sheet only** or **All sheets** for sheet
groups, then press **Pin** or **Unpin**. Your choice is remembered for next time.

**What it changes in the model:** the pinned state of those elements.

**Undo:** each Pin or Unpin press is one Undo step - **Ctrl+Z**, or press the other button.

**Limits:** in a workshared model, elements a colleague owns are left alone and counted.

Screenshot needed: the Pin / Unpin window.

---

## Smart Selection

**What it does:** pick one element, then window-select - only elements of the **same category** are kept in the
selection. Everything else caught in the box is skipped.

**How to use it:**

1. Click **Smart Selection** (or select one element first).
2. Pick the reference element.
3. Drag one window or crossing box over the area.
4. The result stays selected for your next step.

**What it changes in the model:** nothing - it only selects.

---

## Revit versions

All Modify tools: 2020 to 2027.
