// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.1.1
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Version-safe project display rounding (Project Units accuracy) for decimal parameters, Revit 2020 -> 2027.

using System;
using Autodesk.Revit.DB;

namespace HeronTools.Utils
{
    /// <summary>
    /// How Revit rounds one decimal parameter for display, read from the project's Project Units
    /// (unit + rounding). Used to turn a shown value such as "2700" back into the whole band of
    /// internal values that show that text (2699.5 to 2700.5 mm), so a filter on "2700" catches every
    /// element that shows 2700 - also those the value list never scanned.
    /// </summary>
    /// <remarks>
    /// Revit 2020 describes units with DisplayUnitType / UnitType; Revit 2021 and later with ForgeTypeId
    /// (spec + unit). That version split lives only here.
    /// </remarks>
    internal sealed class DisplayRounding
    {
        private readonly double _accuracy;   // in display units, as FormatOptions.Accuracy gives it

#if REVIT2021_OR_GREATER
        private readonly ForgeTypeId _unit;

        private DisplayRounding(ForgeTypeId unit, double accuracy)
        {
            _unit = unit;
            _accuracy = accuracy;
        }

        private double ToInternal(double display) => UnitUtils.ConvertToInternalUnits(display, _unit);
        private double FromInternal(double value) => UnitUtils.ConvertFromInternalUnits(value, _unit);
#else
        private readonly DisplayUnitType _unit;

        private DisplayRounding(DisplayUnitType unit, double accuracy)
        {
            _unit = unit;
            _accuracy = accuracy;
        }

        private double ToInternal(double display) => UnitUtils.ConvertToInternalUnits(display, _unit);
        private double FromInternal(double value) => UnitUtils.ConvertFromInternalUnits(value, _unit);
#endif

        /// <summary>
        /// The project's display rounding for this parameter's kind of value (length, flow...), or null
        /// when Revit does not say (no unit, a special built-in type): the caller then keeps the spread
        /// of the values it saw.
        /// </summary>
        internal static DisplayRounding For(Document doc, Definition definition)
        {
            if (doc == null || definition == null)
                return null;

            try
            {
                Units units = doc.GetUnits();
#if REVIT2022_OR_GREATER
                ForgeTypeId spec = definition.GetDataType();
                if (spec == null || spec.Empty() || !UnitUtils.IsMeasurableSpec(spec))
                    return null;
                FormatOptions options = units.GetFormatOptions(spec);
#elif REVIT2021
                ForgeTypeId spec = definition.GetSpecTypeId();
                if (spec == null || spec.Empty() || !UnitUtils.IsSpec(spec))
                    return null;
                FormatOptions options = units.GetFormatOptions(spec);
#else
                UnitType unitType = definition.UnitType;
                if (!UnitUtils.IsValidUnitType(unitType))
                    return null;
                FormatOptions options = units.GetFormatOptions(unitType);
#endif
                // The project's own options never "use default"; if they ever did, Unit/Accuracy would throw.
                if (options == null || options.UseDefault)
                    return null;

                double accuracy = options.Accuracy;
                if (!(accuracy > 0) || double.IsInfinity(accuracy))
                    return null;

#if REVIT2021_OR_GREATER
                ForgeTypeId unit = options.GetUnitTypeId();
                if (unit == null || unit.Empty())
                    return null;
                return new DisplayRounding(unit, accuracy);
#else
                return new DisplayRounding(options.DisplayUnits, accuracy);
#endif
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Not a measurable spec / unit type for this parameter (the API's documented refusal).
                return null;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // FormatOptions with UseDefault set refuses Accuracy and the unit.
                return null;
            }
        }

        /// <summary>
        /// The lowest and highest internal value that Revit shows with the same text as
        /// <paramref name="value"/> (half a rounding step either side of the shown number).
        /// </summary>
        internal bool TryGetBand(double value, out double lower, out double upper)
        {
            lower = upper = value;
            if (double.IsNaN(value) || double.IsInfinity(value))
                return false;

            // Revit rounds the display value to the nearest multiple of the accuracy (FormatOptions.Accuracy).
            double shown = Math.Round(FromInternal(value) / _accuracy, MidpointRounding.AwayFromZero) * _accuracy;

            // Each edge is converted on its own, so a unit with an offset (degrees C to kelvin) stays right.
            double a = ToInternal(shown - _accuracy / 2.0);
            double b = ToInternal(shown + _accuracy / 2.0);
            if (double.IsNaN(a) || double.IsNaN(b) || double.IsInfinity(a) || double.IsInfinity(b))
                return false;

            lower = Math.Min(a, b);
            upper = Math.Max(a, b);
            return true;
        }
    }
}
