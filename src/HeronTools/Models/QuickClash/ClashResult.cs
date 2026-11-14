// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Quick Clash - one row of the results list: which two elements, what kind of clash, how deep or how close.

using System.Globalization;
using Autodesk.Revit.DB;

namespace HeronTools.Models.QuickClash
{
    /// <summary>What a pair turned out to be.</summary>
    internal enum ClashKind
    {
        /// <summary>The two elements overlap by more than the tolerance.</summary>
        Hard = 0,
        /// <summary>Not overlapping, but closer than the clearance distance (touching counts).</summary>
        Clearance = 1,
        /// <summary>Revit refused to compare the two shapes (damaged or very complex geometry).</summary>
        NotChecked = 2
    }

    /// <summary>One clash: element A (always this model), element B (this model or a link), and the size.</summary>
    internal sealed class ClashResult
    {
        public int Number { get; set; }
        public ClashKind Kind { get; set; }

        /// <summary>Hard: overlap depth in mm. Clearance: gap in mm. Not checked: -1.</summary>
        public double Millimetres { get; set; }

        public long ElementAId { get; set; }
        public long ElementBId { get; set; }

        /// <summary>The link instance element B lives in, or -1 when B is in this model.</summary>
        public long LinkInstanceId { get; set; } = -1;

        public string NameA { get; set; }
        public string NameB { get; set; }

        /// <summary>Where the clash is, in this model's coordinates (feet) - for Zoom.</summary>
        public XYZ BoxMin { get; set; }
        public XYZ BoxMax { get; set; }

        public bool BIsLinked => LinkInstanceId >= 0;

        // ---- shown in the list
        public string NumberText => Number.ToString(CultureInfo.InvariantCulture);

        public string KindText =>
            Kind == ClashKind.Hard ? "Hard" : Kind == ClashKind.Clearance ? "Clearance" : "Not checked";

        public string MillimetresText =>
            Kind == ClashKind.NotChecked ? "-" : Millimetres.ToString("0", CultureInfo.InvariantCulture);
    }
}
