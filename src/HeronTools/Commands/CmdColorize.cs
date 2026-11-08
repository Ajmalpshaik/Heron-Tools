// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Entry point command - validates context and opens the Colorize window. The window itself applies each Shuffle Colors click directly (its own transaction, same as Filter Pro), so this command only reports whether any click made changes.

using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.UI;

namespace HeronTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdColorize : IExternalCommand
    {
        private const string DialogTitle = "Colorize";
        private static bool _isWindowOpen;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;

            if (!ValidateContext(uiDoc, out Document doc, out View activeView, out message))
            {
                TaskDialog.Show(DialogTitle, message);
                return Result.Cancelled;
            }

            if (_isWindowOpen)
            {
                TaskDialog.Show(DialogTitle, "The Colorize window is already open.");
                return Result.Cancelled;
            }

            ColorizeWindow window = null;
            try
            {
                try
                {
                    _isWindowOpen = true;
                    window = new ColorizeWindow(doc, activeView);
                    new WindowInteropHelper(window)
                    {
                        Owner = commandData.Application.MainWindowHandle
                    };
                    window.ShowDialog();
                }
                finally
                {
                    _isWindowOpen = false;
                }

                return window.HasChanges ? Result.Succeeded : Result.Cancelled;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                // Revit undoes everything a command kept when it returns Failed - so if Colorize already kept
                // changes, the result is Succeeded and those changes stay.
                bool keep = window != null && window.HasChanges;
                TaskDialog.Show(DialogTitle,
                    (keep ? "Colorize stopped unexpectedly. The colours already applied were kept.\n\n"
                          : "Colorize stopped unexpectedly. Nothing was changed.\n\n") +
                    "Revit said: " + ex.Message);
                return keep ? Result.Succeeded : Result.Failed;
            }
        }

        private static bool ValidateContext(
            UIDocument uiDoc,
            out Document doc,
            out View activeView,
            out string validationMessage)
        {
            doc = null;
            activeView = null;
            validationMessage = string.Empty;

            if (uiDoc == null || uiDoc.Document == null)
            {
                validationMessage = "Open a project document before running this command.";
                return false;
            }

            doc = uiDoc.Document;

            if (doc.IsReadOnly)
            {
                validationMessage = "The current document is read-only. Please open an editable document.";
                return false;
            }

            if (doc.IsFamilyDocument)
            {
                validationMessage = "Colorize cannot be used in family documents. Please open a project document.";
                return false;
            }

            activeView = uiDoc.ActiveView;

            if (activeView == null)
            {
                validationMessage = "No active view found. Please open a view before running this command.";
                return false;
            }

            if (!CmdFilterProAvailability.CanViewHaveFilters(activeView, out string viewReason))
            {
                validationMessage = "Colorize cannot work in this view. " + viewReason;
                return false;
            }

            return true;
        }
    }
}
