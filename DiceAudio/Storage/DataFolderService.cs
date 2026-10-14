/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Diagnostics;
using System.Text.Json;
using DiceAudio.Portability;

namespace DiceAudio.Storage;

/// <summary>
/// The data folder of DiceAudio (spec §5.4, §5.6), adapted from DiceCombats'
/// DataFolderService: loading at startup once the folder can be used, the
/// marker, moving to another folder — the audio files included — using or
/// replacing or merging with a folder that already holds DiceAudio data, and
/// the old copy left behind. Windows only; the UI asks.
/// </summary>
public sealed class DataFolderService
{
    /// <summary>The local preference that remembers where the data was before a move, until the old copy is deleted.</summary>
    public const string PreviousRootKey = "PreviousDataRootPath";

    public const string AudioFolderName = "Audio";

    /// <summary>The preferences that move into settings.json (spec §6). DiceAudio has no language and no help toggles.</summary>
    public static readonly IReadOnlyList<string> MovedPreferenceKeys = new[]
    {
        DAViewPrefs.PrefFolderView, DAViewPrefs.PrefItemView, DAViewPrefs.PrefSort,
        DAControlServer.PrefEnabled, DAControlServer.PrefPort,
        YtDlpUpdateService.PrefAutoUpdate,
    };

    /// <summary>What a move copies: the data files, the backups, and the audio files. Tools/ and EBWebView stay on this computer.</summary>
    public static readonly IReadOnlyList<string> MovedEntries = BackupService.DataFileNames.Concat(new[] { "Backups", AudioFolderName }).ToList();

    private static readonly TimeSpan DownloadStopTimeout = TimeSpan.FromSeconds(5);

    private readonly DataLocation _location;
    private readonly SafeFileStore _store;
    private readonly DiceAudioService _service;
    private readonly BackupService _backups;
    private readonly AppSettings _settings;
    private readonly ILocalPreferences _localPreferences;
    private readonly DAStorageNotices _notices;
    private readonly DAPlaybackCoordinator _playback;
    private readonly DADownloadQueue _downloads;
    private readonly DAControlServer _controlServer;

    public DataFolderService(DataLocation location, SafeFileStore store, DiceAudioService service, BackupService backups,
                             AppSettings settings, ILocalPreferences localPreferences, DAStorageNotices notices,
                             DAPlaybackCoordinator playback, DADownloadQueue downloads, DAControlServer controlServer)
    {
        _location = location;
        _store = store;
        _service = service;
        _backups = backups;
        _settings = settings;
        _localPreferences = localPreferences;
        _notices = notices;
        _playback = playback;
        _downloads = downloads;
        _controlServer = controlServer;
    }

    /// <summary>True once the data has been read; until then nothing is saved.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Raised when IsLoaded changes, for the startup screen.</summary>
    public event Action? LoadedChanged;

    public DataLocation Location => _location;

    /// <summary>
    /// Reads everything, once the folder can be used: the settings (with the
    /// one-time copy of the old preferences), the library, the marker; then
    /// starts the remote-control server if it is on, and makes the automatic
    /// backup of the day in the background.
    /// </summary>
    public async Task LoadAsync()
    {
        if (IsLoaded) return;
        if (!_settings.IsInitialized)
        {
            await _settings.InitializeAsync(_localPreferences, MovedPreferenceKeys);
        }
        await _service.InitializeAsync();
        _notices.ReportLoadFailures();
        await WriteMarkerAsync();
        IsLoaded = true;
        LoadedChanged?.Invoke();

        // Its settings are in settings.json, read just above.
        _controlServer.ApplySavedSettings();

        // The first start of the day on this machine makes a backup, once the
        // saves the load itself started are on disk. Not after a file failed to
        // load: the backups kept would slowly become copies of the broken state.
        if (_store.BlockedFiles.Count == 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _store.FlushAsync();
                    await _backups.AutoBackupIfDueAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Automatic backup failed: {ex}");
                }
            });
        }
    }

    /// <summary>The marker of the data folder: created with a new LibraryId the first time, its last writer updated each time.</summary>
    public async Task WriteMarkerAsync()
    {
        try
        {
            var current = DataFolderMarker.Read(_location.Root);
            var marker = DataFolderMarker.Create(_location.AppName, current?.LibraryId ?? Guid.NewGuid(), _location.MachineName, _service.GetVersion());
            await _store.SaveTextAsync(DataFolderMarker.FileName, marker.ToJson(), overwriteExternalChange: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"The marker could not be written: {ex.Message}");
        }
    }

    // ── moving ──────────────────────────────────────────────────────────

    public string? PreviousRoot
    {
        get
        {
            string value = _localPreferences.Get(PreviousRootKey, string.Empty) ?? string.Empty;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>The size of the audio files of the data folder, shown before a move.</summary>
    public long AudioSize => DataFolderMove.SizeOf(_location.Root, new[] { AudioFolderName });

    /// <summary>Checks a folder chosen as the new data folder (spec §5.6). Changes nothing but may create the folder.</summary>
    public MoveTargetResult CheckTarget(string folder)
    {
        long needed = DataFolderMove.SizeOf(_location.Root, MovedEntries);
        var appData = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        };
        var result = DataFolderMove.CheckTarget(folder, _location.Root, _location.DefaultRoot, _location.AppName, needed, appData);

        // The default folder may still hold the data from before a move,
        // without a marker if it was never opened by this version: that is a
        // library, not "some other files" calling for a subfolder.
        if (result.Check == MoveTargetCheck.UseSubfolder && IsDefault(folder)
            && BackupService.DataFileNames.Any(n => File.Exists(Path.Combine(folder, n))))
        {
            if (Directory.Exists(result.Folder) && !Directory.EnumerateFileSystemEntries(result.Folder).Any())
            {
                Directory.Delete(result.Folder);   // the subfolder the check made
            }
            return result with { Check = MoveTargetCheck.ExistingLibrary, Folder = _location.DefaultRoot };
        }
        return result;
    }

    /// <summary>
    /// Moves the data, audio files included, to an empty folder (or the
    /// default one): stops and saves everything, makes a before-move backup,
    /// copies and checks (a copy cut off earlier is resumed), writes the
    /// marker, then points at the new folder. The caller restarts the app.
    /// </summary>
    public async Task MoveAsync(string target, IProgress<(int Done, int Total)>? progress = null)
    {
        await StopAndFlushAsync();
        await _backups.CreateBackupAsync(BackupReason.BeforeMove);
        var blocked = await BlockAllAsync();
        try
        {
            await DataFolderMove.CopyAsync(_location.Root, MovedEntries, target, Array.Empty<string>(), progress, resume: true);
            WriteMarkerTo(target);
        }
        catch
        {
            Unblock(blocked);
            ResumeActivity();
            throw;
        }
        _location.SetPointer(IsDefault(target) ? null : target);
        _localPreferences.Set(PreviousRootKey, _location.Root);
    }

    /// <summary>"Use the folder's data": points at a folder that already holds DiceAudio data. Nothing is copied.</summary>
    public async Task UseExistingAsync(string target)
    {
        await StopAndFlushAsync();
        await BlockAllAsync();
        _location.SetPointer(IsDefault(target) ? null : target);
    }

    /// <summary>
    /// "Replace it with mine": the folder's data is backed up in its own
    /// Backups/, then replaced by this computer's data, which is then used.
    /// Its audio files stay, joined by this computer's (one of the same name
    /// but a different file is renamed first, and its items follow).
    /// </summary>
    public async Task ReplaceExistingAsync(string target, IProgress<(int Done, int Total)>? progress = null)
    {
        await StopAndFlushAsync();
        if (RenameAudioClashes(target, new List<(string From, string To)>()) > 0)
        {
            await _service.SaveEverythingAsync();
            await _store.FlushAsync();
        }
        // Two backups made in the same second: this computer's name must stay
        // free in the folder's Backups/, which this computer's Backups/ joins.
        await _backups.CreateBackupOfFolderAsync(target, BackupReason.BeforeMove);
        await _backups.CreateBackupAsync(BackupReason.BeforeMove, new[] { Path.Combine(target, "Backups") });
        var blocked = await BlockAllAsync();
        try
        {
            await DataFolderMove.CopyAsync(_location.Root, MovedEntries, target, BackupService.DataFileNames, progress, resume: true);
            WriteMarkerTo(target);
        }
        catch
        {
            Unblock(blocked);
            ResumeActivity();
            throw;
        }
        string oldRoot = _location.Root;
        _location.SetPointer(IsDefault(target) ? null : target);
        _localPreferences.Set(PreviousRootKey, oldRoot);
    }

    /// <summary>
    /// Before a merge: playback stops and this computer's audio files that
    /// clash with the folder's are renamed (see RenameAudioClashes), so that
    /// the imported items find the folder's files under their own names.
    /// Returns the renames, for <see cref="UndoRenamesAsync"/> when the merge
    /// is cancelled.
    /// </summary>
    public async Task<IReadOnlyList<(string From, string To)>> PrepareMergeAsync(string target)
    {
        await _playback.StopAllAsync();
        DAAudioPlayer.DisposePlayersUnder(_location.GetPath(AudioFolderName));
        var renames = new List<(string From, string To)>();
        RenameAudioClashes(target, renames);
        if (renames.Count > 0)
        {
            await _service.SaveEverythingAsync();
            await _store.FlushAsync();
        }
        return renames;
    }

    /// <summary>The merge was cancelled: the files renamed for it get their names back, and their items follow.</summary>
    public async Task UndoRenamesAsync(IReadOnlyList<(string From, string To)> renames)
    {
        if (renames.Count == 0) return;
        string mine = _location.GetPath(AudioFolderName);
        foreach (var (from, to) in renames.Reverse())
        {
            try
            {
                if (File.Exists(Path.Combine(mine, from)) || !File.Exists(Path.Combine(mine, to))) continue;
                File.Move(Path.Combine(mine, to), Path.Combine(mine, from));
                foreach (var item in _service.AudioItems.Where(i => string.Equals(i.LocalFileName, to, StringComparison.OrdinalIgnoreCase)))
                {
                    item.LocalFileName = from;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"{to} could not get its name back: {ex.Message}");
            }
        }
        await _service.SaveEverythingAsync();
        await _store.FlushAsync();
    }

    /// <summary>
    /// A merge: the pinned scenario groups of the other folder join this
    /// computer's, before its data replaces the folder's.
    /// </summary>
    public async Task MergePinsFromAsync(string folder)
    {
        string path = Path.Combine(folder, DiceAudioService.PinnedScenarioGroupsFileName);
        if (!File.Exists(path)) return;
        List<Guid> pins;
        try
        {
            pins = JsonSerializer.Deserialize<List<Guid>>(File.ReadAllText(path)) ?? new List<Guid>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"The other folder's pins could not be read: {ex.Message}");
            return;
        }
        await _service.MergePinnedScenarioGroupsAsync(pins);
    }

    /// <summary>
    /// A file of this computer's Audio/ and a different one of the folder's
    /// (not the same size) with the same name: the copy never overwrites the
    /// folder's file, so this computer's is renamed first ("Name (2).mp3")
    /// and its items follow. Playback must be stopped.
    /// </summary>
    private int RenameAudioClashes(string target, List<(string From, string To)> renames)
    {
        string mine = _location.GetPath(AudioFolderName);
        string theirs = Path.Combine(target, AudioFolderName);
        if (!Directory.Exists(mine) || !Directory.Exists(theirs)) return 0;

        int renamed = 0;
        foreach (string file in Directory.EnumerateFiles(mine).ToList())
        {
            string name = Path.GetFileName(file);
            string other = Path.Combine(theirs, name);
            if (!File.Exists(other) || new FileInfo(other).Length == new FileInfo(file).Length) continue;

            string extension = Path.GetExtension(name);
            string newName = DAAudioFileNames.UniqueBaseName(Path.GetFileNameWithoutExtension(name), extension,
                                 n => File.Exists(Path.Combine(mine, n)) || File.Exists(Path.Combine(theirs, n))) + extension;
            File.Move(file, Path.Combine(mine, newName));
            renames.Add((name, newName));
            foreach (var item in _service.AudioItems.Where(i => string.Equals(i.LocalFileName, name, StringComparison.OrdinalIgnoreCase)))
            {
                item.LocalFileName = newName;
            }
            renamed++;
        }
        return renamed;
    }

    /// <summary>
    /// The library of another folder, as a bundle: "Merge" imports it into
    /// this computer's data with the import wizard, then replaces the folder's
    /// data with the result. Its audio files are found again in its own Audio/.
    /// </summary>
    public static DABundle ReadFolderAsBundle(string folder, string appVersion)
    {
        List<T> Load<T>(string name)
        {
            string path = Path.Combine(folder, name);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), DABundleReader.ReadOptions) ?? new List<T>()
                : new List<T>();
        }

        var snapshot = new DALibrarySnapshot
        {
            AudioItems = Load<DAAudioItem>(DiceAudioService.AudioItemsFileName),
            Folders = Load<DAVirtualAudioFolder>(DiceAudioService.AudioVirtualFoldersFileName),
            Scenes = Load<DAScene>(DiceAudioService.ScenesFileName),
            ScenarioGroups = Load<DAScenarioGroup>(DiceAudioService.ScenarioGroupsFileName),
            Presets = Load<DAPreset>(DiceAudioService.PresetsFileName),
            TypeTags = Load<string>(DAAudioTagsProvider.TypeFileName),
            LocationTags = Load<string>(DAAudioTagsProvider.LocationFileName),
            ExpressionTags = Load<string>(DAAudioTagsProvider.ExpressionFileName),
        };
        return new DABundleCollector(snapshot, appVersion).ForLibrary();
    }

    /// <summary>A folder the check made for nothing (the move was cancelled) is removed, when it is still empty.</summary>
    public static void DiscardTarget(MoveTargetResult check)
    {
        if (check.Check is MoveTargetCheck.Ok or MoveTargetCheck.UseSubfolder
            && Directory.Exists(check.Folder) && !Directory.EnumerateFileSystemEntries(check.Folder).Any())
        {
            try { Directory.Delete(check.Folder); }
            catch (IOException) { }
        }
    }

    /// <summary>Deletes the data left in the previous folder after a move, audio files included, once the user asks.</summary>
    public void DeleteOldCopy()
    {
        string? previous = PreviousRoot;
        if (previous == null) return;
        if (Directory.Exists(previous) && !DataFolderMove.IsSameOrInside(previous, _location.Root))
        {
            DataFolderMove.DeleteOldCopy(previous, MovedEntries, keepFolder: IsDefault(previous));
        }
        _localPreferences.Set(PreviousRootKey, string.Empty);
    }

    public bool IsDefault(string folder) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), Path.TrimEndingDirectorySeparator(_location.DefaultRoot),
                      OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>The remote-control server port paused by a move; null when it was not running.</summary>
    private int? _pausedServerPort;

    /// <summary>
    /// Nothing may play from, download into or save the data while it moves:
    /// the downloads stop (and the one running winds down), the remote-control
    /// server pauses, playback stops and every audio file is let go; then
    /// everything in memory goes to disk.
    /// </summary>
    private async Task StopAndFlushAsync()
    {
        _downloads.CancelAll();
        var idle = _downloads.WhenIdleAsync();
        if (await Task.WhenAny(idle, Task.Delay(DownloadStopTimeout)) != idle)
            Debug.WriteLine("The downloads did not stop within 5 s; the move goes on.");

        if (_controlServer.IsRunning)
        {
            _pausedServerPort = _controlServer.Port;
            _controlServer.Stop();
        }

        await _playback.StopAllAsync();
        DAAudioPlayer.DisposePlayersUnder(_location.GetPath(AudioFolderName));

        await _service.SaveEverythingAsync();
        await _store.FlushAsync();
    }

    /// <summary>After a move that failed: the remote-control server comes back.</summary>
    private void ResumeActivity()
    {
        if (_pausedServerPort is int port)
        {
            _pausedServerPort = null;
            _controlServer.Start(port);
        }
    }

    // From here until the restart, nothing is written to the old folder: a
    // save would be lost, since the copy is already made.
    // Returns the files it blocked, so that a failed move lifts only those:
    // a file that failed to load stays blocked.
    private async Task<List<string>> BlockAllAsync()
    {
        var blocked = new List<string>();
        foreach (string name in BackupService.DataFileNames)
        {
            if (_store.IsBlocked(name)) continue;
            await _store.BlockAsync(name, "The data is being moved");
            blocked.Add(name);
        }
        return blocked;
    }

    private void Unblock(IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            _store.ForgetFile(name);
        }
    }

    private void WriteMarkerTo(string target)
    {
        var current = DataFolderMarker.Read(_location.Root);
        DataFolderMarker.Create(_location.AppName, current?.LibraryId ?? Guid.NewGuid(), _location.MachineName, _service.GetVersion())
                        .WriteTo(target);
    }
}
