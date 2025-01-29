using BootstrapBlazor.Components;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceAudio
{
    public class YtDlpService
    {
        private readonly string _baseDirectory;
        private readonly string _ytDlpPath;
        private readonly string _ffmpegPath;
        private readonly string _audioFilesDirectory;
        private Process? _process;

        public YtDlpService()
        {
            // Base directory for the application
            _baseDirectory = AppContext.BaseDirectory;

            // Paths to the executables in the "Resources" subfolder
            _ytDlpPath = Path.Combine(_baseDirectory, "Resources", "yt-dlp.exe");
            _ffmpegPath = Path.Combine(_baseDirectory, "Resources", "ffmpeg.exe");

            _audioFilesDirectory = Path.Combine(FileSystem.AppDataDirectory, "Audio");
            Directory.CreateDirectory(_audioFilesDirectory);
        }

        public async Task<string> GetVideoTitleAsync(string youtubeUrl)
        {
            var tcs = new TaskCompletionSource<string>();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = $"--print title {youtubeUrl}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.OutputDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    tcs.TrySetResult(args.Data); // Capture the title
                }
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    tcs.TrySetResult($"YtDLP Error: {args.Data}");
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetException(new Exception("No title was returned from yt-dlp."));
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                process.Dispose();
            }

            return await tcs.Task;
        }

        public async Task<List<string>> GetPlaylistVideoUrlsAsync(string playlistUrl)
        {
            var videoUrls = new List<string>();
            var tcs = new TaskCompletionSource();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = $"--flat-playlist --print url \"{playlistUrl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.OutputDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    videoUrls.Add(args.Data);
                }
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    //tcs.TrySetException(new Exception($"Error: {args.Data}"));
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
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                process.Dispose();
            }

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
                    FileName = _ytDlpPath,
                    Arguments = $"--flat-playlist --print title --print url \"{playlistUrl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.OutputDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    if (pendingRequest == false)
                    {
                        videoTitles.Add(args.Data);
                        pendingRequest = true;
                    } else
                    {
                        videoUrls.Add(args.Data);
                        pendingRequest = false;
                    }
                }
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    //tcs.TrySetException(new Exception($"Error: {args.Data}"));
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
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                process.Dispose();
            }

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

            string arguments = $"--extract-audio --audio-format mp3 --ffmpeg-location \"{_ffmpegPath}\" -o \"{Path.Combine(_audioFilesDirectory, "%(title)s.%(ext)s")}\" {item.SourceURL}";

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            item.SourceIsDownloading = true;

            _process.OutputDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    args.Data.Trim();
                    Debug.WriteLine(args.Data);
                    if (args.Data.EndsWith(".mp3"))
                    {
                        item.LocalFileName = args.Data.Split("\\").Last();
                    }
                    // Extract progress percentage from yt-dlp output
                    string progressStr = args.Data.Split("[download]  ").Last().Split("%").First();
                    if (double.TryParse(progressStr, NumberStyles.Number, CultureInfo.InvariantCulture, out double parsedProgress))
                    {
                        item.SourceDownloadProgress = (int)parsedProgress;
                    }

                    item.SourceDownloadStatusMessage = args.Data;

                    reportStatus(args.Data); // Report status to the app
                }
            };

            _process.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    reportStatus($"Error: {args.Data}"); // Report errors to the app
                }
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
                        item.SourceDownloadStatusMessage = "Done";
                        item.IsLocallyAvailable = true;
                    } else
                    {
                        item.SourceDownloadStatusMessage = $"Error: {_process.ExitCode}";
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
                _process.Kill(true); // Kill the process and any child processes
                reportStatus("Download aborted.");
                _process.Dispose();
                _process = null;
            }
            else
            {
                reportStatus("No download in progress to abort.");
            }
        }
    }
}
