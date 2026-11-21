// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Ribbon entry point - pick a source level, then pick target levels one-by-one to match their 3D extents; delegates to LevelExtentsService.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Utils;
using HeronTools.Services.LevelExtents;

namespace HeronTools.Commands
{
    /// <summary>
    /// Extends picked target levels in the active view to match a selected source level.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdExtendLevelsBySelected : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return LevelExtentsService.Execute(commandData, "Match Level Extents");
            }
            catch (System.Exception ex)
            {
                // Cancelled, not Failed: our own box is the message, and Revit undoes anything not
                // Succeeded - so "nothing was changed" is true.
                DialogHelper.ShowError("Match Level Extents",
                    "Match Level Extents stopped because of an unexpected error, so nothing was changed.\n\nRevit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }
}
