/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Reflection;
using System.Net.Http.Headers;
using Plugin.Maui.Audio;
using DiceAudio.Storage;

namespace DiceAudio
{
    public class DiceAudioService
    {
        public List<DAAudioItem> AudioItems { get; set; } = new List<DAAudioItem>();
        public List<DAVirtualAudioFolder> AudioVirtualFolders { get; set; } = new List<DAVirtualAudioFolder>();
        public List<DAScene> Scenes { get; set; } = new List<DAScene>();
        public List<DAScenarioGroup> ScenarioGroups { get; set; } = new List<DAScenarioGroup>();
        public List<DAPreset> Presets { get; set; } = new List<DAPreset>();

        /// <summary>
        /// The scenario groups the user pinned, newest pin last. Ids rather than
        /// groups so the list survives a group being renamed or re-filed, and so a
        /// group deleted elsewhere simply stops resolving instead of leaving a
        /// dangling reference; <see cref="GetPinnedScenarioGroups"/> prunes those.
        /// </summary>
        private List<Guid> _pinnedScenarioGroups = new List<Guid>();

        readonly IAudioManager audioManager;

        /// <summary>
        /// Every data file is read and written through this store: atomic,
        /// write-if-changed saves, and a file that failed to load is never
        /// saved over (see <see cref="UnreadableFiles"/>).
        /// </summary>
        public SafeFileStore Store { get; }

        public const string AudioItemsFileName = "audioItems.json";
        public const string AudioVirtualFoldersFileName = "audioVirtualFolders.json";
        public const string ScenarioGroupsFileName = "scenarioGroups.json";
        public const string PinnedScenarioGroupsFileName = "pinnedScenarioGroups.json";
        public const string ScenesFileName = "scenes.json";
        public const string PresetsFileName = "presets.json";

        /// <summary>
        /// Changes made to a file outside the app are not looked for yet (that
        /// comes with the use from several computers), so every save overwrites
        /// the file, as it always has.
        /// </summary>
        private const bool OverwriteExternalChange = true;

        /// <summary>
        /// Enums are written by name rather than by their position, so adding a
        /// value to any of them later cannot silently reinterpret the files that
        /// are already on disk. Reading still accepts the numbers written by the
        /// versions before this.
        /// </summary>
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            DefaultBufferSize = 15 * 1024 * 1024, // 15MiB
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            DefaultBufferSize = 15 * 1024 * 1024, // 15MiB
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Completes when the lists have been read. Until then, saving would
        /// write an empty list over a file that has not been read yet, so every
        /// save waits for this.
        /// </summary>
        private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// The load, once <see cref="InitializeAsync"/> has started it.
        private Task? _initialization;
        private readonly object _initializationGate = new();

        public bool IsLoaded => _loaded.Task.IsCompleted;

        /// <summary>Completes once the persisted lists are in memory — await before reading them.</summary>
        public Task WhenLoaded => _loaded.Task;

        /// <summary>Where the data files and the audio files are.</summary>
        public DataLocation Location { get; }

        private readonly ILocalPreferences _localPreferences;

        public DiceAudioService(IAudioManager audioManager, SafeFileStore store, DataLocation location, ILocalPreferences localPreferences)
        {
            this.audioManager = audioManager;
            Store = store;
            Location = location;
            _localPreferences = localPreferences;
        }

        /// <summary>
        /// Reads every data file, once; later calls return the same task. Called
        /// at startup only when the data folder can be used, so that nothing is
        /// ever read from, or saved into, a folder that cannot.
        /// </summary>
        public Task InitializeAsync()
        {
            lock (_initializationGate)
            {
                _initialization ??= Task.Run(async () =>
                {
                    try
                    {
                        await LoadEverythingAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        _loaded.TrySetResult();
                    }
                });
                return _initialization;
            }
        }

        /// <summary>
        /// False when the data folder cannot be used: then nothing, audio files
        /// included, may be written into it. (The startup screen stands in for
        /// the app until a usable folder is chosen; nothing is read before.)
        /// </summary>
        public bool CanWriteData => Location.State == DataLocationState.Ok;

        /// <summary>
        /// Waits for the load before a save. False when the load never started
        /// (the data folder could not be used): nothing must be written then.
        /// </summary>
        private async Task<bool> ReadyToSaveAsync()
        {
            Task? initialization;
            lock (_initializationGate) initialization = _initialization;
            if (initialization == null) return false;
            await _loaded.Task;
            return true;
        }

        /// <summary>The folder of the audio files, worked out on each call (the data folder can move).</summary>
        public string AudioDir => Location.AudioDir;

        /// <summary>
        /// Absolute path to play an audio file from, or null when the file is not
        /// on disk.
        ///
        /// Earlier builds copied the file into <see cref="FileSystem.CacheDirectory"/>
        /// and played the copy. Nothing needed that: both are ordinary read/write
        /// directories in the same container, and both playback backends take a
        /// path. It only duplicated the whole library on disk — the copies were
        /// never evicted — and served stale audio whenever a replacement file
        /// happened to have the same byte length as the one it replaced.
        /// </summary>
        public string? GetPlayablePath(string localFileName)
        {
            if (string.IsNullOrEmpty(localFileName)) return null;

            var path = Path.Combine(AudioDir, localFileName);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Loads each file on its own: one that cannot be read leaves its list
        /// empty and is blocked by the store, so it is never saved over, while
        /// the others load normally. The main layout tells the user.
        /// </summary>
        // Nothing here touches the UI, so no continuation needs to come back to
        // the thread that created the service.
        private async Task LoadEverythingAsync()
        {
            await LoadStepAsync(DAAudioTagsProvider.FileNames, () => DAAudioTagsProvider.Instance.LoadAsync(Store)).ConfigureAwait(false);
            await LoadStepAsync(new[] { AudioItemsFileName }, LoadAudioItemListAsync).ConfigureAwait(false);
            await LoadStepAsync(new[] { AudioVirtualFoldersFileName }, LoadAudioVirtualFoldersListAsync).ConfigureAwait(false);
            await LoadStepAsync(new[] { ScenarioGroupsFileName }, LoadScenarioGroupsAsync).ConfigureAwait(false);
            await LoadStepAsync(new[] { ScenesFileName }, LoadScenesAsync).ConfigureAwait(false);
            await LoadStepAsync(new[] { PresetsFileName }, LoadPresetsAsync).ConfigureAwait(false);
            await LoadStepAsync(new[] { PinnedScenarioGroupsFileName }, LoadPinnedScenarioGroupsAsync).ConfigureAwait(false);
        }

        private async Task LoadStepAsync(IReadOnlyList<string> fileNames, Func<Task> load)
        {
            try
            {
                await load().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The store reports a damaged file without throwing, so this is
                // the unexpected. Whatever went wrong, the files of this step
                // were not taken in: block them, so the lists left in their
                // place are never saved over them, and go on with the others.
                Debug.WriteLine($"Loading {string.Join(", ", fileNames)} failed: {ex.Message}");
                foreach (var fileName in fileNames)
                    Store.Block(fileName, ex.Message);
            }
        }

        /// <summary>The data files that could not be read at startup, and that this session does not write.</summary>
        public IReadOnlyCollection<string> UnreadableFiles => Store.BlockedFiles;

        /// <summary>
        /// Reads every file again, after a restore. Each list is replaced only
        /// once its file has been read (a missing file gives an empty list); a
        /// file that cannot be read keeps the list as it was, and stays blocked.
        /// The caller stops playback first and rebuilds the pages afterwards.
        /// </summary>
        public async Task ReloadAsync()
        {
            await LoadEverythingAsync().ConfigureAwait(false);
            _loaded.TrySetResult();
        }

        /// <summary>
        /// Saves every list and the tags, then waits until every write, including
        /// those started elsewhere, is done. Used when the app closes.
        /// </summary>
        public async Task SaveEverythingAsync()
        {
            if (!await ReadyToSaveAsync()) return;

            var saves = new List<Task>
            {
                SaveAudioItemListAsync(),
                SaveAudioVirtualFoldersAsync(),
                SaveScenarioGroupsAsync(),
                SaveScenesAsync(),
                SavePresetsAsync(),
                SavePinnedScenarioGroupsAsync(),
                DAAudioTagsProvider.Instance.SaveTagsAsync(),
            };
            // A save that fails has already told the user (SaveFailed), and the
            // other files must still be written: neither the saves nor the
            // flush below stop at the first error.
            try
            {
                await Task.WhenAll(saves);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Saving everything failed: {ex.Message}");
            }

            try
            {
                await Store.FlushAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Waiting for the saves failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves every list and the tags and waits until all of it is on disk,
        /// throwing if any of it could not be written — or was not written
        /// because the file is blocked. An import needs to know, to put the
        /// library back as it was.
        /// </summary>
        public async Task SaveAllOrThrowAsync()
        {
            if (!await ReadyToSaveAsync()) throw new InvalidOperationException("The data folder cannot be used.");
            if (Store.BlockedFiles.Count > 0)
                throw new InvalidOperationException("A data file could not be read at startup: " + string.Join(", ", Store.BlockedFiles));

            await Task.WhenAll(
                SaveAudioItemListAsync(),
                SaveAudioVirtualFoldersAsync(),
                SaveScenarioGroupsAsync(),
                SaveScenesAsync(),
                SavePresetsAsync(),
                SavePinnedScenarioGroupsAsync(),
                DAAudioTagsProvider.Instance.SaveTagsAsync());
            await Store.FlushAsync();
        }

        /// <summary>Waits for every save already started. See <see cref="SafeFileStore.FlushAsync"/>.</summary>
        public Task FlushAsync(CancellationToken ct = default) => Store.FlushAsync(ct);

        public void AddAudioItem(DAAudioItem audioItem)
        {
            // Root is not a folder entry, so an item sitting there keeps its own
            // "." marker instead of creating a bogus folder named "." in the tree.
            if (!IsRootFolder(audioItem.Folder))
            {
                var canonical = AudioVirtualFolders.FirstOrDefault(x => x.Name == audioItem.Folder.Name);
                if (canonical == null)
                {
                    canonical = new DAVirtualAudioFolder(audioItem.Folder.Name)
                    {
                        ParentFolderId = audioItem.Folder.ParentFolderId
                    };
                    AudioVirtualFolders.Add(canonical);
                }
                audioItem.Folder = canonical;
            }
            AudioItems.Add(audioItem);
        }

        // ── Virtual folder tree ────────────────────────────────────────────
        // Every folder-aware UI goes through these helpers so the whole app
        // agrees on what "Root" is, how a path reads and how folders are made.

        /// <summary>The name an item carries while it sits at the root of the tree.</summary>
        public const string RootFolderName = ".";

        /// <summary>Root is represented by <c>null</c> in the UI and by a "." folder on items.</summary>
        public static bool IsRootFolder(DAVirtualAudioFolder? folder) =>
            folder == null || folder.Name == RootFolderName;

        /// <summary>A fresh "." folder for an item being placed at the root.</summary>
        public static DAVirtualAudioFolder NewRootFolderMarker() => new DAVirtualAudioFolder(RootFolderName);

        public DAVirtualAudioFolder? FindFolder(Guid id) =>
            id == Guid.Empty ? null : AudioVirtualFolders.FirstOrDefault(f => f.Id == id);

        public DAVirtualAudioFolder? FindFolderByName(string? name) =>
            string.IsNullOrEmpty(name) || name == RootFolderName
                ? null
                : AudioVirtualFolders.FirstOrDefault(f => f.Name == name);

        /// <summary>
        /// Items loaded from JSON carry their own copy of the folder they live in.
        /// Resolves such a copy to the canonical entry of <see cref="AudioVirtualFolders"/>
        /// (null when the item sits at the root, or its folder no longer exists).
        /// </summary>
        public DAVirtualAudioFolder? CanonicalFolderOf(DAVirtualAudioFolder? folder) =>
            IsRootFolder(folder) ? null : FindFolderByName(folder!.Name);

        /// <summary>Direct subfolders of <paramref name="parent"/> (null = root), by name.</summary>
        public List<DAVirtualAudioFolder> ChildFolders(DAVirtualAudioFolder? parent)
        {
            var parentId = parent?.Id ?? Guid.Empty;
            return AudioVirtualFolders
                .Where(f => f.Name != RootFolderName && f.ParentFolderId == parentId)
                .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// The ancestor chain of <paramref name="folder"/>, root-most first and
        /// ending with the folder itself. Empty for the root.
        /// </summary>
        public List<DAVirtualAudioFolder> FolderAncestry(DAVirtualAudioFolder? folder)
        {
            var chain = new List<DAVirtualAudioFolder>();
            var current = IsRootFolder(folder) ? null : folder;
            // The parent links come from persisted data, so a cycle is possible:
            // walking a bounded number of steps keeps a corrupted file from hanging the UI.
            int guard = 0;
            while (current != null && guard++ < 100)
            {
                chain.Insert(0, current);
                if (current.ParentFolderId == Guid.Empty) break;
                current = FindFolder(current.ParentFolderId);
            }
            return chain;
        }

        /// <summary>Human-readable path, e.g. "Root" or "SWTOR / Combat".</summary>
        public string FolderDisplayPath(DAVirtualAudioFolder? folder)
        {
            var chain = FolderAncestry(folder);
            return chain.Count == 0 ? "Root" : string.Join(" / ", chain.Select(f => f.Name));
        }

        /// <summary>Folder names are globally unique in this app's model.</summary>
        public bool FolderNameExists(string name) =>
            AudioVirtualFolders.Any(f => string.Equals(f.Name, name?.Trim(), StringComparison.CurrentCultureIgnoreCase));

        /// <summary>Every folder below <paramref name="folder"/>, at any depth.</summary>
        public List<DAVirtualAudioFolder> DescendantFolders(DAVirtualAudioFolder folder)
        {
            var found = new List<DAVirtualAudioFolder>();
            var queue = new Queue<DAVirtualAudioFolder>(ChildFolders(folder));
            // Bounded by the folder count: a parent cycle in a corrupted file
            // must not turn this into an endless walk.
            int guard = AudioVirtualFolders.Count + 1;
            while (queue.Count > 0 && guard-- > 0)
            {
                var next = queue.Dequeue();
                found.Add(next);
                foreach (var child in ChildFolders(next)) queue.Enqueue(child);
            }
            return found;
        }

        /// <summary>
        /// True when <paramref name="candidate"/> is <paramref name="folder"/>
        /// itself or sits anywhere below it. The root (null) never is.
        /// </summary>
        public bool IsSameOrDescendant(DAVirtualAudioFolder? candidate, DAVirtualAudioFolder folder)
        {
            var current = candidate;
            int guard = 0;
            while (current != null && guard++ < 100)
            {
                if (current.Id == folder.Id) return true;
                current = FindFolder(current.ParentFolderId);
            }
            return false;
        }

        /// <summary>
        /// Re-parents a folder (null = root); its subfolders and the items inside
        /// them come along, since both hang off the folder rather than off its
        /// position. Refuses a move into the folder itself or one of its own
        /// descendants, which would cut that branch out of the tree, and a move
        /// to where the folder already is. Returns false in those cases.
        /// </summary>
        public bool MoveFolder(DAVirtualAudioFolder folder, DAVirtualAudioFolder? newParent)
        {
            if (newParent != null && IsSameOrDescendant(newParent, folder)) return false;

            var parentId = newParent?.Id ?? Guid.Empty;
            if (folder.ParentFolderId == parentId) return false;
            folder.ParentFolderId = parentId;

            // Items carry their own copy of the folder they live in: left stale,
            // it puts the folder back where it was the next time the tree is
            // rebuilt from item metadata.
            foreach (var item in AudioItems.Where(i => i.Folder != null && i.Folder.Name == folder.Name))
                item.Folder.ParentFolderId = parentId;

            return true;
        }

        /// <summary>
        /// Creates a subfolder of <paramref name="parent"/> (null = root). Returns
        /// null when the name is empty or already taken somewhere in the tree.
        /// </summary>
        public DAVirtualAudioFolder? CreateFolder(string name, DAVirtualAudioFolder? parent)
        {
            name = name?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name) || name == RootFolderName) return null;
            if (FolderNameExists(name)) return null;

            var folder = new DAVirtualAudioFolder(name) { ParentFolderId = parent?.Id ?? Guid.Empty };
            AudioVirtualFolders.Add(folder);
            return folder;
        }

        /// <summary>
        /// Returns the virtual folder at the given path (e.g. ["SWTOR", "Combat"]),
        /// creating any missing folders along the way (nested via ParentFolderId).
        /// Folder names are globally unique in the app's model: an existing folder
        /// with a matching name is reused. The path is resolved below
        /// <paramref name="parent"/> (null = root). Returns <paramref name="parent"/>
        /// itself for an empty path.
        /// </summary>
        public DAVirtualAudioFolder? GetOrCreateFolderPath(IEnumerable<string> segments, DAVirtualAudioFolder? parent = null)
        {
            DAVirtualAudioFolder? current = IsRootFolder(parent) ? null : parent;
            Guid parentId = current?.Id ?? Guid.Empty;

            foreach (var raw in segments)
            {
                var name = raw.Trim();
                if (string.IsNullOrEmpty(name) || name == RootFolderName) continue;

                var existing = AudioVirtualFolders.FirstOrDefault(f => f.Name == name);
                if (existing == null)
                {
                    existing = new DAVirtualAudioFolder(name) { ParentFolderId = parentId };
                    AudioVirtualFolders.Add(existing);
                }
                current = existing;
                parentId = existing.Id;
            }

            return current;
        }

        public async Task SaveAudioItemListAsync()
        {
            if (!await ReadyToSaveAsync()) return;
            await SaveListAsync(AudioItems, AudioItemsFileName);
        }

        public async Task LoadAudioItemListAsync()
        {
            AudioItems = await LoadListAsync<DAAudioItem>(AudioItemsFileName).ConfigureAwait(false) ?? AudioItems;
            RefreshLocalAvailability();
        }

        /// <summary>
        /// Works out again, from the audio folder, which items have their file on
        /// this computer. The saved flag only says what was true when it was
        /// written, which a file deleted, restored or copied since then changes.
        /// </summary>
        public void RefreshLocalAvailability()
        {
            string audioDir = AudioDir;
            foreach (var item in AudioItems)
            {
                item.IsLocallyAvailable = !string.IsNullOrEmpty(item.LocalFileName)
                                          && File.Exists(Path.Combine(audioDir, item.LocalFileName));
            }
        }

        public async Task SaveAudioVirtualFoldersAsync()
        {
            if (!await ReadyToSaveAsync()) return;
            await SaveListAsync(AudioVirtualFolders, AudioVirtualFoldersFileName);
        }

        public async Task LoadAudioVirtualFoldersListAsync()
        {
            AudioVirtualFolders = await LoadListAsync<DAVirtualAudioFolder>(AudioVirtualFoldersFileName).ConfigureAwait(false) ?? AudioVirtualFolders;
        }

        // The scenario pages auto-save on every change and the app lifecycle
        // saves on its own: the store puts the writes of one file in order
        // (one lock per file), so they can no longer overlap.
        public async Task SaveScenarioGroupsAsync()
        {
            if (!await ReadyToSaveAsync()) return;
            await SaveListAsync(ScenarioGroups, ScenarioGroupsFileName);
        }

        /// <summary>Idle time before an auto-save actually writes.</summary>
        private const int AutoSaveDelayMs = 600;

        private CancellationTokenSource? _scenarioGroupsAutoSave;

        /// <summary>
        /// Saves the scenario groups a moment from now, without the caller
        /// waiting for the file: this is the auto-save the scenario pages use
        /// instead of a Save button. Successive calls restart the delay, so a
        /// burst of changes — typing a name, dragging cards around — ends in a
        /// single write. Callers that need the file on disk right away, such as
        /// a deletion before leaving the page, await
        /// <see cref="SaveScenarioGroupsAsync"/> instead.
        /// </summary>
        public void RequestScenarioGroupsSave()
        {
            var previous = _scenarioGroupsAutoSave;
            var cts = new CancellationTokenSource();
            _scenarioGroupsAutoSave = cts;

            previous?.Cancel();
            previous?.Dispose();

            _ = AutoSaveScenarioGroupsAsync(cts.Token);
        }

        private async Task AutoSaveScenarioGroupsAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(AutoSaveDelayMs, token);
                await SaveScenarioGroupsAsync();
            }
            catch (OperationCanceledException)
            {
                // A later change superseded this write; that one saves instead.
            }
            catch (Exception ex)
            {
                // An auto-save has no UI to report to, and losing the write is
                // better than taking the app down: the next change tries again,
                // and so does the save on app shutdown.
                Debug.WriteLine($"Scenario groups auto-save failed: {ex.Message}");
            }
        }

        public async Task LoadScenarioGroupsAsync()
        {
            ScenarioGroups = await LoadListAsync<DAScenarioGroup>(ScenarioGroupsFileName).ConfigureAwait(false) ?? ScenarioGroups;
        }

        /// <summary>
        /// The scene a scenario item plays: the group-local scene it references,
        /// or its own embedded copy. Local scenes are looked up by id across every
        /// group, so callers that do not know the owning group — the playback and
        /// remote-control paths — can resolve without carrying it around. UI code
        /// that already has the group should use
        /// <see cref="DAScenarioGroup.ResolveScene"/> instead.
        /// </summary>
        public DAScene? ResolveScene(DAScenarioItem item)
        {
            if (item.LocalSceneId is not Guid id) return item.Scene;

            foreach (var group in ScenarioGroups)
            {
                var scene = group.LocalScenes.FirstOrDefault(s => s.Id == id);
                if (scene != null) return scene;
            }
            return null;
        }

        /// <summary>The group a scenario belongs to, or null if it is detached.</summary>
        public DAScenarioGroup? FindGroupOfScenario(DAScenario scenario) =>
            ScenarioGroups.FirstOrDefault(g => g.Scenarios.Contains(scenario));

        // ── Collections of scenario groups ──────────────────────────────────
        // A collection is a folder of scenario groups, and nothing more. The
        // names are derived from the groups themselves rather than kept in a
        // list of their own, so there is no second place to keep in sync:
        // renaming a collection or taking its last group out can never leave a
        // ghost folder behind.

        /// <summary>Every collection in use, de-duplicated case-insensitively and sorted.</summary>
        public List<string> GetCollections() =>
            ScenarioGroups
                .Where(g => !string.IsNullOrWhiteSpace(g.Collection))
                .Select(g => g.Collection)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCulture)
                .ToList();

        /// <summary>The scenario groups filed under the given collection.</summary>
        public List<DAScenarioGroup> GetGroupsOfCollection(string collection) =>
            ScenarioGroups.FindAll(g =>
                string.Equals(g.Collection, collection, StringComparison.CurrentCultureIgnoreCase));

        /// <summary>
        /// Files a group under a collection, creating that collection by the mere
        /// fact of naming it. An empty name takes the group back out of any.
        /// </summary>
        public async Task SetGroupCollectionAsync(DAScenarioGroup group, string? collection)
        {
            group.Collection = (collection ?? string.Empty).Trim();
            await SaveScenarioGroupsAsync();
        }

        /// <summary>
        /// Renames a collection by moving every group that carries the old name.
        /// An empty new name simply takes those groups out of any collection.
        /// </summary>
        public async Task RenameCollectionAsync(string oldName, string newName)
        {
            string trimmed = (newName ?? string.Empty).Trim();
            foreach (var group in GetGroupsOfCollection(oldName))
                group.Collection = trimmed;
            await SaveScenarioGroupsAsync();
        }

        /// <summary>Whether this group is pinned to the home page.</summary>
        public bool IsScenarioGroupPinned(DAScenarioGroup group) => _pinnedScenarioGroups.Contains(group.Id);

        /// <summary>
        /// Pins a group, or unpins it if it already was. One call rather than two
        /// because every caller is a single control that shows the state it toggles.
        /// </summary>
        public async Task ToggleScenarioGroupPinnedAsync(DAScenarioGroup group)
        {
            if (!_pinnedScenarioGroups.Remove(group.Id))
                _pinnedScenarioGroups.Add(group.Id);
            await SavePinnedScenarioGroupsAsync();
        }

        /// <summary>
        /// The pinned groups, in the order they were pinned, skipping any whose
        /// group has since been deleted. Pruning happens here rather than at delete
        /// time so nothing has to remember to call it.
        /// </summary>
        public List<DAScenarioGroup> GetPinnedScenarioGroups()
        {
            var groups = new List<DAScenarioGroup>();
            foreach (var id in _pinnedScenarioGroups)
            {
                var group = ScenarioGroups.FirstOrDefault(g => g.Id == id);
                if (group != null) groups.Add(group);
            }
            return groups;
        }

        /// <summary>
        /// A merge with another data folder: its pinned groups join these, in
        /// their order after the ones already pinned. Pins of groups that do not
        /// exist (any more) are dropped.
        /// </summary>
        public async Task MergePinnedScenarioGroupsAsync(IEnumerable<Guid> otherPins)
        {
            var existing = ScenarioGroups.Select(g => g.Id).ToHashSet();
            var merged = _pinnedScenarioGroups.Concat(otherPins).Where(existing.Contains).Distinct().ToList();
            if (merged.SequenceEqual(_pinnedScenarioGroups)) return;
            _pinnedScenarioGroups = merged;
            await SavePinnedScenarioGroupsAsync();
        }

        public async Task SavePinnedScenarioGroupsAsync()
        {
            if (!await ReadyToSaveAsync()) return;
            await SaveListAsync(_pinnedScenarioGroups, PinnedScenarioGroupsFileName);
        }

        public async Task LoadPinnedScenarioGroupsAsync()
        {
            _pinnedScenarioGroups = await LoadListAsync<Guid>(PinnedScenarioGroupsFileName).ConfigureAwait(false) ?? _pinnedScenarioGroups;
        }

        public async Task SaveScenesAsync()
        {
            if (!await ReadyToSaveAsync()) return;
            await SaveListAsync(Scenes, ScenesFileName);
        }

        public async Task LoadScenesAsync()
        {
            Scenes = await LoadListAsync<DAScene>(ScenesFileName).ConfigureAwait(false) ?? Scenes;
        }

        public async Task SavePresetsAsync()
        {
            if (!await ReadyToSaveAsync()) return;
            await SaveListAsync(Presets, PresetsFileName);
        }

        public async Task LoadPresetsAsync()
        {
            Presets = await LoadListAsync<DAPreset>(PresetsFileName).ConfigureAwait(false) ?? Presets;
        }

        // Shared JSON list persistence helpers

        /// <summary>
        /// Saves through the store, which takes the snapshot now and writes it
        /// only when it changed. A file that failed to load is left alone.
        /// </summary>
        private async Task SaveListAsync<T>(List<T> list, string fileName)
        {
            var outcome = await Store.SaveJsonAsync(fileName, list, WriteOptions, OverwriteExternalChange);
            if (outcome != SaveOutcome.Unchanged)
                Debug.WriteLine($"Save {fileName}: {outcome}");
        }

        /// <summary>
        /// The list in the file, an empty list when the file is missing, or null
        /// when it cannot be read: the list in memory is then kept, and the
        /// store blocks the file so that it is never saved over. (A missing file
        /// gives an empty list rather than keeping the old one, so that a
        /// restore of a backup without that file empties it.)
        /// </summary>
        private async Task<List<T>?> LoadListAsync<T>(string fileName)
        {
            var result = await Store.LoadJsonAsync<List<T>>(fileName, ReadOptions).ConfigureAwait(false);
            if (result.Outcome == LoadOutcome.Failed)
                Debug.WriteLine($"Failed to load {fileName}: {result.Error}");
            return result.Outcome switch
            {
                LoadOutcome.Loaded => result.Value,
                LoadOutcome.Missing => new List<T>(),
                _ => null,
            };
        }

        // Generic management

        /// <summary>
        /// The version this build reports. The release workflow rewrites
        /// &lt;AssemblyVersion&gt; in the csproj from the tag it is building, so this is
        /// the tag for a published build and the csproj's own value for a local one
        /// - which is why that value is kept in step with the last release rather
        /// than left at 0.0.0, as it was.
        /// </summary>
        public string GetVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            // Major.Minor.Build spelled out: the old code cut everything after the
            // last dot of ToString(), which turns a three-part version into "0.0".
            return version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }

        // ── checking for a newer release ───────────────────────────────────────
        // One client for the process. A fresh HttpClient per call leaks a socket
        // per call, and this used to run on every visit to the home page and to
        // the settings page.
        private static readonly HttpClient UpdateHttpClient = CreateUpdateHttpClient();

        private static HttpClient CreateUpdateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            // The GitHub API rejects a request with no User-Agent.
            client.DefaultRequestHeaders.UserAgent.TryParseAdd("DiceAudio");
            return client;
        }

        /// How long a successful answer is trusted before the network is asked
        /// again. GitHub allows 60 unauthenticated requests an hour per IP and the
        /// app used to spend one on every page visit, so a few minutes of browsing
        /// exhausted the quota - after which every reply is 403.
        private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

        private const string PrefUpdateCheckedUtc = "UpdateLastCheckUtc";
        private const string PrefUpdateLatestTag = "UpdateLatestTag";
        private const string PrefUpdateLatestUrl = "UpdateLatestUrl";

        private DAUpdateStatus? _updateStatus;

        /// <summary>
        /// What the last check concluded, without touching the network. Null only
        /// before <see cref="CheckUpdateAsync"/> has run once in this process.
        /// </summary>
        public DAUpdateStatus? LastUpdateStatus => _updateStatus;

        /// <summary>
        /// Asks GitHub for the latest release, at most once every
        /// <see cref="UpdateCheckInterval"/> unless <paramref name="force"/> says
        /// otherwise, and never downgrades what is already known: a check that
        /// fails reports <see cref="DAUpdateCheckState.CheckFailed"/> while keeping
        /// the release it last saw. Reporting a failure as "up to date" is what
        /// made a detected update vanish from the settings minutes after it
        /// appeared.
        /// </summary>
        public async Task<DAUpdateStatus> CheckUpdateAsync(bool force = false)
        {
            var known = _updateStatus ?? LoadPersistedUpdateStatus();

            if (!force
                && known.State != DAUpdateCheckState.CheckFailed
                && known.CheckedUtc is DateTime last
                && DateTime.UtcNow - last < UpdateCheckInterval)
            {
                _updateStatus = known;
                return known;
            }

            GitHubRelease? release = null;
            try
            {
                var response = await UpdateHttpClient.GetAsync(
                    $"https://api.github.com/repos/YannCharbon/DiceAudio/releases/latest");

                if (response.IsSuccessStatusCode)
                {
                    release = JsonSerializer.Deserialize<GitHubRelease>(
                        await response.Content.ReadAsStringAsync(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                else
                {
                    Debug.WriteLine($"Update check refused: {(int)response.StatusCode} {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
            }

            if (release == null || string.IsNullOrWhiteSpace(release.tag_name))
            {
                _updateStatus = known with { State = DAUpdateCheckState.CheckFailed };
                return _updateStatus;
            }

            _localPreferences.Set(PrefUpdateLatestTag, release.tag_name);
            _localPreferences.Set(PrefUpdateLatestUrl, release.html_url ?? string.Empty);
            _localPreferences.Set(PrefUpdateCheckedUtc, DateTime.UtcNow.Ticks);

            _updateStatus = new DAUpdateStatus(
                IsNewerThanInstalled(release.tag_name)
                    ? DAUpdateCheckState.UpdateAvailable
                    : DAUpdateCheckState.UpToDate,
                release,
                DateTime.UtcNow);

            return _updateStatus;
        }

        /// The answer from a previous run, so a cold start with no network still
        /// shows what was last true instead of claiming the app is up to date.
        private DAUpdateStatus LoadPersistedUpdateStatus()
        {
            string tag = _localPreferences.Get(PrefUpdateLatestTag, string.Empty);
            long ticks = _localPreferences.Get(PrefUpdateCheckedUtc, 0L);

            if (string.IsNullOrWhiteSpace(tag) || ticks == 0)
            {
                return new DAUpdateStatus(DAUpdateCheckState.NotChecked, null, null);
            }

            var release = new GitHubRelease
            {
                tag_name = tag,
                html_url = _localPreferences.Get(PrefUpdateLatestUrl, string.Empty),
            };

            return new DAUpdateStatus(
                IsNewerThanInstalled(tag)
                    ? DAUpdateCheckState.UpdateAvailable
                    : DAUpdateCheckState.UpToDate,
                release,
                new DateTime(ticks, DateTimeKind.Utc));
        }

        /// Compares the tag against this build. A tag that will not parse is
        /// treated as newer only when it differs from the installed version, so a
        /// release named something unexpected is surfaced rather than swallowed.
        private bool IsNewerThanInstalled(string tag)
        {
            string remote = tag.TrimStart('v', 'V');
            string installed = GetVersion();

            if (Version.TryParse(remote, out var remoteVersion)
                && Version.TryParse(installed, out var installedVersion))
            {
                return remoteVersion > installedVersion;
            }

            return !string.Equals(remote, installed, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>How a check for a newer release turned out.</summary>
    public enum DAUpdateCheckState
    {
        /// Nothing has been asked yet, in this run or any previous one.
        NotChecked,
        UpToDate,
        UpdateAvailable,
        /// The network or the GitHub API refused. Deliberately distinct from
        /// UpToDate so a rate-limited reply cannot erase a real answer.
        CheckFailed,
    }

    /// <summary>The conclusion, the release it is about, and when it was reached.</summary>
    public sealed record DAUpdateStatus(DAUpdateCheckState State, GitHubRelease? Release, DateTime? CheckedUtc);

    public class GitHubRelease
    {
        public string tag_name { get; set; } = "";
        public string html_url { get; set; } = "";
    }
}
