// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Validates supported View Crop target view types and provides friendly type labels.

using Autodesk.Revit.DB;

namespace HeronTools.Services.ViewCrop
{
    /// <summary>
    /// Centralizes support checks for views targeted by View Crop tools.
    /// </summary>
    internal static class ViewCropViewSupport
    {
        internal static bool IsSupportedViewType(ViewType viewType)
        {
            return viewType == ViewType.FloorPlan
                || viewType == ViewType.CeilingPlan
                || viewType == ViewType.EngineeringPlan
                || viewType == ViewType.AreaPlan;
        }

        internal static bool TryValidateType(View view, out string reason)
        {
            if (view == null)
            {
                reason = "View not found.";
                return false;
            }

            if (view.IsTemplate)
            {
                reason = "View templates are not supported.";
                return false;
            }

            if (!IsSupportedViewType(view.ViewType))
            {
                reason = $"Unsupported view type: {view.ViewType}.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        internal static string ToFriendlyTypeName(View view)
        {
            if (view == null)
                return string.Empty;

            return ToFriendlyTypeName(view.ViewType);
        }

        internal static string ToFriendlyTypeName(ViewType viewType)
        {
            switch (viewType)
            {
                case ViewType.FloorPlan:
                    return "Floor Plan";
                case ViewType.CeilingPlan:
                    return "Ceiling Plan";
                case ViewType.EngineeringPlan:
                    return "Engineering Plan";
                case ViewType.AreaPlan:
                    return "Area Plan";
                case ViewType.Section:
                    return "Section";
                case ViewType.Elevation:
                    return "Elevation";
                case ViewType.Detail:
                    return "Detail/Callout";
                case ViewType.ThreeD:
                    return "3D View";
                case ViewType.DrawingSheet:
                    return "Sheet";
                case ViewType.Legend:
                    return "Legend";
                case ViewType.Schedule:
                    return "Schedule";
                case ViewType.DraftingView:
                    return "Drafting View";
                default:
                    return viewType.ToString();
            }
        }
    }
}
