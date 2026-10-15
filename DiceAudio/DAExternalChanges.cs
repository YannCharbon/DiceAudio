/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Diagnostics;
using DiceAudio.Storage;
using Texts = DiceAudio.Resources.Localization.Portability;

namespace DiceAudio
{
    /// <summary>
    /// The use of one data folder from several computers (spec §3.4, A9), on
    /// Windows: the warnings at startup (conflict copies, files only in the
    /// cloud), the banner when a data file changes elsewhere, and the question
    /// when a save finds its file changed on disk. Nothing is ever reloaded
    /// without the user asking; a reload first stops the playback it affects.
    /// </summary>
    public sealed class DAExternalChanges : IDisposable
    {
        /// <summary>The files a playing scenario reads from: reloading one of them stops playback first.</summary>
        private static readonly string[] PlaybackFiles =
        {
            DiceAudioService.AudioItemsFileName, DiceAudioService.ScenarioGroupsFileName,
            DiceAudioService.ScenesFileName, DiceAudioService.PresetsFileName,
        };

        private readonly DiceAudioService _service;
        private readonly DataLocation _location;
        private readonly AppSettings _settings;
        private readonly DAPlaybackCoordinator _playback;
        private readonly DAStorageNotices _notices;
        private readonly DataFolderWatcher _watcher;
        private readonly SafeFileStore _store;
        private readonly object _gate = new();
        private readonly List<string> _conflicts = new();

        public DAExternalChanges(DiceAudioService service, SafeFileStore store, DataLocation location, AppSettings settings,
                                 DAPlaybackCoordinator playback, DAStorageNotices notices)
        {
            _service = service;
            _store = store;
            _location = location;
            _settings = settings;
            _playback = playback;
            _notices = notices;
            _watcher = new DataFolderWatcher(store, WatchedFiles);
            _watcher.ChangedElsewhere += _ => Changed?.Invoke();
            _service.ChangedOnDisk += OnChangedOnDisk;
            DAAudioTagsProvider.Instance.ChangedOnDisk += OnChangedOnDisk;
        }

        /// <summary>The data files and settings.json: what a reload reads.</summary>
        public static IReadOnlyList<string> WatchedFiles => BackupService.DataFileNames;

        /// <summary>Raised when the banner or the question must show or go.</summary>
        public event Action? Changed;

        /// <summary>Files changed on another computer since they were read: the banner.</summary>
        public IReadOnlyList<string> ChangedElsewhere => _watcher.Changed;

        /// <summary>The first file a save did not write because it changed on disk: the question.</summary>
        public string? PendingConflict
        {
            get { lock (_gate) return _conflicts.FirstOrDefault(); }
        }

        /// <summary>
        /// At startup, before the data is read: conflict copies, data files only
        /// in the cloud (spec §5.7) and the versions kept by a close that could
        /// not save are reported. Windows only.
        /// </summary>
        public void ReportCloudProblems()
        {
            if (!OperatingSystem.IsWindows()) return;
            var names = BackupService.DataFileNames;
            var copies = CloudFiles.FindConflictCopies(_location.Root, names);
            if (copies.Count > 0) _notices.Report(Texts.NoticeCloudCopiesTitle, string.Format(Texts.CloudConflictCopies, string.Join(", ", copies)));
            var cloudOnly = CloudFiles.FindCloudOnly(_location.Root, names);
            if (cloudOnly.Count > 0) _notices.Report(Texts.NoticeCloudOnlyTitle, string.Format(Texts.CloudOnlyWarning, string.Join(", ", cloudOnly)));
            var unsaved = UnsavedCopies.Find(_location.Root, names);
            if (unsaved.Count > 0) _notices.Report(Texts.NoticeUnsavedTitle, string.Format(Texts.UnsavedCopiesManual, string.Join(", ", unsaved)));
        }

        /// <summary>
        /// When the app closes, after the last saves: the user's version of each
        /// file a save could not write (changed on another computer, question
        /// left open or refused at close) is kept next to it. Never throws.
        /// </summary>
        public void KeepUnsavedVersions()
        {
            var written = _store.WriteUnsavedCopies(_location.MachineName, DateTime.Now);
            if (written.Count > 0) Debug.WriteLine($"Unsaved versions kept: {string.Join(", ", written)}");
        }

        /// <summary>Watches the data folder, once the data is read. Windows only.</summary>
        public void StartWatching()
        {
            if (OperatingSystem.IsWindows()) _watcher.Start(_location.Root);
        }

        public void StopWatching() => _watcher.Stop();

        /// <summary>While the app replaces its own files (restore, move), nothing is reported. Dispose to watch again.</summary>
        public IDisposable PauseWatching() => _watcher.Pause();

        /// <summary>
        /// "Reload": the files changed elsewhere and the one of the question,
        /// read again. Playback that reads from them stops first; the pages are
        /// rebuilt afterwards. What was not saved in those files is lost, which
        /// is what the user chose.
        /// </summary>
        public async Task ReloadAsync()
        {
            List<string> files;
            lock (_gate)
            {
                files = _watcher.Changed.Concat(_conflicts).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                _conflicts.Clear();
            }
            if (files.Count == 0) return;

            if (files.Any(f => PlaybackFiles.Contains(f, StringComparer.OrdinalIgnoreCase)))
            {
                await _playback.StopAllAsync();
            }

            using (_watcher.Pause())
            {
                await _service.ReloadFilesAsync(files);
                if (files.Contains(BackupService.SettingsFileName, StringComparer.OrdinalIgnoreCase))
                {
                    await _settings.ReloadAsync();
                }
                _service.RefreshLocalAvailability();
            }
            _watcher.Clear(files);
            _notices.ReportLoadFailures();
            Changed?.Invoke();
            (Application.Current as App)?.ReloadPages();
        }

        /// <summary>"Overwrite": the file of the question is saved over the version changed elsewhere.</summary>
        public async Task OverwriteAsync()
        {
            string? file;
            lock (_gate)
            {
                file = _conflicts.FirstOrDefault();
                if (file != null) _conflicts.RemoveAt(0);
            }
            if (file == null) return;
            try
            {
                await _service.OverwriteFileAsync(file);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Overwriting {file} failed: {ex.Message}");
            }
            _watcher.Clear(new[] { file });
            Changed?.Invoke();
        }

        public void Dispose()
        {
            _service.ChangedOnDisk -= OnChangedOnDisk;
            DAAudioTagsProvider.Instance.ChangedOnDisk -= OnChangedOnDisk;
            _watcher.Dispose();
        }

        private void OnChangedOnDisk(string fileName)
        {
            lock (_gate)
            {
                if (_conflicts.Contains(fileName, StringComparer.OrdinalIgnoreCase)) return;
                _conflicts.Add(fileName);
            }
            Changed?.Invoke();
        }
    }
}
