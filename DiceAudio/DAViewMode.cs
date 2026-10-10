/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using Microsoft.Maui.Storage;

namespace DiceAudio
{
    /// <summary>
    /// How the explorer and the audio picker lay out folders or audio items.
    /// Folders and items are switched separately.
    /// </summary>
    public enum DAViewMode { Big, List }

    /// <summary>
    /// Neither <see cref="DAAudioItem"/> nor <see cref="DAVirtualAudioFolder"/>
    /// stores a timestamp, so <c>DateAdded</c> is the order they sit in on disk.
    /// </summary>
    public enum DASortMode { DateAdded, Name }

    /// <summary>
    /// The explorer's and the picker's view choices, remembered across runs in
    /// the app preferences (same store as the control server's settings). Both
    /// screens share one set: it is one preference about how the library reads.
    /// </summary>
    public static class DAViewPrefs
    {
        public const string PrefFolderView = "ExplorerFolderView";
        public const string PrefItemView   = "ExplorerItemView";
        public const string PrefSort       = "ExplorerSort";

        public static DAViewMode FolderView
        {
            get => (DAViewMode)Preferences.Default.Get(PrefFolderView, (int)DAViewMode.Big);
            set => Preferences.Default.Set(PrefFolderView, (int)value);
        }

        public static DAViewMode ItemView
        {
            get => (DAViewMode)Preferences.Default.Get(PrefItemView, (int)DAViewMode.Big);
            set => Preferences.Default.Set(PrefItemView, (int)value);
        }

        public static DASortMode Sort
        {
            get => (DASortMode)Preferences.Default.Get(PrefSort, (int)DASortMode.DateAdded);
            set => Preferences.Default.Set(PrefSort, (int)value);
        }
    }
}
