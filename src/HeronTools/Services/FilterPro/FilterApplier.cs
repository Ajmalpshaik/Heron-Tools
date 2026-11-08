// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Applies parameter filters to views with graphic colour overrides, fill pattern assignment, halftone, and view-template protection checks.

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using HeronTools.Models;
using HeronTools.Utils;

namespace HeronTools.Services.FilterPro
{
    /// <summary>
    /// Adds filters to views and writes their colour overrides. Runs inside the caller's transaction.
    /// </summary>
    /// <remarks>
    /// Heron Tools change (2026-11-07): Revit failures are no longer swallowed here. They throw, and the
    /// window rolls the whole run back, so a run is either complete or leaves the model untouched.
    /// Things the user should know but that are not failures (a view controlled by a template, no solid
    /// fill pattern in the project) are still reported as "skipped".
    /// </remarks>
    internal static class FilterApplier
    {
        /// <summary>
        /// Adds a filter to a view (once) and applies the chosen graphics.
        /// </summary>
        internal static void ApplyToView(
            Document doc,
            View view,
            ElementId filterId,
            FilterSelection selection,
            ElementId solidFillId,
            IList<string> skipped)
        {
            if (view == null || filterId == null || filterId == ElementId.InvalidElementId)
                return;

            if (IsViewControlledByTemplate(view))
            {
                skipped?.Add(TemplateSkipMessage(view));
                return;
            }

            if (doc.GetElement(filterId) == null)
                return;

            if (!view.IsFilterApplied(filterId))
                view.AddFilter(filterId);

            ApplyGraphicsToFilter(doc, view, filterId, selection, solidFillId, skipped);
            view.SetFilterVisibility(filterId, true);
        }

        /// <summary>
        /// Applies colour overrides to a filter already added to the view, keeping every other override
        /// the filter has. Returns true when overrides were written to the view.
        /// </summary>
        internal static bool ApplyGraphicsToFilter(
            Document doc,
            View view,
            ElementId filterId,
            FilterSelection selection,
            ElementId solidFillId,
            IList<string> skipped)
        {
            if (selection == null || !selection.ApplyGraphics)
                return false;

            bool applyProjLines = selection.ColorProjectionLines;
            bool applyProjPatterns = selection.ColorProjectionPatterns;
            bool applyCutLines = selection.ColorCutLines;
            bool applyCutPatterns = selection.ColorCutPatterns;
            bool applyHalftone = selection.ColorHalftone;

            if (!applyProjLines && !applyProjPatterns &&
                !applyCutLines && !applyCutPatterns &&
                !applyHalftone)
            {
                return false;
            }

            // Start from the filter's current overrides so nothing the user set is lost.
            var ogs = CloneOverrideGraphics(view.GetFilterOverrides(filterId));

            Color chosenColor = GetColor(selection, filterId);
            ElementId patternId = ResolvePatternId(doc, selection.PatternId, solidFillId);

            if ((applyProjPatterns || applyCutPatterns) && patternId == ElementId.InvalidElementId)
            {
                // A pattern colour without a pattern draws nothing, so say so instead of pretending.
                skipped?.Add("No solid fill pattern was found in this project, so pattern colours were not applied (line colours were).");
                applyProjPatterns = false;
                applyCutPatterns = false;
            }

            if (applyProjLines)
                ogs.SetProjectionLineColor(chosenColor);

            if (applyCutLines)
                ogs.SetCutLineColor(chosenColor);

            // Applying a colour pattern makes it visible, even if the user had hidden that pattern.
            if (applyProjPatterns)
            {
                ogs.SetSurfaceForegroundPatternId(patternId);
                ogs.SetSurfaceForegroundPatternColor(chosenColor);
                ogs.SetSurfaceForegroundPatternVisible(true);
            }

            if (applyCutPatterns)
            {
                ogs.SetCutForegroundPatternId(patternId);
                ogs.SetCutForegroundPatternColor(chosenColor);
                ogs.SetCutForegroundPatternVisible(true);
            }

            if (applyHalftone)
                ogs.SetHalftone(true);

            view.SetFilterOverrides(filterId, ogs);
            return true;
        }

        /// <summary>
        /// A full copy of override graphic settings - every setting a filter can carry - so nothing the
        /// user set is lost when the copy is written back to the view.
        /// </summary>
        internal static OverrideGraphicSettings CloneOverrideGraphics(OverrideGraphicSettings source)
        {
            // Revit's own copy constructor (2020 onward) copies all 21 settings.
            return source == null
                ? new OverrideGraphicSettings()
                : new OverrideGraphicSettings(source);
        }

        /// <summary>True if the view has a view template applied.</summary>
        /// <summary>The one message for a view whose template controls its filters.</summary>
        internal static string TemplateSkipMessage(View view) =>
            $"View '{view.Name}': its view template controls filters, so it was not changed. Edit the template, or untick Filters in it.";

        /// <summary>
        /// True when the view's FILTERS are controlled by its view template, so they cannot be changed in the
        /// view. A template that leaves "V/G Overrides Filters" unticked does not control them, and such a
        /// view is no longer skipped (it used to be, whenever it had any template at all).
        /// </summary>
        internal static bool IsViewControlledByTemplate(View view)
        {
            if (view == null || view.IsTemplate || view.ViewTemplateId == ElementId.InvalidElementId)
                return false;

            if (!(view.Document.GetElement(view.ViewTemplateId) is View template))
                return false;

            var filtersParam = new ElementId(BuiltInParameter.VIS_GRAPHICS_FILTERS);
            bool filtersLeftToView = template.GetNonControlledTemplateParameterIds()
                .Any(id => id == filtersParam);
            return !filtersLeftToView;
        }

        /// <summary>
        /// The project's solid fill DRAFTING pattern, or InvalidElementId when there is none.
        /// A solid MODEL pattern is not used: view overrides expect the drafting one (Heron AI lesson).
        /// </summary>
        internal static ElementId GetSolidFillId(Document doc)
        {
            var solidPattern = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(p =>
                {
                    FillPattern fp = p.GetFillPattern();
                    return fp != null && fp.IsSolidFill && fp.Target == FillPatternTarget.Drafting;
                });

            return solidPattern?.Id ?? ElementId.InvalidElementId;
        }

        private static Color GetColor(FilterSelection selection, ElementId filterId)
        {
            return selection.RandomColors
                ? ColorPalette.GetShuffledColorFor(filterId)
                : ColorPalette.GetColorFor(filterId);
        }

        private static ElementId ResolvePatternId(Document doc, ElementId requested, ElementId solidFillId)
        {
            if (requested != null &&
                requested != ElementId.InvalidElementId &&
                doc.GetElement(requested) is FillPatternElement)
            {
                return requested;
            }

            if (solidFillId != null && solidFillId != ElementId.InvalidElementId)
                return solidFillId;

            return ElementId.InvalidElementId;
        }
    }
}
