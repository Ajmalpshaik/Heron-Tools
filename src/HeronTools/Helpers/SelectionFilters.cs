// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Reusable selection filters for common Revit element types.

using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace HeronTools.Utils
{
    /// <summary>
    /// Selection filter for Grid and Level datum elements.
    /// </summary>
    internal class DatumSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Grid || elem is Level;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }

    /// <summary>
    /// Selection filter for MEP curve elements (pipes, ducts, cable trays, conduits, flex curves).
    /// </summary>
    internal class MepSelectionFilter : ISelectionFilter
    {
        private readonly HashSet<BuiltInCategory> _categories = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_FlexPipeCurves
        };

        public bool AllowElement(Element elem)
        {
            Category cat = elem?.Category;
            if (cat == null)
                return false;

            return ElementIdHelper.TryGetBuiltInCategory(cat.Id, out BuiltInCategory builtIn) && _categories.Contains(builtIn);
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
