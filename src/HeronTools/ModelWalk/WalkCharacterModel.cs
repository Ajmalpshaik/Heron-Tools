// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - the third-person character as read from its file: a site worker in a yellow hard hat, built into
// HeronTools.dll (Resources/ModelWalk/WalkCharacter.gltf, a standard glTF 2.0 file with its data inside it). Reads the
// skinned mesh, its skeleton, its plain material colours and its three animation clips (Idle, Walk, Run). Pure data -
// no Revit API, no drawing. WalkCharacterAnimator poses it; WalkCharacterServer draws it.
//
// Ported from AJ-Tools Game Mode v1.61.0 (GameCharacterModel.cs, commit ab98e2e), read only, never changed. Heron
// changes: the file is an assembly resource, not a file beside the DLL (it can never go missing); the JSON is read by
// WalkJson (Heron Tools ships no Newtonsoft.Json, and net472 / net48 have no System.Text.Json); no fallback figure.
//
// Only what Model Walk needs is read: ONE skinned mesh (triangles), POSITION / NORMAL / JOINTS_0 / WEIGHTS_0, the
// material base colour (no textures - Revit's DirectContext3D in 2020 has no texture vertex format), one skin, node
// rest poses, and translation / rotation animation tracks. Anything else in the file is ignored. The data stays in the
// file's own axes and units (Y up, the character facing +Z, +X = its left); the server turns it into Revit's.
//
// The character is CC0 (public domain, no credit required) - see NOTICE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace HeronTools.Services.ModelWalk
{
    /// <summary>The character as read from the glTF file (file axes and units).</summary>
    internal sealed class WalkCharacterModel
    {
        /// <summary>The resource name in HeronTools.dll (set in HeronTools.csproj).</summary>
        internal const string ResourceName = "HeronTools.ModelWalk.WalkCharacter.gltf";

        internal sealed class Node
        {
            public string Name;
            public int Parent = -1;
            public int[] Children = new int[0];
            public double[] Translation = { 0, 0, 0 };
            public double[] Rotation = { 0, 0, 0, 1 };   // quaternion x, y, z, w
            public double[] Scale = { 1, 1, 1 };
        }

        /// <summary>One triangle list with one colour.</summary>
        internal sealed class Part
        {
            public int Material;
            public float[] Positions;   // 3 per vertex
            public float[] Normals;     // 3 per vertex
            public int[] Joints;        // 4 per vertex - index into the skin's joints
            public float[] Weights;     // 4 per vertex
            public int[] Indices;       // 3 per triangle
            public int VertexCount => Positions.Length / 3;
        }

        internal sealed class Material
        {
            public string Name;
            public double[] BaseColorLinear = { 1, 1, 1 };
        }

        internal const int PathTranslation = 0;
        internal const int PathRotation = 1;

        internal sealed class Track
        {
            public int Node;
            public int Path;            // PathTranslation or PathRotation
            public bool Step;           // STEP interpolation (else LINEAR)
            public bool CubicSpline;    // values stored as in-tangent / value / out-tangent triples
            public float[] Times;
            public float[] Values;      // 3 or 4 per key (x3 for cubic spline)
        }

        internal sealed class Clip
        {
            public string Name;
            public double Duration;
            public List<Track> Tracks = new List<Track>();
        }

        public Node[] Nodes;
        public Part[] Parts;
        public Material[] Materials;
        public int[] SkinJoints;            // node index per joint
        public double[][] InverseBind;      // 12 per joint, row-major 3x4
        public Dictionary<string, Clip> Clips = new Dictionary<string, Clip>(StringComparer.OrdinalIgnoreCase);

        private WalkCharacterModel()
        {
        }

        /// <summary>
        /// Reads the character built into HeronTools.dll. Returns null with a plain reason when it cannot be read - third
        /// person then works with no character. Never throws.
        /// </summary>
        internal static WalkCharacterModel TryLoad(out string problem)
        {
            problem = null;
            try
            {
                Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
                if (stream == null)
                {
                    problem = "the character file is missing from HeronTools.dll";
                    return null;
                }
                string text;
                using (stream)
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    text = reader.ReadToEnd();
                return Parse(WalkJson.Parse(text), out problem);
            }
            catch (Exception ex)
            {
                // Broad on purpose: a damaged character file must only mean "no character", never a broken walk.
                problem = "the character file could not be read (" + ex.Message + ")";
                return null;
            }
        }

        /// <summary>Reads the character from glTF text (also used by a test outside Revit). Throws on a broken file.</summary>
        internal static WalkCharacterModel Parse(object root, out string problem)
        {
            problem = null;
            List<byte[]> buffers = ReadBuffers(root);
            var reader = new AccessorReader(root, buffers);
            var model = new WalkCharacterModel();

            // --- Nodes ---
            List<object> nodesJson = WalkJson.Arr(root, "nodes") ?? throw new InvalidDataException("no nodes");
            model.Nodes = new Node[nodesJson.Count];
            for (int i = 0; i < nodesJson.Count; i++)
            {
                object n = nodesJson[i];
                var node = new Node { Name = WalkJson.Str(n, "name") ?? ("node" + i) };
                node.Translation = WalkJson.Doubles(WalkJson.Arr(n, "translation"), node.Translation);
                node.Rotation = WalkJson.Doubles(WalkJson.Arr(n, "rotation"), node.Rotation);
                node.Scale = WalkJson.Doubles(WalkJson.Arr(n, "scale"), node.Scale);
                node.Children = WalkJson.Ints(WalkJson.Arr(n, "children"));
                if (node.Translation.Length < 3 || node.Rotation.Length < 4 || node.Scale.Length < 3)
                    throw new InvalidDataException("node " + i + " has a short transform");
                model.Nodes[i] = node;
            }
            for (int i = 0; i < model.Nodes.Length; i++)
            {
                foreach (int c in model.Nodes[i].Children)
                {
                    if (c < 0 || c >= model.Nodes.Length)
                        throw new InvalidDataException("node " + i + " has a child that does not exist");
                    model.Nodes[c].Parent = i;
                }
            }

            // --- The skinned mesh node ---
            int meshIndex = -1;
            int skinIndex = -1;
            foreach (object n in nodesJson)
            {
                if (WalkJson.Has(n, "mesh") && WalkJson.Has(n, "skin"))
                {
                    meshIndex = WalkJson.Int(n, "mesh", -1);
                    skinIndex = WalkJson.Int(n, "skin", -1);
                    break;
                }
            }
            List<object> skins = WalkJson.Arr(root, "skins");
            List<object> meshes = WalkJson.Arr(root, "meshes");
            if (meshIndex < 0 || skinIndex < 0 || skins == null || meshes == null || skinIndex >= skins.Count || meshIndex >= meshes.Count)
            {
                problem = "the character file has no skinned mesh";
                return null;
            }

            // --- Skin ---
            object skin = skins[skinIndex];
            model.SkinJoints = WalkJson.Ints(WalkJson.Arr(skin, "joints"));
            foreach (int j in model.SkinJoints)
                if (j < 0 || j >= model.Nodes.Length)
                    throw new InvalidDataException("a skin joint does not exist");
            float[] ibm = WalkJson.Has(skin, "inverseBindMatrices") ? reader.Floats(WalkJson.Int(skin, "inverseBindMatrices", -1), 16) : null;
            model.InverseBind = new double[model.SkinJoints.Length][];
            for (int j = 0; j < model.SkinJoints.Length; j++)
            {
                // glTF matrices are column-major; keep the top 3 rows as row-major 3x4.
                var m = new double[12];
                if (ibm != null && ((j * 16) + 15) < ibm.Length)
                {
                    int o = j * 16;
                    for (int r = 0; r < 3; r++)
                        for (int c = 0; c < 4; c++)
                            m[(r * 4) + c] = ibm[o + (c * 4) + r];
                }
                else
                {
                    m[0] = m[5] = m[10] = 1.0;
                }
                model.InverseBind[j] = m;
            }

            // --- Materials ---
            List<object> matsJson = WalkJson.Arr(root, "materials") ?? new List<object>();
            model.Materials = new Material[Math.Max(1, matsJson.Count)];
            for (int i = 0; i < model.Materials.Length; i++)
            {
                var mat = new Material { Name = "Default" };
                if (i < matsJson.Count)
                {
                    mat.Name = WalkJson.Str(matsJson[i], "name") ?? ("Material" + i);
                    double[] factor = WalkJson.Doubles(WalkJson.Arr(WalkJson.Obj(matsJson[i], "pbrMetallicRoughness"), "baseColorFactor"), null);
                    if (factor != null && factor.Length >= 3)
                        mat.BaseColorLinear = new[] { factor[0], factor[1], factor[2] };
                }
                model.Materials[i] = mat;
            }

            // --- Mesh parts ---
            var parts = new List<Part>();
            foreach (object prim in WalkJson.Arr(meshes[meshIndex], "primitives") ?? new List<object>())
            {
                int mode = WalkJson.Int(prim, "mode", 4);
                Dictionary<string, object> attrs = WalkJson.Obj(prim, "attributes");
                if (mode != 4 || !WalkJson.Has(attrs, "POSITION") || !WalkJson.Has(attrs, "JOINTS_0") || !WalkJson.Has(attrs, "WEIGHTS_0"))
                    continue;   // only skinned triangle lists are drawn

                var part = new Part
                {
                    Material = WalkJson.Int(prim, "material", 0),
                    Positions = reader.Floats(WalkJson.Int(attrs, "POSITION", -1), 3),
                    Joints = reader.Ints(WalkJson.Int(attrs, "JOINTS_0", -1), 4),
                    Weights = reader.Floats(WalkJson.Int(attrs, "WEIGHTS_0", -1), 4)
                };
                part.Normals = WalkJson.Has(attrs, "NORMAL") ? reader.Floats(WalkJson.Int(attrs, "NORMAL", -1), 3) : new float[part.Positions.Length];
                part.Indices = WalkJson.Has(prim, "indices") ? reader.Ints(WalkJson.Int(prim, "indices", -1), 1) : Sequence(part.VertexCount);
                if (part.Material < 0 || part.Material >= model.Materials.Length)
                    part.Material = 0;
                if (part.Normals.Length != part.Positions.Length || part.Joints.Length != part.VertexCount * 4 ||
                    part.Weights.Length != part.VertexCount * 4 || part.Indices.Length % 3 != 0)
                    throw new InvalidDataException("a mesh part's data does not line up");
                foreach (int index in part.Indices)
                    if (index < 0 || index >= part.VertexCount)
                        throw new InvalidDataException("a triangle points at a vertex that does not exist");
                parts.Add(part);
            }
            if (parts.Count == 0)
            {
                problem = "the character's mesh has no skinned triangles";
                return null;
            }
            model.Parts = parts.ToArray();

            // --- Animation clips ---
            foreach (object anim in WalkJson.Arr(root, "animations") ?? new List<object>())
            {
                var clip = new Clip { Name = WalkJson.Str(anim, "name") ?? "Clip" };
                List<object> samplers = WalkJson.Arr(anim, "samplers") ?? new List<object>();
                foreach (object ch in WalkJson.Arr(anim, "channels") ?? new List<object>())
                {
                    Dictionary<string, object> target = WalkJson.Obj(ch, "target");
                    string path = WalkJson.Str(target, "path");
                    int nodeIndex = WalkJson.Int(target, "node", -1);
                    int samplerIndex = WalkJson.Int(ch, "sampler", -1);
                    if (nodeIndex < 0 || nodeIndex >= model.Nodes.Length || samplerIndex < 0 || samplerIndex >= samplers.Count ||
                        (path != "translation" && path != "rotation"))
                        continue;

                    object sampler = samplers[samplerIndex];
                    string interp = WalkJson.Str(sampler, "interpolation") ?? "LINEAR";
                    int width = path == "rotation" ? 4 : 3;
                    var track = new Track
                    {
                        Node = nodeIndex,
                        Path = path == "rotation" ? PathRotation : PathTranslation,
                        Step = interp == "STEP",
                        CubicSpline = interp == "CUBICSPLINE",
                        Times = reader.Floats(WalkJson.Int(sampler, "input", -1), 1),
                        Values = reader.Floats(WalkJson.Int(sampler, "output", -1), width)
                    };
                    if (track.Times.Length == 0)
                        continue;
                    clip.Duration = Math.Max(clip.Duration, track.Times[track.Times.Length - 1]);
                    clip.Tracks.Add(track);
                }
                if (clip.Tracks.Count > 0)
                    model.Clips[clip.Name] = clip;
            }

            return model;
        }

        // ------------------------------------------------------------------ helpers

        private static int[] Sequence(int count)
        {
            var s = new int[count];
            for (int i = 0; i < count; i++)
                s[i] = i;
            return s;
        }

        private static List<byte[]> ReadBuffers(object root)
        {
            var list = new List<byte[]>();
            foreach (object b in WalkJson.Arr(root, "buffers") ?? new List<object>())
            {
                string uri = WalkJson.Str(b, "uri");
                int comma = uri != null ? uri.IndexOf(',') : -1;
                if (uri == null || !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || comma < 0)
                    throw new InvalidDataException("only data held inside the file is supported");
                list.Add(Convert.FromBase64String(uri.Substring(comma + 1)));
            }
            return list;
        }

        /// <summary>Reads glTF accessors into flat float / int arrays, checking every read stays inside its buffer.</summary>
        private sealed class AccessorReader
        {
            private readonly List<object> _accessors;
            private readonly List<object> _views;
            private readonly List<byte[]> _buffers;

            public AccessorReader(object root, List<byte[]> buffers)
            {
                _accessors = WalkJson.Arr(root, "accessors") ?? new List<object>();
                _views = WalkJson.Arr(root, "bufferViews") ?? new List<object>();
                _buffers = buffers;
            }

            public float[] Floats(int accessor, int width)
            {
                object a = Accessor(accessor);
                int count = WalkJson.Int(a, "count", 0);
                bool normalized = WalkJson.Bool(a, "normalized");
                var result = new float[count * width];
                Walk(a, width, (k, c, bytes, offset, type) => result[(k * width) + c] = ReadFloat(bytes, offset, type, normalized));
                return result;
            }

            public int[] Ints(int accessor, int width)
            {
                object a = Accessor(accessor);
                int count = WalkJson.Int(a, "count", 0);
                var result = new int[count * width];
                Walk(a, width, (k, c, bytes, offset, type) => result[(k * width) + c] = ReadInt(bytes, offset, type));
                return result;
            }

            private object Accessor(int index)
            {
                if (index < 0 || index >= _accessors.Count)
                    throw new InvalidDataException("data block " + index + " does not exist");
                return _accessors[index];
            }

            private void Walk(object a, int width, Action<int, int, byte[], int, int> read)
            {
                int type = WalkJson.Int(a, "componentType", 0);
                int size = ComponentSize(type);
                int count = WalkJson.Int(a, "count", 0);
                if (count < 0)
                    throw new InvalidDataException("a negative count");
                if (!WalkJson.Has(a, "bufferView"))
                    return;   // all zeros
                int viewIndex = WalkJson.Int(a, "bufferView", -1);
                if (viewIndex < 0 || viewIndex >= _views.Count)
                    throw new InvalidDataException("a data view that does not exist");
                object view = _views[viewIndex];
                int bufferIndex = WalkJson.Int(view, "buffer", -1);
                if (bufferIndex < 0 || bufferIndex >= _buffers.Count)
                    throw new InvalidDataException("a buffer that does not exist");
                byte[] bytes = _buffers[bufferIndex];
                int stride = WalkJson.Int(view, "byteStride", 0);
                if (stride <= 0)
                    stride = size * width;
                int start = WalkJson.Int(view, "byteOffset", 0) + WalkJson.Int(a, "byteOffset", 0);
                if (count > 0 && (start < 0 || (long)start + ((long)(count - 1) * stride) + ((long)width * size) > bytes.Length))
                    throw new InvalidDataException("data reaches past the end of its buffer");
                for (int k = 0; k < count; k++)
                {
                    int o = start + (k * stride);
                    for (int c = 0; c < width; c++)
                        read(k, c, bytes, o + (c * size), type);
                }
            }

            private static int ComponentSize(int type)
            {
                switch (type)
                {
                    case 5120: case 5121: return 1;   // byte / unsigned byte
                    case 5122: case 5123: return 2;   // short / unsigned short
                    case 5125: case 5126: return 4;   // unsigned int / float
                    default: throw new InvalidDataException("unknown component type " + type);
                }
            }

            private static int ReadInt(byte[] b, int o, int type)
            {
                switch (type)
                {
                    case 5120: return (sbyte)b[o];
                    case 5121: return b[o];
                    case 5122: return BitConverter.ToInt16(b, o);
                    case 5123: return BitConverter.ToUInt16(b, o);
                    case 5125: return (int)BitConverter.ToUInt32(b, o);
                    default: return (int)BitConverter.ToSingle(b, o);
                }
            }

            private static float ReadFloat(byte[] b, int o, int type, bool normalized)
            {
                switch (type)
                {
                    case 5126: return BitConverter.ToSingle(b, o);
                    case 5121: return normalized ? b[o] / 255f : b[o];
                    case 5123: return normalized ? BitConverter.ToUInt16(b, o) / 65535f : BitConverter.ToUInt16(b, o);
                    case 5120: return normalized ? Math.Max((sbyte)b[o] / 127f, -1f) : (sbyte)b[o];
                    case 5122: return normalized ? Math.Max(BitConverter.ToInt16(b, o) / 32767f, -1f) : BitConverter.ToInt16(b, o);
                    default: return ReadInt(b, o, type);
                }
            }
        }
    }
}
