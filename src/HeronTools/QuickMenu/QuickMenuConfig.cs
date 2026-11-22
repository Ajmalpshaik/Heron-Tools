// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Ajmal's own wheel layout - how many slots, how big the wheel is, which tool sits in each slot, and its look (design, tool names, opening). Saved to %APPDATA%\HeronTools\quickmenu-slots.txt so it survives Revit restarts and add-in updates.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HeronTools.Utils;

namespace HeronTools.Services.QuickMenu
{
    /// <summary>The saved Quick Menu wheel layout.</summary>
    internal sealed class QuickMenuConfig
    {
        internal const int MinSlotCount = 4;
        internal const int MaxSlotCount = 12;
        internal const int DefaultSlotCount = 8;

        internal const int MinDiameter = 360;   // room for the Extra small (380) wheel
        internal const int MaxDiameter = 760;
        internal const int DefaultDiameter = 560;

        private const string FileName = "quickmenu-slots.txt";

        /// <summary>
        /// What a brand-new install starts with - the everyday tools, in reading order round the
        /// wheel from 12 o'clock. Any of these that is not on the ribbon is simply left blank.
        /// </summary>
        private static readonly string[] DefaultSlotKeys =
        {
            "HeronTools.Commands.CmdUnhideAll",
            "HeronTools.Commands.CmdToggleRevitLinks",
            "HeronTools.Commands.GraphicsTools.CmdHighlightSelection",
            "HeronTools.Commands.CmdSmartSelection",
            "HeronTools.Commands.CmdColorize",
            "HeronTools.Commands.CmdFilterPro",
            "HeronTools.Commands.CmdPinElements",
            "HeronTools.Commands.CmdMatchElevation"
        };

        private QuickMenuConfig()
        {
            SlotCount = DefaultSlotCount;
            Diameter = DefaultDiameter;
            Style = QuickMenuLook.DefaultStyle;
            ShowNames = true;
            Opening = QuickMenuLook.DefaultOpening;
            Colour = QuickMenuLook.DefaultColour;
            Effect = QuickMenuLook.DefaultEffect;
            Slots = new List<string>();
        }

        /// <summary>How many wedges the wheel is cut into (4 to 12).</summary>
        public int SlotCount { get; set; }

        /// <summary>Outer diameter of the wheel in WPF units (420 to 760).</summary>
        public int Diameter { get; set; }

        /// <summary>Which of the six wheel designs opens.</summary>
        public QuickMenuStyle Style { get; set; }

        /// <summary>True: tool names on the wheel. False: icons only, the name shown in the centre.</summary>
        public bool ShowNames { get; set; }

        /// <summary>Which of the four ways the wheel opens.</summary>
        public QuickMenuOpening Opening { get; set; }

        /// <summary>Dark or Light. A file saved before this setting existed reads as Dark.</summary>
        public QuickMenuColour Colour { get; set; }

        /// <summary>Flat, Soft or Clay. A file saved before this setting existed reads as Flat.</summary>
        public QuickMenuEffect Effect { get; set; }

        /// <summary>One command class name per slot, "" for an empty slot. Always SlotCount long.</summary>
        public List<string> Slots { get; private set; }

        /// <summary>
        /// True when a saved layout file exists but could not be read, so this is the DEFAULT layout
        /// standing in for it. Never save such a config back - it would overwrite the real layout.
        /// </summary>
        public bool LoadFailed { get; private set; }

        /// <summary>
        /// The full path a damaged layout file (empty, or not one line we know) was moved to, so this is
        /// the DEFAULT layout and it is safe to save. Null when nothing was set aside.
        /// </summary>
        public string SetAsidePath { get; private set; }

        /// <summary>Reads the saved layout, or hands back the defaults if there is nothing saved
        /// or the saved file cannot be read (LoadFailed tells the two apart).</summary>
        internal static QuickMenuConfig Load()
        {
            var config = new QuickMenuConfig();
            var readSlots = new Dictionary<int, string>();
            int recognised = 0;
            string path = AppDataConfigStore.GetPath(FileName);
            string[] fileLines = null;

            try
            {
                if (File.Exists(path))
                {
                    fileLines = File.ReadAllLines(path);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is System.Security.SecurityException)
            {
                // The file is there but could not be read (locked by another program, no access, ...).
                // Show the default layout and flag it, so the customise window does not save the
                // defaults over the real file, which is most likely fine.
                config.LoadFailed = true;
            }

            if (fileLines == null)
            {
                // No saved layout yet, or it could not be read (LoadFailed): the defaults.
                FillDefaultSlots(readSlots);
            }
            else
            {
                foreach (string rawLine in fileLines)
                {
                    string line = (rawLine ?? string.Empty).Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int split = line.IndexOf('=');
                    if (split <= 0)
                    {
                        continue;
                    }

                    string name = line.Substring(0, split).Trim();
                    string value = line.Substring(split + 1).Trim();

                    if (string.Equals(name, "SlotCount", StringComparison.OrdinalIgnoreCase))
                    {
                        recognised++;
                        config.SlotCount = ParseInt(value, DefaultSlotCount);
                    }
                    else if (string.Equals(name, "Diameter", StringComparison.OrdinalIgnoreCase))
                    {
                        recognised++;
                        config.Diameter = ParseInt(value, DefaultDiameter);
                    }
                    else if (string.Equals(name, "Style", StringComparison.OrdinalIgnoreCase))
                    {
                        recognised++;
                        config.Style = QuickMenuLook.ParseStyle(value);
                    }
                    else if (string.Equals(name, "Names", StringComparison.OrdinalIgnoreCase))
                    {
                        recognised++;
                        config.ShowNames = !string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (string.Equals(name, "Opening", StringComparison.OrdinalIgnoreCase))
                    {
                        recognised++;
                        config.Opening = QuickMenuLook.ParseOpening(value);
                    }
                    else if (string.Equals(name, "Colour", StringComparison.OrdinalIgnoreCase))
                    {
                        recognised++;
                        config.Colour = QuickMenuLook.ParseColour(value);
                    }
                    else if (string.Equals(name, "Effect", StringComparison.OrdinalIgnoreCase))
                    {
                        recognised++;
                        config.Effect = QuickMenuLook.ParseEffect(value);
                    }
                    else if (name.StartsWith("Slot", StringComparison.OrdinalIgnoreCase))
                    {
                        int slotNumber = ParseInt(name.Substring(4), 0);
                        if (slotNumber >= 1 && slotNumber <= MaxSlotCount)
                        {
                            recognised++;
                            readSlots[slotNumber] = value;
                        }
                    }
                }

                // A file with not one line we know (empty, garbled, or emptied by a crash) is not a
                // layout. Reading it again will never work, so set it aside as .bad (kept, not
                // deleted) and start from the default layout - which may then be saved.
                if (recognised == 0)
                {
                    config.SetAsidePath = SetAside(path);
                    readSlots.Clear();
                    FillDefaultSlots(readSlots);
                }
            }

            config.SlotCount = Clamp(config.SlotCount, MinSlotCount, MaxSlotCount);
            config.Diameter = Clamp(config.Diameter, MinDiameter, MaxDiameter);

            config.Slots = new List<string>(config.SlotCount);
            for (int i = 1; i <= config.SlotCount; i++)
            {
                string value;
                config.Slots.Add(readSlots.TryGetValue(i, out value) ? (value ?? string.Empty) : string.Empty);
            }

            return config;
        }

        /// <summary>
        /// Renames a damaged layout file to quickmenu-slots.txt.bad (replacing an older .bad) and returns the
        /// new path. If the rename fails, the damaged file stays where it is - it holds no layout, so a later
        /// Save may write over it - and the old path is returned.
        /// </summary>
        private static string SetAside(string path)
        {
            string badPath = path + ".bad";
            try
            {
                if (File.Exists(badPath))
                {
                    File.Delete(badPath);
                }

                File.Move(path, badPath);
                return badPath;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return path;
            }
        }

        private static void FillDefaultSlots(Dictionary<int, string> slots)
        {
            for (int i = 0; i < DefaultSlotKeys.Length; i++)
            {
                slots[i + 1] = DefaultSlotKeys[i];
            }
        }

        /// <summary>
        /// Writes the layout back. Best-effort - returns false if the file could not be written, with
        /// Windows' own reason in <paramref name="error"/> (null on success).
        /// </summary>
        internal bool Save(out string error)
        {
            error = null;
            try
            {
                Normalize();

                string path = AppDataConfigStore.GetPath(FileName);
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                var lines = new List<string>
                {
                    "# Heron Tools - Quick Menu layout. Slot 1 is at the top, then clockwise.",
                    "SlotCount=" + SlotCount.ToString(CultureInfo.InvariantCulture),
                    "Diameter=" + Diameter.ToString(CultureInfo.InvariantCulture),
                    "Style=" + Style,
                    "Names=" + (ShowNames ? "On" : "Off"),
                    "Opening=" + Opening,
                    "Colour=" + Colour,
                    "Effect=" + Effect
                };

                for (int i = 0; i < Slots.Count; i++)
                {
                    lines.Add("Slot" + (i + 1).ToString(CultureInfo.InvariantCulture) + "=" + Slots[i]);
                }

                // Write a temporary file first, then swap it in: a crash mid-write can never leave a
                // half-written layout behind.
                string temp = path + ".tmp";
                File.WriteAllLines(temp, lines.ToArray());
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }
                return true;
            }
            catch (Exception ex)
            {
                // Hand back the real reason (locked, read-only, no access, disk full ...) - not a guess.
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Forces SlotCount/Diameter into range and makes Slots exactly SlotCount long.</summary>
        internal void Normalize()
        {
            SlotCount = Clamp(SlotCount, MinSlotCount, MaxSlotCount);
            Diameter = Clamp(Diameter, MinDiameter, MaxDiameter);

            if (Slots == null)
            {
                Slots = new List<string>();
            }

            while (Slots.Count < SlotCount)
            {
                Slots.Add(string.Empty);
            }

            while (Slots.Count > SlotCount)
            {
                Slots.RemoveAt(Slots.Count - 1);
            }

            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i] == null)
                {
                    Slots[i] = string.Empty;
                }
            }
        }

        private static int ParseInt(string text, int fallback)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            return value > maximum ? maximum : value;
        }
    }
}
