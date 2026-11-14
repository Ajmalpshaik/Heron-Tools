// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// What 3D Snapshot will show: a box measured in a level frame (one horizontal "right" direction, its
// horizontal "back" direction, and world up), and - for a section - the way the camera should face.

using System;
using Autodesk.Revit.DB;

namespace HeronTools.Services.Snapshot3D
{
    /// <summary>Which of the three ways the box was found. Named the way the modeller thinks of it.</summary>
    internal enum SnapshotMode
    {
        /// <summary>Rooms or spaces (picked directly, or through their tags).</summary>
        Rooms,

        /// <summary>A section or elevation - a selected mark, or the section view the modeller is in.</summary>
        Section,

        /// <summary>A window dragged in a plan, bottom to top of the plan's view range.</summary>
        Rectangle
    }

    /// <summary>
    /// A box standing upright in the model, possibly turned in plan (never tilted), and how to look at it.
    /// </summary>
    /// <remarks>
    /// Coordinates are measured along three axes: <see cref="Right"/> (horizontal), Back = Up x Right
    /// (horizontal) and world Z. Measuring every point along the same axes makes rooms, rectangles and
    /// sections all produce the same kind of box - the kind a 3D view's section box can take (Revit turns a
    /// section box only about the vertical, like the rotate handle on the box in the UI).
    /// </remarks>
    internal sealed class SnapshotTarget
    {
        private double _minX = double.MaxValue, _minY = double.MaxValue, _minZ = double.MaxValue;
        private double _maxX = double.MinValue, _maxY = double.MinValue, _maxZ = double.MinValue;

        internal SnapshotTarget(SnapshotMode mode, XYZ right, string sourceName)
        {
            Mode = mode;
            Right = Horizontal(right);
            Back = XYZ.BasisZ.CrossProduct(Right).Normalize();
            SourceName = sourceName ?? string.Empty;
        }

        internal SnapshotMode Mode { get; }

        /// <summary>Horizontal unit vector the box's width runs along.</summary>
        internal XYZ Right { get; }

        /// <summary>Horizontal unit vector the box's depth runs along (Up x Right).</summary>
        internal XYZ Back { get; }

        /// <summary>What the box came from, for messages ("Room 101 Office", "Section 3", ...).</summary>
        internal string SourceName { get; }

        /// <summary>
        /// For a section: the section's view direction (pointing out of the screen, towards the modeller).
        /// The camera then looks the same way the section does. Null keeps the 3D view's own camera angle.
        /// </summary>
        internal XYZ FaceDirection { get; set; }

        /// <summary>
        /// For a section: which face of the box is the cut, so no padding is added in front of it.
        /// True means the cut is on the box's minimum Back side, false on its maximum Back side.
        /// </summary>
        internal bool? CutOnMinBackSide { get; set; }

        internal bool IsEmpty => _minX > _maxX || _minY > _maxY || _minZ > _maxZ;

        internal double Width => _maxX - _minX;
        internal double Depth => _maxY - _minY;
        internal double Height => _maxZ - _minZ;

        /// <summary>Takes a model point into the box.</summary>
        internal void Add(XYZ point)
        {
            if (point == null)
                return;

            double x = point.DotProduct(Right);
            double y = point.DotProduct(Back);
            double z = point.Z;
            if (x < _minX) _minX = x;
            if (x > _maxX) _maxX = x;
            if (y < _minY) _minY = y;
            if (y > _maxY) _maxY = y;
            if (z < _minZ) _minZ = z;
            if (z > _maxZ) _maxZ = z;
        }

        /// <summary>Stretches the box's height to cover the elevations given (internal feet, model Z).</summary>
        internal void IncludeElevation(double z)
        {
            if (z < _minZ) _minZ = z;
            if (z > _maxZ) _maxZ = z;
        }

        /// <summary>
        /// The section box for a 3D view: this box grown by <paramref name="padding"/> on every side -
        /// except in front of a section's cut, which stays exactly on the cut ("same cut as the section").
        /// </summary>
        internal BoundingBoxXYZ ToSectionBox(double padding)
        {
            double minY = _minY - padding;
            double maxY = _maxY + padding;
            if (CutOnMinBackSide == true) minY = _minY;
            if (CutOnMinBackSide == false) maxY = _maxY;

            double minX = _minX - padding, maxX = _maxX + padding;
            double minZ = _minZ - padding, maxZ = _maxZ + padding;

            // The box's own origin sits at its centre, so Min and Max stay small numbers even far from the
            // model's internal origin.
            double cx = (minX + maxX) / 2.0, cy = (minY + maxY) / 2.0, cz = (minZ + maxZ) / 2.0;
            double hx = (maxX - minX) / 2.0, hy = (maxY - minY) / 2.0, hz = (maxZ - minZ) / 2.0;

            Transform frame = Transform.Identity;
            frame.BasisX = Right;
            frame.BasisY = Back;
            frame.BasisZ = XYZ.BasisZ;
            frame.Origin = Right.Multiply(cx) + Back.Multiply(cy) + XYZ.BasisZ.Multiply(cz);

            return new BoundingBoxXYZ
            {
                Transform = frame,
                Min = new XYZ(-hx, -hy, -hz),
                Max = new XYZ(hx, hy, hz),
            };
        }

        /// <summary>A horizontal unit vector from any direction; world X if the direction is (nearly) vertical.</summary>
        internal static XYZ Horizontal(XYZ direction)
        {
            if (direction == null)
                return XYZ.BasisX;

            var flat = new XYZ(direction.X, direction.Y, 0.0);
            return flat.GetLength() < 1e-6 ? XYZ.BasisX : flat.Normalize();
        }

        /// <summary>The 8 corners of a section box, in model coordinates.</summary>
        internal static XYZ[] Corners(BoundingBoxXYZ box)
        {
            Transform t = box.Transform ?? Transform.Identity;
            XYZ a = box.Min, b = box.Max;
            return new[]
            {
                t.OfPoint(new XYZ(a.X, a.Y, a.Z)), t.OfPoint(new XYZ(b.X, a.Y, a.Z)),
                t.OfPoint(new XYZ(a.X, b.Y, a.Z)), t.OfPoint(new XYZ(b.X, b.Y, a.Z)),
                t.OfPoint(new XYZ(a.X, a.Y, b.Z)), t.OfPoint(new XYZ(b.X, a.Y, b.Z)),
                t.OfPoint(new XYZ(a.X, b.Y, b.Z)), t.OfPoint(new XYZ(b.X, b.Y, b.Z)),
            };
        }

        internal static double MillimetresToFeet(double mm) => mm / 304.8;

        internal static string Describe(double feet) =>
            Math.Round(feet * 304.8).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " mm";
    }
}
