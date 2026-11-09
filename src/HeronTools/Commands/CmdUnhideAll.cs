// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Unhides permanently hidden elements and clears Temporary Hide/Isolate in the active view.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Services.UnhideAll;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdUnhideAll : IExternalCommand
    {
        private const string ToolTitle = "Unhide All";

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
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

                // The model work lives in UnhideAllService, UI-free; this command only presents it.
                UnhideAllResult result = UnhideAllService.Run(doc, doc.ActiveView);

                // A run that did its job ends silently - the elements are back on screen. It only speaks when
                // nothing was hidden, something was refused, or Revit did not keep the change.
                bool isolateStuck = result.TemporaryHideIsolateWasActive && !result.TemporaryHideIsolateCleared;
                if (!result.DidSomething || isolateStuck || result.RefusedCount > 0)
                {
                    if (isolateStuck || result.RefusedCount > 0)
                        DialogHelper.ShowWarning(ToolTitle, result.Summary);
                    else
                        DialogHelper.ShowInfo(ToolTitle, result.Summary);
                }

                return result.DidSomething ? Result.Succeeded : Result.Cancelled;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError(
                    ToolTitle,
                    "Could not unhide elements in the active view.\n\nPlease check that the view is editable and try again."
                    + "\n\nWhat went wrong: " + ex.Message);
                return Result.Failed;
            }
        }

    }
}
