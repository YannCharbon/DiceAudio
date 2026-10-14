/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

namespace DiceAudio.Resources.Localization
{
    /// <summary>
    /// The texts of the data safety and portability features. DiceAudio is in
    /// English only for now, so they are plain constants; the names are the
    /// keys of the shared spec, so a Portability.resx can replace this class
    /// later without touching the call sites.
    /// </summary>
    public static class Portability
    {
        public const string ErrorLoadFailed =
            "The file {0} could not be read. To protect it, DiceAudio will not save over it during this session. " +
            "You can restore a backup from Settings, in the Data section.";

        public const string ErrorSaveFailed =
            "The file {0} could not be saved. Please check that the data folder can be reached and that the disk is not full, then try again.";

        public const string AlertCloseBtn = "Close";

        public const string DataFolderUnusable =
            "The data folder cannot be used at the moment, so nothing can be saved in it during this session.";
    }
}
