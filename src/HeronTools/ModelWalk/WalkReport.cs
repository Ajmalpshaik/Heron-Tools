// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - the snag report: one folder per walk, with Snags.csv (opens in a spreadsheet) and one picture per
// snag, plus any snapshots. The folder sits next to the model ("<model folder>\Model Walk - <model>\<date time>");
// a model that is not saved, a cloud model, or a folder that cannot be written falls back to
// Documents\Heron Model Walk\<model>\<date time>.
//
// The CSV is written again after every snag, comment and picture, so nothing is lost if Revit stops; the end of the
// walk writes it one last time and says where it is (exactly once, however the walk ends). A failed write is said,
// never swallowed. Positions are in SHARED coordinates (east / north / elevation, mm) - the numbers Revit's Spot
// Coordinates show - not Revit's internal coordinates. Element ids are the full 64-bit value.

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
        public string Comment = "";

        // What was hit
        public string Category = "";
        public string FamilyType = "";
        public string Size = "";
        public long ElementIdValue;       // full 64-bit id, in the element's own model (the link's, for a linked element)
        public string UniqueId = "";
        public string Level = "";
        public string System = "";
        public string LinkName = "";      // empty for this model's own elements
        public string Room = "";          // where the coordinator stood

        /// <summary>This model's element, for Open snag; null for a linked element (it cannot be selected).</summary>
        public ElementId HostElementId;

        // Where, in Revit internal feet, and how the camera looked
        public XYZ HitPoint;
        public XYZ Eye;
        public double YawDeg;
        public double PitchDeg;

        public string PictureFile = "";   // file name inside the walk folder
        public bool Coloured;
        public string NotColouredWhy;

        /// <summary>"Ducts - Rectangular Duct : Supply" - for the pause menu's snag list.</summary>
        public string Summary => Category + (string.IsNullOrEmpty(FamilyType) ? "" : " - " + FamilyType);
    }

    internal sealed class WalkReport
    {
        private const string CsvName = "Snags.csv";

        private readonly List<WalkSnag> _snags = new List<WalkSnag>();
        private readonly Transform _toShared;
        private readonly string _modelFolder;   // next to the model, or null
        private readonly DateTime _started = DateTime.Now;
        private string _folder;
        private string _lastProblem;
        private bool _written;

        /// <summary>Call on Revit's thread (the command): the report may be written after the model closed.</summary>
        public WalkReport(Document doc)
        {
            ModelTitle = doc.Title;
            Author = doc.Application?.Username ?? "";
            try { _toShared = doc.ActiveProjectLocation?.GetTotalTransform()?.Inverse; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { _toShared = null; }
            _modelFolder = FolderNextToModel(doc);
        }

        public string ModelTitle { get; }
        public string Author { get; }
        public int Count => _snags.Count;
        public IReadOnlyList<WalkSnag> Snags => _snags;

        /// <summary>The walk folder once something was saved in it, for the end message and the pause menu; else null.</summary>
        public string FolderPath => _folder;

        public WalkSnag Add(WalkSnag snag)
        {
            snag.Number = _snags.Count + 1;
            snag.Time = DateTime.Now;
            _snags.Add(snag);
            return snag;
        }

        public WalkSnag Find(int number) => number >= 1 && number <= _snags.Count ? _snags[number - 1] : null;

        // ------------------------------------------------------------------ the folder

        private static string FolderNextToModel(Document doc)
        {
            try
            {
                string path = doc.PathName;
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                    return null;   // not saved yet, or a cloud model
                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return null;
                return Path.Combine(dir, "Model Walk - " + SafeName(doc.Title));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is IOException ||
                                       ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                return null;   // a path Windows cannot read (a cloud path): use Documents
            }
        }

        /// <summary>
        /// This walk's folder - made the first time something is saved. Next to the model when it can be written there,
        /// else under Documents. Throws IOException / UnauthorizedAccessException when neither can be made.
        /// </summary>
        public string EnsureFolder()
        {
            if (_folder != null)
                return _folder;
            string stamp = _started.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
            if (_modelFolder != null)
            {
                try
                {
                    string next = Path.Combine(_modelFolder, stamp);
                    Directory.CreateDirectory(next);
                    _folder = next;
                    return _folder;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                {
                    // A read-only model folder (a server share): fall back to Documents below.
                }
            }
            string documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heron Model Walk",
                                            SafeName(ModelTitle), stamp);
            Directory.CreateDirectory(documents);
            _folder = documents;
            return _folder;
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

        // ------------------------------------------------------------------ writing

        /// <summary>
        /// Writes Snags.csv now (after each snag, comment or picture). Returns null, or why it could not be written.
        /// Never throws: a walk must never stop for a file.
        /// </summary>
        public string Save()
        {
            if (_snags.Count == 0)
                return null;
            try
            {
                string folder = EnsureFolder();
                string file = Path.Combine(folder, CsvName);
                string temp = file + ".tmp";
                // UTF-8 with the byte-order mark, so a spreadsheet shows accents and non-English names correctly.
                File.WriteAllText(temp, BuildCsv(), new UTF8Encoding(true));
                if (File.Exists(file))
                    File.Replace(temp, file, null);
                else
                    File.Move(temp, file);
                _lastProblem = null;
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                // Excel holding the file open is the usual cause: say so, and try again on the next change.
                _lastProblem = ex.Message;
                return "The snag report could not be written (" + ex.Message + "). Close it if a spreadsheet has it open - it is tried again.";
            }
        }

        /// <summary>
        /// The end of the walk: writes the report one last time (only the first time it is called, and only when
        /// something was snagged). Returns the line for the end message, or null when there is nothing to say.
        /// </summary>
        public string WriteOnce()
        {
            if (_written)
                return null;
            _written = true;
            if (_snags.Count == 0)
                return null;
            string problem = Save();
            if (problem != null)
                return "The snag report could NOT be saved (" + (_lastProblem ?? problem) + "). Nothing in the model was lost.";
            return "Snag report (" + _snags.Count + (_snags.Count == 1 ? " snag" : " snags") + "): " + Path.Combine(_folder, CsvName);
        }

        private string BuildCsv()
        {
            bool shared = _toShared != null;
            string axes = shared ? " (mm, shared)" : " (mm, internal)";
            var r = new StringBuilder();
            Row(r, "No.", "Time", "Author", "Comment", "Category", "Family : Type", "Size", "Element ID", "Unique ID",
                "Level", "System", "Link", "Room", "East" + axes, "North" + axes, "Elevation" + axes, "Camera", "Picture",
                "Snag colour");
            foreach (WalkSnag s in _snags)
            {
                XYZ p = ToReport(s.HitPoint);
                Row(r,
                    s.Number.ToString(CultureInfo.InvariantCulture),
                    s.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Author,
                    s.Comment,
                    s.Category,
                    s.FamilyType,
                    s.Size,
                    s.ElementIdValue.ToString(CultureInfo.InvariantCulture),
                    s.UniqueId,
                    s.Level,
                    s.System,
                    s.LinkName,
                    s.Room,
                    Mm(p?.X), Mm(p?.Y), Mm(p?.Z),
                    Camera(s),
                    s.PictureFile,
                    s.Coloured ? "coloured in the walk view" : "not coloured: " + s.NotColouredWhy);
            }
            return r.ToString();
        }

        private XYZ ToReport(XYZ p) => p == null ? null : (_toShared != null ? _toShared.OfPoint(p) : p);

        private static string Mm(double? feet) =>
            feet.HasValue ? Math.Round(WalkTuning.FtToMm(feet.Value)).ToString("0", CultureInfo.InvariantCulture) : "";

        /// <summary>"E 1234 N 5678 Z 9012 - facing 45 deg, 10 deg down" (eye in the report's coordinates).</summary>
        private string Camera(WalkSnag s)
        {
            XYZ eye = ToReport(s.Eye);
            if (eye == null)
                return "";
            double pitch = Math.Round(s.PitchDeg);
            string tilt = pitch > 0 ? pitch.ToString("0", CultureInfo.InvariantCulture) + " deg up"
                        : pitch < 0 ? (-pitch).ToString("0", CultureInfo.InvariantCulture) + " deg down" : "level";
            double yaw = ((s.YawDeg % 360.0) + 360.0) % 360.0;
            return "E " + Mm(eye.X) + " N " + Mm(eye.Y) + " Z " + Mm(eye.Z) + " - facing " +
                   Math.Round(yaw).ToString("0", CultureInfo.InvariantCulture) + " deg from project east, " + tilt;
        }

        private static void Row(StringBuilder r, params string[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) r.Append(',');
                r.Append(Cell(cells[i]));
            }
            r.Append("\r\n");
        }

        /// <summary>Quoted when it holds a comma, quote or line break; a leading = + - @ is made plain text (no formulas).</summary>
        private static string Cell(string text)
        {
            text = text ?? "";
            if (text.Length > 0 && "=+-@".IndexOf(text[0]) >= 0 && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                text = "'" + text;
            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
