/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DiceAudio.Portability
{
    /// <summary>Why a file could not be imported (spec §2.1 validation); each maps to a text of spec §8.</summary>
    public enum DABundleError { None, UnreadableFile, OtherAppFile, NewerFormat, FileTooLarge }

    public sealed record DABundleReadResult(DABundle? Bundle, DABundleError Error, string? Detail, int BrokenReferences)
    {
        public bool Ok => Bundle != null && Error == DABundleError.None;
    }

    /// <summary>
    /// Steps that bring an older bundle up to <see cref="DABundle.CurrentVersion"/>,
    /// one version at a time, on the raw JSON (spec §2.1). Version 1 has none.
    /// </summary>
    public static class DABundleUpgrader
    {
        public static void Upgrade(JsonObject root, int fromVersion)
        {
            // for (int v = fromVersion; v < DABundle.CurrentVersion; v++) { if (v == 1) Upgrade1To2(root); … }
        }
    }

    /// <summary>
    /// Reads and checks a .dicea file before anything is analysed (pipeline
    /// step 1): format, version, size, unique ids, references inside the file.
    /// Writes nothing. No MAUI calls.
    /// </summary>
    public static class DABundleReader
    {
        public const long MaxBytes = 50L * 1024 * 1024;
        public const string OtherAppFormat = "DiceCombats.Bundle";

        /// <summary>Lenient reading (spec §2.1): any case, comments, trailing commas, enums by name or number.</summary>
        public static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static async Task<DABundleReadResult> ReadAsync(Stream stream, CancellationToken ct = default)
        {
            if (stream.CanSeek && stream.Length > MaxBytes)
                return new(null, DABundleError.FileTooLarge, MegaBytes(stream.Length), 0);

            // Read at most one byte more than the limit, so a stream that cannot
            // tell its length is still refused without being read whole.
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes)
                    return new(null, DABundleError.FileTooLarge, MegaBytes(buffer.Length), 0);
            }
            return Read(buffer.ToArray());
        }

        public static DABundleReadResult Read(byte[] bytes)
        {
            if (bytes.LongLength > MaxBytes) return new(null, DABundleError.FileTooLarge, MegaBytes(bytes.LongLength), 0);

            JsonObject? root;
            try
            {
                root = JsonNode.Parse(bytes, new JsonNodeOptions { PropertyNameCaseInsensitive = true },
                                      new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
            }
            catch (JsonException ex)
            {
                return new(null, DABundleError.UnreadableFile, ex.Message, 0);
            }
            if (root == null) return new(null, DABundleError.UnreadableFile, "Not a JSON object.", 0);

            // 1. Format
            string? format = root["Format"]?.GetValueKind() == JsonValueKind.String ? root["Format"]!.GetValue<string>() : null;
            if (format == OtherAppFormat) return new(null, DABundleError.OtherAppFile, null, 0);
            if (format != DABundle.FormatName) return new(null, DABundleError.UnreadableFile, "Not a DiceAudio file.", 0);

            // 2. Version: missing = 1; newer = refused; older = upgraded step by step.
            int version = 1;
            if (root["FormatVersion"] is JsonValue v && v.TryGetValue<int>(out var parsed)) version = parsed;
            if (version > DABundle.CurrentVersion) return new(null, DABundleError.NewerFormat, version.ToString(), 0);
            if (version < 1) return new(null, DABundleError.UnreadableFile, "Bad version.", 0);
            DABundleUpgrader.Upgrade(root, version);

            DABundle? bundle;
            try
            {
                bundle = root.Deserialize<DABundle>(ReadOptions);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                return new(null, DABundleError.UnreadableFile, ex.Message, 0);
            }
            if (bundle == null) return new(null, DABundleError.UnreadableFile, "Empty file.", 0);
            Normalise(bundle);

            // 3. Ids unique within each kind.
            string? duplicate = FirstDuplicate(bundle);
            if (duplicate != null) return new(null, DABundleError.UnreadableFile, duplicate, 0);

            // 4. References inside the file: not an error, counted once.
            int broken = CountBrokenReferences(bundle);
            return new(bundle, DABundleError.None, null, broken);
        }

        /// <summary>Absent lists read as empty; nulls inside lists are dropped.</summary>
        private static void Normalise(DABundle b)
        {
            b.Roots = (b.Roots ?? new()).Where(x => x != null).ToList();
            b.Tags = (b.Tags ?? new()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name)).ToList();
            b.Folders = (b.Folders ?? new()).Where(x => x != null).ToList();
            b.AudioItems = (b.AudioItems ?? new()).Where(x => x != null).ToList();
            b.Scenes = (b.Scenes ?? new()).Where(x => x != null).ToList();
            b.ScenarioGroups = (b.ScenarioGroups ?? new()).Where(x => x != null).ToList();
            b.Scenarios = (b.Scenarios ?? new()).Where(x => x?.Scenario != null).ToList();
            b.ScenarioItems = (b.ScenarioItems ?? new()).Where(x => x?.Item != null).ToList();
            b.LocalScenes = (b.LocalScenes ?? new()).Where(x => x?.Scene != null).ToList();
            b.Presets = (b.Presets ?? new()).Where(x => x != null).ToList();
            b.Warnings = (b.Warnings ?? new()).Where(x => x != null).ToList();
            foreach (var i in b.AudioItems) i.Tags = (i.Tags ?? new()).Where(t => !string.IsNullOrEmpty(t)).ToList();
            foreach (var g in b.ScenarioGroups)
            {
                g.Scenarios ??= new();
                g.LocalScenes ??= new();
                foreach (var s in g.Scenarios) s.Items ??= new();
            }
            foreach (var r in b.Scenarios) r.Scenario.Items ??= new();
        }

        private static string? FirstDuplicate(DABundle b)
        {
            string? Dup(string kind, IEnumerable<Guid> ids)
            {
                var seen = new HashSet<Guid>();
                foreach (var id in ids) if (!seen.Add(id)) return $"Two {kind} share the id {id}.";
                return null;
            }

            var scenarios = b.ScenarioGroups.SelectMany(g => g.Scenarios).Select(s => s.Id)
                            .Concat(b.Scenarios.Select(r => r.Scenario.Id));
            var items = b.ScenarioGroups.SelectMany(g => g.Scenarios).SelectMany(s => s.Items).Select(i => i.Id)
                        .Concat(b.Scenarios.SelectMany(r => r.Scenario.Items).Select(i => i.Id))
                        .Concat(b.ScenarioItems.Select(r => r.Item.Id));
            var localScenes = b.ScenarioGroups.SelectMany(g => g.LocalScenes).Select(s => s.Id)
                              .Concat(b.LocalScenes.Select(r => r.Scene.Id));
            var duplicateTag = b.Tags.GroupBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);

            return Dup("folders", b.Folders.Select(f => f.Id))
                ?? (b.Folders.GroupBy(f => f.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } dn ? $"Two folders are named {dn.Key}." : null)
                ?? Dup("audio items", b.AudioItems.Select(i => i.Id))
                ?? Dup("scenes", b.Scenes.Select(s => s.Id))
                ?? Dup("scenario groups", b.ScenarioGroups.Select(g => g.Id))
                ?? Dup("scenarios", scenarios)
                ?? Dup("scenario items", items)
                ?? Dup("local scenes", localScenes)
                ?? Dup("presets", b.Presets.Select(p => p.Id))
                ?? (duplicateTag != null ? $"The tag {duplicateTag.Key} is listed twice." : null);
        }

        /// <summary>Usages of audio items, local-scene links, folder parents and item folders that point outside the file.</summary>
        public static int CountBrokenReferences(DABundle b)
        {
            var items = b.AudioItems.Select(i => i.Id).ToHashSet();
            var folders = b.Folders.Select(f => f.Id).ToHashSet();
            int broken = 0;

            foreach (var u in DABundleWalk.Usages(b)) if (!items.Contains(u.AudioItemId)) broken++;
            foreach (var f in b.Folders) if (f.ParentId is Guid p && !folders.Contains(p)) broken++;
            foreach (var i in b.AudioItems) if (i.FolderId is Guid f && !folders.Contains(f)) broken++;

            foreach (var g in b.ScenarioGroups)
            {
                var local = g.LocalScenes.Select(s => s.Id).ToHashSet();
                foreach (var i in g.Scenarios.SelectMany(s => s.Items))
                    if (i.LocalSceneId is Guid id && !local.Contains(id)) broken++;
            }
            var recordScenes = b.LocalScenes.Select(r => r.Scene.Id).ToHashSet();
            foreach (var i in b.Scenarios.SelectMany(r => r.Scenario.Items).Concat(b.ScenarioItems.Select(r => r.Item)))
                if (i.LocalSceneId is Guid id && !recordScenes.Contains(id)) broken++;
            return broken;
        }

        private static string MegaBytes(long bytes) => Math.Ceiling(bytes / (1024.0 * 1024.0)).ToString("0");
    }

    /// <summary>Walks every audio usage of a bundle (scenes, groups, records, presets).</summary>
    public static class DABundleWalk
    {
        public static IEnumerable<DAAudioUsage> Usages(DABundle b) =>
            b.Scenes.SelectMany(Usages)
             .Concat(b.ScenarioGroups.SelectMany(Usages))
             .Concat(b.Scenarios.SelectMany(r => r.Scenario.Items).SelectMany(Usages))
             .Concat(b.ScenarioItems.SelectMany(r => Usages(r.Item)))
             .Concat(b.LocalScenes.SelectMany(r => Usages(r.Scene)))
             .Concat(b.Presets.SelectMany(Usages));

        public static IEnumerable<DAAudioUsage> Usages(DAScene s) => s.Layers.Where(l => l?.Audio != null).Select(l => l.Audio);

        public static IEnumerable<DAAudioUsage> Usages(DAScenarioItem i) =>
            (i.PlaylistTracks ?? new()).Concat(i.SoundEffects ?? new()).Concat(i.Scene != null ? Usages(i.Scene) : Enumerable.Empty<DAAudioUsage>());

        public static IEnumerable<DAAudioUsage> Usages(DAScenario s) => s.Items.SelectMany(Usages);

        public static IEnumerable<DAAudioUsage> Usages(DAScenarioGroup g) =>
            g.Scenarios.SelectMany(Usages).Concat(g.LocalScenes.SelectMany(Usages));

        /// <summary>A preset's payload, read as what it holds; nothing for a damaged one.</summary>
        public static IEnumerable<DAAudioUsage> Usages(DAPreset p)
        {
            try
            {
                if (p.Kind == DAPresetKind.Scene)
                    return JsonSerializer.Deserialize<DAScene>(p.PayloadJson) is { } s ? Usages(s).ToList() : new List<DAAudioUsage>();
                return JsonSerializer.Deserialize<DAScenarioItem>(p.PayloadJson) is { } i ? Usages(i).ToList() : new List<DAAudioUsage>();
            }
            catch (JsonException) { return Enumerable.Empty<DAAudioUsage>(); }
        }

        /// <summary>The tags a scenario item's random-play criteria name.</summary>
        public static IEnumerable<string> CriteriaTags(DAScenarioItem i) => (i.RandomPlayCriterias ?? new()).SelectMany(c => c.TagNames ?? new());
    }
}
