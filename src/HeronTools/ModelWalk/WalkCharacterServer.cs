// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - the third-person figure: a simple blocky person (head, body, arms, legs, feet) drawn INSIDE the walk
// view with Revit's own "draw your own 3D graphics" service (DirectContext3D), so walls, ducts and slabs hide it when
// it walks behind them. Arms and legs swing while walking or running. Nothing is added to the model: no element, no
// transaction, no Undo entry - screen graphics only, in the walk view only.
//
// Ported from AJ-Tools Game Mode v1.60.0 (GameCharacterServer.cs, commit a5eb30b), read only, never changed. Heron
// changes: brand colours only; the server is switched on only while a walk runs in third person and switched off on
// every end path; the walk view is matched by ElementId AND its document (ids repeat across models); no gun words.
//
// The Revit API rule this follows (ExternalService.RemoveServer, checked in the 2020 to 2027 API notes, identical):
// a server that has drawn in a model that is still open CANNOT be removed until that model closes. So "switch off"
// means: the figure is hidden (CanExecute says no), the server is taken off the active list, and it is removed as
// soon as Revit allows - at the end of the walk, when a model closes, or when Revit closes. A switched-off server
// draws nothing anywhere. Every member used here exists with the same signature in Revit 2020 to 2027.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.UI;

namespace HeronTools.Services.ModelWalk
{
    /// <summary>Where the figure stands this frame and how its limbs are swung. Written by the engine, read by the server.</summary>
    internal sealed class WalkFigurePose
    {
        /// <summary>True only while a walk runs in third person (and no snag picture is being taken).</summary>
        public bool Visible;

        /// <summary>The walk view and its model - the figure is drawn there and nowhere else.</summary>
        public Document Document;
        public ElementId ViewId;

        /// <summary>Point between the feet, on the floor, in Revit internal feet.</summary>
        public XYZ Feet = XYZ.Zero;

        /// <summary>Facing direction, radians from +X towards +Y (the walk's yaw).</summary>
        public double YawRad;

        /// <summary>Leg swing (radians, + = left leg forward). Arms swing the other way.</summary>
        public double SwingRad;

        /// <summary>Knee bend for each leg (radians, 0 = straight).</summary>
        public double KneeLeftRad;
        public double KneeRightRad;
    }

    /// <summary>Draws the third-person figure in the walk view (Revit-native 3D graphics, no model change).</summary>
    internal sealed class WalkFigureServer : IDirectContext3DServer
    {
        private static readonly Guid ServerGuid = new Guid("125599D2-21BD-439F-B3C5-6F354D653035");
        private static WalkFigureServer _instance;
        private static ExternalEvent _switchOffEvent;
        private static bool _watching;
        private static bool _switchOffPending;

        /// <summary>The pose the next redraw uses (engine writes, server reads - both on Revit's thread).</summary>
        internal static readonly WalkFigurePose Pose = new WalkFigurePose();

        /// <summary>True from the frame the figure is switched on until it is switched off again.</summary>
        internal static bool InUse { get; private set; }

        // Colour groups - one vertex buffer and one effect each. Heron brand palette only (UI/HeronStyles.xaml),
        // used here as model-view colours like the snag red: no colour outside the palette.
        private const int GroupSkin = 0;      // head, neck, hands
        private const int GroupShirt = 1;     // body and arms
        private const int GroupTrousers = 2;  // pelvis and legs
        private const int GroupDark = 3;      // shoes and the face stripe that shows which way it faces
        private const int GroupCount = 4;

        private static readonly Color[] GroupColors =
        {
            new Color(0xF7, 0xF5, 0xF2), // Mist
            new Color(0xFD, 0x64, 0x1A), // Accent (Heron orange) - easy to find in any model
            new Color(0x1B, 0x1B, 0x1F), // Surface
            new Color(0x0F, 0x0F, 0x10)  // Ink
        };

        private WalkFigureServer()
        {
        }

        // ------------------------------------------------------------------ switch on / off (Revit's thread only)

        /// <summary>
        /// Once per Revit session, at the start of a walk (a Revit command - a valid API context): makes the event that
        /// switches the figure off from the overlay's own end paths, and watches models closing.
        /// </summary>
        internal static void Prepare(UIApplication uiApp)
        {
            if (_switchOffEvent == null)
                _switchOffEvent = ExternalEvent.Create(new SwitchOffHandler());
            if (_watching || uiApp == null)
                return;
            _watching = true;
            // A model closing while its walk shows the figure: hide it first (the walk ends on its next frame).
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
        /// Switches the figure on (a walk frame - Revit's thread). Registers the server the first time and puts it on the
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
                    _instance = new WalkFigureServer();
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
                // Broad on purpose: a refusal of Revit's graphics service only means no figure - never a broken walk.
                Pose.Visible = false;
                return false;
            }
        }

        /// <summary>
        /// Switches the figure off on Revit's thread (the end of a walk, a model closing, Revit closing): hidden at once,
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
        /// figure is hidden at once (plain .NET), and the full switch-off runs on Revit's thread at its next idle moment.
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

        /// <summary>Runs the switch-off on Revit's thread, unless a new walk has switched the figure on again meanwhile.</summary>
        private sealed class SwitchOffHandler : IExternalEventHandler
        {
            public void Execute(UIApplication app)
            {
                if (!_switchOffPending)
                    return;   // already switched off, or a new walk switched it on again meanwhile
                SwitchOff();
                try { app?.ActiveUIDocument?.RefreshActiveView(); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* the next redraw clears it anyway */ }
            }

            public string GetName() => "Heron Tools - Model Walk figure off";
        }

        // ------------------------------------------------------------------ IExternalServer

        public Guid GetServerId() => ServerGuid;

        public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;

        public string GetName() => "Heron Tools - Model Walk figure";

        public string GetVendorId() => "HeronTools";

        public string GetDescription() => "Draws the third-person figure in Model Walk's walk view. Nothing is added to the model.";

        // ------------------------------------------------------------------ IDirectContext3DServer

        public string GetApplicationId() => string.Empty;

        public string GetSourceId() => string.Empty;

        public bool UsesHandles() => false;

        public bool UseInTransparentPass(View dBView) => false;

        public bool CanExecute(View dBView)
        {
            try
            {
                return Pose.Visible && dBView != null && Pose.ViewId != null && Pose.Document != null &&
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
                if (!Pose.Visible || !DrawContext.IsAvailable())
                    return;

                var groups = new List<Box>[GroupCount];
                for (int i = 0; i < GroupCount; i++)
                    groups[i] = new List<Box>();
                BuildBody(groups);

                for (int g = 0; g < GroupCount; g++)
                    if (groups[g].Count > 0)
                        FlushGroup(groups[g], GroupColors[g]);
            }
            catch (Exception)
            {
                // Broad on purpose: a failed draw only loses the figure for one redraw - never Revit's own redraw.
            }
        }

        // ------------------------------------------------------------------ the body

        /// <summary>One box: a frame (rotation and origin), and its centre and half sizes in that frame, in feet.</summary>
        private struct Box
        {
            public Transform Frame;
            public XYZ Center;
            public XYZ Half;
        }

        private static XYZ P(double xMm, double yMm, double zMm) =>
            new XYZ(WalkTuning.MmToFt(xMm), WalkTuning.MmToFt(yMm), WalkTuning.MmToFt(zMm));

        /// <summary>
        /// The figure in its own frame: X = facing, Y = left, Z = up, origin on the floor between the feet; 1810 mm tall.
        /// Limbs hang from their joint, so a turn about the joint's Y axis swings them (+ = the hanging end goes back).
        /// </summary>
        private static void BuildBody(List<Box>[] groups)
        {
            Transform body = Transform.CreateTranslation(Pose.Feet ?? XYZ.Zero)
                .Multiply(Transform.CreateRotation(XYZ.BasisZ, Pose.YawRad));

            Add(groups[GroupTrousers], body, P(0, 0, 960), P(100, 170, 80));   // pelvis 880..1040
            Add(groups[GroupShirt], body, P(0, 0, 1290), P(110, 190, 250));     // body 1040..1540
            Add(groups[GroupSkin], body, P(0, 0, 1575), P(50, 50, 35));         // neck
            Add(groups[GroupSkin], body, P(0, 0, 1690), P(110, 100, 120));      // head 1570..1810
            Add(groups[GroupDark], body, P(108, 0, 1715), P(12, 80, 25));       // face stripe: shows which way it faces

            double swing = Pose.SwingRad;
            AddLeg(groups, body, 100.0, -swing, Pose.KneeLeftRad);   // forward swing = a negative turn about Y
            AddLeg(groups, body, -100.0, swing, Pose.KneeRightRad);

            double armSwing = swing * 0.8;                           // arms the other way, a bit less
            AddArm(groups, body, 250.0, armSwing);
            AddArm(groups, body, -250.0, -armSwing);
        }

        private static void AddLeg(List<Box>[] groups, Transform body, double sideMm, double hipRad, double kneeRad)
        {
            Transform thigh = body.Multiply(Transform.CreateTranslation(P(0, sideMm, 960)))
                                  .Multiply(Transform.CreateRotation(XYZ.BasisY, hipRad));
            Add(groups[GroupTrousers], thigh, P(0, 0, -225), P(70, 70, 225));

            Transform shin = thigh.Multiply(Transform.CreateTranslation(P(0, 0, -450)))
                                  .Multiply(Transform.CreateRotation(XYZ.BasisY, kneeRad));
            Add(groups[GroupTrousers], shin, P(0, 0, -215), P(60, 60, 215));

            Transform ankle = shin.Multiply(Transform.CreateTranslation(P(0, 0, -430)));
            Add(groups[GroupDark], ankle, P(50, 0, -40), P(120, 60, 40));
        }

        private static void AddArm(List<Box>[] groups, Transform body, double sideMm, double shoulderRad)
        {
            Transform upper = body.Multiply(Transform.CreateTranslation(P(0, sideMm, 1480)))
                                  .Multiply(Transform.CreateRotation(XYZ.BasisY, shoulderRad));
            Add(groups[GroupShirt], upper, P(0, 0, -150), P(55, 55, 150));

            // The elbow bends the forearm forward, more as the arm swings back.
            double elbowRad = -(0.25 + Math.Max(0.0, shoulderRad) * 0.6);
            Transform fore = upper.Multiply(Transform.CreateTranslation(P(0, 0, -300)))
                                  .Multiply(Transform.CreateRotation(XYZ.BasisY, elbowRad));
            Add(groups[GroupShirt], fore, P(0, 0, -140), P(50, 50, 140));

            Transform wrist = fore.Multiply(Transform.CreateTranslation(P(0, 0, -280)));
            Add(groups[GroupSkin], wrist, P(0, 0, -50), P(45, 40, 50));
        }

        private static void Add(List<Box> list, Transform frame, XYZ center, XYZ half) =>
            list.Add(new Box { Frame = frame, Center = center, Half = half });

        // ------------------------------------------------------------------ drawing

        // Each face: its 4 corners as (+/-) sign triples, counter-clockwise seen from outside, and its outward normal.
        private static readonly int[][] FaceCorners =
        {
            new[] {  1, -1, -1,   1,  1, -1,   1,  1,  1,   1, -1,  1 }, // +X
            new[] { -1,  1, -1,  -1, -1, -1,  -1, -1,  1,  -1,  1,  1 }, // -X
            new[] {  1,  1, -1,  -1,  1, -1,  -1,  1,  1,   1,  1,  1 }, // +Y
            new[] { -1, -1, -1,   1, -1, -1,   1, -1,  1,  -1, -1,  1 }, // -Y
            new[] { -1, -1,  1,   1, -1,  1,   1,  1,  1,  -1,  1,  1 }, // +Z
            new[] { -1,  1, -1,   1,  1, -1,   1, -1, -1,  -1, -1, -1 }  // -Z
        };

        private static readonly XYZ[] FaceNormals =
        {
            XYZ.BasisX, -XYZ.BasisX, XYZ.BasisY, -XYZ.BasisY, XYZ.BasisZ, -XYZ.BasisZ
        };

        /// <summary>
        /// One colour's boxes as one shaded triangle list. Buffers are made fresh on every draw: the figure is about 17
        /// boxes (about 400 corners), so this is cheap, and nothing is kept that Revit could invalidate between redraws.
        /// </summary>
        private static void FlushGroup(List<Box> boxes, Color color)
        {
            int vertexCount = boxes.Count * 24;
            int triangleCount = boxes.Count * 12;

            var format = new VertexFormat(VertexFormatBits.PositionNormal);
            var effect = new EffectInstance(VertexFormatBits.PositionNormal);
            effect.SetColor(color);
            effect.SetDiffuseColor(color);
            effect.SetAmbientColor(Scale(color, 0.55));
            effect.SetEmissiveColor(Scale(color, 0.15));
            effect.SetSpecularColor(Scale(color, 0.12));

            int vertexFloats = vertexCount * VertexPositionNormal.GetSizeInFloats();
            var vertexBuffer = new VertexBuffer(vertexFloats);
            vertexBuffer.Map(vertexFloats);
            VertexStreamPositionNormal vertices = vertexBuffer.GetVertexStreamPositionNormal();

            int indexShorts = triangleCount * IndexTriangle.GetSizeInShortInts();
            var indexBuffer = new IndexBuffer(indexShorts);
            indexBuffer.Map(indexShorts);
            IndexStreamTriangle triangles = indexBuffer.GetIndexStreamTriangle();

            int baseIndex = 0;
            foreach (Box box in boxes)
            {
                for (int face = 0; face < 6; face++)
                {
                    int[] c = FaceCorners[face];
                    XYZ normal = box.Frame.OfVector(FaceNormals[face]);
                    for (int corner = 0; corner < 4; corner++)
                    {
                        var local = new XYZ(box.Center.X + (c[corner * 3] * box.Half.X),
                                            box.Center.Y + (c[(corner * 3) + 1] * box.Half.Y),
                                            box.Center.Z + (c[(corner * 3) + 2] * box.Half.Z));
                        vertices.AddVertex(new VertexPositionNormal(box.Frame.OfPoint(local), normal));
                    }
                    triangles.AddTriangle(new IndexTriangle(baseIndex, baseIndex + 1, baseIndex + 2));
                    triangles.AddTriangle(new IndexTriangle(baseIndex, baseIndex + 2, baseIndex + 3));
                    baseIndex += 4;
                }
            }

            vertexBuffer.Unmap();
            indexBuffer.Unmap();

            DrawContext.FlushBuffer(vertexBuffer, vertexCount, indexBuffer, indexShorts, format, effect,
                                    PrimitiveType.TriangleList, 0, triangleCount);
        }

        /// <summary>A darker step of the same palette colour (for shading only - never a new hue).</summary>
        private static Color Scale(Color c, double factor) =>
            new Color((byte)Math.Min(255, c.Red * factor), (byte)Math.Min(255, c.Green * factor), (byte)Math.Min(255, c.Blue * factor));
    }
}
