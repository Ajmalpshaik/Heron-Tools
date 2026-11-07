// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Heron Tools - the add-in's start-up class. Builds its panel on the shared "Heron" ribbon tab.

using System;
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

        /// <summary>This product's own panel. Heron AI uses other panel names on the same tab.</summary>
        internal const string PanelName = "View";

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
            return Result.Succeeded;
        }

        private static void BuildRibbon(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TabName); }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { /* tab exists */ }
            var panel = app.CreateRibbonPanel(TabName, PanelName);

            string assemblyPath = typeof(HeronToolsApp).Assembly.Location;

            var filterPro = new PushButtonData(
                "HeronTools_FilterPro",
                "Filter\nPro",
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

            panel.AddItem(filterPro);

            panel.AddItem(Button(assemblyPath, "HeronTools_Colorize", "Colorize", typeof(CmdColorize), typeof(CmdColorizeAvailability), "Colorize",
                "Colour elements by category or by parameter value in the active view (or views you pick) - no view filter is made.",
                "The same picks as Filter Pro (categories, a parameter, values), but the colour is put straight on the matching " +
                "elements instead of through a view filter. Shuffle colours gives every value its own colour; click it again for " +
                "a new set. Each run is one Undo step."));

            // View Crop: two related tools under one button.
            var viewCrop = new PulldownButtonData("HeronTools_ViewCrop", "View\nCrop")
            {
                ToolTip = "Fit crop regions and annotation crops of plan views.",
                Image = BrandImages.Load("ViewCrop16.png"),
                LargeImage = BrandImages.Load("ViewCrop32.png"),
            };
            var viewCropButton = (PulldownButton)panel.AddItem(viewCrop);
            viewCropButton.AddPushButton(Button(assemblyPath, "HeronTools_CropByElements", "Crop View\nby Elements",
                typeof(CmdViewCropByAllModelElements), typeof(CmdPlanViewAvailability), "ViewCrop",
                "Fit the crop region of plan views around their elements.",
                "Pick the views, then choose whether the crop fits the visible elements or all model elements, and the offset. " +
                "Each run is one Undo step."));
            viewCropButton.AddPushButton(Button(assemblyPath, "HeronTools_AnnotationCrop", "Annotation Crop\nby View Crop",
                typeof(CmdSetAnnotationCropByViewCrop), typeof(CmdPlanViewAvailability), "AnnotationCrop",
                "Turn on the annotation crop in the picked views and set an equal offset from each view's crop region.",
                "Pick the views and the offset. Revit's own limits on the annotation crop are respected. Each run is one Undo step."));

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
        }

        /// <summary>One ribbon button: id, caption, command, when it is enabled, its two icons and its help text.</summary>
        private static PushButtonData Button(string assemblyPath, string id, string text, Type command, Type availability,
                                             string icon, string toolTip, string longDescription)
        {
            return new PushButtonData(id, text, assemblyPath, command.FullName)
            {
                ToolTip = toolTip,
                LongDescription = longDescription,
                AvailabilityClassName = availability.FullName,
                Image = BrandImages.Load(icon + "16.png"),
                LargeImage = BrandImages.Load(icon + "32.png"),
            };
        }
    }
}
