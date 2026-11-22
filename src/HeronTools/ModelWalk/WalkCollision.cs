// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - rays against the walk view. Only what the view SHOWS exists for the walk: hide a category in
// Visibility/Graphics, filter it out or cut it with the section box, and you walk through it (Revit's
// ReferenceIntersector skips hidden elements and geometry outside the section box). Linked models collide too.
//
// Speed on big models: the old tool cast up to ~19 full-model rays a frame. Here:
//  - collision rays test this model's elements only inside a box around the eye (re-centred as you walk), and
//    linked models only when their box is near - a box filter cannot be used on linked elements (the API note says
//    it is checked in the link's own coordinates), so a nearby link is tested whole;
//  - the long rays (Inspect, clear height, a floor far below) use the whole view, a few times a second only;
//  - every ray is counted; past the frame's budget a move is refused rather than guessed.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using HeronTools.Utils;

namespace HeronTools.Services.ModelWalk
{
    /// <summary>One resolved ray hit.</summary>
    internal sealed class WalkRayHit
    {
        public double DistanceFt;
        public XYZ Point;
        public bool IsDoor;
        public bool IsWindow;

        /// <summary>The element hit - the linked element itself when the hit came from a link.</summary>
        public Element Element;

        /// <summary>The link instance the element is in, or null for this model's own elements.</summary>
        public RevitLinkInstance Link;

        public bool FromLink => Link != null;

        /// <summary>
        /// When Revit gave a surface but no element (<see cref="Element"/> null): what is still known - the link it is in
        /// (<see cref="Link"/>), and the id Revit's reference named, for the readout. Empty when nothing at all.
        /// </summary>
        public string UnnamedId = "";
    }

    internal sealed class WalkCollision
    {
        private readonly Document _doc;
        private readonly View3D _view;

        private ReferenceIntersector _full;
        private ReferenceIntersector _nearHost;
        private ReferenceIntersector _nearLinks;
        private XYZ _nearCentre;
        private readonly List<KeyValuePair<ElementId, BoundingBoxXYZ>> _links = new List<KeyValuePair<ElementId, BoundingBoxXYZ>>();

        public WalkCollision(Document doc, View3D view)
        {
            _doc = doc;
            _view = view;
            Refresh();
        }

        /// <summary>Rays cast since <see cref="BeginFrame"/>.</summary>
        public int RaysThisFrame { get; private set; }

        /// <summary>True once this frame has used its ray budget.</summary>
        public bool OverBudget => RaysThisFrame >= WalkTuning.RayBudgetPerFrame;

        public void BeginFrame() => RaysThisFrame = 0;

        /// <summary>Rebuild after visibility or the model changed (hide, VG, filters, a link loaded).</summary>
        public void Refresh()
        {
            _full = new ReferenceIntersector(_view) { FindReferencesInRevitLinks = true };

            _links.Clear();
            foreach (Element e in new FilteredElementCollector(_doc, _view.Id).OfClass(typeof(RevitLinkInstance)))
            {
                BoundingBoxXYZ box = null;
                try { box = e.get_BoundingBox(null); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { box = null; }
                _links.Add(new KeyValuePair<ElementId, BoundingBoxXYZ>(e.Id, box));
            }

            _nearCentre = null;   // the near box is rebuilt on the next cast
        }

        /// <summary>Nearest visible hit within reach, testing only what is near the eye. Null when nothing is in reach.</summary>
        public WalkRayHit CastNear(XYZ origin, XYZ direction, double maxDistanceFt)
        {
            EnsureNear(origin);
            WalkRayHit host = Cast(_nearHost, origin, direction, maxDistanceFt);
            WalkRayHit link = _nearLinks != null ? Cast(_nearLinks, origin, direction, maxDistanceFt) : null;
            if (host == null) return link;
            if (link == null) return host;
            return link.DistanceFt < host.DistanceFt ? link : host;
        }

        /// <summary>Nearest visible hit in the whole view (host and links), for the long rays.</summary>
        public WalkRayHit CastFull(XYZ origin, XYZ direction, double maxDistanceFt)
        {
            return Cast(_full, origin, direction, maxDistanceFt);
        }

        private void EnsureNear(XYZ eye)
        {
            if (_nearCentre != null && _nearHost != null &&
                Math.Abs(eye.X - _nearCentre.X) < WalkTuning.NearBoxRecentreFt &&
                Math.Abs(eye.Y - _nearCentre.Y) < WalkTuning.NearBoxRecentreFt &&
                Math.Abs(eye.Z - _nearCentre.Z) < WalkTuning.NearBoxRecentreFt)
                return;

            _nearCentre = eye;
            double h = WalkTuning.NearBoxHalfFt;
            var min = new XYZ(eye.X - h, eye.Y - h, eye.Z - WalkTuning.NearBoxBelowFt);
            var max = new XYZ(eye.X + h, eye.Y + h, eye.Z + WalkTuning.NearBoxAboveFt);
            var filter = new BoundingBoxIntersectsFilter(new Outline(min, max));
            _nearHost = new ReferenceIntersector(filter, FindReferenceTarget.All, _view) { FindReferencesInRevitLinks = false };

            var near = new List<ElementId>();
            foreach (KeyValuePair<ElementId, BoundingBoxXYZ> link in _links)
            {
                BoundingBoxXYZ b = link.Value;
                // No box from Revit: test the link always, never skip a wall that may be in it.
                if (b == null || (b.Min.X <= max.X && b.Max.X >= min.X && b.Min.Y <= max.Y && b.Max.Y >= min.Y &&
                                  b.Min.Z <= max.Z && b.Max.Z >= min.Z))
                    near.Add(link.Key);
            }
            _nearLinks = near.Count == 0
                ? null
                : new ReferenceIntersector(near, FindReferenceTarget.All, _view) { FindReferencesInRevitLinks = true };
        }

        private WalkRayHit Cast(ReferenceIntersector intersector, XYZ origin, XYZ direction, double maxDistanceFt)
        {
            if (intersector == null || direction == null || direction.GetLength() < 1e-9)
                return null;

            RaysThisFrame++;
            ReferenceWithContext context;
            try
            {
                context = intersector.FindNearest(origin, direction);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // A view half-way through regenerating must never end a frame.
                return null;
            }

            if (context == null || context.Proximity > maxDistanceFt)
                return null;

            var hit = new WalkRayHit
            {
                DistanceFt = context.Proximity,
                Point = origin + (direction.Normalize() * context.Proximity)
            };

            Reference reference = context.GetReference();
            if (reference == null)
                return hit;

            Element element = _doc.GetElement(reference);
            if (element is RevitLinkInstance link)
            {
                // The link is known even when its element is not (a link inside the link, or a link being reloaded):
                // the readout then names the link instead of "nothing".
                hit.Link = link;
                Document linkDoc = link.GetLinkDocument();
                Element linked = linkDoc?.GetElement(reference.LinkedElementId);
                hit.Element = linked;
                if (linked == null && ElementIdHelper.IsValid(reference.LinkedElementId))
                    hit.UnnamedId = reference.LinkedElementId.LongValue().ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                hit.Element = element;
                if (element == null && ElementIdHelper.IsValid(reference.ElementId))
                    hit.UnnamedId = reference.ElementId.LongValue().ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            hit.IsDoor = IsOrInside(hit.Element, BuiltInCategory.OST_Doors);
            hit.IsWindow = !hit.IsDoor && IsOrInside(hit.Element, BuiltInCategory.OST_Windows);
            return hit;
        }

        /// <summary>
        /// The element is of the category, or is a nested part of a family that is (a door panel or frame made as a
        /// shared nested family - common in real architectural door families, host or linked: the ray hits the part,
        /// whose own category may be Generic Models). Up to 5 levels. Never throws.
        /// </summary>
        private static bool IsOrInside(Element element, BuiltInCategory builtIn)
        {
            try
            {
                for (int depth = 0; element != null && depth < 5; depth++)
                {
                    if (element.Category != null && ElementIdHelper.IsCategory(element.Category.Id, builtIn))
                        return true;
                    element = (element as FamilyInstance)?.SuperComponent;
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { /* not a door, then */ }
            return false;
        }

        // ------------------------------------------------------------------ plain words for the HUD and the report

        /// <summary>"Ducts - Rectangular Duct : Supply - 600x400 - ID 1234567" (the full 64-bit id).</summary>
        public static string DescribeLine1(WalkRayHit hit)
        {
            if (hit == null)
                return "";
            if (hit.Element == null)
            {
                // Revit hit a surface but gave no element: say what is known, in plain words.
                string id = string.IsNullOrEmpty(hit.UnnamedId) ? "" : " (ID " + hit.UnnamedId + ")";
                return hit.FromLink
                    ? "Something in the link " + LinkName(hit.Link) + id + " - Revit gives no details for it"
                    : "A surface Revit gives no element for" + id;
            }

            Element element = hit.Element;
            string size = SizeText(element);
            return CategoryName(element) + " - " + FamilyTypeName(element) + (string.IsNullOrWhiteSpace(size) ? "" : " - " + size) +
                   " - ID " + element.Id.LongValue();
        }

        /// <summary>"Level 03 - SA-03 - Link: MEP.rvt" - only the parts the element has.</summary>
        public static string DescribeLine2(WalkRayHit hit)
        {
            if (hit?.Element == null)
                return "";
            var parts = new List<string>();
            string level = LevelName(hit.Element);
            if (!string.IsNullOrWhiteSpace(level)) parts.Add(level);
            string system = SystemText(hit.Element);
            if (!string.IsNullOrWhiteSpace(system)) parts.Add(system);
            if (hit.FromLink) parts.Add("Link: " + LinkName(hit.Link));
            return string.Join("  -  ", parts);
        }

        public static string CategoryName(Element element) =>
            element?.Category != null ? element.Category.Name : "Element";

        /// <summary>"Family : Type" for a family instance, the type name for a system family (walls, ducts, pipes).</summary>
        public static string FamilyTypeName(Element element)
        {
            if (element == null)
                return "";
            try
            {
                if (element is FamilyInstance fi && fi.Symbol != null)
                    return fi.Symbol.FamilyName + " : " + element.Name;
                if (element.Document?.GetElement(element.GetTypeId()) is ElementType type && !string.IsNullOrWhiteSpace(type.FamilyName))
                    return type.FamilyName + " : " + element.Name;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { /* fall back to the name */ }
            return element.Name ?? "";
        }

        public static string SizeText(Element element) => (ReadText(element, BuiltInParameter.RBS_CALCULATED_SIZE) ?? "").Trim();

        /// <summary>The MEP system name, else its classification; empty for elements with none.</summary>
        public static string SystemText(Element element)
        {
            string system = ReadText(element, BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
            if (string.IsNullOrWhiteSpace(system))
                system = ReadText(element, BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
            return (system ?? "").Trim();
        }

        public static string LinkName(RevitLinkInstance link)
        {
            if (link == null) return "";
            try
            {
                Document linkDoc = link.GetLinkDocument();
                if (linkDoc != null && !string.IsNullOrWhiteSpace(linkDoc.Title))
                    return linkDoc.Title;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { }
            return link.Name;
        }

        private static string ReadText(Element element, BuiltInParameter which)
        {
            if (element == null)
                return null;
            return ReadTextOf(element, which);
        }

        private static string ReadTextOf(Element element, BuiltInParameter which)
        {
            try
            {
                Parameter p = element.get_Parameter(which);
                if (p == null || !p.HasValue)
                    return null;
                string text = p.StorageType == StorageType.String ? p.AsString() : null;
                return string.IsNullOrWhiteSpace(text) ? p.AsValueString() : text;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;   // a bonus line - never fail the readout for it
            }
        }

        /// <summary>
        /// The element's level, read from the element's OWN document (so a linked element shows the linked model's
        /// level name): Level first, then the MEP reference level, then the family level parameters.
        /// </summary>
        public static string LevelName(Element element)
        {
            try
            {
                ElementId levelId = element.LevelId;
                foreach (BuiltInParameter p in new[] { BuiltInParameter.RBS_START_LEVEL_PARAM, BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM })
                {
                    if (ElementIdHelper.IsValid(levelId))
                        break;
                    Parameter param = element.get_Parameter(p);
                    levelId = param != null && param.HasValue && param.StorageType == StorageType.ElementId ? param.AsElementId() : ElementId.InvalidElementId;
                }
                if (!ElementIdHelper.IsValid(levelId))
                    return "";
                return element.Document?.GetElement(levelId) is Level level ? level.Name : "";
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return "";
            }
        }
    }
}
