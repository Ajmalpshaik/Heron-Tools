// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Applies and clears element-level graphics overrides for the active view.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using HeronTools.Models.GraphicsTools;
using HeronTools.Utils;

namespace HeronTools.Services.GraphicsTools
{
    /// <summary>
    /// Element-focused graphics override operations for the active view.
    /// </summary>
    internal static class GraphicsElementService
    {
        public static GraphicsOperationSummary ApplyOverrides(
            Document doc,
            View view,
            IEnumerable<ElementId> elementIds,
            OverrideGraphicSettings settings)
        {
            var summary = new GraphicsOperationSummary();

            if (doc == null || view == null || elementIds == null || settings == null)
            {
                return summary;
            }

            var processed = new HashSet<long>();

            foreach (ElementId elementId in elementIds)
            {
                if (elementId == null || elementId == ElementId.InvalidElementId)
                {
                    continue;
                }

                long key = elementId.LongValue();
                if (processed.Contains(key))
                {
                    continue;
                }

                processed.Add(key);
                summary.Attempted++;

                Element element = doc.GetElement(elementId);
                if (element == null)
                {
                    summary.NoteSkip("The element no longer exists.");
                    continue;
                }

                try
                {
                    view.SetElementOverrides(elementId, settings);
                    summary.Applied++;
                }
                catch (Exception ex)
                {
                    summary.NoteSkip(ex.Message);
                }
            }

            return summary;
        }

        public static GraphicsOperationSummary ClearOverrides(
            Document doc,
            View view,
            IEnumerable<ElementId> elementIds)
        {
            return ApplyOverrides(doc, view, elementIds, new OverrideGraphicSettings());
        }
    }
}
