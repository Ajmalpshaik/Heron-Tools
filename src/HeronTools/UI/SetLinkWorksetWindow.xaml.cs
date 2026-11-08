// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Link Workset window: the modeller ticks Revit links and CAD imports and puts them on a user workset, in one
// named transaction. Workshared models only; worksharing is never turned on by this tool.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using HeronTools.Utils;

namespace HeronTools.UI
{
    /// <summary>
    /// Interaction logic for SetLinkWorksetWindow.xaml
    /// </summary>
    public partial class SetLinkWorksetWindow : Window
    {
        private const string NotWorksharedMessage =
            "Not a workshared model, so there are no worksets. Turn on worksharing first (Collaborate > Worksets).";
        private readonly Document _doc;
        private readonly ObservableCollection<LinkWorksetItem> _linkItems;

        /// <param name="selectedIds">Revit's selection when the tool started: only those links start ticked.</param>
        public SetLinkWorksetWindow(Document doc, ICollection<ElementId> selectedIds)
        {
            InitializeComponent();
            Icon = BrandImages.Load(BrandImages.AppIcon);
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);

            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _linkItems = new ObservableCollection<LinkWorksetItem>(CollectLinkItems(_doc));

            // Start from what the modeller selected - never "everything ticked", which moved every link and
            // import when only one was meant.
            var selected = new HashSet<long>((selectedIds ?? new List<ElementId>()).Select(id => id.LongValue()));
            foreach (LinkWorksetItem item in _linkItems)
                item.IsChecked = selected.Contains(item.Element.Id.LongValue());
            links_listbox.ItemsSource = _linkItems;
            foreach (LinkWorksetItem item in _linkItems)
                item.PropertyChanged += (s, e) => UpdateLinkCount();
            UpdateLinkCount();

            var worksetNames = new FilteredWorksetCollector(_doc)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .Select(ws => ws.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            workset_combobox.ItemsSource = worksetNames;
            workset_combobox.Text = LinkWorksetSettings.GetLastWorksetName();

            // Worksets only exist in a workshared model. Turning worksharing on is a decision for the
            // whole model (it also clears the Undo history), so this tool never does it - it says so
            // up front instead of failing part way through.
            if (!_doc.IsWorkshared)
            {
                assign_workset_button.IsEnabled = false;
                ShowStatus(NotWorksharedMessage, isProblem: true);
            }
            else if (_linkItems.Count > 0 && !_linkItems.Any(item => item.IsChecked))
            {
                ShowStatus("Tick the links and imports to move (select them in Revit first to have them ticked).", isProblem: false);
            }

            Loaded += (s, e) => workset_combobox.Focus();
        }

        // "3 of 5 ticked" under the list title.
        private void UpdateLinkCount()
        {
            int ticked = _linkItems.Count(item => item.IsChecked);
            links_count_text.Text = _linkItems.Count == 0
                ? "Nothing to move."
                : $"{ticked} of {_linkItems.Count} ticked";
        }

        private void ShowStatus(string message, bool isProblem)
        {
            StatusText.Text = message ?? string.Empty;
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, isProblem ? "AccentWarning" : "TextSecondary");
            StatusText.Visibility = string.IsNullOrEmpty(StatusText.Text)
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
        }

        private static IList<LinkWorksetItem> CollectLinkItems(Document doc)
        {
            var linkInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .WhereElementIsNotElementType()
                .Cast<Element>();

            var importInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .Cast<Element>();

            return linkInstances
                .Concat(importInstances)
                .Select(element => new LinkWorksetItem(element))
                .OrderBy(item => item.Name ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            foreach (var item in _linkItems)
            {
                item.IsChecked = true;
            }
        }

        private void OnSelectNone(object sender, RoutedEventArgs e)
        {
            foreach (var item in _linkItems)
            {
                item.IsChecked = false;
            }
        }

        private void OnInvertSelection(object sender, RoutedEventArgs e)
        {
            foreach (var item in _linkItems)
            {
                item.IsChecked = !item.IsChecked;
            }
        }

        private void OnAssignWorkset(object sender, RoutedEventArgs e)
        {
            // The button is disabled for a non-workshared model; re-checked here as a guard.
            if (!_doc.IsWorkshared)
            {
                ShowStatus(NotWorksharedMessage, isProblem: true);
                return;
            }

            string targetWorksetName = (workset_combobox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(targetWorksetName))
            {
                ShowStatus("Pick a workset, or type a name for a new one.", isProblem: true);
                return;
            }

            Workset existingWorkset = FindWorkset(_doc, targetWorksetName);
            if (existingWorkset != null)
            {
                targetWorksetName = existingWorkset.Name;   // "links" typed, "Links" exists: use that one
            }
            else if (targetWorksetName.IndexOfAny(BannedWorksetChars) >= 0)
            {
                ShowStatus("A workset name cannot contain any of these: { } [ ] | ; < > ? ` ~", isProblem: true);
                return;
            }

            List<LinkWorksetItem> linksToMove = _linkItems
                .Where(item => item.IsChecked)
                .ToList();

            if (linksToMove.Count == 0)
            {
                ShowStatus("Tick at least one link to move.", isProblem: true);
                return;
            }

            var moved = new List<string>();
            var notMoved = new List<string>();
            int alreadyThere = 0;

            try
            {
                using (var t = new Transaction(_doc, "Assign Links to Workset"))
                {
                    t.Start();

                    Workset targetWorkset = existingWorkset ?? Workset.Create(_doc, targetWorksetName);

                    // The workset parameter expects an integer Workset id value.
                    // WorksetId.IntegerValue is unrelated to ElementId and is valid in all versions.
                    int targetId = targetWorkset.Id.IntegerValue;

                    foreach (LinkWorksetItem item in linksToMove)
                    {
                        Parameter worksetParam = item.Element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                        if (worksetParam == null || worksetParam.IsReadOnly)
                        {
                            notMoved.Add(item.Name);
                            continue;
                        }

                        if (worksetParam.AsInteger() == targetId)
                        {
                            alreadyThere++;
                            continue;
                        }

                        if (worksetParam.Set(targetId))
                            moved.Add(item.Name);
                        else
                            notMoved.Add(item.Name);
                    }

                    // A rolled-back commit (e.g. a link owned by another user) moved nothing.
                    if (t.Commit() != TransactionStatus.Committed)
                    {
                        ShowStatus("Revit did not keep the change, so no link was moved.", isProblem: true);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                // The transaction was not committed, so nothing changed.
                ShowStatus("Nothing was moved. Revit said: " + ex.Message, isProblem: true);
                return;
            }

            bool saved = LinkWorksetSettings.SaveLastWorksetName(targetWorksetName, out string saveError);

            // The result goes in the status line - no success popup. The window stays open so the
            // line can be read; Close when done.
            string status = (existingWorkset == null ? $"Made the new workset \"{targetWorksetName}\". " : string.Empty) +
                            $"Moved {moved.Count} link(s) to the \"{targetWorksetName}\" workset.";
            if (alreadyThere > 0)
                status += $" {alreadyThere} were already on it.";
            if (notMoved.Count > 0)
                status += " Not moved - their workset can't be changed: " + JoinNames(notMoved) + ".";
            if (!saved)
                status += " The workset name could not be remembered for next time: " + saveError;

            ShowStatus(status, isProblem: notMoved.Count > 0 || !saved);
        }

        private static string JoinNames(IList<string> names)
        {
            const int MaxShown = 5;
            string shown = string.Join(", ", names.Take(MaxShown));
            return names.Count > MaxShown
                ? shown + $" and {names.Count - MaxShown} more"
                : shown;
        }

        /// <summary>The characters Revit refuses in a workset name.</summary>
        private static readonly char[] BannedWorksetChars = { '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

        /// <summary>An existing user workset by name, ignoring case (Revit treats "Links" and "links" as the same).</summary>
        private static Workset FindWorkset(Document doc, string name)
        {
            return new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .FirstOrDefault(ws => string.Equals(ws.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal sealed class LinkWorksetItem : INotifyPropertyChanged
    {
        private bool _isChecked;

        internal LinkWorksetItem(Element element)
        {
            Element = element;
            _isChecked = true;
            Name = GetDisplayName(element);
        }

        public string Name { get; }

        public Element Element { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                    return;

                _isChecked = value;
                OnPropertyChanged(nameof(IsChecked));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private static string GetDisplayName(Element element)
        {
            if (element is RevitLinkInstance linkInstance)
            {
                string name = linkInstance.Name ?? string.Empty;
                int index = name.IndexOf(':');
                return index >= 0 ? name.Substring(0, index) : name;
            }

            if (element is ImportInstance importInstance)
            {
                Parameter param = importInstance.get_Parameter(BuiltInParameter.IMPORT_SYMBOL_NAME);
                if (param != null && param.HasValue)
                    return param.AsString();
            }

            return "Unnamed Import/Link";
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
