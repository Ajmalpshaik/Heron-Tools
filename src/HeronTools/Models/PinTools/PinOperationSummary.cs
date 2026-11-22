// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Captures pin/unpin execution counts for result reporting.

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

        /// <summary>
        /// Elements a colleague changed or deleted and synced (workshared model): Revit refuses them until this
        /// model does Reload Latest, so they are left alone and the rest still change.
        /// </summary>
        public int OutOfDateCount { get; set; }

        /// <summary>Why the first skipped element was skipped, in plain words; null when none was.</summary>
        public string FirstSkipReason { get; private set; }

        /// <summary>Keeps the first reason only - the status line has room for one.</summary>
        public void KeepFirstSkipReason(string reason)
        {
            if (FirstSkipReason == null && !string.IsNullOrWhiteSpace(reason))
                FirstSkipReason = reason;
        }

        /// <summary>True when Revit refused the change at commit: nothing in the model changed.</summary>
        public bool NotCommitted { get; set; }
    }
}
