// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// External command for View Crop (one button). The annotation crop is an option in its window.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Utils;
using HeronTools.Services.ViewCrop;

namespace HeronTools.Commands
{
    /// <summary>
    /// Unified crop command. User picks "Visible elements" or "All model elements" inside the settings dialog.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdViewCropByAllModelElements : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return ViewCropCommandService.Execute(
                    commandData,
                    "Crop View by Elements",
                    ref message);
            }
            catch (System.Exception ex)
            {
                // Cancelled, not Failed: our own box is the message, and Revit undoes anything not
                // Succeeded - so "nothing was changed" is true.
                DialogHelper.ShowError("Crop View by Elements",
                    "Crop View by Elements stopped because of an unexpected error, so nothing was changed.\n\nRevit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }
}
