// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk HUD - drawing each frame: location, mode chips, Inspect readout, go-to-point ring, viewpoints, door
// hint, snag count, messages and the help card. Brushes come only from UI/HeronStyles.xaml (FindResource); the
// active mode chip is the one orange thing on screen.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HeronTools.Services.ModelWalk;

namespace HeronTools.UI.ModelWalk
{
    public partial class ModelWalkHudWindow
    {
        private int _lastViewpointsVersion = -1;
        private readonly Border[] _chips = new Border[4];
        private readonly TextBlock[] _chipTexts = new TextBlock[4];

        private Brush B(string key) => (Brush)FindResource(key);

        private void RenderHud(WalkHudState hud)
        {
            WalkInput input = _session.Input;

            HudChrome.Visibility = _cleanScreen ? Visibility.Collapsed : Visibility.Visible;

            LocationText.Text = hud.LocationLine;
            StatusText.Text = hud.StatusLine;
            ClearHeightText.Text = hud.ClearHeightMm > 0 ? string.Format("Clear height {0:N0} mm", hud.ClearHeightMm) : "";
            ClearHeightText.Visibility = hud.ClearHeightMm > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Frame budget, measured every frame - shown only on the Frame timing key.
            // The same key shows the third-person character's line under it (what Revit drew, and why not).
            FrameText.Visibility = _showFrameTiming ? Visibility.Visible : Visibility.Collapsed;
            hud.DiagnosticsWanted = _showFrameTiming;
            if (_showFrameTiming)
                FrameText.Text = string.Format("Frame {0:0} ms (average {1:0} ms) - {2} rays", hud.LastFrameMs, hud.AverageFrameMs, hud.LastFrameRays) +
                                 (string.IsNullOrEmpty(hud.CharacterLine) ? "" : "\n" + hud.CharacterLine);

            // Messages
            long age = DateTimeOffset.Now.ToUnixTimeMilliseconds() - hud.ToastStampMs;
            if (hud.ToastStampMs > 0 && age < hud.ToastHoldMs && !string.IsNullOrEmpty(hud.ToastText))
            {
                ToastText.Text = hud.ToastText;
                ToastText.Foreground = B(hud.ToastKind == WalkToastKind.Done ? "AccentSuccess"
                                       : hud.ToastKind == WalkToastKind.Refused ? "AccentDanger" : "TextPrimary");
                ToastCard.Visibility = Visibility.Visible;
            }
            else
            {
                ToastCard.Visibility = Visibility.Collapsed;
            }

            // Inspect readout (shown in every mode: it is what a click would act on)
            if (hud.AimHasHit && !string.IsNullOrEmpty(hud.AimLine1))
            {
                ReadoutLine1.Text = hud.AimLine1;
                ReadoutLine2.Text = hud.AimLine2;
                ReadoutLine2.Visibility = string.IsNullOrEmpty(hud.AimLine2) ? Visibility.Collapsed : Visibility.Visible;
                DistanceText.Text = string.Format("{0:N0} mm", hud.AimDistanceMm);
                ReadoutCard.Visibility = Visibility.Visible;
            }
            else
            {
                ReadoutCard.Visibility = Visibility.Collapsed;
            }

            // Go to point
            if (input.GoToPointAimHold)
            {
                GoToRing.Visibility = Visibility.Visible;
                GoToRing.Stroke = B(hud.GoToPointValid ? "TextPrimary" : "TextDisabled");
                GoToCard.Visibility = Visibility.Visible;
                GoToText.Text = hud.GoToPointValid
                    ? string.Format("Release {0} to go - {1:N0} mm", _keys.Label(WalkKeyBindings.GoToPoint), hud.GoToPointDistanceMm)
                    : "Aim at a floor or a surface...";
            }
            else
            {
                GoToRing.Visibility = Visibility.Collapsed;
                GoToCard.Visibility = Visibility.Collapsed;
            }

            // Viewpoints - rebuilt only when one was saved
            if (_session.ViewpointsVersion != _lastViewpointsVersion)
            {
                _lastViewpointsVersion = _session.ViewpointsVersion;
                ViewpointList.Children.Clear();
                foreach (WalkViewpoint vp in _session.Viewpoints)
                {
                    ViewpointList.Children.Add(new TextBlock
                    {
                        Text = (vp.Number <= 9 ? vp.Number + "   " : "     ") + vp.Name,
                        Foreground = B("TextSecondary"),
                        FontSize = 11.5,
                        Margin = new Thickness(0, 2, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }
                ViewpointCard.Visibility = _session.Viewpoints.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            // Measure (a finished one fades after 20 s; M again starts a new one)
            if (hud.MeasureShown && hud.MeasureDoneStampMs > 0 && DateTimeOffset.Now.ToUnixTimeMilliseconds() - hud.MeasureDoneStampMs > 20000)
                hud.MeasureShown = false;
            MeasureCard.Visibility = hud.MeasureShown ? Visibility.Visible : Visibility.Collapsed;
            if (hud.MeasureShown)
            {
                MeasureLine1.Text = hud.MeasureLine1;
                MeasureLine2.Text = hud.MeasureLine2;
            }

            // Door hint
            if (hud.DoorHint)
            {
                HintText.Text = _keys.Label(WalkKeyBindings.Door) + "   Go through the door";
                HintCard.Visibility = Visibility.Visible;
            }
            else
            {
                HintCard.Visibility = Visibility.Collapsed;
            }

            // Snags (Warning: needs attention), and what this walk hid
            string snags = hud.SnagCount > 0 ? "Snags " + hud.SnagCount : "";
            if (hud.HiddenCount > 0)
                snags += (snags.Length > 0 ? "   -   " : "") + "Hidden " + hud.HiddenCount;
            SnagText.Text = snags;
            SnagCard.Visibility = snags.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            HelpCard.Visibility = _helpVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildModeChips()
        {
            ModeChips.Children.Clear();
            string[] names = { "Inspect", "Select", "Snag", "Hide" };
            for (int i = 0; i < names.Length; i++)
            {
                var text = new TextBlock { Text = names[i], FontSize = 12, FontWeight = FontWeights.SemiBold };
                var chip = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0), Child = text };
                _chips[i] = chip;
                _chipTexts[i] = text;
                ModeChips.Children.Add(chip);
            }
            UpdateModeChips();
        }

        private void UpdateModeChips()
        {
            int active = (int)_session.Input.Pointer;
            for (int i = 0; i < _chips.Length; i++)
            {
                if (_chips[i] == null) continue;
                bool on = i == active;
                _chips[i].Background = B(on ? "Accent" : "AccentGray");
                _chipTexts[i].Foreground = B(on ? "TextOnAccent" : "TextSecondary");
            }
            ModeHint.Text = ClickHint(_session.Input.Pointer) + "   -   right-click or Tab to change";
        }

        private static string ClickHint(WalkPointerMode mode)
        {
            switch (mode)
            {
                case WalkPointerMode.Select: return "Click selects";
                case WalkPointerMode.Snag: return "Click records a snag";
                case WalkPointerMode.Hide: return "Click hides for now";
                default: return "Reads only";
            }
        }

        private void UpdateKeyHints()
        {
            KeyHints.Text = _keys.Label(WalkKeyBindings.Help) + " Help    " + _keys.Label(WalkKeyBindings.Measure) + " Measure    " +
                            _keys.Label(WalkKeyBindings.SaveViewpoint) + " Viewpoint    Esc Pause";
        }

        /// <summary>The help card: every key, in plain words, naming the keys actually bound (it follows Key Settings).</summary>
        private string BuildHelpText()
        {
            Func<string, string> k = _keys.Label;
            return string.Join("\n", new[]
            {
                "Walk\t" + k(WalkKeyBindings.MoveForward) + " " + k(WalkKeyBindings.StrafeLeft) + " " + k(WalkKeyBindings.MoveBack) + " " +
                    k(WalkKeyBindings.StrafeRight) + " or the arrow keys  -  look with the mouse  -  hold " + k(WalkKeyBindings.Run) + " to go faster",
                "Speed\t" + k(WalkKeyBindings.SpeedUp) + " / " + k(WalkKeyBindings.SpeedDown) + " or the mouse wheel (" + _session.Settings.SpeedStepCount + " steps)",
                "Low eye\t" + k(WalkKeyBindings.DownOrLowEye) + " on / off (" + _session.Settings.LowEyeHeightMm.ToString("N0") + " mm) - to look under ducts and into ceiling voids",
                "Fly\t" + k(WalkKeyBindings.Fly) + " on / off  -  hold " + k(WalkKeyBindings.Up) + " to rise, " + k(WalkKeyBindings.DownOrLowEye) + " to sink  -  walls and slabs still stop you",
                "Pass-through\t" + k(WalkKeyBindings.PassThrough) + " on / off  -  nothing stops you; rise and sink as in Fly",
                "Door\t" + k(WalkKeyBindings.Door) + " when the door hint shows",
                "Go to point\thold " + k(WalkKeyBindings.GoToPoint) + ", aim at a floor, let go",
                "Modes\tright-click or Tab (Shift+Tab back): Inspect, Select, Snag, Hide - left click acts",
                _session.Settings.AskSnagComment
                    ? "Snag\tin Snag mode, click: a picture is taken, then type a comment (Enter saves, Esc skips)"
                    : "Snag\tin Snag mode, click snag after snag (each gets a picture)  -  write the comments in the pause menu (Esc)",
                "Snag colours\t" + k(WalkKeyBindings.ClearSnags) + " gives every snagged element its own colours back (also in the pause menu)",
                "Show hidden\t" + k(WalkKeyBindings.ShowHidden) + " shows again what this walk hid",
                "Measure\t" + k(WalkKeyBindings.Measure) + " at point 1, " + k(WalkKeyBindings.Measure) + " at point 2 - distance in mm",
                "Viewpoints\t" + k(WalkKeyBindings.SaveViewpoint) + " saves and names one  -  1 to 9 go back  -  " + k(WalkKeyBindings.PlayViewpoints) + " plays them",
                "Snapshot\t" + k(WalkKeyBindings.Snapshot) + " saves a picture of the view, without the HUD",
                "Back to start\t" + k(WalkKeyBindings.BackToStart),
                "View\t" + k(WalkKeyBindings.ThirdPerson) + " first / third person (a character where you stand) - clicks act on the crosshair",
                "Pause\tEsc or " + k(WalkKeyBindings.Pause) + " - Revit is free to use; the pause menu has snags, viewpoints, settings",
                "Screen\t" + k(WalkKeyBindings.Help) + " this help  -  " + k(WalkKeyBindings.CleanScreen) + " clean screen  -  " +
                    k(WalkKeyBindings.FrameTiming) + " frame timing"
            });
        }
    }
}
