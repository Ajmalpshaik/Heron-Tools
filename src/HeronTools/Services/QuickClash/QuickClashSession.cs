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
        private const string IsolateName = "Quick Clash - Isolate";
        private const string ShowAllName = "Quick Clash - Show all";

        private readonly UIApplication _uiApp;
        private readonly Document _doc;
        private readonly QuickClashRequestHandler _handler;
        private readonly ExternalEvent _event;
        private QuickClashWindow _window;

        /// <summary>The view the last Run coloured (its memory belongs to this session), or -1.</summary>
        private long _colouredViewId = -1;

        /// <summary>The model's memory key while a Save As runs (its path, or its title if never saved).</summary>
        private string _docKeyBeforeSaveAs;

        /// <summary>The linked clash elements' solids the overlay shows now (all of them, or only the isolated clashes').</summary>
        private List<Solid> _linkedSolids = new List<Solid>();

        /// <summary>Every linked clash element's solids of the last Run, by linked element id.</summary>
        private Dictionary<long, List<Solid>> _linkedSolidsById = new Dictionary<long, List<Solid>>();
        private ClashColour _overlayColour;

        /// <summary>The Temporary Hide/Isolate on the results view is Quick Clash's own (Isolate), not the modeller's.</summary>
        private bool _isolatedByUs;

        /// <summary>The view Quick Clash isolated (may differ from the results view after a Run elsewhere), or -1.</summary>
        private long _isolatedViewId = -1;

        /// <summary>
        /// The elements Quick Clash isolated, and a sample of the view's other elements it hid. The isolate is taken as
        /// Quick Clash's own only while Revit still shows the first and hides the second - Revit raises no event for its
        /// own "Reset Temporary Hide/Isolate", and a new isolate of the modeller's would show or hide other elements.
        /// </summary>
        private List<ElementId> _isolatedIds = new List<ElementId>();
        private List<ElementId> _hiddenSample = new List<ElementId>();
        private const int HiddenSampleSize = 200;

        /// <summary>The view the results list belongs to.</summary>
        private long _resultsViewId = -1;

        /// <summary>
        /// "Current selection" as Selection A: the element ids the last Run tested, or null. Clicking a clash selects its
        /// two elements in Revit, so a later Run reuses these while the selection is still only what a click selected.
        /// </summary>
        private HashSet<long> _selectionAKeys;

        /// <summary>Revit's selection just after Quick Clash itself selected clash elements (a click or Select), or null.</summary>
        private HashSet<long> _clickSelectionKeys;

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
                // An isolate left on (e.g. the colours were undone by hand) goes too: the window takes Show all with it.
                if (!EndOwnIsolate())
                    return;
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
            _uiApp.Application.DocumentSavedAs += OnDocumentSavedAs;
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
            _uiApp.Application.DocumentSavedAs -= OnDocumentSavedAs;
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

        /// <summary>
        /// Run is one Undo step "Quick Clash", even when it first ends Quick Clash's isolate (its own small transaction):
        /// the group folds them together.
        /// </summary>
        internal void Run(UIApplication app, QuickClashSettings s)
        {
            using (var group = new TransactionGroup(_doc, RunName))
            {
                group.Start();
                try
                {
                    RunCore(app, s);
                }
                finally
                {
                    if (group.GetStatus() == TransactionStatus.Started)
                        group.Assimilate();   // what committed inside stays; an empty group leaves no Undo step
                }
            }
        }

        private void RunCore(UIApplication app, QuickClashSettings s)
        {
            if (_window == null) return;
            if (!TryActive3DView(app, out View3D view, out string why))
            {
                _window.ShowNotA3DView(why);
                return;
            }
            UIDocument uiDoc = app.ActiveUIDocument;

            // Quick Clash's own Isolate hides elements; a new test must see the whole view, so it ends first
            // (wherever it is - an isolate left on another view would otherwise be forgotten).
            if (!EndOwnIsolate())
                return;

            // ---- who takes part
            List<Element> host = ClashElementCollector.HostElementsInView(_doc, view);
            List<Element> elementsA;
            string selectionANote = "";
            if (s.UseSelectionA)
            {
                var current = new HashSet<long>(uiDoc.Selection.GetElementIds().Select(id => id.LongValue()));
                // The selection is only what a clash click selected: keep testing the Selection A of the last Run.
                bool reuse = _selectionAKeys != null && _clickSelectionKeys != null &&
                             current.Count > 0 && current.SetEquals(_clickSelectionKeys);
                HashSet<long> picked = reuse ? _selectionAKeys : current;
                elementsA = host.Where(e => picked.Contains(e.Id.LongValue())).ToList();
                if (elementsA.Count == 0)
                {
                    _window.SetStatus(reuse
                        ? "The Selection A of the last Run is no longer shown in this view. Select the elements to test again, then press Run."
                        : picked.Count == 0
                        ? "Nothing is selected in Revit. Select the elements to test as Selection A, or tick categories instead."
                        : "None of the selected elements is a model element shown in this view. Select elements you can see here.");
                    return;
                }
                _selectionAKeys = new HashSet<long>(picked);
                string count = elementsA.Count.ToString("N0", CultureInfo.InvariantCulture);
                selectionANote = reuse
                    ? " Selection A: the same " + count + " element(s) as the last Run (the clash elements Quick Clash selected were not used). Select new elements to change it."
                    : " Selection A: the " + count + " selected element(s).";
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
                    // Refresh first: it rewrites the status line with the counts, which would hide why
                    // nothing ran.
                    Refresh(app, s);
                    _window.SetStatus("The link picked for Selection B is no longer loaded. Pick it again, or reload it in Manage Links.");
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

                    ElementId solid = ClashColourMemory.SolidFillId(_doc);
                    GraphicsElementService.ApplyOverrides(_doc, view, rest, ClashColourMemory.QuickClashOverride(s.ColourRest, s.RestTransparency, solid));
                    GraphicsElementService.ApplyOverrides(_doc, view, idsB, ClashColourMemory.QuickClashOverride(s.ColourB, 0, solid));
                    GraphicsElementService.ApplyOverrides(_doc, view, idsA, ClashColourMemory.QuickClashOverride(s.ColourA, 0, solid)); // A wins
                    memory.SetColours(s.ColourA, s.ColourB, s.ColourRest, s.RestTransparency, solid);
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
            _linkedSolidsById = itemsB.Where(i => linkedIds.Contains(i.Id))
                .ToDictionary(i => i.Id, i => i.Solids.Select(p => p.Solid).ToList());
            _linkedSolids = _linkedSolidsById.Values.SelectMany(x => x).ToList();
            _overlayColour = s.ColourB;
            if (_linkedSolids.Count > 0)
                ClashOverlayServer.Instance.Show(_doc, view, _linkedSolids, _overlayColour);
            else
                ClashOverlayServer.Instance.Hide();
            uiDoc.RefreshActiveView();

            s.Save();
            _window.ShowResults(clashes, RunStatus(found, itemsA.Count, itemsB.Count) + selectionANote);
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

        /// <summary>
        /// The Reset transaction. The memory is marked put back but kept for this session, so an Undo of this Reset
        /// can be reset again; it counts as coloured again only when the full Quick Clash override is back on.
        /// </summary>
        private bool PutBack(View view, ClashColourMemory memory, bool clearList)
        {
            bool endIsolate = OwnIsolateOn() && _isolatedViewId == view.Id.LongValue();
            using (var t = new Transaction(_doc, ResetName))
            {
                t.Start();
                memory.PutBack(_doc, view);
                if (endIsolate)
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);   // the whole view shows again
                if (t.Commit() != TransactionStatus.Committed)
                {
                    _window?.SetStatus("Revit did not keep the change, so the clash colours are still on. Try again, or press Undo.");
                    return false;
                }
            }
            memory.MarkPutBack();
            if (endIsolate)
                ForgetIsolate();
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

        // ------------------------------------------------------------------ the picked clashes: zoom, select, isolate

        /// <summary>
        /// Clicking clashes in the list (like Navisworks): zoom to them and select both elements of each in Revit.
        /// </summary>
        internal void Zoom(UIApplication app, List<ClashResult> clashes)
        {
            if (clashes.Count == 0 || !InResultsView(app, "zoom to a clash", out UIDocument uiDoc, out _))
                return;

            UIView uiView = uiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId.LongValue() == _resultsViewId);
            List<ClashResult> boxed = clashes.Where(c => c.BoxMin != null && c.BoxMax != null).ToList();
            if (uiView != null && boxed.Count > 0)
            {
                var min = new XYZ(boxed.Min(c => c.BoxMin.X), boxed.Min(c => c.BoxMin.Y), boxed.Min(c => c.BoxMin.Z));
                var max = new XYZ(boxed.Max(c => c.BoxMax.X), boxed.Max(c => c.BoxMax.Y), boxed.Max(c => c.BoxMax.Z));
                var pad = new XYZ(3.28, 3.28, 3.28);   // about 1 m around the clashes
                uiView.ZoomAndCenterRectangle(min - pad, max + pad);
            }

            int notSelected = SelectElements(uiDoc, clashes);
            ClashResult first = clashes[0];
            string text = clashes.Count == 1
                ? "Clash " + first.Number + ": " + first.NameA + "  -  " + first.NameB
                : clashes.Count + " clashes picked, their elements selected in Revit.";
            if (_isolatedByUs)
                text += " Isolate is on: press Isolate to show only these, or Show all.";
            _window?.SetStatus(text + LinkedNotSelectedNote(notSelected));
        }

        /// <summary>Select: the elements of the picked clashes (all clashes when none is picked) become Revit's selection.</summary>
        internal void SelectInRevit(UIApplication app, List<ClashResult> clashes)
        {
            if (clashes.Count == 0) return;
            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc == null || !uiDoc.Document.Equals(_doc))
            {
                _window?.SetStatus("Switch back to " + _doc.Title + " to select the clash elements.");
                return;
            }
            int notSelected = SelectElements(uiDoc, clashes);
            _window?.SetStatus("The elements of " + Clashes(clashes.Count) + " are selected in Revit." + LinkedNotSelectedNote(notSelected));
        }

        /// <summary>
        /// Isolate: the view shows only the elements of the picked clashes (all clashes when none is picked), with
        /// Revit's own Temporary Hide/Isolate - so Revit's "Reset Temporary Hide/Isolate" also brings the rest back.
        /// A linked clash element keeps its link visible (Revit cannot isolate one element inside a link).
        /// </summary>
        internal void Isolate(UIApplication app, List<ClashResult> clashes)
        {
            if (clashes.Count == 0 || !InResultsView(app, "isolate clashes", out UIDocument uiDoc, out View view))
                return;

            if (!view.CanUseTemporaryVisibilityModes())
            {
                _window?.SetStatus("This view cannot use Temporary Hide/Isolate (its view template or settings lock it), so Isolate cannot work here.");
                return;
            }

            // An isolate of Quick Clash's own on another view goes first - only one is ever kept track of.
            if (_isolatedByUs && _isolatedViewId != view.Id.LongValue() && !EndOwnIsolate())
                return;

            if (view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate) && !OwnIsolateOn() &&
                !DialogHelper.ShowYesNo(Title,
                    "This view already has a Temporary Hide/Isolate of your own. Isolate replaces it, and Show all " +
                    "then shows everything.\n\nIsolate the clashes anyway?"))
            {
                _window?.SetStatus("Not isolated. Your own Temporary Hide/Isolate is unchanged.");
                return;
            }

            var bare = new List<ElementId>();       // the clash elements (and the link of a linked clash)
            var ids = new List<ElementId>();        // the same with their insulation and lining
            var bareKeys = new HashSet<long>();
            var keys = new HashSet<long>();
            foreach (ClashResult c in clashes)
            {
                AddIfThere(ElementIdHelper.FromLong(c.ElementAId), bare, bareKeys);
                AddWithWraps(ElementIdHelper.FromLong(c.ElementAId), ids, keys);
                ElementId b = ElementIdHelper.FromLong(c.BIsLinked ? c.LinkInstanceId : c.ElementBId);
                AddIfThere(b, bare, bareKeys);
                if (c.BIsLinked) AddIfThere(b, ids, keys);
                else AddWithWraps(b, ids, keys);
            }
            if (bare.Count == 0)
            {
                _window?.SetStatus("The elements of these clashes are no longer in the model. Press Run to test again.");
                return;
            }

            // Some of what the view shows now and Isolate will hide - to recognise this isolate as ours later.
            List<ElementId> sample = new FilteredElementCollector(_doc, view.Id).WhereElementIsNotElementType().ToElementIds()
                .Where(id => !keys.Contains(id.LongValue())).Take(HiddenSampleSize).ToList();

            // Revit can refuse an id it cannot isolate (e.g. insulation of a hidden category): then try without the wraps.
            List<ElementId> isolated = ids;
            string refused = TryIsolate(view, ids);
            if (refused != null && ids.Count > bare.Count)
            {
                isolated = bare;
                refused = TryIsolate(view, bare);
            }
            if (refused != null)
            {
                _window?.SetStatus("Revit did not isolate the clashes, so the view is as it was (" + refused + ").");
                return;
            }
            _isolatedByUs = true;
            _isolatedViewId = view.Id.LongValue();
            // Keep only what Revit really shows / hides now (some elements, e.g. the section box, never hide).
            try
            {
                _isolatedIds = isolated.Where(id => view.IsElementVisibleInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate, id)).ToList();
                _hiddenSample = sample.Where(id => !view.IsElementVisibleInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate, id)).ToList();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                _isolatedIds = new List<ElementId>();
                _hiddenSample = new List<ElementId>();   // no fingerprint: the view, the flag and the mode decide alone
            }

            // The overlay draws only the picked clashes' linked elements, so the isolated view shows just these.
            var linked = new HashSet<long>(clashes.Where(c => c.BIsLinked).Select(c => c.ElementBId));
            ShowOverlay(view, _linkedSolidsById.Where(kv => linked.Contains(kv.Key)).SelectMany(kv => kv.Value).ToList());
            uiDoc.RefreshActiveView();
            _window?.SetIsolated(true);
            _window?.SetStatus("Showing only " + Clashes(clashes.Count) + ". Show all brings back the rest of the view.");
        }

        /// <summary>One Undo step "Quick Clash - Isolate". Null when done; otherwise what Revit said.</summary>
        private string TryIsolate(View view, List<ElementId> ids)
        {
            try
            {
                using (var t = new Transaction(_doc, IsolateName))
                {
                    t.Start();
                    view.IsolateElementsTemporary(ids);
                    return t.Commit() == TransactionStatus.Committed ? null : "Revit did not keep the change";
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                return ex.Message;      // the transaction was rolled back by its using block
            }
        }

        /// <summary>Show all: ends Quick Clash's isolate, and the overlay draws every linked clash element again.</summary>
        internal void ShowAll(UIApplication app)
        {
            View isolated = _doc.GetElement(ElementIdHelper.FromLong(_isolatedViewId)) as View;
            bool ours = OwnIsolateOn();
            if (!EndOwnIsolate())
                return;
            if (!ours && isolated != null && isolated.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
            {
                // It shows more than Quick Clash isolated (e.g. elements drawn since, or an isolate of the modeller's): left alone.
                _window?.SetStatus("The Temporary Hide/Isolate on " + isolated.Name + " is no longer only Quick Clash's, so it was left as it is. " +
                                   "Use Revit's Reset Temporary Hide/Isolate to show everything.");
                return;
            }
            View coloured = ColouredView();
            ClashColourMemory memory = coloured != null ? ClashColourMemory.Find(_doc, coloured) : null;
            if (memory != null && memory.StillColoured(_doc, coloured))
                ShowOverlay(coloured, _linkedSolidsById.Values.SelectMany(x => x).ToList());
            app.ActiveUIDocument?.RefreshActiveView();
            Refresh(app, null);    // the Test tab's counts were of the isolated view
            _window?.SetStatus("The whole view shows again.");
        }

        /// <summary>
        /// Ends Quick Clash's own isolate, on whichever view it is (its own Undo step "Quick Clash - Show all").
        /// False if Revit refused - the status line says so and Show all stays offered.
        /// </summary>
        private bool EndOwnIsolate()
        {
            if (!OwnIsolateOn())
            {
                ForgetIsolate();
                return true;
            }
            View view = (View)_doc.GetElement(ElementIdHelper.FromLong(_isolatedViewId));
            try
            {
                using (var t = new Transaction(_doc, ShowAllName))
                {
                    t.Start();
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                    if (t.Commit() != TransactionStatus.Committed)
                    {
                        _window?.SetStatus("Revit did not show the whole of " + view.Name + " again. Use Revit's Reset Temporary Hide/Isolate there.");
                        return false;
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                _window?.SetStatus("Revit did not show the whole of " + view.Name + " again (" + ex.Message + "). Use Revit's Reset Temporary Hide/Isolate there.");
                return false;
            }
            ForgetIsolate();
            return true;
        }

        private void ForgetIsolate()
        {
            _isolatedByUs = false;
            _isolatedViewId = -1;
            _isolatedIds = new List<ElementId>();
            _hiddenSample = new List<ElementId>();
            _window?.SetIsolated(false);
        }

        /// <summary>
        /// Quick Clash's isolate is still on: its view still has a Temporary Hide/Isolate that shows what Quick Clash
        /// isolated and hides what it hid. Elements drawn since do not matter; a modeller's own isolate would.
        /// </summary>
        private bool OwnIsolateOn()
        {
            if (!_isolatedByUs) return false;
            View view = _doc.GetElement(ElementIdHelper.FromLong(_isolatedViewId)) as View;
            if (view == null || !view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
                return false;
            try
            {
                foreach (ElementId id in _isolatedIds)
                    if (_doc.GetElement(id) != null && !view.IsElementVisibleInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate, id))
                        return false;
                foreach (ElementId id in _hiddenSample)
                    if (_doc.GetElement(id) != null && view.IsElementVisibleInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate, id))
                        return false;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return false;   // cannot tell: treat it as the modeller's, so it is never ended without asking
            }
            return true;
        }

        /// <summary>Selects both elements of every clash; returns how many linked ones Revit could not select.</summary>
        private int SelectElements(UIDocument uiDoc, List<ClashResult> clashes)
        {
            var hostIds = new List<ElementId>();
            var keys = new HashSet<long>();
            var linked = new List<KeyValuePair<Element, RevitLinkInstance>>();
            var linkedKeys = new HashSet<string>();
            foreach (ClashResult c in clashes)
            {
                AddIfThere(ElementIdHelper.FromLong(c.ElementAId), hostIds, keys);
                if (!c.BIsLinked)
                {
                    AddIfThere(ElementIdHelper.FromLong(c.ElementBId), hostIds, keys);
                    continue;
                }
                if (!linkedKeys.Add(c.LinkInstanceId + ":" + c.ElementBId)) continue;
                var instance = _doc.GetElement(ElementIdHelper.FromLong(c.LinkInstanceId)) as RevitLinkInstance;
                Element inLink = instance?.GetLinkDocument()?.GetElement(ElementIdHelper.FromLong(c.ElementBId));
                linked.Add(new KeyValuePair<Element, RevitLinkInstance>(inLink, instance));
            }
            int notSelected;
            try
            {
                notSelected = LinkSelection.Select(uiDoc, hostIds, linked);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // A linked reference Revit will not select here (e.g. the link was reloaded): select this model's elements.
                try { uiDoc.Selection.SetElementIds(hostIds); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* nothing selected: the note below says so */ }
                notSelected = linked.Count;
            }
            // What Revit now reports as selected is Quick Clash's own selection, not a new Selection A of the modeller's.
            _clickSelectionKeys = new HashSet<long>(uiDoc.Selection.GetElementIds().Select(id => id.LongValue()));
            return notSelected;
        }

        private static string LinkedNotSelectedNote(int notSelected)
        {
            if (notSelected <= 0) return "";
            return LinkSelection.CanSelectLinked
                ? " " + notSelected + " linked element(s) could not be selected (the link was reloaded or unloaded)."
                : " Linked elements cannot be selected in Revit 2020-2022 - only this model's are selected.";
        }

        private static string Clashes(int count) => count == 1 ? "1 clash" : count + " clashes";

        private void AddIfThere(ElementId id, List<ElementId> into, HashSet<long> keys)
        {
            if (_doc.GetElement(id) != null && keys.Add(id.LongValue()))
                into.Add(id);
        }

        /// <summary>The element and its insulation and lining - a duct shown without its jacket would look wrong.</summary>
        private void AddWithWraps(ElementId id, List<ElementId> into, HashSet<long> keys)
        {
            if (_doc.GetElement(id) == null) return;
            AddIfThere(id, into, keys);
            foreach (ElementId wrap in WrapIds(id))
                AddIfThere(wrap, into, keys);
        }

        private IEnumerable<ElementId> WrapIds(ElementId hostId)
        {
            var wraps = new List<ElementId>();
            try { wraps.AddRange(InsulationLiningBase.GetInsulationIds(_doc, hostId)); }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { /* this element cannot carry insulation */ }
            try { wraps.AddRange(InsulationLiningBase.GetLiningIds(_doc, hostId)); }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { /* this element cannot carry lining */ }
            return wraps;
        }

        private void ShowOverlay(View view, List<Solid> solids)
        {
            _linkedSolids = solids;
            try
            {
                if (solids.Count > 0)
                    ClashOverlayServer.Instance.Show(_doc, view, solids, _overlayColour);
                else
                    ClashOverlayServer.Instance.Hide();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // the link was reloaded since the Run and its old shapes are gone: no overlay
                _linkedSolids = new List<Solid>();
                _linkedSolidsById = new Dictionary<long, List<Solid>>();
                ClashOverlayServer.Instance.Hide();
            }
        }

        /// <summary>The active view is the one the clash list belongs to (zoom and isolate only make sense there).</summary>
        private bool InResultsView(UIApplication app, string what, out UIDocument uiDoc, out View view)
        {
            uiDoc = app.ActiveUIDocument;
            view = null;
            if (uiDoc != null && uiDoc.Document.Equals(_doc) && uiDoc.ActiveView.Id.LongValue() == _resultsViewId)
            {
                view = uiDoc.ActiveView;
                return true;
            }
            View resultsView = _doc.GetElement(ElementIdHelper.FromLong(_resultsViewId)) as View;
            _window?.SetStatus("Go back to " + (resultsView?.Name ?? "the clash view") + " to " + what + ".");
            return false;
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
            if (coloured)
                memory.MarkColouredAgain();   // an Undo of a Reset: the colours are back, so is the crash copy
            _window?.SetColoured(coloured);

            // The isolate may have been undone, or reset with Revit's own sunglasses button.
            if (_isolatedByUs && !OwnIsolateOn())
                ForgetIsolate();
            _window?.SetIsolated(_isolatedByUs);

            if (!coloured)
                ShowOverlay(view, new List<Solid>());
            else if (!_isolatedByUs)
                ShowOverlay(view, _linkedSolidsById.Values.SelectMany(x => x).ToList());
            else
                ShowOverlay(view, _linkedSolids);   // still isolated: keep drawing only the isolated clashes' linked elements
            _uiApp.ActiveUIDocument?.RefreshActiveView();
        }

        private void OnDocumentSaving(object sender, DocumentSavingEventArgs e) => PutBackBeforeWriting(e.Document, "saving");
        private void OnDocumentSavingAs(object sender, DocumentSavingAsEventArgs e)
        {
            if (e.Document != null && e.Document.Equals(_doc))
                _docKeyBeforeSaveAs = ClashColourMemory.DocKey(_doc);
            PutBackBeforeWriting(e.Document, "saving");
        }

        /// <summary>Save As gave the model a new path: the view's memory moves with it, so Reset view still finds it.</summary>
        private void OnDocumentSavedAs(object sender, DocumentSavedAsEventArgs e)
        {
            if (e.Document == null || !e.Document.Equals(_doc)) return;
            string oldKey = _docKeyBeforeSaveAs ?? e.OriginalPath;
            _docKeyBeforeSaveAs = null;
            if (e.Status == RevitAPIEventStatus.Succeeded)
                ClashColourMemory.MoveDocument(oldKey, ClashColourMemory.DocKey(_doc));
        }

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
