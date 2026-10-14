/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Text.Json;

namespace DiceAudio.Portability
{
    /// <summary>
    /// The library as the collector reads it: plain lists, taken on the UI
    /// thread by the app (DiceAudioService and DAAudioTagsProvider), so the
    /// collector has no service to call and the tests can build one by hand.
    /// </summary>
    public sealed class DALibrarySnapshot
    {
        public IReadOnlyList<DAAudioItem> AudioItems { get; init; } = Array.Empty<DAAudioItem>();
        public IReadOnlyList<DAVirtualAudioFolder> Folders { get; init; } = Array.Empty<DAVirtualAudioFolder>();
        public IReadOnlyList<DAScene> Scenes { get; init; } = Array.Empty<DAScene>();
        public IReadOnlyList<DAScenarioGroup> ScenarioGroups { get; init; } = Array.Empty<DAScenarioGroup>();
        public IReadOnlyList<DAPreset> Presets { get; init; } = Array.Empty<DAPreset>();
        public IReadOnlyList<string> TypeTags { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> LocationTags { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ExpressionTags { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Builds the bundle of an export (spec §3.2): the assets the user chose
    /// (the roots) and everything they use. Every object in the bundle is a
    /// copy, so writing it never races with the pages, and nothing in it is a
    /// local path or audio data. A reference to an audio item or a local scene
    /// that no longer exists never fails the export: it becomes a
    /// <see cref="DAWarningCode.BrokenReference"/> warning and is left as it is.
    /// </summary>
    public sealed class DABundleCollector
    {
        private const string RootFolderName = ".";

        private readonly DALibrarySnapshot _library;
        private readonly string _appVersion;
        private readonly Func<DAAudioItem, (double? DurationSeconds, long? SizeBytes)>? _probe;

        private readonly Dictionary<Guid, DAAudioItem> _itemsById = new();
        private readonly Dictionary<Guid, DAVirtualAudioFolder> _foldersById = new();
        private readonly Dictionary<string, DAVirtualAudioFolder> _foldersByName = new(StringComparer.Ordinal);

        /// <param name="probe">
        /// Reads the duration and size of an item's file when it is on this
        /// computer (TagLib and FileInfo in the app); null leaves them out.
        /// </param>
        public DABundleCollector(DALibrarySnapshot library, string appVersion,
                                 Func<DAAudioItem, (double? DurationSeconds, long? SizeBytes)>? probe = null)
        {
            _library = library;
            _appVersion = appVersion;
            _probe = probe;

            foreach (var item in library.AudioItems) _itemsById.TryAdd(item.Id, item);
            foreach (var folder in library.Folders)
            {
                _foldersById.TryAdd(folder.Id, folder);
                _foldersByName.TryAdd(folder.Name, folder);
            }
        }

        // ── Entry points, one per kind of export (spec §3.2) ───────────────

        public DABundle ForAudioItems(IEnumerable<Guid> itemIds)
        {
            var b = new Builder(this);
            foreach (var id in itemIds.Distinct())
            {
                if (!_itemsById.ContainsKey(id)) continue;
                b.Root(DARootKind.AudioItem, id);
                b.PullItem(id, referrer: null);
            }
            return b.Finish();
        }

        public DABundle ForFolder(Guid folderId, bool includeSubfolders)
        {
            var b = new Builder(this);
            if (!_foldersById.TryGetValue(folderId, out var folder)) return b.Finish();

            b.Root(DARootKind.Folder, folderId, includeSubfolders: includeSubfolders);
            var folders = new List<DAVirtualAudioFolder> { folder };
            if (includeSubfolders) folders.AddRange(Descendants(folder));

            foreach (var f in folders) b.PullFolder(f.Id);
            var ids = folders.Select(f => f.Id).ToHashSet();
            foreach (var item in _library.AudioItems)
            {
                if (CanonicalFolderOf(item) is { } f && ids.Contains(f.Id))
                    b.PullItem(item.Id, referrer: null);
            }
            return b.Finish();
        }

        public DABundle ForScene(Guid sceneId) => ForScenes(_library.Scenes.Where(s => s.Id == sceneId));

        public DABundle ForAllScenes() => ForScenes(_library.Scenes);

        private DABundle ForScenes(IEnumerable<DAScene> scenes)
        {
            var b = new Builder(this);
            foreach (var scene in scenes)
            {
                b.Root(DARootKind.Scene, scene.Id);
                b.AddScene(scene);
            }
            return b.Finish();
        }

        public DABundle ForLocalScene(Guid groupId, Guid sceneId) =>
            ForLocalScenes(groupId, s => s.Id == sceneId);

        public DABundle ForAllLocalScenes(Guid groupId) => ForLocalScenes(groupId, _ => true);

        private DABundle ForLocalScenes(Guid groupId, Func<DAScene, bool> which)
        {
            var b = new Builder(this);
            var group = _library.ScenarioGroups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return b.Finish();
            foreach (var scene in group.LocalScenes.Where(which))
            {
                b.Root(DARootKind.LocalScene, scene.Id);
                b.AddLocalSceneRecord(group, scene);
            }
            return b.Finish();
        }

        public DABundle ForScenarioItem(Guid groupId, Guid scenarioId, Guid itemId)
        {
            var b = new Builder(this);
            var group = _library.ScenarioGroups.FirstOrDefault(g => g.Id == groupId);
            var scenario = group?.Scenarios.FirstOrDefault(s => s.Id == scenarioId);
            var item = scenario?.Items.FirstOrDefault(i => i.Id == itemId);
            if (item == null) return b.Finish();

            b.Root(DARootKind.ScenarioItem, item.Id);
            b.AddScenarioItemRecord(group!, scenario!, item);
            return b.Finish();
        }

        public DABundle ForScenario(Guid groupId, Guid scenarioId)
        {
            var b = new Builder(this);
            var group = _library.ScenarioGroups.FirstOrDefault(g => g.Id == groupId);
            var scenario = group?.Scenarios.FirstOrDefault(s => s.Id == scenarioId);
            if (scenario == null) return b.Finish();

            b.Root(DARootKind.Scenario, scenario.Id);
            b.AddScenarioRecord(group!, scenario);
            return b.Finish();
        }

        public DABundle ForScenarioGroup(Guid groupId)
        {
            var b = new Builder(this);
            var group = _library.ScenarioGroups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return b.Finish();

            b.Root(DARootKind.ScenarioGroup, group.Id);
            b.AddGroup(group);
            return b.Finish();
        }

        /// <summary>The groups filed under <paramref name="collection"/>, compared as the app does (case-insensitive).</summary>
        public DABundle ForCollection(string collection)
        {
            var b = new Builder(this);
            b.Root(DARootKind.Collection, null, name: collection);
            foreach (var group in _library.ScenarioGroups.Where(g =>
                         string.Equals(g.Collection, collection, StringComparison.CurrentCultureIgnoreCase)))
                b.AddGroup(group);
            return b.Finish();
        }

        public DABundle ForPreset(Guid presetId) => ForPresets(_library.Presets.Where(p => p.Id == presetId));

        public DABundle ForAllPresets() => ForPresets(_library.Presets);

        private DABundle ForPresets(IEnumerable<DAPreset> presets)
        {
            var b = new Builder(this);
            foreach (var preset in presets)
            {
                b.Root(DARootKind.Preset, preset.Id);
                b.AddPreset(preset);
            }
            return b.Finish();
        }

        /// <summary>
        /// Everything: every tag of the three lists, every folder (empty ones
        /// too), every item, scene, scenario group and preset. Pinned groups
        /// stay on this computer.
        /// </summary>
        public DABundle ForLibrary()
        {
            var b = new Builder(this);
            b.Root(DARootKind.Library, null);
            foreach (var tag in _library.TypeTags.Concat(_library.LocationTags).Concat(_library.ExpressionTags))
                b.PullTag(tag);
            foreach (var folder in _library.Folders) b.PullFolder(folder.Id);
            foreach (var item in _library.AudioItems) b.PullItem(item.Id, referrer: null);
            foreach (var scene in _library.Scenes) b.AddScene(scene);
            foreach (var group in _library.ScenarioGroups) b.AddGroup(group);
            foreach (var preset in _library.Presets) b.AddPreset(preset);
            return b.Finish();
        }

        // ── Folders ────────────────────────────────────────────────────────

        /// <summary>The folder an item lives in, from the name its embedded copy carries; null = the root.</summary>
        private DAVirtualAudioFolder? CanonicalFolderOf(DAAudioItem item)
        {
            string? name = item.Folder?.Name;
            if (string.IsNullOrEmpty(name) || name == RootFolderName) return null;
            return _foldersByName.TryGetValue(name, out var folder) ? folder : null;
        }

        /// <summary>The folder and its parents, root first. Bounded, since the parent links come from saved data.</summary>
        private List<DAVirtualAudioFolder> Ancestry(DAVirtualAudioFolder folder)
        {
            var chain = new List<DAVirtualAudioFolder>();
            var current = folder;
            for (int guard = 0; current != null && guard < 100; guard++)
            {
                if (chain.Contains(current)) break;
                chain.Insert(0, current);
                if (current.ParentFolderId == Guid.Empty) break;
                current = _foldersById.TryGetValue(current.ParentFolderId, out var parent) ? parent : null;
            }
            return chain;
        }

        private List<DAVirtualAudioFolder> Descendants(DAVirtualAudioFolder folder)
        {
            var found = new List<DAVirtualAudioFolder>();
            var seen = new HashSet<Guid> { folder.Id };
            var queue = new Queue<DAVirtualAudioFolder>();
            queue.Enqueue(folder);
            while (queue.Count > 0)
            {
                var next = queue.Dequeue();
                foreach (var child in _library.Folders.Where(f => f.ParentFolderId == next.Id && f.Name != RootFolderName))
                {
                    if (!seen.Add(child.Id)) continue;
                    found.Add(child);
                    queue.Enqueue(child);
                }
            }
            return found;
        }

        private string CategoryOf(string tag) =>
            _library.TypeTags.Contains(tag) ? "Type"
            : _library.LocationTags.Contains(tag) ? "Location"
            : _library.ExpressionTags.Contains(tag) ? "Expression"
            : "Unknown";

        private static T Clone<T>(T value) =>
            JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, DABundle.WriteOptions), DABundle.WriteOptions)!;

        // ── The bundle being built ─────────────────────────────────────────

        /// <summary>What refers to something: used for the warnings.</summary>
        private sealed record Referrer(string Kind, Guid Id, string Name);

        private sealed class Builder
        {
            private readonly DABundleCollector _c;
            private readonly DABundle _bundle;

            private readonly HashSet<Guid> _items = new();
            private readonly HashSet<Guid> _folders = new();
            private readonly HashSet<string> _tags = new(StringComparer.Ordinal);
            private readonly List<string> _tagOrder = new();
            private readonly HashSet<Guid> _scenes = new();
            private readonly HashSet<Guid> _groups = new();
            private readonly HashSet<Guid> _scenarioRecords = new();
            private readonly HashSet<Guid> _itemRecords = new();
            private readonly HashSet<Guid> _localSceneRecords = new();
            private readonly HashSet<Guid> _presets = new();
            private readonly HashSet<(Guid, Guid)> _brokenSeen = new();

            public Builder(DABundleCollector collector)
            {
                _c = collector;
                _bundle = new DABundle { AppVersion = collector._appVersion };
            }

            public void Root(string kind, Guid? id, string? name = null, bool? includeSubfolders = null) =>
                _bundle.Roots.Add(new BundleRoot { Kind = kind, Id = id, Name = name, IncludeSubfolders = includeSubfolders });

            // Items, folders, tags

            public void PullItem(Guid id, Referrer? referrer)
            {
                if (!_c._itemsById.TryGetValue(id, out var item))
                {
                    if (referrer != null && _brokenSeen.Add((referrer.Id, id)))
                    {
                        _bundle.Warnings.Add(new BundleWarning
                        {
                            Code = DAWarningCode.BrokenReference, Kind = referrer.Kind, Id = referrer.Id, Detail = referrer.Name,
                        });
                    }
                    return;
                }
                if (!_items.Add(id)) return;

                if (_c.CanonicalFolderOf(item) is { } folder) PullFolder(folder.Id);
                foreach (var tag in item.Tags) PullTag(tag.Name);
            }

            /// <summary>A folder and all its parents, so the folder can be rebuilt where it was.</summary>
            public void PullFolder(Guid id)
            {
                if (!_c._foldersById.TryGetValue(id, out var folder)) return;
                foreach (var f in _c.Ancestry(folder)) _folders.Add(f.Id);
            }

            public void PullTag(string name)
            {
                if (string.IsNullOrEmpty(name) || !_tags.Add(name)) return;
                _tagOrder.Add(name);
            }

            private void PullUsage(DAAudioUsage? usage, Referrer referrer)
            {
                if (usage != null) PullItem(usage.AudioItemId, referrer);
            }

            private void PullSceneAudio(DAScene scene, Referrer referrer)
            {
                foreach (var layer in scene.Layers) PullUsage(layer.Audio, referrer);
            }

            // Scenes

            public void AddScene(DAScene scene)
            {
                if (!_scenes.Add(scene.Id)) return;
                _bundle.Scenes.Add(Clone(scene));
                PullSceneAudio(scene, new Referrer(nameof(DAAssetKind.Scene), scene.Id, scene.Name));
            }

            /// <summary>A local scene exported without its group, as a record naming the group it came from.</summary>
            public void AddLocalSceneRecord(DAScenarioGroup group, DAScene scene)
            {
                if (_groups.Contains(group.Id) || !_localSceneRecords.Add(scene.Id)) return;
                _bundle.LocalScenes.Add(new DALocalSceneRecord
                {
                    SourceGroupId = group.Id, SourceGroupName = group.Name, Scene = Clone(scene),
                });
                PullSceneAudio(scene, new Referrer(nameof(DAAssetKind.LocalScene), scene.Id, scene.Name));
            }

            // Scenario items, scenarios, groups

            /// <summary>
            /// What an item uses: its tracks, sound effects, embedded scene, the
            /// local scene it plays (as a record when its group is not in the
            /// bundle), and the tags of its random-play criteria (tags only,
            /// never the items that match them).
            /// </summary>
            private void PullItemContents(DAScenarioGroup group, DAScenarioItem item)
            {
                var referrer = new Referrer(nameof(DAAssetKind.ScenarioItem), item.Id, item.Name);
                foreach (var u in item.PlaylistTracks) PullUsage(u, referrer);
                foreach (var u in item.SoundEffects) PullUsage(u, referrer);
                if (item.Scene != null) PullSceneAudio(item.Scene, referrer);

                if (item.LocalSceneId is Guid localId)
                {
                    var local = group.LocalScenes.FirstOrDefault(s => s.Id == localId);
                    if (local == null)
                    {
                        if (_brokenSeen.Add((item.Id, localId)))
                        {
                            _bundle.Warnings.Add(new BundleWarning
                            {
                                Code = DAWarningCode.BrokenReference, Kind = referrer.Kind, Id = item.Id, Detail = item.Name,
                            });
                        }
                    }
                    else if (!_groups.Contains(group.Id))
                    {
                        AddLocalSceneRecord(group, local);
                    }
                }

                foreach (var criteria in item.RandomPlayCriterias)
                    foreach (var tag in criteria.TagNames) PullTag(tag);
            }

            public void AddScenarioItemRecord(DAScenarioGroup group, DAScenario scenario, DAScenarioItem item)
            {
                if (!_itemRecords.Add(item.Id)) return;
                _bundle.ScenarioItems.Add(new DAScenarioItemRecord
                {
                    SourceGroupId = group.Id, SourceGroupName = group.Name,
                    SourceScenarioId = scenario.Id, SourceScenarioName = scenario.Name,
                    Item = Clone(item),
                });
                PullItemContents(group, item);
            }

            public void AddScenarioRecord(DAScenarioGroup group, DAScenario scenario)
            {
                if (!_scenarioRecords.Add(scenario.Id)) return;
                _bundle.Scenarios.Add(new DAScenarioRecord
                {
                    SourceGroupId = group.Id, SourceGroupName = group.Name, Scenario = Clone(scenario),
                });
                foreach (var item in scenario.Items) PullItemContents(group, item);
            }

            /// <summary>A whole group: its scenarios and all its local scenes travel inside it.</summary>
            public void AddGroup(DAScenarioGroup group)
            {
                if (!_groups.Add(group.Id)) return;
                _bundle.ScenarioGroups.Add(Clone(group));
                foreach (var scene in group.LocalScenes)
                    PullSceneAudio(scene, new Referrer(nameof(DAAssetKind.LocalScene), scene.Id, scene.Name));
                foreach (var scenario in group.Scenarios)
                    foreach (var item in scenario.Items) PullItemContents(group, item);
            }

            // Presets

            /// <summary>What a preset's payload uses, read as the scenario item or scene it holds.</summary>
            public void AddPreset(DAPreset preset)
            {
                if (!_presets.Add(preset.Id)) return;
                _bundle.Presets.Add(Clone(preset));
                var referrer = new Referrer(nameof(DAAssetKind.Preset), preset.Id, preset.Name);
                try
                {
                    if (preset.Kind == DAPresetKind.ScenarioItem)
                    {
                        var item = JsonSerializer.Deserialize<DAScenarioItem>(preset.PayloadJson);
                        if (item == null) return;
                        foreach (var u in item.PlaylistTracks) PullUsage(u, referrer);
                        foreach (var u in item.SoundEffects) PullUsage(u, referrer);
                        if (item.Scene != null) PullSceneAudio(item.Scene, referrer);
                        foreach (var criteria in item.RandomPlayCriterias)
                            foreach (var tag in criteria.TagNames) PullTag(tag);
                    }
                    else if (JsonSerializer.Deserialize<DAScene>(preset.PayloadJson) is { } scene)
                    {
                        PullSceneAudio(scene, referrer);
                    }
                }
                catch (JsonException)
                {
                    // A damaged payload still travels as it is; it uses nothing we can tell.
                }
            }

            // Finishing: records in library order

            public DABundle Finish()
            {
                foreach (var folder in _c._library.Folders.Where(f => _folders.Contains(f.Id)))
                {
                    var chain = _c.Ancestry(folder);
                    var parent = chain.Count >= 2 ? chain[^2] : null;
                    _bundle.Folders.Add(new DAFolderRecord
                    {
                        Id = folder.Id,
                        Name = folder.Name,
                        ParentId = parent?.Id,
                        Path = string.Join(" / ", chain.Select(f => f.Name)),
                    });
                }

                foreach (var item in _c._library.AudioItems.Where(i => _items.Contains(i.Id)))
                {
                    (double? duration, long? size) = _c._probe?.Invoke(item) ?? (null, null);
                    _bundle.AudioItems.Add(new DAAudioItemRecord
                    {
                        Id = item.Id,
                        Name = item.Name,
                        FolderId = _c.CanonicalFolderOf(item)?.Id,
                        Kind = item.Kind,
                        Tags = item.Tags.Select(t => t.Name).ToList(),
                        Volume = item.Volume,
                        Loop = item.Loop,
                        FadeInDuration = item.FadeInDuration,
                        StartDelay = item.StartDelay,
                        SourceURL = item.SourceURL ?? "",
                        // A bare name, whatever was stored: never a path.
                        OriginalFileName = string.IsNullOrEmpty(item.LocalFileName) ? null : Path.GetFileName(item.LocalFileName),
                        DurationSeconds = duration,
                        SizeBytes = size,
                    });

                    // Without a link the person who imports can only pick a file
                    // of their own (the media pack, which could carry it, is A6).
                    if (string.IsNullOrWhiteSpace(item.SourceURL))
                    {
                        _bundle.Warnings.Add(new BundleWarning
                        {
                            Code = DAWarningCode.NoSourceNoFile, Kind = nameof(DAAssetKind.AudioItem), Id = item.Id, Detail = item.Name,
                        });
                    }
                }

                foreach (var tag in _tagOrder)
                    _bundle.Tags.Add(new DATagRecord { Name = tag, Category = _c.CategoryOf(tag) });

                return _bundle;
            }
        }
    }
}
