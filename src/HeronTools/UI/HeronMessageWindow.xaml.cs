// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The Heron message window. Never opened directly: DialogHelper builds it, owns it to whatever Revit or
// Heron window is in front, and falls back to Revit's TaskDialog if it cannot be shown. It changes nothing
// in the model; it only shows text and, for a question, returns Yes or No.

using System;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using HeronTools.Utils;

namespace HeronTools.UI
{
    /// <summary>What a message is: sets the glyph, its colour, the line under the title and the sound.</summary>
    internal enum MessageKind
    {
        Info,
        Warning,
        Error,
        Question
    }

    public partial class HeronMessageWindow : Window
    {
        private readonly string _copyText;

        /// <summary>True only when the first (orange) button was pressed. Esc, the X and No give false.</summary>
        internal bool Accepted { get; private set; }

        internal HeronMessageWindow(
            MessageKind kind,
            string toolTitle,
            string heading,
            string message,
            string details,
            string primaryText,
            string secondText)
        {
            InitializeComponent();
            Icon = BrandImages.Load(BrandImages.AppIcon);
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);

            string tool = string.IsNullOrWhiteSpace(toolTitle) ? "Heron Tools" : toolTitle.Trim();
            Title = tool == "Heron Tools" ? tool : tool + " - Heron Tools";
            ToolText.Text = tool;
            KindText.Text = KindLine(kind);
            ApplyGlyph(kind);

            if (!string.IsNullOrWhiteSpace(heading))
            {
                HeadingText.Text = heading.Trim();
                HeadingText.Visibility = Visibility.Visible;
                MessageText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            }
            MessageText.Text = (message ?? "").Trim();
            if (MessageText.Text.Length == 0)
                MessageText.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrWhiteSpace(details))
            {
                DetailsText.Text = details.Trim();
                DetailsButton.Visibility = Visibility.Visible;
                DetailsButton.Click += (s, e) => ToggleDetails();
            }

            PrimaryButton.Content = string.IsNullOrWhiteSpace(primaryText) ? "Close" : primaryText;
            if (string.IsNullOrWhiteSpace(secondText))
            {
                // One button: it both accepts and cancels, so Enter and Esc both close.
                PrimaryButton.IsCancel = true;
            }
            else
            {
                SecondButton.Content = secondText;
                SecondButton.Visibility = Visibility.Visible;
                SecondButton.IsCancel = true;
                SecondButton.Click += (s, e) => Close();
            }
            PrimaryButton.Click += (s, e) => { Accepted = true; Close(); };

            _copyText = tool + Environment.NewLine + Environment.NewLine
                        + (string.IsNullOrWhiteSpace(heading) ? "" : heading.Trim() + Environment.NewLine + Environment.NewLine)
                        + MessageText.Text
                        + (string.IsNullOrWhiteSpace(details) ? "" : Environment.NewLine + Environment.NewLine + details.Trim());
            CopyButton.Click += (s, e) => CopyToClipboard();

            Loaded += (s, e) =>
            {
                PrimaryButton.Focus();
                PlaySound(kind);
            };
        }

        private static string KindLine(MessageKind kind)
        {
            switch (kind)
            {
                case MessageKind.Error: return "Could not finish";
                case MessageKind.Warning: return "Please check";
                case MessageKind.Question: return "Your choice";
                default: return "For your information";
            }
        }

        private void ApplyGlyph(MessageKind kind)
        {
            string glyph, brush;
            switch (kind)
            {
                case MessageKind.Error: glyph = "GlyphError"; brush = "AccentDanger"; break;
                case MessageKind.Warning: glyph = "GlyphWarning"; brush = "AccentWarning"; break;
                case MessageKind.Question: glyph = "GlyphQuestion"; brush = "Accent"; break;
                default: glyph = "GlyphInfo"; brush = "TextPrimary"; break;
            }
            GlyphPath.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brush);
            GlyphPath.SetResourceReference(System.Windows.Shapes.Path.DataProperty, glyph);
        }

        private void ToggleDetails()
        {
            bool show = DetailsPanel.Visibility != Visibility.Visible;
            DetailsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            DetailsButton.Content = show ? "Hide details" : "Show details";
        }

        private void CopyToClipboard()
        {
            try
            {
                Clipboard.SetText(_copyText);
                CopyButton.Content = "Copied";
            }
            catch (Exception)
            {
                // The clipboard can be held by another program for a moment; say so instead of failing.
                CopyButton.Content = "Clipboard busy - try again";
            }
        }

        private static void PlaySound(MessageKind kind)
        {
            try
            {
                // Same sounds Windows plays for its own error and warning boxes; notes and questions stay quiet.
                if (kind == MessageKind.Error)
                    SystemSounds.Hand.Play();
                else if (kind == MessageKind.Warning)
                    SystemSounds.Exclamation.Play();
            }
            catch (Exception)
            {
                // Sound is a courtesy only.
            }
        }
    }
}
