/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

namespace DiceAudio.Portability
{
    /// <summary>The page the import was opened from: it says what the file is expected to hold.</summary>
    public enum DAImportOrigin { Settings, Explorer, Scenes, Scenarios, LocalScenes }

    /// <summary>
    /// What the import does with the page it was opened from: the check that
    /// the file holds what that page is about, and, from the local scenes of a
    /// scenario group, the scenes of the file turned into local scenes of it.
    /// </summary>
    public static class DAImportScope
    {
        /// <summary>True when the file holds something of the kind the page is about (always, from Settings).</summary>
        public static bool HasExpectedContent(DABundle bundle, DAImportOrigin origin) => origin switch
        {
            DAImportOrigin.Explorer => bundle.AudioItems.Count + bundle.Folders.Count > 0,
            DAImportOrigin.Scenes => bundle.Scenes.Count > 0,
            DAImportOrigin.Scenarios => bundle.ScenarioGroups.Count + bundle.Scenarios.Count + bundle.ScenarioItems.Count > 0,
            DAImportOrigin.LocalScenes => bundle.Scenes.Count + bundle.LocalScenes.Count > 0,
            _ => true,
        };

        /// <summary>
        /// Makes the scenes of the file local scenes of the group <paramref name="groupId"/>:
        /// each catalogue scene becomes a new local scene record (a new id, so
        /// it never shares one with the catalogue scene it came from), and each
        /// local scene record goes to that group. A record played by a scenario
        /// or a scenario item of the file stays with it, so that the link holds.
        /// The planner then targets the group, which exists in the library.
        /// Returns how many scenes go to the group.
        /// </summary>
        public static int ScenesIntoGroup(DABundle bundle, Guid groupId, string groupName, Func<Guid>? newGuid = null)
        {
            var played = bundle.Scenarios.SelectMany(r => r.Scenario.Items).Concat(bundle.ScenarioItems.Select(r => r.Item))
                               .Where(i => i.LocalSceneId != null).Select(i => i.LocalSceneId!.Value).ToHashSet();
            int count = 0;
            foreach (var record in bundle.LocalScenes.Where(r => !played.Contains(r.Scene.Id)))
            {
                record.SourceGroupId = groupId;
                record.SourceGroupName = groupName;
                count++;
            }

            foreach (var scene in bundle.Scenes)
            {
                scene.Id = (newGuid ?? Guid.NewGuid)();
                bundle.LocalScenes.Add(new DALocalSceneRecord { SourceGroupId = groupId, SourceGroupName = groupName, Scene = scene });
                count++;
            }
            bundle.Scenes.Clear();
            return count;
        }
    }
}
