// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Shared custom-title-bar behaviour (drag, minimize, maximize) for borderless Heron Tools windows.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HeronTools.Utils
{
    /// <summary>
    /// Shared behaviour for borderless (WindowStyle=None) Heron Tools windows that draw their own title bar.
    /// </summary>
    internal static class WindowChromeHelper
    {
        private const double ShadowMargin = 10.0;

        /// <summary>
        /// Remembers the CornerRadius the XAML designed for a shell border, so it can be restored after
        /// a maximize squares it off. Attached per element rather than held in a static dictionary, so
        /// it is collected with the window and each window keeps its own radius.
        /// </summary>
        private static readonly DependencyProperty DesignCornerRadiusProperty =
            DependencyProperty.RegisterAttached(
                "DesignCornerRadius",
                typeof(object),
                typeof(WindowChromeHelper),
                new PropertyMetadata(null));

        /// <summary>
        /// Drags the window when the user presses the left mouse button on the custom title bar.
        /// Ignored while maximized (DragMove is invalid in that state).
        /// </summary>
        internal static void HandleTitleBarDrag(Window window, MouseButtonEventArgs e)
        {
            if (window == null || e == null)
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            if (window.WindowState == WindowState.Maximized)
                return;

            try
            {
                window.DragMove();
            }
            catch
            {
                // DragMove can throw if the mouse capture state changes mid-drag; ignore.
            }
        }

        /// <summary>Minimizes the window.</summary>
        internal static void Minimize(Window window)
        {
            if (window != null)
                window.WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// Toggles between maximized and normal, adjusting the outer border's shadow margin so the
        /// content fills the screen cleanly while maximized.
        /// </summary>
        internal static void ToggleMaximize(Window window, FrameworkElement rootBorder)
        {
            if (window == null)
                return;

            if (window.WindowState == WindowState.Maximized)
            {
                window.WindowState = WindowState.Normal;
                if (rootBorder != null)
                    rootBorder.Margin = new Thickness(ShadowMargin);
            }
            else
            {
                if (rootBorder != null)
                    rootBorder.Margin = new Thickness(0);

                window.WindowState = WindowState.Maximized;
            }

            // Also square/round the corners. Called here as well as from the window's OnStateChanged
            // override so the button path works even for a window that hasn't added the override.
            ApplyStateChrome(window, rootBorder);
        }

        /// <summary>
        /// Brings the shell border in line with the window's current state: no shadow margin and square
        /// corners while maximized, the designed margin and radius otherwise.
        ///
        /// A rounded shell MUST square off while maximized - the window fills the screen but the
        /// CornerRadius keeps cutting the corners away, so the desktop shows through all four.
        /// Call this from the window's OnStateChanged override, not just from the maximize button:
        /// Win+Up and snapping to the top edge maximize the window without ever going through
        /// ToggleMaximize.
        /// </summary>
        internal static void ApplyStateChrome(Window window, FrameworkElement rootBorder)
        {
            if (window == null || rootBorder == null)
                return;

            bool isMaximized = window.WindowState == WindowState.Maximized;

            rootBorder.Margin = isMaximized ? new Thickness(0) : new Thickness(ShadowMargin);

            var border = rootBorder as Border;
            if (border == null)
                return;

            // Capture the designed radius the first time we touch this border. Safe even if the first
            // call already reports Maximized: we read the radius before overwriting it, and nothing
            // else in the project writes CornerRadius on a shell border.
            object stored = border.GetValue(DesignCornerRadiusProperty);
            if (!(stored is CornerRadius))
            {
                stored = border.CornerRadius;
                border.SetValue(DesignCornerRadiusProperty, stored);
            }

            border.CornerRadius = isMaximized ? new CornerRadius(0) : (CornerRadius)stored;
        }
    }
}
