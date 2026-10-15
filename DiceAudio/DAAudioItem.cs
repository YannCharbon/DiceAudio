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
using System.Text.Json.Serialization;
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

        /// <summary>
        /// The name of the DaIcon glyph that stands for this kind. It replaces the
        /// old Icon()/Color() pair, which handed out MudBlazor constants and so kept
        /// a UI library referenced from a core model; three separate pages had each
        /// grown a private copy of this switch to avoid them.
        /// </summary>
        public static string DaIconName(this AudioKind kind) => kind switch
        {
            AudioKind.Music => "music-note",
            AudioKind.Ambiance => "forest",
            _ => "volume-up",
        };

        /// <summary>CSS class carrying the kind tint (--kc) for the shared audio card.</summary>
        public static string KindClass(this AudioKind kind) => kind switch
        {
            AudioKind.Music => "da-k-music",
            AudioKind.Ambiance => "da-k-amb",
            _ => "da-k-sfx",
        };
    }

    public class DAAudioItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Default";
        public DAVirtualAudioFolder Folder { get; set; } = new DAVirtualAudioFolder();
        public string SourceURL { get; set; } = string.Empty;

        // The progress of a download in this session only. Not saved: a value
        // left in the file by a download cut short showed a spinner forever.
        // Older files that still carry these properties read fine.
        [JsonIgnore] public int SourceDownloadProgress { get; set; } = 0;
        [JsonIgnore] public string SourceDownloadStatusMessage { get; set; } = String.Empty;
        [JsonIgnore] public bool SourceIsDownloading { get; set; } = false;

        /// Saved for older versions of the app, but worked out again from the
        /// audio folder at every load (DiceAudioService.RefreshLocalAvailability).
        public bool IsLocallyAvailable { get; set; } = false;

        /// The file is in the audio folder but only in the cloud (OneDrive Files
        /// On-Demand): playing it first waits for the download. Worked out at
        /// every load, never saved (spec §5.7).
        [JsonIgnore] public bool IsCloudOnly { get; set; }
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

    /// <summary>Where the audio file of an item stands, as the Explorer shows it.</summary>
    public enum DAFileStatus
    {
        /// <summary>The file is in the audio folder (perhaps only in the cloud).</summary>
        Available,
        /// <summary>A link and no file name: "Not downloaded yet", with Download.</summary>
        NotDownloaded,
        /// <summary>A file name whose file is not there: "Not found on disk", with Pick file… (and Download with a link).</summary>
        NotFound,
        /// <summary>Neither a link nor a file name: "No audio file", with Pick file….</summary>
        NoFile,
    }

    public static class DAFileStatusRules
    {
        public static DAFileStatus FileStatus(this DAAudioItem item) =>
            item.IsLocallyAvailable ? DAFileStatus.Available
            : !string.IsNullOrWhiteSpace(item.LocalFileName) ? DAFileStatus.NotFound
            : item.HasLink() ? DAFileStatus.NotDownloaded
            : DAFileStatus.NoFile;

        public static bool HasLink(this DAAudioItem item) => !string.IsNullOrWhiteSpace(item.SourceURL);

        /// <summary>Download is offered: the file is not there and the item has a link.</summary>
        public static bool CanDownload(this DAAudioItem item) => !item.IsLocallyAvailable && item.HasLink();

        /// <summary>Pick file… is offered: the file is not there and is not simply waiting to be downloaded.</summary>
        public static bool CanPickFile(this DAAudioItem item) => item.FileStatus() is DAFileStatus.NotFound or DAFileStatus.NoFile;
    }
}
