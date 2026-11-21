// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk HUD - Snapshot: a clean picture of the walk view (no HUD, no crosshair) saved next to the snag report,
// Documents\Heron Model Walk\<model>\Snapshot_<date time ms>.png. The HUD is hidden first and the picture is taken
// only after WPF has drawn the hidden HUD (a render pass, then a short wait) - the old tool could catch the HUD.
// Screen pixels are copied with plain GDI and saved with WPF's PNG encoder: no Windows Forms or System.Drawing.

using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HeronTools.Services.ModelWalk;

namespace HeronTools.UI.ModelWalk
{
    public partial class ModelWalkHudWindow
    {
        private bool _snapshotBusy;

        private void TakeSnapshot()
        {
            if (!_haveFullRect || _snapshotBusy)
                return;
            _snapshotBusy = true;
            PlayLayer.Visibility = Visibility.Collapsed;

            // ContextIdle: after WPF has rendered the hidden HUD; then 80 ms more for the screen to show it.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                wait.Tick += (s, e) =>
                {
                    wait.Stop();
                    Guard(CaptureNow);
                };
                wait.Start();
            }), DispatcherPriority.ContextIdle);
        }

        private void CaptureNow()
        {
            try
            {
                int width = Math.Max(50, _fullRight - _fullLeft);
                int height = Math.Max(50, _fullBottom - _fullTop);
                BitmapSource picture = CopyScreen(_fullLeft, _fullTop, width, height);

                string folder = WalkReport.Folder(_session.Document.IsValidObject ? _session.Document.Title : "Model");
                string file = Path.Combine(folder, "Snapshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(picture));
                using (FileStream stream = File.Create(file))
                    encoder.Save(stream);
                _session.Hud.Toast("Snapshot saved: Documents\\Heron Model Walk\\" + Path.GetFileName(folder) + "\\" + Path.GetFileName(file), WalkToastKind.Done);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException ||
                                       ex is System.Runtime.InteropServices.ExternalException)
            {
                _session.Hud.Toast("Snapshot not saved (" + ex.Message + ")", WalkToastKind.Refused);
            }
            finally
            {
                _snapshotBusy = false;
                if (!_paused && !_ending)
                    PlayLayer.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Copies a screen rectangle (device pixels) into a frozen WPF picture.</summary>
        private static BitmapSource CopyScreen(int left, int top, int width, int height)
        {
            IntPtr screen = WalkNative.GetDC(IntPtr.Zero);
            IntPtr memory = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;
            IntPtr old = IntPtr.Zero;
            try
            {
                memory = WalkNative.CreateCompatibleDC(screen);
                bitmap = WalkNative.CreateCompatibleBitmap(screen, width, height);
                if (memory == IntPtr.Zero || bitmap == IntPtr.Zero)
                    throw new InvalidOperationException("Windows could not make room for the picture");
                old = WalkNative.SelectObject(memory, bitmap);
                if (!WalkNative.BitBlt(memory, 0, 0, width, height, screen, left, top, WalkNative.SRCCOPY))
                    throw new InvalidOperationException("Windows did not copy the screen");
                WalkNative.SelectObject(memory, old);
                old = IntPtr.Zero;
                BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                if (old != IntPtr.Zero) WalkNative.SelectObject(memory, old);
                if (bitmap != IntPtr.Zero) WalkNative.DeleteObject(bitmap);
                if (memory != IntPtr.Zero) WalkNative.DeleteDC(memory);
                if (screen != IntPtr.Zero) WalkNative.ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
