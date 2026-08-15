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
using System.Threading.Tasks;

namespace DiceAudio
{
    public class DAVirtualAudioFolder
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = ".";
        public Guid ParentFolderId { get; set; } = Guid.Empty;
        public List<DAVirtualAudioFolder> VirtualAudioSubFolders { get; set; } = new List<DAVirtualAudioFolder>();
        public DAVirtualAudioFolder() { }

        public DAVirtualAudioFolder(string name)
        {
            Name = name;
        }
    }
}
