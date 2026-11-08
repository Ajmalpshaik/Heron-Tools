// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Stores last-used annotation crop settings for the active Revit session.

using Autodesk.Revit.DB;
using HeronTools.Models.ViewCrop;

namespace HeronTools.Services.ViewCrop
{
    /// <summary>
    /// Stores annotation crop settings in-memory for the active document during the session.
    /// </summary>
    internal sealed class ViewCropAnnotationSettingsTracker : SettingsTrackerBase<ViewCropAnnotationSettings>
    {
        internal ViewCropAnnotationSettingsTracker(Document doc) : base(doc)
        {
        }

        protected override ViewCropAnnotationSettings CreateDefault() => new ViewCropAnnotationSettings();

        protected override ViewCropAnnotationSettings CloneSettings(ViewCropAnnotationSettings settings) =>
            (settings ?? CreateDefault()).Clone();
    }
}
