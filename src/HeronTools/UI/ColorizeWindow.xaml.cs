// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The Colorize window: the same category / parameter / value picks as Filter Pro, but the colour goes
// straight onto the matching elements in the target views. Shuffle colours and Restore original each run
// as one named transaction (one Undo step); the window stays open between clicks.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Commands;
using HeronTools.Models;
using HeronTools.Models.GraphicsTools;
using HeronTools.Services.Colorize;
using HeronTools.Services.FilterPro;
using HeronTools.Services.GraphicsTools;
using HeronTools.Utils;

namespace HeronTools.UI
{
    /// <summary>
    /// Colorize window. Modal (shown with ShowDialog from CmdColorize), so its Revit calls run on Revit's own
    /// thread inside the command - no ExternalEvent is needed.
    /// </summary>
    public partial class ColorizeWindow : Window
    {
        private readonly Document _doc;
        private readonly View _activeView;
        private readonly FilterProDataProvider _dataProvider;

        private readonly List<FilterValueItem> _currentValues = new List<FilterValueItem>();
        private readonly List<FilterCategoryItem> _allCategories = new List<FilterCategoryItem>();
        private readonly List<FilterParameterItem> _allParameters = new List<FilterParameterItem>();
        private List<ApplyViewItem> _allViews = new List<ApplyViewItem>();
        private List<PatternItem> _patterns = new List<PatternItem>();

        private bool _isLoadingParameters;
        private bool _isLoadingValues;
        // Set when the picks change while a list is still loading; the load then runs once more.
        private bool _reloadParameters;
        private bool _reloadValues;
        private bool _madeChanges;

        public ColorizeWindow(Document doc, View activeView)
        {
            InitializeComponent();

            Icon = BrandImages.Load(BrandImages.AppIcon);
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);
            TabMotionHelper.AttachTabTransitions(this);

            _doc = doc;
            _activeView = activeView;
            _dataProvider = new FilterProDataProvider(_doc);

            WireEvents();
            active_view_name_text.Text = _activeView != null ? _activeView.Name : "(none)";
            LoadCategories();
            LoadViewsForApply();
            LoadPatterns();
            UpdateApplyScopeLabel();
            UpdateCounts();
            UpdateRestoreButton();
            UpdateStatus("Pick one or more categories to begin.");
        }

        /// <summary>True when at least one click kept changes; CmdColorize then returns Succeeded.</summary>
        internal bool HasChanges => _madeChanges;

        // ---------------------------------------------------------------- wiring

        private void WireEvents()
        {
            close_button.Click += (s, e) => Close();   // CmdColorize reads HasChanges, not DialogResult
            shuffle_colors_button.Click += ShuffleColorsButton_Click;
            restore_button.Click += RestoreButton_Click;
            refresh_views_button.Click += (s, e) => LoadViewsForApply();
            values_select_all_button.Click += (s, e) => values_listbox.SelectAll();
            values_clear_button.Click += (s, e) => values_listbox.UnselectAll();

            // async void handlers: nothing may escape to Revit's message loop, so each one catches.
            categories_listbox.SelectionChanged += async (s, e) =>
            {
                try { await LoadParameters(); }
                catch (Exception ex) { UpdateStatus("Could not load the parameters for these categories: " + ex.Message); }
                UpdateCounts();
            };
            parameters_listbox.SelectionChanged += async (s, e) =>
            {
                try { await LoadValues(); }
                catch (Exception ex) { UpdateStatus("Could not load the values for this parameter: " + ex.Message); }
                UpdateCounts();
            };
            values_listbox.SelectionChanged += (s, e) => UpdateCounts();
            views_listbox.SelectionChanged += (s, e) => { UpdateApplyScopeLabel(); UpdateRestoreButton(); };

            apply_active_radio.Checked += (s, e) => { views_listbox.IsEnabled = false; UpdateApplyScopeLabel(); UpdateRestoreButton(); };
            apply_multiple_radio.Checked += (s, e) => { views_listbox.IsEnabled = true; UpdateApplyScopeLabel(); UpdateRestoreButton(); };

            value_search_textbox.TextChanged += (s, e) => ApplyValueFilters();
            value_sort_combobox.SelectionChanged += (s, e) => ApplyValueFilters();
            category_search_textbox.TextChanged += (s, e) => ApplyCategoryFilters();
            category_sort_combobox.SelectionChanged += (s, e) => ApplyCategoryFilters();
            parameter_search_textbox.TextChanged += (s, e) => ApplyParameterFilters();
            parameter_sort_combobox.SelectionChanged += (s, e) => ApplyParameterFilters();
        }

        // ---------------------------------------------------------------- the three picks

        private void LoadCategories()
        {
            try
            {
                _allCategories.Clear();
                _allCategories.AddRange(_dataProvider.GetFilterableCategories());
                categories_listbox.DisplayMemberPath = "Name";
                ApplyCategoryFilters();
            }
            catch (Exception ex)
            {
                UpdateStatus("Could not read the categories: " + ex.Message);
            }
        }

        private void ApplyCategoryFilters()
        {
            var source = _allCategories.AsEnumerable();

            // Picked categories always stay listed: a list that drops a picked item silently un-picks it.
            string term = category_search_textbox.Text;
            if (!string.IsNullOrWhiteSpace(term))
            {
                var picked = new HashSet<FilterCategoryItem>(categories_listbox.SelectedItems.Cast<FilterCategoryItem>());
                source = source.Where(c => picked.Contains(c) ||
                                           (c.Name != null && c.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            source = SortTag(category_sort_combobox) == "za" ? source.OrderByDescending(c => c.Name) : source.OrderBy(c => c.Name);
            categories_listbox.ItemsSource = source.ToList();
        }

        private async Task LoadParameters()
        {
            if (_isLoadingParameters) { _reloadParameters = true; return; }

            _isLoadingParameters = true;
            loading_indicator.Visibility = System.Windows.Visibility.Visible;
            try
            {
                var catIds = categories_listbox.SelectedItems.Cast<FilterCategoryItem>().Select(c => c.Id).ToList();
                if (!catIds.Any())
                {
                    _allParameters.Clear();
                    parameters_listbox.ItemsSource = null;
                    _currentValues.Clear();
                    values_listbox.ItemsSource = null;
                    UpdateStatus("Pick one or more categories.");
                    return;
                }

                await Task.Yield(); // let the loading cover paint
                _allParameters.Clear();
                _allParameters.AddRange(_dataProvider.GetParametersForCategories(catIds));
                parameters_listbox.DisplayMemberPath = "Name";
                ApplyParameterFilters();

                UpdateStatus("Pick a parameter to colour by value - or leave it empty to colour whole categories.");
                await LoadValues();
            }
            finally
            {
                _isLoadingParameters = false;
                loading_indicator.Visibility = System.Windows.Visibility.Collapsed;
            }

            if (_reloadParameters) { _reloadParameters = false; await LoadParameters(); }
        }

        private void ApplyParameterFilters()
        {
            var source = _allParameters.AsEnumerable();

            // The picked parameter always stays listed - losing it would quietly switch the run to whole categories.
            string term = parameter_search_textbox.Text;
            if (!string.IsNullOrWhiteSpace(term))
            {
                var picked = parameters_listbox.SelectedItem as FilterParameterItem;
                source = source.Where(p => ReferenceEquals(p, picked) ||
                                           (p.Name != null && p.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            source = SortTag(parameter_sort_combobox) == "za" ? source.OrderByDescending(p => p.Name) : source.OrderBy(p => p.Name);
            parameters_listbox.ItemsSource = source.ToList();
        }

        private async Task LoadValues()
        {
            if (_isLoadingValues) { _reloadValues = true; return; }

            _isLoadingValues = true;
            values_listbox.ItemsSource = null;
            _currentValues.Clear();
            loading_indicator.Visibility = System.Windows.Visibility.Visible;
            try
            {
                var param = parameters_listbox.SelectedItem as FilterParameterItem;
                var catIds = categories_listbox.SelectedItems.Cast<FilterCategoryItem>().Select(c => c.Id).ToList();
                if (param == null || !catIds.Any())
                    return;

                UpdateStatus("Reading the values...");
                await Task.Yield();

                var values = _dataProvider.GetValues(param, catIds, out bool hitScanLimit);
                _currentValues.AddRange(values);
                values_listbox.DisplayMemberPath = "Display";
                ApplyValueFilters();

                UpdateStatus(hitScanLimit
                    ? $"{values.Count} value(s) found. Reading stopped after {Constants.ELEMENT_SCAN_LIMIT:N0} elements to keep Revit quick."
                    : $"{values.Count} value(s) found. Pick the ones to colour.");
            }
            finally
            {
                _isLoadingValues = false;
                loading_indicator.Visibility = System.Windows.Visibility.Collapsed;
            }

            if (_reloadValues) { _reloadValues = false; await LoadValues(); }
        }

        private void ApplyValueFilters()
        {
            var source = _currentValues.AsEnumerable();

            string term = value_search_textbox.Text;
            if (!string.IsNullOrWhiteSpace(term))
            {
                var picked = new HashSet<FilterValueItem>(values_listbox.SelectedItems.Cast<FilterValueItem>());
                source = source.Where(v => picked.Contains(v) ||
                                           (v.Display != null && v.Display.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            source = SortTag(value_sort_combobox) == "za" ? source.OrderByDescending(v => v.Display) : source.OrderBy(v => v.Display);
            values_listbox.ItemsSource = source.ToList();
        }

        private static string SortTag(System.Windows.Controls.ComboBox combo) =>
            (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "az";

        // ---------------------------------------------------------------- counts and summary

        private void UpdateCounts()
        {
            if (categories_count_text == null) return;

            var cats = categories_listbox.SelectedItems.Cast<FilterCategoryItem>().ToList();
            var param = parameters_listbox.SelectedItem as FilterParameterItem;
            int valuePicked = values_listbox.SelectedItems.Count;

            categories_count_text.Text = cats.Count == 0
                ? $"None picked - {_allCategories.Count} available"
                : $"{cats.Count} of {_allCategories.Count} picked";

            parameters_count_text.Text = cats.Count == 0
                ? "Pick categories first"
                : param == null
                    ? $"None - whole categories ({_allParameters.Count} shared). Ctrl+click un-picks."
                    : $"{param.Name}. Ctrl+click to un-pick.";

            values_count_text.Text = param == null
                ? "Not needed for whole categories"
                : valuePicked == 0
                    ? $"None picked - {_currentValues.Count} found"
                    : valuePicked == 1
                        ? $"1 of {_currentValues.Count} picked - 1 colour"
                        : $"{valuePicked} of {_currentValues.Count} picked - {valuePicked} colours";

            // What gets coloured, in one plain line.
            string catText = cats.Count == 0 ? null
                : cats.Count <= 2 ? string.Join(", ", cats.Select(c => c.Name)) : $"{cats[0].Name} +{cats.Count - 1}";
            summary_text.Text = catText == null
                ? "Pick categories to begin"
                : param == null
                    ? $"{catText} - each category one colour"
                    : valuePicked == 0
                        ? $"{catText} by {param.Name} - pick values"
                        : valuePicked == 1
                            ? $"{catText} by {param.Name} - 1 value"
                            : $"{catText} by {param.Name} - {valuePicked} values, one colour each";
        }

        // ---------------------------------------------------------------- views, patterns, scope

        private void LoadViewsForApply()
        {
            try
            {
                var previouslySelected = new HashSet<long>(
                    views_listbox.SelectedItems.Cast<ApplyViewItem>().Select(v => v.Id.LongValue()));

                // Only views that take graphic overrides - the same test the ribbon uses.
                _allViews = new FilteredElementCollector(_doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v != null && CmdFilterProAvailability.CanViewHaveFilters(v, out _))
                    .Select(v => new ApplyViewItem(v.Id, v.Name, v.ViewType))
                    .OrderBy(v => v.ViewType)
                    .ThenBy(v => v.Name)
                    .ToList();
                views_listbox.ItemsSource = _allViews;

                foreach (ApplyViewItem item in _allViews)
                {
                    if (previouslySelected.Contains(item.Id.LongValue()))
                        views_listbox.SelectedItems.Add(item);
                }

                views_listbox.IsEnabled = apply_multiple_radio.IsChecked == true;
                UpdateApplyScopeLabel();
            }
            catch (Exception ex)
            {
                UpdateStatus("Could not read the views: " + ex.Message);
            }
        }

        private void LoadPatterns()
        {
            try
            {
                // Drafting patterns only: graphic overrides refuse a model pattern.
                _patterns = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .Where(p => p.GetFillPattern()?.Target == FillPatternTarget.Drafting)
                    .Select(p =>
                    {
                        FillPattern fp = p.GetFillPattern();
                        bool solidDrafting = fp != null && fp.IsSolidFill && fp.Target == FillPatternTarget.Drafting;
                        return new PatternItem(p.Id, solidDrafting ? "Solid fill" : p.Name);
                    })
                    .OrderBy(p => p.Name)
                    .ToList();
                pattern_combo.ItemsSource = _patterns;
                pattern_combo.SelectedItem = _patterns.FirstOrDefault(p => p.Name == "Solid fill") ?? _patterns.FirstOrDefault();
            }
            catch (Exception ex)
            {
                UpdateStatus("Could not read the fill patterns: " + ex.Message);
            }
        }

        private ElementId GetSelectedPatternId() =>
            (pattern_combo?.SelectedItem as PatternItem)?.Id ?? ElementId.InvalidElementId;

        private void UpdateApplyScopeLabel()
        {
            if (apply_scope_text == null) return;

            if (apply_active_radio.IsChecked == true)
            {
                apply_scope_text.Text = $"In: active view ({(_activeView != null ? _activeView.Name : "none")})";
                views_listbox.IsEnabled = false;
            }
            else
            {
                int count = views_listbox.SelectedItems.Count;
                apply_scope_text.Text = count == 0 ? "In: picked views - none picked yet"
                    : count == 1 ? "In: 1 picked view" : $"In: {count} picked views";
                views_listbox.IsEnabled = true;
            }
        }

        /// <summary>Target views for a run, or an empty list after telling the modeller what is missing.</summary>
        private List<View> ResolveTargetViews()
        {
            if (apply_active_radio.IsChecked == true)
            {
                if (_activeView == null)
                {
                    ShowProblem("There is no active view. Open a plan, section, elevation or 3D view and try again.");
                    return new List<View>();
                }
                return new List<View> { _activeView };
            }

            var picked = views_listbox.SelectedItems.Cast<ApplyViewItem>().ToList();
            if (picked.Count == 0)
            {
                ShowProblem("Pick at least one view in Apply > Picked views, or switch to Active view.");
                return new List<View>();
            }

            var views = picked.Select(i => _doc.GetElement(i.Id) as View).Where(v => v != null).ToList();
            if (views.Count == 0)
                ShowProblem("The picked views were not found any more. Click Refresh in the Apply tab and pick again.");
            return views;
        }

        private void UpdateRestoreButton()
        {
            if (restore_button == null) return;
            var views = apply_active_radio.IsChecked == true
                ? (_activeView != null ? new List<View> { _activeView } : new List<View>())
                : views_listbox.SelectedItems.Cast<ApplyViewItem>().Select(i => _doc.GetElement(i.Id) as View).Where(v => v != null).ToList();
            restore_button.IsEnabled = views.Any(v => ColorizeMemory.HasMemory(_doc, v));
        }

        // ---------------------------------------------------------------- actions

        private void ShuffleColorsButton_Click(object sender, RoutedEventArgs e)
        {
            FilterSelection selection = BuildColorizeSelection();
            if (selection == null)
                return;

            List<View> targetViews = ResolveTargetViews();
            if (targetViews.Count == 0)
                return;

            var skipped = new List<string>();
            GraphicsOperationSummary summary;
            try
            {
                // One named transaction = one Undo step; nothing changed is rolled back (no empty Undo entry).
                summary = GraphicsCommandService.ExecuteSummaryTransaction(
                    _doc, "Colorize: Shuffle colours",
                    () => ColorizeApplier.ApplyColorizeToViews(_doc, targetViews, selection, skipped));
            }
            catch (Exception ex)
            {
                ShowReport(null);
                ShowProblem("Nothing was changed - the run was undone. Revit said: " + ex.Message);
                return;
            }

            if (summary.HasChanges)
                _madeChanges = true;

            ShowReport(skipped);
            UpdateRestoreButton();
            UpdateStatus(summary.HasChanges
                ? $"Coloured {summary.Applied} element(s)" + (summary.Skipped > 0 ? $"  |  {summary.Skipped} not changed (see the list above)" : "")
                : "Nothing was coloured. " + (skipped.Count > 0 ? "See the list above."
                    : !string.IsNullOrWhiteSpace(summary.FirstSkipReason) ? "Revit said: " + summary.FirstSkipReason
                    : "No element in the target views matched the picks."));
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            List<View> targetViews = ResolveTargetViews();
            if (targetViews.Count == 0)
                return;

            int restored = 0;
            try
            {
                using (var t = new Transaction(_doc, "Colorize: Restore original"))
                {
                    t.Start();
                    foreach (View view in targetViews)
                        restored += ColorizeMemory.Restore(_doc, view);

                    if (restored == 0)
                    {
                        t.RollBack();
                    }
                    else if (t.Commit() != TransactionStatus.Committed)
                    {
                        ShowProblem("Revit did not keep the change, so the colours are still on. Try again, or press Undo.");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowProblem("Nothing was changed - the restore was undone. Revit said: " + ex.Message);
                return;
            }

            foreach (View view in targetViews)
                ColorizeMemory.Forget(_doc, view);
            if (restored > 0)
                _madeChanges = true;

            ShowReport(null);
            UpdateRestoreButton();
            UpdateStatus(restored > 0
                ? $"Restored the original graphics of {restored} element(s)."
                : "Nothing to restore in the target views.");
        }

        /// <summary>The picks as a selection for ColorizeApplier, or null after saying what is missing.</summary>
        private FilterSelection BuildColorizeSelection()
        {
            var catIds = categories_listbox.SelectedItems.Cast<FilterCategoryItem>().Select(c => c.Id).ToList();
            if (catIds.Count == 0)
            {
                ShowProblem("Pick at least one category (step 1).");
                return null;
            }

            var param = parameters_listbox.SelectedItem as FilterParameterItem;
            var values = values_listbox.SelectedItems.Cast<FilterValueItem>().ToList();
            if (param != null && values.Count == 0)
            {
                ShowProblem("Pick at least one value (step 3) - or Ctrl+click the parameter to un-pick it and colour whole categories.");
                return null;
            }

            if (color_proj_lines_checkbox.IsChecked != true && color_proj_patterns_checkbox.IsChecked != true &&
                color_cut_lines_checkbox.IsChecked != true && color_cut_patterns_checkbox.IsChecked != true &&
                color_halftone_checkbox.IsChecked != true)
            {
                ShowProblem("Tick at least one item in the Colours tab (lines, patterns or halftone).");
                return null;
            }

            return new FilterSelection
            {
                CategoryIds = catIds,
                Parameter = param,
                Values = param == null ? new List<FilterValueItem>() : values,
                RuleType = RuleTypes.EqualsRule,
                CaseSensitive = false,
                RandomColors = true,
                ColorProjectionLines = color_proj_lines_checkbox.IsChecked == true,
                ColorProjectionPatterns = color_proj_patterns_checkbox.IsChecked == true,
                ColorCutLines = color_cut_lines_checkbox.IsChecked == true,
                ColorCutPatterns = color_cut_patterns_checkbox.IsChecked == true,
                ColorHalftone = color_halftone_checkbox.IsChecked == true,
                PatternId = GetSelectedPatternId(),
                ApplyGraphics = true
            };
        }

        // ---------------------------------------------------------------- status and report

        private void UpdateStatus(string message)
        {
            status_text.Text = message;
            status_text.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
        }

        private void ShowProblem(string message)
        {
            status_text.Text = message;
            status_text.Foreground = (System.Windows.Media.Brush)FindResource("AccentWarning");
        }

        private void ShowReport(IList<string> skipped)
        {
            var lines = (skipped ?? new List<string>()).Distinct().ToList();
            if (lines.Count == 0)
            {
                report_panel.Visibility = System.Windows.Visibility.Collapsed;
                report_items.ItemsSource = null;
                return;
            }

            report_title_text.Text = lines.Count == 1 ? "1 note - why:" : $"{lines.Count} notes - why:";
            report_items.ItemsSource = lines;
            report_panel.Visibility = System.Windows.Visibility.Visible;
        }
    }
}
