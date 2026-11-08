// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  platform
// See docs/metadata-standard.md

// HeronToolsInstaller.exe - starts the installer window. With switches it runs without a window, for an office
// that rolls Heron Tools out to many PCs, and for testing:
//
//   HeronToolsInstaller.exe                       the window
//   HeronToolsInstaller.exe /install              install into every Revit 2020-2027 found on this PC
//   HeronToolsInstaller.exe /install /years 2024,2026
//   HeronToolsInstaller.exe /uninstall            remove Heron Tools from every Revit year
//   HeronToolsInstaller.exe /status               what is installed where
//   /log <file>                                   also write what happened to a file
//   (-install and --install work too; a switch it does not know stops it with a message, never opens the window)
//
// Testing only (never needed by a modeller): --addins-root, --revit-root and --backup-root point the installer
// at test folders (and stop it watching the real Revit), and --picture <png> draws the window into a picture.
//
// Exit codes: 0 done, 1 something failed, 2 nothing to do (no Revit found / no build inside).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace HeronTools.Installer
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        private static TextWriter _log;

        [STAThread]
        private static int Main(string[] args)
        {
            // "/install", "-install" and "--install" all mean the same: some shells (Git Bash) rewrite a
            // leading "/" into a folder path, and the switch then went unseen and the WINDOW opened instead.
            var withValue = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "years", "log", "addins-root", "revit-root", "backup-root", "picture", "report" };
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "install", "uninstall", "status" };
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unknown = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string name = args[i].TrimStart('/', '-');
                if (withValue.Contains(name) && i + 1 < args.Length)
                    options[name] = args[++i];
                else if (known.Contains(name))
                    flags.Add(name);
                else
                    unknown.Add(args[i]);
            }

            // A switch it does not know never falls through to the window: it says so and stops.
            if (unknown.Count > 0)
            {
                AttachConsole(-1);
                if (options.TryGetValue("log", out string badLog))
                    _log = new StreamWriter(badLog, false) { AutoFlush = true };
                Say("Not understood: " + string.Join(" ", unknown) +
                    ". Use /install, /uninstall or /status (with /years 2024,2026 and /log <file>), or no switch for the window.");
                _log?.Dispose();
                return 1;
            }

            Payload payload;
            try
            {
                payload = Payload.Load();
            }
            catch (Exception ex)
            {
                return Fatal("The Heron Tools build inside this installer is damaged: " + ex.Message, flags.Count > 0);
            }

            var paths = new MachinePaths();
            if (options.TryGetValue("addins-root", out string addins)) { paths.AddinsRoot = addins; paths.WatchRunningRevit = false; }
            if (options.TryGetValue("revit-root", out string revit)) { paths.RevitRoot = revit; paths.WatchRunningRevit = false; }
            if (options.TryGetValue("backup-root", out string backup)) { paths.BackupRoot = backup; }
            var engine = new InstallEngine(payload, paths);

            if (options.TryGetValue("picture", out string picture))
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                options.TryGetValue("report", out string report);
                new InstallerWindow(payload, engine).SaveAsPicture(picture, report);
                app.Shutdown();
                return 0;
            }

            bool install = flags.Contains("install");
            bool uninstall = flags.Contains("uninstall");
            bool status = flags.Contains("status");
            if (!install && !uninstall && !status)
            {
                var app = new Application();
                return app.Run(new InstallerWindow(payload, engine));
            }

            // ---- no window
            AttachConsole(-1);   // write into the command window that started us, if there is one
            if (options.TryGetValue("log", out string logPath))
                _log = new StreamWriter(logPath, false) { AutoFlush = true };
            try
            {
                return RunQuiet(engine, payload, install, uninstall, options.TryGetValue("years", out string years) ? years : null);
            }
            finally
            {
                _log?.Dispose();
            }
        }

        private static int RunQuiet(InstallEngine engine, Payload payload, bool install, bool uninstall, string yearList)
        {
            Say("Heron Tools Installer " + (payload?.ShortVersion ?? "(no build inside)"));
            List<YearState> states = engine.ReadYears();

            if (!install && !uninstall)
            {
                foreach (YearState s in states)
                    Say($"Revit {s.Year}: Revit {(s.RevitInstalled ? "found" : "not found")}{(s.RevitOpen ? ", open" : "")}, " +
                        $"Heron Tools {(s.Installed != null ? InstallEngine.Short(s.Installed) : "not installed")}" +
                        (s.AllUsersCopy != null ? ", ALSO in the all-users folder: " + s.AllUsersCopy : ""));
                return 0;
            }

            if (payload == null)
            {
                Say("This installer was built without Heron Tools inside, so it has nothing to install.");
                return 2;
            }

            List<int> wanted;
            if (yearList != null)
            {
                wanted = new List<int>();
                foreach (string part in yearList.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int y) || !states.Any(s => s.Year == y))
                    {
                        Say($"'{part}' is not a Revit year this installer has a build for (2020 to 2027).");
                        return 1;
                    }
                    wanted.Add(y);
                }
            }
            else
            {
                wanted = install
                    ? states.Where(s => s.RevitInstalled).Select(s => s.Year).ToList()
                    : states.Where(s => s.Installed != null).Select(s => s.Year).ToList();
            }

            if (wanted.Count == 0)
            {
                Say(install
                    ? "No Revit 2020 to 2027 was found on this PC, so there is nothing to install into."
                    : "Heron Tools is not installed in any Revit year, so there is nothing to remove.");
                return 2;
            }

            bool failed = false;
            foreach (int year in wanted)
            {
                StepResult r = install ? engine.Install(year) : engine.Remove(year);
                Say(r.Message);
                failed |= !r.Ok;
            }
            return failed ? 1 : 0;
        }

        private static void Say(string line)
        {
            try { Console.WriteLine(line); } catch (Exception) { }
            _log?.WriteLine(line);
        }

        private static int Fatal(string message, bool quiet)
        {
            if (quiet)
            {
                AttachConsole(-1);
                Say(message);
            }
            else
            {
                MessageBox.Show(message, "Heron Tools Installer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return 1;
        }
    }
}
