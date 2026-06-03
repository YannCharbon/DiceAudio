using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Diagnostics;
using static MudBlazor.FilterOperator;
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

        readonly IAudioManager audioManager;

        public DiceAudioService(IAudioManager audioManage)
        {
            this.audioManager = audioManager;

            LoadAudioItemListAsync();
            LoadAudioVirtualFoldersListAsync();
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

        private static string audioItemsFilePath = Path.Combine(FileSystem.AppDataDirectory, "audioItems.json");

        public async Task SaveAudioItemListAsync()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultBufferSize = 15 * 1024 * 1024 // 15MiB
            };
            string json = JsonSerializer.Serialize(AudioItems, options);
            await File.WriteAllTextAsync(audioItemsFilePath, json);
            Debug.WriteLine($"Write file to {audioItemsFilePath}");
        }

        public async Task LoadAudioItemListAsync()
        {
            if (File.Exists(audioItemsFilePath))
            {
                Debug.WriteLine("File exists");
                string json = await File.ReadAllTextAsync(audioItemsFilePath);
                var options = new JsonSerializerOptions
                {
                    DefaultBufferSize = 15 * 1024 * 1024 // 15MiB
                };
                var temp = JsonSerializer.Deserialize<List<DAAudioItem>>(json, options);
                if (temp != null)
                {
                    AudioItems = temp;
                }
            }
        }

        private static string audioVirtualFoldersFilePath = Path.Combine(FileSystem.AppDataDirectory, "audioVirtualFolders.json");

        public async Task SaveAudioVirtualFoldersAsync()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultBufferSize = 15 * 1024 * 1024 // 15MiB
            };
            string json = JsonSerializer.Serialize(AudioVirtualFolders, options);
            await File.WriteAllTextAsync(audioVirtualFoldersFilePath, json);
            Debug.WriteLine($"Write file to {audioVirtualFoldersFilePath}");
        }

        public async Task LoadAudioVirtualFoldersListAsync()
        {
            if (File.Exists(audioVirtualFoldersFilePath))
            {
                Debug.WriteLine("File exists");
                string json = await File.ReadAllTextAsync(audioVirtualFoldersFilePath);
                var options = new JsonSerializerOptions
                {
                    DefaultBufferSize = 15 * 1024 * 1024 // 15MiB
                };
                var temp = JsonSerializer.Deserialize<List<DAVirtualAudioFolder>>(json, options);
                if (temp != null)
                {
                    AudioVirtualFolders = temp;
                }
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
