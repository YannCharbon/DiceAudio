/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Text.RegularExpressions;

namespace DiceAudio
{
    /// <summary>
    /// How downloaded audio files are named, and how a file in the audio folder
    /// is recognised as the one of an item. Two names exist on disk:
    /// <list type="bullet">
    /// <item>older downloads: <c>Title.mp3</c>;</item>
    /// <item>current downloads: <c>Title [videoId].mp3</c>, so that two videos
    /// with the same title no longer end up in the same file.</item>
    /// </list>
    /// Existing files are never renamed; both are recognised everywhere.
    /// </summary>
    public static class DAAudioFileNames
    {
        /// <summary>The yt-dlp output template of every new download.</summary>
        public const string OutputTemplate = "%(title)s [%(id)s].%(ext)s";

        // "Some title [dQw4w9WgXcQ]": a title, a space, and an id in brackets.
        private static readonly Regex WithId = new(@"^(?<title>.*) \[(?<id>[A-Za-z0-9_-]{3,64})\]$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Splits a file name of the current pattern into title and id. False for
        /// a name of the older pattern.
        /// </summary>
        public static bool TrySplit(string fileName, out string title, out string id)
        {
            var match = WithId.Match(Path.GetFileNameWithoutExtension(fileName));
            title = match.Success ? match.Groups["title"].Value : string.Empty;
            id = match.Success ? match.Groups["id"].Value : string.Empty;
            return match.Success;
        }

        /// <summary>
        /// The name to give a library item made from a file found in the audio
        /// folder: the title without the id the current pattern adds.
        /// </summary>
        public static string ItemNameFor(string fileName) =>
            TrySplit(fileName, out var title, out _) && title.Length > 0
                ? title
                : Path.GetFileNameWithoutExtension(fileName);

        /// <summary>
        /// True when <paramref name="fileName"/> looks like the file of the item
        /// with this name and link: same title (either pattern), or the same
        /// video id as the link.
        /// </summary>
        public static bool Matches(string fileName, string itemName, string? sourceUrl)
        {
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            if (string.Equals(baseName, itemName, StringComparison.OrdinalIgnoreCase)) return true;

            if (!TrySplit(fileName, out var title, out var id)) return false;

            // Video ids are case-sensitive, titles are not.
            string? urlId = VideoIdFromUrl(sourceUrl);
            if (urlId != null && string.Equals(id, urlId, StringComparison.Ordinal)) return true;
            return string.Equals(title, itemName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The YouTube video id of a link (watch?v=, youtu.be/, /shorts/, /embed/,
        /// /live/), or null for a link without one or from another site.
        /// </summary>
        public static string? VideoIdFromUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;

            string host = uri.Host.ToLowerInvariant();
            if (host == "youtu.be" || host.EndsWith(".youtu.be"))
                return Clean(uri.AbsolutePath.Trim('/').Split('/')[0]);

            if (!(host == "youtube.com" || host.EndsWith(".youtube.com") || host == "youtube-nocookie.com" || host.EndsWith(".youtube-nocookie.com")))
                return null;

            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && kv[0] == "v") return Clean(Uri.UnescapeDataString(kv[1]));
            }

            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length >= 2 && segments[0] is "shorts" or "embed" or "live" or "v")
                return Clean(segments[1]);

            return null;
        }

        private static string? Clean(string candidate) =>
            Regex.IsMatch(candidate, "^[A-Za-z0-9_-]{3,64}$") ? candidate : null;

        /// <summary>
        /// A base name (no extension) that no other item uses, made from
        /// <paramref name="wanted"/>: "Name", then "Name (2)", "Name (3)"…, the
        /// pattern of <see cref="DAImportHelper.GetUniqueLibraryFileName"/>.
        /// </summary>
        public static string UniqueBaseName(string wanted, string extension, Func<string, bool> isTaken)
        {
            string candidate = wanted;
            for (int n = 2; isTaken(candidate + extension); n++)
                candidate = $"{wanted} ({n})";
            return candidate;
        }

        /// <summary>
        /// Whether a file that appeared in the audio folder during a download
        /// that was stopped belongs to that download, and so is deleted: with
        /// the name given to yt-dlp, any file of that name ("Name.webm.part",
        /// "Name.temp.mp3", "Name.mp3"…); without it, only yt-dlp's own
        /// temporary files (".part", ".ytdl", "-Frag" pieces, ".temp.").
        /// A file that appeared but is none of these, for example one copied
        /// by "Pick file…" at the same time, is left alone.
        /// </summary>
        public static bool IsDownloadLeftover(string fileName, string? baseName)
        {
            if (!string.IsNullOrEmpty(baseName))
                return fileName.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase);
            return fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase)
                || fileName.Contains(".part-Frag", StringComparison.OrdinalIgnoreCase)
                || fileName.Contains(".temp.", StringComparison.OrdinalIgnoreCase);
        }
    }
}
