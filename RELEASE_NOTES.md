# Release notes

First public release of DiceAudio, the audio side of the DiceCombats ecosystem.

### New

- **Audio library**
  Virtual folders and tags in three families, music, ambience and sound effect, with a search that goes
  through the whole library. Items keep their own volume, loop, fade and delay.

- **Bring the sound in**
  Download from a video address with the bundled yt-dlp, or import files already on the disk. Rename,
  tag and move items without touching the file system.

- **Layered scenes**
  A scene stacks layers, each one an ambience, a track or an effect pointing at an item of the library,
  with its own playback settings.

- **Contexts**
  A scene carries the states it needs, such as fireplace, crowded or brawl. Switching to a context
  crossfades every layer to its level in that state over the transition time.

- **Scenarios**
  The scenes of an evening chained in order, kept one click away and played through as the session goes.

- **Presets**
  A catalogue of ready made setups a scene can be started from.

- **Local control API**
  An HTTP server on localhost exposing the transport, the scenario navigation and the scene contexts, so
  another application can drive the sound. Off by default, switched on in the settings.

### Notes

- Windows is the main target. The Android build is experimental and some views still render imperfectly.
- The interface is in English.
