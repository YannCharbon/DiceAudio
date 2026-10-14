/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using DiceAudio.Storage;

namespace DiceAudio.Integrations
{
    /// <summary>
    /// The local preferences of this computer, kept by MAUI. Every preference
    /// goes through <see cref="ILocalPreferences"/> instead of MAUI directly, so
    /// that a debug run on a sandbox (DICE_DATA_DIR) reads and writes a file of
    /// the sandbox and never the real preferences.
    /// </summary>
    public sealed class MauiLocalPreferences : ILocalPreferences
    {
        public T Get<T>(string key, T defaultValue) => Preferences.Default.Get(key, defaultValue);

        public void Set<T>(string key, T value) => Preferences.Default.Set(key, value);

        public bool ContainsKey(string key) => Preferences.Default.ContainsKey(key);
    }
}
