// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Version-safe access to Revit ElementId numeric value across Revit 2020 -> latest.

using System;
using Autodesk.Revit.DB;

namespace HeronTools.Utils
{
    /// <summary>
    /// Provides version-safe access to ElementId numeric value across Revit 2020 -> latest.
    /// </summary>
    internal static class ElementIdHelper
    {
        /// <summary>
        /// The numeric value of an ElementId as an int, for the few places that need one (built-in category
        /// and parameter ids are small negative numbers). Returns the InvalidElementId value when
        /// <paramref name="id"/> is null.
        /// </summary>
        /// <remarks>
        /// From Revit 2024 an id is 64-bit. This used to cut a large id down to 32 bits without a word, so two
        /// different elements could look the same. It now refuses: an id that does not fit throws
        /// <see cref="OverflowException"/> instead of returning a wrong number. Compare ids with
        /// <c>==</c> or <see cref="ElementIdExtensions.LongValue"/>; test categories with
        /// <see cref="IsCategory"/> / <see cref="TryGetBuiltInCategory"/>.
        /// </remarks>
        internal static int GetIntegerValue(ElementId id)
        {
            if (id == null)
                return GetIntegerValue(ElementId.InvalidElementId);
#if REVIT2024_OR_GREATER
            return checked((int)id.Value);
#else
            return id.IntegerValue;
#endif
        }

        /// <summary>
        /// True when <paramref name="categoryId"/> is the id of <paramref name="builtIn"/>. Compares the full
        /// 64-bit value, so a large id (Revit 2024+) can never be cut down and mistaken for a built-in category.
        /// </summary>
        internal static bool IsCategory(ElementId categoryId, BuiltInCategory builtIn)
        {
            return categoryId != null && categoryId.LongValue() == (long)builtIn;
        }

        /// <summary>
        /// The built-in category an id stands for. False for null, InvalidElementId, and any id that is not a
        /// built-in category (built-in ids are negative and fit in 32 bits; the full value is checked first).
        /// </summary>
        internal static bool TryGetBuiltInCategory(ElementId categoryId, out BuiltInCategory builtIn)
        {
            builtIn = BuiltInCategory.INVALID;
            if (categoryId == null)
                return false;
            long value = categoryId.LongValue();
            if (value >= -1 || value < int.MinValue)
                return false;
            builtIn = (BuiltInCategory)value;
            return true;
        }

        /// <summary>
        /// Returns a printable string representation of an ElementId numeric value
        /// suitable for diagnostic reports.
        /// </summary>
        internal static string ToReportString(ElementId id)
        {
            if (id == null)
                return "(none)";

            // The full 64-bit value: from Revit 2024 an id can be larger than an int holds.
            return id.LongValue().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// True if the ElementId is non-null and not equal to <see cref="ElementId.InvalidElementId"/>.
        /// </summary>
        internal static bool IsValid(ElementId id)
        {
            return id != null && id != ElementId.InvalidElementId;
        }

        /// <summary>
        /// True if <paramref name="parameterId"/> matches a defined BuiltInParameter value, version-safe.
        /// Replaces the unsafe <c>Enum.IsDefined(typeof(BuiltInParameter), someInt)</c>, which throws in
        /// Revit 2024+ because BuiltInParameter's underlying type widened from Int32 to Int64.
        /// </summary>
        internal static bool IsDefinedBuiltInParameter(int parameterId)
        {
            return IsDefinedEnumValue(typeof(BuiltInParameter), parameterId);
        }

        /// <summary>
        /// True if <paramref name="id"/> is a built-in parameter, with that parameter in <paramref name="builtIn"/>.
        /// Built-in parameter ids are negative and fit in 32 bits; the full 64-bit value is checked first, so a
        /// large project-parameter id (Revit 2024+) can never be cut down and mistaken for a built-in one.
        /// </summary>
        internal static bool TryGetBuiltInParameter(ElementId id, out BuiltInParameter builtIn)
        {
            builtIn = BuiltInParameter.INVALID;
            if (id == null)
                return false;

            long value = id.LongValue();
            // -1 is InvalidElementId (and BuiltInParameter.INVALID) - never a real parameter.
            if (value >= -1 || value < int.MinValue)
                return false;

            if (!IsDefinedBuiltInParameter((int)value))
                return false;

            builtIn = (BuiltInParameter)(int)value;
            return true;
        }

        private static bool IsDefinedEnumValue(Type enumType, int value)
        {
            Type underlyingType = Enum.GetUnderlyingType(enumType);
            object boxedValue = Convert.ChangeType(value, underlyingType);
            return Enum.IsDefined(enumType, boxedValue);
        }

        /// <summary>
        /// The ElementId for a value saved with LongValue(). Before Revit 2024 ids are 32-bit, so a value
        /// saved there always fits an int.
        /// </summary>
        internal static ElementId FromLong(long value)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(value);
#else
            return new ElementId((int)value);
#endif
        }

        /// <summary>
        /// Version-safe replacement for <c>new ElementId(int)</c>, which no longer exists on Revit
        /// 2027 (confirmed against the real installed API). Never construct an ElementId from a plain
        /// int directly at a call site - always go through this method.
        /// </summary>
        internal static ElementId FromInt(int value)
        {
#if REVIT2024_OR_GREATER
            return new ElementId((long)value);
#else
            return new ElementId(value);
#endif
        }
    }
}
