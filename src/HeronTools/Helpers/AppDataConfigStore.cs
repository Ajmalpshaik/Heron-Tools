// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The one place Heron Tools keeps its settings and logs: %AppData%\HeronTools\<file> (local AppData, then
// the temp folder, when a profile has no %AppData%).

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
            return Path.Combine(GetRoot(), SettingsFolderName, fileName);
        }

        /// <summary>
        /// %APPDATA%, or - when Windows hands back no such folder (a locked-down or broken user profile) -
        /// the local AppData folder, then the user's temp folder. An empty root would make a relative path,
        /// and the files would land in whatever folder Revit happens to be working in.
        /// </summary>
        private static string GetRoot()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = Path.GetTempPath();
            return root;
        }
    }
}
