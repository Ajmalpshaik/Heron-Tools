// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk engine - third-person view: the camera behind and right of the player, sliding in when a wall is behind,
// and the character's place for this frame (where it stands, which way it faces, how fast it moves - which picks its
// stand / walk / run animation). The character itself is drawn by WalkCharacterServer. Camera ported from AJ-Tools Game
// Mode v1.60.0 (GameMotionEngine.ThirdPerson.cs); the speed hand-over to the animation from v1.61.0 (commit ab98e2e).
//
// What stays with the PLAYER (not the camera): collision, gravity, step-up, the door hint, the floor and room checks,
// viewpoints and the snag's "where you stood". What starts at the CAMERA: the crosshair ray (readout, Select, Snag,
// Hide, Measure, Go to point), so every click lands on what is under the crosshair. Distances shown are measured from
// the player. The camera's wall slide costs one cast (up to 2 rays: the model and near links), only when the camera
// moved, and only when both rays fit in the frame's ray budget.

using System;
using Autodesk.Revit.DB;

namespace HeronTools.Services.ModelWalk
{
    internal sealed partial class WalkEngine
    {
        private const double SpeedEaseSec = 0.12;   // the speed handed to the animation is smoothed over about this long

        // Third person: this frame's camera eye (aim rays start here; null in first person), the character's last feet
        // (the distance walked gives its speed), that speed smoothed, what was drawn last, whether "no character" was
        // already said this walk, and the wall-slide ray's last answer (cast again only when the camera moved).
        private XYZ _thirdEye;
        private XYZ _thirdForward;   // the look sent with _thirdEye - together they are the picture on screen
        private XYZ _characterLastFeet;
        private double _characterSpeedFtPerSec;
        private bool _characterShown;
        private bool _noCharacterToldThisWalk;
        private XYZ _slidePivot;
        private XYZ _slideDirection;
        private double _slideAllowedFt = -1;

        /// <summary>Where the crosshair ray starts: the camera in third person, the eye in first person.</summary>
        private XYZ AimFrom(XYZ position) => _thirdEye ?? position;

        /// <summary>
        /// Which way the crosshair ray goes. Third person: the look sent with the camera in <see cref="AimFrom"/>, so a
        /// click (handled before this frame's camera move) aims through the crosshair of the picture on screen - the
        /// previous camera with this frame's newer look missed by a few cm when turning fast. First person: as before.
        /// </summary>
        private XYZ AimAlong(XYZ forward) => _thirdEye != null && _thirdForward != null ? _thirdForward : forward;

        /// <summary>Distance from the player to a crosshair hit (the ray itself may start at the camera, 3 m behind).</summary>
        private double PlayerDistanceFt(WalkRayHit hit, XYZ position) =>
            _thirdEye != null ? hit.Point.DistanceTo(position) : hit.DistanceFt;

        /// <summary>
        /// The camera eye for this frame. First person: the player's eye, character hidden. Third person: behind and right
        /// of the player's head, pulled in front of any wall in between, and the character's place for the next redraw.
        /// <paramref name="redraw"/> is true when the character changed with no camera move (shown, hidden, walking or
        /// blending on the spot); <paramref name="animating"/> while its animation still changes by itself.
        /// </summary>
        private XYZ UpdateThirdPerson(Document doc, View3D view, WalkInput input, WalkHudState hud, XYZ position,
                                      XYZ forward, XYZ right, double dt, out bool redraw, out bool animating)
        {
            redraw = false;
            animating = false;
            WalkCharacterPose pose = WalkCharacterServer.Pose;

            // The character is read the first time third person is used (once per Revit session). Without it third
            // person still works - the camera behind you, nothing drawn - and the walk says so once.
            bool hasCharacter = input.ThirdPerson && WalkCharacterServer.EnsureCharacter();
            if (input.ThirdPerson && !hasCharacter && !_noCharacterToldThisWalk)
            {
                _noCharacterToldThisWalk = true;
                hud.Toast("Third person without the character: " + (WalkCharacterServer.CharacterProblem ?? "it could not be read") +
                          ". The camera is behind you; nothing else changes.", WalkToastKind.Refused);
            }
            if (hasCharacter && !WalkCharacterServer.InUse && !WalkCharacterServer.SwitchOn())
            {
                input.ThirdPerson = false;
                hud.Toast("Third-person view is not available: Revit did not let Model Walk draw the character. First person.",
                          WalkToastKind.Refused);
            }

            if (!input.ThirdPerson)
            {
                if (_characterShown)
                    redraw = true;
                pose.Visible = false;
                _characterShown = false;
                _thirdEye = null;
                _thirdForward = null;
                _characterLastFeet = null;
                _characterSpeedFtPerSec = 0;
                _slideAllowedFt = -1;
                return position;
            }

            // --- The character: feet on the floor under the eye, facing the look direction ---
            var feet = new XYZ(position.X, position.Y, position.Z - _session.EyeHeightFt);
            double walkedFt = 0.0;
            if (_characterLastFeet != null)
            {
                double dx = feet.X - _characterLastFeet.X;
                double dy = feet.Y - _characterLastFeet.Y;
                walkedFt = Math.Sqrt((dx * dx) + (dy * dy));
                if (walkedFt > WalkTuning.CharacterJumpFt)
                    walkedFt = 0.0;   // a jump (viewpoint, go to point, door, back to start) is not a step
            }
            _characterLastFeet = feet;

            // Horizontal speed only (going up stairs or flying straight up is not walking), smoothed so one late frame
            // does not flick the legs between stand and walk.
            double speedNow = dt > 1e-6 ? walkedFt / dt : 0.0;
            _characterSpeedFtPerSec += (speedNow - _characterSpeedFtPerSec) * Math.Min(1.0, dt / SpeedEaseSec);
            if (speedNow == 0.0 && _characterSpeedFtPerSec < WalkTuning.MmToFt(20.0))
                _characterSpeedFtPerSec = 0.0;
            bool moving = WalkCharacterServer.Advance(dt, _characterSpeedFtPerSec, input.Run);

            pose.Document = doc;
            pose.ViewId = view.Id;
            pose.Feet = feet;
            pose.YawRad = input.YawDeg * Math.PI / 180.0;

            // Snag pictures are of the model, not of the character: hidden while one waits or is being taken.
            bool show = hasCharacter && WalkCharacterServer.HasCharacter && hud.SnagAwaitingPicture <= 0 && !hud.SnagPictureBusy;
            pose.Visible = show;
            if (show != _characterShown || (show && moving))
                redraw = true;
            _characterShown = show;
            animating = show && moving;

            // --- The camera: behind and right of the head, never through a wall ---
            var pivot = new XYZ(feet.X, feet.Y, feet.Z + WalkTuning.ThirdPersonPivotFt);
            XYZ offset = (-forward * WalkTuning.ThirdPersonBackFt) + (right * WalkTuning.ThirdPersonShoulderFt);
            double length = offset.GetLength();
            if (length < 1e-6)
            {
                _thirdEye = pivot;
                _thirdForward = forward;
                return pivot;
            }
            XYZ direction = offset / length;

            // One ray, and only when the camera moved; past the frame's ray budget the last answer is kept (or the
            // closest camera, if there is none yet) - never a guess that could put the camera inside a wall.
            bool moved = _slidePivot == null || _slideAllowedFt < 0 || !_slidePivot.IsAlmostEqualTo(pivot) ||
                         !_slideDirection.IsAlmostEqualTo(direction);
            // CastNear is up to 2 rays (the model and the near links): cast only when both fit in the frame's budget.
            if (moved && _collision.RaysThisFrame <= WalkTuning.RayBudgetPerFrame - 2)
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
            _thirdForward = forward;
            return _thirdEye;
        }
    }
}
