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

namespace DiceAudio
{
    public class DiceAudioService
    {
        public List<DAAudioItem> AudioItems { get; set; } = new List<DAAudioItem>();

        public void AddAudioItem(DAAudioItem audioItem)
        {
            AudioItems.Add(audioItem);
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
