// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Element ID window: shows the picked element's ID and the model it belongs to, with a Copy ID button.
// Reads only; changes nothing in the model.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HeronTools.Utils;

namespace HeronTools.UI
{
    public partial class LinkedIdViewerWindow : Window
    {
        private readonly string _defaultInfoText;

        public LinkedIdViewerWindow(string elementId, string modelSource)
        {
            InitializeComponent();
            Icon = BrandImages.Load(BrandImages.AppIcon);
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);

            ElementIdBox.Text = elementId ?? string.Empty;
            ModelSourceBox.Text = modelSource ?? "This model";
            _defaultInfoText = InfoText.Text;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            ApplyTextWidthSizing();
        }

        private void OnCopyId(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ElementIdBox.Text))
                {
                    InfoText.Text = _defaultInfoText;
                    InfoText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                    return;
                }

                Clipboard.SetText(ElementIdBox.Text);
                InfoText.Text = "Copied " + ElementIdBox.Text + " to the clipboard.";
                InfoText.SetResourceReference(TextBlock.ForegroundProperty, "AccentSuccess");
            }
            catch (Exception ex)
            {
                // Keep the window open, but say the copy did not happen - otherwise the user pastes
                // whatever was on the clipboard before. Usually another program holding the clipboard.
                InfoText.Text = "Could not copy to the clipboard: " + ex.Message +
                                " Try Copy again, or select the ID above and press Ctrl+C.";
                InfoText.SetResourceReference(TextBlock.ForegroundProperty, "AccentWarning");
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ApplyTextWidthSizing()
        {
            double targetWidth = Math.Max(
                MeasureTextWidth(ElementIdBox),
                MeasureTextWidth(ModelSourceBox));

            double minWidth = Math.Max(280, Math.Min(targetWidth, 640));
            ElementIdBox.MinWidth = minWidth;
            ModelSourceBox.MinWidth = minWidth;
        }

        private double MeasureTextWidth(TextBox box)
        {
            string text = box.Text ?? string.Empty;
            var typeface = new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch);
            var dpi = VisualTreeHelper.GetDpi(this);

            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                box.FontSize,
                Brushes.Black,
                dpi.PixelsPerDip);

            double chrome = box.Padding.Left + box.Padding.Right +
                            box.BorderThickness.Left + box.BorderThickness.Right;

            return formatted.Width + chrome;
        }
    }
}
