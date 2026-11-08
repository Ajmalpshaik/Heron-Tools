// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Code-behind for the View Crop settings and run-scope dialog.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using HeronTools.Models.ViewCrop;
using HeronTools.Utils;

namespace HeronTools.UI.ViewCrop
{
    /// <summary>
    /// Interaction logic for ViewCropOptionsWindow.xaml
    /// </summary>
    public partial class ViewCropOptionsWindow : Window
    {
        private bool _isCustomPresetSelection = false;

        /// <summary>The annotation offset the window opened with - kept if annotation crop is off and the box is unreadable.</summary>
        private readonly double _openingAnnotationOffsetMm;

        internal ViewCropOptionsWindow(string toolTitle, ViewCropSettings initialSettings)
        {
            InitializeComponent();

            // Shared Heron Tools window entrance and exit (fade + short rise / sink). Cosmetic only.
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);

            // The Heron app icon on the title bar and taskbar.
            Icon = BrandImages.Load(BrandImages.AppIcon);

            Title = string.IsNullOrWhiteSpace(toolTitle)
                ? "Crop View by Elements - Heron Tools"
                : toolTitle + " - Heron Tools";

            // Populate presets dropdown
            PresetComboBox.Items.Add("Custom - type your own");
            PresetComboBox.Items.Add("Tight - 50 mm");
            PresetComboBox.Items.Add("Standard plan - 300 mm");
            PresetComboBox.Items.Add("Spacious plan - 1000 mm");
            PresetComboBox.Items.Add("Site / masterplan - 3000 mm");

            ViewCropSettings settings = (initialSettings ?? new ViewCropSettings()).Clone();
            MarginTextBox.Text = settings.MarginMm.ToString("0.###", CultureInfo.CurrentCulture);

            // Select matching preset if available
            SelectMatchingPreset(settings.MarginMm);

            ModeAllModelRadio.IsChecked = settings.ExtentSource == ViewCropExtentSource.AllModelElements;
            ModeVisibleRadio.IsChecked = settings.ExtentSource == ViewCropExtentSource.ActiveViewElements;

            IncludeLinksCheckBox.IsChecked = settings.IncludeRevitLinks;
            IncludeCoordinationModelsCheckBox.IsChecked = settings.IncludeCoordinationModels;
            IncludeDatumsCheckBox.IsChecked = settings.IncludeDatums;
            IgnoreHiddenCheckBox.IsChecked = settings.IgnoreHiddenCategories;

            // Annotation Integration
            ApplyAnnotationCheckBox.IsChecked = settings.ApplyAnnotationCrop;
            _openingAnnotationOffsetMm = settings.AnnotationOffsetMm;
            AnnotationOffsetTextBox.Text = settings.AnnotationOffsetMm.ToString("0.###", CultureInfo.CurrentCulture);
            UpdateAnnotationControlsState();

            SelectedSettings = settings;
            UpdateRunButtonText();
        }

        internal bool ApplyToActiveViewOnly => ActiveViewOnlyRadio.IsChecked == true;

        internal ViewCropSettings SelectedSettings { get; private set; }

        private void OnApplyScopeChanged(object sender, RoutedEventArgs e)
        {
            UpdateRunButtonText();
        }

        private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MarginTextBox == null) return;

            _isCustomPresetSelection = true;
            try
            {
                int index = PresetComboBox.SelectedIndex;
                switch (index)
                {
                    case 1: // Tight
                        MarginTextBox.Text = "50";
                        break;
                    case 2: // Standard
                        MarginTextBox.Text = "300";
                        break;
                    case 3: // Spacious
                        MarginTextBox.Text = "1000";
                        break;
                    case 4: // Site
                        MarginTextBox.Text = "3000";
                        break;
                }
            }
            finally
            {
                _isCustomPresetSelection = false;
            }
        }

        private void OnMarginTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isCustomPresetSelection) return;

            string text = MarginTextBox.Text;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double margin))
            {
                SelectMatchingPreset(margin);
            }
            else
            {
                PresetComboBox.SelectedIndex = 0; // Custom
            }
        }

        private void SelectMatchingPreset(double margin)
        {
            if (Math.Abs(margin - 50) < 0.001)
                PresetComboBox.SelectedIndex = 1;
            else if (Math.Abs(margin - 300) < 0.001)
                PresetComboBox.SelectedIndex = 2;
            else if (Math.Abs(margin - 1000) < 0.001)
                PresetComboBox.SelectedIndex = 3;
            else if (Math.Abs(margin - 3000) < 0.001)
                PresetComboBox.SelectedIndex = 4;
            else
                PresetComboBox.SelectedIndex = 0; // Custom
        }

        private void OnApplyAnnotationToggled(object sender, RoutedEventArgs e)
        {
            UpdateAnnotationControlsState();
        }

        private void UpdateAnnotationControlsState()
        {
            if (AnnotationOffsetLabel == null || AnnotationOffsetTextBox == null || ApplyAnnotationCheckBox == null)
                return;

            bool enable = ApplyAnnotationCheckBox.IsChecked == true;
            AnnotationOffsetLabel.IsEnabled = enable;
            AnnotationOffsetTextBox.IsEnabled = enable;
        }

        private bool ValidateAndBuildSettings(bool showDiagnostics)
        {
            ErrorText.Text = string.Empty;
            ErrorText.Visibility = Visibility.Collapsed;

            string marginText = (MarginTextBox.Text ?? string.Empty).Trim();
            if (!double.TryParse(marginText, NumberStyles.Float, CultureInfo.CurrentCulture, out double marginMm))
            {
                ErrorText.Text = "The margin must be a number in millimetres, for example 300.";
                ErrorText.Visibility = Visibility.Visible;
                return false;
            }

            // "NaN" and "Infinity" parse as numbers; the same limits as the saved settings file.
            if (double.IsNaN(marginMm) || double.IsInfinity(marginMm) || marginMm > 100000)
            {
                ErrorText.Text = "The margin must be a number from 0 to 100000 mm.";
                ErrorText.Visibility = Visibility.Visible;
                return false;
            }

            if (marginMm < 0)
            {
                ErrorText.Text = "The margin cannot be below 0 mm. Type 0 or more.";
                ErrorText.Visibility = Visibility.Visible;
                return false;
            }

            // Always read the box: forcing a fixed value while annotation crop is off saved it over the
            // user's own offset, so the next time they ticked the box their value was gone.
            string offsetText = (AnnotationOffsetTextBox.Text ?? string.Empty).Trim();
            bool offsetParsed = double.TryParse(offsetText, NumberStyles.Float, CultureInfo.CurrentCulture, out double annotationOffset);
            if (ApplyAnnotationCheckBox.IsChecked == true)
            {
                if (!offsetParsed)
                {
                    ErrorText.Text = "The annotation offset is a distance on the printed sheet, in millimetres - for example 25.";
                    ErrorText.Visibility = Visibility.Visible;
                    return false;
                }

                if (double.IsNaN(annotationOffset) || double.IsInfinity(annotationOffset) || annotationOffset > 1000)
                {
                    ErrorText.Text = "The annotation offset must be a number from 0 to 1000 mm on the sheet.";
                    ErrorText.Visibility = Visibility.Visible;
                    return false;
                }

                if (annotationOffset < 0)
                {
                    ErrorText.Text = "The annotation offset cannot be below 0 mm. Type 0 or more.";
                    ErrorText.Visibility = Visibility.Visible;
                    return false;
                }
            }
            else if (!offsetParsed || annotationOffset < 0 || double.IsNaN(annotationOffset) || double.IsInfinity(annotationOffset) || annotationOffset > 1000)
            {
                // The box is disabled while annotation crop is off, so the user cannot correct it here.
                // Keep the offset the window opened with rather than refuse to run over an unused field.
                annotationOffset = _openingAnnotationOffsetMm;
            }

            SelectedSettings = new ViewCropSettings
            {
                MarginMm = marginMm,
                IncludeRevitLinks = IncludeLinksCheckBox.IsChecked == true,
                IgnoreHiddenCategories = IgnoreHiddenCheckBox.IsChecked == true,
                RectangularCropOnly = false,   // shaped crops are left as they are (skipped, with the reason)
                IncludeDatums = IncludeDatumsCheckBox.IsChecked == true,
                ApplyAnnotationCrop = ApplyAnnotationCheckBox.IsChecked == true,
                AnnotationOffsetMm = annotationOffset,
                ShowDiagnostics = showDiagnostics,
                IncludeCoordinationModels = IncludeCoordinationModelsCheckBox.IsChecked == true,
                ExtentSource = ModeVisibleRadio.IsChecked == true
                    ? ViewCropExtentSource.ActiveViewElements
                    : ViewCropExtentSource.AllModelElements
            };

            return true;
        }

        private void OnRun(object sender, RoutedEventArgs e)
        {
            if (ValidateAndBuildSettings(false))
            {
                DialogResult = true;
                Close();
            }
        }

        private void OnDiagnosticsRun(object sender, RoutedEventArgs e)
        {
            if (ValidateAndBuildSettings(true))
            {
                DialogResult = true;
                Close();
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void UpdateRunButtonText()
        {
            if (RunButton == null || ActiveViewOnlyRadio == null)
                return;

            RunButton.Content = ActiveViewOnlyRadio.IsChecked == true ? "Crop view" : "Next: pick views";
        }
    }
}
