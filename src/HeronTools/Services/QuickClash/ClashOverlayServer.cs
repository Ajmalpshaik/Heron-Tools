// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Quick Clash - draws the linked clash elements in colour on top of the view. Revit cannot override one element
// inside a link, so their shapes are drawn by Revit's DirectContext3D service instead: nothing is added to the
// model, nothing goes in Undo, and it disappears when the overlay is switched off.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using HeronTools.Models.QuickClash;
using HeronTools.Utils;

namespace HeronTools.Services.QuickClash
{
    internal sealed class ClashOverlayServer : IDirectContext3DServer
    {
        // 16-bit indices: one buffer holds at most this many vertices (3 per triangle, none shared).
        private const int MaxVerticesPerBuffer = 60000;

        // Drawn 2 mm outside the real faces, so the overlay does not flicker against the grey link drawn in the same place.
        private const double PushOutFeet = 2.0 * Constants.MM_TO_FEET;

        private static readonly Guid ServerGuid = new Guid("5E8B1C2A-3F47-4D0B-9C61-7A2E4B9D1F03");

        private readonly object _lock = new object();
        private readonly List<Solid> _solids = new List<Solid>();
        private List<Buffer> _buffers;
        private Document _document;
        private long _viewId = long.MinValue;
        private ClashColour _colour;
        private Outline _outline;

        private static ClashOverlayServer _instance;

        /// <summary>The one overlay of this Revit session.</summary>
        internal static ClashOverlayServer Instance => _instance ?? (_instance = new ClashOverlayServer());

        private ClashOverlayServer() { }

        // ------------------------------------------------------------------ switching on and off (Revit thread)

        /// <summary>
        /// Shows <paramref name="solids"/> (this model's coordinates) in one view, in one colour. Asking again for the
        /// same solids, view and colour does nothing: the shapes already drawn are kept, not triangulated again.
        /// </summary>
        internal void Show(Document doc, View view, IEnumerable<Solid> solids, ClashColour colour)
        {
            var next = new List<Solid>(solids);
            long viewId = view.Id.LongValue();
            bool on;
            lock (_lock)
            {
                if (_solids.Count > 0 && SameSolids(_solids, next) && viewId == _viewId &&
                    _document != null && _document.Equals(doc) && colour.SameAs(_colour))
                    return;     // nothing changed: already drawn

                DisposeBuffers();
                _solids.Clear();
                _solids.AddRange(next);
                _document = doc;
                _viewId = viewId;
                _colour = colour;
                _outline = OutlineOf(_solids);
                on = _solids.Count > 0;
            }
            Register(on);
        }

        /// <summary>Removes the overlay everywhere.</summary>
        internal void Hide()
        {
            lock (_lock)
            {
                DisposeBuffers();
                _solids.Clear();
                _document = null;
                _viewId = long.MinValue;
            }
            Register(false);
        }

        /// <summary>The same solid objects in the same order - the overlay would draw exactly the same shapes.</summary>
        private static bool SameSolids(List<Solid> a, List<Solid> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }

        /// <summary>Frees the graphics memory of the shapes drawn so far. Called with the lock held.</summary>
        private void DisposeBuffers()
        {
            if (_buffers == null) return;
            foreach (Buffer b in _buffers)
                b.Dispose();
            _buffers = null;
        }

        internal bool IsShowing
        {
            get { lock (_lock) return _solids.Count > 0; }
        }

        private void Register(bool on)
        {
            var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService) as MultiServerService;
            if (service == null)
                return;

            bool registered = service.IsRegisteredServerId(ServerGuid);
            if (on && !registered)
                service.AddServer(this);

            if (service.IsRegisteredServerId(ServerGuid))
            {
                var active = new List<Guid>(service.GetActiveServerIds());
                bool isActive = active.Contains(ServerGuid);
                if (on && !isActive) active.Add(ServerGuid);
                if (!on && isActive) active.Remove(ServerGuid);
                if (on != isActive)
                    service.SetActiveServers(active);
            }
        }

        // ------------------------------------------------------------------ IExternalServer

        public Guid GetServerId() => ServerGuid;
        public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
        public string GetName() => "Heron Tools - Quick Clash";
        public string GetVendorId() => "HERON";
        public string GetDescription() => "Draws Quick Clash's linked clash elements in the clash colour.";

        // ------------------------------------------------------------------ IDirectContext3DServer

        public string GetApplicationId() => "";
        public string GetSourceId() => "";
        public bool UsesHandles() => false;
        public bool UseInTransparentPass(View view) => false;

        public bool CanExecute(View view)
        {
            lock (_lock)
            {
                return _solids.Count > 0 && _document != null && view != null &&
                       view.Id.LongValue() == _viewId && view.Document.Equals(_document);
            }
        }

        public Outline GetBoundingBox(View view)
        {
            lock (_lock) return _outline;
        }

        public void RenderScene(View view, DisplayStyle displayStyle)
        {
            try
            {
                // Drawn with the lock held, so Show or Hide can never free a buffer while it is being drawn.
                lock (_lock)
                {
                    if (_buffers == null)
                    {
                        // Empty first: if Revit refuses the shapes, the next frame does not triangulate them all again.
                        _buffers = new List<Buffer>();
                        _buffers = Build(_solids, _colour);
                    }
                    foreach (Buffer b in _buffers)
                    {
                        DrawContext.FlushBuffer(b.Vertices, b.VertexCount, b.Indices, b.IndexCount,
                            b.Format, b.Effect, PrimitiveType.TriangleList, 0, b.TriangleCount);
                    }
                }
            }
            catch (Exception)
            {
                // Drawing must never take Revit down: a failed frame simply shows no overlay.
                // (Broad on purpose: this runs inside Revit's own redraw, where nothing may escape.)
            }
        }

        // ------------------------------------------------------------------ buffers

        private sealed class Buffer
        {
            public VertexBuffer Vertices;
            public IndexBuffer Indices;
            public VertexFormat Format;
            public EffectInstance Effect;
            public int VertexCount;
            public int IndexCount;
            public int TriangleCount;

            /// <summary>All four are IDisposable in Revit 2020-2027: their graphics memory is freed here, not left to the GC.</summary>
            public void Dispose()
            {
                Vertices?.Dispose();
                Indices?.Dispose();
                Format?.Dispose();
                Effect?.Dispose();
            }
        }

        private static List<Buffer> Build(List<Solid> solids, ClashColour colour)
        {
            var color = new ColorWithTransparency(colour.R, colour.G, colour.B, 0);
            var points = new List<VertexPositionNormalColored>();
            foreach (Solid solid in solids)
            {
                foreach (Face face in solid.Faces)
                {
                    Mesh mesh;
                    try { mesh = face.Triangulate(); }
                    catch (Autodesk.Revit.Exceptions.ApplicationException) { continue; }
                    if (mesh == null) continue;
                    for (int i = 0; i < mesh.NumTriangles; i++)
                    {
                        MeshTriangle t = mesh.get_Triangle(i);
                        XYZ p0 = t.get_Vertex(0), p1 = t.get_Vertex(1), p2 = t.get_Vertex(2);
                        XYZ n = (p1 - p0).CrossProduct(p2 - p0);
                        if (n.GetLength() < 1e-12) continue;
                        n = n.Normalize();
                        // The triangle's winding is not promised to face outward; the face's own normal is.
                        XYZ outward = OutwardNormal(face, (p0 + p1 + p2) / 3);
                        if (outward != null && outward.DotProduct(n) < 0)
                            n = n.Negate();
                        XYZ push = n * PushOutFeet;
                        points.Add(new VertexPositionNormalColored(p0 + push, n, color));
                        points.Add(new VertexPositionNormalColored(p1 + push, n, color));
                        points.Add(new VertexPositionNormalColored(p2 + push, n, color));
                    }
                }
            }

            var buffers = new List<Buffer>();
            bool built = false;
            try
            {
                AddBuffers(points, buffers);
                built = true;
            }
            finally
            {
                if (!built)
                    foreach (Buffer b in buffers) b.Dispose();   // a half-built set is freed, never leaked
            }
            return buffers;
        }

        private static void AddBuffers(List<VertexPositionNormalColored> points, List<Buffer> buffers)
        {
            for (int start = 0; start < points.Count; start += MaxVerticesPerBuffer)
            {
                int count = Math.Min(MaxVerticesPerBuffer, points.Count - start);
                int triangles = count / 3;
                if (triangles == 0) break;
                count = triangles * 3;

                int vertexFloats = VertexPositionNormalColored.GetSizeInFloats() * count;
                int indexShorts = IndexTriangle.GetSizeInShortInts() * triangles;
                // Listed before anything is made, so a failure part-way still frees what was made.
                var buffer = new Buffer { VertexCount = count, IndexCount = indexShorts, TriangleCount = triangles };
                buffers.Add(buffer);

                buffer.Vertices = new VertexBuffer(vertexFloats);
                buffer.Vertices.Map(vertexFloats);
                VertexStreamPositionNormalColored vs = buffer.Vertices.GetVertexStreamPositionNormalColored();
                for (int i = 0; i < count; i++)
                    vs.AddVertex(points[start + i]);
                buffer.Vertices.Unmap();

                buffer.Indices = new IndexBuffer(indexShorts);
                buffer.Indices.Map(indexShorts);
                IndexStreamTriangle ist = buffer.Indices.GetIndexStreamTriangle();
                for (int i = 0; i < triangles; i++)
                    ist.AddTriangle(new IndexTriangle(i * 3, i * 3 + 1, i * 3 + 2));
                buffer.Indices.Unmap();

                buffer.Format = new VertexFormat(VertexFormatBits.PositionNormalColored);
                buffer.Effect = new EffectInstance(VertexFormatBits.PositionNormalColored);
            }
        }

        /// <summary>The face's outward normal near a point, or null if Revit cannot give one there.</summary>
        private static XYZ OutwardNormal(Face face, XYZ point)
        {
            try
            {
                IntersectionResult onFace = face.Project(point);
                return onFace != null ? face.ComputeNormal(onFace.UVPoint) : null;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
        }

        private static Outline OutlineOf(List<Solid> solids)
        {
            Outline outline = null;
            foreach (Solid solid in solids)
            {
                BoundingBoxXYZ box = solid.GetBoundingBox();
                Transform t = box.Transform ?? Transform.Identity;
                foreach (double x in new[] { box.Min.X, box.Max.X })
                foreach (double y in new[] { box.Min.Y, box.Max.Y })
                foreach (double z in new[] { box.Min.Z, box.Max.Z })
                {
                    XYZ p = t.OfPoint(new XYZ(x, y, z));
                    if (outline == null) outline = new Outline(p, p);
                    else outline.AddPoint(p);
                }
            }
            return outline ?? new Outline(XYZ.Zero, XYZ.Zero);
        }
    }
}
