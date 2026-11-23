// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.3.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// What one Auto Dimension run made, what it left out and why - the text of the report a modeller sees when
// something was left out (a run that did everything stays quiet).

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HeronTools.Services.AutoDimension
{
    internal sealed class AutoDimensionReport
    {
        private readonly Dictionary<string, int> _leftOut = new Dictionary<string, int>();
        private readonly List<string> _leftOutOrder = new List<string>();
        private readonly List<string> _notes = new List<string>();

        /// <summary>Dimension rows placed.</summary>
        internal int RowsMade { get; private set; }

        /// <summary>Rows that were already in the view, so were not made again.</summary>
        internal int RowsAlreadyThere { get; private set; }

        internal bool HasLeftOut => _leftOut.Count > 0;

        internal void RowMade() => RowsMade++;

        internal void RowAlreadyThere() => RowsAlreadyThere++;

        /// <summary>Something the modeller might expect that was not dimensioned. Counted per reason.</summary>
        internal void LeftOut(string reason)
        {
            if (_leftOut.ContainsKey(reason))
            {
                _leftOut[reason]++;
                return;
            }

            _leftOut[reason] = 1;
            _leftOutOrder.Add(reason);
        }

        /// <summary>Information only: the settings working as asked. Never makes the report appear by itself.</summary>
        internal void Note(string text)
        {
            if (!_notes.Contains(text))
                _notes.Add(text);
        }

        internal string Summary()
        {
            var text = new StringBuilder();
            if (RowsMade > 0)
                text.Append(RowsMade == 1 ? "1 dimension row was placed." : RowsMade + " dimension rows were placed.");
            else
                text.Append("No dimension rows were placed, so nothing was changed.");

            if (RowsAlreadyThere > 0)
                text.Append(RowsAlreadyThere == 1
                    ? " 1 row was already in the view and was not made again."
                    : " " + RowsAlreadyThere + " rows were already in the view and were not made again.");

            if (HasLeftOut)
                text.Append(" Some were left out - see the details.");
            return text.ToString();
        }

        internal string Details()
        {
            var text = new StringBuilder();
            if (HasLeftOut)
            {
                text.AppendLine("Left out:");
                foreach (string reason in _leftOutOrder)
                    text.AppendLine("  - " + reason + (_leftOut[reason] > 1 ? " (" + _leftOut[reason] + ")" : string.Empty));
            }

            if (_notes.Count > 0)
            {
                if (text.Length > 0)
                    text.AppendLine();
                text.AppendLine("Notes:");
                foreach (string note in _notes)
                    text.AppendLine("  - " + note);
            }

            return text.ToString().TrimEnd();
        }

        internal bool HasDetails => HasLeftOut || _notes.Any();
    }
}
