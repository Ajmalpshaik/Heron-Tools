// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Represents one selectable target view row in the View Crop view-picker UI.

using System.ComponentModel;
using Autodesk.Revit.DB;

namespace HeronTools.Models.ViewCrop
{
    /// <summary>
    /// Represents one selectable target view row in the view picker dialog.
    /// </summary>
    internal sealed class ViewCropTargetViewItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public ElementId ViewId { get; set; }

        public string ViewName { get; set; }

        public string ViewTypeName { get; set; }

        public string SheetNumber { get; set; }

        public string SheetName { get; set; }

        public string GroupName { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
