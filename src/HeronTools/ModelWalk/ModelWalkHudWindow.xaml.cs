// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk HUD - lifetime, the frame timer, pause and end, and pixel-exact placement over the Revit view.
// The window never calls Revit's API: it fills WalkInput and raises the engine's ExternalEvent.
//
// Safety: every timer tick and every key and mouse handler runs inside Guard(). Any exception there ends the walk
// cleanly - the mouse is let go and shown, capture released, the timer stopped and the overlay closed - instead of
// reaching Revit's message loop with the pointer hidden and captured.

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using HeronTools.Services.ModelWalk;

namespace HeronTools.UI.ModelWalk
{
    public partial class ModelWalkHudWindow : Window
    {
        private const int MaxLookStepPx = 400;      // a larger single mouse step is not aiming (see OnWalkMouseMove)
        private const int EndWaitTicks = 90;        // ~3 s for Revit to run the clean-up before the overlay closes anyway
        private const int EndShowTicks = 150;       // ~5 s to read the end message (a click closes it sooner)
        private const double DeniedStopSeconds = 5; // Revit refusing frames this long ends the walk
        private const double PauseAfterPictureMaxSeconds = 2; // Esc right after a snag waits for its picture, never longer

        /// <summary>The running walk's overlay (one at a time), for the ribbon toggle.</summary>
        internal static ModelWalkHudWindow Active { get; private set; }

        private readonly WalkSession _session;
        private readonly ExternalEvent _frameEvent;
        private readonly WalkEngine _engine;
        private readonly WalkKeyBindings _keys;
        private readonly DispatcherTimer _timer;

        private IntPtr _hwnd = IntPtr.Zero;
        private bool _paused;
        private bool _pauseAfterPicture;   // Esc came while a snag's picture was on its way: pause once it is saved
        private DateTime _pauseAfterPictureSince;
        private bool _ending;          // the end sequence has begun
        private bool _waitingForEngine;
        private bool _closedDown;
        private bool _failing;
        private int _endTicks;
        private bool _helpVisible = true;
        private bool _cleanScreen;
        private bool _showFrameTiming;
        private bool _mouseLookActive;
        private bool _lookDirty;
        private DateTime _lastRaise = DateTime.MinValue;
        private DateTime? _deniedSince;

        private int _fullLeft, _fullTop, _fullRight, _fullBottom;
        private bool _haveFullRect;

        internal ModelWalkHudWindow(WalkSession session, ExternalEvent frameEvent, WalkEngine engine)
        {
            _session = session;
            _frameEvent = frameEvent;
            _engine = engine;
            _keys = session.Keys;
            InitializeComponent();

            HelpText.Text = BuildHelpText();
            BuildModeChips();
            UpdateKeyHints();

            _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(WalkTuning.FrameIntervalMs) };
            _timer.Tick += (s, e) => Guard(OnTick);

            Loaded += (s, e) => Guard(OnLoadedOnce);
            Deactivated += (s, e) => Guard(() => { if (!_paused && !_ending && !_closedDown) PauseWalk(); });
            LostMouseCapture += (s, e) => Guard(() => { if (!_paused && !_ending && !_closedDown) PauseWalk(); });
            Closing += (s, e) => Guard(() => OnClosingRequest(e));
            Closed += OnClosedCleanup;

            PreviewKeyDown += (s, e) => Guard(() => OnWalkKeyDown(e));
            PreviewKeyUp += (s, e) => Guard(() => OnWalkKeyUp(e));
            PreviewMouseMove += (s, e) => Guard(() => OnWalkMouseMove(e));
            PreviewMouseDown += (s, e) => Guard(() => OnWalkMouseDown(e));
            PreviewMouseWheel += (s, e) => Guard(() => OnWalkMouseWheel(e));

            SnagColourMemory.GivenBackBeforeWriting += OnColoursGivenBack;

            if (session.Keys.LoadWarnings.Count > 0)
                session.Hud.Toast(session.Keys.LoadWarnings[0], WalkToastKind.Refused);
            else if (session.Settings.LoadWarnings.Count > 0)
                session.Hud.Toast(session.Settings.LoadWarnings[0], WalkToastKind.Refused);
        }

        /// <summary>Shows the overlay; only once it is up does the ribbon button know a walk is running.</summary>
        internal void ShowWalk()
        {
            Show();
            Active = this;
        }

        // ------------------------------------------------------------------ safety

        private void Guard(Action action)
        {
            if (_closedDown)
                return;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                FailSafe(ex);
            }
        }

        /// <summary>Whatever went wrong: free the mouse first, then end the walk; if even that fails, just close.</summary>
        private void FailSafe(Exception ex)
        {
            try { ReleaseLook(); } catch (Exception) { /* keep going: the close below also frees it */ }
            if (_failing)
            {
                try { Close(); } catch (Exception) { /* nothing more can be done */ }
                return;
            }
            _failing = true;
            try
            {
                EndHere("Model Walk stopped because of an unexpected problem (" + ex.Message + ").");
            }
            catch (Exception)
            {
                try { Close(); } catch (Exception) { /* nothing more can be done */ }
            }
        }

        // ------------------------------------------------------------------ lifetime

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            WalkHudState hud = _session.Hud;
            if (hud.RectValid)
                ApplyPixelRect(hud.RectLeft, hud.RectTop, hud.RectRight, hud.RectBottom, true);
        }

        private void OnLoadedOnce()
        {
            _timer.Start();
            EnterPlay();
        }

        /// <summary>The modeller ends the walk (Esc while paused). The clean-up needs Revit's thread: the next frame runs it.</summary>
        internal void RequestStop(string reason)
        {
            if (_ending || _closedDown)
                return;
            _session.StopRequested = true;
            _session.StopRequestReason = reason;
            _waitingForEngine = true;
            BeginEndSequence("Ending the walk...", "Putting the view back.");
            RaiseFrame();
        }

        /// <summary>The ribbon button, on Revit's thread: the engine cleans up now, then the overlay shows how it ended.</summary>
        internal void StopFromRibbon(UIApplication app)
        {
            _engine.End(app, "You ended Model Walk from the ribbon.", touchModel: true, returnToStart: true);
            _waitingForEngine = false;
            BeginEndSequence("Model Walk ended", _session.Hud.StopReason);
        }

        /// <summary>Ends without Revit's thread (a crash in the overlay, Revit refusing frames): the report is still written.</summary>
        private void EndHere(string reason)
        {
            string notes = _engine.EndWithoutRevit();
            _waitingForEngine = false;
            BeginEndSequence("Model Walk ended", reason + (notes != null ? "\n" + notes : ""));
        }

        private void BeginEndSequence(string title, string text)
        {
            if (_closedDown)
                return;
            CloseEntry(save: true);
            // Marked as ending BEFORE the mouse is let go: letting go raises LostMouseCapture, whose handler would
            // otherwise run the pause code (pause menu, picture and name requests dropped) inside this end.
            _ending = true;
            ReleaseLook();
            ClearHeldKeys();
            _paused = true;
            _session.Paused = true;
            HidePauseMenu();
            _endTicks = _waitingForEngine ? EndWaitTicks : EndShowTicks;
            ShowPill(title, string.IsNullOrWhiteSpace(text) ? "The walk view stays in the project - run Model Walk again to carry on." : text,
                     _waitingForEngine ? "" : "Click or press Enter to close.");
        }

        /// <summary>
        /// Alt+F4 (or anything else that asks the overlay to close) while the walk runs: not closed yet - the walk is
        /// ended the normal way first, so the next frame shows what this walk hid again, writes the report and goes back
        /// to the start view; the end sequence then closes the overlay. Model Walk's own closes come during the end
        /// sequence or a fault and go through. When Revit itself shuts down, Windows destroys this owned window without
        /// asking it to close, so nothing here can hold Revit up.
        /// </summary>
        private void OnClosingRequest(System.ComponentModel.CancelEventArgs e)
        {
            if (_ending || _closedDown || _failing || Dispatcher.HasShutdownStarted)
                return;
            e.Cancel = true;
            RequestStop("You closed the walk.");
        }

        private void OnClosedCleanup(object sender, EventArgs e)
        {
            if (_closedDown)
                return;
            _closedDown = true;
            SnagColourMemory.GivenBackBeforeWriting -= OnColoursGivenBack;

            // The overlay can also close without an end (Revit closing its main window): the report must still be written.
            try { _engine.EndWithoutRevit(); } catch (Exception) { /* closing must finish */ }
            _session.Running = false;
            try { _timer.Stop(); } catch (Exception) { /* closing must finish */ }
            try { ReleaseLook(); } catch (Exception) { /* closing must finish */ }
            try { _frameEvent?.Dispose(); } catch (Exception) { /* closing must finish */ }
            if (Active == this)
                Active = null;
        }

        private void OnColoursGivenBack(string line)
        {
            Guard(() => _session.Hud.Toast(line, WalkToastKind.Done));
        }

        // ------------------------------------------------------------------ frame timer

        private void OnTick()
        {
            WalkHudState hud = _session.Hud;

            if (_ending)
            {
                if (_waitingForEngine && !hud.Alive)
                {
                    // Revit ran the clean-up: show how it went.
                    _waitingForEngine = false;
                    _endTicks = EndShowTicks;
                    ShowPill("Model Walk ended", hud.StopReason, "Click or press Enter to close.");
                }
                if (--_endTicks <= 0)
                {
                    if (_waitingForEngine)
                    {
                        // Revit never ran it (busy with a command or a dialog).
                        string notes = _engine.EndWithoutRevit();
                        _waitingForEngine = false;
                        _endTicks = EndShowTicks;
                        ShowPill("Model Walk ended", "Revit was busy, so the view could not be put back." + (notes != null ? "\n" + notes : ""),
                                 "Click or press Enter to close.");
                        return;
                    }
                    _timer.Stop();
                    Close();
                }
                return;
            }

            if (!hud.Alive || !_session.Running)
            {
                _waitingForEngine = false;
                BeginEndSequence("Model Walk ended", hud.StopReason);
                return;
            }

            if (_paused)
            {
                UpdatePausedFromEngine(hud);
                return;
            }

            // A snag was just recorded: its picture (HUD hidden), then its comment box.
            if (hud.SnagAwaitingPicture > 0 && !_snapshotBusy && !EntryOpen)
            {
                int number = hud.SnagAwaitingPicture;
                hud.SnagAwaitingPicture = 0;
                FinishSnagRecord(number);
            }
            else if (_pauseAfterPicture && (!SnagPictureOnItsWay ||
                     (DateTime.UtcNow - _pauseAfterPictureSince).TotalSeconds > PauseAfterPictureMaxSeconds))
            {
                PauseWalk();   // the snag's picture is saved: now the pause asked for with Esc
                return;
            }
            else if (hud.ViewpointAwaitingName > 0 && !_snapshotBusy && !EntryOpen)
            {
                int number = hud.ViewpointAwaitingName;
                hud.ViewpointAwaitingName = 0;
                AskViewpointName(number);
            }

            // Follow the Revit view window if it moved or changed size.
            if (hud.RectValid && (hud.RectLeft != _fullLeft || hud.RectTop != _fullTop || hud.RectRight != _fullRight || hud.RectBottom != _fullBottom))
                ApplyPixelRect(hud.RectLeft, hud.RectTop, hud.RectRight, hud.RectBottom, true);

            if (hud.LookOverridePending)
            {
                _session.Input.YawDeg = hud.LookOverrideYawDeg;
                _session.Input.PitchDeg = hud.LookOverridePitchDeg;
                hud.LookOverridePending = false;
                _lookDirty = true;
            }

            // Ask Revit for a frame only when something can change - standing still costs four frames a second.
            WalkInput input = _session.Input;
            bool wanted = input.AnyMoveHeld || input.AnyQueued || _lookDirty || hud.NeedsFrames || input.GoToPointAimHold ||
                          (DateTime.UtcNow - _lastRaise).TotalMilliseconds >= WalkTuning.IdleFrameIntervalMs;
            if (wanted)
                RaiseFrame();

            RenderHud(hud);
        }

        private void RaiseFrame()
        {
            _lastRaise = DateTime.UtcNow;
            _lookDirty = false;
            ExternalEventRequest result = _frameEvent.Raise();
            if (result == ExternalEventRequest.Accepted || result == ExternalEventRequest.Pending)
            {
                _deniedSince = null;
                return;
            }
            // Denied or TimedOut: Revit is in a command or an edit mode. A short spell is normal; a long one ends the walk.
            if (_deniedSince == null)
                _deniedSince = DateTime.UtcNow;
            else if ((DateTime.UtcNow - _deniedSince.Value).TotalSeconds > DeniedStopSeconds && !_ending)
                EndHere("Revit stopped taking Model Walk's frames (it may be busy with a command), so the walk ended.");
        }

        // ------------------------------------------------------------------ play / pause

        private void EnterPlay()
        {
            if (_ending || _closedDown)
                return;

            _paused = false;
            _pauseAfterPicture = false;
            _session.Paused = false;
            HidePauseMenu();
            PauseLayer.Visibility = Visibility.Collapsed;
            PlayLayer.Visibility = Visibility.Visible;

            if (_haveFullRect)
                ApplyPixelRect(_fullLeft, _fullTop, _fullRight, _fullBottom, false);
            else if (_session.Hud.RectValid)
                ApplyPixelRect(_session.Hud.RectLeft, _session.Hud.RectTop, _session.Hud.RectRight, _session.Hud.RectBottom, true);

            Activate();
            Focus();
            Keyboard.Focus(this);
            StartLook();

            // While paused the view may have been zoomed or panned: re-fit, so clicks land on the crosshair.
            _session.Input.ResyncViewQueued = true;
            RaiseFrame();
        }

        /// <summary>A snag's picture is waiting to be taken or being taken right now.</summary>
        private bool SnagPictureOnItsWay => _session.Hud.SnagAwaitingPicture > 0 || _session.Hud.SnagPictureBusy;

        /// <summary>
        /// Esc or the Pause key right after a snag click (quick snagging): the pause waits for that snag's picture
        /// (about a tenth of a second) - pausing at once dropped the picture, or caught the pause card in it. Held keys
        /// stop at once. At most <see cref="PauseAfterPictureMaxSeconds"/>, then the walk pauses anyway.
        /// </summary>
        private void PauseWhenAsked()
        {
            if (_paused || _ending || _closedDown)
                return;
            if (!EntryOpen && SnagPictureOnItsWay)
            {
                if (!_pauseAfterPicture)
                    _pauseAfterPictureSince = DateTime.UtcNow;
                _pauseAfterPicture = true;
                ClearHeldKeys();
                return;
            }
            PauseWalk();
        }

        private void PauseWalk()
        {
            if (_paused || _ending || _closedDown)
                return;
            _pauseAfterPicture = false;
            CloseEntry(save: true);                 // keep what was typed
            // The view may change while paused: no picture of the wrong place. Said in the pause menu, so a snag
            // without a picture is never a surprise (this happens only when Revit took the focus right after a snag).
            int lostPicture = _session.Hud.SnagAwaitingPicture;
            _session.Hud.SnagAwaitingPicture = 0;
            _session.Hud.ViewpointAwaitingName = 0; // it keeps its own name; rename it by saving again
            _paused = true;
            _session.Paused = true;
            ReleaseLook();
            ClearHeldKeys();
            ShowPauseMenu();
            ShowPill("Paused", "Revit is free to use - change Visibility/Graphics, filters, anything, then carry on.",
                "Enter = carry on    S = settings    K = keys    Esc = end the walk");
            if (lostPicture > 0)
            {
                SetPauseStatus("Snag " + lostPicture + " was recorded without a picture: the walk paused before it was taken.");
                ShrinkToPill();
            }
        }

        private void ShowPill(string title, string line1, string line2)
        {
            PlayLayer.Visibility = Visibility.Collapsed;
            PauseLayer.Visibility = Visibility.Visible;
            PauseTitle.Text = title;
            PauseLine1.Text = line1 ?? "";
            PauseLine2.Text = line2 ?? "";
            PauseLine2.Visibility = string.IsNullOrEmpty(line2) ? Visibility.Collapsed : Visibility.Visible;
            ShrinkToPill();
        }

        /// <summary>
        /// The pause card is sized to its content, in device pixels from the window's own DPI - at 150 % or 200 %
        /// display scaling the old fixed 400 x 160 px pill cut the text off. Never taller than the view.
        /// </summary>
        private void ShrinkToPill()
        {
            if (!_haveFullRect)
                return;
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            PauseLayer.Measure(new Size(_pauseMenuShown ? 600 : 460, double.PositiveInfinity));
            Size wanted = PauseLayer.DesiredSize;
            double maxHeight = Math.Max(120, ((_fullBottom - _fullTop) / dpi.DpiScaleY) - 20);
            int width = (int)Math.Ceiling(Math.Max(300, wanted.Width) * dpi.DpiScaleX);
            int height = (int)Math.Ceiling(Math.Min(maxHeight, Math.Max(90, wanted.Height)) * dpi.DpiScaleY);
            int centreX = (_fullLeft + _fullRight) / 2;
            MoveWindowPixels(centreX - (width / 2), _fullTop + (int)(10 * dpi.DpiScaleY), width, height);
        }

        private void ClearHeldKeys()
        {
            WalkInput input = _session.Input;
            input.MoveForward = false;
            input.MoveBack = false;
            input.StrafeLeft = false;
            input.StrafeRight = false;
            input.UpKey = false;
            input.DownKey = false;
            input.Run = false;
            input.GoToPointAimHold = false;
        }

        // ------------------------------------------------------------------ pixel-exact placement

        private void ApplyPixelRect(int left, int top, int right, int bottom, bool remember)
        {
            if (remember)
            {
                // Does this move the CENTRE (the point every mouse-look step is measured from)? Revit moves the view's
                // rectangle whenever its own chrome changes - and changing the SELECTION does exactly that (the Options
                // Bar, the "Modify | ..." tab). With the pointer still parked on the old centre, the next mouse move
                // read as one huge turn and the camera flew across the model. Put the pointer on the new centre.
                bool centreMoved = _haveFullRect &&
                                   ((left + right) != (_fullLeft + _fullRight) || (top + bottom) != (_fullTop + _fullBottom));
                _fullLeft = left;
                _fullTop = top;
                _fullRight = right;
                _fullBottom = bottom;
                _haveFullRect = true;
                if (centreMoved && _mouseLookActive)
                    CentreCursor();
            }

            if (_paused)
            {
                ShrinkToPill();
                return;
            }
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            MoveWindowPixels(left, top, Math.Max((int)(200 * dpi.DpiScaleX), right - left), Math.Max((int)(150 * dpi.DpiScaleY), bottom - top));
        }

        private void MoveWindowPixels(int left, int top, int width, int height)
        {
            if (_hwnd == IntPtr.Zero)
                return;
            WalkNative.SetWindowPos(_hwnd, IntPtr.Zero, left, top, width, height, WalkNative.SWP_NOZORDER | WalkNative.SWP_NOACTIVATE);
        }
    }
}
