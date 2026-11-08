// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// String constants for all supported filter rule operators, shared between the UI (FilterProWindow's rule selector) and the service layer (FilterCreator rule-building switch).

namespace HeronTools.Models
{
    internal static class RuleTypes
    {
        public const string EqualsRule = "equals";
        public const string NotEquals = "not_equals";

        public const string Contains = "contains";
        public const string NotContains = "not_contains";

        public const string BeginsWith = "begins_with";
        public const string NotBeginsWith = "not_begins_with";

        public const string EndsWith = "ends_with";
        public const string NotEndsWith = "not_ends_with";

        public const string Greater = "greater";
        public const string GreaterOrEqual = "greater_or_equal";

        public const string Less = "less";
        public const string LessOrEqual = "less_or_equal";

        public const string HasValue = "has_value";
        public const string HasNoValue = "has_no_value";

        /// <summary>The words the window shows for a rule, for messages to the modeller.</summary>
        public static string Label(string ruleType)
        {
            switch (ruleType)
            {
                case EqualsRule: return "Equals";
                case NotEquals: return "Does Not Equal";
                case Contains: return "Contains";
                case NotContains: return "Does Not Contain";
                case BeginsWith: return "Begins With";
                case NotBeginsWith: return "Does Not Begin With";
                case EndsWith: return "Ends With";
                case NotEndsWith: return "Does Not End With";
                case Greater: return "Is Greater Than";
                case GreaterOrEqual: return "Is Greater Than Or Equal To";
                case Less: return "Is Less Than";
                case LessOrEqual: return "Is Less Than Or Equal To";
                case HasValue: return "Has Value";
                case HasNoValue: return "Has No Value";
                default: return ruleType ?? "(none)";
            }
        }
    }
}
