/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using BootstrapBlazor.Components;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using DiceAudio.Storage;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceAudio
{
    public class YtDlpService
    {
        private readonly string _baseDirectory;
        private readonly string _bundledYtDlpPath;
        private readonly string _ffmpegPath;
        private readonly DataLocation _dataLocation;

        /// The audio folder, worked out on each call: the data folder it sits
        /// in can change while the app runs.
        private string AudioFilesDirectory => _dataLocation.AudioDir;
        private Process? _process;

        /// <summary>
        /// Recent yt-dlp requires a JavaScript runtime for full YouTube extraction
        /// and only auto-detects deno by default. Enabling node and bun as well lets
        /// yt-dlp use whichever runtime is installed on the machine.
        /// </summary>
        private const string JsRuntimesArgs = "--js-runtimes deno --js-runtimes node --js-runtimes bun";

        /// <summary>
        /// Where <see cref="YtDlpUpdateService"/> keeps the yt-dlp it downloads.
        /// The bundled binary sits in the install folder, which is read-only for a
        /// packaged (MSIX) app, so updates land in the app data folder instead.
        /// </summary>
        public string ManagedExecutablePath =>
            _dataLocation.GetLocalPath(Path.Combine("Tools", "yt-dlp.exe"));

        /// <summary>The yt-dlp that ships inside the application package.</summary>
        public string BundledExecutablePath => _bundledYtDlpPath;

        /// <summary>
        /// The binary every yt-dlp call runs: the downloaded one when it exists,
        /// otherwise the bundled one. Resolved per call so an update that lands
        /// during this session takes effect immediately.
        /// </summary>
        public string ExecutablePath =>
            File.Exists(ManagedExecutablePath) ? ManagedExecutablePath : _bundledYtDlpPath;

        public YtDlpService(DataLocation dataLocation)
        {
            _dataLocation = dataLocation;
            _baseDirectory = AppContext.BaseDirectory;
            _bundledYtDlpPath = Path.Combine(_baseDirectory, "Resources", "yt-dlp.exe");
            _ffmpegPath = Path.Combine(_baseDirectory, "Resources", "ffmpeg.exe");
        }

        /// <summary>
        /// Runs <c>yt-dlp --version</c>. Returns null when the binary is missing
        /// or cannot run, which is how the updater tells a usable download from a
        /// broken one.
        /// </summary>
        public async Task<string?> GetYtDlpVersionAsync(CancellationToken cancellationToken = default)
        {
            var path = ExecutablePath;
            if (!File.Exists(path)) return null;

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                }
            };

            try
            {
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0) return null;

                var version = output.Trim();
                return string.IsNullOrEmpty(version) ? null : version;
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not read the yt-dlp version: {ex.Message}");
                return null;
            }
        }

        public async Task<string> GetVideoTitleAsync(string youtubeUrl)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ExecutablePath,
                    Arguments = $"{JsRuntimesArgs} --print title \"{youtubeUrl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                }
            };

            string? title = null;
            string? fatalError = null;

            process.OutputDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data) && title == null)
                    title = args.Data;
            };
            process.ErrorDataReceived += (sender, args) =>
            {
                if (string.IsNullOrEmpty(args.Data)) return;
                Debug.WriteLine("[yt-dlp stderr] " + args.Data);
                // yt-dlp writes WARNINGs to stderr too (e.g. the missing-JS-runtime
                // notice) while still producing a valid title on stdout — only
                // actual ERROR lines are fatal.
                if (fatalError == null &&
                    args.Data.TrimStart().StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                    fatalError = args.Data;
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                return $"YtDLP Error: {ex.Message}";
            }
            finally { process.Dispose(); }

            if (title != null) return title;
            return $"YtDLP Error: {fatalError ?? "No title was returned from yt-dlp."}";
        }

        public async Task<List<string>> GetPlaylistVideoUrlsAsync(string playlistUrl)
        {
            var videoUrls = new List<string>();
            var tcs = new TaskCompletionSource();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ExecutablePath,
                    Arguments = $"{JsRuntimesArgs} --flat-playlist --print url \"{playlistUrl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                }
            };
            process.OutputDataReceived += (sender, args) => { if (!string.IsNullOrEmpty(args.Data)) videoUrls.Add(args.Data); };
            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();
                tcs.TrySetResult();
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
            finally { process.Dispose(); }
            await tcs.Task;
            return videoUrls;
        }

        public async Task<(List<string>, List<string>)> GetPlaylistVideoTitlesAndUrlsAsync(string playlistUrl)
        {
            var videoTitles = new List<string>();
            var videoUrls = new List<string>();
            var tcs = new TaskCompletionSource();
            bool pendingRequest = false;
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ExecutablePath,
                    Arguments = $"{JsRuntimesArgs} --flat-playlist --print title --print url \"{playlistUrl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                }
            };
            process.OutputDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    if (!pendingRequest) { videoTitles.Add(args.Data); pendingRequest = true; }
                    else { videoUrls.Add(args.Data); pendingRequest = false; }
                }
            };
            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();
                tcs.TrySetResult();
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
            finally { process.Dispose(); }
            await tcs.Task;
            return (videoTitles, videoUrls);
        }

        public async Task RunYtDlpAsync(DAAudioItem item, Action<string> reportStatus)
        {
            if (_process != null)
            {
                reportStatus("Another download is already in progress.");
                return;
            }

            if (_dataLocation.State != DataLocationState.Ok)
            {
                reportStatus(DiceAudio.Resources.Localization.Portability.DataFolderUnusable);
                return;
            }

            // Created here rather than at startup: the audio folder is part of
            // the data folder, which is only written once it is known to be usable.
            Directory.CreateDirectory(AudioFilesDirectory);

            string arguments = $"{JsRuntimesArgs} --extract-audio --audio-format mp3 --ffmpeg-location \"{_ffmpegPath}\" -o \"{Path.Combine(AudioFilesDirectory, "%(title)s.%(ext)s")}\" \"{item.SourceURL}\"";

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ExecutablePath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // yt-dlp emits UTF-8. Without this the redirected stream is decoded
                    // with the OS code page, mangling non-ASCII characters in the parsed
                    // "Destination:" filename so it no longer matches the file on disk.
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                }
            };

            item.SourceIsDownloading = true;

            // Snapshot existing MP3s before download so we can detect the new one
            var existingFiles = new HashSet<string>(
                Directory.GetFiles(AudioFilesDirectory, "*.mp3").Select(f => Path.GetFileName(f)),
                StringComparer.OrdinalIgnoreCase);

            // Parse filename from any yt-dlp output line (works for stdout and stderr)
            void TryParseFilename(string? data)
            {
                if (string.IsNullOrEmpty(data) || !string.IsNullOrEmpty(item.LocalFileName)) return;

                // Pattern: "[ExtractAudio] Destination: /path/to/file.mp3"
                if (data.Contains("Destination:"))
                {
                    var part = data.Split("Destination:").Last().Trim();
                    if (part.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                    {
                        item.LocalFileName = Path.GetFileName(part);
                        return;
                    }
                }

                // Pattern: "[download] /path/to/file.mp3 has already been downloaded"
                if (data.Contains("has already been downloaded"))
                {
                    var part = data.Replace("[download]", "").Trim();
                    var filePart = part.Split(" has already been downloaded").First().Trim();
                    if (filePart.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                    {
                        item.LocalFileName = Path.GetFileName(filePart);
                        return;
                    }
                }
            }

            _process.OutputDataReceived += (sender, args) =>
            {
                if (string.IsNullOrEmpty(args.Data)) return;
                Debug.WriteLine("[yt-dlp stdout] " + args.Data);
                TryParseFilename(args.Data);

                // Progress extraction
                string progressStr = args.Data.Split("[download]  ").Last().Split("%").First();
                if (double.TryParse(progressStr, NumberStyles.Number, CultureInfo.InvariantCulture, out double p))
                    item.SourceDownloadProgress = (int)p;

                item.SourceDownloadStatusMessage = args.Data;
                reportStatus(args.Data);
            };

            _process.ErrorDataReceived += (sender, args) =>
            {
                if (string.IsNullOrEmpty(args.Data)) return;
                Debug.WriteLine("[yt-dlp stderr] " + args.Data);
                TryParseFilename(args.Data);

                // None of the enabled JS runtimes is installed: tell the user how to fix it.
                if (args.Data.Contains("No supported JavaScript runtime"))
                {
                    item.SourceDownloadStatusMessage =
                        "yt-dlp needs a JavaScript runtime for YouTube: install Node.js (nodejs.org) or Deno (deno.com), then retry.";
                }

                reportStatus(args.Data);
            };

            try
            {
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                await _process.WaitForExitAsync();

                if (_process != null)
                {
                    reportStatus($"Process exited with code {_process.ExitCode}");

                    if (_process.ExitCode == 0)
                    {
                        // The mp3(s) that actually appeared on disk during this download.
                        var newFiles = Directory.GetFiles(AudioFilesDirectory, "*.mp3")
                            .Select(f => Path.GetFileName(f))
                            .Where(f => !existingFiles.Contains(f))
                            .ToList();

                        // Trust the file that really appeared over the console-parsed name:
                        // the parsed name can be wrong (sanitisation/encoding), which left
                        // the item pointing at a non-existent file ("File not found on disk").
                        bool parsedExists = !string.IsNullOrEmpty(item.LocalFileName)
                            && File.Exists(Path.Combine(AudioFilesDirectory, item.LocalFileName));

                        if (!parsedExists && newFiles.Count > 0)
                            item.LocalFileName = newFiles[0];

                        if (!string.IsNullOrEmpty(item.LocalFileName)
                            && File.Exists(Path.Combine(AudioFilesDirectory, item.LocalFileName)))
                        {
                            item.SourceDownloadStatusMessage = "Done";
                            item.IsLocallyAvailable = true;
                        }
                        else
                        {
                            item.SourceDownloadStatusMessage = "Download succeeded but file could not be located";
                        }
                    }
                    else
                    {
                        item.SourceDownloadStatusMessage = $"Error (exit code {_process.ExitCode})";
                        item.SourceDownloadProgress = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                reportStatus($"Exception: {ex.Message}");
                item.SourceDownloadStatusMessage = ex.Message;
                item.SourceDownloadProgress = 0;
            }
            finally
            {
                if (_process != null)
                {
                    _process.Dispose();
                    _process = null;
                }
            }

            item.SourceIsDownloading = false;
        }

        public void AbortDownload(Action<string> reportStatus)
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(true);
                reportStatus("Download aborted.");
                _process.Dispose();
                _process = null;
            }
            else
            {
                reportStatus("No download in progress to abort.");
            }
        }

        /// <summary>
        /// Scans the audio folder and tries to match MP3 files to items that have an empty LocalFileName.
        /// Returns the number of items repaired.
        /// </summary>
        public int RepairMissingLocalFileNames(List<DAAudioItem> audioItems)
        {
            if (!Directory.Exists(AudioFilesDirectory)) return 0;

            var allMp3s = Directory.GetFiles(AudioFilesDirectory, "*.mp3")
                .Select(f => Path.GetFileName(f))
                .ToList();

            // Build a set of already-claimed filenames
            var claimed = new HashSet<string>(
                audioItems.Where(a => !string.IsNullOrEmpty(a.LocalFileName)).Select(a => a.LocalFileName),
                StringComparer.OrdinalIgnoreCase);

            int repaired = 0;
            foreach (var item in audioItems)
            {
                // Verify / fix IsLocallyAvailable for items that already have a filename
                if (!string.IsNullOrEmpty(item.LocalFileName))
                {
                    var fullPath = Path.Combine(AudioFilesDirectory, item.LocalFileName);
                    item.IsLocallyAvailable = File.Exists(fullPath);
                    continue;
                }

                // Try to match by name (yt-dlp uses the video title as the filename)
                foreach (var mp3 in allMp3s)
                {
                    if (claimed.Contains(mp3)) continue;
                    var baseName = Path.GetFileNameWithoutExtension(mp3);
                    if (string.Equals(baseName, item.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        item.LocalFileName = mp3;
                        item.IsLocallyAvailable = true;
                        claimed.Add(mp3);
                        repaired++;
                        break;
                    }
                }
            }

            return repaired;
        }

        /// <summary>
        /// Returns all MP3 files in the audio folder that are not referenced by any existing audio item.
        /// </summary>
        public List<string> GetUnclaimedMp3Files(List<DAAudioItem> audioItems)
        {
            if (!Directory.Exists(AudioFilesDirectory)) return new();

            var claimed = new HashSet<string>(
                audioItems.Where(a => !string.IsNullOrEmpty(a.LocalFileName)).Select(a => a.LocalFileName),
                StringComparer.OrdinalIgnoreCase);

            return Directory.GetFiles(AudioFilesDirectory, "*.mp3")
                .Select(f => Path.GetFileName(f))
                .Where(f => !claimed.Contains(f))
                .ToList();
        }
    }
}
