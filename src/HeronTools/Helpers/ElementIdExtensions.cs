// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Version-safe ElementId numeric-value extension methods across Revit 2020 -> 2027.

using Autodesk.Revit.DB;

namespace HeronTools.Utils
{
    /// <summary>
    /// Version-safe ElementId numeric-value access, replacing direct ElementId.IntegerValue usage
    /// (removed in Revit 2026). Keeps call sites readable: <c>id.LongValue()</c>. There is no int version on purpose: from Revit 2024 an id can be larger than an int holds.
    /// </summary>
    internal static class ElementIdExtensions
    {
        /// <summary>
        /// The full 64-bit ElementId numeric value. Use when an id may exceed 32 bits (Revit 2024+).
        /// Returns the InvalidElementId value when <paramref name="id"/> is null.
        /// </summary>
        internal static long LongValue(this ElementId id)
        {
            if (id == null)
                return LongValue(ElementId.InvalidElementId);

#if REVIT2024_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }
    }
}
