// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Immutable wrapper pairing an ElementId with a parameter name and StorageType for binding to the Filter Pro parameter list.

using Autodesk.Revit.DB;

namespace HeronTools.Models
{
    internal class FilterParameterItem
    {
        public FilterParameterItem(ElementId id, string name, StorageType storageType)
        {
            Id = id;
            Name = name;
            StorageType = storageType;
        }

        public ElementId Id { get; }
        public string Name { get; }
        public StorageType StorageType { get; }

        public override string ToString() => Name;
    }
}
