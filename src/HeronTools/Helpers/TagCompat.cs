// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Version-safe reading of what an IndependentTag tags. Revit 2022 made tags able to tag several elements
// (GetTaggedLocalElementIds); Revit 2020-2021 have only the single TaggedLocalElementId, and 2024 removed it.

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace HeronTools.Utils
{
    internal static class TagCompat
    {
        /// <summary>The ids of the elements in THIS model that the tag tags (empty for a tag on a linked element).</summary>
        internal static IList<ElementId> TaggedLocalElementIds(IndependentTag tag)
        {
            var ids = new List<ElementId>();
            if (tag == null)
                return ids;

#if REVIT2022_OR_GREATER
            ICollection<ElementId> tagged = tag.GetTaggedLocalElementIds();
            if (tagged != null)
            {
                foreach (ElementId id in tagged)
                {
                    if (id != null && id != ElementId.InvalidElementId)
                        ids.Add(id);
                }
            }
#else
            ElementId id = tag.TaggedLocalElementId;
            if (id != null && id != ElementId.InvalidElementId)
                ids.Add(id);
#endif
            return ids;
        }
    }
}
