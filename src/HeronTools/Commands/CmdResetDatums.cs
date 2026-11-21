// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Ribbon entry points (grids only / levels only / both) that reset visible datum extents in the active view back to 3D, delegating to ResetDatumService.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Utils;
using HeronTools.Services.ResetDatums;
using HeronTools.Models;

namespace HeronTools.Commands
{
    /// <summary>
    /// Resets both grid and level datums back to 3D extents.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdResetDatums : IExternalCommand
    {
        /// <summary>
        /// Executes the reset datums workflow for grids and levels.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return ResetDatumService.Execute(
                    commandData,
                    ResetDatumMode.Combined,
                    "Reset Grids & Levels");
            }
            catch (System.Exception ex)
            {
                // Cancelled, not Failed: our own box is the message, and Revit undoes anything not
                // Succeeded - so "nothing was changed" is true.
                DialogHelper.ShowError("Reset Grids & Levels",
                    "Reset Grids & Levels stopped because of an unexpected error, so nothing was changed.\n\nRevit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }

    /// <summary>
    /// Resets grid datums back to 3D extents.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdResetDatumsGrids : IExternalCommand
    {
        /// <summary>
        /// Executes the reset datums workflow for grids only.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return ResetDatumService.Execute(
                    commandData,
                    ResetDatumMode.GridsOnly,
                    "Reset Grids");
            }
            catch (System.Exception ex)
            {
                // Cancelled, not Failed: our own box is the message, and Revit undoes anything not
                // Succeeded - so "nothing was changed" is true.
                DialogHelper.ShowError("Reset Grids",
                    "Reset Grids stopped because of an unexpected error, so nothing was changed.\n\nRevit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }

    /// <summary>
    /// Resets level datums back to 3D extents.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdResetDatumsLevels : IExternalCommand
    {
        /// <summary>
        /// Executes the reset datums workflow for levels only.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return ResetDatumService.Execute(
                    commandData,
                    ResetDatumMode.LevelsOnly,
                    "Reset Levels");
            }
            catch (System.Exception ex)
            {
                // Cancelled, not Failed: our own box is the message, and Revit undoes anything not
                // Succeeded - so "nothing was changed" is true.
                DialogHelper.ShowError("Reset Levels",
                    "Reset Levels stopped because of an unexpected error, so nothing was changed.\n\nRevit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }
}
