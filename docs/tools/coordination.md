# Coordination panel

[Tool catalogue](README.md) > Coordination

Tools for coordination work: **Quick Clash**, **Model Walk**, **Element ID** (drop-down), **3D Views by
Workset** and **Link Workset**.

---

## Quick Clash

**What it does:** a Navisworks-style clash test in the active 3D view, in a window that stays open while you work
in Revit.

**When to use it:** a quick clash check between two disciplines or systems before a coordination meeting,
without exporting to Navisworks.

**How to use it:**

1. Open a **3D view**. A section box around the area makes the test quicker. Click **Quick Clash**.
2. **Selection A:** categories of this model, or the elements selected in Revit (**Current selection**).
3. **Selection B:** categories of this model or of a linked model.
4. Choose **Hard** (with a tolerance) and/or **Clearance** (with a distance), in mm.
5. Press **Run** (the orange button). **Esc** stops a long run.
6. Clashes turn red and green, the rest of the view grey and see-through. Click one or several clashes in the
   list to zoom to them, then **Isolate** (show only those), **Show all**, or **Select** them in Revit. With no
   clash picked, these buttons use every clash.
7. Press **Reset view** when you are done.

**What it changes in the model:** element graphic overrides in this 3D view. Every element's own colours are
remembered.

**Undo:** **Reset view** puts every element's own colours back exactly. It is also done by itself before **Save**
and **Synchronize with Central**, so no clash colours are saved. **Run** and **Reset view** are each one Undo step.

**Limits:**

- 3D views only (the button is greyed in other views).
- Only what the view shows is tested.
- Revit 2020-2022: link elements are tested inside the section box.
- The colours you pick in the window are remembered on this PC.

Screenshot needed: the Quick Clash window next to a 3D view with clashes in red and green.

---

## Model Walk

**What it does:** walk the model in first person from a 3D view (W A S D and the mouse, Fly, doors, Measure,
viewpoints) - or in third person, with a site worker drawn on screen where you stand (**Q**). Modes: **Inspect**,
**Select**, **Snag** (a picture and a red colour per snag, a CSV report next to the model) and **Hide**.

**How to use it (short):**

1. Open a **3D view**. Click **Model Walk**. The walk starts where the view looks.
2. Walk with **W A S D**, look with the mouse. **Right-click** or **Tab** changes the mode; **left click** does it.
3. **Esc** pauses (Revit is free to use): snag list and comments, viewpoints, settings and keys.
4. **Esc** again, **End walk**, or a second click on the button ends the walk.

**What it changes in the model:** the first time, it makes the 3D view **"Heron Model Walk"** (one per person in
a workshared model). Each snag is one Undo step; camera moves make no Undo entries. Snag colours stay in the walk
view after the walk until **J**, or the next Save or Sync - no red is saved in the model.

**Full guide:** [Model Walk user guide](../model-walk-guide.md) - every key, mode, setting and the snag report.

**Limits:** 3D views only. Only what the view shows blocks you - hide a category or use a section box to walk
through it.

---

## Element ID

A drop-down with two buttons. Both only read the model; they change nothing.

### ID of a Picked Element

**What it does:** pick a model element - in this model or in a linked model - and see its Element ID and which
model it comes from.

**How to use it:** **Element ID > ID of a Picked Element**, pick the element (for a link, pick the element inside
the link when asked). **Esc** cancels.

**Limits:** for annotation in this model, use Revit's own **Manage > Inquiry > IDs of Selection**.

### Find by Element ID

**What it does:** finds an element by its ID in this model or in a linked model, and zooms to it.

**How to use it:**

1. **Element ID > Find by Element ID.**
2. Type the ID and choose where to look (this model, a link, or **All models**).
3. Press **Find and zoom**. The active view zooms to the element; an element in this model is also selected.
   If the same ID is in this model and in a link, you pick which one.

**Limits:** IDs above 2,147,483,647 exist only in Revit 2024 and later; older Revit says so in plain words.

---

## 3D Views by Workset

**What it does:** makes one 3D view per user workset, each named after its workset and showing only that
workset.

**When to use it:** to check what sits on each workset, or to find elements on the wrong workset.

**How to use it:** in a workshared model, click **3D Views by Workset**.

**What it changes in the model:** new 3D views. A view template the new view would inherit is taken off (the
report says so), so the workset visibility is not overridden.

**Undo:** one Undo step - **Ctrl+Z**, or delete the views.

**Limits:** workshared models only. A workset whose 3D view already exists is skipped; a workset name a view
cannot take is listed.

---

## Link Workset

**What it does:** puts Revit links and CAD imports on a workset.

**How to use it:**

1. In a workshared model, select the links you want to move (optional) and click **Link Workset**.
2. Tick the links and CAD imports (the ones you selected start ticked).
3. Pick a workset, or type a new name - a new workset is made with that name.
4. Press **Move to workset**.

**What it changes in the model:** the workset of the ticked links and imports (and a new workset if you typed
one).

**Undo:** one Undo step - **Ctrl+Z**.

**Limits:** workshared models only. Names are trimmed and matched ignoring upper/lower case; characters Revit does
not allow in a workset name are explained.

Screenshot needed: the Link Workset window.

---

## Revit versions

All Coordination tools: 2020 to 2027. Differences are listed under **Limits** above.
