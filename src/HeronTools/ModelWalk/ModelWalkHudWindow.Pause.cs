// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk HUD - the pause menu: carry on, Settings, Keys, Clear snag colours, the report folder, End walk, the
// snag list (Open snag) and the viewpoint list (go, save as a Revit 3D view, delete, play). Anything that needs
// Revit's API (clear colours, save a 3D view) is queued and run by the engine's ExternalEvent - never from here.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using HeronTools.Services.ModelWalk;

namespace HeronTools.UI.ModelWalk
{
    public partial class ModelWalkHudWindow
    {
        private int _lastPauseStatusVersion = -1;
        private bool _pauseMenuShown;

        private void ShowPauseMenu()
        {
            _pauseMenuShown = true;
            PauseMenu.Visibility = Visibility.Visible;
            ClearSnagsButton.Content = "Clear snag colours (" + _keys.Label(WalkKeyBindings.ClearSnags) + ")";
            SetPauseStatus("");
            RebuildPauseLists();
        }

        private void HidePauseMenu()
        {
            _pauseMenuShown = false;
            PauseMenu.Visibility = Visibility.Collapsed;
        }

        private void SetPauseStatus(string text)
        {
            PauseStatus.Text = text ?? "";
            PauseStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>While paused: the engine's answer to a queued action (colours cleared, view saved).</summary>
        private void UpdatePausedFromEngine(WalkHudState hud)
        {
            if (!_pauseMenuShown || hud.PauseStatusVersion == _lastPauseStatusVersion)
                return;
            _lastPauseStatusVersion = hud.PauseStatusVersion;
            SetPauseStatus(hud.PauseStatus);
            RebuildPauseLists();
            ShrinkToPill();
        }

        private void RebuildPauseLists()
        {
            // Snags, newest first.
            SnagMenuList.Children.Clear();
            IReadOnlyList<WalkSnag> snags = _session.Report.Snags;
            for (int i = snags.Count - 1; i >= 0; i--)
            {
                WalkSnag snag = snags[i];
                string text = snag.Number + "    " + snag.Summary + (string.IsNullOrEmpty(snag.Comment) ? "" : "  -  " + snag.Comment);
                SnagMenuList.Children.Add(RowButton(text, "Go back to snag " + snag.Number + " and select the element", snag.Number, OnSnagRowClick));
            }
            SnagSection.Visibility = snags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            RebuildViewpointMenu();
        }

        private Button RowButton(string text, string tip, object tag, RoutedEventHandler click)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis },
                Tag = tag,
                ToolTip = tip,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 1, 0, 1)
            };
            button.SetResourceReference(StyleProperty, "CompactSecondaryButton");
            button.Click += (s, e) => Guard(() => click(s, e));
            return button;
        }

        private void RebuildViewpointMenu()
        {
            ViewpointMenuList.Children.Clear();
            foreach (WalkViewpoint vp in _session.Viewpoints)
            {
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                Button delete = SmallButton("Delete", "Forget this viewpoint (the saved Revit views stay)", vp.Number, OnViewpointDeleteClick);
                Button save = SmallButton("Save as 3D view", "Save a Revit 3D view looking from here - for a meeting or a sheet (one Undo step)",
                                          vp.Number, OnViewpointSaveViewClick);
                DockPanel.SetDock(delete, Dock.Right);
                DockPanel.SetDock(save, Dock.Right);
                row.Children.Add(delete);
                row.Children.Add(save);
                row.Children.Add(RowButton((vp.Number <= 9 ? vp.Number + "    " : "      ") + vp.Name,
                    "Go to this viewpoint" + (vp.Number <= 9 ? " (" + vp.Number + " in the walk)" : ""), vp.Number, OnViewpointGoClick));
                ViewpointMenuList.Children.Add(row);
            }
            ViewpointSection.Visibility = _session.Viewpoints.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Button SmallButton(string text, string tip, object tag, RoutedEventHandler click)
        {
            var button = new Button { Content = text, Tag = tag, ToolTip = tip, Margin = new Thickness(4, 1, 0, 1) };
            button.SetResourceReference(StyleProperty, "CompactSecondaryButton");
            button.Click += (s, e) => Guard(() => click(s, e));
            return button;
        }

        private void OnViewpointGoClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is int number))
                return;
            _session.Input.JumpViewpoint = number;
            EnterPlay();
        }

        private void OnViewpointSaveViewClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is int number))
                return;
            _session.Input.SaveViewpointAsView = number;
            SetPauseStatus("Saving the 3D view...");
            ShrinkToPill();
            RaiseFrame();   // the engine makes the view on Revit's thread, while the walk stays paused
        }

        private void OnViewpointDeleteClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is int number))
                return;
            WalkViewpoint vp = _session.Viewpoints.Find(v => v.Number == number);
            if (vp == null)
                return;
            _session.Viewpoints.Remove(vp);
            string problem = _session.SaveViewpoints();   // numbers close up: 1, 2, 3...
            SetPauseStatus(problem ?? "Viewpoint \"" + vp.Name + "\" deleted. The others are numbered again.");
            RebuildViewpointMenu();
            ShrinkToPill();
        }

        private void OnPlayViewpointsClick(object sender, RoutedEventArgs e) => Guard(() =>
        {
            _session.Input.PlayViewpointsQueued = true;
            EnterPlay();
        });

        // ------------------------------------------------------------------ buttons

        private void OnResumeClick(object sender, RoutedEventArgs e) => Guard(EnterPlay);

        private void OnSettingsClick(object sender, RoutedEventArgs e) => Guard(OpenSettings);

        private void OnKeysClick(object sender, RoutedEventArgs e) => Guard(OpenKeySettings);

        private void OnEndClick(object sender, RoutedEventArgs e) => Guard(() => RequestStop("You ended the walk."));

        private void OnClearSnagsClick(object sender, RoutedEventArgs e) => Guard(() =>
        {
            _session.Input.ClearSnagsQueued = true;
            SetPauseStatus("Giving the snag colours back...");
            RaiseFrame();   // the engine runs it on Revit's thread, while the walk stays paused
        });

        private void OnOpenFolderClick(object sender, RoutedEventArgs e) => Guard(() =>
        {
            string folder = _session.Report.FolderPath;
            if (string.IsNullOrEmpty(folder))
            {
                SetPauseStatus("Nothing is saved yet - the folder is made with the first snag or snapshot.");
                ShrinkToPill();
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                SetPauseStatus("The folder could not be opened (" + ex.Message + "). It is " + folder);
                ShrinkToPill();
            }
        });

        private void OnSnagRowClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is int number))
                return;
            _session.Input.OpenSnag = number;   // the engine puts the camera back and selects the element
            EnterPlay();
        }
    }
}
