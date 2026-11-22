// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Defines fixed group keys used by the Pin Elements tool.

namespace HeronTools.Models.PinTools
{
    /// <summary>
    /// Fixed selectable groups in the Pin Elements tool.
    /// </summary>
    internal enum PinTargetGroup
    {
        TitleBlocks,
        PlacedViews,
        Legends,
        Schedules,
        DuctSystem,
        PipeSystem,
        CableTraySystem,
        AirTerminal,
        Links,
        GenericModels,
        MechanicalEquipment,
        PlumbingFixtures,
        ElectricalEquipment,
        Grids,
        Levels
    }
}
