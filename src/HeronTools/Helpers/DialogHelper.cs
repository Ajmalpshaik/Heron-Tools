// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Every pop-up message in Heron Tools goes through here, so each one wears the Heron look
// (HeronMessageWindow) instead of Revit's grey TaskDialog. The window is owned by whatever is in front -
// a Heron window or Revit itself - so it opens centred on it and never hides behind. If the Heron window
// cannot be shown for any reason, the same text goes out in a plain TaskDialog: a message is never lost.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Autodesk.Revit.UI;
using HeronTools.UI;

namespace HeronTools.Utils
{
    /// <summary>
    /// Provides simplified methods for common dialog patterns.
    /// </summary>
    internal static class DialogHelper
    {
        /// <summary>
        /// Shows an error: the tool could not do what was asked.
        /// </summary>
        /// <param name="title">The tool's name (shown as the title).</param>
        /// <param name="message">What went wrong and what to do next.</param>
        public static void ShowError(string title, string message)
        {
            Show(MessageKind.Error, title, null, message, null, "Close", null);
        }

        /// <summary>
        /// Shows a note: something the modeller should know, nothing went wrong.
        /// </summary>
        /// <param name="title">The tool's name (shown as the title).</param>
        /// <param name="message">The information message.</param>
        public static void ShowInfo(string title, string message)
        {
            Show(MessageKind.Info, title, null, message, null, "Close", null);
        }

        /// <summary>
        /// Shows a warning: the tool ran, but part of it needs the modeller's attention.
        /// </summary>
        public static void ShowWarning(string title, string message)
        {
            Show(MessageKind.Warning, title, null, message, null, "Close", null);
        }

        /// <summary>
        /// Shows a run report: a heading, a short summary and (optionally) a long list behind "Show details".
        /// </summary>
        public static void ShowReport(string title, string heading, string summary, string details, bool needsAttention)
        {
            Show(needsAttention ? MessageKind.Warning : MessageKind.Info, title, heading, summary, details, "Close", null);
        }

        /// <summary>
        /// Shows a question with Yes / No buttons.
        /// </summary>
        /// <param name="title">The tool's name (shown as the title).</param>
        /// <param name="message">The question.</param>
        /// <returns>True if the modeller pressed Yes; No, Esc or closing the window give false.</returns>
        public static bool ShowYesNo(string title, string message)
        {
            return Show(MessageKind.Question, title, null, message, null, "Yes", "No");
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
        /// Shows a message with a heading and a body.
        /// </summary>
        /// <param name="title">The tool's name (shown as the title).</param>
        /// <param name="mainInstruction">The heading.</param>
        /// <param name="mainContent">The detailed content.</param>
        /// <returns>Always <see cref="TaskDialogResult.Ok"/>: the message has one button.</returns>
        public static TaskDialogResult ShowDialog(string title, string mainInstruction, string mainContent)
        {
            Show(MessageKind.Info, title, mainInstruction, mainContent, null, "Close", null);
            return TaskDialogResult.Ok;
        }

        // ------------------------------------------------------------------ the one way a message is shown

        /// <returns>True when the first button was pressed.</returns>
        private static bool Show(MessageKind kind, string title, string heading, string message, string details,
                                 string primaryText, string secondText)
        {
            HeronMessageWindow window;
            try
            {
                window = new HeronMessageWindow(kind, title, heading, message, details, primaryText, secondText);
                IntPtr owner = OwnerWindow();
                if (owner != IntPtr.Zero)
                    new WindowInteropHelper(window) { Owner = owner };
            }
            catch (Exception)
            {
                // The Heron window could not be built (a theme or font failed to load): say it the plain way.
                return ShowPlain(title, heading, message, details, secondText != null);
            }

            try
            {
                window.ShowDialog();
                return window.Accepted;
            }
            catch (Exception)
            {
                return ShowPlain(title, heading, message, details, secondText != null);
            }
        }

        private static bool ShowPlain(string title, string heading, string message, string details, bool isQuestion)
        {
            var dialog = new TaskDialog(string.IsNullOrWhiteSpace(title) ? "Heron Tools" : title)
            {
                MainInstruction = heading ?? "",
                MainContent = message ?? "",
                ExpandedContent = details ?? "",
                CommonButtons = isQuestion ? TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                                           : TaskDialogCommonButtons.Close
            };
            return dialog.Show() == TaskDialogResult.Yes;
        }

        /// <summary>
        /// The window in front on Revit's own thread: a Heron tool window if one is open, else Revit.
        /// </summary>
        private static IntPtr OwnerWindow()
        {
            IntPtr active = GetActiveWindow();
            if (active != IntPtr.Zero)
                return active;

            using (Process revit = Process.GetCurrentProcess())
                return revit.MainWindowHandle;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();
    }
}
