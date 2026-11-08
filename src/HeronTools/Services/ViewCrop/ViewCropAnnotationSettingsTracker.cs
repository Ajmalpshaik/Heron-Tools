// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Stores last-used annotation crop settings and keeps them on disk, so the window reopens with them - also
// after Revit restarts (%AppData%\HeronTools\memory-view-crop-annotation.txt). Same pattern as
// ViewCropSettingsTracker: the base class keeps them in memory, LoadFromStore/SaveToStore read and write the file.

using Autodesk.Revit.DB;
using HeronTools.Models.ViewCrop;
using HeronTools.Utils;

namespace HeronTools.Services.ViewCrop
{
    /// <summary>
    /// Stores annotation crop settings in-memory during the active document session and persists them on disk.
    /// </summary>
    internal sealed class ViewCropAnnotationSettingsTracker : SettingsTrackerBase<ViewCropAnnotationSettings>
    {
        private const string MemoryTool = "view-crop-annotation";
        private const string OffsetMmKey = "OffsetMm";
        private const string ActiveViewOnlyKey = "ActiveViewOnly";

        // The same limits the options window accepts - a damaged file must not reach the crop maths.
        private const double MaximumOffsetMm = 1000.0;

        internal ViewCropAnnotationSettingsTracker(Document doc) : base(doc)
        {
        }

        protected override ViewCropAnnotationSettings CreateDefault() => new ViewCropAnnotationSettings();

        protected override ViewCropAnnotationSettings CloneSettings(ViewCropAnnotationSettings settings) =>
            (settings ?? CreateDefault()).Clone();

        /// <summary>The saved settings; anything missing or out of range keeps its default.</summary>
        protected override ViewCropAnnotationSettings LoadFromStore()
        {
            ViewCropAnnotationSettings settings = CreateDefault();
            WindowMemory memory = WindowMemory.Load(MemoryTool);

            double offset = memory.GetDouble(OffsetMmKey, settings.OffsetMm);
            if (offset >= 0 && offset <= MaximumOffsetMm)
                settings.OffsetMm = offset;

            settings.ApplyToActiveViewOnly = memory.GetBool(ActiveViewOnlyKey, settings.ApplyToActiveViewOnly);
            return settings;
        }

        protected override bool SaveToStore(ViewCropAnnotationSettings settings, out string errorMessage)
        {
            WindowMemory memory = WindowMemory.Load(MemoryTool);
            memory.Set(OffsetMmKey, settings.OffsetMm);
            memory.Set(ActiveViewOnlyKey, settings.ApplyToActiveViewOnly);
            bool saved = memory.Save(out string error);
            errorMessage = error ?? string.Empty;
            return saved;
        }
    }
}
