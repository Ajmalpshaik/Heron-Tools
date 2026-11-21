// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - the shared state of one walk: where the eye is, how it moves, what the HUD asked for (input)
// and what the engine tells the HUD back. Every distance the modeller feels is set in mm, once, in WalkTuning.
// Nothing here touches the model. Everything runs on Revit's UI thread (WPF timer + ExternalEvent), so plain
// fields need no lock.

using System.Collections.Generic;
using Autodesk.Revit.DB;
using HeronTools.Utils;

namespace HeronTools.Services.ModelWalk
{
    /// <summary>How the eye moves through the model.</summary>
    internal enum WalkMoveMode
    {
        /// <summary>On the floor: gravity, stairs and collision.</summary>
        Walk,

        /// <summary>Free flight: no gravity, but walls and slabs still block.</summary>
        Fly,

        /// <summary>No gravity and nothing blocks - to get past a locked room or a clash.</summary>
        PassThrough
    }

    /// <summary>What a left click does. Changed with right-click or Tab (Shift+Tab back).</summary>
    internal enum WalkPointerMode
    {
        /// <summary>Live readout of what is under the crosshair and how far it is. Changes nothing.</summary>
        Inspect = 0,

        /// <summary>Click adds the element to Revit's selection (click again removes it). Kept after the walk.</summary>
        Select = 1,

        /// <summary>Click colours the element and adds it to the snag report. Its own colours come back on exit.</summary>
        Snag = 2,

        /// <summary>Click hides the element for now (Temporary Hide). Show hidden brings back only what this walk hid.</summary>
        Hide = 3
    }

    /// <summary>
    /// All tuning in one place, in mm (or mm per second), exposed in Revit's internal feet so no other file
    /// repeats a conversion. mm to feet is exactly 1 / 304.8.
    /// </summary>
    internal static class WalkTuning
    {
        private const double FtPerMm = Constants.MM_TO_FEET;

        // --- Body. Eye height, low eye and step-up height are the modeller's own (WalkSettings, standard 1700 /
        //     1000 / 350 mm). ---
        public const double BodyRadiusMm = 350.0;     // how close the eye gets to a face

        // --- Speeds: five steps (+ / - or the mouse wheel), set in WalkSettings. Fly is twice the step. ---
        public const int DefaultSpeedStep = 2;        // the middle step (3.5 m/s standard) - the speed the old tool walked at
        public const double EaseSeconds = 0.12;       // moves speed up and slow down over about a third of a second
        public const double StoppedBelowMmPerSec = 20.0;  // an easing-out move slower than this has stopped
        public const double PlayMinSpeedMm = 300.0;   // Play viewpoints never crawls slower than 300 mm/s into a viewpoint
        public const double PlayArriveMm = 5.0;       // ...and is there within 5 mm

        // --- Rooms (location line): looked up at this height above the floor, again after this much movement ---
        public const double RoomProbeAboveFloorMm = 300.0;
        public const double RoomRecheckMoveMm = 300.0;
        public const double FlyFactor = 2.0;
        public const double RunFactor = 3.0;          // Shift held
        public const double GravityMmPerSec2 = 9810.0;
        public const double MaxFallMmPerSec = 15000.0;

        // --- Reach ---
        public const double DoorReachMm = 1600.0;     // how near a door must be for E
        public const double DoorPassExtraMm = 900.0;  // how far beyond the door face the eye lands
        public const double FloorSearchMm = 500000.0; // give up looking for a floor 500 m below
        public const double AimRangeMm = 300000.0;    // Inspect / Snag / Select / Hide reach (300 m)

        // --- Collision box: host elements are tested only inside this box around the eye (re-centred as you move) ---
        public const double NearBoxHalfMm = 15000.0;  // 15 m each way across
        public const double NearBoxBelowMm = 30000.0; // 30 m down (tall atria, stairs)
        public const double NearBoxAboveMm = 10000.0;
        public const double NearBoxRecentreMm = 5000.0;

        // --- Look ---
        public const double MouseDegPerPixel = 0.15;  // at 100 % sensitivity
        public const double PitchClampDeg = 85.0;

        // --- Frame pacing ---
        public const int FrameIntervalMs = 33;        // HUD timer (about 30 frames a second)
        public const int IdleFrameIntervalMs = 250;   // standing still: only four frames a second (readout, window checks)
        public const double InspectRefreshSec = 0.1;  // the readout ray at most 10 times a second
        public const double ClearHeightRefreshSec = 0.25;
        public const double WorldRefreshSec = 4.0;    // pick up VG / filter / level changes
        public const int RayBudgetPerFrame = 24;      // more than this in one frame and the move is refused, not guessed

        // Third person (from AJ-Tools Game Mode v1.60.0): the camera behind and right of the player's head.
        public const double ThirdPersonBackMm = 3000.0;     // camera distance behind the player
        public const double ThirdPersonPivotMm = 1900.0;    // the point the camera swings round, above the feet
        public const double ThirdPersonShoulderMm = 500.0;  // camera shifted right, so the crosshair is not on the figure
        public const double ThirdPersonWallGapMm = 250.0;   // gap kept to a wall behind the player
        public const double ThirdPersonMinBackMm = 300.0;   // the closest the camera comes
        public const double FigureStrideMm = 900.0;         // one step of the figure's walk

        public static double BodyRadiusFt => BodyRadiusMm * FtPerMm;
        public static double GravityFtPerSec2 => GravityMmPerSec2 * FtPerMm;
        public static double MaxFallFtPerSec => MaxFallMmPerSec * FtPerMm;
        public static double DoorReachFt => DoorReachMm * FtPerMm;
        public static double DoorPassExtraFt => DoorPassExtraMm * FtPerMm;
        public static double FloorSearchFt => FloorSearchMm * FtPerMm;
        public static double AimRangeFt => AimRangeMm * FtPerMm;
        public static double NearBoxHalfFt => NearBoxHalfMm * FtPerMm;
        public static double NearBoxBelowFt => NearBoxBelowMm * FtPerMm;
        public static double NearBoxAboveFt => NearBoxAboveMm * FtPerMm;
        public static double NearBoxRecentreFt => NearBoxRecentreMm * FtPerMm;

        public static double ThirdPersonBackFt => ThirdPersonBackMm * FtPerMm;
        public static double ThirdPersonPivotFt => ThirdPersonPivotMm * FtPerMm;
        public static double ThirdPersonShoulderFt => ThirdPersonShoulderMm * FtPerMm;
        public static double ThirdPersonWallGapFt => ThirdPersonWallGapMm * FtPerMm;
        public static double ThirdPersonMinBackFt => ThirdPersonMinBackMm * FtPerMm;
        public static double FigureStrideFt => FigureStrideMm * FtPerMm;

        public static double MmToFt(double mm) => mm * FtPerMm;
        public static double FtToMm(double ft) => ft * Constants.FEET_TO_MM;
    }

    /// <summary>Written by the HUD (keys, mouse); read - and for one-shot actions cleared - by the engine.</summary>
    internal sealed class WalkInput
    {
        // Held keys
        public bool MoveForward;
        public bool MoveBack;
        public bool StrafeLeft;
        public bool StrafeRight;
        public bool UpKey;      // Space held: up in Fly / Pass-through
        public bool DownKey;    // C held: down in Fly / Pass-through
        public bool Run;        // Shift held

        // Look - owned by the HUD (mouse), applied by the engine
        public double YawDeg;
        public double PitchDeg;

        public WalkPointerMode Pointer = WalkPointerMode.Inspect;

        // Third person: the camera behind the player and a figure drawn where the player stands. Owned by the HUD
        // (its key, the setting), read by the engine every frame.
        public bool ThirdPerson;
        public int SpeedStep = WalkTuning.DefaultSpeedStep;

        // One-shot actions: set by the HUD, cleared by the engine when handled
        public bool DoorQueued;
        public bool BackToStartQueued;
        public bool FlyToggleQueued;
        public bool PassThroughToggleQueued;
        public bool LowEyeToggleQueued;
        public bool GoToPointAimHold;      // held: the landing ring shows
        public bool GoToPointQueued;       // released: go
        public bool SaveViewpointQueued;
        public int JumpViewpoint;          // 1..9, 0 = none
        public bool ClickQueued;           // a left click in Select / Snag / Hide
        public bool MeasureQueued;         // M: the next measure point
        public bool ShowHiddenQueued;
        public bool ClearSnagsQueued;
        public bool PlayViewpointsQueued;
        public bool ResyncViewQueued;      // re-fit the picture to the window (aim and picture in step)
        public bool SettingsChangedQueued; // eye height or step-up changed: find the floor again
        public int OpenSnag;               // pause menu: go back to snag N and select its element, 0 = none
        public int SaveViewpointAsView;    // pause menu: save viewpoint N as a Revit 3D view, 0 = none

        /// <summary>True while any held key asks for movement.</summary>
        public bool AnyMoveHeld => MoveForward || MoveBack || StrafeLeft || StrafeRight || UpKey || DownKey;

        /// <summary>True while any one-shot is waiting for the engine.</summary>
        public bool AnyQueued =>
            DoorQueued || BackToStartQueued || FlyToggleQueued || PassThroughToggleQueued || LowEyeToggleQueued ||
            GoToPointQueued || SaveViewpointQueued || JumpViewpoint > 0 || ClickQueued || MeasureQueued || ShowHiddenQueued ||
            ClearSnagsQueued || PlayViewpointsQueued || ResyncViewQueued || SettingsChangedQueued || OpenSnag > 0 || SaveViewpointAsView > 0;
    }

    /// <summary>What the engine tells the HUD after each frame.</summary>
    internal sealed class WalkHudState
    {
        public string LocationLine = "";   // "Level 03"
        public string StatusLine = "";     // "Facing NE (project)  -  Walk  -  Speed 3/5"
        public bool DoorHint;
        public bool Hovering;              // no floor anywhere below

        // Inspect readout
        public bool AimHasHit;
        public double AimDistanceMm;
        public string AimLine1 = "";       // category - family : type - ID
        public string AimLine2 = "";       // level - system - link

        // Measure: two points from the crosshair, text only (nothing is drawn on the model)
        public bool MeasureShown;
        public string MeasureLine1 = "";   // "Point 1 set" / "2 450 mm"
        public string MeasureLine2 = "";   // live distance / horizontal and vertical parts
        public long MeasureDoneStampMs;    // a finished measure fades after 20 s; 0 while point 2 is awaited

        // Go to point
        public bool GoToPointValid;
        public double GoToPointDistanceMm;

        public int SnagCount;
        public int HiddenCount;

        // Engine to HUD: a snag was just recorded - take its picture (HUD hidden), then ask for its comment.
        public int SnagAwaitingPicture;

        // HUD to engine: a snag's picture is being taken right now - the third-person figure stays out of it.
        public bool SnagPictureBusy;

        // Engine to HUD: a viewpoint was just saved - ask for its name.
        public int ViewpointAwaitingName;

        // While paused: the engine's answer to a pause-menu action, and a counter the HUD watches.
        public string PauseStatus = "";
        public int PauseStatusVersion;

        public void SayInPause(string text)
        {
            PauseStatus = text ?? "";
            PauseStatusVersion++;
        }

        // The room the eye is in ("Room 3.12 Plant Room"), empty when none; filled by the engine a few times a second.
        public string RoomText = "";
        public bool PlayingViewpoints;

        // Engine to HUD: take this look direction on the next tick (viewpoints, Play viewpoints).
        public bool LookOverridePending;
        public double LookOverrideYawDeg;
        public double LookOverridePitchDeg;

        /// <summary>True while the engine itself needs frames (falling, playing viewpoints) with no key held.</summary>
        public bool NeedsFrames;

        // Short message for ~2.6 s. Kind picks the colour: info (Mist), done (Success), refused (Error).
        public string ToastText = "";
        public long ToastStampMs;
        public WalkToastKind ToastKind;

        public void Toast(string text, WalkToastKind kind = WalkToastKind.Info)
        {
            ToastText = text;
            ToastKind = kind;
            ToastStampMs = System.DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        public double ClearHeightMm;

        // The Revit view's window rectangle in device pixels (can be negative on a second monitor).
        public bool RectValid;
        public int RectLeft;
        public int RectTop;
        public int RectRight;
        public int RectBottom;

        // Frame budget, measured every frame (shown with F12).
        public double LastFrameMs;
        public double AverageFrameMs;
        public int LastFrameRays;

        public bool Alive = true;
        public string StopReason = "";
    }

    internal enum WalkToastKind { Info, Done, Refused }

    /// <summary>One saved eye position and look direction.</summary>
    internal sealed class WalkViewpoint
    {
        public int Number;
        public string Name;
        public XYZ Eye;
        public double YawDeg;
        public double PitchDeg;
    }

    /// <summary>One running Model Walk (there is at most one at a time).</summary>
    internal sealed class WalkSession
    {
        public WalkSession(Document doc, ElementId walkViewId, ElementId startViewId, XYZ startPosition,
                           double startYawDeg, double startPitchDeg, WalkKeyBindings keys, WalkSettings settings)
        {
            Settings = settings;
            Report = new WalkReport(doc);   // here, on Revit's thread: it reads the model's path and shared coordinates
            Document = doc;
            WalkViewId = walkViewId;
            StartViewId = startViewId;
            Position = startPosition;
            StartPosition = startPosition;
            Keys = keys;
            Input.YawDeg = startYawDeg;
            Input.PitchDeg = startPitchDeg;
            Input.ThirdPerson = settings != null && settings.ThirdPerson;
        }

        /// <summary>The model this walk belongs to - compared as a Document, never by title (two files can share a name).</summary>
        public Document Document { get; }
        public ElementId WalkViewId { get; }

        /// <summary>The view the modeller started from; Model Walk goes back to it when the walk ends.</summary>
        public ElementId StartViewId { get; }

        /// <summary>Eye position in Revit internal feet.</summary>
        public XYZ Position;
        public XYZ StartPosition;
        public double VerticalSpeedFtPerSec;
        public bool Grounded;
        public bool LowEye;
        public WalkMoveMode Mode = WalkMoveMode.Walk;

        /// <summary>False once the walk is ending; the engine then does nothing but the clean-up.</summary>
        public bool Running = true;

        /// <summary>
        /// Set by the HUD while the walk is paused: a frame then runs only the pause menu's queued actions and never
        /// ends the walk because Revit shows another view (the modeller is free to use Revit while paused).
        /// </summary>
        public bool Paused;

        /// <summary>Set by the HUD when the modeller ends the walk; the next engine frame cleans up (it needs Revit's API).</summary>
        public bool StopRequested;
        public string StopRequestReason = "";

        /// <summary>True when the walk view should become the start view again at the end (not when the modeller left it).</summary>
        public bool ReturnToStartView = true;

        public WalkInput Input { get; } = new WalkInput();
        public WalkHudState Hud { get; } = new WalkHudState();

        /// <summary>This walk's keys - one instance shared by the HUD, the Key Settings window and the engine's messages.</summary>
        public WalkKeyBindings Keys { get; }

        /// <summary>This walk's snags, their pictures and the CSV.</summary>
        public WalkReport Report { get; }

        /// <summary>The modeller's settings. Replaced as a whole when Settings is saved (the HUD's thread, the same as Revit's).</summary>
        public WalkSettings Settings { get; set; }

        public List<WalkViewpoint> Viewpoints { get; } = new List<WalkViewpoint>();
        public int ViewpointsVersion;

        /// <summary>Where this model's viewpoints are kept for this user (set by the command, on Revit's thread).</summary>
        public string ViewpointFile { get; set; }

        /// <summary>Numbers 1, 2, 3... in list order, then keeps the list. Returns null, or why it could not be kept.</summary>
        public string SaveViewpoints()
        {
            for (int i = 0; i < Viewpoints.Count; i++)
                Viewpoints[i].Number = i + 1;
            ViewpointsVersion++;
            return WalkViewpointStore.Save(ViewpointFile, Viewpoints);
        }

        public double EyeHeightFt => LowEye && Mode == WalkMoveMode.Walk ? Settings.LowEyeHeightFt : Settings.EyeHeightFt;
    }
}
