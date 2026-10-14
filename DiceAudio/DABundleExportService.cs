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
        /// writes the bundle. False when the user cancels.
        /// </summary>
        public Task<bool> SaveAsync(DABundle bundle, string suggestedName)
        {
            string audioDir = _service.AudioDir;
            return _files.SaveStreamAsync(SafeFileName(suggestedName) + DABundle.FileExtension, DABundle.FileExtension,
                Resources.Localization.Portability.ExportFileType,
                async stream =>
                {
                    // The file reads take a while for a whole library: off the UI thread,
                    // on the bundle's own copies.
                    await Task.Run(() => AddFileFacts(bundle, audioDir));
                    await bundle.WriteAsync(stream);
                });
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
