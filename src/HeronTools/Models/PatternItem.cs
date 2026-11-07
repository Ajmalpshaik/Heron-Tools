// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Immutable wrapper pairing a FillPatternElement ElementId with its display name for binding to the pattern selector in the Filter Pro Apply tab.

using Autodesk.Revit.DB;

namespace HeronTools.Models
{
    internal class PatternItem
    {
        public PatternItem(ElementId id, string name)
        {
            Id = id;
            Name = name;
        }

        public ElementId Id { get; }
        public string Name { get; }

        public override string ToString() => Name;
    }
}
