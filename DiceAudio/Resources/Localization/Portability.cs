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

        // ── Backups (spec §8.1, §8.3, and DiceCombats C3's added keys) ─────

        public const string BackupsTitle = "Backups";
        public const string BackupsHelp =
            "DiceAudio backs up your data the first time you open it each day, and before every import, restore or change of data folder. " +
            "A backup contains your library, folders, tags, scenes, scenario groups, presets and settings, but never the audio files themselves.";
        public const string BackupNowBtn = "Back up now";
        public const string BackupRestoreBtn = "Restore";
        public const string BackupRestoreFromFileBtn = "Restore from a file…";
        public const string BackupSaveToFileBtn = "Save a backup to a file…";
        public const string BackupOpenFolderBtn = "Open the backups folder";
        public const string BackupListEmpty = "No backup yet.";
        public const string BackupLatestLabel = "Most recent backup";
        public const string BackupAllBtn = "All backups…";
        public const string BackupAllTitle = "All backups";
        public const string BackupColDate = "Date";
        public const string BackupColComputer = "Computer";
        public const string BackupColReason = "Reason";
        public const string BackupColContents = "Contents";
        public const string BackupReasonAuto = "Automatic";
        public const string BackupReasonManual = "Made by you";
        public const string BackupReasonBeforeImport = "Before an import";
        public const string BackupReasonBeforeRestore = "Before a restore";
        public const string BackupReasonBeforeMove = "Before a change of data folder";
        public const string BackupUnreadable = "This backup cannot be read.";
        public const string BackupFileType = "DiceAudio backup";
        public const string BackupDone = "The backup has been made.";
        public const string BackupFailed = "The backup could not be made: {0}. Your data has not been changed.";

        public const string RestoreConfirmTitle = "Restore this backup?";
        public const string RestoreConfirmText =
            "Your current data will be replaced by the data saved on {0}. " +
            "A backup of your current data is made first, so that you can go back if needed.";
        public const string RestoreConfirmBtn = "Restore";
        public const string RestoreDone = "The backup has been restored.";
        public const string RestoreWrongApp = "This backup comes from DiceCombats and cannot be restored in DiceAudio.";
        public const string RestoreNewerVersion = "This backup was made with a newer version of DiceAudio. Please update DiceAudio to restore it.";
        public const string RestoreDamaged = "This backup is damaged and cannot be restored. Your data has not been changed.";
        public const string RestoreFailed = "The restore could not be completed: {0}. Your data has been put back from the backup made just before.";
        public const string RestoreRollbackFailed =
            "The restore could not be completed, and your data could not be put back automatically. " +
            "Your data from just before the restore is in the backup {0}. Please restore it from this page.";
        public const string RestoreAudioNote =
            "Audio files are not part of a backup. Audio items whose file is no longer on this computer are listed after the restore.";
        public const string RestoreMissingFiles = "These audio items no longer have their file on this computer ({0}):";

        /// <summary>New string (not in the spec): the end of a long list of names.</summary>
        public const string RestoreMissingMore = "and {0} more.";

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
