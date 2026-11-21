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
            FrameText.Visibility = _showFrameTiming ? Visibility.Visible : Visibility.Collapsed;
            if (_showFrameTiming)
                FrameText.Text = string.Format("Frame {0:0} ms (average {1:0} ms) - {2} rays", hud.LastFrameMs, hud.AverageFrameMs, hud.LastFrameRays);

            // Messages
            long age = DateTimeOffset.Now.ToUnixTimeMilliseconds() - hud.ToastStampMs;
            if (hud.ToastStampMs > 0 && age < 2600 && !string.IsNullOrEmpty(hud.ToastText))
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
        }

        private void UpdateKeyHints()
        {
            KeyHints.Text = _keys.Label(WalkKeyBindings.Help) + " Help    " + _keys.Label(WalkKeyBindings.CleanScreen) + " Clean screen    Esc Pause";
        }

        /// <summary>The help card, naming the keys actually bound (it follows Key Settings).</summary>
        private string BuildHelpText()
        {
            Func<string, string> k = _keys.Label;
            return string.Join("\n", new[]
            {
                "Walk\t" + k(WalkKeyBindings.MoveForward) + " " + k(WalkKeyBindings.StrafeLeft) + " " + k(WalkKeyBindings.MoveBack) + " " +
                    k(WalkKeyBindings.StrafeRight) + " or the arrows  -  look with the mouse  -  run: hold " + k(WalkKeyBindings.Run),
                "Speed\t" + k(WalkKeyBindings.SpeedUp) + " / " + k(WalkKeyBindings.SpeedDown) + " or the mouse wheel (5 steps)",
                "Eye\t" + k(WalkKeyBindings.DownOrLowEye) + " low eye on / off (" + WalkTuning.LowEyeHeightMm.ToString("N0") + " mm), for ceiling voids",
                "Fly\t" + k(WalkKeyBindings.Fly) + " on / off  -  " + k(WalkKeyBindings.Up) + " up, " + k(WalkKeyBindings.DownOrLowEye) + " down",
                "Pass-through\t" + k(WalkKeyBindings.PassThrough) + " on / off - nothing blocks you",
                "Door\t" + k(WalkKeyBindings.Door) + " when the door hint shows",
                "Go to point\thold " + k(WalkKeyBindings.GoToPoint) + ", aim, release",
                "Modes\tright-click or Tab: Inspect, Select, Snag, Hide - left click acts",
                "Show hidden\t" + k(WalkKeyBindings.ShowHidden) + " brings back what this walk hid",
                "Snag colours\t" + k(WalkKeyBindings.ClearSnags) + " clears them now (they also come back at the end)",
                "Viewpoints\t" + k(WalkKeyBindings.SaveViewpoint) + " saves  -  1..9 goes back  -  " + k(WalkKeyBindings.PlayViewpoints) + " plays them",
                "Snapshot\t" + k(WalkKeyBindings.Snapshot) + " saves a picture of the view (no HUD)",
                "Back to start\t" + k(WalkKeyBindings.BackToStart),
                "Pause\tEsc or " + k(WalkKeyBindings.Pause) + " - Revit is free to use  -  paused: S = keys, Esc = end",
                "Help\t" + k(WalkKeyBindings.Help) + "  -  clean screen " + k(WalkKeyBindings.CleanScreen)
            });
        }
    }
}
