// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Ribbon availability filter — enables a button only in views where element visibility and graphics overrides can be edited (AreGraphicsOverridesAllowed). Used by the Graphics panel tools and Unhide All.

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HeronTools.Commands
{
    /// <summary>
    /// Enables a ribbon button only when the active view allows graphics/visibility edits.
    /// </summary>
    public class CmdGraphicalViewAvailability : IExternalCommandAvailability
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

                View view = uiDoc.ActiveView;
                if (view == null || !view.IsValidObject || view.IsTemplate)
                    return false;

                return view.AreGraphicsOverridesAllowed();
            }
            catch
            {
                // Any unexpected issue keeps the button disabled rather than crashing the ribbon.
                return false;
            }
        }
    }
}
