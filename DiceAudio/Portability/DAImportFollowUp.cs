/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

namespace DiceAudio.Portability
{
    /// <summary>What the follow-up steps did, for the report screen.</summary>
    public sealed class DAFollowUpReport
    {
        public int FromPack { get; set; }
        public int Matched { get; set; }
        public int Queued { get; set; }
        /// <summary>Names of items whose file in the pack was damaged.</summary>
        public List<string> Rejected { get; } = new();
        /// <summary>Items still without a file and without a link: the user picks a file for each.</summary>
        public List<DAAudioItem> Missing { get; } = new();

        /// <summary>Set when the pack was not extracted for lack of space: what it needed and what the drive had.</summary>
        public (long Needed, long Available)? NotEnoughSpace { get; set; }
    }

    /// <summary>
    /// After an import (pipeline step 5): gives the imported audio items their
    /// files — from the media pack, else from files already in the audio
    /// folder. Writes only new files with names it builds itself, never over
    /// an existing file. No MAUI calls; the app then queues the downloads and
    /// offers "Pick file…" for the rest.
    /// </summary>
    public static class DAImportFollowUp
    {
        /// <summary>
        /// The imported items that still need a file: every audio item this
        /// import added or replaced and whose library item has no file here.
        /// Paired with its record (the original file name and size).
        /// </summary>
        public static List<(DAAudioItem Item, DAAudioItemRecord Record, Guid IncomingId)> ItemsNeedingFiles(
            DAImportPlan plan, DAImportResult result, string audioDir)
        {
            var list = new List<(DAAudioItem, DAAudioItemRecord, Guid)>();
            // Only what this import added or replaced: an item that was up to
            // date, or kept as it was, is left alone (Download and Pick file…
            // in the Explorer are there for those).
            foreach (var planItem in plan.Items.Where(i => i.Kind == DAAssetKind.AudioItem && i.Inserts))
            {
                if (planItem.IncomingId is not Guid id || !result.FinalItems.TryGetValue(id, out var item)) continue;
                if (HasFile(item, audioDir)) continue;
                list.Add((item, (DAAudioItemRecord)planItem.Incoming, id));
            }
            return list;
        }

        private static long DriveFreeSpace(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace; }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return long.MaxValue; }
        }

        public static bool HasFile(DAAudioItem item, string audioDir) =>
            !string.IsNullOrEmpty(item.LocalFileName) && File.Exists(Path.Combine(audioDir, item.LocalFileName));

        /// <summary>
        /// Writes each needed file the pack carries into the audio folder, under
        /// a name made from its original name (invalid characters replaced,
        /// then made unique), and links it to its item. A file whose size or
        /// hash does not match is left out and listed.
        /// </summary>
        /// <param name="freeSpace">The free space of the drive of the audio folder; the drive's own by default.</param>
        public static async Task ExtractPackAsync(DAMediaPackReader pack, List<(DAAudioItem Item, DAAudioItemRecord Record, Guid IncomingId)> needed,
                                                  string audioDir, DAFollowUpReport report, CancellationToken ct = default,
                                                  Func<string, long>? freeSpace = null)
        {
            Directory.CreateDirectory(audioDir);

            // The files must fit on the drive, with a margin of 10 % (spec §2.1);
            // otherwise nothing is extracted, rather than filling the disk halfway.
            long size = needed.Where(n => pack.Usable.ContainsKey(n.IncomingId)).Sum(n => pack.Usable[n.IncomingId].SizeBytes);
            long required = size + size / 10;
            long available = (freeSpace ?? DriveFreeSpace)(audioDir);
            if (size > 0 && available < required)
            {
                report.NotEnoughSpace = (required, available);
                return;
            }

            foreach (var (item, _, incomingId) in needed.ToList())
            {
                if (!pack.Usable.TryGetValue(incomingId, out var entry)) continue;
                string fileName = UniqueFileName(audioDir, SafeFileName(entry.OriginalFileName, entry.Path));
                if (await pack.ExtractAsync(incomingId, Path.Combine(audioDir, fileName), ct))
                {
                    item.LocalFileName = fileName;
                    item.IsLocallyAvailable = true;
                    report.FromPack++;
                    needed.RemoveAll(n => ReferenceEquals(n.Item, item));
                }
                else
                {
                    report.Rejected.Add(item.Name);
                }
            }
        }

        /// <summary>
        /// Looks in the audio folder for a file no item uses that is the one of
        /// a needed item: the same original file name, else a name of the yt-dlp
        /// patterns, else the only unused file of the same size and type.
        /// </summary>
        public static void MatchExistingFiles(List<(DAAudioItem Item, DAAudioItemRecord Record, Guid IncomingId)> needed,
                                              IEnumerable<DAAudioItem> library, string audioDir,
                                              IReadOnlyCollection<string> audioExtensions, DAFollowUpReport report)
        {
            if (!Directory.Exists(audioDir) || needed.Count == 0) return;

            var claimed = new HashSet<string>(library.Where(i => !string.IsNullOrEmpty(i.LocalFileName)).Select(i => i.LocalFileName),
                                              StringComparer.OrdinalIgnoreCase);
            var free = Directory.GetFiles(audioDir)
                .Where(f => audioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(f => new FileInfo(f))
                .Where(f => !claimed.Contains(f.Name))
                .ToList();

            foreach (var (item, record, _) in needed.ToList())
            {
                FileInfo? match =
                    (string.IsNullOrEmpty(record.OriginalFileName) ? null
                        : free.FirstOrDefault(f => string.Equals(f.Name, Path.GetFileName(record.OriginalFileName), StringComparison.OrdinalIgnoreCase)))
                    ?? free.FirstOrDefault(f => DAAudioFileNames.Matches(f.Name, item.Name, item.SourceURL));

                if (match == null && record.SizeBytes is long size && size > 0)
                {
                    string ext = Path.GetExtension(record.OriginalFileName ?? "").ToLowerInvariant();
                    var bySize = free.Where(f => f.Length == size && (ext.Length == 0 || f.Extension.ToLowerInvariant() == ext)).ToList();
                    if (bySize.Count == 1) match = bySize[0];
                }

                if (match == null) continue;
                item.LocalFileName = match.Name;
                item.IsLocallyAvailable = true;
                free.Remove(match);
                report.Matched++;
                needed.RemoveAll(n => ReferenceEquals(n.Item, item));
            }
        }

        /// <summary>A plain file name from the original one: no folder, no invalid character, the extension of the entry kept.</summary>
        public static string SafeFileName(string? originalFileName, string entryPath)
        {
            string ext = Path.GetExtension(entryPath);
            string name = Path.GetFileNameWithoutExtension(Path.GetFileName(originalFileName ?? "")) ?? "";
            var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':' }).ToHashSet();
            name = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            if (name.Length == 0 || name == "." || name == "..") name = "Imported audio";
            if (name.Length > 150) name = name[..150];
            return name + ext;
        }

        /// <summary>"Name.mp3", else "Name (2).mp3"…: never a name already in the folder.</summary>
        public static string UniqueFileName(string audioDir, string fileName)
        {
            string ext = Path.GetExtension(fileName);
            string stem = Path.GetFileNameWithoutExtension(fileName);
            return DAAudioFileNames.UniqueBaseName(stem, ext, candidate => File.Exists(Path.Combine(audioDir, candidate))) + ext;
        }
    }
}
