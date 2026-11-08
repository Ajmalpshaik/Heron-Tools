// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Code-behind for the annotation-crop settings and run-scope dialog.

using System.Globalization;
using System.Windows;
using HeronTools.Models.ViewCrop;
using HeronTools.Utils;

namespace HeronTools.UI.ViewCrop
{
    /// <summary>
    /// Interaction logic for ViewCropAnnotationOptionsWindow.xaml
    /// </summary>
    public partial class ViewCropAnnotationOptionsWindow : Window
    {
        internal ViewCropAnnotationOptionsWindow(string toolTitle, ViewCropAnnotationSettings initialSettings)
        {
            InitializeComponent();

            // Shared Heron Tools window entrance and exit (fade + short rise / sink). Cosmetic only.
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);

            // The Heron app icon on the title bar and taskbar.
            Icon = BrandImages.Load(BrandImages.AppIcon);

            Title = string.IsNullOrWhiteSpace(toolTitle)
                ? "Set Annotation Crop - Heron Tools"
                : toolTitle + " - Heron Tools";

            ViewCropAnnotationSettings settings = (initialSettings ?? new ViewCropAnnotationSettings()).Clone();
            OffsetTextBox.Text = settings.OffsetMm.ToString("0.###", CultureInfo.CurrentCulture);

            // The views choice used last time ("Active view" is the XAML default).
            if (!settings.ApplyToActiveViewOnly)
                MultipleViewsRadio.IsChecked = true;

            SelectedSettings = settings;
            UpdateRunButtonText();
        }

        internal bool ApplyToActiveViewOnly => ActiveViewOnlyRadio.IsChecked == true;

        internal ViewCropAnnotationSettings SelectedSettings { get; private set; }

        private void OnApplyScopeChanged(object sender, RoutedEventArgs e)
        {
            UpdateRunButtonText();
        }

        private void OnRun(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;
            ErrorText.Visibility = Visibility.Collapsed;

            string offsetText = (OffsetTextBox.Text ?? string.Empty).Trim();
            if (!double.TryParse(offsetText, NumberStyles.Float, CultureInfo.CurrentCulture, out double offsetMm))
            {
                ShowError("The offset is a distance on the printed sheet, in millimetres - for example 25 (Revit's smallest is 3.2).");
                return;
            }

            if (double.IsNaN(offsetMm) || double.IsInfinity(offsetMm) || offsetMm > 1000)
            {
                ShowError("The offset must be a number from 0 to 1000 mm on the sheet.");
                return;
            }

            if (offsetMm < 0)
            {
                ShowError("The offset cannot be below 0 mm. Type 0 or more.");
                return;
            }

            SelectedSettings = new ViewCropAnnotationSettings
            {
                OffsetMm = offsetMm,
                ApplyToActiveViewOnly = ApplyToActiveViewOnly
            };

            DialogResult = true;
            Close();
        }

        private void ShowError(string text)
        {
            ErrorText.Text = text;
            ErrorText.Visibility = Visibility.Visible;
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

            RunButton.Content = ActiveViewOnlyRadio.IsChecked == true ? "Set annotation crop" : "Next: pick views";
        }
    }
}
