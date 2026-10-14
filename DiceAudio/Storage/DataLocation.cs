/*
 * Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Shared by DiceAudio and DiceCombats, both released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */
// Mirrored in DiceCombats/DiceCombats/Storage/DataLocation.cs — keep both in sync.

using System.Diagnostics;

namespace DiceAudio.Storage;

public enum DataLocationState { Ok, Missing, Invalid, OpenElsewhere }

/// <summary>
/// Says where the user's data is. The pointer to the data folder is kept in
/// the local preferences ("" = the default folder of this machine); in debug
/// builds, the DICE_DATA_DIR environment variable replaces the default folder
/// and the local preferences, so that a run on a sandbox copy never touches
/// the real data. The app passes the MAUI values in, so this file stays free
/// of MAUI calls.
/// </summary>
public sealed class DataLocation
{
    public const string PointerKey = "DataRootPath";
    public const string DebugOverrideVariable = "DICE_DATA_DIR";

    private const string WriteTestFileName = ".dice-write-test";

    private readonly ILocalPreferences _localPreferences;

    /// <param name="appName">"DiceCombats" or "DiceAudio".</param>
    /// <param name="localRoot">FileSystem.AppDataDirectory. Ignored when <paramref name="debugOverride"/> is set.</param>
    /// <param name="debugOverride">The value of DICE_DATA_DIR in a debug build; always null in a release build.</param>
    public DataLocation(string appName, string localRoot, ILocalPreferences localPreferences, string machineName, string? debugOverride)
    {
        AppName = appName;
        MachineName = machineName;
        _localPreferences = localPreferences;

        IsDebugOverride = !string.IsNullOrWhiteSpace(debugOverride);
        LocalRoot = Path.GetFullPath(IsDebugOverride ? debugOverride! : localRoot);

        string pointer = localPreferences.Get(PointerKey, string.Empty) ?? string.Empty;
        Root = string.IsNullOrWhiteSpace(pointer) ? LocalRoot : Path.GetFullPath(pointer);

        Evaluate();
    }

    public string AppName { get; }

    /// <summary>Machine-only things (Tools/, wwwroot/ release notes): the app data folder, or the debug override.</summary>
    public string LocalRoot { get; }

    public string DefaultRoot => LocalRoot;

    /// <summary>Where the data is now.</summary>
    public string Root { get; private set; }

    public bool IsDefault => PathsEqual(Root, DefaultRoot);
    public bool IsDebugOverride { get; }

    /// <summary>"Use the data on this computer" was chosen at startup, for this session only.</summary>
    public bool IsSessionFallback { get; private set; }

    public DataLocationState State { get; private set; }

    /// <summary>Set when State is OpenElsewhere.</summary>
    public DataFolderLockInfo? OtherLock { get; private set; }

    public string MachineName { get; }

    public string BackupsDir => GetPath("Backups");

    /// <summary>DiceAudio only.</summary>
    public string AudioDir => GetPath("Audio");

    /// <summary>A file or folder of the data folder. The name must be relative and stay inside it.</summary>
    public string GetPath(string fileName) => Combine(Root, fileName);

    /// <summary>A file or folder of the machine-only folder.</summary>
    public string GetLocalPath(string name) => Combine(LocalRoot, name);

    /// <summary>
    /// Checks the data folder again and updates <see cref="State"/>. The
    /// default folder is created when it is missing; any other folder,
    /// including a debug override, never is.
    /// </summary>
    public DataLocationState Evaluate()
    {
        OtherLock = null;
        State = Check();
        return State;
    }

    /// <summary>Null or "" means the default folder. Takes effect at the next start.</summary>
    public void SetPointer(string? path)
    {
        _localPreferences.Set(PointerKey, string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path));
    }

    /// <summary>"Use the data on this computer": the default folder, for this session only. The pointer is kept.</summary>
    public void UseDefaultForThisSession()
    {
        Root = DefaultRoot;
        IsSessionFallback = true;
        Evaluate();
    }

    private DataLocationState Check()
    {
        try
        {
            if (!Directory.Exists(Root))
            {
                if (!IsDefault || IsDebugOverride) return DataLocationState.Missing;
                Directory.CreateDirectory(Root);
            }

            // Listing fails on a drive or a share that has gone away.
            using (var entries = Directory.EnumerateFileSystemEntries(Root).GetEnumerator())
            {
                entries.MoveNext();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Data folder {Root} cannot be reached: {ex.Message}");
            return DataLocationState.Missing;
        }

        try
        {
            string testPath = Path.Combine(Root, WriteTestFileName);
            File.WriteAllText(testPath, string.Empty);
            File.Delete(testPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Data folder {Root} is not writable: {ex.Message}");
            return DataLocationState.Invalid;
        }

        // The marker (dice-data.json) and the lock (dice.lock) are checked
        // here once they exist.
        return DataLocationState.Ok;
    }

    private static string Combine(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name))
        {
            throw new ArgumentException($"'{name}' is not a relative name.", nameof(name));
        }
        if (name.Split('/', '\\').Any(part => part == ".."))
        {
            throw new ArgumentException($"'{name}' leaves the folder.", nameof(name));
        }
        return Path.Combine(root, name);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
                      OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
