// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Quick Clash: opens the modeless clash window for the active 3D view (or brings it forward if it is open).
// The command itself changes nothing; Run and Reset in the window are each their own Undo step.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Services.QuickClash;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdQuickClash : IExternalCommand
    {
        private const string DialogTitle = "Quick Clash";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uiDoc = commandData.Application.ActiveUIDocument;
                Document doc = uiDoc?.Document;
                if (doc == null || doc.IsFamilyDocument)
                {
                    DialogHelper.ShowError(DialogTitle, "Open a project and a 3D view, then click Quick Clash.");
                    return Result.Cancelled;
                }
                if (doc.IsReadOnly)
                {
                    DialogHelper.ShowError(DialogTitle, "This model is read-only, so Quick Clash cannot colour the view. Open an editable copy.");
                    return Result.Cancelled;
                }
                if (!(uiDoc.ActiveView is View3D view) || view.IsTemplate)
                {
                    DialogHelper.ShowError(DialogTitle, "Quick Clash works in a 3D view. Open a 3D view and click it again.");
                    return Result.Cancelled;
                }

                QuickClashSession.Open(commandData.Application);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError(DialogTitle, "Quick Clash could not open, and nothing was changed.\n\nRevit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }
}
