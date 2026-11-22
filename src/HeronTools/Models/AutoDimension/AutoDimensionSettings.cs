// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.3.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The Auto Dimension settings: which rows, where they sit, which dimension types, which grids and levels.

using System;

namespace HeronTools.Models.AutoDimension
{
    /// <summary>Which datums a button dimensions.</summary>
    internal enum AutoDimensionMode
    {
        Grids,
        Levels,
        GridsAndLevels
    }

    /// <summary>Which end of the grids (or levels) the rows sit at.</summary>
    internal enum AutoDimensionSide
    {
        /// <summary>The end where most of the datums show their bubble.</summary>
        BubbleEnd,

        /// <summary>The end opposite the bubbles.</summary>
        OtherEnd,

        /// <summary>A matching set of rows at both ends.</summary>
        BothEnds
    }

    /// <summary>Where grids or levels are read from.</summary>
    internal enum DatumSourceScope
    {
        None,
        ThisModel,
        LinkedModels,
        ThisModelAndLinks
    }

    /// <summary>
    /// Settings shared by the three Auto Dimension buttons. Saved per user, for every project.
    /// </summary>
    internal sealed class AutoDimensionSettings
    {
        internal const double MaxGapMm = 200.0;

        /// <summary>The chain: one dimension through every grid (or level) in turn.</summary>
        public bool ChainRow { get; set; } = true;

        /// <summary>The overall: first to last.</summary>
        public bool OverallRow { get; set; } = true;

        /// <summary>With only two datums the overall repeats the chain, so it is left out.</summary>
        public bool SkipOverallForTwo { get; set; } = true;

        public AutoDimensionSide Side { get; set; } = AutoDimensionSide.BubbleEnd;

        /// <summary>Sheet millimetres from the grid heads to the first row (times the view scale in the model).</summary>
        public double FirstGapMm { get; set; } = 8.0;

        /// <summary>Sheet millimetres between the first and second row.</summary>
        public double RowGapMm { get; set; } = 6.0;

        /// <summary>Dimension type name for the chain. Empty = the project's default.</summary>
        public string ChainTypeName { get; set; } = string.Empty;

        /// <summary>Dimension type name for the overall. Empty = the project's default.</summary>
        public string OverallTypeName { get; set; } = string.Empty;

        public DatumSourceScope GridsFrom { get; set; } = DatumSourceScope.ThisModel;

        public DatumSourceScope LevelsFrom { get; set; } = DatumSourceScope.ThisModel;

        /// <summary>Only levels ticked "Building Story".</summary>
        public bool StoryLevelsOnly { get; set; }

        /// <summary>Names to leave out, separated by ';'. Whole names; '*' and '?' as wildcards.</summary>
        public string LeaveOutNames { get; set; } = string.Empty;

        /// <summary>Do not make a row that is already in the view (same grids, same kind).</summary>
        public bool SkipExistingRows { get; set; } = true;

        /// <summary>Show what was left out after a run that left something out.</summary>
        public bool ShowLeftOut { get; set; } = true;

        /// <summary>Puts every value back in range. A damaged file can never give a broken run.</summary>
        internal void Normalize()
        {
            if (!ChainRow && !OverallRow)
                ChainRow = true;
            if (!Enum.IsDefined(typeof(AutoDimensionSide), Side))
                Side = AutoDimensionSide.BubbleEnd;
            if (!Enum.IsDefined(typeof(DatumSourceScope), GridsFrom))
                GridsFrom = DatumSourceScope.ThisModel;
            if (!Enum.IsDefined(typeof(DatumSourceScope), LevelsFrom))
                LevelsFrom = DatumSourceScope.ThisModel;
            FirstGapMm = Clamp(FirstGapMm, 8.0);
            RowGapMm = Clamp(RowGapMm, 6.0);
            ChainTypeName = (ChainTypeName ?? string.Empty).Trim();
            OverallTypeName = (OverallTypeName ?? string.Empty).Trim();
            LeaveOutNames = (LeaveOutNames ?? string.Empty).Trim();
        }

        internal AutoDimensionSettings Clone()
        {
            return (AutoDimensionSettings)MemberwiseClone();
        }

        private static double Clamp(double value, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return fallback;
            if (value < 0)
                return 0;
            return value > MaxGapMm ? MaxGapMm : value;
        }
    }
}
