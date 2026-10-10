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

namespace DiceAudio
{
    public class DiceAudioService
    {
        public List<DAAudioItem> AudioItems { get; set; } = new List<DAAudioItem>();
        public List<DAVirtualAudioFolder> AudioVirtualFolders { get; set; } = new List<DAVirtualAudioFolder>();
        public List<DAScene> Scenes { get; set; } = new List<DAScene>();
        public List<DAScenarioGroup> ScenarioGroups { get; set; } = new List<DAScenarioGroup>();
        public List<DAPreset> Presets { get; set; } = new List<DAPreset>();

        readonly IAudioManager audioManager;

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
        /// The lists are loaded in the background when the service is created.
        /// Until that is done, saving would write an empty list over a file that
        /// has not been read yet, so every save waits for this.
        /// </summary>
        private readonly Task _loaded;

        public bool IsLoaded => _loaded.IsCompleted;

        /// <summary>Completes once the persisted lists are in memory — await before reading them.</summary>
        public Task WhenLoaded => _loaded;

        public DiceAudioService(IAudioManager audioManager)
        {
            this.audioManager = audioManager;

            _loaded = LoadEverythingAsync();
        }

        private async Task LoadEverythingAsync()
        {
            try
            {
                await LoadAudioItemListAsync();
                await LoadAudioVirtualFoldersListAsync();
                await LoadScenarioGroupsAsync();
                await LoadScenesAsync();
                await LoadPresetsAsync();
            }
            catch (Exception ex)
            {
                // Starting with empty lists is better than not starting at all.
                Debug.WriteLine($"Initial load failed: {ex.Message}");
            }
        }

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

        private static string audioItemsFilePath = Path.Combine(FileSystem.AppDataDirectory, "audioItems.json");

        public async Task SaveAudioItemListAsync()
        {
            await _loaded;

            string json = JsonSerializer.Serialize(AudioItems, WriteOptions);
            await File.WriteAllTextAsync(audioItemsFilePath, json);
            Debug.WriteLine($"Write file to {audioItemsFilePath}");
        }

        public async Task LoadAudioItemListAsync()
        {
            if (File.Exists(audioItemsFilePath))
            {
                Debug.WriteLine("File exists");
                string json = await File.ReadAllTextAsync(audioItemsFilePath);
                var temp = JsonSerializer.Deserialize<List<DAAudioItem>>(json, ReadOptions);
                if (temp != null)
                {
                    AudioItems = temp;
                }
            }
        }

        private static string audioVirtualFoldersFilePath = Path.Combine(FileSystem.AppDataDirectory, "audioVirtualFolders.json");

        public async Task SaveAudioVirtualFoldersAsync()
        {
            await _loaded;

            string json = JsonSerializer.Serialize(AudioVirtualFolders, WriteOptions);
            await File.WriteAllTextAsync(audioVirtualFoldersFilePath, json);
            Debug.WriteLine($"Write file to {audioVirtualFoldersFilePath}");
        }

        public async Task LoadAudioVirtualFoldersListAsync()
        {
            if (File.Exists(audioVirtualFoldersFilePath))
            {
                Debug.WriteLine("File exists");
                string json = await File.ReadAllTextAsync(audioVirtualFoldersFilePath);
                var temp = JsonSerializer.Deserialize<List<DAVirtualAudioFolder>>(json, ReadOptions);
                if (temp != null)
                {
                    AudioVirtualFolders = temp;
                }
            }
        }

        private static string scenarioGroupsFilePath = Path.Combine(FileSystem.AppDataDirectory, "scenarioGroups.json");

        public async Task SaveScenarioGroupsAsync()
        {
            await _loaded;

            string json = JsonSerializer.Serialize(ScenarioGroups, WriteOptions);
            await File.WriteAllTextAsync(scenarioGroupsFilePath, json);
            Debug.WriteLine($"Write file to {scenarioGroupsFilePath}");
        }

        public async Task LoadScenarioGroupsAsync()
        {
            ScenarioGroups = await LoadListAsync<DAScenarioGroup>(scenarioGroupsFilePath) ?? ScenarioGroups;
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

        private static string scenesFilePath = Path.Combine(FileSystem.AppDataDirectory, "scenes.json");

        public async Task SaveScenesAsync()
        {
            await _loaded;
            await SaveListAsync(Scenes, scenesFilePath);
        }

        public async Task LoadScenesAsync()
        {
            Scenes = await LoadListAsync<DAScene>(scenesFilePath) ?? Scenes;
        }

        private static string presetsFilePath = Path.Combine(FileSystem.AppDataDirectory, "presets.json");

        public async Task SavePresetsAsync()
        {
            await _loaded;
            await SaveListAsync(Presets, presetsFilePath);
        }

        public async Task LoadPresetsAsync()
        {
            Presets = await LoadListAsync<DAPreset>(presetsFilePath) ?? Presets;
        }

        // Shared JSON list persistence helpers

        private static async Task SaveListAsync<T>(List<T> list, string filePath)
        {
            string json = JsonSerializer.Serialize(list, WriteOptions);
            await File.WriteAllTextAsync(filePath, json);
            Debug.WriteLine($"Write file to {filePath}");
        }

        private static async Task<List<T>?> LoadListAsync<T>(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            try
            {
                string json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<List<T>>(json, ReadOptions);
            }
            catch (Exception ex)
            {
                // A malformed/outdated file must never prevent the app from starting.
                Debug.WriteLine($"Failed to load {filePath}: {ex.Message}");
                return null;
            }
        }

        // Generic management

        public string GetVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            if (version != null)
            {
                return version.ToString().Substring(0, version.ToString().LastIndexOf("."));
            }

            return "0.0.0";
        }

        public async Task<GitHubRelease?> CheckUpdate()
        {
            HttpClient _httpClient = new HttpClient();

            string owner = "YannCharbon";
            string repo = "DiceAudio";

            string? token = null;

            string currentVersion = GetVersion();

            try
            {
                // Set GitHub API base URL
                string url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

                // Set User-Agent header (required by GitHub API)
                _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("DiceAudio/1.0");

                // Add token for authentication if provided
                if (!string.IsNullOrEmpty(token))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                // Make a GET request to fetch the latest release
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error fetching release: {response.StatusCode}");
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                var release = JsonSerializer.Deserialize<GitHubRelease>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (release != null)
                {
                    // Parse the current and remote version strings
                    if (Version.TryParse(currentVersion, out var localVersion) && Version.TryParse(release.tag_name.TrimStart('v'), out var remoteVersion))
                    {
                        if (remoteVersion > localVersion)
                        {
                            Debug.WriteLine($"More recent release available: {release.tag_name}");
                            return release;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Failed to parse version strings.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            return null; // No new release or error occurred
        }
    }

    public class GitHubRelease
    {
        public string tag_name { get; set; } = "";
        public string html_url { get; set; } = "";
    }
}
