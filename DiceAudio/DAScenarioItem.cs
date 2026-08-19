/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

namespace DiceAudio
{
    public class DAScenarioItem
    {
        public enum ItemType { Single, Playlist, RandomWithCriteria, Scene }

        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Default item";
        public ItemType Type { get; set; } = ItemType.Single;

        /// <summary>
        /// The layered scene played by this item when <see cref="Type"/> is
        /// <see cref="ItemType.Scene"/> and <see cref="LocalSceneId"/> is null.
        /// Embedded (self-contained) so scenario groups and presets carry their
        /// scenes with them; the scene catalogue holds independent reusable copies.
        /// </summary>
        public DAScene? Scene { get; set; }

        /// <summary>
        /// When set, the item plays the group-local scene with this id (see
        /// <see cref="DAScenarioGroup.LocalScenes"/>) instead of the embedded
        /// <see cref="Scene"/> — so items across the whole scenario group can
        /// share one editable scene without publishing it to the global
        /// catalogue. Always resolve through <see cref="DAScenarioGroup.ResolveScene"/>
        /// (UI) or <see cref="DiceAudioService.ResolveScene"/> (playback) rather
        /// than reading these two fields directly.
        /// </summary>
        public Guid? LocalSceneId { get; set; }

        /// <summary>Main ambience/music tracks, each with its own per-usage settings.</summary>
        public List<DAAudioUsage> PlaylistTracks { get; set; } = new();

        /// <summary>Manually-triggered sound effects, each with per-usage settings.</summary>
        public List<DAAudioUsage> SoundEffects { get; set; } = new();

        public bool PlayInLoop { get; set; } = false;
        public bool PlaylistOrderIsRandom { get; set; } = false;
        public List<DARandomPlayCriteria> RandomPlayCriterias { get; set; } = new();
    }
}
