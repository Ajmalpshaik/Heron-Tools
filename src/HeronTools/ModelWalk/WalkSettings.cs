// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - the modeller's own settings: mouse sensitivity, invert look, the five walk speeds, eye height,
// step-up height and "keep snag colours after the walk". Saved for this Windows user in
// %AppData%\HeronTools\ModelWalkSettings.txt. Distances are millimetres here and in the window; feet only come out
// of the *Ft properties, for the engine. A damaged or hand-edited file can never give a value outside the limits
// below - such a line keeps its standard value. No Revit API here.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HeronTools.Utils;

namespace HeronTools.Services.ModelWalk
{
    internal sealed class WalkSettings
    {
        private const string FileName = "ModelWalkSettings.txt";

        // --- Standard values and limits (mm, m/s, %) ---
        public const double StandardSensitivityPercent = 100.0;
        public const double MinSensitivityPercent = 25.0;
        public const double MaxSensitivityPercent = 250.0;

        public const double StandardEyeHeightMm = 1700.0;
        public const double MinEyeHeightMm = 1000.0;
        public const double MaxEyeHeightMm = 2000.0;

        public const double StandardStepUpMm = 350.0;
        public const double MinStepUpMm = 100.0;
        public const double MaxStepUpMm = 450.0;

        public const double MinSpeedMetresPerSec = 0.2;
        public const double MaxSpeedMetresPerSec = 20.0;
        public static readonly double[] StandardSpeedsMetresPerSec = { 0.8, 1.5, 3.5, 5.5, 8.0 };

        /// <summary>The lowest low eye (C in Walk); it is never above 1000 mm and always 300 mm under the eye.</summary>
        private const double LowEyeMm = 1000.0;
        private const double LowEyeBelowEyeMm = 300.0;

        public WalkSettings()
        {
            SpeedsMetresPerSec = (double[])StandardSpeedsMetresPerSec.Clone();
        }

        /// <summary>Mouse turn speed, in % of the standard 0.15 degrees per pixel.</summary>
        public double SensitivityPercent = StandardSensitivityPercent;

        /// <summary>Mouse forward looks down (as in some flight viewers).</summary>
        public bool InvertLook;

        /// <summary>Five walk speeds, slowest first (m/s). Fly is twice the step, Shift three times.</summary>
        public double[] SpeedsMetresPerSec;

        public double EyeHeightMm = StandardEyeHeightMm;
        public double StepUpMm = StandardStepUpMm;

        /// <summary>
        /// Off (standard): snag colours are given back when the walk ends. On: they stay in the walk view after the
        /// walk - "Clear snag colours" (X, or the pause menu) still gives them back, and so does every Save / Sync.
        /// </summary>
        public bool KeepSnagColours;

        /// <summary>Lines saying what a loaded file could not use (shown once when the walk starts).</summary>
        public List<string> LoadWarnings { get; } = new List<string>();

        // --- For the engine (feet, degrees) ---
        public double MouseDegPerPixel => WalkTuning.MouseDegPerPixel * SensitivityPercent / 100.0;
        public double EyeHeightFt => WalkTuning.MmToFt(EyeHeightMm);
        public double LowEyeHeightMm => Math.Min(LowEyeMm, EyeHeightMm - LowEyeBelowEyeMm);
        public double LowEyeHeightFt => WalkTuning.MmToFt(LowEyeHeightMm);
        public double StepUpFt => WalkTuning.MmToFt(StepUpMm);
        public int SpeedStepCount => SpeedsMetresPerSec.Length;

        public int ClampSpeedStep(int step)
        {
            if (step < 0) return 0;
            if (step >= SpeedsMetresPerSec.Length) return SpeedsMetresPerSec.Length - 1;
            return step;
        }

        public double WalkSpeedFtPerSec(int step) => WalkTuning.MmToFt(SpeedsMetresPerSec[ClampSpeedStep(step)] * 1000.0);

        public WalkSettings Copy()
        {
            var copy = (WalkSettings)MemberwiseClone();
            copy.SpeedsMetresPerSec = (double[])SpeedsMetresPerSec.Clone();
            return copy;
        }

        /// <summary>Every value back to the standard one.</summary>
        public static WalkSettings Standard() => new WalkSettings();

        // ------------------------------------------------------------------ checks (shared with the window)

        public static bool InRange(double value, double min, double max) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max;

        /// <summary>Null when the five speeds can be used, or a plain reason.</summary>
        public static string SpeedsProblem(double[] speeds)
        {
            if (speeds == null || speeds.Length != StandardSpeedsMetresPerSec.Length)
                return "Give five speeds.";
            for (int i = 0; i < speeds.Length; i++)
            {
                if (!InRange(speeds[i], MinSpeedMetresPerSec, MaxSpeedMetresPerSec))
                    return "Each speed is " + MinSpeedMetresPerSec.ToString("0.0", CultureInfo.CurrentCulture) + " to " +
                           MaxSpeedMetresPerSec.ToString("0", CultureInfo.CurrentCulture) + " m/s.";
                if (i > 0 && speeds[i] <= speeds[i - 1])
                    return "Each speed must be faster than the one before it.";
            }
            return null;
        }

        // ------------------------------------------------------------------ file

        public static WalkSettings Load()
        {
            var s = new WalkSettings();
            string file = AppDataConfigStore.GetPath(FileName);
            string[] lines;
            try
            {
                if (!File.Exists(file))
                    return s;
                lines = File.ReadAllLines(file);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                s.LoadWarnings.Add("Your Model Walk settings could not be read, so the standard settings are used.");
                return s;
            }

            bool damaged = false;
            foreach (string raw in lines)
            {
                int split = raw.IndexOf('=');
                if (split <= 0)
                    continue;
                string key = raw.Substring(0, split).Trim().ToLowerInvariant();
                string value = raw.Substring(split + 1).Trim();
                switch (key)
                {
                    case "sensitivitypercent":
                        damaged |= !ReadNumber(value, MinSensitivityPercent, MaxSensitivityPercent, ref s.SensitivityPercent);
                        break;
                    case "invertlook":
                        if (bool.TryParse(value, out bool invert)) s.InvertLook = invert; else damaged = true;
                        break;
                    case "eyeheightmm":
                        damaged |= !ReadNumber(value, MinEyeHeightMm, MaxEyeHeightMm, ref s.EyeHeightMm);
                        break;
                    case "stepupmm":
                        damaged |= !ReadNumber(value, MinStepUpMm, MaxStepUpMm, ref s.StepUpMm);
                        break;
                    case "speedsmetrespersec":
                        string[] parts = value.Split(';');
                        var speeds = new double[parts.Length];
                        bool ok = true;
                        for (int i = 0; i < parts.Length; i++)
                            ok &= double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out speeds[i]);
                        if (ok && SpeedsProblem(speeds) == null) s.SpeedsMetresPerSec = speeds; else damaged = true;
                        break;
                    case "keepsnagcolours":
                        if (bool.TryParse(value, out bool keep)) s.KeepSnagColours = keep; else damaged = true;
                        break;
                }
            }
            if (damaged)
                s.LoadWarnings.Add("Some Model Walk settings could not be used, so they keep their standard value. Pause and press S to check them.");
            return s;
        }

        private static bool ReadNumber(string text, double min, double max, ref double target)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && InRange(value, min, max))
            {
                target = value;
                return true;
            }
            return false;
        }

        /// <summary>Saves the settings. Returns null, or why they could not be saved.</summary>
        public string Save()
        {
            string file = AppDataConfigStore.GetPath(FileName);
            string temp = file + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var speeds = new List<string>();
                foreach (double v in SpeedsMetresPerSec)
                    speeds.Add(v.ToString("0.###", CultureInfo.InvariantCulture));
                var lines = new List<string>
                {
                    "SensitivityPercent=" + SensitivityPercent.ToString("0.###", CultureInfo.InvariantCulture),
                    "InvertLook=" + InvertLook,
                    "SpeedsMetresPerSec=" + string.Join(";", speeds),
                    "EyeHeightMm=" + EyeHeightMm.ToString("0.###", CultureInfo.InvariantCulture),
                    "StepUpMm=" + StepUpMm.ToString("0.###", CultureInfo.InvariantCulture),
                    "KeepSnagColours=" + KeepSnagColours
                };
                // Written beside it first, then moved over, so a crash half-way never leaves a broken file.
                File.WriteAllLines(temp, lines);
                if (File.Exists(file))
                    File.Replace(temp, file, null);
                else
                    File.Move(temp, file);
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return "The settings could not be saved (" + ex.Message + "). They still work until the walk ends.";
            }
        }
    }
}
