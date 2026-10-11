# DiceAudio reskin — the shared vocabulary

Working notes for the MudBlazor→`da-` conversion. `DiceAudio/wwwroot/css/app.css` is the single source of
truth; this file only lists what is in it.

## Rules

1. **Do not change colours.** Every colour comes from a token. Never write a hex.
2. **Do not change layout.** Same elements, same order, same positions. Only the
   drawing of the controls changes. The exception is where a MudBlazor control
   carried its own structure (e.g. `MudStack`) — replace it with the plain flex
   container that was implied.
3. **Do not drop a feature.** Every handler, binding, `title`, `disabled`,
   `aria-*` and keyboard path that exists now must still exist afterwards.
4. **Keep the icons.** Where a `MudIcon` conveys something, it becomes a
   `<DaIcon Name="..." />` depicting the same thing — never a bare text label.

## Tokens

Surfaces `--s0`…`--s8` (ground → card `--s3` → panel surface `--s4` → inset
`--s5` → hover `--s6`). Borders `--b1`…`--b4`, operable edge `--ctl` / `--ctlhi`.
Text `--t1`…`--t4`. Accents `--a1` pink, `--a2` blue, `--a3` teal, `--a1t` pink
for small text, `--ink` dark ink for use on an accent fill. `--danger`, `--warn`.
Radii `--r-card|panel|tile|bar|btn|chip|pill`. `--brand` is the app gradient.

## Classes

| MudBlazor | replacement |
|---|---|
| `MudButton` | `<button type="button" class="da-btn">` — variants `da-btn-fill` (primary), `da-btn-out`, `da-btn-danger`, `da-btn-text` |
| `MudIconButton` | `<button type="button" class="da-ib" title aria-label>` + `<DaIcon>`; `da-ib-round` for a transport key |
| `MudIcon` | `<DaIcon Name="..." Size="18" />` |
| `MudText` | `h1.da-h1`, `h2.da-h2`, `p.da-sub`, or a plain span |
| `MudTextField` | `.da-field` wrapper + `label.da-flabel` + `input.da-inp` |
| `MudNumericField` | `.da-step` (− / input.da-num / +) |
| `MudSelect`/`MudSelectItem` | `<select class="da-inp">` + `<option>` |
| `MudCheckBox`/`MudSwitch` | `<label class="da-chk"><input type="checkbox">` |
| `MudChip` | `.da-chip` (+ a `cat-*` tint) or `.da-tag` |
| `MudDivider` | `<hr style="border:0;border-top:1px solid var(--b1);">` or nothing |
| `MudStack` | `div.da-irow` (row) / `div.da-stack` (column) |
| `MudCard`/`MudCardContent` | `.da-panel` |
| `MudSlider` | `input type=range class="da-range"` |
| `MudProgressLinear` | a 2-div bar: track `background:var(--s2)` + fill `background:var(--a1)` |
| `IDialogService.ShowMessageBoxAsync` | `<DaConfirm @ref="_confirm" />` + `await _confirm.AskAsync(title, msg, yes, cancel)` |

Layout helpers: `da-stack`, `da-irow`, `da-irow-wrap`, `da-panel`, `da-grid`,
`da-grid-3`, `da-msec` (section inside a modal), `da-mrow`, `da-note`,
`da-pill` (gradient section header), `da-empty`, `da-sub`, `da-meta`.

DiceAudio's own, unchanged: `da-card`, `da-hero`, `da-body`, `da-name`,
`da-tags`, `da-kind`, `da-row`, `da-list`, `da-transport`, `da-cmd-btn*`,
`da-range`, `da-track`, `da-time`, `da-selbox`, `da-pickmark`, `da-folder*`,
`da-view-toggles`, `da-viewbtn`, `da-playing-banner`, `da-k-music|amb|sfx`.

## Icons

`DaIcon.razor` holds the set. Audio names: `library-music music-note movie
auto-stories bookmarks volume-up volume-down volume-off skip-next skip-previous
fast-forward replay loop shuffle timer hourglass graphic-eq headphones mic tag
folder folder-add playlist queue link cloud-download filter fade speed dice`.
Shared names: `add remove close check check-circle delete edit save search
restore sync sort tune settings chevron-right chevron-down arrow-back
arrow-right circle-up circle-down launch open-in-browser public person groups
shield star pin favorite lightbulb help error access-time image text-snippet
dvr grid-view list folder-open download upload upload-file drag bookmark
bookmark-off github rocket play pause stop electric-bolt`.

`Size` (default 18–20), `Stroke`, `Filled`, `Class` (`da-i-accent`, `da-i-info`,
`da-i-danger`, `da-i-warn`, `da-i-muted`). If a needed icon is missing, add it
to `DaIcon.razor` as a stroked 24×24 path in `currentColor` — do not reach back
for a `MudIcon`.
