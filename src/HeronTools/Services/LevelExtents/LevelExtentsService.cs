// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Copies one source level's 3D extents onto target levels picked one-by-one, so the targets match the source length across every view that shows them.

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

                int updatedCount = 0;
                int refusedCount = 0;

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

                            if (TryApplySourceExtentsAllViews(sourceLevel, targetLevel, datumViews) &&
                                t.Commit() == TransactionStatus.Committed)
                            {
                                updatedCount++;
                            }
                            else
                            {
                                if (t.GetStatus() == TransactionStatus.Started)
                                    t.RollBack();
                                refusedCount++;
                            }
                        }
                    }

                    if (updatedCount > 0)
                        group.Assimilate();
                    else
                        group.RollBack();
                }

                if (updatedCount == 0)
                {
                    DialogHelper.ShowInfo(title, refusedCount == 0
                        ? "No target levels were picked, so nothing changed."
                        : $"No level was updated: Revit would not take the new extents for the {refusedCount} level(s) picked.");
                    return Result.Cancelled;
                }

                if (refusedCount > 0)
                {
                    DialogHelper.ShowInfo(title, $"{updatedCount} level(s) matched. {refusedCount} level(s) could not be matched " +
                        "(Revit would not take the new extents for them).");
                }

                // Otherwise success is silent - the matched levels update visibly in their views.
                return Result.Succeeded;
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
        /// changed: a level end a modeller made 2D in some view stays 2D there.
        /// </summary>
        private static bool TryApplySourceExtentsAllViews(Level sourceLevel, Level targetLevel, List<View> datumViews)
        {
            bool anyApplied = false;

            foreach (View view in datumViews)
            {
                if (!TryGetCurve(sourceLevel, view, DatumExtentType.Model, out Curve sourceCurve) ||
                    !TryGetCurve(targetLevel, view, DatumExtentType.Model, out Curve targetCurve))
                    continue;

                try
                {
                    // Measured from the two curves themselves, so levels whose types use different
                    // elevation bases (Project / Shared) still line up.
                    double zOffset = targetCurve.GetEndPoint(0).Z - sourceCurve.GetEndPoint(0).Z;
                    Curve newCurve = sourceCurve.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, zOffset)));
                    targetLevel.SetCurveInView(DatumExtentType.Model, view, newCurve);
                    anyApplied = true;
                }
                catch (Exception)
                {
                    // This view would not take it (e.g. the curve is not in its plane); try the next.
                }
            }

            return anyApplied;
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
            catch
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
}
