// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Quick Clash - one element taking part in a test: its box (always), its solids (read only when a box overlap
// asks for them) and the MEP elements it is connected to. Everything is in this model's coordinates.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using HeronTools.Utils;

namespace HeronTools.Services.QuickClash
{
    internal sealed class ClashItem
    {
        private static readonly Options GeometryOptions = new Options
        {
            DetailLevel = ViewDetailLevel.Fine,
            ComputeReferences = false,
            IncludeNonVisibleObjects = false
        };

        private List<SolidPart> _solids;
        private HashSet<long> _connected;

        private ClashItem(Element element, Transform toHost, long linkInstanceId, XYZ min, XYZ max)
        {
            Element = element;
            ToHost = toHost;
            LinkInstanceId = linkInstanceId;
            Id = element.Id.LongValue();
            Min = min;
            Max = max;
        }

        public Element Element { get; }
        public long Id { get; }

        /// <summary>-1 for this model.</summary>
        public long LinkInstanceId { get; }

        /// <summary>Null for this model; the link's total transform otherwise.</summary>
        public Transform ToHost { get; }

        public XYZ Min { get; }
        public XYZ Max { get; }

        /// <summary>The element's box in this model's coordinates, or null when it has no box (nothing to test).</summary>
        public static ClashItem Create(Element element, Transform toHost, long linkInstanceId)
        {
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null || !box.Enabled)
                return null;

            Transform t = box.Transform ?? Transform.Identity;
            if (toHost != null)
                t = toHost.Multiply(t);

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (double x in new[] { box.Min.X, box.Max.X })
            foreach (double y in new[] { box.Min.Y, box.Max.Y })
            foreach (double z in new[] { box.Min.Z, box.Max.Z })
            {
                XYZ p = t.OfPoint(new XYZ(x, y, z));
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
            }
            return new ClashItem(element, toHost, linkInstanceId, new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
        }

        /// <summary>The element's solids with volume, in this model's coordinates, each with its box. Read once, then kept.</summary>
        public List<SolidPart> Solids
        {
            get
            {
                if (_solids != null)
                    return _solids;
                var raw = new List<Solid>();
                try
                {
                    Collect(Element.get_Geometry(GeometryOptions), raw);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // An element Revit cannot give geometry for is simply not tested.
                }
                _solids = new List<SolidPart>(raw.Count);
                foreach (Solid solid in raw)
                {
                    try
                    {
                        Solid placed = ToHost != null ? SolidUtils.CreateTransformed(solid, ToHost) : solid;
                        _solids.Add(SolidPart.Create(placed));
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                        // a solid Revit cannot move is left out; the element's other solids are still tested
                    }
                }
                return _solids;
            }
        }

        /// <summary>Ids of the elements joined to this one through MEP connectors (same model only).</summary>
        public HashSet<long> ConnectedIds
        {
            get
            {
                if (_connected != null)
                    return _connected;
                _connected = new HashSet<long>();

                // Insulation and lining sit around their own duct or pipe - always "clashing" with it.
                if (Element is InsulationLiningBase wrap && wrap.HostElementId != null)
                    _connected.Add(wrap.HostElementId.LongValue());

                ConnectorManager manager = null;
                if (Element is MEPCurve curve)
                    manager = curve.ConnectorManager;
                else if (Element is FamilyInstance fi && fi.MEPModel != null)
                    manager = fi.MEPModel.ConnectorManager;
                if (manager == null)
                    return _connected;

                try
                {
                    foreach (Connector connector in manager.Connectors)
                    {
                        if (!connector.IsConnected)
                            continue;
                        foreach (Connector other in connector.AllRefs)
                        {
                            if (other?.Owner != null && other.Owner.Id.LongValue() != Id)
                                _connected.Add(other.Owner.Id.LongValue());
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // Some families report connectors they cannot list; they are then simply not skipped.
                }
                return _connected;
            }
        }

        private static void Collect(GeometryElement geometry, List<Solid> into)
        {
            if (geometry == null)
                return;
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Solid solid)
                {
                    if (solid.Faces.Size > 0 && solid.Volume > 1e-9)
                        into.Add(solid);
                }
                else if (obj is GeometryInstance instance)
                {
                    Collect(instance.GetInstanceGeometry(), into);
                }
            }
        }
    }

    /// <summary>One solid of an element and its box, both in this model's coordinates.</summary>
    internal sealed class SolidPart
    {
        private SolidPart(Solid solid, XYZ min, XYZ max) { Solid = solid; Min = min; Max = max; }

        public Solid Solid { get; }
        public XYZ Min { get; }
        public XYZ Max { get; }

        public static SolidPart Create(Solid solid)
        {
            BoundingBoxXYZ box = solid.GetBoundingBox();
            Transform t = box.Transform ?? Transform.Identity;
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (double x in new[] { box.Min.X, box.Max.X })
            foreach (double y in new[] { box.Min.Y, box.Max.Y })
            foreach (double z in new[] { box.Min.Z, box.Max.Z })
            {
                XYZ p = t.OfPoint(new XYZ(x, y, z));
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
            }
            return new SolidPart(solid, new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
        }
    }
}
