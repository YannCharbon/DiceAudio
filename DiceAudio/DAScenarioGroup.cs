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
    }
}
