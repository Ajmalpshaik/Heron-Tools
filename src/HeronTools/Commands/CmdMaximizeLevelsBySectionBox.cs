// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Ribbon entry point that maximizes all level 3D extents to the active 3D view's section box; delegates to LevelExtentsBySectionBoxService.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Utils;
using HeronTools.Services.LevelExtents;

namespace HeronTools.Commands
{
    /// <summary>
    /// Maximizes all level 3D extents to fit the active 3D view's section box.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdMaximizeLevelsBySectionBox : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return LevelExtentsBySectionBoxService.Execute(commandData, "Maximize Levels by Section Box");
            }
            catch (System.Exception ex)
            {
                // Cancelled, not Failed: our own box is the message, and Revit undoes anything not
                // Succeeded - so "nothing was changed" is true.
                DialogHelper.ShowError("Maximize Levels by Section Box",
                    "Maximize Levels by Section Box stopped because of an unexpected error, so nothing was changed.\n\nRevit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }
}
