/*
 * Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Shared by DiceAudio and DiceCombats, both released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */
// Mirrored in DiceCombats/DiceCombats/Storage/DataFolderLock.cs — keep both in sync.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace DiceAudio.Storage;

/// <summary>The content of a dice.lock file (spec §5.5).</summary>
public sealed class DataFolderLockInfo
{
    public string Format { get; init; } = DataFolderLock.FormatName;
    public int Version { get; init; } = DataFolderLock.CurrentVersion;
    public string App { get; init; } = "";
    public Guid SessionId { get; init; }
    public string Machine { get; init; } = "";
    public int ProcessId { get; init; }
    public string AppVersion { get; init; } = "";
    public DateTime StartedUtc { get; init; }
    public DateTime HeartbeatUtc { get; init; }
}

/// <summary>
/// dice.lock (spec §5.5, Windows only): says which computer has the data
/// folder open. Written once the user goes on at startup, refreshed every
/// minute, and deleted at a clean exit if it is still ours. Another session's
/// lock is live when its heartbeat is less than five minutes old and, on this
/// computer, its process still runs; a stale lock is written over.
/// It only warns: nothing stops two computers from opening the folder.
/// </summary>
public sealed class DataFolderLock : IDisposable
{
    public const string FileName = "dice.lock";
    public const string FormatName = "Dice.Lock";
    public const int CurrentVersion = 1;
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan LiveFor = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly SafeFileStore _store;
    private readonly Func<string> _root;
    private readonly string _app;
    private readonly string _machine;
    private readonly string _appVersion;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<int, bool> _isProcessRunning;
    private readonly object _gate = new();
    private Timer? _timer;
    private DateTime _startedUtc;

    /// <param name="root">The data folder now (it follows DataLocation.Root).</param>
    /// <param name="utcNow">The clock; tests pass their own.</param>
    /// <param name="isProcessRunning">Whether a process of this computer still runs; tests pass their own.</param>
    public DataFolderLock(SafeFileStore store, Func<string> root, string app, string machine, string appVersion,
                          Func<DateTime>? utcNow = null, Func<int, bool>? isProcessRunning = null)
    {
        _store = store;
        _root = root;
        _app = app;
        _machine = machine;
        _appVersion = appVersion;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _isProcessRunning = isProcessRunning ?? IsProcessRunning;
    }

    /// <summary>This session, written in the lock; a new one at each start.</summary>
    public Guid SessionId { get; } = Guid.NewGuid();

    /// <summary>True between Acquire and Release.</summary>
    public bool IsHeld { get; private set; }

    /// <summary>
    /// The lock in a folder; null when there is none or it cannot be read.
    /// Never throws: it is read at startup, and a lock that cannot be read
    /// counts as stale rather than stopping the app.
    /// </summary>
    public static DataFolderLockInfo? Read(string folder)
    {
        try
        {
            string path = Path.Combine(folder, FileName);
            if (!File.Exists(path)) return null;
            var info = JsonSerializer.Deserialize<DataFolderLockInfo>(File.ReadAllText(path), ReadOptions);
            return info != null && info.Format == FormatName ? info : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{FileName} cannot be read: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Whether another session's lock still counts: a heartbeat less than five
    /// minutes old and, on this computer, a process that still runs and is
    /// not this one. A heartbeat ahead of this clock counts as recent.
    /// </summary>
    public static bool IsLive(DataFolderLockInfo info, string machine, DateTime utcNow, Func<int, bool> isProcessRunning, int ownProcessId)
    {
        if (utcNow - info.HeartbeatUtc >= LiveFor) return false;
        if (string.Equals(info.Machine, machine, StringComparison.OrdinalIgnoreCase))
        {
            return info.ProcessId != ownProcessId && isProcessRunning(info.ProcessId);
        }
        return true;
    }

    /// <summary>
    /// Another session's live lock in a folder (spec §5.5), or null.
    /// DataLocation asks at startup, so this never throws: when the lock
    /// cannot be judged, the app starts without the warning.
    /// </summary>
    public static DataFolderLockInfo? FindOtherLiveLock(string folder, string app, string machine, DateTime utcNow,
                                                        Func<int, bool>? isProcessRunning = null)
    {
        try
        {
            var info = Read(folder);
            if (info == null) return null;
            if (!string.IsNullOrEmpty(info.App) && !string.Equals(info.App, app, StringComparison.Ordinal)) return null;
            return IsLive(info, machine, utcNow, isProcessRunning ?? IsProcessRunning, Environment.ProcessId) ? info : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{FileName} could not be checked: {ex.Message}");
            return null;
        }
    }

    /// <summary>Writes this session's lock and starts the heartbeat. Called once the data folder is used.</summary>
    public async Task AcquireAsync()
    {
        _startedUtc = _utcNow();
        IsHeld = true;
        await WriteAsync(force: true).ConfigureAwait(false);
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = new Timer(_ => _ = HeartbeatAsync(), null, HeartbeatInterval, HeartbeatInterval);
        }
    }

    /// <summary>
    /// The heartbeat: writes the lock again with the time now. When another
    /// live session wrote its own lock since (it was opened anyway on another
    /// computer), that one is left in place: it is the one the next computer
    /// should see. A lock that is gone or stale is written again.
    /// </summary>
    public async Task HeartbeatAsync()
    {
        if (!IsHeld) return;
        try
        {
            await WriteAsync(force: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{FileName} heartbeat failed: {ex.Message}");
        }
    }

    /// <summary>Stops the heartbeat and deletes the lock if it is still ours. Synchronous: called while the window closes.</summary>
    public void Release()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
        if (!IsHeld) return;
        IsHeld = false;
        try
        {
            string folder = _root();
            var info = Read(folder);
            if (info != null && info.SessionId == SessionId)
            {
                File.Delete(Path.Combine(folder, FileName));
            }
        }
        catch (Exception ex)
        {
            // Called while the window closes: nothing may stop it.
            Debug.WriteLine($"{FileName} could not be deleted: {ex.Message}");
        }
    }

    public void Dispose() => Release();

    private async Task WriteAsync(bool force)
    {
        DateTime now = _utcNow();
        if (!force)
        {
            var current = Read(_root());
            if (current != null && current.SessionId != SessionId
                && IsLive(current, _machine, now, _isProcessRunning, Environment.ProcessId))
            {
                return;
            }
        }
        var info = new DataFolderLockInfo
        {
            App = _app,
            SessionId = SessionId,
            Machine = _machine,
            ProcessId = Environment.ProcessId,
            AppVersion = _appVersion,
            StartedUtc = _startedUtc,
            HeartbeatUtc = now,
        };
        // No change check: the lock is meant to be written over.
        await _store.SaveTextAsync(FileName, JsonSerializer.Serialize(info, WriteOptions), overwriteExternalChange: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether a process of this computer still runs. Never throws. A process
    /// that cannot be looked at (Windows gave the ID to a protected or elevated
    /// process: access denied) exists, so it counts as running: the user then
    /// sees the warning and can open anyway.
    /// </summary>
    public static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;   // no such process, or it has just ended
        }
        catch (Win32Exception)
        {
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Process {processId} could not be checked: {ex.Message}");
            return true;
        }
    }
}
