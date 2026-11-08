// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Provides a 20-colour neon palette for filter graphic overrides — deterministic colour by ElementId and thread-safe random colour selection.

using Autodesk.Revit.DB;
using System;
using System.Threading;

using HeronTools.Utils;
namespace HeronTools.Models
{
    internal static class ColorPalette
    {
        // Thread-safe Random instance to avoid issues with concurrent access
        private static readonly ThreadLocal<Random> _rand = 
            new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));

        // Ultra-distinct 20-color neon palette (no similar colors)
        private static readonly Color[] Palette =
        {
            new Color(255,   0,   0), // Pure Red
            new Color(  0, 255,   0), // Pure Green
            new Color(  0,   0, 255), // Pure Blue
            new Color(255, 255,   0), // Yellow
            new Color(255,   0, 255), // Magenta
            new Color(  0, 255, 255), // Cyan

            new Color(255, 128,   0), // Vivid Orange
            new Color(128,   0, 255), // Vivid Purple
            new Color(255,   0, 128), // Hot Pink
            new Color(  0, 128, 255), // Electric Blue

            new Color(  0, 255, 128), // Spring Green
            new Color(128, 255,   0), // Chartreuse
            new Color(128,   0,   0), // Deep Burgundy
            new Color(  0, 128,   0), // Deep Forest Green
            new Color(  0,   0, 128), // Deep Navy

            new Color(255,  64,   0), // Neon Orange-Red
            new Color(255,   0,  64), // Neon Pink-Red
            new Color( 64,   0, 255), // Ultramarine
            new Color(  0,  64, 255), // Royal Blue
            new Color( 64, 255,   0), // Lime Neon
        };

        /// <summary>
        /// Returns a consistent color for an ElementId.
        /// Same ID → same color.
        /// </summary>
        public static Color GetColorFor(ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId || id.LongValue() == 0)
                return Palette[0];

            // The full 64-bit id is used (Revit 2024+ ids can be larger than 32 bits). The remainder is
            // taken first, so every id keeps the same colour it had with the old 32-bit maths.
            long idValue = id.LongValue();
            int index = (int)Math.Abs(idValue % Palette.Length);
            return Palette[index];
        }

        // Shuffle Colors: one shuffled deck per run, dealt in order, so filters in one run never repeat a
        // colour until all 20 are used (picking at random repeated a colour about 4 times in 10 with only 5
        // filters). A filter keeps the colour it was dealt for every view in the same run.
        private static int[] _deck;
        private static int _dealt;
        private static readonly System.Collections.Generic.Dictionary<long, Color> _dealtByFilter =
            new System.Collections.Generic.Dictionary<long, Color>();

        /// <summary>Starts a new Shuffle run: a freshly shuffled deck, no filter dealt yet.</summary>
        public static void BeginShuffle()
        {
            _deck = new int[Palette.Length];
            for (int i = 0; i < _deck.Length; i++) _deck[i] = i;
            Random rand = _rand.Value;
            for (int i = _deck.Length - 1; i > 0; i--)
            {
                int j = rand.Next(i + 1);
                int t = _deck[i]; _deck[i] = _deck[j]; _deck[j] = t;
            }
            _dealt = 0;
            _dealtByFilter.Clear();
        }

        /// <summary>The colour dealt to this filter in the current Shuffle run (dealing the next one if new).</summary>
        public static Color GetShuffledColorFor(ElementId filterId)
        {
            if (_deck == null)
                BeginShuffle();

            long key = filterId.LongValue();
            if (_dealtByFilter.TryGetValue(key, out Color known))
                return known;

            Color next = Palette[_deck[_dealt % _deck.Length]];
            _dealt++;
            _dealtByFilter[key] = next;
            return next;
        }

        /// <summary>The colour at this position of the current shuffled deck (Colorize: one per value).</summary>
        public static Color GetShuffledColorAt(int index)
        {
            if (_deck == null)
                BeginShuffle();
            int safe = (index == int.MinValue ? 0 : System.Math.Abs(index)) % _deck.Length;
            return Palette[_deck[safe]];
        }

        public static Color GetRandomColor()
        {
            int idx = _rand.Value.Next(Palette.Length);
            return Palette[idx];
        }

        /// <summary>
        /// Returns a stable palette color by position (e.g. a value's index in a selection list).
        /// Same index -> same color, with no dependency on a Revit ElementId.
        /// </summary>
        public static Color GetColorAt(int index)
        {
            int safeIndex = (index == int.MinValue ? 0 : Math.Abs(index)) & 0x7FFFFFFF;
            return Palette[safeIndex % Palette.Length];
        }
    }
}
