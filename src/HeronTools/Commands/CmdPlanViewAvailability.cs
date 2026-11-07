// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Ribbon availability for the View Crop tools: enabled in plan views.

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HeronTools.Commands
{
    /// <summary>
    /// Enables a ribbon button only when the active view is a plan-family view.
    /// </summary>
    public class CmdPlanViewAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            try
            {
                UIDocument uiDoc = applicationData?.ActiveUIDocument;
                if (uiDoc?.Document == null)
                    return false;

                if (uiDoc.Document.IsFamilyDocument)
                    return false;

                return IsPlanFamilyView(uiDoc.ActiveView);
            }
            catch
            {
                // Any unexpected issue keeps the button disabled rather than crashing the ribbon.
                return false;
            }
        }

        /// <summary>
        /// True for Floor Plan, Ceiling Plan, Area Plan, and Structural (Engineering) Plan views.
        /// </summary>
        internal static bool IsPlanFamilyView(View view)
        {
            if (view == null || !view.IsValidObject || view.IsTemplate)
                return false;

            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.AreaPlan:
                case ViewType.EngineeringPlan:
                    return true;
                default:
                    return false;
            }
        }
    }
}
