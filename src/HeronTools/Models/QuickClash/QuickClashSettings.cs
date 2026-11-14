// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Quick Clash - the clash test as set in the window (categories, clash types, colours), remembered on this PC
// in %AppData%\HeronTools\memory-quick-clash.txt. Names and built-in category numbers only, never element ids.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HeronTools.Utils;

namespace HeronTools.Models.QuickClash
{
    /// <summary>A colour as three 0-255 numbers - no Revit type, so the window can hold it.</summary>
    internal struct ClashColour
    {
        public ClashColour(byte r, byte g, byte b) { R = r; G = g; B = b; }
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public override string ToString() =>
            string.Join(",", R.ToString(CultureInfo.InvariantCulture), G.ToString(CultureInfo.InvariantCulture), B.ToString(CultureInfo.InvariantCulture));

        public static ClashColour Parse(string text, ClashColour fallback)
        {
            string[] parts = (text ?? "").Split(',');
            if (parts.Length == 3 &&
                byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte r) &&
                byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte g) &&
                byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b))
                return new ClashColour(r, g, b);
            return fallback;
        }

        public bool SameAs(ClashColour other) => R == other.R && G == other.G && B == other.B;
    }

    /// <summary>Everything one Run needs, copied from the window at the moment Run is pressed.</summary>
    internal sealed class QuickClashSettings
    {
        // Defaults - Navisworks' own clash colours (item 1 red, item 2 green), the rest grey and see-through.
        public static readonly ClashColour DefaultColourA = new ClashColour(255, 0, 0);
        public static readonly ClashColour DefaultColourB = new ClashColour(0, 192, 0);
        public static readonly ClashColour DefaultColourRest = new ClashColour(180, 180, 180);
        public const int DefaultRestTransparency = 70;
        public const double DefaultToleranceMm = 10;
        public const double DefaultClearanceMm = 50;

        // Limits the window enforces (a typing slip like 5000 mm must not freeze Revit).
        public const double MaxToleranceMm = 1000;
        public const double MinClearanceMm = 1;
        public const double MaxClearanceMm = 2000;

        private const string MemoryTool = "quick-clash";

        public List<long> CategoriesA { get; set; } = new List<long>();
        public List<long> CategoriesB { get; set; } = new List<long>();
        public bool UseSelectionA { get; set; }

        /// <summary>-1 = Selection B is this model; otherwise the link instance id (for this run only).</summary>
        public long SourceLinkInstanceId { get; set; } = -1;
        public string SourceRememberName { get; set; } = "";

        public bool HardOn { get; set; } = true;
        public double ToleranceMm { get; set; } = DefaultToleranceMm;
        public bool ClearanceOn { get; set; } = true;
        public double ClearanceMm { get; set; } = DefaultClearanceMm;

        public ClashColour ColourA { get; set; } = DefaultColourA;
        public ClashColour ColourB { get; set; } = DefaultColourB;
        public ClashColour ColourRest { get; set; } = DefaultColourRest;
        public int RestTransparency { get; set; } = DefaultRestTransparency;

        /// <summary>The selected element ids of this model, read at Run time when "Current selection" is on.</summary>
        public List<long> SelectionIds { get; set; } = new List<long>();

        public QuickClashSettings Copy()
        {
            var copy = (QuickClashSettings)MemberwiseClone();
            copy.CategoriesA = new List<long>(CategoriesA);
            copy.CategoriesB = new List<long>(CategoriesB);
            copy.SelectionIds = new List<long>(SelectionIds);
            return copy;
        }

        public static QuickClashSettings Load()
        {
            WindowMemory memory = WindowMemory.Load(MemoryTool);
            var s = new QuickClashSettings();
            if (memory.IsEmpty)
                return s;

            s.CategoriesA = ParseIds(memory.GetList("CategoriesA"));
            s.CategoriesB = ParseIds(memory.GetList("CategoriesB"));
            s.UseSelectionA = memory.GetBool("UseSelectionA", false);
            s.SourceRememberName = memory.Get("Source", "");
            s.HardOn = memory.GetBool("HardOn", true);
            s.ToleranceMm = Clamp(memory.GetDouble("ToleranceMm", DefaultToleranceMm), 0, MaxToleranceMm);
            s.ClearanceOn = memory.GetBool("ClearanceOn", true);
            s.ClearanceMm = Clamp(memory.GetDouble("ClearanceMm", DefaultClearanceMm), MinClearanceMm, MaxClearanceMm);
            s.ColourA = ClashColour.Parse(memory.Get("ColourA"), DefaultColourA);
            s.ColourB = ClashColour.Parse(memory.Get("ColourB"), DefaultColourB);
            s.ColourRest = ClashColour.Parse(memory.Get("ColourRest"), DefaultColourRest);
            s.RestTransparency = (int)Clamp(memory.GetInt("RestTransparency", DefaultRestTransparency), 0, 100);
            return s;
        }

        /// <summary>Saved when Run is pressed (the choices that were used). Best effort: a failure is not an error.</summary>
        public void Save()
        {
            WindowMemory memory = WindowMemory.Load(MemoryTool);
            memory.Set("CategoriesA", CategoriesA.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            memory.Set("CategoriesB", CategoriesB.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            memory.Set("UseSelectionA", UseSelectionA);
            memory.Set("Source", SourceRememberName ?? "");
            memory.Set("HardOn", HardOn);
            memory.Set("ToleranceMm", ToleranceMm);
            memory.Set("ClearanceOn", ClearanceOn);
            memory.Set("ClearanceMm", ClearanceMm);
            memory.Set("ColourA", ColourA.ToString());
            memory.Set("ColourB", ColourB.ToString());
            memory.Set("ColourRest", ColourRest.ToString());
            memory.Set("RestTransparency", RestTransparency);
            memory.Save(out _);
        }

        private static List<long> ParseIds(IEnumerable<string> items)
        {
            var ids = new List<long>();
            foreach (string item in items)
                if (long.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                    ids.Add(id);
            return ids;
        }

        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
    }
}
