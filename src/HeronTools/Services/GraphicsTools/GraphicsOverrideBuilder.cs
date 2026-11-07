// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Builds complete Revit OverrideGraphicSettings objects from Apply Graphics input values, and clones existing settings.

using Autodesk.Revit.DB;
using HeronTools.Models.GraphicsTools;

namespace HeronTools.Services.GraphicsTools
{
    /// <summary>
    /// Creates complete OverrideGraphicSettings objects from UI input.
    /// </summary>
    internal static class GraphicsOverrideBuilder
    {
        public static OverrideGraphicSettings Build(GraphicsOverrideInput input)
        {
            var safeInput = input ?? new GraphicsOverrideInput();
            var settings = new OverrideGraphicSettings();

            GraphicsColorValue cutLineColor = safeInput.UseProjectionSurfaceSettingsForCut
                ? safeInput.ProjectionLineColor
                : safeInput.CutLineColor;
            ElementId cutLinePatternId = safeInput.UseProjectionSurfaceSettingsForCut
                ? safeInput.ProjectionLinePatternId
                : safeInput.CutLinePatternId;
            int cutLineWeight = safeInput.UseProjectionSurfaceSettingsForCut
                ? safeInput.ProjectionLineWeight
                : safeInput.CutLineWeight;
            ElementId cutForegroundPatternId = safeInput.UseProjectionSurfaceSettingsForCut
                ? safeInput.SurfaceForegroundPatternId
                : safeInput.CutForegroundPatternId;
            GraphicsColorValue cutForegroundColor = safeInput.UseProjectionSurfaceSettingsForCut
                ? safeInput.SurfaceForegroundPatternColor
                : safeInput.CutForegroundPatternColor;
            ElementId cutBackgroundPatternId = safeInput.UseProjectionSurfaceSettingsForCut
                ? safeInput.SurfaceBackgroundPatternId
                : safeInput.CutBackgroundPatternId;
            GraphicsColorValue cutBackgroundColor = safeInput.UseProjectionSurfaceSettingsForCut
                ? safeInput.SurfaceBackgroundPatternColor
                : safeInput.CutBackgroundPatternColor;

            // A pattern's Visible switch is only touched when that pattern is set: switching it OFF when no pattern
            // was chosen used to hide the element's own fill patterns (e.g. under a line-colour-only Colorize).
            settings.SetProjectionLineColor(ToColorOrInvalid(safeInput.ProjectionLineColor));
            settings.SetProjectionLinePatternId(ToIdOrInvalid(safeInput.ProjectionLinePatternId));
            settings.SetProjectionLineWeight(NormalizeLineWeight(safeInput.ProjectionLineWeight));

            ElementId surfaceForegroundPatternId = ToIdOrInvalid(safeInput.SurfaceForegroundPatternId);
            settings.SetSurfaceForegroundPatternId(surfaceForegroundPatternId);
            if (IsValidId(surfaceForegroundPatternId)) settings.SetSurfaceForegroundPatternVisible(true);
            settings.SetSurfaceForegroundPatternColor(ToColorOrInvalid(safeInput.SurfaceForegroundPatternColor));

            ElementId surfaceBackgroundPatternId = ToIdOrInvalid(safeInput.SurfaceBackgroundPatternId);
            settings.SetSurfaceBackgroundPatternId(surfaceBackgroundPatternId);
            if (IsValidId(surfaceBackgroundPatternId)) settings.SetSurfaceBackgroundPatternVisible(true);
            settings.SetSurfaceBackgroundPatternColor(ToColorOrInvalid(safeInput.SurfaceBackgroundPatternColor));

            settings.SetSurfaceTransparency(ClampTransparency(safeInput.Transparency));

            settings.SetCutLineColor(ToColorOrInvalid(cutLineColor));
            settings.SetCutLinePatternId(ToIdOrInvalid(cutLinePatternId));
            settings.SetCutLineWeight(NormalizeLineWeight(cutLineWeight));

            ElementId resolvedCutForegroundPatternId = ToIdOrInvalid(cutForegroundPatternId);
            settings.SetCutForegroundPatternId(resolvedCutForegroundPatternId);
            if (IsValidId(resolvedCutForegroundPatternId)) settings.SetCutForegroundPatternVisible(true);
            settings.SetCutForegroundPatternColor(ToColorOrInvalid(cutForegroundColor));

            ElementId resolvedCutBackgroundPatternId = ToIdOrInvalid(cutBackgroundPatternId);
            settings.SetCutBackgroundPatternId(resolvedCutBackgroundPatternId);
            if (IsValidId(resolvedCutBackgroundPatternId)) settings.SetCutBackgroundPatternVisible(true);
            settings.SetCutBackgroundPatternColor(ToColorOrInvalid(cutBackgroundColor));

            settings.SetHalftone(safeInput.Halftone);

            return settings;
        }

        public static OverrideGraphicSettings Clone(OverrideGraphicSettings source)
        {
            return source == null
                ? new OverrideGraphicSettings()
                : new OverrideGraphicSettings(source);
        }

        private static Color ToColorOrInvalid(GraphicsColorValue colorValue)
        {
            return colorValue == null
                ? Color.InvalidColorValue
                : colorValue.ToRevitColorOrInvalid();
        }

        private static ElementId ToIdOrInvalid(ElementId id)
        {
            return id ?? ElementId.InvalidElementId;
        }

        private static bool IsValidId(ElementId id)
        {
            return id != null && id != ElementId.InvalidElementId;
        }

        private static int NormalizeLineWeight(int lineWeight)
        {
            if (lineWeight == OverrideGraphicSettings.InvalidPenNumber)
            {
                return OverrideGraphicSettings.InvalidPenNumber;
            }

            if (lineWeight < 1)
            {
                return OverrideGraphicSettings.InvalidPenNumber;
            }

            if (lineWeight > 16)
            {
                return 16;
            }

            return lineWeight;
        }

        private static int ClampTransparency(int transparency)
        {
            if (transparency < 0)
            {
                return 0;
            }

            if (transparency > 100)
            {
                return 100;
            }

            return transparency;
        }
    }
}
