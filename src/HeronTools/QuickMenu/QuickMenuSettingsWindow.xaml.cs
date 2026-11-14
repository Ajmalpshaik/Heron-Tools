// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Lets Ajmal decide what the quick wheel holds - which Heron Tools button sits in each slot, how many slots there are (4 to 12) and how big the wheel opens.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using HeronTools.Services.QuickMenu;
using HeronTools.Utils;

namespace HeronTools.UI.QuickMenu
{
    /// <summary>Quick Menu Settings: choose the tool in each slot of the Heron quick wheel.</summary>
    public partial class QuickMenuSettingsWindow : Window
    {
        private static readonly int[] SlotCountChoices = { 4, 6, 8, 10, 12 };

        private static readonly int[] WheelSizeChoices = { 460, 560, 660, 760 };

        private static readonly string[] WheelSizeLabels =
        {
            "Small (460)",
            "Medium (560)",
            "Large (660)",
            "Extra large (760)"
        };

        /// <summary>What the Show combo offers, in order. Null means "no filter - show everything".</summary>
        private static readonly QuickToolSource?[] SourceFilterChoices =
        {
            null,
            QuickToolSource.HeronTools,
            QuickToolSource.Revit
        };

        private static readonly string[] SourceFilterLabels =
        {
            "All",
            "Heron Tools only",
            "Revit commands only"
        };

        /// <summary>Blank space left between two wedges, in degrees, per side. Matches the real wheel.</summary>
        private const double WedgeGapDegrees = 1.6;

        /// <summary>Hole in the middle, as a fraction of the outer radius. Matches the real wheel.</summary>
        private const double HubRadiusFraction = 0.42;

        /// <summary>Drag payload carrying a tool's key, dragged out of the tool list.</summary>
        private const string ToolKeyFormat = "HeronTools.QuickMenu.ToolKey";

        /// <summary>Drag payload carrying a slot number, dragged off the wheel itself.</summary>
        private const string SlotIndexFormat = "HeronTools.QuickMenu.SlotIndex";

        // The wheel is painted in the same colours as the real one, so this preview is not a diagram
        // of the wheel - it is what Ajmal is about to get.
        // Heron brand only. The wheel sits on a Surface card, so the slots are Ink (one step deeper),
        // the picked slot is lit in Accent and a live drop target in Success - each at an opacity.
        private static readonly Brush WedgeFill = MakeBrush(0xFF, 0x0F, 0x0F, 0x10);            // Ink
        private static readonly Brush WedgeFillEmpty = MakeBrush(0x80, 0x0F, 0x0F, 0x10);       // Ink 50 %
        private static readonly Brush WedgeFillDrop = MakeBrush(0x47, 0x3E, 0xCF, 0x8E);        // Success 28 %
        private static readonly Brush WedgeStroke = MakeBrush(0x24, 0xF7, 0xF5, 0xF2);          // Mist 14 %
        private static readonly Brush WedgeStrokeSelected = MakeBrush(0xFF, 0xFD, 0x64, 0x1A);  // Accent
        private static readonly Brush WedgeStrokeDrop = MakeBrush(0xFF, 0x3E, 0xCF, 0x8E);      // Success
        private static readonly Brush HubFill = MakeBrush(0xFF, 0x0F, 0x0F, 0x10);              // Ink
        private static readonly Brush HubStroke = MakeBrush(0x24, 0xF7, 0xF5, 0xF2);            // Mist 14 %
        private static readonly Brush WheelTextPrimary = MakeBrush(0xEB, 0xF7, 0xF5, 0xF2);     // Mist 92 %
        private static readonly Brush WheelTextIdle = MakeBrush(0xB8, 0xF7, 0xF5, 0xF2);        // Mist 72 %
        private static readonly Brush WheelTextDisabled = MakeBrush(0x73, 0xF7, 0xF5, 0xF2);    // Mist 45 %

        private readonly IList<QuickToolEntry> _allTools;
        private readonly List<string> _slotKeys = new List<string>();
        private readonly List<Path> _wedges = new List<Path>();

        // The picked (or drop-target) slot steps out a little, like the lit slot on the real wheel.
        // One move per slot, shared by its wedge, icon and number; the target is kept so a repaint
        // during a drag does not restart a move that is already on its way.
        private readonly List<TranslateTransform> _wedgeLifts = new List<TranslateTransform>();
        private readonly List<double> _wedgeAngles = new List<double>();
        private readonly List<bool> _wedgeLifted = new List<bool>();
        private bool _loading = true;

        /// <summary>Which slot the middle buttons act on. -1 when nothing is picked.</summary>
        private int _selectedSlot = -1;

        /// <summary>Which slot a drag is currently hovering over. -1 when none.</summary>
        private int _dropSlot = -1;

        private Point _dragOrigin;
        private int _dragFromSlot = -1;

        /// <summary>
        /// True when the saved layout file could not be read: the window is showing the DEFAULT
        /// layout in its place, and saving would replace Ajmal's real layout with it.
        /// </summary>
        private readonly bool _layoutUnreadable;

        internal QuickMenuSettingsWindow(IList<QuickToolEntry> allTools, QuickMenuConfig config)
        {
            InitializeComponent();
            Icon = BrandImages.Load(BrandImages.AppIcon);
            WindowMotionHelper.AttachStandardEntrance(this);

            _allTools = allTools ?? new List<QuickToolEntry>();

            QuickMenuConfig working = config ?? QuickMenuConfig.Load();
            working.Normalize();
            _layoutUnreadable = working.LoadFailed;

            _slotKeys.AddRange(working.Slots);

            FillChoiceCombos(working);
            FillToolList(string.Empty);
            RefreshWheel(0);

            ToolList.SelectionChanged += OnListSelectionChanged;

            _loading = false;
            UpdateButtonStates();

            if (_layoutUnreadable)
            {
                SaveButton.IsEnabled = false;
                ShowError("Your saved Quick Menu layout could not be read, so the default layout is shown and " +
                          "Save is turned off - saving now would replace your layout with these defaults. " +
                          "Close this window and try again in a moment.");
            }
        }

        /// <summary>Inline message under the window body - the house rule is no popup.</summary>
        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = System.Windows.Visibility.Visible;
        }

        #region Setting up the lists

        private void FillChoiceCombos(QuickMenuConfig config)
        {
            foreach (int choice in SlotCountChoices)
            {
                SlotCountCombo.Items.Add(new ComboBoxItem
                {
                    Content = choice.ToString(CultureInfo.InvariantCulture) + " tools",
                    Tag = choice
                });
            }

            SlotCountCombo.SelectedIndex = NearestIndex(SlotCountChoices, config.SlotCount);

            for (int i = 0; i < WheelSizeChoices.Length; i++)
            {
                WheelSizeCombo.Items.Add(new ComboBoxItem
                {
                    Content = WheelSizeLabels[i],
                    Tag = WheelSizeChoices[i]
                });
            }

            WheelSizeCombo.SelectedIndex = NearestIndex(WheelSizeChoices, config.Diameter);

            for (int i = 0; i < SourceFilterLabels.Length; i++)
            {
                SourceFilterCombo.Items.Add(new ComboBoxItem
                {
                    Content = SourceFilterLabels[i],
                    Tag = i
                });
            }

            SourceFilterCombo.SelectedIndex = 0;

            // The wheel's look. Tags carry the value; the order is the order on the design page.
            foreach (QuickMenuStyle style in QuickMenuLook.Styles)
            {
                StyleCombo.Items.Add(new ComboBoxItem { Content = QuickMenuLook.StyleName(style), Tag = style });
                if (style == config.Style)
                {
                    StyleCombo.SelectedIndex = StyleCombo.Items.Count - 1;
                }
            }

            NamesCombo.Items.Add(new ComboBoxItem { Content = "On the wheel", Tag = true });
            NamesCombo.Items.Add(new ComboBoxItem { Content = "Icons only", Tag = false });
            NamesCombo.SelectedIndex = config.ShowNames ? 0 : 1;

            foreach (QuickMenuOpening opening in QuickMenuLook.Openings)
            {
                OpeningCombo.Items.Add(new ComboBoxItem { Content = QuickMenuLook.OpeningName(opening), Tag = opening });
                if (opening == config.Opening)
                {
                    OpeningCombo.SelectedIndex = OpeningCombo.Items.Count - 1;
                }
            }

            if (StyleCombo.SelectedIndex < 0)
            {
                StyleCombo.SelectedIndex = 0;
            }

            if (OpeningCombo.SelectedIndex < 0)
            {
                OpeningCombo.SelectedIndex = 0;
            }
        }

        private QuickMenuStyle ChosenStyle()
        {
            var item = StyleCombo.SelectedItem as ComboBoxItem;
            return item != null && item.Tag is QuickMenuStyle ? (QuickMenuStyle)item.Tag : QuickMenuLook.DefaultStyle;
        }

        private bool ChosenShowNames()
        {
            var item = NamesCombo.SelectedItem as ComboBoxItem;
            return item == null || !(item.Tag is bool) || (bool)item.Tag;
        }

        private QuickMenuOpening ChosenOpening()
        {
            var item = OpeningCombo.SelectedItem as ComboBoxItem;
            return item != null && item.Tag is QuickMenuOpening ? (QuickMenuOpening)item.Tag : QuickMenuLook.DefaultOpening;
        }

        /// <summary>Which source the Show combo is asking for, or null for everything.</summary>
        private QuickToolSource? SelectedSourceFilter()
        {
            var item = SourceFilterCombo.SelectedItem as ComboBoxItem;
            if (item == null || !(item.Tag is int))
            {
                return null;
            }

            int index = (int)item.Tag;
            return index >= 0 && index < SourceFilterChoices.Length
                ? SourceFilterChoices[index]
                : null;
        }

        private void FillToolList(string filter)
        {
            string previousKey = SelectedToolKey();

            ToolList.Items.Clear();

            string needle = (filter ?? string.Empty).Trim();
            QuickToolSource? wantedSource = SelectedSourceFilter();
            int shown = 0;

            foreach (QuickToolEntry entry in _allTools)
            {
                if (wantedSource.HasValue && entry.Source != wantedSource.Value)
                {
                    continue;
                }

                if (needle.Length > 0 && !Matches(entry, needle))
                {
                    continue;
                }

                var item = new ListBoxItem
                {
                    Content = entry.ListLabel,
                    Tag = entry,
                    ToolTip = string.IsNullOrEmpty(entry.Tooltip) ? null : entry.Tooltip
                };

                ToolList.Items.Add(item);
                shown++;

                if (previousKey != null && string.Equals(entry.Key, previousKey, StringComparison.OrdinalIgnoreCase))
                {
                    ToolList.SelectedItem = item;
                }
            }

            ToolCountText.Text = shown == _allTools.Count
                ? shown.ToString(CultureInfo.InvariantCulture) + " tools"
                : shown.ToString(CultureInfo.InvariantCulture) + " of " +
                  _allTools.Count.ToString(CultureInfo.InvariantCulture) + " tools";
        }

        /// <summary>
        /// Redraws the wheel and picks a slot. The geometry deliberately mirrors QuickMenuWindow -
        /// slot 1 at the top, the rest clockwise - so what Ajmal arranges here is what opens.
        /// </summary>
        private void RefreshWheel(int selectIndex)
        {
            _selectedSlot = _slotKeys.Count == 0
                ? -1
                : Math.Min(Math.Max(selectIndex, 0), _slotKeys.Count - 1);

            WheelCanvas.Children.Clear();
            _wedges.Clear();
            _wedgeLifts.Clear();
            _wedgeAngles.Clear();
            _wedgeLifted.Clear();

            for (int i = 0; i < _slotKeys.Count; i++)
            {
                AddWedge(i);
            }

            AddHub();
            UpdateWheelVisuals();
        }

        private double WheelCentre
        {
            get { return WheelCanvas.Width / 2.0; }
        }

        private double OuterRadius
        {
            get { return WheelCentre - 4.0; }
        }

        private double InnerRadius
        {
            get { return OuterRadius * HubRadiusFraction; }
        }

        private double StepDegrees
        {
            get { return _slotKeys.Count > 0 ? 360.0 / _slotKeys.Count : 360.0; }
        }

        private void AddWedge(int index)
        {
            QuickToolEntry entry = FindTool(_slotKeys[index]);
            bool filled = entry != null;

            double step = StepDegrees;
            double centreAngle = -90.0 + (index * step);
            double startAngle = centreAngle - (step / 2.0) + WedgeGapDegrees;
            double endAngle = centreAngle + (step / 2.0) - WedgeGapDegrees;

            var lift = new TranslateTransform();
            _wedgeLifts.Add(lift);
            _wedgeAngles.Add(centreAngle);
            _wedgeLifted.Add(false);

            var wedge = new Path
            {
                Data = BuildWedgeGeometry(startAngle, endAngle),
                Fill = filled ? WedgeFill : WedgeFillEmpty,
                Stroke = WedgeStroke,
                StrokeThickness = 1.0,
                RenderTransform = lift,

                // The canvas does the hit testing by angle, exactly like the real wheel, so the
                // shapes themselves must stay out of the way.
                IsHitTestVisible = false
            };

            WheelCanvas.Children.Add(wedge);
            _wedges.Add(wedge);

            double contentRadius = (InnerRadius + OuterRadius) / 2.0;
            Point contentCentre = Polar(contentRadius, centreAngle);

            // Same reasoning as the real wheel: measure the room at the wedge's inner corners, where
            // it is narrowest, and never let the block outgrow the thickness of the ring.
            double halfWedgeRadians = ((step / 2.0) - WedgeGapDegrees) * Math.PI / 180.0;
            double innerCornerRadius = Math.Max(InnerRadius + 4.0, contentRadius - 26.0);
            double angularAllowance = 2.0 * innerCornerRadius * Math.Tan(Math.Min(halfWedgeRadians, 1.2));
            double radialAllowance = (OuterRadius - InnerRadius) - 32.0;   // clear of the rim slot number
            double contentWidth = Math.Max(42.0, Math.Min(angularAllowance, radialAllowance));

            var stack = new StackPanel
            {
                Width = contentWidth,
                IsHitTestVisible = false,
                RenderTransform = lift
            };

            if (filled && entry.Icon != null)
            {
                stack.Children.Add(new Image
                {
                    Source = entry.Icon,
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(0, 0, 0, 3),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    SnapsToDevicePixels = true
                });
            }

            stack.Children.Add(new TextBlock
            {
                Text = filled ? entry.DisplayName : "Empty",
                FontSize = 10.5,
                MaxHeight = 30,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.WrapWithOverflow,   // break between words, never inside one
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = filled ? WheelTextPrimary : WheelTextDisabled,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            stack.Measure(new Size(stack.Width, double.PositiveInfinity));
            Canvas.SetLeft(stack, contentCentre.X - (stack.Width / 2.0));
            Canvas.SetTop(stack, contentCentre.Y - (stack.DesiredSize.Height / 2.0));
            WheelCanvas.Children.Add(stack);

            // Slot number out by the rim, the same place the real wheel puts its 1-9 keys.
            Point digitCentre = Polar(OuterRadius - 11.0, centreAngle);
            var digit = new TextBlock
            {
                Text = (index + 1).ToString(CultureInfo.InvariantCulture),
                FontSize = 10.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = WheelTextDisabled,
                IsHitTestVisible = false,
                RenderTransform = lift
            };

            digit.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(digit, digitCentre.X - (digit.DesiredSize.Width / 2.0));
            Canvas.SetTop(digit, digitCentre.Y - (digit.DesiredSize.Height / 2.0));
            WheelCanvas.Children.Add(digit);
        }

        private void AddHub()
        {
            double inner = InnerRadius;

            var hub = new Ellipse
            {
                Width = inner * 2.0,
                Height = inner * 2.0,
                Fill = HubFill,
                Stroke = HubStroke,
                StrokeThickness = 1.0,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(hub, WheelCentre - inner);
            Canvas.SetTop(hub, WheelCentre - inner);
            WheelCanvas.Children.Add(hub);

            var caption = new StackPanel
            {
                Width = (inner * 2.0) - 16.0,
                IsHitTestVisible = false
            };

            if (TryFindResource("HeronMarkGeometry") is Geometry mark)
            {
                caption.Children.Add(new Viewbox
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(0, 0, 0, 6),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new Path { Data = mark, Fill = WheelTextIdle, Stretch = Stretch.Uniform }
                });
            }

            caption.Children.Add(new TextBlock
            {
                Text = _slotKeys.Count.ToString(CultureInfo.InvariantCulture) + " slots",
                FontSize = 11.0,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = WheelTextIdle
            });

            caption.Measure(new Size(caption.Width, double.PositiveInfinity));
            Canvas.SetLeft(caption, WheelCentre - (caption.Width / 2.0));
            Canvas.SetTop(caption, WheelCentre - (caption.DesiredSize.Height / 2.0));
            WheelCanvas.Children.Add(caption);
        }

        private Geometry BuildWedgeGeometry(double startAngle, double endAngle)
        {
            // The same rounded slot as the real wheel, at this preview's size.
            return RoundedSlotGeometry.Build(
                new Point(WheelCentre, WheelCentre), InnerRadius, OuterRadius, startAngle, endAngle,
                OuterRadius * RoundedSlotGeometry.CornerFraction);
        }

        private Point Polar(double radius, double angleDegrees)
        {
            double radians = angleDegrees * Math.PI / 180.0;
            double centre = WheelCentre;
            return new Point(
                centre + (radius * Math.Cos(radians)),
                centre + (radius * Math.Sin(radians)));
        }

        /// <summary>Which slot the pointer is over, or -1 for the hub / outside the ring.</summary>
        private int IndexAt(Point point)
        {
            if (_slotKeys.Count == 0)
            {
                return -1;
            }

            double centre = WheelCentre;
            double dx = point.X - centre;
            double dy = point.Y - centre;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));

            if (distance < InnerRadius || distance > OuterRadius)
            {
                return -1;
            }

            double step = StepDegrees;
            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            double shifted = angle + 90.0 + (step / 2.0);
            shifted = ((shifted % 360.0) + 360.0) % 360.0;

            int index = (int)(shifted / step);
            return index >= 0 && index < _slotKeys.Count ? index : -1;
        }

        /// <summary>Repaints the selected and drop-target wedges without rebuilding the whole wheel.</summary>
        private void UpdateWheelVisuals()
        {
            for (int i = 0; i < _wedges.Count; i++)
            {
                bool filled = FindTool(_slotKeys[i]) != null;
                Brush fill = filled ? WedgeFill : WedgeFillEmpty;
                Brush stroke = WedgeStroke;
                double thickness = 1.0;

                if (i == _selectedSlot)
                {
                    fill = SelectedFill();
                    stroke = WedgeStrokeSelected;
                    thickness = 1.6;
                }

                // A live drop target wins over the selection, so the landing spot is never in doubt.
                if (i == _dropSlot)
                {
                    fill = WedgeFillDrop;
                    stroke = WedgeStrokeDrop;
                    thickness = 2.0;
                }

                _wedges[i].Fill = fill;
                _wedges[i].Stroke = stroke;
                _wedges[i].StrokeThickness = thickness;
                SetLift(i, i == _selectedSlot || i == _dropSlot);
            }

            UpdateWheelHint();
        }

        /// <summary>Steps a slot 4 units out towards the rim, or lets it settle back - only when that changes.</summary>
        private void SetLift(int index, bool lifted)
        {
            if (index < 0 || index >= _wedgeLifts.Count || _wedgeLifted[index] == lifted)
            {
                return;
            }

            _wedgeLifted[index] = lifted;

            double radians = _wedgeAngles[index] * Math.PI / 180.0;
            double distance = lifted ? 4.0 : 0.0;
            double x = distance * Math.Cos(radians);
            double y = distance * Math.Sin(radians);
            TranslateTransform move = _wedgeLifts[index];

            if (!SystemParameters.ClientAreaAnimation)
            {
                move.X = x;
                move.Y = y;
                return;
            }

            var duration = new Duration(TimeSpan.FromMilliseconds(lifted ? 140 : 160));
            IEasingFunction ease = lifted
                ? (IEasingFunction)new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }
                : new CubicEase { EasingMode = EasingMode.EaseOut };

            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, duration) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, duration) { EasingFunction = ease });
        }

        /// <summary>
        /// The picked slot's fill: the same outward Accent glow as the real wheel's lit slot (faint at the
        /// hub, stronger at the rim), centred on this canvas. Built once - the canvas size never changes.
        /// </summary>
        private Brush SelectedFill()
        {
            if (_selectedFill == null)
            {
                var glow = new RadialGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    Center = new Point(WheelCentre, WheelCentre),
                    GradientOrigin = new Point(WheelCentre, WheelCentre),
                    RadiusX = OuterRadius,
                    RadiusY = OuterRadius
                };
                glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x1F, 0xFD, 0x64, 0x1A), HubRadiusFraction)); // Accent 12 %
                glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0xFD, 0x64, 0x1A), 1.0));               // Accent 40 %
                glow.Freeze();
                _selectedFill = glow;
            }

            return _selectedFill;
        }

        private Brush _selectedFill;

        private void UpdateWheelHint()
        {
            if (_selectedSlot < 0 || _selectedSlot >= _slotKeys.Count)
            {
                WheelHintText.Text = "Drag a tool from the list onto any slot.";
                return;
            }

            QuickToolEntry entry = FindTool(_slotKeys[_selectedSlot]);
            string slotName = "Slot " + (_selectedSlot + 1).ToString(CultureInfo.InvariantCulture);

            WheelHintText.Text = entry != null
                ? slotName + ": " + entry.DisplayName
                : slotName + " is empty - drop a tool on it.";
        }

        private static Brush MakeBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Search hit test. The label is matched first; a Revit command also matches its own
        /// unspaced name, so typing "visibilitygraphics" finds "Visibility Graphics".
        /// </summary>
        private static bool Matches(QuickToolEntry entry, string needle)
        {
            if (entry.ListLabel.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return !string.IsNullOrEmpty(entry.ItemName) &&
                   entry.ItemName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private QuickToolEntry FindTool(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            foreach (QuickToolEntry entry in _allTools)
            {
                if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private string SelectedToolKey()
        {
            var item = ToolList.SelectedItem as ListBoxItem;
            var entry = item != null ? item.Tag as QuickToolEntry : null;
            return entry != null ? entry.Key : null;
        }

        private static int NearestIndex(int[] choices, int value)
        {
            int bestIndex = 0;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < choices.Length; i++)
            {
                int distance = Math.Abs(choices[i] - value);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        #endregion

        #region Events

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            FillToolList(SearchBox.Text);
            UpdateButtonStates();
        }

        private void OnSourceFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            FillToolList(SearchBox.Text);
            UpdateButtonStates();
        }

        private void OnSlotCountChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            int wanted = ChoiceValue(SlotCountCombo, QuickMenuConfig.DefaultSlotCount);

            while (_slotKeys.Count < wanted)
            {
                _slotKeys.Add(string.Empty);
            }

            while (_slotKeys.Count > wanted)
            {
                _slotKeys.RemoveAt(_slotKeys.Count - 1);
            }

            RefreshWheel(_selectedSlot);
            UpdateButtonStates();
        }

        private void OnWheelSizeChanged(object sender, SelectionChangedEventArgs e)
        {
            // Nothing to redraw here - the value is read straight off the combo when saving.
        }

        private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonStates();
        }

        private void OnToolListDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            AssignSelectedTool();
        }

        private void OnAssignClick(object sender, RoutedEventArgs e)
        {
            AssignSelectedTool();
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            int slot = _selectedSlot;
            if (slot < 0 || slot >= _slotKeys.Count)
            {
                return;
            }

            _slotKeys[slot] = string.Empty;
            RefreshWheel(slot);
            UpdateButtonStates();
        }

        private void OnMoveUpClick(object sender, RoutedEventArgs e)
        {
            SwapSlot(-1);
        }

        private void OnMoveDownClick(object sender, RoutedEventArgs e)
        {
            SwapSlot(1);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            // Belt and braces - the button is already disabled in this case.
            if (_layoutUnreadable)
            {
                return;
            }

            QuickMenuConfig config = QuickMenuConfig.Load();
            config.SlotCount = _slotKeys.Count;
            config.Diameter = ChoiceValue(WheelSizeCombo, QuickMenuConfig.DefaultDiameter);
            config.Style = ChosenStyle();
            config.ShowNames = ChosenShowNames();
            config.Opening = ChosenOpening();
            config.Normalize();

            for (int i = 0; i < config.Slots.Count && i < _slotKeys.Count; i++)
            {
                config.Slots[i] = _slotKeys[i];
            }

            if (!config.Save())
            {
                // Keep the window - and everything arranged in it - open, so nothing is lost.
                ShowError("The layout could not be saved - the settings file may be locked or read-only. " +
                          "Your arrangement is still here; try Save again.");
                return;
            }

            DialogResult = true;
        }

        /// <summary>
        /// Opens the real wheel with the choices and slots as they are in this window - saved or not - so the
        /// look can be judged before saving. Whatever is clicked on it, nothing runs and nothing is saved.
        /// </summary>
        private void OnPreviewClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var slots = new List<QuickToolEntry>(_slotKeys.Count);
                foreach (string key in _slotKeys)
                {
                    slots.Add(FindTool(key));
                }

                var wheel = new QuickMenuWindow(slots, null,
                    ChoiceValue(WheelSizeCombo, QuickMenuConfig.DefaultDiameter),
                    ChosenStyle(), ChosenShowNames(), ChosenOpening())
                {
                    Owner = this
                };
                wheel.ShowDialog();
            }
            catch (Exception ex)
            {
                ShowError("The preview could not open: " + ex.Message);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        #endregion

        #region Editing the slots

        private void AssignSelectedTool()
        {
            var item = ToolList.SelectedItem as ListBoxItem;
            var entry = item != null ? item.Tag as QuickToolEntry : null;
            int slot = _selectedSlot;

            if (entry == null || slot < 0 || slot >= _slotKeys.Count)
            {
                return;
            }

            _slotKeys[slot] = entry.Key;

            // Step to the next slot so filling the wheel is one click per tool, right round it.
            int next = slot + 1 < _slotKeys.Count ? slot + 1 : slot;
            RefreshWheel(next);
            UpdateButtonStates();
        }

        private void SwapSlot(int direction)
        {
            int slot = _selectedSlot;
            int target = slot + direction;

            if (slot < 0 || slot >= _slotKeys.Count || target < 0 || target >= _slotKeys.Count)
            {
                return;
            }

            string moved = _slotKeys[slot];
            _slotKeys[slot] = _slotKeys[target];
            _slotKeys[target] = moved;

            RefreshWheel(target);
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            int slot = _selectedSlot;
            bool slotChosen = slot >= 0 && slot < _slotKeys.Count;
            bool toolChosen = SelectedToolKey() != null;

            AssignButton.IsEnabled = slotChosen && toolChosen;
            ClearButton.IsEnabled = slotChosen && !string.IsNullOrEmpty(_slotKeys[slot]);
            MoveUpButton.IsEnabled = slotChosen && slot > 0;
            MoveDownButton.IsEnabled = slotChosen && slot < _slotKeys.Count - 1;
        }

        private static int ChoiceValue(ComboBox combo, int fallback)
        {
            var item = combo.SelectedItem as ComboBoxItem;
            if (item == null || item.Tag == null)
            {
                return fallback;
            }

            try
            {
                return Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        #endregion

        #region Drag and drop

        // Three drags are supported, and they are all the same two steps - pick something up, decide
        // what the wedge under the pointer should become:
        //   tool list -> wedge   fills that slot
        //   wedge     -> wedge   swaps the two slots
        //   wedge     -> list    empties that slot

        private void OnToolListMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragOrigin = e.GetPosition(null);
        }

        private void OnToolListMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || !PastDragThreshold(e))
            {
                return;
            }

            string key = SelectedToolKey();
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            DragDrop.DoDragDrop(ToolList, new DataObject(ToolKeyFormat, key), DragDropEffects.Copy);
        }

        private void OnWheelMouseDown(object sender, MouseButtonEventArgs e)
        {
            int index = IndexAt(e.GetPosition(WheelCanvas));

            _dragOrigin = e.GetPosition(null);
            _dragFromSlot = index;

            if (index >= 0)
            {
                RefreshWheel(index);
                UpdateButtonStates();
            }
        }

        private void OnWheelMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragFromSlot < 0 || !PastDragThreshold(e))
            {
                return;
            }

            int from = _dragFromSlot;

            // An empty slot has nothing to pick up.
            if (from >= _slotKeys.Count || string.IsNullOrEmpty(_slotKeys[from]))
            {
                return;
            }

            // Cleared before the call, because DoDragDrop blocks until the drop finishes and the
            // mouse-up that would normally reset this never reaches us.
            _dragFromSlot = -1;

            DragDrop.DoDragDrop(WheelCanvas, new DataObject(SlotIndexFormat, from), DragDropEffects.Move);
        }

        private void OnWheelMouseLeave(object sender, MouseEventArgs e)
        {
            _dragFromSlot = -1;
        }

        private void OnWheelDragOver(object sender, DragEventArgs e)
        {
            int index = IndexAt(e.GetPosition(WheelCanvas));
            bool carryingTool = e.Data.GetDataPresent(ToolKeyFormat);
            bool carryingSlot = e.Data.GetDataPresent(SlotIndexFormat);

            if (index < 0 || (!carryingTool && !carryingSlot))
            {
                e.Effects = DragDropEffects.None;
            }
            else
            {
                e.Effects = carryingTool ? DragDropEffects.Copy : DragDropEffects.Move;
            }

            if (index != _dropSlot)
            {
                _dropSlot = index;
                UpdateWheelVisuals();
            }

            e.Handled = true;
        }

        private void OnWheelDragLeave(object sender, DragEventArgs e)
        {
            if (_dropSlot != -1)
            {
                _dropSlot = -1;
                UpdateWheelVisuals();
            }
        }

        private void OnWheelDrop(object sender, DragEventArgs e)
        {
            int index = IndexAt(e.GetPosition(WheelCanvas));

            _dropSlot = -1;
            _dragFromSlot = -1;

            if (index < 0 || index >= _slotKeys.Count)
            {
                UpdateWheelVisuals();
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(ToolKeyFormat))
            {
                var key = e.Data.GetData(ToolKeyFormat) as string;
                if (!string.IsNullOrEmpty(key))
                {
                    _slotKeys[index] = key;
                }
            }
            else if (e.Data.GetDataPresent(SlotIndexFormat))
            {
                int from = SlotIndexFrom(e.Data);
                if (from >= 0 && from < _slotKeys.Count && from != index)
                {
                    string moved = _slotKeys[from];
                    _slotKeys[from] = _slotKeys[index];
                    _slotKeys[index] = moved;
                }
            }

            RefreshWheel(index);
            UpdateButtonStates();
            e.Handled = true;
        }

        private void OnToolListDragOver(object sender, DragEventArgs e)
        {
            // Only a slot can be dropped here, and doing so empties it. A tool dragged back to its
            // own list is a cancelled drag, not an instruction.
            e.Effects = e.Data.GetDataPresent(SlotIndexFormat)
                ? DragDropEffects.Move
                : DragDropEffects.None;

            e.Handled = true;
        }

        private void OnToolListDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(SlotIndexFormat))
            {
                return;
            }

            int from = SlotIndexFrom(e.Data);
            if (from >= 0 && from < _slotKeys.Count)
            {
                _slotKeys[from] = string.Empty;
                RefreshWheel(from);
                UpdateButtonStates();
            }

            e.Handled = true;
        }

        private bool PastDragThreshold(MouseEventArgs e)
        {
            Vector moved = e.GetPosition(null) - _dragOrigin;

            return Math.Abs(moved.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                   Math.Abs(moved.Y) >= SystemParameters.MinimumVerticalDragDistance;
        }

        private static int SlotIndexFrom(IDataObject data)
        {
            object raw = data.GetData(SlotIndexFormat);
            return raw is int ? (int)raw : -1;
        }

        #endregion
    }
}
