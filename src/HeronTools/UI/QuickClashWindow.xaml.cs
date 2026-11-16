// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Quick Clash window - MODELESS. It never calls the Revit API itself: every button hands its work to
// QuickClashSession.Post (an ExternalEvent), and the session calls back the Show/Set methods below on Revit's
// thread, which is also this window's thread.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using HeronTools.Models.QuickClash;
using HeronTools.Services.QuickClash;
using HeronTools.Utils;

namespace HeronTools.UI
{
    public partial class QuickClashWindow : Window
    {
        private readonly QuickClashSession _session;
        private readonly ObservableCollection<ClashCategoryItem> _rowsA = new ObservableCollection<ClashCategoryItem>();
        private readonly ObservableCollection<ClashCategoryItem> _rowsB = new ObservableCollection<ClashCategoryItem>();
        private readonly ObservableCollection<ClashResult> _results = new ObservableCollection<ClashResult>();
        private readonly ICollectionView _viewA;
        private readonly ICollectionView _viewB;

        private ClashColour _colourA, _colourB, _colourRest;
        private readonly HashSet<long> _tickedA;
        private readonly HashSet<long> _tickedB;
        private string _sourceRemember;

        private bool _ready;            // false while the window fills itself (events then change nothing)
        private bool _busy;
        private bool _coloured;         // the view shows Quick Clash colours (Reset view is offered)
        private bool _isolated;         // Quick Clash's Isolate is on (Show all is offered)
        private List<ClashResult> _zoomPick;   // the latest clashes clicked, waiting for Revit to zoom to them
        private bool _is3DView;
        private bool _closingForReal;
        private int _selectionCount;

        internal QuickClashWindow(QuickClashSession session, QuickClashSettings settings)
        {
            InitializeComponent();
            WindowMotionHelper.AttachStandardEntrance(this);
            _session = session;

            _tickedA = new HashSet<long>(settings.CategoriesA);
            _tickedB = new HashSet<long>(settings.CategoriesB);
            _sourceRemember = settings.SourceRememberName;

            _viewA = CollectionViewSource.GetDefaultView(_rowsA);
            _viewB = CollectionViewSource.GetDefaultView(_rowsB);
            _viewA.Filter = o => Matches(o, SearchA.Text);
            _viewB.Filter = o => Matches(o, SearchB.Text);
            ListA.ItemsSource = _viewA;
            ListB.ItemsSource = _viewB;
            ResultsList.ItemsSource = _results;

            SelectionARadio.IsChecked = settings.UseSelectionA;
            CategoriesARadio.IsChecked = !settings.UseSelectionA;
            HardCheck.IsChecked = settings.HardOn;
            ClearanceCheck.IsChecked = settings.ClearanceOn;
            ToleranceBox.Text = settings.ToleranceMm.ToString("0.##", CultureInfo.InvariantCulture);
            ClearanceBox.Text = settings.ClearanceMm.ToString("0.##", CultureInfo.InvariantCulture);
            TransparencyBox.Text = settings.RestTransparency.ToString(CultureInfo.InvariantCulture);
            SetColour(0, settings.ColourA);
            SetColour(1, settings.ColourB);
            SetColour(2, settings.ColourRest);

            RestorePlace();
            _ready = true;
            OnSideAModeChanged(this, null);   // greys the A list when "Current selection" was remembered
            UpdatePicked();
        }

        // ------------------------------------------------------------------ called by the session (Revit thread)

        internal void ShowLists(string viewName, List<ClashCategoryItem> rowsA, List<ClashSourceItem> sources,
                                ClashSourceItem chosen, List<ClashCategoryItem> rowsB, int selectionCount, bool exact)
        {
            _ready = false;
            _is3DView = true;
            ViewNameText.Text = viewName;

            Fill(_rowsA, rowsA);
            Fill(_rowsB, rowsB);

            SourceCombo.ItemsSource = sources;
            SourceCombo.SelectedItem = chosen;
            _sourceRemember = chosen.RememberName;

            _selectionCount = selectionCount;
            SelectionAText.Text = "Current selection (" + selectionCount.ToString("N0", CultureInfo.InvariantCulture) + ")";
            SourceCombo.ToolTip = exact
                ? "Test against this model, or against one loaded link placed in this view."
                : "Revit 2020-2022: link elements inside the section box are tested, even ones hidden in the link by V/G.";

            _ready = true;
            UpdateCounts();
            UpdateRunState();
        }

        internal void ShowNotA3DView(string why)
        {
            _is3DView = false;
            ViewNameText.Text = "not a 3D view";
            StatusText.Text = why;
            UpdateRunState();
        }

        internal void ShowResults(List<ClashResult> clashes, string status)
        {
            _ready = false;
            _results.Clear();
            foreach (ClashResult c in clashes)
                _results.Add(c);
            _ready = true;

            int hard = clashes.Count(c => c.Kind == ClashKind.Hard);
            int clear = clashes.Count(c => c.Kind == ClashKind.Clearance);
            ResultsTab.Header = clashes.Count > 0 ? "Results (" + clashes.Count + ")" : "Results";
            ResultsSummaryText.Text = clashes.Count == 0
                ? "No clashes"
                : (hard + clear) + " clashes · " + hard + " hard · " + clear + " clearance";
            StatusText.Text = status;
            if (hard + clear > 0)
                Tabs.SelectedItem = ResultsTab;
            UpdatePicked();
        }

        internal void ClearResults()
        {
            _ready = false;
            _results.Clear();
            _ready = true;
            ResultsTab.Header = "Results";
            ResultsSummaryText.Text = "No test run yet";
            StatusText.Text = "The view is back as it was.";
            UpdatePicked();
        }

        internal void SetStatus(string text) => StatusText.Text = text ?? "";

        internal void SetColoured(bool coloured)
        {
            _coloured = coloured;
            ResetButton.IsEnabled = coloured && !_busy;
        }

        internal void SetIsolated(bool isolated)
        {
            _isolated = isolated;
            UpdateResultButtons();
        }

        internal void SetBusy(bool busy)
        {
            _busy = busy;
            Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
            CloseButton.IsEnabled = !busy;
            ResetButton.IsEnabled = !busy && _coloured;
            UpdateRunState();
            UpdateResultButtons();
        }

        internal void SetColour(int which, ClashColour colour)
        {
            var brush = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
            brush.Freeze();
            if (which == 0) { _colourA = colour; SwatchA.Background = brush; }
            else if (which == 1) { _colourB = colour; SwatchB.Background = brush; }
            else { _colourRest = colour; SwatchRest.Background = brush; }
        }

        internal void BringForward()
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }

        internal void CloseForReal()
        {
            _closingForReal = true;
            SavePlace();
            Close();
        }

        /// <summary>The test as set in the window right now (ticks, numbers, colours).</summary>
        internal QuickClashSettings CurrentSettings()
        {
            var source = SourceCombo.SelectedItem as ClashSourceItem;
            TryNumber(ToleranceBox.Text, 0, QuickClashSettings.MaxToleranceMm, out double tolerance);
            TryNumber(ClearanceBox.Text, QuickClashSettings.MinClearanceMm, QuickClashSettings.MaxClearanceMm, out double clearance);
            TryNumber(TransparencyBox.Text, 0, 100, out double transparency);
            return new QuickClashSettings
            {
                CategoriesA = _tickedA.ToList(),
                CategoriesB = _tickedB.ToList(),
                UseSelectionA = SelectionARadio.IsChecked == true,
                SourceLinkInstanceId = source?.LinkInstanceId ?? -1,
                SourceRememberName = source?.RememberName ?? _sourceRemember ?? "",
                HardOn = HardCheck.IsChecked == true,
                ToleranceMm = tolerance,
                ClearanceOn = ClearanceCheck.IsChecked == true,
                ClearanceMm = clearance,
                ColourA = _colourA,
                ColourB = _colourB,
                ColourRest = _colourRest,
                RestTransparency = (int)Math.Round(transparency)
            };
        }

        // ------------------------------------------------------------------ the modeller's clicks

        private void OnRun(object sender, RoutedEventArgs e)
        {
            if (!Validate(out string problem))
            {
                StatusText.Text = problem;
                return;
            }
            QuickClashSettings settings = CurrentSettings();
            StatusText.Text = "Testing... (Esc stops it)";
            _session.Post(app => _session.Run(app, settings));
        }

        private void OnReset(object sender, RoutedEventArgs e) => _session.Post(app => _session.Reset(app));

        private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_closingForReal)
            {
                // The view must be put back first - that needs Revit's thread. The session closes the window after.
                e.Cancel = true;
                if (!_busy)
                    _session.RequestClose();
                return;
            }
            base.OnClosing(e);
        }

        private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || !(SourceCombo.SelectedItem is ClashSourceItem source)) return;
            _sourceRemember = source.RememberName;
            QuickClashSettings settings = CurrentSettings();
            _session.Post(app => _session.Refresh(app, settings));
        }

        private void OnCategoryTicked(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            foreach (ClashCategoryItem row in _rowsA) Track(_tickedA, row);
            foreach (ClashCategoryItem row in _rowsB) Track(_tickedB, row);
            UpdateCounts();
            UpdateRunState();
        }

        private void OnSideAModeChanged(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            bool selection = SelectionARadio.IsChecked == true;
            ListA.IsEnabled = !selection;
            SearchA.IsEnabled = !selection;
            UpdateCounts();
            UpdateRunState();
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            if (sender == SearchA) _viewA.Refresh(); else _viewB.Refresh();
        }

        private void OnClashTypeClicked(object sender, RoutedEventArgs e) => UpdateRunState();

        private void OnNumberChanged(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            if (!Validate(out string problem)) StatusText.Text = problem;
            UpdateRunState();
        }

        private void OnSwatchClicked(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !int.TryParse(button.Tag as string, out int which)) return;
            ClashColour current = which == 0 ? _colourA : which == 1 ? _colourB : _colourRest;
            _session.Post(app => _session.PickColour(app, which, current));
        }

        private void OnDefaultColours(object sender, RoutedEventArgs e)
        {
            SetColour(0, QuickClashSettings.DefaultColourA);
            SetColour(1, QuickClashSettings.DefaultColourB);
            SetColour(2, QuickClashSettings.DefaultColourRest);
            TransparencyBox.Text = QuickClashSettings.DefaultRestTransparency.ToString(CultureInfo.InvariantCulture);
        }

        private void OnResultSelected(object sender, SelectionChangedEventArgs e)
        {
            UpdatePicked();
            if (!_ready) return;
            List<ClashResult> picked = ResultsList.SelectedItems.OfType<ClashResult>().ToList();
            if (picked.Count == 0) return;

            // Arrow keys or quick clicks change the pick many times: only the latest pick is zoomed to.
            bool alreadyAsked = _zoomPick != null;
            _zoomPick = picked;
            if (alreadyAsked) return;
            _session.Post(app =>
            {
                List<ClashResult> latest = _zoomPick;
                _zoomPick = null;
                if (latest != null) _session.Zoom(app, latest);
            });
        }

        private void OnIsolate(object sender, RoutedEventArgs e)
        {
            List<ClashResult> clashes = PickedOrAll();
            if (clashes.Count > 0) _session.Post(app => _session.Isolate(app, clashes));
        }

        private void OnShowAll(object sender, RoutedEventArgs e) => _session.Post(app => _session.ShowAll(app));

        private void OnSelectInRevit(object sender, RoutedEventArgs e)
        {
            List<ClashResult> clashes = PickedOrAll();
            if (clashes.Count > 0) _session.Post(app => _session.SelectInRevit(app, clashes));
        }

        private void OnPickNone(object sender, RoutedEventArgs e)
        {
            _ready = false;              // un-picking is not a click on a clash: no zoom
            ResultsList.UnselectAll();
            _ready = true;
            UpdatePicked();
        }

        /// <summary>The clashes picked in the list, or every clash when none is picked (Navisworks-style).</summary>
        private List<ClashResult> PickedOrAll()
        {
            List<ClashResult> picked = ResultsList.SelectedItems.OfType<ClashResult>().OrderBy(c => c.Number).ToList();
            return picked.Count > 0 ? picked : _results.ToList();
        }

        private void UpdatePicked()
        {
            int picked = ResultsList.SelectedItems.Count;
            PickNoneButton.Visibility = picked > 0 ? Visibility.Visible : Visibility.Collapsed;
            PickedText.Text = _results.Count == 0
                ? "Run a test to see clashes here."
                : picked == 0
                    ? "No clash picked - the buttons use every clash."
                    : (picked == 1 ? "1 clash picked." : picked + " clashes picked.");
            UpdateResultButtons();
        }

        private void UpdateResultButtons()
        {
            bool any = _results.Count > 0 && !_busy;
            IsolateButton.IsEnabled = any;
            SelectButton.IsEnabled = any;
            ShowAllButton.IsEnabled = _isolated && !_busy;
        }

        // ------------------------------------------------------------------ helpers

        private void UpdateRunState()
        {
            bool sideA = SelectionARadio.IsChecked == true || _rowsA.Any(r => r.IsChecked);
            bool sideB = _rowsB.Any(r => r.IsChecked);
            bool type = HardCheck.IsChecked == true || ClearanceCheck.IsChecked == true;
            RunButton.IsEnabled = !_busy && _is3DView && sideA && sideB && type && Validate(out _);
        }

        private void UpdateCounts()
        {
            List<ClashCategoryItem> a = _rowsA.Where(r => r.IsChecked).ToList();
            List<ClashCategoryItem> b = _rowsB.Where(r => r.IsChecked).ToList();
            CountAText.Text = SelectionARadio.IsChecked == true
                ? "The elements selected in Revit when you press Run"
                : Count(a);
            CountBText.Text = Count(b);
            if (_is3DView && !_busy && _results.Count == 0)
            {
                int left = SelectionARadio.IsChecked == true ? _selectionCount : a.Sum(r => r.Count);
                int right = b.Sum(r => r.Count);
                StatusText.Text = left > 0 && right > 0
                    ? "Checks " + left.ToString("N0", CultureInfo.InvariantCulture) + " elements against " +
                      right.ToString("N0", CultureInfo.InvariantCulture) + ". Only what is visible in this view."
                    : "Tick at least one category on each side.";
            }
        }

        private static string Count(List<ClashCategoryItem> rows)
        {
            int elements = rows.Sum(r => r.Count);
            return rows.Count + (rows.Count == 1 ? " category · " : " categories · ") +
                   elements.ToString("N0", CultureInfo.InvariantCulture) + " elements";
        }

        private bool Validate(out string problem)
        {
            problem = null;
            if (HardCheck.IsChecked == true && !TryNumber(ToleranceBox.Text, 0, QuickClashSettings.MaxToleranceMm, out _))
                problem = "Tolerance must be a number of mm from 0 to " + QuickClashSettings.MaxToleranceMm + ".";
            else if (ClearanceCheck.IsChecked == true && !TryNumber(ClearanceBox.Text, QuickClashSettings.MinClearanceMm, QuickClashSettings.MaxClearanceMm, out _))
                problem = "Clearance distance must be a number of mm from " + QuickClashSettings.MinClearanceMm + " to " + QuickClashSettings.MaxClearanceMm + ".";
            else if (!TryNumber(TransparencyBox.Text, 0, 100, out _))
                problem = "See-through must be a percentage from 0 to 100.";
            return problem == null;
        }

        private static bool TryNumber(string text, double min, double max, out double value)
        {
            bool ok = double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                      && value >= min && value <= max;
            if (!ok) value = Math.Max(min, Math.Min(max, value));
            return ok;
        }

        private static void Fill(ObservableCollection<ClashCategoryItem> into, List<ClashCategoryItem> rows)
        {
            into.Clear();
            foreach (ClashCategoryItem row in rows)
                into.Add(row);
        }

        private static void Track(HashSet<long> ticked, ClashCategoryItem row)
        {
            if (row.IsChecked) ticked.Add(row.CategoryId); else ticked.Remove(row.CategoryId);
        }

        private static bool Matches(object item, string search)
        {
            return string.IsNullOrWhiteSpace(search) ||
                   (item is ClashCategoryItem row && row.Name.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // Where the window was last time (ScreenFit moves it back on screen if the screens changed).
        private void RestorePlace()
        {
            WindowMemory memory = WindowMemory.Load("quick-clash-window");
            double left = memory.GetDouble("Left", double.NaN), top = memory.GetDouble("Top", double.NaN);
            double height = memory.GetDouble("Height", double.NaN);
            if (double.IsNaN(left) || double.IsNaN(top)) return;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
            if (!double.IsNaN(height) && height >= MinHeight) Height = height;
        }

        private void SavePlace()
        {
            if (WindowState != WindowState.Normal) return;
            WindowMemory memory = WindowMemory.Load("quick-clash-window");
            memory.Set("Left", Left);
            memory.Set("Top", Top);
            memory.Set("Height", ActualHeight);
            memory.Save(out _);
        }
    }
}
