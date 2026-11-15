# Release notes

Fifth release of DiceAudio. From now on, DiceAudio installs its own updates: when a new version is out, it
shows what is new and installs it for you, right away or when you close the app. This version is the last
one to install by hand, from the ZIP as before. It also makes scenarios quicker to build, with items you
can duplicate and drag from one scenario to another.

### New

- **Automatic updates**
  When a new version is out, a window shows its release notes with three choices. Update now saves your
  data, closes DiceAudio, installs the new version and opens it again, in a few seconds. Postpone until
  closing downloads it in the background and installs it when you close DiceAudio, which is handy in the
  middle of a session. Later closes the window; the update stays available in Settings, in the Updates
  section. Before anything is installed, DiceAudio checks that the download is complete and carries the
  same signature as the version you use, and it puts the previous version back if anything goes wrong.
  Your data is never touched.

- **Duplicate a scenario item**
  The Duplicate button on a scenario item places a copy right after it, with "(copy)" after its name.

- **Drag scenario items between scenarios**
  An item can now be dragged from one scenario to another scenario of the same group, as well as within
  its own scenario.

- **Rename an audio item**
  A pencil button on each audio item in the Explorer renames it in place. Only the name shown in
  DiceAudio changes: the file on disk keeps its own name, and the scenes and scenarios that use the item
  are not affected.

- **Pinned audio items in the audio picker**
  While you look for the right sound, the pin on an item keeps it in a Pinned tab, so you can make your
  final choice among a few candidates. Each scenario group keeps its own pinned items until you close
  DiceAudio. Picking an item straight away works as before.

- **Sound effects in the Trilium widget**
  The widget now shows the sound effects of the selected scenario item, and plays each one with a click.
  To use them, copy the new widget files into Trilium, as described in the README of the widget.

### Improvements

- **A new scenario item takes the name of what it plays**
  As long as you have not named it yourself, a new item is named after the music, the ambiance or the
  scene you choose for it, instead of keeping "Default item".

- **New items stay in view**
  Adding an item to a scenario that is wider than the window scrolls the row, so the new item and the
  New item button stay visible.

- **Back and Forward in the audio picker**
  The audio picker has the same Back and Forward buttons as the Explorer.

- **A home icon in the folder paths**
  The top of the folder paths in the Explorer, the audio picker and the Move to folder windows shows a
  home icon instead of the word "Root".

### Fixed

- **Going back to an earlier step of a linear scene**
  Every layer was cut at once, so the fade-outs of that step had nothing left to fade. The layers now keep
  playing and fade as the step says, which makes going back and forth between two steps smooth.

- **A new scenario group created in a collection**
  The group was filed in the collection, but leaving it brought you back to the list of collections, where
  it seemed to be missing. Leaving a scenario group now returns to its collection.

- **Long audio names in the scene editor**
  A long name covered the Settings and Delete buttons of its layer. It is now cut short, and the full
  name shows when you hover over it.

- **The playback cursor of a scenario item**
  On a computer set to a language that writes decimals with a comma, such as French, the cursor moved as
  if every track were 100 seconds long.

- **The license in the welcome window**
  It named the Apache 2.0 license. DiceAudio is published under the GPL-3.0-or-later license.
