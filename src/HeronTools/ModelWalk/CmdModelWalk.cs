// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk (Coordination panel): walk the live model in first person from a 3D view - point at things, select
// them, mark snags, hand over a report. A toggle: a second click ends the walk.
//
// The only model change the command makes is the walk view itself ("Heron Model Walk", made once and reused,
// one Undo step). Camera moves during the walk make no Undo entries. Snag colours are Undo steps of their own and
// are given back at the end (or kept, when the modeller's setting asks - then Clear snag colours, a Save or a Sync
// gives them back). An old "AJ Game View" from AJ-Tools is left alone - never reused or renamed.

using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Services.ModelWalk;
using HeronTools.UI.ModelWalk;
using HeronTools.Utils;

namespace HeronTools.Commands.ModelWalk
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdModelWalk : IExternalCommand
    {
        internal const string ToolTitle = "Model Walk";
        internal const string ViewBaseName = "Heron Model Walk";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;

            // The toggle comes first, before any view check: a running walk can always be ended from the ribbon.
            ModelWalkHudWindow running = ModelWalkHudWindow.Active;
            if (running != null)
            {
                try
                {
                    running.StopFromRibbon(uiapp);
                }
                catch (Exception ex)
                {
                    DialogHelper.ShowError(ToolTitle, "Model Walk could not end cleanly. Close the walk view to end it.\n\nRevit said: " + ex.Message);
                }
                return Result.Succeeded;
            }

            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                DialogHelper.ShowError(ToolTitle, "Model Walk needs a project model. Open a project and a 3D view, then click Model Walk.");
                return Result.Cancelled;
            }
            if (!(uidoc.ActiveView is View3D startView) || startView.IsTemplate)
            {
                DialogHelper.ShowError(ToolTitle, "Open a 3D view first (the walk starts where that view looks), then click Model Walk.");
                return Result.Cancelled;
            }

            try
            {
                SnagColourMemory.Watch(uiapp);
                WalkSettings settings = WalkSettings.Load();
                if (!settings.KeepSnagColours)
                    GiveBackLeftovers(doc);   // kept colours stay until Clear snag colours, a save or a sync

                string viewName = WalkViewName(doc);
                View3D walkView = FindWalkView(doc, viewName);
                XYZ eye;
                double yaw, pitch;
                string firstTimeNote = null;

                if (walkView != null)
                {
                    // Carry on from where the walk view's camera is.
                    ViewOrientation3D o = walkView.GetOrientation();
                    eye = o.EyePosition;
                    ForwardToAngles(o.ForwardDirection, out yaw, out pitch);
                }
                else
                {
                    StartFromView(uidoc, startView, settings.EyeHeightFt, out eye, out yaw, out pitch);
                    if (doc.IsReadOnly)
                    {
                        DialogHelper.ShowError(ToolTitle, "This model is read-only, so Model Walk cannot make its walk view. Open an editable copy.");
                        return Result.Cancelled;
                    }
                    walkView = CreateWalkView(doc, viewName, out string madeName, out string problem);
                    if (walkView == null)
                    {
                        DialogHelper.ShowError(ToolTitle, "Model Walk could not make its walk view, so nothing was changed.\n\n" + problem);
                        return Result.Cancelled;
                    }
                    firstTimeNote = "Model Walk made the view \"" + madeName + "\"" +
                                    (doc.IsWorkshared ? " - others see it after Sync with Central." : ".");
                }

                if (walkView.IsLocked)
                {
                    DialogHelper.ShowError(ToolTitle, "The view \"" + walkView.Name + "\" is locked, so Revit does not let its camera move. " +
                                                      "Open it, unlock it (the lock on the view control bar), then click Model Walk again.");
                    return Result.Cancelled;
                }

                uidoc.ActiveView = walkView;

                if (firstTimeNote != null)
                {
                    try
                    {
                        walkView.SetOrientation(WalkEngine.Orientation(eye, yaw, pitch));
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                    {
                        DialogHelper.ShowError(ToolTitle, "Revit did not let the walk view's camera move (the view is locked). Unlock it, then click Model Walk again.");
                        return Result.Cancelled;
                    }
                }

                var session = new WalkSession(doc, walkView.Id, startView.Id, eye, yaw, pitch, WalkKeyBindings.Load(), settings);
                session.ViewpointFile = WalkViewpointStore.ModelFile(doc);
                session.Viewpoints.AddRange(WalkViewpointStore.Load(session.ViewpointFile, out int skipped, out string viewpointProblem));
                session.ViewpointsVersion++;
                if (viewpointProblem != null)
                    session.Keys.LoadWarnings.Add(viewpointProblem);
                else if (skipped > 0)
                    session.Keys.LoadWarnings.Add(skipped + (skipped == 1 ? " saved viewpoint" : " saved viewpoints") + " could not be read and " +
                                                  (skipped == 1 ? "was" : "were") + " left out.");
                var engine = new WalkEngine(session);
                ExternalEvent frameEvent = ExternalEvent.Create(engine);
                FillFirstRectangle(uidoc, session);

                var hud = new ModelWalkHudWindow(session, frameEvent, engine);
                new System.Windows.Interop.WindowInteropHelper(hud) { Owner = uiapp.MainWindowHandle };
                try
                {
                    hud.ShowWalk();
                }
                catch (Exception)
                {
                    // The overlay never came up: nothing is left running, so the next click can start again.
                    try { hud.Close(); } catch (Exception) { /* already gone */ }
                    frameEvent.Dispose();
                    throw;
                }
                if (firstTimeNote != null)
                    session.Hud.Toast(firstTimeNote);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                // Cancelled, not Failed, after our own message - Failed makes Revit add a second, generic box.
                DialogHelper.ShowError(ToolTitle, "Model Walk could not start, and nothing was changed in the walk.\n\nRevit said: " + ex.Message);
                return Result.Cancelled;
            }
        }

        /// <summary>
        /// "Heron Model Walk", or "Heron Model Walk - username" in a workshared model: one walk view per person, so
        /// one coordinator's snag colours and camera never land in a teammate's walk.
        /// </summary>
        private static string WalkViewName(Document doc)
        {
            string user = doc.Application?.Username;
            return doc.IsWorkshared && !string.IsNullOrWhiteSpace(user) ? ViewBaseName + " - " + user.Trim() : ViewBaseName;
        }

        /// <summary>The walk view: a perspective 3D view named exactly so, or "name (2)", "name (3)"... from an earlier clash of names.</summary>
        private static View3D FindWalkView(Document doc, string name)
        {
            var views = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .Where(v => !v.IsTemplate && v.IsPerspective).ToList();
            View3D exact = views.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;
            return views.Where(v => IsNumberedName(v.Name, name)).OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        private static bool IsNumberedName(string candidate, string name)
        {
            if (candidate == null || !candidate.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase) || !candidate.EndsWith(")", StringComparison.Ordinal))
                return false;
            string number = candidate.Substring(name.Length + 2, candidate.Length - name.Length - 3);
            return int.TryParse(number, out int n) && n > 1;
        }

        /// <summary>
        /// Makes the walk view in one named transaction (one Undo step): perspective, shaded, Crop View and the crop
        /// boundary off, far clipping off. Revit's first 3D view type is used, with no view template. If another view
        /// already has the name, the first free "name (2)", "name (3)"... is used and found again next time.
        /// </summary>
        private static View3D CreateWalkView(Document doc, string name, out string madeName, out string problem)
        {
            madeName = null;
            problem = null;
            ViewFamilyType type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
            if (type == null)
            {
                problem = "The model has no 3D view type.";
                return null;
            }

            var taken = new System.Collections.Generic.HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            string free = name;
            for (int n = 2; taken.Contains(free); n++)
                free = name + " (" + n + ")";

            using (var t = new Transaction(doc, "Model Walk: walk view"))
            {
                try
                {
                    t.Start();
                    View3D view = View3D.CreatePerspective(doc, type.Id);
                    view.Name = free;
                    if (view.CanModifyDisplayStyle())
                        view.DisplayStyle = DisplayStyle.Shading;
                    Parameter farClip = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR);
                    if (farClip != null && !farClip.IsReadOnly)
                        farClip.Set(0);   // far clipping would cut the model off in the distance
                    view.CropBoxActive = false;
                    view.CropBoxVisible = false;
                    if (t.Commit() != TransactionStatus.Committed)
                    {
                        problem = "Revit did not keep the new view.";
                        return null;
                    }
                    madeName = free;
                    return view;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    if (t.HasStarted() && !t.HasEnded())
                        t.RollBack();
                    problem = "Revit said: " + ex.Message;
                    return null;
                }
            }
        }

        /// <summary>Snag colours an earlier walk in this model could not give back (Revit was busy) - given back now, one Undo step.</summary>
        private static void GiveBackLeftovers(Document doc)
        {
            foreach (View3D view in new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().Where(v => v.IsPerspective))
            {
                if (SnagColourMemory.Find(doc, view)?.Count > 0)
                    SnagColourMemory.GiveBack(doc, view, "Model Walk: give back snag colours");
            }
        }

        /// <summary>Where the walk starts when the walk view is new: from the 3D view the modeller is in.</summary>
        private static void StartFromView(UIDocument uidoc, View3D view, double eyeHeightFt, out XYZ eye, out double yaw, out double pitch)
        {
            if (view.IsPerspective)
            {
                ViewOrientation3D o = view.GetOrientation();
                eye = o.EyePosition;
                ForwardToAngles(o.ForwardDirection, out yaw, out pitch);
                return;
            }

            // An orthographic 3D view: start at the middle of what is on screen, at eye height, looking the same way.
            eye = new XYZ(0, 0, eyeHeightFt);
            foreach (UIView uiView in uidoc.GetOpenUIViews())
            {
                if (uiView.ViewId != view.Id)
                    continue;
                try
                {
                    var corners = uiView.GetZoomCorners();
                    if (corners != null && corners.Count >= 2)
                    {
                        XYZ middle = (corners[0] + corners[1]) / 2.0;
                        eye = new XYZ(middle.X, middle.Y, middle.Z + eyeHeightFt);
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* keep the model origin */ }
                break;
            }
            XYZ forward = view.ViewDirection != null ? view.ViewDirection.Negate() : XYZ.BasisX;
            ForwardToAngles(forward, out yaw, out pitch);
            pitch = Math.Max(-45, Math.Min(45, pitch));
        }

        private static void ForwardToAngles(XYZ forward, out double yawDeg, out double pitchDeg)
        {
            yawDeg = 0;
            pitchDeg = 0;
            if (forward == null || forward.GetLength() < 1e-9)
                return;
            XYZ f = forward.Normalize();
            pitchDeg = Math.Asin(Math.Max(-1.0, Math.Min(1.0, f.Z))) * 180.0 / Math.PI;
            pitchDeg = Math.Max(-WalkTuning.PitchClampDeg, Math.Min(WalkTuning.PitchClampDeg, pitchDeg));
            if (Math.Abs(f.X) > 1e-9 || Math.Abs(f.Y) > 1e-9)
                yawDeg = Math.Atan2(f.Y, f.X) * 180.0 / Math.PI;
        }

        private static void FillFirstRectangle(UIDocument uidoc, WalkSession session)
        {
            foreach (UIView uiView in uidoc.GetOpenUIViews())
            {
                if (uiView.ViewId != session.WalkViewId)
                    continue;
                try
                {
                    Rectangle rect = uiView.GetWindowRectangle();
                    session.Hud.RectLeft = rect.Left;
                    session.Hud.RectTop = rect.Top;
                    session.Hud.RectRight = rect.Right;
                    session.Hud.RectBottom = rect.Bottom;
                    session.Hud.RectValid = true;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    session.Hud.RectValid = false;
                }
                return;
            }
        }
    }

    /// <summary>
    /// Enabled in a 3D view of a project (the walk starts where it looks), and always while a walk runs - so the
    /// button can end it from anywhere.
    /// </summary>
    public class CmdModelWalkAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            try
            {
                if (ModelWalkHudWindow.Active != null)
                    return true;
                UIDocument uiDoc = applicationData?.ActiveUIDocument;
                if (uiDoc?.Document == null || uiDoc.Document.IsFamilyDocument)
                    return false;
                return uiDoc.ActiveView is View3D view && view.IsValidObject && !view.IsTemplate;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Any Revit hiccup keeps the button greyed rather than breaking the ribbon.
                return false;
            }
        }
    }
}
