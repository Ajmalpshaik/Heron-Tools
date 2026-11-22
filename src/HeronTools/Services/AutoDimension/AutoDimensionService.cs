// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.3.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Places dimension rows across the grids and levels of the active view: each set of parallel datums gets a chain
// and an overall, just inside the datum heads, in one named Undo step. Never shows a window; the command does.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Models.AutoDimension;
using HeronTools.Utils;

namespace HeronTools.Services.AutoDimension
{
    internal sealed class AutoDimensionOutcome
    {
        /// <summary>Set when the run could not start (wrong view, nothing switched on): say this and nothing else.</summary>
        internal string Blocked;

        /// <summary>Set when Revit refused to commit: nothing was changed.</summary>
        internal string Refused;

        internal AutoDimensionReport Report = new AutoDimensionReport();
    }

    internal static class AutoDimensionService
    {
        /// <summary>Grids within this angle of each other (radians, about half a degree) form one set.</summary>
        private const double ParallelTolerance = 0.0087;

        /// <summary>Two datums closer than this (feet) are the same line on the drawing.</summary>
        private const double SameLineTolerance = 0.003;

        internal static string UndoName(AutoDimensionMode mode)
        {
            return mode == AutoDimensionMode.Grids ? "Auto Dimension Grids"
                 : mode == AutoDimensionMode.Levels ? "Auto Dimension Levels"
                 : "Auto Dimension Grids and Levels";
        }

        internal static AutoDimensionOutcome Run(UIDocument uidoc, AutoDimensionMode mode, AutoDimensionSettings settings)
        {
            var outcome = new AutoDimensionOutcome();
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (doc.IsFamilyDocument)
            {
                outcome.Blocked = "Auto Dimension runs in a project, not in the Family Editor.";
                return outcome;
            }

            if (!ViewKind(view, out bool planLike))
            {
                outcome.Blocked = "Auto Dimension works in plans, sections, elevations and their callouts. " +
                                  "Open one of those views and try again.";
                return outcome;
            }

            settings = (settings ?? new AutoDimensionSettings()).Clone();
            settings.Normalize();

            if (mode == AutoDimensionMode.Levels && planLike)
            {
                outcome.Blocked = "Levels are dimensioned in sections, elevations and their callouts - a plan shows no levels. " +
                                  "Open a section or elevation, or use Auto Dimension > Grids here.";
                return outcome;
            }

            bool wantGrids = mode != AutoDimensionMode.Levels && settings.GridsFrom != DatumSourceScope.None;
            bool wantLevels = mode != AutoDimensionMode.Grids && !planLike && settings.LevelsFrom != DatumSourceScope.None;
            if (!wantGrids && !wantLevels)
            {
                outcome.Blocked = mode == AutoDimensionMode.Levels
                    ? "\"Levels from\" is set to \"Do not dimension\" in Auto Dimension > Settings, so there is nothing to place."
                    : mode == AutoDimensionMode.Grids || planLike
                        ? "\"Grids from\" is set to \"Do not dimension\" in Auto Dimension > Settings, so there is nothing to place."
                        : "Grids and levels are both set to \"Do not dimension\" in Auto Dimension > Settings, so there is nothing to place.";
                return outcome;
            }

            AutoDimensionReport report = outcome.Report;
            var plane = new ViewPlane(view);
            CropFrame crop = CropFrame.Of(view, plane);
            ICollection<ElementId> selected = SelectedDatums(uidoc, wantGrids, wantLevels);

            List<DatumLine> grids = wantGrids
                ? DatumCollector.Grids(doc, view, plane, planLike, settings, selected, report)
                : new List<DatumLine>();
            List<DatumLine> levels = wantLevels
                ? DatumCollector.Levels(doc, view, plane, settings, selected, report)
                : new List<DatumLine>();

            var sets = new List<DatumSet>();
            sets.AddRange(BuildSets(grids, "grids", crop, report));
            sets.AddRange(BuildSets(levels, "levels", crop, report));

            if (sets.Count == 0)
            {
                if (grids.Count + levels.Count == 0 && !report.HasLeftOut)
                    outcome.Blocked = selected != null
                        ? "None of the selected grids or levels could be dimensioned in this view."
                        : wantLevels && !wantGrids ? "No levels were found in this view to dimension."
                        : wantGrids && !wantLevels ? "No grids were found in this view to dimension."
                        : "No grids or levels were found in this view to dimension.";
                return outcome;
            }

            HashSet<string> existingRows = settings.SkipExistingRows ? ExistingRows(doc, view) : new HashSet<string>();
            DimensionType chainType = ResolveType(doc, settings.ChainTypeName, report, "chain");
            DimensionType overallType = ResolveType(doc, settings.OverallTypeName, report, "overall");

            using (var transaction = new Transaction(doc, UndoName(mode)))
            {
                transaction.Start();
                foreach (DatumSet set in sets)
                    PlaceRows(doc, view, plane, crop, set, settings, chainType, overallType, existingRows, report);

                if (report.RowsMade == 0)
                {
                    transaction.RollBack();
                    return outcome;
                }

                if (transaction.Commit() != TransactionStatus.Committed)
                {
                    outcome.Refused = "Revit did not accept the new dimensions, so nothing was changed. " +
                                      "In a workshared model, check that no colleague is editing this view, then try again.";
                }
            }

            return outcome;
        }

        // ---------------------------------------------------------------------------------------------------------
        // View and selection
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>
        /// True for plans, sections, elevations and callouts. <paramref name="planLike"/> is decided by which way
        /// the view looks (down = plan), so a plan callout of type "Detail" is still treated as a plan.
        /// </summary>
        private static bool ViewKind(View view, out bool planLike)
        {
            planLike = false;
            if (view == null || !view.IsValidObject || view.IsTemplate)
                return false;

            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.EngineeringPlan:
                case ViewType.AreaPlan:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.Detail:
                    break;
                default:
                    return false;
            }

            double vertical = Math.Abs(view.ViewDirection.Normalize().Z);
            if (vertical > 0.999)
            {
                planLike = true;
                return true;
            }

            // A section or elevation looks sideways; a tilted view is neither and is refused.
            return vertical < 0.001;
        }

        /// <summary>The selected grids/levels of this model, or null when none are selected (= use every one).</summary>
        private static ICollection<ElementId> SelectedDatums(UIDocument uidoc, bool grids, bool levels)
        {
            var ids = new HashSet<ElementId>();
            foreach (ElementId id in uidoc.Selection.GetElementIds())
            {
                Element element = uidoc.Document.GetElement(id);
                if ((grids && element is Grid) || (levels && element is Level))
                    ids.Add(id);
            }

            return ids.Count > 0 ? ids : null;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Sets of parallel datums
        // ---------------------------------------------------------------------------------------------------------

        private sealed class DatumSet
        {
            internal string Kind;              // "grids" / "levels"
            internal Flat Along;               // the datums' direction (unit)
            internal Flat Across;              // the row's direction (unit), a quarter turn from Along
            internal List<DatumLine> Members;  // sorted across, one per drawn line
            internal double HeadPlus;          // the outermost datum end on the + side, measured along
            internal double HeadMinus;         // ... and on the - side
            internal int BubblesPlus;
            internal int BubblesMinus;
        }

        private static IEnumerable<DatumSet> BuildSets(List<DatumLine> lines, string kind, CropFrame crop, AutoDimensionReport report)
        {
            var sets = new List<DatumSet>();
            if (lines.Count == 0)
                return sets;

            // Direction as an angle in [0, pi): opposite-drawn grids are the same direction.
            var byAngle = lines
                .Select(l => new { Line = l, Angle = HalfTurnAngle(l.Direction) })
                .OrderBy(x => x.Angle)
                .ToList();

            var groups = new List<List<DatumLine>>();
            double groupStart = double.NaN;
            foreach (var item in byAngle)
            {
                if (groups.Count == 0 || item.Angle - groupStart > ParallelTolerance)
                {
                    groups.Add(new List<DatumLine>());
                    groupStart = item.Angle;
                }

                groups[groups.Count - 1].Add(item.Line);
            }

            // Just under 180 degrees and just over 0 are the same direction.
            if (groups.Count > 1 && Math.PI - HalfTurnAngle(groups[groups.Count - 1].Last().Direction) +
                HalfTurnAngle(groups[0][0].Direction) <= ParallelTolerance)
            {
                groups[0].AddRange(groups[groups.Count - 1]);
                groups.RemoveAt(groups.Count - 1);
            }

            foreach (List<DatumLine> group in groups)
            {
                DatumSet set = BuildSet(group, kind, crop, report);
                if (set != null)
                    sets.Add(set);
            }

            return sets;
        }

        private static DatumSet BuildSet(List<DatumLine> group, string kind, CropFrame crop, AutoDimensionReport report)
        {
            // The longest drawn line sets the direction; linked datums in a section have none of their own.
            DatumLine guide = group.Where(l => l.HasExtent).OrderByDescending(l => l.Length).FirstOrDefault() ?? group[0];
            Flat along = guide.Direction;
            if (along.Y < -1e-9 || (Math.Abs(along.Y) <= 1e-9 && along.X < 0))
                along = along.Scaled(-1); // the + end is up (or right), whichever way the guide was drawn
            Flat across = along.Normal(); // the direction the rows run

            // A multi-segment grid: one segment per direction (the longest), never one stop per segment.
            List<DatumLine> members = group
                .Where(l => l.MultiSegmentOwner == null)
                .Concat(group.Where(l => l.MultiSegmentOwner != null)
                             .GroupBy(l => l.MultiSegmentOwner.LongValue())
                             .Select(g => g.OrderByDescending(l => l.Length).First()))
                .ToList();

            // Linked datums: Revit cannot limit them to the view, so the crop does (or, with no crop, this model's own).
            List<DatumLine> host = members.Where(l => !l.Source.IsLinked).ToList();
            double hostMin = host.Count > 0 ? host.Min(l => l.Start.Dot(across)) : double.NaN;
            double hostMax = host.Count > 0 ? host.Max(l => l.Start.Dot(across)) : double.NaN;
            bool linkedUnlimited = false;
            members = members.Where(l =>
            {
                if (!l.Source.IsLinked)
                    return true;
                double c = l.Start.Dot(across);
                if (crop != null)
                {
                    crop.Range(across, out double lo, out double hi);
                    if (c < lo - SameLineTolerance || c > hi + SameLineTolerance)
                        return false;
                    if (l.HasExtent)
                    {
                        crop.Range(along, out double alo, out double ahi);
                        double t0 = Math.Min(l.Start.Dot(along), l.End.Dot(along));
                        double t1 = Math.Max(l.Start.Dot(along), l.End.Dot(along));
                        if (t1 < alo || t0 > ahi)
                            return false; // runs outside the crop: not on the drawing
                    }
                    return true;
                }
                if (host.Count > 0)
                    return c >= hostMin - SameLineTolerance && c <= hostMax + SameLineTolerance;
                linkedUnlimited = true;
                return true;
            }).ToList();

            if (linkedUnlimited)
                report.Note("This view has no crop, so every linked " + kind.TrimEnd('s') + " in line was used. " +
                            "Turn on Crop View to limit them to what you see.");

            // One stop per drawn line: a linked copy on top of this model's own datum is dropped.
            var ordered = members
                .OrderBy(l => l.Start.Dot(across))
                .ThenBy(l => l.Source.IsLinked ? 1 : 0)
                .ThenBy(l => l.HasExtent ? 0 : 1)
                .ToList();
            var spaced = new List<DatumLine>();
            foreach (DatumLine line in ordered)
            {
                if (spaced.Count > 0 &&
                    Math.Abs(line.Start.Dot(across) - spaced[spaced.Count - 1].Start.Dot(across)) <= SameLineTolerance)
                {
                    report.LeftOut(kind + " drawn on top of another " + kind.TrimEnd('s'));
                    continue;
                }

                spaced.Add(line);
            }

            if (spaced.Count < 2)
            {
                report.LeftOut(spaced.Count == 1
                    ? "a " + kind.TrimEnd('s') + " with no other parallel " + kind.TrimEnd('s') + " to dimension to"
                    : kind + " outside this view's crop");
                return null;
            }

            // Where the heads are, measured along the datums.
            var set = new DatumSet { Kind = kind, Along = along, Across = across, Members = spaced };
            List<DatumLine> drawn = spaced.Where(l => l.HasExtent).ToList();
            if (drawn.Count > 0)
            {
                set.HeadPlus = drawn.Max(l => Math.Max(l.Start.Dot(along), l.End.Dot(along)));
                set.HeadMinus = drawn.Min(l => Math.Min(l.Start.Dot(along), l.End.Dot(along)));
                foreach (DatumLine l in drawn)
                {
                    bool startIsPlus = l.Start.Dot(along) > l.End.Dot(along);
                    if (l.BubbleAtStart) { if (startIsPlus) set.BubblesPlus++; else set.BubblesMinus++; }
                    if (l.BubbleAtEnd) { if (startIsPlus) set.BubblesMinus++; else set.BubblesPlus++; }
                }
            }
            else if (crop != null)
            {
                crop.Range(along, out set.HeadMinus, out set.HeadPlus);
            }
            else
            {
                report.LeftOut("linked " + kind + " with nothing to show how far they are drawn - turn on Crop View, " +
                               "or include this model's " + kind);
                return null;
            }

            // The heads may run past the crop (cut off on the sheet): keep the rows inside what is shown.
            if (crop != null)
            {
                crop.Range(along, out double lo, out double hi);
                set.HeadPlus = Math.Min(set.HeadPlus, hi);
                set.HeadMinus = Math.Max(set.HeadMinus, lo);
            }

            return set;
        }

        private static double HalfTurnAngle(Flat direction)
        {
            double angle = Math.Atan2(direction.Y, direction.X);
            if (angle < 0)
                angle += Math.PI;
            if (angle >= Math.PI - 1e-12)
                angle -= Math.PI;
            return angle;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Rows
        // ---------------------------------------------------------------------------------------------------------

        private static void PlaceRows(Document doc, View view, ViewPlane plane, CropFrame crop, DatumSet set,
            AutoDimensionSettings settings, DimensionType chainType, DimensionType overallType,
            HashSet<string> existingRows, AutoDimensionReport report)
        {
            bool overall = settings.OverallRow &&
                           !(settings.ChainRow && settings.SkipOverallForTwo && set.Members.Count == 2);
            if (settings.OverallRow && !overall)
                report.Note("Where only two " + set.Kind + " are in line the overall was left out - it would repeat the chain.");

            // Nearest the heads first: the overall, then the chain - as drawn by hand.
            var rows = new List<KeyValuePair<List<DatumLine>, DimensionType>>();
            if (overall)
                rows.Add(new KeyValuePair<List<DatumLine>, DimensionType>(
                    new List<DatumLine> { set.Members.First(), set.Members.Last() }, overallType));
            if (settings.ChainRow)
                rows.Add(new KeyValuePair<List<DatumLine>, DimensionType>(set.Members, chainType));

            var ends = new List<bool>(); // true = the + end
            bool bubblePlus = set.BubblesPlus >= set.BubblesMinus;
            if (settings.Side == AutoDimensionSide.BothEnds)
            {
                ends.Add(true);
                ends.Add(false);
            }
            else
            {
                ends.Add(settings.Side == AutoDimensionSide.BubbleEnd ? bubblePlus : !bubblePlus);
            }

            double toFeet = Constants.MM_TO_FEET * plane.Scale;
            double first = settings.FirstGapMm * toFeet;
            double step = settings.RowGapMm * toFeet;

            foreach (bool plusEnd in ends)
            {
                double gap = first;
                foreach (KeyValuePair<List<DatumLine>, DimensionType> row in rows)
                {
                    double at = plusEnd ? set.HeadPlus - gap : set.HeadMinus + gap;
                    MakeRow(doc, view, plane, set, row.Key, row.Value, at, existingRows, report);
                    gap += step;
                }
            }
        }

        private static void MakeRow(Document doc, View view, ViewPlane plane, DatumSet set, List<DatumLine> stops,
            DimensionType type, double at, HashSet<string> existingRows, AutoDimensionReport report)
        {
            string rowKey = RowKey(stops.Select(s => s.Key));
            if (existingRows.Contains(rowKey))
            {
                report.RowAlreadyThere();
                return;
            }

            var references = new ReferenceArray();
            foreach (DatumLine stop in stops)
                references.Append(stop.Reference);

            double from = stops.First().Start.Dot(set.Across);
            double to = stops.Last().Start.Dot(set.Across);
            XYZ start = plane.ToModel(set.Across.Scaled(from).Plus(set.Along.Scaled(at)));
            XYZ end = plane.ToModel(set.Across.Scaled(to).Plus(set.Along.Scaled(at)));
            string label = (stops.Count == 2 && set.Members.Count > 2 ? "an overall row" : "a row") + " across " +
                           set.Kind + " " + stops.First().Name + " to " + stops.Last().Name;

            // A sub-step per row: a row Revit refuses is taken back alone; the others stay in the one Undo step.
            using (var step = new SubTransaction(doc))
            {
                step.Start();
                try
                {
                    Line line = Line.CreateBound(start, end);
                    Dimension dimension = type == null
                        ? doc.Create.NewDimension(view, line, references)
                        : doc.Create.NewDimension(view, line, references, type);
                    if (dimension == null)
                    {
                        step.RollBack();
                        report.LeftOut(label + " - Revit refused it");
                        return;
                    }

                    doc.Regenerate(); // a problem with the new dimension shows here, while it can still be taken back alone
                    step.Commit();
                    existingRows.Add(rowKey);
                    report.RowMade();
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    if (step.HasStarted() && !step.HasEnded())
                        step.RollBack();
                    report.LeftOut(label + " - Revit said: " + ex.Message.Trim());
                }
            }
        }

        /// <summary>Every existing dimension in the view, as the set of datums it measures.</summary>
        private static HashSet<string> ExistingRows(Document doc, View view)
        {
            var rows = new HashSet<string>(StringComparer.Ordinal);
            // By category, not OfClass(Dimension): Revit 2025+ returns linear dimensions as a subclass.
            foreach (Dimension dimension in new FilteredElementCollector(doc, view.Id)
                         .OfCategory(BuiltInCategory.OST_Dimensions)
                         .WhereElementIsNotElementType()
                         .OfType<Dimension>())
            {
                ReferenceArray references;
                try
                {
                    references = dimension.References;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    continue;
                }

                if (references == null || references.Size < 2)
                    continue;

                var keys = new List<string>();
                foreach (Reference reference in references)
                    keys.Add(DatumSource.KeyOf(reference));
                rows.Add(RowKey(keys));
            }

            return rows;
        }

        private static string RowKey(IEnumerable<string> keys)
        {
            return string.Join("|", keys.OrderBy(k => k, StringComparer.Ordinal));
        }

        private static DimensionType ResolveType(Document doc, string name, AutoDimensionReport report, string row)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            DimensionType type = LinearDimensionTypes(doc)
                .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.CurrentCultureIgnoreCase));
            if (type == null)
                report.LeftOut("the " + row + " dimension type \"" + name + "\", which is not in this project - " +
                               "the project's default type was used instead");
            return type;
        }

        /// <summary>The linear dimension types of the project, by name - the only ones a row can use.</summary>
        internal static List<DimensionType> LinearDimensionTypes(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .Where(t => t.StyleType == DimensionStyleType.Linear || t.StyleType == DimensionStyleType.LinearFixed)
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .GroupBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => g.First())
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        // ---------------------------------------------------------------------------------------------------------
        // Crop
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>The view's crop region on its plane (its 8 corners), or null when the crop is off.</summary>
        private sealed class CropFrame
        {
            private readonly List<Flat> _corners = new List<Flat>();

            internal static CropFrame Of(View view, ViewPlane plane)
            {
                if (!view.CropBoxActive)
                    return null;
                BoundingBoxXYZ box = view.CropBox;
                if (box == null)
                    return null;

                var frame = new CropFrame();
                Transform transform = box.Transform ?? Transform.Identity;
                foreach (double x in new[] { box.Min.X, box.Max.X })
                    foreach (double y in new[] { box.Min.Y, box.Max.Y })
                        foreach (double z in new[] { box.Min.Z, box.Max.Z })
                            frame._corners.Add(plane.ToFlat(transform.OfPoint(new XYZ(x, y, z))));
                return frame;
            }

            /// <summary>How far the crop reaches, measured along <paramref name="direction"/>.</summary>
            internal void Range(Flat direction, out double low, out double high)
            {
                low = _corners.Min(c => c.Dot(direction));
                high = _corners.Max(c => c.Dot(direction));
            }
        }
    }
}
