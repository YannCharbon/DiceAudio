/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

namespace DiceAudio
{
    /// <summary>
    /// Per-usage playback settings for a library audio file.
    /// The same <see cref="DAAudioItem"/> can be referenced from many places
    /// (scenario tracks, scene layers, presets) with different volume, clip
    /// bounds, fades, etc. — without ever mutating the shared library item or
    /// the file on disk.
    /// </summary>
    public class DAAudioUsage
    {
        /// <summary>Reference into <see cref="DiceAudioService.AudioItems"/>.</summary>
        public Guid AudioItemId { get; set; }

        public double Volume { get; set; } = 1.0;          // 0..1
        public bool Loop { get; set; } = false;
        public double FadeInSeconds { get; set; } = 0;
        public double FadeOutSeconds { get; set; } = 0;
        public double StartDelaySeconds { get; set; } = 0;

        /// <summary>Non-destructive trim start; null = from file start.</summary>
        public double? ClipStartSeconds { get; set; }

        /// <summary>Non-destructive trim end; null = to file end.</summary>
        public double? ClipEndSeconds { get; set; }

        public bool IsClipped => ClipStartSeconds is > 0 || ClipEndSeconds != null;

        public DAAudioUsage() { }

        public DAAudioUsage(Guid audioItemId)
        {
            AudioItemId = audioItemId;
        }

        public DAAudioUsage Clone() => new()
        {
            AudioItemId = AudioItemId,
            Volume = Volume,
            Loop = Loop,
            FadeInSeconds = FadeInSeconds,
            FadeOutSeconds = FadeOutSeconds,
            StartDelaySeconds = StartDelaySeconds,
            ClipStartSeconds = ClipStartSeconds,
            ClipEndSeconds = ClipEndSeconds,
        };
    }
}
