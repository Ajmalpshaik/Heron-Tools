// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Tool Name: AppData Config Store
// Description: Shared %APPDATA%\HeronTools\<file> path builder for the tool-specific config stores
//              (LinkWorksetSettings, SectionMarkVisibilityConfigStore, TagArrangeSettings,
//              ViewCropConfigStore), which previously each hand-rolled an identical GetConfigPath().
// Author: Ajmal P.S.
// Version: 1.0.0
// Last Updated: 2026-07-17
// Revit Version: 2020
// Dependencies: System, System.IO

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
