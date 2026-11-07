// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The actual model work behind the Unhide All tool, separated from any UI so the command that calls it (CmdUnhideAll) only has to present the result. Returns a result object and shows nothing itself. The split was made for a second caller (the Web Panel) that was removed in 1.44.0. It is kept because UI-free model work is worth having on its own - it is the shape any future caller needs, and it is testable without a dialog.

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace HeronTools.Services.UnhideAll
{
    /// <summary>What Unhide All did, with the exact text the ribbon command displays.</summary>
    public class UnhideAllResult
    {
        public int UnhiddenCount { get; set; }

        /// <summary>Elements Revit would not show again (with the first reason).</summary>
        public int RefusedCount { get; set; }

        public string FirstRefusal { get; set; }

        public bool TemporaryHideIsolateCleared { get; set; }

        public bool TemporaryHideIsolateWasActive { get; set; }

        /// <summary>False when Revit did not keep the change (for example, the view is owned by another user).</summary>
        public bool Kept { get; set; } = true;

        public bool DidSomething => Kept && (UnhiddenCount > 0 || TemporaryHideIsolateCleared);

        public string Summary
        {
            get
            {
                if (!Kept)
                    return "Revit did not keep the change, so nothing was unhidden. The view may be owned by another user - " +
                           "try again after it is free.";

                var lines = new List<string>();
                lines.Add(UnhiddenCount > 0
                    ? UnhiddenCount + " hidden element(s) shown again."
                    : "No element was hidden one by one in this view.");

                if (TemporaryHideIsolateCleared)
                    lines.Add("Temporary Hide/Isolate turned off.");
                else if (TemporaryHideIsolateWasActive)
                    lines.Add("Temporary Hide/Isolate is on but Revit would not turn it off.");

                if (RefusedCount > 0)
                    lines.Add($"{RefusedCount} element(s) could not be shown again: {FirstRefusal}");

                lines.Add("Categories and filters turned off in Visibility/Graphics were not changed.");
                return string.Join("\n", lines);
            }
        }
    }

    public static class UnhideAllService
    {
        private const string TransactionName = "Unhide All";

        /// <summary>
        /// Shows everything hidden one by one in the view and turns Temporary Hide/Isolate off - as ONE
        /// transaction (one Undo step). Does nothing at all, and leaves no empty Undo step, when nothing is hidden.
        /// </summary>
        public static UnhideAllResult Run(Document doc, View view)
        {
            List<ElementId> hiddenIds = CollectHiddenElementIds(doc, view);
            bool thiWasActive = view.IsTemporaryHideIsolateActive();
            var result = new UnhideAllResult { TemporaryHideIsolateWasActive = thiWasActive };

            if (hiddenIds.Count == 0 && !thiWasActive)
                return result;

            using (Transaction trans = new Transaction(doc, TransactionName))
            {
                trans.Start();

                // One call for all; if Revit refuses any one id, fall back to one by one so the rest still show.
                if (hiddenIds.Count > 0)
                {
                    try
                    {
                        view.UnhideElements(hiddenIds);
                        result.UnhiddenCount = hiddenIds.Count;
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        foreach (ElementId id in hiddenIds)
                        {
                            try
                            {
                                view.UnhideElements(new List<ElementId> { id });
                                result.UnhiddenCount++;
                            }
                            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                            {
                                result.RefusedCount++;
                                if (result.FirstRefusal == null) result.FirstRefusal = ex.Message;
                            }
                        }
                    }
                }

                if (thiWasActive)
                {
                    // DisableTemporaryViewMode does nothing, without error, when the mode is already off.
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                    result.TemporaryHideIsolateCleared = !view.IsTemporaryHideIsolateActive();
                }

                if (result.UnhiddenCount == 0 && !result.TemporaryHideIsolateCleared)
                {
                    trans.RollBack();
                }
                else if (trans.Commit() != TransactionStatus.Committed)
                {
                    result.Kept = false;
                    result.UnhiddenCount = 0;
                    result.TemporaryHideIsolateCleared = false;
                }
            }

            return result;
        }

        /// <summary>
        /// Elements hidden one by one in this view. Only model elements and this view's own annotation are
        /// looked at (other views' annotation can never be hidden here), and only those Revit says can be
        /// hidden in the view - much faster than asking every element in the model.
        /// </summary>
        private static List<ElementId> CollectHiddenElementIds(Document doc, View view)
        {
            var ownedHereOrModel = new LogicalOrFilter(
                new ElementOwnerViewFilter(view.Id),
                new ElementOwnerViewFilter(ElementId.InvalidElementId));

            var hiddenIds = new List<ElementId>();
            foreach (Element element in new FilteredElementCollector(doc)
                         .WhereElementIsNotElementType()
                         .WherePasses(ownedHereOrModel))
            {
                if (element.Category == null || !element.IsValidObject)
                    continue;

                // A few special elements throw on IsHidden; one of them must not stop the whole tool.
                try
                {
                    if (element.IsHidden(view))
                        hiddenIds.Add(element.Id);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                }
            }

            return hiddenIds;
        }
    }
}
