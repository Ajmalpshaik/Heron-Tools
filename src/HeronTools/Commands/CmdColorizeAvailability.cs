// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Controls ribbon button availability - enabled only in the same views where Filter Pro can operate (Plan, Section, Elevation, 3D, with overrides allowed).

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HeronTools.Commands
{
    /// <summary>
    /// Enables the Colorize ribbon button only when the active view supports graphics overrides
    /// the same way Filter Pro requires (Plan, Section, Elevation, 3D).
    /// </summary>
    public class CmdColorizeAvailability : IExternalCommandAvailability
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

                View activeView = uiDoc.ActiveView;
                if (activeView == null)
                    return false;

                return CmdFilterProAvailability.CanViewHaveFilters(activeView, out _);
            }
            catch
            {
                // Any unexpected issue keeps the button disabled rather than crashing the ribbon.
                return false;
            }
        }
    }
}
