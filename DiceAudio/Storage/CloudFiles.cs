/*
 * Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Shared by DiceAudio and DiceCombats, both released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */
// Mirrored in DiceCombats/DiceCombats/Storage/CloudFiles.cs — keep both in sync.

using System.Diagnostics;

namespace DiceAudio.Storage;

/// <summary>
/// What a synchronisation service (OneDrive, Dropbox) leaves in a data folder
/// (spec §5.7): conflict copies of the data files, and files that are only in
/// the cloud (Files On-Demand), which the app would have to wait for.
/// </summary>
public static class CloudFiles
{
    /// <summary>FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS: reading the file downloads it first.</summary>
    public const int RecallOnDataAccess = 0x400000;

    /// <summary>FILE_ATTRIBUTE_OFFLINE.</summary>
    public const int Offline = 0x1000;

    /// <summary>Whether attributes say the content is not on this device.</summary>
    public static bool IsCloudOnly(FileAttributes attributes) =>
        ((int)attributes & (RecallOnDataAccess | Offline)) != 0;

    /// <summary>The attributes of a file; null when it cannot be read. Never downloads it.</summary>
    public static FileAttributes? GetAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"Attributes of {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Whether a file exists and is only in the cloud.</summary>
    public static bool IsCloudOnly(string path, Func<string, FileAttributes?>? getAttributes = null) =>
        (getAttributes ?? GetAttributes)(path) is FileAttributes attributes && IsCloudOnly(attributes);

    /// <summary>The data files of a folder that exist and are only in the cloud, by name.</summary>
    public static IReadOnlyList<string> FindCloudOnly(string folder, IEnumerable<string> fileNames,
                                                      Func<string, FileAttributes?>? getAttributes = null)
    {
        var found = new List<string>();
        foreach (string name in fileNames)
        {
            string path = Path.Combine(folder, name);
            if (File.Exists(path) && IsCloudOnly(path, getAttributes)) found.Add(name);
        }
        return found;
    }

    /// <summary>
    /// Whether a file name is a conflict copy of one of the data files: the
    /// stem of a data file followed by '-', ' ' or '(', ending in ".json", and
    /// not itself a data file. "creatures-YANN-PC.json" (OneDrive),
    /// "creatures (Yann's conflicted copy 2026-10-14).json" (Dropbox).
    /// </summary>
    public static bool IsConflictCopy(string fileName, IReadOnlyCollection<string> dataFileNames)
    {
        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
        if (dataFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)) return false;
        foreach (string known in dataFileNames)
        {
            if (!known.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            string stem = Path.GetFileNameWithoutExtension(known);
            if (fileName.Length > stem.Length + ".json".Length
                && fileName.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
                && fileName[stem.Length] is '-' or ' ' or '(')
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The conflict copies of the data files in a folder (its top level only), by name.</summary>
    public static IReadOnlyList<string> FindConflictCopies(string folder, IReadOnlyCollection<string> dataFileNames)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                            .Select(Path.GetFileName)
                            .Where(name => name != null && IsConflictCopy(name, dataFileNames))
                            .Select(name => name!)
                            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                            .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Debug.WriteLine($"Listing {folder}: {ex.Message}");
            return Array.Empty<string>();
        }
    }
}
