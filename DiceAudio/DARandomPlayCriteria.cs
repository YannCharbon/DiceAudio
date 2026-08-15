/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Text.Json.Serialization;

namespace DiceAudio
{
    public class DARandomPlayCriteria
    {
        public enum ConditionType { Not, And, Or }

        public ConditionType Condition { get; set; } = ConditionType.Not;
        public List<string> TagNames { get; set; } = new List<string>();

        [JsonIgnore]
        public bool EditionModeEnabled { get; set; } = true;
    }
}
