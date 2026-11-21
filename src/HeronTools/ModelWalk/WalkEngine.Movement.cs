// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - moving with collision, gravity, stairs and the landing checks (door, go to point).
// The body is a ring of short rays at four heights, from the eye down to just above step height, plus two
// side rays at the body's edge - so a column corner or a door frame beside the path stops you, and a pipe or a
// table edge at chest height blocks you instead of popping you on top of it.

using System;
using Autodesk.Revit.DB;

namespace HeronTools.Services.ModelWalk
{
    internal sealed partial class WalkEngine
    {
        /// <summary>Try the move; when blocked, slide along the free axes instead of stopping dead.</summary>
        private XYZ MoveWithCollision(XYZ position, XYZ move)
        {
            if (_session.Mode == WalkMoveMode.PassThrough)
                return position + move;

            if (IsFree(position, move, withSides: true))
                return position + move;

            // Walk along the wall, not into it.
            XYZ result = position;
            var x = new XYZ(move.X, 0, 0);
            var y = new XYZ(0, move.Y, 0);
            var z = new XYZ(0, 0, move.Z);
            if (Math.Abs(move.X) > 1e-9 && IsFree(result, x, withSides: false)) result += x;
            if (Math.Abs(move.Y) > 1e-9 && IsFree(result, y, withSides: false)) result += y;
            if (Math.Abs(move.Z) > 1e-9 && IsFree(result, z, withSides: false)) result += z;
            return result;
        }

        /// <summary>
        /// Is this step free? Rays from the eye, at a quarter and half of the eye height down, and just above step
        /// height; plus two side rays at the body's edge. A frame past its ray budget says "not free" - never a guess.
        /// </summary>
        private bool IsFree(XYZ eye, XYZ move, bool withSides)
        {
            double length = move.GetLength();
            if (length < 1e-9 || _collision.OverBudget)
                return false;

            XYZ direction = move.Normalize();
            double reach = length + WalkTuning.BodyRadiusFt;
            double eyeFt = _session.EyeHeightFt;
            // Shin ray just ABOVE the step height (the old 300 mm ray blocked 300-350 mm stairs it should let you walk).
            double shin = Math.Max(0.0, eyeFt - WalkTuning.StepUpFt - WalkTuning.MmToFt(50));
            double[] drops = { 0.0, eyeFt * 0.25, eyeFt * 0.5, shin };
            foreach (double drop in drops)
            {
                if (_collision.CastNear(new XYZ(eye.X, eye.Y, eye.Z - drop), direction, reach) != null || _collision.OverBudget)
                    return false;
            }

            if (withSides)
            {
                var flat = new XYZ(direction.X, direction.Y, 0);
                if (flat.GetLength() > 1e-6)
                {
                    XYZ side = new XYZ(-flat.Y, flat.X, 0).Normalize() * WalkTuning.BodyRadiusFt;
                    var chest = new XYZ(eye.X, eye.Y, eye.Z - (eyeFt * 0.5));
                    if (_collision.CastNear(chest + side, direction, reach) != null ||
                        _collision.CastNear(chest - side, direction, reach) != null || _collision.OverBudget)
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Gravity, stairs and landing in Walk. <paramref name="before"/> is where the eye was before this frame's
        /// step: a step that would lift you more than a stair (onto a pipe, a table, a sill) is refused and you stay there.
        /// </summary>
        private XYZ ApplyGravity(XYZ before, XYZ position, double dt)
        {
            double eye = _session.EyeHeightFt;
            bool moved = !position.IsAlmostEqualTo(before);

            if (_session.Grounded && !moved && !_forceFloorCheck)
                return position;   // standing still on a floor: no ray needed
            _forceFloorCheck = false;

            if (_collision.OverBudget)
                return before;     // cannot check the floor this frame: do not step

            WalkRayHit ground = _collision.CastNear(position, -XYZ.BasisZ, WalkTuning.FloorSearchFt)
                                ?? (_collision.OverBudget ? null : _collision.CastFull(position, -XYZ.BasisZ, WalkTuning.FloorSearchFt));

            if (ground == null)
            {
                // Nothing to stand on anywhere below (an open shaft, or a model with no floors - an MEP-only file).
                // Hover where you are instead of falling for ever.
                _hovering = true;
                _session.Grounded = true;
                _session.VerticalSpeedFtPerSec = 0;
                return position;
            }

            _hovering = false;
            if (_session.Grounded)
            {
                double gap = ground.DistanceFt - eye;
                if (Math.Abs(gap) <= WalkTuning.StepUpFt)
                    return new XYZ(position.X, position.Y, position.Z - gap);   // stairs, ramps, thresholds
                if (gap > WalkTuning.StepUpFt)
                {
                    _session.Grounded = false;                                 // walked off an edge: start dropping
                    _session.VerticalSpeedFtPerSec = 0;
                    return position;
                }
                // Something higher than a stair is under the eye (a pipe, a table, a sill): do not climb it.
                return moved ? before : new XYZ(position.X, position.Y, position.Z - gap);
            }

            // Dropping: integrate gravity, fall speed capped.
            _session.VerticalSpeedFtPerSec -= WalkTuning.GravityFtPerSec2 * dt;
            if (_session.VerticalSpeedFtPerSec < -WalkTuning.MaxFallFtPerSec)
                _session.VerticalSpeedFtPerSec = -WalkTuning.MaxFallFtPerSec;
            double fall = Math.Abs(_session.VerticalSpeedFtPerSec * dt);

            // Landing check from where the eye was BEFORE this drop, over the whole drop plus the eye height: a fast
            // fall (15 m/s with a slow 0.25 s frame = 3.75 m) would otherwise pass a slab in one step. (Kept from the
            // old tool's 2026-10-12 fix.)
            if (ground.DistanceFt <= fall + eye)
            {
                _session.Grounded = true;
                _session.VerticalSpeedFtPerSec = 0;
                return new XYZ(position.X, position.Y, position.Z - ground.DistanceFt + eye);
            }

            var dropped = new XYZ(position.X, position.Y, position.Z - fall);
            if (dropped.Z < _session.StartPosition.Z - WalkTuning.FloorSearchFt)
            {
                // Safety net: dropped absurdly far - back to the start.
                _session.Grounded = false;
                _session.VerticalSpeedFtPerSec = 0;
                return _session.StartPosition;
            }
            return dropped;
        }

        /// <summary>
        /// Is there room to stand here? In Walk: a floor below (the eye is put at eye height above it) with head room
        /// over it. Then four short horizontal rays at chest height. Returns null when there is room, or a plain reason;
        /// <paramref name="standingEye"/> is where the eye ends up.
        /// </summary>
        private string LandingProblem(XYZ eye, out XYZ standingEye)
        {
            standingEye = eye;
            double eyeFt = _session.EyeHeightFt;

            if (_session.Mode == WalkMoveMode.Walk)
            {
                WalkRayHit floor = _collision.CastFull(eye, -XYZ.BasisZ, WalkTuning.FloorSearchFt);
                if (floor == null)
                    return "there is no floor there";
                XYZ feet = eye - (XYZ.BasisZ * floor.DistanceFt);
                // Head room: a ceiling void or a gap under a duct is not a place to stand.
                WalkRayHit head = _collision.CastFull(feet + (XYZ.BasisZ * WalkTuning.MmToFt(50)), XYZ.BasisZ, eyeFt + WalkTuning.MmToFt(100));
                if (head != null)
                    return "there is not enough head room there";
                standingEye = feet + (XYZ.BasisZ * eyeFt);
            }

            var chest = new XYZ(standingEye.X, standingEye.Y, standingEye.Z - (eyeFt * 0.5));
            foreach (XYZ dir in new[] { XYZ.BasisX, -XYZ.BasisX, XYZ.BasisY, -XYZ.BasisY })
            {
                if (_collision.CastFull(chest, dir, WalkTuning.BodyRadiusFt) != null)
                    return "something is in the way there";
            }
            return null;
        }
    }
}
