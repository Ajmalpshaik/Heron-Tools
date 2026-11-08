// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// IEqualityComparer<ElementId> that compares by IntegerValue so two ElementId wrapper objects for the same element are treated as equal in collections.

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace HeronTools.Utils
{
    /// <summary>
    /// Comparer that treats ElementIds as equal when their numeric values match (null-safe).
    /// </summary>
    internal class ElementIdIntegerComparer : IEqualityComparer<ElementId>
    {
        public bool Equals(ElementId x, ElementId y)
        {
            if (ReferenceEquals(x, y))
                return true;

            if (x == null || y == null)
                return false;

            // Compare by integer value — the only reliable identity for ElementIds.
            return x.LongValue() == y.LongValue();
        }

        public int GetHashCode(ElementId obj)
        {
            // Hash on IntegerValue to match the Equals contract.
            return obj == null ? 0 : obj.LongValue().GetHashCode();
        }
    }
}
