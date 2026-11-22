# Model Walk - user guide

Walk through the live Revit model in first person (or third person, with a character): look at things, select them, measure, and record snags with a
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

**Section box:** if you start a walk **outside** the walk view's section box (for example on top of the roof), Model Walk
switches the walk view's section box off, so the building is whole - one Undo step, and the walk tells you. A section box
you start inside stays. Far clipping is always switched off in the walk view. Your own 3D views are never changed.

**On a roof?** The location line says "on a roof (outside)" and the walk tells you how to get in: hold **T**, aim at a
floor inside and let go (Go to point), or **F** to fly down - or start the walk from a 3D view that looks inside.

## Keys (standard - change them in the pause menu, Keys)

| Key | What it does |
|---|---|
| W A S D or the arrow keys | Walk forward, back, left, right. The mouse looks around. |
| Shift (hold) | Go three times faster |
| + / - or the mouse wheel | Speed step 1 to 5 (shown as "Speed 3/5") |
| C | Low eye on / off (1000 mm, or 300 mm under your eye height if that is lower) - look under ducts and into ceiling voids. In Fly and Pass-through: hold to sink |
| F | Fly on / off. Space (hold) rises, C (hold) sinks. Walls and slabs still stop you |
| G | Pass-through on / off - nothing stops you (past a locked room or a clash). Space rises, C sinks, as in Fly |
| E | Go through the door you look at (when the door hint shows) |
| T (hold, aim, let go) | Go to point - aim at a floor; a ring shows where you land |
| R | Back to start |
| Right-click, Tab (Shift+Tab back) | Change the mode: Inspect, Select, Snag, Hide |
| Left click | Do the mode's action on what is under the crosshair |
| M | Measure: M at point 1, M at point 2 |
| B | Save a viewpoint and name it |
| 1 to 9 | Go to viewpoint 1 to 9 |
| O | Play viewpoints (any move key stops) |
| J | Clear snag colours - every snagged element gets its own colours back |
| U | Show again what this walk hid |
| K | Snapshot - a picture of the view, without the HUD |
| Z | Clean screen - only the crosshair |
| H | Help card on / off |
| P or Esc | Pause |
| F12 | Frame timing (for checking speed on a big model), and a line saying why the third-person character is or is not drawn |
| Q | First person / third person - see a site worker where you stand (below) |

Fixed keys that cannot be changed: Esc, Tab, Enter, the mouse, 1-9 and the arrow keys. Left and right Shift, Ctrl
and Alt count as one key.

**Change a key:** pause (Esc), press **K** (or click **Keys**), click the action's key, then press the key you want
(Esc keeps the old one). A key another action already has is swapped with it, and the window says so. **Save** uses
the new keys at once and keeps them for your next walks (`%AppData%\HeronTools\ModelWalkKeys.txt`); **Cancel** or
the close box puts the old keys back. **Reset to standard keys** puts every key back to this table. The help card (H)
and the HUD hints always name the keys you set.

## Modes

| Mode | Left click | Changes the model? |
|---|---|---|
| **Inspect** (standard) | Nothing - the bottom line always shows what is under the crosshair: category, family : type, size, ID, level, system, link, and the distance | No |
| **Select** | Adds the element to Revit's selection (click again takes it out). It stays selected after the walk, ready to edit | No |
| **Snag** | Records a snag (below) and colours the element red in the walk view | Yes - one Undo step per snag; the red stays after the walk until J, Save or Sync gives the own colours back |
| **Hide** | Hides the element for now (Temporary Hide). U ends the walk view's Temporary Hide/Isolate - see the note below | No (temporary). If your Revit year asks for a transaction for it, it is one Undo step "Model Walk: hide" / "show hidden" |

**Hide and Show hidden - what Revit allows.** Revit has no "unhide one element": U ends the whole Temporary
Hide/Isolate of the walk view. So:

- Hide is refused while a Temporary Hide/Isolate of your own is already on in the walk view (reset it with the glasses
  on the view control bar first).
- If you pause and add your own temporary hide **on top of** what the walk hid, U (and the end of the walk) shows that
  too.
- If you pause, **reset** the walk's Temporary Hide and then hide things of your own, Model Walk notices (an element
  it hid is visible again) and leaves your hide alone. An isolate of your own that happens to hide everything the walk
  hid cannot be told apart, and U would end it.

Linked elements: Select and Hide refuse them (they cannot be picked alone). Snag records them, without a colour.

## Snags and the snag report

**Quick snagging (standard).** Pick **Snag** mode, aim, left click - and again, and again. Each click records the
element, colours it red and takes a picture of the view (the HUD is hidden for it, about a tenth of a second). No box
opens: a short message says "Snag n recorded ... - add a comment in the pause menu (Esc)" and you keep walking. A click
while the last picture (or a Snapshot) is still being taken is refused with "One moment"; click again. Esc right after a
snag waits that moment, so the snag keeps its picture.

**Write the comments afterwards.** Press **Esc**. The pause menu lists every snag (newest first): its number, category,
family : type and comment - **No comment yet** (in the warning colour) where there is none, and how many still have
none. Click a snag: a comment box opens under the list. Type, then **Enter** (or **Save comment**) - it is saved and the
box goes straight on to the next snag with no comment. **Esc** closes the box without saving (the next Esc ends the
walk, as usual). Empty the box and save to remove a comment. While you type, no walk or menu key acts (S, K, W ... are
just letters). Carry on walking, Open or End walk keep what you typed. **Open** on a row goes back to that snag.

**Comment at each snag instead.** Tick **Ask for a comment at each snag** in Settings: after each snag's picture a
small box asks for the comment, **Enter** saves, **Esc** skips (the snag is kept without one). You can still change any
comment in the pause menu.

Everything goes in one folder per walk, **next to the model**:
`<model folder>\Model Walk - <model name>\<date time>\`. If the model is not saved, is a cloud model, or that folder
cannot be written, it goes to `Documents\Heron Model Walk\<model name>\<date time>\`.

The folder holds:

- **Snags.csv** - opens in Excel. One row per snag: No., Time, Author, Comment, Category, Family : Type, Size,
  Element ID (full 64-bit) and Unique ID, Level, System, Link, Room, East / North / Elevation in **shared coordinates
  (mm)** - the numbers Spot Coordinates shows - the camera, the picture file and whether it was coloured.
  The columns are split with your Windows list separator (Region settings): a comma on English Windows, a semicolon
  where the decimal mark is a comma (German, French...). So a double-click opens it in columns either way. If you open
  it on a PC with another region and it lands in one column: Excel > Data > From Text/CSV, and pick the separator.
- **Snag 001.png**, **Snag 002.png**... - one picture per snag.
- **Snapshot ....png** - pictures taken with K.

The CSV is saved again after every snag and every comment (also each one saved in the pause menu), so nothing is lost
if Revit stops. If Excel has it open, close
it - the next change saves it again. **Open report folder** in the pause menu opens the folder.

**Snag colours:** the red stays in the walk view after the walk (standard), until J or **Clear snag colours** in the
pause menu, or the next Save or Sync with Central - no snag red is ever saved in the model. An element whose colour you
changed yourself after snagging is left as you set it. To get the colours back as soon as the walk ends, turn off
**Keep snag colours after the walk** in Settings.

## The pause menu (Esc)

- **Carry on walking** (or Enter).
- **Settings** (S), **Keys** (K), **Clear snag colours**, **Open report folder**, **End walk** (Esc).
- **Snags** - newest first, with the comment or **No comment yet**. Click one to write or change its comment (see
  above). **Open** on a row: the camera goes back where it was recorded and the element is added to Revit's
  selection - what you already selected stays (a linked element is not selected). **Save as 3D view** on a row makes
  a real Revit 3D view, "Model Walk - Snag 003 - *first words of the comment*" (the category if there is no comment
  yet), looking from where you stood when you recorded it - same Visibility/Graphics, filters and section box as the
  walk view, without the snag red. One Undo step. Use it for the issue meeting or on a sheet.
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

## Third person

**Q** switches to third person: the camera moves **3000 mm behind you and 500 mm to the right**, and a **site worker
in a yellow hard hat** (1850 mm tall, safety vest) stands where you are. He **stands** when you stop, **walks** when you
move and **runs** when you hold **Shift**, blending smoothly from one to the next. His legs speed up as you go faster, up
to a limit: at the faster speed steps (3.5 m/s and up, or Shift) he moves faster than a real person, so his feet slide
a little over the floor - that is expected. **Q** again goes back to first person. The status line (top left) always
says **First person** or **Third person**. To start every walk in third person, tick **Third-person view (show a
character)** in Settings.

- **Nothing is added to the model.** The character is drawn on screen by Revit's own graphics, in the walk view only -
  no element, no Undo step, nothing saved. Walls and ducts hide him when he walks behind them.
- Standing still with the mouse still, nothing is redrawn, so he holds his standing pose; he moves again as soon as
  you walk or look around. This keeps a big model from being redrawn 30 times a second just for him.
- **Character missing?** Press **F12**: the second line starts with why (for example "CUT OFF BY REVIT'S NEAR CUT" -
  in a very big model Revit itself does not draw anything close to the camera - or "OUTSIDE THE SECTION BOX"). Send
  that line with a bug report.
- If the character cannot be read (a damaged install), third person still works - the camera is behind you, nobody is
  drawn - and the walk says so once.
- When a wall is behind you, the camera **slides in** closer, always stopping in front of the surface, so it never
  looks through it.
- You still walk, bump into walls, climb steps and go through doors as **you**, not the camera.
- **Clicks act on what is under the crosshair**: Select, Snag, Hide, Measure, Go to point and the readout aim from the
  camera, so they hit what you see in the middle of the screen. The distance shown is from you, not from the camera.
- **Viewpoints** and **Open snag** keep where *you* stood and looked, not the camera behind you. "Save as 3D view"
  looks from your eyes (first person). When the walk ends, the walk view's camera goes back to your eyes, so the next
  walk starts where you stood.
- **Snag pictures never show the character** (he is hidden for the moment the picture is taken). **Snapshots (K) do
  show him** - they are what you see. Press Q first for a snapshot without him.

## Measure

**M** at the first point, **M** at the second. The HUD shows the distance in mm, with its horizontal and vertical
parts (for clearance above a duct). Between the two presses it shows the distance from point 1 to the crosshair.
Nothing is drawn on the model. M again starts a new measure.

## Settings (pause menu, S)

| Setting | Standard | Range |
|---|---|---|
| Mouse sensitivity | 100 % | 25 - 250 % (lower it if fast turning makes you feel unwell) |
| Invert look | off | mouse forward looks down |
| Third-person view (show a character) | off | start each walk in third person; Q switches during the walk |
| Walk speeds, slowest first | 0.8, 1.5, 3.5, 5.5, 8 m/s | 0.2 - 20 m/s, each faster than the last. Every walk starts on step 3. Fly and Pass-through x2, Shift x3 |
| Eye height | 1700 mm | 1000 - 2000 mm (low eye = 1000 mm, or 300 mm under the eye) |
| Step-up height | 350 mm | 100 - 450 mm - the highest stair or step you walk up |
| Keep snag colours after the walk | on | |
| Ask for a comment at each snag | off | off = quick snagging, comments in the pause menu; on = a box after each snag |

Saved for you on this computer (`%AppData%\HeronTools\ModelWalkSettings.txt`). Save applies them at once (ticking
or clearing Third-person view switches the view now too). **Reset to standard settings** fills in the standard values;
Save keeps them. Cancel changes nothing. The pause card says "Settings saved." (or why they could not be saved:
they still work for this walk).

## What goes in the Undo list

Making the walk view (first time only), each snag colour, Clear snag colours, giving colours back at the end, and
Save as 3D view - one named step each ("Model Walk: ..."); Hide and Show hidden too, only if your Revit year asks
for a transaction for them. Walking and looking make no Undo entries.
