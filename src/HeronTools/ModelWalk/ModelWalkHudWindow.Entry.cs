// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk HUD - the small box for typing on the spot: a snag's comment (right after its picture, only when the
// setting "Ask for a comment at each snag" is on - quick snagging writes comments in the pause menu) and a viewpoint's
// name. Enter saves, Esc skips, a left click saves. While it is open the walk stands still: the keys type into the
// box, the mouse does not turn the view. Pausing (or the walk ending) keeps what was typed. No Revit API here.

using System.Windows;
using System.Windows.Input;
using HeronTools.Services.ModelWalk;

namespace HeronTools.UI.ModelWalk
{
    public partial class ModelWalkHudWindow
    {
        private enum EntryKind { None, SnagComment, ViewpointName }

        private EntryKind _entryKind = EntryKind.None;
        private int _entryNumber;
        private string _entryDefault = "";

        private bool EntryOpen => _entryKind != EntryKind.None;

        private void OpenEntry(EntryKind kind, int number, string title, string text, string hint)
        {
            CloseEntry(save: true);
            ClearHeldKeys();
            _entryKind = kind;
            _entryNumber = number;
            _entryDefault = text ?? "";
            EntryTitle.Text = title;
            EntryHint.Text = hint;
            EntryBox.Text = _entryDefault;
            EntryCard.Visibility = Visibility.Visible;
            EntryBox.Focus();
            Keyboard.Focus(EntryBox);
            EntryBox.SelectAll();
        }

        /// <summary>Closes the box. <paramref name="save"/> false (Esc) keeps the snag without a comment, or the viewpoint's own name.</summary>
        private void CloseEntry(bool save)
        {
            if (!EntryOpen)
                return;
            EntryKind kind = _entryKind;
            int number = _entryNumber;
            string text = (EntryBox.Text ?? "").Trim();
            _entryKind = EntryKind.None;
            EntryCard.Visibility = Visibility.Collapsed;
            if (!_paused && !_ending)
                Keyboard.Focus(this);

            switch (kind)
            {
                case EntryKind.SnagComment:
                {
                    WalkSnag snag = _session.Report.Find(number);
                    if (snag == null || !save || text.Length == 0)
                        return;
                    snag.Comment = text;
                    string problem = _session.Report.Save();
                    _session.Hud.Toast(problem ?? "Snag " + number + " comment saved", problem == null ? WalkToastKind.Done : WalkToastKind.Refused);
                    return;
                }
                case EntryKind.ViewpointName:
                    FinishViewpointName(number, save && text.Length > 0 ? text : _entryDefault);
                    return;
            }
        }

        /// <summary>Keys while the box is open. Returns true when the key was for the box (the walk ignores it).</summary>
        private bool EntryKeyDown(KeyEventArgs e, Key key)
        {
            if (!EntryOpen)
                return false;
            if (key == Key.Enter)
            {
                CloseEntry(save: true);
                e.Handled = true;
            }
            else if (key == Key.Escape)
            {
                CloseEntry(save: false);
                e.Handled = true;
            }
            else if (key == Key.Tab)
            {
                e.Handled = true;   // focus stays in the box
            }
            return true;            // every other key types into the box
        }

        /// <summary>
        /// A snag was recorded: its picture first (HUD hidden), then the comment box - or, with quick snagging (the
        /// standard), nothing more: the walk carries on and the comment is written in the pause menu.
        /// </summary>
        private void FinishSnagRecord(int number)
        {
            WalkSnag snag = _session.Report.Find(number);
            if (snag == null)
                return;
            string fileName = "Snag " + number.ToString("000") + ".png";
            CapturePicture(fileName,
                saved =>
                {
                    snag.PictureFile = saved;
                    string problem = _session.Report.Save();
                    if (problem != null)
                        _session.Hud.Toast(problem, WalkToastKind.Refused);
                    AskSnagComment(snag);
                },
                failed =>
                {
                    _session.Hud.Toast("Snag " + number + " picture not saved (" + failed + ")", WalkToastKind.Refused);
                    AskSnagComment(snag);
                });
        }

        private void AskViewpointName(int number)
        {
            WalkViewpoint vp = _session.Viewpoints.Find(v => v.Number == number);
            if (vp == null || _paused || _ending || _closedDown)
                return;
            OpenEntry(EntryKind.ViewpointName, number, "Name viewpoint " + number, vp.Name,
                "Type a name  -  Enter saves  -  Esc keeps this one");
        }

        private void FinishViewpointName(int number, string name)
        {
            WalkViewpoint vp = _session.Viewpoints.Find(v => v.Number == number);
            name = WalkViewpointStore.CleanName(name);
            if (vp == null || vp.Name == name)
                return;
            vp.Name = name;
            string problem = _session.SaveViewpoints();
            _session.Hud.Toast(problem ?? "Viewpoint " + number + ": " + name, problem == null ? WalkToastKind.Done : WalkToastKind.Refused);
        }

        private void AskSnagComment(WalkSnag snag)
        {
            if (_paused || _ending || _closedDown || !_session.Settings.AskSnagComment)
                return;
            OpenEntry(EntryKind.SnagComment, snag.Number, "Snag " + snag.Number + " - " + snag.Summary, "",
                "Type a comment  -  Enter saves  -  Esc skips");
        }
    }
}
