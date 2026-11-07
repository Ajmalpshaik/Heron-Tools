# Metadata standard

**Every Heron Tools source file carries the same five metadata fields as the Heron AI platform.** Same
names, same allowed values, read the same way - so a file from either product reads alike and the Heron
family's tools can treat them alike.

Checked by [`build/check-metadata.ps1`](../build/check-metadata.ps1), which `build/build.ps1` runs before
it builds anything: a file without its header stops the build.

## The five fields

| Field | Meaning | Allowed values |
|---|---|---|
| `Heron-Agent` | Which Heron AI registry agent(s) this file implements | `none` for Heron Tools (it is a stand-alone add-in and implements no agent). It may not be left out: an absent field is a gap, `none` is a decision |
| `Heron-Step` | The Heron Tools build step that introduced it | any value; `1` = the first release (Filter Pro moved in) |
| `Heron-Status` | Lifecycle stage | `DISCOVERED` · `DRAFT` · `TESTING` · `VALIDATED` · `SHADOW` · `PROVEN` · `PRODUCTION` · `DEPRECATED` · `ARCHIVED` |
| `Heron-Since` | The Heron Tools version it first appeared in | any value, e.g. `1.0.0` |
| `Heron-Layer` | Which layer the file belongs to | `bridge` · `revit` · `brain` · `platform` · `test` · `tool` - add-in code is `revit`, build scripts are `tool` |

**Status follows the Heron trust model.** A file is `DRAFT` until it has been proven in a real Revit
model, then `PROVEN`. Everything in Heron Tools 1.0.0 is `DRAFT` until the Filter Pro Revit test passes.

## Where the checker looks

Every `.cs`, `.ps1` and `.xaml` file under `src/` and `build/`, skipping `bin`, `obj` and `.vs`. A field
counts only in the file's first 41 lines, on a comment line.

## What it looks like

C#:

```csharp
// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// One line saying what the file is for.
```

PowerShell (before the help block, which PowerShell allows):

```powershell
# Heron-Agent:  none
# Heron-Step:   1
# Heron-Status: DRAFT
# Heron-Since:  1.0.0
# Heron-Layer:  tool
# See docs/metadata-standard.md
```

XAML: the same five lines inside the file's first `<!-- -->` comment.

History is not kept in file headers: it lives in git and in `docs/` (the old AJ-Tools change logs that
these files used to carry are in the AJ-Tools repository).
