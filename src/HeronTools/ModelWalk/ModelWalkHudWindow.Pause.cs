// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk HUD - the pause menu: carry on, Settings, Keys, Clear snag colours, the report folder, End walk, the
// snag list (write or change each snag's comment, Open snag) and the viewpoint list (go, save as a Revit 3D view,
// delete, play). A comment saved here rewrites Snags.csv at once (through a temporary file, as every save does). Anything that needs
// Revit's API (clear colours, save a 3D view) is queued and run by the engine's ExternalEvent - never from here.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HeronTools.Services.ModelWalk;

namespace HeronTools.UI.ModelWalk
{
    public partial class ModelWalkHudWindow
    {
        private int _lastPauseStatusVersion = -1;
        private bool _pauseMenuShown;
        private int _editingSnag;   // the snag whose comment box is open in the pause menu (0 = none)

        private void ShowPauseMenu()
        {
            _pauseMenuShown = true;
            PauseMenu.Visibility = Visibility.Visible;
            ClearSnagsButton.Content = ClearSnagsLabel();
            SetPauseStatus("");
            RebuildPauseLists();
        }

        private string ClearSnagsLabel() => "Clear snag colours (" + _keys.Label(WalkKeyBindings.ClearSnags) + ")";

        private void HidePauseMenu()
        {
            // Carry on, Open snag, End walk: what was typed in the comment box is kept (only Esc drops it).
            CloseSnagEditor(save: true);
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
            // Snags, newest first: number, category, family : type, and the comment - or "No comment yet".
            SnagMenuList.Children.Clear();
            IReadOnlyList<WalkSnag> snags = _session.Report.Snags;
            int missing = 0;
            for (int i = snags.Count - 1; i >= 0; i--)
            {
                WalkSnag snag = snags[i];
                bool noComment = string.IsNullOrWhiteSpace(snag.Comment);
                if (noComment)
                    missing++;

                var line1 = new TextBlock { Text = snag.Number + "    " + snag.Summary, TextTrimming = TextTrimming.CharacterEllipsis };
                line1.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
                var line2 = new TextBlock
                {
                    Text = noComment ? "No comment yet" : snag.Comment,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontSize = 11.5,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                line2.SetResourceReference(TextBlock.ForegroundProperty, noComment ? "AccentWarning" : "TextSecondary");
                var content = new StackPanel();
                content.Children.Add(line1);
                content.Children.Add(line2);

                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                Button open = SmallButton("Open", "Go back to snag " + snag.Number + " and add the element to Revit's selection",
                                          snag.Number, OnSnagRowClick);
                Button saveView = SmallButton("Save as 3D view", "Save a Revit 3D view looking at snag " + snag.Number +
                                              " - for the issue meeting or a sheet (one Undo step)", snag.Number, OnSnagSaveViewClick);
                DockPanel.SetDock(open, Dock.Right);
                DockPanel.SetDock(saveView, Dock.Right);
                row.Children.Add(open);
                row.Children.Add(saveView);
                row.Children.Add(RowButton(content, (noComment ? "Write" : "Change") + " the comment of snag " + snag.Number,
                                           snag.Number, OnSnagCommentRowClick));
                SnagMenuList.Children.Add(row);
            }
            SnagSection.Visibility = snags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SnagMissingText.Text = missing == 0 ? "" : missing == 1 ? "1 with no comment yet" : missing + " with no comment yet";

            RebuildViewpointMenu();
        }

        // ------------------------------------------------------------------ snag comments (pause menu)

        private void OnSnagCommentRowClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is int number))
                return;
            OpenSnagEditor(number);
        }

        /// <summary>Opens the one comment box below the list for this snag (what was typed for another snag is saved first).</summary>
        private void OpenSnagEditor(int number)
        {
            WalkSnag snag = _session.Report.Find(number);
            if (snag == null || !_pauseMenuShown)
                return;
            if (_editingSnag != 0 && _editingSnag != number)
                CloseSnagEditor(save: true);
            _editingSnag = number;
            SnagEditTitle.Text = "Snag " + number + " - " + snag.Summary;
            SnagCommentBox.Text = snag.Comment ?? "";
            SnagEditCard.Visibility = Visibility.Visible;
            ShrinkToPill();
            // The overlay may not be the active window (Revit was used while paused): take the keys back for typing.
            Activate();
            SnagCommentBox.Focus();
            Keyboard.Focus(SnagCommentBox);
            SnagCommentBox.CaretIndex = SnagCommentBox.Text.Length;
        }

        /// <summary>
        /// Closes the comment box. <paramref name="save"/> writes what was typed (an emptied box clears the comment) and
        /// rewrites Snags.csv; false (Esc) drops it. Returns the number saved, or 0.
        /// </summary>
        private int CloseSnagEditor(bool save)
        {
            int number = _editingSnag;
            if (number == 0)
                return 0;
            _editingSnag = 0;
            string text = (SnagCommentBox.Text ?? "").Trim();
            SnagEditCard.Visibility = Visibility.Collapsed;
            if (SnagCommentBox.IsKeyboardFocusWithin)
                Keyboard.Focus(this);

            WalkSnag snag = _session.Report.Find(number);
            if (!save || snag == null || text == (snag.Comment ?? "").Trim())
            {
                if (_pauseMenuShown)
                    ShrinkToPill();
                return 0;
            }
            snag.Comment = text;
            string problem = _session.Report.Save();
            if (_pauseMenuShown)
            {
                SetPauseStatus(problem ?? (text.Length == 0 ? "Snag " + number + ": comment removed." : "Snag " + number + ": comment saved."));
                RebuildPauseLists();
                ShrinkToPill();
            }
            else if (problem != null)
            {
                _session.Hud.Toast(problem, WalkToastKind.Refused);
            }
            return number;
        }

        /// <summary>Save, then straight on to the next snag (in list order, newest first) that still has no comment.</summary>
        private void SaveSnagCommentAndGoOn()
        {
            int from = _editingSnag;
            CloseSnagEditor(save: true);
            IReadOnlyList<WalkSnag> snags = _session.Report.Snags;
            int start = snags.Count - 1;
            for (int i = snags.Count - 1; i >= 0; i--)
            {
                if (snags[i].Number == from)
                {
                    start = i - 1;
                    break;
                }
            }
            for (int step = 0; step < snags.Count; step++)
            {
                int i = ((start - step) % snags.Count + snags.Count) % snags.Count;
                if (snags[i].Number != from && string.IsNullOrWhiteSpace(snags[i].Comment))
                {
                    OpenSnagEditor(snags[i].Number);
                    return;
                }
            }
        }

        private void OnSnagCommentSaveClick(object sender, RoutedEventArgs e) => Guard(SaveSnagCommentAndGoOn);

        /// <summary>
        /// Keys while paused and the comment box has them. Returns true when the key belongs to the box: Enter saves,
        /// Esc closes the box only, every other key (and its repeat) types - none of them is a walk or menu key here.
        /// </summary>
        private bool PauseCommentKeyDown(KeyEventArgs e, Key key)
        {
            if (_editingSnag == 0 || !SnagCommentBox.IsKeyboardFocusWithin)
                return false;
            if (key == Key.Enter)
            {
                if (!e.IsRepeat)
                    SaveSnagCommentAndGoOn();
                e.Handled = true;
            }
            else if (key == Key.Escape)
            {
                if (!e.IsRepeat)
                    CloseSnagEditor(save: false);
                e.Handled = true;
            }
            return true;
        }

        private Button RowButton(string text, string tip, object tag, RoutedEventHandler click) =>
            RowButton(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis }, tip, tag, click);

        private Button RowButton(UIElement content, string tip, object tag, RoutedEventHandler click)
        {
            var button = new Button
            {
                Content = content,
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

        private void OnSnagSaveViewClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is int number))
                return;
            _session.Input.SaveSnagAsView = number;
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
