// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.3.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Ribbon availability for the Auto Dimension buttons: on in plans, sections, elevations and callouts of a project.

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HeronTools.Commands
{
    /// <summary>Enables the Auto Dimension buttons only in views where grids or levels can be dimensioned.</summary>
    public class CmdAutoDimensionAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            try
            {
                UIDocument uiDoc = applicationData?.ActiveUIDocument;
                if (uiDoc?.Document == null || uiDoc.Document.IsFamilyDocument)
                    return false;

                View view = uiDoc.ActiveView;
                if (view == null || !view.IsValidObject || view.IsTemplate)
                    return false;

                switch (view.ViewType)
                {
                    case ViewType.FloorPlan:
                    case ViewType.CeilingPlan:
                    case ViewType.EngineeringPlan:
                    case ViewType.AreaPlan:
                    case ViewType.Section:
                    case ViewType.Elevation:
                    case ViewType.Detail:
                        return true;
                    default:
                        return false;
                }
            }
            catch (System.Exception)
            {
                // Any unexpected issue keeps the button disabled rather than crashing the ribbon.
                return false;
            }
        }
    }
}
