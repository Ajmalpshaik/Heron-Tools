## What this changes

<!-- In modeller's words: which tool, what it now does differently, and why. -->

## Results

| Check | State |
|---|---|
| Metadata check | PASS / FAIL |
| `dotnet build` 2020-2027 (0 warnings) | PASS / FAIL |
| Install into the Revits on the build PC | PASS / NOT RUN (why) |
| Window layout loads outside Revit | PASS / NOT RUN |
| Tried in a real Revit model | NEEDS REAL REVIT / PASS (which version) |

## Needs a real Revit test

- [ ] ...

## Checklist

- [ ] One named transaction per button; rolled back on failure; commit result checked
- [ ] No overrides or settings destroyed without a way back
- [ ] Works 2020-2027 (version code only in `Helpers/`), ids compared as 64-bit
- [ ] Colours only from `HeronStyles.xaml`; one orange button per window
- [ ] Heron metadata header on every new file
- [ ] No Revit DLLs, client models or project data committed
