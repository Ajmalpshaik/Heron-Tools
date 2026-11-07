// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Reassigns supported MEP elements (MEP curves, free-standing family instances, spaces) from one level to another without moving them physically - either across the whole project (FROM level -> TO level), or just the elements already selected in Revit (TO level only; each element's own current level is used as its FROM).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Services.ReassignLevel;
using HeronTools.UI.ReassignLevel;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    /// <summary>
    /// Reassigns supported MEP elements from one level to another while keeping physical positions unchanged.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdReassignLevel : IExternalCommand
    {
        private const string ToolTitle = "Reassign Level";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return Run(commandData, ref message);
            }
            catch (Exception ex)
            {
                // Revit undoes a command that does not succeed, so nothing from this run is kept.
                DialogHelper.ShowError(
                    ToolTitle,
                    "Reassign Level could not finish, so nothing was changed.\n\nWhat went wrong: " + ex.Message);
                return Result.Cancelled;   // our own message is shown; no second Revit failure box
            }
        }

        private static Result Run(ExternalCommandData commandData, ref string message)
        {
            UIDocument uidoc = commandData?.Application?.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null)
            {
                DialogHelper.ShowError(ToolTitle, "Open a model first.");
                return Result.Cancelled;
            }

            List<Level> allLevels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (allLevels.Count < 2)
            {
                DialogHelper.ShowError(ToolTitle, "At least 2 levels are required.");
                return Result.Cancelled;
            }

            ICollection<ElementId> preSelectedIds = uidoc.Selection.GetElementIds() ?? new List<ElementId>();
            int totalSelectedCount = preSelectedIds.Count;
            List<Element> selectedCandidates = ReassignLevelService.CollectCandidatesFromSelection(
                doc, preSelectedIds, out int selSkippedHosted, out int selSkippedSpaces, out int selSkippedUnsupported);

            if (!TryPromptScope(
                    commandData.Application,
                    allLevels,
                    totalSelectedCount,
                    selectedCandidates.Count,
                    out ReassignScope scope,
                    out Level fromLevel,
                    out Level toLevel))
            {
                return Result.Cancelled;
            }

            if (scope == ReassignScope.SelectedElements)
            {
                return ExecuteSelectedElementsScope(
                    doc, toLevel, selectedCandidates, selSkippedHosted, selSkippedSpaces, selSkippedUnsupported);
            }

            ElementId fromId = fromLevel.Id;

            List<Element> candidates = ReassignLevelService.CollectCandidates(doc, fromId, out int skippedHosted, out int skippedSpaces);

            if (candidates.Count == 0)
            {
                DialogHelper.ShowInfo(
                    ToolTitle,
                    string.Format(
                        CultureInfo.CurrentCulture,
                        "No elements were found on \"{0}\" that can be reassigned.",
                        fromLevel.Name));
                return Result.Cancelled;
            }

            // Full Project scope - confirm the bulk change before touching the model.
            string confirmMessage = string.Format(
                CultureInfo.CurrentCulture,
                "This will reassign {0} element(s) from \"{1}\" to \"{2}\" across the whole project.\n\n" +
                "Each element is checked to stay where it is; any that would move is left alone. Continue?",
                candidates.Count,
                fromLevel.Name,
                toLevel.Name);
            if (!DialogHelper.ShowYesNo(ToolTitle, confirmMessage))
            {
                return Result.Cancelled;
            }

            var tally = new Tally();
            string undoName = string.Format(CultureInfo.CurrentCulture, "Reassign Level: {0} to {1}", fromLevel.Name, toLevel.Name);
            if (!RunAll(doc, undoName, candidates, toLevel, fromId, tally))
            {
                return Result.Cancelled;
            }

            string resultMessage = string.Format(
                CultureInfo.CurrentCulture,
                "{0} element(s) reassigned, each checked to be where it was.\n\nFROM : {1}\nTO   : {2}",
                tally.Reassigned,
                fromLevel.Name,
                toLevel.Name);
            resultMessage += tally.Describe();
            resultMessage += Skipped(skippedHosted, skippedSpaces, 0);
            DialogHelper.ShowInfo("Reassign Level - Complete", resultMessage);
            return tally.Reassigned > 0 ? Result.Succeeded : Result.Cancelled;
        }

        /// <summary>Counts of what happened, element by element.</summary>
        private sealed class Tally
        {
            public int Reassigned;
            public int AlreadyOnTarget;
            public int Refused;
            public int WouldMove;

            public void Add(ReassignOutcome outcome)
            {
                switch (outcome)
                {
                    case ReassignOutcome.Reassigned: Reassigned++; break;
                    case ReassignOutcome.AlreadyOnTarget: AlreadyOnTarget++; break;
                    case ReassignOutcome.WouldMove: WouldMove++; break;
                    default: Refused++; break;
                }
            }

            public string Describe()
            {
                string text = string.Empty;
                if (AlreadyOnTarget > 0)
                    text += string.Format(CultureInfo.CurrentCulture, "\n\n{0} element(s) were already on this level.", AlreadyOnTarget);
                if (WouldMove > 0)
                    text += string.Format(CultureInfo.CurrentCulture,
                        "\n\n{0} element(s) left as they were: changing their level would have moved them, and their offset could not put them back.",
                        WouldMove);
                if (Refused > 0)
                    text += string.Format(CultureInfo.CurrentCulture,
                        "\n\n{0} element(s) left as they were: Revit would not take the new level for them (pinned, in a group, owned by a colleague, or no level parameter).",
                        Refused);
                return text;
            }
        }

        private static string Skipped(int hosted, int spaces, int unsupported)
        {
            string text = string.Empty;
            if (hosted > 0)
                text += string.Format(CultureInfo.CurrentCulture,
                    "\n\n{0} hosted element(s) skipped - they follow their host (wall, ceiling, face).", hosted);
            if (spaces > 0)
                text += string.Format(CultureInfo.CurrentCulture,
                    "\n\n{0} space(s) skipped - a space cannot change level without being re-made, which would lose its tags and zone. Move spaces by hand.", spaces);
            if (unsupported > 0)
                text += string.Format(CultureInfo.CurrentCulture,
                    "\n\n{0} selected item(s) skipped - only ducts, pipes, cable trays, conduits and free-standing families with a level can be reassigned.", unsupported);
            return text;
        }

        /// <summary>
        /// Runs every element in one transaction (one Undo step). Returns false when nothing changed - the
        /// transaction is rolled back, or Revit refused it at commit - after telling the modeller why.
        /// </summary>
        private static bool RunAll(Document doc, string undoName, IEnumerable<Element> candidates, Level toLevel,
                                   ElementId requiredFromId, Tally tally)
        {
            using (var tx = new Transaction(doc, undoName))
            {
                tx.Start();

                foreach (Element element in candidates)
                {
                    try
                    {
                        tally.Add(ReassignLevelService.Reassign(doc, element, toLevel, requiredFromId));
                    }
                    catch (Exception)
                    {
                        tally.Refused++;
                    }
                }

                if (tally.Reassigned == 0)
                {
                    tx.RollBack();
                    DialogHelper.ShowInfo(ToolTitle, "No element was reassigned, so nothing was changed." + tally.Describe());
                    return false;
                }

                if (tx.Commit() != TransactionStatus.Committed)
                {
                    DialogHelper.ShowError(ToolTitle,
                        "Revit did not accept the change, so nothing was changed. In a workshared model, " +
                        "synchronise and check that no colleague owns these elements, then try again.");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Runs the Selected Elements scope: reassigns only the already-eligible candidates gathered
        /// from Revit's selection before the window opened, to the single TO level chosen there. Each
        /// element's own current level is used as its FROM (a selection can span several levels at
        /// once), via ReassignLevelService.ReassignElementToLevel.
        /// </summary>
        private static Result ExecuteSelectedElementsScope(
            Document doc,
            Level toLevel,
            List<Element> candidates,
            int skippedHosted,
            int skippedSpaces,
            int skippedUnsupported)
        {
            if (candidates.Count == 0)
            {
                DialogHelper.ShowInfo(ToolTitle, "None of the currently selected elements can be reassigned." +
                                                 Skipped(skippedHosted, skippedSpaces, skippedUnsupported));
                return Result.Cancelled;
            }

            string confirmMessage = string.Format(
                CultureInfo.CurrentCulture,
                "This will reassign {0} of your selected element(s) to \"{1}\".\n\n" +
                "Each element is checked to stay where it is; any that would move is left alone. Continue?",
                candidates.Count,
                toLevel.Name);
            if (!DialogHelper.ShowYesNo(ToolTitle, confirmMessage))
            {
                return Result.Cancelled;
            }

            var tally = new Tally();
            string undoName = string.Format(CultureInfo.CurrentCulture, "Reassign Level: Selection to {0}", toLevel.Name);
            if (!RunAll(doc, undoName, candidates, toLevel, null, tally))
            {
                return Result.Cancelled;
            }

            string resultMessage = string.Format(
                CultureInfo.CurrentCulture,
                "{0} element(s) reassigned, each checked to be where it was.\n\nTO : {1}",
                tally.Reassigned,
                toLevel.Name);
            resultMessage += tally.Describe();
            resultMessage += Skipped(skippedHosted, skippedSpaces, skippedUnsupported);
            DialogHelper.ShowInfo("Reassign Level - Complete", resultMessage);
            return Result.Succeeded;
        }

        private static bool TryPromptScope(
            UIApplication uiapp,
            IList<Level> levels,
            int totalSelectedCount,
            int eligibleSelectedCount,
            out ReassignScope scope,
            out Level fromLevel,
            out Level toLevel)
        {
            scope = ReassignScope.WholeProject;
            fromLevel = null;
            toLevel = null;

            var window = new ReassignLevelWindow(levels, totalSelectedCount, eligibleSelectedCount);

            if (uiapp != null)
            {
                new WindowInteropHelper(window)
                {
                    Owner = uiapp.MainWindowHandle
                };
            }

            if (window.ShowDialog() != true)
                return false;

            scope = window.Scope;
            fromLevel = window.FromLevel;
            toLevel = window.ToLevel;

            if (toLevel == null)
                return false;

            // The window disables Run unless the combination is valid; re-check anyway.
            if (scope == ReassignScope.WholeProject && (fromLevel == null || fromLevel.Id == toLevel.Id))
                return false;

            return true;
        }
    }
}
