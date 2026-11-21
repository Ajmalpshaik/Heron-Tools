// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Opens the Pin Elements window where the user picks Sheet groups (title blocks, placed views, legends, schedules) or Model groups (duct, pipe, cable tray, generic models, mechanical equipment, plumbing fixtures, electrical equipment, grids, levels) and pins or unpins them.

using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.UI;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    /// <summary>
    /// Opens a fixed-group selector and applies pin or unpin.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdPinElements : IExternalCommand
    {
        private const string Title = "Pin Elements";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            PinElementsWindow window = null;
            try
            {
                UIDocument uidoc = commandData?.Application?.ActiveUIDocument;
                if (!ValidationHelper.ValidateUIDocument(uidoc, out message))
                {
                    DialogHelper.ShowError(Title, message);
                    return Result.Cancelled;
                }

                Document doc = uidoc.Document;
                if (!ValidationHelper.ValidateEditableDocument(doc, out message))
                {
                    DialogHelper.ShowError(Title, message);
                    return Result.Cancelled;
                }

                window = new PinElementsWindow(doc);
                new WindowInteropHelper(window)
                {
                    Owner = commandData.Application.MainWindowHandle
                };

                bool? dialogResult = window.ShowDialog();
                if (dialogResult != true && !window.HasExecutedOperation)
                    return Result.Cancelled;
                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                // Any result but Succeeded makes Revit undo what this command committed: keep a Pin /
                // Unpin the modeller already ran in the window, and say so in plain words.
                if (window != null && window.HasExecutedOperation)
                {
                    DialogHelper.ShowError(Title, "The Pin Elements window stopped. The pins already changed were kept. Revit said: " + ex.Message);
                    return Result.Succeeded;
                }

                DialogHelper.ShowError(Title, "Pin Elements could not run. Nothing was changed. Revit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }
}
