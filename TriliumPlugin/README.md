# DiceAudio widget for Trilium Render Notes

This package contains a Trilium-friendly version of the DiceAudio remote-control widget.

The important change from the original standalone HTML file is that Trilium should use **two notes**:

1. an **HTML Code note** containing only the widget mount element, and
2. a **JavaScript frontend Code note** as a direct child of that HTML note containing the widget logic.

This avoids inline `<script>` execution problems and avoids duplicate `id` problems when the rendered widget is included inside other notes.

## Files

```text
trilium/
  DiceAudioWidgetHTML.html          Paste into the HTML Code note.
  DiceAudioWidgetJS.frontend.js     Paste into a JavaScript frontend child note.

browser-preview/
  diceaudio-widget-standalone.html  Optional browser-only preview/test file.
```

## Required DiceAudio setting

In DiceAudio, enable the HTTP control server / remote control feature.

Default URL used by the widget:

```text
http://localhost:8765
```

If you changed the port in DiceAudio, edit this line in `DiceAudioWidgetHTML.html` before pasting it into Trilium:

```html
<div class="diceaudio-widget" data-diceaudio-port="8765"></div>
```

For example, port `9000` would be:

```html
<div class="diceaudio-widget" data-diceaudio-port="9000"></div>
```

## Trilium setup

Create these notes in Trilium:

```text
DiceAudio widget renderer                 Type: Render Note
  ~renderNote -> DiceAudioWidgetHTML

DiceAudioWidgetHTML                       Type: Code, language: HTML
  DiceAudioWidgetJS.frontend              Type: Code, language: JavaScript frontend
```

The JavaScript frontend note must be a **direct child** of the HTML Code note.

### Step 1 — create the HTML Code note

1. Create a new note named `DiceAudioWidgetHTML`.
2. Set its note type to **Code**.
3. Set language to **HTML**.
4. Paste the full content of:

```text
trilium/DiceAudioWidgetHTML.html
```

### Step 2 — create the JavaScript frontend child note

1. Create a child note under `DiceAudioWidgetHTML`.
2. Name it `DiceAudioWidgetJS.frontend`.
3. Set its note type to **Code**.
4. Set language to **JavaScript frontend**.
5. Paste the full content of:

```text
trilium/DiceAudioWidgetJS.frontend.js
```

### Step 3 — create the Render Note

1. Create a new note named `DiceAudio widget renderer`.
2. Set its note type to **Render Note**.
3. Add an owned relation attribute:

```text
~renderNote=@DiceAudioWidgetHTML
```

Use Trilium's autocomplete after typing `=@` so the relation points to the actual note.

### Step 4 — test directly

Open `DiceAudio widget renderer` directly.

Expected result:

- a compact DiceAudio control panel appears, or
- a warning appears saying DiceAudio is not reachable on `http://localhost:8765`.

If you only see a title or a blank note, see the troubleshooting section below.

## Include it inline inside another note

In the campaign/text note where you want the widget:

1. Insert an **Include Note** block.
2. Select `DiceAudio widget renderer`, not `DiceAudioWidgetHTML` and not `DiceAudioWidgetJS.frontend`.

Correct include target:

```text
DiceAudio widget renderer
```

Incorrect include targets:

```text
DiceAudioWidgetHTML
DiceAudioWidgetJS.frontend
```

## Layout

The widget is a fixed 2×2 grid — wide rather than tall, so it sits between two
paragraphs without eating the note:

```text
┌────────────────────────┬─────────────────────────────┐
│ icon + scenario        │ player · volume · status    │
├────────────────────────┼─────────────────────────────┤
│ item                   │ scene steps / contexts      │
└────────────────────────┴─────────────────────────────┘
```

Nothing ever moves between cells. The left column is a single cell holding both
dropdowns in a fixed-gap stack, so a cue row that wraps onto a second line never
pushes them apart; cue chips wrap centred and nothing scrolls. Below ~500 px of
note width the grid collapses to one column, in the same reading order.

Scenarios are listed flat — the group level is not shown, since a scenario name
is what identifies a selection during play.

When the selected item is the one playing, the whole widget background runs the
same sliding gradient the app shows behind a playing scenario item — same
keyframes, same 3 s cadence, at about a third of the opacity so it stays quiet
inside a note. The play button becomes a pause button at the same moment, and
loses its highlight so it weighs no more than stop. Both stop when the item
stops, or when you select something else. The animation is disabled (the tint
stays) under `prefers-reduced-motion`.

The palette follows the Trilium theme: the widget measures the brightness of the
note background at mount time and picks its light or dark token set accordingly.

## Volume

The speaker + slider next to the transport drive the **master level of the
selected scenario** through `POST /api/volume`. Clicking the speaker mutes and
unmutes while remembering the level; the icon turns red and the track greys out
while muted.

The level is a bus gain applied on top of everything that scenario plays —
tracks, scene layers and one-shots — so it scales playback without touching the
per-usage volumes stored in the scenario, and fades and crossfades keep working
unchanged. It lives for the app session and is *not* saved to disk.

While you drag, the widget stops accepting the polled value for 1.5 s so the
server cannot fight the slider; otherwise it always shows the app's real level
(including changes made by another widget pointing at the same scenario).

## Widget state persistence

Each widget instance remembers its selected scenario and item across note
refreshes and Trilium restarts (stored in `localStorage`).

## Scene items

The widget adapts its controls to the scene's driving model, shown in the
bottom-right cell:

- **Linear scenes** — numbered step chips plus an **advance** button. Picking a
  chip jumps to that step; advancing walks through them in order and steps
  already passed are dimmed. Play starts at step 1.
- **Contextual scenes** — one chip per named state (e.g. *fireplace*,
  *crowded*, *brawl*). Click one to crossfade the whole scene into that state;
  the active context is highlighted. Play (▶) starts the scene in its configured
  **default context**.

Non-scene items get the now-playing line in that cell instead, so it is never
empty. Highlighting only follows the *selected* item: when another item is
playing, the chips stay neutral rather than reporting someone else's state.

The widget learns the model from the `sceneMode` field returned by
`/api/groups`, so no manual configuration is needed.

How instances are identified:

- By default: the **hosting note's id + the widget's position** on the page.
  A note containing three players keeps three independent selections.
- Reordering the includes inside a note shifts the positions, so selections may
  swap. To make an instance immune to reordering, give it an explicit key by
  using a dedicated HTML note for it with:

```html
<div class="diceaudio-widget" data-diceaudio-port="8765" data-diceaudio-key="harbour-arrival"></div>
```

Any unique string works as `data-diceaudio-key`; state then follows that key no
matter where the widget appears.

## Troubleshooting

### I only see the title when I include the renderer note

Check that `DiceAudio widget renderer` is actually note type **Render Note**. A normal text note with a `~renderNote` relation will not behave like a Render Note.

Also open `DiceAudio widget renderer` directly first. If it does not render directly, it will not render when included.

### The renderer note is blank

Check these things:

1. `~renderNote` is a **relation**, not a label.
2. `~renderNote` points to `DiceAudioWidgetHTML`.
3. `DiceAudioWidgetHTML` is a **Code** note with language **HTML**.
4. `DiceAudioWidgetJS.frontend` is a **Code** note with language **JavaScript frontend**.
5. `DiceAudioWidgetJS.frontend` is a **direct child** of `DiceAudioWidgetHTML`.
6. The HTML note contains this element:

```html
<div class="diceaudio-widget" data-diceaudio-port="8765"></div>
```

### The widget appears but says DiceAudio is not reachable

Make sure DiceAudio is running and the HTTP control server is enabled.

Test in a browser:

```text
http://localhost:8765/api/groups
```

You should see JSON. If not, the DiceAudio control server is not reachable on that port.

### It worked in a browser but not in Trilium

That was the reason for this split package. The standalone HTML file uses an inline `<script>`, while Trilium Render Notes are more reliable when the JavaScript is placed in a **JavaScript frontend** child note.

### I want to use it outside Trilium

Open:

```text
browser-preview/diceaudio-widget-standalone.html
```

That file is only for browser testing. Do not use it as the Trilium render source.

## API endpoints used by the widget

Base URL:

```text
http://localhost:{port}
```

| Method | Path | Body | Effect |
|---|---|---|---|
| GET | `/api/groups` | – | Loads groups, scenarios, items, `sceneMode`, and scene cues (steps or contexts). |
| GET | `/api/state` | – | Reads the currently playing item, `sceneMode`, and current cue. |
| POST | `/api/play` | `{scenarioId, itemId}` | Plays the selected item (contextual scenes start in their default context). |
| POST | `/api/stop` | `{scenarioId?}` | Stops one scenario or all playback. |
| POST | `/api/next` | `{scenarioId}` | Moves to the next item. |
| POST | `/api/prev` | `{scenarioId}` | Moves to the previous item. |
| POST | `/api/scene/advance` | `{scenarioId, itemId}` | Advances a linear scene to the next step (cycles contexts for contextual scenes). |
| POST | `/api/scene/goto` | `{scenarioId, itemId, stepIndex}` | Jumps to a scene cue: a step (linear) or a context (contextual), by index. |
| POST | `/api/volume` | `{scenarioId?, volume?, muted?}` | Sets the master level of one scenario, or of every active one when `scenarioId` is omitted. `volume` is `0..1`; `volume` and `muted` are independent, so sending only `muted` keeps the stored level. Replies `{ok, volume, muted}`. |

Scene cue fields:

- `/api/groups` → each item carries `sceneMode` (`"Linear"`, `"Contextual"`, or
  `null` for non-scene items) and `steps` (the ordered step names for linear
  scenes, or the context names for contextual scenes).
- `/api/state` → the active entry carries `sceneMode`, `sceneStepIndex`,
  `sceneStepName`, and `sceneStepCount`, unified across steps and contexts.

Volume fields:

- `/api/state` → each active entry carries `volume` and `muted`, and a top-level
  `volumes` array reports `{scenarioId, volume, muted}` for **every scenario that
  has a player**, playing or not — so a widget can show the right level for its
  selection before pressing play. A scenario never played yet has no player and
  is simply absent; the widget then shows the default, 100 %.
