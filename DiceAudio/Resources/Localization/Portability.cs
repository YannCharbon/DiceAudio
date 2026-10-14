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

        /// <summary>
        /// Until the audio file pack exists (A6), the library help cannot point
        /// at an option below: this says the same without it.
        /// </summary>
        public const string ExportLibraryHelpNoMedia =
            "Saves your whole library in a single file: audio items, folders, tags, scenes, scenario groups and presets. " +
            "The audio files themselves are not included.";

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
