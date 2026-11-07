// Heron Tools - the add-in's start-up class. Builds its panel on the shared "Heron" ribbon tab.

using System;
using System.IO;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using HeronTools.Commands;

namespace HeronTools.App
{
    /// <summary>
    /// Revit calls this once when it starts. It adds the Heron Tools panel to the "Heron" tab.
    /// </summary>
    /// <remarks>
    /// WHY THE TAB IS JOINED, NOT ASSUMED
    /// The "Heron" tab is shared with Heron AI. Revit chooses which add-in loads first and never says,
    /// so either one may be the first to arrive. CreateRibbonTab throws when the tab already exists:
    ///   - loaded first (or alone): the tab is created here;
    ///   - loaded second: the "already exists" exception is caught and this panel joins that tab.
    /// Only that one exception is caught, by its full name. A wider catch would hide a real failure
    /// and leave a silently missing ribbon.
    /// </remarks>
    public sealed class HeronToolsApp : IExternalApplication
    {
        /// <summary>Exact spelling and case. Never change it: a one-letter difference makes a second tab.</summary>
        internal const string TabName = "Heron";

        /// <summary>This product's own panel. Heron AI uses other panel names on the same tab.</summary>
        internal const string PanelName = "View";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                BuildRibbon(application);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                // Never let an exception escape OnStartup. Tell the modeller in plain words instead.
                TaskDialog.Show(
                    "Heron Tools",
                    "Heron Tools could not add its buttons to the Heron tab, so its tools are not available " +
                    "in this Revit session.\n\nRestart Revit. If it happens again, reinstall Heron Tools.\n\n" +
                    "Detail for support: " + ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static void BuildRibbon(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TabName); }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { /* tab exists */ }
            var panel = app.CreateRibbonPanel(TabName, PanelName);

            string assemblyPath = typeof(HeronToolsApp).Assembly.Location;

            var filterPro = new PushButtonData(
                "HeronTools_FilterPro",
                "Filter\nPro",
                assemblyPath,
                typeof(CmdFilterPro).FullName)
            {
                ToolTip = "Create view filters quickly from a category, a parameter and its values, then apply them to the active view.",
                LongDescription =
                    "Pick one or more categories, a parameter and the values you want. Filter Pro makes one view " +
                    "filter per value, names them for you, gives each a colour and adds them to the active view " +
                    "(or to other views you pick). Each run is a single Undo step.\n\n" +
                    "Works in plan, ceiling, area, section, elevation and 3D views that are not controlled by a view template.",
                AvailabilityClassName = typeof(CmdFilterProAvailability).FullName,
                Image = LoadIcon("FilterPro16.png"),
                LargeImage = LoadIcon("FilterPro32.png"),
            };

            panel.AddItem(filterPro);
        }

        /// <summary>
        /// Loads a ribbon icon built into this DLL (Resources\Icons, embedded as "HeronTools.Icons.&lt;file&gt;").
        /// Read as a stream, so it does not depend on a pack:// address being registered in Revit.
        /// A missing icon gives a button without a picture, never a failed start-up.
        /// </summary>
        private static BitmapImage LoadIcon(string fileName)
        {
            using (Stream stream = typeof(HeronToolsApp).Assembly
                       .GetManifestResourceStream("HeronTools.Icons." + fileName))
            {
                if (stream == null)
                    return null;

                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
    }
}
