// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Quick Clash - the clash test itself. Two steps, so a big model stays quick:
//   1. boxes: every element's box (grown by the clearance) is swept along X; only overlapping boxes go on;
//   2. shapes: for those pairs only, Revit cuts the two solids against each other (Hard) or the shortest gap
//      between them is measured (Clearance).
// Reads the model only - changes nothing.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Autodesk.Revit.DB;
using HeronTools.Models.QuickClash;
using HeronTools.Utils;

namespace HeronTools.Services.QuickClash
{
    /// <summary>What one run found.</summary>
    internal sealed class ClashRunResult
    {
        public List<ClashResult> Clashes { get; } = new List<ClashResult>();
        public int PairsTested { get; set; }
        public bool StoppedByEsc { get; set; }
        public bool HitLimit { get; set; }

        /// <summary>Pairs whose shapes were too detailed to compare every edge: their gap was measured on a sample of edges.</summary>
        public int GapSampled { get; set; }
        public TimeSpan Elapsed { get; set; }
    }

    internal static class ClashEngine
    {
        internal const double MmPerFoot = Constants.FEET_TO_MM;

        /// <summary>A run stops at this many clashes (more is not readable; tighten the section box instead).</summary>
        internal const int MaxClashes = 500;

        /// <summary>Above this many box-overlapping pairs the modeller is asked before the shapes are tested.</summary>
        internal const int AskAbovePairs = 50000;

        private const double Tiny = 1e-9;

        /// <summary>
        /// At most this many edge-to-edge comparisons per pair. Detailed equipment families can have thousands of edge
        /// pieces each, and comparing all of them against all would freeze Revit for minutes on one pair.
        /// </summary>
        internal const int MaxSegmentChecks = 250000;

        /// <summary>How often Esc is read during a run, in milliseconds - by time, not by pair count, so one slow pair never hides it.</summary>
        private const long EscCheckMs = 100;

        // ------------------------------------------------------------------ step 1: boxes

        /// <summary>
        /// Pairs (A, B) whose boxes, grown by <paramref name="growFeet"/>, overlap. Never an element with itself, each pair
        /// once, and never two elements of this model joined through MEP connectors (a duct and its own fitting).
        /// </summary>
        internal static List<KeyValuePair<ClashItem, ClashItem>> CandidatePairs(
            IList<ClashItem> sideA, IList<ClashItem> sideB, double growFeet)
        {
            var pairs = new List<KeyValuePair<ClashItem, ClashItem>>();
            var seen = new HashSet<Tuple<long, long>>();

            var all = new List<Tuple<ClashItem, bool>>(sideA.Count + sideB.Count);
            all.AddRange(sideA.Select(i => Tuple.Create(i, true)));
            all.AddRange(sideB.Select(i => Tuple.Create(i, false)));
            all.Sort((x, y) => x.Item1.Min.X.CompareTo(y.Item1.Min.X));

            var activeA = new List<ClashItem>();
            var activeB = new List<ClashItem>();
            double g = growFeet + 1e-6;

            foreach (Tuple<ClashItem, bool> entry in all)
            {
                ClashItem item = entry.Item1;
                bool isA = entry.Item2;
                List<ClashItem> others = isA ? activeB : activeA;
                others.RemoveAll(o => o.Max.X + g < item.Min.X);

                foreach (ClashItem other in others)
                {
                    if (other.Min.Y > item.Max.Y + g || item.Min.Y > other.Max.Y + g ||
                        other.Min.Z > item.Max.Z + g || item.Min.Z > other.Max.Z + g)
                        continue;

                    ClashItem a = isA ? item : other;
                    ClashItem b = isA ? other : item;
                    if (!Allowed(a, b, seen))
                        continue;
                    pairs.Add(new KeyValuePair<ClashItem, ClashItem>(a, b));
                }

                (isA ? activeA : activeB).Add(item);
            }
            return pairs;
        }

        private static bool Allowed(ClashItem a, ClashItem b, HashSet<Tuple<long, long>> seen)
        {
            if (b.LinkInstanceId >= 0)
                return true;                            // a link element is never the same element nor connected

            if (a.Id == b.Id)
                return false;                           // an element ticked on both sides
            var key = a.Id < b.Id ? Tuple.Create(a.Id, b.Id) : Tuple.Create(b.Id, a.Id);
            if (!seen.Add(key))
                return false;                           // the same two elements, met the other way round
            return !a.ConnectedIds.Contains(b.Id) && !b.ConnectedIds.Contains(a.Id);
        }

        // ------------------------------------------------------------------ step 2: shapes

        /// <summary>Tests every pair. Esc stops (nothing is reported then); a run ends at <see cref="MaxClashes"/>.</summary>
        internal static ClashRunResult Test(List<KeyValuePair<ClashItem, ClashItem>> pairs, QuickClashSettings settings)
        {
            var result = new ClashRunResult();
            var clock = Stopwatch.StartNew();
            double toleranceFeet = settings.ToleranceMm / MmPerFoot;
            double clearanceFeet = settings.ClearanceMm / MmPerFoot;

            EscPressed();   // clears Windows' "pressed since last asked": an Esc from before Run must not stop it
            long lastEscCheck = 0;

            foreach (KeyValuePair<ClashItem, ClashItem> pair in pairs)
            {
                if (clock.ElapsedMilliseconds - lastEscCheck >= EscCheckMs)
                {
                    lastEscCheck = clock.ElapsedMilliseconds;
                    if (EscPressed())
                    {
                        result.StoppedByEsc = true;
                        break;
                    }
                }
                result.PairsTested++;

                ClashResult clash = TestPair(pair.Key, pair.Value, settings, toleranceFeet, clearanceFeet, result);
                if (clash == null)
                    continue;
                result.Clashes.Add(clash);
                if (result.Clashes.Count >= MaxClashes)
                {
                    result.HitLimit = true;
                    break;
                }
            }

            // Hard first (deepest first), then clearance (closest first), then the pairs Revit could not compare.
            List<ClashResult> ordered = result.Clashes
                .OrderBy(c => c.Kind)
                .ThenBy(c => c.Kind == ClashKind.Hard ? -c.Millimetres : c.Millimetres)
                .ToList();
            result.Clashes.Clear();
            result.Clashes.AddRange(ordered);
            for (int i = 0; i < result.Clashes.Count; i++)
                result.Clashes[i].Number = i + 1;

            result.Elapsed = clock.Elapsed;
            return result;
        }

        private static ClashResult TestPair(ClashItem a, ClashItem b, QuickClashSettings s, double toleranceFeet, double clearanceFeet,
            ClashRunResult result)
        {
            List<SolidPart> solidsA = a.Solids;
            List<SolidPart> solidsB = b.Solids;
            if (solidsA.Count == 0 || solidsB.Count == 0)
                return null;                            // no body (e.g. a family of lines only)

            bool intersects = false, refused = false;
            double depth = 0;
            foreach (SolidPart pa in solidsA)
            foreach (SolidPart pb in solidsB)
            {
                if (!BoxesOverlap(pa.Min, pa.Max, pb.Min, pb.Max, 0))
                    continue;
                try
                {
                    Solid common = BooleanOperationsUtils.ExecuteBooleanOperation(pa.Solid, pb.Solid, BooleanOperationsType.Intersect);
                    if (common != null && common.Volume > Tiny)
                    {
                        intersects = true;
                        depth = Math.Max(depth, OverlapDepth(common));
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    refused = true;   // Revit could not cut these two shapes (damaged or very complex geometry)
                }
            }

            if (intersects)
            {
                if (s.HardOn && depth > toleranceFeet)
                    return Make(ClashKind.Hard, depth * MmPerFoot, a, b);
                // Overlapping is closer than any clearance distance, so with Clearance on it is a clearance clash
                // at 0 mm - also when Hard is on and the overlap is within the Hard tolerance (a pipe 5 mm into
                // a beam must not pass while a pipe 30 mm away is listed). With only Hard on, an overlap within
                // the tolerance is ignored, as the modeller asked.
                if (s.ClearanceOn)
                    return Make(ClashKind.Clearance, 0, a, b);
                return null;
            }

            if (s.ClearanceOn)
            {
                double gap;
                try
                {
                    gap = ShortestGap(solidsA, solidsB, a, b, clearanceFeet, out bool sampled);
                    if (sampled)
                        result.GapSampled++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    return Make(ClashKind.NotChecked, -1, a, b);
                }
                if (gap < clearanceFeet)
                    return Make(ClashKind.Clearance, gap * MmPerFoot, a, b);
            }

            return refused ? Make(ClashKind.NotChecked, -1, a, b) : null;
        }

        private static ClashResult Make(ClashKind kind, double mm, ClashItem a, ClashItem b)
        {
            // Zoom target: where the two boxes meet (or the gap between them).
            var min = new XYZ(Math.Min(Math.Max(a.Min.X, b.Min.X), Math.Min(a.Max.X, b.Max.X)),
                              Math.Min(Math.Max(a.Min.Y, b.Min.Y), Math.Min(a.Max.Y, b.Max.Y)),
                              Math.Min(Math.Max(a.Min.Z, b.Min.Z), Math.Min(a.Max.Z, b.Max.Z)));
            var max = new XYZ(Math.Max(Math.Max(a.Min.X, b.Min.X), Math.Min(a.Max.X, b.Max.X)),
                              Math.Max(Math.Max(a.Min.Y, b.Min.Y), Math.Min(a.Max.Y, b.Max.Y)),
                              Math.Max(Math.Max(a.Min.Z, b.Min.Z), Math.Min(a.Max.Z, b.Max.Z)));
            return new ClashResult
            {
                Kind = kind,
                Millimetres = Math.Round(mm, 1),
                ElementAId = a.Id,
                ElementBId = b.Id,
                LinkInstanceId = b.LinkInstanceId,
                NameA = ClashElementCollector.DisplayName(a.Element),
                NameB = ClashElementCollector.DisplayName(b.Element),
                BoxMin = min,
                BoxMax = max
            };
        }

        /// <summary>
        /// Overlap depth: the thinnest width of the shared solid, measured across the solid itself - not along the
        /// model's X, Y and Z. In plain words: lay the overlap between two parallel plates, turn the plates to every
        /// direction the overlap's own faces point in (and to X, Y, Z), and keep the smallest gap between the plates.
        /// For a box this is its thinnest side; for two pipes crossing with a 2 mm overlap it is about 2 mm, also
        /// when the pipes run at 45 degrees in plan (the old "smallest side of the X/Y/Z box" read about 28 mm there).
        /// The answer is never larger than the old one, because X, Y and Z are among the directions tried.
        /// </summary>
        private static double OverlapDepth(Solid common)
        {
            double boxDepth = SmallestSide(common);
            var points = new List<XYZ>();
            var directions = new List<XYZ> { XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ };
            try
            {
                foreach (Face face in common.Faces)
                {
                    Mesh mesh = face.Triangulate();
                    if (mesh == null)
                        continue;
                    points.AddRange(mesh.Vertices);
                    if (face is PlanarFace planar)
                    {
                        AddDirection(directions, planar.FaceNormal);
                        continue;
                    }
                    // A curved face (round pipe, round duct): each small triangle of it points its own way.
                    for (int i = 0; i < mesh.NumTriangles; i++)
                    {
                        MeshTriangle t = mesh.get_Triangle(i);
                        XYZ normal = (t.get_Vertex(1) - t.get_Vertex(0)).CrossProduct(t.get_Vertex(2) - t.get_Vertex(0));
                        AddDirection(directions, normal);
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return boxDepth;   // Revit could not mesh the overlap: keep the X/Y/Z box measure
            }
            if (points.Count < 4)
                return boxDepth;

            double best = boxDepth;
            foreach (XYZ n in directions)
            {
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (XYZ p in points)
                {
                    double d = p.DotProduct(n);
                    if (d < lo) lo = d;
                    if (d > hi) hi = d;
                }
                best = Math.Min(best, hi - lo);
            }
            return best;
        }

        /// <summary>Adds a unit direction unless it (or its reverse) is already in the list - keeps the work small.</summary>
        private static void AddDirection(List<XYZ> directions, XYZ v)
        {
            double length = v.GetLength();
            if (length < Tiny || directions.Count >= MaxDepthDirections)
                return;
            XYZ n = v / length;
            foreach (XYZ d in directions)
                if (Math.Abs(d.DotProduct(n)) > 0.9998)   // within about 1 degree
                    return;
            directions.Add(n);
        }

        /// <summary>Directions tried per overlap; plenty for a pipe, duct or beam, and keeps a big run quick.</summary>
        private const int MaxDepthDirections = 256;

        /// <summary>The smallest side of the shared solid's X/Y/Z box (the fallback depth).</summary>
        private static double SmallestSide(Solid common)
        {
            BoundingBoxXYZ box = common.GetBoundingBox();
            XYZ size = box.Max - box.Min;
            return Math.Min(Math.Abs(size.X), Math.Min(Math.Abs(size.Y), Math.Abs(size.Z)));
        }

        private static bool BoxesOverlap(XYZ minA, XYZ maxA, XYZ minB, XYZ maxB, double grow)
        {
            return !(minA.X > maxB.X + grow || minB.X > maxA.X + grow ||
                     minA.Y > maxB.Y + grow || minB.Y > maxA.Y + grow ||
                     minA.Z > maxB.Z + grow || minB.Z > maxA.Z + grow);
        }

        // ------------------------------------------------------------------ clearance: the shortest gap

        /// <summary>
        /// The shortest gap between two sets of solids that do not overlap, or <paramref name="limit"/> (or more) when
        /// they are further apart. Two measures, the smaller wins:
        ///   - edge to edge (straight pieces of every edge, exact for boxes crossing at right angles - a duct over a beam);
        ///   - "walking": from seed points near where the two meet, step to the closest point on one solid, then on the
        ///     other, until it settles - this finds the gap between curved faces (a round duct over a round pipe).
        /// Accurate to a few mm; Revit has no ready-made "distance between two solids".
        /// <paramref name="sampled"/>: the edges were too many to compare all, so an even sample of them was compared.
        /// </summary>
        private static double ShortestGap(List<SolidPart> solidsA, List<SolidPart> solidsB, ClashItem a, ClashItem b, double limit,
            out bool sampled)
        {
            sampled = false;
            // Only the region where the two boxes (grown by the limit) meet can hold a gap below the limit.
            XYZ zoneMin = new XYZ(Math.Max(a.Min.X, b.Min.X) - limit, Math.Max(a.Min.Y, b.Min.Y) - limit, Math.Max(a.Min.Z, b.Min.Z) - limit);
            XYZ zoneMax = new XYZ(Math.Min(a.Max.X, b.Max.X) + limit, Math.Min(a.Max.Y, b.Max.Y) + limit, Math.Min(a.Max.Z, b.Max.Z) + limit);

            List<Face> facesA = FacesNear(solidsA, zoneMin, zoneMax);
            List<Face> facesB = FacesNear(solidsB, zoneMin, zoneMax);
            if (facesA.Count == 0 || facesB.Count == 0)
                return double.MaxValue;

            double best = double.MaxValue;

            // Edge to edge.
            List<XYZ[]> segA = Segments(solidsA, zoneMin, zoneMax);
            List<XYZ[]> segB = Segments(solidsB, zoneMin, zoneMax);
            long checks = (long)segA.Count * segB.Count;
            if (checks > MaxSegmentChecks)
            {
                // Too detailed to compare every edge with every edge: compare an even spread of both sides' edges.
                // The walking below still runs over all faces, so a near miss is rarely lost.
                sampled = true;
                double keep = Math.Sqrt((double)MaxSegmentChecks / checks);
                segA = EvenSample(segA, Math.Max(1, (int)(segA.Count * keep)));
                segB = EvenSample(segB, Math.Max(1, (int)(segB.Count * keep)));
            }
            foreach (XYZ[] sa in segA)
            foreach (XYZ[] sb in segB)
                best = Math.Min(best, SegmentDistance(sa[0], sa[1], sb[0], sb[1]));

            // Walking from seeds: the zone's centre and corners, and the ends of the edges near the zone.
            var seeds = new List<XYZ> { (zoneMin + zoneMax) / 2 };
            foreach (double x in new[] { zoneMin.X, zoneMax.X })
            foreach (double y in new[] { zoneMin.Y, zoneMax.Y })
            foreach (double z in new[] { zoneMin.Z, zoneMax.Z })
                seeds.Add(new XYZ(x, y, z));
            int stride = Math.Max(1, segA.Count / 24);
            for (int i = 0; i < segA.Count; i += stride)
                seeds.Add(segA[i][0]);

            foreach (XYZ seed in seeds)
            {
                XYZ p = Closest(seed, facesA, out _);
                if (p == null) continue;
                for (int step = 0; step < 8; step++)
                {
                    XYZ q = Closest(p, facesB, out double d);
                    if (q == null) break;
                    best = Math.Min(best, d);
                    XYZ p2 = Closest(q, facesA, out _);
                    if (p2 == null || p2.DistanceTo(p) < 1e-5) break;
                    p = p2;
                }
                if (best < 1e-6) break;
            }
            return best;
        }

        /// <summary>About <paramref name="count"/> items, evenly spread over the list.</summary>
        private static List<XYZ[]> EvenSample(List<XYZ[]> items, int count)
        {
            if (count >= items.Count)
                return items;
            var sample = new List<XYZ[]>(count);
            double step = (double)items.Count / count;
            for (int i = 0; i < count; i++)
                sample.Add(items[(int)(i * step)]);
            return sample;
        }

        private static List<Face> FacesNear(List<SolidPart> solids, XYZ min, XYZ max)
        {
            var faces = new List<Face>();
            foreach (SolidPart part in solids)
            {
                if (!BoxesOverlap(part.Min, part.Max, min, max, 0))
                    continue;
                foreach (Face face in part.Solid.Faces)
                    faces.Add(face);
            }
            return faces;
        }

        /// <summary>Straight pieces of every edge that comes near the zone.</summary>
        private static List<XYZ[]> Segments(List<SolidPart> solids, XYZ min, XYZ max)
        {
            var segments = new List<XYZ[]>();
            foreach (SolidPart part in solids)
            {
                if (!BoxesOverlap(part.Min, part.Max, min, max, 0))
                    continue;
                foreach (Edge edge in part.Solid.Edges)
                {
                    IList<XYZ> points = edge.Tessellate();
                    for (int i = 1; i < points.Count; i++)
                    {
                        XYZ p = points[i - 1], q = points[i];
                        if (BoxesOverlap(Min(p, q), Max(p, q), min, max, 0))
                            segments.Add(new[] { p, q });
                    }
                }
            }
            return segments;
        }

        /// <summary>The closest point to <paramref name="point"/> on any of the faces (inside the face, or on its edges).</summary>
        private static XYZ Closest(XYZ point, List<Face> faces, out double distance)
        {
            distance = double.MaxValue;
            XYZ best = null;
            foreach (Face face in faces)
            {
                IntersectionResult onFace = face.Project(point);
                if (onFace != null && onFace.Distance < distance)
                {
                    distance = onFace.Distance;
                    best = onFace.XYZPoint;
                }
                // Face.Project gives nothing when the foot of the perpendicular falls outside the face - then the
                // closest point is on one of its edges.
                foreach (EdgeArray loop in face.EdgeLoops)
                foreach (Edge edge in loop)
                {
                    IntersectionResult onEdge = edge.AsCurve().Project(point);
                    if (onEdge != null && onEdge.Distance < distance)
                    {
                        distance = onEdge.Distance;
                        best = onEdge.XYZPoint;
                    }
                }
            }
            return best;
        }

        /// <summary>The shortest distance between two straight pieces (segments) in space.</summary>
        private static double SegmentDistance(XYZ p1, XYZ q1, XYZ p2, XYZ q2)
        {
            XYZ d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            double a = d1.DotProduct(d1), e = d2.DotProduct(d2), f = d2.DotProduct(r);
            double s, t;
            if (a <= Tiny && e <= Tiny)
                return p1.DistanceTo(p2);
            if (a <= Tiny)
            {
                s = 0;
                t = Clamp01(f / e);
            }
            else
            {
                double c = d1.DotProduct(r);
                if (e <= Tiny)
                {
                    t = 0;
                    s = Clamp01(-c / a);
                }
                else
                {
                    double b = d1.DotProduct(d2);
                    double denom = a * e - b * b;
                    s = denom > Tiny ? Clamp01((b * f - c * e) / denom) : 0;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = Clamp01(-c / a); }
                    else if (t > 1) { t = 1; s = Clamp01((b - c) / a); }
                }
            }
            XYZ c1 = p1 + d1 * s, c2 = p2 + d2 * t;
            return c1.DistanceTo(c2);
        }

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        private static XYZ Min(XYZ p, XYZ q) => new XYZ(Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), Math.Min(p.Z, q.Z));
        private static XYZ Max(XYZ p, XYZ q) => new XYZ(Math.Max(p.X, q.X), Math.Max(p.Y, q.Y), Math.Max(p.Z, q.Z));

        // ------------------------------------------------------------------ Esc

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        private const int VkEscape = 0x1B;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        /// <summary>
        /// True when Esc is held down now, or was tapped since the last time it was read, and Revit (or the Quick Clash
        /// window) is the window in front - an Esc pressed in another program does not stop the run. Read between pairs.
        /// </summary>
        private static bool EscPressed()
        {
            // 0x8000: held down now. 0x0001: pressed since the last read - catches a quick tap between two reads.
            if ((GetAsyncKeyState(VkEscape) & 0x8001) == 0)
                return false;
            GetWindowThreadProcessId(GetForegroundWindow(), out uint process);
            return process == (uint)Process.GetCurrentProcess().Id;
        }
    }
}
