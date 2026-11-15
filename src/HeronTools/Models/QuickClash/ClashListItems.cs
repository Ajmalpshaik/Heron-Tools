// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Quick Clash - the rows of the window's two category lists and of the Selection B model drop-down.

using System.ComponentModel;
using System.Globalization;

namespace HeronTools.Models.QuickClash
{
    /// <summary>One category in a Selection list: its name, how many elements of it are in the view, ticked or not.</summary>
    internal sealed class ClashCategoryItem : INotifyPropertyChanged
    {
        private bool _isChecked;

        public ClashCategoryItem(long categoryId, string name, int count, bool isChecked)
        {
            CategoryId = categoryId;
            Name = name;
            Count = count;
            _isChecked = isChecked;
        }

        /// <summary>The built-in category number - the same in every model and link, so it can be remembered.</summary>
        public long CategoryId { get; }
        public string Name { get; }
        public int Count { get; }
        public string CountText => Count.ToString("N0", CultureInfo.InvariantCulture);

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>One entry of the Selection B drop-down: "This model", or one placed copy of a loaded link.</summary>
    internal sealed class ClashSourceItem
    {
        public ClashSourceItem(long linkInstanceId, string displayName, string rememberName)
        {
            LinkInstanceId = linkInstanceId;
            DisplayName = displayName;
            RememberName = rememberName;
        }

        /// <summary>-1 = this model.</summary>
        public long LinkInstanceId { get; }
        public string DisplayName { get; }

        /// <summary>Remembered between uses instead of the id (ids differ between models).</summary>
        public string RememberName { get; }

        public bool IsThisModel => LinkInstanceId < 0;

        public override string ToString() => DisplayName;
    }
}
