// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.3.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Ribbon entry points for Auto Dimension (Datums panel): Grids, Levels, Grids and Levels, and Settings.

using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Models.AutoDimension;
using HeronTools.Services.AutoDimension;
using HeronTools.UI.AutoDimension;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    /// <summary>Shared run for the three dimension buttons: settings in, report out (only when needed).</summary>
    internal static class AutoDimensionRunner
    {
        internal static Result Run(ExternalCommandData commandData, AutoDimensionMode mode)
        {
            string title = AutoDimensionService.UndoName(mode);
            UIDocument uidoc = commandData?.Application?.ActiveUIDocument;
            if (uidoc == null)
            {
                DialogHelper.ShowError(title, "Open a project view before running this command.");
                return Result.Cancelled;
            }

            try
            {
                AutoDimensionSettings settings = AutoDimensionConfigStore.Load();
                AutoDimensionOutcome outcome = AutoDimensionService.Run(uidoc, mode, settings);

                if (!string.IsNullOrEmpty(outcome.Blocked))
                {
                    DialogHelper.ShowInfo(title, outcome.Blocked);
                    return Result.Cancelled;
                }

                if (!string.IsNullOrEmpty(outcome.Refused))
                {
                    DialogHelper.ShowError(title, outcome.Refused);
                    return Result.Cancelled;
                }

                AutoDimensionReport report = outcome.Report;
                if (report.RowsMade == 0)
                {
                    // Nothing on screen to look at, so always say why.
                    DialogHelper.ShowReport(title, "Nothing was dimensioned", report.Summary(),
                        report.HasDetails ? report.Details() : null, report.HasLeftOut);
                    return Result.Cancelled;
                }

                // It worked: the rows are the result. Speak up only when something was left out.
                if (report.HasLeftOut && settings.ShowLeftOut)
                    DialogHelper.ShowReport(title, "Some rows were left out", report.Summary(), report.Details(), true);

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                // Cancelled, not Failed: our own box is the message, and Revit undoes anything not Succeeded.
                DialogHelper.ShowError(title, title + " stopped because of an unexpected error, so nothing was changed.\n\nRevit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }

    /// <summary>Dimension the grids of the active view.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdAutoDimensionGrids : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return AutoDimensionRunner.Run(commandData, AutoDimensionMode.Grids);
        }
    }

    /// <summary>Dimension the levels of the active section or elevation.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdAutoDimensionLevels : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return AutoDimensionRunner.Run(commandData, AutoDimensionMode.Levels);
        }
    }

    /// <summary>Dimension grids and (in sections and elevations) levels of the active view.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdAutoDimensionGridsLevels : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return AutoDimensionRunner.Run(commandData, AutoDimensionMode.GridsAndLevels);
        }
    }

    /// <summary>Open the Auto Dimension settings. Changes nothing in the model.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdAutoDimensionSettings : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            const string title = "Auto Dimension Settings";
            try
            {
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                var typeNames = doc == null
                    ? new System.Collections.Generic.List<string>()
                    : AutoDimensionService.LinearDimensionTypes(doc).Select(t => t.Name).ToList();

                var window = new AutoDimensionSettingsWindow(AutoDimensionConfigStore.Load(), typeNames);
                if (window.ShowDialog() != true || window.Result == null)
                    return Result.Cancelled;

                if (!AutoDimensionConfigStore.Save(window.Result, out string error))
                {
                    DialogHelper.ShowError(title, "The settings could not be saved, so the old ones stay.\n\nWindows said: " + error);
                    return Result.Cancelled;
                }

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                DialogHelper.ShowError(title, "The settings window could not open.\n\nRevit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }
}
