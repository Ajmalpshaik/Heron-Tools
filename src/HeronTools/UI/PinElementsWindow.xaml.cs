// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Pin / Unpin window: the modeller ticks groups (title blocks, views on sheets, ducts, grids...) and pins or
// unpins them. The model change itself is PinElementsService.ApplyPinState (one named transaction).

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using HeronTools.Models.PinTools;
using HeronTools.Services.PinTools;
using HeronTools.Utils;

namespace HeronTools.UI
{
    /// <summary>
    /// Interaction logic for PinElementsWindow.xaml.
    /// </summary>
    public partial class PinElementsWindow : Window
    {
        private readonly Document _doc;
        private readonly View _activeView;
        private readonly bool _isSheetContext;
        private readonly ObservableCollection<PinCategoryItem> _sheetItems = new ObservableCollection<PinCategoryItem>();
        private readonly ObservableCollection<PinCategoryItem> _modelItems = new ObservableCollection<PinCategoryItem>();

        public PinElementsWindow(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _activeView = _doc.ActiveView;
            _isSheetContext = PinElementsService.IsSheetContext(_activeView);

            InitializeComponent();
            Icon = BrandImages.Load(BrandImages.AppIcon);
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);

            SheetCategoryListBox.ItemsSource = _sheetItems;
            ModelCategoryListBox.ItemsSource = _modelItems;

            if (ActiveSheetOnlyRadioButton != null)
                ActiveSheetOnlyRadioButton.Checked += OnSheetScopeChanged;

            if (AllSheetsRadioButton != null)
                AllSheetsRadioButton.Checked += OnSheetScopeChanged;

            LoadItems();
            UpdateUiState();
        }

        internal bool HasExecutedOperation { get; private set; }

        private IEnumerable<PinCategoryItem> ActiveItems => _isSheetContext ? _sheetItems : _modelItems;

        private bool IncludeAllSheets =>
            _isSheetContext
            && AllSheetsRadioButton != null
            && AllSheetsRadioButton.IsChecked == true;

        private string SheetScopeLabel => IncludeAllSheets ? "All sheets" : "This sheet only";

        private void LoadItems()
        {
            if (_isSheetContext)
            {
                ContextText.Text = "You are on a sheet - these are the sheet items.";
                ModelItemsGroupBox.Visibility = System.Windows.Visibility.Collapsed;
                SheetScopePanel.Visibility = System.Windows.Visibility.Visible;
                LoadSheetItems(preserveSelection: false);
                return;
            }

            ContextText.Text = "You are in a model view - these groups cover the whole project.";
            SheetItemsGroupBox.Visibility = System.Windows.Visibility.Collapsed;
            SheetScopePanel.Visibility = System.Windows.Visibility.Collapsed;
            LoadModelItems();
        }

        private void LoadSheetItems(bool preserveSelection)
        {
            var previousSelection = preserveSelection
                ? _sheetItems.ToDictionary(item => item.Group, item => item.IsChecked)
                : null;

            UnwireItemEvents(_sheetItems);
            _sheetItems.Clear();

            foreach (PinTargetDefinition definition in PinElementsService.GetSheetTargetDefinitions())
            {
                int candidateCount = PinElementsService.CountCandidates(
                    _doc,
                    _activeView,
                    definition.Group,
                    IncludeAllSheets);

                bool isChecked = definition.DefaultSelected;
                if (previousSelection != null && previousSelection.TryGetValue(definition.Group, out bool wasChecked))
                    isChecked = wasChecked;

                var item = new PinCategoryItem(
                    definition.Group,
                    definition.Name,
                    definition.Description,
                    candidateCount,
                    isChecked);

                item.PropertyChanged += OnItemPropertyChanged;
                _sheetItems.Add(item);
            }
        }

        private void LoadModelItems()
        {
            UnwireItemEvents(_modelItems);
            _modelItems.Clear();

            foreach (PinTargetDefinition definition in PinElementsService.GetModelTargetDefinitions())
            {
                int candidateCount = PinElementsService.CountCandidates(_doc, _activeView, definition.Group);
                var item = new PinCategoryItem(
                    definition.Group,
                    definition.Name,
                    definition.Description,
                    candidateCount,
                    definition.DefaultSelected);

                item.PropertyChanged += OnItemPropertyChanged;
                _modelItems.Add(item);
            }
        }

        private void UnwireItemEvents(IEnumerable<PinCategoryItem> items)
        {
            if (items == null)
                return;

            foreach (PinCategoryItem item in items)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }
        }

        private void OnSheetScopeChanged(object sender, RoutedEventArgs e)
        {
            if (!_isSheetContext || !IsInitialized)
                return;

            LoadSheetItems(preserveSelection: true);
            UpdateUiState();
        }

        private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PinCategoryItem.IsChecked))
                UpdateUiState();
        }

        private void OnSelectAllClick(object sender, RoutedEventArgs e)
        {
            foreach (PinCategoryItem item in ActiveItems)
                item.IsChecked = true;

            UpdateUiState();
        }

        private void OnClearAllClick(object sender, RoutedEventArgs e)
        {
            foreach (PinCategoryItem item in ActiveItems)
                item.IsChecked = false;

            UpdateUiState();
        }

        private void OnPinClick(object sender, RoutedEventArgs e)
        {
            ExecuteOperation(pinState: true);
        }

        private void OnUnpinClick(object sender, RoutedEventArgs e)
        {
            ExecuteOperation(pinState: false);
        }

        private void ExecuteOperation(bool pinState)
        {
            IList<PinTargetGroup> selectedGroups = ActiveItems
                .Where(i => i.IsChecked)
                .Select(i => i.Group)
                .ToList();

            if (selectedGroups.Count == 0)
            {
                UpdateUiState("Tick at least one group first.", isProblem: true);
                return;
            }

            PinOperationSummary summary;
            try
            {
                summary = PinElementsService.ApplyPinState(
                    _doc,
                    _activeView,
                    selectedGroups,
                    pinState,
                    IncludeAllSheets);
            }
            catch (Exception ex)
            {
                // The service's transaction is not committed when it throws, so the model is unchanged.
                UpdateUiState("Nothing was changed. Revit said: " + ex.Message, isProblem: true);
                return;
            }

            string operationWord = pinState ? "Pinned" : "Unpinned";
            string stateWord = pinState ? "already pinned" : "already unpinned";
            string scopePrefix = _isSheetContext ? SheetScopeLabel + ": " : string.Empty;

            string status = summary.TargetedCount == 0
                ? scopePrefix + "no elements in the ticked groups - nothing to do."
                : string.Format(
                    "{0}{1} {2} of {3}. {4} {5}, {6} skipped.",
                    scopePrefix,
                    operationWord,
                    summary.UpdatedCount,
                    summary.TargetedCount,
                    summary.UnchangedCount,
                    stateWord,
                    summary.SkippedCount);

            if (summary.NotCommitted)
            {
                UpdateUiState("Revit did not accept the change, so nothing was " + operationWord.ToLowerInvariant() +
                              ". In a workshared model, synchronise and try again.", isProblem: true);
                return;
            }

            if (summary.OwnedByOthersCount > 0)
            {
                status += string.Format(" {0} left alone - a colleague owns them.", summary.OwnedByOthersCount);
            }

            HasExecutedOperation = summary.UpdatedCount > 0 || HasExecutedOperation;
            UpdateUiState(status, isProblem: summary.OwnedByOthersCount > 0);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = HasExecutedOperation;
            Close();
        }

        /// <summary>
        /// The category lists sit inside the window's own outer ScrollViewer (so the two group boxes
        /// combined can scroll once they exceed MaxHeight), but ModernListBox's control template has
        /// its own internal ScrollViewer too. WPF's ScrollViewer always marks a MouseWheel event as
        /// handled once it processes it, even when there's nothing to scroll inside - so without this,
        /// the outer scrollbar could only be dragged by hand, never scrolled with the mouse wheel while
        /// hovering over a list. Re-raising the event sourced at the ListBox lets it bubble past the
        /// ListBox (skipping its own template's ScrollViewer) up to the window's outer ScrollViewer.
        /// </summary>
        private void OnListBoxPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || !(sender is ListBox listBox))
                return;

            var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent
            };
            listBox.RaiseEvent(forwarded);
            e.Handled = true;
        }

        private void UpdateUiState(string statusOverride = null, bool isProblem = false)
        {
            List<PinCategoryItem> active = ActiveItems.ToList();
            int selectedGroupCount = active.Count(i => i.IsChecked);
            int candidateCount = active.Where(i => i.IsChecked).Sum(i => i.CandidateCount);

            SelectionSummaryText.Text = string.Format(
                "{0} of {1} groups ticked  ·  {2} elements",
                selectedGroupCount,
                active.Count,
                candidateCount);

            PinButton.IsEnabled = selectedGroupCount > 0;
            UnpinButton.IsEnabled = selectedGroupCount > 0;
            SelectAllButton.IsEnabled = active.Count > 0;
            ClearAllButton.IsEnabled = active.Count > 0;

            StatusText.SetResourceReference(TextBlock.ForegroundProperty, isProblem ? "AccentWarning" : "TextSecondary");

            if (!string.IsNullOrWhiteSpace(statusOverride))
            {
                StatusText.Text = statusOverride;
                return;
            }

            if (selectedGroupCount == 0)
            {
                StatusText.Text = "Tick one or more groups.";
                return;
            }

            StatusText.Text = _isSheetContext
                ? SheetScopeLabel + " - ready to pin or unpin."
                : "Ready to pin or unpin.";
        }

        protected override void OnClosed(EventArgs e)
        {
            foreach (PinCategoryItem item in _sheetItems)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }

            foreach (PinCategoryItem item in _modelItems)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }

            if (ActiveSheetOnlyRadioButton != null)
                ActiveSheetOnlyRadioButton.Checked -= OnSheetScopeChanged;

            if (AllSheetsRadioButton != null)
                AllSheetsRadioButton.Checked -= OnSheetScopeChanged;

            base.OnClosed(e);
        }
    }
}
