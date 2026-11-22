// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.3.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Code-behind for the Auto Dimension settings window: loads, checks and returns the settings. No Revit API here.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HeronTools.Models.AutoDimension;
using HeronTools.Utils;

namespace HeronTools.UI.AutoDimension
{
    public partial class AutoDimensionSettingsWindow : Window
    {
        private const string ProjectDefault = "(project default)";

        private static readonly KeyValuePair<DatumSourceScope, string>[] Scopes =
        {
            new KeyValuePair<DatumSourceScope, string>(DatumSourceScope.None, "Do not dimension"),
            new KeyValuePair<DatumSourceScope, string>(DatumSourceScope.ThisModel, "This model"),
            new KeyValuePair<DatumSourceScope, string>(DatumSourceScope.LinkedModels, "Linked models"),
            new KeyValuePair<DatumSourceScope, string>(DatumSourceScope.ThisModelAndLinks, "This model and linked models"),
        };

        private readonly IList<string> _typeNames;
        private bool _loading;

        internal AutoDimensionSettingsWindow(AutoDimensionSettings settings, IList<string> dimensionTypeNames)
        {
            InitializeComponent();

            // Shared Heron Tools window entrance and exit (fade + short rise / sink). Cosmetic only.
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);
            Icon = BrandImages.Load(BrandImages.AppIcon);

            _typeNames = dimensionTypeNames ?? new List<string>();
            foreach (KeyValuePair<DatumSourceScope, string> scope in Scopes)
            {
                GridsFromCombo.Items.Add(scope.Value);
                LevelsFromCombo.Items.Add(scope.Value);
            }

            Fill(settings ?? new AutoDimensionSettings());
        }

        /// <summary>The settings to save; set only when Save was pressed.</summary>
        internal AutoDimensionSettings Result { get; private set; }

        private void Fill(AutoDimensionSettings source)
        {
            _loading = true;
            AutoDimensionSettings s = source.Clone();
            s.Normalize();

            ChainCheck.IsChecked = s.ChainRow;
            OverallCheck.IsChecked = s.OverallRow;
            SkipOverallForTwoCheck.IsChecked = s.SkipOverallForTwo;
            BubbleEndRadio.IsChecked = s.Side == AutoDimensionSide.BubbleEnd;
            OtherEndRadio.IsChecked = s.Side == AutoDimensionSide.OtherEnd;
            BothEndsRadio.IsChecked = s.Side == AutoDimensionSide.BothEnds;
            FirstGapBox.Text = s.FirstGapMm.ToString("0.###", CultureInfo.CurrentCulture);
            RowGapBox.Text = s.RowGapMm.ToString("0.###", CultureInfo.CurrentCulture);
            FillTypes(ChainTypeCombo, s.ChainTypeName);
            FillTypes(OverallTypeCombo, s.OverallTypeName);
            GridsFromCombo.SelectedIndex = ScopeIndex(s.GridsFrom);
            LevelsFromCombo.SelectedIndex = ScopeIndex(s.LevelsFrom);
            StoryOnlyCheck.IsChecked = s.StoryLevelsOnly;
            LeaveOutBox.Text = s.LeaveOutNames;
            SkipExistingCheck.IsChecked = s.SkipExistingRows;
            ShowLeftOutCheck.IsChecked = s.ShowLeftOut;
            _loading = false;
            Refresh();
        }

        private void FillTypes(ComboBox combo, string selected)
        {
            combo.Items.Clear();
            combo.Items.Add(ProjectDefault);
            foreach (string name in _typeNames)
                combo.Items.Add(name);

            if (string.IsNullOrWhiteSpace(selected))
            {
                combo.SelectedIndex = 0;
                return;
            }

            string match = _typeNames.FirstOrDefault(n => string.Equals(n, selected, StringComparison.CurrentCultureIgnoreCase));
            if (match == null)
            {
                // Saved from another project: keep the choice rather than wiping it for every project. At run time a
                // project without it uses its default type, and the report says so.
                match = selected;
                combo.Items.Add(match);
            }

            combo.SelectedItem = match;
        }

        private static int ScopeIndex(DatumSourceScope scope)
        {
            for (int i = 0; i < Scopes.Length; i++)
                if (Scopes[i].Key == scope)
                    return i;
            return 1;
        }

        private static DatumSourceScope ScopeAt(ComboBox combo)
        {
            int i = combo.SelectedIndex;
            return i >= 0 && i < Scopes.Length ? Scopes[i].Key : DatumSourceScope.ThisModel;
        }

        private static string TypeAt(ComboBox combo)
        {
            string name = combo.SelectedItem as string;
            return string.IsNullOrWhiteSpace(name) || name == ProjectDefault ? string.Empty : name;
        }

        private AutoDimensionSide Side =>
            BothEndsRadio.IsChecked == true ? AutoDimensionSide.BothEnds
            : OtherEndRadio.IsChecked == true ? AutoDimensionSide.OtherEnd
            : AutoDimensionSide.BubbleEnd;

        // ------------------------------------------------------------------ checks and the live preview

        private void Refresh()
        {
            if (_loading || SaveButton == null)
                return;

            bool chain = ChainCheck.IsChecked == true;
            bool overall = OverallCheck.IsChecked == true;
            SkipOverallForTwoCheck.IsEnabled = chain && overall;

            AutoDimensionSide side = Side;
            bool top = side != AutoDimensionSide.OtherEnd;
            bool bottom = side != AutoDimensionSide.BubbleEnd;
            TopOverall.Visibility = top && overall ? Visibility.Visible : Visibility.Collapsed;
            TopChain.Visibility = top && chain ? Visibility.Visible : Visibility.Collapsed;
            BottomOverall.Visibility = bottom && overall ? Visibility.Visible : Visibility.Collapsed;
            BottomChain.Visibility = bottom && chain ? Visibility.Visible : Visibility.Collapsed;
            // With one row only, it sits at the first gap from the heads.
            TopOverall.Margin = top && overall && !chain ? new Thickness(0, -14, 0, 0) : new Thickness(0);
            BottomOverall.Margin = bottom && overall && !chain ? new Thickness(0, 14, 0, 0) : new Thickness(0);

            DatumSourceScope grids = ScopeAt(GridsFromCombo);
            DatumSourceScope levels = ScopeAt(LevelsFromCombo);
            bool links = grids == DatumSourceScope.LinkedModels || grids == DatumSourceScope.ThisModelAndLinks ||
                         levels == DatumSourceScope.LinkedModels || levels == DatumSourceScope.ThisModelAndLinks;
            LinkNote.Visibility = links ? Visibility.Visible : Visibility.Collapsed;

            string problem = Problem(chain, overall, grids, levels);
            ErrorText.Text = problem ?? string.Empty;
            ErrorText.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
            SaveButton.IsEnabled = problem == null;
        }

        private string Problem(bool chain, bool overall, DatumSourceScope grids, DatumSourceScope levels)
        {
            if (!chain && !overall)
                return "Tick at least one row: the chain, the overall, or both.";
            if (grids == DatumSourceScope.None && levels == DatumSourceScope.None)
                return "Grids and levels are both set to \"Do not dimension\" - choose where at least one comes from.";
            if (!TryGap(FirstGapBox.Text, out double first))
                return "Heads to first row: type a number from 0 to " + AutoDimensionSettings.MaxGapMm + " mm.";
            if (!TryGap(RowGapBox.Text, out double between))
                return "Between rows: type a number from 0 to " + AutoDimensionSettings.MaxGapMm + " mm.";
            if (chain && overall && between <= 0)
                return "Between rows must be more than 0 mm when both rows are ticked - at 0 they lie on top of each other.";
            return first >= 0 ? null : "Heads to first row cannot be below 0 mm.";
        }

        private static bool TryGap(string text, out double value)
        {
            return double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                   && !double.IsNaN(value) && !double.IsInfinity(value)
                   && value >= 0 && value <= AutoDimensionSettings.MaxGapMm;
        }

        // ------------------------------------------------------------------ events

        private void OnInputChanged(object sender, RoutedEventArgs e) => Refresh();

        private void OnTextChanged(object sender, TextChangedEventArgs e) => Refresh();

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh();

        private void OnReset(object sender, RoutedEventArgs e) => Fill(new AutoDimensionSettings());

        private void OnSave(object sender, RoutedEventArgs e)
        {
            Refresh();
            if (!SaveButton.IsEnabled)
                return;

            TryGap(FirstGapBox.Text, out double first);
            TryGap(RowGapBox.Text, out double between);
            var settings = new AutoDimensionSettings
            {
                ChainRow = ChainCheck.IsChecked == true,
                OverallRow = OverallCheck.IsChecked == true,
                SkipOverallForTwo = SkipOverallForTwoCheck.IsChecked == true,
                Side = Side,
                FirstGapMm = first,
                RowGapMm = between,
                ChainTypeName = TypeAt(ChainTypeCombo),
                OverallTypeName = TypeAt(OverallTypeCombo),
                GridsFrom = ScopeAt(GridsFromCombo),
                LevelsFrom = ScopeAt(LevelsFromCombo),
                StoryLevelsOnly = StoryOnlyCheck.IsChecked == true,
                LeaveOutNames = (LeaveOutBox.Text ?? string.Empty).Trim(),
                SkipExistingRows = SkipExistingCheck.IsChecked == true,
                ShowLeftOut = ShowLeftOutCheck.IsChecked == true
            };
            settings.Normalize();
            Result = settings;
            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
