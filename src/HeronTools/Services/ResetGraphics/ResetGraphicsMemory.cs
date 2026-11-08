// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The copy Reset Graphics keeps of what it cleared, so "Restore Last Reset" can put every override back -
// even after other work, where Ctrl+Z would also undo that work. One copy per model: the LAST reset (Reset
// Selected or the window). It lives while Revit is open; after a restart, Ctrl+Z history is gone too, and the
// copy with it (Revit's overrides cannot be written to a file and read back exactly).

using System.Collections.Generic;
using Autodesk.Revit.DB;
using HeronTools.Utils;

namespace HeronTools.Services.ResetGraphics
{
    internal static class ResetGraphicsMemory
    {
        /// <summary>What one view had before the reset: per element and per category.</summary>
        internal sealed class ViewSnapshot
        {
            public ViewSnapshot(ElementId viewId)
            {
                ViewId = viewId.LongValue();
            }

            public readonly long ViewId;
            public readonly Dictionary<long, OverrideGraphicSettings> Elements = new Dictionary<long, OverrideGraphicSettings>();
            public readonly Dictionary<long, OverrideGraphicSettings> Categories = new Dictionary<long, OverrideGraphicSettings>();
        }

        private static readonly Dictionary<string, List<ViewSnapshot>> LastReset = new Dictionary<string, List<ViewSnapshot>>();

        private static string Key(Document doc) =>
            string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;

        /// <summary>Replaces the kept copy with this reset's.</summary>
        internal static void Keep(Document doc, List<ViewSnapshot> snapshot)
        {
            LastReset[Key(doc)] = snapshot;
        }

        internal static bool HasCopy(Document doc) =>
            doc != null && LastReset.TryGetValue(Key(doc), out List<ViewSnapshot> kept) && kept.Count > 0;

        /// <summary>
        /// Puts the kept overrides back, in one transaction (one Undo step). The copy is kept afterwards, so an
        /// Undo of the restore can be restored again. Returns (elements, categories) put back.
        /// </summary>
        internal static (int Elements, int Categories, bool Committed) Restore(Document doc)
        {
            if (!LastReset.TryGetValue(Key(doc), out List<ViewSnapshot> kept) || kept.Count == 0)
                return (0, 0, true);

            int elements = 0, categories = 0;
            using (var t = new Transaction(doc, "Restore Last Reset"))
            {
                t.Start();
                foreach (ViewSnapshot snapshot in kept)
                {
                    if (!(doc.GetElement(ElementIdHelper.FromLong(snapshot.ViewId)) is View view))
                        continue;   // the view was deleted since

                    foreach (KeyValuePair<long, OverrideGraphicSettings> entry in snapshot.Elements)
                    {
                        ElementId id = ElementIdHelper.FromLong(entry.Key);
                        if (doc.GetElement(id) == null)
                            continue;
                        try { view.SetElementOverrides(id, entry.Value); elements++; }
                        catch (Autodesk.Revit.Exceptions.ApplicationException) { }
                    }
                    foreach (KeyValuePair<long, OverrideGraphicSettings> entry in snapshot.Categories)
                    {
                        try { view.SetCategoryOverrides(ElementIdHelper.FromLong(entry.Key), entry.Value); categories++; }
                        catch (Autodesk.Revit.Exceptions.ApplicationException) { }
                    }
                }

                if (elements + categories == 0)
                {
                    t.RollBack();
                    return (0, 0, true);
                }
                if (t.Commit() != TransactionStatus.Committed)
                    return (0, 0, false);
            }
            return (elements, categories, true);
        }
    }
}
