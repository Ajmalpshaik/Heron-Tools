// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// WPF code-behind for the Filter Pro window — handles category/parameter/value selection, naming preview, graphics options, multi-view targeting, and delegates all Revit API work to FilterCreator, FilterApplier, and FilterReorderer.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Models;
using HeronTools.Services.FilterPro;

using HeronTools.Utils;
namespace HeronTools.UI
{
    /// <summary>
    /// Interaction logic for FilterProWindow.xaml
    /// </summary>
    public partial class FilterProWindow : Window
    {
        private readonly Document _doc;
        private readonly View _activeView;
        private readonly FilterProDataProvider _dataProvider;
        private readonly FilterProStateTracker _stateTracker;

        private readonly List<FilterValueItem> _currentValues = new List<FilterValueItem>();
        private readonly List<FilterCategoryItem> _allCategories = new List<FilterCategoryItem>();
        private readonly List<FilterParameterItem> _allParameters = new List<FilterParameterItem>();
        private bool _restoringState;
        private bool _isLoadingParameters;
        private bool _isLoadingValues;
        // Set when the picks change while a list is still loading; the load then runs once more, so the
        // list always matches the CURRENT picks (a quick Ctrl+click used to be ignored).
        private bool _reloadParameters;
        private bool _reloadValues;
        private List<ApplyViewItem> _allViews = new List<ApplyViewItem>();
        private List<PatternItem> _patterns = new List<PatternItem>();
        private bool _madeChanges;

        public FilterProWindow(Document doc, View activeView)
        {
            InitializeComponent();

            // The Heron app icon on the title bar and taskbar - the same mark as Heron AI.
            Icon = BrandImages.Load(BrandImages.AppIcon);

            // Shared Heron Tools window entrance (fade + short rise). Cosmetic only.
            WindowMotionHelper.AttachStandardEntrance(this);

            // Shared Heron Tools window exit (fade + short sink). The window's result,
            // validation and close behaviour are unchanged.
            WindowMotionHelper.AttachStandardExit(this);

            // Shared tab-change transition (fade + short rise on the panel). Cosmetic only.
            TabMotionHelper.AttachTabTransitions(this);
            _doc = doc;
            _activeView = activeView;
            _dataProvider = new FilterProDataProvider(_doc);
            _stateTracker = new FilterProStateTracker(_doc);

            WireEvents();
            SetActiveViewName();
            LoadCategories();
            LoadViewsForApply();
            LoadPatterns();
            // Restored once the window is up (inside ShowDialog's message loop), so its awaits come back on the
            // window's own thread.
            Loaded += (s, e) => RestoreLastSelection();

            if (_stateTracker.LastState == null)
                UpdateStatus("Select categories to begin.");

            UpdateApplyScopeLabel();
        }

        private void WireEvents()
        {
            close_button.Click += (s, e) =>
            {
                // Setting DialogResult just closes the window. Whether Revit keeps the changes is decided
                // by CmdFilterPro from HasChanges (each button already committed its own transaction).
                DialogResult = _madeChanges;
            };
            create_button.Click += CreateButton_Click;
            apply_view_button.Click += ApplyViewButton_Click;
            shuffle_colors_button.Click += ShuffleColorsButton_Click;
            refresh_views_button.Click += (s, e) => LoadViewsForApply();

            // List selection changes
            // These handlers are async void: an exception escaping them would reach Revit's own message
            // loop (an unhandled-error box, or worse). Each one catches and shows it in the status line.
            categories_listbox.SelectionChanged += async (s, e) =>
            {
                if (_restoringState) return;
                try
                {
                    await LoadParameters();
                    UpdateNamePreview();
                }
                catch (Exception ex)
                {
                    UpdateStatus("Could not load the parameters for these categories: " + ex.Message);
                }
            };

            parameters_listbox.SelectionChanged += async (s, e) =>
            {
                if (_restoringState) return;
                try
                {
                    await LoadValues();
                    UpdateNamePreview();
                }
                catch (Exception ex)
                {
                    UpdateStatus("Could not load the values for this parameter: " + ex.Message);
                }
            };

            values_listbox.SelectionChanged += (s, e) => { UpdateNamePreview(); UpdateCounts(); };
            views_listbox.SelectionChanged += (s, e) => UpdateApplyScopeLabel();
            categories_listbox.SelectionChanged += (s, e) => UpdateCounts();
            parameters_listbox.SelectionChanged += (s, e) => { UpdateCounts(); UpdateRuleAvailability(); };

            values_select_all_button.Click += (s, e) => values_listbox.SelectAll();
            values_clear_button.Click += (s, e) => values_listbox.UnselectAll();

            // Naming convention changes
            prefix_textbox.TextChanged += (s, e) => UpdateNamePreview();
            suffix_textbox.TextChanged += (s, e) => UpdateNamePreview();
            separator_textbox.TextChanged += (s, e) => UpdateNamePreview();
            include_cat_checkbox.Checked += (s, e) => UpdateNamePreview();
            include_cat_checkbox.Unchecked += (s, e) => UpdateNamePreview();
            include_param_checkbox.Checked += (s, e) => UpdateNamePreview();
            include_param_checkbox.Unchecked += (s, e) => UpdateNamePreview();

            apply_active_radio.Checked += (s, e) =>
            {
                views_listbox.IsEnabled = false;
                UpdateApplyScopeLabel();
            };
            apply_multiple_radio.Checked += (s, e) =>
            {
                views_listbox.IsEnabled = true;
                UpdateApplyScopeLabel();
            };

            // Rule changes
            foreach (var rb in new[]
                     {
                         radio_equals, radio_not_equals, radio_contains, radio_not_contains,
                         radio_starts, radio_not_starts, radio_ends, radio_not_ends,
                         radio_has_value, radio_not_has_value
                     })
            {
                rb.Checked += (s, e) => { UpdateNamePreview(); UpdateCounts(); };
            }

            // Value filtering and sorting
            value_search_textbox.TextChanged += (s, e) => ApplyValueFilters();
            value_sort_combobox.SelectionChanged += (s, e) => ApplyValueFilters();

            // Category filtering and sorting
            category_search_textbox.TextChanged += (s, e) => ApplyCategoryFilters();
            category_sort_combobox.SelectionChanged += (s, e) => ApplyCategoryFilters();

            // Parameter filtering and sorting
            parameter_search_textbox.TextChanged += (s, e) => ApplyParameterFilters();
            parameter_sort_combobox.SelectionChanged += (s, e) => ApplyParameterFilters();
        }

        private void ApplyValueFilters()
        {
            if (_currentValues == null) return;

            var source = _currentValues.AsEnumerable();

            // Filter by search text. Values already selected always stay in the list: a new ItemsSource
            // that leaves a selected item out makes WPF silently deselect it, so the run would use fewer
            // values than were picked (and clearing the search would not bring them back).
            string term = value_search_textbox.Text;
            if (!string.IsNullOrWhiteSpace(term))
            {
                var picked = new HashSet<FilterValueItem>(values_listbox.SelectedItems.Cast<FilterValueItem>());
                source = source.Where(v =>
                    picked.Contains(v) ||
                    v.Display.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // Sort
            if (value_sort_combobox.SelectedItem is ComboBoxItem selectedSort &&
                selectedSort.Tag is string sortTag)
            {
                if (sortTag == "za")
                    source = source.OrderByDescending(v => v.Display);
                else
                    source = source.OrderBy(v => v.Display);
            }

            values_listbox.ItemsSource = source.ToList();
        }

        private void ApplyCategoryFilters()
        {
            if (_allCategories == null) return;

            var source = _allCategories.AsEnumerable();

            // Filter by search text; selected categories always stay listed (see ApplyValueFilters).
            string term = category_search_textbox.Text;
            if (!string.IsNullOrWhiteSpace(term))
            {
                var picked = new HashSet<FilterCategoryItem>(categories_listbox.SelectedItems.Cast<FilterCategoryItem>());
                source = source.Where(c =>
                    picked.Contains(c) ||
                    (c.Name != null && c.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            // Sort
            if (category_sort_combobox.SelectedItem is ComboBoxItem selectedSort &&
                selectedSort.Tag is string sortTag)
            {
                if (sortTag == "za")
                    source = source.OrderByDescending(c => c.Name);
                else
                    source = source.OrderBy(c => c.Name);
            }

            categories_listbox.ItemsSource = source.ToList();
        }

        private void ApplyParameterFilters()
        {
            if (_allParameters == null) return;

            var source = _allParameters.AsEnumerable();

            // Filter by search text; the selected parameter always stays listed (see ApplyValueFilters) -
            // losing it would also clear the value list.
            string term = parameter_search_textbox.Text;
            if (!string.IsNullOrWhiteSpace(term))
            {
                var picked = parameters_listbox.SelectedItem as FilterParameterItem;
                source = source.Where(p =>
                    ReferenceEquals(p, picked) ||
                    (p.Name != null && p.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            // Sort
            if (parameter_sort_combobox.SelectedItem is ComboBoxItem selectedSort &&
                selectedSort.Tag is string sortTag)
            {
                if (sortTag == "za")
                    source = source.OrderByDescending(p => p.Name);
                else
                    source = source.OrderBy(p => p.Name);
            }

            parameters_listbox.ItemsSource = source.ToList();
        }

        private void SetActiveViewName()
        {
            active_view_name_text.Text = _activeView != null ? _activeView.Name : "(none)";
        }

        private void LoadViewsForApply()
        {
            try
            {
                var previouslySelected = new HashSet<long>(
                    views_listbox.SelectedItems
                        .Cast<ApplyViewItem>()
                        .Select(v => v.Id.LongValue()));

                // Only views that can actually take filters - the same test the ribbon button uses
                // (plan, ceiling, area, section, elevation, 3D, with graphics overrides allowed). Sheets,
                // legends, drafting views and schedules used to be listed and then fail.
                var views = new FilteredElementCollector(_doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v != null && Commands.CmdFilterProAvailability.CanViewHaveFilters(v, out _))
                    .Select(v => new ApplyViewItem(v.Id, v.Name, v.ViewType))
                    .OrderBy(v => v.ViewType)
                    .ThenBy(v => v.Name)
                    .ToList();

                _allViews = views;
                views_listbox.ItemsSource = _allViews;

                if (previouslySelected.Any())
                {
                    views_listbox.SelectedItems.Clear();
                    foreach (ApplyViewItem item in _allViews)
                    {
                        if (previouslySelected.Contains(item.Id.LongValue()))
                            views_listbox.SelectedItems.Add(item);
                    }
                }

                if (_restoringState)
                    return;

                views_listbox.IsEnabled = apply_multiple_radio.IsChecked == true;
                UpdateApplyScopeLabel();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading views: {ex.Message}");
            }
        }

        private void LoadPatterns()
        {
            try
            {
                // Drafting patterns only: graphic overrides refuse a model pattern.
                var patterns = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .Where(p => p.GetFillPattern()?.Target == FillPatternTarget.Drafting)
                    .Select(p =>
                    {
                        var fp = p.GetFillPattern();
                        bool isSolid = fp != null && fp.IsSolidFill;
                        string name = isSolid ? "Solid Fill" : p.Name;
                        return new PatternItem(p.Id, name);
                    })
                    .OrderBy(p => p.Name)
                    .ToList();

                _patterns = patterns;
                pattern_combo.ItemsSource = _patterns;

                // Default to Solid Fill if available
                var solid = _patterns.FirstOrDefault(p =>
                    string.Equals(p.Name, "Solid Fill", StringComparison.OrdinalIgnoreCase));
                if (solid != null)
                    pattern_combo.SelectedItem = solid;
                else if (_patterns.Any())
                    pattern_combo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading patterns: {ex.Message}");
            }
        }

        private void UpdateNamePreview()
        {
            if (preview_text == null) return;

            var param = parameters_listbox.SelectedItem as FilterParameterItem;
            var values = values_listbox.SelectedItems.Cast<FilterValueItem>().ToList();

            string valueText = "Value";
            if (values.Count == 1)
                valueText = values[0].Display;
            else if (values.Count > 1)
                valueText = $"{values.Count} Values";

            string separator = string.IsNullOrEmpty(separator_textbox.Text)
                ? "_"
                : separator_textbox.Text;

            var tempSelection = new FilterSelection
            {
                CategoryIds = categories_listbox.SelectedItems
                    .Cast<FilterCategoryItem>()
                    .Select(c => c.Id)
                    .ToList(),
                Parameter = param,
                Prefix = prefix_textbox.Text,
                Suffix = suffix_textbox.Text,
                IncludeCategory = include_cat_checkbox.IsChecked == true,
                IncludeParameter = include_param_checkbox.IsChecked == true,
                Separator = separator,
                RuleType = GetSelectedRuleType(),
                CaseSensitive = false
            };

            var previewValue = new FilterValueItem(valueText, valueText, StorageType.String);

            // Called from many typing/ticking events; a failure here only blanks the preview, never
            // reaches Revit.
            try
            {
                preview_text.Text = FilterCreator.ComposeFilterName(tempSelection, previewValue, _doc);
            }
            catch (Exception ex)
            {
                preview_text.Text = "(preview not available)";
                UpdateStatus("Could not build the name preview: " + ex.Message);
            }
        }

        private void UpdateApplyScopeLabel()
        {
            if (apply_scope_text == null) return;

            if (apply_active_radio.IsChecked == true)
            {
                string viewName = _activeView != null ? _activeView.Name : "none";
                apply_scope_text.Text = $"Apply to: active view ({viewName})";
                views_listbox.IsEnabled = false;
            }
            else
            {
                int count = views_listbox.SelectedItems.Count;
                apply_scope_text.Text = count == 0
                    ? "Apply to: picked views - none picked yet"
                    : count == 1 ? "Apply to: 1 picked view" : $"Apply to: {count} picked views";
                views_listbox.IsEnabled = true;
            }
        }

        /// <summary>
        /// The "N of M picked" line under each step title, so the modeller always sees what the run will use.
        /// </summary>
        private void UpdateCounts()
        {
            if (categories_count_text == null) return;

            int catTotal = _allCategories.Count;
            int catPicked = categories_listbox.SelectedItems.Count;
            categories_count_text.Text = catPicked == 0
                ? $"None picked - {catTotal} available"
                : $"{catPicked} of {catTotal} picked";

            int paramTotal = _allParameters.Count;
            var param = parameters_listbox.SelectedItem as FilterParameterItem;
            parameters_count_text.Text = catPicked == 0
                ? "Pick categories first"
                : param == null
                    ? $"None picked - {paramTotal} shared"
                    : $"{param.Name} - {DescribeStorage(param)}";

            int valueTotal = _currentValues.Count;
            int valuePicked = values_listbox.SelectedItems.Count;
            string rule = GetSelectedRuleType();
            values_count_text.Text = param == null
                ? "Pick a parameter first"
                : rule == RuleTypes.HasValue || rule == RuleTypes.HasNoValue
                    ? "Not needed - this rule makes 1 filter"
                : valuePicked == 0
                    ? $"None picked - {valueTotal} found"
                    : valuePicked == 1
                        ? $"1 of {valueTotal} picked - 1 filter"
                        : $"{valuePicked} of {valueTotal} picked - {valuePicked} filters";
        }

        private static string DescribeStorage(FilterParameterItem param)
        {
            if (param.Id == SpecialParameterIds.FamilyAndType)
                return "family + type";

            switch (param.StorageType)
            {
                case StorageType.String: return "text";
                case StorageType.Integer: return "whole number or Yes/No";
                case StorageType.Double: return "number";
                case StorageType.ElementId: return "element (type, level, system...)";
                default: return "value";
            }
        }

        /// <summary>
        /// Switches off the rules the picked parameter cannot do, so the modeller cannot choose a rule that
        /// would only be refused later: text rules (Contains, Begins With...) need a text parameter, and
        /// "Family and Type" can only be matched with Equals. If the ticked rule is switched off, Equals is
        /// ticked instead and the status line says so.
        /// </summary>
        private void UpdateRuleAvailability()
        {
            var param = parameters_listbox.SelectedItem as FilterParameterItem;
            bool isFamilyAndType = param != null && param.Id == SpecialParameterIds.FamilyAndType;
            bool isText = param == null || (param.StorageType == StorageType.String && !isFamilyAndType);

            foreach (var rb in new[] { radio_contains, radio_not_contains, radio_starts, radio_not_starts, radio_ends, radio_not_ends })
                rb.IsEnabled = isText;

            radio_not_equals.IsEnabled = !isFamilyAndType;
            radio_has_value.IsEnabled = !isFamilyAndType;
            radio_not_has_value.IsEnabled = !isFamilyAndType;

            var ticked = new[]
            {
                radio_equals, radio_not_equals, radio_contains, radio_not_contains, radio_starts,
                radio_not_starts, radio_ends, radio_not_ends, radio_has_value, radio_not_has_value
            }.FirstOrDefault(r => r.IsChecked == true);

            if (ticked != null && !ticked.IsEnabled)
            {
                radio_equals.IsChecked = true;
                UpdateStatus($"'{ticked.Content}' does not work with '{param?.Name}', so the rule was set to Equals.");
            }
        }

        private ElementId GetSelectedPatternId()
        {
            if (pattern_combo?.SelectedItem is PatternItem item)
                return item.Id;
            if (pattern_combo?.SelectedValue is ElementId eid)
                return eid;
            return ElementId.InvalidElementId;
        }

        private void RestorePatternSelection(ElementId savedId)
        {
            if (savedId == null || savedId == ElementId.InvalidElementId || pattern_combo == null)
                return;

            foreach (PatternItem item in pattern_combo.Items)
            {
                if (item.Id != null && item.Id.LongValue() == savedId.LongValue())
                {
                    pattern_combo.SelectedItem = item;
                    return;
                }
            }
        }

        private List<ElementId> GetSelectedViewIds()
        {
            if (apply_active_radio.IsChecked == true)
            {
                if (_activeView != null)
                    return new List<ElementId> { _activeView.Id };
                return new List<ElementId>();
            }

            return views_listbox.SelectedItems
                .Cast<ApplyViewItem>()
                .Select(v => v.Id)
                .ToList();
        }

        private List<View> ResolveTargetViews(out List<ElementId> targetIds)
        {
            targetIds = new List<ElementId>();
            var results = new List<View>();

            if (apply_active_radio.IsChecked == true)
            {
                if (_activeView == null)
                {
                    ShowProblem("There is no active view. Open a plan, section, elevation or 3D view and try again.");
                    return results;
                }

                targetIds.Add(_activeView.Id);
                results.Add(_activeView);
                return results;
            }

            var selectedItems = views_listbox.SelectedItems.Cast<ApplyViewItem>().ToList();
            if (!selectedItems.Any())
            {
                ShowProblem("Pick at least one view in Apply > Picked views, or switch to Active view.");
                return results;
            }

            foreach (var item in selectedItems)
            {
                targetIds.Add(item.Id);
                var view = _doc.GetElement(item.Id) as View;
                if (view != null)
                    results.Add(view);
            }

            if (!results.Any())
                ShowProblem("The picked views were not found any more. Click Refresh in the Apply tab and pick again.");

            return results;
        }

        private async void RestoreLastSelection()
        {
            var lastState = _stateTracker.LastState;
            if (lastState == null) return;

            string restoredMessage = "Restored previous Filter Pro selection.";
            _restoringState = true;
            try
            {
                await RestoreCategoriesAndParameters(lastState);
                await RestoreValues(lastState);
                RestoreRuleTypeAndNaming(lastState);
                RestoreGraphicOverrides(lastState);
                RestoreApplyScope(lastState);
            }
            catch (Exception ex)
            {
                // async void, started from the constructor: nothing may escape to Revit. The window still
                // works; only the remembered picks are not filled in.
                restoredMessage = "Could not restore the previous selection (" + ex.Message + "). Pick again.";
            }
            finally
            {
                _restoringState = false;
            }

            UpdateCounts();
            UpdateRuleAvailability();
            UpdateStatus(restoredMessage);
        }

        private async Task RestoreCategoriesAndParameters(FilterProState lastState)
        {
            if (lastState.CategoryIds?.Any() == true)
            {
                categories_listbox.SelectedItems.Clear();
                var wanted = new HashSet<long>(lastState.CategoryIds.Select(id => id.LongValue()));
                foreach (FilterCategoryItem item in categories_listbox.Items)
                {
                    if (wanted.Contains(item.Id.LongValue()))
                        categories_listbox.SelectedItems.Add(item);
                }

                if (!parameters_listbox.Items.Cast<object>().Any())
                    await LoadParameters();
            }

            if (lastState.ParameterId != null)
            {
                foreach (FilterParameterItem item in parameters_listbox.Items)
                {
                    if (item.Id.LongValue() == lastState.ParameterId.LongValue())
                    {
                        parameters_listbox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private async Task RestoreValues(FilterProState lastState)
        {
            if (!values_listbox.Items.Cast<object>().Any())
                await LoadValues();

            if (lastState.Values != null && lastState.Values.Any())
            {
                values_listbox.SelectedItems.Clear();
                foreach (FilterValueItem valueItem in values_listbox.Items)
                {
                    if (FilterValueKeyMatcher.MatchesValue(valueItem, lastState.Values))
                        values_listbox.SelectedItems.Add(valueItem);
                }
            }
        }

        private void RestoreRuleTypeAndNaming(FilterProState lastState)
        {
            ApplyRuleTypeSelection(lastState.RuleType);
            prefix_textbox.Text = lastState.Prefix ?? string.Empty;
            suffix_textbox.Text = lastState.Suffix ?? string.Empty;
            separator_textbox.Text = string.IsNullOrEmpty(lastState.Separator) ? "_" : lastState.Separator;
            include_cat_checkbox.IsChecked = lastState.IncludeCategory;
            include_param_checkbox.IsChecked = lastState.IncludeParameter;
            // Never restored ON: it rewrites same-named filters project-wide, so each run asks for it afresh.
            override_existing_checkbox.IsChecked = false;
        }

        private void RestoreGraphicOverrides(FilterProState lastState)
        {
            bool hasColorPrefs = lastState.ColorProjectionLines || lastState.ColorProjectionPatterns || lastState.ColorCutLines || lastState.ColorCutPatterns || lastState.ColorHalftone;

            if (hasColorPrefs)
            {
                color_proj_lines_checkbox.IsChecked = lastState.ColorProjectionLines;
                color_proj_patterns_checkbox.IsChecked = lastState.ColorProjectionPatterns;
                color_cut_lines_checkbox.IsChecked = lastState.ColorCutLines;
                color_cut_patterns_checkbox.IsChecked = lastState.ColorCutPatterns;
                color_halftone_checkbox.IsChecked = lastState.ColorHalftone;
            }
            else
            {
                color_proj_lines_checkbox.IsChecked = true;
                color_cut_lines_checkbox.IsChecked = true;
                color_proj_patterns_checkbox.IsChecked = false;
                color_cut_patterns_checkbox.IsChecked = false;
                color_halftone_checkbox.IsChecked = false;
            }

            RestorePatternSelection(lastState.PatternId);
        }

        private void RestoreApplyScope(FilterProState lastState)
        {
            if (lastState.ApplyToActiveView || lastState.TargetViewIds == null || lastState.TargetViewIds.Count == 0)
            {
                apply_active_radio.IsChecked = true;
            }
            else
            {
                apply_multiple_radio.IsChecked = true;
                views_listbox.IsEnabled = true;
                var wantedViews = new HashSet<long>(lastState.TargetViewIds.Select(id => id.LongValue()));
                views_listbox.SelectedItems.Clear();
                foreach (ApplyViewItem item in views_listbox.Items)
                {
                    if (wantedViews.Contains(item.Id.LongValue()))
                        views_listbox.SelectedItems.Add(item);
                }
            }
            UpdateApplyScopeLabel();
        }

        private void ApplyRuleTypeSelection(string ruleType)
        {
            if (string.IsNullOrEmpty(ruleType))
                ruleType = RuleTypes.EqualsRule;

            foreach (var panel in rule_type_panel.Children.OfType<StackPanel>())
            {
                foreach (var child in panel.Children.OfType<RadioButton>())
                {
                    if (child.Tag as string == ruleType)
                    {
                        child.IsChecked = true;
                        return;
                    }
                }
            }
        }

        private void RememberState(FilterSelection selection)
        {
            _stateTracker.Save(
                selection,
                separator_textbox.Text,
                apply_active_radio.IsChecked == true,
                GetSelectedViewIds(),
                false);
        }

        private string BuildResultStatus(int totalChecked, int applied, IList<string> skipped)
        {
            int skippedCount = skipped?.Count ?? 0;
            string summary = $"Checked {totalChecked}  |  Made or applied {applied}  |  Skipped {skippedCount}";
            return skippedCount > 0 ? summary + " (see the list above)" : summary;
        }

        private FilterSelection BuildFilterSelection(
            bool requiresTargetViews,
            out List<View> targetViews,
            out List<FilterValueItem> selectedValues)
        {
            targetViews = new List<View>();
            selectedValues = new List<FilterValueItem>();

            var catIds = categories_listbox.SelectedItems
                .Cast<FilterCategoryItem>()
                .Select(c => c.Id)
                .ToList();
            if (!catIds.Any())
            {
                ShowProblem("Pick at least one category (step 1).");
                return null;
            }

            var param = parameters_listbox.SelectedItem as FilterParameterItem;
            if (param == null)
            {
                ShowProblem("Pick a parameter (step 2).");
                return null;
            }

            var ruleType = GetSelectedRuleType();
            selectedValues = values_listbox.SelectedItems
                .Cast<FilterValueItem>()
                .ToList();

            if (selectedValues.Count == 0 &&
                ruleType != RuleTypes.HasValue &&
                ruleType != RuleTypes.HasNoValue)
            {
                ShowProblem("Pick at least one value (step 3), or use the rule Has a value / Has no value.");
                return null;
            }

            if (requiresTargetViews)
            {
                targetViews = ResolveTargetViews(out var targetIds);
                if (!targetViews.Any())
                    return null;
            }

            string separator = string.IsNullOrEmpty(separator_textbox.Text)
                ? "_"
                : separator_textbox.Text;

            bool presenceRule = ruleType == RuleTypes.HasValue || ruleType == RuleTypes.HasNoValue;
            var valuesForCreation = presenceRule || selectedValues.Count == 0
                ? new List<FilterValueItem>
                  {
                      new FilterValueItem("Any", null, param.StorageType)
                  }
                : selectedValues;

            return new FilterSelection
            {
                CategoryIds = catIds,
                Parameter = param,
                Values = valuesForCreation,
                RuleType = ruleType,
                OverrideExisting = override_existing_checkbox.IsChecked == true,
                ColorProjectionLines = color_proj_lines_checkbox.IsChecked == true,
                ColorProjectionPatterns = color_proj_patterns_checkbox.IsChecked == true,
                ColorCutLines = color_cut_lines_checkbox.IsChecked == true,
                ColorCutPatterns = color_cut_patterns_checkbox.IsChecked == true,
                ColorHalftone = color_halftone_checkbox.IsChecked == true,
                PatternId = GetSelectedPatternId(),
                PlaceNewFiltersFirst = true,
                Prefix = prefix_textbox.Text,
                Suffix = suffix_textbox.Text,
                IncludeCategory = include_cat_checkbox.IsChecked == true,
                IncludeParameter = include_param_checkbox.IsChecked == true,
                TargetViewIds = targetViews.Select(v => v.Id).ToList(),
                ApplyToActiveView = apply_active_radio.IsChecked == true,
                Separator = separator,
                CaseSensitive = false
            };
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            var selection = BuildFilterSelection(false, out _, out var selectedValues);
            if (selection == null) return;

            selection.ApplyToView = false;
            selection.ApplyGraphics = false;
            selection.RandomColors = false;

            RunAsOneUndoStep("Filter Pro: Create Filters", selection, new List<View>());
        }

        /// <summary>
        /// Runs one button's work as ONE named transaction - one entry in Revit's Undo list.
        /// If anything fails, or Revit does not commit, everything this run did is rolled back and the
        /// modeller is told in plain words that nothing was changed. Never half a set of filters.
        /// </summary>
        private void RunAsOneUndoStep(string undoName, FilterSelection selection, List<View> targetViews)
        {
            var skipped = new List<string>();
            int affected = 0;

            using (var t = new Transaction(_doc, undoName))
            {
                try
                {
                    t.Start();
                    affected = FilterProHelper.CreateFilters(_doc, targetViews, selection, skipped);

                    if (affected == 0)
                    {
                        // Nothing to keep: roll back so no empty step lands in the Undo list.
                        t.RollBack();
                        FilterReorderer.DiscardPendingOrders();
                    }
                    else
                    {
                        TransactionStatus status = t.Commit();
                        if (status != TransactionStatus.Committed)
                            throw new FilterProException(
                                "Revit did not keep the changes (it reported: " + status + ").");
                        FilterReorderer.AcceptPendingOrders();
                        _madeChanges = true;
                    }
                }
                catch (Exception ex)
                {
                    FilterReorderer.DiscardPendingOrders();
                    string reason = ex is FilterProException
                        ? ex.Message
                        : "Revit stopped the run: " + ex.Message;

                    // Roll back only a transaction that is still open. A failed rollback must not hide the
                    // first reason, so it is added to the message rather than thrown.
                    try
                    {
                        if (t.GetStatus() == TransactionStatus.Started)
                            t.RollBack();
                    }
                    catch (Exception rollbackEx)
                    {
                        reason += "\n\nRevit also reported, while undoing: " + rollbackEx.Message +
                                  "\nCheck the model, and use Undo if anything from this run is still there.";
                    }

                    TaskDialog.Show(
                        "Filter Pro",
                        "Nothing was changed - the whole run was undone.\n\n" + reason);
                    ShowReport(null);
                    ShowProblem("Nothing was changed. " + reason);
                    return;
                }
            }

            RememberState(selection);
            ShowReport(skipped);
            UpdateStatus(affected == 0
                ? (skipped.Count > 0 ? "Nothing was changed. " : "Nothing needed changing. ") + BuildResultStatus(selection.Values.Count, affected, skipped)
                : BuildResultStatus(selection.Values.Count, affected, skipped));
        }

        private string GetSelectedRuleType()
        {
            if (radio_equals.IsChecked == true) return RuleTypes.EqualsRule;
            if (radio_not_equals.IsChecked == true) return RuleTypes.NotEquals;
            if (radio_contains.IsChecked == true) return RuleTypes.Contains;
            if (radio_not_contains.IsChecked == true) return RuleTypes.NotContains;
            if (radio_starts.IsChecked == true) return RuleTypes.BeginsWith;
            if (radio_not_starts.IsChecked == true) return RuleTypes.NotBeginsWith;
            if (radio_ends.IsChecked == true) return RuleTypes.EndsWith;
            if (radio_not_ends.IsChecked == true) return RuleTypes.NotEndsWith;
            if (radio_has_value.IsChecked == true) return RuleTypes.HasValue;
            if (radio_not_has_value.IsChecked == true) return RuleTypes.HasNoValue;
            return RuleTypes.EqualsRule; // Default fallback
        }

        private void LoadCategories()
        {
            try
            {
                var cats = _dataProvider.GetFilterableCategories();
                _allCategories.Clear();
                _allCategories.AddRange(cats);

                categories_listbox.DisplayMemberPath = "Name";
                ApplyCategoryFilters();
                UpdateCounts();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading categories: {ex.Message}");
            }
        }

        private async Task LoadParameters()
        {
            if (_isLoadingParameters)
            {
                _reloadParameters = true;
                return;
            }

            _isLoadingParameters = true;
            loading_indicator.Visibility = System.Windows.Visibility.Visible;
            try
            {
                var selectedCategories = categories_listbox.SelectedItems
                    .Cast<FilterCategoryItem>()
                    .ToList();

                if (!selectedCategories.Any())
                {
                    _allParameters.Clear();
                    parameters_listbox.ItemsSource = null;
                    values_listbox.ItemsSource = null;
                    UpdateStatus("Select one or more categories.");
                    return;
                }

                await Task.Yield(); // Let the UI render the loading overlay.

                var catIds = selectedCategories.Select(c => c.Id).ToList();
                var parameters = _dataProvider.GetParametersForCategories(catIds);

                _allParameters.Clear();
                _allParameters.AddRange(parameters);

                parameters_listbox.DisplayMemberPath = "Name";
                ApplyParameterFilters();
                UpdateCounts();
                UpdateRuleAvailability();

                UpdateStatus("Select a parameter to load its values.");
                await LoadValues();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading parameters: {ex.Message}");
            }
            finally
            {
                _isLoadingParameters = false;
                loading_indicator.Visibility = System.Windows.Visibility.Collapsed;
            }

            if (_reloadParameters)
            {
                _reloadParameters = false;
                await LoadParameters();
            }
        }

        private async Task LoadValues()
        {
            if (_isLoadingValues)
            {
                _reloadValues = true;
                return;
            }

            _isLoadingValues = true;
            values_listbox.ItemsSource = null;
            loading_indicator.Visibility = System.Windows.Visibility.Visible;

            try
            {
                var param = parameters_listbox.SelectedItem as FilterParameterItem;
                var catIds = categories_listbox.SelectedItems
                    .Cast<FilterCategoryItem>()
                    .Select(c => c.Id)
                    .ToList();

                if (param == null || !catIds.Any())
                {
                    UpdateStatus("Select a parameter to load its values.");
                    return;
                }

                UpdateStatus("Collecting values...");
                await Task.Yield(); // Keep work on the Revit UI thread while showing progress.

                bool hitScanLimit;
                var collectedValues = _dataProvider.GetValues(param, catIds, out hitScanLimit);

                _currentValues.Clear();
                _currentValues.AddRange(collectedValues);

                ApplyValueFilters();   // keeps the search text and sort order already set
                values_listbox.DisplayMemberPath = "Display";
                UpdateCounts();

                if (hitScanLimit)
                    UpdateStatus($"Loaded {collectedValues.Count} unique value(s). Stopped after scanning 10,000 elements for performance.");
                else
                    UpdateStatus($"Loaded {collectedValues.Count} unique value(s).");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading values: {ex.Message}");
            }
            finally
            {
                _isLoadingValues = false;
                loading_indicator.Visibility = System.Windows.Visibility.Collapsed;
            }

            if (_reloadValues)
            {
                _reloadValues = false;
                await LoadValues();
            }
        }

        private void UpdateStatus(string message)
        {
            if (_restoringState)
                return;

            status_text.Text = message;
            status_text.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
        }

        /// <summary>
        /// Something the modeller must fix before the run can start: shown in the status line in the
        /// Warning colour - no pop-up, nothing changed.
        /// </summary>
        private void ShowProblem(string message)
        {
            status_text.Text = message;
            status_text.Foreground = (System.Windows.Media.Brush)FindResource("AccentWarning");
        }

        /// <summary>
        /// Lists what a run skipped, and why, in the report strip above the footer. Hidden when nothing
        /// was skipped.
        /// </summary>
        private void ShowReport(IList<string> skipped)
        {
            var lines = (skipped ?? new List<string>()).Distinct().ToList();
            if (lines.Count == 0)
            {
                report_panel.Visibility = System.Windows.Visibility.Collapsed;
                report_items.ItemsSource = null;
                return;
            }

            report_title_text.Text = lines.Count == 1 ? "1 item skipped - why:" : $"{lines.Count} items skipped - why:";
            report_items.ItemsSource = lines;
            report_panel.Visibility = System.Windows.Visibility.Visible;
        }

        private void ApplyViewButton_Click(object sender, RoutedEventArgs e)
        {
            var selection = BuildFilterSelection(true, out var targetViews, out var selectedValues);
            if (selection == null) return;

            selection.ApplyToView = true;
            selection.ApplyGraphics = false;
            selection.RandomColors = false;
            // OverrideExisting stays as the "Update Existing Filters" toggle says: an existing same-named
            // filter is always reused (FilterCreator, via ApplyToView), but only rewritten when it is on.

            RunAsOneUndoStep("Filter Pro: Apply Filters to View", selection, targetViews);
        }

        private void ShuffleColorsButton_Click(object sender, RoutedEventArgs e)
        {
            var selection = BuildFilterSelection(true, out var targetViews, out var selectedValues);
            if (selection == null) return;

            selection.ApplyToView = true;
            selection.ApplyGraphics = true;
            selection.RandomColors = true;
            selection.OverrideExisting = false;   // Shuffle only recolours - it never rewrites a filter's rule

            if (!selection.ColorProjectionLines && !selection.ColorProjectionPatterns && !selection.ColorCutLines &&
                !selection.ColorCutPatterns && !selection.ColorHalftone)
            {
                ShowProblem("Tick at least one item in the Colours tab (lines, patterns or halftone).");
                return;
            }

            RunAsOneUndoStep("Filter Pro: Shuffle Colors", selection, targetViews);
        }

        // Exposed to the external command so it can return Succeeded when changes were made.
        internal bool HasChanges => _madeChanges;
    }
}
