// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk HUD - pictures: Snapshot (K) and each snag's picture - a clean picture of the walk view (no HUD, no
// crosshair) saved in the walk folder with the snag report ("Snapshot <date time ms>.png", "Snag 001.png"). The HUD
// is hidden first and the picture is taken only after WPF has drawn the hidden HUD (a render pass, then a short
// wait) - the old tool could catch the HUD.
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
            string fileName = "Snapshot " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss-fff", CultureInfo.InvariantCulture) + ".png";
            CapturePicture(fileName,
                saved => _session.Hud.Toast("Snapshot saved: " + ShortFolder() + "\\" + saved, WalkToastKind.Done),
                failed => _session.Hud.Toast("Snapshot not saved (" + failed + ")", WalkToastKind.Refused));
        }

        /// <summary>The walk folder as "...\Model Walk - Model\2026-11-21 10-30-00" - short enough for a message.</summary>
        private string ShortFolder()
        {
            string folder = _session.Report.FolderPath;
            if (string.IsNullOrEmpty(folder))
                return "";
            string parent = Path.GetFileName(Path.GetDirectoryName(folder) ?? "");
            return "...\\" + parent + "\\" + Path.GetFileName(folder);
        }

        /// <summary>
        /// A clean picture of the walk view into the walk folder: the HUD is hidden, WPF draws the hidden HUD (a render
        /// pass), 80 ms more for the screen, then the copy. <paramref name="saved"/> gets the file name, or
        /// <paramref name="failed"/> the reason. A second picture asked for while one is being taken is refused.
        /// </summary>
        private void CapturePicture(string fileName, Action<string> saved, Action<string> failed)
        {
            if (!_haveFullRect || _snapshotBusy)
            {
                failed?.Invoke("a picture is already being taken");
                return;
            }
            _snapshotBusy = true;
            PlayLayer.Visibility = Visibility.Collapsed;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                wait.Tick += (s, e) =>
                {
                    wait.Stop();
                    Guard(() => CaptureNow(fileName, saved, failed));
                };
                wait.Start();
            }), DispatcherPriority.ContextIdle);
        }

        private void CaptureNow(string fileName, Action<string> saved, Action<string> failed)
        {
            string problem = null;
            try
            {
                int width = Math.Max(50, _fullRight - _fullLeft);
                int height = Math.Max(50, _fullBottom - _fullTop);
                BitmapSource picture = CopyScreen(_fullLeft, _fullTop, width, height);

                string file = Path.Combine(_session.Report.EnsureFolder(), fileName);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(picture));
                using (FileStream stream = File.Create(file))
                    encoder.Save(stream);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException ||
                                       ex is System.Security.SecurityException || ex is System.Runtime.InteropServices.ExternalException)
            {
                problem = ex.Message;
            }
            finally
            {
                _snapshotBusy = false;
                if (!_paused && !_ending)
                    PlayLayer.Visibility = Visibility.Visible;
            }

            if (problem == null)
                saved?.Invoke(fileName);
            else
                failed?.Invoke(problem);
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
