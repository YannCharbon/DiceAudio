/*
 * Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Shared by DiceAudio and DiceCombats, both released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */
// Mirrored in DiceCombats/DiceCombats/Storage/DataFolderWatcher.cs — keep both in sync.

using System.Diagnostics;

namespace DiceAudio.Storage;

/// <summary>
/// Watches the data files for changes made elsewhere (spec §3.4 "Several
/// PCs", C7/A9): a synchronisation service bringing another computer's
/// version. A change event is only a hint: once the events calm down, each
/// file named is compared with what the app last read or wrote, so the app's
/// own saves are never reported. The app shows a banner; it never reloads by
/// itself.
/// </summary>
public sealed class DataFolderWatcher : IDisposable
{
    private readonly SafeFileStore _store;
    private readonly HashSet<string> _watched;
    private readonly TimeSpan _settle;
    private readonly object _gate = new();
    private readonly HashSet<string> _candidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private int _paused;

    /// <param name="watchedFiles">The data files to watch, by name (the lock and the marker are left out).</param>
    /// <param name="settle">How long the events must be quiet before the files are compared (2 s by default).</param>
    public DataFolderWatcher(SafeFileStore store, IEnumerable<string> watchedFiles, TimeSpan? settle = null)
    {
        _store = store;
        _watched = new HashSet<string>(watchedFiles, StringComparer.OrdinalIgnoreCase);
        _settle = settle ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>The files changed elsewhere since the last <see cref="Clear"/>, by name; raised again for each new one.</summary>
    public event Action<IReadOnlyList<string>>? ChangedElsewhere;

    public IReadOnlyList<string> Changed
    {
        get { lock (_gate) return _changed.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(); }
    }

    /// <summary>Starts watching a folder (again). Nothing is reported for what happened before.</summary>
    public void Start(string folder)
    {
        Stop();
        try
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += (_, e) => OnEvent(e.Name);
            watcher.Created += (_, e) => OnEvent(e.Name);
            watcher.Deleted += (_, e) => OnEvent(e.Name);
            watcher.Renamed += (_, e) => { OnEvent(e.Name); OnEvent(e.OldName); };
            // Too many events at once: every file is compared.
            watcher.Error += (_, _) => { foreach (string name in _watched) OnEvent(name); };
            watcher.EnableRaisingEvents = true;
            lock (_gate) _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Debug.WriteLine($"The data folder cannot be watched: {ex.Message}");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _watcher?.Dispose();
            _watcher = null;
            _timer?.Dispose();
            _timer = null;
            _candidates.Clear();
        }
    }

    /// <summary>
    /// While the app itself replaces its files (a restore, a reload, a move),
    /// events are ignored. Dispose the result to watch again.
    /// </summary>
    public IDisposable Pause()
    {
        Interlocked.Increment(ref _paused);
        return new Resumer(this);
    }

    /// <summary>After a reload: the files are what the app has now.</summary>
    public void Clear(IEnumerable<string>? names = null)
    {
        lock (_gate)
        {
            if (names == null) _changed.Clear();
            else foreach (string name in names) _changed.Remove(name);
        }
    }

    /// <summary>Compares the files named with what the app last read or wrote, and reports those changed elsewhere.</summary>
    public async Task CheckAsync(IEnumerable<string> names)
    {
        var found = new List<string>();
        foreach (string name in names.Where(_watched.Contains).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await _store.HasChangedOnDiskAsync(name).ConfigureAwait(false)) found.Add(name);
        }
        if (Volatile.Read(ref _paused) > 0) return;

        bool added = false;
        lock (_gate)
        {
            foreach (string name in found) added |= _changed.Add(name);
        }
        if (added) ChangedElsewhere?.Invoke(Changed);
    }

    public void Dispose() => Stop();

    private void OnEvent(string? name)
    {
        if (name == null || !_watched.Contains(name) || Volatile.Read(ref _paused) > 0) return;
        lock (_gate)
        {
            if (_watcher == null) return;
            _candidates.Add(name);
            _timer ??= new Timer(_ => _ = SettledAsync(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(_settle, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task SettledAsync()
    {
        string[] names;
        lock (_gate)
        {
            names = _candidates.ToArray();
            _candidates.Clear();
        }
        try
        {
            await CheckAsync(names).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Checking the changed files failed: {ex.Message}");
        }
    }

    private sealed class Resumer : IDisposable
    {
        private DataFolderWatcher? _owner;
        public Resumer(DataFolderWatcher owner) => _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner == null) return;
            // Events of the app's own writes may still be settling.
            lock (owner._gate) owner._candidates.Clear();
            Interlocked.Decrement(ref owner._paused);
        }
    }
}
