# Privacy policy - Heron Tools

*Last updated: 2026-11-23. Applies to Heron Tools, the free add-in for Autodesk Revit by Ajmal PS.*

**Short answer: Heron Tools collects no personal data and sends nothing over the internet.**

## What Heron Tools does not do

- It makes **no network connection** of its own: no usage statistics, no crash reports sent anywhere, no update
  check, no licence check, no account, no sign-in, no adverts.
- It does not read, copy or upload your Revit models, drawings or project data. It works inside the Revit that is
  open on your PC, on the model you are using, and changes only what you ask a tool to change.
- It does not collect your name, email address, company or any other personal detail.

## What it keeps on your PC

Heron Tools remembers a few choices so a window opens the way you left it. Everything is plain text, saved only
for your Windows user on your own PC, in this folder:

`%AppData%\HeronTools\` (normally `C:\Users\<you>\AppData\Roaming\HeronTools\`)

| File or folder | What it holds |
|---|---|
| `quickmenu-slots.txt` | The tools you put on the Quick Menu wheel and how the wheel looks |
| `AutoDimension.config` | Your Auto Dimension settings (gaps, types, ends, names to leave out) |
| `ViewCrop.config` | Your View Crop settings (margin, annotation crop offset, ticks) |
| `SetLinkWorkset.config` | Your Link Workset choices |
| `memory-filter-pro.txt`, `memory-colorize.txt` | The categories, parameter and values you picked last time in Filter Pro and Colorize |
| `memory-pin-unpin.txt` | Your last Pin / Unpin choices |
| `memory-reassign-level.txt` | Your last Reassign Level choices (level names) |
| `memory-find-by-element-id.txt` | The last Element ID you searched for |
| `memory-quick-clash.txt` | Your last Quick Clash choices (category names) |
| `Logs\HeronTools_<tool>_Error_<date>.log` | Written only if a tool fails unexpectedly: the tool's name, the time and the technical error text. It stays on your PC; Heron Tools never sends it. If you report a problem, you choose whether to share it |

Quick Clash also keeps the colours it overwrote, so a Revit crash does not lose them, in
`%LocalAppData%\HeronTools\QuickClash\` (files with a code as the name). They hold colour and line settings only.

If a profile has no AppData folder, Windows' temp folder is used instead.

**Model Walk** (in the GitHub installer version only; it is left out of the Autodesk App Store version) also keeps its
settings and key choices (`ModelWalkSettings.txt`, `ModelWalkKeys.txt`) and saved camera viewpoints
(`ModelWalk Viewpoints\`) in the same folder. Its snag pictures and CSV report are saved next to your model, in a
folder you can see, and go nowhere else.

## How to delete it

Close Revit, then delete the folder `%AppData%\HeronTools` (paste that into the Windows Explorer address bar) and
the folder `%LocalAppData%\HeronTools`. The tools simply start with their standard choices next time. Uninstalling
Heron Tools does not remove these small files on its own, so delete them if you want them gone.

## Links

The only web links are in the About Heron window and the help file (the Heron Tools and Heron AI GitHub pages, this
policy, the licence). They open in your own web browser **only when you click them**. Heron Tools does not open them
by itself. GitHub's own privacy rules apply once you are on their pages.

## Children, sharing, changes

Heron Tools is a work tool for Revit users. Because nothing is collected, there is nothing to share, sell or
hand over. If this policy changes, the new text and date will appear here, and the change will be listed in the
[changelog](CHANGELOG.md).

## Contact

Questions about privacy, or a problem: [open an issue](https://github.com/Ajmalpshaik/Heron-Tools/issues/new/choose).
Please do not attach client models or personal data.

This policy is public at
<https://github.com/Ajmalpshaik/Heron-Tools/blob/main/PRIVACY.md>.
