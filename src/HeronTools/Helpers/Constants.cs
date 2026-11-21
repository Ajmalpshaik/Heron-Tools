// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Centralized numeric constants shared across Heron Tools — unit conversions, geometry tolerances, and scan limits.

namespace HeronTools.Utils
{
    /// <summary>
    /// Provides centralized constant values used throughout the application.
    /// </summary>
    internal static class Constants
    {
        /// <summary>
        /// Conversion factor from millimeters to feet (Revit internal units): exactly 1 / 304.8, so mm -> feet -> mm
        /// gives back the same number (issue #40; it used to be the rounded 0.00328084).
        /// </summary>
        public const double MM_TO_FEET = 1.0 / FEET_TO_MM;

        /// <summary>
        /// Conversion factor from feet (Revit internal units) to millimeters.
        /// </summary>
        public const double FEET_TO_MM = 304.8;

        /// <summary>
        /// Minimum distance (feet) below which two points or a span are treated as zero (Level Extents).
        /// </summary>
        public const double MIN_DISTANCE_TOLERANCE = 0.001;

        /// <summary>
        /// Maximum number of elements to scan when collecting parameter values.
        /// Prevents performance issues in large models.
        /// </summary>
        public const int ELEMENT_SCAN_LIMIT = 10000;


        /// <summary>
        /// Very small value used for near-zero length checks.
        /// </summary>
        public const double ZERO_LENGTH_TOLERANCE = 1e-9;
    }
}
