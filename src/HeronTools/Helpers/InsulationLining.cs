// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Insulation and lining go with their duct or pipe: colour or reset a duct and its jacket must follow, or the
// new look sits inside a jacket of the old one. One copy of the rule, for every tool that works on a selection.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace HeronTools.Utils
{
    internal static class InsulationLining
    {
        /// <summary>
        /// Insulation and lining follow their duct or pipe: picking the wrap takes the host too, and a
        /// duct/pipe takes its insulation and lining with it (Highlight Selection, Reset Selected, Reset Graphics).
        /// </summary>
        internal static List<ElementId> WithWraps(
            Document doc,
            List<ElementId> selectedIds,
            HashSet<long> viewKeys)
        {
            var expandedKeys = new HashSet<long>(selectedIds.Select(id => id.LongValue()));
            var expanded = new List<ElementId>(selectedIds);

            // A selected insulation/lining element pulls in its host (DuctInsulation, DuctLining and
            // PipeInsulation all derive from InsulationLiningBase in 2020-2027).
            foreach (ElementId selectedId in selectedIds)
            {
                if (!(doc.GetElement(selectedId) is InsulationLiningBase wrap))
                    continue;

                ElementId hostId = wrap.HostElementId;
                if (hostId == null || hostId == ElementId.InvalidElementId)
                    continue;

                if (viewKeys.Contains(hostId.LongValue()) && expandedKeys.Add(hostId.LongValue()))
                    expanded.Add(hostId);
            }

            // Every highlighted element pulls in its hosted insulation and lining. Wraps appended in the loop
            // host nothing themselves, so the loop stops at the original count.
            int hostCandidateCount = expanded.Count;
            for (int i = 0; i < hostCandidateCount; i++)
            {
                AddHostedWrapIds(InsulationLiningBase.GetInsulationIds, doc, expanded[i], viewKeys, expandedKeys, expanded);
                AddHostedWrapIds(InsulationLiningBase.GetLiningIds, doc, expanded[i], viewKeys, expandedKeys, expanded);
            }

            return expanded;
        }

        private static void AddHostedWrapIds(
            Func<Document, ElementId, ICollection<ElementId>> getWrapIds,
            Document doc,
            ElementId hostId,
            HashSet<long> viewKeys,
            HashSet<long> expandedKeys,
            List<ElementId> expanded)
        {
            ICollection<ElementId> wrapIds;
            try
            {
                // Revit throws ArgumentException for an element that cannot host insulation/lining; that IS
                // the category test, so it is the one exception caught here.
                wrapIds = getWrapIds(doc, hostId);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return;
            }

            if (wrapIds == null)
                return;

            foreach (ElementId wrapId in wrapIds)
            {
                if (viewKeys.Contains(wrapId.LongValue()) && expandedKeys.Add(wrapId.LongValue()))
                    expanded.Add(wrapId);
            }
        }
    }
}
