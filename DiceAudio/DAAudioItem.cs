/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceAudio
{
    // Ambiance is appended (not inserted) so existing persisted int values keep their meaning.
    public enum AudioKind { Music, SoundEffect, Ambiance }

    public static class AudioKindExtensions
    {
        public static string Label(this AudioKind kind) => kind switch
        {
            AudioKind.Music => "Music",
            AudioKind.Ambiance => "Ambiance",
            _ => "Sound effect",
        };

        public static string Icon(this AudioKind kind) => kind switch
        {
            AudioKind.Music => MudBlazor.Icons.Material.Filled.MusicNote,
            AudioKind.Ambiance => MudBlazor.Icons.Material.Filled.Forest,
            _ => MudBlazor.Icons.Material.Filled.VolumeUp,
        };

        public static MudBlazor.Color Color(this AudioKind kind) => kind switch
        {
            AudioKind.Music => MudBlazor.Color.Primary,
            AudioKind.Ambiance => MudBlazor.Color.Info,
            _ => MudBlazor.Color.Secondary,
        };
    }

    public class DAAudioItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Default";
        public DAVirtualAudioFolder Folder { get; set; } = new DAVirtualAudioFolder();
        public string SourceURL { get; set; } = string.Empty;
        public int SourceDownloadProgress { get; set; } = 0;
        public string SourceDownloadStatusMessage { get; set; } = String.Empty;
        public bool SourceIsDownloading { get; set; } = false;
        public bool IsLocallyAvailable { get; set; } = false;
        public string LocalFileName { get; set; } = String.Empty;
        public List<DAAudioTag> Tags { get; set; } = new List<DAAudioTag>();
        public AudioKind Kind { get; set; } = AudioKind.Music;

        public int FadeInDuration { get; set; } = 0;
        public int StartDelay { get; set; } = 0;
        public double Volume { get; set; } = 1.0;
        public bool Loop { get; set; } = true;

        public DAAudioItem() { }

        public DAAudioItem(string name)
        {
            Name = name;
        }
    }
}
