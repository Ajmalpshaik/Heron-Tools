// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Ribbon entry for the quick tool wheel - the video-game style ring that opens around the mouse pointer, holding Ajmal's own favourite Heron Tools buttons. Point at one, click, and Revit runs that tool exactly as if its ribbon button had been clicked.

using System;
using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Services.QuickMenu;
using HeronTools.UI.QuickMenu;
using HeronTools.Utils;

namespace HeronTools.Commands.QuickMenu
{
    /// <summary>Opens the quick tool wheel at the mouse pointer and runs whatever is chosen.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdQuickMenu : IExternalCommand
    {
        private const string ToolTitle = "Heron Quick Menu";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData != null ? commandData.Application : null;
            if (uiapp == null)
            {
                message = "Revit did not hand the Quick Menu an application to work with.";
                return Result.Failed;
            }

            try
            {
                QuickMenuConfig config = QuickMenuConfig.Load();
                IList<QuickToolEntry> tools = QuickMenuCatalog.GetEntries(uiapp);

                // Count only Heron Tools buttons: Revit's own commands are always appended to the
                // catalog, so the whole list is never empty and a plain Count check could not fire.
                if (CountHeronToolsEntries(tools) == 0)
                {
                    DialogHelper.ShowError(
                        ToolTitle,
                        "No Heron Tools buttons could be read from the ribbon, so the wheel has nothing " +
                        "to show. Restart Revit and try again.");
                    return Result.Cancelled;
                }

                var slots = new List<QuickToolEntry>(config.Slots.Count);
                foreach (string key in config.Slots)
                {
                    slots.Add(QuickMenuCatalog.Find(uiapp, key));
                }

                IList<bool> available = QuickMenuAvailability.Evaluate(uiapp, slots);

                var wheel = new QuickMenuWindow(slots, available, config.Diameter);
                new WindowInteropHelper(wheel)
                {
                    Owner = uiapp.MainWindowHandle
                };

                wheel.ShowDialog();

                if (wheel.OpenSettingsRequested)
                {
                    return CmdQuickMenuSettings.Show(uiapp);
                }

                QuickToolEntry chosen = wheel.SelectedEntry;
                if (chosen == null)
                {
                    return Result.Cancelled;
                }

                string error;
                if (!QuickMenuLauncher.TryRun(uiapp, chosen, out error))
                {
                    DialogHelper.ShowError(ToolTitle, error);
                    return Result.Cancelled;
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError(ToolTitle, "The Quick Menu could not open:\n\n" + ex.Message);
                return Result.Cancelled;
            }
        }

        /// <summary>How many catalog entries are Heron Tools ribbon buttons (not Revit commands).
        /// Shared with CmdQuickMenuSettings.</summary>
        internal static int CountHeronToolsEntries(IList<QuickToolEntry> tools)
        {
            int count = 0;
            if (tools == null)
            {
                return count;
            }

            foreach (QuickToolEntry entry in tools)
            {
                if (entry != null && entry.Source == QuickToolSource.HeronTools)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
