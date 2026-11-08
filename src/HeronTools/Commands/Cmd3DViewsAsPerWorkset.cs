// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Entry command that creates one isometric 3D view per user workset and isolates each workset in its matching view. Delegates the work to Workset3DViewService.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Services.WorksetViews;

namespace HeronTools.Commands
{
    /// <summary>
    /// Creates 3D views named after user worksets and isolates each workset in its own view.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class Cmd3DViewsAsPerWorkset : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return Workset3DViewService.Execute(commandData, ref message);
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
