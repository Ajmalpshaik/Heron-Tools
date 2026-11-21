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
        /// Conversion factor from feet (Revit internal units) to millimeters. Exact: one foot is 304.8 mm by definition.
        /// </summary>
        public const double FEET_TO_MM = 304.8;

        /// <summary>
        /// Conversion factor from millimeters to feet (Revit internal units). Exactly 1 / 304.8 - it used to be the
        /// rounded 0.00328084, which drifted about 0.3 mm in every 10 km and did not round-trip with FEET_TO_MM.
        /// </summary>
        public const double MM_TO_FEET = 1.0 / FEET_TO_MM;

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
