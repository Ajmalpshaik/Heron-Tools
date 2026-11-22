// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - the third-person character: a site worker in a yellow hard hat, 1850 mm tall, drawn INSIDE the walk view
// with Revit's own "draw your own 3D graphics" service (DirectContext3D), so walls, ducts and slabs hide it when it walks
// behind them. It stands when you stop, walks when you move and runs with Shift (WalkCharacterAnimator). Nothing is added
// to the model: no element, no transaction, no Undo entry - screen graphics only, in the walk view only.
//
// The character is read once per Revit session from HeronTools.dll (WalkCharacterModel). There is no fallback figure:
// if it cannot be read, third person still works (the camera behind you), nothing is drawn, and the walk says so once.
//
// Ported from AJ-Tools Game Mode v1.61.0 (GameCharacterServer.cs v1.1.0, commit ab98e2e), read only, never changed.
// Heron changes: no blocky fallback figure; the server is switched on only while a walk runs in third person and
// switched off on every end path; the walk view is matched by ElementId AND its document (ids repeat across models);
// Revit's buffers are kept between redraws and rebuilt only when the pose changed or Revit invalidated them.
//
// The Revit API rule this follows (ExternalService.RemoveServer, checked in the 2020 to 2027 API notes, identical):
// a server that has drawn in a model that is still open CANNOT be removed until that model closes. So "switch off"
// means: the character is hidden (CanExecute says no), the server is taken off the active list, and it is removed as
// soon as Revit allows - at the end of the walk, when a model closes, or when Revit closes. A switched-off server
// draws nothing anywhere. Every member used here exists with the same signature in Revit 2020 to 2027.
//
// Colours: the character's own plain colours from its file (shirt, safety vest, trousers, yellow hard hat), with its
// black skin and white eyes swapped for a skin tone and dark eyes. These are model-view graphics like Colorize's
// palette, not window colours: CONTRIBUTING rule 6 and HeronStyles.xaml govern window colours.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.UI;

namespace HeronTools.Services.ModelWalk
{
    /// <summary>Where the character stands this frame and which way it faces. Written by the engine, read by the server.</summary>
    internal sealed class WalkCharacterPose
    {
        /// <summary>True only while a walk runs in third person (and no snag picture is being taken).</summary>
        public bool Visible;

        /// <summary>The walk view and its model - the character is drawn there and nowhere else.</summary>
        public Document Document;
        public ElementId ViewId;

        /// <summary>Point between the feet, on the floor, in Revit internal feet.</summary>
        public XYZ Feet = XYZ.Zero;

        /// <summary>Facing direction, radians from +X towards +Y (the walk's yaw).</summary>
        public double YawRad;
    }

    /// <summary>Draws the third-person character in the walk view (Revit-native 3D graphics, no model change).</summary>
    internal sealed class WalkCharacterServer : IDirectContext3DServer
    {
        private static readonly Guid ServerGuid = new Guid("125599D2-21BD-439F-B3C5-6F354D653035");
        private static WalkCharacterServer _instance;
        private static ExternalEvent _switchOffEvent;
        private static bool _watching;
        private static bool _switchOffPending;

        /// <summary>The pose the next redraw uses (engine writes, server reads - both on Revit's thread).</summary>
        internal static readonly WalkCharacterPose Pose = new WalkCharacterPose();

        /// <summary>True from the frame the character is switched on until it is switched off again.</summary>
        internal static bool InUse { get; private set; }

        /// <summary>Height of the character, to the top of the hard hat.</summary>
        internal const double CharacterHeightMm = 1850.0;

        // The character (null = not read yet, or it could not be read). Read once per Revit session.
        private static WalkCharacterAnimator _animator;
        private static bool _loadTried;

        /// <summary>Why there is no character (null while it is fine). Plain words, for the walk's message.</summary>
        internal static string CharacterProblem { get; private set; }

        // Changes every walk frame the pose moved on; a redraw rebuilds the vertex buffers only when it changed.
        private static int _poseVersion;

        // Revit's buffers, kept between redraws (one set per colour part). Revit can invalidate them at any time, so each
        // is tested with IsValid() before use and made again if needed (the API notes ask for exactly that).
        private static PartBuffers[] _buffers;
        private static VertexFormat _format;

        private sealed class PartBuffers
        {
            public VertexBuffer Vertices;
            public int VerticesVersion = int.MinValue;
            public IndexBuffer Indices;
            public EffectInstance Effect;
            public Color Colour;
        }

        // The file's skin is black and its eyes white (by material name): swapped so the face reads as a face.
        private static readonly Dictionary<string, Color> ColourSwaps = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "Skin", new Color(224, 182, 142) },
            { "Face", new Color(38, 30, 26) }
        };

        private WalkCharacterServer()
        {
        }

        // ------------------------------------------------------------------ the character (Revit's thread, plain .NET)

        /// <summary>
        /// Reads the character the first time it is needed (about 40 ms, once per Revit session). True when there is one to
        /// draw; false with <see cref="CharacterProblem"/> saying why. Never throws.
        /// </summary>
        internal static bool EnsureCharacter()
        {
            if (_loadTried)
                return _animator != null;
            _loadTried = true;
            try
            {
                WalkCharacterModel model = WalkCharacterModel.TryLoad(out string problem);
                if (model != null)
                {
                    _animator = new WalkCharacterAnimator(model, WalkTuning.MmToFt(CharacterHeightMm));
                    _buffers = new PartBuffers[model.Parts.Length];
                    for (int p = 0; p < _buffers.Length; p++)
                        _buffers[p] = new PartBuffers { Colour = PartColour(model.Materials[model.Parts[p].Material]) };
                }
                CharacterProblem = model != null ? null : problem;
            }
            catch (Exception ex)
            {
                // Broad on purpose: a bad character file only means "no character" - never a broken walk.
                _animator = null;
                CharacterProblem = "the character could not be set up (" + ex.Message + ")";
            }
            return _animator != null;
        }

        /// <summary>
        /// Moves the character's animation on by one walk frame (stand / walk / run to match the player's horizontal speed).
        /// Returns true while the pose keeps changing by itself (walking, running, blending), so the engine keeps frames
        /// and redraws coming; false once it stands. Never throws - a failure only loses the character.
        /// </summary>
        internal static bool Advance(double dt, double speedFtPerSec, bool running)
        {
            WalkCharacterAnimator animator = _animator;
            if (animator == null)
                return false;
            try
            {
                animator.Advance(dt, speedFtPerSec, running);
                _poseVersion++;
                return animator.IsMoving;
            }
            catch (Exception ex)
            {
                // Broad on purpose: a maths fault in the animation must not end the walk; the camera keeps working.
                _animator = null;
                CharacterProblem = "the character's animation failed (" + ex.Message + ")";
                Pose.Visible = false;
                return false;
            }
        }

        /// <summary>
        /// Puts the character back to standing (start of Idle, no blend) - called on the first third-person frame of a
        /// walk or after Q, so he never finishes a clip left over from the last time. Never throws.
        /// </summary>
        internal static void ResetPose()
        {
            WalkCharacterAnimator animator = _animator;
            if (animator == null)
                return;
            try
            {
                animator.Reset();
                _poseVersion++;
            }
            catch (Exception ex)
            {
                // Broad on purpose, as in Advance: a maths fault only loses the character, never the walk.
                _animator = null;
                CharacterProblem = "the character's animation failed (" + ex.Message + ")";
                Pose.Visible = false;
            }
        }

        /// <summary>True when a character was read and can be drawn.</summary>
        internal static bool HasCharacter => _animator != null;

        // ------------------------------------------------------------------ switch on / off (Revit's thread only)

        /// <summary>
        /// Once per Revit session, at the start of a walk (a Revit command - a valid API context): makes the event that
        /// switches the character off from the overlay's own end paths, and watches models closing.
        /// </summary>
        internal static void Prepare(UIApplication uiApp)
        {
            if (_switchOffEvent == null)
                _switchOffEvent = ExternalEvent.Create(new SwitchOffHandler());
            if (_watching || uiApp == null)
                return;
            _watching = true;
            // A model closing while its walk shows the character: hide it first (the walk ends on its next frame).
            uiApp.Application.DocumentClosing += (s, e) =>
            {
                if (Pose.Document != null && Pose.Document.Equals(e.Document))
                    SwitchOff();
            };
            // Revit allows the removal only once no open model has used the server: try again after each close.
            uiApp.Application.DocumentClosed += (s, e) =>
            {
                if (!InUse)
                    TryRemove();
            };
        }

        /// <summary>
        /// Switches the character on (a walk frame - Revit's thread). Registers the server the first time and puts it on the
        /// active list. Returns false when Revit refuses; the walk then stays in first person. Never throws.
        /// </summary>
        internal static bool SwitchOn()
        {
            try
            {
                MultiServerService service = Service();
                if (service == null)
                    return false;
                if (_instance == null)
                    _instance = new WalkCharacterServer();
                if (!service.IsRegisteredServerId(ServerGuid))
                    service.AddServer(_instance);

                var active = new List<Guid>(service.GetActiveServerIds());
                if (!active.Contains(ServerGuid))
                {
                    active.Add(ServerGuid);
                    service.SetActiveServers(active);
                }
                InUse = true;
                _switchOffPending = false;   // a new walk: a switch-off asked for by the last one is no longer wanted
                return true;
            }
            catch (Exception)
            {
                // Broad on purpose: a refusal of Revit's graphics service only means no character - never a broken walk.
                Pose.Visible = false;
                return false;
            }
        }

        /// <summary>
        /// Switches the character off on Revit's thread (the end of a walk, a model closing, Revit closing): hidden at once,
        /// taken off the active list, and removed if Revit allows it now. Never throws.
        /// </summary>
        internal static void SwitchOff()
        {
            Pose.Visible = false;
            Pose.Document = null;
            Pose.ViewId = null;
            InUse = false;
            _switchOffPending = false;
            try
            {
                MultiServerService service = Service();
                if (service != null && service.IsRegisteredServerId(ServerGuid))
                {
                    var active = new List<Guid>(service.GetActiveServerIds());
                    if (active.Remove(ServerGuid))
                        service.SetActiveServers(active);
                }
            }
            catch (Exception)
            {
                // Hidden already (CanExecute says no), so an inactive-list refusal changes nothing on screen.
            }
            TryRemove();
        }

        /// <summary>
        /// From the overlay's own end paths (overlay closed, Revit busy, an overlay fault) - outside Revit's API: the
        /// character is hidden at once (plain .NET), and the full switch-off runs on Revit's thread at its next idle moment.
        /// </summary>
        internal static void SwitchOffSoon()
        {
            Pose.Visible = false;
            if (!InUse)
                return;
            _switchOffPending = true;
            try
            {
                _switchOffEvent?.Raise();
            }
            catch (Exception)
            {
                // Hidden already; the next model close, the next walk's end or Revit closing switches it off.
            }
        }

        /// <summary>Removes the server when Revit allows it (no open model has used it). A refusal is normal - not an error.</summary>
        private static void TryRemove()
        {
            try
            {
                MultiServerService service = Service();
                if (service != null && service.IsRegisteredServerId(ServerGuid))
                    service.RemoveServer(ServerGuid);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // "Already used in a currently open document": it stays registered but inactive and draws nothing.
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // The service is drawing right now: the next model close or walk end tries again.
            }
        }

        private static MultiServerService Service() =>
            ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService) as MultiServerService;

        /// <summary>Runs the switch-off on Revit's thread, unless a new walk has switched the character on again meanwhile.</summary>
        private sealed class SwitchOffHandler : IExternalEventHandler
        {
            public void Execute(UIApplication app)
            {
                if (!_switchOffPending)
                    return;   // already switched off, or a new walk switched it on again meanwhile
                Document doc = Pose.Document;   // SwitchOff forgets it
                SwitchOff();
                try
                {
                    // Every open view of the walk's model: the walk view may be on screen without being the active view.
                    if (doc != null && doc.IsValidObject)
                        new UIDocument(doc).UpdateAllOpenViews();
                    else
                        app?.ActiveUIDocument?.RefreshActiveView();
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* the next redraw clears it anyway */ }
            }

            public string GetName() => "Heron Tools - Model Walk character off";
        }

        // ------------------------------------------------------------------ IExternalServer

        public Guid GetServerId() => ServerGuid;

        public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;

        public string GetName() => "Heron Tools - Model Walk character";

        public string GetVendorId() => "HeronTools";

        public string GetDescription() => "Draws the third-person character in Model Walk's walk view. Nothing is added to the model.";

        // ------------------------------------------------------------------ IDirectContext3DServer

        public string GetApplicationId() => string.Empty;

        public string GetSourceId() => string.Empty;

        public bool UsesHandles() => false;

        public bool UseInTransparentPass(View dBView) => false;

        public bool CanExecute(View dBView)
        {
            try
            {
                return Pose.Visible && _animator != null && dBView != null && Pose.ViewId != null && Pose.Document != null &&
                       dBView.Id == Pose.ViewId && dBView.Document.Equals(Pose.Document);
            }
            catch (Exception)
            {
                return false;   // a view half-way through closing: draw nothing
            }
        }

        public Outline GetBoundingBox(View dBView)
        {
            // A loose box round the body - Revit only needs it to cull and fit.
            double reach = WalkTuning.MmToFt(1200.0);
            double height = WalkTuning.MmToFt(2000.0);
            XYZ f = Pose.Feet ?? XYZ.Zero;
            return new Outline(new XYZ(f.X - reach, f.Y - reach, f.Z - WalkTuning.MmToFt(100.0)),
                               new XYZ(f.X + reach, f.Y + reach, f.Z + height));
        }

        public void RenderScene(View dBView, DisplayStyle displayStyle)
        {
            try
            {
                WalkCharacterAnimator animator = _animator;
                if (!Pose.Visible || animator == null || _buffers == null || !DrawContext.IsAvailable())
                    return;

                if (_format == null || !_format.IsValid())
                {
                    _format?.Dispose();
                    _format = new VertexFormat(VertexFormatBits.PositionNormal);
                }

                WalkCharacterModel model = animator.Model;
                for (int p = 0; p < model.Parts.Length; p++)
                {
                    WalkCharacterModel.Part part = model.Parts[p];
                    int vertexCount = part.VertexCount;
                    int triangleCount = part.Indices.Length / 3;
                    if (vertexCount == 0 || triangleCount == 0 || vertexCount > 65535)
                        continue;   // index buffers are 16-bit: a part this big cannot be drawn (the shipped file's biggest is 2,592)

                    PartBuffers b = _buffers[p];
                    // A replaced buffer is disposed at once (all four types are IDisposable in 2020-2027), so a walk
                    // does not leave one graphics buffer per frame for the garbage collector.
                    if (b.Effect == null || !b.Effect.IsValid())
                    {
                        b.Effect?.Dispose();
                        b.Effect = MakeEffect(b.Colour);
                    }
                    if (b.Indices == null || !b.Indices.IsValid())
                    {
                        b.Indices?.Dispose();
                        b.Indices = MakeIndices(part, triangleCount);
                    }
                    if (b.Vertices == null || !b.Vertices.IsValid() || b.VerticesVersion != _poseVersion)
                    {
                        b.Vertices?.Dispose();
                        b.Vertices = null;
                        b.Vertices = MakeVertices(animator, p, vertexCount);
                        b.VerticesVersion = _poseVersion;
                    }

                    DrawContext.FlushBuffer(b.Vertices, vertexCount, b.Indices, triangleCount * IndexTriangle.GetSizeInShortInts(),
                                            _format, b.Effect, PrimitiveType.TriangleList, 0, triangleCount);
                }
            }
            catch (Exception)
            {
                // Broad on purpose: a failed draw only loses the character for one redraw - never Revit's own redraw.
            }
        }

        // ------------------------------------------------------------------ Revit buffers

        /// <summary>
        /// The posed part in Revit's terms: the file's axes (Y up, facing +Z, +X = its left) become the walk's (Z up, facing
        /// the yaw, +Y = its left) - a cyclic swap, so the triangles keep their winding - scaled to 1850 mm, soles on the floor.
        /// </summary>
        private static VertexBuffer MakeVertices(WalkCharacterAnimator animator, int part, int vertexCount)
        {
            float[] pos = animator.Positions[part];
            float[] nor = animator.Normals[part];
            XYZ feet = Pose.Feet ?? XYZ.Zero;
            double cos = Math.Cos(Pose.YawRad);
            double sin = Math.Sin(Pose.YawRad);
            double scale = animator.UnitsToFeet;
            double floor = animator.FloorOffsetUnits;

            int floats = vertexCount * VertexPositionNormal.GetSizeInFloats();
            var buffer = new VertexBuffer(floats);
            buffer.Map(floats);
            VertexStreamPositionNormal stream = buffer.GetVertexStreamPositionNormal();
            for (int v = 0; v < vertexCount; v++)
            {
                int o = v * 3;
                double fx = pos[o + 2] * scale;              // forward = file Z
                double fy = pos[o] * scale;                  // left    = file X
                double fz = (pos[o + 1] - floor) * scale;    // up      = file Y
                double nx = nor[o + 2], ny = nor[o], nz = nor[o + 1];
                var point = new XYZ(feet.X + (fx * cos) - (fy * sin), feet.Y + (fx * sin) + (fy * cos), feet.Z + fz);
                var normal = new XYZ((nx * cos) - (ny * sin), (nx * sin) + (ny * cos), nz);
                stream.AddVertex(new VertexPositionNormal(point, normal));
            }
            buffer.Unmap();
            return buffer;
        }

        /// <summary>The part's triangles - the same every frame, so made once and kept while Revit says it is valid.</summary>
        private static IndexBuffer MakeIndices(WalkCharacterModel.Part part, int triangleCount)
        {
            int shorts = triangleCount * IndexTriangle.GetSizeInShortInts();
            var buffer = new IndexBuffer(shorts);
            buffer.Map(shorts);
            IndexStreamTriangle stream = buffer.GetIndexStreamTriangle();
            int[] idx = part.Indices;
            for (int t = 0; t < triangleCount; t++)
                stream.AddTriangle(new IndexTriangle(idx[t * 3], idx[(t * 3) + 1], idx[(t * 3) + 2]));
            buffer.Unmap();
            return buffer;
        }

        private static EffectInstance MakeEffect(Color colour)
        {
            var effect = new EffectInstance(VertexFormatBits.PositionNormal);
            effect.SetColor(colour);
            effect.SetDiffuseColor(colour);
            effect.SetAmbientColor(Scale(colour, 0.55));
            effect.SetEmissiveColor(Scale(colour, 0.15));
            effect.SetSpecularColor(Scale(colour, 0.12));
            return effect;
        }

        /// <summary>One material's colour: a swap from ColourSwaps, else the file's own colour.</summary>
        private static Color PartColour(WalkCharacterModel.Material material)
        {
            if (material.Name != null && ColourSwaps.TryGetValue(material.Name, out Color swap))
                return swap;
            double[] c = material.BaseColorLinear;
            return new Color(ToScreen(c[0]), ToScreen(c[1]), ToScreen(c[2]));
        }

        /// <summary>glTF colours are linear light; Revit wants screen (sRGB) values 0-255, or everything looks too dark.</summary>
        private static byte ToScreen(double linear)
        {
            linear = Math.Max(0.0, Math.Min(1.0, linear));
            double s = linear <= 0.0031308 ? linear * 12.92 : (1.055 * Math.Pow(linear, 1.0 / 2.4)) - 0.055;
            return (byte)Math.Round(Math.Max(0.0, Math.Min(1.0, s)) * 255.0);
        }

        /// <summary>A darker step of the same colour (for shading only).</summary>
        private static Color Scale(Color c, double factor) =>
            new Color((byte)Math.Min(255, c.Red * factor), (byte)Math.Min(255, c.Green * factor), (byte)Math.Min(255, c.Blue * factor));
    }
}
