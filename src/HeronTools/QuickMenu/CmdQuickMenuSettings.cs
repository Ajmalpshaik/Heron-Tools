// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Ribbon entry for customising the quick wheel - which Heron Tools button sits in each slot, how many slots, and how big the wheel opens.

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
    /// <summary>Opens the Quick Menu customise window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdQuickMenuSettings : IExternalCommand
    {
        internal const string ToolTitle = "Heron Quick Menu";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData != null ? commandData.Application : null;
            if (uiapp == null)
            {
                message = "Revit did not hand the Quick Menu an application to work with.";
                return Result.Failed;
            }

            return Show(uiapp);
        }

        /// <summary>Shows the customise window. Shared with the wheel's own "S" shortcut.</summary>
        internal static Result Show(UIApplication uiapp)
        {
            try
            {
                IList<QuickToolEntry> tools = QuickMenuCatalog.Refresh(uiapp);

                // Heron Tools buttons only - Revit's own commands are always in the list, so a plain
                // Count check could never fire.
                if (CmdQuickMenu.CountHeronToolsEntries(tools) == 0)
                {
                    DialogHelper.ShowError(
                        ToolTitle,
                        "No Heron Tools buttons could be read from the ribbon, so there is nothing to put " +
                        "on the wheel yet. Restart Revit and try again.");
                    return Result.Cancelled;
                }

                var window = new QuickMenuSettingsWindow(tools, QuickMenuConfig.Load());
                new WindowInteropHelper(window)
                {
                    Owner = uiapp.MainWindowHandle
                };

                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError(ToolTitle, "The Quick Menu settings could not open:\n\n" + ex.Message);
                return Result.Cancelled;
            }
        }
    }
}
