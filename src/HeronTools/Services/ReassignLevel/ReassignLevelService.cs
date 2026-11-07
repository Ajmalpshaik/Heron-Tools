// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The level-reassignment engine for MEP curves and free-standing family instances: candidate collection
// (whole-project FROM level, or an explicit selection), eligibility checks, and a per-element change that
// is MEASURED - each element's position is read before and after, its offset corrected if Revit moved it,
// and the change undone for that element if it still is not where it was. Spaces are not reassigned: the
// API cannot change a space's level without deleting and re-making it, which would lose its tags, zone and
// schedule links.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

namespace HeronTools.Services.ReassignLevel
{
    /// <summary>What happened to one element.</summary>
    internal enum ReassignOutcome
    {
        Reassigned,
        AlreadyOnTarget,
        NotOnFromLevel,
        /// <summary>Revit would not take the new level for this element.</summary>
        Refused,
        /// <summary>The element would have moved and could not be put back by its offset - left as it was.</summary>
        WouldMove
    }

    /// <summary>
    /// Level-reassignment engine. Contains no UI and starts no Transaction of its own - the caller runs it
    /// inside one transaction; each element gets its own SubTransaction so one bad element is undone alone.
    /// </summary>
    internal static class ReassignLevelService
    {
        /// <summary>How far an element may shift and still count as "not moved": about 0.3 mm.</summary>
        private const double PositionTolerance = 0.001;

        /// <summary>
        /// Every supported element (MEP curve, free-standing or level-based family instance) on the FROM
        /// level. Hosted family instances and spaces on that level are counted, not returned.
        /// </summary>
        internal static List<Element> CollectCandidates(Document doc, ElementId fromId,
                                                        out int skippedHostedCount, out int skippedSpaceCount)
        {
            var filter = new LogicalOrFilter(new List<ElementFilter>
            {
                new ElementClassFilter(typeof(MEPCurve)),
                new ElementClassFilter(typeof(FamilyInstance)),
                new ElementClassFilter(typeof(SpatialElement))
            });

            var candidates = new List<Element>();
            int skippedHosted = 0;
            int skippedSpaces = 0;

            foreach (Element element in new FilteredElementCollector(doc).WherePasses(filter).WhereElementIsNotElementType())
            {
                if (element is Space space)
                {
                    if (space.LevelId == fromId)
                        skippedSpaces++;
                    continue;
                }

                if (element is FamilyInstance familyInstance && IsHosted(familyInstance))
                {
                    if (IsFamilyInstanceOnLevel(familyInstance, fromId))
                        skippedHosted++;
                    continue;
                }

                ElementId current = GetCurrentLevelId(element);
                if (current != null && current == fromId)
                    candidates.Add(element);
            }

            skippedHostedCount = skippedHosted;
            skippedSpaceCount = skippedSpaces;
            return candidates;
        }

        /// <summary>
        /// The supported elements among the selection, whatever level each is on now. Hosted family
        /// instances, spaces and anything unsupported (or with no level) are counted, not returned.
        /// </summary>
        internal static List<Element> CollectCandidatesFromSelection(
            Document doc,
            ICollection<ElementId> selectedIds,
            out int skippedHostedCount,
            out int skippedSpaceCount,
            out int skippedUnsupportedCount)
        {
            var candidates = new List<Element>();
            int skippedHosted = 0;
            int skippedSpaces = 0;
            int skippedUnsupported = 0;

            if (doc != null && selectedIds != null)
            {
                foreach (ElementId id in selectedIds)
                {
                    Element element = doc.GetElement(id);
                    if (element is Space)
                    {
                        skippedSpaces++;
                        continue;
                    }

                    if (element is FamilyInstance familyInstance && IsHosted(familyInstance))
                    {
                        skippedHosted++;
                        continue;
                    }

                    ElementId current = element != null ? GetCurrentLevelId(element) : null;
                    if (current != null && current != ElementId.InvalidElementId)
                        candidates.Add(element);
                    else
                        skippedUnsupported++;
                }
            }

            skippedHostedCount = skippedHosted;
            skippedSpaceCount = skippedSpaces;
            skippedUnsupportedCount = skippedUnsupported;
            return candidates;
        }

        /// <summary>
        /// Points the element at <paramref name="toLevel"/> without moving it. When
        /// <paramref name="requiredFromId"/> is given, only an element on that level is changed. Must run
        /// inside an open Transaction.
        /// </summary>
        internal static ReassignOutcome Reassign(Document doc, Element element, Level toLevel, ElementId requiredFromId)
        {
            ElementId currentId = GetCurrentLevelId(element);
            if (currentId == null || currentId == ElementId.InvalidElementId)
                return ReassignOutcome.Refused;
            if (requiredFromId != null && currentId != requiredFromId)
                return ReassignOutcome.NotOnFromLevel;
            if (currentId == toLevel.Id)
                return ReassignOutcome.AlreadyOnTarget;

            IList<XYZ> before = GetPositionPoints(element);
            if (before.Count == 0)
                return ReassignOutcome.Refused;   // nothing to measure, so "not moved" could not be proved

            using (var step = new SubTransaction(doc))
            {
                step.Start();

                bool set;
                try { set = SetLevel(element, toLevel); }
                catch (Exception) { set = false; }
                if (!set)
                {
                    step.RollBack();
                    return ReassignOutcome.Refused;
                }

                doc.Regenerate();
                IList<XYZ> after = GetPositionPoints(element);
                if (!SamePlace(before, after))
                {
                    // Revit kept the offset and moved the element to the new level: give the height back
                    // through its offset, then measure again.
                    double rise = HeightChange(before, after);
                    bool corrected = !double.IsNaN(rise) && TryShiftOffset(element, -rise);
                    if (corrected)
                    {
                        doc.Regenerate();
                        corrected = SamePlace(before, GetPositionPoints(element));
                    }

                    if (!corrected)
                    {
                        step.RollBack();
                        return ReassignOutcome.WouldMove;
                    }
                }

                return step.Commit() == TransactionStatus.Committed ? ReassignOutcome.Reassigned : ReassignOutcome.Refused;
            }
        }

        /// <summary>A level-based family's Host IS its level - that is not "hosted" for this tool.</summary>
        private static bool IsHosted(FamilyInstance familyInstance)
        {
            return familyInstance.Host != null && !(familyInstance.Host is Level);
        }

        /// <summary>The level a supported element references now, or null when unsupported / none.</summary>
        private static ElementId GetCurrentLevelId(Element element)
        {
            if (element is MEPCurve mepCurve)
            {
                try
                {
                    if (mepCurve.ReferenceLevel != null)
                        return mepCurve.ReferenceLevel.Id;
                }
                catch (Exception)
                {
                    // Some MEP curves have no reference level - fall through to the parameters.
                }

                if (mepCurve.LevelId != null && mepCurve.LevelId != ElementId.InvalidElementId)
                    return mepCurve.LevelId;

                Parameter startLevel = mepCurve.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                return startLevel != null && startLevel.StorageType == StorageType.ElementId ? startLevel.AsElementId() : null;
            }

            if (element is FamilyInstance familyInstance && !IsHosted(familyInstance))
            {
                Parameter levelParam = GetFamilyLevelParameter(familyInstance);
                if (levelParam != null && levelParam.StorageType == StorageType.ElementId)
                    return levelParam.AsElementId();
                return null;   // no writable level parameter: it cannot be reassigned anyway
            }

            return null;
        }

        private static bool IsFamilyInstanceOnLevel(FamilyInstance familyInstance, ElementId levelId)
        {
            Parameter levelParam = familyInstance.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                                   ?? familyInstance.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
            if (levelParam != null && levelParam.StorageType == StorageType.ElementId && levelParam.AsElementId() == levelId)
                return true;
            return familyInstance.LevelId == levelId;
        }

        private static Parameter GetFamilyLevelParameter(Element element)
        {
            Parameter levelParam = element?.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
            if (levelParam != null && !levelParam.IsReadOnly)
                return levelParam;

            Parameter referenceLevelParam = element?.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
            if (referenceLevelParam != null && !referenceLevelParam.IsReadOnly)
                return referenceLevelParam;

            return null;
        }

        private static bool SetLevel(Element element, Level toLevel)
        {
            if (element is MEPCurve mepCurve)
            {
                try
                {
                    mepCurve.ReferenceLevel = toLevel;
                    return true;
                }
                catch (Exception)
                {
                    Parameter startLevel = mepCurve.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                    return startLevel != null && !startLevel.IsReadOnly &&
                           startLevel.StorageType == StorageType.ElementId && startLevel.Set(toLevel.Id);
                }
            }

            Parameter levelParam = GetFamilyLevelParameter(element);
            return levelParam != null && levelParam.StorageType == StorageType.ElementId && levelParam.Set(toLevel.Id);
        }

        /// <summary>Raises (or lowers) the element by <paramref name="delta"/> feet through its offset.</summary>
        private static bool TryShiftOffset(Element element, double delta)
        {
            BuiltInParameter[] offsets = element is MEPCurve
                ? new[] { BuiltInParameter.RBS_OFFSET_PARAM }
                : new[] { BuiltInParameter.INSTANCE_ELEVATION_PARAM, BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM };

            foreach (BuiltInParameter id in offsets)
            {
                Parameter offset = element.get_Parameter(id);
                if (offset != null && !offset.IsReadOnly && offset.StorageType == StorageType.Double)
                {
                    try { return offset.Set(offset.AsDouble() + delta); }
                    catch (Exception) { return false; }
                }
            }

            return false;
        }

        /// <summary>The points that say where the element is: curve ends, the insertion point, or its box.</summary>
        private static IList<XYZ> GetPositionPoints(Element element)
        {
            var points = new List<XYZ>();
            try
            {
                if (element.Location is LocationCurve locationCurve && locationCurve.Curve != null)
                {
                    points.Add(locationCurve.Curve.GetEndPoint(0));
                    points.Add(locationCurve.Curve.GetEndPoint(1));
                }
                else if (element.Location is LocationPoint locationPoint && locationPoint.Point != null)
                {
                    points.Add(locationPoint.Point);
                }
                else
                {
                    BoundingBoxXYZ box = element.get_BoundingBox(null);
                    if (box != null)
                    {
                        points.Add(box.Min);
                        points.Add(box.Max);
                    }
                }
            }
            catch (Exception)
            {
                points.Clear();
            }
            return points;
        }

        private static bool SamePlace(IList<XYZ> before, IList<XYZ> after)
        {
            if (before.Count != after.Count)
                return false;
            for (int i = 0; i < before.Count; i++)
            {
                if (before[i].DistanceTo(after[i]) > PositionTolerance)
                    return false;
            }
            return true;
        }

        /// <summary>The average rise, or NaN when the element also moved sideways (an offset cannot fix that).</summary>
        private static double HeightChange(IList<XYZ> before, IList<XYZ> after)
        {
            if (before.Count != after.Count || before.Count == 0)
                return double.NaN;

            double rise = after[0].Z - before[0].Z;
            for (int i = 0; i < before.Count; i++)
            {
                double dx = after[i].X - before[i].X;
                double dy = after[i].Y - before[i].Y;
                double dz = after[i].Z - before[i].Z;
                if (Math.Abs(dx) > PositionTolerance || Math.Abs(dy) > PositionTolerance ||
                    Math.Abs(dz - rise) > PositionTolerance)
                    return double.NaN;
            }
            return rise;
        }
    }
}
