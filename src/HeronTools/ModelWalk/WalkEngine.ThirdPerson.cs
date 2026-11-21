// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk engine - third-person view: the camera behind and right of the player, sliding in when a wall is behind,
// and the figure's pose for this frame (where it stands, which way it faces, how far its arms and legs swing). The
// figure itself is drawn by WalkFigureServer. Ported from AJ-Tools Game Mode v1.60.0 (GameMotionEngine.ThirdPerson.cs).
//
// What stays with the PLAYER (not the camera): collision, gravity, step-up, the door hint, the floor and room checks,
// viewpoints and the snag's "where you stood". What starts at the CAMERA: the crosshair ray (readout, Select, Snag,
// Hide, Measure, Go to point), so every click lands on what is under the crosshair. Distances shown are measured from
// the player. The camera's wall slide costs one ray, only when the camera moved, inside the frame's ray budget.

using System;
using Autodesk.Revit.DB;

namespace HeronTools.Services.ModelWalk
{
    internal sealed partial class WalkEngine
    {
        private const double WalkSwingRad = 0.45;   // about 26 degrees
        private const double RunSwingRad = 0.75;    // about 43 degrees
        private const double SwingEasePerSec = 8.0;
        private const double SwingAtRestRad = 0.01; // below this the limbs are at rest: no more redraws for them

        // Third person: this frame's camera eye (aim rays start here; null in first person), the figure's last feet
        // (the distance walked drives the swing), the walk cycle and its size, what was drawn last, and the wall-slide
        // ray's last answer (cast again only when the camera moved).
        private XYZ _thirdEye;
        private XYZ _figureLastFeet;
        private double _walkPhaseRad;
        private double _swingRad;
        private bool _figureShown;
        private XYZ _slidePivot;
        private XYZ _slideDirection;
        private double _slideAllowedFt = -1;

        /// <summary>Where the crosshair ray starts: the camera in third person, the eye in first person.</summary>
        private XYZ AimFrom(XYZ position) => _thirdEye ?? position;

        /// <summary>Distance from the player to a crosshair hit (the ray itself may start at the camera, 3 m behind).</summary>
        private double PlayerDistanceFt(WalkRayHit hit, XYZ position) =>
            _thirdEye != null ? hit.Point.DistanceTo(position) : hit.DistanceFt;

        /// <summary>
        /// The camera eye for this frame. First person: the player's eye, figure hidden. Third person: behind and right
        /// of the player's head, pulled in front of any wall in between, and the figure's pose for the next redraw.
        /// <paramref name="redraw"/> is true when the figure changed with no camera move (shown, hidden, limbs easing).
        /// </summary>
        private XYZ UpdateThirdPerson(Document doc, View3D view, WalkInput input, WalkHudState hud, XYZ position,
                                      XYZ forward, XYZ right, double dt, out bool redraw, out bool animating)
        {
            redraw = false;
            animating = false;
            WalkFigurePose pose = WalkFigureServer.Pose;

            if (input.ThirdPerson && !WalkFigureServer.InUse && !WalkFigureServer.SwitchOn())
            {
                input.ThirdPerson = false;
                hud.Toast("Third-person view is not available: Revit did not let Model Walk draw the figure. First person.",
                          WalkToastKind.Refused);
            }

            if (!input.ThirdPerson)
            {
                if (_figureShown)
                    redraw = true;
                pose.Visible = false;
                _figureShown = false;
                _thirdEye = null;
                _figureLastFeet = null;
                _swingRad = 0;
                _slideAllowedFt = -1;
                return position;
            }

            // --- The figure: feet on the floor under the eye, facing the look direction ---
            var feet = new XYZ(position.X, position.Y, position.Z - _session.EyeHeightFt);
            double walkedFt = 0.0;
            if (_figureLastFeet != null)
            {
                double dx = feet.X - _figureLastFeet.X;
                double dy = feet.Y - _figureLastFeet.Y;
                walkedFt = Math.Sqrt((dx * dx) + (dy * dy));
                if (walkedFt > WalkTuning.FigureStrideFt * 2.0)
                    walkedFt = 0.0;   // a jump (viewpoint, go to point, door, back to start) is not a step
            }
            _figureLastFeet = feet;

            double targetSwing = walkedFt / dt > WalkTuning.MmToFt(200.0) ? (input.Run ? RunSwingRad : WalkSwingRad) : 0.0;
            double before = _swingRad;
            _swingRad += (targetSwing - _swingRad) * Math.Min(1.0, dt * SwingEasePerSec);
            if (targetSwing == 0.0 && _swingRad < SwingAtRestRad)
                _swingRad = 0.0;
            _walkPhaseRad = (_walkPhaseRad + (walkedFt / WalkTuning.FigureStrideFt * Math.PI)) % (Math.PI * 2.0);

            double cos = Math.Cos(_walkPhaseRad);
            pose.Document = doc;
            pose.ViewId = view.Id;
            pose.Feet = feet;
            pose.YawRad = input.YawDeg * Math.PI / 180.0;
            pose.SwingRad = _swingRad * Math.Sin(_walkPhaseRad);
            // Each knee bends while its leg swings through under the body, straight while it carries.
            pose.KneeLeftRad = _swingRad * 1.2 * (0.5 + (0.5 * cos));
            pose.KneeRightRad = _swingRad * 1.2 * (0.5 - (0.5 * cos));

            // Snag pictures are of the model, not of the figure: hidden while one waits or is being taken.
            bool show = hud.SnagAwaitingPicture <= 0 && !hud.SnagPictureBusy;
            pose.Visible = show;
            if (show != _figureShown || Math.Abs(before - _swingRad) > 1e-6)
                redraw = true;
            _figureShown = show;
            animating = _swingRad > 0.0;

            // --- The camera: behind and right of the head, never through a wall ---
            var pivot = new XYZ(feet.X, feet.Y, feet.Z + WalkTuning.ThirdPersonPivotFt);
            XYZ offset = (-forward * WalkTuning.ThirdPersonBackFt) + (right * WalkTuning.ThirdPersonShoulderFt);
            double length = offset.GetLength();
            if (length < 1e-6)
            {
                _thirdEye = pivot;
                return pivot;
            }
            XYZ direction = offset / length;

            // One ray, and only when the camera moved; past the frame's ray budget the last answer is kept (or the
            // closest camera, if there is none yet) - never a guess that could put the camera inside a wall.
            bool moved = _slidePivot == null || _slideAllowedFt < 0 || !_slidePivot.IsAlmostEqualTo(pivot) ||
                         !_slideDirection.IsAlmostEqualTo(direction);
            if (moved && !_collision.OverBudget)
            {
                double allowed = length;
                if (_session.Mode != WalkMoveMode.PassThrough)
                {
                    WalkRayHit blocker = _collision.CastNear(pivot, direction, length);
                    if (blocker != null)
                    {
                        // A gap before the surface; with no room for the gap (a duct just above the head, looking
                        // down) half way to it - always in front of the surface, never 300 mm past it.
                        double room = blocker.DistanceFt - WalkTuning.ThirdPersonWallGapFt;
                        allowed = room >= WalkTuning.ThirdPersonMinBackFt
                            ? Math.Min(length, room)
                            : Math.Min(WalkTuning.ThirdPersonMinBackFt, blocker.DistanceFt * 0.5);
                    }
                }
                _slidePivot = pivot;
                _slideDirection = direction;
                _slideAllowedFt = allowed;
            }
            else if (moved && _slideAllowedFt < 0)
            {
                _slideAllowedFt = WalkTuning.ThirdPersonMinBackFt;
            }

            _thirdEye = pivot + (direction * Math.Min(length, _slideAllowedFt));
            return _thirdEye;
        }
    }
}
