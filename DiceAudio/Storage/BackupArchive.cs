/*
 * Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Shared by DiceAudio and DiceCombats, both released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */
// Mirrored in DiceCombats/DiceCombats/Storage/BackupArchive.cs — keep both in sync.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiceAudio.Storage;

public sealed record BackupFileEntry(string Name, long Size, string Sha256);

public sealed record BackupManifest(string Format, int Version, string App, string AppVersion, DateTime CreatedAtUtc,
                                    string Machine, string Reason, IReadOnlyList<BackupFileEntry> Files,
                                    IReadOnlyDictionary<string, int> Summary);

public enum BackupCheck { Ok, WrongApp, NewerVersion, Damaged }

/// <summary>
/// A backup is a zip holding the data files, flat at its root, and
/// backup-manifest.json, which lists each file with its size and SHA-256.
/// Nothing here knows which app it serves: the caller gives the file names.
/// </summary>
public static class BackupArchive
{
    public const int CurrentVersion = 1;
    public const string FormatName = "Dice.Backup";
    public const string ManifestFileName = "backup-manifest.json";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Writes each file of <paramref name="fileNames"/> that
    /// <paramref name="readFile"/> returns (null = the file does not exist),
    /// then the manifest, whose Files list is filled here from what was
    /// actually written. The caller reads the files (through SafeFileStore),
    /// so a file being saved at that moment is never missed.
    /// </summary>
    public static async Task WriteAsync(Stream output, Func<string, CancellationToken, Task<byte[]?>> readFile, IReadOnlyList<string> fileNames, BackupManifest manifestWithoutFiles, CancellationToken ct = default)
    {
        var files = new List<BackupFileEntry>();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string name in fileNames)
            {
                CheckName(name);

                // Read once: the bytes hashed are the bytes stored.
                byte[]? bytes = await readFile(name, ct).ConfigureAwait(false);
                if (bytes == null) continue;
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using (var entryStream = entry.Open())
                {
                    await entryStream.WriteAsync(bytes, ct).ConfigureAwait(false);
                }
                files.Add(new BackupFileEntry(name, bytes.LongLength, Hash(bytes)));
            }

            var manifest = manifestWithoutFiles with { Format = FormatName, Version = CurrentVersion, Files = files };
            var manifestEntry = zip.CreateEntry(ManifestFileName, CompressionLevel.Optimal);
            await using var manifestStream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(manifestStream, manifest, WriteOptions, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Reads the manifest. Throws InvalidDataException when the zip or the manifest cannot be read.</summary>
    public static async Task<BackupManifest> ReadManifestAsync(Stream zip, CancellationToken ct = default)
    {
        try
        {
            if (zip.CanSeek) zip.Position = 0;
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            var entry = archive.GetEntry(ManifestFileName) ?? throw new InvalidDataException("The backup has no manifest.");
            await using var stream = entry.Open();
            var node = await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false) as JsonObject
                       ?? throw new InvalidDataException("The manifest is not a JSON object.");

            // A manifest without a version is a version 1 manifest. Later
            // versions are upgraded here, one step at a time, before reading.
            if (!node.ContainsKey(nameof(BackupManifest.Version))) node[nameof(BackupManifest.Version)] = 1;

            return node.Deserialize<BackupManifest>(ReadOptions) ?? throw new InvalidDataException("The manifest is empty.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException("The backup cannot be read: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// Checks a backup before anything is restored from it: the format, the
    /// app, the version, then every listed file is present, has the listed size
    /// and hash, and is one of <paramref name="allowedFileNames"/>.
    /// </summary>
    public static async Task<BackupCheck> VerifyAsync(Stream zip, string expectedApp, IReadOnlyList<string> allowedFileNames, CancellationToken ct = default)
    {
        BackupManifest manifest;
        try
        {
            manifest = await ReadManifestAsync(zip, ct).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return BackupCheck.Damaged;
        }

        if (manifest.Format != FormatName) return BackupCheck.Damaged;
        if (!string.Equals(manifest.App, expectedApp, StringComparison.Ordinal)) return BackupCheck.WrongApp;
        if (manifest.Version > CurrentVersion) return BackupCheck.NewerVersion;
        if (manifest.Files == null) return BackupCheck.Damaged;

        try
        {
            zip.Position = 0;
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                if (file == null || !allowedFileNames.Contains(file.Name, StringComparer.Ordinal) || !seen.Add(file.Name))
                {
                    return BackupCheck.Damaged;
                }
                var entry = archive.GetEntry(file.Name);
                if (entry == null) return BackupCheck.Damaged;

                byte[] bytes = await ReadEntryAsync(entry, ct).ConfigureAwait(false);
                if (bytes.LongLength != file.Size || !string.Equals(Hash(bytes), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return BackupCheck.Damaged;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return BackupCheck.Damaged;
        }
        return BackupCheck.Ok;
    }

    /// <summary>
    /// Writes into <paramref name="targetDir"/> the files the manifest lists
    /// that are also in <paramref name="allowedFileNames"/>; anything else in
    /// the zip is ignored. Only those exact names are read, so no path from the
    /// zip is ever used. Call VerifyAsync first.
    /// </summary>
    public static async Task ExtractAsync(Stream zip, string targetDir, IReadOnlyList<string> allowedFileNames, CancellationToken ct = default)
    {
        var manifest = await ReadManifestAsync(zip, ct).ConfigureAwait(false);
        Directory.CreateDirectory(targetDir);
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        foreach (string name in manifest.Files.Select(f => f.Name).Where(n => allowedFileNames.Contains(n, StringComparer.Ordinal)))
        {
            CheckName(name);
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"{name} is listed but missing.");
            byte[] bytes = await ReadEntryAsync(entry, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.Combine(targetDir, name), bytes, ct).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        await using var stream = entry.Open();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct).ConfigureAwait(false);
        return memory.ToArray();
    }

    private static void CheckName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name == "." || name == "..")
        {
            throw new ArgumentException($"'{name}' is not a plain file name.", nameof(name));
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
