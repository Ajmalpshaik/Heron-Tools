// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The Reset Graphics window. Modal (opened from its command), so its buttons may change the model directly:
// each Reset or Restore is its own named Undo step, committed before the window carries on. The counting and
// resetting are ResetGraphicsService's; this file only gathers the choices, shows the count, and says what
// happened. It remembers its choices (WindowMemory "reset-graphics"): what, where, the picked views BY NAME,
// and "leave Heron's colours".

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using HeronTools.Services.ResetGraphics;
using HeronTools.Utils;

namespace HeronTools.UI
{
    public partial class ResetGraphicsWindow : Window
    {
        /// <summary>Counting many views on every click would be slow; beyond this many, Reset counts as it goes.</summary>
        private const int CountViewsUpTo = 15;

        private readonly Document _doc;
        private readonly View _activeView;
        private readonly ObservableCollection<ViewChoice> _views = new ObservableCollection<ViewChoice>();
        private bool _loading = true;

        /// <summary>True when a Reset or Restore in this window committed a change.</summary>
        internal bool ChangedSomething { get; private set; }

        public ResetGraphicsWindow(Document doc, View activeView)
        {
            InitializeComponent();
            Icon = BrandImages.Load(BrandImages.AppIcon);
            WindowMotionHelper.AttachStandardEntrance(this);

            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _activeView = activeView;
            ThisViewText.Text = "This view (" + (activeView?.Name ?? "none") + ")";

            foreach (View view in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                         .Where(v => !v.IsTemplate && SafeOverridesAllowed(v))
                         .OrderBy(v => v.ViewType.ToString()).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
            {
                var choice = new ViewChoice(view);
                choice.PropertyChanged += (s, e) => { if (!_loading) { UpdateViewsCount(); Recount(); } };
                _views.Add(choice);
            }
            ViewsList.ItemsSource = _views;

            RestoreChoices();

            foreach (RadioButton radio in new[] { ElementsRadio, CategoriesRadio, BothRadio, ThisViewRadio, PickedViewsRadio })
                radio.Checked += (s, e) => { if (!_loading) { ShowViewsPanel(); Recount(); } };
            KeepHeronCheck.Checked += (s, e) => { if (!_loading) Recount(); };
            KeepHeronCheck.Unchecked += (s, e) => { if (!_loading) Recount(); };
            ViewSearchBox.TextChanged += (s, e) => FilterViews();
            SelectAllViewsButton.Click += (s, e) => TickVisibleViews(true);
            ClearViewsButton.Click += (s, e) => TickVisibleViews(false);
            ResetButton.Click += (s, e) => OnReset();
            RestoreButton.Click += (s, e) => OnRestore();
            CloseButton.Click += (s, e) => Close();

            _loading = false;
            ShowViewsPanel();
            UpdateViewsCount();
            UpdateRestore();
            Recount();
        }

        private static bool SafeOverridesAllowed(View view)
        {
            try { return view.AreGraphicsOverridesAllowed(); }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------ choices and memory

        private ResetWhat What => CategoriesRadio.IsChecked == true ? ResetWhat.Categories
                                : BothRadio.IsChecked == true ? ResetWhat.Both
                                : ResetWhat.Elements;

        private bool PickedViews => PickedViewsRadio.IsChecked == true;

        private void RestoreChoices()
        {
            WindowMemory memory = WindowMemory.Load("reset-graphics");
            switch (memory.Get("what", "Elements"))
            {
                case "Categories": CategoriesRadio.IsChecked = true; break;
                case "Both": BothRadio.IsChecked = true; break;
                default: ElementsRadio.IsChecked = true; break;
            }
            KeepHeronCheck.IsChecked = memory.GetBool("keepHeron", true);

            var names = new HashSet<string>(memory.GetList("views"), StringComparer.OrdinalIgnoreCase);
            foreach (ViewChoice choice in _views)
                choice.IsChecked = names.Contains(choice.Name);   // a name this model does not have is skipped

            bool picked = memory.Get("where", "ThisView") == "Picked" && _views.Any(v => v.IsChecked);
            if (picked || _activeView == null || !SafeOverridesAllowed(_activeView))
                PickedViewsRadio.IsChecked = true;
            else
                ThisViewRadio.IsChecked = true;
        }

        private void SaveChoices()
        {
            WindowMemory memory = WindowMemory.Load("reset-graphics");
            memory.Set("what", What.ToString());
            memory.Set("where", PickedViews ? "Picked" : "ThisView");
            memory.Set("keepHeron", KeepHeronCheck.IsChecked == true);
            memory.Set("views", _views.Where(v => v.IsChecked).Select(v => v.Name));
            memory.SaveQuietly();
        }

        // ------------------------------------------------------------------ the view list

        private void ShowViewsPanel()
        {
            ViewsPanel.Visibility = PickedViews ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        private void FilterViews()
        {
            string text = (ViewSearchBox.Text ?? "").Trim();
            ViewsList.ItemsSource = text.Length == 0
                ? (IEnumerable<ViewChoice>)_views
                : _views.Where(v => v.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    v.Kind.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private void TickVisibleViews(bool tick)
        {
            _loading = true;
            foreach (ViewChoice choice in ViewsList.ItemsSource.Cast<ViewChoice>())
                choice.IsChecked = tick;
            _loading = false;
            UpdateViewsCount();
            Recount();
        }

        private void UpdateViewsCount()
        {
            int ticked = _views.Count(v => v.IsChecked);
            ViewsCountText.Text = $"{ticked} of {_views.Count} views picked";
        }

        private List<View> TargetViews()
        {
            if (!PickedViews)
                return _activeView != null && SafeOverridesAllowed(_activeView) ? new List<View> { _activeView } : new List<View>();
            return _views.Where(v => v.IsChecked).Select(v => v.View).Where(v => v.IsValidObject).ToList();
        }

        // ------------------------------------------------------------------ counting

        private List<ViewResetPlan> Plan(List<View> views)
        {
            var plans = new List<ViewResetPlan>();
            foreach (View view in views)
            {
                ISet<long> keep = KeepHeronCheck.IsChecked == true ? HeronColours.In(_doc, view) : null;
                plans.Add(ResetGraphicsService.Plan(_doc, view, What, null, keep));
            }
            return plans;
        }

        /// <summary>The line above the footer: what a press of Reset would clear.</summary>
        private void Recount()
        {
            List<View> views = TargetViews();
            if (views.Count == 0)
            {
                FoundText.Text = PickedViews ? "Tick at least one view." : "This view does not take graphic overrides - pick views instead.";
                ResetButton.IsEnabled = false;
                return;
            }
            if (views.Count > CountViewsUpTo)
            {
                FoundText.Text = $"{views.Count} views picked - Reset counts as it goes.";
                ResetButton.IsEnabled = true;
                return;
            }

            try
            {
                List<ViewResetPlan> plans = Plan(views);
                int elements = plans.Sum(p => p.Elements.Count);
                int categories = plans.Sum(p => p.Categories.Count);
                int kept = plans.Sum(p => p.KeptHeronColours);
                int templated = plans.Count(p => p.CategoryNote != null);

                var parts = new List<string>();
                if (What != ResetWhat.Categories)
                    parts.Add($"{elements} element(s) with overrides");
                if (What != ResetWhat.Elements)
                    parts.Add($"{categories} categor{(categories == 1 ? "y" : "ies")} overridden");
                string text = "Found: " + string.Join(", ", parts) + (views.Count > 1 ? $" in {views.Count} views." : ".");
                if (kept > 0)
                    text += $" {kept} element(s) in Heron's colours are left alone.";
                if (templated > 0)
                    text += $" {templated} view(s) keep their category overrides - a view template controls them.";
                FoundText.Text = text;
                ResetButton.IsEnabled = elements + categories > 0;
            }
            catch (Exception ex)
            {
                FoundText.Text = "Could not count: " + ex.Message;
                ResetButton.IsEnabled = true;
            }
        }

        // ------------------------------------------------------------------ buttons

        private void OnReset()
        {
            List<View> views = TargetViews();
            if (views.Count == 0)
            {
                ShowStatus("Tick at least one view.", problem: true);
                return;
            }

            SaveChoices();
            try
            {
                ResetSummary summary = ResetGraphicsService.Apply(_doc, Plan(views), "Reset Graphics");
                string text = ResetGraphicsService.Describe(summary);
                if (summary.Notes.Count > 0)
                    text += " " + string.Join(" ", summary.Notes.Take(3)) + (summary.Notes.Count > 3 ? $" (+{summary.Notes.Count - 3} more)" : "");
                ShowStatus(text, problem: summary.NotCommitted || summary.Refused > 0);
                if (summary.Elements + summary.Categories > 0)
                    ChangedSomething = true;
            }
            catch (Exception ex)
            {
                ShowStatus("Nothing was reset. Revit said: " + ex.Message, problem: true);
            }
            UpdateRestore();
            Recount();
        }

        private void OnRestore()
        {
            try
            {
                (int elements, int categories, bool committed) = ResetGraphicsMemory.Restore(_doc);
                if (!committed)
                    ShowStatus("Revit did not accept the change, so nothing was restored.", problem: true);
                else if (elements + categories == 0)
                    ShowStatus("Nothing could be restored: the views or elements of the last reset are gone.", problem: true);
                else
                {
                    ShowStatus($"Restored {elements} element(s) and {categories} categor{(categories == 1 ? "y" : "ies")} as they were before the last reset.", problem: false);
                    ChangedSomething = true;
                }
            }
            catch (Exception ex)
            {
                ShowStatus("Nothing was restored. Revit said: " + ex.Message, problem: true);
            }
            Recount();
        }

        private void UpdateRestore()
        {
            RestoreButton.IsEnabled = ResetGraphicsMemory.HasCopy(_doc);
            RestoreButton.ToolTip = RestoreButton.IsEnabled
                ? "Put back everything the last reset in this model cleared (one Undo step)."
                : "Nothing to restore yet - no reset in this model since Revit was opened.";
        }

        private void ShowStatus(string text, bool problem)
        {
            StatusText.Text = text ?? "";
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, problem ? "AccentWarning" : "TextSecondary");
        }

        // ------------------------------------------------------------------ one row of the view list

        private sealed class ViewChoice : INotifyPropertyChanged
        {
            private bool _isChecked;

            public ViewChoice(View view)
            {
                View = view;
                Name = view.Name;
                Kind = KindOf(view.ViewType);
            }

            public View View { get; }
            public string Name { get; }
            public string Kind { get; }

            public bool IsChecked
            {
                get => _isChecked;
                set
                {
                    if (_isChecked == value)
                        return;
                    _isChecked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            private static string KindOf(ViewType type)
            {
                switch (type)
                {
                    case ViewType.FloorPlan: return "Floor plan";
                    case ViewType.CeilingPlan: return "Ceiling plan";
                    case ViewType.EngineeringPlan: return "Structural plan";
                    case ViewType.AreaPlan: return "Area plan";
                    case ViewType.Section: return "Section";
                    case ViewType.Elevation: return "Elevation";
                    case ViewType.ThreeD: return "3D view";
                    case ViewType.DraftingView: return "Drafting";
                    case ViewType.Detail: return "Detail";
                    case ViewType.Legend: return "Legend";
                    case ViewType.DrawingSheet: return "Sheet";
                    default: return type.ToString();
                }
            }
        }
    }
}
