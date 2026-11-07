# AGENTS.md - for an AI agent working on Heron Tools

## What Heron Tools is

A stand-alone Revit add-in (Revit 2020-2027) of everyday BIM tools on the shared **Heron** ribbon tab.
It is a sibling of the Heron AI platform: same tab name, same brand, same file metadata standard - and
**no shared code**. The owner, Ajmal PS, is an MEP BIM Coordinator, not a developer: explain in plain words,
with Revit terms, answer first.

## Reading order

1. `README.md` - what it is, build, install, the ribbon, the rules.
2. `CONTRIBUTING.md` - the absolute rules.
3. `docs/metadata-standard.md` - the five header fields every source file carries.
4. `docs/filter-pro-move-notes.md` - every defect already found and fixed, and why.
5. `src/HeronTools/UI/HeronStyles.xaml` header - the brand palette.

## Never

- Reference, copy or load a Heron AI assembly or file.
- Commit Autodesk Revit DLLs, client models, drawings or project data.
- Add a window colour that is not in `HeronStyles.xaml`, or a second orange button.
- Start a transaction from a modeless window (use an `ExternalEvent`), or leave a run half-committed.
- Overwrite a modeller's graphic overrides without a way back.
- Compare element ids as `int` (64-bit from Revit 2024).
- Claim a tool works because it compiles. Only a real Revit test proves it; say what still needs one.
- Merge, delete, or remove anything from AJ-Tools without the owner's typed "yes".

## How to work

- Build every Revit year: `powershell -ExecutionPolicy Bypass -File build\build.ps1 -NoInstall`
  (it runs the metadata check first). Fix every warning.
- Report results in four states: PASS / FAIL / NOT RUN (with why) / NEEDS REAL REVIT.
- Check a window's layout without Revit by parsing its XAML against the built theme and rendering it.
- If something about the Revit API is unclear, check the API notes in the `Nice3point.Revit.Api` NuGet package
  for that year, or stop and ask - do not guess.

## Where the truth lives

| Question | Source |
|---|---|
| Which .NET for which Revit | `Directory.Build.props` |
| What a ribbon button does and when it is enabled | `src/HeronTools/App/HeronToolsApp.cs` |
| Brand colours | `src/HeronTools/UI/HeronStyles.xaml` (from the Heron brand kit) |
| What was fixed and why | `docs/filter-pro-move-notes.md`, git history |

## If two sources disagree

The code and the build win over a document; fix the document in the same change.
