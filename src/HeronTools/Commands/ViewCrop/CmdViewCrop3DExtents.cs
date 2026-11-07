// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// External commands for View Crop (active-view / all-model) and integrated annotation crop.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
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
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Enables annotation crop and sets annotation offsets from the view crop boundary.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdSetAnnotationCropByViewCrop : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return ViewCropAnnotationCommandService.Execute(
                    commandData,
                    "Set Annotation Crop by View Crop",
                    ref message);
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
