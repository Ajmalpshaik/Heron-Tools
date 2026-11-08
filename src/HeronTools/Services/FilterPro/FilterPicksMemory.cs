// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The lasting memory of the Filter Pro and Colorize picks, so each window opens with the choices used last
// time - also after Revit restarts, and in another model. Kept through WindowMemory
// (%AppData%\HeronTools\memory-filter-pro.txt / memory-colorize.txt).
//
// Everything is stored by something that is the same in every model, never by an element id:
//   categories  - built-in category number (e.g. -2008000 = Ducts)
//   parameter   - built-in number, shared-parameter GUID, or (project parameter) its name
//   values      - the display text, re-ticked only under that same parameter
//   views       - view type + view name
//   pattern     - fill pattern name
// On opening, only what exists in this model is filled in; the rest is skipped quietly.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using HeronTools.Models;
using HeronTools.Utils;

namespace HeronTools.Services.FilterPro
{
    internal static class FilterPicksMemory
    {
        internal const string FilterProTool = "filter-pro";
        internal const string ColorizeTool = "colorize";

        private const string KeyCategories = "categories";
        private const string KeyParameter = "parameter";
        private const string KeyParameterName = "parameter-name";   // for a human reading the file only
        private const string KeyValues = "values";
        private const string KeyRule = "rule";
        private const string KeyPrefix = "prefix";
        private const string KeySuffix = "suffix";
        private const string KeySeparator = "separator";
        private const string KeyIncludeCategory = "include-category";
        private const string KeyIncludeParameter = "include-parameter";
        private const string KeyApplyToActiveView = "apply-to-active-view";
        private const string KeyViews = "views";
        private const string KeyProjectionLines = "colour-projection-lines";
        private const string KeyProjectionPatterns = "colour-projection-patterns";
        private const string KeyCutLines = "colour-cut-lines";
        private const string KeyCutPatterns = "colour-cut-patterns";
        private const string KeyHalftone = "colour-halftone";
        private const string KeyPattern = "pattern";

        private const string ParamFamilyAndType = "family-and-type";
        private const string ParamBuiltIn = "builtin:";
        private const string ParamShared = "shared:";
        private const string ParamByName = "name:";

        // ------------------------------------------------------------------ saving

        /// <summary>
        /// Remembers the picks a run just used. <paramref name="rememberRuleAndNaming"/> is for Filter Pro only
        /// (Colorize has no rule or filter name). Never throws: a lost memory is not worth an error.
        /// </summary>
        internal static void Save(string tool, Document doc, FilterProState state, IEnumerable<string> valueTexts,
                                  string patternName, bool rememberRuleAndNaming)
        {
            if (doc == null || state == null)
                return;

            try
            {
                WindowMemory memory = WindowMemory.Load(tool);

                // Only built-in categories (negative numbers): they are the same in every model.
                memory.Set(KeyCategories, (state.CategoryIds ?? new List<ElementId>())
                    .Where(id => id != null && id.LongValue() < -1)
                    .Select(id => id.LongValue().ToString(CultureInfo.InvariantCulture)));

                string paramKey = ParameterKey(doc, state.ParameterId);
                memory.Set(KeyParameter, paramKey ?? string.Empty);
                memory.Set(KeyParameterName, paramKey == null ? string.Empty : ParameterName(doc, state.ParameterId));
                // Values belong to their parameter: without one there is nothing to re-tick them under.
                memory.Set(KeyValues, paramKey == null ? Enumerable.Empty<string>() : (valueTexts ?? Enumerable.Empty<string>()));

                if (rememberRuleAndNaming)
                {
                    memory.Set(KeyRule, state.RuleType ?? string.Empty);
                    memory.Set(KeyPrefix, Wrap(state.Prefix));
                    memory.Set(KeySuffix, Wrap(state.Suffix));
                    memory.Set(KeySeparator, Wrap(state.Separator));
                    memory.Set(KeyIncludeCategory, state.IncludeCategory);
                    memory.Set(KeyIncludeParameter, state.IncludeParameter);
                }

                WriteScope(memory, doc, state.ApplyToActiveView, state.TargetViewIds);

                memory.Set(KeyProjectionLines, state.ColorProjectionLines);
                memory.Set(KeyProjectionPatterns, state.ColorProjectionPatterns);
                memory.Set(KeyCutLines, state.ColorCutLines);
                memory.Set(KeyCutPatterns, state.ColorCutPatterns);
                memory.Set(KeyHalftone, state.ColorHalftone);
                memory.Set(KeyPattern, patternName ?? string.Empty);

                memory.SaveQuietly();
            }
            catch (Exception)
            {
                // Best effort: the run itself already succeeded; only the memory is lost.
            }
        }

        /// <summary>
        /// Remembers only where a run went (active view or picked views) - for a click like Colorize's
        /// Restore original, which uses the target views but not the category / parameter picks.
        /// </summary>
        internal static void SaveScope(string tool, Document doc, bool applyToActiveView, IList<ElementId> targetViewIds)
        {
            if (doc == null)
                return;

            try
            {
                WindowMemory memory = WindowMemory.Load(tool);
                WriteScope(memory, doc, applyToActiveView, targetViewIds);
                memory.SaveQuietly();
            }
            catch (Exception)
            {
                // Best effort, as above.
            }
        }

        private static void WriteScope(WindowMemory memory, Document doc, bool applyToActiveView, IList<ElementId> targetViewIds)
        {
            memory.Set(KeyApplyToActiveView, applyToActiveView);

            // The picked views only matter when the run used them; the active view is found afresh each time.
            var viewKeys = new List<string>();
            if (!applyToActiveView && targetViewIds != null)
            {
                foreach (ElementId id in targetViewIds)
                {
                    if (doc.GetElement(id) is View view && !string.IsNullOrWhiteSpace(view.Name))
                        viewKeys.Add(ViewKey(view.ViewType, view.Name));
                }
            }
            memory.Set(KeyViews, viewKeys);
        }

        // ------------------------------------------------------------------ loading

        /// <summary>
        /// The remembered picks as a state for the window's restore path, or null when nothing is remembered
        /// (first use, deleted or unreadable file). Views and the pattern are matched to THIS model's lists
        /// here; categories are built-in numbers; the parameter and values are matched by the restore itself,
        /// once their lists have loaded (see <see cref="SameParameter"/> and <see cref="ValueTextSet"/>).
        /// </summary>
        internal static FilterProState Load(string tool, IEnumerable<ApplyViewItem> views, IEnumerable<PatternItem> patterns,
                                            bool rememberRuleAndNaming)
        {
            try
            {
                WindowMemory memory = WindowMemory.Load(tool);
                if (memory.IsEmpty)
                    return null;

                var state = new FilterProState();

                foreach (string text in memory.GetList(KeyCategories))
                {
                    // Built-in category numbers are negative and fit 32 bits; anything else is a damaged line.
                    if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) &&
                        number < -1 && number >= int.MinValue)
                        state.CategoryIds.Add(ElementIdHelper.FromLong(number));
                }

                string paramKey = memory.Get(KeyParameter);
                state.ParameterKey = string.IsNullOrWhiteSpace(paramKey) ? null : paramKey;
                state.ValueTexts = state.ParameterKey == null ? new List<string>() : memory.GetList(KeyValues).ToList();

                if (rememberRuleAndNaming)
                {
                    state.RuleType = memory.Get(KeyRule);
                    state.Prefix = Unwrap(memory.Get(KeyPrefix)) ?? string.Empty;
                    state.Suffix = Unwrap(memory.Get(KeySuffix)) ?? string.Empty;
                    string separator = Unwrap(memory.Get(KeySeparator));
                    state.Separator = string.IsNullOrEmpty(separator) ? "_" : separator;
                    // The window's own defaults (both ticked) when a line is missing.
                    state.IncludeCategory = memory.GetBool(KeyIncludeCategory, true);
                    state.IncludeParameter = memory.GetBool(KeyIncludeParameter, true);
                }

                state.ApplyToActiveView = memory.GetBool(KeyApplyToActiveView, true);
                var wantedViews = memory.GetList(KeyViews);
                state.TargetViewIds = (views ?? Enumerable.Empty<ApplyViewItem>())
                    .Where(v => v != null && v.Id != null && wantedViews.Any(w => ViewMatches(w, v)))
                    .Select(v => v.Id)
                    .ToList();

                state.ColorProjectionLines = memory.GetBool(KeyProjectionLines, true);
                state.ColorProjectionPatterns = memory.GetBool(KeyProjectionPatterns, false);
                state.ColorCutLines = memory.GetBool(KeyCutLines, true);
                state.ColorCutPatterns = memory.GetBool(KeyCutPatterns, false);
                state.ColorHalftone = memory.GetBool(KeyHalftone, false);
                if (!state.ColorProjectionLines && !state.ColorProjectionPatterns && !state.ColorCutLines &&
                    !state.ColorCutPatterns && !state.ColorHalftone)
                {
                    // Nothing ticked can only come from a damaged file - Shuffle refuses it. Use the defaults.
                    state.ColorProjectionLines = true;
                    state.ColorCutLines = true;
                }

                string patternName = Normalize(memory.Get(KeyPattern));
                PatternItem pattern = string.IsNullOrEmpty(patternName)
                    ? null
                    : (patterns ?? Enumerable.Empty<PatternItem>()).FirstOrDefault(p =>
                        p != null && string.Equals(Normalize(p.Name), patternName, StringComparison.OrdinalIgnoreCase));
                state.PatternId = pattern?.Id;

                return state;
            }
            catch (Exception)
            {
                return null;   // a damaged memory never stops the window: it opens with its defaults
            }
        }

        // ------------------------------------------------------------------ matching

        /// <summary>True when the parameter with this id in this model is the remembered one.</summary>
        internal static bool SameParameter(Document doc, ElementId parameterId, string rememberedKey) =>
            !string.IsNullOrEmpty(rememberedKey) &&
            string.Equals(ParameterKey(doc, parameterId), rememberedKey, StringComparison.Ordinal);

        /// <summary>The remembered value texts, ready for <see cref="MatchesValueText"/>.</summary>
        internal static HashSet<string> ValueTextSet(IEnumerable<string> valueTexts) =>
            new HashSet<string>((valueTexts ?? Enumerable.Empty<string>()).Select(Normalize).Where(s => s.Length > 0),
                                StringComparer.Ordinal);

        /// <summary>True when this listed value shows the same text as a remembered one.</summary>
        internal static bool MatchesValueText(FilterValueItem item, HashSet<string> wanted) =>
            item?.Display != null && wanted != null && wanted.Contains(Normalize(item.Display));

        /// <summary>
        /// A parameter's identity that is the same in every model: built-in ones by their (negative) number,
        /// shared ones by GUID, project parameters by name. Null when it cannot be told.
        /// </summary>
        private static string ParameterKey(Document doc, ElementId id)
        {
            if (doc == null || id == null || id == ElementId.InvalidElementId)
                return null;

            // The Filter Pro-only "Family and Type" stand-in is checked first: its number is not a real parameter.
            if (id == SpecialParameterIds.FamilyAndType)
                return ParamFamilyAndType;

            if (ElementIdHelper.TryGetBuiltInParameter(id, out _))
                return ParamBuiltIn + id.LongValue().ToString(CultureInfo.InvariantCulture);

            Element element = doc.GetElement(id);
            if (element is SharedParameterElement shared)
                return ParamShared + shared.GuidValue.ToString("D");
            if (element is ParameterElement project && !string.IsNullOrWhiteSpace(project.Name))
                return ParamByName + Normalize(project.Name);

            return null;
        }

        private static string ParameterName(Document doc, ElementId id)
        {
            if (id == SpecialParameterIds.FamilyAndType)
                return "Family and Type";
            if (ElementIdHelper.TryGetBuiltInParameter(id, out BuiltInParameter builtIn))
            {
                try { return LabelUtils.GetLabelFor(builtIn); }
                catch (Exception) { return string.Empty; }   // some built-ins have no label; the name is only for reading
            }
            return doc.GetElement(id)?.Name ?? string.Empty;
        }

        // A view name cannot contain "|" in Revit, so it safely separates the view type from the name.
        private static string ViewKey(ViewType type, string name) => type + "|" + Normalize(name);

        private static bool ViewMatches(string remembered, ApplyViewItem view)
        {
            int bar = remembered.IndexOf('|');
            if (bar < 0)   // a hand-edited line with a name only
                return string.Equals(remembered, Normalize(view.Name), StringComparison.Ordinal);
            return string.Equals(remembered, ViewKey(view.ViewType, view.Name), StringComparison.Ordinal);
        }

        /// <summary>The same clean-up WindowMemory applies on save, so a saved text and a listed one compare alike.</summary>
        private static string Normalize(string value) =>
            (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();

        // WindowMemory trims every value; brackets keep a prefix, suffix or separator with spaces (" - ") whole.
        private static string Wrap(string value) => "[" + (value ?? string.Empty) + "]";

        private static string Unwrap(string value)
        {
            if (value == null)
                return null;
            return value.Length >= 2 && value[0] == '[' && value[value.Length - 1] == ']'
                ? value.Substring(1, value.Length - 2)
                : value;
        }
    }
}
