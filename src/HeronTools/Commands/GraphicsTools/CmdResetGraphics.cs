// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The three Reset buttons (View panel > Reset pulldown):
//   Reset Selected      - no window: the selected elements (or pick them) lose their overrides in this view,
//                         with their insulation and lining. Silent when it works: the view shows it.
//   Reset Graphics...   - the window: element and/or category overrides, this view or picked views.
//   Restore Last Reset  - puts back everything the last reset cleared in this model, as one Undo step.
// Each reset keeps a copy first (ResetGraphicsMemory), so nothing a modeller set by hand is ever lost.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Models.GraphicsTools;
using HeronTools.Services.GraphicsTools;
using HeronTools.Services.ResetGraphics;
using HeronTools.UI;
using HeronTools.Utils;

namespace HeronTools.Commands.GraphicsTools
{
    /// <summary>Reset Selected: one click, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdResetSelected : IExternalCommand
    {
        private const string Title = "Reset Selected";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Result contextResult = GraphicsCommandService.TryCreateContext(commandData, Title, ref message,
                                                                               out GraphicsCommandContext context);
                if (contextResult != Result.Succeeded)
                    return contextResult;

                SelectionCaptureResult selection = GraphicsSelectionService.GetPreselectedOrPromptElementIds(
                    context.UIDocument,
                    selectionFilter: null,
                    prompt: "Select the elements to reset, then click Finish.");
                if (selection.WasCancelled)
                    return Result.Cancelled;
                if (selection.ElementIds.Count == 0)
                {
                    DialogHelper.ShowError(Title, "Nothing is selected. Select the elements to reset and click Reset Selected again.");
                    return Result.Cancelled;
                }

                Document doc = context.Document;
                View view = context.ActiveView;
                var viewKeys = new HashSet<long>(new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType().ToElementIds().Select(id => id.LongValue()));
                List<ElementId> picked = selection.ElementIds.Where(id => viewKeys.Contains(id.LongValue()))
                    .Distinct(new ElementIdIntegerComparer()).ToList();
                if (picked.Count == 0)
                {
                    DialogHelper.ShowError(Title, "None of the selected elements are shown in this view. Select elements you can see here and try again.");
                    return Result.Cancelled;
                }
                picked = InsulationLining.WithWraps(doc, picked, viewKeys);

                // The modeller picked these on purpose, so Heron's own colours on them are reset too.
                ViewResetPlan plan = ResetGraphicsService.Plan(doc, view, ResetWhat.Elements, picked, keepApart: null);
                if (plan.Elements.Count == 0)
                {
                    DialogHelper.ShowInfo(Title, "The selected elements have no graphic overrides in this view, so nothing changed.");
                    return Result.Cancelled;
                }

                ResetSummary summary = ResetGraphicsService.Apply(doc, new List<ViewResetPlan> { plan }, "Reset Selected Graphics");
                if (summary.NotCommitted)
                {
                    DialogHelper.ShowError(Title, ResetGraphicsService.Describe(summary));
                    return Result.Cancelled;
                }
                // Silent when it works: the view shows it, and Restore Last Reset can bring it back.
                return summary.Elements > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError(Title, "Reset Selected stopped, and nothing was changed.\n\nRevit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }

    /// <summary>Reset Graphics...: the window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdResetGraphics : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Result contextResult = GraphicsCommandService.TryCreateContext(commandData, "Reset Graphics", ref message,
                                                                               out GraphicsCommandContext context);
                if (contextResult != Result.Succeeded)
                    return contextResult;

                var window = new ResetGraphicsWindow(context.Document, context.ActiveView);
                new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };
                window.ShowDialog();
                // Each Reset / Restore in the window committed its own Undo step; nothing is open here.
                return window.ChangedSomething ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("Reset Graphics", "The Reset Graphics window could not open. Revit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }

    /// <summary>Restore Last Reset: puts back what the last reset in this model cleared.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdRestoreLastReset : IExternalCommand
    {
        private const string Title = "Restore Last Reset";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData?.Application?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                DialogHelper.ShowError(Title, "Open a model first.");
                return Result.Cancelled;
            }
            if (!ResetGraphicsMemory.HasCopy(doc))
            {
                DialogHelper.ShowInfo(Title,
                    "There is no reset to bring back in this model since Revit was opened. (After a restart, the copy is gone - like Undo history.)");
                return Result.Cancelled;
            }

            try
            {
                (int restoredElements, int restoredCategories, bool committed) = ResetGraphicsMemory.Restore(doc);
                if (!committed)
                {
                    DialogHelper.ShowError(Title, "Revit did not accept the change, so nothing was restored. In a workshared model, check that no colleague owns the view.");
                    return Result.Cancelled;
                }
                if (restoredElements + restoredCategories == 0)
                {
                    DialogHelper.ShowInfo(Title, "Nothing could be restored: the views or elements of the last reset are gone.");
                    return Result.Cancelled;
                }
                return Result.Succeeded;   // silent: the views show it
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError(Title, "Restore stopped, and nothing was changed.\n\nRevit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }
}
