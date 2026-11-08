// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Orchestrates the Colorize apply step - for each selected parameter value (or, if no parameter is selected, for the selected categories as a whole), finds the matching elements in the target view(s) and overrides them directly. No ParameterFilterElement is ever created; this is the "colorize, don't filter" counterpart to Filter Pro's FilterCreator + FilterApplier.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using HeronTools.Models;
using HeronTools.Models.GraphicsTools;
using HeronTools.Services.FilterPro;
using HeronTools.Services.GraphicsTools;
using HeronTools.Utils;

namespace HeronTools.Services.Colorize
{
    /// <summary>
    /// Applies Colorize overrides directly to matched elements - no saved filter is created.
    /// </summary>
    internal static class ColorizeApplier
    {
        /// <summary>
        /// Applies Colorize to every view in <paramref name="views"/>, merging the results into a
        /// single summary so the caller can commit/roll back the whole multi-view apply as one step.
        /// </summary>
        internal static GraphicsOperationSummary ApplyColorizeToViews(
            Document doc,
            IEnumerable<View> views,
            FilterSelection selection,
            IList<string> skipped)
        {
            var summary = new GraphicsOperationSummary();

            if (doc == null || views == null || selection == null)
            {
                return summary;
            }

            // Colors are resolved ONCE for the whole multi-view apply so the same parameter value gets
            // the same color in every target view, instead of RandomColors rolling a fresh color per view.
            IList<Color> colors = ResolveColors(selection);

            foreach (View view in views)
            {
                if (view == null)
                {
                    continue;
                }

                ColorizeMemory.DropIfStale(doc, view);   // an undone earlier run must not keep its old originals
                MergeSummary(summary, ApplyColorize(doc, view, selection, colors, skipped));
            }

            return summary;
        }

        private static IList<Color> ResolveColors(FilterSelection selection)
        {
            var colors = new List<Color>();
            if (selection == null)
            {
                return colors;
            }

            bool hasParameterValues = selection.Parameter != null &&
                                      selection.Values != null &&
                                      selection.Values.Count > 0;

            // One shuffled deck per run: no two values share a colour until all 20 are used (random picks
            // with repeats often gave two values the same colour).
            if (selection.RandomColors)
                ColorPalette.BeginShuffle();

            if (!hasParameterValues)
            {
                // Whole categories: one colour for each picked category.
                int count = Math.Max(1, selection.CategoryIds?.Count ?? 1);
                for (int i = 0; i < count; i++)
                    colors.Add(selection.RandomColors ? ColorPalette.GetShuffledColorAt(i) : ColorPalette.GetColorAt(i));
                return colors;
            }

            for (int i = 0; i < selection.Values.Count; i++)
            {
                colors.Add(selection.RandomColors ? ColorPalette.GetShuffledColorAt(i) : ColorPalette.GetColorAt(i));
            }

            return colors;
        }

        private static GraphicsOperationSummary ApplyColorize(
            Document doc,
            View view,
            FilterSelection selection,
            IList<Color> colors,
            IList<string> skipped)
        {
            var summary = new GraphicsOperationSummary();

            if (doc == null || view == null || selection == null)
            {
                return summary;
            }

            IList<ElementId> categoryIds = selection.CategoryIds;
            if (categoryIds == null || categoryIds.Count == 0)
            {
                skipped?.Add("No categories selected for Colorize.");
                return summary;
            }

            if (!HasAnyGraphicsToggleEnabled(selection))
            {
                skipped?.Add("No graphics options (lines, patterns, or halftone) were enabled.");
                return summary;
            }

            ElementId solidFillId = FilterApplier.GetSolidFillId(doc);
            ElementId patternId = ResolvePatternId(doc, selection.PatternId, solidFillId);

            bool hasParameterValues = selection.Parameter != null &&
                                      selection.Values != null &&
                                      selection.Values.Count > 0;

            if (!hasParameterValues)
            {
                ApplyCategoryOnly(doc, view, selection, categoryIds, patternId, colors, summary, skipped);
                return summary;
            }

            ApplyPerValue(doc, view, selection, categoryIds, patternId, colors, summary, skipped);
            return summary;
        }

        private static void ApplyCategoryOnly(
            Document doc,
            View view,
            FilterSelection selection,
            IList<ElementId> categoryIds,
            ElementId patternId,
            IList<Color> colors,
            GraphicsOperationSummary summary,
            IList<string> skipped)
        {
            // One colour per category (it used to paint every picked category the same colour).
            bool anyMatched = false;
            for (int i = 0; i < categoryIds.Count; i++)
            {
                ICollection<ElementId> matched = ColorizeElementMatcher.GetMatchingElementIds(
                    doc, view, new List<ElementId> { categoryIds[i] }, null);
                if (matched.Count == 0)
                    continue;

                anyMatched = true;
                Color color = colors != null && i < colors.Count ? colors[i] : ColorPalette.GetColorAt(i);
                OverrideGraphicSettings settings = BuildOverrideSettings(selection, color, patternId);

                matched = WithInsulationAndLining(doc, view, matched);
                ColorizeMemory.Remember(doc, view, matched);   // so Restore original can put them back
                MergeSummary(summary, GraphicsElementService.ApplyOverrides(doc, view, matched, settings));
            }

            if (!anyMatched)
                skipped?.Add($"View '{view.Name}': no element of the picked categories is shown there.");
        }

        /// <summary>Categories that can carry insulation or lining.</summary>
        private static readonly BuiltInCategory[] WrapHostCategories =
        {
            BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_FlexDuctCurves, BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_FlexPipeCurves
        };

        /// <summary>
        /// The matched elements plus their insulation and lining shown in this view - colour a duct and leave
        /// its insulation alone, and the new colour sits inside a jacket of the old one. Same rule as
        /// Highlight Selection.
        /// </summary>
        private static ICollection<ElementId> WithInsulationAndLining(Document doc, View view, ICollection<ElementId> matched)
        {
            var hostCategories = new HashSet<long>();
            foreach (BuiltInCategory bic in WrapHostCategories)
                hostCategories.Add((long)bic);

            var wrapsInView = new HashSet<long>();
            foreach (ElementId id in new FilteredElementCollector(doc, view.Id)
                         .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>
                         {
                             BuiltInCategory.OST_DuctInsulations, BuiltInCategory.OST_PipeInsulations,
                             BuiltInCategory.OST_DuctLinings
                         }))
                         .ToElementIds())
            {
                wrapsInView.Add(id.LongValue());
            }

            if (wrapsInView.Count == 0)
                return matched;

            var result = new List<ElementId>(matched);
            var seen = new HashSet<long>();
            foreach (ElementId id in matched)
                seen.Add(id.LongValue());

            foreach (ElementId hostId in matched)
            {
                Category category = doc.GetElement(hostId)?.Category;
                if (category == null || !hostCategories.Contains(category.Id.LongValue()))
                    continue;

                AddWraps(InsulationLiningBase.GetInsulationIds, doc, hostId, wrapsInView, seen, result);
                AddWraps(InsulationLiningBase.GetLiningIds, doc, hostId, wrapsInView, seen, result);
            }

            return result;
        }

        private static void AddWraps(Func<Document, ElementId, ICollection<ElementId>> getWraps, Document doc, ElementId hostId,
                                     HashSet<long> wrapsInView, HashSet<long> seen, List<ElementId> result)
        {
            ICollection<ElementId> wraps;
            try
            {
                wraps = getWraps(doc, hostId);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return;   // this element cannot carry insulation/lining
            }

            if (wraps == null)
                return;
            foreach (ElementId id in wraps)
            {
                long key = id.LongValue();
                if (wrapsInView.Contains(key) && seen.Add(key))
                    result.Add(id);
            }
        }

        private static void ApplyPerValue(
            Document doc,
            View view,
            FilterSelection selection,
            IList<ElementId> categoryIds,
            ElementId patternId,
            IList<Color> colors,
            GraphicsOperationSummary summary,
            IList<string> skipped)
        {
            // valueIndex is the value's position in selection.Values - the same position ResolveColors used
            // to build `colors` - so it advances for every entry, empty ones included.
            int valueIndex = 0;

            foreach (FilterValueItem value in selection.Values)
            {
                if (value == null)
                {
                    valueIndex++;
                    continue;
                }

                IList<FilterRule> rules = BuildEqualsRules(selection.Parameter, value, selection.CaseSensitive);
                if (rules == null || rules.Count == 0)
                {
                    skipped?.Add(
                        $"Filter rule not supported for parameter '{selection.Parameter?.Name}' and value '{value.Display}'.");
                    valueIndex++;
                    continue;
                }

                ICollection<ElementId> matched;
                try
                {
                    matched = ColorizeElementMatcher.GetMatchingElementIds(doc, view, categoryIds, rules);
                }
                catch (System.Exception ex)
                {
                    skipped?.Add($"Error matching elements for value '{value.Display}': {ex.Message}");
                    valueIndex++;
                    continue;
                }

                if (matched.Count == 0)
                {
                    valueIndex++;
                    continue;
                }

                // Take the colour worked out once for the whole apply, so a value gets the same colour in
                // every target view (rolling a fresh random colour here gave it a different one per view).
                Color color = colors != null && valueIndex < colors.Count
                    ? colors[valueIndex]
                    : (selection.RandomColors ? ColorPalette.GetRandomColor() : ColorPalette.GetColorAt(valueIndex));

                OverrideGraphicSettings settings = BuildOverrideSettings(selection, color, patternId);

                matched = WithInsulationAndLining(doc, view, matched);
                ColorizeMemory.Remember(doc, view, matched);   // so Restore original can put them back
                MergeSummary(summary, GraphicsElementService.ApplyOverrides(doc, view, matched, settings));
                valueIndex++;
            }
        }

        /// <summary>
        /// Builds the Equals rule(s) for the given parameter/value pair. Colorize never exposes a
        /// rule-type choice in its UI, so only the Equals branch of Filter Pro's storage-type switch
        /// is needed here (String/ElementId/Integer/Double). Family+Type returns TWO rules (family
        /// name AND type name) - ColorizeElementMatcher ANDs every rule it's given via a single
        /// ElementParameterFilter, so both must come back together or a "Family + Type" value would
        /// match every element of that family regardless of type.
        /// </summary>
        private static IList<FilterRule> BuildEqualsRules(FilterParameterItem parameter, FilterValueItem value, bool caseSensitive)
        {
            if (parameter == null || value == null)
            {
                return null;
            }

            if (value.RawValue is System.Tuple<string, string> familyAndType)
            {
                return new List<FilterRule>
                {
                    FilterRuleCompat.Equals(new ElementId(BuiltInParameter.ALL_MODEL_FAMILY_NAME), familyAndType.Item1, caseSensitive),
                    FilterRuleCompat.Equals(new ElementId(BuiltInParameter.ALL_MODEL_TYPE_NAME), familyAndType.Item2, caseSensitive)
                };
            }

            FilterRule rule;
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    string text = value.RawValue as string ?? value.Display ?? string.Empty;
                    rule = FilterRuleCompat.Equals(parameter.Id, text, caseSensitive);
                    break;

                case StorageType.ElementId:
                    ElementId id = value.ElementId ?? value.RawValue as ElementId ?? ElementId.InvalidElementId;
                    if (id == ElementId.InvalidElementId)
                    {
                        return null;
                    }
                    rule = ParameterFilterRuleFactory.CreateEqualsRule(parameter.Id, id);
                    break;

                case StorageType.Integer:
                    if (!TryGetInt(value.RawValue, out int intVal))
                    {
                        return null;
                    }
                    rule = ParameterFilterRuleFactory.CreateEqualsRule(parameter.Id, intVal);
                    break;

                case StorageType.Double:
                    if (!TryGetDouble(value.RawValue, out double dblVal))
                    {
                        return null;
                    }
                    // The whole spread of values that show the same text ("1500"), as Filter Pro does - one
                    // sample +/- 1e-6 ft missed elements whose value differed in a far decimal place.
                    double min = value.MinDouble ?? dblVal;
                    double max = value.MaxDouble ?? dblVal;
                    rule = ParameterFilterRuleFactory.CreateEqualsRule(parameter.Id, (min + max) / 2.0, (max - min) / 2.0 + 1e-6);
                    break;

                default:
                    return null;
            }

            return new List<FilterRule> { rule };
        }

        private static bool TryGetInt(object raw, out int value)
        {
            if (raw is int i)
            {
                value = i;
                return true;
            }

            return int.TryParse(raw?.ToString(), out value);
        }

        private static bool TryGetDouble(object raw, out double value)
        {
            if (raw is double d)
            {
                value = d;
                return true;
            }

            return double.TryParse(raw?.ToString(), out value);
        }

        private static bool HasAnyGraphicsToggleEnabled(FilterSelection selection)
        {
            return selection.ColorProjectionLines || selection.ColorProjectionPatterns ||
                   selection.ColorCutLines || selection.ColorCutPatterns || selection.ColorHalftone;
        }

        /// <summary>
        /// The fill pattern to colour with: the picked one if it is a DRAFTING pattern, otherwise the drafting
        /// solid fill. Element overrides refuse a model pattern ("must be a drafting pattern"), which used to
        /// make every element fail.
        /// </summary>
        private static ElementId ResolvePatternId(Document doc, ElementId requested, ElementId solidFillId)
        {
            if (requested != null && requested != ElementId.InvalidElementId &&
                doc.GetElement(requested) is FillPatternElement picked &&
                picked.GetFillPattern()?.Target == FillPatternTarget.Drafting)
            {
                return requested;
            }

            return solidFillId ?? ElementId.InvalidElementId;
        }

        /// <summary>
        /// Builds a fresh per-element OverrideGraphicSettings from the selection's graphics toggles -
        /// never clones an existing override, since Shuffle Colors re-randomizes from scratch each click.
        /// </summary>
        private static OverrideGraphicSettings BuildOverrideSettings(FilterSelection selection, Color color, ElementId patternId)
        {
            var settings = new OverrideGraphicSettings();

            if (selection.ColorProjectionLines)
            {
                settings.SetProjectionLineColor(color);
            }

            if (selection.ColorCutLines)
            {
                settings.SetCutLineColor(color);
            }

            if (selection.ColorProjectionPatterns)
            {
                if (patternId != ElementId.InvalidElementId)
                {
                    settings.SetSurfaceForegroundPatternId(patternId);
                }

                settings.SetSurfaceForegroundPatternColor(color);
            }

            if (selection.ColorCutPatterns)
            {
                if (patternId != ElementId.InvalidElementId)
                {
                    settings.SetCutForegroundPatternId(patternId);
                }

                settings.SetCutForegroundPatternColor(color);
            }

            if (selection.ColorHalftone)
            {
                settings.SetHalftone(true);
            }

            return settings;
        }

        private static void MergeSummary(GraphicsOperationSummary target, GraphicsOperationSummary group)
        {
            if (target == null || group == null)
            {
                return;
            }

            target.Attempted += group.Attempted;
            target.Applied += group.Applied;
            target.Skipped += group.Skipped;
            if (target.FirstSkipReason == null) target.FirstSkipReason = group.FirstSkipReason;
        }
    }
}
