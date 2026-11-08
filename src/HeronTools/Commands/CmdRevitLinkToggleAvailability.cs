// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Ribbon availability filter — enables the Toggle Link button only in views where the Revit Links category visibility can actually be changed.

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HeronTools.Commands
{
    /// <summary>
    /// Enables the Toggle Link button only when the Revit Links category can be hidden in the active view.
    /// </summary>
    public class CmdRevitLinkToggleAvailability : IExternalCommandAvailability
    {
        private static readonly ElementId RevitLinksCategoryId = new ElementId(BuiltInCategory.OST_RvtLinks);

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

                return view.CanCategoryBeHidden(RevitLinksCategoryId);
            }
            catch
            {
                // Any unexpected issue keeps the button disabled rather than crashing the ribbon.
                return false;
            }
        }
    }
}
