// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Toggles Revit Link category visibility in the active view.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdToggleRevitLinks : IExternalCommand
    {
        private const string ToolTitle = "Toggle Links";
        private const string TransactionName = "Toggle Links";
        private static readonly ElementId RevitLinksCategoryId = new ElementId(BuiltInCategory.OST_RvtLinks);

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application?.ActiveUIDocument;
                if (!ValidationHelper.ValidateUIDocumentAndView(uidoc, out message))
                {
                    DialogHelper.ShowError(ToolTitle, message);
                    return Result.Cancelled;
                }

                Document doc = uidoc.Document;
                if (!ValidationHelper.ValidateEditableDocument(doc, out message))
                {
                    DialogHelper.ShowError(ToolTitle, message);
                    return Result.Cancelled;
                }

                View view = doc.ActiveView;
                if (!CanToggleRevitLinks(view, out message))
                {
                    DialogHelper.ShowError(ToolTitle, message);
                    return Result.Cancelled;
                }

                bool hideLinks = !view.GetCategoryHidden(RevitLinksCategoryId);
                using (Transaction transaction = new Transaction(doc, TransactionName))
                {
                    transaction.Start();
                    view.SetCategoryHidden(RevitLinksCategoryId, hideLinks);
                    if (transaction.Commit() != TransactionStatus.Committed)
                    {
                        DialogHelper.ShowError(ToolTitle,
                            "Revit did not keep the change, so the links were not switched. The view may be owned by another user.");
                        return Result.Cancelled;
                    }
                }

                // Done: no pop-up. Ajmal (2026-11-14): errors may pop up, a "done" note may not. Links hidden one
                // by one stay hidden, exactly as Revit's own RVT Links tick box leaves them.
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError(
                    ToolTitle,
                    "Could not toggle Revit Link visibility in the active view.\n\n"
                    + "If a view template controls Revit Links visibility, unlock that setting and try again."
                    + "\n\nWhat went wrong: " + ex.Message);
                return Result.Cancelled;
            }
        }

        private static bool CanToggleRevitLinks(View view, out string reason)
        {
            reason = string.Empty;

            if (view == null || !view.IsValidObject)
            {
                reason = "No valid active view found.";
                return false;
            }

            try
            {
                if (!view.CanCategoryBeHidden(RevitLinksCategoryId))
                {
                    reason = "Revit Links category visibility cannot be changed in the active view.";
                    return false;
                }
            }
            catch
            {
                reason = "Revit Links category visibility check failed. Please try in a different view.";
                return false;
            }

            return true;
        }
    }
}
