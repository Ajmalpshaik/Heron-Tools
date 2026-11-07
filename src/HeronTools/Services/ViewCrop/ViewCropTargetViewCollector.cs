// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Collects project views for the View Crop target-view picker and maps each to its sheet.

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using HeronTools.Models.ViewCrop;
using HeronTools.Utils;

namespace HeronTools.Services.ViewCrop
{
    /// <summary>
    /// Collects project views and maps each to its sheet context where available.
    /// </summary>
    internal static class ViewCropTargetViewCollector
    {
        private sealed class SheetInfo
        {
            internal string Number { get; set; }
            internal string Name { get; set; }
        }

        internal static IList<ViewCropTargetViewItem> Collect(Document doc, ElementId preselectViewId)
        {
            if (doc == null)
                return new List<ViewCropTargetViewItem>();

            Dictionary<long, SheetInfo> sheetMap = BuildSheetMap(doc);
            long preselectKey = preselectViewId != null
                ? (preselectViewId).LongValue()
                : (ElementId.InvalidElementId).LongValue();

            // Only collect views the tool can actually crop (Floor / Ceiling / Engineering / Area plans).
            // Unsupported types (3D, section, elevation, sheets, legends, schedules, etc.) are not listed.
            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v != null && !v.IsTemplate && ViewCropViewSupport.IsSupportedViewType(v.ViewType))
                .OrderBy(v => ViewCropViewSupport.ToFriendlyTypeName(v.ViewType))
                .ThenBy(v => v.Name)
                .ToList();

            var items = new List<ViewCropTargetViewItem>(views.Count);
            foreach (View view in views)
            {
                long viewKey = (view.Id).LongValue();

                string groupName = "Unplaced Views";
                string sheetNumber = string.Empty;
                string sheetName = string.Empty;

                if (sheetMap.TryGetValue(viewKey, out SheetInfo sheet))
                {
                    sheetNumber = sheet.Number;
                    sheetName = sheet.Name;
                    groupName = $"Sheet {sheetNumber} - {sheetName}";
                }

                items.Add(new ViewCropTargetViewItem
                {
                    ViewId = view.Id,
                    ViewName = view.Name,
                    ViewTypeName = ViewCropViewSupport.ToFriendlyTypeName(view),
                    SheetNumber = sheetNumber,
                    SheetName = sheetName,
                    GroupName = groupName,
                    IsSelected = ElementIdHelper.IsValid(preselectViewId) && preselectKey == viewKey
                });
            }

            return items;
        }

        private static Dictionary<long, SheetInfo> BuildSheetMap(Document doc)
        {
            var map = new Dictionary<long, SheetInfo>();

            IList<ViewSheet> sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            foreach (ViewSheet sheet in sheets)
            {
                if (sheet == null)
                    continue;

                string number = sheet.SheetNumber ?? string.Empty;
                string name = sheet.Name ?? string.Empty;
                var sheetInfo = new SheetInfo { Number = number, Name = name };

                ICollection<ElementId> viewportIds = sheet.GetAllViewports();
                foreach (ElementId viewportId in viewportIds)
                {
                    Viewport viewport = doc.GetElement(viewportId) as Viewport;
                    if (viewport == null || viewport.ViewId == null)
                        continue;

                    long viewInt = (viewport.ViewId).LongValue();
                    if (!map.ContainsKey(viewInt))
                    {
                        map.Add(viewInt, sheetInfo);
                    }
                    else
                    {
                        // Placed on more than one sheet (a legend). The record in the map is shared with
                        // every other view on its first sheet, so never edit it - give this view its own.
                        SheetInfo existing = map[viewInt];
                        map[viewInt] = new SheetInfo { Number = existing.Number + "+", Name = existing.Name };
                    }
                }
            }

            return map;
        }
    }
}
