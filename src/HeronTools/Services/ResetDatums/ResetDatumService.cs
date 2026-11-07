// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Switches every visible grid/level datum end in the active view back to 3D (Model) extent type, so the datum follows its shared 3D extent again.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Models;
using HeronTools.Utils;

namespace HeronTools.Services.ResetDatums
{
    /// <summary>
    /// Resets grid and level datum extents in the active view back to model (3D) extents.
    /// </summary>
    internal static class ResetDatumService
    {
        /// <summary>
        /// Runs the reset workflow for grids/levels according to the selected mode.
        /// </summary>
        internal static Result Execute(
            ExternalCommandData commandData,
            ResetDatumMode mode,
            string title)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null)
                {
                    DialogHelper.ShowError(title, "Open a project view before running this command.");
                    return Result.Cancelled;
                }

                Document doc = uidoc.Document;
                if (doc.IsFamilyDocument)
                {
                    DialogHelper.ShowError(title, "This tool runs in a project, not the Family Editor.");
                    return Result.Cancelled;
                }

                View view = doc.ActiveView;
                if (view == null || view.IsTemplate)
                {
                    DialogHelper.ShowError(title, "Please run this tool inside a normal project view.");
                    return Result.Cancelled;
                }

                var tally = new ResetTally();
                string undoName = mode == ResetDatumMode.GridsOnly ? "Reset Grids to 3D Extents"
                                : mode == ResetDatumMode.LevelsOnly ? "Reset Levels to 3D Extents"
                                : "Reset Grids and Levels to 3D Extents";

                using (Transaction t = new Transaction(doc, undoName))
                {
                    t.Start();

                    if (mode != ResetDatumMode.LevelsOnly)
                        ResetDatums(doc, view, typeof(Grid), tally);

                    if (mode != ResetDatumMode.GridsOnly)
                        ResetDatums(doc, view, typeof(Level), tally);

                    if (tally.Changed == 0)
                    {
                        t.RollBack();
                    }
                    else if (t.Commit() != TransactionStatus.Committed)
                    {
                        DialogHelper.ShowError(title, "Revit did not accept the change, so nothing was reset. " +
                            "In a workshared model, check that no colleague owns these datums, then try again.");
                        return Result.Cancelled;
                    }
                }

                if (tally.Found == 0)
                {
                    DialogHelper.ShowInfo(
                        title,
                        "No grids or levels were found to reset in this view. " +
                        "Open a view that shows grids or levels and try again.");
                    return Result.Cancelled;
                }

                if (tally.Changed == 0)
                {
                    DialogHelper.ShowInfo(title, tally.Refused == 0
                        ? $"All {tally.Found} datum(s) in this view already use their 3D extents - nothing to reset."
                        : $"Nothing was reset: {tally.Refused} datum end(s) could not be changed in this view " +
                          "(owned by a colleague, or controlled by a view template).");
                    return Result.Cancelled;
                }

                if (tally.Refused > 0)
                {
                    DialogHelper.ShowInfo(title, $"{tally.Changed} datum(s) reset to 3D extents. " +
                        $"{tally.Refused} datum end(s) could not be changed in this view " +
                        "(owned by a colleague, or controlled by a view template).");
                }

                // Otherwise success is silent - the datums update visibly in the view.
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError(title, "The datums could not be reset, so nothing was changed. Revit said: " + ex.Message);
                return Result.Cancelled;
            }
        }

        private sealed class ResetTally
        {
            public int Found;
            public int Changed;
            public int Refused;
        }

        /// <summary>
        /// Resets every visible datum of the given type (Grid or Level) in the view to 3D (Model) extents.
        /// Ends already on 3D extents are left alone and not counted as reset.
        /// </summary>
        private static void ResetDatums(Document doc, View view, Type datumClass, ResetTally tally)
        {
            foreach (Element element in new FilteredElementCollector(doc, view.Id).OfClass(datumClass))
            {
                DatumPlane datum = element as DatumPlane;
                if (datum == null || !datum.IsValidObject)
                    continue;

                tally.Found++;
                bool changed = false;

                foreach (DatumEnds end in new[] { DatumEnds.End0, DatumEnds.End1 })
                {
                    try
                    {
                        if (datum.GetDatumExtentTypeInView(end, view) == DatumExtentType.Model)
                            continue;
                        datum.SetDatumExtentType(end, view, DatumExtentType.Model);
                        changed = true;
                    }
                    catch (Exception)
                    {
                        // This end cannot change in this view (owned by a colleague, template, ...): counted.
                        tally.Refused++;
                    }
                }

                if (changed)
                    tally.Changed++;
            }
        }
    }
}
