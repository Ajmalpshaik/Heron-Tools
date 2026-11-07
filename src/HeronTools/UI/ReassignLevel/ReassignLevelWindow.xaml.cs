// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Reassign Level window: choose Whole project (From and To level) or Only my selection (To level only, using
// whatever was already selected in Revit). The window only collects the choice; the command does the change.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using HeronTools.Utils;

namespace HeronTools.UI.ReassignLevel
{
    /// <summary>
    /// Scope for the level reassignment - the whole project (FROM level -> TO level) or just the
    /// elements already selected in Revit (TO level only; each element's own current level is used
    /// as its FROM).
    /// </summary>
    public enum ReassignScope
    {
        WholeProject,
        SelectedElements
    }

    /// <summary>
    /// Interaction logic for ReassignLevelWindow.xaml
    /// </summary>
    public partial class ReassignLevelWindow : Window
    {
        private const double MetersPerFoot = 0.3048;

        private readonly int _eligibleSelectedCount;
        private bool _loaded;

        /// <summary>
        /// Whole Project or Selected Elements. Only meaningful when DialogResult is true.
        /// </summary>
        public ReassignScope Scope { get; private set; }

        /// <summary>
        /// Level the elements currently reference. Only set (and only meaningful) for the Whole
        /// Project scope; null for Selected Elements, where each element's own level is used instead.
        /// </summary>
        public Level FromLevel { get; private set; }

        /// <summary>
        /// Level the elements will reference instead. Only meaningful when DialogResult is true.
        /// </summary>
        public Level ToLevel { get; private set; }

        /// <summary>
        /// Creates the scope/level picker.
        /// </summary>
        /// <param name="levels">Project levels, already ordered by elevation.</param>
        /// <param name="totalSelectedCount">How many elements are currently selected in Revit.</param>
        /// <param name="eligibleSelectedCount">How many of those can actually be reassigned.</param>
        public ReassignLevelWindow(IList<Level> levels, int totalSelectedCount, int eligibleSelectedCount)
        {
            InitializeComponent();
            Icon = BrandImages.Load(BrandImages.AppIcon);
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);

            _eligibleSelectedCount = eligibleSelectedCount;

            List<LevelChoice> items = (levels ?? new List<Level>())
                .Where(level => level != null)
                .Select(level => new LevelChoice(level))
                .ToList();

            // Two separate lists: a shared ItemsSource would make both combos move together.
            FromCombo.ItemsSource = items;
            ToCombo.ItemsSource = items.ToList();
            FromCombo.DisplayMemberPath = nameof(LevelChoice.Label);
            ToCombo.DisplayMemberPath = nameof(LevelChoice.Label);

            if (items.Count > 0)
                FromCombo.SelectedIndex = 0;

            ToCombo.SelectedIndex = items.Count > 1 ? 1 : 0;

            if (eligibleSelectedCount > 0)
            {
                SelectionSummaryText.Text = totalSelectedCount == eligibleSelectedCount
                    ? string.Format(CultureInfo.CurrentCulture, "{0} selected element(s) will move to the To level.", eligibleSelectedCount)
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        "{0} of your {1} selected element(s) can move. The rest are hosted or not supported, and are skipped.",
                        eligibleSelectedCount,
                        totalSelectedCount);
            }
            else
            {
                SelectedElementsRadio.IsEnabled = false;
                SelectedElementsRadio.ToolTip = totalSelectedCount == 0
                    ? "Select elements in the model first, then run this tool again."
                    : "None of the selected elements can move (only ducts, pipes and other MEP runs, free-standing families and spaces can).";
                SelectionSummaryText.Text = "Nothing selected that can move.";
            }

            // Wired here, not via a XAML "Checked=" attribute: touching FromPanel/NoteBodyText from a
            // handler fired during InitializeComponent() itself (which the XAML-attribute form can do)
            // would NullReferenceException before those x:Name fields exist.
            WholeProjectRadio.Checked += OnScopeChanged;
            SelectedElementsRadio.Checked += OnScopeChanged;

            if (eligibleSelectedCount > 0)
            {
                SelectedElementsRadio.IsChecked = true;
            }

            UpdateScopeVisibility();

            _loaded = true;
            Validate(out _, out _);
        }

        private void OnScopeChanged(object sender, RoutedEventArgs e)
        {
            UpdateScopeVisibility();

            if (_loaded)
                Validate(out _, out _);
        }

        private void UpdateScopeVisibility()
        {
            bool wholeProject = WholeProjectRadio.IsChecked == true;

            FromPanel.Visibility = wholeProject ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            SwapButton.Visibility = wholeProject ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            SelectionInfoPanel.Visibility = wholeProject ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

            NoteBodyText.Text = wholeProject
                ? "This works on the whole project, not just this view. Hosted families are skipped - their level follows the host. Next you see how many elements will change, and can still cancel."
                : "This works on your selection only. Hosted families and element types that are not supported are skipped and listed. Next you can still cancel.";
        }

        private void OnLevelChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_loaded)
                return;

            Validate(out _, out _);
        }

        private void OnSwap(object sender, RoutedEventArgs e)
        {
            int from = FromCombo.SelectedIndex;
            FromCombo.SelectedIndex = ToCombo.SelectedIndex;
            ToCombo.SelectedIndex = from;
        }

        private void OnRun(object sender, RoutedEventArgs e)
        {
            if (!Validate(out Level from, out Level to))
                return;

            bool wholeProject = WholeProjectRadio.IsChecked == true;
            Scope = wholeProject ? ReassignScope.WholeProject : ReassignScope.SelectedElements;
            FromLevel = wholeProject ? from : null;
            ToLevel = to;
            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Checks the current scope/level combination, refreshes the inline message and enables or
        /// disables Run. Never shows a popup and never closes the window.
        /// </summary>
        private bool Validate(out Level from, out Level to)
        {
            from = (FromCombo.SelectedItem as LevelChoice)?.Level;
            to = (ToCombo.SelectedItem as LevelChoice)?.Level;

            bool wholeProject = WholeProjectRadio.IsChecked == true;

            if (wholeProject)
            {
                if (from == null || to == null)
                    return Reject("Choose both a From level and a To level.");

                if (from.Id == to.Id)
                    return Reject("From and To are the same level. Pick another To level.");
            }
            else
            {
                if (_eligibleSelectedCount == 0)
                    return Reject("Nothing selected that can move. Select elements in the model, then start the tool again.");

                if (to == null)
                    return Reject("Choose a To level.");
            }

            ErrorText.Text = string.Empty;
            RunButton.IsEnabled = true;
            return true;
        }

        private bool Reject(string message)
        {
            ErrorText.Text = message;
            RunButton.IsEnabled = false;
            return false;
        }

        private sealed class LevelChoice
        {
            public LevelChoice(Level level)
            {
                Level = level;
                Label = string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} ({1:0.000} m)",
                    level?.Name ?? "<Unnamed>",
                    level == null ? 0 : level.Elevation * MetersPerFoot);
            }

            public Level Level { get; }

            public string Label { get; }

            public override string ToString() => Label;
        }
    }
}
