// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Searches for an element by its Element ID in the host model or any loaded linked model and zooms the active view to it.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.UI;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdLinkedElementSearch : IExternalCommand
    {
        private const string Title = "Linked Element Search";

        /// <summary>
        /// Opens the Linked Element Search dialog to locate elements by ID in host or linked documents.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uiDoc = uiApp.ActiveUIDocument;

                if (uiDoc == null)
                {
                    DialogHelper.ShowError(Title, "Please open a project before running this tool.");
                    return Result.Cancelled;
                }

                Document doc = uiDoc.Document;
                View activeView = doc.ActiveView;

                if (activeView == null || activeView.IsTemplate)
                {
                    DialogHelper.ShowError(Title, "Run this command in a project view (not a template).");
                    return Result.Cancelled;
                }

                if (activeView.ViewType == ViewType.DrawingSheet ||
                    activeView.ViewType == ViewType.DraftingView)
                {
                    DialogHelper.ShowError(Title, "Linked element search is not available on sheets or drafting views.");
                    return Result.Cancelled;
                }

                IList<RevitLinkInstance> linkInstances = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .Where(l => l != null && l.GetLinkDocument() != null)
                    .ToList();

                var searchWindow = new LinkedSearchWindow(uiDoc, doc, activeView, linkInstances);
                new WindowInteropHelper(searchWindow)
                {
                    Owner = commandData.Application.MainWindowHandle
                };

                searchWindow.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                // This tool only reads the model, so nothing was changed. Cancelled: our box is the message.
                DialogHelper.ShowError(Title,
                    "Linked Element Search stopped because of an unexpected error. Nothing in the model was changed.\n\nRevit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }
}
