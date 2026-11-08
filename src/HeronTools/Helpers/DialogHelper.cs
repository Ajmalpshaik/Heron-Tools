// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Tool Name: Dialog Helper
// Description: Centralized dialog methods for common user interactions.
// Author: Ajmal P.S.
// Version: 1.0.0
// Last Updated: 2025-12-11
// Revit Version: 2020
// Dependencies: Autodesk.Revit.UI

using Autodesk.Revit.UI;

namespace HeronTools.Utils
{
    /// <summary>
    /// Provides simplified methods for common dialog patterns.
    /// </summary>
    internal static class DialogHelper
    {
        /// <summary>
        /// Shows an error message dialog.
        /// </summary>
        /// <param name="title">The dialog title.</param>
        /// <param name="message">The error message.</param>
        public static void ShowError(string title, string message)
        {
            TaskDialog.Show(title, message);
        }

        /// <summary>
        /// Shows an information message dialog.
        /// </summary>
        /// <param name="title">The dialog title.</param>
        /// <param name="message">The information message.</param>
        public static void ShowInfo(string title, string message)
        {
            TaskDialog.Show(title, message);
        }

        /// <summary>
        /// Shows a warning message with a Yes/No prompt.
        /// </summary>
        /// <param name="title">The dialog title.</param>
        /// <param name="message">The warning message.</param>
        /// <returns>True if user clicked Yes, false otherwise.</returns>
        public static bool ShowYesNo(string title, string message)
        {
            TaskDialogResult result = TaskDialog.Show(
                title,
                message,
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

            return result == TaskDialogResult.Yes;
        }

        /// <summary>
        /// Above this many elements, a long-running tool asks before it starts. Below it the run is
        /// quick enough that a prompt is just a click in the way.
        /// </summary>
        /// <remarks>
        /// Deliberately NOT exposed in any settings window: it is a "warn me before Revit goes busy"
        /// threshold, not a modelling choice. Shared so every tool agrees on what counts as a big run.
        /// Lived on TagClashSettings until v1.49.5, which is where it was first needed - it was moved
        /// here once tools outside the clash feature (Center Room Tags, L-Shape Leader) started using
        /// it, since a suite-wide threshold living on one feature's settings class reads as if it only
        /// applies to that feature.
        /// </remarks>
        internal const int LongRunConfirmThreshold = 500;

        /// <summary>
        /// Shows a confirmation dialog with custom buttons.
        /// </summary>
        /// <param name="title">The dialog title.</param>
        /// <param name="mainInstruction">The main instruction text.</param>
        /// <param name="mainContent">The detailed content.</param>
        /// <returns>The user's selection result.</returns>
        public static TaskDialogResult ShowDialog(string title, string mainInstruction, string mainContent)
        {
            TaskDialog dialog = new TaskDialog(title)
            {
                MainInstruction = mainInstruction,
                MainContent = mainContent,
                CommonButtons = TaskDialogCommonButtons.Ok
            };

            return dialog.Show();
        }
    }
}
