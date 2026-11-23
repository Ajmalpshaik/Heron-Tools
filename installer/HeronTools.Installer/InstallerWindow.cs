// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  platform
// See docs/metadata-standard.md

// The installer window - one window, one list - laid out like the Heron AI installer (no code shared): the
// Heron Tools logo at the top with "Installer <version>" on the right, over a line of the brand's orange;
// Mist for the page, Ink for the words; short labels; the orange Install, a quiet Close, and a quiet
// Remove Heron Tools while it is installed; the heron as the window's icon.
//
// HOW IT WORKS, the Heron AI way (its installer since 2026-11-08, Heron AI pull request 821):
//   - "Revit versions found on this PC": tick the versions to install into (all found ones start ticked).
//   - "Products": Heron Tools and what is installed on the right - words only, NO tick box. With one
//     product a box offers no choice and one trap: unticking it used to turn Install into a removal.
//   - Install installs (or replaces) Heron Tools in the ticked versions. It never removes anything.
//   - "Remove Heron Tools", quiet like Close and drawn only while Heron Tools is on this PC, takes it out of
//     every Revit it is in, after a Yes/No question with No first.
//   - Install or Remove while one of those Revit versions is open WAITS, says so, and carries on by itself
//     once it is closed. It never closes Revit.
// Every install decision is the engine's (InstallEngine); this window only draws and asks.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HeronTools.Installer
{
    internal sealed class InstallerWindow : Window
    {
        // The brand's colours (the Heron brand kit; the same values as the add-in's HeronStyles.xaml).
        private static readonly Color Ink = Color.FromRgb(0x0F, 0x0F, 0x10);
        private static readonly Color Mist = Color.FromRgb(0xF7, 0xF5, 0xF2);
        private static readonly Color Accent = Color.FromRgb(0xFD, 0x64, 0x1A);
        private static readonly Color AccentLight = Color.FromRgb(0xFF, 0x9A, 0x5C);
        private static readonly Color White = Color.FromRgb(0xFF, 0xFF, 0xFF);   // text on Accent

        private static Color At(Color colour, byte alpha) => Color.FromArgb(alpha, colour.R, colour.G, colour.B);

        private const string CloseRevitFirst = "Close Revit before installing - the installer waits until it is closed.";
        private const string Replaces = "Already installed. Pressing Install replaces it - there is no separate Update or Repair.";

        private readonly Payload _payload;
        private readonly InstallEngine _engine;
        private readonly Dictionary<int, CheckBox> _releaseTicks = new Dictionary<int, CheckBox>();
        private List<YearState> _years = new List<YearState>();

        private readonly WrapPanel _releaseList = new WrapPanel { Margin = new Thickness(12, 4, 0, 0) };
        private readonly TextBlock _releaseNote;
        private readonly TextBlock _productWords;
        private readonly TextBlock _productState;
        private readonly TextBlock _closeRevitFirst;
        private readonly TextBlock _report;
        private readonly Button _install;
        private readonly Button _remove;
        private DispatcherTimer _waiting;

        public InstallerWindow(Payload payload, InstallEngine engine)
        {
            _payload = payload;
            _engine = engine;

            string version = payload?.ShortVersion ?? "";
            Title = "Heron Tools Installer" + (version.Length > 0 ? "   " + version : "");
            Width = 620;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanMinimize;
            Background = new SolidColorBrush(Mist);
            Foreground = new SolidColorBrush(Ink);
            FontFamily = new FontFamily("Segoe UI");
            UseLayoutRounding = true;
            Icon = Picture("Resources/HeronTools.ico");

            var page = new StackPanel { Margin = new Thickness(22, 16, 22, 20) };
            page.Children.Add(Masthead(version));

            page.Children.Add(Heading("Revit versions found on this PC"));
            page.Children.Add(_releaseList);
            _releaseNote = Quiet("");
            page.Children.Add(_releaseNote);

            page.Children.Add(Heading("Products"));
            var product = new Grid { Margin = new Thickness(12, 4, 0, 0) };
            product.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            product.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // ONE PRODUCT, NO TICK BOX - the words alone; "Already installed..." is on hover, as in Heron AI.
            _productWords = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
            _productWords.Inlines.Add(new System.Windows.Documents.Run("Heron Tools") { FontWeight = FontWeights.SemiBold });
            _productWords.Inlines.Add(new System.Windows.Documents.Run("    Everyday Revit tools on the Heron tab.")
            {
                Foreground = new SolidColorBrush(At(Ink, 0xAB)),
            });
            product.Children.Add(_productWords);
            _productState = new TextBlock
            {
                Foreground = new SolidColorBrush(At(Ink, 0xAB)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12, 0, 0, 0),
            };
            Grid.SetColumn(_productState, 1);
            product.Children.Add(_productState);
            page.Children.Add(product);

            // ONE LINE, with the folder on hover - short words, like the Heron AI installer.
            page.Children.Add(Heading("Install location"));
            TextBlock where = Quiet("For your Windows user only - no administrator needed.");
            where.ToolTip = @"%AppData%\Autodesk\Revit\Addins\<year>\HeronTools";
            page.Children.Add(where);

            _closeRevitFirst = Notice(CloseRevitFirst);
            page.Children.Add(_closeRevitFirst);

            _report = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
            page.Children.Add(_report);

            _install = new Button
            {
                Content = "Install",
                Width = 120,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 0),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(White),   // White text on Accent (brand rule)
                Cursor = Cursors.Hand,
                // Hover: Accent Light only as the light end of an Accent gradient, never alone.
                Template = ButtonTemplate(new SolidColorBrush(Accent), new LinearGradientBrush(AccentLight, Accent, 90.0), null),
            };
            _install.Click += (s, e) => StartInstall();

            // REMOVE HERON TOOLS, quiet like Close - drawn only while Heron Tools is on this PC (DrawProduct).
            _remove = new Button
            {
                Content = "Remove Heron Tools",
                Width = 140,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 0),
                Foreground = new SolidColorBrush(Ink),
                Cursor = Cursors.Hand,
                ToolTip = "Takes Heron Tools out of every Revit on this PC. It asks first.",
                Template = ButtonTemplate(Brushes.Transparent, new SolidColorBrush(At(Ink, 0x0F)), At(Ink, 0x40)),
            };
            _remove.Click += (s, e) => StartRemove();

            var close = new Button
            {
                Content = "Close",
                Width = 120,
                Height = 34,
                Foreground = new SolidColorBrush(Ink),
                Cursor = Cursors.Hand,
                IsCancel = true,
                Template = ButtonTemplate(Brushes.Transparent, new SolidColorBrush(At(Ink, 0x0F)), At(Ink, 0x40)),
            };
            close.Click += (s, e) => Close();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0),
            };
            buttons.Children.Add(_remove);
            buttons.Children.Add(_install);
            buttons.Children.Add(close);
            page.Children.Add(buttons);

            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };

            Refresh(keepTicks: false);
            Closed += (s, e) => _waiting?.Stop();
        }

        // ------------------------------------------------------------------ drawing the PC's state

        /// <summary>Reads the PC again and draws it. Ticks the modeller changed are kept unless asked not to.</summary>
        private void Refresh(bool keepTicks)
        {
            var before = _releaseTicks.ToDictionary(t => t.Key, t => t.Value.IsChecked == true);
            _years = _engine.ReadYears().Where(y => y.RevitInstalled || y.Installed != null).ToList();
            _releaseList.Children.Clear();
            _releaseTicks.Clear();

            foreach (YearState year in _years)
            {
                var tick = new CheckBox
                {
                    Content = "Revit " + year.Year,
                    IsChecked = year.StoreCopy == null && (keepTicks && before.ContainsKey(year.Year) ? before[year.Year] : year.RevitInstalled),
                    Margin = new Thickness(0, 4, 24, 4),
                    MinWidth = 120,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                if (!year.RevitInstalled)
                    tick.ToolTip = $"Revit {year.Year} is not on this PC any more, but Heron Tools still is - Remove Heron Tools takes it out.";
                if (year.StoreCopy != null)
                {
                    // The Autodesk App Store version is there: a second copy would give Revit's "duplicate add-in" message.
                    tick.IsEnabled = false;
                    tick.ToolTip = InstallEngine.StoreCopyMessage(year.Year, year.StoreCopy);
                    ToolTipService.SetShowOnDisabled(tick, true);
                }
                _releaseTicks[year.Year] = tick;
                _releaseList.Children.Add(tick);
            }

            if (_payload == null)
                _releaseNote.Text = "This installer was built without Heron Tools inside, so it has nothing to install.";
            else if (_years.Count == 0)
                _releaseNote.Text = "No Revit was found on this PC. Heron Tools installs into Revit 2020 to 2027, so there is nothing for it to install into. Install Revit first.";
            else if (_years.Any(y => y.StoreCopy != null))
                _releaseNote.Text = "Revit " + Join(_years.Where(y => y.StoreCopy != null).Select(y => y.Year)) +
                    " already has Heron Tools from the Autodesk App Store, so this installer leaves it out (two copies " +
                    "would make Revit show a \"duplicate add-in\" message). To use this version there, remove the Store " +
                    "version first (Windows Settings > Apps).";
            else
                _releaseNote.Text = "";
            _releaseNote.Visibility = _releaseNote.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            DrawProduct();
            DrawCloseRevitFirst();
        }

        /// <summary>The product row (what is installed now, on the right) and the buttons.</summary>
        private void DrawProduct()
        {
            List<YearState> installed = _years.Where(y => y.Installed != null).ToList();

            if (installed.Count == 0)
                _productState.Text = "";
            else if (_years.All(y => y.Installed != null))
                _productState.Text = "Installed";
            else
                _productState.Text = "Installed for Revit " + Join(installed.Select(y => y.Year));

            string hover = installed.Count > 0 ? Replaces : null;
            _productWords.ToolTip = hover;
            _productState.ToolTip = hover;

            bool waiting = _waiting != null && _waiting.IsEnabled;
            _install.IsEnabled = _payload != null && _years.Count > 0 && !waiting;
            _remove.Visibility = installed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _remove.IsEnabled = !waiting;
        }

        /// <summary>The bold line above the report, naming any Revit open right now.</summary>
        private void DrawCloseRevitFirst()
        {
            List<int> running = _engine.RunningRevitYears();
            List<int> open = running.Where(y => y != 0).Distinct().OrderBy(y => y).ToList();
            if (open.Count > 0)
                _closeRevitFirst.Text = "Revit " + Join(open) + (open.Count == 1 ? " is" : " are") + " open right now. " + CloseRevitFirst;
            else if (running.Contains(0))
                _closeRevitFirst.Text = "A Revit is open right now. " + CloseRevitFirst;
            else
                _closeRevitFirst.Text = CloseRevitFirst;
        }

        private List<int> Chosen() => _releaseTicks.Where(t => t.Value.IsChecked == true).Select(t => t.Key).ToList();

        // ------------------------------------------------------------------ install

        private void StartInstall()
        {
            _waiting?.Stop();
            List<int> chosen = Chosen();
            if (chosen.Count == 0)
            {
                _report.Text = "No Revit version is ticked, so there is nowhere to install to.";
                return;
            }
            WaitThenRun(chosen, new List<int>(), chosen);
        }

        /// <summary>Remove Heron Tools: out of every Revit it is in, after a Yes/No question with No first.</summary>
        private void StartRemove()
        {
            _waiting?.Stop();
            List<int> remove = _years.Where(y => y.Installed != null).Select(y => y.Year).ToList();
            if (remove.Count == 0)
            {
                _report.Text = "Heron Tools is not on this PC, so there is nothing to remove.";
                return;
            }

            MessageBoxResult answer = MessageBox.Show(this,
                "This will REMOVE 'Heron Tools' from Revit " + Join(remove) + "." + Environment.NewLine + Environment.NewLine +
                "Restart Revit afterwards to unload it." + Environment.NewLine + Environment.NewLine +
                "Your models are not touched.",
                "Heron Tools Installer", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                _report.Text = "Nothing was changed.";
                return;
            }

            WaitThenRun(new List<int>(), remove, Chosen());
        }

        /// <summary>Runs now, or - never working around it - waits while a Revit that is involved is open.</summary>
        private void WaitThenRun(List<int> install, List<int> remove, List<int> chosen)
        {
            List<int> involved = install.Concat(remove).ToList();
            string blocking = Blocking(involved);
            if (blocking == null)
            {
                Run(install, remove, chosen);
                return;
            }

            _report.Text = blocking + " Close it and this will carry on by itself - nothing has been changed yet.";
            _waiting = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _waiting.Tick += (s, e) =>
            {
                DrawCloseRevitFirst();
                if (Blocking(involved) == null)
                {
                    _waiting.Stop();
                    Run(install, remove, chosen);
                }
            };
            _waiting.Start();
            DrawProduct();
        }

        /// <summary>Which open Revit stops this run, said in a sentence; null when none does.</summary>
        private string Blocking(List<int> involved)
        {
            List<int> running = _engine.RunningRevitYears();
            if (running.Contains(0))
                return "A Revit is open and which one it is could not be read, so none can be replaced while it runs.";
            List<int> open = running.Where(involved.Contains).Distinct().OrderBy(y => y).ToList();
            return open.Count == 0 ? null : "Revit " + Join(open) + (open.Count == 1 ? " is" : " are") + " open.";
        }

        private void Run(List<int> install, List<int> remove, List<int> chosen)
        {
            _install.IsEnabled = false;
            _remove.IsEnabled = false;
            Cursor = Cursors.Wait;
            var lines = new List<string>();
            try
            {
                foreach (int year in install)
                    lines.Add(_engine.Install(year).Message);
                foreach (int year in remove)
                    lines.Add(_engine.Remove(year).Message);
                if (remove.Count > 0)
                    lines.Add("Restart Revit so Heron Tools stops loading.");
                foreach (YearState year in _years.Where(y => install.Count > 0 && y.RevitInstalled && y.StoreCopy == null && !chosen.Contains(y.Year)))
                    lines.Add($"Revit {year.Year} is on this PC but was not ticked, so nothing was installed for it. Run this again and tick it if that was not meant.");
            }
            catch (Exception ex)
            {
                lines.Add("The installer stopped: " + ex.Message);
            }
            finally
            {
                Cursor = null;
            }

            _report.Text = lines.Count == 0 ? "Nothing was installed." : string.Join(Environment.NewLine, lines);
            Refresh(keepTicks: true);
        }

        // ------------------------------------------------------------------ parts

        private static string Join(IEnumerable<int> years)
        {
            List<string> list = years.Select(y => y.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
            if (list.Count == 0)
                return "none";
            if (list.Count == 1)
                return list[0];
            return string.Join(", ", list.Take(list.Count - 1)) + " and " + list[list.Count - 1];
        }

        private static UIElement Masthead(string version)
        {
            var bar = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            ImageSource logo = Picture("Resources/heron-tools-logo.png");
            if (logo != null)
            {
                var image = new Image { Source = logo, Height = 40, HorizontalAlignment = HorizontalAlignment.Left };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                bar.Children.Add(image);
            }
            else
            {
                bar.Children.Add(new TextBlock { Text = "Heron Tools", FontSize = 22, FontWeight = FontWeights.Bold });
            }

            var installer = new TextBlock
            {
                Text = "Installer" + (string.IsNullOrEmpty(version) ? "" : "   " + version),
                Foreground = new SolidColorBrush(At(Ink, 0x8C)),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 4),
            };
            Grid.SetColumn(installer, 1);
            bar.Children.Add(installer);

            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            stack.Children.Add(bar);
            stack.Children.Add(new Border { Height = 2, Background = new SolidColorBrush(Accent), CornerRadius = new CornerRadius(1) });
            return stack;
        }

        private static TextBlock Heading(string text) => new TextBlock
        {
            Text = text,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Ink),
            Margin = new Thickness(0, 12, 0, 3),
        };

        private static TextBlock Quiet(string text) => new TextBlock
        {
            Text = text ?? "",
            Foreground = new SolidColorBrush(At(Ink, 0xAB)),
            Margin = new Thickness(12, 1, 0, 1),
            TextWrapping = TextWrapping.Wrap,
        };

        private static TextBlock Notice(string text) => new TextBlock
        {
            Text = text ?? "",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Ink),
            Margin = new Thickness(0, 14, 0, 0),
        };

        private static ControlTemplate ButtonTemplate(Brush fill, Brush hover, Color? border)
        {
            var template = new ControlTemplate(typeof(Button));
            var box = new FrameworkElementFactory(typeof(Border), "box");
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            box.SetValue(Border.BackgroundProperty, fill);
            box.SetValue(Border.BorderThicknessProperty, new Thickness(border.HasValue ? 1 : 0));
            if (border.HasValue)
                box.SetValue(Border.BorderBrushProperty, new SolidColorBrush(border.Value));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            box.AppendChild(content);
            template.VisualTree = box;

            var over = new Trigger { Property = IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty, hover, "box"));
            template.Triggers.Add(over);

            var off = new Trigger { Property = IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(OpacityProperty, 0.45, "box"));
            template.Triggers.Add(off);
            return template;
        }

        private static BitmapSource Picture(string path)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri("pack://application:,,,/HeronToolsInstaller;component/" + path);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null;   // the window still works, with the name in text
            }
        }

        // ------------------------------------------------------------------ the proof picture

        /// <summary>Draws the window into a PNG (for the README and the record), then closes it.</summary>
        public void SaveAsPicture(string pngPath, string reportText = null)
        {
            if (reportText != null)
                _report.Text = reportText;
            Left = -20000;
            Top = -20000;
            ShowInTaskbar = false;
            Show();
            UpdateLayout();
            var content = (FrameworkElement)Content;
            double w = content.ActualWidth, h = content.ActualHeight;
            var bitmap = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Mist), null, new Rect(0, 0, w, h));
                // Absolute viewbox: the page's own margins are drawn too (a plain brush crops to the ink).
                dc.DrawRectangle(new VisualBrush(content)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, w, h),
                }, null, new Rect(0, 0, w, h));
            }
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = System.IO.File.Create(pngPath))
                encoder.Save(stream);
            Close();
        }
    }
}
