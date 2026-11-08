// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Matches center, top, or bottom elevation from one picked source MEP element to one or more picked target elements, moving each target vertically only (X/Y unchanged).

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    public enum ElevationMatchMode
    {
        Center,
        Top,
        Bottom
    }

    /// <summary>
    /// Matches center elevation from a source MEP element to selected targets.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdMatchElevation : MatchElevationCommandBase
    {
        protected override ElevationMatchMode MatchMode => ElevationMatchMode.Center;
        protected override string ModeLabel => "Center";
    }

    /// <summary>
    /// Matches top elevation from a source MEP element to selected targets.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdMatchElevationTop : MatchElevationCommandBase
    {
        protected override ElevationMatchMode MatchMode => ElevationMatchMode.Top;
        protected override string ModeLabel => "Top";
    }

    /// <summary>
    /// Matches bottom elevation from a source MEP element to selected targets.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdMatchElevationBottom : MatchElevationCommandBase
    {
        protected override ElevationMatchMode MatchMode => ElevationMatchMode.Bottom;
        protected override string ModeLabel => "Bottom";
    }

    /// <summary>
    /// Shared implementation for elevation matching command variants.
    /// </summary>
    public abstract class MatchElevationCommandBase : IExternalCommand
    {
        private const double ElevationTolerance = 1e-9;

        protected abstract ElevationMatchMode MatchMode { get; }
        protected abstract string ModeLabel { get; }

        /// <summary>
        /// Executes the match elevation workflow.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            Document doc = uidoc.Document;
            var filter = new MepSelectionFilter();
            string title = $"Match Elevation ({ModeLabel})";
            string modeLower = ModeLabel.ToLowerInvariant();

            try
            {
                // 1) Pick SOURCE
                Reference sourceRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    filter,
                    $"Select SOURCE element to copy {modeLower} elevation from");

                Element sourceElem = doc.GetElement(sourceRef);
                double? sourceReferenceElevation = GetReferenceElevation(sourceElem, MatchMode);
                if (sourceReferenceElevation == null)
                {
                    DialogHelper.ShowError(title, "Could not read elevation from the selected element.");
                    return Result.Cancelled;
                }

                // 2) Pick TARGETS in a loop until user cancels. The whole session is grouped so a
                //    single Ctrl+Z reverses every matched target.
                int updatedCount = 0;
                int refusedCount = 0;
                int alreadyCount = 0;
                using (TransactionGroup group = new TransactionGroup(doc, title))
                {
                    group.Start();

                    while (true)
                    {
                        Reference targetRef;
                        try
                        {
                            targetRef = uidoc.Selection.PickObject(
                                ObjectType.Element,
                                filter,
                                $"Select TARGET element to match {modeLower} elevation (ESC to finish)");
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break; // user pressed ESC to finish applying
                        }

                        Element targetElem = doc.GetElement(targetRef);
                        if (targetElem == null)
                        {
                            continue;
                        }

                        using (Transaction t = new Transaction(doc, title))
                        {
                            try
                            {
                                t.Start();

                                bool changed = SetTargetReferenceElevation(
                                    targetElem,
                                    sourceReferenceElevation.Value,
                                    MatchMode);
                                if (changed)
                                {
                                    // Revit can refuse at commit (a colleague owns it): then it did not move.
                                    if (t.Commit() == TransactionStatus.Committed)
                                        updatedCount++;
                                    else
                                        refusedCount++;
                                }
                                else
                                {
                                    t.RollBack();
                                    alreadyCount++;
                                }
                            }
                            catch (Exception)
                            {
                                if (t.HasStarted() && !t.HasEnded())
                                {
                                    t.RollBack();
                                }
                                // Skip a target that cannot be moved; keep matching the rest, and say so at the end.
                                refusedCount++;
                            }
                        }
                    }

                    if (updatedCount > 0)
                    {
                        group.Assimilate();
                    }
                    else
                    {
                        group.RollBack();
                    }
                }

                // House rule: picks that moved need no popup - they moved on screen. Speak only for picks
                // Revit refused, or when every pick was already at that elevation. Esc with no pick is quiet.
                if (refusedCount > 0)
                {
                    DialogHelper.ShowInfo(
                        title,
                        updatedCount > 0
                            ? $"Updated {updatedCount} element(s). {refusedCount} picked element(s) could not be moved (pinned, or Revit refused the change) and were left as they were."
                            : $"None of the {refusedCount} picked element(s) could be moved (pinned, or Revit refused the change).");
                }
                else if (updatedCount == 0 && alreadyCount > 0)
                {
                    DialogHelper.ShowInfo(title, $"Nothing to move - the picked element(s) were already at that {modeLower} elevation.");
                }

                return updatedCount > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static double? GetReferenceElevation(Element elem, ElevationMatchMode mode)
        {
            double? centerElevation = GetCenterElevation(elem);
            if (centerElevation == null)
            {
                return null;
            }

            double halfVerticalSize = GetHalfVerticalSize(elem);
            switch (mode)
            {
                case ElevationMatchMode.Top:
                    return centerElevation.Value + halfVerticalSize;
                case ElevationMatchMode.Bottom:
                    return centerElevation.Value - halfVerticalSize;
                default:
                    return centerElevation.Value;
            }
        }

        /// <summary>
        /// Returns the center elevation (Z at mid-point) of the element's location curve.
        /// </summary>
        private static double? GetCenterElevation(Element elem)
        {
            LocationCurve loc = elem?.Location as LocationCurve;
            if (loc == null)
            {
                return null;
            }

            Curve curve = loc.Curve;
            if (curve == null)
            {
                return null;
            }

            XYZ p0 = curve.GetEndPoint(0);
            XYZ p1 = curve.GetEndPoint(1);

            return (p0.Z + p1.Z) / 2.0;
        }

        /// <summary>
        /// Moves the element vertically so that the requested reference elevation matches targetReferenceElevation.
        /// </summary>
        private static bool SetTargetReferenceElevation(Element elem, double targetReferenceElevation, ElevationMatchMode mode)
        {
            LocationCurve loc = elem?.Location as LocationCurve;
            if (loc == null)
            {
                return false;
            }

            Curve curve = loc.Curve;
            if (curve == null)
            {
                return false;
            }

            XYZ p0 = curve.GetEndPoint(0);
            XYZ p1 = curve.GetEndPoint(1);
            double currentCenter = (p0.Z + p1.Z) / 2.0;

            double targetCenter = targetReferenceElevation;
            double halfVerticalSize = GetHalfVerticalSize(elem);
            switch (mode)
            {
                case ElevationMatchMode.Top:
                    targetCenter = targetReferenceElevation - halfVerticalSize;
                    break;
                case ElevationMatchMode.Bottom:
                    targetCenter = targetReferenceElevation + halfVerticalSize;
                    break;
            }

            double diff = targetCenter - currentCenter;
            if (Math.Abs(diff) < ElevationTolerance)
            {
                return false; // already matched
            }

            // Translate the whole curve in Z direction, preserving curve type (Line, Arc, NURBS, etc.)
            Transform translation = Transform.CreateTranslation(new XYZ(0, 0, diff));
            Curve movedCurve = curve.CreateTransformed(translation);

            loc.Curve = movedCurve;
            return true;
        }

        /// <summary>
        /// Returns half of the element's profile size in internal units (feet) for top/bottom matching.
        /// Falls back to 0 when a profile parameter is unavailable.
        /// </summary>
        private static double GetHalfVerticalSize(Element elem)
        {
            if (TryGetPositiveDouble(elem, BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM, out double cableTrayHeight))
            {
                return cableTrayHeight / 2.0;
            }

            if (TryGetPositiveDouble(elem, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM, out double curveHeight))
            {
                return curveHeight / 2.0;
            }

            // A pipe's real size is its Outside Diameter - the one Revit's own Top/Bottom Elevation uses.
            // "Diameter" is the nominal size (DN100 steel is 114.3 mm outside), kept only as a fallback.
            if (TryGetPositiveDouble(elem, BuiltInParameter.RBS_PIPE_OUTER_DIAMETER, out double pipeOuterDiameter))
            {
                return pipeOuterDiameter / 2.0;
            }

            if (TryGetPositiveDouble(elem, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, out double pipeDiameter))
            {
                return pipeDiameter / 2.0;
            }

            if (TryGetPositiveDouble(elem, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM, out double curveDiameter))
            {
                return curveDiameter / 2.0;
            }

            // Conduit reports its size through its own parameters - the ones above are the cable tray,
            // duct and pipe ones - so it used to fall through to 0 and a Top/Bottom match on a conduit
            // landed half its diameter off. Its physical size is the "Outside Diameter"; the
            // "Diameter(Trade Size)" value is only the fallback (same order Create Openings reads).
            if (TryGetPositiveDouble(elem, BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM, out double conduitOuterDiameter))
            {
                return conduitOuterDiameter / 2.0;
            }

            if (TryGetPositiveDouble(elem, BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM, out double conduitTradeDiameter))
            {
                return conduitTradeDiameter / 2.0;
            }

            return 0.0;
        }

        private static bool TryGetPositiveDouble(Element elem, BuiltInParameter parameterId, out double value)
        {
            value = 0;
            Parameter parameter = elem?.get_Parameter(parameterId);
            if (parameter == null ||
                parameter.StorageType != StorageType.Double ||
                !parameter.HasValue)
            {
                return false;
            }

            double rawValue = parameter.AsDouble();
            if (rawValue <= ElevationTolerance)
            {
                return false;
            }

            value = rawValue;
            return true;
        }
    }
}
