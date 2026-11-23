// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.3.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Finds the grids and levels drawn in the active view (this model and/or links shown in it) and reads each one
// as a line on the view's own sheet plane: where it runs, where its ends are, which end shows the bubble.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using HeronTools.Models.AutoDimension;

namespace HeronTools.Services.AutoDimension
{
    /// <summary>A point or direction on the view's plane: X to the right of the screen, Y up it (feet).</summary>
    internal struct Flat
    {
        internal readonly double X;
        internal readonly double Y;

        internal Flat(double x, double y) { X = x; Y = y; }

        internal double Dot(Flat other) => X * other.X + Y * other.Y;
        internal double Length => Math.Sqrt(X * X + Y * Y);
        internal Flat Scaled(double k) => new Flat(X * k, Y * k);
        internal Flat Plus(Flat other) => new Flat(X + other.X, Y + other.Y);
        internal Flat Minus(Flat other) => new Flat(X - other.X, Y - other.Y);
        internal Flat Unit() { double l = Length; return l <= 0 ? this : new Flat(X / l, Y / l); }
        /// <summary>A quarter turn anticlockwise.</summary>
        internal Flat Normal() => new Flat(-Y, X);
    }

    /// <summary>The active view's plane: turns model points into <see cref="Flat"/> points and back.</summary>
    internal sealed class ViewPlane
    {
        private readonly XYZ _origin;
        private readonly XYZ _right;
        private readonly XYZ _up;

        internal ViewPlane(View view)
        {
            _origin = view.Origin;
            _right = view.RightDirection.Normalize();
            _up = view.UpDirection.Normalize();
            Normal = view.ViewDirection.Normalize();
            Scale = Math.Max(1, view.Scale);
        }

        internal XYZ Normal { get; }
        internal XYZ Origin => _origin;
        internal XYZ Up => _up;
        internal int Scale { get; }

        internal Flat ToFlat(XYZ model)
        {
            XYZ d = model - _origin;
            return new Flat(d.DotProduct(_right), d.DotProduct(_up));
        }

        internal Flat DirectionToFlat(XYZ direction) => new Flat(direction.DotProduct(_right), direction.DotProduct(_up));

        internal XYZ ToModel(Flat flat) => _origin + _right * flat.X + _up * flat.Y;
    }

    /// <summary>One grid or level as drawn in the view.</summary>
    internal sealed class DatumLine
    {
        internal Element Datum;
        internal bool IsLevel;
        internal DatumSource Source;
        internal Reference Reference;
        internal string Key;
        internal string Name;

        /// <summary>A point on the line (the start, when <see cref="HasExtent"/>).</summary>
        internal Flat Start;
        internal Flat End;

        /// <summary>Unit direction of the line on the view plane.</summary>
        internal Flat Direction;

        /// <summary>False for a linked datum in a section: only where it is is known, not how far it is drawn.</summary>
        internal bool HasExtent;

        internal bool BubbleAtStart;
        internal bool BubbleAtEnd;

        /// <summary>The multi-segment grid this grid is a segment of, or null.</summary>
        internal ElementId MultiSegmentOwner;

        internal double Length => HasExtent ? End.Minus(Start).Length : 0;
    }

    internal static class DatumCollector
    {
        /// <summary>The grids drawn in the view, from the sources the settings ask for.</summary>
        internal static List<DatumLine> Grids(Document doc, View view, ViewPlane plane, bool planLike,
            AutoDimensionSettings settings, ICollection<ElementId> selected, AutoDimensionReport report)
        {
            var lines = new List<DatumLine>();
            Regex[] leaveOut = ParseLeaveOut(settings.LeaveOutNames);

            foreach (DatumSource source in Sources(doc, view, settings.GridsFrom, selected, report, "grids"))
            {
                foreach (Grid grid in Datums<Grid>(source, view))
                {
                    if (!source.IsLinked && selected != null && !selected.Contains(grid.Id))
                        continue;

                    string name = SafeName(grid);
                    if (IsLeftOut(name, leaveOut))
                    {
                        report.LeftOut("grids left out by name in the settings");
                        continue;
                    }

                    if (grid.IsCurved)
                    {
                        report.LeftOut("curved grids (only straight grids can be dimensioned in a row)");
                        continue;
                    }

                    DatumLine line = source.IsLinked
                        ? LinkedGrid(grid, source, plane, planLike, report)
                        : DrawnLine(grid, view, plane, report, "grids");
                    if (line == null)
                        continue;

                    if (!source.IsLinked)
                        line.MultiSegmentOwner = MultiSegmentOwnerOf(grid);

                    if (Finish(line, grid, false, source, name, report, "grid"))
                        lines.Add(line);
                }
            }

            return lines;
        }

        /// <summary>The levels drawn in the view (sections, elevations and their callouts only).</summary>
        internal static List<DatumLine> Levels(Document doc, View view, ViewPlane plane,
            AutoDimensionSettings settings, ICollection<ElementId> selected, AutoDimensionReport report)
        {
            var lines = new List<DatumLine>();
            Regex[] leaveOut = ParseLeaveOut(settings.LeaveOutNames);

            foreach (DatumSource source in Sources(doc, view, settings.LevelsFrom, selected, report, "levels"))
            {
                foreach (Level level in Datums<Level>(source, view))
                {
                    if (!source.IsLinked && selected != null && !selected.Contains(level.Id))
                        continue;

                    string name = SafeName(level);
                    if (IsLeftOut(name, leaveOut))
                    {
                        report.LeftOut("levels left out by name in the settings");
                        continue;
                    }

                    if (settings.StoryLevelsOnly && !IsBuildingStory(level))
                    {
                        report.Note("Levels that are not a Building Story were left out, as the settings ask.");
                        continue;
                    }

                    DatumLine line = source.IsLinked
                        ? LinkedLevel(level, source, plane)
                        : DrawnLine(level, view, plane, report, "levels");
                    if (line == null)
                        continue;

                    if (Finish(line, level, true, source, name, report, "level"))
                        lines.Add(line);
                }
            }

            return lines;
        }

        private static IEnumerable<DatumSource> Sources(Document doc, View view, DatumSourceScope scope,
            ICollection<ElementId> selected, AutoDimensionReport report, string kind)
        {
            if (scope == DatumSourceScope.ThisModel || scope == DatumSourceScope.ThisModelAndLinks)
                yield return DatumSource.ForHost(doc);

            if (scope != DatumSourceScope.LinkedModels && scope != DatumSourceScope.ThisModelAndLinks)
                yield break;

            if (selected != null)
            {
                // A selection names this model's datums; linked ones cannot be part of it.
                report.Note("Linked " + kind + " were not used, because you selected the " + kind + " to dimension.");
                yield break;
            }

            foreach (DatumSource link in DatumSource.LinksShownIn(doc, view))
                yield return link;
        }

        private static IEnumerable<T> Datums<T>(DatumSource source, View view) where T : Element
        {
            // This model: Revit gives only what the view shows. A link cannot be read through the host view, so
            // its datums are culled to the crop later.
            FilteredElementCollector collector = source.IsLinked
                ? new FilteredElementCollector(source.Document)
                : new FilteredElementCollector(source.Document, view.Id);
            return collector.OfClass(typeof(T)).WhereElementIsNotElementType().OfType<T>().ToList();
        }

        /// <summary>The datum as drawn in this view (2D extents, so a datum cut short in this view is read short).</summary>
        private static DatumLine DrawnLine(DatumPlane datum, View view, ViewPlane plane, AutoDimensionReport report, string kind)
        {
            Line drawn = null;
            try
            {
                drawn = datum.GetCurvesInView(DatumExtentType.ViewSpecific, view)?.OfType<Line>().FirstOrDefault();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                drawn = null;
            }

            if (drawn == null)
            {
                report.LeftOut(kind + " that are not drawn as a straight line in this view");
                return null;
            }

            Flat start = plane.ToFlat(drawn.GetEndPoint(0));
            Flat end = plane.ToFlat(drawn.GetEndPoint(1));
            if (end.Minus(start).Length < 1e-6)
            {
                // Seen end-on: a grid running along the section's line of sight is not on the drawing.
                return null;
            }

            bool bubbleStart = false, bubbleEnd = false;
            try
            {
                // Curve end 0 is DatumEnds.End0.
                bubbleStart = datum.IsBubbleVisibleInView(DatumEnds.End0, view);
                bubbleEnd = datum.IsBubbleVisibleInView(DatumEnds.End1, view);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // No bubble information: the row goes to the default end.
            }

            return new DatumLine
            {
                Start = start,
                End = end,
                Direction = end.Minus(start).Unit(),
                HasExtent = true,
                BubbleAtStart = bubbleStart,
                BubbleAtEnd = bubbleEnd
            };
        }

        /// <summary>
        /// A linked grid. In a plan its model line, moved into this model. In a section only where its plane
        /// crosses the section - and only when the grid really reaches the section line.
        /// </summary>
        private static DatumLine LinkedGrid(Grid grid, DatumSource source, ViewPlane plane, bool planLike, AutoDimensionReport report)
        {
            Line model = grid.Curve as Line;
            if (model == null)
                return null;

            XYZ a = source.ToHost.OfPoint(model.GetEndPoint(0));
            XYZ b = source.ToHost.OfPoint(model.GetEndPoint(1));

            if (planLike)
            {
                Flat start = plane.ToFlat(a);
                Flat end = plane.ToFlat(b);
                if (end.Minus(start).Length < 1e-6)
                    return null;
                return new DatumLine { Start = start, End = end, Direction = end.Minus(start).Unit(), HasExtent = true };
            }

            XYZ along = b - a;
            double length = along.GetLength();
            if (length < 1e-6)
                return null;
            along = along / length;

            double facing = along.DotProduct(plane.Normal);
            if (Math.Abs(facing) < 1e-6)
                return null; // Runs parallel to the section: not on this drawing.

            double t = (plane.Origin - a).DotProduct(plane.Normal) / facing;
            if (t < -0.01 || t > length + 0.01)
                return null; // Its line stops before reaching the section.

            Flat at = plane.ToFlat(a + along * t);
            return new DatumLine { Start = at, End = at, Direction = plane.DirectionToFlat(plane.Up).Unit(), HasExtent = false };
        }

        /// <summary>A linked level in a section: its height is known, its drawn length is not.</summary>
        private static DatumLine LinkedLevel(Level level, DatumSource source, ViewPlane plane)
        {
            XYZ point = source.ToHost.OfPoint(new XYZ(0, 0, level.ProjectElevation));
            Flat at = plane.ToFlat(point);
            return new DatumLine { Start = at, End = at, Direction = new Flat(1, 0), HasExtent = false };
        }

        private static bool Finish(DatumLine line, Element datum, bool isLevel, DatumSource source, string name,
            AutoDimensionReport report, string kind)
        {
            Reference reference = source.ReferenceFor(datum);
            if (reference == null)
            {
                report.LeftOut((source.IsLinked ? "linked " : string.Empty) + kind + "s Revit gives no reference to");
                return false;
            }

            line.Datum = datum;
            line.IsLevel = isLevel;
            line.Source = source;
            line.Reference = reference;
            line.Key = source.KeyFor(datum.Id);
            line.Name = source.IsLinked ? name + " (" + source.DisplayName + ")" : name;
            return true;
        }

        private static ElementId MultiSegmentOwnerOf(Grid grid)
        {
            try
            {
                ElementId owner = MultiSegmentGrid.GetMultiSegementGridId(grid);
                return owner != null && owner != ElementId.InvalidElementId ? owner : null;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
        }

        private static bool IsBuildingStory(Level level)
        {
            Parameter story = level.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
            return story == null || story.AsInteger() != 0;
        }

        private static string SafeName(Element element)
        {
            try
            {
                return element.Name ?? string.Empty;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// "A;B*;?-X" -> whole-name patterns, ignoring case. A plain "1" leaves out grid 1 only, never 10 or 21.
        /// </summary>
        internal static Regex[] ParseLeaveOut(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new Regex[0];

            return text.Split(';')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Select(p => new Regex("^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                                       RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .ToArray();
        }

        private static bool IsLeftOut(string name, Regex[] patterns)
        {
            return patterns.Length > 0 && patterns.Any(p => p.IsMatch(name ?? string.Empty));
        }
    }
}
