// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Stores crop margin and element-inclusion options for View Crop.

using HeronTools.Utils;

namespace HeronTools.Models.ViewCrop
{
    /// <summary>
    /// Contains configurable options for the View Crop tools.
    /// </summary>
    internal sealed class ViewCropSettings
    {
        internal const double DefaultMarginMm = 300.0;

        internal double MarginMm { get; set; } = DefaultMarginMm;

        internal bool IncludeRevitLinks { get; set; } = true;

        internal bool IgnoreHiddenCategories { get; set; } = true;

        internal bool RectangularCropOnly { get; set; } = true;

        internal bool IncludeDatums { get; set; } = false;

        internal bool ApplyAnnotationCrop { get; set; } = false;

        internal double AnnotationOffsetMm { get; set; } = 25.0;   // on paper (Revit default 1")

        internal bool ShowDiagnostics { get; set; } = false;

        internal bool IncludeCoordinationModels { get; set; } = false;

        internal ViewCropExtentSource ExtentSource { get; set; } = ViewCropExtentSource.AllModelElements;

        /// <summary>"Active view" (true, the default) or "Pick several views" (false) - remembered for next time.</summary>
        internal bool ApplyToActiveViewOnly { get; set; } = true;

        internal double MarginInternal => MarginMm * Constants.MM_TO_FEET;

        internal ViewCropSettings Clone()
        {
            return new ViewCropSettings
            {
                MarginMm = MarginMm,
                IncludeRevitLinks = IncludeRevitLinks,
                IgnoreHiddenCategories = IgnoreHiddenCategories,
                RectangularCropOnly = RectangularCropOnly,
                IncludeDatums = IncludeDatums,
                ApplyAnnotationCrop = ApplyAnnotationCrop,
                AnnotationOffsetMm = AnnotationOffsetMm,
                ShowDiagnostics = ShowDiagnostics,
                IncludeCoordinationModels = IncludeCoordinationModels,
                ExtentSource = ExtentSource,
                ApplyToActiveViewOnly = ApplyToActiveViewOnly
            };
        }
    }
}
