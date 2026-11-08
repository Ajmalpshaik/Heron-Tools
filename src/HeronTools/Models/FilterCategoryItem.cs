// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Immutable wrapper pairing an ElementId with a category display name for binding to the Filter Pro category list.

using Autodesk.Revit.DB;

namespace HeronTools.Models
{
    internal class FilterCategoryItem
    {
        public FilterCategoryItem(ElementId id, string name)
        {
            Id = id;
            Name = name;
        }

        public ElementId Id { get; }
        public string Name { get; }

        public override string ToString() => Name;
    }
}
