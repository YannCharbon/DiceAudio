/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System;
using System.Diagnostics;
using DiceAudio.Storage;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DiceAudio
{
    public enum YtDlpUpdateOutcome
    {
        /// <summary>Nothing was attempted (unsupported platform, or the user skipped).</summary>
        Skipped,
        UpToDate,
        Updated,
        Failed
    }

    /// <summary>Result of one update run — what happened and what to tell the user.</summary>
    public record YtDlpUpdateReport(
        YtDlpUpdateOutcome Outcome,
        string? InstalledVersion,
        string? LatestVersion,
        string Message);

    /// <summary>One step of the update, for the startup splash.</summary>
    public readonly record struct YtDlpUpdateProgress(string Message, int? Percent);

    /// <summary>
    /// Keeps the bundled yt-dlp binary current. YouTube breaks yt-dlp every few
    /// weeks, so the version shipped inside a DiceAudio release goes stale long
    /// before the next one; this downloads the latest yt-dlp.exe from GitHub into
    /// the (writable) app data folder, which <see cref="YtDlpService"/> then
    /// prefers over the bundled copy.
    ///
    /// The check runs at startup but at most twice a day — see
    /// <see cref="IsUpdateCheckDue"/>.
    /// </summary>
    public class YtDlpUpdateService
    {
        /// <summary>Timestamp (round-trip UTC) of the last completed check.</summary>
        private const string PrefLastCheck = "YtDlpLastUpdateCheckUtc";

        /// <summary>Whether that last check failed, which shortens the retry delay.</summary>
        private const string PrefLastCheckFailed = "YtDlpLastUpdateCheckFailed";

        /// <summary>Whether the startup check may run at all (Settings).</summary>
        private const string PrefAutoUpdate = "YtDlpAutoUpdateEnabled";

        /// <summary>At most two checks per day.</summary>
        public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);

        /// <summary>A check that failed (offline, GitHub down) is retried sooner.</summary>
        public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(2);

        /// <summary>
        /// The stable channel. The binary that ships with DiceAudio comes from the
        /// nightly channel (the Soenneker.Libraries.ytdlp package), whose version
        /// numbers carry a build suffix (2026.08.20.234504) and run ahead of the
        /// latest stable tag; <see cref="IsNewer"/> compares them numerically, so a
        /// stable release only replaces it once it really is newer. Set this to
        /// yt-dlp/yt-dlp-nightly-builds to follow the nightly channel instead.
        /// </summary>
        private const string Repository = "yt-dlp/yt-dlp";

        /// <summary>
        /// Deliberately the web redirect and not api.github.com: the unauthenticated
        /// API allows only 60 requests per hour per IP, shared with every other tool
        /// on the machine and with DiceAudio's own release check, and answers 403
        /// once that is used up. This URL answers 302 to /releases/tag/&lt;version&gt;
        /// and carries no such quota.
        /// </summary>
        private const string LatestReleaseUrl = $"https://github.com/{Repository}/releases/latest";

        private const string AssetName = "yt-dlp.exe";

        /// <summary>Cap on running the local binary to read its version.</summary>
        private static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(20);

        /// <summary>
        /// Cap on reaching GitHub, for both the version lookup and the start of the
        /// download. Short on purpose: these are tiny requests, and this is the only
        /// delay a network that is up but not working can impose on startup.
        /// </summary>
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);

        /// <summary>
        /// Cap on going without a single received byte mid-download. This replaces a
        /// total download timeout on purpose: a slow line that keeps making progress
        /// is allowed to finish, while a connection that dies (Wi-Fi drops, cable
        /// pulled) is abandoned instead of waiting on a dead socket.
        /// </summary>
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(20);

        private readonly YtDlpService _ytDlp;
        private readonly ILocalPreferences _localPreferences;

        public YtDlpUpdateService(YtDlpService ytDlp, ILocalPreferences localPreferences)
        {
            _ytDlp = ytDlp;
            _localPreferences = localPreferences;
        }

        /// <summary>Only the Windows build runs yt-dlp.exe, so only it can update one.</summary>
        public static bool IsSupported => OperatingSystem.IsWindows();

        /// <summary>
        /// Whether the startup check runs. On by default: a stale yt-dlp stops being
        /// able to download from YouTube, so staying current is the useful default,
        /// and Settings lets it be turned off.
        /// </summary>
        public bool AutoUpdateEnabled
        {
            get => _localPreferences.Get(PrefAutoUpdate, true);
            set => _localPreferences.Set(PrefAutoUpdate, value);
        }

        /// <summary>
        /// True when the yt-dlp in use is a downloaded update rather than the copy
        /// that shipped with the application.
        /// </summary>
        public bool IsUsingDownloadedBinary => File.Exists(_ytDlp.ManagedExecutablePath);

        /// <summary>
        /// The version of the yt-dlp currently in use, or null when it cannot be run.
        /// Bounded, so a yt-dlp that hangs (antivirus inspecting the first run, slow
        /// disk) cannot hold up the caller.
        /// </summary>
        public async Task<string?> ReadInstalledVersionAsync(CancellationToken cancellationToken = default)
        {
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probe.CancelAfter(VersionProbeTimeout);
            try
            {
                return await _ytDlp.GetYtDlpVersionAsync(probe.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        /// <summary>When the last check ran, or null if it never did.</summary>
        public DateTime? LastCheckUtc =>
            DateTime.TryParse(_localPreferences.Get(PrefLastCheck, string.Empty),
                              CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last)
                ? last.ToUniversalTime()
                : null;

        /// <summary>
        /// True when startup should spend time on a check: never checked yet, or
        /// the last one is older than the interval. A clock that moved backwards
        /// also counts as due rather than postponing the check indefinitely.
        ///
        /// Answers false when the machine reports no usable internet, so the
        /// common offline cases cost nothing at all and the splash never appears.
        /// Nothing is recorded in that case, so the next start tries again.
        /// </summary>
        public bool IsUpdateCheckDue()
        {
            if (!IsSupported) return false;
            if (!AutoUpdateEnabled) return false;
            if (!HasInternet()) return false;

            var last = LastCheckUtc;
            if (last == null) return true;

            var interval = _localPreferences.Get(PrefLastCheckFailed, false) ? RetryInterval : CheckInterval;
            var age = DateTime.UtcNow - last.Value;
            return age >= interval || age < TimeSpan.Zero;
        }

        /// <summary>
        /// Whether Windows considers the machine to have internet access. This is
        /// an instant, local answer (no traffic), and it already covers the cases
        /// that would otherwise cost a connect timeout: airplane mode, no adapter,
        /// a router with no uplink (reported as Local) and a captive portal
        /// (reported as ConstrainedInternet). A platform that cannot answer is
        /// assumed online, so a quirk there cannot disable updates for good.
        /// </summary>
        private static bool HasInternet()
        {
            try
            {
                return Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not read the network state: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Compares the installed yt-dlp with the latest GitHub release and
        /// replaces it when a newer one exists. Never throws: a failed update
        /// leaves the previous binary in place and is reported, not fatal.
        /// </summary>
        public async Task<YtDlpUpdateReport> CheckAndUpdateAsync(
            IProgress<YtDlpUpdateProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsSupported)
                return new(YtDlpUpdateOutcome.Skipped, null, null, "yt-dlp updates are only available on Windows.");

            // The startup path is gated on this already, so this is for a check the
            // user asked for by hand: it answers at once instead of spending the
            // connect timeout on a network that is known not to work. Nothing is
            // recorded, because no check actually happened.
            if (!HasInternet())
                return new(YtDlpUpdateOutcome.Failed, null, null, "No internet connection.");

            string? installed = null;
            try
            {
                progress?.Report(new("Checking the yt-dlp version...", null));

                // An unknown version (the read timed out, or the binary will not
                // run) simply means the update goes ahead.
                installed = await ReadInstalledVersionAsync(cancellationToken);

                progress?.Report(new("Looking for a newer yt-dlp...", null));
                var (latest, downloadUrl) = await GetLatestReleaseAsync(cancellationToken);
                if (latest == null || downloadUrl == null)
                {
                    RecordCheck(succeeded: false);
                    return new(YtDlpUpdateOutcome.Failed, installed, null,
                        "Could not read the latest yt-dlp release from GitHub.");
                }

                if (!IsNewer(latest, installed))
                {
                    RecordCheck(succeeded: true);
                    return new(YtDlpUpdateOutcome.UpToDate, installed, latest,
                        $"yt-dlp {installed ?? latest} is up to date.");
                }

                var report = await InstallAsync(latest, downloadUrl, progress, cancellationToken);
                RecordCheck(succeeded: report.Outcome != YtDlpUpdateOutcome.Failed);
                return report;
            }
            catch (OperationCanceledException)
            {
                // The user pressed "Continue" on the splash — not a failure, and
                // not a completed check either, so the next startup tries again.
                return new(YtDlpUpdateOutcome.Skipped, installed, null, "yt-dlp update skipped.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"yt-dlp update check failed: {ex}");
                RecordCheck(succeeded: false);
                return new(YtDlpUpdateOutcome.Failed, installed, null, $"yt-dlp update failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Downloads the asset next to the managed binary and swaps it in, keeping
        /// the previous copy until the new one has proven that it runs.
        /// </summary>
        private async Task<YtDlpUpdateReport> InstallAsync(
            string version,
            string downloadUrl,
            IProgress<YtDlpUpdateProgress>? progress,
            CancellationToken cancellationToken)
        {
            var target = _ytDlp.ManagedExecutablePath;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var temporary = target + ".part";
            var backup = target + ".bak";

            progress?.Report(new($"Downloading yt-dlp {version}...", 0));
            await DownloadAsync(downloadUrl, temporary, version, progress, cancellationToken);

            // Keep the copy that is being replaced: a binary that downloads but
            // cannot run (truncated file, blocked by security software) must not
            // cost the user the yt-dlp they already had.
            bool hadPrevious = File.Exists(target);
            try
            {
                if (hadPrevious) File.Move(target, backup, overwrite: true);
                File.Move(temporary, target, overwrite: true);
            }
            catch (Exception ex)
            {
                SafeDelete(temporary);
                if (hadPrevious && !File.Exists(target) && File.Exists(backup))
                    File.Move(backup, target, overwrite: true);
                return new(YtDlpUpdateOutcome.Failed, null, version,
                    $"Could not replace yt-dlp: {ex.Message}");
            }

            progress?.Report(new($"Verifying yt-dlp {version}...", null));

            // Deliberately not cancellable: the binary is already in place, so
            // the run that proves it works has to finish (or time out) or the
            // rollback below would never happen. A binary that hangs instead of
            // printing its version counts as broken.
            string? running = null;
            using (var verification = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                try { running = await _ytDlp.GetYtDlpVersionAsync(verification.Token); }
                catch (OperationCanceledException) { /* timed out — handled as a failure */ }
            }

            if (running == null)
            {
                SafeDelete(target);
                if (hadPrevious && File.Exists(backup))
                    File.Move(backup, target, overwrite: true);
                return new(YtDlpUpdateOutcome.Failed, null, version,
                    $"yt-dlp {version} was downloaded but would not run — kept the previous version.");
            }

            SafeDelete(backup);
            return new(YtDlpUpdateOutcome.Updated, running, version, $"yt-dlp updated to {running}.");
        }

        private static async Task DownloadAsync(
            string url,
            string destination,
            string version,
            IProgress<YtDlpUpdateProgress>? progress,
            CancellationToken cancellationToken)
        {
            // The asset URL redirects to a short-lived signed URL, so this one
            // does follow redirects. The client timeout stays long because the
            // transfer is bounded by StallTimeout below, not by a total deadline.
            using var http = CreateClient(TimeSpan.FromMinutes(30), followRedirects: true);

            // Only the wait for the response to begin is bounded. A linked token
            // carrying that deadline would stay armed and abort a healthy transfer
            // once it elapsed, so the deadline is raced against the request and
            // only cancels it if it wins.
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var request = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, connect.Token);

            if (await Task.WhenAny(request, Task.Delay(ConnectTimeout, CancellationToken.None)) != request)
            {
                connect.Cancel();
                _ = request.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
                throw new IOException(
                    $"GitHub did not start the download within {ConnectTimeout.TotalSeconds:0} s.");
            }

            using var response = await request;
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(await DescribeFailureAsync(response, url, cancellationToken));

            var total = response.Content.Headers.ContentLength;
            using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            using (var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[128 * 1024];
                long written = 0;
                int lastPercent = -1;
                while (true)
                {
                    // HttpClient.Timeout stops applying once the headers are in, so
                    // without this a connection that dies mid-transfer would leave
                    // the read waiting on a dead socket and hang the splash.
                    int read;
                    using (var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        stall.CancelAfter(StallTimeout);
                        try
                        {
                            read = await source.ReadAsync(buffer, stall.Token);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            throw new IOException(
                                $"The download received nothing for {StallTimeout.TotalSeconds:0} s " +
                                $"and was abandoned after {written / (1024 * 1024)} MiB.");
                        }
                    }

                    if (read == 0) break;

                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;

                    if (total is > 0)
                    {
                        int percent = (int)(written * 100 / total.Value);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress?.Report(new($"Downloading yt-dlp {version}...", percent));
                        }
                    }
                    else
                    {
                        progress?.Report(new($"Downloading yt-dlp {version} ({written / (1024 * 1024)} MiB)...", null));
                    }
                }
            }

            if (new FileInfo(destination).Length == 0)
                throw new IOException("The downloaded yt-dlp file is empty.");
        }

        /// <summary>
        /// Reads the latest version from the Location header of the /releases/latest
        /// redirect, and builds the asset URL from it. Every release publishes the
        /// asset under the same name, so no asset listing is needed.
        /// </summary>
        private static async Task<(string? Version, string? DownloadUrl)> GetLatestReleaseAsync(
            CancellationToken cancellationToken)
        {
            // The redirect itself is the answer, so it must not be followed.
            using var http = CreateClient(ConnectTimeout, followRedirects: false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);

            HttpResponseMessage response;
            try
            {
                response = await http.GetAsync(LatestReleaseUrl, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IOException(
                    $"GitHub did not answer within {ConnectTimeout.TotalSeconds:0} s.");
            }

            using (response)
            {
                var location = response.Headers.Location;
                if (location == null)
                    throw new HttpRequestException(
                        $"{(int)response.StatusCode} {response.ReasonPhrase} from {LatestReleaseUrl} " +
                        "(expected a redirect to the latest release tag).");

                // A captive portal answers any request with a redirect to its own
                // login page, which would otherwise be read as a version and sent
                // on to build a nonsense download URL.
                if (location.IsAbsoluteUri && !location.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                    throw new HttpRequestException(
                        $"The latest-release redirect pointed at {location.Host} instead of github.com " +
                        "(a captive portal or proxy is intercepting the connection).");

                // .../releases/tag/2026.08.19 — the tag is the last path segment.
                var path = (location.IsAbsoluteUri ? location.AbsolutePath : location.ToString()).TrimEnd('/');
                var version = Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]).Trim();

                // Every yt-dlp tag is a date, so a segment that does not even start
                // with a digit ("login", "auth") is not a release tag.
                if (version.Length == 0 || !char.IsDigit(version[0]))
                    throw new HttpRequestException($"Could not read a version from the redirect to {location}.");

                return (version, $"https://github.com/{Repository}/releases/download/{version}/{AssetName}");
            }
        }

        private static HttpClient CreateClient(TimeSpan timeout, bool followRedirects)
        {
            // Disposing the client disposes the handler with it.
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = followRedirects })
            {
                Timeout = timeout
            };
            // GitHub rejects requests that carry no User-Agent.
            http.DefaultRequestHeaders.UserAgent.TryParseAdd("DiceAudio/1.0");
            return http;
        }

        /// <summary>
        /// The status line alone hides why GitHub refused (a rate limit, a missing
        /// asset), so the first part of the body goes into the message too.
        /// </summary>
        private static async Task<string> DescribeFailureAsync(
            HttpResponseMessage response, string url, CancellationToken cancellationToken)
        {
            string detail = string.Empty;
            try
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(body))
                    detail = " — " + body.Trim().Replace('\n', ' ').Replace('\r', ' ');
                if (detail.Length > 220) detail = detail[..220] + "…";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not read the error body: {ex.Message}");
            }

            return $"{(int)response.StatusCode} {response.ReasonPhrase} from {url}{detail}";
        }

        /// <summary>
        /// yt-dlp versions are dates (2025.09.26), so they compare component-wise
        /// as numbers. Comparing rather than testing for inequality matters for a
        /// nightly build, which is ahead of the latest stable release and must not
        /// be downgraded on every startup.
        /// </summary>
        internal static bool IsNewer(string candidate, string? installed)
        {
            if (string.IsNullOrWhiteSpace(installed)) return true;

            var left = ParseVersion(candidate);
            var right = ParseVersion(installed);
            if (left == null || right == null)
                return !string.Equals(candidate.Trim(), installed.Trim(), StringComparison.OrdinalIgnoreCase);

            for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
            {
                long a = i < left.Length ? left[i] : 0;
                long b = i < right.Length ? right[i] : 0;
                if (a != b) return a > b;
            }
            return false;
        }

        private static long[]? ParseVersion(string version)
        {
            var parts = version.Trim().TrimStart('v', 'V').Split('.');
            var numbers = new long[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                    return null;
            }
            return numbers;
        }

        private void RecordCheck(bool succeeded)
        {
            _localPreferences.Set(PrefLastCheck, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            _localPreferences.Set(PrefLastCheckFailed, !succeeded);
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Debug.WriteLine($"Could not delete {path}: {ex.Message}"); }
        }
    }
}
