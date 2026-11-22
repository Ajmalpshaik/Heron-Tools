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
using System.Collections.Generic;
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

            string madeViewName = null;   // the walk view, if this click made it (it stays even if the start fails)
            try
            {
                SnagColourMemory.Watch(uiapp);
                WalkEngine.WatchModelClosing(uiapp);   // the walk ends the moment its model starts to close (once)
                WalkCharacterServer.Prepare(uiapp);   // the third-person character's switch-off event and model-close watch (once)
                WalkSettings settings = WalkSettings.Load();
                string leftoverProblem = settings.KeepSnagColours
                    ? null                       // kept colours stay until Clear snag colours, a save or a sync
                    : GiveBackLeftovers(doc);

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
                    // The last walk ended in third person while Revit was busy: start from the player, not the camera.
                    if (WalkEngine.TakeLeftThirdPersonCamera(doc, walkView.Id, eye, out XYZ playerEye, out double playerYaw, out double playerPitch))
                    {
                        eye = playerEye;
                        yaw = playerYaw;
                        pitch = playerPitch;
                    }
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
                    madeViewName = madeName;
                    firstTimeNote = "Model Walk made the view \"" + madeName + "\"" +
                                    (doc.IsWorkshared ? " - others see it after Sync with Central." : ".");
                }

                if (walkView.IsLocked)
                {
                    DialogHelper.ShowError(ToolTitle, "The view \"" + walkView.Name + "\" is locked, so Revit does not let its camera move. " +
                                                      "Open it, unlock it (the lock on the view control bar), then click Model Walk again.");
                    return Result.Cancelled;
                }

                // A section box the start is outside of cuts the building away and hides the character; far clipping
                // cuts the model off in the distance. Only the walk view is touched - never the modeller's own 3D view.
                string viewNote = TidyWalkView(doc, walkView, eye);

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
                if (leftoverProblem != null)
                    session.Keys.LoadWarnings.Add(leftoverProblem);
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
                    hud.AbortOpen();
                    try { frameEvent.Dispose(); } catch (Exception) { /* the overlay's clean-up may have done it */ }
                    throw;
                }
                if (firstTimeNote != null || viewNote != null)
                    session.Hud.Toast(firstTimeNote != null && viewNote != null ? firstTimeNote + " " + viewNote : firstTimeNote ?? viewNote);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                // Cancelled, not Failed, after our own message - Failed makes Revit add a second, generic box.
                // Not "nothing was changed": the walk view may have been made (and committed) before the failure.
                DialogHelper.ShowError(ToolTitle, "Model Walk could not start." +
                    (madeViewName != null ? " It had already made the view \"" + madeViewName + "\", which stays in the project for the next walk." : "") +
                    "\n\nRevit said: " + ex.Message);
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
            if (!doc.IsWorkshared || string.IsNullOrWhiteSpace(user))
                return ViewBaseName;
            // A Revit user name can hold a character Revit refuses in a view name (DOMAIN\name, a colon): the view
            // could then never be made. Those characters become "-" (a name that had one was never made before).
            var clean = new System.Text.StringBuilder();
            foreach (char c in user.Trim())
                clean.Append("\\:{}[]|;<>?`~".IndexOf(c) >= 0 ? '-' : c);
            return ViewBaseName + " - " + clean;
        }

        /// <summary>The walk view: a perspective 3D view named exactly so, or "name (2)", "name (3)"... from an earlier clash of names.</summary>
        private static View3D FindWalkView(Document doc, string name)
        {
            var views = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .Where(v => !v.IsTemplate && v.IsPerspective).ToList();
            View3D exact = views.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;
            // By number, not by text: as text "(10)" comes before "(2)", and the walk would jump to another view.
            return views.Where(v => NameNumber(v.Name, name) > 1).OrderBy(v => NameNumber(v.Name, name)).FirstOrDefault();
        }

        /// <summary>N for "name (N)", else 0.</summary>
        private static int NameNumber(string candidate, string name)
        {
            if (candidate == null || !candidate.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase) || !candidate.EndsWith(")", StringComparison.Ordinal))
                return 0;
            string number = candidate.Substring(name.Length + 2, candidate.Length - name.Length - 3);
            return int.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : 0;
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

        /// <summary>
        /// At every start, on the walk view only, in one Undo step "Model Walk: walk view settings" (none when nothing
        /// needs changing):
        /// - a view template is taken off the walk view (it would lock Visibility/Graphics and far clipping);
        /// - every annotation category is hidden (levels, grids, section and elevation marks, tags, dimensions, text...),
        ///   plus scope boxes and reference planes - a walk is about the model. Linked models' annotation follows the
        ///   walk view when the link is "By host view" (Revit's default);
        /// - the section box is switched off when the start eye is OUTSIDE it (standing outside the box the building is
        ///   cut away and the character is clipped - a box you are inside of is kept, it is how you walk through what it
        ///   cuts);
        /// - far clipping is switched off.
        /// Turn any of these on again while paused if you want them for that walk. The modeller's own views never change.
        /// </summary>
        /// <returns>Null, or one plain line saying what was changed (or why it could not be).</returns>
        private static string TidyWalkView(Document doc, View3D view, XYZ eye)
        {
            bool templateOff, boxOff = false, farOff;
            Parameter farClip;
            var hide = new List<ElementId>();
            try
            {
                templateOff = ElementIdHelper.IsValid(view.ViewTemplateId);
                if (view.IsSectionBoxActive)
                    boxOff = !WalkTuning.InsideBox(view.GetSectionBox(), eye);
                farClip = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR);
                farOff = farClip != null && farClip.StorageType == StorageType.Integer && farClip.AsInteger() != 0;
                foreach (Category category in doc.Settings.Categories)
                {
                    if (category == null || !IsAnnotationForWalk(category))
                        continue;
                    // Read while a template may still be on: a template-locked category is changed once it is off.
                    if (templateOff || (view.CanCategoryBeHidden(category.Id) && !view.GetCategoryHidden(category.Id)))
                        hide.Add(category.Id);
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;   // reading the settings failed: leave the view as it is
            }
            if (!templateOff && !boxOff && !farOff && hide.Count == 0)
                return null;
            if (doc.IsReadOnly)
                return "The model is read-only, so the walk view keeps its settings (annotation, section box, far clipping).";

            int hidden = 0;
            using (var t = new Transaction(doc, "Model Walk: walk view settings"))
            {
                try
                {
                    t.Start();
                    if (templateOff)
                        view.ViewTemplateId = ElementId.InvalidElementId;
                    if (boxOff)
                        view.IsSectionBoxActive = false;
                    if (farOff && farClip != null && !farClip.IsReadOnly)
                        farClip.Set(0);
                    foreach (ElementId id in hide)
                    {
                        if (view.CanCategoryBeHidden(id) && !view.GetCategoryHidden(id))
                        {
                            view.SetCategoryHidden(id, true);
                            hidden++;
                        }
                    }
                    if (t.Commit() != TransactionStatus.Committed)
                        return "Revit did not let Model Walk tidy the walk view (annotation, section box, far clipping), so it is as it was.";
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    if (t.HasStarted() && !t.HasEnded())
                        t.RollBack();
                    return "The walk view keeps its settings (Revit said: " + ex.Message + ").";
                }
            }

            var said = new List<string>();
            if (templateOff) said.Add("its view template was taken off");
            if (hidden > 0) said.Add("annotation (levels, grids, tags...) hidden");
            if (boxOff) said.Add("its section box switched off - you started outside it");
            if (farOff) said.Add("far clipping switched off");
            if (said.Count == 0)
                return null;
            return "Walk view: " + string.Join(", ", said) + " (one Undo step, this view only).";
        }

        /// <summary>Annotation categories, plus scope boxes and reference planes (shown in 3D, of no use in a walk).</summary>
        private static bool IsAnnotationForWalk(Category category)
        {
            if (category.CategoryType == CategoryType.Annotation)
                return true;
            return ElementIdHelper.IsCategory(category.Id, BuiltInCategory.OST_VolumeOfInterest) ||
                   ElementIdHelper.IsCategory(category.Id, BuiltInCategory.OST_CLines);
        }

        /// <summary>Snag colours an earlier walk in this model could not give back (Revit was busy) - given back now, one Undo step.</summary>
        /// <returns>Null, or why some colours are still on (shown when the walk starts - never silent).</returns>
        private static string GiveBackLeftovers(Document doc)
        {
            var problems = new List<string>();
            foreach (View3D view in new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().Where(v => v.IsPerspective))
            {
                if (!(SnagColourMemory.Find(doc, view)?.Count > 0))
                    continue;
                string line = SnagColourMemory.GiveBack(doc, view, "Model Walk: give back snag colours");
                // GiveBack also returns a line when it worked ("3 elements have their own colours back"): only a memory
                // still holding colours means it did not.
                if (SnagColourMemory.Find(doc, view)?.Count > 0 && !string.IsNullOrEmpty(line))
                    problems.Add("Snag colours left from an earlier walk in \"" + view.Name + "\": " + line);
            }
            return problems.Count > 0 ? string.Join(" ", problems) : null;
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
