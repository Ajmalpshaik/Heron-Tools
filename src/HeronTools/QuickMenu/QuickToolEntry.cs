// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// One launchable thing as the Quick Menu sees it - either an Heron Tools ribbon button or one of Revit's own built-in commands - with the name, icon and tooltip to draw on the wheel plus everything needed to start it again.

using System.Windows.Media;
using Autodesk.Revit.UI;

namespace HeronTools.Services.QuickMenu
{
    /// <summary>Where a Quick Menu entry came from - and therefore how it gets launched.</summary>
    internal enum QuickToolSource
    {
        /// <summary>A button on the Heron ribbon tab.</summary>
        HeronTools = 0,

        /// <summary>One of Revit's own built-in commands.</summary>
        Revit = 1
    }

    /// <summary>One launchable tool or command, as shown on the Quick Menu wheel.</summary>
    internal sealed class QuickToolEntry
    {
        /// <summary>Prefix that marks a saved key as one of Revit's own commands.</summary>
        internal const string RevitKeyPrefix = "revit:";

        private QuickToolEntry(
            QuickToolSource source,
            string key,
            string displayName,
            string tooltip,
            string tabName,
            string panelName,
            string groupName,
            string itemName,
            string groupItemName,
            string controlId,
            string availabilityClassName,
            PostableCommand? postableCommandValue,
            ImageSource icon)
        {
            Source = source;
            Key = key;
            DisplayName = displayName;
            Tooltip = tooltip;
            TabName = tabName;
            PanelName = panelName;
            GroupName = groupName;
            ItemName = itemName;
            GroupItemName = groupItemName;
            ControlId = controlId;
            AvailabilityClassName = availabilityClassName;
            PostableCommandValue = postableCommandValue;
            Icon = icon;
        }

        /// <summary>Builds an entry for one of Ajmal's own ribbon buttons.</summary>
        internal static QuickToolEntry ForRibbonButton(
            string key,
            string displayName,
            string tooltip,
            string tabName,
            string panelName,
            string groupName,
            string itemName,
            string groupItemName,
            string controlId,
            string availabilityClassName,
            ImageSource icon)
        {
            return new QuickToolEntry(
                QuickToolSource.HeronTools,
                key,
                displayName,
                tooltip,
                tabName,
                panelName,
                groupName,
                itemName,
                groupItemName,
                controlId,
                availabilityClassName,
                null,
                icon);
        }

        /// <summary>Builds an entry for one of Revit's own built-in commands.</summary>
        internal static QuickToolEntry ForRevitCommand(
            string commandName,
            string displayName,
            PostableCommand commandValue)
        {
            return new QuickToolEntry(
                QuickToolSource.Revit,
                RevitKeyPrefix + commandName,
                displayName,
                "Revit's own \"" + displayName + "\" command, started exactly as if you had clicked it " +
                "on Revit's ribbon.",
                "Revit",
                "Revit command",
                null,
                commandName,
                null,
                null,
                null,
                commandValue,
                null);
        }

        /// <summary>Whether this is an Heron Tools button or one of Revit's own commands.</summary>
        public QuickToolSource Source { get; }

        /// <summary>Stable identity saved in the slot file - a command class name, or "revit:Name".</summary>
        public string Key { get; }

        /// <summary>Label with line breaks flattened, e.g. "Unhide All".</summary>
        public string DisplayName { get; }

        /// <summary>Tooltip shown in the Quick Menu hub while hovering.</summary>
        public string Tooltip { get; }

        /// <summary>"Heron" for a Heron Tools button, or "Revit" for a built-in command.</summary>
        public string TabName { get; }

        /// <summary>Ribbon panel label, e.g. "View"; "Revit command" for a built-in command.</summary>
        public string PanelName { get; }

        /// <summary>Parent split/pulldown button label, or null.</summary>
        public string GroupName { get; }

        /// <summary>Internal ribbon item name, or the PostableCommand name for a Revit command.</summary>
        public string ItemName { get; }

        /// <summary>Internal name of the parent split/pulldown button, or null.</summary>
        public string GroupItemName { get; }

        /// <summary>Revit's own command id string for an Heron Tools button. Null otherwise.</summary>
        public string ControlId { get; }

        /// <summary>
        /// The class Revit consults to decide whether this button is greyed out on the ribbon.
        /// Empty when the button is always enabled, and always empty for a Revit command.
        /// </summary>
        public string AvailabilityClassName { get; }

        /// <summary>The built-in command to post, for a Revit entry. Null for an Heron Tools button.</summary>
        public PostableCommand? PostableCommandValue { get; }

        /// <summary>The button's own ribbon icon. Always null for a Revit command.</summary>
        public ImageSource Icon { get; }

        /// <summary>"View - Unhide All" style label for the customise window's tool list.</summary>
        public string ListLabel
        {
            get
            {
                string where = string.IsNullOrEmpty(GroupName)
                    ? PanelName
                    : PanelName + " / " + GroupName;
                return where + "  -  " + DisplayName;
            }
        }
    }
}
