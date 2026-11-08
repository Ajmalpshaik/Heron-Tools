// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The radial tool wheel itself - the video-game style ring that opens centred on the mouse pointer, lights up the wedge the pointer is pointing at, and hands the chosen tool back to CmdQuickMenu to run.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using HeronTools.Services.QuickMenu;

namespace HeronTools.UI.QuickMenu
{
    /// <summary>The Heron Tools quick-tool wheel, opened at the mouse pointer.</summary>
    public partial class QuickMenuWindow : Window
    {
        /// <summary>Blank space left between two wedges, in degrees, per side.</summary>
        private const double WedgeGapDegrees = 1.6;

        /// <summary>Hole in the middle, as a fraction of the outer radius.</summary>
        private const double HubRadiusFraction = 0.42;

        /// <summary>Breathing room between the ring and the window edge, in WPF units.</summary>
        private const double EdgePadding = 16.0;

        /// <summary>
        /// Half the tallest a slot's icon-plus-label block gets (34 icon + 5 gap + two 17pt lines),
        /// used to work out how wide that block may be before its lower corners leave the wedge.
        /// </summary>
        private const double EstimatedContentHalfHeight = 44.0;

        // Heron brand only: Surface #1B1B1F and Ink #0F0F10 for the ring, Mist #F7F5F2 at an opacity for
        // text and lines, Accent #FD641A for the slot being pointed at.
        private static readonly Brush WedgeFill = MakeBrush(0xF2, 0x1B, 0x1B, 0x1F);            // Surface 95 %
        private static readonly Brush WedgeFillEmpty = MakeBrush(0xD9, 0x0F, 0x0F, 0x10);       // Ink 85 %
        private static readonly Brush WedgeFillUnavailable = MakeBrush(0xE6, 0x1B, 0x1B, 0x1F); // Surface 90 %
        private static readonly Brush WedgeStroke = MakeBrush(0x24, 0xF7, 0xF5, 0xF2);          // Mist 14 %
        private static readonly Brush WedgeStrokeHover = MakeBrush(0xFF, 0xFD, 0x64, 0x1A);     // Accent
        private static readonly Brush WedgeStrokeHoverDisabled = MakeBrush(0x47, 0xF7, 0xF5, 0xF2); // Mist 28 %
        private static readonly Brush HubFill = MakeBrush(0xFA, 0x0F, 0x0F, 0x10);              // Ink 98 %
        private static readonly Brush HubStroke = MakeBrush(0x24, 0xF7, 0xF5, 0xF2);            // Mist 14 %
        private static readonly Brush TextPrimary = MakeBrush(0xFF, 0xFF, 0xFF, 0xFF);          // White on the lit slot
        private static readonly Brush TextIdle = MakeBrush(0xDB, 0xF7, 0xF5, 0xF2);             // Mist 86 %
        private static readonly Brush TextSecondary = MakeBrush(0xB8, 0xF7, 0xF5, 0xF2);        // Mist 72 %
        private static readonly Brush TextDisabled = MakeBrush(0x73, 0xF7, 0xF5, 0xF2);         // Mist 45 %
        private static readonly Brush TextAccent = MakeBrush(0xFF, 0xFD, 0x64, 0x1A);           // Accent

        /// <summary>The heron mark (designer's kit, 1080 x 1071 units), drawn small in the hub.</summary>
        private const string HeronMarkData =
            "M811.368 1070.84C811.475 1070.9 811.583 1070.95 811.691 1071C711.874 1049.97 610.779 1028.5 519.57 982.796C423.923 934.858 329.769 852.648 310.05 743.375C300.796 692.115 306.069 638.823 324.725 590.199C360.49 496.987 439.324 428.738 488.284 342.748C505.582 312.377 534.352 262.959 517.647 226.912C483.711 153.646 346.286 236.556 264.506 291.677L0 516.517L219.904 256.718C219.904 256.718 240.86 142.765 376.294 62.6933C434.024 28.5556 497.35 28.1118 566.203 26.9147C643.921 25.5696 694.509 39.2354 782.839 0C782.839 0 758.682 53.2644 573.252 52.9416C459.917 52.7398 425.026 104.363 424.945 104.498C428.765 98.8351 445.995 95.1496 451.9 93.2262C473.475 86.1915 496.449 82.7885 519.126 84.4967C588.088 89.7021 647.069 143.747 646.948 214.591C646.786 302.437 593.145 382.576 538.562 447.044C475.318 521.722 433.567 610.792 443.924 710.999C453.259 801.293 503.9 886.503 578.766 937.844C526.766 856.925 493.167 756.624 520.323 664.366C545.974 577.206 620.154 513.746 695.101 462.378C770.047 411.01 851.477 364.498 905.67 291.582C931.818 256.422 951.214 211.807 940.426 169.33C957.979 238.426 960.965 302.155 937.857 370.847C919.349 425.86 888.13 476.461 846.864 517.324C802.759 560.998 751.647 598.767 715.317 649.732C620.1 783.31 664.702 996.098 811.368 1070.84Z " +
            "M824.899 1057.76C783.928 1015.83 749.575 970.529 731.7 914.05C715.478 862.83 712.102 806.27 728.162 754.566C729.978 748.728 732.022 742.985 734.336 737.322C783.861 615.903 912.852 553.896 965.404 434.401C1000.43 354.76 1012.64 262.395 984.786 178.691C992.198 194.711 1024.09 236.072 1033.63 250.988C1076.29 317.69 1092.73 386.799 1069.3 466.965C1036.93 577.704 943.977 643.598 881.392 734.834C840.73 794.124 816.021 861.35 815.672 933.688C815.483 973.461 821.2 1043.67 824.899 1057.74V1057.76Z";

        private readonly IList<QuickToolEntry> _slots;
        private readonly int _slotCount;
        private readonly double _outerRadius;
        private readonly double _innerRadius;
        private readonly double _centre;
        private readonly double _stepDegrees;

        private readonly List<Path> _wedges = new List<Path>();

        // One Accent layer per wedge, shown only while the pointer is on that slot. The wedge itself keeps
        // its Surface fill underneath, so the lit slot reads the same over a light or a dark Revit canvas.
        private readonly List<Path> _hoverLayers = new List<Path>();

        /// <summary>
        /// The lit slot's fill: Accent that grows from faint at the hub to stronger at the rim, so the slot
        /// glows outwards instead of turning flat brown. Built per wheel because it is centred on this ring.
        /// </summary>
        private Brush _hoverFill;
        private readonly List<TextBlock> _labels = new List<TextBlock>();
        private readonly bool[] _available;

        private TextBlock _hubToolText;
        private TextBlock _hubHintText;
        private int _hoverIndex = -1;
        private bool _choiceMade;
        private bool _autoCloseArmed;

        /// <summary>The tool Ajmal picked, or null if he closed the wheel without choosing.</summary>
        internal QuickToolEntry SelectedEntry { get; private set; }

        /// <summary>True when he asked for the customise window instead of running a tool.</summary>
        internal bool OpenSettingsRequested { get; private set; }

        /// <summary>
        /// Builds the wheel. <paramref name="slots"/> holds one entry per wedge, clockwise from the
        /// top, with null for an empty slot. <paramref name="available"/> is the matching list of
        /// "would the ribbon let this be clicked right now" flags; a shorter or missing list simply
        /// means every slot is treated as available.
        /// </summary>
        internal QuickMenuWindow(IList<QuickToolEntry> slots, IList<bool> available, int diameter)
        {
            InitializeComponent();

            // A wheel with no slots at all cannot be drawn or aimed at, so an empty list becomes a
            // single empty wedge rather than an index that does not exist.
            _slots = slots != null && slots.Count > 0
                ? slots
                : new List<QuickToolEntry> { null };
            _slotCount = _slots.Count;
            _stepDegrees = 360.0 / _slotCount;

            _available = new bool[_slotCount];
            for (int i = 0; i < _slotCount; i++)
            {
                _available[i] = available == null || i >= available.Count || available[i];
            }

            double size = diameter + (EdgePadding * 2.0);
            Width = size;
            Height = size;
            _centre = size / 2.0;
            _outerRadius = diameter / 2.0;
            _innerRadius = _outerRadius * HubRadiusFraction;

            Cursor = Cursors.Arrow;

            BuildWheel();

            Loaded += OnWheelLoaded;
            MouseMove += OnWheelMouseMove;
            MouseLeftButtonUp += OnWheelLeftButtonUp;
            MouseRightButtonUp += OnWheelRightButtonUp;
            PreviewKeyDown += OnWheelKeyDown;
            Deactivated += OnWheelDeactivated;
        }

        #region Placement

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            PlaceOnPointer();
        }

        /// <summary>Centres the window on the mouse pointer, kept fully on screen.</summary>
        private void PlaceOnPointer()
        {
            try
            {
                QuickMenuNative.NativePoint cursor;
                if (!QuickMenuNative.GetCursorPos(out cursor))
                {
                    return;
                }

                // Screen pixels -> WPF units, so 125%/150% displays still land on the pointer.
                var pointer = new Point(cursor.X, cursor.Y);
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source != null && source.CompositionTarget != null)
                {
                    pointer = source.CompositionTarget.TransformFromDevice.Transform(pointer);
                }

                double left = pointer.X - (Width / 2.0);
                double top = pointer.Y - (Height / 2.0);

                double minLeft = SystemParameters.VirtualScreenLeft;
                double minTop = SystemParameters.VirtualScreenTop;
                double maxLeft = minLeft + SystemParameters.VirtualScreenWidth - Width;
                double maxTop = minTop + SystemParameters.VirtualScreenHeight - Height;

                Left = maxLeft > minLeft ? Math.Min(Math.Max(left, minLeft), maxLeft) : minLeft;
                Top = maxTop > minTop ? Math.Min(Math.Max(top, minTop), maxTop) : minTop;
            }
            catch (Exception)
            {
                // Placement is cosmetic - a wheel in the default position still works.
            }
        }

        private void OnWheelLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnWheelLoaded;
            Activate();
            Focus();
            Keyboard.Focus(this);
            PlayPopIn();
        }

        /// <summary>
        /// The scale-only pop. There is deliberately no opacity fade: this window is see-through, so
        /// WPF redraws it on the CPU, and fading the whole ring was re-rasterising every element on
        /// every frame just to open. Arming close-on-lose-focus when the pop finishes is the point
        /// at which the wheel is genuinely up and a real focus change means the user left.
        /// </summary>
        private void PlayPopIn()
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(90));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var grow = new DoubleAnimation(0.88, 1.0, duration) { EasingFunction = ease };
            grow.Completed += OnPopInCompleted;

            PopScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            PopScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }

        private void OnPopInCompleted(object sender, EventArgs e)
        {
            _autoCloseArmed = true;
        }

        #endregion

        #region Drawing

        private void BuildWheel()
        {
            WheelCanvas.Children.Clear();
            _wedges.Clear();
            _hoverLayers.Clear();
            _labels.Clear();

            _hoverFill = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = new Point(_centre, _centre),
                GradientOrigin = new Point(_centre, _centre),
                RadiusX = _outerRadius,
                RadiusY = _outerRadius,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x1F, 0xFD, 0x64, 0x1A), HubRadiusFraction),   // Accent 12 %
                    new GradientStop(Color.FromArgb(0x66, 0xFD, 0x64, 0x1A), 1.0)                  // Accent 40 %
                }
            };
            _hoverFill.Freeze();

            AddHalo();

            for (int i = 0; i < _slotCount; i++)
            {
                AddWedge(i);
            }

            AddHub();
        }

        private void AddWedge(int index)
        {
            QuickToolEntry entry = _slots[index];
            bool filled = entry != null;
            bool usable = IsUsable(index);

            double centreAngle = -90.0 + (index * _stepDegrees);
            double startAngle = centreAngle - (_stepDegrees / 2.0) + WedgeGapDegrees;
            double endAngle = centreAngle + (_stepDegrees / 2.0) - WedgeGapDegrees;

            var wedge = new Path
            {
                Data = BuildWedgeGeometry(startAngle, endAngle),
                Fill = IdleFill(index),
                Stroke = WedgeStroke,
                StrokeThickness = 1.0,
                IsHitTestVisible = false
            };

            WheelCanvas.Children.Add(wedge);
            _wedges.Add(wedge);

            var hoverLayer = new Path
            {
                Data = wedge.Data,
                Fill = _hoverFill,
                Stroke = WedgeStrokeHover,
                StrokeThickness = 2.0,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            WheelCanvas.Children.Add(hoverLayer);
            _hoverLayers.Add(hoverLayer);

            // Icon + label, sitting halfway between the hub and the rim.
            double contentRadius = (_innerRadius + _outerRadius) / 2.0;
            Point contentCentre = Polar(contentRadius, centreAngle);

            // Fit the icon + label inside the wedge. The tight spot is the INNER corners of the text
            // block, not its middle: a wedge narrows towards the hub, so a block measured against
            // the wedge's width at the content radius still spills over the divider lower down.
            // Measure the allowance at the inner corners instead, and never let the block be wider
            // than the ring is thick (that is what bites on a 4-slot wheel, where the wedge is huge
            // but the ring is not). The ring also gives up ~22 px at each edge, so on a side wedge the
            // label never runs into the slot number by the rim.
            double halfWedgeRadians = ((_stepDegrees / 2.0) - WedgeGapDegrees) * Math.PI / 180.0;
            double innerCornerRadius = Math.Max(_innerRadius + 6.0, contentRadius - EstimatedContentHalfHeight);
            double angularAllowance = 2.0 * innerCornerRadius * Math.Tan(Math.Min(halfWedgeRadians, 1.2));
            double radialAllowance = (_outerRadius - _innerRadius) - 44.0;
            double contentWidth = Math.Max(60.0, Math.Min(angularAllowance, radialAllowance));

            var stack = new StackPanel
            {
                Width = contentWidth,
                IsHitTestVisible = false
            };

            if (filled && entry.Icon != null)
            {
                stack.Children.Add(new Image
                {
                    Source = entry.Icon,
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness(0, 0, 0, 6),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    SnapsToDevicePixels = true,

                    // Dimmed the way the ribbon dims a greyed button's icon.
                    Opacity = usable ? 1.0 : 0.35
                });
            }

            var label = new TextBlock
            {
                Text = filled ? entry.DisplayName : "Empty",
                Foreground = usable ? TextIdle : TextDisabled,
                FontSize = 12,
                FontWeight = filled ? FontWeights.SemiBold : FontWeights.Normal,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.WrapWithOverflow,   // break between words, never inside one
                MaxHeight = 46,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            stack.Children.Add(label);
            _labels.Add(label);

            stack.Measure(new Size(stack.Width, double.PositiveInfinity));
            Canvas.SetLeft(stack, contentCentre.X - (stack.Width / 2.0));
            Canvas.SetTop(stack, contentCentre.Y - (stack.DesiredSize.Height / 2.0));
            WheelCanvas.Children.Add(stack);

            // Number key hint, just inside the rim - 1 to 9 only, since those are the keys that work.
            if (index < 9)
            {
                var digit = new TextBlock
                {
                    Text = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Foreground = TextDisabled,
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    Width = 16,
                    IsHitTestVisible = false
                };

                Point digitCentre = Polar(_outerRadius - 15.0, centreAngle);
                digit.Measure(new Size(16, double.PositiveInfinity));
                Canvas.SetLeft(digit, digitCentre.X - 8.0);
                Canvas.SetTop(digit, digitCentre.Y - (digit.DesiredSize.Height / 2.0));
                WheelCanvas.Children.Add(digit);
            }
        }

        private Geometry BuildWedgeGeometry(double startAngle, double endAngle)
        {
            bool isLargeArc = (endAngle - startAngle) > 180.0;

            var figure = new PathFigure
            {
                StartPoint = Polar(_outerRadius, startAngle),
                IsClosed = true,
                IsFilled = true
            };

            figure.Segments.Add(new ArcSegment(
                Polar(_outerRadius, endAngle),
                new Size(_outerRadius, _outerRadius),
                0,
                isLargeArc,
                SweepDirection.Clockwise,
                true));

            figure.Segments.Add(new LineSegment(Polar(_innerRadius, endAngle), true));

            figure.Segments.Add(new ArcSegment(
                Polar(_innerRadius, startAngle),
                new Size(_innerRadius, _innerRadius),
                0,
                isLargeArc,
                SweepDirection.Counterclockwise,
                true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();
            return geometry;
        }

        private void AddHub()
        {
            var hub = new Ellipse
            {
                Width = _innerRadius * 2.0,
                Height = _innerRadius * 2.0,
                Fill = HubFill,
                Stroke = HubStroke,
                StrokeThickness = 1.0,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(hub, _centre - _innerRadius);
            Canvas.SetTop(hub, _centre - _innerRadius);
            WheelCanvas.Children.Add(hub);

            var stack = new StackPanel
            {
                Width = (_innerRadius * 2.0) - 22.0,
                IsHitTestVisible = false
            };

            var mark = Geometry.Parse(HeronMarkData);
            mark.Freeze();
            stack.Children.Add(new Viewbox
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new Path { Data = mark, Fill = TextSecondary, Stretch = Stretch.Uniform }
            });

            _hubToolText = new TextBlock
            {
                Text = "Point at a tool",
                Foreground = TextAccent,
                FontSize = 15,
                FontWeight = FontWeights.ExtraBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 60,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(_hubToolText);

            _hubHintText = new TextBlock
            {
                Text = "Click to run  ·  S settings  ·  Esc close",
                Foreground = TextDisabled,
                FontSize = 10.5,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(_hubHintText);

            stack.Measure(new Size(stack.Width, double.PositiveInfinity));
            Canvas.SetLeft(stack, _centre - (stack.Width / 2.0));
            Canvas.SetTop(stack, _centre - (stack.DesiredSize.Height / 2.0));
            WheelCanvas.Children.Add(stack);
        }

        /// <summary>
        /// A soft Ink halo just outside the ring, so the wheel stays readable over a white sheet as well as a
        /// dark 3D view. A static gradient brush (no blur effect: this window is drawn by the CPU).
        /// </summary>
        private void AddHalo()
        {
            double radius = _outerRadius + EdgePadding - 2.0;
            var halo = new Ellipse
            {
                Width = radius * 2.0,
                Height = radius * 2.0,
                IsHitTestVisible = false,
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0xD9, 0x0F, 0x0F, 0x10), 0.0),          // Ink 85 %
                        new GradientStop(Color.FromArgb(0xD9, 0x0F, 0x0F, 0x10), _outerRadius / radius - 0.005),
                        new GradientStop(Color.FromArgb(0x00, 0x0F, 0x0F, 0x10), 1.0)
                    }
                }
            };
            halo.Fill.Freeze();
            Canvas.SetLeft(halo, _centre - radius);
            Canvas.SetTop(halo, _centre - radius);
            WheelCanvas.Children.Add(halo);
        }

        private Point Polar(double radius, double angleDegrees)
        {
            double radians = angleDegrees * Math.PI / 180.0;
            return new Point(
                _centre + (radius * Math.Cos(radians)),
                _centre + (radius * Math.Sin(radians)));
        }

        #endregion

        #region Aiming and choosing

        private void OnWheelMouseMove(object sender, MouseEventArgs e)
        {
            SetHover(IndexAt(e.GetPosition(WheelCanvas)));
        }

        /// <summary>Which wedge the pointer is aiming at, or -1 for the hub / outside the ring.</summary>
        private int IndexAt(Point point)
        {
            double dx = point.X - _centre;
            double dy = point.Y - _centre;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));

            if (distance < _innerRadius || distance > _outerRadius)
            {
                return -1;
            }

            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            double shifted = angle + 90.0 + (_stepDegrees / 2.0);
            shifted = ((shifted % 360.0) + 360.0) % 360.0;

            int index = (int)(shifted / _stepDegrees);
            return index >= _slotCount ? _slotCount - 1 : index;
        }

        private void SetHover(int index)
        {
            if (index == _hoverIndex)
            {
                return;
            }

            if (_hoverIndex >= 0 && _hoverIndex < _wedges.Count)
            {
                Path previous = _wedges[_hoverIndex];
                previous.Fill = IdleFill(_hoverIndex);
                previous.Stroke = WedgeStroke;
                previous.StrokeThickness = 1.0;
                _hoverLayers[_hoverIndex].Visibility = Visibility.Collapsed;
                _labels[_hoverIndex].Foreground =
                    IsUsable(_hoverIndex) ? TextIdle : TextDisabled;
            }

            _hoverIndex = index;

            if (_hoverIndex >= 0 && _hoverIndex < _wedges.Count)
            {
                // A slot the ribbon would grey out does not light up - it only acknowledges the
                // pointer, the same as hovering a greyed ribbon button.
                bool usable = IsUsable(_hoverIndex);
                Path current = _wedges[_hoverIndex];
                current.Fill = IdleFill(_hoverIndex);
                current.Stroke = usable ? WedgeStroke : WedgeStrokeHoverDisabled;
                current.StrokeThickness = usable ? 1.0 : 1.4;
                _hoverLayers[_hoverIndex].Visibility = usable ? Visibility.Visible : Visibility.Collapsed;
                _labels[_hoverIndex].Foreground = usable ? TextPrimary : TextDisabled;
            }

            UpdateHubText();
        }

        /// <summary>True when this slot holds a tool that could be clicked on the ribbon right now.</summary>
        private bool IsUsable(int index)
        {
            return index >= 0 &&
                   index < _slotCount &&
                   _slots[index] != null &&
                   _available[index];
        }

        /// <summary>The wedge colour when the pointer is not on it: filled, greyed out, or empty.</summary>
        private Brush IdleFill(int index)
        {
            if (index < 0 || index >= _slotCount || _slots[index] == null)
            {
                return WedgeFillEmpty;
            }

            return _available[index] ? WedgeFill : WedgeFillUnavailable;
        }

        private void UpdateHubText()
        {
            if (_hubToolText == null || _hubHintText == null)
            {
                return;
            }

            QuickToolEntry entry = _hoverIndex >= 0 && _hoverIndex < _slots.Count
                ? _slots[_hoverIndex]
                : null;

            if (entry != null)
            {
                _hubToolText.Text = entry.DisplayName;

                if (IsUsable(_hoverIndex))
                {
                    _hubToolText.Foreground = TextAccent;
                    _hubHintText.Text = "Click to run  ·  Esc close";
                }
                else
                {
                    _hubToolText.Foreground = TextDisabled;
                    _hubHintText.Text = "Not available here - it is greyed out on the ribbon too";
                }

                return;
            }

            if (_hoverIndex >= 0)
            {
                _hubToolText.Text = "Empty slot";
                _hubToolText.Foreground = TextDisabled;
                _hubHintText.Text = "Press S to put a tool here";
                return;
            }

            _hubToolText.Text = "Point at a tool";
            _hubToolText.Foreground = TextAccent;
            _hubHintText.Text = "Click to run  ·  S settings  ·  Esc close";
        }

        private void OnWheelLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            Choose(IndexAt(e.GetPosition(WheelCanvas)));
        }

        private void OnWheelRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            CloseWithoutChoice();
        }

        private void OnWheelKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseWithoutChoice();
                return;
            }

            if (e.Key == Key.S)
            {
                e.Handled = true;
                OpenSettingsRequested = true;
                CloseWithoutChoice();
                return;
            }

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                e.Handled = true;

                // Nothing aimed at yet - stay open rather than closing without running anything.
                if (_hoverIndex >= 0)
                {
                    Choose(_hoverIndex);
                }

                return;
            }

            int slot = SlotFromKey(e.Key);
            if (slot >= 0)
            {
                e.Handled = true;

                // A number bigger than this wheel has slots is a mistyped key, not "close".
                if (slot < _slotCount)
                {
                    Choose(slot);
                }
            }
        }

        private static int SlotFromKey(Key key)
        {
            if (key >= Key.D1 && key <= Key.D9)
            {
                return key - Key.D1;
            }

            if (key >= Key.NumPad1 && key <= Key.NumPad9)
            {
                return key - Key.NumPad1;
            }

            return -1;
        }

        private void Choose(int index)
        {
            if (index < 0 || index >= _slots.Count)
            {
                // Aimed at the hub or outside the ring - same as a game, that means "never mind".
                CloseWithoutChoice();
                return;
            }

            QuickToolEntry entry = _slots[index];
            if (entry == null)
            {
                // An empty slot is not a mistake worth a popup - just show what to do and stay open.
                SetHover(index);
                return;
            }

            if (!_available[index])
            {
                // Clicking a greyed ribbon button does nothing and leaves the ribbon open. Same here:
                // point the hub at the reason rather than closing or posting a command Revit refuses.
                SetHover(index);
                return;
            }

            if (_choiceMade)
            {
                return;
            }

            _choiceMade = true;
            SelectedEntry = entry;
            SetDialogResultSafely(true);
        }

        private void CloseWithoutChoice()
        {
            if (_choiceMade)
            {
                return;
            }

            _choiceMade = true;
            SelectedEntry = null;
            SetDialogResultSafely(false);
        }

        /// <summary>
        /// Setting DialogResult on a window that has already closed throws, and the wheel can be
        /// closed from three places at once (choice, Esc, losing focus). One guarded writer.
        /// </summary>
        private void SetDialogResultSafely(bool result)
        {
            try
            {
                DialogResult = result;
            }
            catch (InvalidOperationException)
            {
                try
                {
                    Close();
                }
                catch (Exception)
                {
                }
            }
        }

        private void OnWheelDeactivated(object sender, EventArgs e)
        {
            // Anything that takes focus away (Alt+Tab, a click that reaches Revit) closes the wheel
            // rather than leaving a floating ring behind.
            if (_autoCloseArmed && !_choiceMade)
            {
                CloseWithoutChoice();
            }
        }

        #endregion

        private static Brush MakeBrush(byte alpha, byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }

        /// <summary>Minimal user32 interop - only to find out where the mouse pointer is.</summary>
        internal static class QuickMenuNative
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct NativePoint
            {
                public int X;
                public int Y;
            }

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetCursorPos(out NativePoint point);
        }
    }
}
