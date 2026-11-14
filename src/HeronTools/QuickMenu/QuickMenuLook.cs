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
