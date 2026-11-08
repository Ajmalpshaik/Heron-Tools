// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Stores annotation crop offset (mm) used by the annotation crop tool.

using HeronTools.Utils;

namespace HeronTools.Models.ViewCrop
{
    /// <summary>
    /// Contains configurable options for the Annotation Crop tool.
    /// </summary>
    internal sealed class ViewCropAnnotationSettings
    {
        // The annotation crop offset is a PAPER distance (Revit API: "a non-negative length in view units",
        // default 1", minimum 1/8"). The old default of 100 mm was treated as a model distance and at 1:100
        // put the annotation crop 10 m outside the crop. 25 mm on paper is Revit's own default (1").
        internal const double DefaultOffsetMm = 25.0;
        internal const double MinimumOffsetMm = 3.175; // Revit's minimum, 1/96 ft (1/8")

        internal double OffsetMm { get; set; } = DefaultOffsetMm;

        /// <summary>"Active view" (true, the default) or "Pick several views" (false) - remembered for next time.</summary>
        internal bool ApplyToActiveViewOnly { get; set; } = true;

        /// <summary>The offset in feet ON PAPER, never below Revit's minimum.</summary>
        internal double OffsetInternal => System.Math.Max(OffsetMm * Constants.MM_TO_FEET, 1.0 / 96.0);

        internal ViewCropAnnotationSettings Clone()
        {
            return new ViewCropAnnotationSettings
            {
                OffsetMm = OffsetMm,
                ApplyToActiveViewOnly = ApplyToActiveViewOnly
            };
        }
    }
}
