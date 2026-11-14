// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The Quick Menu wheel's look as the modeller chooses it in the settings window: which of the six designs, whether tool names are written on the wheel, and which of the four ways it opens. Chosen 2026-11-13 from the design page with Ajmal.

using System;

namespace HeronTools.Services.QuickMenu
{
    /// <summary>The six wheel designs.</summary>
    internal enum QuickMenuStyle
    {
        /// <summary>Wide ring, names in the slots, the pointed slot steps out; arc on the rim and needle on the hub.</summary>
        HeronRing = 0,

        /// <summary>Thin ring of icons near the rim; the big centre card shows the pointed tool.</summary>
        SlimRing = 1,

        /// <summary>Wide ring with a band of rim lights; the pointed slot's light turns orange.</summary>
        DoubleRing = 2,

        /// <summary>Round bubbles round the centre; the pointed one steps out (name in an orange pill).</summary>
        OrbitButtons = 3,

        /// <summary>Ring with rim lights and a tick-ring centre, no pointer marks.</summary>
        RimLights = 4,

        /// <summary>Notched rim, a degree ruler round the outside and a chevron marker.</summary>
        HudNotched = 5
    }

    /// <summary>The four ways the wheel opens. Every one starts with a small centre button that grows.</summary>
    internal enum QuickMenuOpening
    {
        /// <summary>Then the slots slide out one after another, clockwise.</summary>
        Unfold = 0,

        /// <summary>Then the tools travel out of the button one by one, like a flower opening.</summary>
        Bloom = 1,

        /// <summary>Then the whole ring swirls out of the centre and settles.</summary>
        SpinIn = 2,

        /// <summary>Then the slots grow out one by one with a twist and a small bounce.</summary>
        IrisPop = 3
    }

    /// <summary>What the centre of the wheel holds.</summary>
    internal enum QuickMenuHub
    {
        /// <summary>Heron mark, the pointed tool's name and a hint.</summary>
        Mark,

        /// <summary>Heron mark on top, then the pointed tool's large icon, name and hint.</summary>
        Card,

        /// <summary>As Mark, with a ring of fine ticks inside the centre's edge.</summary>
        Ticks
    }

    /// <summary>
    /// The shape of one design, as fractions of the wheel's radius - the one place both the real wheel and the
    /// settings window's drawing read it from, so the drawing always matches what opens.
    /// </summary>
    internal sealed class QuickMenuDesign
    {
        /// <summary>The designs were drawn on a wheel 170 units in radius; unit sizes below are in those units.</summary>
        internal const double DesignRadius = 170.0;

        /// <summary>Orbit Buttons: separate bubbles instead of a ring.</summary>
        public bool Bubbles { get; private set; }

        /// <summary>Ring: inner edge. Bubbles: the centre's radius. As a fraction of the wheel radius.</summary>
        public double InnerFraction { get; private set; }

        /// <summary>Ring: outer edge. Bubbles: the orbit the bubble centres sit on. As a fraction of the wheel radius.</summary>
        public double OuterFraction { get; private set; }

        /// <summary>Bubble radius in design units (shrunk further where neighbours would touch).</summary>
        public double BubbleUnits { get; private set; }

        public bool LabelsOnSlots { get; private set; }
        public QuickMenuHub Hub { get; private set; }

        /// <summary>How far the pointed slot steps out, in WPF units (0: it does not).</summary>
        public double Lift { get; private set; }

        public bool RimLights { get; private set; }
        public bool Notched { get; private set; }
        public bool Ruler { get; private set; }
        public bool Halo { get; private set; }
        public bool Digits { get; private set; }
        public bool Arc { get; private set; }
        public bool Needle { get; private set; }
        public bool Chevron { get; private set; }

        /// <summary>The six designs, as chosen on the design page (2026-11-13).</summary>
        internal static QuickMenuDesign For(QuickMenuStyle style, bool showNames)
        {
            var d = new QuickMenuDesign
            {
                InnerFraction = 0.42,
                OuterFraction = 1.0,
                LabelsOnSlots = showNames,
                Hub = showNames ? QuickMenuHub.Mark : QuickMenuHub.Card,
                Lift = 7.0,
                Halo = true,
                Digits = true
            };

            switch (style)
            {
                case QuickMenuStyle.SlimRing:
                    d.InnerFraction = 0.72;
                    d.LabelsOnSlots = false;          // too thin for names: the card shows them
                    d.Hub = QuickMenuHub.Card;
                    d.Digits = false;
                    d.Arc = true;
                    d.Needle = true;
                    break;

                case QuickMenuStyle.DoubleRing:
                    d.OuterFraction = 0.90;
                    d.RimLights = true;
                    d.Lift = 0.0;
                    d.Needle = true;
                    break;

                case QuickMenuStyle.OrbitButtons:
                    d.Bubbles = true;
                    d.InnerFraction = 0.30;           // the centre
                    d.OuterFraction = 0.66;           // the orbit
                    d.BubbleUnits = 34.0;
                    d.LabelsOnSlots = false;          // names come in the pill instead
                    d.Hub = QuickMenuHub.Mark;
                    d.Lift = 12.0;
                    d.Halo = false;
                    d.Digits = false;
                    break;

                case QuickMenuStyle.RimLights:
                    d.OuterFraction = 0.92;
                    d.RimLights = true;
                    d.Lift = 0.0;
                    d.Hub = QuickMenuHub.Ticks;
                    break;

                case QuickMenuStyle.HudNotched:
                    d.OuterFraction = 0.93;           // leaves room for the ruler round the outside
                    d.Notched = true;
                    d.Ruler = true;
                    d.Hub = QuickMenuHub.Ticks;
                    d.Halo = false;
                    d.Chevron = true;
                    break;

                default:                              // Heron Ring
                    d.Arc = true;
                    d.Needle = true;
                    break;
            }

            return d;
        }

        /// <summary>The bubble radius for a wheel of <paramref name="radius"/> with <paramref name="slotCount"/> slots.</summary>
        internal double BubbleRadius(double radius, int slotCount)
        {
            double unit = radius / DesignRadius;
            double chordHalf = radius * OuterFraction * Math.Sin(Math.PI / Math.Max(2, slotCount));
            return Math.Min(BubbleUnits * unit, chordHalf - 4.0);
        }
    }

    /// <summary>Names for the settings window, and reading the saved values back.</summary>
    internal static class QuickMenuLook
    {
        internal const QuickMenuStyle DefaultStyle = QuickMenuStyle.HeronRing;
        internal const QuickMenuOpening DefaultOpening = QuickMenuOpening.Unfold;

        internal static readonly QuickMenuStyle[] Styles =
        {
            QuickMenuStyle.HeronRing,
            QuickMenuStyle.SlimRing,
            QuickMenuStyle.DoubleRing,
            QuickMenuStyle.OrbitButtons,
            QuickMenuStyle.RimLights,
            QuickMenuStyle.HudNotched
        };

        internal static readonly QuickMenuOpening[] Openings =
        {
            QuickMenuOpening.Unfold,
            QuickMenuOpening.Bloom,
            QuickMenuOpening.SpinIn,
            QuickMenuOpening.IrisPop
        };

        internal static string StyleName(QuickMenuStyle style)
        {
            switch (style)
            {
                case QuickMenuStyle.SlimRing: return "Slim Ring";
                case QuickMenuStyle.DoubleRing: return "Double Ring";
                case QuickMenuStyle.OrbitButtons: return "Orbit Buttons";
                case QuickMenuStyle.RimLights: return "Rim Lights";
                case QuickMenuStyle.HudNotched: return "HUD Notched";
                default: return "Heron Ring";
            }
        }

        internal static string OpeningName(QuickMenuOpening opening)
        {
            switch (opening)
            {
                case QuickMenuOpening.Bloom: return "Bloom";
                case QuickMenuOpening.SpinIn: return "Spin in";
                case QuickMenuOpening.IrisPop: return "Iris Pop";
                default: return "Unfold";
            }
        }

        /// <summary>Reads a saved style name; anything unknown (an older or edited file) gives the default.</summary>
        internal static QuickMenuStyle ParseStyle(string text)
        {
            QuickMenuStyle value;
            return Enum.TryParse(text ?? string.Empty, true, out value) && Enum.IsDefined(typeof(QuickMenuStyle), value)
                ? value
                : DefaultStyle;
        }

        /// <summary>Reads a saved opening name; anything unknown gives the default.</summary>
        internal static QuickMenuOpening ParseOpening(string text)
        {
            QuickMenuOpening value;
            return Enum.TryParse(text ?? string.Empty, true, out value) && Enum.IsDefined(typeof(QuickMenuOpening), value)
                ? value
                : DefaultOpening;
        }
    }
}
