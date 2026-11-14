// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The model work behind 3D Snapshot: measure the box (rooms/spaces, a section, or a plan rectangle) and put
// it on Revit's own default 3D view ("{3D - user}" or "{3D}") in one transaction. Shows nothing itself; the
// command (Cmd3DSnapshot) talks to the modeller.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using HeronTools.Utils;

namespace HeronTools.Services.Snapshot3D
{
    /// <summary>What a run did, for the command to report.</summary>
    internal sealed class SnapshotApplyResult
    {
        internal bool Succeeded { get; set; }
        internal View3D View { get; set; }
        internal bool Created { get; set; }
        internal bool CameraKept { get; set; }
        internal string Problem { get; set; }

        /// <summary>The section box as set, so the command can zoom the 3D view to it.</summary>
        internal BoundingBoxXYZ Box { get; set; }
    }

    internal static class Snapshot3DService
    {
        internal const string TransactionName = "3D Snapshot";

        /// <summary>Extra space round the box, like Revit's own Selection Box (BX). Ajmal's choice: 300 mm.</summary>
        internal static readonly double Padding = SnapshotTarget.MillimetresToFeet(300.0);

        /// <summary>Used only when a section's crop box says nothing about depth (should not happen).</summary>
        private static readonly double FallbackSectionDepth = SnapshotTarget.MillimetresToFeet(10000.0);

        /// <summary>Plan view range set to "Unlimited": how far above / below the plan's level the box goes instead.</summary>
        private static readonly double UnlimitedTopAboveLevel = SnapshotTarget.MillimetresToFeet(4000.0);
        private static readonly double UnlimitedBottomBelowLevel = SnapshotTarget.MillimetresToFeet(1000.0);

        /// <summary>
        /// For a section the camera looks the way the section looks, turned this much to the left and down
        /// (as a fraction of the forward direction - about 19 degrees each), so the depth reads in 3D instead of
        /// looking flat like the section itself.
        /// </summary>
        private const double SectionCameraSideTurn = 0.35;
        private const double SectionCameraDownTurn = 0.35;

        // ------------------------------------------------------------------ what was selected

        /// <summary>A room or space, and how to bring its coordinates into this model (identity unless it is in a link).</summary>
        internal sealed class SpatialPart
        {
            internal SpatialPart(SpatialElement element, Transform toHost, bool linked)
            {
                Element = element;
                ToHost = toHost ?? Transform.Identity;
                Linked = linked;
            }

            internal SpatialElement Element { get; }
            internal Transform ToHost { get; }
            internal bool Linked { get; }
        }

        /// <summary>What the modeller's selection holds that 3D Snapshot can use.</summary>
        internal sealed class SelectionRead
        {
            internal List<SpatialPart> Spatials { get; } = new List<SpatialPart>();
            internal List<View> SectionViews { get; } = new List<View>();

            /// <summary>Tags whose room or space cannot be reached: the link is not loaded, or it is not placed.</summary>
            internal int TagsWithoutLocalRoom { get; set; }

            /// <summary>Zones (or zone tags) with no placed space in them.</summary>
            internal int EmptyZones { get; set; }

            /// <summary>Rooms or spaces selected that are not placed or not enclosed (no area).</summary>
            internal int UnusableSpatials { get; set; }

            /// <summary>Section-type marks whose view is not a section (a plan callout, a drafting reference...).</summary>
            internal int MarksNotSections { get; set; }
        }

        /// <summary>
        /// Sorts the selection into rooms/spaces (also through room tags - linked rooms too -, space tags, zones and
        /// zone tags) and section views. Anything else is ignored.
        /// </summary>
        internal static SelectionRead ReadSelection(Document doc, ICollection<ElementId> ids)
        {
            var read = new SelectionRead();
            if (doc == null || ids == null)
                return read;

            var seenSpatial = new HashSet<string>(StringComparer.Ordinal);
            var seenViews = new HashSet<ElementId>();
            var viewersId = new ElementId(BuiltInCategory.OST_Viewers);
            var zoneTagsId = new ElementId(BuiltInCategory.OST_ZoneTags);

            void AddPart(SpatialPart part, string linkKey)
            {
                if (!IsUsable(part.Element)) { read.UnusableSpatials++; return; }
                // Same room twice (a room and its tag, two tags) counts once. The key carries the link, because
                // two links can hold rooms with the same id.
                if (seenSpatial.Add(linkKey + ":" + part.Element.Id.LongValue().ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    read.Spatials.Add(part);
            }

            void AddZoneSpaces(IList<ElementId> spaceIds)
            {
                int before = read.Spatials.Count;
                foreach (ElementId spaceId in spaceIds)
                {
                    if (doc.GetElement(spaceId) is Space space && IsUsable(space))
                        AddPart(new SpatialPart(space, null, false), "host");
                }
                if (read.Spatials.Count == before)
                    read.EmptyZones++;
            }

            foreach (ElementId id in ids)
            {
                Element e = doc.GetElement(id);
                if (e == null)
                    continue;

                if (e is Room room) { AddPart(new SpatialPart(room, null, false), "host"); continue; }
                if (e is Space space) { AddPart(new SpatialPart(space, null, false), "host"); continue; }
                if (ZoneCompat.TryGetSpaceIds(e, out IList<ElementId> zoneSpaces)) { AddZoneSpaces(zoneSpaces); continue; }

                if (e is RoomTag roomTag)
                {
                    SpatialPart part = RoomOfTag(doc, roomTag, out string linkKey);
                    if (part == null) read.TagsWithoutLocalRoom++;
                    else AddPart(part, linkKey);
                    continue;
                }

                if (e is SpaceTag spaceTag)
                {
                    if (spaceTag.Space == null) read.TagsWithoutLocalRoom++;
                    else AddPart(new SpatialPart(spaceTag.Space, null, false), "host");
                    continue;
                }

                // A zone tag is an ordinary tag (IndependentTag, see the API notes), in the Zone Tags category.
                if (e is IndependentTag tag && tag.Category != null && tag.Category.Id == zoneTagsId)
                {
                    bool foundZone = false;
                    foreach (ElementId taggedId in TagCompat.TaggedLocalElementIds(tag))
                    {
                        if (ZoneCompat.TryGetSpaceIds(doc.GetElement(taggedId), out IList<ElementId> taggedSpaces))
                        {
                            AddZoneSpaces(taggedSpaces);
                            foundZone = true;
                        }
                    }
                    if (!foundZone)
                        read.EmptyZones++;
                    continue;
                }

                // An elevation marker can hold up to four elevations; it counts only when it holds exactly one.
                if (e is ElevationMarker marker)
                {
                    View only = SingleElevationOf(doc, marker);
                    if (only != null && seenViews.Add(only.Id))
                        read.SectionViews.Add(only);
                    else if (only == null)
                        read.MarksNotSections++;
                    continue;
                }

                if (e.Category != null && e.Category.Id == viewersId)
                {
                    View view = ViewOfMark(doc, e);
                    if (view == null) { read.MarksNotSections++; continue; }
                    if (seenViews.Add(view.Id))
                        read.SectionViews.Add(view);
                }
            }

            return read;
        }

        /// <summary>
        /// The room a room tag points at - in this model, or in a loaded link (with the link's placement, so the box
        /// lands where the linked room shows here). Null when the link is not loaded or the tag is orphaned.
        /// </summary>
        private static SpatialPart RoomOfTag(Document doc, RoomTag tag, out string linkKey)
        {
            linkKey = "host";
            LinkElementId tagged = tag.TaggedRoomId;
            if (tagged == null)
                return null;

            if (tagged.LinkInstanceId == null || tagged.LinkInstanceId == ElementId.InvalidElementId)
            {
                Room local = doc.GetElement(tagged.HostElementId) as Room;
                return local == null ? null : new SpatialPart(local, null, false);
            }

            RevitLinkInstance link = doc.GetElement(tagged.LinkInstanceId) as RevitLinkInstance;
            Document linkDoc = link?.GetLinkDocument();
            Room linked = linkDoc?.GetElement(tagged.LinkedElementId) as Room;
            if (linked == null)
                return null;

            linkKey = "link" + tagged.LinkInstanceId.LongValue().ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new SpatialPart(linked, link.GetTotalTransform(), true);
        }

        /// <summary>A room or space that is placed and enclosed, with a height.</summary>
        private static bool IsUsable(SpatialElement spatial)
        {
            if (spatial == null || !spatial.IsValidObject || spatial.Location == null)
                return false;
            if (spatial.Area <= 1e-9 || spatial.Level == null)
                return false;
            return UnboundedHeight(spatial) > 1e-6;
        }

        private static double UnboundedHeight(SpatialElement spatial)
        {
            if (spatial is Room room) return room.UnboundedHeight;
            if (spatial is Space space) return space.UnboundedHeight;
            return 0.0;
        }

        private static double BaseOffset(SpatialElement spatial)
        {
            if (spatial is Room room) return room.BaseOffset;
            if (spatial is Space space) return space.BaseOffset;
            return 0.0;
        }

        /// <summary>True for the views 3D Snapshot treats as a section: sections, detail sections and elevations.</summary>
        internal static bool IsSectionLike(View view)
        {
            if (!(view is ViewSection) || view.IsTemplate)
                return false;
            return view.ViewType == ViewType.Section
                || view.ViewType == ViewType.Detail
                || view.ViewType == ViewType.Elevation;
        }

        /// <summary>True for the plans a window can be dragged in.</summary>
        internal static bool IsPlan(View view)
        {
            if (!(view is ViewPlan) || view.IsTemplate)
                return false;
            return view.ViewType == ViewType.FloorPlan
                || view.ViewType == ViewType.CeilingPlan
                || view.ViewType == ViewType.EngineeringPlan
                || view.ViewType == ViewType.AreaPlan;
        }

        /// <summary>
        /// The section view a selected section mark stands for. A mark is its own element (category Viewers)
        /// that carries its view's name; the view is found by that name and, if two section-type views share
        /// it, by which one owns the mark.
        /// </summary>
        private static View ViewOfMark(Document doc, Element mark)
        {
            string name = mark.get_Parameter(BuiltInParameter.VIEW_NAME)?.AsString();
            if (string.IsNullOrWhiteSpace(name))
                name = mark.Name;
            if (string.IsNullOrWhiteSpace(name))
                return null;

            List<View> candidates = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSection))
                .Cast<View>()
                .Where(v => IsSectionLike(v) && string.Equals(v.Name, name, StringComparison.Ordinal))
                .ToList();

            if (candidates.Count <= 1)
                return candidates.FirstOrDefault();

            var viewersFilter = new ElementCategoryFilter(BuiltInCategory.OST_Viewers);
            foreach (View candidate in candidates)
            {
                try
                {
                    if (candidate.GetDependentElements(viewersFilter).Contains(mark.Id))
                        return candidate;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // This view cannot list what it owns; try the next one.
                }
            }

            return candidates[0];
        }

        private static View SingleElevationOf(Document doc, ElevationMarker marker)
        {
            View found = null;
            for (int i = 0; i < marker.MaximumViewCount; i++)
            {
                if (marker.IsAvailableIndex(i))
                    continue;   // empty arrow

                View v = doc.GetElement(marker.GetViewId(i)) as View;
                if (v == null || !IsSectionLike(v))
                    continue;
                if (found != null)
                    return null;   // more than one elevation on this marker: the modeller must pick the arrow
                found = v;
            }

            return found;
        }

        // ------------------------------------------------------------------ measuring the box

        /// <summary>
        /// Rooms / spaces: their outline, from level + base offset up to upper limit + limit offset. Each point is
        /// measured in the room's own model and brought into this one (a linked room keeps the link's placement).
        /// </summary>
        internal static SnapshotTarget FromSpatials(IList<SpatialPart> parts, View activeView)
        {
            string name = parts.Count == 1
                ? DescribeSpatial(parts[0].Element) + (parts[0].Linked ? " (linked)" : string.Empty)
                : parts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " rooms / spaces";
            var target = new SnapshotTarget(SnapshotMode.Rooms, activeView?.RightDirection, name);

            var options = new SpatialElementBoundaryOptions();
            foreach (SpatialPart part in parts)
            {
                SpatialElement spatial = part.Element;
                double bottom = spatial.Level.ProjectElevation + BaseOffset(spatial);
                double top = bottom + UnboundedHeight(spatial);

                // Both the floor and the ceiling of each outline point, so a link placed higher or lower, or turned,
                // moves the whole box with it.
                void AddColumn(XYZ p)
                {
                    target.Add(part.ToHost.OfPoint(new XYZ(p.X, p.Y, bottom)));
                    target.Add(part.ToHost.OfPoint(new XYZ(p.X, p.Y, top)));
                }

                bool hadBoundary = false;
                IList<IList<BoundarySegment>> loops = null;
                try { loops = spatial.GetBoundarySegments(options); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { loops = null; }

                if (loops != null)
                {
                    foreach (IList<BoundarySegment> loop in loops)
                    {
                        foreach (BoundarySegment segment in loop)
                        {
                            Curve curve = segment?.GetCurve();
                            if (curve == null)
                                continue;
                            foreach (XYZ p in curve.Tessellate())
                            {
                                AddColumn(p);
                                hadBoundary = true;
                            }
                        }
                    }
                }

                if (!hadBoundary)
                {
                    // No boundary to read: fall back to the room's own extents (in its own model's coordinates).
                    BoundingBoxXYZ bb = spatial.get_BoundingBox(null);
                    if (bb != null)
                    {
                        foreach (XYZ corner in SnapshotTarget.Corners(bb))
                            AddColumn(corner);
                    }
                }
            }

            return target;
        }

        /// <summary>
        /// A section or elevation: its crop region and far clip, the cut on the front face, looking the same way.
        /// </summary>
        /// <remarks>
        /// The section's CropBox is its view volume: X right, Y up, Z towards the modeller, the cut plane at the
        /// front (Z max) and the far clip at the back (Z min) - see ViewSection.CreateSection and View.CropBox in
        /// the API notes. Its eight corners are measured in a frame whose Right is the section's own right, so
        /// the 3D box is turned exactly like the section line in plan.
        /// </remarks>
        internal static SnapshotTarget FromSection(View section, out string problem)
        {
            problem = null;
            BoundingBoxXYZ crop = section.CropBox;
            if (crop == null)
            {
                problem = "The section \"" + section.Name + "\" has no crop region to measure.";
                return null;
            }

            XYZ viewDirection = section.ViewDirection;
            if (Math.Abs(viewDirection.Z) > 0.01)
            {
                problem = "The view \"" + section.Name + "\" does not look sideways (it is tilted), so a 3D section " +
                          "box cannot match it.";
                return null;
            }

            var target = new SnapshotTarget(SnapshotMode.Section, section.RightDirection, section.Name)
            {
                FaceDirection = viewDirection,
            };

            double minZ = crop.Min.Z, maxZ = crop.Max.Z;
            if (maxZ - minZ < 1e-6)
                minZ = maxZ - FallbackSectionDepth;

            Transform t = crop.Transform ?? Transform.Identity;
            foreach (double x in new[] { crop.Min.X, crop.Max.X })
                foreach (double y in new[] { crop.Min.Y, crop.Max.Y })
                    foreach (double z in new[] { minZ, maxZ })
                        target.Add(t.OfPoint(new XYZ(x, y, z)));

            // The cut is the face nearest the modeller (furthest along the view direction).
            target.CutOnMinBackSide = viewDirection.DotProduct(target.Back) < 0.0;

            if (target.IsEmpty || target.Width < 1e-3 || target.Height < 1e-3)
            {
                problem = "The section \"" + section.Name + "\" has a crop region too small to measure.";
                return null;
            }

            return target;
        }

        /// <summary>A window dragged in a plan: its two corners, from the bottom to the top of the view range.</summary>
        internal static SnapshotTarget FromPlanRectangle(ViewPlan plan, XYZ cornerA, XYZ cornerB, out string problem)
        {
            problem = null;
            var target = new SnapshotTarget(SnapshotMode.Rectangle, plan.RightDirection, plan.Name);

            // The two clicks are opposite corners on screen. Measured along the plan's own right and up, they
            // give the rectangle as drawn - also in a plan whose crop is turned.
            XYZ up = target.Back;
            double x1 = cornerA.DotProduct(target.Right), x2 = cornerB.DotProduct(target.Right);
            double y1 = cornerA.DotProduct(up), y2 = cornerB.DotProduct(up);
            if (Math.Abs(x1 - x2) < SnapshotTarget.MillimetresToFeet(10) || Math.Abs(y1 - y2) < SnapshotTarget.MillimetresToFeet(10))
            {
                problem = "The window was too small. Drag a window round the area you want - click one corner, then the opposite one.";
                return null;
            }

            if (!TryViewRangeElevations(plan, out double bottom, out double top, out problem))
                return null;

            // Height comes from the view range, not from the elevation the clicks landed on.
            foreach (double x in new[] { x1, x2 })
                foreach (double y in new[] { y1, y2 })
                {
                    XYZ flat = target.Right.Multiply(x) + up.Multiply(y);
                    target.Add(new XYZ(flat.X, flat.Y, bottom));
                    target.Add(new XYZ(flat.X, flat.Y, top));
                }

            return target;
        }

        /// <summary>
        /// The lowest and highest planes of a plan's view range (Top, Bottom, View Depth), in model Z.
        /// Works for ceiling plans too (they look up, so their planes are the other way round).
        /// </summary>
        private static bool TryViewRangeElevations(ViewPlan plan, out double bottom, out double top, out string problem)
        {
            bottom = double.MaxValue;
            top = double.MinValue;
            problem = null;

            Level planLevel = plan.GenLevel;
            if (planLevel == null)
            {
                problem = "The plan \"" + plan.Name + "\" has no level, so its view range cannot be read.";
                return false;
            }

            PlanViewRange range = plan.GetViewRange();
            List<Level> levels = new FilteredElementCollector(plan.Document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.ProjectElevation)
                .ToList();

            bool anyUnlimited = false;
            foreach (PlanViewPlane plane in new[] { PlanViewPlane.TopClipPlane, PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane })
            {
                ElementId levelId = range.GetLevelId(plane);
                Level level = ResolveRangeLevel(plan.Document, levelId, planLevel, levels);
                if (level == null)
                {
                    anyUnlimited = true;
                    continue;
                }

                double z = level.ProjectElevation + range.GetOffset(plane);
                if (z < bottom) bottom = z;
                if (z > top) top = z;
            }

            if (anyUnlimited)
            {
                // "Unlimited" has no height. Stand in with a storey: a little below and a storey above the level.
                double low = planLevel.ProjectElevation - UnlimitedBottomBelowLevel;
                double high = planLevel.ProjectElevation + UnlimitedTopAboveLevel;
                if (bottom == double.MaxValue || low < bottom) bottom = Math.Min(bottom, low);
                if (top == double.MinValue || high > top) top = Math.Max(top, high);
            }

            if (top - bottom < 1e-3)
            {
                problem = "The view range of \"" + plan.Name + "\" has no height (its top and bottom are at the same level).";
                return false;
            }

            return true;
        }

        /// <summary>A view range plane's level; null for "Unlimited" or a level that is not there.</summary>
        private static Level ResolveRangeLevel(Document doc, ElementId levelId, Level planLevel, List<Level> levels)
        {
            if (levelId == null || levelId == ElementId.InvalidElementId || levelId == PlanViewRange.Unlimited)
                return null;
            if (levelId == PlanViewRange.Current)
                return planLevel;
            if (levelId == PlanViewRange.LevelAbove)
                return levels.FirstOrDefault(l => l.ProjectElevation > planLevel.ProjectElevation + 1e-6);
            if (levelId == PlanViewRange.LevelBelow)
                return levels.LastOrDefault(l => l.ProjectElevation < planLevel.ProjectElevation - 1e-6);
            return doc.GetElement(levelId) as Level;
        }

        private static string DescribeSpatial(SpatialElement spatial)
        {
            string number = spatial.Number;
            string name = spatial.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
            string kind = spatial is Space ? "Space" : "Room";
            return (kind + " " + number + " " + name).Trim();
        }

        // ------------------------------------------------------------------ the 3D view

        /// <summary>
        /// Revit's own default 3D view - the one its 3D button opens: "{3D - ajmal.al}" in a workshared model
        /// (one per user), "{3D}" otherwise. Ajmal's choice: update that view, so there is never a second 3D view.
        /// </summary>
        internal static string DefaultViewName(Document doc)
        {
            if (doc != null && doc.IsWorkshared)
                return "{3D - " + (doc.Application?.Username ?? string.Empty) + "}";
            return "{3D}";
        }

        private static View3D FindDefaultView(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static ElementId ThreeDViewTypeId(Document doc)
        {
            ViewFamilyType type = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);
            return type?.Id ?? ElementId.InvalidElementId;
        }

        /// <summary>
        /// Finds Revit's default 3D view (making it the first time, if Revit allows) and sets its section box -
        /// and, for a section, its camera - in ONE transaction named "3D Snapshot". Any failure rolls the whole
        /// run back.
        /// </summary>
        internal static SnapshotApplyResult Apply(Document doc, SnapshotTarget target)
        {
            var result = new SnapshotApplyResult();
            string viewName = DefaultViewName(doc);
            BoundingBoxXYZ box = target.ToSectionBox(Padding);
            result.Box = box;

            using (var t = new Transaction(doc, TransactionName))
            {
                try
                {
                    t.Start();

                    View3D view = FindDefaultView(doc, viewName);
                    if (view == null)
                    {
                        ElementId typeId = ThreeDViewTypeId(doc);
                        if (typeId == ElementId.InvalidElementId)
                        {
                            t.RollBack();
                            result.Problem = "This project has no 3D view type, so no 3D view can be made.";
                            return result;
                        }

                        // Revit's default 3D view does not exist yet. Make it with Revit's exact name, so Revit's 3D
                        // button opens this same view. Revit may refuse curly brackets in a name an add-in gives; then
                        // the modeller makes it once with Revit's own button.
                        view = View3D.CreateIsometric(doc, typeId);
                        try
                        {
                            view.Name = viewName;
                        }
                        catch (Autodesk.Revit.Exceptions.ArgumentException)
                        {
                            t.RollBack();
                            result.Problem = "Revit's own 3D view \"" + viewName + "\" is not in this model yet, and Revit " +
                                             "does not let an add-in make it. Nothing was changed.\n\n" +
                                             "Click Revit's 3D button once (View tab > 3D View, or the small house on the " +
                                             "Quick Access Toolbar), go back to your view, then run 3D Snapshot again.";
                            return result;
                        }

                        result.Created = true;
                    }

                    view.SetSectionBox(box);
                    view.IsSectionBoxActive = true;

                    if (target.FaceDirection != null)
                    {
                        if (view.IsLocked)
                            result.CameraKept = true;   // a locked 3D view cannot be turned; the box still moves
                        else
                            view.SetOrientation(SectionCamera(target, box));
                    }

                    if (t.Commit() != TransactionStatus.Committed)
                    {
                        result.Problem = "Revit did not accept the change to \"" + viewName + "\", so nothing was changed.\n\n" +
                                         "If the model is workshared, check you can edit that view (synchronise, then try again).";
                        return result;
                    }

                    result.View = view;
                    result.Succeeded = true;
                    return result;
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded())
                        t.RollBack();
                    result.Problem = "Revit refused the change, so nothing was changed.\n\nWhat Revit said: " + ex.Message;
                    return result;
                }
            }
        }

        /// <summary>
        /// The camera for a section: looking the way the section looks, from a little above and to the right,
        /// aimed at the middle of the box.
        /// </summary>
        private static ViewOrientation3D SectionCamera(SnapshotTarget target, BoundingBoxXYZ box)
        {
            XYZ intoScreen = target.FaceDirection.Negate();
            XYZ sectionRight = target.Right;   // the section's own right, so the eye moves to the modeller's right

            XYZ forward = (intoScreen - sectionRight.Multiply(SectionCameraSideTurn) - XYZ.BasisZ.Multiply(SectionCameraDownTurn)).Normalize();
            XYZ up = (XYZ.BasisZ - forward.Multiply(XYZ.BasisZ.DotProduct(forward))).Normalize();

            XYZ[] corners = SnapshotTarget.Corners(box);
            XYZ centre = corners.Aggregate(XYZ.Zero, (sum, p) => sum + p).Divide(corners.Length);
            double reach = corners.Max(p => p.DistanceTo(centre));
            XYZ eye = centre - forward.Multiply(Math.Max(reach * 3.0, 10.0));

            return new ViewOrientation3D(eye, up, forward);
        }
    }
}
