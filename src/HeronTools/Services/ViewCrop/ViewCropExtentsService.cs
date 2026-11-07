// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Calculates and applies crop extents for supported plan views.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using HeronTools.Models.ViewCrop;
using HeronTools.Utils;

namespace HeronTools.Services.ViewCrop
{
    /// <summary>
    /// Executes crop fitting for a set of views using either view-scoped or model-wide elements.
    /// </summary>
    internal sealed class ViewCropExtentsService
    {
        private const bool RectangularFallbackAllowed = true;

        private readonly Document _doc;
        private readonly ViewCropSettings _settings;
        private readonly ViewCropExtentSource _source;
        private readonly List<GlobalExtentCandidate> _globalCandidates;

        // Per view being measured: link instance id -> the linked elements the view shows (Revit 2024+ only).
        private Dictionary<long, List<Element>> _linkElementsVisibleInView;

        private sealed class GlobalExtentCandidate
        {
            internal GlobalExtentCandidate(
                long categoryId, 
                XYZ[] corners, 
                WorksetId worksetId, 
                ElementId designOptionId,
                ElementId elementId,
                string elementName,
                string categoryName,
                string linkName)
            {
                CategoryId = categoryId;
                Corners = corners;
                WorksetId = worksetId;
                DesignOptionId = designOptionId;
                ElementId = elementId;
                ElementName = elementName ?? string.Empty;
                CategoryName = categoryName ?? string.Empty;
                LinkName = linkName ?? string.Empty;
            }

            internal long CategoryId { get; }
            internal XYZ[] Corners { get; }
            internal WorksetId WorksetId { get; }
            internal ElementId DesignOptionId { get; }
            internal ElementId ElementId { get; }
            internal string ElementName { get; }
            internal string CategoryName { get; }
            internal string LinkName { get; }
        }

        internal sealed class OutermostElementInfo
        {
            internal string Id { get; set; } = "(none)";
            internal string Name { get; set; } = "(none)";
            internal string Category { get; set; } = "(none)";
            internal string LinkName { get; set; } = null;
            internal double ValueFeet { get; set; }
            internal double ValueMm => ValueFeet * Constants.FEET_TO_MM;
        }

        private sealed class BoundTracker
        {
            internal OutermostElementInfo Left { get; } = new OutermostElementInfo { ValueFeet = double.MaxValue };
            internal OutermostElementInfo Right { get; } = new OutermostElementInfo { ValueFeet = double.MinValue };
            internal OutermostElementInfo Bottom { get; } = new OutermostElementInfo { ValueFeet = double.MaxValue };
            internal OutermostElementInfo Top { get; } = new OutermostElementInfo { ValueFeet = double.MinValue };

            internal void Update(Element element, string linkName, double minX, double maxX, double minY, double maxY)
            {
                string elementIdText = ElementIdHelper.ToReportString(element.Id);
                string categoryName = element.Category?.Name ?? "Unknown";

                if (minX < Left.ValueFeet)
                {
                    Left.ValueFeet = minX;
                    Left.Id = elementIdText;
                    Left.Name = element.Name;
                    Left.Category = categoryName;
                    Left.LinkName = linkName;
                }
                if (maxX > Right.ValueFeet)
                {
                    Right.ValueFeet = maxX;
                    Right.Id = elementIdText;
                    Right.Name = element.Name;
                    Right.Category = categoryName;
                    Right.LinkName = linkName;
                }
                if (minY < Bottom.ValueFeet)
                {
                    Bottom.ValueFeet = minY;
                    Bottom.Id = elementIdText;
                    Bottom.Name = element.Name;
                    Bottom.Category = categoryName;
                    Bottom.LinkName = linkName;
                }
                if (maxY > Top.ValueFeet)
                {
                    Top.ValueFeet = maxY;
                    Top.Id = elementIdText;
                    Top.Name = element.Name;
                    Top.Category = categoryName;
                    Top.LinkName = linkName;
                }
            }

            internal void UpdateFromCandidate(GlobalExtentCandidate candidate, double minX, double maxX, double minY, double maxY)
            {
                string candidateIdText = ElementIdHelper.ToReportString(candidate.ElementId);
                string candidateLink = string.IsNullOrWhiteSpace(candidate.LinkName) ? null : candidate.LinkName;

                if (minX < Left.ValueFeet)
                {
                    Left.ValueFeet = minX;
                    Left.Id = candidateIdText;
                    Left.Name = candidate.ElementName;
                    Left.Category = candidate.CategoryName;
                    Left.LinkName = candidateLink;
                }
                if (maxX > Right.ValueFeet)
                {
                    Right.ValueFeet = maxX;
                    Right.Id = candidateIdText;
                    Right.Name = candidate.ElementName;
                    Right.Category = candidate.CategoryName;
                    Right.LinkName = candidateLink;
                }
                if (minY < Bottom.ValueFeet)
                {
                    Bottom.ValueFeet = minY;
                    Bottom.Id = candidateIdText;
                    Bottom.Name = candidate.ElementName;
                    Bottom.Category = candidate.CategoryName;
                    Bottom.LinkName = candidateLink;
                }
                if (maxY > Top.ValueFeet)
                {
                    Top.ValueFeet = maxY;
                    Top.Id = candidateIdText;
                    Top.Name = candidate.ElementName;
                    Top.Category = candidate.CategoryName;
                    Top.LinkName = candidateLink;
                }
            }

            internal string GenerateReport(double finalMarginMm)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== CROP BOUNDARY DIAGNOSTICS ===");
                sb.AppendLine($"Applied Crop Margin: {finalMarginMm} mm");
                sb.AppendLine();
                
                sb.AppendLine("--- OUTERMOST LEFT ELEMENT (Min X) ---");
                sb.AppendLine($"Element ID : {Left.Id}");
                sb.AppendLine($"Name       : {Left.Name}");
                sb.AppendLine($"Category   : {Left.Category}");
                if (Left.LinkName != null) sb.AppendLine($"Revit Link : {Left.LinkName}");
                sb.AppendLine($"Coord (X)  : {Left.ValueMm:0.##} mm");
                sb.AppendLine();

                sb.AppendLine("--- OUTERMOST RIGHT ELEMENT (Max X) ---");
                sb.AppendLine($"Element ID : {Right.Id}");
                sb.AppendLine($"Name       : {Right.Name}");
                sb.AppendLine($"Category   : {Right.Category}");
                if (Right.LinkName != null) sb.AppendLine($"Revit Link : {Right.LinkName}");
                sb.AppendLine($"Coord (X)  : {Right.ValueMm:0.##} mm");
                sb.AppendLine();

                sb.AppendLine("--- OUTERMOST BOTTOM ELEMENT (Min Y) ---");
                sb.AppendLine($"Element ID : {Bottom.Id}");
                sb.AppendLine($"Name       : {Bottom.Name}");
                sb.AppendLine($"Category   : {Bottom.Category}");
                if (Bottom.LinkName != null) sb.AppendLine($"Revit Link : {Bottom.LinkName}");
                sb.AppendLine($"Coord (Y)  : {Bottom.ValueMm:0.##} mm");
                sb.AppendLine();

                sb.AppendLine("--- OUTERMOST TOP ELEMENT (Max Y) ---");
                sb.AppendLine($"Element ID : {Top.Id}");
                sb.AppendLine($"Name       : {Top.Name}");
                sb.AppendLine($"Category   : {Top.Category}");
                if (Top.LinkName != null) sb.AppendLine($"Revit Link : {Top.LinkName}");
                sb.AppendLine($"Coord (Y)  : {Top.ValueMm:0.##} mm");
                sb.AppendLine("=================================");

                return sb.ToString();
            }
        }

        internal ViewCropExtentsService(Document doc, ViewCropSettings settings, ViewCropExtentSource source)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _settings = (settings ?? new ViewCropSettings()).Clone();
            _source = source;

            _globalCandidates = source == ViewCropExtentSource.AllModelElements
                ? CollectAllModelCandidates()
                : new List<GlobalExtentCandidate>();
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
                        using (Transaction tx = new Transaction(_doc, transactionName))
                        {
                            tx.Start();

                            ViewCropResultState state = ProcessSingleView(view, item);

                            if (state == ViewCropResultState.Updated)
                            {
                                if (tx.Commit() != TransactionStatus.Committed)
                                    item.MarkFailed("Revit did not keep the change (the view may be owned by another user, or a warning stopped it).");
                            }
                            else
                            {
                                tx.RollBack();
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

        private ViewCropResultState ProcessSingleView(View view, ViewCropViewResult item)
        {
            string reason;
            if (!TryEnsureEditableCrop(view, out reason))
            {
                item.MarkSkipped(reason);
                return ViewCropResultState.Skipped;
            }

            var tracker = new BoundTracker();
            ViewCropGeometryProjectionHelper.PlaneBounds bounds = _source == ViewCropExtentSource.ActiveViewElements
                ? CollectBoundsFromActiveViewElements(view, tracker)
                : CollectBoundsFromAllModelElements(view, tracker);

            if (bounds == null || !bounds.HasData)
            {
                reason = _source == ViewCropExtentSource.ActiveViewElements
                    ? "No valid visible model elements found in this view."
                    : "No valid model elements found for this view orientation.";
                item.MarkSkipped(reason);
                return ViewCropResultState.Skipped;
            }

            // Save the diagnostic report inside the processed view item
            item.DiagnosticReport = tracker.GenerateReport(_settings.MarginMm);

            bounds.Inflate(Math.Max(0, _settings.MarginInternal));
            bounds.EnsureMinimumSpan(5.0 * Constants.MM_TO_FEET);

            if (!TryApplyCrop(view, bounds, out reason))
            {
                item.MarkFailed(reason);
                return ViewCropResultState.Failed;
            }

            item.MarkUpdated("Updated crop successfully.");
            return ViewCropResultState.Updated;
        }

        private bool TryEnsureEditableCrop(View view, out string reason)
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

            Parameter scopeBoxParam = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if (scopeBoxParam != null && scopeBoxParam.HasValue)
            {
                ElementId scopeBoxId = scopeBoxParam.AsElementId();
                if (ElementIdHelper.IsValid(scopeBoxId))
                {
                    reason = "Scope box is assigned. Remove scope box control before running this tool.";
                    return false;
                }
            }

            Parameter cropActiveParam = view.get_Parameter(BuiltInParameter.VIEWER_CROP_REGION);
            ElementId templateId = view.ViewTemplateId;
            if (ElementIdHelper.IsValid(templateId) && cropActiveParam != null && cropActiveParam.IsReadOnly)
            {
                reason = "View template controls crop settings for this view.";
                return false;
            }

            if (!view.CropBoxActive)
            {
                try
                {
                    view.CropBoxActive = true;
                }
                catch (Exception ex)
                {
                    reason = $"Crop View is disabled and cannot be enabled: {ex.Message}";
                    return false;
                }
            }

            return true;
        }

        private ViewCropGeometryProjectionHelper.PlaneBounds CollectBoundsFromActiveViewElements(View view, BoundTracker tracker)
        {
            Transform modelToView = ViewCropGeometryProjectionHelper.GetModelToViewTransform(view);
            var bounds = new ViewCropGeometryProjectionHelper.PlaneBounds();
            bool hasData = false;

            Dictionary<long, bool> hiddenCategoryCache = _settings.IgnoreHiddenCategories
                ? new Dictionary<long, bool>()
                : null;

            // A collector on a view returns only what is inside the CURRENT crop, so measuring with the crop on
            // could only ever shrink it. The crop is switched off just for the measuring (inside this view's
            // transaction) and back on straight after.
            bool cropWasOn = view.CropBoxActive;
            List<Element> elements;
            if (cropWasOn)
            {
                view.CropBoxActive = false;
                _doc.Regenerate();
            }
            try
            {
                elements = new FilteredElementCollector(_doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .ToList();
                _linkElementsVisibleInView = new Dictionary<long, List<Element>>();
#if REVIT2024_OR_GREATER
                // Revit 2024+ can say which LINKED elements this view shows; collect them now, uncropped.
                foreach (RevitLinkInstance link in elements.OfType<RevitLinkInstance>())
                {
                    if (link.GetLinkDocument() == null) continue;
                    _linkElementsVisibleInView[link.Id.LongValue()] =
                        new FilteredElementCollector(_doc, view.Id, link.Id).WhereElementIsNotElementType().ToElements().ToList();
                }
#endif
            }
            finally
            {
                if (cropWasOn)
                    view.CropBoxActive = true;
            }

            foreach (Element element in elements)
            {
                if (element is RevitLinkInstance linkInstance)
                {
                    if (!_settings.IncludeRevitLinks)
                        continue;

                    CollectBoundsFromRevitLink(linkInstance, view, modelToView, bounds, ref hasData, hiddenCategoryCache, tracker);
                    continue;
                }

                if (!ShouldUseElement(element, view, hiddenCategoryCache))
                    continue;

                BoundingBoxXYZ bbox = null;
                try
                {
                    bbox = element.get_BoundingBox(view);
                }
                catch
                {
                    continue;
                }

                if (!IsValidBoundingBox(bbox))
                    continue;

                XYZ[] corners = ViewCropGeometryProjectionHelper.GetBoundingBoxCorners(bbox);
                double elMinX = double.MaxValue;
                double elMaxX = double.MinValue;
                double elMinY = double.MaxValue;
                double elMaxY = double.MinValue;
                bool elHasData = false;

                for (int i = 0; i < corners.Length; i++)
                {
                    XYZ local = modelToView.OfPoint(corners[i]);
                    if (!ViewCropGeometryProjectionHelper.IsFinite(local))
                        continue;

                    if (local.X < elMinX) elMinX = local.X;
                    if (local.X > elMaxX) elMaxX = local.X;
                    if (local.Y < elMinY) elMinY = local.Y;
                    if (local.Y > elMaxY) elMaxY = local.Y;
                    elHasData = true;
                }

                if (elHasData)
                {
                    bounds.Include(elMinX, elMinY);
                    bounds.Include(elMaxX, elMaxY);
                    tracker.Update(element, null, elMinX, elMaxX, elMinY, elMaxY);
                    hasData = true;
                }
            }

            return hasData ? bounds : null;
        }

        private void CollectBoundsFromRevitLink(
            RevitLinkInstance linkInstance,
            View view,
            Transform modelToView,
            ViewCropGeometryProjectionHelper.PlaneBounds bounds,
            ref bool hasData,
            Dictionary<long, bool> hiddenCategoryCache,
            BoundTracker tracker)
        {
            Document linkDoc = linkInstance.GetLinkDocument();
            if (linkDoc == null)
                return;

            Transform linkTransform = linkInstance.GetTransform();
            Transform linkToView = modelToView.Multiply(linkTransform);

            // Revit 2024+: only the linked elements this view actually shows (collected uncropped above). Older
            // releases cannot ask that, so the whole link is measured there (a large linked site can still
            // widen the crop on 2020-2023 - untick "Revit links" for those views).
            IEnumerable<Element> elements =
                _linkElementsVisibleInView != null &&
                _linkElementsVisibleInView.TryGetValue(linkInstance.Id.LongValue(), out List<Element> visibleInView)
                    ? visibleInView
                    : new FilteredElementCollector(linkDoc).WhereElementIsNotElementType().ToElements();

            foreach (Element element in elements)
            {
                if (element.ViewSpecific)
                    continue;

                Category category = element.Category;
                if (category == null || category.Id == null)
                    continue;

                if (category.CategoryType != CategoryType.Model)
                    continue;

                long categoryId = (category.Id).LongValue();
                if (categoryId == (int)BuiltInCategory.OST_Coordination_Model)
                    continue;

                if (IsExcludedCategory(categoryId))
                    continue;

                if (!_settings.IncludeDatums && IsDatumCategory(categoryId))
                    continue;

                if (_settings.IgnoreHiddenCategories && IsCategoryHidden(view, categoryId, hiddenCategoryCache))
                    continue;

                DesignOption designOption = element.DesignOption;
                if (designOption != null)
                {
                    ElementId activeOptId = DesignOption.GetActiveDesignOptionId(linkDoc);
                    if (activeOptId != ElementId.InvalidElementId)
                    {
                        if (designOption.Id != activeOptId)
                            continue;
                    }
                    else
                    {
                        if (!designOption.IsPrimary)
                            continue;
                    }
                }

                BoundingBoxXYZ bbox = null;
                try
                {
                    bbox = element.get_BoundingBox(null);
                }
                catch
                {
                    continue;
                }

                if (!IsValidBoundingBox(bbox))
                    continue;

                XYZ[] corners = ViewCropGeometryProjectionHelper.GetBoundingBoxCorners(bbox);
                double elMinX = double.MaxValue;
                double elMaxX = double.MinValue;
                double elMinY = double.MaxValue;
                double elMaxY = double.MinValue;
                bool elHasData = false;

                for (int i = 0; i < corners.Length; i++)
                {
                    XYZ local = linkToView.OfPoint(corners[i]);
                    if (!ViewCropGeometryProjectionHelper.IsFinite(local))
                        continue;

                    if (local.X < elMinX) elMinX = local.X;
                    if (local.X > elMaxX) elMaxX = local.X;
                    if (local.Y < elMinY) elMinY = local.Y;
                    if (local.Y > elMaxY) elMaxY = local.Y;
                    elHasData = true;
                }

                if (elHasData)
                {
                    bounds.Include(elMinX, elMinY);
                    bounds.Include(elMaxX, elMaxY);
                    tracker.Update(element, linkInstance.Name, elMinX, elMaxX, elMinY, elMaxY);
                    hasData = true;
                }
            }
        }

        private ViewCropGeometryProjectionHelper.PlaneBounds CollectBoundsFromAllModelElements(View view, BoundTracker tracker)
        {
            if (_globalCandidates.Count == 0)
                return null;

            Transform modelToView = ViewCropGeometryProjectionHelper.GetModelToViewTransform(view);
            var bounds = new ViewCropGeometryProjectionHelper.PlaneBounds();
            bool hasData = false;

            Dictionary<long, bool> hiddenCategoryCache = _settings.IgnoreHiddenCategories
                ? new Dictionary<long, bool>()
                : null;

            foreach (GlobalExtentCandidate candidate in _globalCandidates)
            {
                if (_settings.IgnoreHiddenCategories
                    && IsCategoryHidden(view, candidate.CategoryId, hiddenCategoryCache))
                {
                    continue;
                }

                if (_doc.IsWorkshared && candidate.WorksetId != WorksetId.InvalidWorksetId && !view.IsWorksetVisible(candidate.WorksetId))
                {
                    continue;
                }

                if (candidate.DesignOptionId != ElementId.InvalidElementId)
                {
                    ElementId activeOptId = DesignOption.GetActiveDesignOptionId(_doc);
                    if (activeOptId != ElementId.InvalidElementId)
                    {
                        if (candidate.DesignOptionId != activeOptId)
                            continue;
                    }
                    else
                    {
                        var opt = _doc.GetElement(candidate.DesignOptionId) as DesignOption;
                        if (opt == null || !opt.IsPrimary)
                            continue;
                    }
                }

                XYZ[] corners = candidate.Corners;
                double elMinX = double.MaxValue;
                double elMaxX = double.MinValue;
                double elMinY = double.MaxValue;
                double elMaxY = double.MinValue;
                bool elHasData = false;

                for (int i = 0; i < corners.Length; i++)
                {
                    XYZ local;
                    try
                    {
                        local = modelToView.OfPoint(corners[i]);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!ViewCropGeometryProjectionHelper.IsFinite(local))
                        continue;

                    if (local.X < elMinX) elMinX = local.X;
                    if (local.X > elMaxX) elMaxX = local.X;
                    if (local.Y < elMinY) elMinY = local.Y;
                    if (local.Y > elMaxY) elMaxY = local.Y;
                    elHasData = true;
                }

                if (elHasData)
                {
                    bounds.Include(elMinX, elMinY);
                    bounds.Include(elMaxX, elMaxY);
                    tracker.UpdateFromCandidate(candidate, elMinX, elMaxX, elMinY, elMaxY);
                    hasData = true;
                }
            }

            return hasData ? bounds : null;
        }

        private List<GlobalExtentCandidate> CollectAllModelCandidates()
        {
            var candidates = new List<GlobalExtentCandidate>();

            IEnumerable<Element> elements = new FilteredElementCollector(_doc)
                .WhereElementIsNotElementType()
                .WhereElementIsViewIndependent()
                .ToElements();

            foreach (Element element in elements)
            {
                if (element is RevitLinkInstance linkInstance)
                {
                    if (!_settings.IncludeRevitLinks)
                        continue;

                    Document linkDoc = linkInstance.GetLinkDocument();
                    if (linkDoc == null)
                        continue;

                    Transform linkTransform = linkInstance.GetTransform();

                    IEnumerable<Element> linkedElements = new FilteredElementCollector(linkDoc)
                        .WhereElementIsNotElementType()
                        .ToElements();

                    foreach (Element linkedElement in linkedElements)
                    {
                        if (linkedElement.ViewSpecific)
                            continue;

                        Category category = linkedElement.Category;
                        if (category == null || category.Id == null)
                            continue;

                        if (category.CategoryType != CategoryType.Model)
                            continue;

                        long categoryId = (category.Id).LongValue();
                        if (categoryId == (int)BuiltInCategory.OST_Coordination_Model)
                            continue;

                        if (IsExcludedCategory(categoryId))
                            continue;

                        if (!_settings.IncludeDatums && IsDatumCategory(categoryId))
                            continue;

                        // Filter linked element design options
                        DesignOption designOption = linkedElement.DesignOption;
                        if (designOption != null)
                        {
                            ElementId activeOptId = DesignOption.GetActiveDesignOptionId(linkDoc);
                            if (activeOptId != ElementId.InvalidElementId)
                            {
                                if (designOption.Id != activeOptId)
                                    continue;
                            }
                            else
                            {
                                if (!designOption.IsPrimary)
                                    continue;
                            }
                        }

                        BoundingBoxXYZ linkedBbox = null;
                        try
                        {
                            linkedBbox = linkedElement.get_BoundingBox(null);
                        }
                        catch
                        {
                            continue;
                        }

                        if (!IsValidBoundingBox(linkedBbox))
                            continue;

                        XYZ[] localCorners = ViewCropGeometryProjectionHelper.GetBoundingBoxCorners(linkedBbox);
                        if (localCorners.Length == 0)
                            continue;

                        XYZ[] worldCorners = new XYZ[localCorners.Length];
                        for (int i = 0; i < localCorners.Length; i++)
                        {
                            worldCorners[i] = linkTransform.OfPoint(localCorners[i]);
                        }

                        candidates.Add(new GlobalExtentCandidate(
                            categoryId,
                            worldCorners,
                            linkInstance.WorksetId,
                            ElementId.InvalidElementId,
                            linkedElement.Id,
                            linkedElement.Name,
                            category.Name,
                            linkInstance.Name));
                    }

                    continue;
                }

                if (!ShouldUseElement(element, null, null))
                    continue;

                BoundingBoxXYZ bbox;
                try
                {
                    bbox = element.get_BoundingBox(null);
                }
                catch
                {
                    continue;
                }

                if (!IsValidBoundingBox(bbox))
                    continue;

                XYZ[] corners = ViewCropGeometryProjectionHelper.GetBoundingBoxCorners(bbox);
                if (corners.Length == 0)
                    continue;

                Category cat = element.Category;
                if (cat == null || cat.Id == null)
                    continue;

                long catId = (cat.Id).LongValue();
                candidates.Add(new GlobalExtentCandidate(
                    catId,
                    corners,
                    element.WorksetId,
                    element.DesignOption != null ? element.DesignOption.Id : ElementId.InvalidElementId,
                    element.Id,
                    element.Name,
                    cat.Name,
                    null));
            }

            return candidates;
        }

        private bool ShouldUseElement(Element element, View view, Dictionary<long, bool> hiddenCategoryCache)
        {
            if (element == null || !element.IsValidObject)
                return false;

            if (element.ViewSpecific)
                return false;

            if (!_settings.IncludeDatums && element is DatumPlane)
                return false;

            if (element is SketchPlane || element is ImportInstance || element is Viewport)
                return false;

            Category category = element.Category;
            if (category == null || category.Id == null)
                return false;

            if (element is RevitLinkInstance)
            {
                if (!_settings.IncludeRevitLinks)
                    return false;
            }
            else if (category.CategoryType != CategoryType.Model)
            {
                return false;
            }

            long categoryId = (category.Id).LongValue();
            if (categoryId == (int)BuiltInCategory.OST_Coordination_Model)
            {
                if (!_settings.IncludeCoordinationModels)
                    return false;
            }

            if (IsExcludedCategory(categoryId))
                return false;

            if (!_settings.IncludeDatums && IsDatumCategory(categoryId))
                return false;

            if (_settings.IgnoreHiddenCategories && view != null)
            {
                if (IsCategoryHidden(view, categoryId, hiddenCategoryCache))
                    return false;

                try
                {
                    if (element.IsHidden(view))
                        return false;
                }
                catch
                {
                    // If Revit cannot evaluate element-level hide state, keep the element.
                }
            }

            return true;
        }

        private bool IsCategoryHidden(View view, long categoryId, Dictionary<long, bool> cache)
        {
            if (!_settings.IgnoreHiddenCategories || view == null || cache == null)
                return false;

            if (cache.TryGetValue(categoryId, out bool hidden))
                return hidden;

            hidden = false;
            try
            {
                ElementId categoryElementId = ElementIdHelper.FromLong(categoryId);
                if (view.CanCategoryBeHidden(categoryElementId))
                    hidden = view.GetCategoryHidden(categoryElementId);
            }
            catch
            {
                hidden = false;
            }

            cache[categoryId] = hidden;
            return hidden;
        }

        private bool TryApplyCrop(View view, ViewCropGeometryProjectionHelper.PlaneBounds bounds, out string reason)
        {
            reason = string.Empty;
            BoundingBoxXYZ currentCrop = view.CropBox;
            if (currentCrop == null)
            {
                reason = "View crop box is unavailable.";
                return false;
            }

            ViewCropRegionShapeManager shapeManager = view.GetCropRegionShapeManager();
            if (shapeManager != null && shapeManager.ShapeSet && !_settings.RectangularCropOnly)
            {
                reason = "This view has a non-rectangular crop shape. It was left as it is so the shape is not lost - " +
                         "use Reset Crop in Revit first if you want a rectangular crop fitted.";
                return false;
            }

            if (_settings.RectangularCropOnly)
            {
                if (TrySetRectangularCrop(view, currentCrop, bounds, out string shapeError))
                    return true;

                if (TrySetCropBox(view, currentCrop, bounds, out string boxFallbackError))
                {
                    reason = "Rectangular crop fallback used CropBox update.";
                    return true;
                }

                reason = CombineErrors(shapeError, boxFallbackError);
                return false;
            }

            if (TrySetCropBox(view, currentCrop, bounds, out string boxError))
                return true;

            string shapeFallbackError = string.Empty;
            if (RectangularFallbackAllowed && TrySetRectangularCrop(view, currentCrop, bounds, out shapeFallbackError))
            {
                reason = "Applied rectangular fallback because CropBox update failed.";
                return true;
            }

            reason = CombineErrors(boxError, shapeFallbackError);
            return false;
        }

        private static bool TrySetCropBox(
            View view,
            BoundingBoxXYZ currentCrop,
            ViewCropGeometryProjectionHelper.PlaneBounds bounds,
            out string error)
        {
            error = string.Empty;

            try
            {
                var newCrop = new BoundingBoxXYZ
                {
                    Transform = currentCrop.Transform,
                    Min = new XYZ(bounds.MinX, bounds.MinY, currentCrop.Min.Z),
                    Max = new XYZ(bounds.MaxX, bounds.MaxY, currentCrop.Max.Z)
                };

                view.CropBox = newCrop;

                BoundingBoxXYZ written = view.CropBox;
                const double tolerance = 1e-4;
                if (written == null ||
                    Math.Abs(written.Min.X - newCrop.Min.X) > tolerance || Math.Abs(written.Min.Y - newCrop.Min.Y) > tolerance ||
                    Math.Abs(written.Max.X - newCrop.Max.X) > tolerance || Math.Abs(written.Max.Y - newCrop.Max.Y) > tolerance)
                {
                    error = "Revit did not take the new crop for this view.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"CropBox update failed: {ex.Message}";
                return false;
            }
        }

        private static bool TrySetRectangularCrop(
            View view,
            BoundingBoxXYZ currentCrop,
            ViewCropGeometryProjectionHelper.PlaneBounds bounds,
            out string error)
        {
            error = string.Empty;

            try
            {
                ViewCropRegionShapeManager manager = view.GetCropRegionShapeManager();
                if (manager == null)
                {
                    error = "Crop region shape manager is not available.";
                    return false;
                }

                CurveLoop loop = ViewCropGeometryProjectionHelper.BuildRectangularCropLoop(currentCrop, bounds);

                bool isShapeValid = true;
                try
                {
                    isShapeValid = manager.IsCropRegionShapeValid(loop);
                }
                catch
                {
                    // If Revit does not expose validation, attempt SetCropShape directly.
                    isShapeValid = true;
                }

                if (!isShapeValid)
                {
                    error = "Computed rectangular crop shape is invalid.";
                    return false;
                }

                manager.SetCropShape(loop);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Crop shape update failed: {ex.Message}";
                return false;
            }
        }

        private static bool IsDatumCategory(long categoryId)
        {
            return categoryId == (int)BuiltInCategory.OST_Levels
                || categoryId == (int)BuiltInCategory.OST_Grids
                || categoryId == (int)BuiltInCategory.OST_CLines;
        }

        private static bool IsExcludedCategory(long categoryId)
        {
            return categoryId == (int)BuiltInCategory.OST_Cameras
                || categoryId == (int)BuiltInCategory.OST_Sheets
                || categoryId == (int)BuiltInCategory.OST_TitleBlocks
                || categoryId == (int)BuiltInCategory.OST_Views;
        }

        private static bool IsValidBoundingBox(BoundingBoxXYZ bbox)
        {
            if (bbox == null || bbox.Min == null || bbox.Max == null)
                return false;

            XYZ min = bbox.Min;
            XYZ max = bbox.Max;
            if (!ViewCropGeometryProjectionHelper.IsFinite(min) || !ViewCropGeometryProjectionHelper.IsFinite(max))
                return false;

            double dx = Math.Abs(max.X - min.X);
            double dy = Math.Abs(max.Y - min.Y);
            double dz = Math.Abs(max.Z - min.Z);

            return dx > Constants.ZERO_LENGTH_TOLERANCE
                || dy > Constants.ZERO_LENGTH_TOLERANCE
                || dz > Constants.ZERO_LENGTH_TOLERANCE;
        }

        private static string CombineErrors(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first))
                return string.IsNullOrWhiteSpace(second) ? "Crop update failed." : second;

            if (string.IsNullOrWhiteSpace(second))
                return first;

            return $"{first} | {second}";
        }

    }
}
