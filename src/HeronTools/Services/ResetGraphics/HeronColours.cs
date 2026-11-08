// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The colours Heron Tools itself put on a view - Colorize's and Highlight's - so Reset Graphics can leave them
// alone when asked: those tools put each element's OWN colours back (Restore original / second click), which
// a plain reset would lose.

using System.Collections.Generic;
using Autodesk.Revit.DB;
using HeronTools.Commands.GraphicsTools;
using HeronTools.Services.Colorize;

namespace HeronTools.Services.ResetGraphics
{
    internal static class HeronColours
    {
        internal static HashSet<long> In(Document doc, View view)
        {
            var ids = new HashSet<long>(ColorizeMemory.StillColoured(doc, view));
            ids.UnionWith(CmdHighlightSelection.HighlightedIds(doc, view));
            return ids;
        }
    }
}
