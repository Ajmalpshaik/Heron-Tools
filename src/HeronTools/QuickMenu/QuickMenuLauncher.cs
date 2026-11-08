// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Runs the tool Ajmal picked on the wheel, exactly as if he had clicked its ribbon button - same command, same dialogs, same undo entry.

using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace HeronTools.Services.QuickMenu
{
    /// <summary>Posts the chosen tool or Revit command so it runs like a normal ribbon click.</summary>
    internal static class QuickMenuLauncher
    {
        /// <summary>
        /// Queues <paramref name="entry"/> to run. Must be called from inside a command's Execute,
        /// while Revit is in a valid API context.
        /// </summary>
        /// <returns>True if the tool was handed to Revit; false with a plain-language reason.</returns>
        internal static bool TryRun(UIApplication uiapp, QuickToolEntry entry, out string error)
        {
            error = string.Empty;

            if (uiapp == null || entry == null)
            {
                error = "No tool was chosen.";
                return false;
            }

            RevitCommandId commandId = ResolveCommandId(entry);
            if (commandId == null)
            {
                error = entry.Source == QuickToolSource.Revit
                    ? "This version of Revit does not have a \"" + entry.DisplayName + "\" command, " +
                      "so the Quick Menu could not start it. Open the Quick Menu settings and pick " +
                      "another command for that slot."
                    : "Revit could not find the \"" + entry.DisplayName + "\" button on the ribbon, " +
                      "so the Quick Menu could not start it. Open the Quick Menu settings and pick " +
                      "the tool again from the list.";
                return false;
            }

            try
            {
                if (!uiapp.CanPostCommand(commandId))
                {
                    error = "Revit would not start \"" + entry.DisplayName + "\" right now. That " +
                            "usually means another command is still running, or this tool cannot be " +
                            "used in the current view. Finish what is running and try again.";
                    return false;
                }

                uiapp.PostCommand(commandId);
                return true;
            }
            catch (Exception ex)
            {
                // Revit refuses a second posted command while one is already queued, and refuses any
                // post from outside a command context. Both surface here as a plain message.
                error = "Revit would not start \"" + entry.DisplayName + "\" right now (" + ex.Message +
                        "). Finish whatever command is running and try again.";
                return false;
            }
        }

        private static RevitCommandId ResolveCommandId(QuickToolEntry entry)
        {
            // One of Revit's own commands - Revit hands over its id directly, no id string involved.
            if (entry.Source == QuickToolSource.Revit)
            {
                if (!entry.PostableCommandValue.HasValue)
                {
                    return null;
                }

                try
                {
                    return RevitCommandId.LookupPostableCommandId(entry.PostableCommandValue.Value);
                }
                catch (Exception)
                {
                    // Not available in this Revit version or edition - reported as a plain message.
                    return null;
                }
            }

            foreach (string candidate in BuildCandidateIds(entry))
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                try
                {
                    RevitCommandId commandId = RevitCommandId.LookupCommandId(candidate);
                    if (commandId != null)
                    {
                        return commandId;
                    }
                }
                catch (Exception)
                {
                    // An unknown id string just means "not this one" - try the next candidate.
                }
            }

            return null;
        }

        private static IEnumerable<string> BuildCandidateIds(QuickToolEntry entry)
        {
            // 1. The id read straight off the live ribbon - right in every normal case.
            yield return entry.ControlId;

            if (string.IsNullOrEmpty(entry.TabName) ||
                string.IsNullOrEmpty(entry.PanelName) ||
                string.IsNullOrEmpty(entry.ItemName))
            {
                yield break;
            }

            // 2. Rebuilt for a button sitting inside a split or pulldown button.
            if (!string.IsNullOrEmpty(entry.GroupItemName))
            {
                yield return "CustomCtrl_%CustomCtrl_%CustomCtrl_%" +
                             entry.TabName + "%" + entry.PanelName + "%" +
                             entry.GroupItemName + "%" + entry.ItemName;
            }

            // 3. Rebuilt for a plain top-level (or stacked) button.
            yield return "CustomCtrl_%CustomCtrl_%" +
                         entry.TabName + "%" + entry.PanelName + "%" + entry.ItemName;
        }
    }
}
