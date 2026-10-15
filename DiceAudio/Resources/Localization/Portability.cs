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
        public const string SharingHelp =
            "Saves your whole library in a single file: audio items, folders, tags, scenes, scenario groups and presets. " +
            "The audio files themselves are only included if you tick \"Also save the audio files\" in the export window.";

        public const string ExportNoSourceWarning =
            "{0} audio items have no download link, and their file is not included. " +
            "The person who imports will have to pick their own file for them.";
        public const string ExportNoSourceWarningOne =
            "1 audio item has no download link, and its file is not included. " +
            "The person who imports will have to pick their own file for it.";

        /// <summary>New string (not in the spec): broken references found while exporting.</summary>
        public const string ExportBrokenReferences =
            "{0} references point to audio items or scenes that are no longer in your library. " +
            "They are exported as they are, and the person who imports is told about them.";
        public const string ExportBrokenReferencesOne =
            "1 reference points to an audio item or a scene that is no longer in your library. " +
            "It is exported as it is, and the person who imports is told about it.";

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
            "DiceAudio backs up your data the first time you open it each day, and before every import, every restore and every copy of your data to another folder. " +
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

        /// <summary>New string (round 2): {0} = how many, {1} = the kind in the plural, {2} = the status.</summary>
        public const string ChoiceSameForAll = "Same choice for all {0} {1} marked “{2}”";

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
        public const string WizardIntroFolder =
            "Part of the content of this folder already exists in your library. " +
            "Please choose what to do in each case; the suggested choice is already selected.";
        public const string WizardInFile = "In the file";
        public const string WizardInLibrary = "In your library";
        public const string WizardNewName = "New name";
        public const string WizardNameEmpty = "Please enter a name.";
        public const string WizardNameTaken = "This name is already used. Please choose another one.";
        /// <summary>New strings (round 2): the bar that sets the names of the items imported under a new name.</summary>
        public const string WizardNamesLabel = "Names of copies:";
        public const string WizardNamesWord = "name";
        public const string WizardNamesBefore = "Text before the name";
        public const string WizardNamesAfter = "Text after the name";
        public const string WizardNamesHelp = "Changes every new name below that you have not typed yourself.";

        /// <summary>New strings (round 2): the file holds nothing of what the page that opened the import is about.</summary>
        public const string ImportOriginNoAudio =
            "You opened this from the Explorer, but this file contains no audio item or folder.";
        public const string ImportOriginNoScene =
            "You opened this from the Scenes page, but this file contains no scene.";
        public const string ImportOriginNoScenario =
            "You opened this from the Scenarios page, but this file contains no scenario group, scenario or scenario item.";
        public const string ImportOriginNoLocalScene =
            "You opened this from the local scenes of \"{0}\", but this file contains no scene.";
        /// <summary>Added to those warnings when the file has something to import.</summary>
        public const string ImportOriginRest = "You can still import the rest of its content.";
        public const string ImportIntoGroupNote = "The scenes of this file are added to the local scenes of \"{0}\".";

        /// <summary>New strings (round 3): the list of what the file holds, and the tick boxes.</summary>
        public const string WizardSelected = "{0} of {1} items selected";
        public const string WizardSelectedOne = "{0} of 1 item selected";
        public const string WizardSelectAll = "Select all";
        public const string WizardSelectNone = "Select none";
        public const string WizardImportThis = "Import \"{0}\"";
        public const string WizardAlsoNeeds = "Also import what it needs ({0})";
        public const string WizardAlsoNeedsHelp = "Ticks the items of this file that it uses and that are not ticked yet: {0}";
        public const string WizardShowContents = "Show what it contains";
        public const string WizardHideContents = "Hide what it contains";
        public const string WizardListIntro =
            "Nothing in this file conflicts with your library. Everything that is ticked will be imported; untick what you do not want.";
        public const string WizardListIntroFolder =
            "Nothing in this folder conflicts with your library. Everything that is ticked will be added; untick what you do not want.";
        public const string WizardUntickHint = "Untick what you do not want to import.";
        public const string NestedScenario = "Scenario";
        public const string NestedLocalScene = "Local scene";
        public const string DetailRoot = "Root";
        public const string DetailHasLink = "download link";
        public const string DetailInPack = "file in the audio file pack";
        public const string DetailNoLink = "no download link";
        public const string DetailScenarios = "{0} scenarios";
        public const string DetailScenarioOne = "1 scenario";
        public const string DetailLocalScenes = "{0} local scenes";
        public const string DetailLocalSceneOne = "1 local scene";
        public const string DetailItems = "{0} items";
        public const string DetailItemOne = "1 item";
        public const string DetailFrom = "from \"{0}\"";
        public const string DetailScenePreset = "Scene preset";
        public const string DetailItemPreset = "Scenario item preset";
        public const string DetailTagCategory = "{0} tag";
        public const string DetailTagNoCategory = "tag in no category";

        public const string WizardSkipWarningTitle = "Other content uses this one";
        public const string WizardSkipWarning = "If you do not import \"{0}\", the following will be imported without it: {1}";
        public const string WizardConfirmBtn = "Import";

        public const string ImportSummaryTitle = "Ready to import";
        public const string ImportSummaryText = "Nothing in this file conflicts with your library. The following will be added:";
        public const string ImportNothingToDo = "Everything in this file is already in your library, so there is nothing to import.";
        public const string ImportSummaryTextFolder = "Nothing in this folder conflicts with your library. The following will be added:";
        public const string ImportNothingToDoFolder = "Everything in this folder is already in your library, so there is nothing to add.";
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
        public const string WarningBrokenReferencesFolder =
            "Some of the content of this folder refers to things that are not in it ({0} references). It is added without them.";
        public const string WarningBrokenReferenceOne =
            "Some of the content of this file refers to something that is not in it (1 reference). It is imported without it.";
        public const string WarningBrokenReferenceOneFolder =
            "Some of the content of this folder refers to something that is not in it (1 reference). It is added without it.";

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

        /// <summary>New strings (round 2): the file of an audio item, in the Explorer.</summary>
        public const string FileNotDownloaded = "Not downloaded yet";
        public const string FileNotFound = "Not found on disk";
        public const string FileNone = "No audio file";
        public const string FileWaiting = "Waiting to download";
        public const string FileDownloading = "Downloading… {0}%";
        public const string DownloadBtn = "Download";
        public const string DownloadHelp = "Downloads the audio file from the link of this item.";
        public const string DownloadAllBtn = "Download all ({0})";
        public const string DownloadAllHelp = "Downloads the audio files of the items shown here that have a link but no file yet.";
        public const string PickFileHelp = "Choose an audio file on this computer for this item. A copy of it is kept in the audio folder.";

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

        // ── The data folder (spec §8.1, §8.3, and C6's StartupInvalidTitle) ──

        public const string DataFolderTitle = "Data folder";
        public const string DataFolderHelp =
            "This is where DiceAudio keeps your data and your audio files. You can choose another folder, for example one that " +
            "OneDrive or Dropbox keeps in sync, to use the same library on several computers. Please open DiceAudio on one computer at a time.";
        public const string DataFolderDefault = "The default folder";
        public const string DataFolderChangeBtn = "Choose another folder…";
        public const string DataFolderOpenBtn = "Open the folder";
        public const string DataFolderResetBtn = "Back to the default folder";
        public const string DataFolderDebugSuffix = "(DICE_DATA_DIR)";

        public const string MoveConfirmTitle = "Move your data to this folder?";
        public const string MoveConfirmText =
            "DiceAudio copies your data to {0}, checks the copy, then restarts and uses the new folder. " +
            "Your current data stays where it is until you delete it from this page.";
        public const string MoveConfirmBtn = "Move my data";
        public const string MoveCloudTip =
            "If OneDrive or another service keeps this folder in sync, please set it to always keep the files on this device. " +
            "Otherwise DiceAudio may have to wait for files to download.";
        public const string MoveSubfolderNote = "This folder already contains other files, so DiceAudio will keep its data in the subfolder {0}.";
        public const string MoveInProgress = "Copying your data… {0} of {1}";
        public const string MoveErrorNotWritable = "DiceAudio cannot write in this folder. Please choose another one.";
        public const string MoveErrorInsideCurrent = "This folder is the current data folder, or is inside it. Please choose a folder elsewhere.";
        public const string MoveErrorAppData =
            "Folders inside AppData cannot be used. Please choose a folder elsewhere, for example in your Documents or in a synchronised folder.";
        public const string MoveErrorOtherApp = "This folder holds DiceCombats data. Please choose a separate folder for DiceAudio.";
        public const string MoveFailed = "Your data could not be copied: {0}. Nothing has been changed, and DiceAudio still uses {1}.";
        public const string MoveRestartNotice = "DiceAudio will now restart to use the new folder.";
        public const string MoveAudioSize = "Your audio files take {0}. The target drive has {1} free.";
        public const string MoveRestartBtn = "Restart now";
        public const string MergeNothingNewText = "There is nothing to add from this folder. Do you want to use this folder with your data?";
        public const string MergeNothingNewBtn = "Use this folder";
        public const string ReplaceConfirmText = "This replaces the data in {0} with yours. The data that was there is backed up first in {0}\\Backups.";
        public const string StartupTryAgainHelp = "Checks the folder again, for example once the drive or the network is connected.";
        public const string StartupChooseFolderHelp = "Opens a folder that holds your DiceAudio data, if it is now somewhere else.";
        public const string StartupQuitHelp = "Closes DiceAudio without changing anything.";
        public const string MoveStopsPlayback = "Playback and downloads are stopped while your data is moved.";

        public const string OldCopyNotice =
            "Your previous data is still in {0}. Once you have checked that everything is in the new folder, you can delete it.";
        public const string OldCopyDeleteBtn = "Delete the old copy";
        public const string OldCopyDeleteConfirm = "This deletes your previous data in {0}. It cannot be undone.";

        public const string ExistingFolderTitle = "This folder already contains DiceAudio data";
        public const string ExistingThisComputer = "This computer: {0}";
        public const string ExistingThisFolder = "This folder: {0}";
        public const string ExistingFolderQuestion = "What would you like to do?";
        public const string ExistingUseFolderBtn = "Use the folder's data";
        public const string ExistingUseFolderHelp = "DiceAudio switches to the folder as it is. Your current data stays where it is.";
        public const string ExistingReplaceBtn = "Replace it with mine";
        public const string ExistingReplaceHelp = "The folder's data is backed up first, then replaced by yours.";
        public const string ExistingMergeBtn = "Merge";
        public const string ExistingMergeHelp =
            "Your data is added to the folder's data, and you choose what to do when an item exists on both sides.";

        public const string StartupMissingTitle = "The data folder cannot be reached";
        public const string StartupInvalidTitle = "The data folder cannot be used";
        public const string StartupMissingText =
            "DiceAudio keeps your data in {0}, but this folder cannot be reached at the moment. If it is on an external drive or on the network, " +
            "please connect it and try again. DiceAudio will not create an empty library there.";
        public const string StartupInvalidText =
            "The folder {0} cannot be used: it does not hold DiceAudio data, DiceAudio cannot write in it, or it was set up by a newer version of DiceAudio.";
        public const string StartupTryAgainBtn = "Try again";
        public const string StartupChooseFolderBtn = "Choose another folder…";
        public const string StartupUseLocalBtn = "Open the default folder instead";
        public const string StartupUseLocalHelp =
            "Opens the data in the default folder of DiceAudio instead, for this session only. This data may be older than the data " +
            "in your chosen folder, or there may be none. Changes you make now stay in the default folder. " +
            "Next time, DiceAudio will look for your chosen folder again.";
        public const string StartupUsingLocalBanner =
            "Your data folder could not be reached, so DiceAudio is showing the data in its default folder. " +
            "Changes you make now are saved there, not in your data folder.";
        public const string StartupQuitBtn = "Quit";

        // ── Several computers (spec §8.1, A9) ──

        public const string LockTitle = "DiceAudio is already open on another computer";
        public const string LockText =
            "The data folder has been in use on {0} since {1}. If you also open it here, changes made on one computer may overwrite those made on the other.";
        public const string LockSameComputerText =
            "DiceAudio already seems to be open on this computer, since {0}. If you open it a second time, the two windows may overwrite each other's changes.";
        public const string LockOpenAnywayBtn = "Open anyway";
        public const string LockOpenAnywayHelp =
            "Opens the data folder here as well. If DiceAudio is still open on the other computer, please close it there first.";
        public const string ChangedOnDiskTitle = "This data was changed on another computer";
        public const string ChangedOnDiskText =
            "The file {0} was changed in the data folder after DiceAudio read it. Reload it to get those changes, or overwrite it with what you have here.";
        public const string ChangedReloadBtn = "Reload";
        public const string ChangedOverwriteBtn = "Overwrite";
        public const string ChangedBanner = "Your data was changed on another computer. Reload it when you are ready.";
        public const string ChangedReloadStopsPlayback = "Reloading stops the scenarios that are playing.";
        public const string CloudConflictCopies =
            "Your synchronisation service has made copies of some files, because two computers changed them at the same time: {0}. " +
            "DiceAudio uses the original files. Please compare them and delete the copies you do not need.";
        public const string CloudOnlyWarning =
            "Some files of the data folder are only in the cloud: {0}. Please set the folder to always keep its files on this device.";
        public const string CloudOnlyBadge = "Only in the cloud";
        public const string UnsavedCopiesManual =
            "Some changes could not be saved because the same data was changed on another computer. They are kept in the data folder: {0}. " +
            "These files cannot be imported. To use one of them, please make a backup first and close DiceAudio. " +
            "Then give the file the name of the data file it belongs to, for example presets.json, in place of that file.";

        /// <summary>New strings (round 2): the headings of the messages shown in the notice window.</summary>
        public const string NoticeLoadFailedTitle = "A data file could not be read";
        public const string NoticeSaveFailedTitle = "A data file could not be saved";
        public const string NoticeRestoreTitle = "Restore";
        public const string NoticeRestoreMissingTitle = "Audio files missing after the restore";
        public const string NoticeCloudCopiesTitle = "Copies made by your synchronisation service";
        public const string NoticeCloudOnlyTitle = "Files only in the cloud";
        public const string NoticeUnsavedTitle = "Changes kept aside";

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
