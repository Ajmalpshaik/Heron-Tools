// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Heron Tools - pictures built into the DLL (ribbon icons, the Heron app icon).

using System.IO;
using System.Windows.Media.Imaging;

namespace HeronTools.Utils
{
    /// <summary>
    /// Loads the images embedded from Resources\Icons (as "HeronTools.Icons.&lt;file&gt;").
    /// Read as a stream, so nothing depends on a pack:// address being registered inside Revit.
    /// A missing image returns null - a button or window without a picture, never a failure.
    /// </summary>
    internal static class BrandImages
    {
        /// <summary>The Heron app icon (orange tile, white heron) for window title bars.</summary>
        internal const string AppIcon = "HeronAppIcon.png";

        internal static BitmapImage Load(string fileName)
        {
            using (Stream stream = typeof(BrandImages).Assembly
                       .GetManifestResourceStream("HeronTools.Icons." + fileName))
            {
                if (stream == null)
                    return null;

                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
    }
}
