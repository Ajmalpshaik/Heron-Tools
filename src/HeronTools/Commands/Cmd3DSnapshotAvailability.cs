// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Ribbon availability for 3D Snapshot: enabled in plans, ceiling plans, structural and area plans, sections,
// detail sections and elevations of a project - the views it can measure a box from.

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Services.Snapshot3D;

namespace HeronTools.Commands
{
    /// <summary>Enables 3D Snapshot only where it can work; greyed in 3D views, sheets, schedules and families.</summary>
    public class Cmd3DSnapshotAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            try
            {
                UIDocument uiDoc = applicationData?.ActiveUIDocument;
                if (uiDoc?.Document == null || uiDoc.Document.IsFamilyDocument)
                    return false;

                View view = uiDoc.ActiveView;
                if (view == null || !view.IsValidObject)
                    return false;

                return Snapshot3DService.IsPlan(view) || Snapshot3DService.IsSectionLike(view);
            }
            catch
            {
                // Any unexpected issue keeps the button disabled rather than crashing the ribbon.
                return false;
            }
        }
    }
}
