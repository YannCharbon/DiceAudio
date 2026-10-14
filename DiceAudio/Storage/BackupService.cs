/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiceAudio.Storage;

public static class BackupReason
{
    public const string Auto = "auto";
    public const string Manual = "manual";
    public const string BeforeImport = "before-import";
    public const string BeforeRestore = "before-restore";
    public const string BeforeMove = "before-move";
}

/// <summary>A backup of the Backups folder. Manifest fields are null when its manifest cannot be read.</summary>
public sealed record BackupInfo(string Path, string FileName, string Reason, string Machine, DateTime CreatedAtUtc,
                                IReadOnlyDictionary<string, int>? Summary, bool Readable);

/// <summary>A backup that cannot be restored here: from the other app, from a newer version, or damaged.</summary>
public sealed class BackupRefusedException : Exception
{
    public BackupRefusedException(BackupCheck check) : base($"The backup was refused: {check}.") { Check = check; }
    public BackupCheck Check { get; }
}

/// <summary>The safety backup a restore makes first could not be made; nothing has been changed.</summary>
public sealed class BackupSafetyFailedException : Exception
{
    public BackupSafetyFailedException(Exception inner) : base(inner.Message, inner) { }
}

/// <summary>
/// A restore that failed, and whose putting back failed too. The data files
/// stay blocked, so that nothing is saved over them; the data from just before
/// the restore is in the safety backup named here.
/// </summary>
public sealed class BackupRollbackFailedException : Exception
{
    public BackupRollbackFailedException(string safetyBackupFileName, Exception inner) : base(inner.Message, inner)
    {
        SafetyBackupFileName = safetyBackupFileName;
    }

    public string SafetyBackupFileName { get; }
}

/// <summary>A restore that failed once it had started; the data has been put back from the safety backup.</summary>
public sealed class BackupRestoreFailedException : Exception
{
    public BackupRestoreFailedException(Exception inner) : base(inner.Message, inner) { }
}

/// <summary>
/// What a backup or a restore needs from the app: the library to save and
/// read again, and the activity to stop. DiceAudioService and its companions
/// in the app; plain fakes in the tests.
/// </summary>
public interface IBackupHost
{
    string AppVersion { get; }

    /// <summary>Writes what is in memory; errors are reported by the store and not thrown.</summary>
    Task SaveEverythingAsync();

    /// <summary>Stops playback and the downloads, so that nothing holds or writes the data during a restore.</summary>
    Task StopActivityAsync();

    /// <summary>Starts again what <see cref="StopActivityAsync"/> paused and must come back (the remote-control server).</summary>
    Task ResumeActivityAsync();

    /// <summary>Reads every data file again, after a restore.</summary>
    Task ReloadAsync();

    /// <summary>Tells the user about the files that could not be read, once every restore block is lifted.</summary>
    void ReportLoadFailures();
}

/// <summary>
/// Backups of the DiceAudio data files (spec §5.3): when they are made, where
/// they go, which are kept, and the restore. The zip itself is BackupArchive.
/// Audio files are never in a backup.
/// </summary>
public sealed class BackupService
{
    public const string AppName = "DiceAudio";
    public const int AutoBackupsKept = 10;
    public const string SettingsFileName = "settings.json";
    private const string RestoreBlockReason = "A restore is in progress";

    /// <summary>Every data file a backup may hold (spec §1.2), and the only names a restore writes.</summary>
    public static readonly IReadOnlyList<string> DataFileNames = new[]
    {
        "audioItems.json",
        "audioVirtualFolders.json",
        "scenes.json",
        "scenarioGroups.json",
        "presets.json",
        "pinnedScenarioGroups.json",
        "TagsCategoryType.json",
        "TagsCategoryLocation.json",
        "TagsCategoryExpression.json",
        SettingsFileName,
    };

    // A restore keeps settings.json when the backup has none (spec §5.3).
    private static readonly HashSet<string> KeptWhenAbsent = new(StringComparer.OrdinalIgnoreCase) { SettingsFileName };

    private static readonly Regex BackupNameRegex = new(
        @"^(DiceCombats|DiceAudio)-backup-(\d{8})-(\d{6})-([A-Za-z0-9-]{1,32})-(auto|manual|before-import|before-restore|before-move)\.zip$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly DataLocation _location;
    private readonly SafeFileStore _store;
    private readonly IBackupHost _host;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BackupService(DataLocation location, SafeFileStore store, IBackupHost host)
    {
        _location = location;
        _store = store;
        _host = host;
    }

    public string BackupsDir => _location.BackupsDir;

    /// <summary>The machine name as it appears in a file name: letters and digits, the rest as "-", 32 characters at most.</summary>
    public static string MachineForFileName(string machine)
    {
        var chars = (machine ?? string.Empty).Select(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-').ToArray();
        string name = new string(chars);
        if (name.Length > 32) name = name[..32];
        return name.Length == 0 ? "unknown" : name;
    }

    /// <summary>Writes a backup of the data files as they are on disk, after every pending save.</summary>
    public async Task<BackupInfo> CreateBackupAsync(string reason, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await CreateBackupLockedAsync(reason, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The automatic backup of the day: made when this machine has no automatic
    /// backup dated today (local date, read from the file names), then the
    /// oldest automatic backups beyond the ten most recent are deleted.
    /// </summary>
    public async Task<BackupInfo?> AutoBackupIfDueAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            string machine = MachineForFileName(_location.MachineName);
            string today = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            bool done = ListFiles().Any(f => f.Reason == BackupReason.Auto && f.Date == today
                                             && string.Equals(f.Machine, machine, StringComparison.OrdinalIgnoreCase));
            if (done) return null;

            // Just after loading: nothing to save, and the lists must not be
            // serialised off the UI thread while a page may change them.
            var backup = await CreateBackupLockedAsync(BackupReason.Auto, ct, saveFirst: false);
            await PruneAutoBackupsAsync(ct);
            return backup;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The backups of this app in the Backups folder, most recent first.</summary>
    public async Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken ct = default)
    {
        var result = new List<BackupInfo>();
        foreach (var file in ListFiles())
        {
            result.Add(await ReadInfoAsync(file, ct));
        }
        return result.OrderByDescending(b => b.CreatedAtUtc).ToList();
    }

    /// <summary>Checks a backup without changing anything. The manifest is null when it cannot be read.</summary>
    public async Task<(BackupCheck Check, BackupManifest? Manifest)> CheckAsync(byte[] zip, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(zip, writable: false);
        var check = await BackupArchive.VerifyAsync(stream, AppName, DataFileNames, ct);
        BackupManifest? manifest = null;
        try { manifest = await BackupArchive.ReadManifestAsync(stream, ct); }
        catch (InvalidDataException) { }
        return (check, manifest);
    }

    /// <summary>
    /// Replaces the data with the content of a backup (spec §5.3): the backup is
    /// checked, playback and downloads stop, the current data is saved and
    /// backed up (before-restore), the files are replaced, then the library is
    /// read again. The caller asks for confirmation first, and rebuilds the
    /// pages afterwards. Audio files are never touched.
    /// </summary>
    /// <exception cref="BackupRefusedException">Nothing has been changed.</exception>
    /// <exception cref="BackupSafetyFailedException">Nothing has been changed.</exception>
    /// <exception cref="BackupRestoreFailedException">The data has been put back from the safety backup.</exception>
    /// <exception cref="BackupRollbackFailedException">Putting the data back failed too; the files stay blocked.</exception>
    public async Task RestoreAsync(byte[] zip, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        bool stopped = false;
        try
        {
            using var stream = new MemoryStream(zip, writable: false);
            var check = await BackupArchive.VerifyAsync(stream, AppName, DataFileNames, ct);
            if (check != BackupCheck.Ok)
            {
                throw new BackupRefusedException(check);
            }

            // Nothing may play from, download into or write the data being
            // replaced; what is in memory then goes to disk, so that the
            // safety backup holds everything the user had.
            stopped = true;
            await _host.StopActivityAsync();
            await SaveEverythingAsync();
            BackupInfo safety;
            try
            {
                safety = await CreateBackupLockedAsync(BackupReason.BeforeRestore, ct, saveFirst: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new BackupSafetyFailedException(ex);
            }

            try
            {
                await ReplaceDataFilesAsync(stream, ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Restore failed, putting the data back: {ex}");
                try
                {
                    await using var safetyStream = File.OpenRead(safety.Path);
                    using var copy = new MemoryStream();
                    await safetyStream.CopyToAsync(copy, CancellationToken.None);
                    await ReplaceDataFilesAsync(copy, CancellationToken.None);
                }
                catch (Exception rollbackEx)
                {
                    // Half the files may be from the backup and half from
                    // before: they stay blocked and nothing is read again, so
                    // that the user restores the safety backup by hand.
                    Debug.WriteLine($"Putting the data back failed: {rollbackEx}");
                    throw new BackupRollbackFailedException(safety.FileName, ex);
                }
                await ReloadAsync();
                throw new BackupRestoreFailedException(ex);
            }

            await ReloadAsync();
        }
        finally
        {
            if (stopped)
            {
                try { await _host.ResumeActivityAsync(); }
                catch (Exception ex) { Debug.WriteLine($"Resuming after a restore failed: {ex.Message}"); }
            }
            _gate.Release();
        }
    }

    private async Task SaveEverythingAsync()
    {
        try
        {
            await _host.SaveEverythingAsync();
            await _store.FlushAsync();
        }
        catch (Exception ex)
        {
            // A save that failed was already reported; the backup then holds
            // what is on disk.
            Debug.WriteLine($"Saving before a backup failed: {ex.Message}");
        }
    }

    private async Task<BackupInfo> CreateBackupLockedAsync(string reason, CancellationToken ct, bool saveFirst = true)
    {
        if (saveFirst)
        {
            await SaveEverythingAsync();
        }
        else
        {
            try { await _store.FlushAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { Debug.WriteLine($"Flushing before a backup failed: {ex.Message}"); }
        }

        Directory.CreateDirectory(BackupsDir);
        string machine = MachineForFileName(_location.MachineName);
        DateTime now = DateTime.Now;
        string path;
        do
        {
            path = Path.Combine(BackupsDir, $"{AppName}-backup-{now:yyyyMMdd-HHmmss}-{machine}-{reason}.zip");
            now = now.AddSeconds(1);
        }
        while (File.Exists(path));

        // Each file is read through the store, under its lock, so that a file
        // being saved (briefly replaced) at this moment is never missed.
        var contents = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (string name in DataFileNames)
        {
            contents[name] = await _store.ReadBytesAsync(name, ct);
        }

        var manifest = new BackupManifest(BackupArchive.FormatName, BackupArchive.CurrentVersion, AppName, _host.AppVersion,
                                          DateTime.UtcNow, _location.MachineName, reason, Array.Empty<BackupFileEntry>(), CountContent(contents));

        string tmp = Path.Combine(BackupsDir, $".tmp-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await BackupArchive.WriteAsync(output, (name, _) => Task.FromResult(contents[name]), DataFileNames, manifest, ct);
                output.Flush(flushToDisk: true);
            }
            File.Move(tmp, path);
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { }
            throw;
        }

        Debug.WriteLine($"Backup written: {path}");
        return await ReadInfoAsync(ParseName(path)!, ct);
    }

    // Only the oldest automatic backups go, all machines together; manual
    // backups and the ones made before an import, a restore or a move stay.
    private async Task PruneAutoBackupsAsync(CancellationToken ct)
    {
        var autos = new List<BackupInfo>();
        foreach (var file in ListFiles().Where(f => f.Reason == BackupReason.Auto))
        {
            var info = await ReadInfoAsync(file, ct);
            if (info.Readable) autos.Add(info);   // never delete what cannot be read
        }

        foreach (var old in autos.OrderByDescending(b => b.CreatedAtUtc).Skip(AutoBackupsKept))
        {
            try
            {
                File.Delete(old.Path);
                Debug.WriteLine($"Old automatic backup deleted: {old.FileName}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Old automatic backup not deleted: {ex.Message}");
            }
        }
    }

    // The restored files are extracted inside the data folder first, so that
    // replacing each file is a rename on the same drive. The files are blocked
    // first, once no save of them is writing, and stay blocked until a load
    // of them succeeds: no save can write the old data in memory over them.
    private async Task ReplaceDataFilesAsync(Stream zip, CancellationToken ct)
    {
        string tempDir = _location.GetPath($".dice-restore-{Guid.NewGuid():N}");
        foreach (string name in DataFileNames)
        {
            await _store.BlockAsync(name, RestoreBlockReason, ct);
        }
        try
        {
            await BackupArchive.ExtractAsync(zip, tempDir, DataFileNames, ct);
            foreach (string name in DataFileNames)
            {
                string source = Path.Combine(tempDir, name);
                string target = _location.GetPath(name);
                if (File.Exists(source))
                {
                    if (File.Exists(target)) File.Replace(source, target, null, ignoreMetadataErrors: true);
                    else File.Move(source, target);
                }
                else if (!KeptWhenAbsent.Contains(name))
                {
                    // Absent from the backup: the file goes, with its previous
                    // copies. A name.bak left alone would make the store read
                    // the file as "only the previous copy was found" and block
                    // it; they are moved into the restore folder, deleted below.
                    foreach (string leftover in new[] { target, target + ".bak", target + ".tmp" })
                    {
                        if (File.Exists(leftover))
                        {
                            File.Move(leftover, Path.Combine(tempDir, Path.GetFileName(leftover) + ".old"));
                        }
                    }
                }
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine($"Restore folder left behind: {ex.Message}"); }
        }
    }

    private async Task ReloadAsync()
    {
        await _host.ReloadAsync();

        // Files no service reads: nothing in memory can be written over them,
        // so their restore block is simply lifted. (settings.json leaves this
        // list once AppSettings reads it.)
        if (_store.GetBlockReason(SettingsFileName) == RestoreBlockReason) _store.ForgetFile(SettingsFileName);

        // Only now: before, settings.json still carried its restore block and
        // was reported as unreadable.
        _host.ReportLoadFailures();
    }

    // Counted from the bytes stored rather than from memory, so that a file
    // that failed to load is still counted for what it holds.
    private static Dictionary<string, int> CountContent(IReadOnlyDictionary<string, byte[]?> contents)
    {
        int tags = CountArray(contents["TagsCategoryType.json"]) + CountArray(contents["TagsCategoryLocation.json"])
                   + CountArray(contents["TagsCategoryExpression.json"]);
        return new Dictionary<string, int>
        {
            ["audioItems"] = CountArray(contents["audioItems.json"]),
            ["folders"] = CountArray(contents["audioVirtualFolders.json"]),
            ["tags"] = tags,
            ["scenes"] = CountArray(contents["scenes.json"]),
            ["scenarioGroups"] = CountArray(contents["scenarioGroups.json"]),
            ["scenarios"] = CountNested(contents["scenarioGroups.json"], "Scenarios"),
            ["presets"] = CountArray(contents["presets.json"]),
        };
    }

    private static int CountArray(byte[]? bytes)
    {
        try
        {
            if (bytes == null) return 0;
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>The sum of the lengths of <paramref name="property"/> in each element of the array.</summary>
    private static int CountNested(byte[]? bytes, string property)
    {
        try
        {
            if (bytes == null) return 0;
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return 0;
            int count = 0;
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var list)
                    && list.ValueKind == JsonValueKind.Array)
                    count += list.GetArrayLength();
            }
            return count;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private sealed record BackupFile(string Path, string FileName, string Date, string Time, string Machine, string Reason);

    private IEnumerable<BackupFile> ListFiles()
    {
        if (!Directory.Exists(BackupsDir)) return Array.Empty<BackupFile>();
        return Directory.EnumerateFiles(BackupsDir, "*.zip")
                        .Select(ParseName)
                        .Where(f => f != null && f.FileName.StartsWith(AppName + "-", StringComparison.Ordinal))
                        .Select(f => f!)
                        .ToList();
    }

    private static BackupFile? ParseName(string path)
    {
        string name = Path.GetFileName(path);
        var match = BackupNameRegex.Match(name);
        return match.Success
            ? new BackupFile(path, name, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value, match.Groups[5].Value)
            : null;
    }

    private static async Task<BackupInfo> ReadInfoAsync(BackupFile file, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var manifest = await BackupArchive.ReadManifestAsync(stream, ct);
            return new BackupInfo(file.Path, file.FileName, manifest.Reason, manifest.Machine,
                                  DateTime.SpecifyKind(manifest.CreatedAtUtc.ToUniversalTime(), DateTimeKind.Utc), manifest.Summary, true);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // The date of the file name stands in, so that the list still sorts.
            DateTime.TryParseExact(file.Date + file.Time, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                                   DateTimeStyles.AssumeLocal, out DateTime local);
            return new BackupInfo(file.Path, file.FileName, file.Reason, file.Machine, local.ToUniversalTime(), null, false);
        }
    }
}
