# Release notes

Third release of DiceAudio. The application has been redrawn: it now wears the same visual language as
DiceCombats, built from the colours DiceAudio already had, so the two halves of the suite finally look
like one program. The home page has been rebuilt around what you actually open the app for, and the
update check no longer forgets what it found.

### New

- **A home page that is worth opening**
  It shows what is playing right now, live, with a stop button per scenario, and the scenario groups you
  pinned. The quick actions start something — a new scenario group, a new scene, a download, an import —
  instead of repeating the links already in the title bar.

- **Pinned scenario groups**
  The groups you keep coming back to can be pinned from the scenarios explorer and are then one click
  away on the home page. The list survives a rename, a re-filing into a collection, and a restart; a
  group deleted elsewhere simply stops appearing.

- **Checking for updates on demand**
  The settings page carries a "Check now" button and says when it last looked, next to the yt-dlp
  section that already worked this way.

### Improvements

- **One visual language across the suite**
  Every MudBlazor control is gone, replaced by the application's own. Buttons, inputs, tick boxes,
  steppers, menus, modals and confirmations are drawn by DiceAudio itself, which means they all answer
  to the same tokens: one card colour, one border, one radius, one set of accents. Nothing was
  recoloured — the tokens were derived from the colours the application already used.

- **Scenario groups, scenes and the home page share one card**
  A scenario group used to be a slab of the brand gradient with a small icon loose on it while a scene
  was a flat card with a gradient tile. They are the same object in three places and now look it.

- **Icons**
  A single stroked set drawn for the application, including the audio vocabulary: levels, transport,
  loop, shuffle, fades, tags, playlists.

- **Readable controls**
  Operable edges now clear the contrast ratio the standard asks for, and accent text uses a tint that is
  legible on a dark card rather than the identity pink, which was not.

- **The taskbar shows the application's name**
  The window had no title, so Windows had only the icon to go on.

### Fixed

- **An update that disappeared**
  The settings page reported anything that was not a confirmed update as "up to date", so a request
  refused by GitHub — which is what happens once the hourly limit for your address is spent — looked
  exactly like good news, and an update found a minute earlier vanished. A failed check now says so and
  keeps what it last knew. The answer is also cached for six hours instead of being asked again on every
  visit to a page, which is what exhausted the limit in the first place.

- **The version was always 0.0.0**
  Shown in the settings, and compared against the latest release to decide whether an update exists.

- **Volume sliders jumping about**
  On a French system the slider was given its value with a comma, rejected it, and sent the handle
  somewhere other than where the coloured part of the track ended. Every slider bound to a decimal value
  is affected: scene layers, scene step commands, clip volume and the seek bars.

- **The slider gradient ran the wrong way**
  Silence wore the loud colour, and the colour under the handle read as full volume at every position.
  Quiet is the cool end now, and the colour under the handle is the colour of the value it is on.
