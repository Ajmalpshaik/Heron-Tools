// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Tracks attempted, applied, and skipped counts for a graphics override operation.

namespace HeronTools.Models.GraphicsTools
{
    /// <summary>
    /// Tracks attempted/applied/skipped counts for graphics operations.
    /// </summary>
    internal sealed class GraphicsOperationSummary
    {
        public int Attempted { get; set; }

        public int Applied { get; set; }

        public int Skipped { get; set; }

        /// <summary>
        /// Why the first skipped item was skipped - Revit's own message where there was one. Lets a tool
        /// that changed nothing say why instead of finishing in silence.
        /// </summary>
        public string FirstSkipReason { get; set; }

        /// <summary>Counts one skipped item and keeps the first reason given.</summary>
        public void NoteSkip(string reason)
        {
            Skipped++;
            if (string.IsNullOrWhiteSpace(FirstSkipReason) && !string.IsNullOrWhiteSpace(reason))
                FirstSkipReason = reason.Trim();
        }

        public bool HasChanges
        {
            get { return Applied > 0; }
        }
    }
}
