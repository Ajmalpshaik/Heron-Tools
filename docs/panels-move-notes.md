# Quick, Datums, Modify, Coordination and About: moved from AJ-Tools (2026-11-07)

The Quick Menu, the whole Datums, Modify and Coordination panels were copied from AJ-Tools
(`D:\06_Development\01_AJ-Tools\Revit Addins`), renamed into Heron Tools, and then checked line by line by
two independent audits (Datums + Modify; Coordination + Quick Menu + About + ribbon), against the Revit API
documentation for 2020 and 2027 and against Heron AI's proven building blocks. Every window was redesigned
on the Heron brand. The **About Heron** button is new: one About for the whole Heron family.

Nothing here has run in a real Revit yet - every tool still **needs a real Revit test**.

## Ribbon (Heron tab)

| Panel | Buttons |
|---|---|
| Quick | `Quick Menu` split button: the wheel, and Quick Menu Settings |
| View | (see [View tools move notes](view-tools-move-notes.md)) |
| Datums | `Reset Datums to 3D` (Grids / Levels / Grids and Levels) · `Level Extents` (Match Level Extents / Levels to Section Box) · `Flip Bubbles` |
| Modify | `Match Elevation` (Centre / Top / Bottom) · `Reassign Level` · `Pin / Unpin` · `Smart Selection` |
| Coordination | `Element ID` (ID of a Picked Element / Find by Element ID) · `3D Views by Workset` · `Link Workset` |
| About | `About Heron` - version, Revit and .NET, the two Heron products and their GitHub pages |

The Quick Menu only offers this add-in's own buttons (class name `HeronTools.*` **and** the class really is in
`HeronTools.dll`) plus Revit's own commands - never another product's button on the shared tab.

## Defects found and fixed

| Tool | Problem in the AJ-Tools version | Fix |
|---|---|---|
| Reassign Level | A level-based family's Host **is** its level, so every FCU, light and sprinkler was skipped as "hosted"; only ducts and pipes ever changed. | Only a real host (wall, ceiling, face) counts as hosted. |
| Reassign Level | The height was "kept" by a sum on 4 categories, written **before** the level, through the wrong parameter for level-based families, never read back; if the level then failed, the element had moved and the run still committed. | Each element is **measured** before and after in its own sub-step: level set, position read, offset corrected if Revit moved it, read again - and if it is still not where it was, that element alone is put back and reported. |
| Reassign Level | Spaces were **deleted and re-made**: new Id (tags, zone and schedule links lost), Base Offset not corrected, Upper Limit overwritten, Limit Offset forced to 1 m, no phase. | Spaces are no longer touched: this tool moves elements only (equipment, ducts, pipes, trays, fixtures), never spaces, rooms or zones. Confirmed by Ajmal 2026-11-08. |
| Reassign Level | MEP curves were not checked for movement. | Measured like everything else. |
| All Modify/Datums tools | `Commit()` results never checked: in a workshared model a refused commit still reported "40/40 updated". | Every commit is checked; a refused one says nothing changed. Empty runs are rolled back. |
| Pin / Unpin | Element ids cut to 32 bits on Revit 2024+ (wrong element pinned, or skipped). One element owned by a colleague made Revit refuse the whole run. | 64-bit ids; elements a colleague owns are left alone and counted. |
| Match Level Extents, Flip Bubbles | One Undo entry per pick; an error part-way made Revit undo the earlier picks while only showing the error. | The whole run is one Undo step (a TransactionGroup stays open across picks). |
| Match Level Extents, Levels to Section Box | Forced both ends to 3D in **every** elevation, section and 3D view - wiping every 2D level end project-wide with no warning. | Only the 3D extent changes; 2D ends stay 2D. |
| Match Level Extents | Offset taken from `Level.Elevation`, which depends on the level type's base (Project / Shared). | Taken from the two level lines themselves. |
| Flip Bubbles | Refused detail views; no read-back, so a datum a view template controls counted as flipped. | Detail views allowed; each flip is read back. |
| Match Elevation | Pipe top/bottom used the **nominal** diameter (DN100 steel lands ~7 mm off). | Outside Diameter first, nominal only as fallback - as Revit's own Top/Bottom Elevation. |
| Reset Datums | One Undo name for all three modes; ends already on 3D counted as "reset"; per-end failures silent. | Undo name per mode; honest counts; ends that cannot change are reported. |
| 3D Views by Workset | Commit unchecked; every name error counted as "already exists" (a workset name may hold `:` or `\`, a view name may not); a 3D view type's default template could override the per-view workset visibility. | Commit checked; names a view cannot take are listed; a template the new view inherits is taken off and the report says so. |
| Element ID, Find by Element ID | IDs above 2,147,483,647 rejected / shown cut short (Revit 2024+). | 64-bit throughout; before 2024 a too-large number gets a plain message. |
| Find by Element ID | Zoom used only 2 corners and ignored the box's own transform: wrong box for a rotated link, zero-height box for a flat element; the fallback still said "Found in". | All 8 corners through the box and link transforms, padded; the fallback says it shows the whole link. |
| Link Workset | Every link and import started **ticked** and Revit's selection was ignored - one click moved them all. Typed names not trimmed, matched case-sensitively, banned characters reached Revit as a raw error. | Only the links you selected start ticked; names trimmed, matched ignoring case, banned characters explained. |
| Quick Menu | A garbled or crash-emptied settings file loaded as an empty wheel and was then saved over; the save itself was not atomic. Revit-command slots always looked usable. Old "AJ Quick Menu" title. | Such a file counts as unreadable (defaults shown, Save off); saves go through a temporary file; Revit commands are asked `CanPostCommand` and greyed; "Heron Quick Menu". |
| Several commands | Returned `Failed` right after their own message, so Revit could add a second, generic failure box. | `Cancelled` after the tool's own message. |
| Theme | The search box applied its padding twice (text started ~60 px in); the empty-list hint never showed (a text/true-false compare). | Fixed once in `HeronStyles.xaml`, for every window. |

Found by the fresh re-check of the earlier tools in the same pass: Filter Pro moved existing filters to the
top of Visibility/Graphics on Shuffle and re-Apply (now only new filters go first), and reused a same-named
filter for other categories (now refused with a message); Unhide All now also finds annotation hidden in a
dependent view; Toggle Links can no longer undo its own committed change; Colorize now also colours
insulation and lining, as Highlight already did.

## Install, like Heron AI's installer

`build/build.ps1` now carries the Heron AI platform's install safeguards (`tools/deploy-addin.ps1`; ideas only,
no code shared):

- the built DLL's own TargetFramework is read and must match the Revit year's .NET (4.7.2 / 4.8 / 8 / 10);
- .NET 8 and 10 years must have their `HeronTools.deps.json`;
- a `<ManifestSettings>` element in a manifest for Revit 2025 or older stops the build (it crashes Revit);
- a Revit whose year cannot be read blocks every install; a year whose Revit is open is skipped; Revit is never closed;
- the install being replaced is kept in `%LocalAppData%\HeronTools\install-backup\<year>\` with
  `replaced.json` (version, SHA-256, time); `-Rollback` puts it back; a failed copy puts it back by itself;
- Autodesk's own DLLs are never copied; copied files lose the "downloaded from the internet" mark;
- every copied file is checked byte for byte;
- an all-users copy of Heron Tools (ProgramData, or Program Files from 2027) is reported - Revit would load it twice.

`build/uninstall.ps1` got the same Revit checks, reports all-users copies, and `-RemoveBackups`.

## Still needs a real Revit test

Every tool above. In particular: Reassign Level's measured move on ducts, pipes (sloped too), equipment and
level-based families; Match Level Extents and Levels to Section Box (`SetCurveInView` may refuse in some views);
Flip Bubbles' read-back under a view template; the Quick Menu's control-id lookup; and installing on each of
2020-2027.
