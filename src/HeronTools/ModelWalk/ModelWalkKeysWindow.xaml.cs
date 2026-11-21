// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - Key Settings window: click-a-key-then-press-a-key for every action. A key another action has is
// swapped with it. Cancel, Esc or the close box put the keys back as they were. No Revit API here.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HeronTools.Services.ModelWalk;
using HeronTools.Utils;

namespace HeronTools.UI.ModelWalk
{
    public partial class ModelWalkKeysWindow : Window
    {
        private readonly WalkKeyBindings _keys;
        private readonly Dictionary<string, Key> _before;
        private readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>();
        private string _capturing;
        private bool _saved;

        internal ModelWalkKeysWindow(WalkKeyBindings keys)
        {
            _keys = keys;
            _before = keys.CopyMap();
            InitializeComponent();
            Icon = BrandImages.Load(BrandImages.AppIcon);
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);

            BuildRows();
            PreviewKeyDown += OnCaptureKeyDown;
            Closed += (s, e) => { if (!_saved) _keys.Restore(_before); };
        }

        private void BuildRows()
        {
            RowsPanel.Children.Clear();
            _buttons.Clear();
            foreach (WalkKeyAction action in WalkKeyBindings.Actions)
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

                var label = new TextBlock { Text = action.Display, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
                row.Children.Add(label);

                var button = new Button { Content = _keys.Label(action.Id), Tag = action.Id, MinWidth = 120 };
                button.SetResourceReference(StyleProperty, "SecondaryButton");
                button.Click += OnKeyButtonClick;
                Grid.SetColumn(button, 1);
                row.Children.Add(button);

                _buttons[action.Id] = button;
                RowsPanel.Children.Add(row);
            }
        }

        private void RefreshButtons()
        {
            foreach (KeyValuePair<string, Button> pair in _buttons)
                pair.Value.Content = _keys.Label(pair.Key);
        }

        private void OnKeyButtonClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button))
                return;
            CancelCapture();
            _capturing = (string)button.Tag;
            button.Content = "Press a key...";
            StatusText.Text = "Press the key for \"" + WalkKeyBindings.DisplayOf(_capturing) + "\". Esc keeps the old one.";
            Keyboard.Focus(this);
        }

        private void OnCaptureKeyDown(object sender, KeyEventArgs e)
        {
            if (_capturing == null)
                return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            e.Handled = true;

            if (key == Key.Escape)
            {
                CancelCapture();
                StatusText.Text = "Not changed.";
                return;
            }
            if (!WalkKeyBindings.IsAllowed(key))
            {
                StatusText.Text = WalkKeyBindings.LabelOf(key) + " is fixed or kept by Windows - try another key.";
                return;
            }

            string action = _capturing;
            _capturing = null;
            _keys.SetSwapping(action, key, out string swappedWith);
            RefreshButtons();
            StatusText.Text = swappedWith == null
                ? "\"" + WalkKeyBindings.DisplayOf(action) + "\" is now " + _keys.Label(action) + "."
                : "\"" + WalkKeyBindings.DisplayOf(action) + "\" is now " + _keys.Label(action) + ". It was on \"" +
                  WalkKeyBindings.DisplayOf(swappedWith) + "\", which now has " + _keys.Label(swappedWith) + " - the two swapped.";
        }

        private void CancelCapture()
        {
            if (_capturing == null)
                return;
            _capturing = null;
            RefreshButtons();
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            CancelCapture();
            _keys.ResetToDefaults();
            RefreshButtons();
            StatusText.Text = "Every key is back to the standard one. Save keeps it.";
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            CancelCapture();
            string problem = _keys.Save();
            _saved = true;   // in memory either way: the keys work for this walk
            if (problem != null)
            {
                StatusText.Text = problem;
                return;      // keep the window open so the modeller sees why
            }
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
