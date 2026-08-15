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
            var canonical = AudioVirtualFolders.FirstOrDefault(x => x.Name == audioItem.Folder.Name);
            if (canonical == null)
            {
                canonical = new DAVirtualAudioFolder(audioItem.Folder.Name);
                AudioVirtualFolders.Add(canonical);
            }
            audioItem.Folder = canonical;
            AudioItems.Add(audioItem);
        }

        /// <summary>
        /// Returns the virtual folder at the given path (e.g. ["SWTOR", "Combat"]),
        /// creating any missing folders along the way (nested via ParentFolderId).
        /// Folder names are globally unique in the app's model: an existing folder
        /// with a matching name is reused. Returns null for an empty path (root).
        /// </summary>
        public DAVirtualAudioFolder? GetOrCreateFolderPath(IEnumerable<string> segments)
        {
            DAVirtualAudioFolder? current = null;
            Guid parentId = Guid.Empty;

            foreach (var raw in segments)
            {
                var name = raw.Trim();
                if (string.IsNullOrEmpty(name) || name == ".") continue;

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
