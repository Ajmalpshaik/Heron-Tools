// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Coordinates View Crop UI flow, target view selection, processing, and reporting.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Models.ViewCrop;
using HeronTools.UI.ViewCrop;
using HeronTools.Utils;

namespace HeronTools.Services.ViewCrop
{
    /// <summary>
    /// Handles UI flow, target view selection, processing invocation, and summary reporting.
    /// </summary>
    internal static class ViewCropCommandService
    {
        internal static Result Execute(
            ExternalCommandData commandData,
            string commandTitle,
            ref string message)
        {
            string extentSourceLabel = "(not yet set)";
            try
            {
                UIDocument uidoc = commandData.Application?.ActiveUIDocument;
                if (!ValidationHelper.ValidateUIDocument(uidoc, out message))
                {
                    DialogHelper.ShowError(commandTitle, message);
                    return Result.Cancelled;
                }

                Document doc = uidoc.Document;
                if (!ValidationHelper.ValidateEditableDocument(doc, out message))
                {
                    DialogHelper.ShowError(commandTitle, message);
                    return Result.Cancelled;
                }

                View activeView = doc.ActiveView;
                if (activeView == null)
                {
                    DialogHelper.ShowError(commandTitle, "No active view found.");
                    return Result.Cancelled;
                }

                var settingsTracker = new ViewCropSettingsTracker(doc);
                var optionsWindow = new ViewCropOptionsWindow(commandTitle, settingsTracker.LastSettings);
                new WindowInteropHelper(optionsWindow)
                {
                    Owner = commandData.Application.MainWindowHandle
                };

                bool? optionsResult = optionsWindow.ShowDialog();
                if (optionsResult != true)
                    return Result.Cancelled;

                ViewCropSettings settings = optionsWindow.SelectedSettings ?? new ViewCropSettings();
                extentSourceLabel = settings.ExtentSource.ToString();

                // Only remembering the settings failed - this run still uses them, so it goes ahead.
                // But say so: otherwise the next run silently reopens with the old values.
                if (!settingsTracker.Save(settings, out string saveError))
                {
                    DialogHelper.ShowError(
                        commandTitle,
                        "The View Crop settings could not be saved, so they will be forgotten when you switch "
                        + "project or restart Revit. This run still uses them.\n\n" + saveError);
                }

                IList<View> targetViews = ResolveTargetViews(
                    commandData,
                    doc,
                    activeView,
                    optionsWindow.ApplyToActiveViewOnly,
                    out string resolveError);

                if (!string.IsNullOrWhiteSpace(resolveError))
                {
                    DialogHelper.ShowError(commandTitle, resolveError);
                    message = resolveError;
                    return Result.Cancelled;
                }

                if (targetViews == null || targetViews.Count == 0)
                    return Result.Cancelled;

                if (!ConfirmBulkRun(commandTitle, targetViews.Count, "view crop"))
                    return Result.Cancelled;

                var service = new ViewCropExtentsService(doc, settings, settings.ExtentSource);
                ViewCropBatchResult batch;
                ViewCropBatchResult annotationBatch = null;

                // One outer group around the crop AND the integrated annotation crop. Each service
                // still opens and closes its own group inside this one; assimilating this group merges
                // both into one undo item named after it - so the run is one Ctrl+Z, not two.
                using (var runGroup = new TransactionGroup(doc, $"View Crop - {commandTitle}"))
                {
                    runGroup.Start();

                    batch = service.Process(targetViews, $"View Crop - {commandTitle}");

                    if (settings.ApplyAnnotationCrop && batch.UpdatedCount > 0)
                    {
                        annotationBatch = RunIntegratedAnnotationCrop(doc, batch, settings, commandTitle);
                    }

                    if (batch.UpdatedCount > 0)
                        runGroup.Assimilate();
                    else
                        runGroup.RollBack();
                }

                try
                {
                    ViewCropReportPresenter.ShowSummaryIfNeeded(commandTitle, "View crop", batch, annotationBatch);
                }
                catch (Exception summaryEx)
                {
                    string summaryLog = ViewCropReportPresenter.TryWriteErrorLog("ViewCrop", commandTitle, extentSourceLabel, summaryEx);
                    DialogHelper.ShowError(
                        commandTitle,
                        $"Processing finished, but summary dialog failed.\n{summaryEx.Message}\n\nLog: {summaryLog}");
                }

                if (settings.ShowDiagnostics && batch.UpdatedCount > 0)
                {
                    ShowDiagnosticsIfAny(commandData, commandTitle, batch);
                }

                return ToCommandResult(batch);
            }
            catch (Exception ex)
            {
                string logPath = ViewCropReportPresenter.TryWriteErrorLog("ViewCrop", commandTitle, extentSourceLabel, ex);
                DialogHelper.ShowError(
                    commandTitle,
                    $"{ex.Message}\n\nDetails log:\n{logPath}");
                return Result.Failed;
            }
        }

        internal static IList<View> ResolveTargetViews(
            ExternalCommandData commandData,
            Document doc,
            View activeView,
            bool applyToActiveOnly,
            out string error)
        {
            error = string.Empty;

            if (applyToActiveOnly)
            {
                if (!ViewCropViewSupport.TryValidateType(activeView, out string reason))
                {
                    error = $"Unsupported active view. {reason}";
                    return new List<View>();
                }

                return new List<View> { activeView };
            }

            IList<ViewCropTargetViewItem> items = ViewCropTargetViewCollector.Collect(doc, activeView.Id);
            var selectionWindow = new ViewCropTargetViewsWindow(items);
            new WindowInteropHelper(selectionWindow)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            bool? selectionResult = selectionWindow.ShowDialog();
            if (selectionResult != true)
            {
                return null;
            }

            IList<ElementId> selectedIds = selectionWindow.SelectedViewIds ?? new List<ElementId>();
            var views = new List<View>();
            foreach (ElementId id in selectedIds)
            {
                View view = doc.GetElement(id) as View;
                if (view != null)
                    views.Add(view);
            }

            views = views
                .GroupBy(v => (v.Id).LongValue())
                .Select(g => g.First())
                .ToList();

            if (views.Count == 0)
            {
                error = "No views selected.";
            }

            return views;
        }

        internal static Result ToCommandResult(ViewCropBatchResult batch)
        {
            if (batch == null)
                return Result.Cancelled;

            if (batch.UpdatedCount > 0)
                return Result.Succeeded;

            return batch.FailedCount > 0 ? Result.Failed : Result.Cancelled;
        }

        /// <summary>
        /// Skill safety-gate: confirm before changing crop on more than one view.
        /// Single-view runs (active view only) need no extra confirmation - the user already
        /// explicitly picked that view.
        /// </summary>
        internal static bool ConfirmBulkRun(string commandTitle, int viewCount, string operationLabel)
        {
            if (viewCount <= 1)
                return true;

            string message =
                $"This will modify the {operationLabel} on {viewCount} views.\n" +
                "This change can be undone with a single Ctrl+Z. Continue?";

            return DialogHelper.ShowYesNo(commandTitle, message);
        }

        /// <summary>
        /// Applies annotation crop to every view the crop just updated.
        /// </summary>
        /// <returns>
        /// The annotation crop's own per-view results, so the summary can report the views it skipped or
        /// failed on. Null when there was nothing to process or the run threw (that error is shown here).
        /// </returns>
        private static ViewCropBatchResult RunIntegratedAnnotationCrop(
            Document doc,
            ViewCropBatchResult batch,
            ViewCropSettings settings,
            string commandTitle)
        {
            try
            {
                var successfullyCropped = new List<View>();
                foreach (var result in batch.Items)
                {
                    if (result.State == ViewCropResultState.Updated)
                    {
                        View v = doc.GetElement(result.ViewId) as View;
                        if (v != null)
                            successfullyCropped.Add(v);
                    }
                }

                if (successfullyCropped.Count == 0)
                    return null;

                var annotationSettings = new ViewCropAnnotationSettings
                {
                    OffsetMm = settings.AnnotationOffsetMm
                };
                var annotationService = new ViewCropAnnotationService(doc, annotationSettings);
                return annotationService.Process(successfullyCropped, "View Crop - Integrated Annotation Crop");
            }
            catch (Exception annotationEx)
            {
                string annotationLog = ViewCropReportPresenter.TryWriteErrorLog(
                    "ViewCrop_AnnotationFallback",
                    commandTitle + " (Annotation Fallback)",
                    settings.ExtentSource.ToString(),
                    annotationEx);
                DialogHelper.ShowError(
                    commandTitle,
                    $"View cropping succeeded, but integrated annotation cropping failed.\n{annotationEx.Message}\n\nLog: {annotationLog}");
                return null;
            }
        }

        private static void ShowDiagnosticsIfAny(
            ExternalCommandData commandData,
            string commandTitle,
            ViewCropBatchResult batch)
        {
            var sbDiag = new System.Text.StringBuilder();
            foreach (var item in batch.Items)
            {
                if (item.State == ViewCropResultState.Updated && !string.IsNullOrWhiteSpace(item.DiagnosticReport))
                {
                    sbDiag.AppendLine($"### VIEW: {item.ViewName} ({item.ViewTypeName}) ###");
                    sbDiag.AppendLine(item.DiagnosticReport);
                    sbDiag.AppendLine();
                    sbDiag.AppendLine();
                }
            }

            string diagReport = sbDiag.ToString().Trim();
            if (string.IsNullOrWhiteSpace(diagReport))
                return;

            try
            {
                var diagWindow = new ViewCropDiagnosticsWindow(diagReport);
                new WindowInteropHelper(diagWindow)
                {
                    Owner = commandData.Application.MainWindowHandle
                };
                diagWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                // Diagnostics report is a "nice to have" - if the WPF window fails, fall back to clipboard.
                try
                {
                    System.Windows.Clipboard.SetText(diagReport);
                    DialogHelper.ShowInfo(
                        commandTitle + " - Diagnostics",
                        "Diagnostics report could not be shown in a window and was copied to the clipboard instead.");
                }
                catch
                {
                    DialogHelper.ShowError(
                        commandTitle + " - Diagnostics",
                        $"Diagnostics report could not be displayed.\n{ex.Message}");
                }
            }
        }
    }
}
