// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Version-safe ParameterFilterRuleFactory string rules across Revit 2020 -> 2027.

using Autodesk.Revit.DB;

namespace HeronTools.Utils
{
    /// <summary>
    /// Bridges the ParameterFilterRuleFactory string-rule methods whose case-sensitivity argument
    /// was removed in Revit 2023 (and the old 3-argument overload deleted in Revit 2026).
    /// </summary>
    internal static class FilterRuleCompat
    {
        internal static FilterRule Equals(ElementId parameter, string value, bool caseSensitive)
#if REVIT2023_OR_GREATER
            => ParameterFilterRuleFactory.CreateEqualsRule(parameter, value);
#else
            => ParameterFilterRuleFactory.CreateEqualsRule(parameter, value, caseSensitive);
#endif

        internal static FilterRule NotEquals(ElementId parameter, string value, bool caseSensitive)
#if REVIT2023_OR_GREATER
            => ParameterFilterRuleFactory.CreateNotEqualsRule(parameter, value);
#else
            => ParameterFilterRuleFactory.CreateNotEqualsRule(parameter, value, caseSensitive);
#endif

        internal static FilterRule Contains(ElementId parameter, string value, bool caseSensitive)
#if REVIT2023_OR_GREATER
            => ParameterFilterRuleFactory.CreateContainsRule(parameter, value);
#else
            => ParameterFilterRuleFactory.CreateContainsRule(parameter, value, caseSensitive);
#endif

        internal static FilterRule NotContains(ElementId parameter, string value, bool caseSensitive)
#if REVIT2023_OR_GREATER
            => ParameterFilterRuleFactory.CreateNotContainsRule(parameter, value);
#else
            => ParameterFilterRuleFactory.CreateNotContainsRule(parameter, value, caseSensitive);
#endif

        internal static FilterRule BeginsWith(ElementId parameter, string value, bool caseSensitive)
#if REVIT2023_OR_GREATER
            => ParameterFilterRuleFactory.CreateBeginsWithRule(parameter, value);
#else
            => ParameterFilterRuleFactory.CreateBeginsWithRule(parameter, value, caseSensitive);
#endif

        internal static FilterRule NotBeginsWith(ElementId parameter, string value, bool caseSensitive)
#if REVIT2023_OR_GREATER
            => ParameterFilterRuleFactory.CreateNotBeginsWithRule(parameter, value);
#else
            => ParameterFilterRuleFactory.CreateNotBeginsWithRule(parameter, value, caseSensitive);
#endif

        internal static FilterRule EndsWith(ElementId parameter, string value, bool caseSensitive)
#if REVIT2023_OR_GREATER
            => ParameterFilterRuleFactory.CreateEndsWithRule(parameter, value);
#else
            => ParameterFilterRuleFactory.CreateEndsWithRule(parameter, value, caseSensitive);
#endif

        internal static FilterRule NotEndsWith(ElementId parameter, string value, bool caseSensitive)
#if REVIT2023_OR_GREATER
            => ParameterFilterRuleFactory.CreateNotEndsWithRule(parameter, value);
#else
            => ParameterFilterRuleFactory.CreateNotEndsWithRule(parameter, value, caseSensitive);
#endif
    }
}
