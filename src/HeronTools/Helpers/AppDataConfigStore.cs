// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The one place Heron Tools keeps its settings and logs: %AppData%\HeronTools\<file>.

using System;
using System.IO;

namespace HeronTools.Utils
{
    internal static class AppDataConfigStore
    {
        private const string SettingsFolderName = "HeronTools";

        /// <summary>
        /// Returns the full path for a config file under %APPDATA%\HeronTools\.
        /// </summary>
        internal static string GetPath(string fileName)
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(root, SettingsFolderName, fileName);
        }
    }
}
