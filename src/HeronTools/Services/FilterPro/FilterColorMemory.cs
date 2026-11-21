// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.1.1
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Remembers each filter's own overrides in a view before Shuffle colours first changes them, so
// "Restore colours" can put them back exactly.

using System.Collections.Generic;
using Autodesk.Revit.DB;
using HeronTools.Utils;

namespace HeronTools.Services.FilterPro
{
    /// <summary>
    /// Per document + view + filter: the overrides the filter had BEFORE Shuffle touched it (the original), and
    /// the overrides Shuffle last wrote. Works like Colorize's memory, with one extra safety: a filter is only
    /// put back while it still carries Shuffle's colours, so a change the modeller made by hand after a Shuffle
    /// is never wiped by Restore.
    /// Lives for the Revit session; after a restart, Undo is the way back.
    /// </summary>
    internal static class FilterColorMemory
    {
        private sealed class Entry
        {
            internal OverrideGraphicSettings Original;
            internal OverrideGraphicSettings Written;
        }

        // Kept only once the run's transaction has committed (see AcceptPending), the same way the filter
        // order is kept: a rolled-back run must not leave memory of colours Revit never kept.
        private static readonly Dictionary<string, Dictionary<long, Entry>> Saved =
            new Dictionary<string, Dictionary<long, Entry>>();

        private static readonly Dictionary<string, Dictionary<long, Entry>> Pending =
            new Dictionary<string, Dictionary<long, Entry>>();

        private static string Key(Document doc, View view)
        {
            string docKey = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;
            return docKey + "|" + view.Id.LongValue();
        }

        /// <summary>
        /// Call just BEFORE Shuffle writes a filter's overrides (inside the run's transaction). The original is
        /// kept from an earlier Shuffle only while the filter still shows that Shuffle's colours; if it was
        /// restored, undone or changed by hand since, its current overrides become the new original.
        /// </summary>
        internal static void RememberBefore(Document doc, View view, ElementId filterId, OverrideGraphicSettings current)
        {
            string key = Key(doc, view);
            long id = filterId.LongValue();

            if (Pending.TryGetValue(key, out var pendingView) && pendingView.ContainsKey(id))
                return; // already recorded in this run (a second write in the same run keeps the first original)

            OverrideGraphicSettings original = Copy(current);
            if (Saved.TryGetValue(key, out var savedView) &&
                savedView.TryGetValue(id, out Entry known) &&
                Same(current, known.Written))
            {
                original = known.Original; // Shuffle on top of Shuffle: the true original stays
            }

            if (pendingView == null)
            {
                pendingView = new Dictionary<long, Entry>();
                Pending[key] = pendingView;
            }
            pendingView[id] = new Entry { Original = original };
        }

        /// <summary>Call just AFTER Shuffle wrote a filter's overrides: records what it wrote.</summary>
        internal static void RememberAfter(Document doc, View view, ElementId filterId, OverrideGraphicSettings written)
        {
            if (Pending.TryGetValue(Key(doc, view), out var pendingView) &&
                pendingView.TryGetValue(filterId.LongValue(), out Entry entry))
            {
                entry.Written = Copy(written);
            }
        }

        /// <summary>Call after the run's transaction committed: what it remembered is now real.</summary>
        internal static void AcceptPending()
        {
            foreach (var view in Pending)
            {
                if (!Saved.TryGetValue(view.Key, out var savedView))
                {
                    savedView = new Dictionary<long, Entry>();
                    Saved[view.Key] = savedView;
                }
                foreach (var filter in view.Value)
                {
                    if (filter.Value.Written != null)
                        savedView[filter.Key] = filter.Value;
                }
            }
            Pending.Clear();
        }

        /// <summary>Call when the run was rolled back: forget what it remembered.</summary>
        internal static void DiscardPending()
        {
            Pending.Clear();
        }

        /// <summary>True when at least one filter in this view still shows Shuffle's colours and can be put back.</summary>
        internal static bool CanRestore(Document doc, View view)
        {
            if (doc == null || view == null || !Saved.TryGetValue(Key(doc, view), out var savedView))
                return false;

            foreach (var entry in savedView)
            {
                if (StillShuffled(doc, view, entry.Key, entry.Value))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Puts back the original overrides of every filter in this view that still shows Shuffle's colours
        /// (inside the caller's transaction) and returns how many were put back. A filter changed by hand since
        /// the Shuffle is left alone and counted in <paramref name="changedByHand"/>. A Revit failure throws, so
        /// the caller rolls the whole restore back. The memory is kept, so an Undo of the restore can be
        /// restored again.
        /// </summary>
        internal static int Restore(Document doc, View view, out int changedByHand)
        {
            changedByHand = 0;
            if (!Saved.TryGetValue(Key(doc, view), out var savedView))
                return 0;

            int restored = 0;
            foreach (var entry in savedView)
            {
                ElementId filterId = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(filterId) == null || !view.IsFilterApplied(filterId))
                    continue; // the filter was deleted or taken off the view since

                OverrideGraphicSettings current = view.GetFilterOverrides(filterId);
                if (Same(current, entry.Value.Original))
                    continue; // already back as it was

                if (!Same(current, entry.Value.Written))
                {
                    changedByHand++;
                    continue;
                }

                view.SetFilterOverrides(filterId, entry.Value.Original);
                restored++;
            }
            return restored;
        }

        private static OverrideGraphicSettings Copy(OverrideGraphicSettings source) =>
            source == null ? new OverrideGraphicSettings() : new OverrideGraphicSettings(source);

        private static bool StillShuffled(Document doc, View view, long filterKey, Entry entry)
        {
            ElementId filterId = ElementIdHelper.FromLong(filterKey);
            if (doc.GetElement(filterId) == null || !view.IsFilterApplied(filterId))
                return false;

            OverrideGraphicSettings current = view.GetFilterOverrides(filterId);
            return Same(current, entry.Written) && !Same(current, entry.Original);
        }

        /// <summary>Every setting a filter's overrides can carry (all 21 the Revit 2020-2027 API has).</summary>
        private static bool Same(OverrideGraphicSettings a, OverrideGraphicSettings b)
        {
            if (a == null || b == null)
                return a == null && b == null;

            return SameColour(a.ProjectionLineColor, b.ProjectionLineColor) &&
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
        }

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
