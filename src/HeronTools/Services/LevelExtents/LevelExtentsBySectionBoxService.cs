// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Stretches every level's 3D extent to the active 3D view's section box, writing the new extents into all elevation, section, and 3D views that show each level.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Utils;

namespace HeronTools.Services.LevelExtents
{
    /// <summary>
    /// Sets all visible level 3D extents to fit the active 3D view's section box.
    /// </summary>
    internal static class LevelExtentsBySectionBoxService
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

                View3D view3D = doc.ActiveView as View3D;
                if (view3D == null || view3D.IsTemplate)
                    return Fail(title, "Run this tool from an active 3D view.");

                if (!view3D.IsSectionBoxActive)
                    return Fail(title, "The active 3D view does not have its section box enabled.");

                BoundingBoxXYZ box = view3D.GetSectionBox();
                if (box == null)
                    return Fail(title, "Could not read the section box of the active 3D view.");

                // Section box is in its own transform; convert corners to world space.
                Transform t = box.Transform;
                XYZ[] worldCorners = new XYZ[]
                {
                    t.OfPoint(new XYZ(box.Min.X, box.Min.Y, box.Min.Z)),
                    t.OfPoint(new XYZ(box.Max.X, box.Min.Y, box.Min.Z)),
                    t.OfPoint(new XYZ(box.Max.X, box.Max.Y, box.Min.Z)),
                    t.OfPoint(new XYZ(box.Min.X, box.Max.Y, box.Min.Z)),
                    t.OfPoint(new XYZ(box.Min.X, box.Min.Y, box.Max.Z)),
                    t.OfPoint(new XYZ(box.Max.X, box.Min.Y, box.Max.Z)),
                    t.OfPoint(new XYZ(box.Max.X, box.Max.Y, box.Max.Z)),
                    t.OfPoint(new XYZ(box.Min.X, box.Max.Y, box.Max.Z)),
                };

                double minX = worldCorners.Min(p => p.X);
                double maxX = worldCorners.Max(p => p.X);
                double minY = worldCorners.Min(p => p.Y);
                double maxY = worldCorners.Max(p => p.Y);

                if ((maxX - minX) <= Constants.MIN_DISTANCE_TOLERANCE ||
                    (maxY - minY) <= Constants.MIN_DISTANCE_TOLERANCE)
                    return Fail(title, "Section box has invalid X/Y extents.");

                List<Level> levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .ToList();

                if (levels.Count == 0)
                    return Fail(title, "No levels found in the project.");

                List<View> datumViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate &&
                                (v.ViewType == ViewType.Elevation ||
                                 v.ViewType == ViewType.Section ||
                                 v.ViewType == ViewType.ThreeD))
                    .ToList();

                if (datumViews.Count == 0)
                    return Fail(title, "No elevation, section, or 3D views found to write level extents.");

                int updatedCount = 0;
                using (Transaction tx = new Transaction(doc, "Maximize Levels by Section Box"))
                {
                    tx.Start();
                    foreach (Level level in levels)
                    {
                        if (ApplyBoxToLevel(level, datumViews, minX, maxX, minY, maxY))
                            updatedCount++;
                    }

                    if (updatedCount == 0)
                    {
                        tx.RollBack();
                    }
                    else if (tx.Commit() != TransactionStatus.Committed)
                    {
                        DialogHelper.ShowError(title, "Revit did not accept the new level extents, so nothing was changed. " +
                            "In a workshared model, check that no colleague owns these levels, then try again.");
                        return Result.Cancelled;
                    }
                }

                if (updatedCount == 0)
                {
                    DialogHelper.ShowInfo(
                        title,
                        "No level extents could be updated. Make sure levels are visible in elevation, " +
                        "section, or 3D views and that the section box has valid extents.");
                    return Result.Cancelled;
                }

                // Normal success is silent - the levels update visibly in elevation/section/3D views.
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError(title, "The level extents could not be changed, so nothing was changed. Revit said: " + ex.Message);
                return Result.Cancelled;
            }
        }

        private static bool ApplyBoxToLevel(Level level, List<View> datumViews, double minX, double maxX, double minY, double maxY)
        {
            bool anyApplied = false;
            double z = level.Elevation;

            foreach (View view in datumViews)
            {
                Curve existing;
                if (!TryGetCurve(level, view, DatumExtentType.Model, out existing))
                    continue;

                if (!(existing is Line line))
                    continue;

                XYZ dir = line.Direction;
                Line unbound = Line.CreateUnbound(line.GetEndPoint(0), dir);
                double minParam = double.MaxValue;
                double maxParam = double.MinValue;

                XYZ[] boxCorners = new XYZ[] 
                {
                    new XYZ(minX, minY, z),
                    new XYZ(maxX, minY, z),
                    new XYZ(maxX, maxY, z),
                    new XYZ(minX, maxY, z)
                };

                foreach (XYZ corner in boxCorners)
                {
                    IntersectionResult res = unbound.Project(corner);
                    if (res != null)
                    {
                        double p = res.Parameter;
                        if (p < minParam) minParam = p;
                        if (p > maxParam) maxParam = p;
                    }
                }

                if (maxParam - minParam <= Constants.MIN_DISTANCE_TOLERANCE)
                    continue;

                try
                {
                    // Only the 3D extent changes: a level end a modeller made 2D in some view stays 2D.
                    XYZ newP0 = unbound.Evaluate(minParam, false);
                    XYZ newP1 = unbound.Evaluate(maxParam, false);
                    Line newLine = Line.CreateBound(newP0, newP1);
                    level.SetCurveInView(DatumExtentType.Model, view, newLine);
                    anyApplied = true;
                }
                catch
                {
                    // skip this view
                }
            }

            return anyApplied;
        }

        private static bool TryGetCurve(Level level, View view, DatumExtentType extentType, out Curve curve)
        {
            curve = null;
            try
            {
                IList<Curve> curves = level.GetCurvesInView(extentType, view);
                if (curves == null || curves.Count == 0) return false;
                curve = curves.FirstOrDefault();
                return curve != null && curve.IsBound;
            }
            catch { return false; }
        }

        private static Result Fail(string title, string message)
        {
            DialogHelper.ShowError(title, message);
            return Result.Cancelled;   // our own message is the whole story - no second Revit failure box
        }
    }
}
