// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Opens the Set Link Workset window where the user assigns selected Revit links and CAD imports to an existing or new workset.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Interop;
using HeronTools.UI;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    /// <summary>
    /// Opens the Set Link Workset dialog.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdSetLinkWorkset : IExternalCommand
    {
        private const string Title = "Set Link Workset";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application?.ActiveUIDocument;
                if (!ValidationHelper.ValidateUIDocument(uidoc, out message))
                {
                    DialogHelper.ShowError(Title, message);
                    return Result.Cancelled;
                }

                var window = new SetLinkWorksetWindow(uidoc.Document, uidoc.Selection.GetElementIds());
                var helper = new WindowInteropHelper(window)
                {
                    Owner = commandData.Application.MainWindowHandle
                };

                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
