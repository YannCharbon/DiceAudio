/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

namespace DiceAudio
{
    public class DAScenarioGroup
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Default scenario group";
        public List<DAScenario> Scenarios { get; set; } = new List<DAScenario>();

        /// <summary>
        /// Scenes owned by this group alone — the private counterpart of the
        /// global scene catalogue. Any item of any scenario in the group can
        /// reference one (see <see cref="DAScenarioItem.LocalSceneId"/>), so
        /// editing it once updates every item that plays it. Persisted with the
        /// group, hence carried along by the scenario group file.
        /// </summary>
        public List<DAScene> LocalScenes { get; set; } = new List<DAScene>();

        /// <summary>
        /// The scene an item actually plays: the group-local scene it points at,
        /// or — when it points at none — its own embedded copy. Returns null when
        /// the item has no scene at all (or references a deleted one).
        /// </summary>
        public DAScene? ResolveScene(DAScenarioItem item)
        {
            if (item.LocalSceneId is Guid id)
                return LocalScenes.FirstOrDefault(s => s.Id == id);
            return item.Scene;
        }

        /// <summary>Items anywhere in this group that play the given local scene.</summary>
        public IEnumerable<DAScenarioItem> ItemsUsingLocalScene(DAScene scene) =>
            Scenarios.SelectMany(s => s.Items).Where(i => i.LocalSceneId == scene.Id);
    }
}
