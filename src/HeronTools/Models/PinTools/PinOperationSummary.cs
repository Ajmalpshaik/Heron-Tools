// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Tool Name: Pin Operation Summary Model
// Description: Captures pin/unpin execution counts for result reporting.
// Author: Ajmal P.S.
// Version: 1.0.0
// Last Updated: 2026-04-15
// Revit Version: 2020

namespace HeronTools.Models.PinTools
{
    /// <summary>
    /// Summary of a pin or unpin run.
    /// </summary>
    internal sealed class PinOperationSummary
    {
        public int TargetedCount { get; set; }

        public int UpdatedCount { get; set; }

        public int UnchangedCount { get; set; }

        public int SkippedCount { get; set; }

        /// <summary>Elements another user owns in a workshared model; left alone so the rest can change.</summary>
        public int OwnedByOthersCount { get; set; }

        /// <summary>True when Revit refused the change at commit: nothing in the model changed.</summary>
        public bool NotCommitted { get; set; }
    }
}
