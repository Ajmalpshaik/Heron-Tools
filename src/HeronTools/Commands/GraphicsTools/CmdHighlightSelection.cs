// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Highlight Selection: the selected elements red, everything else in the active view grey - and the same
// button puts the view back exactly as it was.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Models.GraphicsTools;
using HeronTools.Services.GraphicsTools;
using HeronTools.Utils;

namespace HeronTools.Commands.GraphicsTools
{
    /// <summary>
    /// First click: red for the selection, grey for the rest of the view. Second click in the same view: every
    /// element gets back the graphic overrides it had before (they are remembered, not reset to blank).
    /// </summary>
    /// <remarks>
    /// Heron Tools change (2026-11-07): the AJ-Tools version wrote over every element's own overrides in the
    /// view and relied on a separate "Reset Graphics" tool that Heron Tools does not have - resetting would also
    /// have wiped overrides the modeller set on purpose. Now the original overrides are captured before the
    /// highlight and restored by the next click. They live for this Revit session; after a restart, Undo or
    /// Revit's own "Override Graphics in View &gt; Reset" are the way back. Ids compared as 64-bit.
    /// </remarks>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdHighlightSelection : IExternalCommand
    {
        private const string DialogTitle = "Highlight Selection";

        private static readonly GraphicsColorValue HighlightColor = GraphicsColorValue.FromRgb(255, 0, 0);
        private static readonly GraphicsColorValue RestColor = GraphicsColorValue.FromRgb(128, 128, 128);

        /// <summary>Per document + view: each touched element's overrides from before the highlight.</summary>
        private static readonly Dictionary<string, Dictionary<long, OverrideGraphicSettings>> SavedByView =
            new Dictionary<string, Dictionary<long, OverrideGraphicSettings>>();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Result contextResult = GraphicsCommandService.TryCreateContext(
                    commandData,
                    DialogTitle,
                    ref message,
                    out GraphicsCommandContext context);
                if (contextResult != Result.Succeeded)
                    return contextResult;

                string key = ViewKey(context.Document, context.ActiveView);

                // Second click in a highlighted view: put it back - unless the highlight was already undone
                // (Undo, or overrides changed by hand), in which case the old memory is dropped and this click
                // highlights again.
                if (SavedByView.TryGetValue(key, out Dictionary<long, OverrideGraphicSettings> saved))
                {
                    if (StillHighlighted(context.Document, context.ActiveView, saved))
                        return Restore(context, key, saved);
                    SavedByView.Remove(key);
                }

                return Highlight(context, key);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError(DialogTitle,
                    "Highlight Selection stopped, and nothing was changed.\n\nRevit said: " + ex.Message);
                return Result.Failed;
            }
        }

        private static Result Highlight(GraphicsCommandContext context, string key)
        {
            SelectionCaptureResult selection = GraphicsSelectionService.GetPreselectedOrPromptElementIds(
                context.UIDocument,
                selectionFilter: null,
                prompt: "Select the elements to highlight, then click Finish.");

            if (selection.WasCancelled)
                return Result.Cancelled;

            if (selection.ElementIds.Count == 0)
            {
                DialogHelper.ShowError(DialogTitle, "Nothing is selected. Select the elements to highlight and click Highlight again.");
                return Result.Cancelled;
            }

            ICollection<ElementId> elementsInView = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();
            var viewKeys = new HashSet<long>(elementsInView.Select(id => id.LongValue()));

            List<ElementId> highlightIds = selection.ElementIds
                .Where(id => viewKeys.Contains(id.LongValue()))
                .Distinct(new ElementIdIntegerComparer())
                .ToList();

            if (highlightIds.Count == 0)
            {
                DialogHelper.ShowError(DialogTitle,
                    "None of the selected elements are shown in this view. Select elements you can see here and try again.");
                return Result.Cancelled;
            }

            highlightIds = InsulationLining.WithWraps(context.Document, highlightIds, viewKeys);

            var highlightKeys = new HashSet<long>(highlightIds.Select(id => id.LongValue()));
            List<ElementId> restIds = elementsInView.Where(id => !highlightKeys.Contains(id.LongValue())).ToList();

            OverrideGraphicSettings highlightSettings = BuildSolidOverride(context.Document, HighlightColor);
            OverrideGraphicSettings restSettings = BuildSolidOverride(context.Document, RestColor);

            // Remember every element's own overrides BEFORE changing anything.
            var original = new Dictionary<long, OverrideGraphicSettings>();
            foreach (ElementId id in highlightIds.Concat(restIds))
                original[id.LongValue()] = new OverrideGraphicSettings(context.ActiveView.GetElementOverrides(id));

            GraphicsOperationSummary summary = GraphicsCommandService.ExecuteSummaryTransaction(
                context.Document,
                "Highlight Selection",
                () =>
                {
                    GraphicsOperationSummary red = GraphicsElementService.ApplyOverrides(
                        context.Document, context.ActiveView, highlightIds, highlightSettings);
                    GraphicsOperationSummary grey = GraphicsElementService.ApplyOverrides(
                        context.Document, context.ActiveView, restIds, restSettings);

                    return new GraphicsOperationSummary
                    {
                        Attempted = red.Attempted + grey.Attempted,
                        Applied = red.Applied + grey.Applied,
                        Skipped = red.Skipped + grey.Skipped,
                        FirstSkipReason = red.FirstSkipReason ?? grey.FirstSkipReason
                    };
                });

            if (!summary.HasChanges)
            {
                GraphicsCommandService.ReportNothingChanged(DialogTitle, summary, "element(s)");
                return Result.Cancelled;
            }

            SavedByView[key] = original;
            return Result.Succeeded;
        }

        private static Result Restore(GraphicsCommandContext context, string key,
                                      Dictionary<long, OverrideGraphicSettings> saved)
        {
            int restored = 0;
            using (var transaction = new Transaction(context.Document, "Clear Highlight"))
            {
                transaction.Start();
                foreach (KeyValuePair<long, OverrideGraphicSettings> entry in saved)
                {
                    ElementId id = ElementIdHelper.FromLong(entry.Key);
                    if (context.Document.GetElement(id) == null)
                        continue; // deleted since the highlight

                    try
                    {
                        context.ActiveView.SetElementOverrides(id, entry.Value);
                        restored++;
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                        // one refused element must not stop the rest being put back
                    }
                }

                if (transaction.Commit() != TransactionStatus.Committed)
                {
                    DialogHelper.ShowError(DialogTitle, "Revit did not keep the change, so the highlight is still on. Try again, or press Undo.");
                    return Result.Failed;
                }
            }

            // The memory is kept: if this restore is undone, the next click restores again (StillHighlighted).
            return restored > 0 ? Result.Succeeded : Result.Cancelled;
        }

        /// <summary>
        /// True if elements this tool coloured still carry the highlight: red or grey AND different from the
        /// element's own remembered colour (so an element the modeller had made red or grey himself does not count).
        /// </summary>
        private static bool StillHighlighted(Document doc, View view, Dictionary<long, OverrideGraphicSettings> saved)
        {
            int looked = 0;
            foreach (KeyValuePair<long, OverrideGraphicSettings> entry in saved)
            {
                ElementId id = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(id) == null)
                    continue;            // deleted elements are not counted

                Color c = view.GetElementOverrides(id).ProjectionLineColor;
                Color own = entry.Value.ProjectionLineColor;
                bool isHighlight = c != null && c.IsValid &&
                    ((c.Red == 255 && c.Green == 0 && c.Blue == 0) || (c.Red == 128 && c.Green == 128 && c.Blue == 128));
                bool sameAsOwn = own != null && own.IsValid && own.Red == c?.Red && own.Green == c?.Green && own.Blue == c?.Blue;
                if (isHighlight && !sameAsOwn)
                    return true;
                if (++looked >= 500)
                    break;
            }
            return false;
        }

        /// <summary>
        /// The elements Highlight coloured in this view, while the highlight is still on - for Reset Graphics,
        /// which can leave them to Highlight's own second click (that puts back each element's own colours).
        /// </summary>
        internal static List<long> HighlightedIds(Document doc, View view)
        {
            if (SavedByView.TryGetValue(ViewKey(doc, view), out Dictionary<long, OverrideGraphicSettings> saved) &&
                StillHighlighted(doc, view, saved))
                return saved.Keys.ToList();
            return new List<long>();
        }

        private static string ViewKey(Document doc, View view)
        {
            string docKey = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;
            return docKey + "|" + view.Id.LongValue();
        }

        private static OverrideGraphicSettings BuildSolidOverride(Document doc, GraphicsColorValue color)
        {
            // The drafting solid fill, as Filter Pro uses (a solid MODEL pattern is not the one view
            // overrides expect).
            ElementId solidFillPatternId = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(pattern =>
                {
                    FillPattern fp = pattern.GetFillPattern();
                    return fp != null && fp.IsSolidFill && fp.Target == FillPatternTarget.Drafting;
                })
                ?.Id ?? ElementId.InvalidElementId;

            var input = new GraphicsOverrideInput
            {
                ProjectionLineColor = color,
                SurfaceForegroundPatternId = solidFillPatternId,
                SurfaceForegroundPatternColor = color,
                UseProjectionSurfaceSettingsForCut = true
            };

            return GraphicsOverrideBuilder.Build(input);
        }
    }
}
