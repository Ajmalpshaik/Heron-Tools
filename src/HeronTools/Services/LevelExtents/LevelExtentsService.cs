// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Copies one source level's 3D extents onto target levels picked one-by-one, so the targets match the source length across every view that shows them. Also holds the end-of-run report both level extents tools share.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using HeronTools.Utils;

namespace HeronTools.Services.LevelExtents
{
    /// <summary>
    /// Applies source level 3D/model extents to picked shorter target levels.
    /// </summary>
    internal static class LevelExtentsService
    {
        internal static Result Execute(ExternalCommandData commandData, string title)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null)
                    return Fail(title, "Open a project view before running this command.");

                Document doc = uidoc.Document;
                if (doc.IsFamilyDocument)
                    return Fail(title, "This tool runs in a project, not the Family Editor.");

                View view = doc.ActiveView;
                if (view == null || view.IsTemplate)
                    return Fail(title, "Please run this tool in a normal project view.");

                Level sourceLevel;
                try
                {
                    sourceLevel = PickSourceLevel(uidoc, doc);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (sourceLevel == null)
                    return Result.Cancelled;

                List<View> datumViews = CollectDatumViewsForLevel(doc, sourceLevel);
                if (datumViews.Count == 0)
                    return Fail(title, "Could not find any 3D/elevation/section view that shows the source level.");

                // Counts what each picked level took, view by view, so a partial match is never silent.
                LevelExtentsReport report = new LevelExtentsReport();

                // One Undo step for the whole run: a TransactionGroup may stay open across picks.
                using (TransactionGroup group = new TransactionGroup(doc, "Match Level Extents"))
                {
                    group.Start();

                    while (true)
                    {
                        Level targetLevel;
                        try
                        {
                            targetLevel = PickTargetLevel(uidoc, doc);
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break;
                        }

                        if (targetLevel == null || targetLevel.Id == sourceLevel.Id)
                            continue;

                        using (Transaction t = new Transaction(doc, "Match Level Extents"))
                        {
                            t.Start();

                            List<string> problems = new List<string>();
                            int viewsChanged = ApplySourceExtentsAllViews(sourceLevel, targetLevel, datumViews, problems);

                            if (viewsChanged > 0 && t.Commit() == TransactionStatus.Committed)
                            {
                                report.AddLevel(targetLevel.Name, viewsChanged, problems);
                            }
                            else
                            {
                                if (t.GetStatus() == TransactionStatus.Started)
                                    t.RollBack();

                                if (viewsChanged > 0)
                                    report.AddRefusedLevel(targetLevel.Name, "Revit did not accept the change " +
                                        "(in a workshared model, a colleague may own this level).");
                                else if (problems.Count == 0)
                                    report.AddRefusedLevel(targetLevel.Name, "no view shows both this level and the source level.");
                                else
                                    report.AddLevel(targetLevel.Name, 0, problems);
                            }
                        }
                    }

                    if (report.Changed > 0)
                        group.Assimilate();
                    else
                        group.RollBack();
                }

                if (report.Changed == 0 && !report.HasProblems)
                {
                    DialogHelper.ShowInfo(title, "No target levels were picked, so nothing changed.");
                    return Result.Cancelled;
                }

                // A message only when something was not matched; a full success is silent -
                // the matched levels update visibly in their views.
                if (report.HasProblems)
                    report.Show(title, "matched to the source level");

                return report.Changed > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                // The group was not assimilated, so it rolled back: nothing from this run is kept.
                DialogHelper.ShowError(title, "Match Level Extents stopped, so nothing was changed. Revit said: " + ex.Message);
                return Result.Cancelled;
            }
        }

        private static Level PickSourceLevel(UIDocument uidoc, Document doc)
        {
            Reference sourceRef = uidoc.Selection.PickObject(
                ObjectType.Element,
                new LevelSelectionFilter(),
                "Select SOURCE level (maximum 3D extent)");

            return doc.GetElement(sourceRef) as Level;
        }

        private static Level PickTargetLevel(UIDocument uidoc, Document doc)
        {
            Reference targetRef = uidoc.Selection.PickObject(
                ObjectType.Element,
                new LevelSelectionFilter(),
                "Select TARGET level to apply source 3D extent (Esc to finish)");

            return doc.GetElement(targetRef) as Level;
        }

        /// <summary>
        /// Gives the target level the source level's 3D (model) extents, view by view. Only the 3D extent is
        /// changed: a level end a modeller made 2D in some view stays 2D there. Returns how many views took it;
        /// each view that did not is added to <paramref name="problems"/> in plain words.
        /// </summary>
        private static int ApplySourceExtentsAllViews(Level sourceLevel, Level targetLevel, List<View> datumViews,
            List<string> problems)
        {
            int viewsChanged = 0;

            foreach (View view in datumViews)
            {
                // No 3D source line in this view: there is nothing to copy here.
                if (!TryGetCurve(sourceLevel, view, DatumExtentType.Model, out Curve sourceCurve))
                    continue;

                if (!TryGetCurve(targetLevel, view, DatumExtentType.Model, out Curve targetCurve))
                {
                    problems.Add(view.Name + ": this level is not shown here (hidden or cropped out), so it was not matched.");
                    continue;
                }

                try
                {
                    // Measured from the two curves themselves, so levels whose types use different
                    // elevation bases (Project / Shared) still line up.
                    double zOffset = targetCurve.GetEndPoint(0).Z - sourceCurve.GetEndPoint(0).Z;
                    Curve newCurve = sourceCurve.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, zOffset)));
                    targetLevel.SetCurveInView(DatumExtentType.Model, view, newCurve);
                    viewsChanged++;
                }
                catch (Exception ex)
                {
                    // This view would not take it (scope box, curve not in its plane, ...): counted and reported.
                    problems.Add(view.Name + ": Revit refused the new extents (" + ex.Message + ")");
                }
            }

            return viewsChanged;
        }

        private static List<View> CollectDatumViewsForLevel(Document doc, Level level)
        {
            List<View> result = new List<View>();
            IEnumerable<View> candidates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate);

            foreach (View v in candidates)
            {
                if (TryGetCurve(level, v, DatumExtentType.Model, out _) ||
                    TryGetCurve(level, v, DatumExtentType.ViewSpecific, out _))
                {
                    result.Add(v);
                }
            }
            return result;
        }

        private static bool TryGetCurve(Level level, View view, DatumExtentType extentType, out Curve curve)
        {
            curve = null;

            try
            {
                IList<Curve> curves = level.GetCurvesInView(extentType, view);
                if (curves == null || curves.Count == 0)
                    return false;

                curve = curves.FirstOrDefault();
                return curve != null && curve.IsBound;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        private static Result Fail(string title, string message)
        {
            DialogHelper.ShowError(title, message);
            return Result.Cancelled;   // our own message is the whole story - no second Revit failure box
        }

        private class LevelSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Level;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// What a level extents run did, level by level, so a partial result is reported in plain words
    /// (the way Reset Datums reports refused ends). Shared by Match Level Extents and Levels to Section Box.
    /// </summary>
    internal sealed class LevelExtentsReport
    {
        private readonly List<string> _details = new List<string>();

        /// <summary>Levels changed in every view that shows them.</summary>
        internal int Full { get; private set; }

        /// <summary>Levels changed in some views but not others.</summary>
        internal int Partly { get; private set; }

        /// <summary>Levels not changed in any view.</summary>
        internal int NotChanged { get; private set; }

        /// <summary>Level/view pairs Revit refused or that could not be written.</summary>
        internal int ProblemPairs { get; private set; }

        internal int Changed => Full + Partly;

        internal bool HasProblems => Partly > 0 || NotChanged > 0;

        /// <summary>Records one level: how many views took the change, and one line per view that did not.</summary>
        internal void AddLevel(string levelName, int viewsChanged, IList<string> viewProblems)
        {
            if (viewProblems.Count == 0)
            {
                if (viewsChanged > 0)
                    Full++;
                return;
            }

            ProblemPairs += viewProblems.Count;
            string state = viewsChanged > 0 ? "partly matched" : "not changed";
            if (viewsChanged > 0) Partly++; else NotChanged++;

            foreach (string problem in viewProblems)
                _details.Add(levelName + " (" + state + ") - " + problem);
        }

        /// <summary>Records a level that was not changed at all, for a reason that is not one view.</summary>
        internal void AddRefusedLevel(string levelName, string reason)
        {
            NotChanged++;
            _details.Add(levelName + " (not changed) - " + reason);
        }

        /// <summary>Shows the end-of-run report. Call only when <see cref="HasProblems"/> is true.</summary>
        internal void Show(string title, string doneWhat)
        {
            List<string> lines = new List<string>();
            if (Full > 0)
                lines.Add($"{Full} level(s) {doneWhat} in every view that shows them.");
            if (Partly > 0)
                lines.Add($"{Partly} level(s) partly matched: some views took the new extents, others did not.");
            if (NotChanged > 0)
                lines.Add($"{NotChanged} level(s) not changed at all.");
            if (ProblemPairs > 0)
                lines.Add($"{ProblemPairs} level/view pair(s) were refused by Revit or could not be written.");
            lines.Add("Common causes: the level is held by a scope box, owned by a colleague, or not shown in that view. " +
                "Show details lists each level and view.");

            DialogHelper.ShowReport(
                title,
                Changed == 0 ? "No level was changed" : "Some levels were not fully changed",
                string.Join(Environment.NewLine, lines),
                string.Join(Environment.NewLine, _details),
                true);
        }
    }
}
