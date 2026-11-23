// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.3.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Where a grid or level comes from: the open model, or a loaded Revit link shown in the view - and how to
// turn its reference into one a dimension in the open model can use.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using HeronTools.Utils;

namespace HeronTools.Services.AutoDimension
{
    internal sealed class DatumSource
    {
        private DatumSource() { }

        /// <summary>The document the datums are read from (the open model or a link's).</summary>
        internal Document Document { get; private set; }

        internal Document HostDocument { get; private set; }

        /// <summary>Null for the open model.</summary>
        internal RevitLinkInstance Link { get; private set; }

        /// <summary>Identity for the open model; the link's total transform otherwise.</summary>
        internal Transform ToHost { get; private set; }

        internal bool IsLinked => Link != null;

        internal string DisplayName { get; private set; }

        internal static DatumSource ForHost(Document doc)
        {
            return new DatumSource { Document = doc, HostDocument = doc, ToHost = Transform.Identity, DisplayName = "this model" };
        }

        /// <summary>
        /// One source per loaded link that is SHOWN in this view. A link hidden in the view (or unloaded) gives
        /// nothing - its grids are not on the drawing, so they must not be dimensioned.
        /// </summary>
        internal static IList<DatumSource> LinksShownIn(Document doc, View view)
        {
            var sources = new List<DatumSource>();
            IEnumerable<RevitLinkInstance> links;
            try
            {
                links = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .ToList();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return sources;
            }

            foreach (RevitLinkInstance link in links)
            {
                Document linkDoc = null;
                Transform toHost = null;
                try
                {
                    linkDoc = link.GetLinkDocument();
                    // GetTotalTransform, not GetTransform: only this one includes a nested link's own placement.
                    toHost = link.GetTotalTransform();
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    linkDoc = null;
                }

                if (linkDoc == null || toHost == null)
                    continue;

                sources.Add(new DatumSource
                {
                    Document = linkDoc,
                    HostDocument = doc,
                    Link = link,
                    ToHost = toHost,
                    DisplayName = CleanName(linkDoc.Title)
                });
            }

            return sources;
        }

        /// <summary>
        /// A reference to the datum that a dimension in the open model accepts. Null when Revit will not give one.
        /// </summary>
        /// <remarks>
        /// For a linked datum, CreateLinkReference alone is refused by NewDimension. The reference is rebuilt from
        /// its stable representation with "RVTLINK/&lt;link type id&gt;" reduced to "RVTLINK". That text is Revit's
        /// internal format with no promise it stays the same, so every step is guarded and the plain link
        /// reference is the fallback (a refusal is then reported for that row, nothing else is affected).
        /// </remarks>
        internal Reference ReferenceFor(Element datum)
        {
            Reference own;
            try
            {
                own = new Reference(datum);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }

            if (!IsLinked)
                return own;

            Reference linkReference;
            try
            {
                linkReference = own.CreateLinkReference(Link);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }

            return Rebuild(linkReference) ?? linkReference;
        }

        /// <summary>"link instance id:element id" - the same key an existing dimension's reference gives.</summary>
        internal string KeyFor(ElementId datumId)
        {
            long linkPart = IsLinked ? Link.Id.LongValue() : 0L;
            return linkPart.ToString(CultureInfo.InvariantCulture) + ":" +
                   datumId.LongValue().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The key of what an existing dimension's reference points at (see <see cref="KeyFor"/>).</summary>
        internal static string KeyOf(Reference reference)
        {
            if (reference == null)
                return string.Empty;

            ElementId owner = reference.ElementId;
            ElementId linked = reference.LinkedElementId;
            bool isLink = linked != null && linked != ElementId.InvalidElementId;
            long linkPart = isLink && owner != null ? owner.LongValue() : 0L;
            long elementPart = isLink ? linked.LongValue() : (owner == null ? -1L : owner.LongValue());
            return linkPart.ToString(CultureInfo.InvariantCulture) + ":" + elementPart.ToString(CultureInfo.InvariantCulture);
        }

        private Reference Rebuild(Reference linkReference)
        {
            try
            {
                string text = linkReference.ConvertToStableRepresentation(HostDocument);
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                var rebuilt = new StringBuilder();
                foreach (string part in text.Split(':'))
                {
                    string piece = part;
                    if (piece.IndexOf("RVTLINK", StringComparison.Ordinal) >= 0)
                        piece = rebuilt.ToString().EndsWith(":0", StringComparison.Ordinal) ? "RVTLINK" : "0:RVTLINK";
                    if (rebuilt.Length > 0)
                        rebuilt.Append(':');
                    rebuilt.Append(piece);
                }

                string result = rebuilt.ToString();
                return result == text ? null : Reference.ParseFromStableRepresentation(HostDocument, result);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string CleanName(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "a linked model";
            int colon = title.IndexOf(':');
            return (colon > 0 ? title.Substring(0, colon) : title).Trim();
        }
    }
}
