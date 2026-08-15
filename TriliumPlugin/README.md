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

## Widget state persistence

Each widget instance remembers its selected scenario item across note refreshes
and Trilium restarts (stored in `localStorage`).

The saved state also shows the selection's **scenario group / scenario** names on
a summary line under the dropdown.

## Scene items

Scene items are marked with a 🎬 in the item dropdown. The widget adapts its
controls to the scene's driving model:

- **Linear scenes** — a step dropdown plus an **⏭ Step** button. Advancing (or
  picking a step) walks through the ordered steps. Play starts at step 1.
- **Contextual scenes** — a row of **context buttons**, one per named state
  (e.g. *fireplace*, *crowded*, *brawl*). Click one to crossfade the whole scene
  into that state; the currently active context is highlighted. Play (▶) starts
  the scene in its configured **default context**.

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

Scene cue fields:

- `/api/groups` → each item carries `sceneMode` (`"Linear"`, `"Contextual"`, or
  `null` for non-scene items) and `steps` (the ordered step names for linear
  scenes, or the context names for contextual scenes).
- `/api/state` → the active entry carries `sceneMode`, `sceneStepIndex`,
  `sceneStepName`, and `sceneStepCount`, unified across steps and contexts.
