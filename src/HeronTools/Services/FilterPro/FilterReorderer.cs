#region Metadata
/*
 * Tool Name     : Filter Pro
 * File Name     : FilterReorderer.cs
 * Purpose       : Reorders filters in a view so newly created filters appear first, while
 *                 preserving the graphic overrides and visibility of all pre-existing filters.
 *
 * Author        : Ajmal P.S.
 * Version       : 1.0.2
 *
 * Created Date  : 2025-12-10
 * Last Updated  : 2026-10-12
 *
 * Target Revit  : 2020 - latest (A: 2020-2024 / B: 2025-2026 / C: 2027+ - verify newest)
 * Framework     : .NET Fx 4.7.2 (2020) / verify 4.8 (2021-2024) | .NET 8 (2025-2026) | 2027+ verify Autodesk SDK
 * Platform      : C# Revit Add-in
 *
 * Dependencies  : Autodesk Revit API, System.Linq
 *
 * Input         : Active View, list of newly processed filter ElementIds, FilterSelection (graphics options)
 * Output        : View filter order updated; graphics and visibility of existing filters preserved
 *
 * Notes         :
 * - Targets Revit 2020 through latest.
 * - 2020 = .NET Fx 4.7.2; 2021-2024 = .NET Fx (verify 4.8 if required); 2025-2026 = .NET 8; 2027+ = verify Autodesk SDK.
 * - View.GetFilters() order is not guaranteed in Revit 2020; a per-view cache (_lastKnownOrderByView)
 *   is maintained to track the intended UI order across calls.
 * - All writes occur inside a Transaction managed by the caller.
 * - Production-ready implementation.
 *
 * Changelog     :
 * v1.0.0 (2025-12-10) - Initial release.
 * v1.0.1 (2026-06-30) - Added mandatory metadata block; confirmed 2020-latest version coverage.
 * v1.0.2 (2026-10-12) - Fix: reordering removes and re-adds every filter, and Revit re-adds a filter
 *                        enabled, so filters the user had switched off ("Enable Filter" unticked) came
 *                        back switched on. Each filter's enabled tick is now saved and put back
 *                        (Revit 2021+; 2020 has no such setting). Ordering logic unchanged.
 *
 * License       : All Rights Reserved
 * Repo          : Heron Tools (moved from AJ-Tools on 2026-11-07)
 */
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using HeronTools.Models;
using HeronTools.Utils;

namespace HeronTools.Services.FilterPro
{
    internal static class FilterReorderer
    {
        // Revit 2020 only: it has no View.GetOrderedFilters, and GetFilters does not promise the order the
        // user sees, so the last order this tool wrote is remembered per document + view. From Revit 2021
        // the live order is read from Revit itself, so a manual reorder in Visibility/Graphics is kept.
        private static readonly Dictionary<string, List<ElementId>> _lastKnownOrderByView
            = new Dictionary<string, List<ElementId>>();

        // Orders written during the current run. They only become "known" once the run's transaction has
        // committed (AcceptPendingOrders); a rolled-back run must not leave a remembered order Revit never kept.
        private static readonly Dictionary<string, List<ElementId>> _pendingOrderByView
            = new Dictionary<string, List<ElementId>>();

        private static string OrderKey(Document doc, View view)
        {
            string docKey = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;
            return docKey + "|" + view.Id.LongValue();
        }

        /// <summary>Call after the run's transaction committed: the orders written are now the real ones.</summary>
        internal static void AcceptPendingOrders()
        {
            foreach (var entry in _pendingOrderByView)
                _lastKnownOrderByView[entry.Key] = entry.Value;
            _pendingOrderByView.Clear();
        }

        /// <summary>Call when the run was rolled back: forget the orders it wrote.</summary>
        internal static void DiscardPendingOrders()
        {
            _pendingOrderByView.Clear();
        }

        /// <summary>
        /// Puts the run's filters first in the view, keeping every other filter's order, overrides,
        /// visibility and Enable tick. Returns true when the view was actually changed.
        /// </summary>
        internal static bool ReorderFiltersInView(Document doc,
                                                  View view,
                                                  List<ElementId> processedFilterIds,
                                                  FilterSelection selection,
                                                  ElementId solidFillId,
                                                  IList<string> skipped)
        {
            if (view == null || processedFilterIds == null || processedFilterIds.Count == 0)
                return false;

            if (FilterApplier.IsViewControlledByTemplate(view))
            {
                skipped?.Add($"View '{view.Name}' uses a view template. Filters were not modified.");
                return false;
            }

            // NO try/catch here on purpose. A reorder removes every filter from the view and adds them
            // back; a failure half-way must reach the window, which rolls the whole run back. Catching it
            // here used to commit a view with filters (and their overrides) missing.
            var liveClean = GetLiveFilters(doc, view);
            var newFilters = GetNewFilters(doc, processedFilterIds);

            if (!newFilters.Any() && !liveClean.Any())
                return false;

            var baseline = BuildBaseline(doc, view, liveClean);
            var desiredOrder = BuildDesiredOrder(newFilters, baseline);

            if (IsOrderIdentical(baseline, desiredOrder))
            {
                // Already in place: only the colours change (Shuffle). Visibility is left as the user set it.
                bool recoloured = UpdateGraphicsForNewFilters(doc, view, newFilters, selection, solidFillId, skipped);
                _pendingOrderByView[OrderKey(doc, view)] = desiredOrder.ToList();
                return recoloured;
            }

            var (overridesMap, visibilityMap) = CaptureFilterSettings(view, liveClean);
            var enabledMap = CaptureFilterEnabledStates(view, liveClean);

            RemoveAllFilters(view, liveClean);
            ReapplyFilters(doc, view, desiredOrder, newFilters, selection, solidFillId, overridesMap, visibilityMap, skipped);
            RestoreFilterEnabledStates(view, enabledMap);

            _pendingOrderByView[OrderKey(doc, view)] = desiredOrder.ToList();
            return true;
        }

        /// <summary>
        /// The view's filters, in the order the user sees them in Visibility/Graphics where Revit can
        /// say (2021 and later); Revit 2020 can only list them.
        /// </summary>
        private static List<ElementId> GetLiveFilters(Document doc, View view)
        {
#if REVIT2021_OR_GREATER
            IEnumerable<ElementId> ids = view.GetOrderedFilters();
#else
            IEnumerable<ElementId> ids = view.GetFilters();
#endif
            return (ids ?? new List<ElementId>())
                .Where(id => id != null &&
                             id != ElementId.InvalidElementId &&
                             doc.GetElement(id) != null)
                .ToList();
        }

        private static List<ElementId> GetNewFilters(Document doc, List<ElementId> processedFilterIds)
        {
            return processedFilterIds
                .Where(id => id != null &&
                             id != ElementId.InvalidElementId &&
                             doc.GetElement(id) != null)
                .Distinct(new ElementIdIntegerComparer())
                .ToList();
        }

        private static List<ElementId> BuildBaseline(Document doc, View view, List<ElementId> liveClean)
        {
#if REVIT2021_OR_GREATER
            // Revit reports the real order (GetLiveFilters), so it wins over anything remembered.
            return new List<ElementId>(liveClean);
#else
            if (_lastKnownOrderByView.TryGetValue(OrderKey(doc, view), out var snapshot) && snapshot != null)
            {
                var snapIds = new HashSet<long>(liveClean.Select(x => x.LongValue()));
                var baseline = snapshot
                    .Where(id => snapIds.Contains(id.LongValue()))
                    .ToList();

                foreach (var id in liveClean)
                {
                    if (!baseline.Any(x => x.LongValue() == id.LongValue()))
                        baseline.Add(id);
                }

                return baseline;
            }

            return new List<ElementId>(liveClean);
#endif
        }

        private static List<ElementId> BuildDesiredOrder(List<ElementId> newFilters, List<ElementId> baseline)
        {
            var newSet = new HashSet<long>(newFilters.Select(x => x.LongValue()));
            var desiredOrder = new List<ElementId>();

            foreach (var id in newFilters)
            {
                if (!desiredOrder.Any(x => x.LongValue() == id.LongValue()))
                    desiredOrder.Add(id);
            }

            foreach (var id in baseline)
            {
                if (!newSet.Contains(id.LongValue()) &&
                    !desiredOrder.Any(x => x.LongValue() == id.LongValue()))
                {
                    desiredOrder.Add(id);
                }
            }

            return desiredOrder;
        }

        private static bool IsOrderIdentical(List<ElementId> baseline, List<ElementId> desiredOrder)
        {
            if (baseline.Count != desiredOrder.Count)
                return false;

            for (int i = 0; i < baseline.Count; i++)
            {
                if (baseline[i].LongValue() != desiredOrder[i].LongValue())
                    return false;
            }

            return true;
        }

        // Every method below lets a Revit failure THROW. They all run inside the window's one transaction,
        // which rolls the whole run back on any failure - a half-done reorder would leave filters missing.

        /// <summary>
        /// The run's filters are already on the view, in place: only their colours change. Their
        /// visibility is left exactly as the user set it. Returns true if any override was written.
        /// </summary>
        private static bool UpdateGraphicsForNewFilters(Document doc,
                                                        View view,
                                                        List<ElementId> newFilters,
                                                        FilterSelection selection,
                                                        ElementId solidFillId,
                                                        IList<string> skipped)
        {
            bool changed = false;
            foreach (var id in newFilters)
                changed |= FilterApplier.ApplyGraphicsToFilter(doc, view, id, selection, solidFillId, skipped);
            return changed;
        }

        private static (Dictionary<ElementId, OverrideGraphicSettings>, Dictionary<ElementId, bool>)
            CaptureFilterSettings(View view, List<ElementId> liveClean)
        {
            var overridesMap = new Dictionary<ElementId, OverrideGraphicSettings>(new ElementIdIntegerComparer());
            var visibilityMap = new Dictionary<ElementId, bool>(new ElementIdIntegerComparer());

            foreach (var id in liveClean)
            {
                // Read failures are not guessed around: a filter whose settings cannot be read must not be
                // removed and re-added with blank overrides or the wrong visibility.
                var ogs = view.GetFilterOverrides(id);
                if (ogs != null)
                    overridesMap[id] = FilterApplier.CloneOverrideGraphics(ogs);

                visibilityMap[id] = view.GetFilterVisibility(id);
            }

            return (overridesMap, visibilityMap);
        }

        /// <summary>
        /// Records each filter's "Enable Filter" tick (Revit 2021 and later). The reorder has to remove
        /// and re-add every filter, and Revit re-adds a filter enabled, so a filter the user had switched
        /// off would come back switched on. Revit 2020 has no such setting, so nothing is recorded there.
        /// </summary>
        private static Dictionary<ElementId, bool> CaptureFilterEnabledStates(View view, List<ElementId> liveClean)
        {
            var enabledMap = new Dictionary<ElementId, bool>(new ElementIdIntegerComparer());
#if REVIT2021_OR_GREATER
            foreach (var id in liveClean)
                enabledMap[id] = view.GetIsFilterEnabled(id);
#endif
            return enabledMap;
        }

        /// <summary>
        /// Puts back the "Enable Filter" ticks recorded by <see cref="CaptureFilterEnabledStates"/> once
        /// every filter has been re-added (Revit 2021 and later; nothing to do on 2020).
        /// </summary>
        private static void RestoreFilterEnabledStates(View view, Dictionary<ElementId, bool> enabledMap)
        {
#if REVIT2021_OR_GREATER
            foreach (var entry in enabledMap)
            {
                if (view.IsFilterApplied(entry.Key) && view.GetIsFilterEnabled(entry.Key) != entry.Value)
                    view.SetIsFilterEnabled(entry.Key, entry.Value);
            }
#endif
        }

        private static void RemoveAllFilters(View view, List<ElementId> liveClean)
        {
            foreach (var id in liveClean)
                view.RemoveFilter(id);
        }

        private static void ReapplyFilters(Document doc,
                                           View view,
                                           List<ElementId> desiredOrder,
                                           List<ElementId> newFilters,
                                           FilterSelection selection,
                                           ElementId solidFillId,
                                           Dictionary<ElementId, OverrideGraphicSettings> overridesMap,
                                           Dictionary<ElementId, bool> visibilityMap,
                                           IList<string> skipped)
        {
            var newSet = new HashSet<long>(newFilters.Select(x => x.LongValue()));
            var appliedIds = new HashSet<long>(
                (view.GetFilters() ?? new List<ElementId>()).Select(x => x.LongValue()));

            foreach (var id in desiredOrder)
            {
                if (doc.GetElement(id) == null)
                    continue;

                if (!appliedIds.Contains(id.LongValue()))
                {
                    view.AddFilter(id);
                    appliedIds.Add(id.LongValue());
                }

                // A filter that was on the view before this run gets back exactly the overrides and
                // visibility it had - including one this run touched (Shuffle / Apply on an existing
                // filter). Re-adding a filter gives it blank overrides, so without this its line weights,
                // patterns and transparency were wiped and a hidden filter came back visible.
                bool wasOnView = visibilityMap.ContainsKey(id);

                if (overridesMap.TryGetValue(id, out var ogs))
                    view.SetFilterOverrides(id, ogs);

                if (visibilityMap.TryGetValue(id, out bool vis))
                    view.SetFilterVisibility(id, vis);

                if (newSet.Contains(id.LongValue()))
                {
                    // This run's filters: new colours on top of what they had (ApplyGraphicsToFilter starts
                    // from the current overrides). Only a filter new to this view is switched visible.
                    FilterApplier.ApplyGraphicsToFilter(doc, view, id, selection, solidFillId, skipped);
                    if (!wasOnView)
                        view.SetFilterVisibility(id, true);
                }
            }
        }
    }
}
