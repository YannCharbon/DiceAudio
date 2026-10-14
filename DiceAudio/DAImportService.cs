/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Diagnostics;
using DiceAudio.Portability;
using DiceAudio.Storage;
using Texts = DiceAudio.Resources.Localization.Portability;

namespace DiceAudio
{
    /// <summary>The library as the import writes into it: the service's lists and the tag lists.</summary>
    public sealed class DAImportTarget : IDAImportTarget
    {
        private readonly DiceAudioService _service;
        public DAImportTarget(DiceAudioService service) => _service = service;

        public List<DAAudioItem> AudioItems => _service.AudioItems;
        public List<DAVirtualAudioFolder> Folders => _service.AudioVirtualFolders;
        public List<DAScene> Scenes => _service.Scenes;
        public List<DAScenarioGroup> ScenarioGroups => _service.ScenarioGroups;
        public List<DAPreset> Presets => _service.Presets;
        public List<string> TypeTags => DAAudioTagsProvider.Instance.CategoryTypes;
        public List<string> LocationTags => DAAudioTagsProvider.Instance.CategoryLocations;
        public List<string> ExpressionTags => DAAudioTagsProvider.Instance.CategoryExpressions;
        public void AddAudioItem(DAAudioItem item) => _service.AddAudioItem(item);
    }

    /// <summary>A .dicea file opened for import, with its audio file pack when there is one.</summary>
    public sealed class DAImportSession : IDisposable
    {
        public required DABundle Bundle { get; init; }
        public required string FileName { get; init; }
        public string? FullPath { get; init; }
        public int BrokenReferences { get; init; }
        public DAMediaPackReader? Pack { get; set; }
        public Stream? PackStream { get; set; }
        public string? PackProblem { get; set; }
        public ConflictPlanner? Planner { get; set; }
        public DAImportPlan? Plan { get; set; }

        /// <summary>
        /// A merge with another data folder: its Audio/ holds the files of the
        /// imported items, under their original names. Those items keep them
        /// (the move then merges the audio folders), and nothing is downloaded.
        /// </summary>
        public string? MergeAudioDir { get; init; }

        public void Dispose()
        {
            Pack?.Dispose();
            PackStream?.Dispose();
        }
    }

    /// <summary>
    /// The import (spec §3.2), from the app's side: the file, the plan, the
    /// apply with its safety net (a before-import backup, playback and
    /// downloads stopped, every save awaited, the library put back on
    /// failure), then the follow-up steps for the audio files. The import
    /// wizard in the main layout drives it.
    /// </summary>
    public sealed class DAImportService
    {
        private static readonly TimeSpan DownloadStopTimeout = TimeSpan.FromSeconds(5);

        private readonly DiceAudioService _service;
        private readonly IFileHandler _files;
        private readonly BackupService _backups;
        private readonly DAPlaybackCoordinator _playback;
        private readonly DADownloadQueue _downloads;

        public DAImportService(DiceAudioService service, IFileHandler files, BackupService backups,
                               DAPlaybackCoordinator playback, DADownloadQueue downloads)
        {
            _service = service;
            _files = files;
            _backups = backups;
            _playback = playback;
            _downloads = downloads;
        }

        /// <summary>Raised when a page asks for an import; the wizard opens.</summary>
        public event Action? Requested;

        public void RequestImport() => Requested?.Invoke();

        /// <summary>Raised to import a bundle that is not a file (a merge with another data folder); the wizard opens on it.</summary>
        public event Action<DAImportSession, TaskCompletionSource<bool>>? BundleRequested;

        /// <summary>
        /// Opens the wizard on a bundle (the library of another data folder),
        /// and completes when it closes: true when the import was applied.
        /// </summary>
        public Task<bool> ImportBundleAsync(DABundle bundle, string? mergeAudioDir)
        {
            var listener = BundleRequested;
            if (listener == null) return Task.FromResult(false);
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            listener(new DAImportSession
            {
                Bundle = bundle, FileName = "", BrokenReferences = DABundleReader.CountBrokenReferences(bundle), MergeAudioDir = mergeAudioDir,
            }, completion);
            return completion.Task;
        }

        /// <summary>
        /// Asks for a .dicea file and reads it. Null when the user cancels;
        /// otherwise a session, or an error message ready to show.
        /// </summary>
        public async Task<(DAImportSession? Session, string? Error, bool Cancelled)> PickAndReadAsync()
        {
            DAPickedFile? picked;
            try
            {
                picked = await _files.PickFileAsync(DABundle.FileExtension, Texts.ExportFileType);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Picking the file failed: {ex}");
                return (null, Texts.ErrorUnreadableFile, false);
            }
            if (picked == null) return (null, null, true);

            await using (picked)
            {
                DABundleReadResult read;
                try
                {
                    read = await DABundleReader.ReadAsync(picked.Stream);
                }
                catch (IOException ex)
                {
                    Debug.WriteLine($"Reading {picked.FileName} failed: {ex.Message}");
                    return (null, Texts.ErrorUnreadableFile, false);
                }

                string? error = read.Error switch
                {
                    DABundleError.None => null,
                    DABundleError.OtherAppFile => Texts.ErrorOtherAppFile,
                    DABundleError.NewerFormat => Texts.ErrorNewerFormat,
                    DABundleError.FileTooLarge => string.Format(Texts.ErrorFileTooLarge, read.Detail, DABundleReader.MaxBytes / (1024 * 1024)),
                    _ => Texts.ErrorUnreadableFile,
                };
                if (error != null) return (null, error, false);

                var session = new DAImportSession
                {
                    Bundle = read.Bundle!, FileName = picked.FileName, FullPath = picked.FullPath, BrokenReferences = read.BrokenReferences,
                };

                // The pack is looked for next to the file, under the name the export gave it.
                if (session.Bundle.MediaPack is { FileName: { Length: > 0 } packName } && picked.FullPath != null)
                {
                    string candidate = Path.Combine(Path.GetDirectoryName(picked.FullPath) ?? "", Path.GetFileName(packName));
                    if (File.Exists(candidate)) OpenPack(session, File.OpenRead(candidate));
                }
                return (session, null, false);
            }
        }

        /// <summary>Asks for the .diceamedia file of the session, when it was not found next to the .dicea.</summary>
        public async Task<bool> PickPackAsync(DAImportSession session)
        {
            var picked = await _files.PickFileAsync(DAMediaManifest.FileExtension, Texts.MediaPackFileType);
            if (picked == null) return false;
            // The reader keeps the stream open until the session ends.
            OpenPack(session, picked.Stream);
            return session.Pack != null;
        }

        private static void OpenPack(DAImportSession session, Stream stream)
        {
            var (reader, error) = DAMediaPackReader.Open(stream, session.Bundle, DAImportHelper.AudioExtensions);
            if (reader == null)
            {
                stream.Dispose();
                session.PackProblem = error == DAMediaPackError.Mismatch ? Texts.MediaPackMismatch : Texts.MediaPackUnreadable;
                return;
            }
            session.Pack?.Dispose();
            session.PackStream?.Dispose();
            session.Pack = reader;
            session.PackStream = stream;
            session.PackProblem = null;
        }

        /// <summary>Compares the file with the library (writes nothing). Call on the UI thread.</summary>
        public void Analyse(DAImportSession session)
        {
            var snapshot = new DALibrarySnapshot
            {
                AudioItems = _service.AudioItems.ToList(),
                Folders = _service.AudioVirtualFolders.ToList(),
                Scenes = _service.Scenes.ToList(),
                ScenarioGroups = _service.ScenarioGroups.ToList(),
                Presets = _service.Presets.ToList(),
                TypeTags = DAAudioTagsProvider.Instance.CategoryTypes.ToList(),
                LocationTags = DAAudioTagsProvider.Instance.CategoryLocations.ToList(),
                ExpressionTags = DAAudioTagsProvider.Instance.CategoryExpressions.ToList(),
            };
            session.Planner = new ConflictPlanner(snapshot);
            session.Plan = session.Planner.Analyse(session.Bundle);
        }

        /// <summary>
        /// Whether importing a bundle would change anything with the default
        /// choices (a merge with a folder that holds nothing new asks instead).
        /// Writes nothing. Call on the UI thread.
        /// </summary>
        public bool HasSomethingToAdd(DABundle bundle)
        {
            using var session = new DAImportSession { Bundle = bundle, FileName = "" };
            Analyse(session);
            return session.Plan!.AllItems.Any(i => i.Inserts);
        }

        /// <summary>
        /// Applies the plan. Call on the UI thread: the lists change there.
        /// </summary>
        /// <exception cref="DAImportBlockedException">A data file could not be read at startup; nothing has been changed.</exception>
        /// <exception cref="DAImportFailedException">The library is as it was.</exception>
        /// <exception cref="DAImportRollbackFailedException">The library could not be saved back; the before-import backup holds it.</exception>
        public Task<DAImportResult> ApplyAsync(DAImportSession session)
        {
            if (session.Plan == null || session.Planner == null) throw new InvalidOperationException("Analyse first.");
            if (!_service.CanWriteData) throw new InvalidOperationException(Texts.DataFolderUnusable);

            return DAImportRunner.RunAsync(session.Plan, session.Bundle, new DAImportTarget(_service),
                blockedFiles: _service.Store.BlockedFiles,
                beforeApply: async () =>
                {
                    // Nothing may play from, download into or save the library meanwhile.
                    _downloads.CancelAll();
                    var idle = _downloads.WhenIdleAsync();
                    await Task.WhenAny(idle, Task.Delay(DownloadStopTimeout));
                    await _playback.StopAllAsync();
                    var backup = await _backups.CreateBackupAsync(BackupReason.BeforeImport);
                    return backup.FileName;
                },
                saveAll: () => _service.SaveAllOrThrowAsync(),
                localFolderIds: _service.AudioVirtualFolders.Select(f => f.Id).ToHashSet());
        }

        /// <summary>
        /// The follow-up steps (pipeline step 5): files from the pack, then
        /// files already on this computer, then downloads for items with a
        /// link; the rest is listed for "Pick file…". Saves the items.
        /// </summary>
        public async Task<DAFollowUpReport> FollowUpAsync(DAImportSession session, DAImportResult result)
        {
            var report = new DAFollowUpReport();
            string audioDir = _service.AudioDir;
            var needed = DAImportFollowUp.ItemsNeedingFiles(session.Plan!, result, audioDir);

            if (session.Pack != null)
            {
                try
                {
                    await DAImportFollowUp.ExtractPackAsync(session.Pack, needed, audioDir, report);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    Debug.WriteLine($"Extracting the audio file pack failed: {ex}");
                }
            }

            if (session.MergeAudioDir is string mergeAudio)
            {
                // The files are in the other folder's Audio/, which the move
                // merges with this one: the items keep their names.
                foreach (var (item, record, _) in needed.ToList())
                {
                    string? name = string.IsNullOrEmpty(record.OriginalFileName) ? null : Path.GetFileName(record.OriginalFileName);
                    if (name == null || !File.Exists(Path.Combine(mergeAudio, name))) continue;
                    item.LocalFileName = name;
                    report.Matched++;
                    needed.RemoveAll(n => ReferenceEquals(n.Item, item));
                }
            }

            DAImportFollowUp.MatchExistingFiles(needed, _service.AudioItems, audioDir, DAImportHelper.AudioExtensions, report);

            // A merge moves the data right after: nothing is downloaded meanwhile.
            var withLink = needed.Where(n => !string.IsNullOrWhiteSpace(n.Item.SourceURL)).Select(n => n.Item).ToList();
            if (session.MergeAudioDir == null) report.Queued = _downloads.EnqueueLibraryItems(withLink);
            var stillMissing = needed.Where(n => string.IsNullOrWhiteSpace(n.Item.SourceURL));
            if (session.MergeAudioDir is string otherAudio)
            {
                // A merge lists only what it makes missing: an item whose file
                // was not in the other folder either was missing before.
                stillMissing = stillMissing.Where(n => !string.IsNullOrEmpty(n.Record.OriginalFileName)
                                                       && File.Exists(Path.Combine(otherAudio, Path.GetFileName(n.Record.OriginalFileName))));
            }
            report.Missing.AddRange(stillMissing.Select(n => n.Item));

            try { await _service.SaveAudioItemListAsync(); }
            catch (Exception ex) { Debug.WriteLine($"Saving after the follow-up failed: {ex.Message}"); }
            return report;
        }

        /// <summary>"Pick file…": copies a file the user picks into the audio folder, under a new name, for this item.</summary>
        public async Task<bool> PickFileForAsync(DAAudioItem item)
        {
            var paths = await DAImportHelper.PickFilesAsync();
            string? source = paths.FirstOrDefault();
            if (source == null) return false;

            string audioDir = _service.AudioDir;
            string name = DAImportHelper.GetUniqueLibraryFileName(audioDir, Path.GetFileName(source));
            await Task.Run(() => File.Copy(source, Path.Combine(audioDir, name), overwrite: false));
            item.LocalFileName = name;
            item.IsLocallyAvailable = true;
            await _service.SaveAudioItemListAsync();
            return true;
        }
    }
}
