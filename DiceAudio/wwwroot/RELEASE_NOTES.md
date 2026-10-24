# Release notes

Fourth release of DiceAudio. It is about your data: you can now share any part of your library with
other game masters, keep backups and restore them from Settings, choose the folder where everything is
kept, and use the same library on several computers. The website has a new page, "Your data", which
explains all of this step by step.

### New

- **Exporting**
  Audio items, folders, scenes, scenarios, scenario groups, collections and presets each have an Export
  button, and Settings → Data offers Export the whole library…. The file also holds everything the
  exported content uses: a scenario group comes with its scenes, the audio items they play, and the
  folders and tags of those items. The audio itself is not in the file, only the download links. If you
  tick Also save the audio files, the audio files on your computer are saved in a second file
  (.diceamedia) next to the first one.

- **Importing**
  Import a DiceAudio file… is available in the Explorer, on the Scenes and Scenarios pages, in the Local
  scenes tab of a scenario group, and in Settings. The import window lists everything the file holds,
  with a tick box for each part. When an item is already in your library, you choose what to do with it:
  import it as a copy, replace yours, keep yours, or merge a folder or a scenario group. One button gives
  the same answer to many items at once, and you can set a text to put before and after the names of the
  copies. After the import, DiceAudio finds the audio files in the pack, on your computer or through the
  download links, and lists the items still without a file.

- **Backups**
  DiceAudio backs up your library, scenes, scenarios, tags, presets and settings the first time you open
  it each day, and before every import or restore. Settings → Data shows the most recent backup with a
  Restore button, and All backups… lists the others. A backup can also be saved to a file and restored
  from one. The audio files are not part of the backups.

- **A data folder of your choice**
  Your library, its backups and your audio files are now kept together in one data folder. Choose
  another folder… in Settings moves everything there, checks the copy and restarts. Your previous data
  stays where it was until you delete it.

- **The same library on several computers**
  A data folder kept in sync by OneDrive, Dropbox or a similar service can be used from several
  computers. DiceAudio tells you when the folder is already open on another computer, offers to reload
  when data was changed elsewhere, and asks which version to keep when both sides changed the same thing.
  It never reloads by itself in the middle of a session.

- **The state of each audio file**
  In the Explorer, an audio item whose file is not on this computer says so on its card: not downloaded
  yet, not found on disk, no audio file, or only in the cloud. A Download or Pick file… button sits next
  to it. Download all adds every item of a folder that is not downloaded yet, and the Missing files filter
  gathers these items from every folder.

### Improvements

- **Downloads carry on in the background**
  Tracks now go through a download list that keeps going when you close the download window. Each track
  joins your library as soon as it is downloaded. Stopping a download deletes the partial files it left.

- **Safer saving**
  The library files are written in a way that survives a crash or a power cut in the middle of a save.
  A file that failed to load is no longer overwritten by an empty library at the next save.

### Fixed

- **Confirmation dialogs that did not appear**
  A mistake in the stylesheet hid the confirmation dialogs raised from inside another window.

- **Long release notes in the welcome dialog**
  They ran past the bottom of the dialog. They now scroll inside it.

- **The title bar**
  It could move out of place when the pages were rebuilt.
