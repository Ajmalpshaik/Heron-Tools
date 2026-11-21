// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.1.1
// Heron-Layer:  revit
// See docs/metadata-standard.md

// One memory per open model, keyed on the Revit Document itself - not its file path or title. Save As keeps
// the same Document, so the memory follows it; two unsaved models with the same title never share one; a
// closed model's memory is dropped.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace HeronTools.Utils
{
    /// <summary>
    /// A value per open Revit model, for this Revit session. Revit says two Document objects are Equal when
    /// they are the same open model, so the Document is the key. Closed models are dropped (IsValidObject is
    /// false) before every lookup, so a closed model is never compared or kept alive.
    /// </summary>
    internal sealed class PerDocumentMemory<T> where T : class
    {
        private readonly List<KeyValuePair<Document, T>> _items = new List<KeyValuePair<Document, T>>();

        /// <summary>The value kept for this model, or null.</summary>
        internal T Get(Document doc)
        {
            if (doc == null)
                return null;
            DropClosed();
            foreach (KeyValuePair<Document, T> item in _items)
            {
                if (item.Key.Equals(doc))
                    return item.Value;
            }
            return null;
        }

        /// <summary>The value kept for this model, made with <paramref name="create"/> if there is none yet.</summary>
        internal T GetOrAdd(Document doc, Func<T> create)
        {
            T value = Get(doc);
            if (value == null)
            {
                value = create();
                _items.Add(new KeyValuePair<Document, T>(doc, value));
            }
            return value;
        }

        /// <summary>Replaces the value kept for this model.</summary>
        internal void Set(Document doc, T value)
        {
            Remove(doc);
            if (value != null)
                _items.Add(new KeyValuePair<Document, T>(doc, value));
        }

        internal void Remove(Document doc)
        {
            if (doc == null)
                return;
            DropClosed();
            _items.RemoveAll(item => item.Key.Equals(doc));
        }

        private void DropClosed() => _items.RemoveAll(item => !item.Key.IsValidObject);
    }
}
