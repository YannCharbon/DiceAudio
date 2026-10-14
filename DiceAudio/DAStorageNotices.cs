/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using Texts = DiceAudio.Resources.Localization.Portability;
using DiceAudio.Storage;

namespace DiceAudio
{
    /// <summary>
    /// The messages about the data files the user must read: a file that could
    /// not be read, a save that failed, how a restore ended. Kept outside the
    /// pages, so that a message raised while no page shows it — or just before
    /// the pages are rebuilt after a restore — is still there. The main layout
    /// shows them in one dialog.
    /// </summary>
    public sealed class DAStorageNotices
    {
        private readonly SafeFileStore _store;
        private readonly object _gate = new();
        private readonly List<string> _pending = new();
        private readonly HashSet<string> _reportedLoadFailures = new(StringComparer.OrdinalIgnoreCase);

        public DAStorageNotices(SafeFileStore store)
        {
            _store = store;
            _store.SaveFailed += (fileName, _) => Add(string.Format(Texts.ErrorSaveFailed, fileName));
        }

        /// <summary>Raised on any thread when a message is added or the list is cleared.</summary>
        public event Action? Changed;

        public IReadOnlyList<string> Pending { get { lock (_gate) return _pending.ToList(); } }

        /// <summary>Tells about each blocked file not told about yet in this session.</summary>
        public void ReportLoadFailures()
        {
            var lines = new List<string>();
            lock (_gate)
            {
                foreach (var file in _store.BlockedFiles)
                    if (_reportedLoadFailures.Add(file)) lines.Add(string.Format(Texts.ErrorLoadFailed, file));
            }
            foreach (var line in lines) Add(line);
        }

        public void ReportRestoreDone(IReadOnlyList<string> itemsWithoutFile)
        {
            Add(Texts.RestoreDone);
            if (itemsWithoutFile.Count == 0) return;

            const int Shown = 30;
            string list = string.Join(", ", itemsWithoutFile.Take(Shown));
            if (itemsWithoutFile.Count > Shown) list += " " + string.Format(Texts.RestoreMissingMore, itemsWithoutFile.Count - Shown);
            Add(string.Format(Texts.RestoreMissingFiles, itemsWithoutFile.Count) + " " + list);
        }

        public void ReportRestoreFailed(string detail) => Add(string.Format(Texts.RestoreFailed, detail));

        public void ReportRollbackFailed(string safetyBackupFileName) =>
            Add(string.Format(Texts.RestoreRollbackFailed, safetyBackupFileName));

        public void Report(string message) => Add(message);

        public void DismissAll()
        {
            lock (_gate) _pending.Clear();
            Changed?.Invoke();
        }

        private void Add(string line)
        {
            lock (_gate)
            {
                if (_pending.Contains(line)) return;
                _pending.Add(line);
            }
            Changed?.Invoke();
        }
    }
}
