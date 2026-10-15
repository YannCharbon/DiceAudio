/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Diagnostics;

namespace DiceAudio
{
    public enum DADownloadState { Waiting, Downloading, Done, AlreadyThere, Failed, Cancelled }

    /// <summary>What the queue needs from yt-dlp. <see cref="YtDlpService"/> in the app.</summary>
    public interface IDADownloadRunner
    {
        Task<string?> GetOutputFileNameAsync(string url, CancellationToken cancellationToken = default);
        Task RunYtDlpAsync(DAAudioItem item, Action<string> reportStatus, string? outputBaseName = null);
        void AbortDownload(Action<string> reportStatus);
    }

    /// <summary>
    /// What the queue needs from the library: <see cref="DADownloadLibrary"/>
    /// over DiceAudioService in the app. <see cref="AudioItems"/>,
    /// <see cref="AddAudioItem"/> and <see cref="SaveItemsAndFoldersAsync"/> are
    /// only used from inside <see cref="OnUiAsync{T}(Func{T})"/>.
    /// </summary>
    public interface IDADownloadLibrary
    {
        bool CanWriteData { get; }
        string AudioDir { get; }
        IReadOnlyList<DAAudioItem> AudioItems { get; }
        void AddAudioItem(DAAudioItem item, DAVirtualAudioFolder? folder);
        Task SaveItemsAndFoldersAsync();
        Task<T> OnUiAsync<T>(Func<T> read);
        Task OnUiAsync(Func<Task> action);
    }

    /// <summary>One link in the download list, and where its item goes once downloaded.</summary>
    public sealed class DADownloadJob
    {
        public DADownloadJob(DAAudioItem item, DAVirtualAudioFolder? targetFolder)
        {
            Item = item;
            TargetFolder = targetFolder;
        }

        /// <summary>The item being downloaded. It joins the library only once its file is there.</summary>
        public DAAudioItem Item { get; }

        /// <summary>The folder chosen when the link was added to the list; null = the root.</summary>
        public DAVirtualAudioFolder? TargetFolder { get; }

        public DADownloadState State { get; internal set; } = DADownloadState.Waiting;

        /// Set by <see cref="DADownloadQueue.CancelAll"/>; checked between the steps of the download.
        internal volatile bool CancelRequested;

        /// <summary>
        /// The item is already in the library (an imported item without its
        /// file): it is only downloaded, never added again, and never given the
        /// file of another item with the same link.
        /// </summary>
        public bool InLibrary { get; init; }

        public bool IsFinished => State is DADownloadState.Done or DADownloadState.AlreadyThere
                                         or DADownloadState.Failed or DADownloadState.Cancelled;
    }

    /// <summary>
    /// The download list. It lives as long as the app, so a download goes on
    /// when the downloader is closed, and the downloader only shows it.
    /// <list type="bullet">
    /// <item>One worker: one yt-dlp at a time, in the order the links were added.</item>
    /// <item>Before a file is written, its name is checked against the files
    /// of the other items, so two items never share a file.</item>
    /// <item>Each finished item joins the library, which is saved straight away.
    /// The library lists are only changed on the UI thread, where the pages and
    /// the saves read them.</item>
    /// </list>
    /// </summary>
    public sealed class DADownloadQueue
    {
        private readonly IDADownloadRunner _runner;
        private readonly IDADownloadLibrary _library;

        private readonly object _gate = new();
        private readonly List<DAAudioItem> _staged = new();
        private readonly List<DADownloadJob> _jobs = new();
        private Task? _worker;
        private DADownloadJob? _current;

        /// <summary>Progress lines arrive many times a second; the page is told at most this often.</summary>
        private static readonly TimeSpan ChangedThrottle = TimeSpan.FromMilliseconds(150);
        private long _lastChangedTicks;

        public DADownloadQueue(IDADownloadRunner runner, IDADownloadLibrary library)
        {
            _runner = runner;
            _library = library;
        }

        /// <summary>Raised when the list or a download's progress changes, on any thread.</summary>
        public event Action? Changed;

        /// <summary>Raised on the UI thread once a downloaded item has joined the library and been saved.</summary>
        public event Action? LibraryChanged;

        /// <summary>The links checked but not yet sent to the download list.</summary>
        public IReadOnlyList<DAAudioItem> Staged { get { lock (_gate) return _staged.ToList(); } }

        /// <summary>Every download of this session, oldest first, until the finished ones are cleared.</summary>
        public IReadOnlyList<DADownloadJob> Jobs { get { lock (_gate) return _jobs.ToList(); } }

        /// <summary>The download of this item that is waiting or running, if any.</summary>
        public DADownloadJob? ActiveJobFor(DAAudioItem item)
        {
            lock (_gate) return _jobs.FirstOrDefault(j => ReferenceEquals(j.Item, item) && !j.IsFinished);
        }

        /// <summary>True while a download is running or waiting.</summary>
        public bool IsBusy { get { lock (_gate) return _jobs.Any(j => !j.IsFinished); } }

        // ── Staging ────────────────────────────────────────────────────────

        /// <summary>Adds a checked link to the staged list, unless that link is already staged.</summary>
        public bool Stage(DAAudioItem item)
        {
            lock (_gate)
            {
                if (_staged.Any(s => s.SourceURL == item.SourceURL)) return false;
                _staged.Add(item);
            }
            RaiseChanged(force: true);
            return true;
        }

        public bool IsStaged(string url) { lock (_gate) return _staged.Any(s => s.SourceURL == url); }

        public void Unstage(DAAudioItem item)
        {
            lock (_gate) _staged.Remove(item);
            RaiseChanged(force: true);
        }

        public void ClearStaged()
        {
            lock (_gate) _staged.Clear();
            RaiseChanged(force: true);
        }

        /// <summary>
        /// Sends every staged link to the download list, all into
        /// <paramref name="targetFolder"/>, and starts the worker if it is idle.
        /// </summary>
        /// <summary>
        /// Downloads the files of items already in the library (after an
        /// import), in the folder they are in. Returns how many were queued.
        /// </summary>
        public int EnqueueLibraryItems(IEnumerable<DAAudioItem> items)
        {
            int count = 0;
            lock (_gate)
            {
                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.SourceURL)) continue;
                    if (_jobs.Any(j => ReferenceEquals(j.Item, item) && !j.IsFinished)) continue;
                    item.SourceDownloadStatusMessage = "Waiting";
                    item.SourceDownloadProgress = 0;
                    _jobs.Add(new DADownloadJob(item, null) { InLibrary = true });
                    count++;
                }
                if (count > 0 && (_worker == null || _worker.IsCompleted))
                    _worker = Task.Run(RunAsync);
            }
            if (count > 0) RaiseChanged(force: true);
            return count;
        }

        public void StartStaged(DAVirtualAudioFolder? targetFolder)
        {
            lock (_gate)
            {
                foreach (var item in _staged)
                {
                    item.SourceDownloadStatusMessage = "Waiting";
                    item.SourceDownloadProgress = 0;
                    _jobs.Add(new DADownloadJob(item, targetFolder));
                }
                _staged.Clear();

                if (_worker == null || _worker.IsCompleted)
                    _worker = Task.Run(RunAsync);
            }
            RaiseChanged(force: true);
        }

        /// <summary>Stops the current download and every waiting one.</summary>
        public void CancelAll()
        {
            bool running;
            lock (_gate)
            {
                foreach (var job in _jobs.Where(j => j.State == DADownloadState.Waiting))
                {
                    job.State = DADownloadState.Cancelled;
                    job.Item.SourceDownloadStatusMessage = "Cancelled";
                }
                if (_current != null) _current.CancelRequested = true;
                running = _current != null;
            }
            // An abort that fails (the download ended at this moment) must not
            // stop the cancelling: the worker still finishes and marks the job.
            if (running)
            {
                try { _runner.AbortDownload(_ => { }); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Aborting the download failed: {ex.Message}"); }
            }
            RaiseChanged(force: true);
        }

        /// <summary>
        /// Completes once nothing runs any more: the worker has finished its
        /// last download, and with it any item that download was adding to the
        /// library. Used after <see cref="CancelAll"/> before a restore or an
        /// import, so that no download adds an item in the middle of it.
        /// </summary>
        public Task WhenIdleAsync()
        {
            lock (_gate) return _worker ?? Task.CompletedTask;
        }

        /// <summary>Takes the finished downloads off the list.</summary>
        public void ClearFinished()
        {
            lock (_gate) _jobs.RemoveAll(j => j.IsFinished);
            RaiseChanged(force: true);
        }

        // ── The worker ─────────────────────────────────────────────────────

        private async Task RunAsync()
        {
            while (true)
            {
                DADownloadJob? job;
                lock (_gate)
                {
                    job = _jobs.FirstOrDefault(j => j.State == DADownloadState.Waiting);
                    if (job == null) { _current = null; return; }
                    job.State = DADownloadState.Downloading;
                    _current = job;
                }
                RaiseChanged(force: true);

                try
                {
                    await ProcessAsync(job).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // One bad link must not stop the ones behind it.
                    Debug.WriteLine($"Download of {job.Item.SourceURL} failed: {ex}");
                    job.State = DADownloadState.Failed;
                    job.Item.SourceDownloadStatusMessage = ex.Message;
                }
                finally
                {
                    job.Item.SourceIsDownloading = false;
                    lock (_gate) _current = null;
                    RaiseChanged(force: true);
                }
            }
        }

        private async Task ProcessAsync(DADownloadJob job)
        {
            var item = job.Item;

            if (!_library.CanWriteData)
            {
                job.State = DADownloadState.Failed;
                item.SourceDownloadStatusMessage = DiceAudio.Resources.Localization.Portability.DataFolderUnusable;
                return;
            }

            // Already in the library with its file: nothing to download. (Not
            // for an item that is itself in the library: another item's file
            // is never shared with it.)
            var known = job.InLibrary ? null : await OnUiAsync(() => FindLibraryFileFor(item)).ConfigureAwait(false);
            if (known != null)
            {
                Finish(job, known, DADownloadState.AlreadyThere, "Already in your library, not downloaded again");
                await AddToLibraryAsync(job).ConfigureAwait(false);
                return;
            }

            // A file of this link already in the audio folder, used by no item.
            var existing = await OnUiAsync(() => FindUnclaimedFileFor(item)).ConfigureAwait(false);
            if (existing != null)
            {
                Finish(job, existing, DADownloadState.AlreadyThere, "Found the file on this computer, not downloaded again");
                await AddToLibraryAsync(job).ConfigureAwait(false);
                return;
            }

            // Before anything is written: the name yt-dlp will use must not be
            // the file of another item, or both items would end up sharing it.
            item.SourceDownloadStatusMessage = "Preparing the download";
            RaiseChanged(force: true);
            string? baseName = null;
            string? planned = await _runner.GetOutputFileNameAsync(item.SourceURL).ConfigureAwait(false);
            if (planned != null)
            {
                string wanted = Path.GetFileNameWithoutExtension(planned);
                // The item's own name does not count: it may name a file that is missing.
                var taken = await OnUiAsync(() => ClaimedFileNames(except: item)).ConfigureAwait(false);
                baseName = DAAudioFileNames.UniqueBaseName(wanted, ".mp3", name => taken.Contains(name));
            }

            if (Cancelled(job)) return;

            // A cancelled download leaves no file (the runner deletes what it
            // wrote), so the item keeps the file name it had before.
            string nameBefore = item.LocalFileName;
            await _runner.RunYtDlpAsync(item, _ => RaiseChanged(force: false), baseName).ConfigureAwait(false);

            if (Cancelled(job))
            {
                if (job.InLibrary)
                {
                    item.LocalFileName = nameBefore;
                    item.IsLocallyAvailable = false;
                }
                return;
            }

            if (!item.IsLocallyAvailable)
            {
                job.State = DADownloadState.Failed;
                item.SourceDownloadProgress = 0;
                return;
            }

            // yt-dlp may still have reused a file already on disk under that
            // name: never link one file to two items.
            var owner = await OnUiAsync(() => _library.AudioItems.FirstOrDefault(a =>
                !ReferenceEquals(a, item)
                && string.Equals(a.LocalFileName, item.LocalFileName, StringComparison.OrdinalIgnoreCase))).ConfigureAwait(false);
            if (owner != null)
            {
                job.State = DADownloadState.Failed;
                item.IsLocallyAvailable = false;
                item.SourceDownloadStatusMessage = $"The file {item.LocalFileName} is already used by \"{owner.Name}\", so this download was not added.";
                item.LocalFileName = string.Empty;
                return;
            }

            item.SourceDownloadProgress = 100;
            job.State = DADownloadState.Done;
            await AddToLibraryAsync(job).ConfigureAwait(false);
        }

        private bool Cancelled(DADownloadJob job)
        {
            lock (_gate)
            {
                if (!job.CancelRequested) return false;
                job.State = DADownloadState.Cancelled;
                job.Item.SourceDownloadStatusMessage = "Cancelled";
                job.Item.SourceDownloadProgress = 0;
                return true;
            }
        }

        private static void Finish(DADownloadJob job, string fileName, DADownloadState state, string message)
        {
            job.Item.LocalFileName = fileName;
            job.Item.IsLocallyAvailable = true;
            job.Item.SourceDownloadProgress = 100;
            job.Item.SourceDownloadStatusMessage = message;
            job.State = state;
        }

        // ── The library, on the UI thread ──────────────────────────────────

        /// <summary>
        /// Adds the item to the library unless it is there already (same link or
        /// same file), then saves the items and the folders. Runs on the UI
        /// thread: the pages read the lists there, and a save serialises them
        /// there, so a list is never changed while it is being written.
        /// </summary>
        private Task AddToLibraryAsync(DADownloadJob job) => _library.OnUiAsync(async () =>
        {
            var item = job.Item;
            bool alreadyInLibrary = _library.AudioItems.Any(a =>
                (!string.IsNullOrEmpty(a.SourceURL) && a.SourceURL == item.SourceURL) ||
                (!string.IsNullOrEmpty(a.LocalFileName) && a.LocalFileName == item.LocalFileName));

            if (!alreadyInLibrary)
                _library.AddAudioItem(item, job.TargetFolder);

            try
            {
                await _library.SaveItemsAndFoldersAsync();
            }
            catch (Exception ex)
            {
                // The store has told the user; the item stays in the library
                // and the next save, or the one on close, tries again.
                Debug.WriteLine($"Saving after a download failed: {ex.Message}");
            }
            LibraryChanged?.Invoke();
        });

        private string? FindLibraryFileFor(DAAudioItem item) =>
            _library.AudioItems.FirstOrDefault(a =>
                !string.IsNullOrEmpty(a.SourceURL) && a.SourceURL == item.SourceURL
                && !string.IsNullOrEmpty(a.LocalFileName)
                && File.Exists(Path.Combine(_library.AudioDir, a.LocalFileName)))?.LocalFileName;

        private string? FindUnclaimedFileFor(DAAudioItem item)
        {
            var audioDir = _library.AudioDir;
            if (!Directory.Exists(audioDir)) return null;

            var claimed = ClaimedFileNames();
            foreach (var file in Directory.GetFiles(audioDir, "*.mp3"))
            {
                var fileName = Path.GetFileName(file);
                if (claimed.Contains(fileName)) continue;
                if (DAAudioFileNames.Matches(fileName, item.Name, item.SourceURL)) return fileName;
            }
            return null;
        }

        private HashSet<string> ClaimedFileNames(DAAudioItem? except = null) => new(
            _library.AudioItems.Where(a => !ReferenceEquals(a, except) && !string.IsNullOrEmpty(a.LocalFileName)).Select(a => a.LocalFileName),
            StringComparer.OrdinalIgnoreCase);

        private Task<T> OnUiAsync<T>(Func<T> read) => _library.OnUiAsync(read);

        private void RaiseChanged(bool force)
        {
            long now = DateTime.UtcNow.Ticks;
            if (!force && now - Interlocked.Read(ref _lastChangedTicks) < ChangedThrottle.Ticks) return;
            Interlocked.Exchange(ref _lastChangedTicks, now);
            Changed?.Invoke();
        }
    }
}
