#region Metadata
/*
 * Tool Name     : Filter Pro
 * File Name     : FilterApplier.cs
 * Purpose       : Applies parameter filters to views with graphic colour overrides, fill pattern
 *                 assignment, halftone, and view-template protection checks.
 *
 * Author        : Ajmal P.S.
 * Version       : 1.0.3
 *
 * Created Date  : 2025-12-10
 * Last Updated  : 2026-10-12
 *
 * Target Revit  : 2020 - latest (A: 2020-2024 / B: 2025-2026 / C: 2027+ - verify newest)
 * Framework     : .NET Fx 4.7.2 (2020) / verify 4.8 (2021-2024) | .NET 8 (2025-2026) | 2027+ verify Autodesk SDK
 * Platform      : C# Revit Add-in
 *
 * Dependencies  : Autodesk Revit API, System.Linq
 *
 * Input         : Active View, FilterSelection (graphics options), ElementId of filter and solid fill pattern
 * Output        : View filter graphics updated — colour, pattern, halftone, visibility
 *
 * Notes         :
 * - Targets Revit 2020 through latest.
 * - 2020 = .NET Fx 4.7.2; 2021-2024 = .NET Fx (verify 4.8 if required); 2025-2026 = .NET 8; 2027+ = verify Autodesk SDK.
 * - OverrideGraphicSettings surface/cut pattern API (SetSurfaceForegroundPatternId etc.) verified
 *   unchanged across Revit 2020-2027 against the per-version RevitAPI reference assemblies (2026-07-23).
 * - Read-only service — all model writes occur inside a Transaction managed by the caller.
 * - Production-ready implementation.
 *
 * Changelog     :
 * v1.0.0 (2025-12-10) - Initial release.
 * v1.0.1 (2026-06-30) - Added mandatory metadata block; confirmed 2020-latest version coverage.
 * v1.0.2 (2026-07-23) - Compatibility audit: every Revit API member used here verified present on
 *                        2020-2027 against the per-version RevitAPI reference assemblies. No code
 *                        changes needed.
 * v1.0.3 (2026-10-12) - Fix: copying a filter's overrides kept only 7 of its 21 settings, so line
 *                        weights/patterns, background patterns, transparency and detail level were
 *                        wiped whenever Filter Pro reordered or recoloured filters. Now a full copy
 *                        (Revit's own copy constructor); applying a colour pattern still makes it visible.
 *
 * License       : All Rights Reserved
 * Repo          : Heron Tools (moved from AJ-Tools on 2026-11-07)
 */
#endregion
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
                skipped?.Add($"View '{view.Name}' uses a view template. Filters were not modified.");
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
        internal static bool IsViewControlledByTemplate(View view)
        {
            if (view == null)
                return false;

            return !view.IsTemplate && view.ViewTemplateId != ElementId.InvalidElementId;
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
                ? ColorPalette.GetRandomColor()
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
