// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Defines sentinel ElementId values for virtual parameters that exist only in the Filter Pro UI (e.g. the combined "Family and Type" parameter).

using Autodesk.Revit.DB;
using HeronTools.Utils;

namespace HeronTools.Models
{
    /// <summary>
    /// Defines special virtual parameter identifiers that do not exist in Revit.
    /// These are used only inside the Filter Pro UI for combined or synthetic parameters,
    /// such as "Family + Type". Values must never collide with built-in or real parameter IDs.
    /// </summary>
    internal static class SpecialParameterIds
    {
        /// <summary>
        /// Sentinel ID used to represent the combined "Family + Type" virtual parameter.
        /// Uses a very low negative number to avoid collision with real ElementIds.
        /// </summary>
        public static readonly ElementId FamilyAndType =
            ElementIdHelper.FromInt(int.MinValue + 100);
    }
}
