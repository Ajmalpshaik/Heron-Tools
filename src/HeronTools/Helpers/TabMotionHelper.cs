// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// One shared transition for switching tabs, so a tab change reads as the panel arriving rather than the window hard-cutting to different content.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HeronTools.Utils
{
    /// <summary>
    /// Shared tab-change transition for Heron Tools windows.
    /// </summary>
    internal static class TabMotionHelper
    {
        /// <summary>Opacity ramp for the incoming panel.</summary>
        private const double FadeMilliseconds = 180.0;

        /// <summary>Travel ramp. Slightly longer than the fade so the panel settles after it is readable.</summary>
        private const double RiseMilliseconds = 220.0;

        /// <summary>How far below its resting place the panel starts, in device-independent pixels.</summary>
        private const double RiseDistance = 8.0;

        /// <summary>The template part holding the selected tab's content.</summary>
        private const string ContentHostPartName = "PART_SelectedContentHost";

        /// <summary>
        /// Gives every TabControl in the window a fade-and-rise when the user switches tabs.
        /// Call once from the constructor, after InitializeComponent(). Safe on any window - a window
        /// with no tabs simply gets nothing.
        /// </summary>
        internal static void AttachTabTransitions(Window window)
        {
            if (window == null)
                return;

            try
            {
                RoutedEventHandler onLoaded = null;
                onLoaded = delegate
                {
                    window.Loaded -= onLoaded;
                    AttachToTabControls(window);
                };

                window.Loaded += onLoaded;
            }
            catch
            {
                // Motion is cosmetic - never let it stop a window from opening.
            }
        }

        private static void AttachToTabControls(DependencyObject root)
        {
            try
            {
                int count = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count; i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(root, i);

                    var tabControl = child as TabControl;
                    if (tabControl != null)
                        tabControl.SelectionChanged += OnTabSelectionChanged;

                    AttachToTabControls(child);
                }
            }
            catch
            {
                // A partially-walked tree is fine: whatever was reached keeps its transition.
            }
        }

        private static void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var tabControl = sender as TabControl;
                if (tabControl == null)
                    return;

                // Selector.SelectionChanged is routed. A ComboBox/ListBox inside the tab raises it too,
                // and animating on those would replay the transition on every dropdown pick.
                if (!ReferenceEquals(e.OriginalSource, tabControl))
                    return;

                if (tabControl.Template == null)
                    return;

                var host = tabControl.Template.FindName(ContentHostPartName, tabControl) as FrameworkElement;
                if (host == null)
                    return;

                PlayTransition(host);
            }
            catch
            {
                // Never let a cosmetic transition interfere with the tab actually changing.
            }
        }

        private static void PlayTransition(FrameworkElement host)
        {
            try
            {
                // Reuse our own transform if this is a repeat switch; never fight one the window owns.
                var translate = host.RenderTransform as TranslateTransform;
                if (translate == null)
                {
                    if (host.RenderTransform != null && !host.RenderTransform.Value.IsIdentity)
                        return;

                    translate = new TranslateTransform();
                    host.RenderTransform = translate;
                }

                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

                var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(FadeMilliseconds))
                {
                    EasingFunction = ease
                };

                var rise = new DoubleAnimation(RiseDistance, 0.0, TimeSpan.FromMilliseconds(RiseMilliseconds))
                {
                    EasingFunction = ease
                };

                host.BeginAnimation(UIElement.OpacityProperty, fade);
                translate.BeginAnimation(TranslateTransform.YProperty, rise);
            }
            catch
            {
                host.Opacity = 1.0;
                host.RenderTransform = null;
            }
        }
    }
}
