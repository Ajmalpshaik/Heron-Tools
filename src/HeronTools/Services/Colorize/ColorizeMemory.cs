// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Remembers each element's own graphic overrides before Colorize first colours it, so "Restore original"
// can put them back exactly - and what Colorize wrote, so an override the modeller sets by hand afterwards is
// never mistaken for a Colorize colour.

using System.Collections.Generic;
using Autodesk.Revit.DB;
using HeronTools.Utils;

namespace HeronTools.Services.Colorize
{
    /// <summary>
    /// Per open model + view, per element: the overrides it had BEFORE Colorize touched it (the original), and
    /// every override Colorize has written on it since. An element counts as "still coloured" only while its
    /// current override is exactly one Colorize wrote. Lives for the Revit session; after a restart, Undo or
    /// Revit's "Override Graphics in View &gt; Reset" are the way back.
    /// </summary>
    internal static class ColorizeMemory
    {
        private sealed class Entry
        {
            public Entry(OverrideGraphicSettings original) { Original = original; }

            /// <summary>The element's own overrides from before Colorize.</summary>
            public readonly OverrideGraphicSettings Original;

            /// <summary>
            /// Every override Colorize wrote on it since the original was taken (not only the last one): an Undo
            /// of a later Shuffle brings back an earlier Colorize colour, which must still count as Colorize's.
            /// </summary>
            public readonly List<OverrideGraphicSettings> Written = new List<OverrideGraphicSettings>();

            public void AddWritten(OverrideGraphicSettings written)
            {
                foreach (OverrideGraphicSettings w in Written)
                {
                    if (Same(w, written))
                        return;
                }
                Written.Add(new OverrideGraphicSettings(written));
            }

            /// <summary>Colorize's colour is on: the override is one Colorize wrote, and not the original.</summary>
            public bool StillColoured(OverrideGraphicSettings current)
            {
                if (Same(current, Original))
                    return false;
                foreach (OverrideGraphicSettings w in Written)
                {
                    if (Same(current, w))
                        return true;
                }
                return false;
            }
        }

        /// <summary>Per open model: view id -> element id -> entry.</summary>
        private static readonly PerDocumentMemory<Dictionary<long, Dictionary<long, Entry>>> Saved =
            new PerDocumentMemory<Dictionary<long, Dictionary<long, Entry>>>();

        private static Dictionary<long, Entry> PerView(Document doc, View view)
        {
            Dictionary<long, Dictionary<long, Entry>> perDoc = Saved.Get(doc);
            return perDoc != null && perDoc.TryGetValue(view.Id.LongValue(), out Dictionary<long, Entry> perView)
                ? perView
                : null;
        }

        /// <summary>
        /// Call BEFORE colouring, with the override about to be written. An element that still shows a Colorize
        /// colour keeps its remembered original. Any other element - never coloured, restored, undone, or changed
        /// by hand since - has its CURRENT overrides remembered as the original, so a hand-made change is what
        /// Restore puts back.
        /// </summary>
        internal static void Remember(Document doc, View view, IEnumerable<ElementId> ids, OverrideGraphicSettings written)
        {
            Dictionary<long, Dictionary<long, Entry>> perDoc =
                Saved.GetOrAdd(doc, () => new Dictionary<long, Dictionary<long, Entry>>());
            long viewKey = view.Id.LongValue();
            if (!perDoc.TryGetValue(viewKey, out Dictionary<long, Entry> perView))
            {
                perView = new Dictionary<long, Entry>();
                perDoc[viewKey] = perView;
            }

            foreach (ElementId id in ids)
            {
                long k = id.LongValue();
                OverrideGraphicSettings current = view.GetElementOverrides(id);
                if (!perView.TryGetValue(k, out Entry entry) || !entry.StillColoured(current))
                {
                    entry = new Entry(new OverrideGraphicSettings(current));
                    perView[k] = entry;
                }
                entry.AddWritten(written);
            }
        }

        /// <summary>
        /// True when at least one element in this view still shows a Colorize colour. Every entry is checked
        /// (a sample could miss the coloured ones). Read-only: after a Restore the memory is KEPT, so an Undo of
        /// the Restore can still be restored again.
        /// </summary>
        internal static bool HasMemory(Document doc, View view)
        {
            Dictionary<long, Entry> perView = PerView(doc, view);
            if (perView == null)
                return false;

            foreach (KeyValuePair<long, Entry> entry in perView)
            {
                ElementId id = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(id) != null && entry.Value.StillColoured(view.GetElementOverrides(id)))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The elements still showing a Colorize colour in this view - for Reset Graphics, which can leave Heron's
        /// own colours to Colorize's Restore original. A colour the modeller set by hand is not in it.
        /// </summary>
        internal static List<long> StillColoured(Document doc, View view)
        {
            var ids = new List<long>();
            Dictionary<long, Entry> perView = PerView(doc, view);
            if (perView == null)
                return ids;
            foreach (KeyValuePair<long, Entry> entry in perView)
            {
                ElementId id = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(id) != null && entry.Value.StillColoured(view.GetElementOverrides(id)))
                    ids.Add(entry.Key);
            }
            return ids;
        }

        /// <summary>
        /// Puts the remembered original back on every element still showing a Colorize colour in this view
        /// (inside the caller's transaction) and returns how many were restored. Elements already restored, or
        /// changed by hand since, are left as they are and not counted. The memory is kept, so an Undo of the
        /// restore can be restored again.
        /// </summary>
        internal static int Restore(Document doc, View view)
        {
            Dictionary<long, Entry> perView = PerView(doc, view);
            if (perView == null)
                return 0;

            int restored = 0;
            foreach (KeyValuePair<long, Entry> entry in perView)
            {
                ElementId id = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(id) == null)
                    continue; // deleted since it was coloured
                if (!entry.Value.StillColoured(view.GetElementOverrides(id)))
                    continue; // already back, or the modeller changed it by hand: leave it

                // One element Revit refuses must not stop the rest being restored.
                try
                {
                    view.SetElementOverrides(id, entry.Value.Original);
                    restored++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                }
            }
            return restored;
        }

        /// <summary>
        /// Every field of an element override is the same - not only the ones Colorize writes, so a line weight
        /// or transparency the modeller added on top of a Colorize colour makes it "changed by hand".
        /// </summary>
        private static bool Same(OverrideGraphicSettings a, OverrideGraphicSettings b) =>
            SameColour(a.ProjectionLineColor, b.ProjectionLineColor) &&
            SameId(a.ProjectionLinePatternId, b.ProjectionLinePatternId) &&
            a.ProjectionLineWeight == b.ProjectionLineWeight &&
            SameColour(a.CutLineColor, b.CutLineColor) &&
            SameId(a.CutLinePatternId, b.CutLinePatternId) &&
            a.CutLineWeight == b.CutLineWeight &&
            SameId(a.SurfaceForegroundPatternId, b.SurfaceForegroundPatternId) &&
            SameColour(a.SurfaceForegroundPatternColor, b.SurfaceForegroundPatternColor) &&
            a.IsSurfaceForegroundPatternVisible == b.IsSurfaceForegroundPatternVisible &&
            SameId(a.SurfaceBackgroundPatternId, b.SurfaceBackgroundPatternId) &&
            SameColour(a.SurfaceBackgroundPatternColor, b.SurfaceBackgroundPatternColor) &&
            a.IsSurfaceBackgroundPatternVisible == b.IsSurfaceBackgroundPatternVisible &&
            SameId(a.CutForegroundPatternId, b.CutForegroundPatternId) &&
            SameColour(a.CutForegroundPatternColor, b.CutForegroundPatternColor) &&
            a.IsCutForegroundPatternVisible == b.IsCutForegroundPatternVisible &&
            SameId(a.CutBackgroundPatternId, b.CutBackgroundPatternId) &&
            SameColour(a.CutBackgroundPatternColor, b.CutBackgroundPatternColor) &&
            a.IsCutBackgroundPatternVisible == b.IsCutBackgroundPatternVisible &&
            a.Transparency == b.Transparency &&
            a.Halftone == b.Halftone &&
            a.DetailLevel == b.DetailLevel;

        private static bool SameId(ElementId a, ElementId b) =>
            (a ?? ElementId.InvalidElementId).LongValue() == (b ?? ElementId.InvalidElementId).LongValue();

        private static bool SameColour(Color a, Color b)
        {
            bool aValid = a != null && a.IsValid, bValid = b != null && b.IsValid;
            if (!aValid || !bValid)
                return aValid == bValid;
            return a.Red == b.Red && a.Green == b.Green && a.Blue == b.Blue;
        }
    }
}
