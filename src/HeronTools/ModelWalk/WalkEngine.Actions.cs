// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - what the keys and clicks do: go to point, viewpoints and Play viewpoints, doors, and the click in
// Select / Snag / Hide. Each click that changes the model is its own named transaction (one Undo step), and its
// Commit() is checked - a change Revit refuses is reported as refused, never as done.
// Linked elements: Select and Hide refuse them (host elements only); Snag records them in the report without a
// colour (an element inside a link cannot be coloured on its own).

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Utils;

namespace HeronTools.Services.ModelWalk
{
    internal sealed partial class WalkEngine
    {
        // Hide: the ids THIS walk hid, and whether the view's Temporary Hide/Isolate is ours. A Temporary
        // Hide/Isolate the modeller had on is never ended by Model Walk.
        private readonly List<ElementId> _hiddenByWalk = new List<ElementId>();
        private bool _tempModeIsOurs;

        // Play viewpoints
        private int _playIndex = -1;
        private double _playDwellLeft;
        private double _playSpeed;

        private OverrideGraphicSettings _snagOverride;

        // Measure: the first point, until the second is taken. Null when no measure is running.
        private XYZ _measureFrom;

        private XYZ HandleActions(UIDocument uidoc, Document doc, View3D view, WalkInput input, WalkHudState hud,
                                  XYZ position, XYZ forward, XYZ flatForward)
        {
            WalkKeyBindings keys = _session.Keys;

            // Revit's own "Reset Temporary Hide/Isolate" raises no event: notice it here - also a reset followed by a
            // Temporary Hide of the modeller's own during a pause (OurTemporaryHideStillOn).
            if (_tempModeIsOurs && !OurTemporaryHideStillOn(view))
            {
                _tempModeIsOurs = false;
                _hiddenByWalk.Clear();
                _collision.Refresh();
            }
            hud.HiddenCount = _hiddenByWalk.Count;

            if (input.GoToPointQueued)
            {
                input.GoToPointQueued = false;
                position = GoToPoint(position, forward, hud);
            }

            if (input.DoorQueued)
            {
                input.DoorQueued = false;
                position = ThroughDoor(position, forward, flatForward, hud);
            }

            if (input.SaveViewpointQueued)
            {
                input.SaveViewpointQueued = false;
                int number = _session.Viewpoints.Count + 1;
                _session.Viewpoints.Add(new WalkViewpoint
                {
                    Number = number,
                    Name = "Viewpoint " + number + " - " + hud.LocationLine,
                    Eye = position,
                    YawDeg = input.YawDeg,
                    PitchDeg = Math.Max(-WalkTuning.PitchClampDeg, Math.Min(WalkTuning.PitchClampDeg, input.PitchDeg))
                });
                string problem = _session.SaveViewpoints();
                hud.ViewpointAwaitingName = number;   // the HUD asks for a name (Esc keeps this one)
                if (problem != null)
                    hud.Toast(problem, WalkToastKind.Refused);
                else
                    hud.Toast(number <= 9
                        ? "Viewpoint " + number + " saved - press " + number + " to come back"
                        : "Viewpoint " + number + " saved - Play viewpoints (" + keys.Label(WalkKeyBindings.PlayViewpoints) + ") visits it",
                        WalkToastKind.Done);
            }

            if (input.JumpViewpoint > 0)
            {
                int number = input.JumpViewpoint;
                input.JumpViewpoint = 0;
                if (number <= _session.Viewpoints.Count)
                {
                    WalkViewpoint vp = _session.Viewpoints[number - 1];
                    position = vp.Eye;
                    LookAt(hud, vp.YawDeg, vp.PitchDeg);
                    _session.VerticalSpeedFtPerSec = 0;
                    _session.Grounded = false;
                    _forceFloorCheck = true;
                    hud.Toast(vp.Name);
                }
                else
                {
                    hud.Toast("No viewpoint " + number + " yet - press " + keys.Label(WalkKeyBindings.SaveViewpoint) + " to save one");
                }
            }

            if (input.MeasureQueued)
            {
                input.MeasureQueued = false;
                Measure(position, forward, hud);
            }

            if (input.ClickQueued)
            {
                input.ClickQueued = false;
                switch (input.Pointer)
                {
                    case WalkPointerMode.Select: ClickSelect(uidoc, position, forward, hud); break;
                    case WalkPointerMode.Snag: ClickSnag(uidoc, doc, view, position, forward, input, hud); break;
                    case WalkPointerMode.Hide: ClickHide(doc, view, position, forward, hud); break;
                }
            }

            if (input.ShowHiddenQueued)
            {
                input.ShowHiddenQueued = false;
                if (_hiddenByWalk.Count == 0)
                {
                    hud.Toast("This walk has not hidden anything");
                }
                else
                {
                    int n = _hiddenByWalk.Count;
                    string problem = EndOwnTemporaryHide(doc, view);
                    if (problem == null)
                        hud.Toast((n == 1 ? "1 element" : n + " elements") + " shown again", WalkToastKind.Done);
                    else
                        hud.Toast(problem, WalkToastKind.Refused);
                }
            }

            if (input.ClearSnagsQueued)
            {
                input.ClearSnagsQueued = false;
                string line = ClearSnagColours(doc, view);
                hud.Toast(line ?? "No snag colours to clear", line != null && line.EndsWith("back.", StringComparison.Ordinal) ? WalkToastKind.Done : WalkToastKind.Info);
            }

            if (input.OpenSnag > 0)
            {
                int number = input.OpenSnag;
                input.OpenSnag = 0;
                position = OpenSnag(uidoc, doc, number, position, hud);
            }

            hud.SnagCount = _session.Report.Count;
            return position;
        }

        /// <summary>
        /// Measure: the first press takes point 1 where the crosshair hits, the second takes point 2 and shows the
        /// distance with its horizontal and vertical parts - in text only, so it cannot drift off the model the way
        /// the old screen-drawn line did. One ray per press; the live distance uses the readout's own ray.
        /// </summary>
        private void Measure(XYZ position, XYZ forward, WalkHudState hud)
        {
            string key = _session.Keys.Label(WalkKeyBindings.Measure);
            WalkRayHit hit = _collision.CastFull(AimFrom(position), forward, WalkTuning.AimRangeFt);
            if (hit == null)
            {
                hud.Toast("Measure: aim at a surface, then press " + key, WalkToastKind.Refused);
                return;
            }

            if (_measureFrom == null)
            {
                _measureFrom = hit.Point;
                hud.MeasureShown = true;
                hud.MeasureDoneStampMs = 0;
                hud.MeasureLine1 = "Measure - point 1 set";
                hud.MeasureLine2 = "Aim at point 2 and press " + key;
                return;
            }

            XYZ d = hit.Point - _measureFrom;
            double total = WalkTuning.FtToMm(d.GetLength());
            double horizontal = WalkTuning.FtToMm(Math.Sqrt((d.X * d.X) + (d.Y * d.Y)));
            double vertical = WalkTuning.FtToMm(Math.Abs(d.Z));
            _measureFrom = null;
            hud.MeasureShown = true;
            hud.MeasureDoneStampMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            hud.MeasureLine1 = string.Format(System.Globalization.CultureInfo.CurrentCulture, "Measured {0:N0} mm", total);
            hud.MeasureLine2 = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                "Horizontal {0:N0} mm  -  vertical {1:N0} mm  -  {2} again for a new one", horizontal, vertical, key);
        }

        /// <summary>While point 1 is set: the live distance to where the crosshair is now (no extra ray).</summary>
        private void UpdateLiveMeasure(WalkRayHit aim, WalkHudState hud)
        {
            if (_measureFrom == null)
                return;
            hud.MeasureLine2 = aim == null
                ? "Aim at point 2 and press " + _session.Keys.Label(WalkKeyBindings.Measure)
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:N0} mm to the crosshair  -  press {1} for point 2",
                    WalkTuning.FtToMm((aim.Point - _measureFrom).GetLength()), _session.Keys.Label(WalkKeyBindings.Measure));
        }

        private static string ClearSnagColours(Document doc, View3D view) =>
            SnagColourMemory.GiveBack(doc, view, "Model Walk: clear snag colours");

        /// <summary>
        /// Open snag: the camera goes back where the snag was recorded (Fly again if it was recorded flying) and the
        /// element is added to Revit's selection - no transaction. A linked element cannot be selected on its own.
        /// </summary>
        private XYZ OpenSnag(UIDocument uidoc, Document doc, int number, XYZ position, WalkHudState hud)
        {
            WalkSnag snag = _session.Report.Find(number);
            if (snag?.Eye == null)
                return position;

            if (snag.MoveMode != WalkMoveMode.Walk)
                _session.Mode = snag.MoveMode;
            LookAt(hud, snag.YawDeg, snag.PitchDeg);
            _session.VerticalSpeedFtPerSec = 0;
            _session.Grounded = false;
            _forceFloorCheck = true;
            StopMotion();

            string selected;
            if (snag.HostElementId == null)
                selected = "the element is in a linked model, so it is not selected";
            else if (doc.GetElement(snag.HostElementId) == null)
                selected = "the element is no longer in the model";
            else
            {
                // Added to Revit's selection, never replacing it (owner's decision): what Select mode picked stays.
                var ids = new List<ElementId>(uidoc.Selection.GetElementIds());
                if (!ids.Contains(snag.HostElementId))
                    ids.Add(snag.HostElementId);
                uidoc.Selection.SetElementIds(ids);
                selected = "the element is added to the selection (" + ids.Count + " selected)";
            }
            hud.Toast("Snag " + number + ": back where it was recorded - " + selected, WalkToastKind.Done);
            return snag.Eye;
        }

        /// <summary>
        /// A frame while the walk is paused: only the pause menu's actions, nothing moves. The walk does not end
        /// because Revit shows another view - the modeller may be using Revit; the walk ends on resume as before.
        /// </summary>
        private void PausedFrame(UIDocument uidoc, Document doc)
        {
            WalkInput input = _session.Input;
            WalkHudState hud = _session.Hud;
            StopMotion();
            if (!input.ClearSnagsQueued && input.SaveViewpointAsView <= 0)
                return;

            if (uidoc == null || !uidoc.Document.Equals(doc))
            {
                input.ClearSnagsQueued = false;
                input.SaveViewpointAsView = 0;
                hud.SayInPause("Go back to the walk's model first, then try again.");
                return;
            }
            if (!(doc.GetElement(_session.WalkViewId) is View3D view))
            {
                End(uidoc.Application, "The walk view was deleted, so Model Walk ended.", touchModel: true, returnToStart: false);
                return;
            }

            if (input.ClearSnagsQueued)
            {
                input.ClearSnagsQueued = false;
                hud.SayInPause(ClearSnagColours(doc, view) ?? "No snag colours to clear.");
            }
            if (input.SaveViewpointAsView > 0)
            {
                int number = input.SaveViewpointAsView;
                input.SaveViewpointAsView = 0;
                WalkViewpoint vp = _session.Viewpoints.Find(v => v.Number == number);
                hud.SayInPause(vp == null ? "That viewpoint is no longer there." : SaveAsRevitView(doc, view, vp));
            }
        }

        /// <summary>
        /// "Save as Revit 3D view": a copy of the walk view (same Visibility/Graphics, filters and section box) looking
        /// from the viewpoint, with that camera saved in the model - one named transaction, one Undo step, a unique
        /// view name. Snag colours are never carried into it: each snagged element gets its own colours in the new
        /// view. Returns the plain line for the pause menu.
        /// </summary>
        private string SaveAsRevitView(Document doc, View3D walkView, WalkViewpoint vp)
        {
            if (doc.IsReadOnly)
                return "The model is read-only, so no view can be saved.";
            if (!walkView.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
                return "Revit does not let the walk view be copied, so no view was saved.";

            string name = UniqueViewName(doc, "Model Walk - " + vp.Name);
            using (var t = new Transaction(doc, "Model Walk: save viewpoint as 3D view"))
            {
                try
                {
                    t.Start();
                    var copy = doc.GetElement(walkView.Duplicate(ViewDuplicateOption.Duplicate)) as View3D;
                    if (copy == null)
                    {
                        t.RollBack();
                        return "Revit did not make the view, so nothing was changed.";
                    }
                    copy.Name = name;
                    SnagColourMemory.Find(doc, walkView)?.GiveOriginalsTo(doc, copy);
                    copy.SetOrientation(Orientation(vp.Eye, vp.YawDeg, vp.PitchDeg));
                    copy.SaveOrientation();   // the camera is kept in the model for this view (it does not lock it)
                    if (t.Commit() != TransactionStatus.Committed)
                        return "Revit did not keep the new view, so nothing was changed.";
                    return "Saved the 3D view \"" + name + "\" (one Undo step). Find it under 3D Views in the Project Browser.";
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    if (t.HasStarted() && !t.HasEnded())
                        t.RollBack();
                    return "The view could not be saved, so nothing was changed (Revit said: " + ex.Message + ").";
                }
            }
        }

        /// <summary>A view name Revit takes: no \ : { } [ ] | ; &lt; &gt; ? ` ~, and "name (2)", "(3)"... when taken.</summary>
        private static string UniqueViewName(Document doc, string wanted)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in wanted)
                sb.Append("\\:{}[]|;<>?`~".IndexOf(c) >= 0 ? '-' : c);
            string clean = sb.ToString().Trim();
            if (clean.Length == 0)
                clean = "Model Walk viewpoint";
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)))
                taken.Add(v.Name);
            string free = clean;
            for (int n = 2; taken.Contains(free); n++)
                free = clean + " (" + n + ")";
            return free;
        }

        private void LookAt(WalkHudState hud, double yawDeg, double pitchDeg)
        {
            hud.LookOverrideYawDeg = yawDeg;
            hud.LookOverridePitchDeg = pitchDeg;
            hud.LookOverridePending = true;
        }

        // ------------------------------------------------------------------ go to point, door

        private XYZ GoToPoint(XYZ position, XYZ forward, WalkHudState hud)
        {
            WalkRayHit target = _collision.CastFull(AimFrom(position), forward, WalkTuning.AimRangeFt);
            if (target == null)
            {
                hud.Toast("Aim at a floor or a surface first, then release " + _session.Keys.Label(WalkKeyBindings.GoToPoint), WalkToastKind.Refused);
                return position;
            }

            XYZ landing = target.Point - (forward * (WalkTuning.BodyRadiusFt * 2.0));
            if (_session.Mode == WalkMoveMode.Walk)
                landing = new XYZ(landing.X, landing.Y, Math.Max(landing.Z, target.Point.Z) + WalkTuning.MmToFt(100));

            if (_session.Mode == WalkMoveMode.PassThrough)
            {
                hud.Toast("Moved " + WalkTuning.FtToMm(PlayerDistanceFt(target, position)).ToString("N0") + " mm", WalkToastKind.Done);
                return landing;
            }

            string problem = LandingProblem(landing, out XYZ standing);
            if (problem != null)
            {
                hud.Toast("Not moved: " + problem, WalkToastKind.Refused);
                return position;
            }
            _session.VerticalSpeedFtPerSec = 0;
            _session.Grounded = _session.Mode == WalkMoveMode.Walk;
            _forceFloorCheck = true;
            hud.Toast("Moved " + WalkTuning.FtToMm(PlayerDistanceFt(target, position)).ToString("N0") + " mm", WalkToastKind.Done);
            return standing;
        }

        private XYZ ThroughDoor(XYZ position, XYZ forward, XYZ flatForward, WalkHudState hud)
        {
            if (_session.Mode == WalkMoveMode.PassThrough)
                return position;   // nothing blocks in Pass-through - just walk
            WalkRayHit door = _collision.CastNear(position, forward, WalkTuning.DoorReachFt);
            if (door == null || !door.IsDoor)
            {
                hud.Toast("Look at a door within " + (WalkTuning.DoorReachMm / 1000.0).ToString("0.0") + " m, then press " +
                          _session.Keys.Label(WalkKeyBindings.Door));
                return position;
            }

            XYZ through = flatForward.GetLength() > 1e-9 ? flatForward : forward;
            XYZ landing = position + (through.Normalize() * (door.DistanceFt + WalkTuning.DoorPassExtraFt));
            string problem = LandingProblem(landing, out XYZ standing);
            if (problem != null)
            {
                hud.Toast("Not through the door: " + problem + " on the other side", WalkToastKind.Refused);
                return position;
            }
            _forceFloorCheck = true;
            return standing;
        }

        // ------------------------------------------------------------------ play viewpoints

        private XYZ HandlePlayViewpoints(WalkInput input, WalkHudState hud, XYZ position, double dt)
        {
            if (input.PlayViewpointsQueued)
            {
                input.PlayViewpointsQueued = false;
                if (hud.PlayingViewpoints)
                {
                    StopPlaying(hud, "Stopped");
                }
                else if (_session.Viewpoints.Count == 0)
                {
                    hud.Toast("Save viewpoints with " + _session.Keys.Label(WalkKeyBindings.SaveViewpoint) + " first, then " +
                              _session.Keys.Label(WalkKeyBindings.PlayViewpoints) + " plays them");
                }
                else
                {
                    _playIndex = 0;
                    _playDwellLeft = 0;
                    _playSpeed = 0;
                    hud.PlayingViewpoints = true;
                    hud.Toast("Playing viewpoints - any move key stops");
                }
            }

            if (!hud.PlayingViewpoints)
                return position;

            if (input.AnyMoveHeld)
            {
                StopPlaying(hud, "Stopped");
                return position;
            }
            if (_playIndex < 0 || _playIndex >= _session.Viewpoints.Count)
            {
                StopPlaying(hud, "Stopped - the viewpoints changed");   // one was deleted from the pause menu
                return position;
            }

            WalkViewpoint target = _session.Viewpoints[_playIndex];
            if (_playDwellLeft > 0)
            {
                _playDwellLeft -= dt;
                EaseLookTo(hud, target.YawDeg, target.PitchDeg, dt);
                if (_playDwellLeft <= 0 && ++_playIndex >= _session.Viewpoints.Count)
                    StopPlaying(hud, "All viewpoints played");
                return position;
            }

            XYZ delta = target.Eye - position;
            double distance = delta.GetLength();
            // Eased: speeds up from each viewpoint and slows into the next, never faster than twice the walk step.
            double top = _session.Settings.WalkSpeedFtPerSec(WalkTuning.DefaultSpeedStep) * 2.0;
            double wanted = Math.Min(top, Math.Max(WalkTuning.MmToFt(WalkTuning.PlayMinSpeedMm), distance * 1.5));
            _playSpeed += (wanted - _playSpeed) * (1.0 - Math.Exp(-dt / 0.35));
            double step = _playSpeed * dt;
            if (distance <= step || distance < WalkTuning.MmToFt(WalkTuning.PlayArriveMm))
            {
                _playDwellLeft = 1.6;
                _playSpeed = 0;
                hud.Toast(target.Name);
                return target.Eye;
            }

            XYZ direction = delta.Normalize();
            double yaw = Math.Atan2(direction.Y, direction.X) * 180.0 / Math.PI;
            double pitch = Math.Asin(Math.Max(-1.0, Math.Min(1.0, direction.Z))) * 180.0 / Math.PI;
            EaseLookTo(hud, yaw, Math.Max(-60.0, Math.Min(60.0, pitch)), dt);
            return position + (direction * step);
        }

        /// <summary>Play viewpoints turns smoothly (about a third of a second) instead of snapping to each new heading.</summary>
        private void EaseLookTo(WalkHudState hud, double yawDeg, double pitchDeg, double dt)
        {
            double k = 1.0 - Math.Exp(-dt / 0.3);
            double yawNow = _session.Input.YawDeg;
            double turn = ((yawDeg - yawNow) % 360.0 + 540.0) % 360.0 - 180.0;   // the short way round
            LookAt(hud, yawNow + (turn * k), _session.Input.PitchDeg + ((pitchDeg - _session.Input.PitchDeg) * k));
        }

        private void StopPlaying(WalkHudState hud, string message)
        {
            hud.PlayingViewpoints = false;
            _playIndex = -1;
            _forceFloorCheck = true;
            hud.Toast(message);
        }

        // ------------------------------------------------------------------ clicks

        private WalkRayHit ClickTarget(XYZ position, XYZ forward, WalkHudState hud)
        {
            WalkRayHit hit = _collision.CastFull(AimFrom(position), forward, WalkTuning.AimRangeFt);
            if (hit?.Element == null)
            {
                hud.Toast("Nothing under the crosshair", WalkToastKind.Refused);
                return null;
            }
            return hit;
        }

        private void ClickSelect(UIDocument uidoc, XYZ position, XYZ forward, WalkHudState hud)
        {
            WalkRayHit hit = ClickTarget(position, forward, hud);
            if (hit == null)
                return;
            if (hit.FromLink)
            {
                hud.Toast("That element is in a linked model - Select works on this model's elements only", WalkToastKind.Refused);
                return;
            }

            // No transaction: the selection is not a model change. It stays after the walk, ready to edit.
            var ids = new List<ElementId>(uidoc.Selection.GetElementIds());
            ElementId id = hit.Element.Id;
            string what = WalkCollision.DescribeLine1(hit);
            if (ids.Remove(id))
            {
                uidoc.Selection.SetElementIds(ids);
                hud.Toast("Taken out of the selection: " + what + "  (" + ids.Count + " selected)");
            }
            else
            {
                ids.Add(id);
                uidoc.Selection.SetElementIds(ids);
                hud.Toast("Selected: " + what + "  (" + ids.Count + " selected - kept after the walk)", WalkToastKind.Done);
            }
        }

        private void ClickSnag(UIDocument uidoc, Document doc, View3D view, XYZ position, XYZ forward, WalkInput input, WalkHudState hud)
        {
            WalkRayHit hit = ClickTarget(position, forward, hud);
            if (hit == null)
                return;

            string what = WalkCollision.DescribeLine1(hit);
            // Already a snag in THIS walk's report: say which one, no second row. An element still red from an earlier
            // walk (kept colours), or whose snag step was undone, is a new snag for this report: it gets its row, and
            // the colour memory keeps its true original (Colour copies the override only the first time).
            if (!hit.FromLink)
            {
                foreach (WalkSnag earlier in _session.Report.Snags)
                {
                    if (earlier.HostElementId != null && earlier.HostElementId == hit.Element.Id)
                    {
                        hud.Toast("Already snag " + earlier.Number + " in this walk: " + what);
                        return;
                    }
                }
            }

            Element element = hit.Element;
            WalkSnag snag = _session.Report.Add(new WalkSnag
            {
                Category = WalkCollision.CategoryName(element),
                FamilyType = WalkCollision.FamilyTypeName(element),
                Size = WalkCollision.SizeText(element),
                ElementIdValue = element.Id.LongValue(),
                UniqueId = element.UniqueId ?? "",
                Level = WalkCollision.LevelName(element),
                System = WalkCollision.SystemText(element),
                LinkName = hit.FromLink ? WalkCollision.LinkName(hit.Link) : "",
                Room = hud.RoomText,
                HostElementId = hit.FromLink ? null : element.Id,
                HitPoint = hit.Point,
                Eye = position,
                YawDeg = input.YawDeg,
                PitchDeg = Math.Max(-WalkTuning.PitchClampDeg, Math.Min(WalkTuning.PitchClampDeg, input.PitchDeg)),
                MoveMode = _session.Mode
            });

            string whyNot;
            if (hit.FromLink)
                whyNot = "a linked element cannot be coloured on its own";
            else if (doc.IsReadOnly)
                whyNot = "the model is read-only";
            else if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, view.Id) == CheckoutStatus.OwnedByOtherUser)
                whyNot = "the walk view is being edited by someone else";
            else
                whyNot = ColourSnag(doc, view, element.Id, snag.Number);

            snag.Coloured = whyNot == null;
            snag.NotColouredWhy = whyNot;
            // The snag's picture is of the model: the third-person figure is hidden until it is taken.
            bool figureWasShown = WalkFigureServer.Pose.Visible;
            WalkFigureServer.Pose.Visible = false;
            if (snag.Coloured || figureWasShown)
                uidoc.RefreshActiveView();   // so the snag's picture shows it red, without the figure

            string problem = _session.Report.Save();
            hud.SnagCount = _session.Report.Count;
            hud.SnagAwaitingPicture = snag.Number;   // the HUD takes the picture, then (if asked for) opens the comment box
            // Quick snagging (standard): no comment box - the comment is written later in the pause menu's snag list.
            string later = _session.Settings.AskSnagComment ? "" : " - add a comment in the pause menu (Esc)";
            if (problem != null)
                hud.Toast(problem, WalkToastKind.Refused);
            else
                hud.Toast(whyNot == null
                    ? "Snag " + snag.Number + " recorded: " + what + later
                    : "Snag " + snag.Number + " recorded (not coloured: " + whyNot + "): " + what + later,
                    WalkToastKind.Done);
        }

        /// <summary>Colours one element as one named Undo step. Returns null, or why it was not coloured.</summary>
        private string ColourSnag(Document doc, View3D view, ElementId id, int number)
        {
            if (_snagOverride == null)
                _snagOverride = SnagColourMemory.SnagOverride(doc);
            SnagColourMemory memory = SnagColourMemory.FindOrCreate(doc, view);
            _snagMemory = memory;
            // Remembered already (an earlier walk, or a snag step undone): its stored original is the true one and must
            // survive a refused colour. Only a copy made by THIS attempt is dropped when the colour does not go on.
            bool newToMemory = !memory.IsColoured(id);

            using (var t = new Transaction(doc, "Model Walk: snag " + number))
            {
                try
                {
                    t.Start();
                    memory.Colour(view, id, _snagOverride);
                    if (t.Commit() == TransactionStatus.Committed)
                        return null;
                    if (newToMemory) memory.Forget(id);
                    return "Revit did not keep the colour";
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    if (t.HasStarted() && !t.HasEnded())
                        t.RollBack();
                    if (newToMemory) memory.Forget(id);
                    return "Revit refused the colour (" + ex.Message + ")";
                }
            }
        }

        private void ClickHide(Document doc, View3D view, XYZ position, XYZ forward, WalkHudState hud)
        {
            WalkRayHit hit = ClickTarget(position, forward, hud);
            if (hit == null)
                return;
            if (hit.FromLink)
            {
                hud.Toast("That element is in a linked model - hide its category in Visibility/Graphics instead", WalkToastKind.Refused);
                return;
            }
            if (!_tempModeIsOurs && view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
            {
                hud.Toast("This view has your own Temporary Hide/Isolate on, so Hide is off - it would mix with yours. " +
                          "Reset it (the glasses on the view control bar) to use Hide.", WalkToastKind.Refused);
                return;
            }

            ElementId id = hit.Element.Id;
            string problem = RunTemporary(doc, "Model Walk: hide", () => view.HideElementsTemporary(new List<ElementId> { id }));
            if (problem != null)
            {
                hud.Toast("Not hidden: " + problem, WalkToastKind.Refused);
                return;
            }
            _tempModeIsOurs = true;
            _hiddenByWalk.Add(id);
            hud.HiddenCount = _hiddenByWalk.Count;
            _collision.Refresh();
            hud.Toast("Hidden for now: " + WalkCollision.DescribeLine1(hit) + "   (" + _session.Keys.Label(WalkKeyBindings.ShowHidden) +
                      " shows what this walk hid)", WalkToastKind.Done);
        }

        /// <summary>
        /// True while the view's Temporary Hide/Isolate is still the one this walk made: the mode is on and every element
        /// this walk hid is still hidden. If the modeller reset it during a pause and hid things of their own, one of
        /// ours shows again - then the mode is theirs, and Show hidden must not end it. (An isolate of their own that
        /// happens to hide all of ours cannot be told apart: Revit has no list of what a Temporary Hide holds.)
        /// </summary>
        private bool OurTemporaryHideStillOn(View3D view)
        {
            if (!view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
                return false;
            Document doc = view.Document;
            int looked = 0;
            foreach (ElementId id in _hiddenByWalk)
            {
                if (doc.GetElement(id) == null)
                    continue;   // deleted since: says nothing either way
                try
                {
                    if (view.IsElementVisibleInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate, id))
                        return false;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    return false;   // cannot tell: treat it as the modeller's, so it is never ended without asking
                }
                if (++looked >= 50)
                    break;          // a few are enough, and keep the frame cheap
            }
            return true;
        }

        /// <summary>Shows again what this walk hid; the modeller's own Temporary Hide/Isolate is never ended. Returns null or a reason.</summary>
        private string EndOwnTemporaryHide(Document doc, View3D view)
        {
            if (_tempModeIsOurs && !OurTemporaryHideStillOn(view))
                _tempModeIsOurs = false;   // ended from the ribbon after a pause: the per-frame check never ran
            if (!_tempModeIsOurs)
            {
                _hiddenByWalk.Clear();
                return null;
            }
            string problem = RunTemporary(doc, "Model Walk: show hidden",
                () => view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate));
            if (problem != null)
                return "Not shown again: " + problem;
            _tempModeIsOurs = false;
            _hiddenByWalk.Clear();
            _collision.Refresh();
            return null;
        }

        private string ShowHiddenAtEnd(Document doc, View3D view)
        {
            if (_hiddenByWalk.Count == 0 || !_tempModeIsOurs)
                return null;
            if (!OurTemporaryHideStillOn(view))
            {
                _tempModeIsOurs = false;
                _hiddenByWalk.Clear();
                return "The Temporary Hide/Isolate in the walk view is yours now (it was reset during a pause), so it was left on.";
            }
            int n = _hiddenByWalk.Count;
            string problem = EndOwnTemporaryHide(doc, view);
            return problem ?? ((n == 1 ? "1 element" : n + " elements") + " this walk hid " + (n == 1 ? "is" : "are") + " shown again.");
        }

        /// <summary>
        /// Temporary Hide/Isolate is not saved in the model, so Revit may take it without a transaction; when it asks
        /// for one, it gets one named transaction, and its Commit() is checked. Returns null, or a plain reason.
        /// NEEDS REAL REVIT: which of the two Revit 2020-2027 want.
        /// </summary>
        private static string RunTemporary(Document doc, string undoName, Action change)
        {
            try
            {
                change();
                return null;
            }
            catch (Autodesk.Revit.Exceptions.ModificationOutsideTransactionException)
            {
                using (var t = new Transaction(doc, undoName))
                {
                    try
                    {
                        t.Start();
                        change();
                        return t.Commit() == TransactionStatus.Committed ? null : "Revit did not keep the change";
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                    {
                        if (t.HasStarted() && !t.HasEnded())
                            t.RollBack();
                        return "Revit refused (" + ex.Message + ")";
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                return "Revit refused (" + ex.Message + ")";
            }
        }
    }
}
