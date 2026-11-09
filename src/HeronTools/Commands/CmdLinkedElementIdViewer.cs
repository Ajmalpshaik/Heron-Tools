// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Lets the user pick one element - in the host model or a linked model - and shows its Element ID together with which model it came from.

using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using HeronTools.UI;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdLinkedElementIdViewer : IExternalCommand
    {
        private const string Title = "Linked ID Viewer";

        /// <summary>
        /// Shows a dialog with the element ID and model source for a picked host or linked element.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uiDoc = uiApp.ActiveUIDocument;

                if (uiDoc == null)
                {
                    DialogHelper.ShowError(Title, "Please open a project before running this tool.");
                    return Result.Cancelled;
                }

                Reference pickedReference = PickObject(uiDoc);
                if (pickedReference == null)
                    return Result.Cancelled;

                if (pickedReference.ElementId == ElementId.InvalidElementId)
                {
                    DialogHelper.ShowError(Title, "Please select a valid element.");
                    return Result.Cancelled;
                }

                ElementId elementIdToShow;
                string modelSource;

                if (pickedReference.LinkedElementId != ElementId.InvalidElementId)
                {
                    if (!GetLinkedElementInfo(
                            uiDoc.Document,
                            pickedReference,
                            out elementIdToShow,
                            out modelSource,
                            out string problem))
                    {
                        // Our own message, then Cancelled - so Revit adds no second, generic failure box.
                        DialogHelper.ShowError(Title, problem);
                        return Result.Cancelled;
                    }
                }
                else
                {
                    if (!GetHostElementInfo(
                            uiDoc.Document,
                            pickedReference,
                            out elementIdToShow,
                            out modelSource,
                            out string problem))
                    {
                        // Our own message, then Cancelled - so Revit adds no second, generic failure box.
                        DialogHelper.ShowError(Title, problem);
                        return Result.Cancelled;
                    }
                }

                if (elementIdToShow == ElementId.InvalidElementId)
                {
                    DialogHelper.ShowError(Title, "Could not determine the Element ID for the selection.");
                    return Result.Cancelled;
                }

                var window = new LinkedIdViewerWindow(
                    ElementIdHelper.ToReportString(elementIdToShow),
                    modelSource);

                new WindowInteropHelper(window)
                {
                    Owner = commandData.Application.MainWindowHandle
                };

                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError(Title, "The Element ID could not be read. Revit said:\n\n" + ex.Message);
                return Result.Cancelled;
            }
        }

        private Reference PickObject(UIDocument uiDoc)
        {
            try
            {
                // PointOnElement reaches the current model AND loaded links: a linked pick returns the link
                // instance as ElementId with LinkedElementId set, a host pick leaves LinkedElementId invalid.
                // (LinkedElement alone can never pick a host element.)
                Reference picked = uiDoc.Selection.PickObject(
                    ObjectType.PointOnElement,
                    "Select an element in the current model or a linked model, or press ESC to cancel");

                // A click on a loaded link that did not land on one element inside it comes back as the link
                // instance itself - ask once more with the linked-element pick this tool always used for links.
                if (picked != null &&
                    picked.LinkedElementId == ElementId.InvalidElementId &&
                    uiDoc.Document.GetElement(picked.ElementId) is RevitLinkInstance linkInstance &&
                    linkInstance.GetLinkDocument() != null)
                {
                    return uiDoc.Selection.PickObject(
                        ObjectType.LinkedElement,
                        "Select the element inside the linked model, or press ESC to cancel");
                }

                return picked;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        private bool GetLinkedElementInfo(
            Document doc,
            Reference reference,
            out ElementId elementId,
            out string modelSource,
            out string errorMessage)
        {
            elementId = ElementId.InvalidElementId;
            modelSource = string.Empty;
            errorMessage = string.Empty;

            RevitLinkInstance linkInstance = doc.GetElement(reference.ElementId) as RevitLinkInstance;
            if (linkInstance == null)
            {
                errorMessage = "The selected reference is not a valid Revit link instance.";
                return false;
            }

            Document linkDoc = linkInstance.GetLinkDocument();
            if (linkDoc == null)
            {
                errorMessage = "The linked model is not loaded. Please load it and try again.";
                return false;
            }

            if (reference.LinkedElementId == ElementId.InvalidElementId)
            {
                errorMessage = "The selected reference does not point to a linked element.";
                return false;
            }

            elementId = reference.LinkedElementId;
            modelSource = "Linked Model: " + GetCleanLinkName(linkInstance, linkDoc);

            return true;
        }

        private bool GetHostElementInfo(
            Document doc,
            Reference reference,
            out ElementId elementId,
            out string modelSource,
            out string errorMessage)
        {
            elementId = ElementId.InvalidElementId;
            modelSource = "Current Model";
            errorMessage = string.Empty;

            Element element = doc.GetElement(reference.ElementId);
            if (element == null)
            {
                errorMessage = "Unable to read the selected element.";
                return false;
            }

            elementId = element.Id;
            return true;
        }

        private static string GetCleanLinkName(RevitLinkInstance linkInstance, Document linkDoc)
        {
            string name = (linkDoc != null && !string.IsNullOrWhiteSpace(linkDoc.Title))
                ? linkDoc.Title
                : (linkInstance?.Name ?? "Linked Model");

            int colonIndex = name.IndexOf(':');
            if (colonIndex > -1)
            {
                name = name.Substring(0, colonIndex).Trim();
            }

            return name;
        }
    }
}
