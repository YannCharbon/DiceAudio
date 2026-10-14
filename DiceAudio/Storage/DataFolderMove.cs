/*
 * Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Shared by DiceAudio and DiceCombats, both released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */
// Mirrored in DiceCombats/DiceCombats/Storage/DataFolderMove.cs — keep both in sync.

using System.Diagnostics;
using System.Security.Cryptography;

namespace DiceAudio.Storage;

public enum MoveTargetCheck
{
    /// <summary>Empty (machine-only things aside): the data can be moved there.</summary>
    Ok,
    /// <summary>The folder was not empty and held no data: the app's own subfolder is used instead (see Folder).</summary>
    UseSubfolder,
    /// <summary>Holds this app's data already: "use an existing folder".</summary>
    ExistingLibrary,
    NotWritable,
    NoSpace,
    InsideCurrent,
    AppData,
    OtherApp,
    /// <summary>Its marker cannot be read, or it was set up by a newer version, or its subfolder is not usable either.</summary>
    Invalid,
}

/// <param name="Folder">The folder the data would go to: the chosen one, or its app subfolder.</param>
public sealed record MoveTargetResult(MoveTargetCheck Check, string Folder, long NeededBytes, long FreeBytes);

/// <summary>
/// Moving the data folder (spec §5.6), without any app logic: the checks on a
/// target, the copy through &lt;target&gt;\.dice-migrating with its
/// verification, and the deletion of an old copy. The app gives the list of
/// what moves (file and folder names at the root of the data folder).
/// </summary>
public static class DataFolderMove
{
    public const string StagingFolderName = ".dice-migrating";

    /// <summary>Things a folder may hold that are not user data; a folder with only these counts as empty.</summary>
    public static readonly IReadOnlyList<string> MachineOnlyNames = new[]
    {
        "EBWebView", "Tools", "wwwroot", ".dice-debug-preferences.json", ".dice-write-test", "dice.lock", StagingFolderName,
    };

    /// <summary>The size of what moves.</summary>
    public static long SizeOf(string root, IReadOnlyList<string> entries)
    {
        long size = 0;
        foreach (string entry in entries)
        {
            string path = Path.Combine(root, entry);
            if (File.Exists(path)) size += new FileInfo(path).Length;
            else if (Directory.Exists(path)) size += Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }
        return size;
    }

    /// <summary>
    /// The checks of spec §5.6, in order: exists or can be created; writable;
    /// not the current folder nor inside it; not under AppData (the default
    /// folder aside); enough free space (+10 %); the marker of another app; then
    /// empty, this app's data, or anything else (then the app's subfolder,
    /// checked the same way, once).
    /// </summary>
    public static MoveTargetResult CheckTarget(string target, string currentRoot, string defaultRoot, string appName, long neededBytes,
                                               IReadOnlyList<string> appDataFolders, bool allowSubfolder = true)
    {
        // A folder this check had to create is removed again when the data
        // does not go there, so that a refused choice leaves nothing behind.
        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        bool existed = Directory.Exists(folder);
        var result = CheckTargetCore(folder, currentRoot, defaultRoot, appName, neededBytes, appDataFolders, allowSubfolder);
        if (!existed && result.Check is not (MoveTargetCheck.Ok or MoveTargetCheck.UseSubfolder) && Directory.Exists(folder)
            && !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            try { Directory.Delete(folder); } catch (IOException) { }
        }
        return result;
    }

    private static MoveTargetResult CheckTargetCore(string folder, string currentRoot, string defaultRoot, string appName, long neededBytes,
                                                    IReadOnlyList<string> appDataFolders, bool allowSubfolder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string test = Path.Combine(folder, ".dice-write-test");
            File.WriteAllText(test, string.Empty);
            File.Delete(test);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Debug.WriteLine($"Target {folder} is not writable: {ex.Message}");
            return new MoveTargetResult(MoveTargetCheck.NotWritable, folder, neededBytes, 0);
        }

        if (IsSameOrInside(folder, currentRoot))
        {
            return new MoveTargetResult(MoveTargetCheck.InsideCurrent, folder, neededBytes, 0);
        }
        if (!IsSameOrInside(folder, defaultRoot) && appDataFolders.Any(a => !string.IsNullOrEmpty(a) && IsSameOrInside(folder, a)))
        {
            return new MoveTargetResult(MoveTargetCheck.AppData, folder, neededBytes, 0);
        }

        long free = FreeBytes(folder);
        long required = neededBytes + neededBytes / 10;
        if (free >= 0 && free < required)
        {
            return new MoveTargetResult(MoveTargetCheck.NoSpace, folder, required, free);
        }

        DataFolderMarker? marker;
        try
        {
            marker = DataFolderMarker.Read(folder);
        }
        catch (InvalidDataException)
        {
            return new MoveTargetResult(MoveTargetCheck.Invalid, folder, required, free);
        }
        if (marker != null)
        {
            if (marker.App != appName) return new MoveTargetResult(MoveTargetCheck.OtherApp, folder, required, free);
            if (marker.Version > DataFolderMarker.CurrentVersion) return new MoveTargetResult(MoveTargetCheck.Invalid, folder, required, free);
            return new MoveTargetResult(MoveTargetCheck.ExistingLibrary, folder, required, free);
        }

        if (IsEmpty(folder))
        {
            return new MoveTargetResult(MoveTargetCheck.Ok, folder, required, free);
        }
        if (!allowSubfolder)
        {
            return new MoveTargetResult(MoveTargetCheck.Invalid, folder, required, free);
        }

        var sub = CheckTarget(Path.Combine(folder, appName), currentRoot, defaultRoot, appName, neededBytes, appDataFolders, allowSubfolder: false);
        return sub.Check == MoveTargetCheck.Ok ? sub with { Check = MoveTargetCheck.UseSubfolder } : sub;
    }

    /// <summary>True when the folder holds nothing but machine-only things.</summary>
    public static bool IsEmpty(string folder) =>
        !Directory.Exists(folder)
        || !Directory.EnumerateFileSystemEntries(folder).Any(e => !MachineOnlyNames.Contains(Path.GetFileName(e), StringComparer.OrdinalIgnoreCase));

    public static bool IsSameOrInside(string path, string folder)
    {
        string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(a, b, comparison) || a.StartsWith(b + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>
    /// Copies what moves into &lt;target&gt;\.dice-migrating, checks every size
    /// and the SHA-256 of every JSON file, then puts each entry in place. A
    /// file the target already has is replaced; a folder is merged, keeping the
    /// target's files of the same name. <paramref name="removeFromTarget"/> are
    /// deleted from the target when the source has none of them (replacing a
    /// library). The source is never changed.
    /// <para>
    /// With <paramref name="resume"/>, a staging folder left by a copy that was
    /// cut off is kept, and every file already in it with the size of the
    /// original is not copied again (DiceAudio: the audio files can be many
    /// gigabytes). JSON files are always copied again, since they are small
    /// and their content is what must be exact.
    /// </para>
    /// </summary>
    public static async Task CopyAsync(string sourceRoot, IReadOnlyList<string> entries, string targetRoot,
                                       IReadOnlyList<string> removeFromTarget, IProgress<(int Done, int Total)>? progress = null,
                                       CancellationToken ct = default, bool resume = false)
    {
        string staging = Path.Combine(targetRoot, StagingFolderName);
        if (!resume && Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        var files = new List<string>();
        foreach (string entry in entries)
        {
            string path = Path.Combine(sourceRoot, entry);
            if (File.Exists(path)) files.Add(entry);
            else if (Directory.Exists(path)) files.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(sourceRoot, f)));
        }

        int done = 0;
        foreach (string relative in files)
        {
            ct.ThrowIfCancellationRequested();
            string from = Path.Combine(sourceRoot, relative);
            string to = Path.Combine(staging, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (File.Exists(to))
            {
                // Only a resumed copy finds files here: one of the right size
                // was finished before the cut; anything else starts again.
                if (!relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    && new FileInfo(to).Length == new FileInfo(from).Length)
                {
                    progress?.Report((++done, files.Count));
                    continue;
                }
                File.Delete(to);
            }
            await using (var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            await using (var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, ct);
                output.Flush(flushToDisk: true);
            }
            progress?.Report((++done, files.Count));
        }

        if (resume)
        {
            // A file the cut-off copy left that the source no longer has (deleted
            // since) must not reach the target.
            var wanted = new HashSet<string>(files.Select(f => Path.GetFullPath(Path.Combine(staging, f))), StringComparer.OrdinalIgnoreCase);
            foreach (string stale in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).ToList())
            {
                if (!wanted.Contains(Path.GetFullPath(stale))) File.Delete(stale);
            }
        }

        foreach (string relative in files)
        {
            string from = Path.Combine(sourceRoot, relative);
            string to = Path.Combine(staging, relative);
            if (new FileInfo(from).Length != new FileInfo(to).Length
                || (relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && Hash(from) != Hash(to)))
            {
                throw new IOException($"The copy of {relative} is not the same as the original.");
            }
        }

        foreach (string entry in entries)
        {
            string staged = Path.Combine(staging, entry);
            string final = Path.Combine(targetRoot, entry);
            if (File.Exists(staged))
            {
                File.Move(staged, final, overwrite: true);
            }
            else if (Directory.Exists(staged))
            {
                MergeFolder(staged, final);
            }
        }
        foreach (string name in removeFromTarget)
        {
            if (File.Exists(Path.Combine(sourceRoot, name))) continue;
            foreach (string leftover in new[] { name, name + ".bak", name + ".tmp" })
            {
                string path = Path.Combine(targetRoot, leftover);
                if (File.Exists(path)) File.Delete(path);
            }
        }
        Directory.Delete(staging, recursive: true);
    }

    /// <summary>Deletes the entries of an old copy (with their name.bak and name.tmp) and its marker; the folder itself goes when it ends up empty and is not <paramref name="keepFolder"/>.</summary>
    public static void DeleteOldCopy(string root, IReadOnlyList<string> entries, bool keepFolder)
    {
        foreach (string entry in entries.Append(DataFolderMarker.FileName))
        {
            string path = Path.Combine(root, entry);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            foreach (string file in new[] { path, path + ".bak", path + ".tmp" })
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }
        if (!keepFolder && Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
        {
            Directory.Delete(root);
        }
    }

    private static void MergeFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.Move(file, target);
        }
    }

    private static long FreeBytes(string folder)
    {
        try
        {
            string? root = Path.GetPathRoot(folder);
            return string.IsNullOrEmpty(root) ? -1 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return -1;   // unknown (a network share): not refused for it
        }
    }

    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
