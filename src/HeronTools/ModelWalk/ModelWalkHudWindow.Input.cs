// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk HUD - mouse look, clicks, the wheel and the keys. Every handler is called through Guard() (see the
// main file), so an exception here ends the walk cleanly. Fixed keys: Esc (pause / end), Tab and Shift+Tab (mode),
// 1-9 (viewpoints), the arrow keys (walk). Everything else follows WalkKeyBindings.

using System;
using System.Windows;
using System.Windows.Input;
using HeronTools.Services.ModelWalk;

namespace HeronTools.UI.ModelWalk
{
    public partial class ModelWalkHudWindow
    {
        // ------------------------------------------------------------------ mouse look

        private void StartLook()
        {
            // Park the pointer on the look centre BEFORE arming the look: capturing the mouse makes WPF send a move
            // at once, and with the look armed it would be measured from wherever the pointer was and whip the camera.
            bool captured = CaptureMouse();
            Cursor = Cursors.None;
            CentreCursor();
            _mouseLookActive = captured;
        }

        private void ReleaseLook()
        {
            bool wasActive = _mouseLookActive;
            _mouseLookActive = false;
            Cursor = Cursors.Arrow;
            if (wasActive && IsMouseCaptured)
                ReleaseMouseCapture();
        }

        private void CentreCursor()
        {
            if (_haveFullRect)
                WalkNative.SetCursorPos((_fullLeft + _fullRight) / 2, (_fullTop + _fullBottom) / 2);
        }

        private void OnWalkMouseMove(MouseEventArgs e)
        {
            if (_paused || _ending || !_mouseLookActive || !_haveFullRect)
                return;
            if (!WalkNative.GetCursorPos(out WalkNative.NativePoint cursor))
                return;

            int centreX = (_fullLeft + _fullRight) / 2;
            int centreY = (_fullTop + _fullBottom) / 2;
            int dx = cursor.X - centreX;
            int dy = cursor.Y - centreY;
            if (dx == 0 && dy == 0)
                return;

            // Backstop: one step this large is not aiming - something moved the centre under the pointer (the view
            // resized, the display scaling changed, a remote session moved the pointer). Re-centre and drop it.
            if (Math.Abs(dx) > MaxLookStepPx || Math.Abs(dy) > MaxLookStepPx || _session.Hud.PlayingViewpoints)
            {
                WalkNative.SetCursorPos(centreX, centreY);
                e.Handled = true;
                return;
            }

            WalkInput input = _session.Input;
            WalkSettings settings = _session.Settings;
            double perPixel = settings.MouseDegPerPixel;
            input.YawDeg -= dx * perPixel;
            input.PitchDeg -= dy * perPixel * (settings.InvertLook ? -1.0 : 1.0);
            input.PitchDeg = Math.Max(-WalkTuning.PitchClampDeg, Math.Min(WalkTuning.PitchClampDeg, input.PitchDeg));
            _lookDirty = true;

            WalkNative.SetCursorPos(centreX, centreY);
            e.Handled = true;
        }

        private void OnWalkMouseDown(MouseButtonEventArgs e)
        {
            if (_closedDown)
                return;
            if (_ending)
            {
                if (!_waitingForEngine)
                    Close();   // the end message: a click closes it
                e.Handled = true;
                return;
            }
            if (_paused)
            {
                EnterPlay();
                e.Handled = true;
                return;
            }
            if (!_mouseLookActive)
                StartLook();

            if (e.ChangedButton == MouseButton.Left)
            {
                if (_session.Input.Pointer == WalkPointerMode.Inspect)
                    _session.Hud.Toast("Inspect only reads. Right-click or Tab for Select, Snag or Hide.");
                else
                    _session.Input.ClickQueued = true;
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                StepMode(+1);
                e.Handled = true;
            }
        }

        private void OnWalkMouseWheel(MouseWheelEventArgs e)
        {
            if (_paused || _ending || _closedDown)
                return;
            StepSpeed(e.Delta > 0 ? +1 : -1);
            e.Handled = true;
        }

        private void StepMode(int by)
        {
            int count = Enum.GetValues(typeof(WalkPointerMode)).Length;
            int next = (((int)_session.Input.Pointer + by) % count + count) % count;
            _session.Input.Pointer = (WalkPointerMode)next;
            UpdateModeChips();
            _session.Hud.Toast(ModeHelp(_session.Input.Pointer));
        }

        private void StepSpeed(int by)
        {
            WalkInput input = _session.Input;
            WalkSettings settings = _session.Settings;
            input.SpeedStep = settings.ClampSpeedStep(input.SpeedStep + by);
            _session.Hud.Toast(string.Format("Speed {0}/{1} - {2:0.0} m/s walking", input.SpeedStep + 1, settings.SpeedStepCount,
                settings.SpeedsMetresPerSec[input.SpeedStep]));
        }

        private string ModeHelp(WalkPointerMode mode)
        {
            switch (mode)
            {
                case WalkPointerMode.Select: return "Select - click adds an element to Revit's selection (click again takes it out). Kept after the walk.";
                case WalkPointerMode.Snag: return "Snag - click colours the element and adds it to the snag report. Its own colours come back at the end.";
                case WalkPointerMode.Hide: return "Hide - click hides the element for now. " + _keys.Label(WalkKeyBindings.ShowHidden) + " shows what this walk hid.";
                default: return "Inspect - what is under the crosshair, and how far it is.";
            }
        }

        // ------------------------------------------------------------------ keys

        private void OnWalkKeyDown(KeyEventArgs e)
        {
            if (_closedDown)
                return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            WalkInput input = _session.Input;

            if (_ending)
            {
                if (!_waitingForEngine && (key == Key.Escape || key == Key.Enter))
                    Close();
                e.Handled = true;
                return;
            }

            if (_paused)
            {
                if (key == Key.Escape)
                {
                    RequestStop("You ended the walk.");
                    e.Handled = true;
                }
                else if (key == Key.S)
                {
                    OpenSettings();
                    e.Handled = true;
                }
                else if (key == Key.K)
                {
                    OpenKeySettings();
                    e.Handled = true;
                }
                return;
            }

            // Fixed keys
            if (key == Key.Escape) { PauseWalk(); e.Handled = true; return; }
            if (key == Key.Tab)
            {
                if (!e.IsRepeat) StepMode((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : +1);
                e.Handled = true;
                return;
            }
            if (key >= Key.D1 && key <= Key.D9) { if (!e.IsRepeat) input.JumpViewpoint = key - Key.D1 + 1; e.Handled = true; return; }
            if (key >= Key.NumPad1 && key <= Key.NumPad9) { if (!e.IsRepeat) input.JumpViewpoint = key - Key.NumPad1 + 1; e.Handled = true; return; }
            if (key == Key.Up) { input.MoveForward = true; AutoHideHelp(); e.Handled = true; return; }
            if (key == Key.Down) { input.MoveBack = true; AutoHideHelp(); e.Handled = true; return; }
            if (key == Key.Left) { input.StrafeLeft = true; AutoHideHelp(); e.Handled = true; return; }
            if (key == Key.Right) { input.StrafeRight = true; AutoHideHelp(); e.Handled = true; return; }

            string action = _keys.FindAction(key);
            if (action == null)
                return;
            bool first = !e.IsRepeat;

            switch (action)
            {
                case WalkKeyBindings.MoveForward: input.MoveForward = true; AutoHideHelp(); break;
                case WalkKeyBindings.MoveBack: input.MoveBack = true; AutoHideHelp(); break;
                case WalkKeyBindings.StrafeLeft: input.StrafeLeft = true; AutoHideHelp(); break;
                case WalkKeyBindings.StrafeRight: input.StrafeRight = true; AutoHideHelp(); break;
                case WalkKeyBindings.Run: input.Run = true; break;
                case WalkKeyBindings.Up: input.UpKey = true; break;
                case WalkKeyBindings.DownOrLowEye:
                    // Walk: a press toggles the low eye. Fly and Pass-through: held = down.
                    if (_session.Mode == WalkMoveMode.Walk) { if (first) input.LowEyeToggleQueued = true; }
                    else input.DownKey = true;
                    break;
                case WalkKeyBindings.Door: if (first) input.DoorQueued = true; break;
                case WalkKeyBindings.Fly: if (first) input.FlyToggleQueued = true; break;
                case WalkKeyBindings.PassThrough: if (first) input.PassThroughToggleQueued = true; break;
                case WalkKeyBindings.BackToStart: if (first) input.BackToStartQueued = true; break;
                case WalkKeyBindings.GoToPoint: input.GoToPointAimHold = true; break;
                case WalkKeyBindings.SaveViewpoint: if (first) input.SaveViewpointQueued = true; break;
                case WalkKeyBindings.PlayViewpoints: if (first) input.PlayViewpointsQueued = true; break;
                case WalkKeyBindings.ShowHidden: if (first) input.ShowHiddenQueued = true; break;
                case WalkKeyBindings.ClearSnags: if (first) input.ClearSnagsQueued = true; break;
                case WalkKeyBindings.Snapshot: if (first) TakeSnapshot(); break;
                case WalkKeyBindings.SpeedUp: if (first) StepSpeed(+1); break;
                case WalkKeyBindings.SpeedDown: if (first) StepSpeed(-1); break;
                case WalkKeyBindings.CleanScreen: if (first) _cleanScreen = !_cleanScreen; break;
                case WalkKeyBindings.Help: if (first) _helpVisible = !_helpVisible; break;
                case WalkKeyBindings.Pause: PauseWalk(); break;
                case WalkKeyBindings.FrameTiming: if (first) _showFrameTiming = !_showFrameTiming; break;
                default: return;
            }
            e.Handled = true;
        }

        private void OnWalkKeyUp(KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            WalkInput input = _session.Input;

            if (key == Key.Up) { input.MoveForward = false; e.Handled = true; return; }
            if (key == Key.Down) { input.MoveBack = false; e.Handled = true; return; }
            if (key == Key.Left) { input.StrafeLeft = false; e.Handled = true; return; }
            if (key == Key.Right) { input.StrafeRight = false; e.Handled = true; return; }

            switch (_keys.FindAction(key))
            {
                case WalkKeyBindings.MoveForward: input.MoveForward = false; break;
                case WalkKeyBindings.MoveBack: input.MoveBack = false; break;
                case WalkKeyBindings.StrafeLeft: input.StrafeLeft = false; break;
                case WalkKeyBindings.StrafeRight: input.StrafeRight = false; break;
                case WalkKeyBindings.Run: input.Run = false; break;
                case WalkKeyBindings.Up: input.UpKey = false; break;
                case WalkKeyBindings.DownOrLowEye: input.DownKey = false; break;
                case WalkKeyBindings.GoToPoint:
                    if (input.GoToPointAimHold)
                    {
                        input.GoToPointAimHold = false;
                        if (!_paused && !_ending) input.GoToPointQueued = true;
                    }
                    break;
                default: return;
            }
            e.Handled = true;
        }

        private void AutoHideHelp() => _helpVisible = false;

        /// <summary>Settings - only while paused. Save applies them to this walk at once; Cancel changes nothing.</summary>
        private void OpenSettings()
        {
            var window = new ModelWalkSettingsWindow(_session.Settings) { Owner = this };
            if (window.ShowDialog() != true || window.Result == null)
                return;
            _session.Settings = window.Result;
            _session.Input.SettingsChangedQueued = true;   // the engine finds the floor again at the new eye height
            HelpText.Text = BuildHelpText();
            _session.Hud.Toast(window.SaveProblem ?? "Settings saved", window.SaveProblem == null ? WalkToastKind.Done : WalkToastKind.Refused);
        }

        /// <summary>Key Settings - only while paused. Cancel (or closing it) puts the old keys back.</summary>
        private void OpenKeySettings()
        {
            var window = new ModelWalkKeysWindow(_keys) { Owner = this };
            window.ShowDialog();
            HelpText.Text = BuildHelpText();
            UpdateKeyHints();
            UpdateModeChips();
        }
    }
}
