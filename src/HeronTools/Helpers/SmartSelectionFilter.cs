// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Restricts a pick session to elements sharing one reference element's category - used by Smart Selection for its window/crossing/click selection stage.

using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace HeronTools.Utils
{
    /// <summary>
    /// Selection filter for Smart Selection: allows any categorized element (reference pick), or
    /// only elements matching one reference category (follow-up pick), depending on the constructor.
    /// </summary>
    internal sealed class SmartSelectionFilter : ISelectionFilter
    {
        private readonly long? _categoryId;

        /// <summary>
        /// No filtering beyond "has a category" - used for the reference element pick.
        /// </summary>
        public SmartSelectionFilter()
        {
            _categoryId = null;
        }

        /// <summary>
        /// Filters to elements whose Category matches <paramref name="categoryId"/> - used for the
        /// follow-up window/crossing/click selection stage.
        /// </summary>
        public SmartSelectionFilter(ElementId categoryId)
        {
            _categoryId = categoryId?.LongValue();
        }

        public bool AllowElement(Element elem)
        {
            Category category = elem?.Category;
            if (category == null)
            {
                return false;
            }

            return _categoryId == null || category.Id.LongValue() == _categoryId;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
