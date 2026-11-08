// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Asks a wheel slot the same question Revit asks a ribbon button before it greys it out - "can this tool run right now?" - so the wheel shows a tool as unavailable instead of appearing to run it and doing nothing.

using System;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HeronTools.Services.QuickMenu
{
    /// <summary>Mirrors the ribbon's greyed-out state onto a wheel slot.</summary>
    internal static class QuickMenuAvailability
    {
        /// <summary>One instance per availability class - they hold no state worth rebuilding.</summary>
        private static readonly Dictionary<string, IExternalCommandAvailability> _checkers =
            new Dictionary<string, IExternalCommandAvailability>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Class names already tried and found unusable, so they are not retried per open.</summary>
        private static readonly HashSet<string> _unresolvable =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// One "would the ribbon let this be clicked right now" answer per slot, in slot order. An
        /// empty slot and anything that cannot be determined both count as available - see the Notes
        /// block. This is the only entry point on purpose: the selected-categories set is expensive
        /// to build and must be built once for the whole wheel, never once per slot.
        /// </summary>
        private static bool CanPostRevitCommand(UIApplication uiapp, QuickToolEntry entry)
        {
            if (!entry.PostableCommandValue.HasValue)
            {
                return true;
            }

            try
            {
                RevitCommandId id = RevitCommandId.LookupPostableCommandId(entry.PostableCommandValue.Value);
                return id == null || uiapp.CanPostCommand(id);
            }
            catch (Exception)
            {
                return true;
            }
        }

        internal static IList<bool> Evaluate(UIApplication uiapp, IList<QuickToolEntry> slots)
        {
            var answers = new List<bool>(slots != null ? slots.Count : 0);
            if (slots == null)
            {
                return answers;
            }

            // Built at most once per wheel, and only if some slot actually has a rule to ask.
            CategorySet selectedCategories = null;

            foreach (QuickToolEntry entry in slots)
            {
                // An empty slot has nothing to grey out; the wheel draws it as empty either way.
                if (entry == null)
                {
                    answers.Add(true);
                    continue;
                }

                if (uiapp == null)
                {
                    answers.Add(false);
                    continue;
                }

                // Revit's own commands: ask Revit now, so a slot that would only end in "this command
                // cannot run here" is greyed instead. Anything that cannot be asked counts as available.
                if (entry.Source == QuickToolSource.Revit)
                {
                    answers.Add(CanPostRevitCommand(uiapp, entry));
                    continue;
                }

                // No availability class: Revit's default, enabled whenever a model is open - and the
                // wheel itself only opens with a model open.
                if (string.IsNullOrEmpty(entry.AvailabilityClassName))
                {
                    answers.Add(true);
                    continue;
                }

                IExternalCommandAvailability checker = Resolve(entry.AvailabilityClassName);
                if (checker == null)
                {
                    answers.Add(true);
                    continue;
                }

                if (selectedCategories == null)
                {
                    selectedCategories = SelectedCategories(uiapp);
                }

                try
                {
                    answers.Add(checker.IsCommandAvailable(uiapp, selectedCategories));
                }
                catch (Exception)
                {
                    // A rule that throws is a rule we cannot honour - leave the slot usable.
                    answers.Add(true);
                }
            }

            return answers;
        }

        /// <summary>The availability class named on the button, built once and kept. Null if unusable.</summary>
        private static IExternalCommandAvailability Resolve(string className)
        {
            IExternalCommandAvailability cached;
            if (_checkers.TryGetValue(className, out cached))
            {
                return cached;
            }

            if (_unresolvable.Contains(className))
            {
                return null;
            }

            IExternalCommandAvailability created = null;

            try
            {
                // Every Heron Tools availability class lives in this same add-in assembly.
                Type type = Assembly.GetExecutingAssembly().GetType(className, false, true);
                if (type != null)
                {
                    created = Activator.CreateInstance(type) as IExternalCommandAvailability;
                }
            }
            catch (Exception)
            {
                created = null;
            }

            if (created == null)
            {
                _unresolvable.Add(className);
                return null;
            }

            _checkers[className] = created;
            return created;
        }

        /// <summary>
        /// The categories of whatever is selected, which is the second argument Revit hands an
        /// availability class. An empty set when nothing is selected or the document is unavailable.
        /// </summary>
        private static CategorySet SelectedCategories(UIApplication uiapp)
        {
            var categories = new CategorySet();

            try
            {
                UIDocument uiDocument = uiapp.ActiveUIDocument;
                Document document = uiDocument != null ? uiDocument.Document : null;
                if (document == null)
                {
                    return categories;
                }

                foreach (ElementId id in uiDocument.Selection.GetElementIds())
                {
                    Element element = document.GetElement(id);
                    Category category = element != null ? element.Category : null;

                    if (category != null && !categories.Contains(category))
                    {
                        categories.Insert(category);
                    }
                }
            }
            catch (Exception)
            {
                // A selection we cannot read is the same as no selection for this question.
            }

            return categories;
        }
    }
}
