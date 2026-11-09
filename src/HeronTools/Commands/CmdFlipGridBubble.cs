// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Toggles which end of a grid or level shows its bubble in the active view, either by picking datums one-by-one (instant flip) or by window-selecting a batch.

using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using HeronTools.UI;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    /// <summary>
    /// Flips the bubble end of grids and levels in the active view, one-by-one or by window selection.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdFlipGridBubble : IExternalCommand
    {
        private const string Title = "Flip Bubbles";   // same name as the ribbon button
        private const string TransactionName = "Flip Grid / Level Bubble";

        private enum SelectionMode
        {
            Single,
            Window,
            Cancel
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null)
                {
                    DialogHelper.ShowError(Title, "Open a project view before running this command.");
                    return Result.Cancelled;
                }

                Document doc = uidoc.Document;
                if (doc.IsFamilyDocument)
                {
                    DialogHelper.ShowError(Title, "This tool runs in a project, not the Family Editor.");
                    return Result.Cancelled;
                }

                View view = doc.ActiveView;
                if (view == null || view.IsTemplate)
                {
                    DialogHelper.ShowError(Title, "Run this tool in a normal project view.");
                    return Result.Cancelled;
                }

                if (!IsSupportedViewType(view.ViewType))
                {
                    DialogHelper.ShowError(Title, "This tool only works in plan, section, elevation or detail views.");
                    return Result.Cancelled;
                }

                SelectionMode mode = PromptSelectionMode(commandData.Application.MainWindowHandle, view.Name);
                if (mode == SelectionMode.Cancel)
                    return Result.Cancelled;

                var filter = new DatumSelectionFilter();
                int flippedCount;

                // The whole run - every pick or window - is one Undo step: a TransactionGroup may stay
                // open across picks; each flip still commits as it goes so it shows at once.
                using (TransactionGroup group = new TransactionGroup(doc, TransactionName))
                {
                    group.Start();
                    flippedCount = mode == SelectionMode.Single
                        ? HandleSinglePickLoop(uidoc, doc, view, filter)
                        : HandleWindowSelection(uidoc, doc, view, filter);

                    if (flippedCount > 0)
                        group.Assimilate();
                    else
                        group.RollBack();
                }

                // Normal success is silent - flipped bubbles are immediately visible in the view.
                return flippedCount == 0 ? Result.Cancelled : Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                // The group was not assimilated, so it rolled back: nothing from this run is kept.
                DialogHelper.ShowError(Title, "Flip Bubbles stopped, so nothing was changed. Revit said: " + ex.Message);
                return Result.Cancelled;
            }
        }

        /// <summary>
        /// Picks datums one-by-one, flipping each instantly until the user presses Esc. Each flip commits
        /// at once (so it shows); the caller's TransactionGroup makes the whole run one Undo step.
        /// </summary>
        private static int HandleSinglePickLoop(UIDocument uidoc, Document doc, View view, DatumSelectionFilter filter)
        {
            int flippedCount = 0;
            const string prompt = "Pick grids/levels to flip bubbles (Esc to finish)";

            while (true)
            {
                Reference pickedRef;
                try
                {
                    pickedRef = uidoc.Selection.PickObject(ObjectType.Element, filter, prompt);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }

                DatumPlane datum = doc.GetElement(pickedRef.ElementId) as DatumPlane;
                if (datum == null || !datum.IsValidObject || !datum.CanBeVisibleInView(view))
                    continue;

                if (TryFlipInTransaction(doc, datum, view))
                    flippedCount++;
            }

            return flippedCount;
        }

        /// <summary>
        /// Window-selects datums and flips each rectangle's worth of bubbles in a single undo step,
        /// repeating until the user presses Esc.
        /// </summary>
        private static int HandleWindowSelection(UIDocument uidoc, Document doc, View view, DatumSelectionFilter filter)
        {
            int flippedCount = 0;
            const string prompt = "Drag a window to select grids/levels (Esc to finish)";

            while (true)
            {
                IList<Element> picked;
                try
                {
                    picked = uidoc.Selection.PickElementsByRectangle(filter, prompt);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }

                var datums = new List<DatumPlane>();
                foreach (Element elem in picked)
                {
                    if (elem is DatumPlane datum && datum.IsValidObject && datum.CanBeVisibleInView(view))
                        datums.Add(datum);
                }

                if (datums.Count == 0)
                    continue;

                flippedCount += FlipBatchInTransaction(doc, datums, view);
            }

            return flippedCount;
        }

        private static bool IsSupportedViewType(ViewType viewType)
        {
            return viewType == ViewType.FloorPlan
                   || viewType == ViewType.CeilingPlan
                   || viewType == ViewType.EngineeringPlan
                   || viewType == ViewType.AreaPlan
                   || viewType == ViewType.Section
                   || viewType == ViewType.Elevation
                   || viewType == ViewType.Detail;
        }

        /// <summary>
        /// Moves the bubble to the opposite end of the datum in this view.
        /// </summary>
        private static bool Flip(DatumPlane datum, View view)
        {
            bool end0Visible = datum.IsBubbleVisibleInView(DatumEnds.End0, view);
            bool end1Visible = datum.IsBubbleVisibleInView(DatumEnds.End1, view);

            ApplyFlip(datum, view, end0Visible, end1Visible);

            // Read it back: a datum a view template controls can take the call and still not change.
            return datum.IsBubbleVisibleInView(DatumEnds.End0, view) != end0Visible ||
                   datum.IsBubbleVisibleInView(DatumEnds.End1, view) != end1Visible;
        }

        private static void ApplyFlip(DatumPlane datum, View view, bool end0Visible, bool end1Visible)
        {

            if (end0Visible && !end1Visible)
            {
                datum.HideBubbleInView(DatumEnds.End0, view);
                datum.ShowBubbleInView(DatumEnds.End1, view);
                return;
            }

            if (end1Visible && !end0Visible)
            {
                datum.HideBubbleInView(DatumEnds.End1, view);
                datum.ShowBubbleInView(DatumEnds.End0, view);
                return;
            }

            if (!end0Visible && !end1Visible)
            {
                datum.ShowBubbleInView(DatumEnds.End0, view);
                return;
            }

            datum.HideBubbleInView(DatumEnds.End1, view);
        }

        /// <summary>
        /// Flips a single datum inside its own transaction (one undo step).
        /// </summary>
        private static bool TryFlipInTransaction(Document doc, DatumPlane datum, View view)
        {
            using (Transaction t = new Transaction(doc, TransactionName))
            {
                t.Start();
                try
                {
                    if (Flip(datum, view))
                    {
                        return t.Commit() == TransactionStatus.Committed;
                    }
                }
                catch
                {
                    // Skip a datum this view will not let us change (e.g. owned by another user).
                }

                t.RollBack();
                return false;
            }
        }

        /// <summary>
        /// Flips a batch of datums inside one transaction so the whole window-select is a single undo
        /// step. Datums that cannot be changed are skipped. Returns how many were flipped.
        /// </summary>
        private static int FlipBatchInTransaction(Document doc, IList<DatumPlane> datums, View view)
        {
            int flipped = 0;

            using (Transaction t = new Transaction(doc, TransactionName))
            {
                t.Start();
                foreach (DatumPlane datum in datums)
                {
                    try
                    {
                        if (Flip(datum, view))
                            flipped++;
                    }
                    catch
                    {
                        // Skip a datum this view will not let us change; keep flipping the rest.
                    }
                }

                if (flipped == 0)
                {
                    t.RollBack();
                    return 0;
                }

                if (t.Commit() != TransactionStatus.Committed)
                    return 0;
            }

            return flipped;
        }

        /// <summary>The Heron chooser window: pick one by one, or window select. Nothing is changed by it.</summary>
        private static SelectionMode PromptSelectionMode(System.IntPtr revitWindow, string viewName)
        {
            var window = new FlipBubblesWindow(viewName);
            new WindowInteropHelper(window) { Owner = revitWindow };
            window.ShowDialog();

            switch (window.Chosen)
            {
                case FlipBubblesWindow.PickMode.OneByOne: return SelectionMode.Single;
                case FlipBubblesWindow.PickMode.Window: return SelectionMode.Window;
                default: return SelectionMode.Cancel;
            }
        }
    }
}
