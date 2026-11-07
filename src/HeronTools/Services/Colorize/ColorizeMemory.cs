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
        /// True when Colorize's colours are still on in this view. A memory whose colours were undone (Ctrl+Z,
        /// or overrides changed back by hand) is dropped here, so Restore never writes stale values over newer edits.
        /// </summary>
        internal static bool HasMemory(Document doc, View view)
        {
            string key = Key(doc, view);
            if (!Saved.TryGetValue(key, out Dictionary<long, OverrideGraphicSettings> perView) || perView.Count == 0)
                return false;

            foreach (KeyValuePair<long, OverrideGraphicSettings> entry in perView.Take(50))
            {
                ElementId id = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(id) == null)
                    continue;

                OverrideGraphicSettings now = view.GetElementOverrides(id);
                if (!SameColour(now.ProjectionLineColor, entry.Value.ProjectionLineColor) ||
                    !SameColour(now.CutLineColor, entry.Value.CutLineColor) ||
                    !SameColour(now.SurfaceForegroundPatternColor, entry.Value.SurfaceForegroundPatternColor) ||
                    now.Halftone != entry.Value.Halftone)
                    return true;   // something Colorize changed is still there
            }

            Saved.Remove(key);     // nothing differs from the originals any more: the memory is stale
            return false;
        }

        private static bool SameColour(Color a, Color b)
        {
            bool aValid = a != null && a.IsValid, bValid = b != null && b.IsValid;
            if (!aValid || !bValid)
                return aValid == bValid;
            return a.Red == b.Red && a.Green == b.Green && a.Blue == b.Blue;
        }

        /// <summary>Call before a new Shuffle: drops this view's memory if its colours were undone meanwhile.</summary>
        internal static void DropIfStale(Document doc, View view) => HasMemory(doc, view);

        /// <summary>
        /// Puts the remembered overrides back in this view (inside the caller's transaction) and returns how
        /// many elements were restored. The memory for the view is cleared by <see cref="Forget"/> once the
        /// caller's transaction has committed.
        /// </summary>
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

        /// <summary>Drops the memory of a view (after a committed restore).</summary>
        internal static void Forget(Document doc, View view) => Saved.Remove(Key(doc, view));
    }
}
