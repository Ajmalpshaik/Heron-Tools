// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model helper for representing host or linked documents in search workflows.

using Autodesk.Revit.DB;

namespace HeronTools.Models
{
    /// <summary>
    /// Lightweight wrapper for displaying either the host document or a linked model entry.
    /// </summary>
    internal sealed class LinkDisplayItem
    {
        internal LinkDisplayItem(string name, RevitLinkInstance instance, Document linkDocument, bool isHost)
        {
            DisplayName = string.IsNullOrWhiteSpace(name) ? "(Unnamed Link)" : name;
            Instance = instance;
            LinkDocument = linkDocument;
            IsHost = isHost;
        }

        public string DisplayName { get; }

        internal bool IsHost { get; }

        public string SourceLabel => IsHost ? "Host" : "Link";

        internal RevitLinkInstance Instance { get; }

        internal Document LinkDocument { get; }

        public override string ToString() => DisplayName;
    }
}
