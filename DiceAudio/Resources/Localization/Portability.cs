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

        // ── Export (spec §8.1 and §8.3) ────────────────────────────────────

        public const string ExportTitle = "Export";
        public const string ExportContains = "This file will contain:";
        public const string ExportDone = "The file has been saved.";
        public const string ExportFailed = "The file could not be saved: {0}";
        public const string WizardCancelBtn = "Cancel";

        public const string ExportItemBtn = "Export";
        public const string ExportSelectionBtn = "Export…";
        public const string ExportFolderBtn = "Export folder";
        public const string ExportIncludeSubfolders = "Include subfolders";
        public const string ExportAllScenesBtn = "Export all scenes";
        public const string ExportGroupScenesBtn = "Export all scenes of this group";
        public const string ExportScenarioBtn = "Export this scenario";
        public const string ExportGroupBtn = "Export this scenario group";
        public const string ExportCollectionBtn = "Export this collection";
        public const string ExportAllPresetsBtn = "Export all presets";
        public const string ExportLibraryBtn = "Export the whole library…";

        public const string ExportLibraryHelp =
            "Saves your whole library in a single file: audio items, folders, tags, scenes, scenario groups and presets. " +
            "The audio files themselves are only included if you tick the option below.";

        public const string ExportNoSourceWarning =
            "{0} audio items have no download link, and their file is not included. " +
            "The person who imports will have to pick their own file for them.";

        /// <summary>New string (not in the spec): broken references found while exporting.</summary>
        public const string ExportBrokenReferences =
            "{0} references point to audio items or scenes that are no longer in your library. " +
            "They are exported as they are, and the person who imports is told about them.";

        /// <summary>New string (not in the spec): the file type shown in the save window.</summary>
        public const string ExportFileType = "DiceAudio file";

        /// <summary>New string (not in the spec): an export with nothing in it.</summary>
        public const string ExportNothing = "There is nothing to export here.";

        // ── The audio file pack (spec §8.3) ───────────────────────────────

        public const string ExportMediaOption = "Also save the audio files, in a second file (.diceamedia)";
        public const string ExportMediaHelp =
            "Without this second file, the person who imports gets the links to download the audio again, " +
            "and can pick their own files for the rest.";
        public const string ExportMediaSize = "Audio files on this computer: {0} of {1}, {2} in total.";
        public const string ExportChecksumOption =
            "Add checksums, so that damaged files are detected on import (slower)";

        public const string CopyrightTitle = "Before you export audio files";
        public const string CopyrightText =
            "Audio files are often protected by copyright. You may use this export for your own needs, " +
            "for example to move your library to another computer. Sharing these files publicly, or with people " +
            "who do not hold the rights to them, is not allowed. You are responsible for what you do with these files; " +
            "neither DiceAudio nor I can be held responsible for it.";
        public const string CopyrightAcknowledge = "I understand";

        /// <summary>New string (not in the spec): progress while the audio files are written.</summary>
        public const string ExportMediaProgress = "Saving the audio files… {0} of {1}";

        /// <summary>New string (not in the spec): the user stopped the export.</summary>
        public const string ExportCancelled = "The export was stopped. Nothing has been saved.";

        public const string DataPanelTitle = "Data";
        public const string SharingTitle = "Sharing";

        public const string SummaryAudioItems = "Audio items: {0}";
        public const string SummaryFolders = "Folders: {0}";
        public const string SummaryTags = "Tags: {0}";
        public const string SummaryScenes = "Scenes: {0}";
        public const string SummaryScenarioGroups = "Scenario groups: {0}";
        public const string SummaryScenarios = "Scenarios: {0}";
        public const string SummaryPresets = "Presets: {0}";

        /// <summary>New string (not in the spec): scenario items exported on their own.</summary>
        public const string SummaryScenarioItems = "Scenario items: {0}";
    }
}
