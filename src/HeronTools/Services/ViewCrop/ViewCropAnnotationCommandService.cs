// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Coordinates annotation crop UI, target view selection, execution, and reporting.

using System;
using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Models.ViewCrop;
using HeronTools.UI.ViewCrop;
using HeronTools.Utils;

namespace HeronTools.Services.ViewCrop
{
    /// <summary>
    /// Handles UI flow, target view selection, processing invocation, and summary reporting
    /// for annotation crop operations.
    /// </summary>
    internal static class ViewCropAnnotationCommandService
    {
        internal static Result Execute(
            ExternalCommandData commandData,
            string commandTitle,
            ref string message)
        {
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

                var settingsTracker = new ViewCropAnnotationSettingsTracker(doc);
                var optionsWindow = new ViewCropAnnotationOptionsWindow(commandTitle, settingsTracker.LastSettings);
                new WindowInteropHelper(optionsWindow)
                {
                    Owner = commandData.Application.MainWindowHandle
                };

                bool? optionsResult = optionsWindow.ShowDialog();
                if (optionsResult != true)
                    return Result.Cancelled;

                ViewCropAnnotationSettings settings = optionsWindow.SelectedSettings ?? new ViewCropAnnotationSettings();

                // Remembered on disk for next time (also after a Revit restart). A lost memory is not worth
                // an error box - this run still uses the settings, the window just reopens with defaults.
                settingsTracker.Save(settings, out _);

                IList<View> targetViews = ViewCropCommandService.ResolveTargetViews(
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

                if (!ViewCropCommandService.ConfirmBulkRun(commandTitle, targetViews.Count, "annotation crop"))
                    return Result.Cancelled;

                var service = new ViewCropAnnotationService(doc, settings);
                var batch = service.Process(targetViews, $"View Crop - {commandTitle}");

                try
                {
                    ViewCropReportPresenter.ShowSummaryIfNeeded(commandTitle, "Annotation crop", batch);
                }
                catch (Exception summaryEx)
                {
                    string summaryLog = ViewCropReportPresenter.TryWriteErrorLog("AnnotationCrop", commandTitle, null, summaryEx);
                    DialogHelper.ShowError(
                        commandTitle,
                        $"Processing finished, but summary dialog failed.\n{summaryEx.Message}\n\nLog: {summaryLog}");
                }

                return ViewCropCommandService.ToCommandResult(batch);
            }
            catch (Exception ex)
            {
                string logPath = ViewCropReportPresenter.TryWriteErrorLog("AnnotationCrop", commandTitle, null, ex);
                DialogHelper.ShowError(
                    commandTitle,
                    $"{ex.Message}\n\nDetails log:\n{logPath}");
                return Result.Failed;
            }
        }
    }
}
