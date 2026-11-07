// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Immutable wrapper holding a parameter value's display string, raw typed value, StorageType, and optional ElementId reference for filter rule creation.

using Autodesk.Revit.DB;

namespace HeronTools.Models
{
    internal class FilterValueItem
    {
        public FilterValueItem(
            string display,
            object rawValue,
            StorageType storageType,
            ElementId elementId = null)
        {
            Display = display;
            RawValue = rawValue;
            StorageType = storageType;
            ElementId = elementId;
        }

        public string Display { get; }
        public object RawValue { get; }
        public StorageType StorageType { get; }
        public ElementId ElementId { get; }

        /// <summary>
        /// Decimal values only: the smallest and largest internal value seen behind this display text.
        /// Several values can show the same text ("1500"); the filter rule covers all of them.
        /// </summary>
        public double? MinDouble { get; private set; }
        public double? MaxDouble { get; private set; }

        /// <summary>Widens <see cref="MinDouble"/>/<see cref="MaxDouble"/> to include one more value.</summary>
        public void IncludeDouble(double value)
        {
            if (!MinDouble.HasValue || value < MinDouble.Value) MinDouble = value;
            if (!MaxDouble.HasValue || value > MaxDouble.Value) MaxDouble = value;
        }

        public override string ToString() => Display;
    }
}
