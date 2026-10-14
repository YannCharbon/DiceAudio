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

        // ── Import (spec §8.1 and §8.3) ───────────────────────────────────

        public const string StatusNew = "New";
        public const string StatusUpToDate = "Up to date";
        public const string StatusDiffers = "Differs";
        public const string StatusNameUsed = "Name already used";

        public const string ChoiceNothing = "Nothing to do";
        public const string ChoiceImport = "Import";
        public const string ChoiceSkip = "Skip";
        public const string ChoiceImportAsCopy = "Import as a copy";
        public const string ChoiceReplaceLocal = "Replace the local one";
        public const string ChoiceKeepLocal = "Keep the local one";
        public const string ChoiceMerge = "Merge";
        public const string ChoiceImportWithNewName = "Import with a new name";
        public const string ChoiceKeepBothSameName = "Keep both with the same name";
        public const string ChoiceUseLocal = "Use the local one instead";
        public const string ChoiceApplyToAll = "Apply to all of this kind";

        public const string ChoiceImportHelp = "Adds it to your library.";
        public const string ChoiceSkipHelp = "Leaves it out of the import.";
        public const string ChoiceImportAsCopyHelp = "Adds the imported version next to yours, under the name below. Yours is not changed.";
        public const string ChoiceReplaceLocalHelp = "Your version is overwritten by the imported one, and everything that uses it then uses the imported version.";
        public const string ChoiceKeepLocalHelp = "The imported version is left out. Anything imported that uses it will use yours.";
        public const string ChoiceMergeHelp = "Keeps yours and adds what the imported one contains. Each part is then checked on its own.";
        public const string ChoiceImportWithNewNameHelp = "Imports it under the name below, so that you can tell the two apart.";
        public const string ChoiceKeepBothSameNameHelp = "Imports it with the same name. You will then have two with this name.";
        public const string ChoiceUseLocalHelp = "The imported one is left out, and anything imported that uses it will use yours instead.";

        public const string WizardTitle = "Import";
        public const string WizardIntro =
            "Part of the content of this file already exists in your library. " +
            "Please choose what to do in each case; the suggested choice is already selected.";
        public const string WizardInFile = "In the file";
        public const string WizardInLibrary = "In your library";
        public const string WizardNewName = "New name";
        public const string WizardNameEmpty = "Please enter a name.";
        public const string WizardNameTaken = "This name is already used. Please choose another one.";
        public const string WizardSkipWarningTitle = "Other content uses this one";
        public const string WizardSkipWarning = "If you do not import \"{0}\", the following will be imported without it: {1}";
        public const string WizardConfirmBtn = "Import";

        public const string ImportSummaryTitle = "Ready to import";
        public const string ImportSummaryText = "Nothing in this file conflicts with your library. The following will be added:";
        public const string ImportNothingToDo = "Everything in this file is already in your library, so there is nothing to import.";
        public const string ImportBackupNote = "A backup of your current data is made before anything is changed.";
        public const string ImportReportTitle = "Import finished";
        public const string ReportAdded = "Added: {0}";
        public const string ReportReplaced = "Replaced: {0}";
        public const string ReportKeptLocal = "Kept your version: {0}";
        public const string ReportMerged = "Merged: {0}";
        public const string ReportSkipped = "Not imported: {0}";
        public const string ReportUpToDate = "Already up to date: {0}";
        public const string ImportFailed = "The import could not be completed, so your data has been put back as it was before. Details: {0}";
        public const string ImportFromFileBtn = "Import a DiceAudio file…";

        public const string ErrorUnreadableFile = "This file could not be read. It may be damaged, or it may not be a DiceAudio file.";
        public const string ErrorOtherAppFile = "This file comes from DiceCombats. Please open it in DiceCombats.";
        public const string ErrorNewerFormat = "This file was made with a newer version of DiceAudio. Please update DiceAudio to open it.";
        public const string ErrorFileTooLarge = "This file is too large: {0} MB, while DiceAudio accepts up to {1} MB.";
        public const string WarningBrokenReferences =
            "Some of the content of this file refers to things that are not in it ({0} references). It is imported without them.";

        public const string KindTag = "Tags";
        public const string KindFolder = "Folders";
        public const string KindAudioItem = "Audio items";
        public const string KindScene = "Scenes";
        public const string KindLocalScene = "Scenes of a scenario group";
        public const string KindScenarioItem = "Scenario items";
        public const string KindScenario = "Scenarios";
        public const string KindScenarioGroup = "Scenario groups";
        public const string KindPreset = "Presets";

        public const string TargetGroupLabel = "Add to the scenario group";
        public const string TargetScenarioLabel = "Add to the scenario";
        public const string TargetNewGroup = "A new scenario group named \"{0}\"";
        public const string TargetNewScenario = "A new scenario named \"{0}\"";

        public const string MediaPackAsk = "This export comes with a second file holding the audio files (.diceamedia). Would you like to open it?";
        public const string MediaPackOpenBtn = "Open the audio files…";
        public const string MediaPackSkipBtn = "Continue without them";
        public const string MediaPackMismatch = "This audio file pack belongs to another export and cannot be used with this file.";
        public const string MediaPackFileRejected = "The file for \"{0}\" in the audio file pack is damaged and was not used.";

        public const string FollowUpTitle = "Audio files";
        public const string FollowUpFromPack = "Taken from the audio file pack: {0}";
        public const string FollowUpMatched = "Found on this computer by name or size: {0}";
        public const string FollowUpQueued = "Added to the download list: {0}";
        public const string FollowUpMissingTitle = "Files still missing";
        public const string FollowUpMissingHelp =
            "These audio items have no file yet. Pick a file from your computer for each of them, or leave them for later: " +
            "the Explorer can show them with the \"Missing files\" filter.";
        public const string PickFileBtn = "Pick file…";

        /// <summary>Spec §8.1 (the move wizard's text), used too when the audio file pack does not fit.</summary>
        public const string MoveErrorNoSpace = "There is not enough free space on this drive: {0} needed, {1} available.";
        public const string MissingFilesFilter = "Missing files";

        /// <summary>New string (not in the spec): the import and its putting back both failed.</summary>
        public const string ImportRollbackFailed =
            "The import could not be completed, and your data could not be put back automatically. " +
            "Your data from just before the import is in the backup {0}. Please restore it from Settings, in the Data section.";

        /// <summary>New string (not in the spec): shown while the import runs.</summary>
        public const string ImportWorking = "Importing…";

        /// <summary>New string (not in the spec): the audio file pack could not be read.</summary>
        public const string MediaPackUnreadable = "The audio file pack could not be read, so the import goes on without it.";

        /// <summary>New string (not in the spec): the label of the import's file type in the open window.</summary>
        public const string MediaPackFileType = "DiceAudio audio file pack";

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
