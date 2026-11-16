// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.1.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Selecting elements of this model AND elements inside a link at once. Revit 2023+ can select a linked element
// (Selection.SetReferences); Revit 2020-2022 cannot, so there only this model's elements are selected and the
// caller says so. The one place this release difference lives.

using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HeronTools.Utils
{
    internal static class LinkSelection
    {
        /// <summary>True when this Revit can select elements inside a link (2023 and later).</summary>
        internal static bool CanSelectLinked
        {
            get
            {
#if REVIT2023_OR_GREATER
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Selects <paramref name="hostIds"/> (this model) and <paramref name="linked"/> (element, its link instance).
        /// Returns how many linked elements could not be selected (all of them on Revit 2020-2022).
        /// </summary>
        internal static int Select(UIDocument uiDoc, IList<ElementId> hostIds, IList<KeyValuePair<Element, RevitLinkInstance>> linked)
        {
#if REVIT2023_OR_GREATER
            if (linked.Count > 0)
            {
                var references = new List<Reference>(hostIds.Count + linked.Count);
                Document doc = uiDoc.Document;
                foreach (ElementId id in hostIds)
                {
                    Element e = doc.GetElement(id);
                    if (e != null)
                        references.Add(new Reference(e));
                }
                int missed = 0;
                foreach (KeyValuePair<Element, RevitLinkInstance> pair in linked)
                {
                    Reference inLink = pair.Key != null && pair.Value != null ? new Reference(pair.Key).CreateLinkReference(pair.Value) : null;
                    if (inLink != null) references.Add(inLink);
                    else missed++;
                }
                uiDoc.Selection.SetReferences(references);
                return missed;
            }
            uiDoc.Selection.SetElementIds(hostIds);
            return 0;
#else
            uiDoc.Selection.SetElementIds(hostIds);
            return linked.Count;
#endif
        }
    }
}
