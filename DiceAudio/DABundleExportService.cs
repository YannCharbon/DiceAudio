/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using DiceAudio.Portability;
using System.Diagnostics;

namespace DiceAudio
{
    /// <summary>An export asked for by a page: what to call the file and how to build its content.</summary>
    public sealed class DAExportRequest
    {
        public required string SuggestedName { get; init; }

        /// <summary>Builds the bundle; the argument is "include subfolders" (only read when <see cref="OffersSubfolders"/>).</summary>
        public required Func<bool, DABundle> Build { get; init; }

        /// <summary>A folder export: the dialog shows the "Include subfolders" tick box.</summary>
        public bool OffersSubfolders { get; init; }

        /// <summary>The whole library: the dialog shows what such a file holds.</summary>
        public bool IsLibrary { get; init; }
    }

    /// <summary>
    /// The one way into an export. A page calls one of the Export… methods;
    /// the export dialog, which sits in the main layout, shows what the file
    /// will contain and saves it. The collector runs on the UI thread, where
    /// the lists live, and gets copies of everything it puts in the bundle.
    /// </summary>
    public sealed class DABundleExportService
    {
        private readonly DiceAudioService _service;
        private readonly IFileHandler _files;

        public DABundleExportService(DiceAudioService service, IFileHandler files)
        {
            _service = service;
            _files = files;
        }

        /// <summary>Raised when a page asks for an export; the dialog opens.</summary>
        public event Action<DAExportRequest>? Requested;

        // ── The entry points of spec §3.2, one per kind ───────────────────

        public void ExportAudioItems(IReadOnlyCollection<DAAudioItem> items) =>
            Request(items.Count == 1 ? items.First().Name : $"{items.Count} audio items",
                    _ => Collector().ForAudioItems(items.Select(i => i.Id)));

        public void ExportFolder(DAVirtualAudioFolder folder) => Requested?.Invoke(new DAExportRequest
        {
            SuggestedName = folder.Name,
            Build = withSubfolders => Collector().ForFolder(folder.Id, withSubfolders),
            OffersSubfolders = true,
        });

        public void ExportScene(DAScene scene) => Request(scene.Name, _ => Collector().ForScene(scene.Id));

        public void ExportAllScenes() => Request("Scenes", _ => Collector().ForAllScenes());

        public void ExportLocalScene(DAScenarioGroup group, DAScene scene) =>
            Request(scene.Name, _ => Collector().ForLocalScene(group.Id, scene.Id));

        public void ExportGroupScenes(DAScenarioGroup group) =>
            Request($"{group.Name} - scenes", _ => Collector().ForAllLocalScenes(group.Id));

        public void ExportScenarioItem(DAScenarioGroup group, DAScenario scenario, DAScenarioItem item) =>
            Request(item.Name, _ => Collector().ForScenarioItem(group.Id, scenario.Id, item.Id));

        public void ExportScenario(DAScenarioGroup group, DAScenario scenario) =>
            Request(scenario.Name, _ => Collector().ForScenario(group.Id, scenario.Id));

        public void ExportGroup(DAScenarioGroup group) => Request(group.Name, _ => Collector().ForScenarioGroup(group.Id));

        public void ExportCollection(string collection) => Request(collection, _ => Collector().ForCollection(collection));

        public void ExportPreset(DAPreset preset) => Request(preset.Name, _ => Collector().ForPreset(preset.Id));

        public void ExportAllPresets() => Request("Presets", _ => Collector().ForAllPresets());

        public void ExportLibrary() => Requested?.Invoke(new DAExportRequest
        {
            SuggestedName = "DiceAudio library",
            Build = _ => Collector().ForLibrary(),
            IsLibrary = true,
        });

        private void Request(string name, Func<bool, DABundle> build) =>
            Requested?.Invoke(new DAExportRequest { SuggestedName = name, Build = build });

        /// <summary>A collector over the library as it is now. Call on the UI thread.</summary>
        private DABundleCollector Collector()
        {
            var tags = DAAudioTagsProvider.Instance;
            var snapshot = new DALibrarySnapshot
            {
                AudioItems = _service.AudioItems.ToList(),
                Folders = _service.AudioVirtualFolders.ToList(),
                Scenes = _service.Scenes.ToList(),
                ScenarioGroups = _service.ScenarioGroups.ToList(),
                Presets = _service.Presets.ToList(),
                TypeTags = tags.CategoryTypes.ToList(),
                LocationTags = tags.CategoryLocations.ToList(),
                ExpressionTags = tags.CategoryExpressions.ToList(),
            };
            return new DABundleCollector(snapshot, _service.GetVersion());
        }

        // ── Saving ─────────────────────────────────────────────────────────

        /// <summary>
        /// Asks where to save, adds the duration and size of the audio files
        /// that are on this computer (a hint for matching on import), then
        /// writes the bundle and, when asked, the audio file pack next to it
        /// (same name, .diceamedia). Both are written to a ".tmp" file first and
        /// put in place only once everything is written, so a cancelled or
        /// failed export leaves no half-written file. False when the user
        /// cancels the save window; <see cref="OperationCanceledException"/>
        /// when the export is stopped.
        /// </summary>
        public async Task<bool> SaveAsync(DABundle bundle, string suggestedName, bool includeMedia, bool checksums,
                                          IProgress<DAMediaProgress>? progress = null, CancellationToken ct = default)
        {
            string? picked = await _files.PickSaveFileAsync(SafeFileName(suggestedName) + DABundle.FileExtension,
                DABundle.FileExtension, Resources.Localization.Portability.ExportFileType);
            if (picked == null) return false;

            string target = picked.EndsWith(DABundle.FileExtension, StringComparison.OrdinalIgnoreCase)
                ? picked : picked + DABundle.FileExtension;
            string packTarget = Path.ChangeExtension(target, DAMediaManifest.FileExtension);
            string targetTmp = target + ".tmp", packTmp = packTarget + ".tmp";
            string audioDir = _service.AudioDir;

            try
            {
                // Off the UI thread, on the bundle's own copies: reading a whole
                // library's files and packing them takes a while.
                await Task.Run(async () =>
                {
                    AddFileFacts(bundle, audioDir);

                    if (includeMedia)
                    {
                        var sources = MediaSources(bundle, audioDir);
                        DAMediaManifest manifest;
                        await using (var pack = new FileStream(packTmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16, useAsync: true))
                        {
                            manifest = await DAMediaPackWriter.WriteAsync(pack, bundle.BundleId, bundle.AppVersion,
                                                                          sources, checksums, progress, ct);
                            await pack.FlushAsync(ct);
                        }
                        bundle.ApplyMediaPack(Path.GetFileName(packTarget), manifest);
                    }

                    await using (var file = new FileStream(targetTmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                    {
                        await bundle.WriteAsync(file, ct);
                    }

                    ct.ThrowIfCancellationRequested();
                    if (includeMedia) File.Move(packTmp, packTarget, overwrite: true);
                    File.Move(targetTmp, target, overwrite: true);
                }, ct);
                return true;
            }
            catch
            {
                TryDelete(targetTmp);
                TryDelete(packTmp);
                // The save window may have created the chosen file empty: an
                // empty .dicea is of no use to anyone.
                if (File.Exists(target) && new FileInfo(target).Length == 0) TryDelete(target);
                throw;
            }
        }

        /// <summary>The files the pack can carry: items of the bundle whose file is in the audio folder.</summary>
        private static List<DAMediaSource> MediaSources(DABundle bundle, string audioDir) =>
            bundle.AudioItems
                .Where(r => !string.IsNullOrEmpty(r.OriginalFileName))
                .Select(r => new DAMediaSource(r.Id, r.OriginalFileName!, Path.Combine(audioDir, r.OriginalFileName!)))
                .Where(s => File.Exists(s.FullPath))
                .ToList();

        /// <summary>
        /// For the dialog: how many items of the bundle have their file here,
        /// out of how many, and their total size. Quick enough for the UI thread.
        /// </summary>
        public (int Present, int Total, long Bytes) MediaEstimate(DABundle bundle)
        {
            var sources = MediaSources(bundle, _service.AudioDir);
            long bytes = 0;
            foreach (var s in sources)
            {
                try { bytes += new FileInfo(s.FullPath).Length; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return (sources.Count, bundle.AudioItems.Count, bytes);
        }

        /// <summary>
        /// Items the person who imports will have to find a file for: no link,
        /// and, when the audio files go along, no file here either.
        /// </summary>
        public int NothingToGetCount(DABundle bundle, bool includeMedia)
        {
            var noSource = bundle.Warnings.Where(w => w.Code == DAWarningCode.NoSourceNoFile && w.Id != null)
                                          .Select(w => w.Id!.Value).ToHashSet();
            if (!includeMedia) return noSource.Count;
            var packed = MediaSources(bundle, _service.AudioDir).Select(s => s.ItemId).ToHashSet();
            return noSource.Count(id => !packed.Contains(id));
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine($"Could not delete {path}: {ex.Message}"); }
        }

        /// <summary>"1.2 GB", "350 MB", "12 KB": sizes as people read them.</summary>
        public static string FormatSize(long bytes)
        {
            string[] units = { "bytes", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return unit == 0 ? $"{bytes} bytes" : $"{size.ToString(size < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture)} {units[unit]}";
        }

        private static void AddFileFacts(DABundle bundle, string audioDir)
        {
            foreach (var record in bundle.AudioItems)
            {
                if (string.IsNullOrEmpty(record.OriginalFileName)) continue;
                string path = Path.Combine(audioDir, record.OriginalFileName);
                if (!File.Exists(path)) continue;
                try
                {
                    record.SizeBytes = new FileInfo(path).Length;
                    using var tagFile = TagLib.File.Create(path);
                    double seconds = tagFile.Properties.Duration.TotalSeconds;
                    record.DurationSeconds = seconds > 0 ? Math.Round(seconds, 1) : null;
                }
                catch (Exception ex)
                {
                    // A file TagLib cannot read only loses its duration.
                    Debug.WriteLine($"Could not read {record.OriginalFileName}: {ex.Message}");
                }
            }
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            return string.IsNullOrEmpty(cleaned) ? "DiceAudio export" : cleaned;
        }
    }
}
