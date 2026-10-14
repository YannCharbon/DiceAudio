/*
 * Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Shared by DiceAudio and DiceCombats, both released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */
// Mirrored in DiceCombats/DiceCombats/Storage/SafeFileStore.cs — keep both in sync.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiceAudio.Storage;

public enum SaveOutcome { Written, Unchanged, Blocked, ChangedOnDisk }
public enum LoadOutcome { Loaded, Missing, Failed }
public sealed record LoadResult<T>(LoadOutcome Outcome, T? Value, string? Error);

/// <summary>
/// Reads and writes the data files without ever leaving one half written.
/// <list type="bullet">
/// <item>One lock per file.</item>
/// <item>The value is serialised when the save is asked for, so later changes
/// in memory do not leak into a save already on its way.</item>
/// <item>A file is written only when its content changed since it was last
/// read or written.</item>
/// <item>The new content goes to name.tmp, then replaces the file, whose
/// previous version is kept as name.bak.</item>
/// <item>A file that exists but cannot be read is never saved over during
/// this session.</item>
/// </list>
/// </summary>
public sealed class SafeFileStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };
    private static readonly int[] RetryDelaysMs = { 200, 400, 800, 1600 };

    private readonly Func<string, string> _resolvePath;
    private readonly ConcurrentDictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingGate = new();
    private readonly HashSet<Task> _pending = new();

    /// <param name="resolvePath">Turns a file name into a full path; DataLocation.GetPath.</param>
    public SafeFileStore(Func<string, string> resolvePath)
    {
        _resolvePath = resolvePath;
    }

    /// <summary>
    /// Raised when a save gives up after its retries; the app tells the user.
    /// Never raised for a save that returns Blocked or ChangedOnDisk.
    /// </summary>
    public event Action<string, Exception>? SaveFailed;

    /// <summary>The files that failed to load, and that are therefore never saved over in this session.</summary>
    public IReadOnlyCollection<string> BlockedFiles =>
        _files.Where(f => f.Value.Blocked).Select(f => f.Key).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

    public bool IsBlocked(string fileName) => _files.TryGetValue(fileName, out var state) && state.Blocked;

    /// <summary>
    /// Marks a file as one that must not be saved over in this session, for a
    /// failure the store could not see itself (the caller failed to use what
    /// was loaded). A save already writing finishes; every later one is Blocked.
    /// </summary>
    public void Block(string fileName, string reason)
    {
        var state = GetState(fileName);
        Debug.WriteLine($"{fileName} is blocked: {reason}");
        state.LoadError = reason;
        state.Blocked = true;
    }

    /// <summary>
    /// Loads and deserialises a file. A file that exists but cannot be read,
    /// is not valid UTF-8, is not valid JSON or holds only <c>null</c> is Failed
    /// and blocked.
    /// </summary>
    public Task<LoadResult<T>> LoadJsonAsync<T>(string fileName, JsonSerializerOptions options, CancellationToken ct = default)
    {
        return LoadCoreAsync(fileName, text => JsonSerializer.Deserialize<T>(text, options)
                                               ?? throw new JsonException("The file holds no data (null)."), ct);
    }

    public Task<LoadResult<string>> LoadTextAsync(string fileName, CancellationToken ct = default)
    {
        return LoadCoreAsync<string>(fileName, text => text, ct);
    }

    // Reads and parses under the file's lock, so that no save can slip in
    // between a read that worked and a parse that failed.
    private async Task<LoadResult<T>> LoadCoreAsync<T>(string fileName, Func<string, T> parse, CancellationToken ct)
    {
        var state = GetState(fileName);
        await state.Lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string path = _resolvePath(fileName);
            if (!File.Exists(path))
            {
                // Only a leftover of an interrupted write: the data is not
                // gone, so the file must not be recreated empty.
                string? leftover = new[] { path + ".bak", path + ".tmp" }.FirstOrDefault(File.Exists);
                if (leftover != null)
                {
                    string reason = $"Only the previous copy ({Path.GetFileName(leftover)}) was found";
                    MarkFailed(state, fileName, reason);
                    return new LoadResult<T>(LoadOutcome.Failed, default, reason);
                }

                state.Hash = null;
                state.Blocked = false;
                state.LoadError = null;
                return new LoadResult<T>(LoadOutcome.Missing, default, null);
            }

            try
            {
                byte[] bytes = await WithRetriesAsync(() => File.ReadAllBytesAsync(path, ct), ct).ConfigureAwait(false);
                ReadOnlySpan<byte> body = bytes.AsSpan().StartsWith(Utf8Bom) ? bytes.AsSpan(Utf8Bom.Length) : bytes;
                T value = parse(StrictUtf8.GetString(body));
                state.Hash = HashOf(bytes);
                return new LoadResult<T>(LoadOutcome.Loaded, value, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                MarkFailed(state, fileName, ex.Message);
                return new LoadResult<T>(LoadOutcome.Failed, default, ex.Message);
            }
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public Task<SaveOutcome> SaveJsonAsync<T>(string fileName, T value, JsonSerializerOptions options, bool overwriteExternalChange = false, CancellationToken ct = default)
    {
        // Serialised now, on the caller's thread: the snapshot is what the
        // caller had in memory at the moment it asked.
        string text = JsonSerializer.Serialize(value, options);
        return SaveTextAsync(fileName, text, overwriteExternalChange, ct);
    }

    /// <param name="overwriteExternalChange">
    /// False: when the file on disk is no longer what was last read or
    /// written here (another computer changed it), nothing is written and the
    /// result is ChangedOnDisk.
    /// </param>
    public Task<SaveOutcome> SaveTextAsync(string fileName, string content, bool overwriteExternalChange = false, CancellationToken ct = default)
    {
        var state = GetState(fileName);
        long generation = Interlocked.Increment(ref state.Generation);
        // The writing itself runs on the thread pool: the caller is often the
        // UI thread, and on close it is blocked waiting for FlushAsync.
        var task = Task.Run(() => SaveAndReportAsync(fileName, state, content, generation, overwriteExternalChange, ct), CancellationToken.None);

        lock (_pendingGate)
        {
            _pending.Add(task);
        }
        task.ContinueWith(t => { lock (_pendingGate) { _pending.Remove(t); } },
                          CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    /// <summary>Awaits every save started before the call. Throws if one of them failed.</summary>
    public Task FlushAsync(CancellationToken ct = default)
    {
        Task[] pending;
        lock (_pendingGate)
        {
            pending = _pending.ToArray();
        }
        return pending.Length == 0 ? Task.CompletedTask : Task.WhenAll(pending).WaitAsync(ct);
    }

    /// <summary>After a restore or a reload: forgets what was read or written, and the block.</summary>
    public void ForgetFile(string fileName)
    {
        var state = GetState(fileName);
        state.Hash = null;
        state.Blocked = false;
        state.LoadError = null;
    }

    // SaveFailed is raised here, once the file's lock is released, so that a
    // handler can never hold up the other saves of the file.
    private async Task<SaveOutcome> SaveAndReportAsync(string fileName, FileState state, string content, long generation, bool overwriteExternalChange, CancellationToken ct)
    {
        try
        {
            return await SaveCoreAsync(fileName, state, content, generation, overwriteExternalChange, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"Saving {fileName} failed: {ex}");
            SaveFailed?.Invoke(fileName, ex);
            throw;
        }
    }

    private async Task<SaveOutcome> SaveCoreAsync(string fileName, FileState state, string content, long generation, bool overwriteExternalChange, CancellationToken ct)
    {
        await state.Lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (state.Blocked) return SaveOutcome.Blocked;

            // A newer snapshot of this file is already waiting: it is the one
            // to write, and writing this older one first would only cost time.
            if (generation != Interlocked.Read(ref state.Generation)) return SaveOutcome.Unchanged;

            string path = _resolvePath(fileName);
            byte[] bytes = Utf8NoBom.GetBytes(content);
            string hash = HashOf(bytes);
            if (File.Exists(path) && hash == state.Hash) return SaveOutcome.Unchanged;

            var outcome = await WithRetriesAsync(async () =>
            {
                if (!overwriteExternalChange && DiskHash(path) != state.Hash)
                {
                    return SaveOutcome.ChangedOnDisk;
                }
                await WriteReplaceAsync(path, bytes, ct).ConfigureAwait(false);
                return SaveOutcome.Written;
            }, ct).ConfigureAwait(false);

            if (outcome == SaveOutcome.Written)
            {
                state.Hash = hash;
            }
            return outcome;
        }
        finally
        {
            state.Lock.Release();
        }
    }

    private static string? DiskHash(string path) => File.Exists(path) ? HashOf(File.ReadAllBytes(path)) : null;

    private static async Task WriteReplaceAsync(string path, byte[] bytes, CancellationToken ct)
    {
        string tmp = path + ".tmp";
        await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
        {
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, path);
        }
    }

    // 5 attempts while the file is locked (by an antivirus, a sync client or
    // another window), waiting 200, 400, 800 then 1600 ms between them.
    private static async Task<T> WithRetriesAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < RetryDelaysMs.Length)
            {
                Debug.WriteLine($"File access failed, attempt {attempt + 1}: {ex.Message}");
                await Task.Delay(RetryDelaysMs[attempt], ct).ConfigureAwait(false);
            }
        }
    }

    private static void MarkFailed(FileState state, string fileName, string reason)
    {
        Debug.WriteLine($"Loading {fileName} failed, it will not be saved over: {reason}");
        state.LoadError = reason;
        state.Blocked = true;
    }

    private FileState GetState(string fileName) => _files.GetOrAdd(fileName, _ => new FileState());

    private static string HashOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class FileState
    {
        public readonly SemaphoreSlim Lock = new(1, 1);
        public long Generation;
        public volatile string? Hash;
        public volatile bool Blocked;
        public volatile string? LoadError;
    }
}
