// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Ribbon availability for Quick Clash: enabled only when the active view is a 3D view of a project.

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HeronTools.Commands
{
    /// <summary>Greyed in plans, sections, elevations, sheets, schedules, view templates and families.</summary>
    public class CmdQuickClashAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            try
            {
                UIDocument uiDoc = applicationData?.ActiveUIDocument;
                if (uiDoc?.Document == null || uiDoc.Document.IsFamilyDocument)
                    return false;
                return uiDoc.ActiveView is View3D view && view.IsValidObject && !view.IsTemplate;
            }
            catch
            {
                // Any unexpected issue keeps the button disabled rather than crashing the ribbon.
                return false;
            }
        }
    }
}
