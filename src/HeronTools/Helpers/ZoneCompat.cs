// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Version-safe "which spaces are in this zone". The HVAC Zone (Mechanical.Zone) lists them in Zone.Spaces in
// every Revit year; Revit 2027 marks that obsolete in favour of GenericZone (new in Revit 2026), whose
// space-based zones list them with GetSpaceIds(). Both kinds are read, so a zone works in any year.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

namespace HeronTools.Utils
{
    internal static class ZoneCompat
    {
        /// <summary>
        /// True when <paramref name="element"/> is a zone; <paramref name="spaceIds"/> then holds its spaces
        /// (empty for a zone with no spaces, or a sketch-based generic zone, which has none).
        /// </summary>
        internal static bool TryGetSpaceIds(Element element, out IList<ElementId> spaceIds)
        {
            spaceIds = new List<ElementId>();

            if (element is Zone zone)
            {
                // Zone.Spaces is obsolete from Revit 2027 only because GenericZone is the new way; an existing
                // HVAC zone still answers it, and it is the only way before 2026. Kept here, in one place.
#pragma warning disable CS0618
                SpaceSet spaces = zone.Spaces;
#pragma warning restore CS0618
                if (spaces != null)
                {
                    foreach (Space space in spaces)
                    {
                        if (space != null)
                            spaceIds.Add(space.Id);
                    }
                }
                return true;
            }

#if REVIT2026_OR_GREATER
            if (element is Autodesk.Revit.DB.Analysis.GenericZone generic)
            {
                try
                {
                    foreach (ElementId id in generic.GetSpaceIds())
                    {
                        if (id != null && id != ElementId.InvalidElementId)
                            spaceIds.Add(id);
                    }
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    // A sketch-based generic zone has no spaces ("must be space-based" in the API notes).
                }
                catch (InvalidOperationException)
                {
                    // Same, if Revit reports it with the .NET exception.
                }
                return true;
            }
#endif

            return false;
        }
    }
}
