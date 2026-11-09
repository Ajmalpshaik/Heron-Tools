// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Shared summary and error-log presentation for View Crop and Annotation Crop services.

using System;
using System.IO;
using HeronTools.Utils;
using HeronTools.Models.ViewCrop;

namespace HeronTools.Services.ViewCrop
{
    /// <summary>
    /// Centralizes the post-run summary dialog and the error-log writer for View Crop tools.
    /// </summary>
    internal static class ViewCropReportPresenter
    {
        /// <param name="annotationBatch">
        /// The integrated annotation crop's result when it ran after the crop, otherwise null. Its skipped
        /// or failed views also open the summary, since nothing on screen shows that they were left out.
        /// </param>
        internal static void ShowSummaryIfNeeded(
            string title,
            string operationLabel,
            ViewCropBatchResult batch,
            ViewCropBatchResult annotationBatch = null)
        {
            if (batch == null)
                return;

            bool hasAnnotationResults = annotationBatch != null && annotationBatch.TotalSelected > 0;
            bool annotationLeftSomethingOut = hasAnnotationResults
                && (annotationBatch.SkippedCount > 0 || annotationBatch.FailedCount > 0);

            if (batch.SkippedCount == 0 && batch.FailedCount == 0 && !annotationLeftSomethingOut)
                return;

            string mainContent = batch.BuildMainSummary();
            string expandedContent = "Reason summary:\n"
                + batch.BuildReasonSummary()
                + "\n\nDetailed results:\n"
                + batch.BuildDetailedLines(250);

            if (hasAnnotationResults)
            {
                mainContent += "\n\nAnnotation crop (on the views cropped above):\n"
                    + $"Updated successfully: {annotationBatch.UpdatedCount}\n"
                    + $"Skipped: {annotationBatch.SkippedCount}\n"
                    + $"Failed: {annotationBatch.FailedCount}";

                expandedContent += "\n\nAnnotation crop reason summary:\n"
                    + annotationBatch.BuildReasonSummary()
                    + "\n\nAnnotation crop detailed results:\n"
                    + annotationBatch.BuildDetailedLines(250);
            }

            // Heron message: summary in front, the long per-view list behind "Show details".
            DialogHelper.ShowReport(title, $"{operationLabel} processing completed.", mainContent, expandedContent,
                needsAttention: true);
        }

        internal static string TryWriteErrorLog(string logPrefix, string commandTitle, string sourceLabel, Exception ex)
        {
            try
            {
                string fileName = $"HeronTools_{logPrefix}_Error_{DateTime.Now:yyyyMMdd_HHmmss}.log";
                // Heron Tools' own folder (%AppData%\HeronTools\Logs), not %TEMP%.
                string path = AppDataConfigStore.GetPath(Path.Combine("Logs", fileName));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var body = new System.Text.StringBuilder();
                body.AppendLine($"Command: {commandTitle}");
                if (!string.IsNullOrWhiteSpace(sourceLabel))
                    body.AppendLine($"Source: {sourceLabel}");
                body.AppendLine($"Timestamp: {DateTime.Now:O}");
                body.AppendLine();
                body.Append(ex);

                File.WriteAllText(path, body.ToString());
                return path;
            }
            catch
            {
                return "Could not write log file.";
            }
        }
    }
}
