// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Find by Element ID window: looks an Element ID up in this model and/or loaded links, then selects and
// zooms to it. Reads only; changes nothing in the model.
// It reopens with the last "All models" choice, the models ticked last time (BY NAME: "This model" and each
// link's name - several copies of one link share a name and are ticked together) and the last Element ID,
// saved in %AppData%\HeronTools\memory-find-by-element-id.txt each time Find runs.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Models;
using HeronTools.Utils;
using RevitTransform = Autodesk.Revit.DB.Transform;

namespace HeronTools.UI
{
    public partial class LinkedSearchWindow : Window
    {
        private readonly UIDocument _uiDoc;
        private readonly Document _hostDoc;
        private readonly View _activeView;
        private readonly IList<LinkDisplayItem> _linkItems;
        private readonly string _defaultInfoText;

        // True while the match list is being filled or cleared in code, so that is not taken as a pick.
        private bool _fillingMatches;

        // What this window remembers between uses (also after Revit restarts). The host model is kept as
        // its own yes/no, so a link that happens to be called "This model" can never be mistaken for it.
        private const string MemoryTool = "find-by-element-id";
        private const string SearchAllKey = "AllModels";
        private const string ThisModelKey = "ThisModel";
        private const string LinksKey = "Links";
        private const string ElementIdKey = "ElementId";
        private readonly WindowMemory _memory = WindowMemory.Load(MemoryTool);

        public LinkedSearchWindow(UIDocument uiDoc, Document hostDoc, View activeView, IEnumerable<RevitLinkInstance> links)
        {
            InitializeComponent();
            Icon = BrandImages.Load(BrandImages.AppIcon);
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);
            _defaultInfoText = InfoText.Text;

            _uiDoc = uiDoc;
            _hostDoc = hostDoc;
            _activeView = activeView;

            _linkItems = new List<LinkDisplayItem>
            {
                new LinkDisplayItem("This model", null, hostDoc, isHost: true)
            };

            if (links != null)
            {
                foreach (var link in links.Where(l => l != null && l.GetLinkDocument() != null))
                {
                    Document linkDoc = link.GetLinkDocument();
                    _linkItems.Add(new LinkDisplayItem(GetCleanLinkName(link, linkDoc), link, linkDoc, isHost: false));
                }
            }

            LinkSelector.ItemsSource = _linkItems;
            if (!ApplyRememberedModels() && _linkItems.Count > 0)
            {
                LinkSelector.SelectedIndex = 0;
            }

            SearchAllCheck.IsChecked = _memory.GetBool(SearchAllKey, false);

            // Only a real Element ID is put back - a damaged value is simply left out.
            string lastId = _memory.Get(ElementIdKey);
            if (long.TryParse(lastId, NumberStyles.None, CultureInfo.InvariantCulture, out long rememberedId) && rememberedId > 0)
            {
                ElementIdBox.Text = lastId;
            }

            OnSearchAllChanged(this, null);
            UpdateModelSummary();
        }

        /// <summary>
        /// Ticks the models ticked last time, found BY NAME. A link that is not loaded now is skipped.
        /// False when nothing remembered matches this model - the window then uses today's default.
        /// </summary>
        private bool ApplyRememberedModels()
        {
            bool thisModel = _memory.GetBool(ThisModelKey, false);
            var linkNames = new HashSet<string>(_memory.GetList(LinksKey), StringComparer.Ordinal);
            if (!thisModel && linkNames.Count == 0)
                return false;

            bool any = false;
            foreach (LinkDisplayItem item in _linkItems)
            {
                bool tick = item.IsHost ? thisModel : linkNames.Contains(item.DisplayName.Trim());
                if (tick)
                {
                    LinkSelector.SelectedItems.Add(item);
                    any = true;
                }
            }

            return any;
        }

        /// <summary>Remembers the choices this Find used. Best effort: a failed save never stops the search.</summary>
        private void RememberChoices(string elementIdText, bool searchAll)
        {
            _memory.Set(ElementIdKey, elementIdText);
            _memory.Set(SearchAllKey, searchAll);

            // The ticks are kept even while "All models" is on, so unticking it later brings them back.
            List<LinkDisplayItem> ticked = LinkSelector.SelectedItems.Cast<LinkDisplayItem>().ToList();
            _memory.Set(ThisModelKey, ticked.Any(i => i.IsHost));
            _memory.Set(LinksKey, ticked.Where(i => !i.IsHost).Select(i => i.DisplayName).Distinct(StringComparer.Ordinal));

            _memory.SaveQuietly();
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // Ready to type the ID straight away; a remembered ID is highlighted so typing replaces it.
            ElementIdBox.Focus();
            ElementIdBox.SelectAll();
        }

        // Enter in the ID box runs the search (the window has no default button, so Enter is handled here).
        private void OnElementIdKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            OnSearch(sender, e);
        }

        private void OnSelectAllModels(object sender, RoutedEventArgs e)
        {
            LinkSelector.SelectAll();
        }

        private void OnClearModels(object sender, RoutedEventArgs e)
        {
            LinkSelector.UnselectAll();
        }

        private void OnSearch(object sender, RoutedEventArgs e)
        {
            ClearMessages();

            // 64-bit: from Revit 2024 an Element ID can be larger than 2,147,483,647.
            if (!long.TryParse(ElementIdBox.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long parsedId) ||
                parsedId <= 0)
            {
                ErrorText.Text = "Type an Element ID - numbers only.";
                ElementIdBox.Focus();
                ElementIdBox.SelectAll();
                return;
            }
#if !REVIT2024_OR_GREATER
            if (parsedId > int.MaxValue)
            {
                ErrorText.Text = "That number is too large to be an Element ID in this Revit version.";
                ElementIdBox.Focus();
                ElementIdBox.SelectAll();
                return;
            }
#endif

            List<LinkDisplayItem> modelsToSearch;
            bool searchAll = SearchAllCheck?.IsChecked == true;

            if (searchAll)
            {
                modelsToSearch = _linkItems.ToList();
            }
            else
            {
                var selectedItems = LinkSelector.SelectedItems
                    .Cast<LinkDisplayItem>()
                    .ToList();

                if (selectedItems.Count == 0)
                {
                    ErrorText.Text = "Tick at least one model to look in.";
                    return;
                }

                var selectedSet = new HashSet<LinkDisplayItem>(selectedItems);
                modelsToSearch = _linkItems.Where(i => selectedSet.Contains(i)).ToList();
            }

            // The search is going ahead with these choices - remember them for next time.
            RememberChoices(parsedId.ToString(CultureInfo.InvariantCulture), searchAll);

            // Element IDs are only unique inside ONE model - the host and every link number their own
            // elements, so the same ID often exists in several of them. Every chosen model is searched
            // instead of stopping at the first hit, so a linked element's ID can't silently land on an
            // unrelated host element.
            List<SearchMatch> matches = FindMatches(parsedId, modelsToSearch);

            if (matches.Count == 0)
            {
                ErrorText.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    "No element with ID {0} in the model(s) you picked.",
                    parsedId);
                return;
            }

            if (matches.Count == 1)
            {
                // ShowMatch may already have said something more exact (e.g. "showing the whole link").
                if (ShowMatch(matches[0]) && InfoText.Text == _defaultInfoText)
                {
                    InfoText.Text = "Found in " + matches[0].Label + ".";
                }

                return;
            }

            // Several models have this ID - nothing is selected until the user says which one they mean.
            InfoText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Element ID {0} is in {1} models. Click the one you mean:",
                parsedId,
                matches.Count);

            _fillingMatches = true;
            try
            {
                MatchList.ItemsSource = matches;
                MatchList.SelectedItem = null;
            }
            finally
            {
                _fillingMatches = false;
            }

            MatchList.Visibility = System.Windows.Visibility.Visible;
        }

        private void OnMatchSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingMatches)
                return;

            if (MatchList.SelectedItem is SearchMatch match)
            {
                ErrorText.Text = string.Empty;
                ShowMatch(match);
            }
        }

        private List<SearchMatch> FindMatches(long elementId, IEnumerable<LinkDisplayItem> models)
        {
            var matches = new List<SearchMatch>();
            ElementId id = ElementIdHelper.FromLong(elementId);

            foreach (LinkDisplayItem model in models)
            {
                Document doc;
                if (model.IsHost)
                {
                    doc = _hostDoc;
                }
                else
                {
                    if (model.Instance == null || model.LinkDocument == null)
                        continue;

                    doc = model.LinkDocument;
                }

                Element element = doc?.GetElement(id);
                if (element != null)
                {
                    matches.Add(new SearchMatch(model, element));
                }
            }

            return matches;
        }

        private bool ShowMatch(SearchMatch match)
        {
            return match.Model.IsHost
                ? ShowHostElement(match.Element.Id)
                : ShowLinkedElement(match.Element, match.Model);
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnSearchAllChanged(object sender, RoutedEventArgs e)
        {
            if (LinkSelector == null || SearchAllCheck == null)
                return;

            bool enableSelection = SearchAllCheck.IsChecked != true;
            LinkSelector.IsEnabled = enableSelection;
            if (SelectAllModelsButton != null)
                SelectAllModelsButton.IsEnabled = enableSelection;
            if (ClearModelsButton != null)
                ClearModelsButton.IsEnabled = enableSelection;

            UpdateModelSummary();
        }

        private void OnModelSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateModelSummary();
        }

        private void UpdateModelSummary()
        {
            if (ModelSummaryText == null || SearchAllCheck == null || LinkSelector == null)
                return;

            if (SearchAllCheck.IsChecked == true)
            {
                int total = _linkItems?.Count ?? 0;
                ModelSummaryText.Text = total == 1
                    ? "Looking in this model (no loaded links)."
                    : string.Format(CultureInfo.CurrentCulture, "Looking in all {0} models.", total);
                return;
            }

            var selectedItems = LinkSelector.SelectedItems.Cast<LinkDisplayItem>().ToList();
            int count = _linkItems?.Count ?? 0;
            if (selectedItems.Count == 0)
            {
                ModelSummaryText.Text = string.Format(CultureInfo.CurrentCulture, "0 of {0} picked - tick at least one.", count);
                return;
            }

            string names = string.Join(", ", selectedItems.Take(2).Select(i => i.DisplayName));
            if (selectedItems.Count > 2)
            {
                names += $" +{selectedItems.Count - 2} more";
            }

            ModelSummaryText.Text = string.Format(CultureInfo.CurrentCulture, "{0} of {1} picked: {2}", selectedItems.Count, count, names);
        }

        private bool ShowHostElement(ElementId id)
        {
            try
            {
                _uiDoc.Selection.SetElementIds(new List<ElementId> { id });
                _uiDoc.ShowElements(id);

                // Found in current model
                return true;
            }
            catch (System.Exception ex)
            {
                ErrorText.Text = "Found it, but could not select or zoom to it: " + ex.Message;
                return false;
            }
        }

        private bool ShowLinkedElement(Element element, LinkDisplayItem item)
        {
            if (element == null || item?.Instance == null || item.LinkDocument == null)
                return false;

            try
            {
                UIView targetUiView = _uiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == _activeView.Id);
                if (targetUiView != null && TryGetHostBox(element, item.Instance, out XYZ min, out XYZ max))
                {
                    targetUiView.ZoomAndCenterRectangle(min, max);
                    return true;
                }

                // No size to zoom to (or no open view): show the whole link, and say so.
                _uiDoc.ShowElements(new List<ElementId> { item.Instance.Id });
                InfoText.Text = "Found in " + item.DisplayName + ", but it has no size to zoom to - showing the whole link instead.";
                return true;
            }
            catch (System.Exception ex)
            {
                ErrorText.Text = "Found it, but could not zoom to it: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// The linked element's box in host coordinates: all 8 corners through the box's own transform and the
        /// link's total transform (so a rotated link still gives the right box), padded so a flat element
        /// (a plan-flat plate, a line) still has room around it.
        /// </summary>
        private static bool TryGetHostBox(Element element, RevitLinkInstance link, out XYZ min, out XYZ max)
        {
            min = max = null;
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null)
                return false;

            RevitTransform toHost = link.GetTotalTransform().Multiply(box.Transform ?? RevitTransform.Identity);
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (double x in new[] { box.Min.X, box.Max.X })
            foreach (double y in new[] { box.Min.Y, box.Max.Y })
            foreach (double z in new[] { box.Min.Z, box.Max.Z })
            {
                XYZ p = toHost.OfPoint(new XYZ(x, y, z));
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
            }

            double widest = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            double pad = Math.Max(1.0, widest / 20.0);   // feet
            min = new XYZ(minX - pad, minY - pad, minZ - pad);
            max = new XYZ(maxX + pad, maxY + pad, maxZ + pad);
            return true;
        }

        private static string GetCleanLinkName(RevitLinkInstance linkInstance, Document linkDoc)
        {
            string name = (linkDoc != null && !string.IsNullOrWhiteSpace(linkDoc.Title))
                ? linkDoc.Title
                : (linkInstance?.Name ?? "Linked Model");

            int colonIndex = name.IndexOf(':');
            if (colonIndex > -1)
            {
                name = name.Substring(0, colonIndex).Trim();
            }

            return name;
        }

        private void ClearMessages()
        {
            ErrorText.Text = string.Empty;
            InfoText.Text = _defaultInfoText;

            _fillingMatches = true;
            try
            {
                MatchList.ItemsSource = null;
            }
            finally
            {
                _fillingMatches = false;
            }

            MatchList.Visibility = System.Windows.Visibility.Collapsed;
        }

        /// <summary>One model that contains the searched Element ID, as listed for the user to pick.</summary>
        private sealed class SearchMatch
        {
            public SearchMatch(LinkDisplayItem model, Element element)
            {
                Model = model;
                Element = element;
                Label = BuildLabel(model, element);
            }

            public LinkDisplayItem Model { get; }

            public Element Element { get; }

            public string Label { get; }

            public override string ToString() => Label;

            private static string BuildLabel(LinkDisplayItem model, Element element)
            {
                string category = "No category";
                string name = string.Empty;

                try { category = element.Category?.Name ?? category; } catch (System.Exception) { }
                try { name = element.Name ?? string.Empty; } catch (System.Exception) { }

                string label = model.DisplayName + " - " + category;
                return string.IsNullOrWhiteSpace(name) ? label : label + ": " + name;
            }
        }
    }
}
