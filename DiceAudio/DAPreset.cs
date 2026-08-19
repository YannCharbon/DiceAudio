/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Text.Json;

namespace DiceAudio
{
    public enum DAPresetKind { ScenarioItem, Scene }

    /// <summary>
    /// A reusable snapshot of a scenario item or scene, stored in the preset
    /// catalogue. The payload is kept as JSON so inserting always produces an
    /// independent deep copy.
    /// </summary>
    public class DAPreset
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";        // free text, e.g. "Town", "Combat"
        public DAPresetKind Kind { get; set; }
        public string PayloadJson { get; set; } = "";

        /// <summary>
        /// Snapshots a scenario item. <paramref name="resolvedScene"/> is the scene
        /// the item actually plays (see <see cref="DAScenarioGroup.ResolveScene"/>):
        /// a group-local scene is baked into the payload, because the preset may be
        /// inserted into a scenario group where that local scene does not exist.
        /// </summary>
        public static DAPreset FromScenarioItem(DAScenarioItem item, string category = "",
                                                DAScene? resolvedScene = null)
        {
            var payload = item;
            if (item.LocalSceneId != null)
            {
                // Deep copy so flattening the reference never touches the scenario.
                payload = JsonSerializer.Deserialize<DAScenarioItem>(JsonSerializer.Serialize(item))!;
                payload.LocalSceneId = null;
                payload.Scene = resolvedScene == null
                    ? null
                    : JsonSerializer.Deserialize<DAScene>(JsonSerializer.Serialize(resolvedScene));
            }

            return new()
            {
                Name = item.Name,
                Category = category,
                Kind = DAPresetKind.ScenarioItem,
                PayloadJson = JsonSerializer.Serialize(payload),
            };
        }

        public static DAPreset FromScene(DAScene scene, string category = "") => new()
        {
            Name = scene.Name,
            Category = category,
            Kind = DAPresetKind.Scene,
            PayloadJson = JsonSerializer.Serialize(scene),
        };

        /// <summary>Materializes an independent copy with fresh Guids everywhere.</summary>
        public DAScenarioItem? ToScenarioItem()
        {
            if (Kind != DAPresetKind.ScenarioItem) return null;
            try
            {
                var item = JsonSerializer.Deserialize<DAScenarioItem>(PayloadJson);
                if (item == null) return null;
                item.Id = Guid.NewGuid();
                // Presets are self-contained: a local-scene reference from an older
                // payload cannot resolve in the target scenario, so drop it.
                item.LocalSceneId = null;
                if (item.Scene != null) RemapScene(item.Scene);
                return item;
            }
            catch { return null; }
        }

        /// <summary>Materializes an independent copy with fresh Guids everywhere.</summary>
        public DAScene? ToScene()
        {
            if (Kind != DAPresetKind.Scene) return null;
            try
            {
                var scene = JsonSerializer.Deserialize<DAScene>(PayloadJson);
                if (scene == null) return null;
                RemapScene(scene);
                return scene;
            }
            catch { return null; }
        }

        /// <summary>
        /// Gives the scene and all of its children fresh Guids, remapping the
        /// commands' LayerId references onto the new layer ids. AudioItemId
        /// references into the library are intentionally preserved.
        /// </summary>
        private static void RemapScene(DAScene scene)
        {
            scene.Id = Guid.NewGuid();

            var layerIdMap = new Dictionary<Guid, Guid>();
            foreach (var layer in scene.Layers)
            {
                var newId = Guid.NewGuid();
                layerIdMap[layer.Id] = newId;
                layer.Id = newId;
            }

            foreach (var step in scene.Steps)
            {
                step.Id = Guid.NewGuid();
                foreach (var command in step.Commands)
                {
                    command.Id = Guid.NewGuid();
                    if (layerIdMap.TryGetValue(command.LayerId, out var mapped))
                        command.LayerId = mapped;
                }
                // The auto-advance trigger points at a layer too.
                if (step.AdvanceOnLayerEndId is Guid advanceOn
                    && layerIdMap.TryGetValue(advanceOn, out var mappedAdvance))
                    step.AdvanceOnLayerEndId = mappedAdvance;
            }

            var contextIdMap = new Dictionary<Guid, Guid>();
            foreach (var context in scene.Contexts)
            {
                var newId = Guid.NewGuid();
                contextIdMap[context.Id] = newId;
                context.Id = newId;
                foreach (var state in context.LayerStates)
                {
                    if (layerIdMap.TryGetValue(state.LayerId, out var mapped))
                        state.LayerId = mapped;
                }
            }

            // Keep the default-context reference pointing at the remapped context.
            if (scene.DefaultContextId is Guid oldDefault
                && contextIdMap.TryGetValue(oldDefault, out var newDefault))
                scene.DefaultContextId = newDefault;
        }
    }
}
