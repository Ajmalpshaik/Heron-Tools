// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - the snag report and the snapshot folder. One folder per model under Documents\Heron Model Walk\.
// The report is written exactly once per walk, however the walk ends; a failed write is said, never swallowed.
// Positions are written in SHARED coordinates (east / north / elevation, mm), the numbers a coordinator sees in
// Revit's Spot Coordinates, with the level - not Revit's internal coordinates.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using HeronTools.Utils;

namespace HeronTools.Services.ModelWalk
{
    internal sealed class WalkSnag
    {
        public int Number;
        public DateTime Time;
        public string What;        // category - family : type - ID
        public string Where;       // level - system - link
        public string UniqueId;
        public XYZ HitPoint;       // internal feet
        public XYZ Eye;            // internal feet
        public bool Coloured;
        public string NotColouredWhy;
    }

    internal sealed class WalkReport
    {
        private readonly List<WalkSnag> _snags = new List<WalkSnag>();
        private bool _written;

        public WalkReport(Document doc)
        {
            ModelTitle = doc.Title;
            Author = doc.Application?.Username ?? "";
            // Read once, on Revit's thread: the report may be written from the HUD after the model closed.
            try { _toShared = doc.ActiveProjectLocation?.GetTotalTransform()?.Inverse; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { _toShared = null; }
        }

        private readonly Transform _toShared;

        public string ModelTitle { get; }
        public string Author { get; }
        public int Count => _snags.Count;

        public WalkSnag Add(string what, string where, string uniqueId, XYZ hit, XYZ eye)
        {
            var snag = new WalkSnag
            {
                Number = _snags.Count + 1,
                Time = DateTime.Now,
                What = what,
                Where = where,
                UniqueId = uniqueId,
                HitPoint = hit,
                Eye = eye
            };
            _snags.Add(snag);
            return snag;
        }

        /// <summary>Documents\Heron Model Walk\&lt;model&gt; - made if missing. Throws IOException / UnauthorizedAccessException.</summary>
        public static string Folder(string modelTitle)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heron Model Walk");
            string folder = Path.Combine(root, SafeName(modelTitle));
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static string SafeName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "Model";
            var sb = new StringBuilder();
            foreach (char c in text.Trim())
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        /// <summary>
        /// Writes the report (only the first time it is called, and only when something was snagged). Returns the line
        /// for the end message: where it went, why it could not be written, or null when there is nothing to say.
        /// Never throws: the walk must always end cleanly.
        /// </summary>
        public string WriteOnce()
        {
            if (_written)
                return null;
            _written = true;
            if (_snags.Count == 0)
                return null;

            try
            {
                string folder = Folder(ModelTitle);
                string file = Path.Combine(folder, "Snags_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".txt");
                File.WriteAllText(file, Build(), Encoding.UTF8);
                return "Snag report saved: Documents\\Heron Model Walk\\" + SafeName(ModelTitle) + "\\" + Path.GetFileName(file);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                return "The snag report could NOT be saved (" + ex.Message + "). Nothing in the model was lost.";
            }
        }

        private string Build()
        {
            var r = new StringBuilder();
            r.AppendLine("Heron Model Walk - snag report");
            r.AppendLine("Model:  " + ModelTitle);
            r.AppendLine("Date:   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(Author))
                r.AppendLine("By:     " + Author);
            r.AppendLine(_toShared != null
                ? "Points: shared coordinates, mm (East / North / Elevation), as Revit's Spot Coordinates show them."
                : "Points: Revit internal coordinates, mm (the shared coordinates could not be read).");
            r.AppendLine(new string('-', 78));
            foreach (WalkSnag s in _snags)
            {
                r.AppendLine(s.Number + ".  " + s.What);
                if (!string.IsNullOrWhiteSpace(s.Where))
                    r.AppendLine("    " + s.Where);
                r.AppendLine("    Point " + Point(s.HitPoint) + "    Eye " + Point(s.Eye));
                if (!string.IsNullOrWhiteSpace(s.UniqueId))
                    r.AppendLine("    Unique ID " + s.UniqueId);
                r.AppendLine("    " + s.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
                             (s.Coloured ? "    coloured in the walk view" : "    not coloured: " + s.NotColouredWhy));
            }
            return r.ToString();
        }

        private string Point(XYZ p)
        {
            if (p == null)
                return "-";
            XYZ q = _toShared != null ? _toShared.OfPoint(p) : p;
            return string.Format(CultureInfo.InvariantCulture, "E {0:N0}  N {1:N0}  Z {2:N0}",
                WalkTuning.FtToMm(q.X), WalkTuning.FtToMm(q.Y), WalkTuning.FtToMm(q.Z));
        }
    }
}
