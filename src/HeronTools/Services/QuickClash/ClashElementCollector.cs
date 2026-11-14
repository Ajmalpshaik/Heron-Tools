// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Quick Clash - which elements and categories a clash test can use: the physical model elements shown in the
// active 3D view (this model), the loaded links placed in it, and the elements of one placed link.

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using HeronTools.Models.QuickClash;
using HeronTools.Utils;

namespace HeronTools.Services.QuickClash
{
    internal static class ClashElementCollector
    {
        /// <summary>
        /// Model categories that are not physical things - testing them would report every duct inside a room.
        /// </summary>
        private static readonly HashSet<long> NotPhysical = new HashSet<long>(new[]
        {
            BuiltInCategory.OST_Rooms, BuiltInCategory.OST_MEPSpaces, BuiltInCategory.OST_Areas,
            BuiltInCategory.OST_HVAC_Zones, BuiltInCategory.OST_RvtLinks, BuiltInCategory.OST_Lines,
            BuiltInCategory.OST_Cameras, BuiltInCategory.OST_SectionBox, BuiltInCategory.OST_PointClouds,
            BuiltInCategory.OST_Levels, BuiltInCategory.OST_Grids, BuiltInCategory.OST_IOSModelGroups,
            BuiltInCategory.OST_Massing,
        }.Select(c => (long)(int)c));

        /// <summary>True for a model category whose elements are real objects with a body.</summary>
        internal static bool IsPhysical(Category category)
        {
            return category != null &&
                   category.CategoryType == CategoryType.Model &&
                   !NotPhysical.Contains(category.Id.LongValue());
        }

        /// <summary>The physical elements of this model shown in the view (section box, hidden elements and V/G respected).</summary>
        internal static List<Element> HostElementsInView(Document doc, View3D view)
        {
            return new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => !e.ViewSpecific && IsPhysical(e.Category))
                .ToList();
        }

        /// <summary>The physical elements of one placed link shown in the view (see <see cref="LinkElementsInView"/>).</summary>
        internal static List<Element> LinkElementsShown(Document doc, View3D view, RevitLinkInstance link)
        {
            return LinkElementsInView.Collect(doc, view, link)
                .Where(e => IsPhysical(e.Category))
                .ToList();
        }

        /// <summary>One list row per category, with its count; ticked when its number is in <paramref name="ticked"/>.</summary>
        internal static List<ClashCategoryItem> CategoryRows(IEnumerable<Element> elements, ICollection<long> ticked)
        {
            return elements
                .GroupBy(e => e.Category.Id.LongValue())
                .Select(g => new ClashCategoryItem(g.Key, g.First().Category.Name, g.Count(), ticked.Contains(g.Key)))
                .OrderBy(r => r.Name)
                .ToList();
        }

        /// <summary>"This model" first, then every loaded link placed in the view. A link placed twice is listed twice.</summary>
        internal static List<ClashSourceItem> Sources(Document doc, View3D view)
        {
            var sources = new List<ClashSourceItem> { new ClashSourceItem(-1, "This model", "") };

            List<RevitLinkInstance> links = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(l => l.GetLinkDocument() != null)
                .ToList();

            foreach (IGrouping<string, RevitLinkInstance> byFile in links.GroupBy(LinkFileName).OrderBy(g => g.Key))
            {
                int copy = 0;
                List<RevitLinkInstance> copies = byFile.OrderBy(l => l.Id.LongValue()).ToList();
                foreach (RevitLinkInstance link in copies)
                {
                    copy++;
                    string display = "Link: " + byFile.Key + (copies.Count > 1 ? "  (copy " + copy + ")" : "");
                    sources.Add(new ClashSourceItem(link.Id.LongValue(), display, byFile.Key + "#" + copy));
                }
            }
            return sources;
        }

        /// <summary>"Category: Family - Type  [id]" - how the results list names an element.</summary>
        internal static string DisplayName(Element element)
        {
            if (element == null)
                return "(deleted)";
            string category = element.Category?.Name ?? "Element";
            string name = element is FamilyInstance fi && fi.Symbol != null
                ? fi.Symbol.FamilyName + " - " + fi.Symbol.Name
                : element.Name;
            return category + ": " + name + "  [" + element.Id.LongValue() + "]";
        }

        private static string LinkFileName(RevitLinkInstance link)
        {
            Element type = link.Document.GetElement(link.GetTypeId());
            return type?.Name ?? link.Name;
        }
    }
}
