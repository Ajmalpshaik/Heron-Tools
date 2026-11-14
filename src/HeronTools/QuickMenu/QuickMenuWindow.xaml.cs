// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The radial tool wheel itself - the video-game style ring that opens centred on the mouse pointer, lights up the slot the pointer is pointing at, and hands the chosen tool back to CmdQuickMenu to run. Draws any of the six designs (QuickMenuStyle), with or without tool names, and opens in any of the four ways (QuickMenuOpening).

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using HeronTools.Services.QuickMenu;

namespace HeronTools.UI.QuickMenu
{
    /// <summary>The Heron Tools quick-tool wheel, opened at the mouse pointer.</summary>
    public partial class QuickMenuWindow : Window
    {
        /// <summary>Blank space left between two slots, in degrees, per side.</summary>
        private const double WedgeGapDegrees = 1.6;

        /// <summary>Breathing room between the wheel and the window edge, in WPF units.</summary>
        private const double EdgePadding = 16.0;

        /// <summary>
        /// The designs were drawn on a wheel 170 units in radius; every size below is in those units and is
        /// multiplied by the real radius / 170, so the wheel keeps its proportions at every chosen size.
        /// </summary>
        private const double DesignRadius = 170.0;

        /// <summary>
        /// Half the tallest a slot's icon-plus-label block gets (34 icon + 5 gap + two 17pt lines),
        /// used to work out how wide that block may be before its lower corners leave the slot.
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
        private static readonly Brush RimIdle = MakeBrush(0x1A, 0xF7, 0xF5, 0xF2);              // Mist 10 %
        private static readonly Brush RulerTick = MakeBrush(0x40, 0xF7, 0xF5, 0xF2);            // Mist 25 %
        private static readonly Brush RulerTickMajor = MakeBrush(0xB3, 0xFD, 0x64, 0x1A);       // Accent 70 %
        private static readonly Brush HubFill = MakeBrush(0xFA, 0x0F, 0x0F, 0x10);              // Ink 98 %
        private static readonly Brush HubStroke = MakeBrush(0x24, 0xF7, 0xF5, 0xF2);            // Mist 14 %
        private static readonly Brush HubTick = MakeBrush(0x33, 0xF7, 0xF5, 0xF2);              // Mist 20 %
        private static readonly Brush InkText = MakeBrush(0xFF, 0x0F, 0x0F, 0x10);              // Ink, on Accent
        private static readonly Brush TextPrimary = MakeBrush(0xFF, 0xFF, 0xFF, 0xFF);          // White on the lit slot
        private static readonly Brush TextIdle = MakeBrush(0xDB, 0xF7, 0xF5, 0xF2);             // Mist 86 %
        private static readonly Brush TextSecondary = MakeBrush(0xB8, 0xF7, 0xF5, 0xF2);        // Mist 72 %
        private static readonly Brush TextDisabled = MakeBrush(0x73, 0xF7, 0xF5, 0xF2);         // Mist 45 %
        private static readonly Brush TextAccent = MakeBrush(0xFF, 0xFD, 0x64, 0x1A);           // Accent

        /// <summary>The heron mark (designer's kit, 1080 x 1071 units), drawn in the hub.</summary>
        private const string HeronMarkData =
            "M811.368 1070.84C811.475 1070.9 811.583 1070.95 811.691 1071C711.874 1049.97 610.779 1028.5 519.57 982.796C423.923 934.858 329.769 852.648 310.05 743.375C300.796 692.115 306.069 638.823 324.725 590.199C360.49 496.987 439.324 428.738 488.284 342.748C505.582 312.377 534.352 262.959 517.647 226.912C483.711 153.646 346.286 236.556 264.506 291.677L0 516.517L219.904 256.718C219.904 256.718 240.86 142.765 376.294 62.6933C434.024 28.5556 497.35 28.1118 566.203 26.9147C643.921 25.5696 694.509 39.2354 782.839 0C782.839 0 758.682 53.2644 573.252 52.9416C459.917 52.7398 425.026 104.363 424.945 104.498C428.765 98.8351 445.995 95.1496 451.9 93.2262C473.475 86.1915 496.449 82.7885 519.126 84.4967C588.088 89.7021 647.069 143.747 646.948 214.591C646.786 302.437 593.145 382.576 538.562 447.044C475.318 521.722 433.567 610.792 443.924 710.999C453.259 801.293 503.9 886.503 578.766 937.844C526.766 856.925 493.167 756.624 520.323 664.366C545.974 577.206 620.154 513.746 695.101 462.378C770.047 411.01 851.477 364.498 905.67 291.582C931.818 256.422 951.214 211.807 940.426 169.33C957.979 238.426 960.965 302.155 937.857 370.847C919.349 425.86 888.13 476.461 846.864 517.324C802.759 560.998 751.647 598.767 715.317 649.732C620.1 783.31 664.702 996.098 811.368 1070.84Z " +
            "M824.899 1057.76C783.928 1015.83 749.575 970.529 731.7 914.05C715.478 862.83 712.102 806.27 728.162 754.566C729.978 748.728 732.022 742.985 734.336 737.322C783.861 615.903 912.852 553.896 965.404 434.401C1000.43 354.76 1012.64 262.395 984.786 178.691C992.198 194.711 1024.09 236.072 1033.63 250.988C1076.29 317.69 1092.73 386.799 1069.3 466.965C1036.93 577.704 943.977 643.598 881.392 734.834C840.73 794.124 816.021 861.35 815.672 933.688C815.483 973.461 821.2 1043.67 824.899 1057.74V1057.76Z";

        /// <summary>What the centre of the wheel holds.</summary>
        private enum HubKind
        {
            /// <summary>Heron mark, the pointed tool's name and a hint.</summary>
            Mark,

            /// <summary>Heron mark on top, then the pointed tool's large icon, name and hint.</summary>
            Card,

            /// <summary>As Mark, with a ring of fine ticks inside the hub's edge.</summary>
            Ticks
        }

        /// <summary>Everything drawn for one slot, and the transforms that move it.</summary>
        private sealed class SlotParts
        {
            public Canvas Layer;                // the whole slot: opacity and every transform below
            public Point Middle;                // the slot's centre, for its own scaling
            public double Angle;                // the slot's centre angle, degrees (0 = east, clockwise)
            public Shape Body;                  // wedge or bubble
            public Shape Glow;                  // the Accent layer shown while pointed at
            public Shape RimLight;              // Accent rim light (Double Ring, Rim Lights)
            public FrameworkElement Pill;       // the orange name pill (Orbit Buttons with names)
            public TextBlock Label;
            public ScaleTransform Pop;          // the quick "point at it" pop
            public TranslateTransform Lift;     // the step-out while pointed at
            public ScaleTransform IconGrow;     // the icon grows a touch while pointed at
            public ScaleTransform OpenScale;    // the opening's scale (centre set per opening)
            public RotateTransform OpenTurn;    // the opening's twist, about the wheel centre
            public TranslateTransform OpenMove; // the opening's travel
        }

        private readonly IList<QuickToolEntry> _slots;
        private readonly int _slotCount;
        private readonly double _outerRadius;   // the chosen wheel size, / 2
        private readonly double _centre;
        private readonly double _stepDegrees;
        private readonly double _unit;          // real units per design unit
        private readonly bool[] _available;

        private readonly QuickMenuStyle _style;
        private readonly bool _showNames;
        private readonly QuickMenuOpening _opening;

        // The chosen design, worked out once in the constructor.
        private bool _bubbles;                  // Orbit Buttons: separate bubbles instead of a ring
        private double _ringInner;              // ring: inner radius; bubbles: the centre's radius
        private double _ringOuter;              // ring: outer radius; bubbles: the orbit radius
        private double _bubbleRadius;
        private bool _labelsOnSlots;
        private HubKind _hub;
        private double _hubRadius;
        private double _lift;
        private bool _rimLights;
        private bool _notched;
        private bool _ruler;
        private bool _halo;
        private bool _digits;
        private bool _arc;
        private bool _needle;
        private bool _chevron;
        private double _hitInner;
        private double _hitOuter;

        private readonly List<SlotParts> _parts = new List<SlotParts>();

        // The pieces the opening grows: the hub, and the halo / ruler behind the wheel.
        private Canvas _hubLayer;
        private ScaleTransform _hubGrow;
        private Canvas _backLayer;
        private ScaleTransform _backGrow;

        // The selector: arc, needle and / or chevron, all turning round the centre to point at the slot.
        private readonly List<Shape> _selectorStrokes = new List<Shape>();
        private readonly List<Shape> _selectorFills = new List<Shape>();
        private Canvas _selectorLayer;
        private RotateTransform _selectorTurn;
        private double _selectorAngle;
        private bool _selectorShown;

        /// <summary>The lit slot's fill: Accent growing outwards, centred on this wheel.</summary>
        private Brush _hoverFill;

        private TextBlock _hubToolText;
        private TextBlock _hubHintText;
        private Canvas _hubIconHolder;
        private double _hubIconSize;
        private int _hoverIndex = -1;
        private bool _choiceMade;
        private bool _autoCloseArmed;

        /// <summary>
        /// False while the opening plays: pointing does not light anything until the tools have arrived,
        /// so the arc and a lit slot never show round a wheel that is still coming out. (Keys still work.)
        /// </summary>
        private bool _opened;

        /// <summary>False when Windows' own "Show animations" is off: the wheel then simply appears.</summary>
        private static bool MotionOn
        {
            get { return SystemParameters.ClientAreaAnimation; }
        }

        /// <summary>The tool Ajmal picked, or null if he closed the wheel without choosing.</summary>
        internal QuickToolEntry SelectedEntry { get; private set; }

        /// <summary>True when he asked for the customise window instead of running a tool.</summary>
        internal bool OpenSettingsRequested { get; private set; }

        /// <summary>Builds the default wheel (Heron Ring, names on, Unfold).</summary>
        internal QuickMenuWindow(IList<QuickToolEntry> slots, IList<bool> available, int diameter)
            : this(slots, available, diameter, QuickMenuLook.DefaultStyle, true, QuickMenuLook.DefaultOpening)
        {
        }

        /// <summary>
        /// Builds the wheel. <paramref name="slots"/> holds one entry per slot, clockwise from the
        /// top, with null for an empty slot. <paramref name="available"/> is the matching list of
        /// "would the ribbon let this be clicked right now" flags; a shorter or missing list simply
        /// means every slot is treated as available.
        /// </summary>
        internal QuickMenuWindow(IList<QuickToolEntry> slots, IList<bool> available, int diameter,
                                 QuickMenuStyle style, bool showNames, QuickMenuOpening opening)
        {
            InitializeComponent();

            // A wheel with no slots at all cannot be drawn or aimed at, so an empty list becomes a
            // single empty slot rather than an index that does not exist.
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
            _unit = _outerRadius / DesignRadius;

            _style = style;
            _showNames = showNames;
            _opening = opening;
            SetUpDesign();

            Cursor = Cursors.Arrow;

            BuildWheel();

            Loaded += OnWheelLoaded;
            MouseMove += OnWheelMouseMove;
            MouseLeftButtonUp += OnWheelLeftButtonUp;
            MouseRightButtonUp += OnWheelRightButtonUp;
            PreviewKeyDown += OnWheelKeyDown;
            Deactivated += OnWheelDeactivated;
        }

        /// <summary>The six designs, as chosen on the design page (2026-11-13).</summary>
        private void SetUpDesign()
        {
            double r = _outerRadius;
            _ringInner = r * 0.42;
            _ringOuter = r;
            _labelsOnSlots = _showNames;
            _hub = _showNames ? HubKind.Mark : HubKind.Card;
            _lift = 7.0;
            _halo = true;
            _digits = true;

            switch (_style)
            {
                case QuickMenuStyle.SlimRing:
                    _ringInner = r * 0.72;
                    _labelsOnSlots = false;          // too thin for names: the card shows them
                    _hub = HubKind.Card;
                    _digits = false;
                    _arc = true;
                    _needle = true;
                    break;

                case QuickMenuStyle.DoubleRing:
                    _ringOuter = r * 0.90;
                    _rimLights = true;
                    _lift = 0.0;
                    _needle = true;
                    break;

                case QuickMenuStyle.OrbitButtons:
                    _bubbles = true;
                    _ringInner = r * 0.30;           // the centre
                    _ringOuter = r * 0.66;           // the orbit
                    // 34 design units, but never so big that neighbours touch (11-12 slots).
                    double chordHalf = _ringOuter * Math.Sin(Math.PI / Math.Max(2, _slotCount));
                    _bubbleRadius = Math.Min(34.0 * _unit, chordHalf - 4.0);
                    _labelsOnSlots = false;          // names come in the pill instead
                    _hub = HubKind.Mark;
                    _lift = 12.0;
                    _halo = false;
                    _digits = false;
                    break;

                case QuickMenuStyle.RimLights:
                    _ringOuter = r * 0.92;
                    _rimLights = true;
                    _lift = 0.0;
                    _hub = HubKind.Ticks;
                    break;

                case QuickMenuStyle.HudNotched:
                    _ringOuter = r * 0.93;           // leaves room for the ruler inside the window
                    _notched = true;
                    _ruler = true;
                    _hub = HubKind.Ticks;
                    _halo = false;
                    _chevron = true;
                    break;

                default:                             // Heron Ring
                    _arc = true;
                    _needle = true;
                    break;
            }

            _hubRadius = _ringInner;
            if (_bubbles)
            {
                _hitInner = _hubRadius;
                _hitOuter = _ringOuter + _bubbleRadius + 12.0;
            }
            else
            {
                _hitInner = _ringInner;
                _hitOuter = _ringOuter + (_rimLights ? 10.0 * _unit : 0.0);
            }
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
            PlayOpening();
        }

        #endregion

        #region Opening and closing

        /// <summary>
        /// Every opening starts the same way: a small centre button pops up, holds a moment and grows into
        /// the full centre, with the soft halo (and the HUD ruler) growing behind it. Then the slots come out
        /// in the chosen way - Unfold, Bloom, Spin in or Iris Pop. Only transforms and the slots' opacity
        /// move: this see-through window is drawn by the CPU, so there is no blur or effect.
        /// Close-on-lose-focus is armed once the centre has grown (pointing waits for the last slot).
        /// </summary>
        private void PlayOpening()
        {
            if (!MotionOn)
            {
                _autoCloseArmed = true;
                _opened = true;
                return;
            }

            // When the last slot has landed, pointing comes alive - and lights whatever is under the pointer.
            var landed = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(OpeningLength() + 20) };
            landed.Tick += (s, e) =>
            {
                landed.Stop();
                _opened = true;
                _autoCloseArmed = true;
                if (!_choiceMade && IsLoaded)
                {
                    SetHover(IndexAt(Mouse.GetPosition(WheelCanvas)));
                }
            };
            landed.Start();

            // The centre: small button, a moment, then the full centre.
            var hubScale = new DoubleAnimationUsingKeyFrames { Duration = Ms(480) };
            hubScale.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            hubScale.KeyFrames.Add(new EasingDoubleKeyFrame(0.42, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(134)),
                new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }));
            hubScale.KeyFrames.Add(new EasingDoubleKeyFrame(0.42, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(202))));
            hubScale.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(480)),
                new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }));
            hubScale.Completed += (s, e) => _autoCloseArmed = true;
            _hubGrow.BeginAnimation(ScaleTransform.ScaleXProperty, hubScale);
            _hubGrow.BeginAnimation(ScaleTransform.ScaleYProperty, hubScale);

            // The halo and ruler grow behind it.
            if (_backLayer != null)
            {
                var backScale = new DoubleAnimationUsingKeyFrames { Duration = Ms(560), BeginTime = TimeSpan.FromMilliseconds(120) };
                backScale.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                backScale.KeyFrames.Add(new EasingDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(196))));
                backScale.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)),
                    new CubicEase { EasingMode = EasingMode.EaseOut }));
                _backGrow.ScaleX = 0.0;
                _backGrow.ScaleY = 0.0;
                _backGrow.BeginAnimation(ScaleTransform.ScaleXProperty, backScale);
                _backGrow.BeginAnimation(ScaleTransform.ScaleYProperty, backScale);
            }

            for (int i = 0; i < _parts.Count; i++)
            {
                PlaySlotOpening(_parts[i], i);
            }
        }

        /// <summary>How long the chosen opening takes until the last slot has landed, in milliseconds.</summary>
        private double OpeningLength()
        {
            int last = Math.Max(0, _slotCount - 1);
            switch (_opening)
            {
                case QuickMenuOpening.Bloom: return 200 + (last * 34) + 320;
                case QuickMenuOpening.SpinIn: return 200 + 380;
                case QuickMenuOpening.IrisPop: return 200 + (last * 45) + 320;
                default: return Math.Max(480, 200 + (last * 22) + 220);
            }
        }

        private void PlaySlotOpening(SlotParts p, int index)
        {
            double radians = p.Angle * Math.PI / 180.0;
            var outEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            switch (_opening)
            {
                case QuickMenuOpening.Bloom:
                {
                    // Out of the centre button: from the middle of the wheel, small, travelling out to its place.
                    TimeSpan begin = TimeSpan.FromMilliseconds(200 + (index * 34));
                    var ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
                    p.OpenScale.CenterX = p.Middle.X;
                    p.OpenScale.CenterY = p.Middle.Y;
                    double dx = _centre - p.Middle.X, dy = _centre - p.Middle.Y;
                    Keyed(p.OpenMove, TranslateTransform.XProperty, begin, 320, dx, dx * 0.6, 0.25, 0.0, ease);
                    Keyed(p.OpenMove, TranslateTransform.YProperty, begin, 320, dy, dy * 0.6, 0.25, 0.0, ease);
                    Keyed(p.OpenScale, ScaleTransform.ScaleXProperty, begin, 320, 0.3, 0.55, 0.25, 1.0, ease);
                    Keyed(p.OpenScale, ScaleTransform.ScaleYProperty, begin, 320, 0.3, 0.55, 0.25, 1.0, ease);
                    Keyed(p.Layer, OpacityProperty, begin, 320, 0.0, 1.0, 0.25, 1.0, null);
                    break;
                }

                case QuickMenuOpening.SpinIn:
                {
                    // The whole ring swirls out of the centre together and settles.
                    TimeSpan begin = TimeSpan.FromMilliseconds(200);
                    p.OpenScale.CenterX = _centre;
                    p.OpenScale.CenterY = _centre;
                    Keyed(p.OpenTurn, RotateTransform.AngleProperty, begin, 380, -55.0, 4.0, 0.75, 0.0, outEase);
                    Keyed(p.OpenScale, ScaleTransform.ScaleXProperty, begin, 380, 0.45, 1.03, 0.75, 1.0, outEase);
                    Keyed(p.OpenScale, ScaleTransform.ScaleYProperty, begin, 380, 0.45, 1.03, 0.75, 1.0, outEase);
                    Keyed(p.Layer, OpacityProperty, begin, 380, 0.0, 1.0, 0.75, 1.0, null);
                    break;
                }

                case QuickMenuOpening.IrisPop:
                {
                    // One by one out of the centre, with a twist and a small bounce.
                    TimeSpan begin = TimeSpan.FromMilliseconds(200 + (index * 45));
                    p.OpenScale.CenterX = _centre;
                    p.OpenScale.CenterY = _centre;
                    Keyed(p.OpenTurn, RotateTransform.AngleProperty, begin, 320, -28.0, 3.0, 0.7, 0.0, outEase);
                    Keyed(p.OpenScale, ScaleTransform.ScaleXProperty, begin, 320, 0.0, 1.08, 0.7, 1.0, outEase);
                    Keyed(p.OpenScale, ScaleTransform.ScaleYProperty, begin, 320, 0.0, 1.08, 0.7, 1.0, outEase);
                    Keyed(p.Layer, OpacityProperty, begin, 320, 0.0, 1.0, 0.7, 1.0, null);
                    break;
                }

                default:
                {
                    // Unfold: each slot slides out a short way, one after another, clockwise.
                    TimeSpan begin = TimeSpan.FromMilliseconds(200 + (index * 22));
                    double inX = -26.0 * Math.Cos(radians), inY = -26.0 * Math.Sin(radians);
                    Keyed(p.OpenMove, TranslateTransform.XProperty, begin, 220, inX, inX, 0.0, 0.0, outEase);
                    Keyed(p.OpenMove, TranslateTransform.YProperty, begin, 220, inY, inY, 0.0, 0.0, outEase);
                    Keyed(p.Layer, OpacityProperty, begin, 220, 0.0, 0.0, 0.0, 1.0, outEase);
                    break;
                }
            }
        }

        /// <summary>
        /// One property's opening move: parked at <paramref name="from"/> until its turn (so a slot still
        /// waiting does not show at its final place), through <paramref name="mid"/> at <paramref name="midAt"/>
        /// of the way, ending at <paramref name="to"/> and holding there.
        /// </summary>
        private static void Keyed(Animatable target, DependencyProperty property, TimeSpan begin, int milliseconds,
                                  double from, double mid, double midAt, double to, IEasingFunction ease)
        {
            target.SetValue(property, from);
            var frames = new DoubleAnimationUsingKeyFrames { Duration = Ms(milliseconds), BeginTime = begin };
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if (midAt > 0.0 && midAt < 1.0)
            {
                frames.KeyFrames.Add(new EasingDoubleKeyFrame(mid,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds * midAt)), ease));
            }

            frames.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)), ease));
            target.BeginAnimation(property, frames);
        }

        private static void Keyed(UIElement target, DependencyProperty property, TimeSpan begin, int milliseconds,
                                  double from, double mid, double midAt, double to, IEasingFunction ease)
        {
            target.SetValue(property, from);
            var frames = new DoubleAnimationUsingKeyFrames { Duration = Ms(milliseconds), BeginTime = begin };
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if (midAt > 0.0 && midAt < 1.0)
            {
                frames.KeyFrames.Add(new EasingDoubleKeyFrame(mid,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds * midAt)), ease));
            }

            frames.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)), ease));
            target.BeginAnimation(property, frames);
        }

        /// <summary>
        /// The closing: the chosen slot stays lit while the wheel drops back to 90 %, then the window goes.
        /// 110 ms - quick enough that the tool still starts "at once", long enough to be seen.
        /// </summary>
        private void PlayCloseThen(Action afterClose)
        {
            if (!MotionOn || !IsLoaded)
            {
                afterClose();
                return;
            }

            var shrink = new DoubleAnimation(0.9, Ms(110))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            bool done = false;
            DispatcherTimer backstop = null;
            EventHandler finish = (s, e) =>
            {
                if (backstop != null)
                {
                    backstop.Stop();
                }

                if (!done)
                {
                    done = true;
                    afterClose();
                }
            };

            // Backstop: if Windows stops drawing frames (a locked or remote screen), the animation never
            // completes - the wheel must still close, never hang open with every key ignored.
            backstop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            backstop.Tick += finish;
            backstop.Start();

            shrink.Completed += finish;
            PopScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            PopScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        }

        #endregion

        #region Drawing

        private void BuildWheel()
        {
            WheelCanvas.Children.Clear();
            _parts.Clear();
            _selectorStrokes.Clear();
            _selectorFills.Clear();

            _hoverFill = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = new Point(_centre, _centre),
                GradientOrigin = new Point(_centre, _centre),
                RadiusX = _outerRadius,
                RadiusY = _outerRadius,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x1F, 0xFD, 0x64, 0x1A), 0.42),   // Accent 12 %
                    new GradientStop(Color.FromArgb(0x6B, 0xFD, 0x64, 0x1A), 1.0)     // Accent 42 %
                }
            };
            _hoverFill.Freeze();

            AddBackLayer();

            for (int i = 0; i < _slotCount; i++)
            {
                if (_bubbles)
                {
                    AddBubble(i);
                }
                else
                {
                    AddWedge(i);
                }
            }

            AddHub();
            AddSelector();
        }

        /// <summary>
        /// Behind the wheel: a soft Ink halo so it stays readable over a white sheet as well as a dark 3D view
        /// (a static gradient - no blur, this window is drawn by the CPU), and the HUD design's degree ruler.
        /// Grown from the centre by the opening.
        /// </summary>
        private void AddBackLayer()
        {
            if (!_halo && !_ruler)
            {
                return;
            }

            _backGrow = new ScaleTransform(1.0, 1.0, _centre, _centre);
            _backLayer = new Canvas { IsHitTestVisible = false, RenderTransform = _backGrow };
            WheelCanvas.Children.Add(_backLayer);

            if (_halo)
            {
                double radius = _ringOuter + EdgePadding - 2.0;
                var halo = new Ellipse
                {
                    Width = radius * 2.0,
                    Height = radius * 2.0,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(0xD9, 0x0F, 0x0F, 0x10), 0.0),          // Ink 85 %
                            new GradientStop(Color.FromArgb(0xD9, 0x0F, 0x0F, 0x10), (_ringOuter / radius) - 0.005),
                            new GradientStop(Color.FromArgb(0x00, 0x0F, 0x0F, 0x10), 1.0)
                        }
                    }
                };
                halo.Fill.Freeze();
                Canvas.SetLeft(halo, _centre - radius);
                Canvas.SetTop(halo, _centre - radius);
                _backLayer.Children.Add(halo);
            }

            if (_ruler)
            {
                // 72 ticks, one every 5 degrees; every 45 degrees a longer Accent one.
                for (int k = 0; k < 72; k++)
                {
                    bool major = k % 9 == 0;
                    double angle = k * 5.0;
                    Point a = Polar(_ringOuter + (6.0 * _unit), angle);
                    Point b = Polar(_ringOuter + ((major ? 16.0 : 11.0) * _unit), angle);
                    _backLayer.Children.Add(new Line
                    {
                        X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                        Stroke = major ? RulerTickMajor : RulerTick,
                        StrokeThickness = major ? 1.6 : 1.0
                    });
                }
            }
        }

        /// <summary>A new, empty slot layer with its transforms in place (none move until asked).</summary>
        private SlotParts NewSlot(double centreAngle, Point middle)
        {
            var p = new SlotParts
            {
                Angle = centreAngle,
                Middle = middle,
                Pop = new ScaleTransform(1.0, 1.0, middle.X, middle.Y),
                Lift = new TranslateTransform(),
                OpenScale = new ScaleTransform(1.0, 1.0, _centre, _centre),
                OpenTurn = new RotateTransform(0.0, _centre, _centre),
                OpenMove = new TranslateTransform()
            };

            // Applied in order: the pop and the lift belong to the slot; the opening moves it into place.
            var group = new TransformGroup();
            group.Children.Add(p.Pop);
            group.Children.Add(p.Lift);
            group.Children.Add(p.OpenScale);
            group.Children.Add(p.OpenTurn);
            group.Children.Add(p.OpenMove);

            p.Layer = new Canvas { IsHitTestVisible = false, RenderTransform = group };
            WheelCanvas.Children.Add(p.Layer);
            _parts.Add(p);
            return p;
        }

        private void AddWedge(int index)
        {
            QuickToolEntry entry = _slots[index];
            bool filled = entry != null;
            bool usable = IsUsable(index);

            double centreAngle = -90.0 + (index * _stepDegrees);
            double startAngle = centreAngle - (_stepDegrees / 2.0) + WedgeGapDegrees;
            double endAngle = centreAngle + (_stepDegrees / 2.0) - WedgeGapDegrees;
            double contentRadius = (_ringInner + _ringOuter) / 2.0;
            SlotParts p = NewSlot(centreAngle, Polar(contentRadius, centreAngle));

            Geometry shape = RoundedSlotGeometry.Build(
                new Point(_centre, _centre), _ringInner, _ringOuter, startAngle, endAngle,
                _outerRadius * RoundedSlotGeometry.CornerFraction, _notched ? 7.0 * _unit : 0.0);

            p.Body = new Path { Data = shape, Fill = IdleFill(index), Stroke = WedgeStroke, StrokeThickness = 1.0 };
            p.Layer.Children.Add(p.Body);

            p.Glow = new Path { Data = shape, Fill = _hoverFill, Stroke = WedgeStrokeHover, StrokeThickness = 2.0, Opacity = 0.0 };
            p.Layer.Children.Add(p.Glow);

            if (_rimLights)
            {
                Geometry rim = RoundedSlotGeometry.Build(
                    new Point(_centre, _centre), _ringOuter + (4.0 * _unit), _ringOuter + (9.0 * _unit),
                    startAngle, endAngle, 2.5);
                p.Layer.Children.Add(new Path { Data = rim, Fill = RimIdle });
                p.RimLight = new Path { Data = rim, Fill = WedgeStrokeHover, Opacity = 0.0 };
                p.Layer.Children.Add(p.RimLight);
            }

            // Fit the icon + label inside the slot. The tight spot is the INNER corners of the block, not its
            // middle: a slot narrows towards the hub, so measure the allowance there, and never let the block
            // be wider than the ring is thick.
            double halfWedgeRadians = ((_stepDegrees / 2.0) - WedgeGapDegrees) * Math.PI / 180.0;
            double innerCornerRadius = Math.Max(_ringInner + 6.0, contentRadius - EstimatedContentHalfHeight);
            double angularAllowance = 2.0 * innerCornerRadius * Math.Tan(Math.Min(halfWedgeRadians, 1.2));
            double radialAllowance = (_ringOuter - _ringInner) - 44.0;
            double contentWidth = Math.Max(60.0, Math.Min(angularAllowance, radialAllowance));

            AddSlotContent(p, index, entry, filled, usable, contentWidth, p.Middle, _labelsOnSlots, _labelsOnSlots ? 32.0 : 40.0);

            // Slot number just inside the rim - 1 to 9 only, since those are the keys that work.
            if (_digits && index < 9)
            {
                var digit = new TextBlock
                {
                    Text = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Foreground = TextDisabled,
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    Width = 16
                };

                Point digitCentre = Polar(_ringOuter - 15.0, centreAngle);
                digit.Measure(new Size(16, double.PositiveInfinity));
                Canvas.SetLeft(digit, digitCentre.X - 8.0);
                Canvas.SetTop(digit, digitCentre.Y - (digit.DesiredSize.Height / 2.0));
                p.Layer.Children.Add(digit);
            }
        }

        /// <summary>Orbit Buttons: one round bubble per slot on a circle round the centre.</summary>
        private void AddBubble(int index)
        {
            QuickToolEntry entry = _slots[index];
            bool filled = entry != null;
            bool usable = IsUsable(index);

            double centreAngle = -90.0 + (index * _stepDegrees);
            Point middle = Polar(_ringOuter, centreAngle);
            SlotParts p = NewSlot(centreAngle, middle);

            var shape = new EllipseGeometry(middle, _bubbleRadius, _bubbleRadius);
            shape.Freeze();
            p.Body = new Path { Data = shape, Fill = IdleFill(index), Stroke = WedgeStroke, StrokeThickness = 1.0 };
            p.Layer.Children.Add(p.Body);

            var glowFill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x73, 0xFD, 0x64, 0x1A), 0.0),   // Accent 45 %
                    new GradientStop(Color.FromArgb(0x2E, 0xFD, 0x64, 0x1A), 1.0)    // Accent 18 %
                }
            };
            glowFill.Freeze();
            p.Glow = new Path { Data = shape, Fill = glowFill, Stroke = WedgeStrokeHover, StrokeThickness = 2.0, Opacity = 0.0 };
            p.Layer.Children.Add(p.Glow);

            AddSlotContent(p, index, entry, filled, usable, _bubbleRadius * 1.6, middle, false, 34.0);

            // With names on, the pointed bubble shows its name in an orange pill on its outer side.
            if (_showNames && filled)
            {
                var pillText = new TextBlock
                {
                    Text = entry.DisplayName,
                    Foreground = InkText,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold
                };
                var pill = new Border
                {
                    Background = WedgeStrokeHover,
                    CornerRadius = new CornerRadius(11),
                    Padding = new Thickness(10, 3, 10, 4),
                    Child = pillText,
                    Opacity = 0.0
                };
                pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double w = pill.DesiredSize.Width, h = pill.DesiredSize.Height;
                double edge = 4.0 + _lift;              // the bubble steps out by _lift while lit
                double size = _centre * 2.0;
                bool rightSide = Math.Cos(centreAngle * Math.PI / 180.0) >= -0.01;
                double x = rightSide ? middle.X + _bubbleRadius + 6.0 : middle.X - _bubbleRadius - 6.0 - w;
                double y = middle.Y - (h / 2.0);
                if (x < edge || x + w > size - edge)
                {
                    // No room beside it: centre the pill under the bubble (above it in the top half),
                    // kept inside the window.
                    x = Math.Min(Math.Max(middle.X - (w / 2.0), edge), size - edge - w);
                    y = middle.Y >= _centre - 1.0 ? middle.Y + _bubbleRadius + 6.0 : middle.Y - _bubbleRadius - 6.0 - h;
                }

                Canvas.SetLeft(pill, x);
                Canvas.SetTop(pill, y);
                p.Layer.Children.Add(pill);
                p.Pill = pill;
            }
        }

        /// <summary>The icon (and, with names on the wheel, the name) for one slot.</summary>
        private void AddSlotContent(SlotParts p, int index, QuickToolEntry entry, bool filled, bool usable,
                                    double width, Point middle, bool withLabel, double iconSize)
        {
            p.IconGrow = new ScaleTransform(1.0, 1.0);
            var stack = new StackPanel
            {
                Width = width,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = p.IconGrow
            };

            if (filled && entry.Icon != null)
            {
                stack.Children.Add(new Image
                {
                    Source = entry.Icon,
                    Width = iconSize,
                    Height = iconSize,
                    Margin = new Thickness(0, 0, 0, withLabel ? 6 : 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    SnapsToDevicePixels = true,

                    // Dimmed the way the ribbon dims a greyed button's icon.
                    Opacity = usable ? 1.0 : 0.35
                });
            }

            // An empty slot always says so, even when names are off, so it never looks like a missing icon.
            if (withLabel || !filled)
            {
                p.Label = new TextBlock
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
                stack.Children.Add(p.Label);
            }

            stack.Measure(new Size(stack.Width, double.PositiveInfinity));
            Canvas.SetLeft(stack, middle.X - (stack.Width / 2.0));
            Canvas.SetTop(stack, middle.Y - (stack.DesiredSize.Height / 2.0));
            p.Layer.Children.Add(stack);
        }

        private void AddHub()
        {
            double hr = _hubRadius;
            _hubGrow = new ScaleTransform(1.0, 1.0, _centre, _centre);
            _hubLayer = new Canvas { IsHitTestVisible = false, RenderTransform = _hubGrow };
            WheelCanvas.Children.Add(_hubLayer);

            var hub = new Ellipse { Width = hr * 2.0, Height = hr * 2.0, Fill = HubFill, Stroke = HubStroke, StrokeThickness = 1.0 };
            Canvas.SetLeft(hub, _centre - hr);
            Canvas.SetTop(hub, _centre - hr);
            _hubLayer.Children.Add(hub);

            if (_hub == HubKind.Ticks)
            {
                for (int k = 0; k < 48; k++)
                {
                    double angle = k * 7.5;
                    Point a = Polar(hr - 4.0, angle);
                    Point b = Polar(hr - (k % 6 == 0 ? 10.0 : 7.0), angle);
                    _hubLayer.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = HubTick, StrokeThickness = 1.0 });
                }
            }

            var mark = Geometry.Parse(HeronMarkData);
            mark.Freeze();

            if (_hub == HubKind.Card)
            {
                // Mark on top, then the pointed tool's large icon, its name and the hint, spaced to the hub.
                double markSize = Math.Min(22.0, hr * 0.24);
                AddHubMark(mark, markSize, _centre - (hr * 0.70));

                _hubIconSize = hr < 90.0 ? 38.0 : 48.0;
                _hubIconHolder = new Canvas();
                _hubLayer.Children.Add(_hubIconHolder);

                _hubToolText = HubText("Point at a tool", hr < 90.0 ? 13 : 15, TextAccent, FontWeights.ExtraBold, hr);
                PlaceHubText(_hubToolText, _centre + (hr * 0.20));
                _hubHintText = HubText("Click to run  ·  S settings  ·  Esc close", 10.5, TextDisabled, FontWeights.Normal, hr);
                PlaceHubText(_hubHintText, _centre + (hr * 0.42));
                return;
            }

            var stack = new StackPanel { Width = Math.Max(60.0, (hr * 2.0) - 22.0) };
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
                FontSize = hr < 70.0 ? 13 : 15,
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
            _hubLayer.Children.Add(stack);
        }

        private void AddHubMark(Geometry mark, double size, double middleY)
        {
            var box = new Viewbox
            {
                Width = size,
                Height = size,
                Child = new Path { Data = mark, Fill = TextSecondary, Stretch = Stretch.Uniform }
            };
            Canvas.SetLeft(box, _centre - (size / 2.0));
            Canvas.SetTop(box, middleY - (size / 2.0));
            _hubLayer.Children.Add(box);
        }

        private TextBlock HubText(string text, double fontSize, Brush brush, FontWeight weight, double hubRadius)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = brush,
                FontSize = fontSize,
                FontWeight = weight,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Width = Math.Max(60.0, (hubRadius * 2.0) - 30.0)
            };
        }

        private void PlaceHubText(TextBlock text, double middleY)
        {
            text.Measure(new Size(text.Width, double.PositiveInfinity));
            Canvas.SetLeft(text, _centre - (text.Width / 2.0));
            Canvas.SetTop(text, middleY - (text.DesiredSize.Height / 2.0));
            _hubLayer.Children.Add(text);
        }

        /// <summary>
        /// The selector, drawn once at slot 1 (12 o'clock) and turned round the centre to whichever slot is
        /// pointed at: an Accent arc just outside the rim, a needle on the hub's edge, or a chevron outside the
        /// rim - as the design has them. Hidden until a slot is pointed at.
        /// </summary>
        private void AddSelector()
        {
            if (!_arc && !_needle && !_chevron)
            {
                return;
            }

            _selectorTurn = new RotateTransform(0.0, _centre, _centre);
            _selectorLayer = new Canvas { IsHitTestVisible = false, Opacity = 0.0, RenderTransform = _selectorTurn };
            WheelCanvas.Children.Add(_selectorLayer);

            if (_arc)
            {
                double half = Math.Max(4.0, (_stepDegrees / 2.0) - (WedgeGapDegrees * 3.0));

                // Just clear of a lifted slot (which steps out 7 + 1 for its edge), inside the window's margin.
                double arcRadius = _ringOuter + 11.0;
                var arc = new PathFigure { StartPoint = Polar(arcRadius, -90.0 - half), IsClosed = false, IsFilled = false };
                arc.Segments.Add(new ArcSegment(Polar(arcRadius, -90.0 + half), new Size(arcRadius, arcRadius), 0,
                    (half * 2.0) > 180.0, SweepDirection.Clockwise, true));
                var geometry = new PathGeometry();
                geometry.Figures.Add(arc);
                geometry.Freeze();
                var path = new Path
                {
                    Data = geometry,
                    Stroke = WedgeStrokeHover,
                    StrokeThickness = 3.0,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                _selectorLayer.Children.Add(path);
                _selectorStrokes.Add(path);
            }

            if (_needle && _hubRadius > 30.0)
            {
                var needle = new PathFigure { StartPoint = Polar(_hubRadius - 3.0, -90.0), IsClosed = true, IsFilled = true };
                needle.Segments.Add(new LineSegment(Polar(_hubRadius - 11.0, -93.2), true));
                needle.Segments.Add(new LineSegment(Polar(_hubRadius - 11.0, -86.8), true));
                var geometry = new PathGeometry();
                geometry.Figures.Add(needle);
                geometry.Freeze();
                var path = new Path { Data = geometry, Fill = WedgeStrokeHover };
                _selectorLayer.Children.Add(path);
                _selectorFills.Add(path);
            }

            if (_chevron)
            {
                double r = _ringOuter + (8.0 * _unit);
                var chevron = new PathFigure { StartPoint = Polar(r + (9.0 * _unit), -94.0), IsClosed = false, IsFilled = false };
                chevron.Segments.Add(new LineSegment(Polar(r, -90.0), true));
                chevron.Segments.Add(new LineSegment(Polar(r + (9.0 * _unit), -86.0), true));
                var geometry = new PathGeometry();
                geometry.Figures.Add(chevron);
                geometry.Freeze();
                var path = new Path
                {
                    Data = geometry,
                    Stroke = WedgeStrokeHover,
                    StrokeThickness = 3.0,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                _selectorLayer.Children.Add(path);
                _selectorStrokes.Add(path);
            }
        }

        private Point Polar(double radius, double angleDegrees)
        {
            double radians = angleDegrees * Math.PI / 180.0;
            return new Point(
                _centre + (radius * Math.Cos(radians)),
                _centre + (radius * Math.Sin(radians)));
        }

        #endregion

        #region Pointing

        /// <summary>
        /// Turns the selector to <paramref name="index"/> by the short way round (never a full spin), or
        /// fades it out for -1. Coming from "nothing pointed at", it appears in place instead of sweeping.
        /// </summary>
        private void MoveSelector(int index)
        {
            if (_selectorLayer == null)
            {
                return;
            }

            if (index < 0)
            {
                if (_selectorShown)
                {
                    _selectorShown = false;
                    FadeTo(_selectorLayer, 0.0, 120);
                }

                return;
            }

            Brush tone = IsUsable(index) ? WedgeStrokeHover : WedgeStrokeHoverDisabled;
            foreach (Shape s in _selectorStrokes)
            {
                s.Stroke = tone;
            }

            foreach (Shape s in _selectorFills)
            {
                s.Fill = tone;
            }

            double target = index * _stepDegrees;
            if (!_selectorShown || !MotionOn)
            {
                _selectorAngle = target;
                _selectorTurn.BeginAnimation(RotateTransform.AngleProperty, null);
                _selectorTurn.Angle = target;
            }
            else
            {
                // Shortest way round from where the selector is aiming now.
                double delta = target - (_selectorAngle % 360.0);
                delta = ((delta % 360.0) + 540.0) % 360.0 - 180.0;
                _selectorAngle += delta;

                _selectorTurn.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(_selectorAngle, Ms(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }

            if (!_selectorShown)
            {
                _selectorShown = true;
                FadeTo(_selectorLayer, 1.0, 90);
            }
        }

        /// <summary>Lifts a slot out towards the rim (or lets it settle back) and grows its icon a touch.</summary>
        private void LiftSlot(int index, bool lifted)
        {
            if (index < 0 || index >= _parts.Count)
            {
                return;
            }

            SlotParts p = _parts[index];
            double radians = p.Angle * Math.PI / 180.0;
            double distance = lifted ? _lift : 0.0;
            double scale = lifted && _lift > 0.0 ? 1.12 : 1.0;
            int milliseconds = lifted ? 120 : 160;

            if (!MotionOn)
            {
                p.Lift.X = distance * Math.Cos(radians);
                p.Lift.Y = distance * Math.Sin(radians);
                p.IconGrow.ScaleX = scale;
                p.IconGrow.ScaleY = scale;
                return;
            }

            var duration = Ms(milliseconds);
            IEasingFunction ease = lifted
                ? (IEasingFunction)new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }
                : new CubicEase { EasingMode = EasingMode.EaseOut };

            p.Lift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(distance * Math.Cos(radians), duration) { EasingFunction = ease });
            p.Lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(distance * Math.Sin(radians), duration) { EasingFunction = ease });
            p.IconGrow.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
            p.IconGrow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
        }

        /// <summary>Every design: the newly pointed slot pops - grows a touch, then settles.</summary>
        private void PopSlot(int index)
        {
            if (!MotionOn || index < 0 || index >= _parts.Count)
            {
                return;
            }

            var pop = new DoubleAnimationUsingKeyFrames { Duration = Ms(260) };
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.07, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(117)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));
            _parts[index].Pop.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            _parts[index].Pop.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        private static void FadeTo(UIElement element, double opacity, int milliseconds)
        {
            if (element == null)
            {
                return;
            }

            if (!MotionOn)
            {
                element.BeginAnimation(OpacityProperty, null);
                element.Opacity = opacity;
                return;
            }

            element.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, Ms(milliseconds)));
        }

        private void OnWheelMouseMove(object sender, MouseEventArgs e)
        {
            // Once a choice is made the wheel is closing: the picked slot stays lit to the end. While the
            // opening plays nothing lights yet - the slot under the pointer lights as soon as it lands.
            if (_choiceMade || !_opened)
            {
                return;
            }

            SetHover(IndexAt(e.GetPosition(WheelCanvas)));
        }

        /// <summary>Which slot the pointer is aiming at, or -1 for the centre / outside the wheel.</summary>
        private int IndexAt(Point point)
        {
            double dx = point.X - _centre;
            double dy = point.Y - _centre;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));

            if (distance < _hitInner || distance > _hitOuter)
            {
                return -1;
            }

            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            double shifted = angle + 90.0 + (_stepDegrees / 2.0);
            shifted = ((shifted % 360.0) + 360.0) % 360.0;

            int index = (int)(shifted / _stepDegrees);
            index = index >= _slotCount ? _slotCount - 1 : index;

            // Orbit Buttons: only the bubble itself (and a few units round it) points at its tool - not the
            // gaps between bubbles, nor the space between the centre and the bubbles.
            if (_bubbles && index < _parts.Count)
            {
                Point middle = _parts[index].Middle;
                double bx = point.X - middle.X, by = point.Y - middle.Y;
                if (Math.Sqrt((bx * bx) + (by * by)) > _bubbleRadius + 6.0)
                {
                    return -1;
                }
            }

            return index;
        }

        private void SetHover(int index)
        {
            if (index == _hoverIndex)
            {
                return;
            }

            if (_hoverIndex >= 0 && _hoverIndex < _parts.Count)
            {
                SlotParts previous = _parts[_hoverIndex];
                previous.Body.Fill = IdleFill(_hoverIndex);
                previous.Body.Stroke = WedgeStroke;
                previous.Body.StrokeThickness = 1.0;
                FadeTo(previous.Glow, 0.0, 140);
                FadeTo(previous.RimLight, 0.0, 140);
                FadeTo(previous.Pill, 0.0, 120);
                LiftSlot(_hoverIndex, false);
                if (previous.Label != null)
                {
                    previous.Label.Foreground = IsUsable(_hoverIndex) ? TextIdle : TextDisabled;
                }
            }

            _hoverIndex = index;

            if (_hoverIndex >= 0 && _hoverIndex < _parts.Count)
            {
                // A slot the ribbon would grey out does not light up - it only acknowledges the pointer,
                // the same as hovering a greyed ribbon button.
                bool usable = IsUsable(_hoverIndex);
                SlotParts current = _parts[_hoverIndex];
                current.Body.Stroke = usable ? WedgeStroke : WedgeStrokeHoverDisabled;
                current.Body.StrokeThickness = usable ? 1.0 : 1.4;
                if (usable)
                {
                    FadeTo(current.Glow, 1.0, 90);
                    FadeTo(current.RimLight, 1.0, 90);
                    FadeTo(current.Pill, 1.0, 90);
                    LiftSlot(_hoverIndex, true);
                    PopSlot(_hoverIndex);
                }

                if (current.Label != null)
                {
                    current.Label.Foreground = usable ? TextPrimary : TextDisabled;
                }
            }

            MoveSelector(_hoverIndex);
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

        /// <summary>The slot colour when the pointer is not on it: filled, greyed out, or empty.</summary>
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

            ShowHubIcon(entry);

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

        /// <summary>The centre card's large icon of the pointed tool (Card centre only).</summary>
        private void ShowHubIcon(QuickToolEntry entry)
        {
            if (_hubIconHolder == null)
            {
                return;
            }

            _hubIconHolder.Children.Clear();
            if (entry == null || entry.Icon == null)
            {
                return;
            }

            var image = new Image
            {
                Source = entry.Icon,
                Width = _hubIconSize,
                Height = _hubIconSize,
                SnapsToDevicePixels = true,
                Opacity = IsUsable(_hoverIndex) ? 1.0 : 0.35
            };
            Canvas.SetLeft(image, _centre - (_hubIconSize / 2.0));
            Canvas.SetTop(image, _centre - (_hubRadius * 0.30) - (_hubIconSize / 2.0));
            _hubIconHolder.Children.Add(image);
        }

        #endregion

        #region Choosing

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
                // Aimed at the centre or outside the wheel - same as a game, that means "never mind".
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
                // point the centre at the reason rather than closing or posting a command Revit refuses.
                SetHover(index);
                return;
            }

            if (_choiceMade)
            {
                return;
            }

            _choiceMade = true;
            SelectedEntry = entry;

            // Keep the picked slot lit and lifted (a key press may pick a slot the pointer is not on).
            SetHover(index);
            PlayCloseThen(() => SetDialogResultSafely(true));
        }

        private void CloseWithoutChoice()
        {
            CloseWithoutChoice(true);
        }

        /// <param name="animate">False when focus has already gone elsewhere: then the wheel just goes.</param>
        private void CloseWithoutChoice(bool animate)
        {
            if (_choiceMade)
            {
                return;
            }

            _choiceMade = true;
            SelectedEntry = null;

            if (animate)
            {
                PlayCloseThen(() => SetDialogResultSafely(false));
            }
            else
            {
                SetDialogResultSafely(false);
            }
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
                CloseWithoutChoice(false);
            }
        }

        #endregion

        private static Duration Ms(double milliseconds)
        {
            return new Duration(TimeSpan.FromMilliseconds(milliseconds));
        }

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
