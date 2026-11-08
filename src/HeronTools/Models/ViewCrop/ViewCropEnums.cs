// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Defines View Crop source and result state enumerations.

namespace HeronTools.Models.ViewCrop
{
    /// <summary>
    /// Defines the element source used to calculate crop extents.
    /// </summary>
    internal enum ViewCropExtentSource
    {
        ActiveViewElements = 0,
        AllModelElements = 1
    }

    /// <summary>
    /// Defines the result state for a processed view.
    /// </summary>
    internal enum ViewCropResultState
    {
        Updated = 0,
        Skipped = 1,
        Failed = 2
    }
}
