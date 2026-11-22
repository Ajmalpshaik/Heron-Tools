# Quick Menu Colour and Effect - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (native, this session). Steps use `- [ ]`.

**Goal:** two new Quick Menu settings, Colour (Dark / Light) and Effect (Flat / Soft / Clay), that work with all six designs.

**Architecture:** the look stays in `QuickMenuLook` and the shapes stay in `QuickMenuDesign`. The wheel picks its
brushes per Colour and draws each slot, the hub and the Soft plate per Effect. Soft and Clay shading (outer shadows and
clipped, blurred inner shadows) is rendered ONCE into frozen bitmaps (idle state in the constructor, lit state just after
the wheel opens), so the see-through, CPU-drawn window never runs a live effect. The Settings window gets two combos;
its slot map keeps its own look.

**Tech Stack:** WPF on .NET Framework 4.7.2 / 4.8 and .NET 8 / 10 (Revit 2020-2027), C# latest, no new packages.

**Spec:** agreed in chat with Ajmal PS on 2026-11-22: interactive page https://claude.ai/artifact/6BRP13Cym653teTWQFvKd2
(source and approved mockups in the shared side-session notes folder `quick-menu-looks/`).

## Global Constraints

- Colours only from `UI/HeronStyles.xaml`; every new shade is a see-through step of a brand colour, listed in its header.
- Flat is the default Effect and Dark the default Colour: an upgraded wheel looks exactly as before.
- Saved file: new lines `Colour=Dark|Light`, `Effect=Flat|Soft|Clay`; a missing or unknown value gives the default and
  never marks the file `.bad`.
- All six designs stay. A design keeps its markers (arc, needle, chevron, rim lights, ruler, digits).
- Soft: no step-out; the lit slot is pressed in with an Accent wash. Clay: lift = max(design lift, 6); the lit slot turns
  to an Accent Light -> Accent clay with a white icon (tinted by an opacity mask, not an effect) and a white name.
- 0 warnings in every Revit year; `build\build.ps1 -NoInstall` passes (metadata check first).
- Never edit `src/HeronTools/Resources/Icons/`. No commit unless Ajmal asks.

## Review Focus

- A greyed-out (unavailable) slot under the pointer in Soft / Clay: stays unlit, centre says why.
- An empty slot in Soft / Clay: drawn dimmer, says "Empty", never lights.
- 4 and 12 slots and the smallest size (420): content fits, shadows are not clipped by the window edge.
- Light colour over a white sheet: the wheel still has a visible edge (shadow / halo).
- Windows "Show animations" off: lit art swaps in at once.

---

### Task 1: Colour and Effect settings
**Files:** `QuickMenu/QuickMenuLook.cs`, `QuickMenu/QuickMenuConfig.cs`
- [ ] `enum QuickMenuColour { Dark = 0, Light = 1 }`, `enum QuickMenuEffect { Flat = 0, Soft = 1, Clay = 2 }`,
      `QuickMenuLook.DefaultColour/DefaultEffect/Colours/Effects/ColourName/EffectName/ParseColour/ParseEffect`.
- [ ] `QuickMenuConfig.Colour`, `.Effect`; read keys `Colour`, `Effect`; write them after `Opening`.

### Task 2: The wheel draws Colour and Effect
**Files:** `UI/HeronStyles.xaml` (new Light / Soft / Clay keys + header), `QuickMenu/QuickMenuWindow.xaml.cs`,
new `QuickMenu/QuickMenuShading.cs` (bakes Raised / Pressed / Clay / Plate art to a positioned, frozen Image),
`QuickMenu/CmdQuickMenu.cs`.
- [ ] Constructor `QuickMenuWindow(slots, available, diameter, style, showNames, opening, colour, effect)`;
      the 6-argument one passes the defaults.
- [ ] Brushes per Colour (instance fields); edge padding per Effect so shadows fit in the window.
- [ ] Slots, bubbles, hub and the Soft plate per Effect; lit art faded in on hover; selector radii clear the plate.
- [ ] Verify with the scratch render harness: every design x colour x effect, idle and pointed, plus 4 / 12 slots,
      a greyed slot and an empty slot; time the baking.

### Task 3: Settings window (changed in chat: a "Look" tab inside Quick Menu Settings, not a new window)
**Files:** `QuickMenu/QuickMenuSettingsWindow.xaml(.cs)`, `QuickMenuWindow.TakeWheelForPreview / PlayPreviewOpening`
- [x] Two tabs, Slots and Look. Look holds Design, Colour, Effect, Names, Opens, Size, "Open the real wheel", and the
      real wheel live (point to light a slot; Opens replays the opening). Slots shows a one-line look summary.
- [x] One Save keeps both tabs. Render the window outside Revit to check both tabs.

### Task 4: Build, docs, report
- [ ] `powershell -ExecutionPolicy Bypass -File build\build.ps1 -NoInstall` - all years, 0 warnings.
- [ ] `docs/tools/quick.md`, `CHANGELOG.md` [Unreleased]; report PASS / FAIL / NOT RUN / NEEDS REAL REVIT.
