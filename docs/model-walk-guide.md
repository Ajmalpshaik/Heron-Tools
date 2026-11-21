# Model Walk - user guide

Walk through the live Revit model in first person: look at things, select them, measure, and record snags with a
picture and a comment for the coordination meeting. Heron tab, **Coordination** panel, **Model Walk**.

> Not yet tested in a real Revit. See [model-walk-move-notes.md](model-walk-move-notes.md) for what still needs a test.

## Start and stop

1. Open any **3D view** (perspective or the {3D} view). The walk starts where that view looks.
2. Click **Model Walk**. The first time, Revit makes the view **"Heron Model Walk"** (one Undo step). In a
   workshared model each person gets their own: "Heron Model Walk - *your name*".
3. Walk. **Esc** pauses (Revit is free to use). **Esc** again, or **End walk** in the pause menu, or a second click
   on the ribbon button, ends the walk and takes you back to your view.

Only what the view **shows** blocks you. Hide a category in Visibility/Graphics, use a filter or a section box, and
you walk through it. Change these while paused, then carry on.

## Keys (standard - change them in the pause menu, Keys)

| Key | What it does |
|---|---|
| W A S D or the arrow keys | Walk forward, back, left, right. The mouse looks around. |
| Shift (hold) | Go three times faster |
| + / - or the mouse wheel | Speed step 1 to 5 (shown as "Speed 3/5") |
| C | Low eye on / off (1000 mm) - look under ducts and into ceiling voids. In Fly: hold to sink |
| F | Fly on / off. Space (hold) rises, C (hold) sinks. Walls and slabs still stop you |
| G | Pass-through on / off - nothing stops you (past a locked room or a clash) |
| E | Go through the door you look at (when the door hint shows) |
| T (hold, aim, let go) | Go to point - aim at a floor; a ring shows where you land |
| R | Back to start |
| Right-click, Tab (Shift+Tab back) | Change the mode: Inspect, Select, Snag, Hide |
| Left click | Do the mode's action on what is under the crosshair |
| M | Measure: M at point 1, M at point 2 |
| B | Save a viewpoint and name it |
| 1 to 9 | Go to viewpoint 1 to 9 |
| O | Play viewpoints (any move key stops) |
| X | Clear snag colours - every snagged element gets its own colours back |
| U | Show again what this walk hid |
| K | Snapshot - a picture of the view, without the HUD |
| Z | Clean screen - only the crosshair |
| H | Help card on / off |
| P or Esc | Pause |
| F12 | Frame timing (for checking speed on a big model) |

Fixed keys that cannot be changed: Esc, Tab, Enter, the mouse, 1-9 and the arrow keys.

## Modes

| Mode | Left click | Changes the model? |
|---|---|---|
| **Inspect** (standard) | Nothing - the bottom line always shows what is under the crosshair: category, family : type, size, ID, level, system, link, and the distance | No |
| **Select** | Adds the element to Revit's selection (click again takes it out). It stays selected after the walk, ready to edit | No |
| **Snag** | Records a snag (below) and colours the element red in the walk view | Yes - one Undo step per snag; colours given back |
| **Hide** | Hides the element for now (Temporary Hide). U shows again only what this walk hid | No (temporary) |

Linked elements: Select and Hide refuse them (they cannot be picked alone). Snag records them, without a colour.

## Snags and the snag report

1. Pick **Snag** mode, aim, left click.
2. A picture of the view is taken (the HUD is hidden for it).
3. A small box asks for a comment. Type it, **Enter** saves, **Esc** skips (the snag is kept without one).

Everything goes in one folder per walk, **next to the model**:
`<model folder>\Model Walk - <model name>\<date time>\`. If the model is not saved, is a cloud model, or that folder
cannot be written, it goes to `Documents\Heron Model Walk\<model name>\<date time>\`.

The folder holds:

- **Snags.csv** - opens in Excel. One row per snag: No., Time, Author, Comment, Category, Family : Type, Size,
  Element ID (full 64-bit) and Unique ID, Level, System, Link, Room, East / North / Elevation in **shared coordinates
  (mm)** - the numbers Spot Coordinates shows - the camera, the picture file and whether it was coloured.
- **Snag 001.png**, **Snag 002.png**... - one picture per snag.
- **Snapshot ....png** - pictures taken with K.

The CSV is saved again after every snag and comment, so nothing is lost if Revit stops. If Excel has it open, close
it - the next change saves it again. **Open report folder** in the pause menu opens the folder.

**Snag colours:** given back when the walk ends (standard), and always before every Save and Sync with Central - no
snag red is ever saved in the model. An element whose colour you changed yourself after snagging is left as you set
it. To keep the red after the walk (for a meeting), turn on **Keep snag colours after the walk** in Settings; X or
**Clear snag colours** in the pause menu still gives them back.

## The pause menu (Esc)

- **Carry on walking** (or Enter).
- **Settings** (S), **Keys** (K), **Clear snag colours**, **Open report folder**, **End walk** (Esc).
- **Snags** - newest first. Click one: the camera goes back where it was recorded and the element becomes Revit's
  selection (a linked element is not selected).
- **Viewpoints** - click one to go there; **Save as 3D view**; **Delete**; **Play viewpoints**.

## Viewpoints

- **B** saves where you stand and where you look, then asks for a name (Esc keeps "Viewpoint 3 - Level 02 - Room ...").
- They are **kept for this model** on your computer (`%AppData%\HeronTools\ModelWalk Viewpoints`), so they are there
  next time. A workshared model is known by its central file, so every local copy finds them. They are not saved in
  the Revit file and teammates do not see them.
- **Save as 3D view** (pause menu) makes a real Revit 3D view, "Model Walk - *name*", that looks from the viewpoint,
  with the walk view's Visibility/Graphics, filters and section box, without the snag red. One Undo step. Use it on
  a sheet or in the meeting.
- **Play viewpoints** (O) moves smoothly through all of them, pausing at each. Any move key stops it.

## Measure

**M** at the first point, **M** at the second. The HUD shows the distance in mm, with its horizontal and vertical
parts (for clearance above a duct). Between the two presses it shows the distance from point 1 to the crosshair.
Nothing is drawn on the model. M again starts a new measure.

## Settings (pause menu, S)

| Setting | Standard | Range |
|---|---|---|
| Mouse sensitivity | 100 % | 25 - 250 % (lower it if fast turning makes you feel unwell) |
| Invert look | off | mouse forward looks down |
| Walk speeds, slowest first | 0.8, 1.5, 3.5, 5.5, 8 m/s | 0.2 - 20 m/s, each faster than the last. Fly x2, Shift x3 |
| Eye height | 1700 mm | 1000 - 2000 mm (low eye = 1000 mm, or 300 mm under the eye) |
| Step-up height | 350 mm | 100 - 450 mm - the highest stair or step you walk up |
| Keep snag colours after the walk | off | |

Saved for you on this computer (`%AppData%\HeronTools\ModelWalkSettings.txt`). Save applies them at once.

## What goes in the Undo list

Making the walk view (first time only), each snag colour, Clear snag colours, giving colours back at the end, and
Save as 3D view - one named step each ("Model Walk: ..."). Walking and looking make no Undo entries.
