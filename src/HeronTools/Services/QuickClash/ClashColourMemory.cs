// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Quick Clash - the way back. Before the first colour goes on, every element's own graphic overrides in the view
// are copied (Highlight's idea); Reset puts the copies back. The copies are the TRUE originals: a second Run
// first puts them back, then colours again, so they are never replaced by the colours of an earlier run.
// They are also written to %LocalAppData%\HeronTools\QuickClash\ so a Revit crash does not lose them.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using HeronTools.Models.QuickClash;
using HeronTools.Utils;

namespace HeronTools.Services.QuickClash
{
    internal sealed class ClashColourMemory
    {
        private static readonly Dictionary<string, ClashColourMemory> ByView = new Dictionary<string, ClashColourMemory>();

        private readonly Dictionary<long, OverrideGraphicSettings> _originals = new Dictionary<long, OverrideGraphicSettings>();
        private readonly string _key;

        private ClashColourMemory(string key) { _key = key; }

        /// <summary>The colours the last Run put on (to tell them apart from the modeller's own colours).</summary>
        public ClashColour ColourA { get; private set; }
        public ClashColour ColourB { get; private set; }
        public ClashColour ColourRest { get; private set; }

        public bool HasOriginals => _originals.Count > 0;

        // ------------------------------------------------------------------ finding one

        internal static string ViewKey(Document doc, View view)
        {
            string docKey = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;
            return docKey + "|" + view.Id.LongValue();
        }

        /// <summary>This view's memory, from this session or (after a crash) from the copy on disk; null if none.</summary>
        internal static ClashColourMemory Find(Document doc, View view)
        {
            string key = ViewKey(doc, view);
            if (ByView.TryGetValue(key, out ClashColourMemory memory))
                return memory;
            memory = LoadFromDisk(key);
            if (memory != null)
                ByView[key] = memory;
            return memory;
        }

        internal static ClashColourMemory FindOrCreate(Document doc, View view)
        {
            ClashColourMemory memory = Find(doc, view);
            if (memory == null)
            {
                memory = new ClashColourMemory(ViewKey(doc, view));
                ByView[memory._key] = memory;
            }
            return memory;
        }

        /// <summary>Forget this view (only when the view is no longer coloured): memory and disk copy.</summary>
        internal static void Forget(Document doc, View view)
        {
            string key = ViewKey(doc, view);
            ByView.Remove(key);
            try { File.Delete(DiskPath(key)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // ------------------------------------------------------------------ inside a transaction

        /// <summary>Puts every remembered override back. Deleted elements are passed over. Returns how many were put back.</summary>
        internal int PutBack(Document doc, View view)
        {
            int count = 0;
            foreach (KeyValuePair<long, OverrideGraphicSettings> entry in _originals)
            {
                ElementId id = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(id) == null)
                    continue;
                try
                {
                    view.SetElementOverrides(id, entry.Value);
                    count++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // one refused element must not stop the rest being put back
                }
            }
            return count;
        }

        /// <summary>Copies the overrides of elements not yet remembered. Call after <see cref="PutBack"/>, so they are originals.</summary>
        internal void RememberMissing(View view, IEnumerable<ElementId> ids)
        {
            foreach (ElementId id in ids)
            {
                long key = id.LongValue();
                if (!_originals.ContainsKey(key))
                    _originals[key] = new OverrideGraphicSettings(view.GetElementOverrides(id));
            }
        }

        internal void SetColours(ClashColour a, ClashColour b, ClashColour rest)
        {
            ColourA = a; ColourB = b; ColourRest = rest;
        }

        // ------------------------------------------------------------------ is it still on?

        /// <summary>
        /// True when elements in the view still carry this tool's colours - a clash or grey colour that is not the
        /// element's own remembered colour. Tells an undone Run from one still on screen.
        /// </summary>
        internal bool StillColoured(Document doc, View view)
        {
            int looked = 0;
            foreach (KeyValuePair<long, OverrideGraphicSettings> entry in _originals)
            {
                ElementId id = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(id) == null)
                    continue;
                Color c = view.GetElementOverrides(id).ProjectionLineColor;
                if (c == null || !c.IsValid)
                {
                    if (++looked >= 500) break;
                    continue;
                }
                var now = new ClashColour(c.Red, c.Green, c.Blue);
                bool ours = now.SameAs(ColourA) || now.SameAs(ColourB) || now.SameAs(ColourRest);
                Color own = entry.Value.ProjectionLineColor;
                bool sameAsOwn = own != null && own.IsValid && now.SameAs(new ClashColour(own.Red, own.Green, own.Blue));
                if (ours && !sameAsOwn)
                    return true;
                if (++looked >= 500)
                    break;
            }
            return false;
        }

        // ------------------------------------------------------------------ disk copy

        /// <summary>Writes the copy kept in case Revit closes unexpectedly. Best effort.</summary>
        internal void SaveToDisk()
        {
            try
            {
                var lines = new List<string>
                {
                    "# Heron Tools - Quick Clash: the view's own graphic overrides, kept until the view is put back.",
                    "key=" + _key,
                    "colours=" + ColourA + ";" + ColourB + ";" + ColourRest
                };
                lines.AddRange(_originals.Select(e => e.Key.ToString(CultureInfo.InvariantCulture) + "\t" + Write(e.Value)));
                string path = DiskPath(_key);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                File.WriteAllLines(temp, lines, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static ClashColourMemory LoadFromDisk(string key)
        {
            string path = DiskPath(key);
            try
            {
                if (!File.Exists(path))
                    return null;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (!lines.Any(l => l == "key=" + key))
                    return null;                                     // a different view that hashed alike
                var memory = new ClashColourMemory(key);
                foreach (string line in lines)
                {
                    if (line.StartsWith("colours=", StringComparison.Ordinal))
                    {
                        string[] c = line.Substring(8).Split(';');
                        if (c.Length == 3)
                            memory.SetColours(ClashColour.Parse(c[0], QuickClashSettings.DefaultColourA),
                                              ClashColour.Parse(c[1], QuickClashSettings.DefaultColourB),
                                              ClashColour.Parse(c[2], QuickClashSettings.DefaultColourRest));
                        continue;
                    }
                    int tab = line.IndexOf('\t');
                    if (tab > 0 && long.TryParse(line.Substring(0, tab), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                    {
                        OverrideGraphicSettings ogs = Read(line.Substring(tab + 1));
                        if (ogs != null)
                            memory._originals[id] = ogs;
                    }
                }
                return memory.HasOriginals ? memory : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static string DiskPath(string key)
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            using (var sha = SHA1.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").Substring(0, 16);
                return Path.Combine(root, "HeronTools", "QuickClash", hash + ".txt");
            }
        }

        // Every field of an element override, in one tab-separated line.
        private static string Write(OverrideGraphicSettings o)
        {
            return string.Join("\t", new[]
            {
                C(o.ProjectionLineColor), I(o.ProjectionLinePatternId), N(o.ProjectionLineWeight),
                I(o.SurfaceForegroundPatternId), C(o.SurfaceForegroundPatternColor), B(o.IsSurfaceForegroundPatternVisible),
                I(o.SurfaceBackgroundPatternId), C(o.SurfaceBackgroundPatternColor), B(o.IsSurfaceBackgroundPatternVisible),
                N(o.Transparency),
                C(o.CutLineColor), I(o.CutLinePatternId), N(o.CutLineWeight),
                I(o.CutForegroundPatternId), C(o.CutForegroundPatternColor), B(o.IsCutForegroundPatternVisible),
                I(o.CutBackgroundPatternId), C(o.CutBackgroundPatternColor), B(o.IsCutBackgroundPatternVisible),
                B(o.Halftone), N((int)o.DetailLevel)
            });
        }

        private static OverrideGraphicSettings Read(string text)
        {
            string[] f = text.Split('\t');
            if (f.Length != 21)
                return null;
            try
            {
                var o = new OverrideGraphicSettings();
                o.SetProjectionLineColor(ToC(f[0])); o.SetProjectionLinePatternId(ToI(f[1])); o.SetProjectionLineWeight(ToN(f[2]));
                o.SetSurfaceForegroundPatternId(ToI(f[3])); o.SetSurfaceForegroundPatternColor(ToC(f[4])); o.SetSurfaceForegroundPatternVisible(f[5] == "1");
                o.SetSurfaceBackgroundPatternId(ToI(f[6])); o.SetSurfaceBackgroundPatternColor(ToC(f[7])); o.SetSurfaceBackgroundPatternVisible(f[8] == "1");
                o.SetSurfaceTransparency(ToN(f[9]));
                o.SetCutLineColor(ToC(f[10])); o.SetCutLinePatternId(ToI(f[11])); o.SetCutLineWeight(ToN(f[12]));
                o.SetCutForegroundPatternId(ToI(f[13])); o.SetCutForegroundPatternColor(ToC(f[14])); o.SetCutForegroundPatternVisible(f[15] == "1");
                o.SetCutBackgroundPatternId(ToI(f[16])); o.SetCutBackgroundPatternColor(ToC(f[17])); o.SetCutBackgroundPatternVisible(f[18] == "1");
                o.SetHalftone(f[19] == "1");
                o.SetDetailLevel((ViewDetailLevel)ToN(f[20]));
                return o;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;   // a value this Revit refuses: that element is not put back from disk
            }
        }

        private static string C(Color c) => c != null && c.IsValid ? c.Red + "," + c.Green + "," + c.Blue : "-";
        private static string I(ElementId id) => (id ?? ElementId.InvalidElementId).LongValue().ToString(CultureInfo.InvariantCulture);
        private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        private static string B(bool b) => b ? "1" : "0";

        private static Color ToC(string s)
        {
            if (s == "-") return Color.InvalidColorValue;
            ClashColour c = ClashColour.Parse(s, new ClashColour(0, 0, 0));
            return new Color(c.R, c.G, c.B);
        }

        private static ElementId ToI(string s) =>
            long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? ElementIdHelper.FromLong(v) : ElementId.InvalidElementId;

        private static int ToN(string s) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
    }
}
