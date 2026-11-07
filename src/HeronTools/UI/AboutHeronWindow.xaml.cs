// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// About Heron: shows the running Heron Tools version, the Revit and .NET it runs in, and the two Heron
// products with their GitHub pages. Reads nothing from the model and changes nothing.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using HeronTools.Utils;

namespace HeronTools.UI
{
    public partial class AboutHeronWindow : Window
    {
        private const string HeronToolsUrl = "https://github.com/Ajmalpshaik/Heron-Tools";
        private const string HeronAiUrl = "https://github.com/Ajmalpshaik/Heron-AI";

        private readonly string _details;

        public AboutHeronWindow(string revitVersion)
        {
            InitializeComponent();
            Icon = BrandImages.Load(BrandImages.AppIcon);
            hero_image.Source = BrandImages.Load("HeronGlow.png");
            WindowMotionHelper.AttachStandardEntrance(this);

            Assembly assembly = typeof(AboutHeronWindow).Assembly;
            string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                             ?? assembly.GetName().Version?.ToString() ?? "unknown";
            // "1.0.0+<full commit id>" -> "1.0.0 (abc1234)"
            int plus = version.IndexOf('+');
            string shown = plus > 0
                ? version.Substring(0, plus) + " (" + version.Substring(plus + 1, Math.Min(7, version.Length - plus - 1)) + ")"
                : version;

            product_text.Text = "Heron Tools " + shown;
            string runtime = RuntimeInformation.FrameworkDescription;
            environment_text.Text = $"Revit {revitVersion}  |  {runtime}  |  {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}";
            _details = $"Heron Tools {version}\nRevit {revitVersion}\n{runtime}\n{RuntimeInformation.OSDescription}";

            tools_link_button.Click += (s, e) => Open(HeronToolsUrl);
            ai_link_button.Click += (s, e) => Open(HeronAiUrl);
            copy_button.Click += (s, e) => CopyDetails();
            close_button.Click += (s, e) => Close();
        }

        private void Open(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                status_text.Text = "Could not open the browser: " + ex.Message;
            }
        }

        private void CopyDetails()
        {
            try
            {
                Clipboard.SetText(_details);
                status_text.Text = "Copied. Paste it into your message.";
            }
            catch (Exception ex)
            {
                // The clipboard can be busy (another program holding it); say so instead of failing.
                status_text.Text = "The clipboard is busy - try again. (" + ex.Message + ")";
            }
        }
    }
}
