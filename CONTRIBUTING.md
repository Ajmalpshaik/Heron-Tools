# Contributing to Heron Tools

Thank you for helping. Heron Tools is used on live Revit project models, so every change is held to one
question first: **can this damage a modeller's model or waste their afternoon?**

## Before you start

- Read [README.md](README.md) (what the add-in is, how it is built and installed) and
  [docs/filter-pro-move-notes.md](docs/filter-pro-move-notes.md) (the lessons already learned).
- Heron Tools stands alone. It shares only the **Heron** ribbon tab name, the brand and the file
  metadata standard with the Heron AI platform - never reference, copy or load a Heron AI assembly.
- Every tool must work in **Revit 2020 to 2027**. A change that only works in the newest Revit is not
  finished.

## Ways to contribute

### Bug reports

Use the **Bug report** issue form. Say which tool, which Revit version, the view type, what you did,
what happened and what you expected. **Never attach a real client model** - describe it, or make a small
test model that shows the problem.

### A new tool or a better one

Use the **Feature request** form. Describe it in modeller's words: what you click, what you pick, what
changes in the model, and how you undo it.

### Documentation

Plain words, short sentences, Revit's own vocabulary (element, family, parameter, view, sheet).

## Absolute rules

1. **One button = one named transaction = one Undo step.** If anything fails, the whole run is rolled
   back and the modeller is told, in plain words, that nothing was changed. Check `Commit()`'s result -
   a change Revit refuses is a failure, not a success.
2. **Never destroy what the modeller set.** A tool that writes graphic overrides remembers the old ones
   and offers a way back (Filter Pro keeps every filter's overrides; Colorize has Restore original;
   Highlight puts the view back on the second click).
3. **The Revit API only on Revit's thread.** A modal window may call it inside its command; a modeless
   window must go through an `ExternalEvent`.
4. **Version-specific code only in `src/HeronTools/Helpers/`**, behind `REVIT<year>` /
   `REVIT<year>_OR_GREATER`. Element ids are 64-bit from Revit 2024: compare `ElementId`s or
   `LongValue()`, never an `int`.
5. **The ribbon joins the Heron tab, never assumes it is first** - the exact
   `try { CreateRibbonTab } catch (Autodesk.Revit.Exceptions.ArgumentException) { }` shape in
   `HeronToolsApp.cs`. No bare `catch`.
6. **Brand only.** Window colours and font come from `src/HeronTools/UI/HeronStyles.xaml` (the Heron brand
   palette). Never add a colour that is not in it. One orange (Accent) button per window.
7. **Every source file carries the five Heron metadata fields** ([docs/metadata-standard.md](docs/metadata-standard.md)).
   `build/check-metadata.ps1` refuses a file without them.
8. **Never commit Autodesk's Revit DLLs**, client models, drawings or project data.
9. **No "done" pop-ups.** Results go in the window's status line and skipped-items list; a pop-up only for
   a real problem the modeller must act on.

## Pull request process

1. Branch from `main`; keep one subject per pull request.
2. Run the build for every Revit year and fix every warning:

   ```powershell
   powershell -ExecutionPolicy Bypass -File build\build.ps1 -NoInstall
   ```

3. Fill in the pull request template, including the four-state results (PASS / FAIL / NOT RUN /
   NEEDS REAL REVIT) and the Revit test checklist. **A compile is not a Revit test** - say what still needs
   one.
4. The `Gates` check (metadata + compile 2020-2027) must pass.
5. The maintainer tests in Revit before merging. Nothing is merged without his word.

## Code style

- C#, `LangVersion latest`, nullable off, no implicit usings - as the project sets them.
- Explain **why** in comments, in plain words; the code already says what.
- Messages to the modeller say what happened, why, and what to do next. Never a raw exception alone.
