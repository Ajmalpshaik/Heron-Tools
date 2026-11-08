// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Keeps a window fully on the screen it opens on, so its bottom buttons (Run, Cancel) are never below the
// screen edge. A window centred on Revit lands partly off-screen when Revit is not maximised or sits low, and
// a tall window does not fit a small or scaled laptop screen (Ajmal's View Crop window, 2026-11-08: "the
// downside I cannot see"). On opening - and whenever it grows - the window is moved up (or left) into the
// screen's usable area (the taskbar excluded) and, if it is still taller than that area, made exactly as tall
// as it - its middle then scrolls where the window has a scroll area.
// Every window that uses WindowMotionHelper.AttachStandardEntrance gets this.

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace HeronTools.Utils
{
    internal static class ScreenFit
    {
        /// <summary>Space kept free between the window and the screen edge, in WPF units.</summary>
        private const double Gap = 8;

        internal static void Attach(Window window)
        {
            if (window == null)
                return;
            window.Loaded += (s, e) => Fit(window);
            window.SizeChanged += (s, e) => Fit(window);
        }

        private static void Fit(Window window)
        {
            try
            {
                if (window.WindowState != WindowState.Normal || !window.IsLoaded)
                    return;
                if (!TryGetWorkArea(window, out Rect work))
                    work = SystemParameters.WorkArea;

                double maxHeight = Math.Max(200, work.Height - 2 * Gap);
                double maxWidth = Math.Max(300, work.Width - 2 * Gap);
                if (window.MaxHeight > maxHeight)
                    window.MaxHeight = maxHeight;
                if (window.MaxWidth > maxWidth)
                    window.MaxWidth = maxWidth;

                double height = Math.Min(window.ActualHeight, maxHeight);
                double width = Math.Min(window.ActualWidth, maxWidth);

                // Bottom edge below the screen: move up; top edge above it: move down.
                if (window.Top + height > work.Bottom - Gap)
                    window.Top = work.Bottom - Gap - height;
                if (window.Top < work.Top + Gap)
                    window.Top = work.Top + Gap;
                if (window.Left + width > work.Right - Gap)
                    window.Left = work.Right - Gap - width;
                if (window.Left < work.Left + Gap)
                    window.Left = work.Left + Gap;
            }
            catch (Exception)
            {
                // Never let placing a window stop it from working.
            }
        }

        // ------------------------------------------------------------------ the usable area of THIS window's screen

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        private const uint MonitorDefaultToNearest = 2;

        /// <summary>The usable area (no taskbar) of the screen the window is on, in WPF units.</summary>
        private static bool TryGetWorkArea(Window window, out Rect work)
        {
            work = Rect.Empty;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return false;
            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
                return false;

            // Screen pixels to WPF units (a 150 % screen: 1.5 pixels per unit).
            PresentationSource source = PresentationSource.FromVisual(window);
            Matrix toUnits = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            Point topLeft = toUnits.Transform(new Point(info.Work.Left, info.Work.Top));
            Point bottomRight = toUnits.Transform(new Point(info.Work.Right, info.Work.Bottom));
            work = new Rect(topLeft, bottomRight);
            return work.Width > 0 && work.Height > 0;
        }
    }
}
