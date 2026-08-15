/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

namespace DiceAudio
{
    /// <summary>
    /// Platform helpers for importing existing audio files from disk:
    /// native file/folder pickers and embedded-metadata tag reading.
    /// </summary>
    public static class DAImportHelper
    {
        /// <summary>Audio file extensions accepted by the importer.</summary>
        public static readonly string[] AudioExtensions =
            { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".wma" };

        public static bool IsAudioFile(string path) =>
            AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        /// <summary>Opens the native multi-file picker filtered to audio files.</summary>
        public static async Task<IReadOnlyList<string>> PickFilesAsync()
        {
            var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, AudioExtensions },
                { DevicePlatform.Android, new[] { "audio/*" } },
            });

            try
            {
                var results = await FilePicker.Default.PickMultipleAsync(new PickOptions
                {
                    PickerTitle = "Pick audio files to import",
                    FileTypes = fileTypes,
                });
                return results.Select(r => r.FullPath).Where(IsAudioFile).ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>Folder picking is only available on Windows.</summary>
        public static bool CanPickFolder =>
#if WINDOWS
            true;
#else
            false;
#endif

        /// <summary>Opens the native folder picker (Windows). Returns null if cancelled.</summary>
        public static async Task<string?> PickFolderAsync()
        {
#if WINDOWS
            try
            {
                var picker = new Windows.Storage.Pickers.FolderPicker();
                picker.FileTypeFilter.Add("*");

                var mauiWindow = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
                                 as Microsoft.UI.Xaml.Window;
                if (mauiWindow == null) return null;
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mauiWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var folder = await picker.PickSingleFolderAsync();
                return folder?.Path;
            }
            catch
            {
                return null;
            }
#else
            await Task.CompletedTask;
            return null;
#endif
        }

        /// <summary>
        /// Reads the tag names embedded in the file's Genre metadata — the format
        /// the old TRPG-Audio-Manager wrote (one genre entry per tag name).
        /// Returns an empty list when the file has no tags or cannot be parsed.
        /// </summary>
        public static List<string> ReadEmbeddedTagNames(string filePath)
        {
            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                return tagFile.Tag.Genres
                    .Select(g => g.Trim())
                    .Where(g => !string.IsNullOrEmpty(g))
                    .Distinct()
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Returns a file name that does not collide with anything in the Audio
        /// library directory ("Name.mp3" → "Name (2).mp3" …).
        /// </summary>
        public static string GetUniqueLibraryFileName(string originalFileName)
        {
            var audioDir = Path.Combine(FileSystem.AppDataDirectory, "Audio");
            Directory.CreateDirectory(audioDir);

            var baseName = Path.GetFileNameWithoutExtension(originalFileName);
            var extension = Path.GetExtension(originalFileName);

            var candidate = baseName + extension;
            int counter = 2;
            while (File.Exists(Path.Combine(audioDir, candidate)))
            {
                candidate = $"{baseName} ({counter}){extension}";
                counter++;
            }
            return candidate;
        }
    }
}
