// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Quick Clash - everything that touches the model while the window is open: fill the lists, Run, Reset, Zoom,
// pick a colour, and the Revit events that keep the view safe (Save / Sync put the view back first; Undo and
// Redo are followed; closing the model or the window never leaves the view coloured).
// Every method here runs on Revit's thread - from the ExternalEvent or from a Revit event.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using HeronTools.Models.QuickClash;
using HeronTools.Services.GraphicsTools;
using HeronTools.UI;
using HeronTools.Utils;

namespace HeronTools.Services.QuickClash
{
    internal sealed class QuickClashSession
    {
        private const string Title = "Quick Clash";
        private const string RunName = "Quick Clash";
        private const string ResetName = "Quick Clash - Reset";

        private readonly UIApplication _uiApp;
        private readonly Document _doc;
        private readonly QuickClashRequestHandler _handler;
        private readonly ExternalEvent _event;
        private QuickClashWindow _window;

        /// <summary>The view the last Run coloured (its memory belongs to this session), or -1.</summary>
        private long _colouredViewId = -1;

        /// <summary>The linked clash elements' solids of the last Run, to show again after an Undo then Redo.</summary>
        private List<Solid> _linkedSolids = new List<Solid>();
        private ClashColour _overlayColour;

        /// <summary>The view the results list belongs to.</summary>
        private long _resultsViewId = -1;

        internal static QuickClashSession Current { get; private set; }

        private QuickClashSession(UIApplication uiApp, Document doc)
        {
            _uiApp = uiApp;
            _doc = doc;
            _handler = new QuickClashRequestHandler { OnError = ReportError };
            _event = ExternalEvent.Create(_handler);
        }

        // ------------------------------------------------------------------ opening and closing

        /// <summary>Opens the window (or brings the open one forward). Called from the command, on Revit's thread.</summary>
        internal static void Open(UIApplication uiApp)
        {
            if (Current != null && Current._window != null)
            {
                Current._window.BringForward();
                return;
            }

            var session = new QuickClashSession(uiApp, uiApp.ActiveUIDocument.Document);
            session._window = new QuickClashWindow(session, QuickClashSettings.Load());
            new System.Windows.Interop.WindowInteropHelper(session._window) { Owner = uiApp.MainWindowHandle };
            Current = session;
            session.Subscribe();
            session._window.Show();
            session.Refresh(uiApp, null);
            session.OfferLeftoverReset(uiApp);
        }

        /// <summary>The window asks to close: put the view back first, then close for real.</summary>
        internal void RequestClose()
        {
            Post(app =>
            {
                View view = ColouredView();
                if (view != null)
                {
                    ClashColourMemory memory = ClashColourMemory.Find(_doc, view);
                    if (memory != null && memory.StillColoured(_doc, view) && !PutBack(view, memory, clearList: true))
                        return;   // the view is still coloured: keep the window, so Reset view stays at hand
                    if (memory != null && !memory.StillColoured(_doc, view))
                        ClashColourMemory.Forget(_doc, view);
                }
                Shutdown();
            });
        }

        private void Shutdown()
        {
            ClashOverlayServer.Instance.Hide();
            Unsubscribe();
            QuickClashWindow window = _window;
            _window = null;
            Current = null;
            window?.CloseForReal();
            // The ExternalEvent is not disposed here: this may be running inside it. It is released with the session.
        }

        private void Subscribe()
        {
            _uiApp.Application.DocumentChanged += OnDocumentChanged;
            _uiApp.Application.DocumentSaving += OnDocumentSaving;
            _uiApp.Application.DocumentSavingAs += OnDocumentSavingAs;
            _uiApp.Application.DocumentSynchronizingWithCentral += OnSynchronizing;
            _uiApp.Application.DocumentClosing += OnDocumentClosing;
            _uiApp.ViewActivated += OnViewActivated;
            _uiApp.ApplicationClosing += OnApplicationClosing;
        }

        private void Unsubscribe()
        {
            _uiApp.Application.DocumentChanged -= OnDocumentChanged;
            _uiApp.Application.DocumentSaving -= OnDocumentSaving;
            _uiApp.Application.DocumentSavingAs -= OnDocumentSavingAs;
            _uiApp.Application.DocumentSynchronizingWithCentral -= OnSynchronizing;
            _uiApp.Application.DocumentClosing -= OnDocumentClosing;
            _uiApp.ViewActivated -= OnViewActivated;
            _uiApp.ApplicationClosing -= OnApplicationClosing;
        }

        /// <summary>Hands a piece of work to Revit's thread. The window shows "busy" until it has run.</summary>
        internal void Post(Action<UIApplication> work)
        {
            _window?.SetBusy(true);
            _handler.Enqueue(app =>
            {
                try { work(app); }
                finally { _window?.SetBusy(false); }
            });
            ExternalEventRequest raised = _event.Raise();
            if (raised != ExternalEventRequest.Accepted && raised != ExternalEventRequest.Pending)
            {
                _window?.SetBusy(false);
                _window?.SetStatus("Revit is busy and did not take the request. Finish what you are doing in Revit, then try again.");
            }
        }

        private void ReportError(Exception ex)
        {
            _window?.SetStatus("Quick Clash stopped, and nothing was changed. Revit said: " + ex.Message);
        }

        // ------------------------------------------------------------------ lists

        /// <summary>Fills both category lists and the Selection B drop-down for the active view.</summary>
        internal void Refresh(UIApplication app, QuickClashSettings ticks)
        {
            if (_window == null) return;
            if (!TryActive3DView(app, out View3D view, out string why))
            {
                _window.ShowNotA3DView(why);
                return;
            }

            QuickClashSettings s = ticks ?? _window.CurrentSettings();
            List<Element> host = ClashElementCollector.HostElementsInView(_doc, view);
            List<ClashCategoryItem> rowsA = ClashElementCollector.CategoryRows(host, s.CategoriesA);

            List<ClashSourceItem> sources = ClashElementCollector.Sources(_doc, view);
            ClashSourceItem chosen =
                sources.FirstOrDefault(x => s.SourceLinkInstanceId >= 0 && x.LinkInstanceId == s.SourceLinkInstanceId) ??
                sources.FirstOrDefault(x => !string.IsNullOrEmpty(s.SourceRememberName) && x.RememberName == s.SourceRememberName) ??
                sources[0];

            List<Element> elementsB = chosen.IsThisModel
                ? host
                : ClashElementCollector.LinkElementsShown(_doc, view, (RevitLinkInstance)_doc.GetElement(ElementIdHelper.FromLong(chosen.LinkInstanceId)));
            List<ClashCategoryItem> rowsB = ClashElementCollector.CategoryRows(elementsB, s.CategoriesB);

            int selected = app.ActiveUIDocument.Selection.GetElementIds().Count;
            _window.ShowLists(view.Name, rowsA, sources, chosen, rowsB, selected, chosen.IsThisModel || LinkElementsInView.IsExact);
            UpdateColouredState();
        }

        // ------------------------------------------------------------------ Run

        internal void Run(UIApplication app, QuickClashSettings s)
        {
            if (_window == null) return;
            if (!TryActive3DView(app, out View3D view, out string why))
            {
                _window.ShowNotA3DView(why);
                return;
            }
            UIDocument uiDoc = app.ActiveUIDocument;

            // ---- who takes part
            List<Element> host = ClashElementCollector.HostElementsInView(_doc, view);
            List<Element> elementsA;
            if (s.UseSelectionA)
            {
                var picked = new HashSet<long>(uiDoc.Selection.GetElementIds().Select(id => id.LongValue()));
                elementsA = host.Where(e => picked.Contains(e.Id.LongValue())).ToList();
                if (elementsA.Count == 0)
                {
                    _window.SetStatus(picked.Count == 0
                        ? "Nothing is selected in Revit. Select the elements to test as Selection A, or tick categories instead."
                        : "None of the selected elements is a model element shown in this view. Select elements you can see here.");
                    return;
                }
            }
            else
            {
                var catsA = new HashSet<long>(s.CategoriesA);
                elementsA = host.Where(e => catsA.Contains(e.Category.Id.LongValue())).ToList();
            }

            var catsB = new HashSet<long>(s.CategoriesB);
            RevitLinkInstance link = null;
            List<Element> elementsB;
            if (s.SourceLinkInstanceId < 0)
            {
                elementsB = host.Where(e => catsB.Contains(e.Category.Id.LongValue())).ToList();
            }
            else
            {
                link = _doc.GetElement(ElementIdHelper.FromLong(s.SourceLinkInstanceId)) as RevitLinkInstance;
                if (link == null || link.GetLinkDocument() == null)
                {
                    _window.SetStatus("The link picked for Selection B is no longer loaded. Pick it again, or reload it in Manage Links.");
                    Refresh(app, s);
                    return;
                }
                elementsB = ClashElementCollector.LinkElementsShown(_doc, view, link)
                    .Where(e => catsB.Contains(e.Category.Id.LongValue())).ToList();
            }

            if (elementsA.Count == 0 || elementsB.Count == 0)
            {
                _window.SetStatus((elementsA.Count == 0 ? "Selection A" : "Selection B") +
                    " has no elements shown in this view. Tick categories that have a count, or widen the section box.");
                return;
            }

            Transform toHost = link?.GetTotalTransform();
            long linkId = link?.Id.LongValue() ?? -1;
            List<ClashItem> itemsA = elementsA.Select(e => ClashItem.Create(e, null, -1)).Where(i => i != null).ToList();
            List<ClashItem> itemsB = elementsB.Select(e => ClashItem.Create(e, toHost, linkId)).Where(i => i != null).ToList();

            // ---- step 1: boxes
            double growFeet = s.ClearanceOn ? s.ClearanceMm / ClashEngine.MmPerFoot : 0;
            List<KeyValuePair<ClashItem, ClashItem>> pairs = ClashEngine.CandidatePairs(itemsA, itemsB, growFeet);
            if (pairs.Count > ClashEngine.AskAbovePairs &&
                !DialogHelper.ShowYesNo(Title,
                    pairs.Count.ToString("N0", CultureInfo.InvariantCulture) + " pairs of elements are close enough to test. " +
                    "This may take a few minutes, and Revit cannot be used meanwhile (Esc stops it).\n\nRun anyway? " +
                    "A tighter section box makes it quicker."))
            {
                _window.SetStatus("Not run. Tighten the section box or tick fewer categories, then press Run.");
                return;
            }

            // ---- step 2: shapes
            ClashRunResult found = ClashEngine.Test(pairs, s);
            if (found.StoppedByEsc)
            {
                _window.SetStatus("Stopped with Esc. Nothing was changed.");
                return;
            }

            // ---- colour the view: one Undo step
            List<ClashResult> clashes = found.Clashes;
            List<ClashResult> coloured = clashes.Where(c => c.Kind != ClashKind.NotChecked).ToList();

            // A memory is only the truth while its colours are still on the view. Once the view is back to its own
            // colours (Reset, Undo, or changed by hand since), the copy is old: forget it and copy afresh, so a
            // second Run never puts back yesterday's overrides over the modeller's newer ones.
            ClashColourMemory memory = FreshOrStillColoured(view);
            View previous = ColouredView();
            ClashColourMemory previousMemory = null;
            if (previous != null && previous.Id.LongValue() != view.Id.LongValue())
            {
                previousMemory = ClashColourMemory.Find(_doc, previous);
                if (previousMemory != null && !previousMemory.StillColoured(_doc, previous))
                {
                    ClashColourMemory.Forget(_doc, previous);
                    previousMemory = null;
                }
            }

            using (var t = new Transaction(_doc, RunName))
            {
                t.Start();

                // A different view still coloured by an earlier run goes back first - never two coloured views.
                previousMemory?.PutBack(_doc, previous);

                memory.PutBack(_doc, view);                   // the true originals (if still coloured), before colouring again

                if (coloured.Count > 0)
                {
                    List<ElementId> inView = new FilteredElementCollector(_doc, view.Id)
                        .WhereElementIsNotElementType()
                        .ToElementIds()
                        .ToList();
                    memory.RememberMissing(view, inView);
                    var viewKeys = new HashSet<long>(inView.Select(id => id.LongValue()));

                    List<ElementId> idsA = InsulationLining.WithWraps(_doc,
                        coloured.Select(c => ElementIdHelper.FromLong(c.ElementAId)).Distinct(new ElementIdIntegerComparer()).ToList(), viewKeys);
                    List<ElementId> idsB = InsulationLining.WithWraps(_doc,
                        coloured.Where(c => !c.BIsLinked).Select(c => ElementIdHelper.FromLong(c.ElementBId)).Distinct(new ElementIdIntegerComparer()).ToList(), viewKeys);
                    var clashKeys = new HashSet<long>(idsA.Concat(idsB).Select(id => id.LongValue()));
                    List<ElementId> rest = inView.Where(id => !clashKeys.Contains(id.LongValue())).ToList();

                    ElementId solid = SolidFillId(_doc);
                    GraphicsElementService.ApplyOverrides(_doc, view, rest, Override(s.ColourRest, s.RestTransparency, solid));
                    GraphicsElementService.ApplyOverrides(_doc, view, idsB, Override(s.ColourB, 0, solid));
                    GraphicsElementService.ApplyOverrides(_doc, view, idsA, Override(s.ColourA, 0, solid)); // A wins
                    memory.SetColours(s.ColourA, s.ColourB, s.ColourRest);
                }

                if (t.Commit() != TransactionStatus.Committed)
                {
                    ClashOverlayServer.Instance.Hide();
                    _window.SetStatus("Revit did not keep the colours, so the view is as it was. Try again.");
                    return;
                }
            }

            if (previousMemory != null)
                ClashColourMemory.Forget(_doc, previous);
            if (coloured.Count > 0)
                memory.SaveToDisk();
            else
                ClashColourMemory.Forget(_doc, view);
            _colouredViewId = view.Id.LongValue();
            _resultsViewId = view.Id.LongValue();

            // ---- linked clash elements: the overlay
            var linkedIds = new HashSet<long>(coloured.Where(c => c.BIsLinked).Select(c => c.ElementBId));
            _linkedSolids = itemsB.Where(i => linkedIds.Contains(i.Id)).SelectMany(i => i.Solids.Select(p => p.Solid)).ToList();
            _overlayColour = s.ColourB;
            if (_linkedSolids.Count > 0)
                ClashOverlayServer.Instance.Show(_doc, view, _linkedSolids, _overlayColour);
            else
                ClashOverlayServer.Instance.Hide();
            uiDoc.RefreshActiveView();

            s.Save();
            _window.ShowResults(clashes, RunStatus(found, itemsA.Count, itemsB.Count));
            _window.SetColoured(coloured.Count > 0);
        }

        /// <summary>The view's memory if its colours are still on; otherwise a new, empty one.</summary>
        private ClashColourMemory FreshOrStillColoured(View view)
        {
            ClashColourMemory existing = ClashColourMemory.Find(_doc, view);
            if (existing != null && !existing.StillColoured(_doc, view))
                ClashColourMemory.Forget(_doc, view);
            return ClashColourMemory.FindOrCreate(_doc, view);
        }

        private static string RunStatus(ClashRunResult found, int countA, int countB)
        {
            int hard = found.Clashes.Count(c => c.Kind == ClashKind.Hard);
            int clear = found.Clashes.Count(c => c.Kind == ClashKind.Clearance);
            int notChecked = found.Clashes.Count(c => c.Kind == ClashKind.NotChecked);
            string seconds = found.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            string text = hard + clear == 0
                ? "No clashes between " + countA.ToString("N0", CultureInfo.InvariantCulture) + " and " +
                  countB.ToString("N0", CultureInfo.InvariantCulture) + " elements (" + seconds + " s). The view was not coloured."
                : (hard + clear) + " clashes found in " + seconds + " s. The view shows them in colour until you press Reset view.";
            if (found.HitLimit)
                text += " Stopped at " + ClashEngine.MaxClashes + " clashes - tighten the section box to see the rest.";
            if (notChecked > 0)
                text += " " + notChecked + " pair(s) could not be checked (Revit refused their shapes) - listed as Not checked.";
            return text;
        }

        private static OverrideGraphicSettings Override(ClashColour colour, int transparency, ElementId solidFill)
        {
            var c = new Color(colour.R, colour.G, colour.B);
            var o = new OverrideGraphicSettings();
            o.SetProjectionLineColor(c);
            o.SetCutLineColor(c);
            if (solidFill != ElementId.InvalidElementId)
            {
                o.SetSurfaceForegroundPatternId(solidFill);
                o.SetSurfaceForegroundPatternVisible(true);
                o.SetSurfaceForegroundPatternColor(c);
                o.SetCutForegroundPatternId(solidFill);
                o.SetCutForegroundPatternVisible(true);
                o.SetCutForegroundPatternColor(c);
            }
            o.SetSurfaceTransparency(Math.Max(0, Math.Min(100, transparency)));
            return o;
        }

        /// <summary>The drafting solid fill - the one view overrides expect (as Highlight and Filter Pro use).</summary>
        private static ElementId SolidFillId(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(p =>
                {
                    FillPattern fp = p.GetFillPattern();
                    return fp != null && fp.IsSolidFill && fp.Target == FillPatternTarget.Drafting;
                })?.Id ?? ElementId.InvalidElementId;
        }

        // ------------------------------------------------------------------ Reset

        /// <summary>Reset view: puts every element's own overrides back (one Undo step) and removes the overlay.</summary>
        internal void Reset(UIApplication app)
        {
            View view = ColouredView() ?? app.ActiveUIDocument.ActiveView;
            ClashColourMemory memory = view != null ? ClashColourMemory.Find(_doc, view) : null;
            if (memory == null || !memory.StillColoured(_doc, view))
            {
                ClashOverlayServer.Instance.Hide();
                app.ActiveUIDocument.RefreshActiveView();
                _window?.ClearResults();
                _window?.SetColoured(false);
                _window?.SetStatus("The view already shows its own colours - nothing to put back.");
                return;
            }
            PutBack(view, memory, clearList: true);
        }

        /// <summary>The Reset transaction. The memory is kept, so an Undo of this Reset can be reset again.</summary>
        private bool PutBack(View view, ClashColourMemory memory, bool clearList)
        {
            using (var t = new Transaction(_doc, ResetName))
            {
                t.Start();
                memory.PutBack(_doc, view);
                if (t.Commit() != TransactionStatus.Committed)
                {
                    _window?.SetStatus("Revit did not keep the change, so the clash colours are still on. Try again, or press Undo.");
                    return false;
                }
            }
            ClashOverlayServer.Instance.Hide();
            _uiApp.ActiveUIDocument?.RefreshActiveView();
            if (clearList)
                _window?.ClearResults();
            _window?.SetColoured(false);
            return true;
        }

        /// <summary>A view left coloured by a Revit that closed unexpectedly: say so, and let Reset view put it back.</summary>
        private void OfferLeftoverReset(UIApplication app)
        {
            View view = app.ActiveUIDocument.ActiveView;
            if (!(view is View3D)) return;
            ClashColourMemory memory = ClashColourMemory.Find(_doc, view);
            if (memory == null) return;
            if (memory.StillColoured(_doc, view))
            {
                _colouredViewId = view.Id.LongValue();
                _window.SetColoured(true);
                _window.SetStatus("This view still has the colours of an earlier Quick Clash (Revit closed before it was put back). " +
                                  "Press Reset view to put back the view's own colours.");
            }
            else
            {
                ClashColourMemory.Forget(_doc, view);
            }
        }

        // ------------------------------------------------------------------ Zoom

        internal void Zoom(UIApplication app, ClashResult clash)
        {
            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc.Document != _doc || uiDoc.ActiveView.Id.LongValue() != _resultsViewId)
            {
                View resultsView = _doc.GetElement(ElementIdHelper.FromLong(_resultsViewId)) as View;
                _window?.SetStatus("Go back to " + (resultsView?.Name ?? "the clash view") + " to zoom to a clash.");
                return;
            }

            UIView uiView = uiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId.LongValue() == _resultsViewId);
            if (uiView != null && clash.BoxMin != null && clash.BoxMax != null)
            {
                var pad = new XYZ(3.28, 3.28, 3.28);   // about 1 m around the clash
                uiView.ZoomAndCenterRectangle(clash.BoxMin - pad, clash.BoxMax + pad);
            }

            ElementId a = ElementIdHelper.FromLong(clash.ElementAId);
            if (_doc.GetElement(a) != null)
                uiDoc.Selection.SetElementIds(new List<ElementId> { a });
            _window?.SetStatus("Clash " + clash.Number + ": " + clash.NameA + "  -  " + clash.NameB);
        }

        // ------------------------------------------------------------------ colours

        /// <summary>Revit's own colour picker for one of the three swatches (0 = A, 1 = B, 2 = rest).</summary>
        internal void PickColour(UIApplication app, int which, ClashColour current)
        {
            using (var dialog = new ColorSelectionDialog())
            {
                dialog.OriginalColor = new Color(current.R, current.G, current.B);
                if (dialog.Show() != ItemSelectionDialogResult.Confirmed)
                    return;
                Color c = dialog.SelectedColor;
                _window?.SetColour(which, new ClashColour(c.Red, c.Green, c.Blue));
            }
        }

        // ------------------------------------------------------------------ Revit events

        private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            if (!e.GetDocument().Equals(_doc)) return;
            if (e.Operation == UndoOperation.TransactionUndone || e.Operation == UndoOperation.TransactionRedone)
                Post(app => UpdateColouredState());   // the overlay is switched on the ExternalEvent, not in here
        }

        /// <summary>Looks at what is really on the view, and matches the overlay and the Reset button to it.</summary>
        private void UpdateColouredState()
        {
            View view = ColouredView();
            ClashColourMemory memory = view != null ? ClashColourMemory.Find(_doc, view) : null;
            bool coloured = memory != null && memory.StillColoured(_doc, view);
            _window?.SetColoured(coloured);
            try
            {
                if (coloured && _linkedSolids.Count > 0)
                    ClashOverlayServer.Instance.Show(_doc, view, _linkedSolids, _overlayColour);
                else if (!coloured)
                    ClashOverlayServer.Instance.Hide();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // e.g. the link was reloaded since the Run and its old shapes are gone: drop the overlay.
                _linkedSolids = new List<Solid>();
                ClashOverlayServer.Instance.Hide();
            }
            _uiApp.ActiveUIDocument?.RefreshActiveView();
        }

        private void OnDocumentSaving(object sender, DocumentSavingEventArgs e) => PutBackBeforeWriting(e.Document, "saving");
        private void OnDocumentSavingAs(object sender, DocumentSavingAsEventArgs e) => PutBackBeforeWriting(e.Document, "saving");
        private void OnSynchronizing(object sender, DocumentSynchronizingWithCentralEventArgs e) => PutBackBeforeWriting(e.Document, "Sync with Central");

        /// <summary>Ajmal's rule: no clash colour ever reaches the file or central. The list stays; Run colours again.</summary>
        private void PutBackBeforeWriting(Document doc, string what)
        {
            if (doc == null || !doc.Equals(_doc)) return;
            try
            {
                View view = ColouredView();
                ClashColourMemory memory = view != null ? ClashColourMemory.Find(_doc, view) : null;
                if (memory == null || !memory.StillColoured(_doc, view)) return;
                if (PutBack(view, memory, clearList: false))
                    _window?.SetStatus("The view was put back before " + what + ", so no clash colours went into the file. Press Run to colour the clashes again.");
                else
                    _window?.SetStatus("Quick Clash could not put the view back before " + what + ", so the clash colours went into the file. Press Reset view, then save again.");
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                // Never let this stop Revit's own save or sync. Say what happened instead.
                _window?.SetStatus("Quick Clash could not put the view back before " + what + " (Revit said: " + ex.Message +
                                   "), so the clash colours went into the file. Press Reset view, then save again.");
            }
        }

        private void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            if (e.Document != null && e.Document.Equals(_doc))
                Shutdown();   // unsaved changes are thrown away by Revit; a save was already put back above
        }

        private void OnApplicationClosing(object sender, ApplicationClosingEventArgs e) => Shutdown();

        private void OnViewActivated(object sender, ViewActivatedEventArgs e)
        {
            if (_window != null)
                Refresh(_uiApp, null);
        }

        // ------------------------------------------------------------------ helpers

        private View ColouredView()
        {
            if (_colouredViewId < 0) return null;
            return _doc.GetElement(ElementIdHelper.FromLong(_colouredViewId)) as View;
        }

        private bool TryActive3DView(UIApplication app, out View3D view, out string why)
        {
            view = null;
            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc == null || !uiDoc.Document.Equals(_doc))
            {
                why = "Quick Clash is open for " + _doc.Title + ". Switch back to that model, or close Quick Clash and open it again here.";
                return false;
            }
            view = uiDoc.ActiveView as View3D;
            if (view == null || view.IsTemplate)
            {
                why = "Open a 3D view to run a clash test. Reset view still puts back the view Quick Clash coloured.";
                view = null;
                return false;
            }
            why = null;
            return true;
        }
    }
}
