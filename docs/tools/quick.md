# Quick panel

[Tool catalogue](README.md) > Quick

One split button, **Quick Menu**. Click its face to open the wheel; click its small arrow for **Quick Menu
Settings**.

## Quick Menu

**What it does:** opens a tool wheel around the mouse pointer, with your favourite Heron Tools buttons and Revit
commands in a ring.

**When to use it:** when you use the same few tools all day and do not want to travel to the ribbon each time.

**How to use it:**

1. Click **Quick Menu** (Heron tab > Quick). The wheel opens where the mouse pointer is.
2. Point at a tool and click it, or press its number.
3. **Esc** closes the wheel without running anything.

**Tip:** give the button a keyboard shortcut so the wheel opens right where you work: **File > Options > User
Interface > Keyboard Shortcuts**, search **Quick Menu**, add a key.

**What it changes in the model:** nothing by itself. The tool you pick does its own work (and has its own Undo
step).

**Limits:**

- A slot whose tool cannot run in the open view is shown as not available (the same rule that greys out the
  ribbon button). Revit commands that cannot run right now are greyed too.
- The wheel offers only Heron Tools' own buttons and Revit's own commands, never another add-in's button.

Screenshot needed: the Quick Menu wheel open over a Revit floor plan.

## Quick Menu Settings

**What it does:** chooses which tool sits in each slot of the wheel, how many slots there are (4 to 12, 8 as
standard) and how big the wheel opens.

**How to use it:** Heron tab > Quick > arrow under **Quick Menu** > **Quick Menu Settings**. Put a tool in each
slot, set the number of slots and the size, then save. It works even with no model open.

**What it changes in the model:** nothing. The layout is saved for your Windows user on this PC in
`%AppData%\HeronTools\quickmenu-slots.txt`, so it stays after Revit restarts and Heron Tools updates.

**Undo:** change the slots again in the same window. If the settings file is damaged, the window shows the
standard layout and does not save over the damaged file.

Screenshot needed: the Quick Menu Settings window.

## Revit versions

2020 to 2027.
