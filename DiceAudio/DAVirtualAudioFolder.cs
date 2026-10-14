/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DiceAudio
{
    public class DAVirtualAudioFolder
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = ".";
        public Guid ParentFolderId { get; set; } = Guid.Empty;

        /// <summary>
        /// The Explorer's view of the tree, rebuilt from <see cref="ParentFolderId"/>
        /// every time it is shown. Not saved: the parent links are the only truth,
        /// and saving this list copied whole branches into every item of a folder.
        /// Older files that still carry it read fine.
        /// </summary>
        [JsonIgnore]
        public List<DAVirtualAudioFolder> VirtualAudioSubFolders { get; set; } = new List<DAVirtualAudioFolder>();
        public DAVirtualAudioFolder() { }

        public DAVirtualAudioFolder(string name)
        {
            Name = name;
        }
    }
}
