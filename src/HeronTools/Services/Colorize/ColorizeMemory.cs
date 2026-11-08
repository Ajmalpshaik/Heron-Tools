// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Remembers each element's own graphic overrides before Colorize first colours it, so "Restore original"
// can put them back exactly.

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using HeronTools.Utils;

namespace HeronTools.Services.Colorize
{
    /// <summary>
    /// Per document + view: the overrides each element had BEFORE Colorize touched it. Only the first colour
    /// is remembered (a second Shuffle must not overwrite the true original with Colorize's own colour).
    /// Lives for the Revit session; after a restart, Undo or Revit's "Override Graphics in View &gt; Reset" are
    /// the way back.
    /// </summary>
    internal static class ColorizeMemory
    {
        private static readonly Dictionary<string, Dictionary<long, OverrideGraphicSettings>> Saved =
            new Dictionary<string, Dictionary<long, OverrideGraphicSettings>>();

        private static string Key(Document doc, View view)
        {
            string docKey = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;
            return docKey + "|" + view.Id.LongValue();
        }

        /// <summary>Call BEFORE colouring: stores the current overrides of elements not remembered yet.</summary>
        internal static void Remember(Document doc, View view, IEnumerable<ElementId> ids)
        {
            string key = Key(doc, view);
            if (!Saved.TryGetValue(key, out Dictionary<long, OverrideGraphicSettings> perView))
            {
                perView = new Dictionary<long, OverrideGraphicSettings>();
                Saved[key] = perView;
            }

            foreach (ElementId id in ids)
            {
                long k = id.LongValue();
                if (!perView.ContainsKey(k))
                    perView[k] = new OverrideGraphicSettings(view.GetElementOverrides(id));
            }
        }

        /// <summary>
        /// True when something Colorize changed is still on in this view (an element's overrides differ from the
        /// remembered original). Read-only: after a Restore the memory is KEPT, so an Undo of the Restore can
        /// still be restored again.
        /// </summary>
        internal static bool HasMemory(Document doc, View view)
        {
            if (!Saved.TryGetValue(Key(doc, view), out Dictionary<long, OverrideGraphicSettings> perView) || perView.Count == 0)
                return false;

            int looked = 0;
            foreach (KeyValuePair<long, OverrideGraphicSettings> entry in perView)
            {
                ElementId id = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(id) == null)
                    continue;            // deleted elements are not counted
                if (Differs(view.GetElementOverrides(id), entry.Value))
                    return true;
                if (++looked >= 500)
                    break;               // a large sample, so the check stays quick in big views
            }
            return false;
        }

        /// <summary>
        /// Call before a new Shuffle: if nothing Colorize changed is still on (undone, or restored), the old memory
        /// is dropped so the next Remember stores the real current overrides.
        /// </summary>
        internal static void DropIfStale(Document doc, View view)
        {
            if (!HasMemory(doc, view))
                Saved.Remove(Key(doc, view));
        }

        /// <summary>Every override Colorize can write: line and pattern colours, pattern ids, halftone.</summary>
        private static bool Differs(OverrideGraphicSettings a, OverrideGraphicSettings b) =>
            !SameColour(a.ProjectionLineColor, b.ProjectionLineColor) ||
            !SameColour(a.CutLineColor, b.CutLineColor) ||
            !SameColour(a.SurfaceForegroundPatternColor, b.SurfaceForegroundPatternColor) ||
            !SameColour(a.CutForegroundPatternColor, b.CutForegroundPatternColor) ||
            a.SurfaceForegroundPatternId != b.SurfaceForegroundPatternId ||
            a.CutForegroundPatternId != b.CutForegroundPatternId ||
            a.IsSurfaceForegroundPatternVisible != b.IsSurfaceForegroundPatternVisible ||
            a.IsCutForegroundPatternVisible != b.IsCutForegroundPatternVisible ||
            a.Halftone != b.Halftone;

        private static bool SameColour(Color a, Color b)
        {
            bool aValid = a != null && a.IsValid, bValid = b != null && b.IsValid;
            if (!aValid || !bValid)
                return aValid == bValid;
            return a.Red == b.Red && a.Green == b.Green && a.Blue == b.Blue;
        }

        /// <summary>
        /// Puts the remembered overrides back in this view (inside the caller's transaction) and returns how
        /// many elements were restored. The memory is kept (see <see cref="HasMemory"/>), so an Undo of the
        /// restore can be restored again; a later Shuffle drops it once nothing coloured is left.
        /// </summary>
        /// <summary>
        /// The elements still carrying a Colorize colour in this view (their overrides differ from the remembered
        /// original) - for Reset Graphics, which can leave Heron's own colours to Colorize's Restore original.
        /// </summary>
        internal static List<long> StillColoured(Document doc, View view)
        {
            var ids = new List<long>();
            if (!Saved.TryGetValue(Key(doc, view), out Dictionary<long, OverrideGraphicSettings> perView))
                return ids;
            foreach (KeyValuePair<long, OverrideGraphicSettings> entry in perView)
            {
                ElementId id = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(id) != null && Differs(view.GetElementOverrides(id), entry.Value))
                    ids.Add(entry.Key);
            }
            return ids;
        }

        internal static int Restore(Document doc, View view)
        {
            if (!Saved.TryGetValue(Key(doc, view), out Dictionary<long, OverrideGraphicSettings> perView))
                return 0;

            int restored = 0;
            foreach (KeyValuePair<long, OverrideGraphicSettings> entry in perView)
            {
                ElementId id = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(id) == null)
                    continue; // deleted since it was coloured

                // One element Revit refuses must not stop the rest being restored.
                try
                {
                    view.SetElementOverrides(id, entry.Value);
                    restored++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                }
            }
            return restored;
        }

    }
}
