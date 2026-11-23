# Heron Tools icon studio

The ribbon icons are **drawn in code**, not in an image program. `icon-studio.html` holds one small drawing
recipe per icon (shapes, colours, shine, shadow); the page draws each one at 16 and 32 px and shows it next to
today's icon, on a light and a dark Revit ribbon, plus a copy of the whole Heron tab. The look is set in
[STYLE.md](STYLE.md).

## Use it

1. Open `icon-studio.html` in Edge or Chrome (double-click). The top shows the Heron tab with every icon as it
   will look; below are the icons still being drawn, each with its options.
2. Export and check the files:

   ```
   powershell -ExecutionPolicy Bypass -File tools\icon-studio\export-icons.ps1 -Shot tools\icon-studio\export\page.png
   ```

   Each line says PASS or FAIL: size from the name (16, 32, `@2x`), 32-bit with transparency, clear corners,
   nothing cut at the border. Files land in `tools/icon-studio/export/` (not committed).
3. Show the owner. Only after his typed **"yes"**, copy `<Name>16.png` and `<Name>32.png` into
   `src/HeronTools/Resources/Icons/`, set that icon's `status` to `'in place'` in the page, and build
   (`build\build.ps1 -NoInstall`).

## Add an icon

In the `ICONS` list (ribbon order) add `{ name, title, status: 'review', what, variants: [...] }` - `name` is
the file name used by `BrandImages.Load` in `src/HeronTools/App/HeronToolsApp.cs`. Draw it with
`softKit(size, { allOrange: true })` (orange clay) or `softKit(size)` (white soft 3D): `k.part(path, tone,
round, clay)` makes one rounded part (tones `lit`, `light`, `deep`, `grey`, `recess`), `k.render(body)` adds
the shadow and leaves room for it. An open path is a line (a wire, a handle); a path ending in `Z` is filled.
Keep a 16 px drawing simpler than the 32 px one.

## Status (2026-11-23)

| Done (in the add-in) | Still today's flat icon |
|---|---|
| Quick Menu, Quick Menu Settings (white soft 3D) | Unhide All (options A eye / B eye + elements / C eye + rays drawn; owner had not chosen) |
| Filter Pro, Colorize (roller), Reset Graphics (ring arrow) | Toggle Links, Highlight |
| View Crop (crop tool), 3D Section Box (camera) | Reset Datums, Level Extents, Flip Bubbles, Auto Dimension (new on main, its own flat icon) |
| | Match Elevation, Reassign Level, Pin / Unpin, Smart Selection |
| | Quick Clash, Model Walk, Element ID, Find by ID, 3D Views by Workset, Link Workset |
| | About Heron (the Heron app mark - probably keep) |

Annotation Crop needs no icon: its button goes into the Crop View window (branch
`claude/annotation-crop-into-view-crop-e6719d`); `AnnotationCrop16/32.png` are unused once that lands.
How the new icons look on the real ribbon still needs a Revit check, light and dark theme.
