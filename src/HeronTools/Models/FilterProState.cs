// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Snapshot model that captures all UI and selection state of the Filter Pro window for restoration on the next session in the same document
// (and, filled from the lasting memory, in a new Revit session or another model - Colorize uses it the same way).

using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace HeronTools.Models
{
    internal class FilterProState
    {
        // Category + Parameter Selection
        public List<ElementId> CategoryIds { get; set; } = new List<ElementId>();
        public ElementId ParameterId { get; set; }
        public string RuleType { get; set; }
        public List<FilterValueKey> Values { get; set; } = new List<FilterValueKey>();

        // Set only when the state comes from the lasting memory (another Revit session or another model), where
        // ids mean nothing: the parameter by a key that is the same in every model (see FilterPicksMemory) and
        // the values by their display text. ParameterId and Values are then empty.
        public string ParameterKey { get; set; }
        public List<string> ValueTexts { get; set; } = new List<string>();

        // Naming Controls
        public string Prefix { get; set; }
        public string Suffix { get; set; }
        public string Separator { get; set; }
        public bool CaseSensitive { get; set; }
        public bool IncludeCategory { get; set; }
        public bool IncludeParameter { get; set; }
        public bool OverrideExisting { get; set; }

        // View Targets
        public bool ApplyToActiveView { get; set; } = true;
        public List<ElementId> TargetViewIds { get; set; } = new List<ElementId>();

        // Graphics Controls
        public bool ColorProjectionLines { get; set; }
        public bool ColorProjectionPatterns { get; set; }
        public bool ColorCutLines { get; set; }
        public bool ColorCutPatterns { get; set; }
        public bool ColorHalftone { get; set; }
        public bool ApplyGraphics { get; set; }
        public ElementId PatternId { get; set; }

        // Filter Placement Logic
        public bool PlaceNewFiltersFirst { get; set; } = true;
    }
}
