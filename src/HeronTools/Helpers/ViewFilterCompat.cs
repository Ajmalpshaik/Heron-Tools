// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.1.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Version-safe reading and writing of a view's filter list. Revit 2021 added View.GetOrderedFilters (the
// order the modeller sees in Visibility/Graphics) and the per-filter "Enable Filter" tick; Revit 2020 has
// neither, so there the filters can only be listed and every filter is always on.

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace HeronTools.Utils
{
    internal static class ViewFilterCompat
    {
        /// <summary>
        /// True when Revit itself reports the filters' order (Revit 2021 and later). False in 2020, where
        /// <see cref="GetFilters"/> does not promise the order the modeller sees.
        /// </summary>
        internal static bool ReportsFilterOrder
        {
            get
            {
#if REVIT2021_OR_GREATER
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>The view's filters: in Visibility/Graphics order from 2021, an unordered list in 2020.</summary>
        internal static ICollection<ElementId> GetFilters(View view)
        {
#if REVIT2021_OR_GREATER
            return view.GetOrderedFilters();
#else
            return view.GetFilters();
#endif
        }

        /// <summary>
        /// Reads a filter's "Enable Filter" tick. False in Revit 2020, which has no such setting
        /// (every filter is always on there), so the caller has nothing to record.
        /// </summary>
        internal static bool TryGetIsFilterEnabled(View view, ElementId filterId, out bool enabled)
        {
#if REVIT2021_OR_GREATER
            enabled = view.GetIsFilterEnabled(filterId);
            return true;
#else
            enabled = true;
            return false;
#endif
        }

        /// <summary>
        /// Puts a filter's "Enable Filter" tick back when the filter is on the view and the tick differs.
        /// Nothing to do in Revit 2020, which has no such setting.
        /// </summary>
        internal static void RestoreIsFilterEnabled(View view, ElementId filterId, bool enabled)
        {
#if REVIT2021_OR_GREATER
            if (view.IsFilterApplied(filterId) && view.GetIsFilterEnabled(filterId) != enabled)
                view.SetIsFilterEnabled(filterId, enabled);
#endif
        }
    }
}
