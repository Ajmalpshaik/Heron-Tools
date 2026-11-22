// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - the frame engine. The ONLY place Model Walk calls Revit's API while the walk runs: the HUD's timer
// raises this ExternalEvent and never touches the model itself. Each frame moves the eye (with collision), aims the
// camera with View3D.SetOrientation - a temporary camera, no transaction, no Undo entry - and fills the HUD.
//
// Every frame is guarded: any exception ends the walk cleanly with a plain reason. The walk also ends by itself
// when the model closes, another model or view becomes active, or the walk view is deleted or its window closed.
// However it ends, the clean-up runs once: snag colours given back (one Undo step), what this walk hid shown
// again, the snag report written, and the modeller taken back to the view they started from.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Utils;

namespace HeronTools.Services.ModelWalk
{
    internal sealed partial class WalkEngine : IExternalEventHandler
    {
        private readonly WalkSession _session;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastFrameSeconds = -1.0;
        private double _lastWorldRefresh;
        private double _lastInspect = -10;
        private double _lastClearHeight = -10;
        private WalkCollision _collision;
        private bool _ended;

        // The previous frame stood still (no move, no fall): the HUD then raises only four frames a second, so the
        // time since it can be 250 ms. The first moving frame after that must not use the whole gap, or the eased
        // move takes almost full speed for a quarter second at once - a jump of about 0.8 m on the first key press.
        private bool _wasStill = true;

        // The camera last sent to Revit: no SetOrientation / redraw while nothing changed.
        private XYZ _lastEye;

        // The eased move: speed and direction now, in feet per second (see the Move block in Frame).
        private XYZ _velocity = XYZ.Zero;
        private double _lastYaw = double.NaN;
        private double _lastPitch = double.NaN;

        // Aim and picture in step: Revit can 2D-zoom or pan a perspective view like a photo, which moves the picture
        // off the camera axis - clicks then land beside the crosshair. ZoomToFit re-centres it; on start, on resume
        // and on every change of the view's window. (Kept from the old tool: do not remove.)
        private bool _viewSyncDone;
        private int _prevLeft, _prevTop, _prevRight, _prevBottom;

        // Floor must be checked again after the world changed (hide, VG) even when standing still.
        private bool _forceFloorCheck = true;
        private bool _hovering;

        // Levels for the location line, by project elevation (internal Z, whatever the level type's Elevation Base).
        private List<KeyValuePair<double, string>> _levels;

        // The walk view's snag colour memory, found on Revit's thread, so EndWithoutRevit (called from the overlay,
        // outside Revit's API) can tell whether colours are still on without calling Revit's API.
        private SnagColourMemory _snagMemory;

        // A walk that ended in third person WITHOUT Revit's thread (Revit busy, an overlay fault) could not put the walk
        // view's camera back on the player: what is needed to do it when Model Walk starts again. Plain .NET only.
        private static Document _leftDoc;
        private static ElementId _leftViewId;
        private static XYZ _leftCameraEye;
        private static XYZ _leftPlayerEye;
        private static double _leftYawDeg;
        private static double _leftPitchDeg;

        /// <summary>
        /// At the start of a walk (inside the command): when the last walk ended in third person without Revit's thread
        /// and the walk view's camera is still exactly where it left it, gives the player's eye and look instead - the
        /// next walk starts where the player stood, not 3 m behind. A camera the modeller moved since is kept. Used once.
        /// </summary>
        internal static bool TakeLeftThirdPersonCamera(Document doc, ElementId viewId, XYZ cameraEye, out XYZ eye, out double yawDeg, out double pitchDeg)
        {
            eye = _leftPlayerEye;
            yawDeg = _leftYawDeg;
            pitchDeg = _leftPitchDeg;
            bool sameModel = _leftDoc != null && _leftDoc.IsValidObject && doc != null && doc.Equals(_leftDoc);
            if (!sameModel && _leftDoc != null && _leftDoc.IsValidObject)
                return false;   // a walk in another model: kept for the next walk in that one
            bool match = sameModel && viewId != null && viewId == _leftViewId && cameraEye != null && _leftPlayerEye != null &&
                         cameraEye.DistanceTo(_leftCameraEye) < WalkTuning.MmToFt(5.0);
            _leftDoc = null;
            _leftViewId = null;
            _leftCameraEye = null;
            _leftPlayerEye = null;
            return match;
        }

        public WalkEngine(WalkSession session)
        {
            _session = session;
            _current = this;
        }

        // ------------------------------------------------------------------ the walk's model closing

        private static WalkEngine _current;   // the latest walk (at most one runs at a time)
        private static bool _watchingClose;

        /// <summary>
        /// Once per Revit session, from the command (a valid API context): end the walk the moment its own model starts
        /// to close - Close, closing the last view, or Revit itself shutting down. Frames stop while Revit closes a model
        /// (and on exit they never come again), so without this the end waited for a frame that might not run.
        /// </summary>
        internal static void WatchModelClosing(UIApplication uiApp)
        {
            if (_watchingClose || uiApp == null)
                return;
            _watchingClose = true;
            uiApp.Application.DocumentClosing += (s, e) =>
            {
                WalkEngine engine = _current;
                if (engine == null || engine._ended || e.Document == null || !e.Document.Equals(engine._session.Document))
                    return;
                try
                {
                    // touchModel false: no transaction while Revit is closing the model. A Temporary Hide is not saved
                    // with the model, and the snag colours are given back before any save Revit asks for now.
                    engine.End(null, "The model is closing, so Model Walk ended.", touchModel: false, returnToStart: false);
                }
                catch (Exception)
                {
                    // Broad on purpose: an exception here would reach Revit's close. The overlay still ends on its own.
                }
            };
        }

        public string GetName() => "Heron Tools - Model Walk";

        internal bool Ended => _ended;

        public void Execute(UIApplication app)
        {
            if (_ended)
                return;

            var frameTimer = Stopwatch.StartNew();
            _collision?.BeginFrame();
            try
            {
                UIDocument uidoc = app?.ActiveUIDocument;
                Document doc = _session.Document;

                if (!doc.IsValidObject)
                {
                    End(app, "The model was closed, so Model Walk ended.", touchModel: false, returnToStart: false);
                    return;
                }
                if (_session.StopRequested)
                {
                    End(app, _session.StopRequestReason, touchModel: true, returnToStart: true);
                    return;
                }
                if (_session.Paused)
                {
                    PausedFrame(uidoc, doc);
                    return;
                }
                if (uidoc == null || !uidoc.Document.Equals(doc))
                {
                    End(app, "You switched to another model, so Model Walk ended.", touchModel: true, returnToStart: false);
                    return;
                }
                if (!(doc.GetElement(_session.WalkViewId) is View3D view))
                {
                    End(app, "The walk view was deleted, so Model Walk ended.", touchModel: true, returnToStart: false);
                    return;
                }
                if (uidoc.ActiveView == null || uidoc.ActiveView.Id != _session.WalkViewId)
                {
                    End(app, "You switched to another view, so Model Walk ended.", touchModel: true, returnToStart: false);
                    return;
                }

                Frame(uidoc, doc, view);
            }
            catch (Exception ex)
            {
                // Broad on purpose: an exception escaping an ExternalEvent shows Revit's own crash message, and the
                // walk must always end with the mouse free and the overlay closed.
                End(app, "Model Walk stopped because of an unexpected problem (Revit said: " + ex.Message + ").",
                    touchModel: true, returnToStart: false);
            }
            finally
            {
                frameTimer.Stop();
                WalkHudState hud = _session.Hud;
                hud.LastFrameMs = frameTimer.Elapsed.TotalMilliseconds;
                hud.AverageFrameMs = hud.AverageFrameMs <= 0 ? hud.LastFrameMs : (hud.AverageFrameMs * 0.9) + (hud.LastFrameMs * 0.1);
                hud.LastFrameRays = _collision?.RaysThisFrame ?? 0;
            }
        }

        private void Frame(UIDocument uidoc, Document doc, View3D view)
        {
            if (_collision == null)
            {
                _collision = new WalkCollision(doc, view);
                _lastWorldRefresh = _clock.Elapsed.TotalSeconds;
                _collision.BeginFrame();
                _snagMemory = SnagColourMemory.Find(doc, view);   // colours kept from an earlier walk
            }

            double now = _clock.Elapsed.TotalSeconds;
            double dt = _lastFrameSeconds < 0 ? 0.016 : now - _lastFrameSeconds;
            _lastFrameSeconds = now;
            // Move by real elapsed time, never by frame count - a late frame keeps the right speed. Clamped so one
            // very slow frame cannot throw the eye through a wall.
            if (dt < 0.005) dt = 0.005;
            if (dt > 0.25) dt = 0.25;
            if (_wasStill && dt > WalkTuning.FrameIntervalMs / 1000.0)
                dt = WalkTuning.FrameIntervalMs / 1000.0;   // after standing still: start from one normal frame

            // Pick up Visibility/Graphics, filter, link and level changes made during a pause.
            if (now - _lastWorldRefresh > WalkTuning.WorldRefreshSec)
            {
                RefreshWorld();
                _lastWorldRefresh = now;
            }

            WalkInput input = _session.Input;
            WalkHudState hud = _session.Hud;

            HandleModeToggles(input, hud);
            if (input.SettingsChangedQueued)
            {
                input.SettingsChangedQueued = false;
                input.SpeedStep = _session.Settings.ClampSpeedStep(input.SpeedStep);
                _forceFloorCheck = true;   // a new eye height or step-up: stand on the floor again at the new height
            }

            double pitchDeg = Math.Max(-WalkTuning.PitchClampDeg, Math.Min(WalkTuning.PitchClampDeg, input.PitchDeg));
            Look(input.YawDeg, pitchDeg, out XYZ forward, out XYZ flatForward, out XYZ right, out XYZ up);

            XYZ position = _session.Position;
            XYZ beforeActions = position;

            position = HandleActions(uidoc, doc, view, input, hud, position, forward, flatForward);
            position = HandlePlayViewpoints(input, hud, position, dt);
            bool playing = hud.PlayingViewpoints;
            if (playing || !position.IsAlmostEqualTo(beforeActions))
                StopMotion();   // a jump (viewpoint, go to point, door, open snag) starts from standing

            // --- Move: eased in and out (time-based), so the picture never jolts on a key press or release. The
            //     look stays direct. The same rays per frame as an un-eased move; a blocked part of the move is
            //     dropped from the speed, so easing never pushes into a wall. ---
            XYZ before = position;   // after any jump this frame: a refused step goes back here
            if (!playing)
            {
                double forwardAxis = (input.MoveForward ? 1 : 0) - (input.MoveBack ? 1 : 0);
                double strafeAxis = (input.StrafeRight ? 1 : 0) - (input.StrafeLeft ? 1 : 0);
                double verticalAxis = (input.UpKey ? 1 : 0) - (input.DownKey ? 1 : 0);
                bool flying = _session.Mode != WalkMoveMode.Walk;
                double speed = _session.Settings.WalkSpeedFtPerSec(input.SpeedStep)
                               * (flying ? WalkTuning.FlyFactor : 1.0)
                               * (input.Run ? WalkTuning.RunFactor : 1.0);

                XYZ wish = flying
                    ? (forward * forwardAxis) + (right * strafeAxis) + (XYZ.BasisZ * verticalAxis)
                    : (flatForward * forwardAxis) + (right * strafeAxis);
                XYZ target = wish.GetLength() > 1e-9 ? wish.Normalize() * speed : XYZ.Zero;
                double ease = 1.0 - Math.Exp(-dt / WalkTuning.EaseSeconds);
                _velocity += (target - _velocity) * ease;
                if (!flying)
                    _velocity = new XYZ(_velocity.X, _velocity.Y, 0);   // gravity owns up and down in Walk
                if (target.GetLength() < 1e-9 && _velocity.GetLength() < WalkTuning.MmToFt(WalkTuning.StoppedBelowMmPerSec))
                    _velocity = XYZ.Zero;                               // slow enough: stopped

                XYZ move = _velocity * dt;
                if (move.GetLength() > 1e-9)
                {
                    XYZ moved = MoveWithCollision(position, move);
                    XYZ done = moved - position;
                    if (done.GetLength() < move.GetLength() - 1e-9)
                        _velocity = done / dt;
                    position = moved;
                }
            }

            // --- Door hint: a short ray, only a few times a second ---
            if (_session.Mode != WalkMoveMode.PassThrough && now - _lastInspect >= WalkTuning.InspectRefreshSec)
            {
                WalkRayHit look = _collision.CastNear(position, forward, WalkTuning.DoorReachFt);
                hud.DoorHint = look != null && look.IsDoor;
            }
            else if (_session.Mode == WalkMoveMode.PassThrough)
            {
                hud.DoorHint = false;
            }

            // --- Gravity (Walk only) ---
            if (!playing && _session.Mode == WalkMoveMode.Walk)
            {
                position = ApplyGravity(before, position, dt);
            }
            else
            {
                _session.VerticalSpeedFtPerSec = 0;
                _session.Grounded = false;
            }

            if (input.BackToStartQueued)
            {
                input.BackToStartQueued = false;
                StopMotion();
                position = _session.StartPosition;
                _session.VerticalSpeedFtPerSec = 0;
                _session.Grounded = false;
                _forceFloorCheck = true;
                hud.Toast("Back at the start");
            }

            hud.Hovering = _session.Mode == WalkMoveMode.Walk && _hovering;
            _session.Position = position;

            // --- Third person: the camera behind the player, and the character's place and animation (WalkEngine.ThirdPerson.cs) ---
            XYZ eye = UpdateThirdPerson(doc, view, input, hud, position, forward, right, dt, out bool characterRedraw, out bool characterAnimating);

            // --- Camera: no transaction, no Undo entry. Sent only when it changed. ---
            if (_lastEye == null || !_lastEye.IsAlmostEqualTo(eye) ||
                Math.Abs(_lastYaw - input.YawDeg) > 1e-6 || Math.Abs(_lastPitch - pitchDeg) > 1e-6)
            {
                try
                {
                    view.SetOrientation(new ViewOrientation3D(eye, up, forward));
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    End(uidoc.Application, "This view is locked, so Revit does not let the camera move. Unlock the view " +
                        "(the lock on the view control bar) and start Model Walk again.", touchModel: true, returnToStart: true);
                    return;
                }
                _lastEye = eye;
                _lastYaw = input.YawDeg;
                _lastPitch = pitchDeg;
                uidoc.RefreshActiveView();
            }
            else if (characterRedraw)
            {
                uidoc.RefreshActiveView();   // the character appeared, went, or walks / blends on the spot: no camera move
            }

            // --- Readout under the crosshair: the full view, ten times a second at most ---
            if (now - _lastInspect >= WalkTuning.InspectRefreshSec || input.GoToPointAimHold)
            {
                _lastInspect = now;
                // The crosshair ray starts at the camera (the player's eye in first person); the distance is the player's.
                WalkRayHit aim = _collision.CastFull(AimFrom(position), AimAlong(forward), WalkTuning.AimRangeFt);
                hud.AimHasHit = aim != null;
                hud.AimDistanceMm = aim != null ? WalkTuning.FtToMm(PlayerDistanceFt(aim, position)) : 0;
                hud.AimLine1 = aim != null ? WalkCollision.DescribeLine1(aim) : "";
                hud.AimLine2 = aim != null ? WalkCollision.DescribeLine2(aim) : "";
                hud.GoToPointValid = input.GoToPointAimHold && aim != null;
                hud.GoToPointDistanceMm = hud.AimDistanceMm;
                UpdateLiveMeasure(aim, hud);
            }

            // --- Clear height (floor to whatever is above), four times a second ---
            if (now - _lastClearHeight >= WalkTuning.ClearHeightRefreshSec && !_collision.OverBudget)
            {
                _lastClearHeight = now;
                hud.ClearHeightMm = 0;
                WalkRayHit down = _collision.CastNear(position, -XYZ.BasisZ, WalkTuning.FloorSearchFt);
                WalkRayHit upHit = down != null ? _collision.CastNear(position, XYZ.BasisZ, WalkTuning.NearBoxAboveFt) : null;
                if (down != null && upHit != null)
                    hud.ClearHeightMm = WalkTuning.FtToMm(down.DistanceFt + upHit.DistanceFt);
            }

            UpdateRoom(doc, view, position, now, hud);
            UpdateStatus(doc, hud, input, position);
            UpdateViewRectangle(uidoc, hud, input);

            hud.NeedsFrames = playing || input.GoToPointAimHold || _velocity.GetLength() > 0 || characterAnimating ||
                              (_session.Mode == WalkMoveMode.Walk && !_session.Grounded && !hud.Hovering);
            _wasStill = _velocity.GetLength() <= 0 && !playing &&
                        !(_session.Mode == WalkMoveMode.Walk && !_session.Grounded && !hud.Hovering);
        }

        private static void Look(double yawDeg, double pitchDeg, out XYZ forward, out XYZ flatForward, out XYZ right, out XYZ up)
        {
            double yaw = yawDeg * Math.PI / 180.0;
            double pitch = pitchDeg * Math.PI / 180.0;
            forward = new XYZ(Math.Cos(pitch) * Math.Cos(yaw), Math.Cos(pitch) * Math.Sin(yaw), Math.Sin(pitch));
            flatForward = new XYZ(Math.Cos(yaw), Math.Sin(yaw), 0);
            right = new XYZ(Math.Sin(yaw), -Math.Cos(yaw), 0);
            up = right.CrossProduct(forward);
        }

        /// <summary>The camera for a start position and look direction (used by the command before the first frame).</summary>
        internal static ViewOrientation3D Orientation(XYZ eye, double yawDeg, double pitchDeg)
        {
            Look(yawDeg, pitchDeg, out XYZ forward, out _, out _, out XYZ up);
            return new ViewOrientation3D(eye, up, forward);
        }

        /// <summary>Stops any move still easing out (a jump, a pause).</summary>
        private void StopMotion()
        {
            _velocity = XYZ.Zero;
        }

        private void RefreshWorld()
        {
            _collision.Refresh();
            _levels = null;
            _forceFloorCheck = true;
            _roomSourcesDirty = true;   // a link loaded or a room placed during a pause
            ForgetCameraSlide();        // the wall behind the camera may have come or gone
        }

        private void HandleModeToggles(WalkInput input, WalkHudState hud)
        {
            if (input.FlyToggleQueued)
            {
                input.FlyToggleQueued = false;
                // F turns Fly on from Walk OR Pass-through, and off back to Walk.
                _session.Mode = _session.Mode == WalkMoveMode.Fly ? WalkMoveMode.Walk : WalkMoveMode.Fly;
                _forceFloorCheck = true;
                hud.Toast(_session.Mode == WalkMoveMode.Fly ? "Fly on - " + _session.Keys.Label(WalkKeyBindings.Up) + " up, " +
                                                              _session.Keys.Label(WalkKeyBindings.DownOrLowEye) + " down" : "Walk");
            }
            if (input.PassThroughToggleQueued)
            {
                input.PassThroughToggleQueued = false;
                _session.Mode = _session.Mode == WalkMoveMode.PassThrough ? WalkMoveMode.Walk : WalkMoveMode.PassThrough;
                _forceFloorCheck = true;
                ForgetCameraSlide();   // Pass-through kept the camera's full distance, even through a wall
                hud.Toast(_session.Mode == WalkMoveMode.PassThrough ? "Pass-through on - nothing blocks you" : "Walk");
            }
            if (input.LowEyeToggleQueued)
            {
                input.LowEyeToggleQueued = false;
                if (_session.Mode == WalkMoveMode.Walk)
                {
                    _session.LowEye = !_session.LowEye;
                    _forceFloorCheck = true;
                    hud.Toast(_session.LowEye
                        ? "Low eye on - " + _session.Settings.LowEyeHeightMm.ToString("N0") + " mm"
                        : "Eye back to " + _session.Settings.EyeHeightMm.ToString("N0") + " mm");
                }
            }
        }

        // ------------------------------------------------------------------ HUD text

        private void UpdateStatus(Document doc, WalkHudState hud, WalkInput input, XYZ position)
        {
            if (_levels == null)
            {
                _levels = new List<KeyValuePair<double, string>>();
                foreach (Level level in new FilteredElementCollector(doc).OfClass(typeof(Level)))
                    // ProjectElevation: the level's height from the project's own origin whatever its Elevation Base
                    // (Project / Survey Point). Level.Elevation follows the Elevation Base and can be metres off.
                    _levels.Add(new KeyValuePair<double, string>(level.ProjectElevation, level.Name));
                _levels.Sort((a, b) => a.Key.CompareTo(b.Key));
            }

            string levelName = "";
            double feetZ = position.Z - _session.EyeHeightFt;
            foreach (KeyValuePair<double, string> level in _levels)
                if (level.Key <= feetZ + WalkTuning.MmToFt(150))
                    levelName = level.Value;
            string location = string.IsNullOrEmpty(levelName) ? "Below the lowest level" : levelName;
            hud.LocationLine = string.IsNullOrEmpty(hud.RoomText) ? location : location + "  -  " + hud.RoomText;

            // Yaw is measured from +X (east) towards +Y - project north, not true north.
            double angle = ((input.YawDeg % 360.0) + 360.0) % 360.0;
            string[] directions = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
            string facing = directions[(int)Math.Round(angle / 45.0) % 8];

            string mode;
            switch (_session.Mode)
            {
                case WalkMoveMode.Fly: mode = "Fly"; break;
                case WalkMoveMode.PassThrough: mode = "Pass-through"; break;
                default:
                    mode = hud.Hovering ? "Walk - no floor below" : (_session.Grounded ? "Walk" : "Walk - dropping to the floor");
                    if (_session.LowEye) mode += " - low eye";
                    break;
            }
            if (input.Run) mode += " - running";
            mode += input.ThirdPerson ? "  -  Third person" : "  -  First person";

            hud.StatusLine = "Facing " + facing + " (project)  -  " + mode + "  -  Speed " + (input.SpeedStep + 1) + "/" + _session.Settings.SpeedStepCount;
        }

        private void UpdateViewRectangle(UIDocument uidoc, WalkHudState hud, WalkInput input)
        {
            hud.RectValid = false;
            foreach (UIView uiView in uidoc.GetOpenUIViews())
            {
                if (uiView.ViewId != _session.WalkViewId)
                    continue;

                Rectangle rect = uiView.GetWindowRectangle();
                hud.RectLeft = rect.Left;
                hud.RectTop = rect.Top;
                hud.RectRight = rect.Right;
                hud.RectBottom = rect.Bottom;
                hud.RectValid = true;

                bool changed = rect.Left != _prevLeft || rect.Top != _prevTop || rect.Right != _prevRight || rect.Bottom != _prevBottom;
                if (!_viewSyncDone || changed || input.ResyncViewQueued)
                {
                    try { uiView.ZoomToFit(); }
                    catch (Autodesk.Revit.Exceptions.ApplicationException) { /* a refused fit never ends a frame; tried again on the next change */ }
                    _viewSyncDone = true;
                    input.ResyncViewQueued = false;
                }
                _prevLeft = rect.Left;
                _prevTop = rect.Top;
                _prevRight = rect.Right;
                _prevBottom = rect.Bottom;
                return;
            }

            End(uidoc.Application, "The walk view's window was closed, so Model Walk ended.", touchModel: true, returnToStart: false);
        }

        // ------------------------------------------------------------------ the end

        /// <summary>
        /// Ends the walk from Revit's thread (a frame, or the ribbon button). Runs the clean-up once; never throws.
        /// <paramref name="touchModel"/> false when the model is gone; <paramref name="returnToStart"/> false when the
        /// modeller left the walk view themselves.
        /// </summary>
        internal void End(UIApplication app, string reason, bool touchModel, bool returnToStart)
        {
            if (_ended)
                return;
            _ended = true;
            var notes = new List<string>();
            Document doc = _session.Document;

            // The third-person character goes first, whatever else fails: hidden, off the active list, removed if Revit allows.
            bool characterWasShown = WalkCharacterServer.Pose.Visible;
            WalkCharacterServer.SwitchOff();
            if (characterWasShown && doc.IsValidObject)
            {
                try { app?.ActiveUIDocument?.RefreshActiveView(); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* the next redraw clears it */ }
            }

            if (touchModel && doc.IsValidObject)
            {
                View3D view = doc.GetElement(_session.WalkViewId) as View3D;
                if (view != null && _thirdEye != null && _session.Position != null)
                {
                    // Third person left the walk view's camera 3 m behind the player. The next walk starts from the
                    // walk view's camera, so put it back on the player's eyes - or that walk would start behind a wall.
                    // No transaction: the same camera move every frame makes.
                    try
                    {
                        double pitch = Math.Max(-WalkTuning.PitchClampDeg, Math.Min(WalkTuning.PitchClampDeg, _session.Input.PitchDeg));
                        view.SetOrientation(Orientation(_session.Position, _session.Input.YawDeg, pitch));
                        _thirdEye = null;
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException) { /* a locked view: the next walk starts from that camera */ }
                }
                if (view != null)
                {
                    if (_session.Settings.KeepSnagColours)
                    {
                        // The modeller's setting: the red stays for the meeting. The way back stays too - J in a walk,
                        // or any Save / Sync with Central gives every element its own colours back.
                        if (SnagColourMemory.Find(doc, view)?.Count > 0)
                            notes.Add("Snag colours kept, as your setting asks. Clear snag colours (" + _session.Keys.Label(WalkKeyBindings.ClearSnags) +
                                      " in a walk) gives them back, and so does the next Save or Sync with Central.");
                    }
                    else
                    {
                        try
                        {
                            string colours = SnagColourMemory.GiveBack(doc, view, "Model Walk: give back snag colours");
                            if (colours != null) notes.Add(colours);
                        }
                        catch (Exception ex) { notes.Add("The snag colours could not be given back (" + ex.Message + "). They are given back before the next save."); }
                    }

                    try
                    {
                        string hidden = ShowHiddenAtEnd(doc, view);
                        if (hidden != null) notes.Add(hidden);
                    }
                    catch (Exception ex) { notes.Add("What this walk hid could not be shown again (" + ex.Message + "). Use Revit's Reset Temporary Hide/Isolate."); }
                }

                if (returnToStart && _session.ReturnToStartView)
                {
                    try
                    {
                        UIDocument uidoc = app?.ActiveUIDocument;
                        if (uidoc != null && uidoc.Document.Equals(doc) && doc.GetElement(_session.StartViewId) is View start &&
                            start.Id != _session.WalkViewId && !start.IsTemplate)
                            uidoc.RequestViewChange(start);   // asynchronous: allowed from an ExternalEvent and a command
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException) { /* staying in the walk view is harmless */ }
                }
            }

            string report = _session.Report.WriteOnce();
            if (report != null) notes.Add(report);
            if (!touchModel && doc.IsValidObject)
            {
                // The model is closing: nothing in it was touched. In third person the walk view's camera stays 3 m
                // behind the player - if the close is cancelled, the next walk in this model puts it right.
                if (_thirdEye != null && _lastEye != null && _session.Position != null)
                {
                    _leftDoc = doc;
                    _leftViewId = _session.WalkViewId;
                    _leftCameraEye = _lastEye;
                    _leftPlayerEye = _session.Position;
                    _leftYawDeg = _session.Input.YawDeg;
                    _leftPitchDeg = Math.Max(-WalkTuning.PitchClampDeg, Math.Min(WalkTuning.PitchClampDeg, _session.Input.PitchDeg));
                }
                // If the close is cancelled, say what is still on.
                if (_snagMemory != null && _snagMemory.Count > 0 && !_session.Settings.KeepSnagColours)
                    notes.Add("If the model stays open, its snag colours are still on - they are given back before the next save, or when Model Walk starts again.");
                if (_hiddenByWalk.Count > 0)
                    notes.Add("If the model stays open, what this walk hid is still hidden - use Revit's Reset Temporary Hide/Isolate to show it.");
            }

            _session.Running = false;
            _session.Hud.Alive = false;
            _session.Hud.StopReason = (reason ?? "") + (notes.Count > 0 ? "\n" + string.Join("\n", notes) : "");
        }

        /// <summary>
        /// For the HUD when the walk ends without Revit's thread (the overlay closed, Revit busy): writes the report
        /// only, and calls no Revit API. Snag colours stay in memory and are given back before the next save or walk. Returns the note, or null.
        /// </summary>
        internal string EndWithoutRevit()
        {
            if (_ended)
                return null;
            _ended = true;
            _session.Running = false;
            // No Revit API here: the character is hidden at once and switched off on Revit's thread at its next idle moment.
            WalkCharacterServer.SwitchOffSoon();
            // The walk view's camera stays 3 m behind the player (no Revit API here): the next walk puts it right.
            if (_thirdEye != null && _lastEye != null && _session.Position != null)
            {
                _leftDoc = _session.Document;
                _leftViewId = _session.WalkViewId;
                _leftCameraEye = _lastEye;
                _leftPlayerEye = _session.Position;
                _leftYawDeg = _session.Input.YawDeg;
                _leftPitchDeg = Math.Max(-WalkTuning.PitchClampDeg, Math.Min(WalkTuning.PitchClampDeg, _session.Input.PitchDeg));
            }
            var notes = new List<string>();
            string report = _session.Report.WriteOnce();
            if (report != null) notes.Add(report);
            // No Revit API here: this runs on the overlay's own events (it closed, Revit was busy, an overlay fault).
            // The memory's count is plain .NET, and drops to 0 when a save, a sync or Clear gave the colours back.
            if (_snagMemory != null && _snagMemory.Count > 0)
                notes.Add(_session.Settings.KeepSnagColours
                    ? "Snag colours kept, as your setting asks. They are given back before the next save, or with Clear snag colours in a walk."
                    : "Revit was busy, so the snag colours are still on. They are given back before the next save, or when Model Walk starts again.");
            if (_hiddenByWalk.Count > 0)
                notes.Add("What this walk hid is still hidden for now. Use Revit's Reset Temporary Hide/Isolate to show it.");
            return notes.Count > 0 ? string.Join("\n", notes) : null;
        }
    }
}
