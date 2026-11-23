# The Autodesk App Store zip

How to make and check the one zip Autodesk asks for to list Heron Tools on the Autodesk App Store (Design and
Make Marketplace). Autodesk builds the Store installer from this zip; our own `HeronToolsInstaller.exe` is not
what the Store ships.

## Make it

```powershell
powershell -ExecutionPolicy Bypass -File build\build-store-package.ps1
```

It builds the **Store version** for Revit 2022 to 2027 and writes `dist\HeronTools-Store-<version>.zip`.
Nothing is installed on the PC. Options: `-Years 2026,2027` (only some years), `-SkipBuild` (pack what is
already built), `-SupportEmail <address>` (the public support email written into the package).

At the end it prints a table (one row per Revit year), the list of files in the zip, and either
**Zip made and checked** or **NOT READY TO SUBMIT** with what is missing (help file, Store icon, support email).

## What the Store version leaves out

**Model Walk** is not finished, so the Store version has none of it: no button, no code, no windows, no walking
character, no icons. The GitHub version keeps all of it.

It is one switch, `-p:HeronStore=true` (the script sets it). It gives the `HERON_STORE` compile symbol, removes
the `ModelWalk\` folder and its resources in `HeronTools.csproj`, and builds into its own folders
(`bin\Release\Store\<year>`, `obj\Store\`), so it never mixes with the normal build. The Quick Menu reads its
tools from the live ribbon, so a slot that held Model Walk (saved from the GitHub version) simply shows empty.

## What is inside the zip

```
HeronTools.bundle\
  PackageContents.xml                 what Revit and Autodesk's installer read: one <Components> per Revit year
  Contents\
    2022\HeronTools.addin             = manifests\HeronTools.2022.addin, unchanged (same AddInId)
    2022\HeronTools\HeronTools.dll
    ...
    2027\HeronTools.addin
    2027\HeronTools\HeronTools.dll + HeronTools.deps.json   (2025 and later)
    Resources\HeronTools-Help.html    the help file (from docs\store\help\)
    Resources\HeronTools.png          the Store icon (docs\store\heron-tools-store-icon-120.png)
    Resources\LICENSE.txt, NOTICE.txt, Manrope-OFL.txt      the licences that travel with the add-in
```

Revit loads a bundle from `%AppData%\Autodesk\ApplicationPlugins` or `%ProgramData%\Autodesk\ApplicationPlugins`;
Autodesk's Store installer puts it there.

**Where the layout comes from.** The `PackageContents.xml` root, `CompanyDetails` and the one-block-per-year
shape (`SeriesMin` = `SeriesMax` = `R<year>`, `ModuleName="./Contents/<year>/....addin"`) copy Autodesk's own
Revit bundle for the IFC exporter (github.com/Autodesk/revit-ifc, `Install\Program Files to Install\bundle`).
That file has one year; putting **several years** in one bundle this way follows an Autodesk developer blog
post (blog.autodesk.io, "Understanding the role of SeriesMin and SeriesMax", 2025), which says Revit reads only
`SeriesMin`, so one block per year is the reliable way. Only a real install proves it (below).

**Fixed codes.** `UpgradeCode` `{57635C9D-886C-474D-8845-A71D1D68C10C}` is fixed for the life of the product
(never change it; the GitHub installer and `build.ps1` also use it to find the Store version). `ProductCode`
is made from it and the version, so it is the same every time for one version and new for every new version.
The version is read from `src\HeronTools\HeronTools.csproj`.

**Adding Revit 2020 and 2021** (if Autodesk agrees): add them to the one line `$storeYears` in
`build\build-store-package.ps1`.

## Check it

The script already checks, and stops without a zip if any fails:

| Check | How |
|---|---|
| Built for the right .NET | reads the target framework written into each DLL |
| No Model Walk left | searches each Store DLL for `modelwalk`, `walkcharacter`, `model walk` (ASCII and UTF-16) |
| The XML is right | reads `PackageContents.xml` back; every `ModuleName`, `HelpFile` and `Icon` is in the bundle |
| Each `.addin` finds its DLL | the `Assembly` path of each `.addin` is resolved inside the bundle |
| Same AddInId as today | each bundled `.addin` matches `manifests\HeronTools.<year>.addin` |
| Zip shape | every entry starts with `HeronTools.bundle/`, forward slashes, under Autodesk's 600 MB |

To look yourself: unzip it into an empty folder (never into Revit's folders) and open `PackageContents.xml`.

**Still NEEDS A REAL REVIT** (on a test PC, not a working one): copy the unzipped `HeronTools.bundle` into
`%AppData%\Autodesk\ApplicationPlugins`, with the GitHub version removed first, then open Revit 2022 and 2027
(and any other year you can): the Heron tab shows every tool except Model Walk, with no message at start.
Remove the bundle folder afterwards.

## The Store version and the GitHub version together

Both use the same AddInId for each Revit year, so Revit loads only one and shows a "duplicate add-in" message at
every start if both are there.

- **Store first, then the GitHub installer:** the GitHub installer (and `build\build.ps1`) finds the Store
  version (`HeronTools.bundle` in an ApplicationPlugins folder) and leaves that Revit year out, saying why. The
  Store version is never touched.
- **GitHub first, then the Store:** Autodesk writes that installer, so it cannot be stopped from our side. The
  help page should ask people to remove the GitHub version first (`HeronToolsInstaller.exe` > Remove Heron Tools).
