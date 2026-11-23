// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Heron Tools - the add-in's start-up class. Builds its panel on the shared "Heron" ribbon tab.

using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using HeronTools.Commands;
using HeronTools.Utils;

namespace HeronTools.App
{
    /// <summary>
    /// Revit calls this once when it starts. It adds the Heron Tools panel to the "Heron" tab.
    /// </summary>
    /// <remarks>
    /// WHY THE TAB IS JOINED, NOT ASSUMED
    /// The "Heron" tab is shared with Heron AI. Revit chooses which add-in loads first and never says,
    /// so either one may be the first to arrive. CreateRibbonTab throws when the tab already exists:
    ///   - loaded first (or alone): the tab is created here;
    ///   - loaded second: the "already exists" exception is caught and this panel joins that tab.
    /// Only that one exception is caught, by its full name. A wider catch would hide a real failure
    /// and leave a silently missing ribbon.
    /// </remarks>
    public sealed class HeronToolsApp : IExternalApplication
    {
        /// <summary>Exact spelling and case. Never change it: a one-letter difference makes a second tab.</summary>
        internal const string TabName = "Heron";

        /// <summary>This product's own panels. Heron AI uses other panel names ("AI Bridge") on the same tab.</summary>
        internal const string PanelName = "View";
        internal const string QuickPanelName = "Quick";
        internal const string DatumsPanelName = "Datums";
        internal const string ModifyPanelName = "Modify";
        internal const string CoordinationPanelName = "Coordination";
        internal const string AboutPanelName = "About";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                BuildRibbon(application);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                // Never let an exception escape OnStartup. Tell the modeller in plain words instead.
                TaskDialog.Show(
                    "Heron Tools",
                    "Heron Tools could not add its buttons to the Heron tab, so its tools are not available " +
                    "in this Revit session.\n\nRestart Revit. If it happens again, reinstall Heron Tools.\n\n" +
                    "Detail for support: " + ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // Model Walk's third-person character: off the graphics service's active list, removed if Revit allows.
            Services.ModelWalk.WalkCharacterServer.SwitchOff();
            return Result.Succeeded;
        }

        private static void BuildRibbon(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TabName); }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { /* tab exists */ }

            string assemblyPath = typeof(HeronToolsApp).Assembly.Location;

            // ---- Quick: the tool wheel and its settings, under one split button.
            var quickPanel = app.CreateRibbonPanel(TabName, QuickPanelName);
            var quick = (SplitButton)quickPanel.AddItem(new SplitButtonData("HeronTools_QuickMenuGroup", "Quick\nMenu"));
            quick.IsSynchronizedWithCurrentItem = false;   // the wheel stays the face of the button
            quick.AddPushButton(Button(assemblyPath, "HeronTools_QuickMenu", "Quick\nMenu",
                typeof(Commands.QuickMenu.CmdQuickMenu), null, "QuickMenu",
                "Open the quick tool wheel around the mouse pointer. Point at a tool and click to run it, or press its number.",
                "Your favourite Heron Tools buttons and Revit commands in a ring. Esc closes it. Tip: give this button a " +
                "keyboard shortcut (File > Options > User Interface > Keyboard Shortcuts, search Quick Menu) so the " +
                "wheel opens right where you are working."));
            quick.AddPushButton(Button(assemblyPath, "HeronTools_QuickMenuSettings", "Quick Menu\nSettings",
                typeof(Commands.QuickMenu.CmdQuickMenuSettings), typeof(CmdAlwaysAvailable), "QuickMenuSettings",
                "Choose which tool sits in each slot of the wheel, how many slots there are and how big the wheel opens.",
                "Saved for you on this PC."));

            var panel = app.CreateRibbonPanel(TabName, PanelName);

            // Filter Pro, Colorize and View Crop: one stack of three small buttons (like Unhide All / Toggle Links /
            // Highlight), so the View panel stays narrow. Reset Graphics and 3D Section Box stay big buttons.
            var filterPro = new PushButtonData(
                "HeronTools_FilterPro",
                "Filter Pro",
                assemblyPath,
                typeof(CmdFilterPro).FullName)
            {
                ToolTip = "Create view filters quickly from a category, a parameter and its values, then apply them to the active view.",
                LongDescription =
                    "Pick one or more categories, a parameter and the values you want. Filter Pro makes one view " +
                    "filter per value, names it for you and adds it to the active view (or to views you pick), first " +
                    "in the filter list. Shuffle Colours then gives every filter its own colour. Each run is one Undo step.\n\n" +
                    "Works in plan, ceiling, structural, area, section, detail, elevation and 3D views.",
                AvailabilityClassName = typeof(CmdFilterProAvailability).FullName,
                Image = BrandImages.Load("FilterPro16.png"),
                LargeImage = BrandImages.Load("FilterPro32.png"),
            };

            var colorize = Button(assemblyPath, "HeronTools_Colorize", "Colorize", typeof(CmdColorize), typeof(CmdColorizeAvailability), "Colorize",
                "Colour elements by category or by parameter value in the active view (or views you pick) - no view filter is made.",
                "The same picks as Filter Pro (categories, a parameter, values), but the colour is put straight on the matching " +
                "elements instead of through a view filter. Shuffle colours gives every value its own colour; click it again for " +
                "a new set. The colour replaces the elements' own overrides until you click Restore original, which puts " +
                "them back. Each run is one Undo step.");

            // View Crop: two related tools under one button.
            var viewCrop = new PulldownButtonData("HeronTools_ViewCrop", "View Crop")
            {
                ToolTip = "Fit crop regions and annotation crops of plan views.",
                Image = BrandImages.Load("ViewCrop16.png"),
                LargeImage = BrandImages.Load("ViewCrop32.png"),
            };

            IList<RibbonItem> viewStack = panel.AddStackedItems(filterPro, colorize, viewCrop);
            var viewCropButton = (PulldownButton)viewStack[2];
            viewCropButton.AddPushButton(Button(assemblyPath, "HeronTools_CropByElements", "Crop View\nby Elements",
                typeof(CmdViewCropByAllModelElements), typeof(CmdPlanViewAvailability), "ViewCrop",
                "Fit the crop region of plan views around their elements.",
                "Pick the views, then choose whether the crop fits the visible elements or all model elements, and the offset. " +
                "Each run is one Undo step."));
            viewCropButton.AddPushButton(Button(assemblyPath, "HeronTools_AnnotationCrop", "Annotation Crop\nby View Crop",
                typeof(CmdSetAnnotationCropByViewCrop), typeof(CmdPlanViewAvailability), "AnnotationCrop",
                "Turn on the annotation crop in the picked views and set an equal offset from each view's crop region.",
                "Pick the views and the offset. Revit's own limits on the annotation crop are respected. Each run is one Undo step."));

            // Reset: clear graphic overrides - the selection in one click, or a view / many views in the window -
            // and bring the last reset back. Each keeps a copy first, so nothing set by hand is lost.
            var reset = new PulldownButtonData("HeronTools_ResetGraphicsGroup", "Reset\nGraphics")
            {
                ToolTip = "Clear graphic overrides - of the selected elements, or of whole views and categories - and bring them back if needed.",
                Image = BrandImages.Load("ResetGraphics16.png"),
                LargeImage = BrandImages.Load("ResetGraphics32.png"),
            };
            var resetButton = (PulldownButton)panel.AddItem(reset);
            resetButton.AddPushButton(Button(assemblyPath, "HeronTools_ResetSelected", "Reset Selected",
                typeof(Commands.GraphicsTools.CmdResetSelected), typeof(CmdGraphicalViewAvailability), "ResetGraphics",
                "Clear the graphic overrides of the selected elements in the active view - one click, no window.",
                "Select elements first (or pick them when asked). Their insulation and lining are reset with them. One Undo " +
                "step, and Restore Last Reset can bring them back."));
            resetButton.AddPushButton(Button(assemblyPath, "HeronTools_ResetGraphics", "Reset Graphics...",
                typeof(Commands.GraphicsTools.CmdResetGraphics), typeof(CmdGraphicalViewAvailability), "ResetGraphics",
                "Clear element overrides, category (V/G) overrides or both - in this view or in views you pick.",
                "Shows how many it found before you press Reset. Can leave Colorize and Highlight colours alone. Views whose " +
                "template controls V/G keep their category overrides. One Undo step; Restore Last Reset brings it back."));
            resetButton.AddPushButton(Button(assemblyPath, "HeronTools_RestoreLastReset", "Restore Last Reset",
                typeof(Commands.GraphicsTools.CmdRestoreLastReset), typeof(CmdGraphicalViewAvailability), "ResetGraphics",
                "Put back every override the last reset in this model cleared.",
                "Works while Revit stays open, even after other work (where Undo would also undo that work). One Undo step."));

            // 3D Section Box: one click fits Revit's default 3D view to a room, a section or a plan window.
            // (The id stays HeronTools_3DSnapshot so keyboard shortcuts and Quick Menu slots keep working after the rename.)
            panel.AddItem(Button(assemblyPath, "HeronTools_3DSnapshot", "3D Section\nBox", typeof(Cmd3DSnapshot), typeof(Cmd3DSnapshotAvailability), "Snapshot3D",
                "Fit the section box of Revit's 3D view ({3D}) to what you are working on - a room, a section, or a window you drag in the plan.",
                "Room, space or zone selected (or its tag - rooms in links too): the box fits it, level to upper limit. Section mark selected, or run " +
                "in a section or elevation: the box matches the section - same cut, same depth - and the camera looks the same way. " +
                "Nothing selected in a plan: drag a window; the box runs from the bottom to the top of the view range.\n\n" +
                "Updates the same 3D view Revit's 3D button opens ({3D - your username} in a workshared model, {3D} otherwise), with " +
                "300 mm extra on every side. You stay " +
                "in your view; the 3D view opens behind it if it is closed. One Undo step."));

            // Small everyday view tools, stacked.
            panel.AddStackedItems(
                Button(assemblyPath, "HeronTools_UnhideAll", "Unhide All", typeof(CmdUnhideAll), typeof(CmdGraphicalViewAvailability), "UnhideAll",
                    "Show everything hidden in the active view: temporary hide/isolate and elements hidden in the view.",
                    "One click, one Undo step. Categories hidden in Visibility/Graphics are left as they are."),
                Button(assemblyPath, "HeronTools_ToggleLinks", "Toggle Links", typeof(CmdToggleRevitLinks), typeof(CmdRevitLinkToggleAvailability), "ToggleLinks",
                    "Turn the Revit Links category off or on in the active view.",
                    "The same as ticking RVT Links in Visibility/Graphics: one click hides all links, the next shows them " +
                    "again. Links hidden one by one stay as they are. One Undo step."),
                Button(assemblyPath, "HeronTools_HighlightSelection", "Highlight", typeof(Commands.GraphicsTools.CmdHighlightSelection), typeof(CmdGraphicalViewAvailability), "HighlightSelection",
                    "Show the selected elements in red and everything else in grey in the active view. Click again to put the view back.",
                    "Select elements first (or pick them when asked). Every element's own graphic overrides are remembered and put " +
                    "back exactly when you click Highlight again in the same view (or press Undo)."));

            BuildDatumsModifyCoordinationAbout(app, assemblyPath);
        }

        private static void BuildDatumsModifyCoordinationAbout(UIControlledApplication app, string assemblyPath)
        {
            // ---- Datums
            var datums = app.CreateRibbonPanel(TabName, DatumsPanelName);
            var reset = new PulldownButtonData("HeronTools_ResetDatumsGroup", "Reset Datums to 3D")
            {
                ToolTip = "Put grid and level ends in this view back to their 3D extents.",
                Image = BrandImages.Load("ResetDatums16.png"),
                LargeImage = BrandImages.Load("ResetDatums32.png"),
            };
            var levelExtents = new PulldownButtonData("HeronTools_LevelExtentsGroup", "Level Extents")
            {
                ToolTip = "Match level extents to another level, or stretch them to the section box.",
                Image = BrandImages.Load("LevelExtents16.png"),
                LargeImage = BrandImages.Load("LevelExtents32.png"),
            };
            IList<RibbonItem> datumItems = datums.AddStackedItems(reset, levelExtents,
                Button(assemblyPath, "HeronTools_FlipBubbles", "Flip Bubbles", typeof(CmdFlipGridBubble), null, "FlipBubbles",
                    "Swap which end of a grid or level shows its bubble.",
                    "Pick datums one at a time (each flips at once), or window-select batches. The whole run is one Undo step."));
            var resetButton = (PulldownButton)datumItems[0];
            resetButton.AddPushButton(Button(assemblyPath, "HeronTools_ResetGrids", "Grids", typeof(CmdResetDatumsGrids), null, "ResetDatums",
                "Reset every visible grid in this view to its 3D extents.", "One Undo step."));
            resetButton.AddPushButton(Button(assemblyPath, "HeronTools_ResetLevels", "Levels", typeof(CmdResetDatumsLevels), null, "ResetDatums",
                "Reset every visible level in this view to its 3D extents.", "One Undo step."));
            resetButton.AddPushButton(Button(assemblyPath, "HeronTools_ResetGridsLevels", "Grids and Levels", typeof(CmdResetDatums), null, "ResetDatums",
                "Reset every visible grid and level in this view to its 3D extents.", "One Undo step."));
            var levelButton = (PulldownButton)datumItems[1];
            levelButton.AddPushButton(Button(assemblyPath, "HeronTools_MatchLevelExtents", "Match Level Extents", typeof(CmdExtendLevelsBySelected), null, "LevelExtents",
                "Pick one level, then the levels to match its 3D extents (Esc to finish).",
                "Only the 3D extents change; level ends made 2D in a view stay 2D. The whole run is one Undo step."));
            levelButton.AddPushButton(Button(assemblyPath, "HeronTools_MaximizeLevelExtents", "Levels to Section Box", typeof(CmdMaximizeLevelsBySectionBox), null, "LevelExtents",
                "Stretch every level's 3D extents to the active 3D view's section box.",
                "Open a 3D view with a section box first. Level ends made 2D in a view stay 2D. One Undo step."));

            // Auto Dimension: chain + overall rows across grids and levels, just inside their heads.
            var autoDimension = new PulldownButtonData("HeronTools_AutoDimensionGroup", "Auto\nDimension")
            {
                ToolTip = "Dimension the grids and levels of this view in rows, just inside their heads.",
                Image = BrandImages.Load("AutoDimension16.png"),
                LargeImage = BrandImages.Load("AutoDimension32.png"),
            };
            var autoDimensionButton = (PulldownButton)datums.AddItem(autoDimension);
            autoDimensionButton.AddPushButton(Button(assemblyPath, "HeronTools_AutoDimensionGrids", "Grids",
                typeof(CmdAutoDimensionGrids), typeof(CmdAutoDimensionAvailability), "AutoDimension",
                "Dimension the grids of this plan, section or elevation: a chain and an overall for each set of parallel grids.",
                "Rows sit just inside the grid heads (and inside the crop). Grids at an angle get their own rows. Select grids " +
                "first to dimension only those. A row already in the view is not made again. One Undo step."));
            autoDimensionButton.AddPushButton(Button(assemblyPath, "HeronTools_AutoDimensionLevels", "Levels",
                typeof(CmdAutoDimensionLevels), typeof(CmdAutoDimensionAvailability), "AutoDimension",
                "Dimension the levels of this section or elevation: a chain and an overall.",
                "Rows sit just inside the level heads (and inside the crop). Select levels first to dimension only those. " +
                "One Undo step."));
            autoDimensionButton.AddPushButton(Button(assemblyPath, "HeronTools_AutoDimensionGridsLevels", "Grids and Levels",
                typeof(CmdAutoDimensionGridsLevels), typeof(CmdAutoDimensionAvailability), "AutoDimension",
                "Plans: dimension the grids. Sections and elevations: the grids and the levels.",
                "Uses Auto Dimension Settings. One Undo step for the whole run."));
            autoDimensionButton.AddPushButton(Button(assemblyPath, "HeronTools_AutoDimensionSettings", "Settings",
                typeof(CmdAutoDimensionSettings), null, "AutoDimension",
                "Choose the rows, where they sit, the dimension types and which grids and levels are used.",
                "Saved for you in every project. Changes nothing in the model."));

            // ---- Modify
            var modify = app.CreateRibbonPanel(TabName, ModifyPanelName);
            var matchElevation = new PulldownButtonData("HeronTools_MatchElevationGroup", "Match Elevation")
            {
                ToolTip = "Match the centre, top or bottom elevation of a source MEP element on other elements.",
                Image = BrandImages.Load("MatchElevation16.png"),
                LargeImage = BrandImages.Load("MatchElevation32.png"),
            };
            IList<RibbonItem> modifyItems = modify.AddStackedItems(matchElevation,
                Button(assemblyPath, "HeronTools_ReassignLevel", "Reassign Level", typeof(CmdReassignLevel), null, "ReassignLevel",
                    "Move ducts, pipes, trays, conduits and free-standing families to another level without moving them.",
                    "Whole project or just your selection. Each element is measured before and after; any that would " +
                    "move is left as it was. Hosted families and spaces are skipped. One Undo step."),
                Button(assemblyPath, "HeronTools_PinElements", "Pin / Unpin", typeof(CmdPinElements), null, "PinElements",
                    "Pin or unpin groups of elements: title blocks, views on sheets, ducts, pipes, grids, levels...",
                    "Pick the groups and whether it is this sheet or all sheets. Each Pin or Unpin press is one Undo step."));
            var matchButton = (PulldownButton)modifyItems[0];
            matchButton.AddPushButton(Button(assemblyPath, "HeronTools_MatchCenter", "Match Centre", typeof(CmdMatchElevation), null, "MatchElevation",
                "Match the centre elevation of a source MEP element on the elements you pick.", "One Undo step."));
            matchButton.AddPushButton(Button(assemblyPath, "HeronTools_MatchTop", "Match Top", typeof(CmdMatchElevationTop), null, "MatchElevation",
                "Match the top elevation of a source MEP element on the elements you pick.", "One Undo step."));
            matchButton.AddPushButton(Button(assemblyPath, "HeronTools_MatchBottom", "Match Bottom", typeof(CmdMatchElevationBottom), null, "MatchElevation",
                "Match the bottom elevation of a source MEP element on the elements you pick.", "One Undo step."));
            modify.AddItem(Button(assemblyPath, "HeronTools_SmartSelection", "Smart\nSelection", typeof(CmdSmartSelection), null, "SmartSelection",
                "Pick one element, then window-select: only elements of the same category are kept.",
                "Everything else caught in the box is skipped. The result stays selected for your next step."));

            // ---- Coordination
            var coordination = app.CreateRibbonPanel(TabName, CoordinationPanelName);

            // Quick Clash: a Navisworks-style clash test in the active 3D view, in a window that stays open.
            coordination.AddItem(Button(assemblyPath, "HeronTools_QuickClash", "Quick\nClash", typeof(CmdQuickClash), typeof(CmdQuickClashAvailability), "QuickClash",
                "Find clashes in the active 3D view, like a Navisworks clash test, and show them in colour. Reset puts the view back.",
                "Pick Selection A (categories of this model, or your current selection) and Selection B (categories of this model " +
                "or of a linked model), then Hard (with a tolerance) and/or Clearance (a distance), in mm. Only what the view shows " +
                "is tested, so a section box makes it quicker. Clashes turn red and green, the rest grey and see-through; click a " +
                "clash in the list to zoom to it. Every element's own colours are remembered: Reset view puts them back exactly " +
                "(also done before Save and Sync with Central). Run and Reset are each one Undo step. Works in 3D views only."));

            // Model Walk: walk the live model in first person from a 3D view; a second click ends the walk.
            coordination.AddItem(Button(assemblyPath, "HeronTools_ModelWalk", "Model\nWalk", typeof(Commands.ModelWalk.CmdModelWalk),
                typeof(Commands.ModelWalk.CmdModelWalkAvailability), "ModelWalk",
                "Open a 3D view, click Model Walk, then walk with W A S D and look with the mouse. Esc pauses; click again to stop.",
                "Walk the model in first person to look at things, select them, measure and record snags for a coordination " +
                "meeting. Uses the view \"Heron Model Walk\" (made once with Revit's first 3D view type and no template; one per " +
                "person in a workshared model) and starts where your 3D view looks. Only what the view shows blocks you - hide a " +
                "category or use a section box to walk through it. Right-click or Tab changes the mode: Inspect, Select, Snag (a " +
                "picture and a red colour, click after click; write the comments in the pause menu; the report goes in a folder next " +
                "to the model), Hide. Snag colours stay in the walk view after the walk (standard; a setting) until J, or the next " +
                "Save or Sync - no red is saved in the model. Q (standard key) switches to third person: a site worker drawn on screen where you stand, nothing added to the model. Pause (Esc) for snags, " +
                "viewpoints, settings and keys. Camera " +
                "moves make no Undo entries; each snag is one Undo step."));

            var elementId = new PulldownButtonData("HeronTools_ElementIdGroup", "Element ID")
            {
                ToolTip = "Element ID tools for this model and linked models.",
                Image = BrandImages.Load("ElementId16.png"),
                LargeImage = BrandImages.Load("ElementId32.png"),
            };
            IList<RibbonItem> coordItems = coordination.AddStackedItems(elementId,
                Button(assemblyPath, "HeronTools_Views3DByWorkset", "3D Views by Workset", typeof(Cmd3DViewsAsPerWorkset), null, "Views3D",
                    "Make one 3D view per user workset, each showing only its workset.",
                    "Workshared models only. One Undo step."),
                Button(assemblyPath, "HeronTools_LinkWorkset", "Link Workset", typeof(CmdSetLinkWorkset), null, "LinkWorkset",
                    "Put Revit links and CAD imports on a workset (the ones you selected start ticked).",
                    "Workshared models only. One Undo step."));
            var idButton = (PulldownButton)coordItems[0];
            idButton.AddPushButton(Button(assemblyPath, "HeronTools_ElementIdOfPick", "ID of a Picked Element", typeof(CmdLinkedElementIdViewer), null, "ElementId",
                "Pick a model element - in this model or a link - and see its Element ID and where it comes from.",
                "Reads only; changes nothing. For annotation in this model, use Revit's own IDs of Selection."));
            idButton.AddPushButton(Button(assemblyPath, "HeronTools_FindById", "Find by Element ID", typeof(CmdLinkedElementSearch), null, "FindById",
                "Find an element by its ID in this model or a linked model, and zoom to it.", "Reads only; changes nothing."));

            // ---- About
            var about = app.CreateRibbonPanel(TabName, AboutPanelName);
            about.AddItem(Button(assemblyPath, "HeronTools_AboutHeron", "About\nHeron", typeof(CmdAboutHeron), typeof(CmdAlwaysAvailable), "AboutHeron",
                "Version details and the Heron family (Heron Tools and Heron AI).",
                "Shows which Heron Tools version runs in this Revit, with links to both Heron products."));
        }

        /// <summary>One ribbon button: id, caption, command, when it is enabled, its two icons and its help text.</summary>
        private static PushButtonData Button(string assemblyPath, string id, string text, Type command, Type availability,
                                             string icon, string toolTip, string longDescription)
        {
            return new PushButtonData(id, text, assemblyPath, command.FullName)
            {
                ToolTip = toolTip,
                LongDescription = longDescription,
                // No availability class = Revit's default: enabled whenever a model is open.
                AvailabilityClassName = availability?.FullName,
                Image = BrandImages.Load(icon + "16.png"),
                LargeImage = BrandImages.Load(icon + "32.png"),
            };
        }
    }
}
