// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Service that creates one isometric 3D view per user workset and isolates each workset in its own view, inside a single transaction, with a created/skipped/failed report.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Utils;

namespace HeronTools.Services.WorksetViews
{
    /// <summary>
    /// Handles creation of 3D views per user workset and workset visibility isolation.
    /// </summary>
    internal static class Workset3DViewService
    {
        private const string Title = "3D Views as per Workset";

        public static Result Execute(ExternalCommandData commandData, ref string message)
        {
            UIDocument uidoc = commandData?.Application?.ActiveUIDocument;
            if (!ValidationHelper.ValidateUIDocument(uidoc, out message))
            {
                DialogHelper.ShowError(Title, message);
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            if (!ValidationHelper.ValidateEditableDocument(doc, out message))
            {
                DialogHelper.ShowError(Title, message);
                return Result.Cancelled;
            }

            if (!doc.IsWorkshared)
            {
                DialogHelper.ShowInfo(Title, "The current model is not workshared.");
                return Result.Cancelled;
            }

            IList<Workset> userWorksets = GetUserWorksets(doc);
            if (userWorksets.Count == 0)
            {
                DialogHelper.ShowInfo(Title, "No user worksets were found in this model.");
                return Result.Cancelled;
            }

            ElementId threeDTypeId = GetThreeDimensionalViewFamilyTypeId(doc);
            if (threeDTypeId == ElementId.InvalidElementId)
            {
                DialogHelper.ShowError(Title, "This project has no 3D view type, so no 3D view can be made.");
                return Result.Cancelled;
            }

            HashSet<string> existingViewNames = CollectExistingViewNames(doc);

            int createdCount = 0;
            int skippedCount = 0;
            int failedCount = 0;
            var badNames = new List<string>();
            var templatesCleared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (Transaction t = new Transaction(doc, "Create 3D Views as per Workset"))
            {
                t.Start();

                foreach (Workset targetWorkset in userWorksets)
                {
                    if (existingViewNames.Contains(targetWorkset.Name))
                    {
                        skippedCount++;
                        continue;
                    }

                    View3D view3D = null;
                    try
                    {
                        view3D = View3D.CreateIsometric(doc, threeDTypeId);

                        // A workset name may hold characters a view name may not (":" "\" ...).
                        try
                        {
                            view3D.Name = targetWorkset.Name;
                        }
                        catch (Autodesk.Revit.Exceptions.ArgumentException)
                        {
                            DeleteCreatedView(doc, view3D);
                            badNames.Add(targetWorkset.Name);
                            continue;
                        }

                        // The 3D view type may hand every new view a template; if that template controls
                        // workset visibility, the per-view setting below would be overridden. Take it off
                        // and say so - the modeller can re-apply a template that leaves worksets alone.
                        if (view3D.ViewTemplateId != ElementId.InvalidElementId)
                        {
                            string templateName = (doc.GetElement(view3D.ViewTemplateId) as View)?.Name ?? "a view template";
                            view3D.ViewTemplateId = ElementId.InvalidElementId;
                            templatesCleared.Add(templateName);
                        }

                        SetWorksetVisibilityForView(view3D, userWorksets, targetWorkset.Id);

                        existingViewNames.Add(targetWorkset.Name);
                        createdCount++;
                    }
                    catch (Exception)
                    {
                        DeleteCreatedView(doc, view3D);
                        failedCount++;
                    }
                }

                if (createdCount == 0)
                {
                    t.RollBack();
                }
                else if (t.Commit() != TransactionStatus.Committed)
                {
                    DialogHelper.ShowError(Title, "Revit did not accept the new views, so nothing was changed. " +
                        "If the model is workshared, check that you can edit views (synchronise, then try again).");
                    return Result.Cancelled;
                }
            }

            string summary = BuildSummary(createdCount, skippedCount, failedCount, badNames, templatesCleared);
            if (createdCount == 0)
            {
                // Nothing changed: our own message is the whole story, so no second Revit failure box.
                DialogHelper.ShowInfo(Title, "No 3D view was made.\n\n" + summary);
                return Result.Cancelled;
            }

            // Done is silent - the new views are in the Project Browser (Ajmal, 2026-11-14: errors may pop up, a
            // "done" note may not). It speaks only when some views failed, a name was refused, or a template was
            // taken off a new view.
            if (failedCount > 0 || badNames.Count > 0 || templatesCleared.Count > 0)
                DialogHelper.ShowWarning(Title, summary);
            return Result.Succeeded;
        }

        private static IList<Workset> GetUserWorksets(Document doc)
        {
            return new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .OrderBy(ws => ws.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static ElementId GetThreeDimensionalViewFamilyTypeId(Document doc)
        {
            ViewFamilyType type = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

            return type != null ? type.Id : ElementId.InvalidElementId;
        }

        private static HashSet<string> CollectExistingViewNames(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v != null && !string.IsNullOrWhiteSpace(v.Name))
                .Select(v => v.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static void SetWorksetVisibilityForView(View3D view3D, IEnumerable<Workset> worksets, WorksetId targetWorksetId)
        {
            foreach (Workset workset in worksets)
            {
                WorksetVisibility visibility = workset.Id == targetWorksetId
                    ? WorksetVisibility.Visible
                    : WorksetVisibility.Hidden;

                view3D.SetWorksetVisibility(workset.Id, visibility);
            }
        }

        private static void DeleteCreatedView(Document doc, View3D view3D)
        {
            if (view3D == null)
            {
                return;
            }

            if (!view3D.IsValidObject)
            {
                return;
            }

            doc.Delete(view3D.Id);
        }

        private static string BuildSummary(int createdCount, int skippedCount, int failedCount,
                                           IList<string> badNames, ICollection<string> templatesCleared)
        {
            string text =
                $"Created: {createdCount}\n" +
                $"Skipped (a view with that name already exists): {skippedCount}\n" +
                $"Failed: {failedCount}";
            if (badNames.Count > 0)
            {
                text += $"\nNot made - the workset name is not allowed as a view name: {badNames.Count}\n  " +
                        string.Join("\n  ", badNames);
            }
            if (templatesCleared.Count > 0)
            {
                text += "\n\nThe new views came with a view template (" + string.Join(", ", templatesCleared) +
                        "). It was taken off them so each view can show only its workset.";
            }
            return text;
        }
    }
}
