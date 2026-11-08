// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  platform
// See docs/metadata-standard.md

// The installer's engine: reads the Heron Tools build packed inside the .exe, finds the Revit versions on the
// PC, and installs or removes Heron Tools for each - with the same safeguards as build\build.ps1 (which in
// turn follow the Heron AI platform's installer; no code shared):
//   - per user only (%AppData%\Autodesk\Revit\Addins\<year>), no administrator, every Revit 2020-2027;
//   - never while that Revit is open, and nothing at all while a Revit of unknown year is open; never closes Revit;
//   - never writes through a link (a dev junction) - emptying it could empty its target;
//   - keeps the install it replaces in %LocalAppData%\HeronTools\install-backup\<year> (the same folder
//     build.ps1 -Rollback reads), and puts it back by itself if the new copy fails;
//   - checks every file it wrote, byte for byte, against the build it carries;
//   - touches nothing but Heron Tools' own HeronTools.addin and HeronTools folder.
// It decides nothing about the window; the window decides nothing about installing.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HeronTools.Installer
{
    /// <summary>One file of the build for one Revit year, as the payload index lists it.</summary>
    internal sealed class PayloadFile
    {
        public int Year;
        /// <summary>"HeronTools.addin" or "HeronTools/HeronTools.dll" - relative to that year's Addins folder.</summary>
        public string Path;
        public string Sha256;
    }

    /// <summary>The Heron Tools build packed inside the installer.</summary>
    internal sealed class Payload
    {
        public string Version = "";
        public string Built = "";
        public readonly List<PayloadFile> Files = new List<PayloadFile>();
        public byte[] Zip;

        public IEnumerable<int> Years => Files.Select(f => f.Year).Distinct().OrderBy(y => y);

        /// <summary>"1.0.0+abc1234..." shown as "1.0.0 (abc1234)".</summary>
        public string ShortVersion => InstallEngine.Short(Version);

        /// <summary>The build inside this .exe, or null when it was built without one.</summary>
        public static Payload Load()
        {
            using (Stream stream = typeof(Payload).Assembly.GetManifestResourceStream("HeronTools.Payload.zip"))
            {
                if (stream == null)
                    return null;
                var memory = new MemoryStream();
                stream.CopyTo(memory);
                return FromZip(memory.ToArray());
            }
        }

        /// <summary>
        /// payload.txt, one line per file: "year|path|sha256"; plus "version=" and "built=". Plain text on
        /// purpose - .NET Framework 4.8 has no JSON reader without a package.
        /// </summary>
        public static Payload FromZip(byte[] zip)
        {
            var payload = new Payload { Zip = zip };
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                ZipArchiveEntry index = archive.GetEntry("payload.txt")
                                        ?? throw new InvalidDataException("The installer's build has no payload.txt.");
                using (var reader = new StreamReader(index.Open(), Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                            continue;
                        if (line.StartsWith("version=", StringComparison.Ordinal))
                            payload.Version = line.Substring(8);
                        else if (line.StartsWith("built=", StringComparison.Ordinal))
                            payload.Built = line.Substring(6);
                        else
                        {
                            string[] parts = line.Split('|');
                            if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int year))
                                throw new InvalidDataException("The installer's build has a damaged line: " + line);
                            payload.Files.Add(new PayloadFile { Year = year, Path = parts[1], Sha256 = parts[2].ToUpperInvariant() });
                        }
                    }
                }
            }
            if (payload.Files.Count == 0)
                throw new InvalidDataException("The installer's build lists no files.");
            return payload;
        }

        public byte[] Read(PayloadFile file)
        {
            using (var archive = new ZipArchive(new MemoryStream(Zip), ZipArchiveMode.Read))
            {
                ZipArchiveEntry entry = archive.GetEntry(file.Year.ToString(CultureInfo.InvariantCulture) + "/" + file.Path)
                                        ?? throw new InvalidDataException("The installer's build is missing " + file.Path + " for Revit " + file.Year + ".");
                using (Stream s = entry.Open())
                {
                    var memory = new MemoryStream();
                    s.CopyTo(memory);
                    return memory.ToArray();
                }
            }
        }
    }

    /// <summary>Where things are on this PC. The roots can be pointed elsewhere for a test run.</summary>
    internal sealed class MachinePaths
    {
        public string AddinsRoot = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins");
        public string RevitRoot = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk");
        public string BackupRoot = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"HeronTools\install-backup");
        /// <summary>False in a test run: a real Revit on the PC must not block a run into a test folder.</summary>
        public bool WatchRunningRevit = true;
    }

    /// <summary>Where one Revit year stands right now.</summary>
    internal sealed class YearState
    {
        public int Year;
        public bool RevitInstalled;
        public bool RevitOpen;
        /// <summary>The Heron Tools version installed for this year, or null.</summary>
        public string Installed;
        /// <summary>Another Heron Tools in the all-users folder - Revit would load two.</summary>
        public string AllUsersCopy;
    }

    /// <summary>What happened to one year.</summary>
    internal sealed class StepResult
    {
        public int Year;
        public bool Ok;
        public string Message;
    }

    internal sealed class InstallEngine
    {
        private const string Product = "HeronTools";
        private readonly Payload _payload;
        private readonly MachinePaths _paths;

        public InstallEngine(Payload payload, MachinePaths paths)
        {
            _payload = payload;
            _paths = paths ?? new MachinePaths();
        }

        public static string Short(string version)
        {
            if (string.IsNullOrEmpty(version))
                return "";
            int plus = version.IndexOf('+');
            return plus > 0
                ? version.Substring(0, plus) + " (" + version.Substring(plus + 1, Math.Min(7, version.Length - plus - 1)) + ")"
                : version;
        }

        private string YearFolder(int year) => Path.Combine(_paths.AddinsRoot, year.ToString(CultureInfo.InvariantCulture));
        private string ProductFolder(int year) => Path.Combine(YearFolder(year), Product);
        private string AddinFile(int year) => Path.Combine(YearFolder(year), Product + ".addin");
        private string BackupFolder(int year) => Path.Combine(_paths.BackupRoot, year.ToString(CultureInfo.InvariantCulture));

        // ------------------------------------------------------------------ reading the PC

        /// <summary>Every running Revit's year, from its program folder ("Revit 2024\Revit.exe"); 0 = cannot be read.</summary>
        public List<int> RunningRevitYears()
        {
            var years = new List<int>();
            if (!_paths.WatchRunningRevit)
                return years;
            foreach (Process process in Process.GetProcessesByName("Revit"))
            {
                int year = 0;
                try
                {
                    Match m = Regex.Match(process.MainModule?.FileName ?? "", @"Revit[ _]?(\d{4})", RegexOptions.IgnoreCase);
                    if (m.Success)
                        year = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    year = 0;   // no access to its path: its year is unknown
                }
                finally
                {
                    process.Dispose();
                }
                years.Add(year);
            }
            return years;
        }

        public bool UnknownRevitOpen() => RunningRevitYears().Contains(0);

        public List<YearState> ReadYears()
        {
            List<int> running = RunningRevitYears();
            var states = new List<YearState>();
            foreach (int year in _payload?.Years ?? Enumerable.Empty<int>())
            {
                var state = new YearState
                {
                    Year = year,
                    RevitInstalled = File.Exists(Path.Combine(_paths.RevitRoot, "Revit " + year, "Revit.exe")),
                    RevitOpen = running.Contains(year),
                    Installed = InstalledVersion(year),
                    AllUsersCopy = AllUsersCopy(year),
                };
                states.Add(state);
            }
            return states;
        }

        private string InstalledVersion(int year)
        {
            string dll = Path.Combine(ProductFolder(year), Product + ".dll");
            if (!File.Exists(dll) || !File.Exists(AddinFile(year)))
                return null;
            try
            {
                string v = FileVersionInfo.GetVersionInfo(dll).ProductVersion;
                return string.IsNullOrEmpty(v) ? "installed" : v;
            }
            catch (Exception)
            {
                return "installed";
            }
        }

        private static string AllUsersCopy(int year)
        {
            var places = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                             @"Autodesk\Revit\Addins", year.ToString(CultureInfo.InvariantCulture), Product + ".addin"),
            };
            if (year >= 2027)
                places.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                        @"Autodesk\Revit\Addins", year.ToString(CultureInfo.InvariantCulture), Product + ".addin"));
            return places.FirstOrDefault(File.Exists);
        }

        private static bool IsLink(string path)
        {
            return Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        // ------------------------------------------------------------------ install

        public StepResult Install(int year)
        {
            var result = new StepResult { Year = year };
            List<int> running = RunningRevitYears();
            if (running.Contains(0))
                return Fail(result, $"'Heron Tools' was not installed for Revit {year}. A Revit is open and which one it is could not be read - close every Revit.");
            if (running.Contains(year))
                return Fail(result, $"'Heron Tools' was not installed for Revit {year}. Revit {year} is open - close it.");

            List<PayloadFile> files = _payload.Files.Where(f => f.Year == year).ToList();
            if (files.Count == 0)
                return Fail(result, $"'Heron Tools' was not installed for Revit {year}. This installer has no build for it.");

            string productFolder = ProductFolder(year);
            if (IsLink(productFolder))
                return Fail(result, $"'Heron Tools' was not installed for Revit {year}. {productFolder} is a link - remove it by hand.");

            string kept;
            try
            {
                kept = KeepPrevious(year);
            }
            catch (Exception ex)
            {
                return Fail(result, $"'Heron Tools' was not installed for Revit {year}. The install already there could not be kept first, so it was left alone. {ex.Message}");
            }

            try
            {
                Directory.CreateDirectory(productFolder);
                foreach (string old in Directory.GetFiles(productFolder))
                    File.Delete(old);
                foreach (string old in Directory.GetDirectories(productFolder))
                    Directory.Delete(old, true);

                foreach (PayloadFile file in files)
                {
                    string target = Path.Combine(YearFolder(year), file.Path.Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllBytes(target, _payload.Read(file));
                    // Written by this program, so Windows puts no "downloaded from the internet" mark on it.
                }

                // Proof: every file on disk is byte for byte the one this installer carries.
                foreach (PayloadFile file in files)
                {
                    string target = Path.Combine(YearFolder(year), file.Path.Replace('/', '\\'));
                    if (!string.Equals(Sha256Of(target), file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException(Path.GetFileName(target) + " did not copy correctly");
                }
            }
            catch (Exception ex)
            {
                // Half a new install is worse than the old one: put the old one back.
                string putBack = kept != null ? (RestorePrevious(year) ?? "the previous install was put back") : "nothing was installed before";
                return Fail(result, $"'Heron Tools' could not be installed for Revit {year}. {ex.Message} - {putBack}.");
            }

            result.Ok = true;
            result.Message = $"'Heron Tools' installed for Revit {year}.";
            string allUsers = AllUsersCopy(year);
            if (allUsers != null)
                result.Message += $" Another copy is in the all-users folder ({allUsers}) - Revit would load both; remove one.";
            return result;
        }

        /// <summary>Keeps the install about to be replaced (one level: the last one). Null when there was none.</summary>
        private string KeepPrevious(int year)
        {
            string dll = Path.Combine(ProductFolder(year), Product + ".dll");
            if (!File.Exists(dll))
                return null;

            string backup = BackupFolder(year);
            if (Directory.Exists(backup))
                Directory.Delete(backup, true);
            string keptProduct = Path.Combine(backup, Product);
            Directory.CreateDirectory(keptProduct);
            foreach (string file in Directory.GetFiles(ProductFolder(year)))
                File.Copy(file, Path.Combine(keptProduct, Path.GetFileName(file)), true);
            if (File.Exists(AddinFile(year)))
                File.Copy(AddinFile(year), Path.Combine(backup, Product + ".addin"), true);

            string version = InstalledVersion(year) ?? "";
            string json = "{\r\n" +
                          "  \"Year\": " + year.ToString(CultureInfo.InvariantCulture) + ",\r\n" +
                          "  \"Version\": \"" + version.Replace("\"", "") + "\",\r\n" +
                          "  \"Sha256\": \"" + Sha256Of(dll) + "\",\r\n" +
                          "  \"SavedAt\": \"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\"\r\n" +
                          "}\r\n";
            File.WriteAllText(Path.Combine(backup, "replaced.json"), json, new UTF8Encoding(false));
            return backup;
        }

        /// <summary>Puts the kept install back. Null when it worked, or what went wrong.</summary>
        private string RestorePrevious(int year)
        {
            try
            {
                string keptProduct = Path.Combine(BackupFolder(year), Product);
                string productFolder = ProductFolder(year);
                Directory.CreateDirectory(productFolder);
                foreach (string old in Directory.GetFiles(productFolder))
                    File.Delete(old);
                foreach (string file in Directory.GetFiles(keptProduct))
                    File.Copy(file, Path.Combine(productFolder, Path.GetFileName(file)), true);
                string keptAddin = Path.Combine(BackupFolder(year), Product + ".addin");
                if (File.Exists(keptAddin))
                    File.Copy(keptAddin, AddinFile(year), true);
                return null;
            }
            catch (Exception ex)
            {
                return "putting the previous install back failed too: " + ex.Message;
            }
        }

        // ------------------------------------------------------------------ remove

        public StepResult Remove(int year)
        {
            var result = new StepResult { Year = year };
            List<int> running = RunningRevitYears();
            if (running.Contains(0))
                return Fail(result, $"'Heron Tools' was not removed from Revit {year}. A Revit is open and which one it is could not be read - close every Revit.");
            if (running.Contains(year))
                return Fail(result, $"'Heron Tools' was not removed from Revit {year}. Revit {year} is open - close it.");

            string productFolder = ProductFolder(year);
            if (IsLink(productFolder))
                return Fail(result, $"'Heron Tools' was not removed from Revit {year}. {productFolder} is a link - remove it by hand.");

            try
            {
                if (File.Exists(AddinFile(year)))
                    File.Delete(AddinFile(year));
                if (Directory.Exists(productFolder))
                    Directory.Delete(productFolder, true);
            }
            catch (Exception ex)
            {
                return Fail(result, $"'Heron Tools' could not be removed from Revit {year}. {ex.Message}");
            }

            result.Ok = true;
            result.Message = $"'Heron Tools' removed from Revit {year}.";
            return result;
        }

        private static StepResult Fail(StepResult result, string message)
        {
            result.Ok = false;
            result.Message = message;
            return result;
        }

        public static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            }
        }
    }
}
