// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Finds the elements in a given view that match a set of categories and (optionally) parameter filter rules, right now - the read-only counterpart to what Filter Pro wraps in a saved ParameterFilterElement.

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace HeronTools.Services.Colorize
{
    /// <summary>
    /// Runs category + parameter-rule matching live against a target view (no saved filter).
    /// </summary>
    internal static class ColorizeElementMatcher
    {
        internal static ICollection<ElementId> GetMatchingElementIds(
            Document doc,
            View view,
            IList<ElementId> categoryIds,
            IList<FilterRule> rules)
        {
            if (doc == null || view == null || categoryIds == null || categoryIds.Count == 0)
            {
                return new List<ElementId>();
            }

            var categoryFilter = new ElementMulticategoryFilter(categoryIds);
            FilteredElementCollector collector = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .WherePasses(categoryFilter);

            if (rules != null && rules.Count > 0)
            {
                var elementFilter = new ElementParameterFilter(rules);
                collector = collector.WherePasses(elementFilter);
            }

            return collector.ToElementIds();
        }
    }
}
