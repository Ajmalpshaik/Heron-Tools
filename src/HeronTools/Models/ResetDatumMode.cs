// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Enumerates which datum types (grids, levels, or both) a Reset Datums run targets.

namespace HeronTools.Models
{
    /// <summary>
    /// Specifies which datum types to reset during Reset Datum operations.
    /// </summary>
    internal enum ResetDatumMode
    {
        /// <summary>
        /// Reset both Grids and Levels.
        /// </summary>
        Combined,

        /// <summary>
        /// Reset only Grid elements.
        /// </summary>
        GridsOnly,

        /// <summary>
        /// Reset only Level elements.
        /// </summary>
        LevelsOnly
    }
}
