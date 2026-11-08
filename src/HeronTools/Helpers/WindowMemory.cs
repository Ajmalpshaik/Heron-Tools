// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// What a window remembers between uses - also after Revit restarts: one small text file per tool,
// %AppData%\HeronTools\memory-<tool>.txt, one "key=value" per line. Lists are tab-separated.
//
// RULES FOR EVERY WINDOW THAT USES IT
//   - Remember NAMES, never element ids: ids differ between models (a level is "Level 2" in every model,
//     its id is not). Categories may be remembered by their built-in number, which is the same everywhere.
//   - On opening, apply only what exists in this model; anything missing is quietly skipped.
//   - Save when the tool RUNS (the choices that were used), not on every click.
//   - A missing or damaged file means the window opens with its defaults - never an error.
// Saved with a temporary file swapped in, so a crash mid-write cannot leave half a file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace HeronTools.Utils
{
    internal sealed class WindowMemory
    {
        private readonly string _path;
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private WindowMemory(string tool)
        {
            _path = AppDataConfigStore.GetPath("memory-" + tool + ".txt");
        }

        /// <summary>True when this tool has nothing remembered yet (first use, or the file could not be read).</summary>
        public bool IsEmpty => _values.Count == 0;

        /// <summary>The remembered choices of one tool; empty when there are none (or the file is unreadable).</summary>
        public static WindowMemory Load(string tool)
        {
            var memory = new WindowMemory(tool);
            try
            {
                if (File.Exists(memory._path))
                {
                    foreach (string line in File.ReadAllLines(memory._path, Encoding.UTF8))
                    {
                        if (line.Length == 0 || line[0] == '#')
                            continue;
                        int eq = line.IndexOf('=');
                        if (eq > 0)
                            memory._values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1);
                    }
                }
            }
            catch (Exception)
            {
                memory._values.Clear();   // unreadable: the window opens with its defaults
            }
            return memory;
        }

        // ------------------------------------------------------------------ reading

        public string Get(string key, string fallback = null) =>
            _values.TryGetValue(key, out string value) ? value : fallback;

        public bool GetBool(string key, bool fallback) =>
            _values.TryGetValue(key, out string value) && bool.TryParse(value, out bool b) ? b : fallback;

        public int GetInt(string key, int fallback) =>
            _values.TryGetValue(key, out string value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : fallback;

        public long GetLong(string key, long fallback) =>
            _values.TryGetValue(key, out string value) &&
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? l : fallback;

        public double GetDouble(string key, double fallback) =>
            _values.TryGetValue(key, out string value) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) &&
            !double.IsNaN(d) && !double.IsInfinity(d) ? d : fallback;

        /// <summary>A remembered list; empty when there is none.</summary>
        public IList<string> GetList(string key) =>
            _values.TryGetValue(key, out string value) && value.Length > 0
                ? value.Split('\t').Where(s => s.Length > 0).ToList()
                : new List<string>();

        // ------------------------------------------------------------------ writing

        public void Set(string key, string value) => _values[key] = Clean(value ?? "");

        public void Set(string key, bool value) => _values[key] = value ? "True" : "False";

        public void Set(string key, int value) => _values[key] = value.ToString(CultureInfo.InvariantCulture);

        public void Set(string key, long value) => _values[key] = value.ToString(CultureInfo.InvariantCulture);

        public void Set(string key, double value) => _values[key] = value.ToString("R", CultureInfo.InvariantCulture);

        public void Set(string key, IEnumerable<string> items) =>
            _values[key] = string.Join("\t", (items ?? Enumerable.Empty<string>()).Select(Clean).Where(s => s.Length > 0));

        /// <summary>Writes the file. Best effort: false (with the reason) when it could not be written.</summary>
        public bool Save(out string error)
        {
            error = null;
            try
            {
                string folder = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);

                var lines = new List<string> { "# Heron Tools - what this window remembers. Safe to delete: it then opens with its defaults." };
                lines.AddRange(_values.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "=" + p.Value));

                string temp = _path + ".tmp";
                File.WriteAllLines(temp, lines, new UTF8Encoding(false));
                if (File.Exists(_path))
                    File.Replace(temp, _path, null);
                else
                    File.Move(temp, _path);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Saves and forgets any failure - a lost memory is never worth an error box.</summary>
        public void SaveQuietly() => Save(out _);

        /// <summary>No line breaks or tabs inside a value - they would split the file's lines and lists.</summary>
        private static string Clean(string value) =>
            value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");   // kept exactly otherwise: " - " stays " - "
    }
}
