// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.1.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Version-safe pieces of View Crop's extents measuring. Revit 2021 added the InternalOrigin element; from
// Revit 2024 View Crop asks Revit which LINKED elements a view shows. Older years simply skip each step.

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace HeronTools.Utils
{
    internal static class ViewCropCompat
    {
        /// <summary>True for the Internal Origin element (Revit 2021 and later; it does not exist in 2020).</summary>
        internal static bool IsInternalOrigin(Element element)
        {
#if REVIT2021_OR_GREATER
            return element is InternalOrigin;
#else
            return false;
#endif
        }

        /// <summary>
        /// Revit 2024 and later: for each loaded link among <paramref name="elements"/>, records the link's
        /// elements this view shows, keyed by the link instance's id. Earlier years record nothing.
        /// Call it while the crop is switched off, so the answer is not cut down to the current crop.
        /// </summary>
        internal static void CollectLinkElementsInView(Document doc, View view, IEnumerable<Element> elements,
                                                       IDictionary<long, List<Element>> byLinkId)
        {
#if REVIT2024_OR_GREATER
            foreach (RevitLinkInstance link in elements.OfType<RevitLinkInstance>())
            {
                if (link.GetLinkDocument() == null) continue;
                byLinkId[link.Id.LongValue()] =
                    new FilteredElementCollector(doc, view.Id, link.Id).WhereElementIsNotElementType().ToElements().ToList();
            }
#endif
        }
    }
}
