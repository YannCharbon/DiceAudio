// Mirrored in DiceCombats/DiceCombats/Storage/SafeFileStore.cs — keep both in sync.
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiceAudio.Storage
{
    public enum SaveOutcome { Written, Unchanged, Blocked, ChangedOnDisk }

    public enum LoadOutcome { Loaded, Missing, Failed }

    public sealed record LoadResult<T>(LoadOutcome Outcome, T? Value, string? Error);

    /// <summary>
    /// Reads and writes the app's data files so that a crash, a locked file or
    /// an unreadable file never costs the user their data:
    /// <list type="bullet">
    /// <item>a save goes to <c>name.tmp</c> first and then replaces the file in
    /// one step, keeping the previous version as <c>name.bak</c>;</item>
    /// <item>a save whose content did not change writes nothing;</item>
    /// <item>a file that exists but cannot be read is never saved over for the
    /// rest of the session, so the user can still recover it.</item>
    /// </list>
    /// No MAUI calls in here: the app passes in how a file name becomes a path.
    /// </summary>
    public sealed class SafeFileStore
    {
        /// Waits between the attempts of a save (or a read) that hits a locked
        /// file: five attempts in all, about three seconds before giving up.
        private static readonly int[] RetryDelaysMs = { 200, 400, 800, 1600 };

        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private readonly Func<string, string> _resolvePath;
        private readonly ConcurrentDictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);

        /// Every save not finished yet, so <see cref="FlushAsync"/> can wait for them.
        private readonly ConcurrentDictionary<Task, byte> _pending = new();

        public SafeFileStore(Func<string, string> resolvePath)
        {
            _resolvePath = resolvePath ?? throw new ArgumentNullException(nameof(resolvePath));
        }

        /// <summary>Raised when a save gives up after its retries; the UI shows ErrorSaveFailed.</summary>
        public event Action<string, Exception>? SaveFailed;

        /// <summary>The files whose load failed, which this session never writes.</summary>
        public IReadOnlyCollection<string> BlockedFiles =>
            _files.Where(f => f.Value.Blocked).Select(f => f.Key).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        public bool IsBlocked(string fileName) =>
            _files.TryGetValue(fileName, out var state) && state.Blocked;

        /// <summary>
        /// Marks a file as not to be written for the rest of the session, for a
        /// file whose content could not be taken in even though it was read.
        /// </summary>
        public void Block(string fileName)
        {
            var state = StateOf(fileName);
            state.Blocked = true;
            state.Hash = null;
        }

        /// <summary>
        /// Drops what the store remembers about a file (its hash and its block),
        /// after a restore or a reload has put a new version in place.
        /// </summary>
        public void ForgetFile(string fileName)
        {
            if (_files.TryGetValue(fileName, out var state))
            {
                state.Hash = null;
                state.Blocked = false;
            }
        }

        // ── Loading ────────────────────────────────────────────────────────

        public async Task<LoadResult<T>> LoadJsonAsync<T>(string fileName, JsonSerializerOptions options, CancellationToken ct = default)
        {
            var state = StateOf(fileName);
            await state.Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var text = await ReadLockedAsync(fileName, state, ct).ConfigureAwait(false);
                if (text.Outcome != LoadOutcome.Loaded)
                    return new LoadResult<T>(text.Outcome, default, text.Error);

                try
                {
                    var value = JsonSerializer.Deserialize<T>(text.Value!, options);
                    // "null" is valid JSON but no data file of the apps ever holds
                    // it, so it is treated as damage rather than as an empty list.
                    if (value == null)
                        return Fail<T>(state, "The file contains no data.");
                    return new LoadResult<T>(LoadOutcome.Loaded, value, null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Fail<T>(state, ex.Message);
                }
            }
            finally
            {
                state.Gate.Release();
            }
        }

        public async Task<LoadResult<string>> LoadTextAsync(string fileName, CancellationToken ct = default)
        {
            var state = StateOf(fileName);
            await state.Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await ReadLockedAsync(fileName, state, ct).ConfigureAwait(false);
            }
            finally
            {
                state.Gate.Release();
            }
        }

        /// Reads the file while its gate is held. Remembers the hash of what was
        /// read, or blocks the file when it exists but cannot be read.
        private async Task<LoadResult<string>> ReadLockedAsync(string fileName, FileState state, CancellationToken ct)
        {
            string path = _resolvePath(fileName);
            if (!File.Exists(path))
            {
                string bak = path + ".bak", tmp = path + ".tmp";
                if (!File.Exists(bak) && !File.Exists(tmp))
                {
                    state.Hash = null;
                    state.Blocked = false;
                    return new LoadResult<string>(LoadOutcome.Missing, null, null);
                }

                // A save was cut off between its steps: the file is gone but its
                // previous version is still there. Put that back rather than
                // starting from nothing, which the next save would make final.
                if (!File.Exists(bak))
                    return Fail<string>(state, $"{fileName} is missing, and only an unfinished save of it ({fileName}.tmp) was found.");
                try
                {
                    File.Move(bak, path);
                    Debug.WriteLine($"{fileName} was missing; restored it from {fileName}.bak.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Fail<string>(state, $"{fileName} is missing, and {fileName}.bak could not be put back: {ex.Message}");
                }
            }

            try
            {
                byte[] bytes = await WithRetriesAsync(() => File.ReadAllBytesAsync(path, ct), ct).ConfigureAwait(false);
                // Strict decoding: invalid UTF-8 is damage, not something to
                // paper over with replacement characters and save back.
                string text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(StripBom(bytes));
                state.Hash = HashOf(bytes);
                state.Blocked = false;
                return new LoadResult<string>(LoadOutcome.Loaded, text, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Fail<string>(state, ex.Message);
            }
        }

        private static LoadResult<T> Fail<T>(FileState state, string error)
        {
            state.Blocked = true;
            state.Hash = null;
            return new LoadResult<T>(LoadOutcome.Failed, default, error);
        }

        // ── Saving ─────────────────────────────────────────────────────────

        /// <summary>
        /// Serialises <paramref name="value"/> now, as it is at the time of the
        /// call, then writes it in the background of the returned task.
        /// </summary>
        public Task<SaveOutcome> SaveJsonAsync<T>(string fileName, T value, JsonSerializerOptions options,
                                                  bool overwriteExternalChange = false, CancellationToken ct = default)
        {
            string text = JsonSerializer.Serialize(value, options);
            return SaveTextAsync(fileName, text, overwriteExternalChange, ct);
        }

        public Task<SaveOutcome> SaveTextAsync(string fileName, string content,
                                               bool overwriteExternalChange = false, CancellationToken ct = default)
        {
            var state = StateOf(fileName);
            long sequence = Interlocked.Increment(ref state.LastRequested);

            var task = SaveCoreAsync(fileName, state, sequence, content, overwriteExternalChange, ct);
            _pending.TryAdd(task, 0);
            _ = task.ContinueWith(t => _pending.TryRemove(t, out _), CancellationToken.None,
                                  TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }

        private async Task<SaveOutcome> SaveCoreAsync(string fileName, FileState state, long sequence, string content,
                                                      bool overwriteExternalChange, CancellationToken ct)
        {
            ExceptionDispatchInfo? failure;
            await state.Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (state.Blocked) return SaveOutcome.Blocked;

                // A newer snapshot of this file has already been written while
                // this one waited for the gate: writing this one would put older
                // data back.
                if (sequence < state.LastWritten) return SaveOutcome.Unchanged;

                string path = _resolvePath(fileName);
                byte[] bytes = Utf8NoBom.GetBytes(content);
                string hash = HashOf(bytes);

                bool exists = File.Exists(path);
                if (exists && hash == state.Hash)
                {
                    state.LastWritten = sequence;
                    return SaveOutcome.Unchanged;
                }

                try
                {
                    return await WithRetriesAsync(async () =>
                    {
                        // Someone else (another computer, an editor) changed the
                        // file since it was read or last written here.
                        if (!overwriteExternalChange && state.Hash != null && File.Exists(path))
                        {
                            string onDisk = HashOf(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false));
                            if (onDisk != state.Hash) return SaveOutcome.ChangedOnDisk;
                        }

                        await WriteReplaceAsync(path, bytes, ct).ConfigureAwait(false);
                        state.Hash = hash;
                        state.LastWritten = sequence;
                        return SaveOutcome.Written;
                    }, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
            }
            finally
            {
                state.Gate.Release();
            }

            // Raised once the gate is free, so a handler that saves again (or
            // shows UI that does) cannot wait on this file's own lock.
            SaveFailed?.Invoke(fileName, failure.SourceException);
            failure.Throw();
            return SaveOutcome.Blocked; // not reached
        }

        /// Writes <c>path.tmp</c>, forces it to disk, then swaps it in. The old
        /// file becomes <c>path.bak</c>, which the app never reads.
        private static async Task WriteReplaceAsync(string path, byte[] bytes, CancellationToken ct)
        {
            string tmp = path + ".tmp";
            await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                                                     bufferSize: 81920, useAsync: true))
            {
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
                File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(tmp, path);
        }

        /// <summary>
        /// Waits for every save started before this call. A save that failed has
        /// already raised <see cref="SaveFailed"/>, so its error is not thrown
        /// again here.
        /// </summary>
        public async Task FlushAsync(CancellationToken ct = default)
        {
            var pending = _pending.Keys.ToArray();
            if (pending.Length == 0) return;

            try
            {
                await Task.WhenAll(pending).WaitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Reported through SaveFailed and to the caller of that save.
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private FileState StateOf(string fileName) => _files.GetOrAdd(fileName, _ => new FileState());

        private static async Task<TResult> WithRetriesAsync<TResult>(Func<Task<TResult>> action, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return await action().ConfigureAwait(false);
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException)
                                           && attempt < RetryDelaysMs.Length)
                {
                    await Task.Delay(RetryDelaysMs[attempt], ct).ConfigureAwait(false);
                }
            }
        }

        private static byte[] StripBom(byte[] bytes) =>
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
                ? bytes[3..]
                : bytes;

        private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        private sealed class FileState
        {
            public readonly SemaphoreSlim Gate = new(1, 1);

            /// SHA-256 of the file as last read or written here; null when unknown.
            public string? Hash;

            public bool Blocked;

            /// Sequence numbers of the save calls: the last one made, and the
            /// last one whose content is known to be on disk.
            public long LastRequested;
            public long LastWritten;
        }
    }
}
