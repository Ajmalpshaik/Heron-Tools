// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Entry point command — validates context and launches the Filter Pro window.

using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.UI;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdFilterPro : IExternalCommand
    {
        /// <summary>
        /// Opens the Filter Pro window after validating document context.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;

            if (!ValidateContext(uiDoc, out Document doc, out View activeView, out message))
            {
                DialogHelper.ShowError("Filter Pro", message);
                return Result.Cancelled;
            }

            FilterProWindow window = null;
            try
            {
                if (!WarnForLargeDocument(doc))
                    return Result.Cancelled;

                window = new FilterProWindow(doc, activeView);
                new WindowInteropHelper(window)
                {
                    Owner = commandData.Application.MainWindowHandle
                };

                window.ShowDialog();

                return window.HasChanges ? Result.Succeeded : Result.Cancelled;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                // Revit undoes everything a command kept when it returns Failed. Filters already made by earlier
                // clicks in this window are kept (Succeeded); otherwise nothing was changed.
                bool keep = window != null && window.HasChanges;
                DialogHelper.ShowError(
                    "Filter Pro",
                    (keep ? "Filter Pro stopped unexpectedly. The filters already made were kept.\n\n"
                          : "Filter Pro stopped unexpectedly. Nothing was changed.\n\n") +
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
                validationMessage = "This model is read-only, so filters cannot be added. Open a model you can edit.";
                return false;
            }

            if (doc.IsFamilyDocument)
            {
                validationMessage = "Filter Pro cannot be used in family documents. Please open a project document.";
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
                validationMessage =
                    $"The current view ({activeView.ViewType}) does not support filters.\n\n" +
                    $"{viewReason}\n\n" +
                    "Please switch to a view that supports visibility/graphics filters " +
                    "(e.g. plan, section, elevation, 3D).";
                return false;
            }

            return true;
        }

        private static bool WarnForLargeDocument(Document doc)
        {
            int elementCount = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .GetElementCount();

            if (elementCount > 100_000)
            {
                return DialogHelper.ShowYesNo(
                    "Filter Pro - Large Document",
                    $"This document contains {elementCount:N0} elements.\n" +
                    "Filter operations may take some time.\n\n" +
                    "Do you want to continue?");
            }

            return true;
        }
    }
}
