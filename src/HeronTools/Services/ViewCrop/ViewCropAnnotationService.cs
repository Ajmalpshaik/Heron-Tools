// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Enables annotation crop and applies equal offsets based on the view crop.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using HeronTools.Models.ViewCrop;
using HeronTools.Utils;

namespace HeronTools.Services.ViewCrop
{
    /// <summary>
    /// Applies annotation crop activation and offset changes for a set of views.
    /// </summary>
    internal sealed class ViewCropAnnotationService
    {
        private readonly Document _doc;
        private readonly ViewCropAnnotationSettings _settings;

        internal ViewCropAnnotationService(Document doc, ViewCropAnnotationSettings settings)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _settings = (settings ?? new ViewCropAnnotationSettings()).Clone();
        }

        internal ViewCropBatchResult Process(IList<View> targetViews, string transactionName)
        {
            var batch = new ViewCropBatchResult();
            if (targetViews == null || targetViews.Count == 0)
                return batch;

            using (var group = new TransactionGroup(_doc, transactionName))
            {
                group.Start();

                foreach (View view in targetViews)
                {
                    var item = new ViewCropViewResult(
                        view?.Id ?? ElementId.InvalidElementId,
                        view?.Name ?? "(missing)",
                        ViewCropViewSupport.ToFriendlyTypeName(view));
                    batch.Add(item);

                    if (!ViewCropViewSupport.TryValidateType(view, out string typeError))
                    {
                        item.MarkSkipped(typeError);
                        continue;
                    }

                    try
                    {
                        using (var tx = new Transaction(_doc, transactionName))
                        {
                            tx.Start();

                            ViewCropResultState state = ProcessSingleView(view, out string reason);
                            if (state == ViewCropResultState.Updated)
                            {
                                if (tx.Commit() == TransactionStatus.Committed)
                                    item.MarkUpdated(reason);
                                else
                                    item.MarkFailed("Revit did not keep the change (the view may be owned by another user, or a warning stopped it).");
                            }
                            else
                            {
                                tx.RollBack();
                                if (state == ViewCropResultState.Skipped)
                                    item.MarkSkipped(reason);
                                else
                                    item.MarkFailed(reason);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        item.MarkFailed($"Unexpected error: {ex.Message}");
                    }
                }

                if (batch.UpdatedCount > 0)
                    group.Assimilate();
                else
                    group.RollBack();
            }

            return batch;
        }

        private ViewCropResultState ProcessSingleView(View view, out string reason)
        {
            reason = string.Empty;

            if (!TryEnsureEditableCrop(view, out reason))
                return ViewCropResultState.Skipped;

            ViewCropRegionShapeManager manager;
            try
            {
                manager = view.GetCropRegionShapeManager();
            }
            catch (Exception ex)
            {
                reason = $"Could not access crop region manager: {ex.Message}";
                return ViewCropResultState.Failed;
            }

            if (manager == null)
            {
                reason = "Crop region manager is not available for this view.";
                return ViewCropResultState.Skipped;
            }

            if (!manager.CanHaveAnnotationCrop)
            {
                reason = "Annotation crop is not available for this view type.";
                return ViewCropResultState.Skipped;
            }

            Parameter annotationCropParam = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
            if (annotationCropParam == null)
            {
                reason = "This view does not expose an annotation crop parameter.";
                return ViewCropResultState.Skipped;
            }

            if (annotationCropParam.IsReadOnly)
            {
                reason = "View template controls annotation crop settings for this view.";
                return ViewCropResultState.Skipped;
            }

            try
            {
                if (annotationCropParam.AsInteger() != 1)
                    annotationCropParam.Set(1);
            }
            catch (Exception ex)
            {
                reason = $"Could not enable annotation crop: {ex.Message}";
                return ViewCropResultState.Failed;
            }

            double offset = _settings.OffsetInternal;   // on paper, at least Revit's 1/8" minimum
            try
            {
                manager.LeftAnnotationCropOffset = offset;
                manager.RightAnnotationCropOffset = offset;
                manager.TopAnnotationCropOffset = offset;
                manager.BottomAnnotationCropOffset = offset;
            }
            catch (Exception ex)
            {
                reason = $"Could not set annotation crop offsets: {ex.Message}";
                return ViewCropResultState.Failed;
            }

            reason = $"Annotation crop on, {Math.Max(ViewCropAnnotationSettings.MinimumOffsetMm, _settings.OffsetMm):0.##} mm outside the crop on paper.";
            return ViewCropResultState.Updated;
        }

        private static bool TryEnsureEditableCrop(View view, out string reason)
        {
            reason = string.Empty;

            if (view == null || !view.IsValidObject)
            {
                reason = "View is not valid.";
                return false;
            }

            if (view.CropBox == null)
            {
                reason = "This view does not expose an editable crop box.";
                return false;
            }

            Parameter cropActiveParam = view.get_Parameter(BuiltInParameter.VIEWER_CROP_REGION);
            ElementId templateId = view.ViewTemplateId;
            if (ElementIdHelper.IsValid(templateId) && cropActiveParam != null && cropActiveParam.IsReadOnly)
            {
                reason = "View template controls crop settings for this view.";
                return false;
            }

            // Crop View off: the annotation crop sits around the crop, and switching an old, forgotten crop on
            // would hide part of the plan without warning. Say so instead.
            if (!view.CropBoxActive)
            {
                reason = "Crop View is off in this view. Turn on Crop View first - the annotation crop sits around it.";
                return false;
            }

            return true;
        }

    }
}
