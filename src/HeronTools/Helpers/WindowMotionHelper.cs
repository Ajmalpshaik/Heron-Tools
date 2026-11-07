// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// One shared entrance animation for Heron Tools working dialogs, so every window opens with the same motion instead of snapping into place.

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace HeronTools.Utils
{
    /// <summary>
    /// Shared entrance motion for Heron Tools working dialogs.
    /// </summary>
    internal static class WindowMotionHelper
    {
        /// <summary>Opacity ramp. Short enough that the window reads as instant.</summary>
        private const double FadeMilliseconds = 220.0;

        /// <summary>Travel ramp. Slightly longer than the fade so the window settles after it is readable.</summary>
        private const double RiseMilliseconds = 280.0;

        /// <summary>How far below its resting place the content starts, in device-independent pixels.</summary>
        private const double RiseDistance = 12.0;

        /// <summary>Exit ramp. Shorter than the entrance - users care about what appears, not what leaves.</summary>
        private const double ExitMilliseconds = 150.0;

        /// <summary>How far the content sinks on the way out, in device-independent pixels.</summary>
        private const double ExitSinkDistance = 6.0;

        /// <summary>
        /// Extra time the backstop timer waits past the animation before forcing the close. A dialog that
        /// cannot be dismissed is far worse than a missing animation, so the close never depends solely
        /// on an animation completing.
        /// </summary>
        private const double ExitSafetyMarginMilliseconds = 250.0;

        /// <summary>
        /// Gives the window a fade-and-rise entrance the first time it is shown.
        /// Call once from the constructor, after InitializeComponent(). Safe to call on any window;
        /// it quietly does nothing if the window has no content or already drives its own transform.
        /// </summary>
        internal static void AttachStandardEntrance(Window window)
        {
            if (window == null)
                return;

            try
            {
                var root = window.Content as FrameworkElement;
                if (root == null)
                    return;

                // Never fight a window that is already transforming its own root.
                if (root.RenderTransform != null && !root.RenderTransform.Value.IsIdentity)
                    return;

                var translate = new TranslateTransform(0.0, RiseDistance);
                root.RenderTransform = translate;
                root.Opacity = 0.0;

                // Start on Loaded (fires before the first paint, so there is no flash of the
                // finished state) and unsubscribe immediately - this is a one-shot entrance.
                RoutedEventHandler onLoaded = null;
                onLoaded = delegate
                {
                    window.Loaded -= onLoaded;
                    PlayEntrance(root, translate);
                };

                window.Loaded += onLoaded;
            }
            catch
            {
                // Motion is cosmetic - never let it stop a window from opening.
                RestoreToRest(window);
            }
        }

        private static void PlayEntrance(FrameworkElement root, TranslateTransform translate)
        {
            try
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

                var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(FadeMilliseconds))
                {
                    EasingFunction = ease
                };

                var rise = new DoubleAnimation(RiseDistance, 0.0, TimeSpan.FromMilliseconds(RiseMilliseconds))
                {
                    EasingFunction = ease
                };

                // Applied straight to the element and the transform: no Storyboard, so there is no
                // namescope to resolve and no frozen-resource problem to work around.
                root.BeginAnimation(UIElement.OpacityProperty, fade);
                translate.BeginAnimation(TranslateTransform.YProperty, rise);
            }
            catch
            {
                root.Opacity = 1.0;
                root.RenderTransform = null;
            }
        }

        /// <summary>
        /// Per-window state for the exit. Held in a closure rather than an attached property so two
        /// windows can never share it, and so nothing survives the window itself.
        /// </summary>
        private sealed class ExitState
        {
            /// <summary>A close is in flight: keep cancelling every further Closing.</summary>
            internal bool IsExitPlaying;

            /// <summary>Our own completion callback asked for the real close: let it through.</summary>
            internal bool IsReadyToClose;

            /// <summary>The real close has been issued once. Guards animation-vs-timer double firing.</summary>
            internal bool IsFinished;

            /// <summary>
            /// The dialog's result, captured BEFORE the close is cancelled. WPF discards DialogResult on a
            /// cancelled close, so without this every Run button would come back as Cancel.
            /// </summary>
            internal bool? SavedDialogResult;

            /// <summary>Backstop, in case the animation never completes.</summary>
            internal DispatcherTimer Timer;
        }

        /// <summary>
        /// Gives the window a short fade-and-sink as it closes. Call once from the constructor, after
        /// InitializeComponent(). The window's result, validation and close behaviour are unchanged -
        /// see the exit notes in this file's header before altering any of this.
        /// </summary>
        internal static void AttachStandardExit(Window window)
        {
            if (window == null)
                return;

            try
            {
                var state = new ExitState();
                window.Closing += delegate(object sender, CancelEventArgs e)
                {
                    HandleClosing(window, state, e);
                };
            }
            catch
            {
                // Motion is cosmetic - never let it stop a window from closing.
            }
        }

        private static void HandleClosing(Window window, ExitState state, CancelEventArgs e)
        {
            try
            {
                // The close we re-issued ourselves once the animation finished.
                if (state.IsReadyToClose)
                    return;

                // Another handler has already vetoed this close - a busy guard, an unsaved-changes
                // prompt, a validation refusal. Respect it and do nothing: without this the exit
                // animation would run and then force the window shut, overriding the veto. Window's
                // own handlers run first (they subscribe during InitializeComponent), so by the time
                // this runs their decision is already on the event args.
                if (e.Cancel)
                    return;

                // Any further Closing while the exit plays - most commonly the second one raised by a
                // button that is both Click= and IsCancel="True".
                if (state.IsExitPlaying)
                {
                    e.Cancel = true;
                    return;
                }

                var root = window.Content as FrameworkElement;
                if (root == null)
                {
                    // Nothing to animate. Close normally rather than inventing a reason to interfere.
                    state.IsReadyToClose = true;
                    return;
                }

                state.IsExitPlaying = true;

                // MUST be read before the cancel below: WPF throws DialogResult away when a close is
                // cancelled, so this is the only moment the real value is still available.
                try { state.SavedDialogResult = window.DialogResult; }
                catch { state.SavedDialogResult = null; }

                e.Cancel = true;
                PlayExit(window, root, state);
            }
            catch
            {
                // If anything at all goes wrong, let the window close the normal way immediately.
                e.Cancel = false;
                state.IsReadyToClose = true;
                state.IsFinished = true;
            }
        }

        private static void PlayExit(Window window, FrameworkElement root, ExitState state)
        {
            try
            {
                // Backstop armed FIRST, so the close never depends on the animation completing.
                var timer = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(ExitMilliseconds + ExitSafetyMarginMilliseconds)
                };
                timer.Tick += delegate { FinishClose(window, state); };
                state.Timer = timer;
                timer.Start();

                // Exits accelerate away, where entrances decelerate in.
                var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

                var translate = root.RenderTransform as TranslateTransform;
                if (translate == null && (root.RenderTransform == null || root.RenderTransform.Value.IsIdentity))
                {
                    translate = new TranslateTransform();
                    root.RenderTransform = translate;
                }

                if (translate != null)
                {
                    var sink = new DoubleAnimation(translate.Y, translate.Y + ExitSinkDistance,
                                                   TimeSpan.FromMilliseconds(ExitMilliseconds))
                    {
                        EasingFunction = ease
                    };
                    translate.BeginAnimation(TranslateTransform.YProperty, sink);
                }

                var fade = new DoubleAnimation(root.Opacity, 0.0, TimeSpan.FromMilliseconds(ExitMilliseconds))
                {
                    EasingFunction = ease
                };
                fade.Completed += delegate { FinishClose(window, state); };
                root.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch
            {
                FinishClose(window, state);
            }
        }

        private static void FinishClose(Window window, ExitState state)
        {
            // Animation completion and the backstop timer both land here; only the first one counts.
            if (state.IsFinished)
                return;

            state.IsFinished = true;
            state.IsReadyToClose = true;

            try
            {
                if (state.Timer != null)
                {
                    state.Timer.Stop();
                    state.Timer = null;
                }
            }
            catch
            {
                // A timer that cannot be stopped is harmless - FinishClose is single-shot.
            }

            try
            {
                if (state.SavedDialogResult.HasValue)
                {
                    // Restoring the captured result re-issues the close by itself, and hands the command
                    // the same answer it would have got with no animation at all.
                    window.DialogResult = state.SavedDialogResult;
                }
                else
                {
                    window.Close();
                }
            }
            catch
            {
                // Modeless windows throw when DialogResult is set. Close plainly instead.
                try { window.Close(); }
                catch { }
            }
        }

        private static void RestoreToRest(Window window)
        {
            try
            {
                var root = window.Content as FrameworkElement;
                if (root == null)
                    return;

                root.Opacity = 1.0;
                root.RenderTransform = null;
            }
            catch
            {
                // Nothing further to do - the window is already in whatever state it can reach.
            }
        }
    }
}
