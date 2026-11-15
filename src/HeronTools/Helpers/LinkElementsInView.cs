// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The elements of one placed link that a host 3D view shows. Revit 2023 added a collector for exactly this;
// before 2023 there is none, so the link's elements inside the view's section box are taken instead.

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace HeronTools.Utils
{
    /// <summary>Version-safe "elements of this link visible in this host view".</summary>
    internal static class LinkElementsInView
    {
        /// <summary>
        /// True when the answer follows the view's real visibility (Revit 2023 and later). False in 2020-2022,
        /// where elements hidden inside the link (V/G, hidden in view) are still returned if inside the section box.
        /// </summary>
        internal static bool IsExact
        {
            get
            {
#if REVIT2023_OR_GREATER
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// The link's model elements (not types, not view-specific) shown in <paramref name="view"/>. Empty when the
        /// link is not loaded.
        /// </summary>
        internal static IList<Element> Collect(Document hostDoc, View3D view, RevitLinkInstance link)
        {
            Document linkDoc = link?.GetLinkDocument();
            if (linkDoc == null)
                return new List<Element>();

#if REVIT2023_OR_GREATER
            return new FilteredElementCollector(hostDoc, view.Id, link.Id)
                .WhereElementIsNotElementType()
                .Where(e => !e.ViewSpecific)
                .ToList();
#else
            var collector = new FilteredElementCollector(linkDoc).WhereElementIsNotElementType();
            if (view.IsSectionBoxActive)
            {
                Outline outline = SectionBoxInLink(view, link);
                if (outline != null)
                    collector = collector.WherePasses(new BoundingBoxIntersectsFilter(outline));
            }
            return collector.Where(e => !e.ViewSpecific).ToList();
#endif
        }

#if !REVIT2023_OR_GREATER
        /// <summary>The view's section box, moved into the link's own coordinates (an axis-aligned box around it).</summary>
        private static Outline SectionBoxInLink(View3D view, RevitLinkInstance link)
        {
            BoundingBoxXYZ box = view.GetSectionBox();
            if (box == null)
                return null;

            Transform toHost = box.Transform;
            Transform toLink = link.GetTotalTransform().Inverse;
            XYZ min = null, max = null;
            foreach (double x in new[] { box.Min.X, box.Max.X })
            foreach (double y in new[] { box.Min.Y, box.Max.Y })
            foreach (double z in new[] { box.Min.Z, box.Max.Z })
            {
                XYZ p = toLink.OfPoint(toHost.OfPoint(new XYZ(x, y, z)));
                min = min == null ? p : new XYZ(System.Math.Min(min.X, p.X), System.Math.Min(min.Y, p.Y), System.Math.Min(min.Z, p.Z));
                max = max == null ? p : new XYZ(System.Math.Max(max.X, p.X), System.Math.Max(max.Y, p.Y), System.Math.Max(max.Z, p.Z));
            }
            return new Outline(min, max);
        }
#endif
    }
}
