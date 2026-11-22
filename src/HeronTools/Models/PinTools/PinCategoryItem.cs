// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// UI row model for selectable pin target groups.

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HeronTools.Models.PinTools
{
    /// <summary>
    /// Represents one selectable row in the Pin Elements dialog.
    /// </summary>
    internal sealed class PinCategoryItem : INotifyPropertyChanged
    {
        private bool _isChecked;

        public PinCategoryItem(PinTargetGroup group, string name, string description, int candidateCount, bool isChecked)
        {
            Group = group;
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            CandidateCount = candidateCount;
            _isChecked = isChecked;
        }

        public PinTargetGroup Group { get; }

        public string Name { get; }

        public string Description { get; }

        public int CandidateCount { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                    return;

                _isChecked = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
