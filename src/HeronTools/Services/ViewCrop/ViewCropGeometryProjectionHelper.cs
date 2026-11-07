// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Projects model-space bounding boxes into view-local crop coordinates and builds rectangular crop loops.

using System;
using Autodesk.Revit.DB;

namespace HeronTools.Services.ViewCrop
{
    /// <summary>
    /// Geometry utility methods used by the View Crop tools.
    /// </summary>
    internal static class ViewCropGeometryProjectionHelper
    {
        /// <summary>
        /// Represents aggregated 2D bounds in a view's local XY plane.
        /// </summary>
        internal sealed class PlaneBounds
        {
            internal double MinX { get; private set; } = double.MaxValue;
            internal double MinY { get; private set; } = double.MaxValue;
            internal double MaxX { get; private set; } = double.MinValue;
            internal double MaxY { get; private set; } = double.MinValue;

            internal bool HasData => MinX <= MaxX && MinY <= MaxY;

            internal void Include(double x, double y)
            {
                if (x < MinX)
                    MinX = x;
                if (y < MinY)
                    MinY = y;
                if (x > MaxX)
                    MaxX = x;
                if (y > MaxY)
                    MaxY = y;
            }

            internal void Inflate(double margin)
            {
                if (!HasData || margin <= 0)
                    return;

                MinX -= margin;
                MinY -= margin;
                MaxX += margin;
                MaxY += margin;
            }

            internal void EnsureMinimumSpan(double minimumHalfSpan)
            {
                if (!HasData || minimumHalfSpan <= 0)
                    return;

                double centerX = (MinX + MaxX) * 0.5;
                double centerY = (MinY + MaxY) * 0.5;

                double halfWidth = Math.Max((MaxX - MinX) * 0.5, minimumHalfSpan);
                double halfHeight = Math.Max((MaxY - MinY) * 0.5, minimumHalfSpan);

                MinX = centerX - halfWidth;
                MaxX = centerX + halfWidth;
                MinY = centerY - halfHeight;
                MaxY = centerY + halfHeight;
            }
        }

        internal static Transform GetModelToViewTransform(View view)
        {
            BoundingBoxXYZ cropBox = view?.CropBox;
            if (cropBox == null || cropBox.Transform == null)
                throw new InvalidOperationException("View crop box transform is not available.");

            return cropBox.Transform.Inverse;
        }

        internal static XYZ[] GetBoundingBoxCorners(BoundingBoxXYZ bbox)
        {
            if (bbox == null)
                return new XYZ[0];

            XYZ min = bbox.Min;
            XYZ max = bbox.Max;
            if (min == null || max == null)
                return new XYZ[0];

            double minX = Math.Min(min.X, max.X);
            double minY = Math.Min(min.Y, max.Y);
            double minZ = Math.Min(min.Z, max.Z);
            double maxX = Math.Max(min.X, max.X);
            double maxY = Math.Max(min.Y, max.Y);
            double maxZ = Math.Max(min.Z, max.Z);

            XYZ[] localCorners =
            {
                new XYZ(minX, minY, minZ),
                new XYZ(maxX, minY, minZ),
                new XYZ(maxX, maxY, minZ),
                new XYZ(minX, maxY, minZ),
                new XYZ(minX, minY, maxZ),
                new XYZ(maxX, minY, maxZ),
                new XYZ(maxX, maxY, maxZ),
                new XYZ(minX, maxY, maxZ)
            };

            Transform transform = bbox.Transform ?? Transform.Identity;
            XYZ[] worldCorners = new XYZ[localCorners.Length];
            for (int i = 0; i < localCorners.Length; i++)
            {
                worldCorners[i] = transform.OfPoint(localCorners[i]);
            }

            return worldCorners;
        }

        internal static CurveLoop BuildRectangularCropLoop(BoundingBoxXYZ referenceCropBox, PlaneBounds bounds)
        {
            if (referenceCropBox == null || bounds == null || !bounds.HasData)
                throw new InvalidOperationException("Cannot build crop shape from empty bounds.");

            Transform viewToModel = referenceCropBox.Transform;
            double z = referenceCropBox.Min.Z;

            XYZ p1 = viewToModel.OfPoint(new XYZ(bounds.MinX, bounds.MinY, z));
            XYZ p2 = viewToModel.OfPoint(new XYZ(bounds.MaxX, bounds.MinY, z));
            XYZ p3 = viewToModel.OfPoint(new XYZ(bounds.MaxX, bounds.MaxY, z));
            XYZ p4 = viewToModel.OfPoint(new XYZ(bounds.MinX, bounds.MaxY, z));

            CurveLoop loop = new CurveLoop();
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p4));
            loop.Append(Line.CreateBound(p4, p1));
            return loop;
        }

        internal static bool IsFinite(XYZ point)
        {
            return point != null
                && !double.IsNaN(point.X)
                && !double.IsNaN(point.Y)
                && !double.IsNaN(point.Z)
                && !double.IsInfinity(point.X)
                && !double.IsInfinity(point.Y)
                && !double.IsInfinity(point.Z);
        }
    }
}
