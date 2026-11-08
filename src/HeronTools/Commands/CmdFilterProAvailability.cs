// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Controls ribbon button availability — enabled only in filter-capable views.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HeronTools.Commands
{
    /// <summary>
    /// Controls the availability of the Filter Pro command in the Revit UI.
    /// </summary>
    public class CmdFilterProAvailability : IExternalCommandAvailability
    {
        /// <summary>
        /// Returns true if Filter Pro is available in the current document context.
        /// </summary>
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            try
            {
                UIDocument uiDoc = applicationData?.ActiveUIDocument;
                if (uiDoc?.Document == null)
                    return false;

                Document doc = uiDoc.Document;
                if (doc.IsFamilyDocument)
                    return false;

                View activeView = uiDoc.ActiveView;
                if (activeView == null)
                    return false;

                return CanViewHaveFilters(activeView, out _);
            }
            catch
            {
                // If anything unexpected happens, keep the button disabled.
                return false;
            }
        }

        /// <summary>
        /// Checks if the view supports parameter filters.
        /// </summary>
        internal static bool CanViewHaveFilters(View view, out string reason)
        {
            reason = string.Empty;

            if (view == null)
            {
                reason = "There is no active view.";
                return false;
            }

            if (view.IsTemplate)
            {
                reason = "A view template is open. Open a model view instead.";
                return false;
            }

            // The cheap view-type test first: this check runs every time Revit refreshes the ribbon, and the
            // overrides call below can throw on sheets and schedules.
            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.EngineeringPlan:   // structural plan
                case ViewType.AreaPlan:
                case ViewType.Section:
                case ViewType.Detail:            // section callout / detail view
                case ViewType.Elevation:
                case ViewType.ThreeD:
                    break;
                default:
                    reason = "This tool works in plan, ceiling, structural, area, section, detail, elevation and 3D views.";
                    return false;
            }

            bool overridesAllowed;
            try
            {
                overridesAllowed = view.AreGraphicsOverridesAllowed();
            }
            catch (Exception ex) // read-only check; any failure just means "no"
            {
                reason = "Revit could not say whether this view takes graphic overrides: " + ex.Message;
                return false;
            }

            if (!overridesAllowed)
            {
                reason = "This view does not allow graphic overrides.";
                return false;
            }

            return true;
        }
    }
}
