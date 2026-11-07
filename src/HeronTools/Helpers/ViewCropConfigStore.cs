// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Persists View Crop and integrated annotation settings on disk.

using System;
using System.Globalization;
using System.IO;
using HeronTools.Models.ViewCrop;

namespace HeronTools.Utils
{
    /// <summary>
    /// Handles persistent disk-based storage and retrieval of View Crop and integrated Annotation settings.
    /// </summary>
    internal static class ViewCropConfigStore
    {
        private const string SettingsFileName = "ViewCrop.config";

        internal static ViewCropSettings Load()
        {
            var settings = new ViewCropSettings();
            try
            {
                string path = GetConfigPath();
                if (!File.Exists(path))
                    return settings;

                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    string[] parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length != 2)
                        continue;

                    string key = parts[0].Trim().ToLowerInvariant();
                    string val = parts[1].Trim();

                    switch (key)
                    {
                        case "marginmm":
                            // A damaged file must not give a NaN, infinite or negative margin (the crop maths would fail).
                            if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double margin) &&
                                !double.IsNaN(margin) && !double.IsInfinity(margin) && margin >= 0 && margin <= 100000)
                                settings.MarginMm = margin;
                            break;
                        case "includerevitlinks":
                            if (bool.TryParse(val, out bool links))
                                settings.IncludeRevitLinks = links;
                            break;
                        case "ignorehiddencategories":
                            if (bool.TryParse(val, out bool ignoreHidden))
                                settings.IgnoreHiddenCategories = ignoreHidden;
                            break;
                        case "rectangularcroponly":
                            if (bool.TryParse(val, out bool rectOnly))
                                settings.RectangularCropOnly = rectOnly;
                            break;
                        case "includedatums":
                            if (bool.TryParse(val, out bool includeDatums))
                                settings.IncludeDatums = includeDatums;
                            break;
                        case "applyannotationcrop":
                            if (bool.TryParse(val, out bool applyAnnotation))
                                settings.ApplyAnnotationCrop = applyAnnotation;
                            break;
                        case "annotationoffsetmm":
                            if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double offset) &&
                                !double.IsNaN(offset) && !double.IsInfinity(offset) && offset >= 0 && offset <= 1000)
                                settings.AnnotationOffsetMm = offset;
                            break;
                        case "showdiagnostics":
                            if (bool.TryParse(val, out bool showDiag))
                                settings.ShowDiagnostics = showDiag;
                            break;
                        case "includecoordinationmodels":
                            if (bool.TryParse(val, out bool includeCoord))
                                settings.IncludeCoordinationModels = includeCoord;
                            break;
                        case "extentsource":
                            // "Fit the crop around" - saved so the choice survives a restart.
                            if (Enum.TryParse(val, true, out HeronTools.Models.ViewCrop.ViewCropExtentSource source) &&
                                Enum.IsDefined(typeof(HeronTools.Models.ViewCrop.ViewCropExtentSource), source))
                                settings.ExtentSource = source;
                            break;
                    }
                }
            }
            catch
            {
                // Return default settings if reading fails.
            }
            return settings;
        }

        /// <summary>
        /// Writes the settings file. Never throws.
        /// </summary>
        /// <returns>False when the file could not be written; <paramref name="errorMessage"/> says why.</returns>
        internal static bool Save(ViewCropSettings settings, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (settings == null)
            {
                errorMessage = "No settings to save.";
                return false;
            }

            try
            {
                string path = GetConfigPath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (var writer = new StreamWriter(path, false))
                {
                    writer.WriteLine($"MarginMm={settings.MarginMm.ToString("0.###", CultureInfo.InvariantCulture)}");
                    writer.WriteLine($"IncludeRevitLinks={settings.IncludeRevitLinks}");
                    writer.WriteLine($"IgnoreHiddenCategories={settings.IgnoreHiddenCategories}");
                    writer.WriteLine($"RectangularCropOnly={settings.RectangularCropOnly}");
                    writer.WriteLine($"IncludeDatums={settings.IncludeDatums}");
                    writer.WriteLine($"ApplyAnnotationCrop={settings.ApplyAnnotationCrop}");
                    writer.WriteLine($"AnnotationOffsetMm={settings.AnnotationOffsetMm.ToString("0.###", CultureInfo.InvariantCulture)}");
                    writer.WriteLine($"ShowDiagnostics={settings.ShowDiagnostics}");
                    writer.WriteLine($"IncludeCoordinationModels={settings.IncludeCoordinationModels}");
                    writer.WriteLine($"ExtentSource={settings.ExtentSource}");
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static string GetConfigPath() => AppDataConfigStore.GetPath(SettingsFileName);
    }
}
