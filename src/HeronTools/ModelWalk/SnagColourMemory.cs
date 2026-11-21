// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - the way back for snag colours. Before an element is coloured, its OWN graphic overrides in the
// walk view are copied; "Clear my snag colours", the end of the walk, and every Save / Sync with Central put the
// copies back (Quick Clash's rule: no tool colour reaches the file). An element whose colour the modeller changed
// after it was snagged is left exactly as the modeller set it. The memory lives for the Revit session, so colours
// left by a walk that could not clean up (Revit busy, overlay closed) still come back at the next save or walk.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Utils;

namespace HeronTools.Services.ModelWalk
{
    internal sealed class SnagColourMemory
    {
        private static readonly List<SnagColourMemory> All = new List<SnagColourMemory>();
        private static bool _watching;

        private readonly Dictionary<long, OverrideGraphicSettings> _originals = new Dictionary<long, OverrideGraphicSettings>();

        private SnagColourMemory(Document doc, long viewId)
        {
            Document = doc;
            ViewId = viewId;
        }

        public Document Document { get; }
        public long ViewId { get; }

        public int Count => _originals.Count;

        /// <summary>Raised (on Revit's thread) when a save or sync gave the colours back - the walk HUD says so.</summary>
        internal static event Action<string> GivenBackBeforeWriting;

        // ------------------------------------------------------------------ finding one

        internal static SnagColourMemory Find(Document doc, View view)
        {
            foreach (SnagColourMemory m in All)
                if (m.Document.Equals(doc) && m.ViewId == view.Id.LongValue())
                    return m;
            return null;
        }

        internal static SnagColourMemory FindOrCreate(Document doc, View view)
        {
            SnagColourMemory memory = Find(doc, view);
            if (memory == null)
            {
                memory = new SnagColourMemory(doc, view.Id.LongValue());
                All.Add(memory);
            }
            return memory;
        }

        /// <summary>Watch Save, Save As, Sync with Central and closing for the rest of the Revit session (once).</summary>
        internal static void Watch(UIApplication uiApp)
        {
            if (_watching || uiApp == null)
                return;
            _watching = true;
            uiApp.Application.DocumentSaving += (s, e) => GiveBackBeforeWriting(e.Document, "saving");
            uiApp.Application.DocumentSavingAs += (s, e) => GiveBackBeforeWriting(e.Document, "saving");
            uiApp.Application.DocumentSynchronizingWithCentral += (s, e) => GiveBackBeforeWriting(e.Document, "Sync with Central");
            // Forget a model's colours only once it has really closed: a close can still be cancelled after
            // DocumentClosing (the save question, another add-in), and the model then stays open with the red on -
            // its way back must stay too. DocumentClosed has no Document, so the pair is matched by DocumentId.
            uiApp.Application.DocumentClosing += (s, e) =>
                Closing[e.DocumentId] = All.FindAll(m => m.Document.Equals(e.Document));
            uiApp.Application.DocumentClosed += (s, e) =>
            {
                if (!Closing.TryGetValue(e.DocumentId, out List<SnagColourMemory> closing))
                    return;
                Closing.Remove(e.DocumentId);
                if (e.Status == Autodesk.Revit.DB.Events.RevitAPIEventStatus.Succeeded)
                    All.RemoveAll(closing.Contains);
            };
        }

        private static readonly Dictionary<int, List<SnagColourMemory>> Closing = new Dictionary<int, List<SnagColourMemory>>();

        // ------------------------------------------------------------------ the snag colour

        /// <summary>
        /// The snag colour: Error red from the Heron palette (#FF5A4E), lines and a solid surface fill. A model colour,
        /// not a window colour. Only the DRAFTING solid fill is used (a model solid fill scales with the element).
        /// With none in the model, the lines are still coloured.
        /// </summary>
        internal static OverrideGraphicSettings SnagOverride(Document doc)
        {
            var red = new Color(0xFF, 0x5A, 0x4E);
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(red);
            foreach (FillPatternElement fp in new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)))
            {
                FillPattern pattern = fp.GetFillPattern();
                if (pattern != null && pattern.IsSolidFill && pattern.Target == FillPatternTarget.Drafting)
                {
                    ogs.SetSurfaceForegroundPatternId(fp.Id);
                    ogs.SetSurfaceForegroundPatternColor(red);
                    break;
                }
            }
            return ogs;
        }

        // ------------------------------------------------------------------ inside a transaction

        /// <summary>Copies the element's own overrides (only the first time), then colours it. Call inside a transaction.</summary>
        internal void Colour(View view, ElementId id, OverrideGraphicSettings snag)
        {
            long key = id.LongValue();
            if (!_originals.ContainsKey(key))
                _originals[key] = new OverrideGraphicSettings(view.GetElementOverrides(id));
            view.SetElementOverrides(id, snag);
        }

        /// <summary>Forget one element after its transaction was rolled back (its colour never went on).</summary>
        internal void Forget(ElementId id) => _originals.Remove(id.LongValue());

        internal bool IsColoured(ElementId id) => _originals.ContainsKey(id.LongValue());

        /// <summary>
        /// Puts every element's own overrides back - unless the element no longer shows the snag colour (the modeller
        /// changed it, or Undo took the snag away): then it is left as it is. Call inside a transaction.
        /// </summary>
        internal void PutBack(Document doc, View view, out int putBack, out int leftAsChanged)
        {
            putBack = 0;
            leftAsChanged = 0;
            OverrideGraphicSettings snag = SnagOverride(doc);
            foreach (KeyValuePair<long, OverrideGraphicSettings> entry in _originals)
            {
                ElementId id = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(id) == null)
                    continue;
                try
                {
                    if (!LooksLikeSnag(view.GetElementOverrides(id), snag))
                    {
                        leftAsChanged++;
                        continue;
                    }
                    view.SetElementOverrides(id, entry.Value);
                    putBack++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // one refused element must not stop the rest being put back
                    leftAsChanged++;
                }
            }
        }

        /// <summary>
        /// A copy of the walk view must not carry the snag red: every remembered element gets its own colours in
        /// <paramref name="target"/>. Call inside a transaction.
        /// </summary>
        internal void GiveOriginalsTo(Document doc, View target)
        {
            foreach (KeyValuePair<long, OverrideGraphicSettings> entry in _originals)
            {
                ElementId id = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(id) == null)
                    continue;
                try
                {
                    target.SetElementOverrides(id, entry.Value);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // one refused element must not stop the view being saved
                }
            }
        }

        /// <summary>Call after the put-back transaction committed.</summary>
        internal void Clear()
        {
            _originals.Clear();
            All.Remove(this);
        }

        private static bool LooksLikeSnag(OverrideGraphicSettings now, OverrideGraphicSettings snag)
        {
            return SameColour(now.ProjectionLineColor, snag.ProjectionLineColor) &&
                   SameColour(now.SurfaceForegroundPatternColor, snag.SurfaceForegroundPatternColor);
        }

        private static bool SameColour(Color a, Color b)
        {
            bool aValid = a != null && a.IsValid;
            bool bValid = b != null && b.IsValid;
            if (!aValid || !bValid)
                return aValid == bValid;
            return a.Red == b.Red && a.Green == b.Green && a.Blue == b.Blue;
        }

        // ------------------------------------------------------------------ one Undo step

        /// <summary>
        /// Gives every remembered colour back in one named transaction (one Undo step). Returns a plain line for the
        /// modeller, or null when there was nothing to give back.
        /// </summary>
        internal static string GiveBack(Document doc, View view, string undoName)
        {
            SnagColourMemory memory = view != null ? Find(doc, view) : null;
            if (memory == null || memory.Count == 0)
                return null;
            if (doc.IsReadOnly)
                return "The model is read-only, so the snag colours could not be given back.";

            using (var t = new Transaction(doc, undoName))
            {
                try
                {
                    t.Start();
                    memory.PutBack(doc, view, out int putBack, out int left);
                    if (t.Commit() != TransactionStatus.Committed)
                        return "Revit did not keep the change, so the snag colours are still on. Run Model Walk again, or save - they are given back before saving.";
                    memory.Clear();
                    string line = putBack == 1 ? "1 element has its own colours back." : putBack + " elements have their own colours back.";
                    if (left > 0)
                        line += " " + left + (left == 1 ? " was" : " were") + " left as you changed " + (left == 1 ? "it" : "them") + ".";
                    return line;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    if (t.HasStarted() && !t.HasEnded())
                        t.RollBack();
                    return "The snag colours could not be given back (Revit said: " + ex.Message + "). They are given back before the next save.";
                }
            }
        }

        private static void GiveBackBeforeWriting(Document doc, string what)
        {
            if (doc == null)
                return;
            foreach (SnagColourMemory memory in All.ToArray())
            {
                if (!memory.Document.Equals(doc) || memory.Count == 0)
                    continue;
                try
                {
                    View view = doc.GetElement(ElementIdHelper.FromLong(memory.ViewId)) as View;
                    if (view == null)
                    {
                        memory.Clear();
                        continue;
                    }
                    string line = GiveBack(doc, view, "Model Walk: give back snag colours");
                    if (line != null)
                        GivenBackBeforeWriting?.Invoke("Before " + what + ": " + line);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    // Never stop Revit's own save or sync. Say what happened instead.
                    GivenBackBeforeWriting?.Invoke("Snag colours could not be given back before " + what + " (Revit said: " + ex.Message + ").");
                }
            }
        }
    }
}
