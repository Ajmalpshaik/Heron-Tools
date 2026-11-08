// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Code-behind for the View Crop diagnostics window. Displays per-view boundary detail and supports clipboard copy.

using System;
using System.Windows;
using HeronTools.Utils;

namespace HeronTools.UI.ViewCrop
{
    /// <summary>
    /// Interaction logic for ViewCropDiagnosticsWindow.xaml
    /// </summary>
    public partial class ViewCropDiagnosticsWindow : Window
    {
        public ViewCropDiagnosticsWindow(string diagnosticReport)
        {
            InitializeComponent();

            // Shared Heron Tools window entrance and exit (fade + short rise / sink). Cosmetic only.
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);

            // The Heron app icon on the title bar and taskbar.
            Icon = BrandImages.Load(BrandImages.AppIcon);
            DiagnosticsTextBox.Text = string.IsNullOrWhiteSpace(diagnosticReport)
                ? "No report was made: no view was cropped in this run."
                : diagnosticReport;
        }

        private void OnCopyReport(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(DiagnosticsTextBox.Text);
                NotificationText.Text = "Report copied. Paste it into an email or a message.";
                NotificationText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                NotificationText.Text = "Could not copy (another program may be using the clipboard). Try again. " + ex.Message;
                NotificationText.Visibility = Visibility.Visible;
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
