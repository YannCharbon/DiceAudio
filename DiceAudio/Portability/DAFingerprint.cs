/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DiceAudio.Portability
{
    /// <summary>
    /// The canonical content of an asset (spec §4.4): a JSON string of only what
    /// describes it, properties in a fixed order, lists in their own order
    /// unless they are sets (then sorted ordinal), numbers written with "R",
    /// nulls and defaults written out. Two assets have the same content when
    /// their fingerprints are equal. Pure functions: no service, no disk.
    /// </summary>
    public static class DAFingerprint
    {
        // ── Audio items ────────────────────────────────────────────────────

        /// <summary>A library item. Its folder counts by name only ("." for the root).</summary>
        public static string Of(DAAudioItem item) =>
            AudioItem(item.Name, item.Kind, item.Tags.Select(t => t.Name), item.Volume, item.Loop,
                      item.FadeInDuration, item.StartDelay, item.SourceURL,
                      string.IsNullOrEmpty(item.Folder?.Name) ? "." : item.Folder!.Name);

        /// <summary>A bundle item record; <paramref name="folderName"/> is its folder's name, "." for the root.</summary>
        public static string Of(DAAudioItemRecord record, string folderName) =>
            AudioItem(record.Name, record.Kind, record.Tags, record.Volume, record.Loop,
                      record.FadeInDuration, record.StartDelay, record.SourceURL, folderName);

        private static string AudioItem(string name, AudioKind kind, IEnumerable<string> tags, double volume, bool loop,
                                        int fadeIn, int startDelay, string? sourceUrl, string folderName) =>
            Write(w =>
            {
                w.WriteString("Name", name);
                w.WriteString("Kind", kind.ToString());
                w.WriteStartArray("Tags");
                foreach (var tag in tags.Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal))
                    w.WriteStringValue(tag);
                w.WriteEndArray();
                Number(w, "Volume", volume);
                w.WriteBoolean("Loop", loop);
                w.WriteNumber("FadeInDuration", fadeIn);
                w.WriteNumber("StartDelay", startDelay);
                w.WriteString("SourceURL", sourceUrl ?? "");
                w.WriteString("Folder", folderName);
            });

        // ── Folders and tags ───────────────────────────────────────────────

        /// <summary>A folder: the names of its parents, root first (empty for a folder at the root).</summary>
        public static string OfFolder(IEnumerable<string> parentPath) =>
            Write(w =>
            {
                w.WriteStartArray("ParentPath");
                foreach (var name in parentPath) w.WriteStringValue(name);
                w.WriteEndArray();
            });

        /// <summary>A tag: the category it is in.</summary>
        public static string OfTag(string category) => Write(w => w.WriteString("Category", category));

        // ── Scenes ─────────────────────────────────────────────────────────

        /// <summary>A scene, its own id excluded; layer and context references are written as indexes.</summary>
        public static string Of(DAScene scene) => Write(w => Scene(w, scene));

        private static void Scene(Utf8JsonWriter w, DAScene scene)
        {
            var layerIndex = new Dictionary<Guid, int>();
            for (int i = 0; i < scene.Layers.Count; i++) layerIndex.TryAdd(scene.Layers[i].Id, i);
            int LayerIndex(Guid id) => layerIndex.TryGetValue(id, out var i) ? i : -1;

            w.WriteString("Name", scene.Name);
            w.WriteString("Category", scene.Category ?? "");
            w.WriteString("Mode", scene.Mode.ToString());

            w.WriteStartArray("Layers");
            foreach (var layer in scene.Layers)
            {
                w.WriteStartObject();
                w.WriteString("Name", layer.Name);
                w.WriteString("Role", layer.Role.ToString());
                w.WritePropertyName("Audio");
                Usage(w, layer.Audio);
                w.WriteBoolean("RandomRepeat", layer.RandomRepeat);
                Number(w, "RandomMinSeconds", layer.RandomMinSeconds);
                Number(w, "RandomMaxSeconds", layer.RandomMaxSeconds);
                w.WriteBoolean("FireImmediatelyOnStart", layer.FireImmediatelyOnStart);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("Steps");
            foreach (var step in scene.Steps)
            {
                w.WriteStartObject();
                w.WriteString("Name", step.Name);
                w.WriteStartArray("Commands");
                foreach (var c in step.Commands)
                {
                    w.WriteStartObject();
                    w.WriteString("Type", c.Type.ToString());
                    w.WriteNumber("LayerId", LayerIndex(c.LayerId));
                    w.WriteBoolean("AllLayers", c.AllLayers);
                    Number(w, "DelaySeconds", c.DelaySeconds);
                    Number(w, "DurationSeconds", c.DurationSeconds);
                    Number(w, "TargetVolume", c.TargetVolume);
                    if (c.Loop is bool loop) w.WriteBoolean("Loop", loop); else w.WriteNull("Loop");
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteString("AdvanceMode", step.AdvanceMode.ToString());
                if (step.AdvanceOnLayerEndId is Guid end) w.WriteNumber("AdvanceOnLayerEndId", LayerIndex(end));
                else w.WriteNull("AdvanceOnLayerEndId");
                Number(w, "AdvanceAfterSeconds", step.AdvanceAfterSeconds);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("Contexts");
            foreach (var context in scene.Contexts)
            {
                w.WriteStartObject();
                w.WriteString("Name", context.Name);
                Number(w, "TransitionSeconds", context.TransitionSeconds);
                w.WriteStartArray("LayerStates");
                foreach (var state in context.LayerStates)
                {
                    w.WriteStartObject();
                    w.WriteNumber("LayerId", LayerIndex(state.LayerId));
                    w.WriteBoolean("Active", state.Active);
                    Number(w, "Volume", state.Volume);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();

            if (scene.DefaultContextId is Guid def)
            {
                int index = scene.Contexts.FindIndex(c => c.Id == def);
                w.WriteNumber("DefaultContextId", index);
            }
            else w.WriteNull("DefaultContextId");
        }

        private static void Usage(Utf8JsonWriter w, DAAudioUsage? u)
        {
            u ??= new DAAudioUsage();
            w.WriteStartObject();
            w.WriteString("AudioItemId", u.AudioItemId.ToString("D"));
            Number(w, "Volume", u.Volume);
            w.WriteBoolean("Loop", u.Loop);
            Number(w, "FadeInSeconds", u.FadeInSeconds);
            Number(w, "FadeOutSeconds", u.FadeOutSeconds);
            Number(w, "StartDelaySeconds", u.StartDelaySeconds);
            Number(w, "ClipStartSeconds", u.ClipStartSeconds);
            Number(w, "ClipEndSeconds", u.ClipEndSeconds);
            Number(w, "ClipFadeInSeconds", u.ClipFadeInSeconds);
            Number(w, "ClipFadeOutSeconds", u.ClipFadeOutSeconds);
            w.WriteEndObject();
        }

        // ── Scenario items, scenarios, groups, presets ────────────────────

        public static string Of(DAScenarioItem item) => Write(w => Item(w, item));

        private static void Item(Utf8JsonWriter w, DAScenarioItem item)
        {
            w.WriteString("Name", item.Name);
            w.WriteString("Type", item.Type.ToString());
            if (item.Scene != null)
            {
                w.WriteStartObject("Scene");
                Scene(w, item.Scene);
                w.WriteEndObject();
            }
            else w.WriteNull("Scene");
            if (item.LocalSceneId is Guid local) w.WriteString("LocalSceneId", local.ToString("D"));
            else w.WriteNull("LocalSceneId");

            w.WriteStartArray("PlaylistTracks");
            foreach (var u in item.PlaylistTracks) Usage(w, u);
            w.WriteEndArray();
            w.WriteStartArray("SoundEffects");
            foreach (var u in item.SoundEffects) Usage(w, u);
            w.WriteEndArray();

            w.WriteBoolean("PlayInLoop", item.PlayInLoop);
            w.WriteBoolean("PlaylistOrderIsRandom", item.PlaylistOrderIsRandom);

            w.WriteStartArray("RandomPlayCriterias");
            foreach (var c in item.RandomPlayCriterias)
            {
                w.WriteStartObject();
                w.WriteString("Condition", c.Condition.ToString());
                w.WriteStartArray("TagNames");
                foreach (var t in c.TagNames) w.WriteStringValue(t);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        /// <summary>A scenario: its name and its items in order, each as (id, fingerprint).</summary>
        public static string Of(DAScenario scenario) => Write(w =>
        {
            w.WriteString("Name", scenario.Name);
            w.WriteStartArray("Items");
            foreach (var item in scenario.Items) IdAndPrint(w, item.Id, Of(item));
            w.WriteEndArray();
        });

        /// <summary>A scenario group: settings, scenarios and local scenes in order, each as (id, fingerprint).</summary>
        public static string Of(DAScenarioGroup group) => Write(w =>
        {
            w.WriteString("Name", group.Name);
            w.WriteString("Collection", group.Collection ?? "");
            w.WriteBoolean("StopOtherScenariosOnPlay", group.StopOtherScenariosOnPlay);
            w.WriteStartArray("Scenarios");
            foreach (var s in group.Scenarios) IdAndPrint(w, s.Id, Of(s));
            w.WriteEndArray();
            w.WriteStartArray("LocalScenes");
            foreach (var s in group.LocalScenes) IdAndPrint(w, s.Id, Of(s));
            w.WriteEndArray();
        });

        /// <summary>
        /// A preset: name, category, kind, and its payload fingerprinted as the
        /// scenario item or scene it holds (whose own id is excluded). A payload
        /// that cannot be read counts as its raw text.
        /// </summary>
        public static string Of(DAPreset preset) => Write(w =>
        {
            w.WriteString("Name", preset.Name);
            w.WriteString("Category", preset.Category ?? "");
            w.WriteString("Kind", preset.Kind.ToString());
            string payload;
            try
            {
                payload = preset.Kind == DAPresetKind.ScenarioItem
                    ? JsonSerializer.Deserialize<DAScenarioItem>(preset.PayloadJson) is { } item ? Of(item) : "null"
                    : JsonSerializer.Deserialize<DAScene>(preset.PayloadJson) is { } scene ? Of(scene) : "null";
            }
            catch (JsonException)
            {
                payload = "raw:" + preset.PayloadJson;
            }
            w.WriteString("Payload", payload);
        });

        // ── Helpers ────────────────────────────────────────────────────────

        private static void IdAndPrint(Utf8JsonWriter w, Guid id, string print)
        {
            w.WriteStartObject();
            w.WriteString("Id", id.ToString("D"));
            w.WriteString("Fingerprint", print);
            w.WriteEndObject();
        }

        private static void Number(Utf8JsonWriter w, string name, double value) =>
            w.WriteString(name, value.ToString("R", CultureInfo.InvariantCulture));

        private static void Number(Utf8JsonWriter w, string name, double? value)
        {
            if (value is double v) Number(w, name, v); else w.WriteNull(name);
        }

        private static string Write(Action<Utf8JsonWriter> body)
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                w.WriteStartObject();
                body(w);
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
