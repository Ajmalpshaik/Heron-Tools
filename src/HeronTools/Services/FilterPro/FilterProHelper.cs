// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Orchestrates Filter Pro operations — validates categories, delegates to FilterCreator and FilterApplier/FilterReorderer, and returns affected count.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using HeronTools.Models;

using HeronTools.Utils;
namespace HeronTools.Services.FilterPro
{
    /// <summary>
    /// Orchestrates FilterPro operations: validation, creation/update, applying, and ordering.
    /// Caller must own the transaction scope.
    /// </summary>
    internal static class FilterProHelper
    {
        public static int CreateFilters(Document doc,
                                        IEnumerable<View> targetViews,
                                        FilterSelection selection,
                                        IList<string> skipped)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

#if DEBUG
            if (!doc.IsModifiable)
                throw new InvalidOperationException(
                    "FilterProHelper.CreateFilters must be called inside an open Transaction or TransactionGroup.");
#endif

            if (selection == null)
            {
                skipped?.Add("Selection is null.");
                return 0;
            }

            if (selection.Parameter == null)
            {
                skipped?.Add("Parameter is not defined in selection.");
                return 0;
            }

            if (selection.Values == null || !selection.Values.Any())
            {
                skipped?.Add("No values provided for filter creation.");
                return 0;
            }

            var viewTargets = (selection.ApplyToView && targetViews != null)
                ? targetViews.Where(v => v != null).ToList()
                : new List<View>();

            var validCategoryIds = ValidateCategories(doc, selection.CategoryIds, skipped);
            if (validCategoryIds.Count == 0)
                return 0;

            var creationResult = FilterCreator.CreateOrUpdateFilters(doc, selection, validCategoryIds, skipped);
            if (creationResult == null)
            {
                skipped?.Add("No filters were created or updated.");
                return 0;
            }

            var processedFilterIds = new List<ElementId>();
            var processedIdSet = new HashSet<long>();

            if (creationResult.ProcessedFilterIds != null)
            {
                foreach (var id in creationResult.ProcessedFilterIds)
                {
                    if (id != null &&
                        id != ElementId.InvalidElementId &&
                        processedIdSet.Add(id.LongValue()))
                    {
                        processedFilterIds.Add(id);
                    }
                }
            }

            // A filter that was only REUSED (it already existed, nothing rewritten) counts as a change only if
            // it actually changed a view. Otherwise a run where every view is template-controlled, or every
            // filter was already in place, would report "Applied: N" and leave an empty step in Undo.
            int madeOrRewritten = creationResult.Created + creationResult.Updated;

            if (!selection.ApplyToView ||
                !viewTargets.Any() ||
                !processedFilterIds.Any())
            {
                return madeOrRewritten;
            }

            var solidFillId = FilterApplier.GetSolidFillId(doc);
            if (selection.RandomColors)
                ColorPalette.BeginShuffle();
            bool anyViewChanged = false;

            if (!selection.PlaceNewFiltersFirst)
            {
                foreach (var view in viewTargets)
                {
                    if (FilterApplier.IsViewControlledByTemplate(view))
                    {
                        skipped?.Add(FilterApplier.TemplateSkipMessage(view));
                        continue;
                    }

                    foreach (var filterId in processedFilterIds)
                    {
                        FilterApplier.ApplyToView(doc, view, filterId, selection, solidFillId, skipped);
                        anyViewChanged = true;
                    }
                }
            }
            else
            {
                foreach (var view in viewTargets)
                {
                    anyViewChanged |= FilterReorderer.ReorderFiltersInView(doc, view, processedFilterIds, selection, solidFillId, skipped);
                }
            }

            return madeOrRewritten + (anyViewChanged ? creationResult.Reused : 0);
        }

        private static List<ElementId> ValidateCategories(Document doc,
                                                          IEnumerable<ElementId> categoryIds,
                                                          IList<string> skipped)
        {
            var validCategoryIds = new List<ElementId>();

            if (categoryIds != null)
            {
                foreach (var catId in categoryIds)
                {
                    try
                    {
                        var cat = Category.GetCategory(doc, catId);
                        // In Revit 2020, use presence of category only. IsCategoryValidForParameterFilter is not available.
                        if (cat != null)
                        {
                            validCategoryIds.Add(catId);
                        }
                    }
                    catch (Exception ex)
                    {
                        skipped?.Add($"Category ID {catId.LongValue()}: skipped — {ex.Message}");
                    }
                }
            }

            if (validCategoryIds.Count == 0)
                skipped?.Add("No valid categories found for parameter filter.");

            return validCategoryIds;
        }
    }
}
