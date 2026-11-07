// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The About Heron button. Opens the About window; reads nothing from the model and changes nothing, so it
// works with no model open too.

using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HeronTools.UI;
using HeronTools.Utils;

namespace HeronTools.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdAboutHeron : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var window = new AboutHeronWindow(commandData.Application.Application.VersionNumber);
                new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("About Heron", "The About window could not open. Revit said: " + ex.Message);
                return Result.Cancelled;
            }
        }
    }

    /// <summary>Always enabled - also with no model open (About and Quick Menu settings need no model).</summary>
    public class CmdAlwaysAvailable : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
    }
}
