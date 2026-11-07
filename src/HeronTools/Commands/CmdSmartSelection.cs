// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Pick one reference element, then one window/crossing box-select more elements in the view - only elements sharing the reference element's category are added to the selection; everything else caught in the box is skipped automatically.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    /// <summary>
    /// Entry command for Smart Selection: use a pre-existing single-element selection (if any) or pick
    /// a reference element, then one window/crossing box-select more elements of the same category only.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdSmartSelection : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDocument = commandData.Application?.ActiveUIDocument;
            if (!ValidationHelper.ValidateUIDocumentAndView(uiDocument, out message))
            {
                DialogHelper.ShowError("Smart Selection", message);
                return Result.Cancelled;
            }

            Document document = uiDocument.Document;

            try
            {
                Element referenceElement = GetPreSelectedReference(uiDocument) ?? PickReferenceElement(uiDocument);
                if (referenceElement == null)
                {
                    return Result.Cancelled;
                }

                Category referenceCategory = referenceElement.Category;
                if (referenceCategory == null)
                {
                    DialogHelper.ShowError(
                        "Smart Selection",
                        "That element has no category to match against. Please pick a normal model element.");
                    return Result.Cancelled;
                }

                HashSet<ElementId> matchedIds = new HashSet<ElementId>(new ElementIdIntegerComparer())
                {
                    referenceElement.Id
                };

                try
                {
                    IList<Element> pickedElements = uiDocument.Selection.PickElementsByRectangle(
                        new SmartSelectionFilter(referenceCategory.Id),
                        $"Smart Selection: window or crossing-select more {referenceCategory.Name} elements.");

                    foreach (Element pickedElement in pickedElements)
                    {
                        if (pickedElement != null)
                        {
                            matchedIds.Add(pickedElement.Id);
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // Esc during the follow-up stage - keep just the reference element selected.
                }

                uiDocument.Selection.SetElementIds(matchedIds);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                DialogHelper.ShowError("Smart Selection", "An unexpected error occurred:\n" + ex.Message);
                return Result.Cancelled;
            }
        }

        private static Element PickReferenceElement(UIDocument uiDocument)
        {
            try
            {
                Reference reference = uiDocument.Selection.PickObject(
                    ObjectType.Element,
                    new SmartSelectionFilter(),
                    "Smart Selection: pick the reference element. Press Esc to cancel.");

                return uiDocument.Document.GetElement(reference);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// If exactly one categorized element was already selected in Revit before the command ran,
        /// use it directly as the reference element and skip the interactive pick. Zero, more than
        /// one, or an uncategorized pre-selection all fall back to the normal PickObject prompt.
        /// </summary>
        private static Element GetPreSelectedReference(UIDocument uiDocument)
        {
            ICollection<ElementId> preSelectedIds = uiDocument.Selection.GetElementIds();
            if (preSelectedIds.Count != 1)
            {
                return null;
            }

            Element preSelectedElement = uiDocument.Document.GetElement(preSelectedIds.First());
            return preSelectedElement?.Category != null ? preSelectedElement : null;
        }
    }
}
