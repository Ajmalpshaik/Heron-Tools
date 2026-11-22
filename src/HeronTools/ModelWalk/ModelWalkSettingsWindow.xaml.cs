// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - Settings window. Reads a copy of the walk's settings, checks every box on Save (a wrong value is
// named in the status line and nothing is applied), then hands the new settings back. Cancel, Esc or the close box
// change nothing. No Revit API here.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using HeronTools.Services.ModelWalk;
using HeronTools.Utils;

namespace HeronTools.UI.ModelWalk
{
    public partial class ModelWalkSettingsWindow : Window
    {
        private TextBox[] _speedBoxes;
        private readonly WalkKeyBindings _keys;

        internal ModelWalkSettingsWindow(WalkSettings current, WalkKeyBindings keys)
        {
            _keys = keys;
            InitializeComponent();
            Icon = BrandImages.Load(BrandImages.AppIcon);
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);

            _speedBoxes = new[] { Speed1Box, Speed2Box, Speed3Box, Speed4Box, Speed5Box };
            // The notes name the keys actually bound (they follow Key Settings).
            SpeedNote.Text = _keys.Label(WalkKeyBindings.SpeedUp) + " / " + _keys.Label(WalkKeyBindings.SpeedDown) +
                             " or the mouse wheel change the step during the walk. Fly and Pass-through are twice as fast; holding " +
                             _keys.Label(WalkKeyBindings.Run) + " is three times.";
            SnagColoursNote.Text = "On (standard): the red stays in the walk view after the walk, for your meeting. Off: every snagged " +
                                   "element gets its own colours back when the walk ends. Either way, Clear snag colours (" +
                                   _keys.Label(WalkKeyBindings.ClearSnags) + ", or the pause menu) gives them back, and so does every " +
                                   "Save and Sync with Central - no snag colour is saved in the model.";
            Fill(current);
            EyeHeightBox.TextChanged += (s, e) => UpdateLowEyeNote();
        }

        /// <summary>The settings to use after Save; null when the window was cancelled.</summary>
        internal WalkSettings Result { get; private set; }

        /// <summary>The save problem, if the file could not be written (the settings still apply to this walk).</summary>
        internal string SaveProblem { get; private set; }

        private void Fill(WalkSettings s)
        {
            SensitivityBox.Text = Number(s.SensitivityPercent);
            InvertLookBox.IsChecked = s.InvertLook;
            ThirdPersonBox.IsChecked = s.ThirdPerson;
            EyeHeightBox.Text = Number(s.EyeHeightMm);
            StepUpBox.Text = Number(s.StepUpMm);
            JumpHeightBox.Text = Number(s.JumpHeightMm);
            for (int i = 0; i < _speedBoxes.Length; i++)
                _speedBoxes[i].Text = i < s.SpeedsMetresPerSec.Length ? s.SpeedsMetresPerSec[i].ToString("0.0##", CultureInfo.CurrentCulture) : "";
            KeepSnagColoursBox.IsChecked = s.KeepSnagColours;
            AskSnagCommentBox.IsChecked = s.AskSnagComment;
            UpdateLowEyeNote();
        }

        private static string Number(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

        private void UpdateLowEyeNote()
        {
            var probe = new WalkSettings();
            if (TryRead(EyeHeightBox.Text, out double eye) && WalkSettings.InRange(eye, WalkSettings.MinEyeHeightMm, WalkSettings.MaxEyeHeightMm))
                probe.EyeHeightMm = eye;
            LowEyeNote.Text = "Low eye (" + _keys.Label(WalkKeyBindings.DownOrLowEye) + " in Walk) is then " + probe.LowEyeHeightMm.ToString("N0", CultureInfo.CurrentCulture) +
                              " mm - for looking under ducts and into ceiling voids.";
        }

        /// <summary>Takes the modeller's own decimal mark, and a dot as well.</summary>
        private static bool TryRead(string text, out double value)
        {
            text = (text ?? "").Trim();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                   double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Reads every box. Returns the new settings, or null with the first problem in plain words.</summary>
        private WalkSettings Read(out string problem)
        {
            problem = null;
            var s = new WalkSettings();

            if (!TryRead(SensitivityBox.Text, out s.SensitivityPercent) ||
                !WalkSettings.InRange(s.SensitivityPercent, WalkSettings.MinSensitivityPercent, WalkSettings.MaxSensitivityPercent))
            {
                problem = "Mouse sensitivity is 25 to 250 %.";
                SensitivityBox.Focus();
                return null;
            }
            s.InvertLook = InvertLookBox.IsChecked == true;
            s.ThirdPerson = ThirdPersonBox.IsChecked == true;

            if (!TryRead(EyeHeightBox.Text, out s.EyeHeightMm) ||
                !WalkSettings.InRange(s.EyeHeightMm, WalkSettings.MinEyeHeightMm, WalkSettings.MaxEyeHeightMm))
            {
                problem = "Eye height is 1000 to 2000 mm.";
                EyeHeightBox.Focus();
                return null;
            }
            if (!TryRead(StepUpBox.Text, out s.StepUpMm) ||
                !WalkSettings.InRange(s.StepUpMm, WalkSettings.MinStepUpMm, WalkSettings.MaxStepUpMm))
            {
                problem = "Step-up height is 100 to 450 mm.";
                StepUpBox.Focus();
                return null;
            }
            if (!TryRead(JumpHeightBox.Text, out s.JumpHeightMm) ||
                !WalkSettings.InRange(s.JumpHeightMm, WalkSettings.MinJumpHeightMm, WalkSettings.MaxJumpHeightMm))
            {
                problem = "Jump height is 0 to 1000 mm (0 = jump off).";
                JumpHeightBox.Focus();
                return null;
            }

            var speeds = new double[_speedBoxes.Length];
            for (int i = 0; i < _speedBoxes.Length; i++)
            {
                if (!TryRead(_speedBoxes[i].Text, out speeds[i]))
                {
                    problem = "Speed " + (i + 1) + " is not a number.";
                    _speedBoxes[i].Focus();
                    return null;
                }
            }
            problem = WalkSettings.SpeedsProblem(speeds);
            if (problem != null)
            {
                Speed1Box.Focus();
                return null;
            }
            s.SpeedsMetresPerSec = speeds;
            s.KeepSnagColours = KeepSnagColoursBox.IsChecked == true;
            s.AskSnagComment = AskSnagCommentBox.IsChecked == true;
            return s;
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            Fill(WalkSettings.Standard());
            StatusText.Text = "Every setting is back to the standard one. Save keeps it.";
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            WalkSettings settings = Read(out string problem);
            if (settings == null)
            {
                StatusText.Text = problem + " Nothing was changed.";
                StatusText.SetResourceReference(TextBlock.ForegroundProperty, "AccentWarning");
                return;
            }
            Result = settings;
            SaveProblem = settings.Save();
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
