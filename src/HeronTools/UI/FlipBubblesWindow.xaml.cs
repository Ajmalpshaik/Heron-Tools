// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The Flip Bubbles chooser. Modal, opened by CmdFlipGridBubble before any pick; it changes nothing in the
// model - it only says how the modeller wants to pick. Remembers the last choice (WindowMemory
// "flip-bubbles"): that card is marked "Last used" and is what Enter takes.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HeronTools.Utils;

namespace HeronTools.UI
{
    public partial class FlipBubblesWindow : Window
    {
        /// <summary>How the modeller chose to pick grids and levels.</summary>
        internal enum PickMode
        {
            None,
            OneByOne,
            Window
        }

        private const string MemoryTool = "flip-bubbles";
        private const string ModeKey = "mode";

        /// <summary>None when the window was closed without a choice (Cancel, Esc or the X).</summary>
        internal PickMode Chosen { get; private set; } = PickMode.None;

        public FlipBubblesWindow(string viewName)
        {
            InitializeComponent();
            Icon = BrandImages.Load(BrandImages.AppIcon);
            WindowMotionHelper.AttachStandardEntrance(this);
            WindowMotionHelper.AttachStandardExit(this);

            ViewNameText.Text = string.IsNullOrWhiteSpace(viewName) ? "none" : viewName;
            ViewNameText.ToolTip = ViewNameText.Text;

            // The remembered card gets Enter and the first keyboard focus; on first use there is no tag.
            string remembered = WindowMemory.Load(MemoryTool).Get(ModeKey);
            bool lastWasWindow = remembered == PickMode.Window.ToString();
            Button lastTile = lastWasWindow ? WindowTile : PickTile;
            lastTile.IsDefault = true;
            if (!string.IsNullOrEmpty(remembered))
                (lastWasWindow ? WindowLastUsed : PickLastUsed).Visibility = Visibility.Visible;

            PickTile.Click += (s, e) => Choose(PickMode.OneByOne);
            WindowTile.Click += (s, e) => Choose(PickMode.Window);
            CancelButton.Click += (s, e) => Close();
            PreviewKeyDown += OnPreviewKeyDown;
            Loaded += (s, e) => lastTile.Focus();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.D1:
                case Key.NumPad1:
                    e.Handled = true;
                    Choose(PickMode.OneByOne);
                    break;
                case Key.D2:
                case Key.NumPad2:
                    e.Handled = true;
                    Choose(PickMode.Window);
                    break;
            }
        }

        private void Choose(PickMode mode)
        {
            if (Chosen != PickMode.None)
                return;   // a second click while the exit animation plays

            Chosen = mode;
            WindowMemory memory = WindowMemory.Load(MemoryTool);
            memory.Set(ModeKey, mode.ToString());
            memory.SaveQuietly();
            Close();
        }
    }
}
