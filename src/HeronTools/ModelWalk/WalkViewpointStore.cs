// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - named viewpoints kept per model, for this Windows user, in
// %AppData%\HeronTools\ModelWalk Viewpoints\<model> <key>.txt. Not in the Revit file: a camera set with
// SetOrientation is temporary, and saving it into the model (SaveOrientation) would change the walk view for
// everyone. A workshared model is known by its central model's path, so every local copy finds the same
// viewpoints; a saved model by its path; a model never saved by its title only. No Revit API here except
// ModelFile, which reads the model's path on Revit's thread.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using HeronTools.Utils;

namespace HeronTools.Services.ModelWalk
{
    internal static class WalkViewpointStore
    {
        private const string Folder = "ModelWalk Viewpoints";
        private const string Header = "# Heron Model Walk viewpoints - name, eye X Y Z (Revit internal feet), yaw, pitch (degrees)";

        /// <summary>The viewpoint file for this model. Call on Revit's thread.</summary>
        public static string ModelFile(Document doc)
        {
            string key = null;
            try
            {
                if (doc.IsWorkshared)
                {
                    ModelPath central = doc.GetWorksharingCentralModelPath();
                    if (central != null)
                        key = ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                key = null;   // no central path to read: use the file's own path below
            }
            if (string.IsNullOrWhiteSpace(key))
                key = doc.PathName;
            if (string.IsNullOrWhiteSpace(key))
                key = "unsaved:" + doc.Title;

            string name = WalkReport.SafeName(Path.GetFileNameWithoutExtension(doc.Title ?? "Model")) + " " + Hash(key.Trim().ToLowerInvariant()) + ".txt";
            return AppDataConfigStore.GetPath(Path.Combine(Folder, name));
        }

        /// <summary>FNV-1a, 64-bit: the same text always gives the same file name on every computer and .NET version.</summary>
        private static string Hash(string text)
        {
            ulong h = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            return h.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>The saved viewpoints, numbered 1, 2, 3... A line that cannot be read is skipped and counted in <paramref name="skipped"/>.</summary>
        public static List<WalkViewpoint> Load(string file, out int skipped, out string problem)
        {
            skipped = 0;
            problem = null;
            var list = new List<WalkViewpoint>();
            if (string.IsNullOrEmpty(file))
                return list;
            string[] lines;
            try
            {
                if (!File.Exists(file))
                    return list;
                lines = File.ReadAllLines(file, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                problem = "The saved viewpoints could not be read (" + ex.Message + ").";
                return list;
            }

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                string[] p = line.Split('\t');
                if (p.Length != 6 || string.IsNullOrWhiteSpace(p[0]) ||
                    !Read(p[1], out double x) || !Read(p[2], out double y) || !Read(p[3], out double z) ||
                    !Read(p[4], out double yaw) || !Read(p[5], out double pitch) || Math.Abs(pitch) > 90)
                {
                    skipped++;
                    continue;
                }
                list.Add(new WalkViewpoint { Number = list.Count + 1, Name = p[0].Trim(), Eye = new XYZ(x, y, z), YawDeg = yaw, PitchDeg = pitch });
            }
            return list;
        }

        private static bool Read(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Saves the list (through a temporary file). Returns null, or why it could not be saved.</summary>
        public static string Save(string file, IList<WalkViewpoint> viewpoints)
        {
            if (string.IsNullOrEmpty(file))
                return "There is no place to keep viewpoints for this model.";
            string temp = file + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var lines = new List<string> { Header };
                foreach (WalkViewpoint vp in viewpoints)
                {
                    lines.Add(string.Join("\t", new[]
                    {
                        CleanName(vp.Name),
                        vp.Eye.X.ToString("R", CultureInfo.InvariantCulture),
                        vp.Eye.Y.ToString("R", CultureInfo.InvariantCulture),
                        vp.Eye.Z.ToString("R", CultureInfo.InvariantCulture),
                        vp.YawDeg.ToString("R", CultureInfo.InvariantCulture),
                        vp.PitchDeg.ToString("R", CultureInfo.InvariantCulture)
                    }));
                }
                File.WriteAllLines(temp, lines, new UTF8Encoding(false));
                if (File.Exists(file))
                    File.Replace(temp, file, null);
                else
                    File.Move(temp, file);
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return "The viewpoints could not be saved (" + ex.Message + "). They still work until the walk ends.";
            }
        }

        /// <summary>One line, no tabs.</summary>
        public static string CleanName(string name)
        {
            string clean = (name ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length == 0 ? "Viewpoint" : clean;
        }
    }
}
