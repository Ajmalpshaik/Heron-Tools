// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Code-behind for the target-view picker (filter, group by sheet, validate selection).

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Autodesk.Revit.DB;
using HeronTools.Models.ViewCrop;
using HeronTools.Utils;

namespace HeronTools.UI.ViewCrop
{
    /// <summary>
    /// Interaction logic for ViewCropTargetViewsWindow.xaml
    /// </summary>
    public partial class ViewCropTargetViewsWindow : Window
    {
        private readonly List<ViewCropTargetViewItem> _items;
        private readonly ICollectionView _collectionView;

        internal ViewCropTargetViewsWindow(IEnumerable<ViewCropTargetViewItem> items)
        {
            InitializeComponent();

            // Shared Heron Tools window entrance and exit (fade + short rise / sink). Cosmetic only.
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);

            // The Heron app icon on the title bar and taskbar.
            Icon = BrandImages.Load(BrandImages.AppIcon);

            _items = (items ?? Enumerable.Empty<ViewCropTargetViewItem>()).ToList();
            foreach (ViewCropTargetViewItem item in _items)
            {
                item.PropertyChanged += OnItemPropertyChanged;
            }

            _collectionView = CollectionViewSource.GetDefaultView(_items);
            _collectionView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ViewCropTargetViewItem.GroupName)));
            _collectionView.Filter = FilterItem;

            ViewsListBox.ItemsSource = _collectionView;
            SelectedViewIds = new List<ElementId>();

            UpdateSelectionCount();
        }

        internal IList<ElementId> SelectedViewIds { get; private set; }

        private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SelectionErrorText.Text = string.Empty;
            _collectionView.Refresh();
            UpdateSelectionCount();
        }

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            SelectionErrorText.Text = string.Empty;
            foreach (ViewCropTargetViewItem item in _items)
            {
                if (FilterItem(item))
                    item.IsSelected = true;
            }

            UpdateSelectionCount();
        }

        private void OnClearAll(object sender, RoutedEventArgs e)
        {
            SelectionErrorText.Text = string.Empty;
            foreach (ViewCropTargetViewItem item in _items)
            {
                item.IsSelected = false;
            }

            UpdateSelectionCount();
        }

        private void OnRun(object sender, RoutedEventArgs e)
        {
            SelectionErrorText.Text = string.Empty;

            List<ElementId> selected = _items
                .Where(i => i.IsSelected && i.ViewId != null)
                .GroupBy(i => i.ViewId.LongValue())
                .Select(g => g.First().ViewId)
                .ToList();

            if (selected.Count == 0)
            {
                SelectionErrorText.Text = "No view is ticked. Tick at least one view in the list, then run again.";
                return;
            }

            SelectedViewIds = selected;
            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool FilterItem(object obj)
        {
            ViewCropTargetViewItem item = obj as ViewCropTargetViewItem;
            if (item == null)
                return false;

            string search = (SearchTextBox?.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(search))
                return true;

            return Contains(item.ViewName, search)
                || Contains(item.ViewTypeName, search)
                || Contains(item.SheetNumber, search)
                || Contains(item.SheetName, search)
                || Contains(item.GroupName, search);
        }

        private static bool Contains(string source, string term)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(term))
                return false;

            return CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                source,
                term,
                CompareOptions.IgnoreCase) >= 0;
        }

        private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewCropTargetViewItem.IsSelected))
            {
                UpdateSelectionCount();
            }
        }

        private void UpdateSelectionCount()
        {
            int selected = _items.Count(i => i.IsSelected);
            int visible = _collectionView.Cast<object>().Count();

            SelectionCountText.Text = visible == _items.Count
                ? $"{selected} of {_items.Count} views picked"
                : $"{selected} of {_items.Count} views picked  |  showing {visible}";
        }

        protected override void OnClosed(EventArgs e)
        {
            foreach (ViewCropTargetViewItem item in _items)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }

            base.OnClosed(e);
        }
    }
}
