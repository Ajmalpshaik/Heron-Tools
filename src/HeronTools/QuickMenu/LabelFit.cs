// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Picks the font size a slot's tool name needs so every word fits the slot's width on at most two lines - a long name shrinks a little instead of being cut off with "...". Shared by the Quick Menu wheel and the settings window's drawing.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace HeronTools.UI.QuickMenu
{
    /// <summary>Fits a tool name into a slot by choosing its font size.</summary>
    internal static class LabelFit
    {
        /// <summary>
        /// The largest size from <paramref name="baseSize"/> down to <paramref name="minSize"/> (half-point steps)
        /// at which no word is wider than <paramref name="width"/> and the name wraps to at most
        /// <paramref name="maxLines"/> lines. If even the smallest size does not fit, the smallest is returned
        /// (the text block's own ellipsis is then the last resort).
        /// </summary>
        internal static double FontSizeFor(string text, double width, int maxLines, double baseSize, double minSize,
                                           Typeface face, double pixelsPerDip)
        {
            if (string.IsNullOrWhiteSpace(text) || width <= 0.0)
            {
                return baseSize;
            }

            string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (double size = baseSize; size >= minSize - 0.01; size -= 0.5)
            {
                double space = Measure(" ", size, face, pixelsPerDip);
                bool fits = true;
                int lines = 1;
                double lineWidth = 0.0;

                foreach (string word in words)
                {
                    double w = Measure(word, size, face, pixelsPerDip);
                    if (w > width)
                    {
                        fits = false;
                        break;
                    }

                    double next = lineWidth <= 0.0 ? w : lineWidth + space + w;
                    if (next > width)
                    {
                        lines++;
                        lineWidth = w;
                    }
                    else
                    {
                        lineWidth = next;
                    }
                }

                if (fits && lines <= maxLines)
                {
                    return size;
                }
            }

            return minSize;
        }

        private static double Measure(string text, double size, Typeface face, double pixelsPerDip)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                              face, size, Brushes.Black, pixelsPerDip);
            return formatted.WidthIncludingTrailingWhitespace;
        }
    }
}
