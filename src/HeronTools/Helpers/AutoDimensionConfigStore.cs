// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.3.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Reads and writes the Auto Dimension settings: %AppData%\HeronTools\AutoDimension.config, one key=value per line.

using System;
using System.Globalization;
using System.IO;
using HeronTools.Models.AutoDimension;

namespace HeronTools.Utils
{
    internal static class AutoDimensionConfigStore
    {
        private const string SettingsFileName = "AutoDimension.config";

        /// <summary>The saved settings, or the defaults when there is no file or it cannot be read. Never throws.</summary>
        internal static AutoDimensionSettings Load()
        {
            var settings = new AutoDimensionSettings();
            try
            {
                string path = AppDataConfigStore.GetPath(SettingsFileName);
                if (!File.Exists(path))
                    return settings;

                foreach (string line in File.ReadAllLines(path))
                {
                    string[] parts = (line ?? string.Empty).Split(new[] { '=' }, 2);
                    if (parts.Length != 2)
                        continue;
                    Apply(settings, parts[0].Trim().ToLowerInvariant(), parts[1].Trim());
                }
            }
            catch (Exception)
            {
                // An unreadable file gives the defaults; the next Save writes a good one.
                settings = new AutoDimensionSettings();
            }

            settings.Normalize();
            return settings;
        }

        /// <summary>Writes the file through a temporary file, so a crash never leaves half a file. Never throws.</summary>
        internal static bool Save(AutoDimensionSettings settings, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (settings == null)
            {
                errorMessage = "No settings to save.";
                return false;
            }

            try
            {
                settings.Normalize();
                string path = AppDataConfigStore.GetPath(SettingsFileName);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string temp = path + ".tmp";
                using (var writer = new StreamWriter(temp, false))
                {
                    writer.WriteLine("ChainRow=" + settings.ChainRow);
                    writer.WriteLine("OverallRow=" + settings.OverallRow);
                    writer.WriteLine("SkipOverallForTwo=" + settings.SkipOverallForTwo);
                    writer.WriteLine("Side=" + settings.Side);
                    writer.WriteLine("FirstGapMm=" + settings.FirstGapMm.ToString("0.###", CultureInfo.InvariantCulture));
                    writer.WriteLine("RowGapMm=" + settings.RowGapMm.ToString("0.###", CultureInfo.InvariantCulture));
                    writer.WriteLine("ChainTypeName=" + settings.ChainTypeName);
                    writer.WriteLine("OverallTypeName=" + settings.OverallTypeName);
                    writer.WriteLine("GridsFrom=" + settings.GridsFrom);
                    writer.WriteLine("LevelsFrom=" + settings.LevelsFrom);
                    writer.WriteLine("StoryLevelsOnly=" + settings.StoryLevelsOnly);
                    writer.WriteLine("LeaveOutNames=" + settings.LeaveOutNames);
                    writer.WriteLine("SkipExistingRows=" + settings.SkipExistingRows);
                    writer.WriteLine("ShowLeftOut=" + settings.ShowLeftOut);
                }

                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static void Apply(AutoDimensionSettings s, string key, string value)
        {
            switch (key)
            {
                case "chainrow": if (bool.TryParse(value, out bool chain)) s.ChainRow = chain; break;
                case "overallrow": if (bool.TryParse(value, out bool overall)) s.OverallRow = overall; break;
                case "skipoverallfortwo": if (bool.TryParse(value, out bool two)) s.SkipOverallForTwo = two; break;
                case "side": if (Enum.TryParse(value, true, out AutoDimensionSide side)) s.Side = side; break;
                case "firstgapmm": if (TryNumber(value, out double first)) s.FirstGapMm = first; break;
                case "rowgapmm": if (TryNumber(value, out double gap)) s.RowGapMm = gap; break;
                case "chaintypename": s.ChainTypeName = value; break;
                case "overalltypename": s.OverallTypeName = value; break;
                case "gridsfrom": if (Enum.TryParse(value, true, out DatumSourceScope grids)) s.GridsFrom = grids; break;
                case "levelsfrom": if (Enum.TryParse(value, true, out DatumSourceScope levels)) s.LevelsFrom = levels; break;
                case "storylevelsonly": if (bool.TryParse(value, out bool story)) s.StoryLevelsOnly = story; break;
                case "leaveoutnames": s.LeaveOutNames = value; break;
                case "skipexistingrows": if (bool.TryParse(value, out bool existing)) s.SkipExistingRows = existing; break;
                case "showleftout": if (bool.TryParse(value, out bool show)) s.ShowLeftOut = show; break;
            }
        }

        private static bool TryNumber(string value, out double number)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                   && !double.IsNaN(number) && !double.IsInfinity(number);
        }
    }
}
