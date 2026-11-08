// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Generic per-document settings tracker shared by View Crop tools.

using System;
using Autodesk.Revit.DB;

namespace HeronTools.Services.ViewCrop
{
    /// <summary>
    /// Generic per-document settings tracker. Each derived type sharing the same T
    /// closure shares the same static cache - which matches the per-tool singleton
    /// pattern used by the View Crop trackers.
    /// </summary>
    internal abstract class SettingsTrackerBase<T> where T : class
    {
        private static T _lastSettings;
        private static string _lastDocKey;

        protected SettingsTrackerBase(Document doc)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            string key = BuildDocKey(doc);
            if (!string.Equals(_lastDocKey, key, StringComparison.OrdinalIgnoreCase))
            {
                _lastDocKey = key;
                _lastSettings = null;
            }
        }

        /// <summary>
        /// Returns a clone of the last-used settings. Falls back to <see cref="LoadFromStore"/>
        /// (which defaults to <see cref="CreateDefault"/>) when no cache exists.
        /// </summary>
        internal T LastSettings
        {
            get
            {
                if (_lastSettings == null)
                    _lastSettings = LoadFromStore();

                return CloneSettings(_lastSettings);
            }
        }

        /// <summary>
        /// Updates the cache (and optional disk store) with a clone of the provided settings.
        /// Null inputs are ignored.
        /// </summary>
        /// <returns>
        /// False only when the store could not keep the settings; <paramref name="errorMessage"/> says why.
        /// The in-memory cache is updated either way.
        /// </returns>
        internal bool Save(T settings, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (settings == null)
                return true;

            _lastSettings = CloneSettings(settings);
            return SaveToStore(_lastSettings, out errorMessage);
        }

        protected abstract T CreateDefault();

        protected abstract T CloneSettings(T settings);

        protected virtual T LoadFromStore() => CreateDefault();

        protected virtual bool SaveToStore(T settings, out string errorMessage)
        {
            // No-op by default (in-memory only), so it cannot fail. Override to persist to disk.
            errorMessage = string.Empty;
            return true;
        }

        private static string BuildDocKey(Document doc)
        {
            if (!string.IsNullOrWhiteSpace(doc.PathName))
                return doc.PathName;

            return $"{doc.Title}|{doc.GetHashCode()}";
        }
    }
}
