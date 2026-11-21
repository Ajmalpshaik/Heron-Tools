// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Reads brand brushes and colours from UI/HeronStyles.xaml for code that paints by hand (the Quick Menu
// wheels), so no colour is typed into C# and HeronStyles.xaml stays the one place the palette lives.

using System;
using System.Windows;
using System.Windows.Media;

namespace HeronTools.UI
{
    /// <summary>Brand brushes and colours from HeronStyles.xaml, looked up by key.</summary>
    internal static class HeronPalette
    {
        // Same theme every window merges; loaded once, on first use.
        private static readonly Lazy<ResourceDictionary> Theme = new Lazy<ResourceDictionary>(() =>
            (ResourceDictionary)Application.LoadComponent(
                new Uri("/HeronTools;component/UI/HeronStyles.xaml", UriKind.Relative)));

        /// <summary>A SolidColorBrush from the theme, frozen so it can be shared.</summary>
        public static Brush Brush(string key)
        {
            var brush = (Brush)Find(key);
            return brush.IsFrozen ? brush : (Brush)brush.GetAsFrozen();
        }

        /// <summary>A raw Color from the theme (for gradient stops).</summary>
        public static Color Colour(string key)
        {
            return (Color)Find(key);
        }

        private static object Find(string key)
        {
            object value = Theme.Value[key];
            if (value == null)
                throw new InvalidOperationException("Heron Tools theme has no '" + key + "' entry in UI/HeronStyles.xaml.");
            return value;
        }
    }
}
