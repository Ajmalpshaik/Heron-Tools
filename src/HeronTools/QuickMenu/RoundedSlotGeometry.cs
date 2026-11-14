// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The shape of one Quick Menu slot - a ring segment with its four corners softly rounded - shared by the wheel and the settings preview so both draw exactly the same slot.

using System;
using System.Windows;
using System.Windows.Media;

namespace HeronTools.UI.QuickMenu
{
    /// <summary>Builds a ring segment (slot) with blended, rounded corners.</summary>
    internal static class RoundedSlotGeometry
    {
        /// <summary>Corner size as a share of the outer radius: about 11 units on the 560 wheel.</summary>
        internal const double CornerFraction = 0.04;

        /// <summary>
        /// A slot from <paramref name="startAngle"/> to <paramref name="endAngle"/> (degrees, clockwise,
        /// 0 = east) between two radii. Each corner is blended by a curve that starts
        /// <paramref name="cornerRadius"/> along the rim and the same distance down the side, so the slot
        /// keeps its exact outline everywhere except the softened corners. The corner shrinks on its own
        /// when the slot is too thin or too narrow for it.
        /// </summary>
        internal static Geometry Build(Point centre, double innerRadius, double outerRadius,
                                       double startAngle, double endAngle, double cornerRadius)
        {
            return Build(centre, innerRadius, outerRadius, startAngle, endAngle, cornerRadius, 0.0);
        }

        /// <summary>
        /// The same slot, with a small V notch cut into the middle of its outer edge when
        /// <paramref name="notchDepth"/> is above zero (the HUD Notched design).
        /// </summary>
        internal static Geometry Build(Point centre, double innerRadius, double outerRadius,
                                       double startAngle, double endAngle, double cornerRadius, double notchDepth)
        {
            double halfSpanRadians = (endAngle - startAngle) / 2.0 * Math.PI / 180.0;
            double rc = Math.Max(0.0, Math.Min(cornerRadius,
                Math.Min((outerRadius - innerRadius) / 2.0 - 1.0,
                         Math.Min(outerRadius, innerRadius) * halfSpanRadians * 0.9)));

            // The corner's length along each rim, as an angle.
            double outerStep = rc / outerRadius * 180.0 / Math.PI;
            double innerStep = innerRadius > 0.0 ? rc / innerRadius * 180.0 / Math.PI : 0.0;
            bool isLargeArc = (endAngle - startAngle - (2.0 * outerStep)) > 180.0;

            var figure = new PathFigure
            {
                StartPoint = Polar(centre, outerRadius - rc, startAngle),
                IsClosed = true,
                IsFilled = true
            };

            // Outer edge, corner - arc - corner.
            figure.Segments.Add(new QuadraticBezierSegment(
                Polar(centre, outerRadius, startAngle), Polar(centre, outerRadius, startAngle + outerStep), true));
            if (notchDepth > 0.0)
            {
                // Rim, a small V notch in the middle (12 % of the slot's width either side), rim again.
                double middle = (startAngle + endAngle) / 2.0;
                double half = (endAngle - startAngle) * 0.12;
                figure.Segments.Add(new ArcSegment(
                    Polar(centre, outerRadius, middle - half),
                    new Size(outerRadius, outerRadius), 0, false, SweepDirection.Clockwise, true));
                figure.Segments.Add(new QuadraticBezierSegment(
                    Polar(centre, outerRadius - notchDepth, middle), Polar(centre, outerRadius, middle + half), true));
                figure.Segments.Add(new ArcSegment(
                    Polar(centre, outerRadius, endAngle - outerStep),
                    new Size(outerRadius, outerRadius), 0, false, SweepDirection.Clockwise, true));
            }
            else
            {
                figure.Segments.Add(new ArcSegment(
                    Polar(centre, outerRadius, endAngle - outerStep),
                    new Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true));
            }
            figure.Segments.Add(new QuadraticBezierSegment(
                Polar(centre, outerRadius, endAngle), Polar(centre, outerRadius - rc, endAngle), true));

            // Down the side, then the inner edge back the other way.
            figure.Segments.Add(new LineSegment(Polar(centre, innerRadius + rc, endAngle), true));
            figure.Segments.Add(new QuadraticBezierSegment(
                Polar(centre, innerRadius, endAngle), Polar(centre, innerRadius, endAngle - innerStep), true));
            figure.Segments.Add(new ArcSegment(
                Polar(centre, innerRadius, startAngle + innerStep),
                new Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true));
            figure.Segments.Add(new QuadraticBezierSegment(
                Polar(centre, innerRadius, startAngle), Polar(centre, innerRadius + rc, startAngle), true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();
            return geometry;
        }

        private static Point Polar(Point centre, double radius, double angleDegrees)
        {
            double radians = angleDegrees * Math.PI / 180.0;
            return new Point(centre.X + (radius * Math.Cos(radians)), centre.Y + (radius * Math.Sin(radians)));
        }
    }
}
