# Quick Clash Implementation Plan

> Executed natively in the session that wrote it (Ajmal typed "Yes, build it" on 2026-11-14).
> There is no unit-test project in Heron Tools: each task is proven by the all-years build
> (`build\build.ps1 -NoInstall`, 0 warnings, metadata check) and, for the window, by rendering it outside Revit.
> Everything else is NEEDS REAL REVIT.

**Goal:** A modeless Navisworks-style clash test in the active 3D view, coloured temporarily, Reset = exact way back.

**Architecture:** WPF window -> one `ExternalEvent` -> handler runs collector, engine and colour memory on
Revit's thread; linked clash elements are drawn by a `DirectContext3D` server. Save/Sync events reset first.

**Tech Stack:** C#, WPF, Revit API 2020-2027 (`Nice3point.Revit.Api`), multi-target via `Directory.Build.props`.

**Spec:** `docs/superpowers/specs/2026-11-14-quick-clash-design.md`

## Global Constraints

- Revit 2020-2027; ids via `LongValue()`; version-specific code only in `Helpers/` (`REVIT2023_OR_GREATER` for the link-in-view collector).
- Five metadata lines on every new file; window colours only from `HeronStyles.xaml`; one orange button (Run).
- No "done" pop-ups; status line instead. One named transaction per Run ("Quick Clash") and Reset ("Quick Clash - Reset").
- Defaults: tolerance 10 mm, clearance 50 mm, A red (255,0,0), B green (0,192,0), rest grey (180,180,180) at 70 % see-through, stop at 500 clashes, ask above 50,000 pairs.

## Review Focus

1. A model with no links and B = "This model" with the same category ticked on both sides - no element may clash with itself, each pair once.
2. The modeller switches to a plan view while the window is open - Run refused with a plain status line, Reset still works on the coloured view.
3. Elements deleted between Run and Reset - Reset skips them and restores the rest.
4. A link that is unloaded while coloured - overlay must not throw while drawing.
5. Hand-set overrides on an element before Run - identical after Reset (Highlight's memory idea).

## Tasks

1. **Models + settings** - `Models/QuickClash/ClashResult.cs`, `ClashCategoryItem.cs`, `ClashSourceItem.cs`, `QuickClashSettings.cs` (load/save through `WindowMemory("quick-clash")`).
2. **Link collector helper** - `Helpers/LinkElementsInView.cs`: `IList<Element> Collect(Document hostDoc, View3D view, RevitLinkInstance link)`; 2023+ uses the link-in-view collector, earlier years the section-box test.
3. **Engine** - `Services/QuickClash/ClashGeometry.cs` (solids + boxes per element, transform), `ClashEngine.cs` (sweep-and-prune, connector skip, hard depth, clearance gap, 500 cap, Esc stop).
4. **Colour memory + overlay** - `Services/QuickClash/ClashColourMemory.cs` (save originals, apply, restore, still-coloured check, disk copy), `ClashOverlayServer.cs` (`IDirectContext3DServer`).
5. **Request handler + session** - `Services/QuickClash/QuickClashSession.cs` (state per open window, events Save/Sync/Undo/close), `QuickClashRequestHandler.cs` (`IExternalEventHandler`).
6. **Window** - `UI/QuickClashWindow.xaml(.cs)` as approved in the picture; colour swatches open the Windows colour picker (`System.Windows.Forms.ColorDialog` is not referenced - use Revit's `ColorSelectionDialog`, run through the handler).
7. **Command, availability, ribbon, icon** - `Commands/CmdQuickClash.cs`, `CmdQuickClashAvailability.cs`, Coordination panel button, `QuickClash16/32.png`.
8. **Docs + build** - README row, CHANGELOG line, all-years build, window render.
