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
// "Still coloured?" asks for the FULL Quick Clash override on an element (solid fill, colour, transparency), never
// the line colour alone - so a modeller's own red, green or grey set after Reset view is never taken for ours.

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
        private string _key;

        private ClashColourMemory(string key) { _key = key; }

        /// <summary>The colours the last Run put on (to tell them apart from the modeller's own colours).</summary>
        public ClashColour ColourA { get; private set; }
        public ClashColour ColourB { get; private set; }
        public ClashColour ColourRest { get; private set; }
        public int RestTransparency { get; private set; } = QuickClashSettings.DefaultRestTransparency;

        /// <summary>The solid fill the last Run used; -1 when not known (an older disk copy).</summary>
        private long _solidFillId = -1;

        /// <summary>False until a Run (or the disk copy) has said which colours went on.</summary>
        private bool _hasColours;

        /// <summary>
        /// True once the view has been put back (Reset, Save, Sync, Close). The copies stay in this session only, so
        /// an Undo of that Reset can be reset again; the disk copy is gone, so a later Revit never brings them back.
        /// </summary>
        public bool IsPutBack { get; private set; }

        public bool HasOriginals => _originals.Count > 0;

        // ------------------------------------------------------------------ finding one

        internal static string ViewKey(Document doc, View view) => DocKey(doc) + "|" + view.Id.LongValue();

        /// <summary>The model part of the key: its file path, or its title before it is first saved.</summary>
        internal static string DocKey(Document doc) => string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;

        /// <summary>
        /// Save As gave the model a new path: every memory of the old path moves to the new one (with its disk copy),
        /// so Reset view, Save and Close still find it.
        /// </summary>
        internal static void MoveDocument(string oldDocKey, string newDocKey)
        {
            if (string.IsNullOrEmpty(oldDocKey) || string.IsNullOrEmpty(newDocKey) || oldDocKey == newDocKey)
                return;
            string prefix = oldDocKey + "|";
            foreach (string oldKey in ByView.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                ClashColourMemory memory = ByView[oldKey];
                ByView.Remove(oldKey);
                DeleteDiskCopy(oldKey);
                memory._key = newDocKey + "|" + oldKey.Substring(prefix.Length);
                ByView[memory._key] = memory;
                if (!memory.IsPutBack)
                    memory.SaveToDisk();
            }
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
            if (_runJournal != null && ByView.TryGetValue(key, out ClashColourMemory forgotten))
                _runJournal.Add(() =>
                {
                    ByView[key] = forgotten;
                    forgotten.RestoreDiskCopy();
                });
            ByView.Remove(key);
            DeleteDiskCopy(key);
        }

        // ------------------------------------------------------------------ a Run that is rolled back

        // What a Run changed in the memories, to be undone if Revit rolls the Run back. Null outside a Run.
        private static List<Action> _runJournal;

        /// <summary>A Run starts: note every memory change from here, so a rolled-back Run can undo them.</summary>
        internal static void BeginRun() => _runJournal = new List<Action>();

        /// <summary>The Run was kept: its memory changes stay.</summary>
        internal static void EndRun() => _runJournal = null;

        /// <summary>
        /// The Run was rolled back, so the view is as it was: the memories (colours, put-back mark, forgotten views,
        /// disk copies) go back to how they were too - otherwise Reset view would look for the wrong colours.
        /// </summary>
        internal static void RollBackRun()
        {
            List<Action> journal = _runJournal;
            _runJournal = null;
            if (journal == null) return;
            for (int i = journal.Count - 1; i >= 0; i--)
                journal[i]();
        }

        // The disk copy matches the memory: kept while its colours are on, gone once put back or never coloured.
        private void RestoreDiskCopy()
        {
            if (_hasColours && !IsPutBack && HasOriginals)
                SaveToDisk();
            else
                DeleteDiskCopy(_key);
        }

        private static void DeleteDiskCopy(string key)
        {
            try { File.Delete(DiskPath(key)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // ------------------------------------------------------------------ inside a transaction

        /// <summary>
        /// Puts the remembered overrides back - only on elements that still carry a Quick Clash colour. An element the
        /// modeller has given an override of their own since is left as it is, and that override becomes its
        /// remembered copy - so a later Run and Reset give back the modeller's newer override, not the older one.
        /// Deleted elements are passed over. Returns how many were put back.
        /// </summary>
        internal int PutBack(Document doc, View view)
        {
            int count = 0;
            if (!_hasColours)
                return 0;   // never coloured (an empty, fresh memory): nothing of ours to take off
            ElementId solid = SolidFill(doc);
            var newerOwn = new Dictionary<long, OverrideGraphicSettings>();
            foreach (KeyValuePair<long, OverrideGraphicSettings> entry in _originals)
            {
                ElementId id = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(id) == null)
                    continue;
                OverrideGraphicSettings now = view.GetElementOverrides(id);
                if (!CarriesOurOverride(now, solid))
                {
                    newerOwn[entry.Key] = new OverrideGraphicSettings(now);
                    continue;
                }
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
            foreach (KeyValuePair<long, OverrideGraphicSettings> own in newerOwn)
                _originals[own.Key] = own.Value;
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

        /// <summary>The colours a Run has just put on. The view is coloured again: not put back.</summary>
        internal void SetColours(ClashColour a, ClashColour b, ClashColour rest, int restTransparency, ElementId solidFill)
        {
            if (_runJournal != null)
            {
                ClashColour oldA = ColourA, oldB = ColourB, oldRest = ColourRest;
                bool oldHas = _hasColours, oldPutBack = IsPutBack;
                int oldTransparency = RestTransparency;
                long oldFill = _solidFillId;
                _runJournal.Add(() =>
                {
                    ColourA = oldA; ColourB = oldB; ColourRest = oldRest;
                    _hasColours = oldHas; IsPutBack = oldPutBack;
                    RestTransparency = oldTransparency; _solidFillId = oldFill;
                    RestoreDiskCopy();
                });
            }
            ColourA = a; ColourB = b; ColourRest = rest;
            _hasColours = true;
            RestTransparency = Clamp(restTransparency);
            _solidFillId = (solidFill ?? ElementId.InvalidElementId).LongValue();
            IsPutBack = false;
        }

        /// <summary>
        /// The view is back to its own colours. Call only after the put-back transaction committed. The disk copy goes,
        /// so no later Revit session brings these old copies back over the modeller's newer overrides.
        /// </summary>
        internal void MarkPutBack()
        {
            IsPutBack = true;
            DeleteDiskCopy(_key);
        }

        /// <summary>An Undo of a Reset brought the colours back: keep the disk copy again, in case Revit closes.</summary>
        internal void MarkColouredAgain()
        {
            if (!IsPutBack) return;
            IsPutBack = false;
            SaveToDisk();
        }

        // ------------------------------------------------------------------ the Quick Clash override

        /// <summary>The override a Run puts on: line, cut line and solid fill in one colour, and a transparency.</summary>
        internal static OverrideGraphicSettings QuickClashOverride(ClashColour colour, int transparency, ElementId solidFill)
        {
            var c = new Color(colour.R, colour.G, colour.B);
            var o = new OverrideGraphicSettings();
            o.SetProjectionLineColor(c);
            o.SetCutLineColor(c);
            if (solidFill != null && solidFill != ElementId.InvalidElementId)
            {
                o.SetSurfaceForegroundPatternId(solidFill);
                o.SetSurfaceForegroundPatternVisible(true);
                o.SetSurfaceForegroundPatternColor(c);
                o.SetCutForegroundPatternId(solidFill);
                o.SetCutForegroundPatternVisible(true);
                o.SetCutForegroundPatternColor(c);
            }
            o.SetSurfaceTransparency(Clamp(transparency));
            return o;
        }

        /// <summary>The drafting solid fill - the one view overrides expect (as Highlight and Filter Pro use).</summary>
        internal static ElementId SolidFillId(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(p =>
                {
                    FillPattern fp = p.GetFillPattern();
                    return fp != null && fp.IsSolidFill && fp.Target == FillPatternTarget.Drafting;
                })?.Id ?? ElementId.InvalidElementId;
        }

        private ElementId SolidFill(Document doc) =>
            _solidFillId >= 0 ? ElementIdHelper.FromLong(_solidFillId) : SolidFillId(doc);

        /// <summary>True when the element carries the whole override of colour A, B or the rest - every field a Run sets.</summary>
        private bool CarriesOurOverride(OverrideGraphicSettings now, ElementId solid)
        {
            return SameQuickClashFields(now, QuickClashOverride(ColourA, 0, solid))
                || SameQuickClashFields(now, QuickClashOverride(ColourB, 0, solid))
                || SameQuickClashFields(now, QuickClashOverride(ColourRest, RestTransparency, solid));
        }

        // The fields QuickClashOverride sets, compared one by one (64-bit ids, colours as numbers).
        private static bool SameQuickClashFields(OverrideGraphicSettings x, OverrideGraphicSettings y)
        {
            return C(x.ProjectionLineColor) == C(y.ProjectionLineColor)
                && C(x.CutLineColor) == C(y.CutLineColor)
                && I(x.SurfaceForegroundPatternId) == I(y.SurfaceForegroundPatternId)
                && C(x.SurfaceForegroundPatternColor) == C(y.SurfaceForegroundPatternColor)
                && x.IsSurfaceForegroundPatternVisible == y.IsSurfaceForegroundPatternVisible
                && I(x.CutForegroundPatternId) == I(y.CutForegroundPatternId)
                && C(x.CutForegroundPatternColor) == C(y.CutForegroundPatternColor)
                && x.IsCutForegroundPatternVisible == y.IsCutForegroundPatternVisible
                && x.Transparency == y.Transparency;
        }

        private static int Clamp(int transparency) => Math.Max(0, Math.Min(100, transparency));

        // ------------------------------------------------------------------ is it still on?

        /// <summary>
        /// True when an element in the view still carries the full Quick Clash override (solid fill, colour and
        /// transparency of colour A, B or the rest) that is not its own remembered override. Every entry is looked at.
        /// Tells an undone Run - or a Reset - from colours still on screen, or brought back by an Undo of a Reset.
        /// A modeller's own colour (a red line, a green fill) is not the full override, so it never counts.
        /// </summary>
        internal bool StillColoured(Document doc, View view)
        {
            if (!_hasColours)
                return false;   // no Run ever coloured with this memory
            ElementId solid = SolidFill(doc);
            foreach (KeyValuePair<long, OverrideGraphicSettings> entry in _originals)
            {
                ElementId id = ElementIdHelper.FromLong(entry.Key);
                if (doc.GetElement(id) == null)
                    continue;
                if (CarriesOurOverride(view.GetElementOverrides(id), solid) && !CarriesOurOverride(entry.Value, solid))
                    return true;
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
                    "colours=" + ColourA + ";" + ColourB + ";" + ColourRest,
                    "rest-transparency=" + RestTransparency.ToString(CultureInfo.InvariantCulture),
                    "solid-fill=" + _solidFillId.ToString(CultureInfo.InvariantCulture)
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
                        {
                            memory.ColourA = ClashColour.Parse(c[0], QuickClashSettings.DefaultColourA);
                            memory.ColourB = ClashColour.Parse(c[1], QuickClashSettings.DefaultColourB);
                            memory.ColourRest = ClashColour.Parse(c[2], QuickClashSettings.DefaultColourRest);
                            memory._hasColours = true;
                        }
                        continue;
                    }
                    if (line.StartsWith("rest-transparency=", StringComparison.Ordinal))
                    {
                        if (int.TryParse(line.Substring(18), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rt))
                            memory.RestTransparency = Clamp(rt);
                        continue;
                    }
                    if (line.StartsWith("solid-fill=", StringComparison.Ordinal))
                    {
                        if (long.TryParse(line.Substring(11), NumberStyles.Integer, CultureInfo.InvariantCulture, out long sf))
                            memory._solidFillId = sf;
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
