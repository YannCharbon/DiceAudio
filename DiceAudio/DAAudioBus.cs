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
    /// A shared output gain for a group of <see cref="DAAudioPlayer"/>s — the
    /// master fader of one <see cref="DAScenarioPlayer"/>.
    ///
    /// Players multiply their own <see cref="DAAudioPlayer.Volume"/> (which fades,
    /// crossfades and per-usage levels keep driving) by <see cref="Gain"/>, so the
    /// bus can be moved at any time without disturbing anything in flight. Players
    /// attach themselves to the bus and re-apply their volume on
    /// <see cref="GainChanged"/>.
    /// </summary>
    public sealed class DAAudioBus
    {
        private double _volume = 1.0;
        private bool _isMuted;

        /// <summary>Master level 0..1 (independent of the mute flag).</summary>
        public double Volume
        {
            get => _volume;
            set
            {
                double v = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(v - _volume) < 0.0001) return;
                _volume = v;
                GainChanged?.Invoke();
            }
        }

        /// <summary>Silences the bus while remembering <see cref="Volume"/>.</summary>
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted == value) return;
                _isMuted = value;
                GainChanged?.Invoke();
            }
        }

        /// <summary>Multiplier actually applied by the players.</summary>
        public double Gain => _isMuted ? 0.0 : _volume;

        /// <summary>Raised whenever <see cref="Gain"/> changes.</summary>
        public event Action? GainChanged;
    }
}
