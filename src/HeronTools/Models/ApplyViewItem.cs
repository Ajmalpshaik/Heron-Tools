// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Immutable wrapper representing a selectable view target in the Filter Pro "Apply" tab, showing the view name and type for multi-view filter application.

using Autodesk.Revit.DB;

namespace HeronTools.Models
{
    internal class ApplyViewItem
    {
        public ApplyViewItem(ElementId id, string name, ViewType type)
        {
            Id = id;
            Name = name;
            ViewType = type;
        }

        public ElementId Id { get; }
        public string Name { get; }
        public ViewType ViewType { get; }

        /// <summary>
        /// Display text used in UI dropdowns/listboxes.
        /// </summary>
        public string Display => $"{Name} ({ViewType})";

        public override string ToString() => Display;
    }
}
