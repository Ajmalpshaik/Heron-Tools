// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Composite key that captures a parameter value's identity (by storage type) for persisting and restoring Filter Pro selections across window sessions.

using Autodesk.Revit.DB;

using HeronTools.Utils;
namespace HeronTools.Models
{
    internal class FilterValueKey
    {
        public StorageType StorageType { get; private set; }
        public string StringValue { get; private set; }
        public int? IntValue { get; private set; }
        public double? DoubleValue { get; private set; }
        public long? ElementIdValue { get; private set; }

        private FilterValueKey() { }

        public static FilterValueKey ForString(string value)
        {
            return new FilterValueKey
            {
                StorageType = StorageType.String,
                StringValue = value
            };
        }

        public static FilterValueKey ForInt(int value)
        {
            return new FilterValueKey
            {
                StorageType = StorageType.Integer,
                IntValue = value
            };
        }

        public static FilterValueKey ForDouble(double value)
        {
            return new FilterValueKey
            {
                StorageType = StorageType.Double,
                DoubleValue = value
            };
        }

        public static FilterValueKey ForElementId(ElementId id)
        {
            return new FilterValueKey
            {
                StorageType = StorageType.ElementId,
                ElementIdValue = id?.LongValue()
            };
        }
    }
}
