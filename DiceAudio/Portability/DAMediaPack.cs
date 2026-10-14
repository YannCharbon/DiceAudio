/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiceAudio.Portability
{
    /// <summary>The <c>media-manifest.json</c> of a .diceamedia file (spec §2.4).</summary>
    public sealed class DAMediaManifest
    {
        public const string FormatName = "DiceAudio.MediaPack";
        public const int CurrentVersion = 1;
        public const string FileExtension = ".diceamedia";
        public const string EntryName = "media-manifest.json";

        public string Format { get; set; } = FormatName;
        public int FormatVersion { get; set; } = CurrentVersion;
        public Guid BundleId { get; set; }
        public string AppVersion { get; set; } = "";
        public string CreatedAtUtc { get; set; } = DateTime.UtcNow.ToString("o");
        public List<DAMediaManifestEntry> Entries { get; set; } = new();
    }

    public sealed class DAMediaManifestEntry
    {
        public Guid ItemId { get; set; }
        /// <summary><c>audio/&lt;itemGuid&gt;.&lt;ext&gt;</c>: never a name taken from the user's files.</summary>
        public string Path { get; set; } = "";
        /// <summary>The bare file name on the exporting computer; the importer builds its own name from it.</summary>
        public string OriginalFileName { get; set; } = "";
        public long SizeBytes { get; set; }
        /// <summary>Lowercase hex SHA-256 of the file, only when the export asked for checksums.</summary>
        public string? Sha256 { get; set; }
    }

    /// <summary>An audio file to put in the pack: the item it belongs to, and where it is on this computer.</summary>
    public sealed record DAMediaSource(Guid ItemId, string OriginalFileName, string FullPath);

    /// <summary>How far the writing is: files and bytes done, out of the totals.</summary>
    public readonly record struct DAMediaProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal);

    /// <summary>
    /// Writes a .diceamedia file (spec §2.4): a zip whose audio entries are
    /// stored without compression (audio does not compress, and storing is
    /// fast), streamed file by file so the pack is never held in memory, with
    /// Zip64 as soon as it is needed. The manifest goes in last, because the
    /// checksums are computed while the files stream. No MAUI calls.
    /// </summary>
    public static class DAMediaPackWriter
    {
        private static readonly Regex SafeExtension = new("^[a-z0-9]{1,5}$", RegexOptions.CultureInvariant);

        private static readonly JsonSerializerOptions ManifestOptions = new() { WriteIndented = true };

        /// <summary>The entry path of an item's file, or null when its extension cannot be carried.</summary>
        public static string? EntryPathFor(Guid itemId, string originalFileName)
        {
            string ext = System.IO.Path.GetExtension(originalFileName).TrimStart('.').ToLowerInvariant();
            return SafeExtension.IsMatch(ext) ? $"audio/{itemId:D}.{ext}" : null;
        }

        /// <summary>
        /// Writes the pack into <paramref name="output"/> (which it does not
        /// close). Files that are missing, unreadable or have no usable
        /// extension are left out; the manifest lists only what is in the zip.
        /// Throws <see cref="OperationCanceledException"/> when cancelled: the
        /// caller then deletes the partial output.
        /// </summary>
        public static async Task<DAMediaManifest> WriteAsync(
            Stream output, Guid bundleId, string appVersion, IReadOnlyList<DAMediaSource> sources,
            bool checksums, IProgress<DAMediaProgress>? progress = null, CancellationToken ct = default)
        {
            var manifest = new DAMediaManifest { BundleId = bundleId, AppVersion = appVersion };

            var files = sources
                .Select(s => (Source: s, Entry: EntryPathFor(s.ItemId, s.OriginalFileName)))
                .Where(f => f.Entry != null && File.Exists(f.Source.FullPath))
                .GroupBy(f => f.Source.ItemId).Select(g => g.First())
                .ToList();
            long bytesTotal = files.Sum(f => SafeLength(f.Source.FullPath));
            long bytesDone = 0;
            int filesDone = 0;
            progress?.Report(new DAMediaProgress(0, files.Count, 0, bytesTotal));

            var buffer = new byte[1 << 20];
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (source, entryPath) in files)
                {
                    ct.ThrowIfCancellationRequested();

                    FileStream input;
                    try
                    {
                        input = new FileStream(source.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        continue;   // a file that cannot be opened is simply not in the pack
                    }

                    await using (input)
                    {
                        var entry = zip.CreateEntry(entryPath!, CompressionLevel.NoCompression);
                        using var hash = checksums ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
                        long size = 0;
                        await using (var target = entry.Open())
                        {
                            int read;
                            while ((read = await input.ReadAsync(buffer, ct)) > 0)
                            {
                                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                                hash?.AppendData(buffer, 0, read);
                                size += read;
                                bytesDone += read;
                                progress?.Report(new DAMediaProgress(filesDone, files.Count, bytesDone, bytesTotal));
                            }
                        }

                        manifest.Entries.Add(new DAMediaManifestEntry
                        {
                            ItemId = source.ItemId,
                            Path = entryPath!,
                            OriginalFileName = System.IO.Path.GetFileName(source.OriginalFileName),
                            SizeBytes = size,
                            Sha256 = hash == null ? null : Convert.ToHexStringLower(hash.GetHashAndReset()),
                        });
                    }

                    filesDone++;
                    progress?.Report(new DAMediaProgress(filesDone, files.Count, bytesDone, bytesTotal));
                }

                ct.ThrowIfCancellationRequested();
                var manifestEntry = zip.CreateEntry(DAMediaManifest.EntryName, CompressionLevel.Optimal);
                await using var manifestStream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(manifestStream, manifest, ManifestOptions, ct);
            }

            return manifest;
        }

        /// <summary>Reads the manifest of a pack, for checks; the full reading rules come with the import.</summary>
        public static DAMediaManifest? ReadManifest(ZipArchive zip)
        {
            var entry = zip.GetEntry(DAMediaManifest.EntryName);
            if (entry == null) return null;
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<DAMediaManifest>(stream, ManifestOptions);
        }

        private static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
        }
    }

    /// <summary>Why a media pack, or one file of it, cannot be used.</summary>
    public enum DAMediaPackError { None, Unreadable, Mismatch, NewerFormat }

    /// <summary>
    /// Reads a .diceamedia file for an import (spec §2.4), zip-slip safe: only
    /// the entries the manifest lists are ever read, each entry path must be
    /// exactly <c>audio/&lt;guid&gt;.&lt;ext&gt;</c> with the guid of its item, and
    /// nothing is ever written under a name taken from the zip — the caller
    /// chooses the file name, from the original name the manifest gives.
    /// </summary>
    public sealed class DAMediaPackReader : IDisposable
    {
        private static readonly Regex EntryPattern = new(
            "^audio/(?<id>[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12})\\.(?<ext>[a-z0-9]{1,5})$", RegexOptions.CultureInvariant);

        public const long MaxEntryBytes = 4L * 1024 * 1024 * 1024;

        private readonly ZipArchive _zip;

        private DAMediaPackReader(ZipArchive zip, DAMediaManifest manifest, Dictionary<Guid, DAMediaManifestEntry> usable, List<string> rejected)
        {
            _zip = zip;
            Manifest = manifest;
            Usable = usable;
            Rejected = rejected;
        }

        public DAMediaManifest Manifest { get; }

        /// <summary>The entries that passed every check, by the item they belong to (the item's id in the bundle).</summary>
        public IReadOnlyDictionary<Guid, DAMediaManifestEntry> Usable { get; }

        /// <summary>Manifest entries that were refused, by their original file name.</summary>
        public IReadOnlyList<string> Rejected { get; }

        /// <summary>
        /// Opens a pack against its bundle. <paramref name="allowedExtensions"/>
        /// are the audio types the app accepts (".mp3"…). The stream stays
        /// open as long as the reader.
        /// </summary>
        public static (DAMediaPackReader? Reader, DAMediaPackError Error) Open(
            Stream stream, DABundle bundle, IReadOnlyCollection<string> allowedExtensions)
        {
            ZipArchive zip;
            DAMediaManifest? manifest;
            try
            {
                zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                var entry = zip.GetEntry(DAMediaManifest.EntryName);
                if (entry == null || entry.Length > 10 * 1024 * 1024) { zip.Dispose(); return (null, DAMediaPackError.Unreadable); }
                using var ms = entry.Open();
                manifest = JsonSerializer.Deserialize<DAMediaManifest>(ms, DABundleReader.ReadOptions);
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or NotSupportedException)
            {
                return (null, DAMediaPackError.Unreadable);
            }

            if (manifest == null || manifest.Format != DAMediaManifest.FormatName) { zip.Dispose(); return (null, DAMediaPackError.Unreadable); }
            if (manifest.FormatVersion > DAMediaManifest.CurrentVersion) { zip.Dispose(); return (null, DAMediaPackError.NewerFormat); }
            if (manifest.BundleId != bundle.BundleId) { zip.Dispose(); return (null, DAMediaPackError.Mismatch); }

            var items = bundle.AudioItems.Select(i => i.Id).ToHashSet();
            var allowed = allowedExtensions.Select(e => e.TrimStart('.').ToLowerInvariant()).ToHashSet();
            var usable = new Dictionary<Guid, DAMediaManifestEntry>();
            var rejected = new List<string>();

            foreach (var e in manifest.Entries ?? new())
            {
                if (e == null) continue;
                var m = EntryPattern.Match(e.Path ?? "");
                bool ok = m.Success
                          && Guid.TryParse(m.Groups["id"].Value, out var id) && id == e.ItemId
                          && items.Contains(e.ItemId)
                          && allowed.Contains(m.Groups["ext"].Value)
                          && e.SizeBytes >= 0 && e.SizeBytes <= MaxEntryBytes
                          && zip.GetEntry(e.Path!) is { } z && z.Length == e.SizeBytes
                          && !usable.ContainsKey(e.ItemId);
                if (ok) usable[e.ItemId] = e;
                else rejected.Add(string.IsNullOrEmpty(e.OriginalFileName) ? e.Path ?? "?" : System.IO.Path.GetFileName(e.OriginalFileName));
            }

            return (new DAMediaPackReader(zip, manifest, usable, rejected), DAMediaPackError.None);
        }

        /// <summary>
        /// Copies the file of <paramref name="itemId"/> to <paramref name="targetPath"/>
        /// (a path the caller built), checking the size and, when present, the
        /// hash. Writes a ".part" file first and never replaces an existing
        /// file. False when the entry is unusable or damaged; nothing is left behind then.
        /// </summary>
        public async Task<bool> ExtractAsync(Guid itemId, string targetPath, CancellationToken ct = default)
        {
            if (!Usable.TryGetValue(itemId, out var e) || File.Exists(targetPath)) return false;
            var entry = _zip.GetEntry(e.Path);
            if (entry == null) return false;

            string part = targetPath + ".part";
            try
            {
                using var hash = e.Sha256 != null ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
                long size = 0;
                await using (var source = entry.Open())
                await using (var target = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                {
                    var buffer = new byte[1 << 20];
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        size += read;
                        if (size > e.SizeBytes) break;   // more than announced: damaged
                        await target.WriteAsync(buffer.AsMemory(0, read), ct);
                        hash?.AppendData(buffer, 0, read);
                    }
                }

                bool good = size == e.SizeBytes
                            && (hash == null || string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), e.Sha256, StringComparison.OrdinalIgnoreCase));
                if (!good) { File.Delete(part); return false; }

                File.Move(part, targetPath, overwrite: false);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
            {
                try { if (File.Exists(part)) File.Delete(part); } catch (IOException) { }
                if (ex is OperationCanceledException) throw;
                return false;
            }
        }

        public void Dispose() => _zip.Dispose();
    }
}
