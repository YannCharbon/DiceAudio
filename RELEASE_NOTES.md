# Release notes

Second release of DiceAudio, the audio side of the DiceCombats ecosystem. Mostly about finding your way
around a library once it has grown, filing the scenarios of several adventures, and keeping the YouTube
downloader alive.

### New

- **Big and list views, with sorting**
  The explorer and the audio picker switch between large cards and a compact list, folders and items
  independently, and sort either by the order things were added or by name. The choice is kept in the
  preferences, so it survives a restart.

- **Collections**
  Once there are many scenario groups, a collection files the ones that belong together behind a single
  folder, one per adventure, season or show. The scenarios page opens a collection like a folder, and the
  search there reaches inside them.

- **Moving folders**
  A virtual folder can now be moved to another place in the tree, with everything it holds, instead of
  being emptied item by item.

- **yt-dlp keeps itself up to date**
  YouTube breaks yt-dlp every few months, which is when downloads stop working. A new section of the
  settings shows the version in use, checks for a newer one and installs it, either on demand or on its
  own at startup, where the loading screen stays up and says what it is doing. Windows only.

- **Scenario group dropdown in the Trilium widget**
  The widget carries its own group selector next to the scenario and item ones, so a note can reach any
  group without going through the application first.

### Improvements

- **Audio item cards**
  The cards of the explorer and of the picker have been redesigned and now come from one shared set of
  styles, so an item reads the same wherever it shows up, and the ring marking a selected card is no
  longer hidden behind the card's own artwork.

- **One way of choosing a folder**
  Moving, importing and downloading each had their own destination picker. They now share a single folder
  browser, which behaves the same in all three places.

- **The scenario group page saves itself**
  Every change is written as it is made, the way the rest of the application persists, and the Save button
  is gone. The group name sits at the top of the page as its title and is renamed in place.

### Bug fixes

- **Seeks in MP3 files landed seconds late**
  Starting a clip from a saved point, or clicking in the waveform of the clip editor, could land as much as
  fifteen seconds after the requested position in a 48 kHz MP3 file, while the position displayed was the
  one asked for. Positions in MP3 files are now read from the frames of the file instead of an estimate
  based on the bitrate. The waveform also draws its bars one screen pixel wide, so the step under the
  cursor is the step that plays.

### Notes

- A clip start that was placed by ear on a 48 kHz MP3 before this release was compensating for the seek
  bug above. Now that seeking is accurate, such a clip starts earlier than it used to, by the error it was
  working around, and has to be set again.
- Windows is the only build published. Android is kept in the project but is not released at the moment.
- The interface is in English.
