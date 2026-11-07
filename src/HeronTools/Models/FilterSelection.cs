// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Runtime model representing the user's complete selection — categories, parameter, values, rule type, naming options, graphics settings, and view targets.

using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace HeronTools.Models
{
    internal class FilterSelection
    {
        public IList<ElementId> CategoryIds { get; set; }

        public FilterParameterItem Parameter { get; set; }

        public IList<FilterValueItem> Values { get; set; }

        public string RuleType { get; set; }

        public bool ApplyToView { get; set; }

        public bool ApplyToActiveView { get; set; }

        public IList<ElementId> TargetViewIds { get; set; }

        public bool OverrideExisting { get; set; }

        public bool RandomColors { get; set; }

        public bool ColorProjectionLines { get; set; }

        public bool ColorProjectionPatterns { get; set; }

        public bool ColorCutLines { get; set; }

        public bool ColorCutPatterns { get; set; }

        public bool ColorHalftone { get; set; }

        public bool ApplyGraphics { get; set; }

        public ElementId PatternId { get; set; }

        public bool PlaceNewFiltersFirst { get; set; } = true;

        public string Prefix { get; set; }

        public string Suffix { get; set; }

        public string Separator { get; set; }

        public bool CaseSensitive { get; set; }

        public bool IncludeCategory { get; set; }

        public bool IncludeParameter { get; set; }
    }
}
