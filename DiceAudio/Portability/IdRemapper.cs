/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Text.Json;

namespace DiceAudio.Portability
{
    /// <summary>
    /// A map from old GUIDs to new ones, and the code that applies it to every
    /// reference of a bundle (spec §3.2 and §4.3). Building the map from an
    /// import plan (<c>BuildFrom</c>) comes with the import, in A7.
    /// </summary>
    public sealed class IdRemapper
    {
        private readonly Dictionary<Guid, Guid> _map = new();

        public IReadOnlyDictionary<Guid, Guid> Map => _map;

        /// <summary>Maps <paramref name="from"/> to <paramref name="to"/>. Throws if it is already mapped elsewhere.</summary>
        public void Add(Guid from, Guid to)
        {
            if (_map.TryGetValue(from, out var existing) && existing != to)
                throw new InvalidOperationException($"{from} is already mapped to {existing}.");
            _map[from] = to;
        }

        /// <summary>The mapped id, or the id itself.</summary>
        public Guid Resolve(Guid id) => _map.TryGetValue(id, out var to) ? to : id;

        public Guid? Resolve(Guid? id) => id is Guid g ? Resolve(g) : null;

        /// <summary>
        /// Rewrites, in place, every GUID of the bundle that the map covers: the
        /// ids of the assets themselves and every reference between them —
        /// <see cref="DAAudioUsage.AudioItemId"/> everywhere (catalogue scenes,
        /// local scenes, embedded scenes, playlists, sound effects, and inside
        /// each preset's payload), <see cref="DAScenarioItem.LocalSceneId"/>,
        /// folder parents, item folders, the Source*Id fields of the records,
        /// and the roots. Scene-internal ids (layers, steps, commands, contexts)
        /// are left alone; <see cref="RenewSceneInternals"/> handles those.
        /// </summary>
        public void Rewrite(DABundle bundle)
        {
            foreach (var root in bundle.Roots) root.Id = Resolve(root.Id);

            foreach (var folder in bundle.Folders)
            {
                folder.Id = Resolve(folder.Id);
                folder.ParentId = Resolve(folder.ParentId);
            }

            foreach (var item in bundle.AudioItems)
            {
                item.Id = Resolve(item.Id);
                item.FolderId = Resolve(item.FolderId);
            }

            foreach (var scene in bundle.Scenes) RewriteScene(scene, ownId: true);

            foreach (var group in bundle.ScenarioGroups) RewriteGroup(group);

            foreach (var record in bundle.Scenarios)
            {
                record.SourceGroupId = Resolve(record.SourceGroupId);
                RewriteScenario(record.Scenario);
            }

            foreach (var record in bundle.ScenarioItems)
            {
                record.SourceGroupId = Resolve(record.SourceGroupId);
                record.SourceScenarioId = Resolve(record.SourceScenarioId);
                RewriteItem(record.Item);
            }

            foreach (var record in bundle.LocalScenes)
            {
                record.SourceGroupId = Resolve(record.SourceGroupId);
                RewriteScene(record.Scene, ownId: true);
            }

            foreach (var preset in bundle.Presets) RewritePreset(preset);

            foreach (var warning in bundle.Warnings) warning.Id = Resolve(warning.Id);
        }

        private void RewriteGroup(DAScenarioGroup group)
        {
            group.Id = Resolve(group.Id);
            foreach (var scenario in group.Scenarios) RewriteScenario(scenario);
            foreach (var scene in group.LocalScenes) RewriteScene(scene, ownId: true);
        }

        private void RewriteScenario(DAScenario scenario)
        {
            scenario.Id = Resolve(scenario.Id);
            foreach (var item in scenario.Items) RewriteItem(item);
        }

        private void RewriteItem(DAScenarioItem item)
        {
            item.Id = Resolve(item.Id);
            item.LocalSceneId = Resolve(item.LocalSceneId);
            foreach (var usage in item.PlaylistTracks) usage.AudioItemId = Resolve(usage.AudioItemId);
            foreach (var usage in item.SoundEffects) usage.AudioItemId = Resolve(usage.AudioItemId);
            // An embedded scene is part of its item, not an asset of its own:
            // only the audio it plays is remapped, as for any scene.
            if (item.Scene != null) RewriteScene(item.Scene, ownId: false);
        }

        private void RewriteScene(DAScene scene, bool ownId)
        {
            if (ownId) scene.Id = Resolve(scene.Id);
            foreach (var layer in scene.Layers)
                layer.Audio.AudioItemId = Resolve(layer.Audio.AudioItemId);
        }

        /// <summary>
        /// The payload is JSON inside the preset: read, rewritten and written
        /// back with the same options DAPreset uses. A payload that cannot be
        /// read is left as it is.
        /// </summary>
        private void RewritePreset(DAPreset preset)
        {
            preset.Id = Resolve(preset.Id);
            try
            {
                if (preset.Kind == DAPresetKind.ScenarioItem)
                {
                    var item = JsonSerializer.Deserialize<DAScenarioItem>(preset.PayloadJson);
                    if (item == null) return;
                    RewriteItem(item);
                    preset.PayloadJson = JsonSerializer.Serialize(item);
                }
                else
                {
                    var scene = JsonSerializer.Deserialize<DAScene>(preset.PayloadJson);
                    if (scene == null) return;
                    RewriteScene(scene, ownId: true);
                    preset.PayloadJson = JsonSerializer.Serialize(scene);
                }
            }
            catch (JsonException)
            {
                // A damaged payload stays as it was; the preset still travels.
            }
        }

        /// <summary>
        /// Gives the layers, steps, commands and contexts of a scene fresh ids,
        /// and points every reference between them at the new ones: commands'
        /// <see cref="DASceneCommand.LayerId"/>, steps'
        /// <see cref="DASceneStep.AdvanceOnLayerEndId"/>, contexts'
        /// <see cref="DAContextLayerState.LayerId"/> and the scene's
        /// <see cref="DAScene.DefaultContextId"/>. The scene's own id and the
        /// audio items it plays are not changed.
        /// </summary>
        public static void RenewSceneInternals(DAScene scene, Func<Guid>? newGuid = null)
        {
            newGuid ??= Guid.NewGuid;

            var layerIdMap = new Dictionary<Guid, Guid>();
            foreach (var layer in scene.Layers)
            {
                var newId = newGuid();
                layerIdMap[layer.Id] = newId;
                layer.Id = newId;
            }

            foreach (var step in scene.Steps)
            {
                step.Id = newGuid();
                foreach (var command in step.Commands)
                {
                    command.Id = newGuid();
                    if (layerIdMap.TryGetValue(command.LayerId, out var mapped))
                        command.LayerId = mapped;
                }
                if (step.AdvanceOnLayerEndId is Guid advanceOn
                    && layerIdMap.TryGetValue(advanceOn, out var mappedAdvance))
                    step.AdvanceOnLayerEndId = mappedAdvance;
            }

            var contextIdMap = new Dictionary<Guid, Guid>();
            foreach (var context in scene.Contexts)
            {
                var newId = newGuid();
                contextIdMap[context.Id] = newId;
                context.Id = newId;
                foreach (var state in context.LayerStates)
                {
                    if (layerIdMap.TryGetValue(state.LayerId, out var mapped))
                        state.LayerId = mapped;
                }
            }

            if (scene.DefaultContextId is Guid oldDefault
                && contextIdMap.TryGetValue(oldDefault, out var newDefault))
                scene.DefaultContextId = newDefault;
        }
    }
}
