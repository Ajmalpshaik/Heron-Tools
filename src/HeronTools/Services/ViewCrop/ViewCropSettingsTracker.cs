// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Stores last-used View Crop settings for the active document and persists them on disk.

using Autodesk.Revit.DB;
using HeronTools.Models.ViewCrop;
using HeronTools.Utils;

namespace HeronTools.Services.ViewCrop
{
    /// <summary>
    /// Stores user settings in-memory during the active document session and persists them on disk.
    /// </summary>
    internal sealed class ViewCropSettingsTracker : SettingsTrackerBase<ViewCropSettings>
    {
        internal ViewCropSettingsTracker(Document doc) : base(doc)
        {
        }

        protected override ViewCropSettings CreateDefault() => new ViewCropSettings();

        protected override ViewCropSettings CloneSettings(ViewCropSettings settings) =>
            (settings ?? CreateDefault()).Clone();

        protected override ViewCropSettings LoadFromStore() => ViewCropConfigStore.Load();

        protected override bool SaveToStore(ViewCropSettings settings, out string errorMessage) =>
            ViewCropConfigStore.Save(settings, out errorMessage);
    }
}
