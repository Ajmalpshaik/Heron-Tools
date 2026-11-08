// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Builds the list of everything the Quick Menu can put in a slot. Two sources: (1) the LIVE Heron ribbon - every push button on it, and (2) Revit's own built-in commands. Nothing is hard-coded either side: add a new button to a ribbon manager and it appears by itself, with its real label, tooltip and icon; Revit's own list comes straight from the running Revit version.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Autodesk.Revit.UI;

namespace HeronTools.Services.QuickMenu
{
    /// <summary>Every Heron Tools ribbon button plus every Revit built-in command, ready for the wheel.</summary>
    internal static class QuickMenuCatalog
    {
        /// <summary>The Heron ribbon tab the Quick Menu offers tools from.</summary>
        // The shared "Heron" tab. Only Heron Tools' OWN buttons are offered (see Add): the tab is shared with
        // Heron AI, and this wheel must never launch another add-in's buttons.
        private static readonly string[] TabNames = { "Heron" };
        private const string OwnCommandPrefix = "HeronTools.";

        /// <summary>Command classes that must never appear as a slot (they open the wheel itself).</summary>
        private static readonly string[] ExcludedClassNames =
        {
            "HeronTools.Commands.QuickMenu.CmdQuickMenu",
            "HeronTools.Commands.QuickMenu.CmdQuickMenuSettings"
        };

        private static List<QuickToolEntry> _entries;

        /// <summary>
        /// getRibbonItem() is looked up by reflection once per ribbon-item type, not once per
        /// button - the lookup is the expensive half and every PushButton shares one type.
        /// </summary>
        private static readonly Dictionary<Type, MethodInfo> _getRibbonItemCache =
            new Dictionary<Type, MethodInfo>();

        /// <summary>
        /// All launchable Heron Tools buttons, built once per session and cached.
        /// </summary>
        internal static IList<QuickToolEntry> GetEntries(UIApplication uiapp)
        {
            if (_entries == null)
            {
                _entries = Build(uiapp);
            }

            return _entries;
        }

        /// <summary>Rebuilds the list from the ribbon as it stands right now.</summary>
        internal static IList<QuickToolEntry> Refresh(UIApplication uiapp)
        {
            _entries = Build(uiapp);
            return _entries;
        }

        /// <summary>Finds one entry by its saved key (command class name), or null.</summary>
        internal static QuickToolEntry Find(UIApplication uiapp, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            foreach (QuickToolEntry entry in GetEntries(uiapp))
            {
                if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private static List<QuickToolEntry> Build(UIApplication uiapp)
        {
            var entries = new List<QuickToolEntry>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (uiapp == null)
            {
                return entries;
            }

            foreach (string tabName in TabNames)
            {
                IList<RibbonPanel> panels;
                try
                {
                    panels = uiapp.GetRibbonPanels(tabName);
                }
                catch (Exception)
                {
                    // Tab not present in this session - skip it rather than fail the whole catalog.
                    continue;
                }

                if (panels == null)
                {
                    continue;
                }

                foreach (RibbonPanel panel in panels)
                {
                    AddPanel(entries, seenKeys, tabName, panel);
                }
            }

            AddRevitCommands(entries, seenKeys);

            return entries;
        }

        /// <summary>
        /// Adds every built-in command the running Revit version publishes. See the Notes block:
        /// the enum is walked by name at run time so one source file builds on 2020 through 2027.
        /// </summary>
        private static void AddRevitCommands(List<QuickToolEntry> entries, HashSet<string> seenKeys)
        {
            string[] commandNames;
            try
            {
                commandNames = Enum.GetNames(typeof(PostableCommand));
            }
            catch (Exception)
            {
                // No built-in list available - the Heron Tools buttons on their own are still usable.
                return;
            }

            if (commandNames == null)
            {
                return;
            }

            foreach (string commandName in commandNames)
            {
                if (string.IsNullOrEmpty(commandName))
                {
                    continue;
                }

                if (!seenKeys.Add(QuickToolEntry.RevitKeyPrefix + commandName))
                {
                    continue;
                }

                PostableCommand commandValue;
                try
                {
                    commandValue = (PostableCommand)Enum.Parse(typeof(PostableCommand), commandName);
                }
                catch (Exception)
                {
                    continue;
                }

                entries.Add(QuickToolEntry.ForRevitCommand(
                    commandName,
                    SplitIntoWords(commandName),
                    commandValue));
            }
        }

        /// <summary>"WallArchitectural" -> "Wall Architectural", so the list reads like English.</summary>
        private static string SplitIntoWords(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(name.Length + 8);

            for (int i = 0; i < name.Length; i++)
            {
                char current = name[i];

                if (i > 0 && char.IsUpper(current))
                {
                    char previous = name[i - 1];
                    bool afterLowerOrDigit = char.IsLower(previous) || char.IsDigit(previous);
                    bool endOfRunOfCapitals = char.IsUpper(previous) &&
                                              i + 1 < name.Length &&
                                              char.IsLower(name[i + 1]);

                    if (afterLowerOrDigit || endOfRunOfCapitals)
                    {
                        builder.Append(' ');
                    }
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        private static void AddPanel(
            List<QuickToolEntry> entries,
            HashSet<string> seenKeys,
            string tabName,
            RibbonPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            IList<RibbonItem> items;
            try
            {
                items = panel.GetItems();
            }
            catch (Exception)
            {
                return;
            }

            if (items == null)
            {
                return;
            }

            foreach (RibbonItem item in items)
            {
                var pushButton = item as PushButton;
                if (pushButton != null)
                {
                    Add(entries, seenKeys, tabName, panel.Name, null, null, pushButton);
                    continue;
                }

                // SplitButton derives from PulldownButton, so this one branch covers both.
                var pulldownButton = item as PulldownButton;
                if (pulldownButton == null)
                {
                    continue;
                }

                IList<PushButton> childButtons;
                try
                {
                    childButtons = pulldownButton.GetItems();
                }
                catch (Exception)
                {
                    continue;
                }

                if (childButtons == null)
                {
                    continue;
                }

                string groupName = Flatten(SafeItemText(pulldownButton));
                string groupItemName = SafeName(pulldownButton);

                foreach (PushButton childButton in childButtons)
                {
                    Add(entries, seenKeys, tabName, panel.Name, groupName, groupItemName, childButton);
                }
            }
        }

        private static void Add(
            List<QuickToolEntry> entries,
            HashSet<string> seenKeys,
            string tabName,
            string panelName,
            string groupName,
            string groupItemName,
            PushButton pushButton)
        {
            if (pushButton == null)
            {
                return;
            }

            string itemName = SafeName(pushButton);
            string className = SafeClassName(pushButton);
            // Only this add-in's own buttons: the name prefix, and the class really is in this DLL (another
            // product on the Heron tab could one day use a similar name).
            if (string.IsNullOrEmpty(className) || !className.StartsWith(OwnCommandPrefix, StringComparison.Ordinal) ||
                typeof(QuickMenuCatalog).Assembly.GetType(className, false) == null)
                return;   // not a Heron Tools button
            string key = string.IsNullOrEmpty(className)
                ? tabName + "|" + panelName + "|" + itemName
                : className;

            if (string.IsNullOrEmpty(key) || !seenKeys.Add(key))
            {
                return;
            }

            foreach (string excluded in ExcludedClassNames)
            {
                if (string.Equals(className, excluded, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            string displayName = Flatten(SafeItemText(pushButton));
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = itemName;
            }

            entries.Add(QuickToolEntry.ForRibbonButton(
                key,
                displayName,
                SafeToolTip(pushButton),
                tabName,
                panelName,
                groupName,
                itemName,
                groupItemName,
                TryGetControlId(pushButton),
                SafeAvailabilityClassName(pushButton),
                SafeIcon(pushButton)));
        }

        /// <summary>
        /// Revit's own command id for this ribbon item, read from the underlying Autodesk.Windows
        /// item (see the Notes block above). Null when it cannot be read - never throws.
        /// </summary>
        private static string TryGetControlId(RibbonItem item)
        {
            try
            {
                MethodInfo getRibbonItem = GetRibbonItemMethod(item.GetType());

                if (getRibbonItem == null)
                {
                    return null;
                }

                object adWindowsItem = getRibbonItem.Invoke(item, null);
                if (adWindowsItem == null)
                {
                    return null;
                }

                PropertyInfo idProperty = adWindowsItem.GetType().GetProperty("Id");
                if (idProperty == null)
                {
                    return null;
                }

                return idProperty.GetValue(adWindowsItem, null) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The cached getRibbonItem() lookup for one ribbon-item type. Null when absent.</summary>
        private static MethodInfo GetRibbonItemMethod(Type itemType)
        {
            MethodInfo cached;
            if (_getRibbonItemCache.TryGetValue(itemType, out cached))
            {
                return cached;
            }

            MethodInfo found = itemType.GetMethod(
                "getRibbonItem",
                BindingFlags.NonPublic | BindingFlags.Instance);

            _getRibbonItemCache[itemType] = found;
            return found;
        }

        private static string Flatten(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text
                .Replace("\r\n", " ")
                .Replace("\n", " ")
                .Replace("\r", " ")
                .Trim();
        }

        private static string SafeName(RibbonItem item)
        {
            try
            {
                return item.Name ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string SafeItemText(RibbonItem item)
        {
            try
            {
                return item.ItemText ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string SafeToolTip(RibbonItem item)
        {
            try
            {
                return item.ToolTip ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string SafeClassName(PushButton pushButton)
        {
            try
            {
                return pushButton.ClassName ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// The class Revit uses to grey this button out, or empty when it is never greyed.
        /// </summary>
        private static string SafeAvailabilityClassName(PushButton pushButton)
        {
            try
            {
                return pushButton.AvailabilityClassName ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static System.Windows.Media.ImageSource SafeIcon(PushButton pushButton)
        {
            try
            {
                return pushButton.LargeImage ?? pushButton.Image;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
