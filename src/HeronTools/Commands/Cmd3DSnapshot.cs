// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// 3D Snapshot: fit Revit's default 3D view's section box to what they are working on, in one click -
// selected rooms/spaces (or their tags), a selected section mark, the section they are in, or a window
// dragged in a plan. The modeller stays in their view; the 3D view updates (and opens behind if closed).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using HeronTools.Services.Snapshot3D;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd3DSnapshot : IExternalCommand
    {
        private const string ToolTitle = "3D Snapshot";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application?.ActiveUIDocument;
                if (!ValidationHelper.ValidateUIDocumentAndView(uidoc, out message))
                {
                    DialogHelper.ShowError(ToolTitle, message);
                    return Result.Cancelled;
                }

                Document doc = uidoc.Document;
                if (!ValidationHelper.ValidateEditableDocument(doc, out message))
                {
                    DialogHelper.ShowError(ToolTitle, message);
                    return Result.Cancelled;
                }

                View activeView = uidoc.ActiveView;
                SnapshotTarget target = FindTarget(uidoc, activeView, out string problem);
                if (target == null)
                {
                    if (!string.IsNullOrEmpty(problem))
                        DialogHelper.ShowError(ToolTitle, problem);
                    return Result.Cancelled;
                }

                SnapshotApplyResult applied = Snapshot3DService.Apply(doc, target);
                if (!applied.Succeeded)
                {
                    DialogHelper.ShowError(ToolTitle, applied.Problem);
                    return Result.Cancelled;
                }

                // The model change is committed. Showing the view is only display: if it fails, say so, but the
                // run itself succeeded.
                string showProblem = ShowBehind(uidoc, activeView, applied.View, applied.Box);

                if (showProblem != null || applied.CameraKept)
                {
                    string note = "The section box of \"" + applied.View.Name + "\" now fits " + target.SourceName + ".";
                    if (applied.CameraKept)
                        note += "\n\nThe view is locked, so its camera was not turned to face the section. " +
                                "Unlock it (View Cube > Unlock View) if you want 3D Snapshot to turn it next time.";
                    if (showProblem != null)
                        note += "\n\n" + showProblem;
                    DialogHelper.ShowWarning(ToolTitle, note);
                }

                // A run that did its job ends silently: the box is there to see.
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // Esc while dragging the window: nothing was changed.
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError(ToolTitle,
                    "3D Snapshot could not update the 3D view. Nothing was changed.\n\n" +
                    "Try again; if it keeps happening, send the detail below.\n\nWhat went wrong: " + ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Picks the mode from the selection: rooms/spaces first, then one section mark; with neither, the
        /// section the modeller is in, or a window dragged in the plan. Other selected elements are ignored.
        /// </summary>
        private static SnapshotTarget FindTarget(UIDocument uidoc, View activeView, out string problem)
        {
            problem = null;
            Document doc = uidoc.Document;
            ICollection<ElementId> selected = uidoc.Selection.GetElementIds();

            if (selected.Count > 0)
            {
                Snapshot3DService.SelectionRead read = Snapshot3DService.ReadSelection(doc, selected);

                if (read.Spatials.Count > 0)
                    return Snapshot3DService.FromSpatials(read.Spatials, activeView);

                if (read.SectionViews.Count > 1)
                {
                    problem = "You selected " + read.SectionViews.Count + " section marks. 3D Snapshot matches one section at a time.\n\n" +
                              "Select just one section mark and run it again.";
                    return null;
                }

                if (read.SectionViews.Count == 1)
                    return Snapshot3DService.FromSection(read.SectionViews[0], out problem);

                // The selection was meant for us but nothing in it can be used: say why, instead of quietly
                // asking for a window.
                if (read.TagsWithoutLocalRoom > 0 || read.UnusableSpatials > 0 || read.MarksNotSections > 0)
                {
                    problem = WhyNothingUsable(read);
                    return null;
                }
            }

            if (Snapshot3DService.IsSectionLike(activeView))
                return Snapshot3DService.FromSection(activeView, out problem);

            if (Snapshot3DService.IsPlan(activeView))
            {
                PickedBox picked = uidoc.Selection.PickBox(PickBoxStyle.Enclosing,
                    "3D Snapshot: drag a window round the area to show in 3D (Esc to cancel)");
                if (picked == null)
                    return null;
                return Snapshot3DService.FromPlanRectangle((ViewPlan)activeView, picked.Min, picked.Max, out problem);
            }

            problem = "3D Snapshot works in a plan, ceiling plan, section or elevation.\n\n" +
                      "Open one of those, then select a room, a room tag, a space or a section mark - or select nothing " +
                      "and drag a window in the plan.";
            return null;
        }

        private static string WhyNothingUsable(Snapshot3DService.SelectionRead read)
        {
            var reasons = new List<string>();
            if (read.TagsWithoutLocalRoom > 0)
                reasons.Add("- " + Plural(read.TagsWithoutLocalRoom, "tag belongs", "tags belong") +
                            " to a room or space in a linked model (not supported yet) or to one that is not placed.");
            if (read.UnusableSpatials > 0)
                reasons.Add("- " + Plural(read.UnusableSpatials, "room or space is", "rooms or spaces are") +
                            " not placed or not enclosed (Area shows \"Not Placed\" or \"Not Enclosed\").");
            if (read.MarksNotSections > 0)
                reasons.Add("- " + Plural(read.MarksNotSections, "mark is", "marks are") +
                            " not a section or a single elevation (a callout, or an elevation marker with several arrows - " +
                            "select the arrow of the elevation you want).");

            return "Nothing you selected can be shown:\n\n" + string.Join("\n", reasons) +
                   "\n\nSelect a placed room, space, their tag or one section mark - or press Esc to clear the selection " +
                   "and drag a window in the plan.";
        }

        private static string Plural(int count, string one, string many) =>
            count + " " + (count == 1 ? one : many);

        /// <summary>
        /// Keeps the modeller in their own view. A 3D view already open just shows the new box; a closed one is
        /// opened behind the current view. Either way it is zoomed to the box. Returns a problem or null.
        /// </summary>
        private static string ShowBehind(UIDocument uidoc, View stayIn, View3D view3D, BoundingBoxXYZ box)
        {
            try
            {
                bool isOpen = uidoc.GetOpenUIViews().Any(u => u.ViewId == view3D.Id);
                if (!isOpen)
                {
                    uidoc.ActiveView = view3D;   // opens it ...
                    uidoc.ActiveView = stayIn;   // ... and puts the modeller back where they were
                }

                UIView ui3D = uidoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == view3D.Id);
                if (ui3D != null)
                {
                    XYZ[] corners = SnapshotTarget.Corners(box);
                    var min = new XYZ(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z));
                    var max = new XYZ(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z));
                    ui3D.ZoomAndCenterRectangle(min, max);
                }

                return null;
            }
            catch (Exception ex)
            {
                return "It could not be opened or zoomed for you - open \"" + view3D.Name + "\" from the Project Browser.\n\n" +
                       "What Revit said: " + ex.Message;
            }
        }
    }
}
