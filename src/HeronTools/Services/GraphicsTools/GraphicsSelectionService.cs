// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Shared command context creation, single-transaction execution, and preselection-first selection capture for all Graphics tools.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using HeronTools.Models.GraphicsTools;
using HeronTools.Utils;

namespace HeronTools.Services.GraphicsTools
{
    internal sealed class GraphicsCommandContext
    {
        internal GraphicsCommandContext(UIDocument uidocument, Document document, View activeView)
        {
            UIDocument = uidocument;
            Document = document;
            ActiveView = activeView;
        }

        internal UIDocument UIDocument { get; }

        internal Document Document { get; }

        internal View ActiveView { get; }
    }

    internal static class GraphicsCommandService
    {
        internal static Result TryCreateContext(
            ExternalCommandData commandData,
            string title,
            ref string message,
            out GraphicsCommandContext context)
        {
            context = null;

            UIDocument uidoc = commandData.Application?.ActiveUIDocument;
            if (!ValidationHelper.ValidateUIDocumentAndView(uidoc, out message))
            {
                DialogHelper.ShowError(title, message);
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            if (!ValidationHelper.ValidateEditableDocument(doc, out message))
            {
                DialogHelper.ShowError(title, message);
                return Result.Cancelled;
            }

            View activeView = doc.ActiveView;
            if (!activeView.AreGraphicsOverridesAllowed())
            {
                message = "Graphics overrides are disabled for this view.";
                DialogHelper.ShowError(title, message);
                return Result.Cancelled;
            }

            context = new GraphicsCommandContext(uidoc, doc, activeView);
            return Result.Succeeded;
        }

        /// <summary>
        /// The one message every Graphics tool shows when it changed NOTHING - "a tool that did nothing
        /// must always say why" (house rule). Several of these tools used to finish in silence here.
        /// </summary>
        internal static void ReportNothingChanged(string title, GraphicsOperationSummary summary, string itemsWord)
        {
            int attempted = summary != null ? summary.Attempted : 0;
            string text = attempted > 0
                ? string.Format("Nothing was changed: Revit did not accept the change for any of the {0} {1} in this view.", attempted, itemsWord)
                : "Nothing was changed: nothing in the selection can be changed by this tool in this view.";

            if (summary != null && !string.IsNullOrWhiteSpace(summary.FirstSkipReason))
                text += "\n\nRevit's reason: " + summary.FirstSkipReason;

            DialogHelper.ShowError(title, text);
        }

        /// <summary>
        /// For the pick-until-Esc tools: some picks were changed, some were not. Says how many were left
        /// and why, once, at the end of the session.
        /// </summary>
        internal static void ReportNotChanged(string title, int changedCount, int notChangedCount, string firstReason)
        {
            if (notChangedCount <= 0)
                return;

            string text = changedCount > 0
                ? string.Format("Changed {0}. {1} pick(s) could not be changed in this view and were left as they were.", changedCount, notChangedCount)
                : string.Format("Nothing was changed: Revit did not accept the change for any of the {0} pick(s) in this view.", notChangedCount);

            if (!string.IsNullOrWhiteSpace(firstReason))
                text += "\n\nRevit's reason: " + firstReason;

            DialogHelper.ShowInfo(title, text);
        }

        internal static GraphicsOperationSummary ExecuteSummaryTransaction(
            Document doc,
            string transactionName,
            Func<GraphicsOperationSummary> operation)
        {
            if (doc == null || operation == null)
                return new GraphicsOperationSummary();

            using (var transaction = new Transaction(doc, transactionName))
            {
                bool completed = false;

                try
                {
                    transaction.Start();
                    GraphicsOperationSummary summary = operation() ?? new GraphicsOperationSummary();

                    if (summary.HasChanges)
                    {
                        // A change Revit refuses at commit (e.g. the view is owned by another user) is not a success.
                        if (transaction.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException(
                                "Revit did not keep the change - the view may be owned by another user, or a warning stopped it.");
                    }
                    else
                    {
                        transaction.RollBack();
                    }

                    completed = true;
                    return summary;
                }
                catch
                {
                    if (!completed)
                    {
                        try
                        {
                            transaction.RollBack();
                        }
                        catch
                        {
                            // Preserve the original operation error.
                        }
                    }

                    throw;
                }
            }
        }
    }

    /// <summary>
    /// Unified result for capture operations that may be cancelled by the user.
    /// </summary>
    internal sealed class SelectionCaptureResult
    {
        public SelectionCaptureResult(IList<ElementId> elementIds, bool wasCancelled)
        {
            ElementIds = elementIds ?? new List<ElementId>();
            WasCancelled = wasCancelled;
        }

        public IList<ElementId> ElementIds { get; }

        public bool WasCancelled { get; }
    }

    /// <summary>
    /// Selection helper methods with preselection-first behavior.
    /// </summary>
    internal static class GraphicsSelectionService
    {
        public static SelectionCaptureResult GetPreselectedOrPromptElementIds(
            UIDocument uidoc,
            ISelectionFilter selectionFilter,
            string prompt,
            ICollection<ElementId> excludedElementIds = null)
        {
            if (uidoc == null || uidoc.Document == null)
            {
                return new SelectionCaptureResult(new List<ElementId>(), false);
            }

            var excluded = BuildExcludedSet(excludedElementIds);

            IList<ElementId> validPreselection = FilterDistinctElementIds(
                uidoc.Document,
                uidoc.Selection.GetElementIds(),
                selectionFilter,
                excluded);
            if (validPreselection.Count > 0)
            {
                return new SelectionCaptureResult(validPreselection, false);
            }

            try
            {
                IList<Reference> picks = selectionFilter == null
                    ? uidoc.Selection.PickObjects(ObjectType.Element, prompt)
                    : uidoc.Selection.PickObjects(ObjectType.Element, selectionFilter, prompt);

                var pickedIds = new List<ElementId>();
                if (picks != null)
                {
                    foreach (Reference pickedRef in picks)
                    {
                        if (pickedRef != null && pickedRef.ElementId != null)
                        {
                            pickedIds.Add(pickedRef.ElementId);
                        }
                    }
                }

                IList<ElementId> validPicks = FilterDistinctElementIds(uidoc.Document, pickedIds, selectionFilter, excluded);
                return new SelectionCaptureResult(validPicks, false);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return new SelectionCaptureResult(new List<ElementId>(), true);
            }
        }

        public static bool TryPickSingleElementId(
            UIDocument uidoc,
            ISelectionFilter selectionFilter,
            string prompt,
            out ElementId elementId,
            out bool wasCancelled)
        {
            elementId = ElementId.InvalidElementId;
            wasCancelled = false;

            if (uidoc == null || uidoc.Document == null)
            {
                return false;
            }

            try
            {
                Reference pickedRef = selectionFilter == null
                    ? uidoc.Selection.PickObject(ObjectType.Element, prompt)
                    : uidoc.Selection.PickObject(ObjectType.Element, selectionFilter, prompt);

                if (pickedRef == null || pickedRef.ElementId == null || pickedRef.ElementId == ElementId.InvalidElementId)
                {
                    return false;
                }

                if (uidoc.Document.GetElement(pickedRef.ElementId) == null)
                {
                    return false;
                }

                elementId = pickedRef.ElementId;
                return true;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                wasCancelled = true;
                return false;
            }
        }

        private static ISet<long> BuildExcludedSet(ICollection<ElementId> excludedElementIds)
        {
            var set = new HashSet<long>();
            if (excludedElementIds == null)
            {
                return set;
            }

            foreach (ElementId id in excludedElementIds)
            {
                if (id != null && id != ElementId.InvalidElementId)
                {
                    set.Add((id).LongValue());
                }
            }

            return set;
        }

        private static IList<ElementId> FilterDistinctElementIds(
            Document doc,
            IEnumerable<ElementId> elementIds,
            ISelectionFilter selectionFilter,
            ISet<long> excluded)
        {
            var result = new List<ElementId>();
            if (doc == null || elementIds == null)
            {
                return result;
            }

            var seen = new HashSet<long>();

            foreach (ElementId id in elementIds)
            {
                if (id == null || id == ElementId.InvalidElementId)
                {
                    continue;
                }

                long key = (id).LongValue();
                if (excluded != null && excluded.Contains(key))
                {
                    continue;
                }

                if (seen.Contains(key))
                {
                    continue;
                }

                Element element = doc.GetElement(id);
                if (element == null)
                {
                    continue;
                }

                if (selectionFilter != null && !selectionFilter.AllowElement(element))
                {
                    continue;
                }

                seen.Add(key);
                result.Add(id);
            }

            return result;
        }
    }
}
