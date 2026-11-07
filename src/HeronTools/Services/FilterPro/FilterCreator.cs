// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Creates and updates ParameterFilterElements — builds filter rules by storage type, composes and sanitises filter names, and ensures name uniqueness in the project.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using HeronTools.Models;
using HeronTools.Utils;

namespace HeronTools.Services.FilterPro
{
    internal class FilterCreationResult
    {
        public List<ElementId> ProcessedFilterIds { get; } = new List<ElementId>();
        public int Created { get; set; }
        public int Updated { get; set; }
        // Existing same-named filters applied to the view(s) as they are (categories and rules untouched).
        public int Reused { get; set; }
        public int TotalAffected => Created + Updated + Reused;
    }

    /// <summary>
    /// Creates and updates rule-based view filters. Runs inside the caller's transaction.
    /// </summary>
    /// <remarks>
    /// Heron Tools changes (2026-11-07), each from a lesson Heron AI proved on real Revit:
    /// - A Revit failure now THROWS (wrapped in plain words) instead of being noted and skipped, so the
    ///   window rolls the whole run back - never half a set of filters.
    /// - Revit's own test (ElementFilterIsAcceptableForParameterFilterElement) runs before a filter is
    ///   created or changed; a rule Revit would refuse is reported as skipped, nothing is made.
    /// - Names are checked against EVERY filter, selection filters included, and with NamingUtils.
    /// - An existing filter is changed in the order Revit needs: ClearRules, SetCategories, SetElementFilter.
    /// - "Family and Type" only offers Equals (the only rule it can honestly build).
    /// - Decimal values use the real spread of the values that show the same text, not a 1e-6 guess.
    /// </remarks>
    internal static class FilterCreator
    {
        private const int MaxFilterNameLength = 120;
        private const double DoubleEpsilon = 1e-6;

        internal static FilterCreationResult CreateOrUpdateFilters(
            Document doc,
            FilterSelection selection,
            IList<ElementId> validCategoryIds,
            IList<string> skipped)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            var result = new FilterCreationResult();

            if (selection == null)
            {
                skipped?.Add("No selection provided for filter creation.");
                return result;
            }

            if (selection.Values == null || selection.Values.Count == 0)
            {
                skipped?.Add("No values selected for filter creation.");
                return result;
            }

            if (validCategoryIds == null || validCategoryIds.Count == 0)
            {
                skipped?.Add("No valid categories supplied for filter creation.");
                return result;
            }

            // Every filter name in the project, rule-based AND selection filters: Revit refuses a new
            // filter whose name any of them already uses. Only rule-based ones can be reused or updated.
            var takenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingFilterNames = new Dictionary<string, ParameterFilterElement>(StringComparer.OrdinalIgnoreCase);
            foreach (FilterElement fe in new FilteredElementCollector(doc)
                .OfClass(typeof(FilterElement))
                .Cast<FilterElement>())
            {
                takenNames.Add(fe.Name);
                if (fe is ParameterFilterElement pfe && !existingFilterNames.ContainsKey(pfe.Name))
                    existingFilterNames[pfe.Name] = pfe;
            }

            var categorySet = new HashSet<ElementId>(validCategoryIds, new ElementIdIntegerComparer());
            var processedIds = new HashSet<long>();
            var namesThisRun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Apply To View and Shuffle Colors (ApplyToView) work on the filter that already carries the
            // name instead of making a numbered " (2)" copy - Shuffle only ever recolours existing filters.
            // Whether that filter's categories and rules may be REWRITTEN is decided by the "Update Existing
            // Filters (overwrite)" toggle alone (OverrideExisting), for every button.
            // Every button uses an existing rule filter with the same name instead of making "X (2)", "X (3)"...
            // on each click; only the "Update existing filters" toggle decides whether its rule is rewritten.
            bool reuseExisting = true;

            foreach (FilterValueItem value in selection.Values)
            {
                if (value == null)
                    continue;

                int skippedBefore = skipped?.Count ?? 0;
                var rules = BuildRules(selection.Parameter, value, selection.RuleType, selection.CaseSensitive, skipped);
                if (rules == null || rules.Count == 0)
                {
                    // BuildRules may already have said why; otherwise give the general reason.
                    if ((skipped?.Count ?? 0) == skippedBefore)
                    {
                        skipped?.Add(selection.Parameter != null
                            ? $"Filter rules are not supported for parameter '{selection.Parameter.Name}' and value '{value.Display}'."
                            : $"No parameter was selected for value '{value.Display}'.");
                    }

                    continue;
                }

                string filterName = ComposeFilterName(selection, value, doc);
                if (!NamingUtils.IsValidName(filterName))
                {
                    skipped?.Add($"'{filterName}' cannot be used as a filter name in Revit. Change the prefix, suffix or separator.");
                    continue;
                }

                filterName = EnsureUniqueFilterName(filterName, takenNames, existingFilterNames, reuseExisting);

                // Two picked values can clean up to the same name ("A:B" and "A_B", or names cut at 120
                // characters). The second would silently reuse - or with Update on, overwrite - the first
                // value's filter, so it is reported instead.
                if (!namesThisRun.Add(filterName))
                {
                    skipped?.Add($"'{value.Display}' gives the same filter name as another picked value ('{filterName}'), so only the first was used.");
                    continue;
                }

                existingFilterNames.TryGetValue(filterName, out ParameterFilterElement existing);
                ElementFilter elementFilter = new ElementParameterFilter(rules);
                ParameterFilterElement filter = existing;

                // A same-named filter that tests a different parameter (e.g. "Ducts_Has value" made earlier for
                // another parameter) must not be reused silently; it is only rewritten when the toggle is on.
                if (filter != null && !selection.OverrideExisting && !TestsSameParameter(filter, selection.Parameter))
                {
                    skipped?.Add($"A filter named '{filterName}' already exists but tests a different parameter, so it was not used. " +
                                 "Change the prefix or suffix, or turn on Update existing filters to rewrite it.");
                    continue;
                }

                // Same name and parameter but other categories (the name can match while the category list
                // differs, e.g. with "Include the category name" off): reusing it would leave the extra
                // categories uncoloured with no word said.
                if (filter != null && !selection.OverrideExisting && !CoversSameCategories(filter, categorySet))
                {
                    skipped?.Add($"A filter named '{filterName}' already exists for other categories, so it was not used. " +
                                 "Change the prefix or suffix, or turn on Update existing filters to rewrite it.");
                    continue;
                }

                bool needsRules = filter == null || selection.OverrideExisting;
                if (needsRules &&
                    !ParameterFilterElement.ElementFilterIsAcceptableForParameterFilterElement(doc, categorySet, elementFilter))
                {
                    skipped?.Add(
                        $"Revit does not accept the rule on '{selection.Parameter?.Name}' = '{value.Display}' for the picked categories " +
                        "(a text rule on a number parameter is the usual reason). No filter was made for it.");
                    continue;
                }

                try
                {
                    if (filter == null)
                    {
                        if (selection.RandomColors)
                        {
                            // Shuffle Colors only recolours filters that already exist.
                            skipped?.Add($"Filter '{filterName}' does not exist yet; Shuffle Colors only recolours existing filters.");
                            continue;
                        }

                        filter = ParameterFilterElement.Create(doc, filterName, validCategoryIds, elementFilter);
                        existingFilterNames[filter.Name] = filter;
                        takenNames.Add(filter.Name);
                        result.Created++;
                    }
                    else if (selection.OverrideExisting)
                    {
                        // The order Revit needs: an old rule that does not suit the new categories would
                        // make SetCategories throw, so the rules are cleared first.
                        filter.ClearRules();
                        filter.SetCategories(validCategoryIds);
                        if (!filter.SetElementFilter(elementFilter))
                            throw new FilterProException($"Revit did not accept the new rule for the existing filter '{filterName}'.");
                        result.Updated++;
                    }
                    else if (selection.ApplyToView)
                    {
                        // Toggle off: apply the existing filter as it is - its categories and rules are
                        // left exactly as they were (it may be used in other views across the project).
                        result.Reused++;
                    }
                    else
                    {
                        // Create filters, toggle off: the filter already exists - left exactly as it is.
                        skipped?.Add($"Filter '{filterName}' already exists, so it was left as it is (turn on Update existing filters to rewrite it).");
                        continue;
                    }
                }
                catch (FilterProException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new FilterProException($"Revit could not make the filter '{filterName}'. {ex.Message}", ex);
                }

                if (processedIds.Add(filter.Id.LongValue()))
                    result.ProcessedFilterIds.Add(filter.Id);
            }

            return result;
        }

        private static IList<FilterRule> BuildRules(
            FilterParameterItem parameter,
            FilterValueItem value,
            string ruleType,
            bool caseSensitive,
            IList<string> skipped)
        {
            if (parameter == null)
                return null;

            bool isFamilyAndType = parameter.Id == SpecialParameterIds.FamilyAndType;
            if (isFamilyAndType)
            {
                // "Family and Type" is two rules joined with AND (family equals X, type equals Y). Any other
                // rule type cannot be built honestly from it, so it is refused instead of silently turned
                // into Equals. Its id is not a real Revit parameter and must never reach the rule factory.
                if (ruleType != RuleTypes.EqualsRule || !(value.RawValue is Tuple<string, string> familyAndType))
                {
                    skipped?.Add("'Family and Type' only works with the 'Equals' rule. Pick Equals, or pick the Family Name or Type Name parameter.");
                    return null;
                }

                return new List<FilterRule>
                {
                    FilterRuleCompat.Equals(
                        new ElementId(BuiltInParameter.ALL_MODEL_FAMILY_NAME),
                        familyAndType.Item1,
                        caseSensitive),
                    FilterRuleCompat.Equals(
                        new ElementId(BuiltInParameter.ALL_MODEL_TYPE_NAME),
                        familyAndType.Item2,
                        caseSensitive)
                };
            }

            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return BuildStringRules(parameter.Id, value, ruleType, caseSensitive);
                case StorageType.ElementId:
                    return BuildElementIdRules(parameter.Id, value, ruleType, skipped);
                case StorageType.Integer:
                    return BuildIntegerRules(parameter, value, ruleType, skipped);
                case StorageType.Double:
                    return BuildDoubleRules(parameter, value, ruleType, skipped);
                default:
                    return null;
            }
        }

        private static IList<FilterRule> BuildStringRules(
            ElementId paramId,
            FilterValueItem value,
            string ruleType,
            bool caseSensitive)
        {
            string text = value.RawValue as string ?? value.Display ?? string.Empty;
            var singleRules = new List<FilterRule>();

            switch (ruleType)
            {
                case RuleTypes.EqualsRule:
                    singleRules.Add(FilterRuleCompat.Equals(paramId, text, caseSensitive));
                    break;
                case RuleTypes.Contains:
                    singleRules.Add(FilterRuleCompat.Contains(paramId, text, caseSensitive));
                    break;
                case RuleTypes.BeginsWith:
                    singleRules.Add(FilterRuleCompat.BeginsWith(paramId, text, caseSensitive));
                    break;
                case RuleTypes.EndsWith:
                    singleRules.Add(FilterRuleCompat.EndsWith(paramId, text, caseSensitive));
                    break;
                case RuleTypes.NotEquals:
                    singleRules.Add(FilterRuleCompat.NotEquals(paramId, text, caseSensitive));
                    break;
                case RuleTypes.NotContains:
                    singleRules.Add(FilterRuleCompat.NotContains(paramId, text, caseSensitive));
                    break;
                case RuleTypes.NotBeginsWith:
                    singleRules.Add(FilterRuleCompat.NotBeginsWith(paramId, text, caseSensitive));
                    break;
                case RuleTypes.NotEndsWith:
                    singleRules.Add(FilterRuleCompat.NotEndsWith(paramId, text, caseSensitive));
                    break;
                case RuleTypes.HasValue:
                    singleRules.Add(ParameterFilterRuleFactory.CreateHasValueParameterRule(paramId));
                    break;
                case RuleTypes.HasNoValue:
                    singleRules.Add(ParameterFilterRuleFactory.CreateHasNoValueParameterRule(paramId));
                    break;
                default:
                    return null;
            }

            return singleRules;
        }

        private static IList<FilterRule> BuildElementIdRules(
            ElementId paramId,
            FilterValueItem value,
            string ruleType,
            IList<string> skipped)
        {
            // An element parameter (Level, System Type...) holds an element, not text: a text rule such as
            // Contains used to be built silently as Equals. It is refused instead.
            if (ruleType != RuleTypes.EqualsRule && ruleType != RuleTypes.NotEquals &&
                ruleType != RuleTypes.HasValue && ruleType != RuleTypes.HasNoValue)
            {
                skipped?.Add($"The rule '{RuleTypes.Label(ruleType)}' only works on text; this parameter holds an element. Pick Equals or Does Not Equal.");
                return null;
            }

            var singleRules = new List<FilterRule>();
            ElementId id = value.ElementId ?? value.RawValue as ElementId ?? ElementId.InvalidElementId;

            if (ruleType == RuleTypes.HasValue)
            {
                singleRules.Add(ParameterFilterRuleFactory.CreateHasValueParameterRule(paramId));
            }
            else if (ruleType == RuleTypes.HasNoValue)
            {
                singleRules.Add(ParameterFilterRuleFactory.CreateHasNoValueParameterRule(paramId));
            }
            else
            {
                if (id == ElementId.InvalidElementId)
                    return null;

                if (ruleType == RuleTypes.NotEquals)
                    singleRules.Add(ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, id));
                else
                    singleRules.Add(ParameterFilterRuleFactory.CreateEqualsRule(paramId, id));
            }

            return singleRules;
        }

        private static IList<FilterRule> BuildIntegerRules(
            FilterParameterItem parameter,
            FilterValueItem value,
            string ruleType,
            IList<string> skipped)
        {
            var singleRules = new List<FilterRule>();
            ElementId paramId = parameter.Id;

            if (!TryGetInt(value.RawValue, out int intVal) &&
                ruleType != RuleTypes.HasValue &&
                ruleType != RuleTypes.HasNoValue)
            {
                skipped?.Add($"'{value.Display}' is not a whole number, so it cannot be used for '{parameter.Name}'.");
                return null;
            }

            switch (ruleType)
            {
                case RuleTypes.NotEquals:
                    singleRules.Add(ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, intVal));
                    break;
                case RuleTypes.Greater:
                    singleRules.Add(ParameterFilterRuleFactory.CreateGreaterRule(paramId, intVal));
                    break;
                case RuleTypes.GreaterOrEqual:
                    singleRules.Add(ParameterFilterRuleFactory.CreateGreaterOrEqualRule(paramId, intVal));
                    break;
                case RuleTypes.Less:
                    singleRules.Add(ParameterFilterRuleFactory.CreateLessRule(paramId, intVal));
                    break;
                case RuleTypes.LessOrEqual:
                    singleRules.Add(ParameterFilterRuleFactory.CreateLessOrEqualRule(paramId, intVal));
                    break;
                case RuleTypes.HasValue:
                    singleRules.Add(ParameterFilterRuleFactory.CreateHasValueParameterRule(paramId));
                    break;
                case RuleTypes.HasNoValue:
                    singleRules.Add(ParameterFilterRuleFactory.CreateHasNoValueParameterRule(paramId));
                    break;
                case RuleTypes.EqualsRule:
                    singleRules.Add(ParameterFilterRuleFactory.CreateEqualsRule(paramId, intVal));
                    break;
                default:
                    // A text-only rule (Contains, Begins With...) on a number is refused, not turned into Equals.
                    skipped?.Add($"The rule '{RuleTypes.Label(ruleType)}' only works on text; '{parameter.Name}' holds a number. Pick Equals or Does Not Equal.");
                    return null;
            }

            return singleRules;
        }

        /// <summary>
        /// Decimal (length, area, flow...) rules. Values are listed by the text Revit shows, and several
        /// slightly different internal values can show the same text (e.g. "1500"). The rule therefore
        /// covers the whole spread of values behind that text, so the filter catches every element the
        /// user saw listed under it.
        /// </summary>
        private static IList<FilterRule> BuildDoubleRules(
            FilterParameterItem parameter,
            FilterValueItem value,
            string ruleType,
            IList<string> skipped)
        {
            var singleRules = new List<FilterRule>();
            ElementId paramId = parameter.Id;

            if (ruleType == RuleTypes.HasValue)
                return new List<FilterRule> { ParameterFilterRuleFactory.CreateHasValueParameterRule(paramId) };
            if (ruleType == RuleTypes.HasNoValue)
                return new List<FilterRule> { ParameterFilterRuleFactory.CreateHasNoValueParameterRule(paramId) };

            if (!TryGetDouble(value.RawValue, out double sample))
            {
                skipped?.Add($"'{value.Display}' is not a number, so it cannot be used for '{parameter.Name}'.");
                return null;
            }

            double min = value.MinDouble ?? sample;
            double max = value.MaxDouble ?? sample;
            double centre = (min + max) / 2.0;
            double halfSpread = (max - min) / 2.0 + DoubleEpsilon;

            switch (ruleType)
            {
                case RuleTypes.EqualsRule:
                    singleRules.Add(ParameterFilterRuleFactory.CreateEqualsRule(paramId, centre, halfSpread));
                    break;
                case RuleTypes.NotEquals:
                    singleRules.Add(ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, centre, halfSpread));
                    break;
                case RuleTypes.Greater:
                    singleRules.Add(ParameterFilterRuleFactory.CreateGreaterRule(paramId, max, DoubleEpsilon));
                    break;
                case RuleTypes.GreaterOrEqual:
                    singleRules.Add(ParameterFilterRuleFactory.CreateGreaterOrEqualRule(paramId, min, DoubleEpsilon));
                    break;
                case RuleTypes.Less:
                    singleRules.Add(ParameterFilterRuleFactory.CreateLessRule(paramId, min, DoubleEpsilon));
                    break;
                case RuleTypes.LessOrEqual:
                    singleRules.Add(ParameterFilterRuleFactory.CreateLessOrEqualRule(paramId, max, DoubleEpsilon));
                    break;
                default:
                    skipped?.Add($"The rule '{RuleTypes.Label(ruleType)}' only works on text; '{parameter.Name}' holds a number. Pick Equals or Does Not Equal.");
                    return null;
            }

            return singleRules;
        }

        private static bool TryGetInt(object raw, out int value)
        {
            if (raw is int i)
            {
                value = i;
                return true;
            }

            return int.TryParse(raw?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryGetDouble(object raw, out double value)
        {
            if (raw is double d)
            {
                value = d;
                return true;
            }

            return double.TryParse(raw?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        internal static string ComposeFilterName(
            FilterSelection selection,
            FilterValueItem value,
            Document doc)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(selection.Prefix))
                parts.Add(selection.Prefix.Trim());

            if (selection.IncludeCategory)
            {
                string catLabel = ResolveCategoryLabel(selection.CategoryIds, doc);
                if (!string.IsNullOrWhiteSpace(catLabel))
                    parts.Add(catLabel);
            }

            bool presenceRule = selection.RuleType == RuleTypes.HasValue || selection.RuleType == RuleTypes.HasNoValue;
            if ((selection.IncludeParameter || presenceRule) && selection.Parameter != null)
                parts.Add(selection.Parameter.Name);   // "Has value" means nothing without the parameter

            parts.Add(DescribeValueWithRule(selection.RuleType, value.Display ?? "Value"));

            string separator = string.IsNullOrEmpty(selection.Separator)
                ? "_"
                : selection.Separator;

            string core = string.Join(separator, parts);

            if (!string.IsNullOrWhiteSpace(selection.Suffix))
                core += " " + selection.Suffix.Trim();

            return SanitizeName(core);
        }

        /// <summary>
        /// True if the filter's rules test the selected parameter (for "Family and Type": the family and type name
        /// parameters). A filter Revit cannot describe is treated as not matching.
        /// </summary>
        private static bool CoversSameCategories(ParameterFilterElement filter, HashSet<ElementId> wanted)
        {
            try
            {
                ICollection<ElementId> current = filter.GetCategories();
                if (current == null || current.Count != wanted.Count)
                    return false;
                foreach (ElementId id in current)
                {
                    if (!wanted.Contains(id))
                        return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TestsSameParameter(ParameterFilterElement filter, FilterParameterItem parameter)
        {
            if (parameter == null)
                return false;

            ICollection<ElementId> used = filter.GetElementFilterParameters();
            if (used == null || used.Count == 0)
                return false;

            if (parameter.Id == SpecialParameterIds.FamilyAndType)
                return used.Contains(new ElementId(BuiltInParameter.ALL_MODEL_FAMILY_NAME)) &&
                       used.Contains(new ElementId(BuiltInParameter.ALL_MODEL_TYPE_NAME));

            return used.Any(id => id == parameter.Id);
        }

        /// <summary>
        /// The value part of a filter name, carrying the rule when it is not Equals: "Not 300", "Contains SA",
        /// "Has value". Without this, "Has value" and "Has no value" (both "Any") or "Equals A" and "Does not
        /// equal A" got the same name, and a later run reused the OPPOSITE filter.
        /// </summary>
        internal static string DescribeValueWithRule(string ruleType, string valueText)
        {
            switch (ruleType)
            {
                case RuleTypes.NotEquals: return "Not " + valueText;
                case RuleTypes.Contains: return "Contains " + valueText;
                case RuleTypes.NotContains: return "Not containing " + valueText;
                case RuleTypes.BeginsWith: return "Begins " + valueText;
                case RuleTypes.NotBeginsWith: return "Not beginning " + valueText;
                case RuleTypes.EndsWith: return "Ends " + valueText;
                case RuleTypes.NotEndsWith: return "Not ending " + valueText;
                case RuleTypes.HasValue: return "Has value";
                case RuleTypes.HasNoValue: return "No value";
                default: return valueText;
            }
        }

        private static string ResolveCategoryLabel(IEnumerable<ElementId> categoryIds, Document doc)
        {
            // Alphabetical, so "Ducts, Pipes" and "Pipes, Ducts" give the same name and the same filter is
            // reused; beyond three, "Ducts +4" says how many more (it used to be "Ducts +" for every set).
            var names = (categoryIds ?? Enumerable.Empty<ElementId>())
                .Select(id => Category.GetCategory(doc, id)?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
                return string.Empty;

            return names.Count > 3
                ? $"{names[0]} +{names.Count - 1}"
                : string.Join(", ", names);
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Filter";

            // The characters Revit refuses in a name. The backtick and tilde were missing before (Heron AI
            // lesson); NamingUtils.IsValidName is still asked afterwards, as the final word.
            char[] invalid = { '<', '>', '{', '}', '[', ']', '|', ';', ':', '\\', '?', '*', '"', '`', '~' };
            foreach (char c in invalid)
                name = name.Replace(c, '_');

            name = name.Trim();
            if (name.Length > MaxFilterNameLength)
                name = name.Substring(0, MaxFilterNameLength).TrimEnd();

            return name;
        }

        /// <summary>
        /// Returns a filter name that Revit will accept. With <paramref name="reuseExisting"/>, a name that
        /// belongs to a rule-based filter is returned as it is, so that filter is the one used. A name held
        /// by a SELECTION filter can never be reused (Revit would refuse it), so it always gets a number.
        /// </summary>
        private static string EnsureUniqueFilterName(
            string name,
            HashSet<string> takenNames,
            Dictionary<string, ParameterFilterElement> ruleFilters,
            bool reuseExisting)
        {
            string baseName = name.Length > MaxFilterNameLength
                ? name.Substring(0, MaxFilterNameLength)
                : name;

            if (reuseExisting && ruleFilters.ContainsKey(baseName))
                return baseName;

            if (!takenNames.Contains(baseName))
                return baseName;

            int suffix = 2;
            while (true)
            {
                string suffixText = $" ({suffix++})";
                int maxBaseLength = Math.Max(1, MaxFilterNameLength - suffixText.Length);

                string trimmedBase = baseName.Length > maxBaseLength
                    ? baseName.Substring(0, maxBaseLength)
                    : baseName;

                string candidate = trimmedBase + suffixText;

                if (reuseExisting && ruleFilters.ContainsKey(candidate))
                    return candidate;   // the rule filter made by an earlier run - reuse it, no new number each run

                if (!takenNames.Contains(candidate))
                    return candidate;
            }
        }
    }

    /// <summary>
    /// A Filter Pro failure whose message is already in plain words for the modeller.
    /// Thrown inside the transaction so the window rolls the whole run back.
    /// </summary>
    internal sealed class FilterProException : Exception
    {
        public FilterProException(string message) : base(message) { }
        public FilterProException(string message, Exception inner) : base(message, inner) { }
    }
}
