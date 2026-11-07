# Disclaimer

## Heron Tools changes live Revit models

Its tools create view filters, write graphic overrides, change crop regions, unhide elements and switch
category visibility in the views you choose. Each run is one Undo step and is rolled back if anything fails,
but **you are responsible for the model**.

## Before you use it

- Try a tool on a copy, or a test model, before using it on a live project for the first time.
- Save before a large run (many views, many filters).
- Check the result in the view, and read the skipped-items list - it tells you what was not changed and why.

## On worksharing / central models

- Tools change views and view-specific settings. If another user owns a view, Revit refuses the change and
  Heron Tools reports it as not done.
- Filters created by Filter Pro belong to the whole project: once created they can be added to any view,
  but Filter Pro only adds them to the views you picked. "Update existing filters" rewrites a filter for every
  view that uses it - it is off by default for that reason.
- Synchronise as your team normally does; Heron Tools does not synchronise for you.

## What Heron Tools sends where

Nothing. Heron Tools makes no network connection. It saves only its own window settings, and a crash log
if a tool fails unexpectedly, in `%AppData%\HeronTools`.

## Graphic overrides

Colorize and Highlight Selection remember each element's own overrides **for the current Revit session**
and put them back with Restore original / a second Highlight click. After Revit is closed that memory is
gone; Undo, or Revit's own Override Graphics in View > Reset, are then the way back.

## No warranty

The software is provided as is, without warranty of any kind. The authors are not liable for any damage,
data loss or project delay arising from its use.
