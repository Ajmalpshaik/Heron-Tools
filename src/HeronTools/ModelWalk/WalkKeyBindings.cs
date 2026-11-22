// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - the keys. Every action can take another key (office keyboards differ); the map is saved for this
// Windows user in %AppData%\HeronTools\ModelWalkKeys.txt. Fixed, never remapped: Esc, Tab, the mouse, the number
// keys 1-9 and the arrow keys. No Revit API here.

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Input;
using HeronTools.Utils;

namespace HeronTools.Services.ModelWalk
{
    /// <summary>One action that can take another key.</summary>
    internal sealed class WalkKeyAction
    {
        public WalkKeyAction(string id, string display, Key defaultKey)
        {
            Id = id;
            Display = display;
            DefaultKey = defaultKey;
        }

        public string Id { get; }
        public string Display { get; }
        public Key DefaultKey { get; }
    }

    internal sealed class WalkKeyBindings
    {
        private const string FileName = "ModelWalkKeys.txt";

        public const string MoveForward = "MoveForward";
        public const string MoveBack = "MoveBack";
        public const string StrafeLeft = "StrafeLeft";
        public const string StrafeRight = "StrafeRight";
        public const string Run = "Run";
        public const string Up = "Up";
        public const string DownOrLowEye = "DownOrLowEye";
        public const string Door = "Door";
        public const string Fly = "Fly";
        public const string PassThrough = "PassThrough";
        public const string BackToStart = "BackToStart";
        public const string GoToPoint = "GoToPoint";
        public const string SaveViewpoint = "SaveViewpoint";
        public const string PlayViewpoints = "PlayViewpoints";
        public const string ShowHidden = "ShowHidden";
        public const string ClearSnags = "ClearSnags";
        public const string Snapshot = "Snapshot";
        public const string Measure = "Measure";
        public const string SpeedUp = "SpeedUp";
        public const string SpeedDown = "SpeedDown";
        public const string CleanScreen = "CleanScreen";
        public const string Help = "Help";
        public const string Pause = "Pause";
        public const string FrameTiming = "FrameTiming";
        public const string ThirdPerson = "ThirdPerson";

        public static readonly IReadOnlyList<WalkKeyAction> Actions = new List<WalkKeyAction>
        {
            new WalkKeyAction(MoveForward,    "Walk forward",                               Key.W),
            new WalkKeyAction(MoveBack,       "Walk back",                                  Key.S),
            new WalkKeyAction(StrafeLeft,     "Step left",                                  Key.A),
            new WalkKeyAction(StrafeRight,    "Step right",                                 Key.D),
            new WalkKeyAction(Run,            "Run (hold)",                                 Key.LeftShift),
            new WalkKeyAction(Up,             "Jump (Walk), up in Fly / Pass-through (hold)", Key.Space),
            new WalkKeyAction(DownOrLowEye,   "Low eye on / off (Walk), down (Fly, hold)",  Key.C),
            new WalkKeyAction(Door,           "Go through the door",                        Key.E),
            new WalkKeyAction(Fly,            "Fly on / off",                               Key.F),
            new WalkKeyAction(PassThrough,    "Pass-through on / off",                      Key.G),
            new WalkKeyAction(BackToStart,    "Back to start",                              Key.R),
            new WalkKeyAction(GoToPoint,      "Go to point (hold, aim, release)",           Key.T),
            new WalkKeyAction(SaveViewpoint,  "Save viewpoint",                             Key.B),
            new WalkKeyAction(PlayViewpoints, "Play viewpoints",                            Key.O),
            new WalkKeyAction(ShowHidden,     "Show what this walk hid",                    Key.U),
            new WalkKeyAction(ClearSnags,     "Clear my snag colours",                      Key.J),
            new WalkKeyAction(Snapshot,       "Snapshot (picture of the view)",             Key.K),
            new WalkKeyAction(Measure,        "Measure (point 1, then point 2)",            Key.M),
            new WalkKeyAction(SpeedUp,        "Faster",                                     Key.OemPlus),
            new WalkKeyAction(SpeedDown,      "Slower",                                     Key.OemMinus),
            new WalkKeyAction(CleanScreen,    "Clean screen (crosshair only)",              Key.Z),
            new WalkKeyAction(Help,           "Help card on / off",                         Key.H),
            new WalkKeyAction(Pause,          "Pause",                                      Key.P),
            new WalkKeyAction(FrameTiming,    "Frame timing on / off",                      Key.F12),
            new WalkKeyAction(ThirdPerson,    "First / third person (show a character)",    Key.Q),
        };

        /// <summary>Keys no action may take: they are fixed, or Windows owns them.</summary>
        public static readonly HashSet<Key> ReservedKeys = new HashSet<Key>
        {
            Key.None, Key.Escape, Key.Enter, Key.Tab, Key.System,
            Key.Left, Key.Right, Key.Up, Key.Down,
            Key.D0, Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9,
            Key.NumPad0, Key.NumPad1, Key.NumPad2, Key.NumPad3, Key.NumPad4,
            Key.NumPad5, Key.NumPad6, Key.NumPad7, Key.NumPad8, Key.NumPad9,
            Key.LWin, Key.RWin, Key.Apps, Key.ImeProcessed, Key.DeadCharProcessed
        };

        private readonly Dictionary<string, Key> _keyByAction = new Dictionary<string, Key>();
        private readonly Dictionary<Key, string> _actionByKey = new Dictionary<Key, string>();

        private WalkKeyBindings() { }

        /// <summary>Lines saying what a loaded file could not use (shown once when the walk starts).</summary>
        public List<string> LoadWarnings { get; } = new List<string>();

        /// <summary>
        /// Left and right Shift / Ctrl / Alt are one key to the modeller: either one works for an action bound to
        /// the other (the old tool only listened to Left Shift).
        /// </summary>
        public static Key Normalise(Key key)
        {
            switch (key)
            {
                case Key.RightShift: return Key.LeftShift;
                case Key.RightCtrl: return Key.LeftCtrl;
                case Key.RightAlt: return Key.LeftAlt;
                case Key.Add: return Key.OemPlus;
                case Key.Subtract: return Key.OemMinus;
                default: return key;
            }
        }

        public Key Get(string actionId)
        {
            return _keyByAction.TryGetValue(actionId, out Key key) ? key : Key.None;
        }

        /// <summary>The action bound to this key (either Shift, Ctrl or Alt), or null.</summary>
        public string FindAction(Key key)
        {
            return _actionByKey.TryGetValue(Normalise(key), out string action) ? action : null;
        }

        public static string DisplayOf(string actionId)
        {
            foreach (WalkKeyAction action in Actions)
                if (action.Id == actionId)
                    return action.Display;
            return actionId;
        }

        /// <summary>The key bound to an action, as the HUD writes it ("E", "Space", "Shift", "+").</summary>
        public string Label(string actionId) => LabelOf(Get(actionId));

        public static string LabelOf(Key key)
        {
            switch (key)
            {
                case Key.None: return "(no key)";
                case Key.LeftShift:
                case Key.RightShift: return "Shift";
                case Key.LeftCtrl:
                case Key.RightCtrl: return "Ctrl";
                case Key.LeftAlt:
                case Key.RightAlt: return "Alt";
                case Key.OemPlus:
                case Key.Add: return "+";
                case Key.OemMinus:
                case Key.Subtract: return "-";
                case Key.OemComma: return ",";
                case Key.OemPeriod: return ".";
                case Key.OemQuestion: return "/";
                case Key.OemSemicolon: return ";";
                case Key.OemQuotes: return "'";
                case Key.OemOpenBrackets: return "[";
                case Key.OemCloseBrackets: return "]";
                case Key.OemPipe: return "\\";
                case Key.OemTilde: return "`";
                case Key.Back: return "Backspace";
                case Key.Capital: return "Caps Lock";
                default: return key.ToString();
            }
        }

        /// <summary>True when a key may be given to an action.</summary>
        public static bool IsAllowed(Key key)
        {
            key = Normalise(key);
            return Enum.IsDefined(typeof(Key), key) && !ReservedKeys.Contains(key);
        }

        /// <summary>
        /// Gives <paramref name="key"/> to <paramref name="actionId"/>. If another action holds it, the two swap keys,
        /// and that action's name comes back in <paramref name="swappedWith"/>.
        /// </summary>
        public void SetSwapping(string actionId, Key key, out string swappedWith)
        {
            swappedWith = null;
            key = Normalise(key);
            Key oldKey = Get(actionId);
            string holder = FindAction(key);
            if (holder != null && holder != actionId)
            {
                swappedWith = holder;
                Assign(holder, oldKey);
            }
            Assign(actionId, key);
        }

        private void Assign(string actionId, Key key)
        {
            if (_keyByAction.TryGetValue(actionId, out Key previous) && _actionByKey.TryGetValue(previous, out string owner) && owner == actionId)
                _actionByKey.Remove(previous);
            _keyByAction[actionId] = key;
            if (key != Key.None)
                _actionByKey[key] = actionId;
        }

        /// <summary>A copy of the current map (Key Settings: Cancel puts it back).</summary>
        public Dictionary<string, Key> CopyMap() => new Dictionary<string, Key>(_keyByAction);

        public void Restore(Dictionary<string, Key> snapshot)
        {
            _keyByAction.Clear();
            _actionByKey.Clear();
            foreach (WalkKeyAction action in Actions)
                Assign(action.Id, snapshot.TryGetValue(action.Id, out Key key) ? key : Key.None);
        }

        public void ResetToDefaults()
        {
            _keyByAction.Clear();
            _actionByKey.Clear();
            foreach (WalkKeyAction action in Actions)
                Assign(action.Id, action.DefaultKey);
        }

        public static WalkKeyBindings Load()
        {
            var bindings = new WalkKeyBindings();
            bindings.ResetToDefaults();

            string file = AppDataConfigStore.GetPath(FileName);
            string[] lines;
            try
            {
                if (!File.Exists(file))
                    return bindings;
                lines = File.ReadAllLines(file);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                bindings.LoadWarnings.Add("Your saved keys could not be read, so the standard keys are used.");
                return bindings;
            }

            // Read every line first, then assign: a file with two keys swapped (forward on S, back on W) must load
            // as saved, not lose one of them to the other's default.
            var wanted = new Dictionary<string, Key>();
            foreach (string raw in lines)
            {
                int split = raw.IndexOf('=');
                if (split <= 0)
                    continue;
                string id = raw.Substring(0, split).Trim();
                string keyName = raw.Substring(split + 1).Trim();
                if (!IsKnownAction(id))
                    continue;
                // "None" is what Save writes for an action that was left without a key: nothing to read, not a damaged
                // line. It then gets its standard key if that is free, else the plain "has no key" note below.
                if (keyName == nameof(Key.None))
                    continue;
                // Enum.TryParse alone takes "999" or a name for no real key - check the key is real and allowed.
                if (!Enum.TryParse(keyName, out Key key) || int.TryParse(keyName, out _) || !IsAllowed(key))
                {
                    bindings.LoadWarnings.Add("\"" + DisplayOf(id) + "\" had a key that cannot be used (" + keyName + "), so it keeps its standard key.");
                    continue;
                }
                wanted[id] = Normalise(key);
            }

            if (wanted.Count == 0)
                return bindings;

            // Saved keys first, then every action the file did not set keeps its default - if that key is still free.
            bindings._keyByAction.Clear();
            bindings._actionByKey.Clear();
            foreach (KeyValuePair<string, Key> pair in wanted)
            {
                if (bindings._actionByKey.ContainsKey(pair.Value))
                {
                    bindings.LoadWarnings.Add("\"" + DisplayOf(pair.Key) + "\" was saved on a key another action also has, so it was left without a key.");
                    continue;
                }
                bindings.Assign(pair.Key, pair.Value);
            }
            foreach (WalkKeyAction action in Actions)
            {
                if (bindings.Get(action.Id) != Key.None)
                    continue;
                if (!bindings._actionByKey.ContainsKey(action.DefaultKey))
                    bindings.Assign(action.Id, action.DefaultKey);
                else
                    bindings.LoadWarnings.Add("\"" + action.Display + "\" has no key: its standard key " + LabelOf(action.DefaultKey) +
                                              " is used by another action. Pause (Esc) and press K (Keys) to give it one.");
            }
            return bindings;
        }

        /// <summary>Saves the map. Returns null, or why it could not be saved.</summary>
        public string Save()
        {
            string file = AppDataConfigStore.GetPath(FileName);
            string temp = file + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var lines = new List<string>();
                foreach (WalkKeyAction action in Actions)
                    lines.Add(action.Id + "=" + Get(action.Id));
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
                return "The keys could not be saved (" + ex.Message + "). They still work until the walk ends.";
            }
        }

        private static bool IsKnownAction(string actionId)
        {
            foreach (WalkKeyAction action in Actions)
                if (action.Id == actionId)
                    return true;
            return false;
        }
    }
}
