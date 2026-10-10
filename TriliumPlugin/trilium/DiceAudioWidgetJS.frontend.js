/*
 * DiceAudio remote-control widget for Trilium Notes.
 *
 * Vanilla JS, no dependencies. Talks to the DiceAudio control server
 * (enable it in DiceAudio → Settings → Remote control).
 *
 * Layout — two rows, wide rather than tall, splitting at different points:
 *
 *   ┌──────────────────────┬──────────────────────────┐
 *   │ icon group · scenario│ player · volume · status │
 *   ├───────────┬──────────┴──────────────────────────┤
 *   │ item      │ scene steps / contexts              │
 *   └───────────┴────────────────────────────────────┘
 *
 * The rows are independent: the top one breaks at the right edge of the scenario
 * dropdown, the bottom one at the right edge of the item, so the cue cell starts
 * immediately right of the item rather than waiting for the wider row above.
 * Group, scenario and item are all one width (--da-sel-w), with the item lined
 * up under the group. Picking a group narrows the scenario list, which would
 * otherwise be one flat pile of every scenario in the app. Cues wrap centred;
 * nothing scrolls. Below ~620 px the grid collapses to a single column.
 *
 * The selected scenario item of every widget instance is persisted in
 * localStorage, keyed by the hosting note and the widget's position on the
 * page (or by an explicit data-diceaudio-key attribute), so refreshing a
 * note restores each player's configuration.
 */
(function () {
    'use strict';

    // Official DiceAudio app icon (DiceAudio/Resources/AppIcon/diceaudio_logo.svg,
    // stripped of editor metadata). Rendered through an <img> data URI so its
    // gradient ids can never collide with anything else on the Trilium page.
    const LOGO_SVG = '<svg width="72.583" height="83.301" viewBox="0 0 72.583008 83.301239" version="1.1" xml:space="preserve" xmlns:xlink="http://www.w3.org/1999/xlink" xmlns="http://www.w3.org/2000/svg"><defs><linearGradient id="linearGradient4114-5"><stop style="stop-color:#00baeb;stop-opacity:1;" offset="0" /><stop style="stop-color:#fb007a;stop-opacity:0.77674413;" offset="1" /></linearGradient><linearGradient id="linearGradient4114-1"><stop style="stop-color:#2ad4ff;stop-opacity:1;" offset="0" /><stop style="stop-color:#ff2a8f;stop-opacity:0;" offset="1" /></linearGradient><linearGradient id="linearGradient4114-3"><stop style="stop-color:#006682;stop-opacity:1;" offset="0" /><stop style="stop-color:#ff258c;stop-opacity:0.44651163;" offset="1" /></linearGradient><linearGradient id="linearGradient4114-4"><stop style="stop-color:#2ad4ff;stop-opacity:1;" offset="0" /><stop style="stop-color:#ff4896;stop-opacity:1;" offset="1" /></linearGradient><linearGradient xlink:href="#linearGradient4114-3" id="linearGradient4116" x1="62.26054" y1="55.193279" x2="96.902527" y2="87.130814" gradientUnits="userSpaceOnUse" /><linearGradient xlink:href="#linearGradient4114-3" id="linearGradient4253" x1="69.654076" y1="35.192711" x2="96.90255" y2="51.319912" gradientUnits="userSpaceOnUse" /><linearGradient xlink:href="#linearGradient4114-5" id="linearGradient4261" x1="74.577705" y1="49.67057" x2="119.22735" y2="88.780319" gradientUnits="userSpaceOnUse" /><linearGradient xlink:href="#linearGradient4114-1" id="linearGradient4274" x1="96.902527" y1="87.130814" x2="131.54454" y2="115.19498" gradientUnits="userSpaceOnUse" /><linearGradient xlink:href="#linearGradient4114-1" id="linearGradient4282" x1="117.57797" y1="55.193283" x2="131.54454" y2="95.19442" gradientUnits="userSpaceOnUse" /><linearGradient xlink:href="#linearGradient4114-3" id="linearGradient4295" x1="96.902527" y1="35.192711" x2="131.54454" y2="55.193283" gradientUnits="userSpaceOnUse" /><linearGradient xlink:href="#linearGradient4114-1" id="linearGradient4303" x1="96.902527" y1="69.225365" x2="131.54453" y2="69.225365" gradientUnits="userSpaceOnUse" /><linearGradient xlink:href="#linearGradient4114-3" id="linearGradient4311" x1="76.227089" y1="87.130814" x2="117.57796" y2="115.19499" gradientUnits="userSpaceOnUse" /><linearGradient xlink:href="#linearGradient4114-1" id="linearGradient4319" x1="62.260521" y1="87.130814" x2="96.902534" y2="115.19498" gradientUnits="userSpaceOnUse" /><linearGradient xlink:href="#linearGradient4114-3" id="linearGradient4327" x1="62.260536" y1="55.193283" x2="76.227104" y2="95.19442" gradientUnits="userSpaceOnUse" /><clipPath id="a"><rect width="64" height="64" x="0" y="0" /></clipPath><linearGradient xlink:href="#linearGradient4114-4" id="linearGradient5811" x1="51.229" y1="49.551483" x2="24.116501" y2="13.3275" gradientUnits="userSpaceOnUse" /><linearGradient xlink:href="#linearGradient4114-4" id="linearGradient2" gradientUnits="userSpaceOnUse" x1="51.229" y1="49.551483" x2="24.116501" y2="13.3275" /><linearGradient xlink:href="#linearGradient4114-4" id="linearGradient3" gradientUnits="userSpaceOnUse" x1="51.229" y1="49.551483" x2="24.116501" y2="13.3275" /><linearGradient xlink:href="#linearGradient4114-4" id="linearGradient4" gradientUnits="userSpaceOnUse" x1="51.229" y1="49.551483" x2="24.116501" y2="13.3275" /><linearGradient xlink:href="#linearGradient4114-4" id="linearGradient5" gradientUnits="userSpaceOnUse" x1="51.229" y1="49.551483" x2="24.116501" y2="13.3275" /></defs><g transform="translate(-60.611028,-33.543231)"><path style="fill:#000000;stroke:#008080;stroke-width:3.29901;stroke-linejoin:round;stroke-opacity:0" d="m 131.54454,95.194416 -34.642006,20.000564 -34.642,-20.000564 0,-40.001137 34.642001,-20.000568 34.642005,20.000568 z" /><path style="fill:url(#linearGradient4261);fill-opacity:1;stroke:#008080;stroke-width:3.29901;stroke-linejoin:round;stroke-opacity:0" d="m 117.57796,87.130816 -41.350868,-10e-7 20.675435,-35.810903 z" /><path style="fill:url(#linearGradient4303);fill-opacity:1;stroke:none;stroke-width:2;stroke-dasharray:none;stroke-opacity:0" d="m 96.902525,51.319911 34.642005,3.873368 -13.96657,31.937536 z" /><path style="fill:url(#linearGradient4311);fill-opacity:1;stroke-width:0.264583" d="M 117.57796,87.130815 96.902525,115.19499 76.227093,87.130815 Z" /><path style="fill:url(#linearGradient4116);fill-opacity:1;stroke-width:0.264583" d="M 76.227093,87.130814 62.260522,55.193275 96.902526,51.319913 Z" /><path style="fill:url(#linearGradient4295);fill-opacity:1;stroke-width:0.264583" d="m 96.902525,51.319911 8e-6,-16.127201 34.641997,20.000569 z" /><path style="fill:url(#linearGradient4274);fill-opacity:1;stroke-width:0.264583" d="m 117.57798,87.130807 13.96656,8.063607 -34.642001,20.000566 z" /><path style="fill:url(#linearGradient4327);fill-opacity:1;stroke-width:0.264583" d="m 76.227103,87.130828 -13.966568,8.063592 2e-6,-40.001136 z" /><path style="fill:url(#linearGradient4253);fill-opacity:1;stroke-width:0.264583" d="m 96.902546,51.319911 -8e-6,-16.127201 -34.641997,20.000569 z" /><path style="fill:url(#linearGradient4282);fill-opacity:1;stroke-width:0.264583" d="m 117.57797,87.130825 13.96657,8.063593 -1e-5,-40.001134 z" /><path style="fill:url(#linearGradient4319);fill-opacity:1;stroke-width:0.264583" d="M 76.227093,87.13081 62.260532,95.194418 96.902535,115.19498 Z" /><g style="isolation:isolate;fill:url(#linearGradient5811);fill-opacity:1" transform="matrix(0.35277778,0,0,0.35277778,85.50604,63.905664)"><g clip-path="url(#a)" style="fill:url(#linearGradient5);fill-opacity:1"><path d="m 6.667,44.207 h 9.767 c 0.674,0 1.608,0.387 2.084,0.863 l 11.197,11.198 c 1.43,1.429 2.59,0.948 2.59,-1.073 V 8.801 c 0,-2.021 -1.16,-2.502 -2.59,-1.072 L 18.518,18.926 c -0.476,0.477 -1.41,0.863 -2.084,0.863 H 6.667 c -2.022,0 -3.663,1.642 -3.663,3.663 v 17.092 c 0,2.022 1.641,3.663 3.663,3.663 z" style="fill:url(#linearGradient2);fill-opacity:1" /><path fill-rule="evenodd" d="m 56.112,32 v 0 0 c 0,0 0,0 0,0 0,-5.518 -1.746,-10.902 -4.993,-15.371 C 47.932,12.234 43.476,8.901 38.361,7.082 L 37.204,6.672 C 36.569,6.447 36.237,5.748 36.462,5.114 L 37.28,2.812 c 0.226,-0.635 0.925,-0.967 1.559,-0.741 l 1.146,0.408 c 6.055,2.149 11.317,6.08 15.09,11.281 3.845,5.299 5.921,11.684 5.921,18.24 0,0 0,0 0,0 0,0 0,0 0,0 0,6.544 -2.076,12.929 -5.921,18.228 -3.773,5.201 -9.035,9.144 -15.09,11.293 l -1.146,0.408 c -0.634,0.226 -1.333,-0.106 -1.559,-0.741 l -0.818,-2.302 c -0.225,-0.634 0.107,-1.333 0.742,-1.558 l 1.157,-0.41 c 5.115,-1.819 9.571,-5.152 12.758,-9.547 3.247,-4.469 4.993,-9.853 4.993,-15.371 z m 4.884,0 z m 0,0" style="fill:url(#linearGradient3);fill-opacity:1" /><path d="m 38.08,19.486 -1.095,-0.55 c -0.602,-0.303 -0.845,-1.038 -0.542,-1.64 l 1.101,-2.189 c 0.302,-0.602 1.036,-0.844 1.638,-0.54 l 1.083,0.548 c 3.187,1.6 5.897,3.98 7.899,6.923 1.978,2.942 3.053,6.409 3.065,9.962 -0.012,3.553 -1.087,7.008 -3.065,9.95 -2.002,2.942 -4.712,5.323 -7.899,6.923 l -1.083,0.547 c -0.602,0.304 -1.336,0.063 -1.639,-0.539 l -1.099,-2.178 c -0.303,-0.602 -0.061,-1.336 0.541,-1.639 l 1.095,-0.55 c 2.43,-1.221 4.505,-3.052 6.043,-5.299 1.429,-2.136 2.21,-4.639 2.222,-7.215 -0.012,-2.576 -0.793,-5.091 -2.222,-7.215 -1.538,-2.259 -3.613,-4.078 -6.043,-5.299 z" style="fill:url(#linearGradient4);fill-opacity:1" /></g></g></g></svg>';
    const LOGO_URI = 'data:image/svg+xml;utf8,' + encodeURIComponent(LOGO_SVG);

    const STYLE_ID = 'diceaudio-widget-style';

    // Two palettes; which one applies is decided per instance from the
    // brightness of the note background, so the widget follows the Trilium theme.
    const CSS = `
.da-widget {
  --da-bg: #191930; --da-bg-2: #12121f; --da-bg-3: #26264c;
  --da-line: #2e2e50; --da-line-2: #3d3d68;
  --da-txt: #dfe3ee; --da-txt-dim: #8b93ab;
  --da-accent: #23a6ab; --da-accent-2: #35d3d9; --da-accent-bg: rgba(35,166,171,.16);
  --da-play: #4ecb71; --da-stop: #ef5350; --da-warn: #f0a03c;
  --da-shadow: 0 2px 10px rgba(0,0,0,.35);
  /* The app's playing banner (da-playing-banner in wwwroot/css/app.css) at a
     third of its opacity — the same signal, quiet enough to sit in a note. */
  --da-live: linear-gradient(-45deg, rgba(238,119,82,.22), rgba(231,60,126,.18),
                                     rgba(35,166,213,.18), rgba(35,166,171,.22));
  font-family: -apple-system, "Segoe UI", Roboto, sans-serif;
  font-size: 13px; color: var(--da-txt); container-type: inline-size;
  -webkit-font-smoothing: antialiased;
}
.da-widget[data-da-theme="light"] {
  --da-bg: #ffffff; --da-bg-2: #f2f4f9; --da-bg-3: #e4e9f4;
  --da-line: #dde1ec; --da-line-2: #c6ccdd;
  --da-txt: #262b39; --da-txt-dim: #6b7284;
  --da-accent: #0f8388; --da-accent-2: #0d6f74; --da-accent-bg: rgba(15,131,136,.12);
  --da-play: #2e9e51; --da-stop: #d9433f; --da-warn: #b76b12;
  --da-shadow: 0 1px 4px rgba(20,30,60,.10);
  --da-live: linear-gradient(-45deg, rgba(238,119,82,.16), rgba(231,60,126,.12),
                                     rgba(35,166,213,.12), rgba(35,166,171,.16));
}
.da-widget *, .da-widget *::before, .da-widget *::after { box-sizing: border-box; }
.da-widget button, .da-widget select, .da-widget input { font-family: inherit; }

/* ── playing state: the same sliding gradient the app runs behind a playing
      scenario item, across the whole widget and dimmer (see da-gradient-shift
      in the app's app.css — identical keyframes and 3 s cadence) ── */
@keyframes da-gradient-shift {
  0%   { background-position: 0%   50%; }
  50%  { background-position: 100% 50%; }
  100% { background-position: 0%   50%; }
}
.da-grid.da-live {
  background: var(--da-live), var(--da-bg);
  background-size: 400% 400%, auto;
  animation: da-gradient-shift 3s ease infinite;
}
@media (prefers-reduced-motion: reduce) {
  .da-grid.da-live { animation: none; }   /* keep the tint, drop the movement */
}

/* ── the grid ──
   Three columns carrying two rows that break in different places: the top row at
   the right edge of the scenario dropdown, the bottom row at the right edge of
   the item. Neither row is held to the other's width, and the cue cell gets
   everything right of the item.

     ┌──────────────────────┬──────────────────────────┐
     │ icon group · scenario│ player · volume · status │
     ├───────────┬──────────┴──────────────────────────┤
     │ item      │ scene steps / contexts              │
     └───────────┴────────────────────────────────────┘
       col 1       col 2      col 3                                */
.da-grid {
  --da-sel-w: 130px;    /* one width for group, scenario and item alike */
  --da-gutter: 24px;    /* the logo, and the pad that puts the item under the group */
  --da-gap: 9px;        /* between controls inside a row */
  --da-col-gap: 14px;   /* between the dropdowns and what sits right of them */
  display: grid;
  grid-template-columns:
    calc(var(--da-gutter) + var(--da-gap) + var(--da-sel-w))   /* ends at the item's right edge */
    calc(var(--da-gap) + var(--da-sel-w) - var(--da-col-gap))  /* ends at the scenario's right edge */
    minmax(0, 1fr);
  grid-template-rows: auto auto;
  column-gap: var(--da-col-gap); row-gap: 8px;
  background: var(--da-bg); border: 1px solid var(--da-line); border-radius: 14px;
  padding: 9px 12px; box-shadow: var(--da-shadow);
}
.da-row { display: flex; align-items: center; gap: var(--da-gap); min-width: 0; }
.da-sel-row { grid-area: 1 / 1 / span 1 / span 2; align-self: center; }
.da-item-row { grid-area: 2 / 1; align-self: start; }
.da-pad { width: var(--da-gutter); flex: none; }
/* Wraps rather than overflows when a narrow note leaves column 3 short. */
.da-top { grid-area: 1 / 3; display: flex; align-items: center; gap: var(--da-gap);
  min-width: 0; justify-content: center; flex-wrap: wrap; }
.da-cues { grid-area: 2 / 2 / span 1 / span 2; display: flex; align-items: center;
  justify-content: center; gap: 6px; flex-wrap: wrap; min-width: 0; }
@container (max-width: 620px) {
  .da-grid { grid-template-columns: 1fr; }
  .da-sel-row, .da-item-row, .da-top, .da-cues { grid-area: auto; }
}

.da-logo { width: var(--da-gutter); height: 24px; flex: none; display: block; }

/* ── selects ── */
.da-sel { appearance: none; -webkit-appearance: none; width: 100%; min-width: 0; height: 30px;
  padding: 0 22px 0 9px; font-size: 12.5px; cursor: pointer; text-overflow: ellipsis;
  color: var(--da-txt); border: 1px solid var(--da-line-2); border-radius: 8px;
  background: var(--da-bg-2) url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'><path d='M7 10l5 5 5-5z' fill='%238b93ab'/></svg>") no-repeat right 2px center / 16px; }
.da-sel:focus { outline: none; border-color: var(--da-accent); }
/* All three are --da-sel-w wide, which is also what the grid columns are built
   from, so the rows line up without either one stretching to match the other.
   They may shrink, never grow, so a cramped note narrows them evenly. */
.da-group, .da-scenario, .da-item { flex: 0 1 var(--da-sel-w); }
.da-group, .da-scenario { color: var(--da-txt-dim); font-size: 12px; }
.da-item { font-weight: 600; }

/* ── transport ── */
.da-btn { display: inline-flex; align-items: center; justify-content: center;
  height: 30px; min-width: 30px; padding: 0 7px; cursor: pointer;
  background: var(--da-bg-2); color: var(--da-txt);
  border: 1px solid var(--da-line-2); border-radius: 8px;
  transition: background .13s, border-color .13s, transform .07s; }
.da-btn:hover { background: var(--da-bg-3); border-color: var(--da-accent); }
.da-btn:active { transform: translateY(1px); }
.da-btn svg { width: 16px; height: 16px; fill: currentColor; }
.da-btn.da-stop-btn { color: var(--da-stop); }
.da-btn.da-primary { background: linear-gradient(180deg, var(--da-accent) 0%, var(--da-accent-2) 130%);
  border-color: transparent; color: #04141b; }
.da-btn.da-primary:hover { filter: brightness(1.1); }
.da-btn.da-ghost { background: transparent; border-color: transparent; color: var(--da-txt-dim); }
.da-btn.da-ghost:hover { background: var(--da-bg-3); color: var(--da-txt); border-color: transparent; }
.da-player { display: flex; align-items: center; gap: 4px; padding: 3px; border-radius: 11px;
  background: var(--da-bg-2); border: 1px solid var(--da-line); }
.da-player .da-btn { background: transparent; border-color: transparent; }
.da-player .da-btn:hover { background: var(--da-bg-3); }
.da-player .da-btn.da-primary { background: linear-gradient(180deg, var(--da-accent) 0%, var(--da-accent-2) 130%); }

/* ── volume ── */
.da-vol { display: flex; align-items: center; gap: 7px; min-width: 0; }
.da-slider { -webkit-appearance: none; appearance: none; background: transparent;
  cursor: pointer; height: 16px; min-width: 54px; margin: 0; flex: 0 1 96px; }
.da-slider::-webkit-slider-runnable-track { height: 4px; border-radius: 99px;
  background: linear-gradient(90deg, var(--da-accent) var(--da-p, 100%), var(--da-line-2) var(--da-p, 100%)); }
.da-slider::-webkit-slider-thumb { -webkit-appearance: none; width: 12px; height: 12px;
  border-radius: 50%; background: var(--da-txt); border: 1px solid var(--da-line-2);
  margin-top: -4px; box-shadow: 0 1px 3px rgba(0,0,0,.4); transition: transform .1s; }
.da-slider:hover::-webkit-slider-thumb { transform: scale(1.15); }
.da-slider:active::-webkit-slider-thumb { background: var(--da-accent-2); }
.da-slider::-moz-range-track { height: 4px; border-radius: 99px; background: var(--da-line-2); }
.da-slider::-moz-range-progress { height: 4px; border-radius: 99px; background: var(--da-accent); }
.da-slider::-moz-range-thumb { width: 11px; height: 11px; border-radius: 50%;
  background: var(--da-txt); border: 1px solid var(--da-line-2); }
.da-slider:focus { outline: none; }
.da-vol.da-muted .da-slider::-webkit-slider-runnable-track { background: var(--da-line-2); }
.da-vol.da-muted .da-slider::-moz-range-progress { background: var(--da-line-2); }
.da-vol.da-muted .da-speaker { color: var(--da-stop); }

/* ── cue chips ── */
.da-cue { border: 1px solid var(--da-line-2); background: var(--da-bg-2); color: var(--da-txt);
  border-radius: 999px; padding: 4px 12px; font-size: 12px; cursor: pointer;
  display: inline-flex; align-items: center; gap: 5px; white-space: nowrap; flex: none;
  transition: background .13s, border-color .13s, color .13s; }
.da-cue:hover { background: var(--da-bg-3); border-color: var(--da-accent); }
.da-cue .da-num { font-size: 10px; opacity: .5; font-variant-numeric: tabular-nums; }
.da-cue.da-on { background: var(--da-accent-bg); border-color: var(--da-accent);
  color: var(--da-accent-2); font-weight: 700; box-shadow: inset 0 0 0 1px var(--da-accent); }
.da-cue.da-done { opacity: .45; }

/* ── status ── */
.da-eq { display: inline-flex; align-items: flex-end; gap: 2px; height: 12px; flex: none; }
.da-eq i { width: 2.5px; background: var(--da-play); border-radius: 1px;
  animation: da-eq .9s ease-in-out infinite; }
.da-eq i:nth-child(1) { height: 40%; animation-delay: -.2s; }
.da-eq i:nth-child(2) { height: 100%; animation-delay: -.55s; }
.da-eq i:nth-child(3) { height: 65%; animation-delay: -.35s; }
.da-eq i:nth-child(4) { height: 85%; }
@keyframes da-eq { 0%, 100% { transform: scaleY(.3); } 50% { transform: scaleY(1); } }
.da-dot { width: 7px; height: 7px; border-radius: 50%; background: var(--da-line-2); flex: none; }
.da-dot.da-offline { background: var(--da-warn); }
.da-hint { font-size: 11.5px; color: var(--da-txt-dim); min-width: 0; overflow: hidden;
  text-overflow: ellipsis; white-space: nowrap; text-align: center; }
.da-hint.da-error { color: var(--da-warn); }
`;

    const ICON = {
        play: '<svg viewBox="0 0 24 24"><path d="M8 5.5v13l10.5-6.5z"/></svg>',
        stop: '<svg viewBox="0 0 24 24"><rect x="6.5" y="6.5" width="11" height="11" rx="1.6"/></svg>',
        prev: '<svg viewBox="0 0 24 24"><rect x="5" y="6" width="2.2" height="12" rx="1"/><path d="M19 6v12l-9.5-6z"/></svg>',
        next: '<svg viewBox="0 0 24 24"><rect x="16.8" y="6" width="2.2" height="12" rx="1"/><path d="M5 6v12l9.5-6z"/></svg>',
        pause:'<svg viewBox="0 0 24 24"><path d="M7 5.5h3.4v13H7zm6.6 0H17v13h-3.4z"/></svg>',
        step: '<svg viewBox="0 0 24 24"><path d="M4 10.6h9V6.4l7 5.6-7 5.6v-4.2H4z"/></svg>',
        vol:  '<svg viewBox="0 0 24 24"><path d="M4 9.5h3.2L12 5.4v13.2L7.2 14.5H4z"/><path d="M14.6 8.6a1 1 0 0 1 1.4.1 5.3 5.3 0 0 1 0 6.6 1 1 0 1 1-1.5-1.3 3.3 3.3 0 0 0 0-4 1 1 0 0 1 .1-1.4z"/><path d="M17.4 5.8a1 1 0 0 1 1.4 0 9 9 0 0 1 0 12.4 1 1 0 1 1-1.4-1.4 7 7 0 0 0 0-9.6 1 1 0 0 1 0-1.4z"/></svg>',
        vol1: '<svg viewBox="0 0 24 24"><path d="M4 9.5h3.2L12 5.4v13.2L7.2 14.5H4z"/><path d="M14.6 8.6a1 1 0 0 1 1.4.1 5.3 5.3 0 0 1 0 6.6 1 1 0 1 1-1.5-1.3 3.3 3.3 0 0 0 0-4 1 1 0 0 1 .1-1.4z"/></svg>',
        mute: '<svg viewBox="0 0 24 24"><path d="M4 9.5h3.2L12 5.4v13.2L7.2 14.5H4z"/><path d="M15.3 9.3 17 11l1.7-1.7 1.3 1.3L18.3 12 20 13.7l-1.3 1.3L17 13.3 15.3 15 14 13.7 15.7 12 14 10.6z"/></svg>',
    };

    function mount(root, options) {
        const port = (options && options.port) || 8765;
        const base = 'http://localhost:' + port;
        const storageKey = computeStorageKey(root, options);

        ensureStyle();

        // ── UI skeleton ─────────────────────────────────────────────────────
        root.innerHTML = '';
        root.classList.add('da-widget');
        root.setAttribute('data-da-theme', isDarkBackground(root) ? 'dark' : 'light');

        const grid = el('div', 'da-grid');
        root.appendChild(grid);

        // top row — logo, group, scenario
        const scenarioRow = el('div', 'da-row da-sel-row');
        const logo = el('img', 'da-logo');
        logo.src = LOGO_URI;
        logo.alt = 'DiceAudio';
        logo.title = 'DiceAudio remote control';
        const groupSelect = el('select', 'da-sel da-group');
        groupSelect.title = 'Scenario group';
        const scenarioSelect = el('select', 'da-sel da-scenario');
        scenarioSelect.title = 'Scenario';
        scenarioRow.appendChild(logo);
        scenarioRow.appendChild(groupSelect);
        scenarioRow.appendChild(scenarioSelect);

        // second row — the item, lined up under the group; the cue cell picks up
        // immediately right of it
        const itemRow = el('div', 'da-row da-item-row');
        const itemSelect = el('select', 'da-sel da-item');
        itemSelect.title = 'Item';
        itemRow.appendChild(el('span', 'da-pad'));
        itemRow.appendChild(itemSelect);

        grid.appendChild(scenarioRow);
        grid.appendChild(itemRow);

        // top right — transport, volume, status
        const top = el('div', 'da-top');
        const player = el('div', 'da-player');
        const btnPrev = button('prev', 'Previous item');
        const btnPlay = button('play', 'Play', 'da-primary');
        const btnStop = button('stop', 'Stop', 'da-stop-btn');
        const btnNext = button('next', 'Next item');
        [btnPrev, btnPlay, btnStop, btnNext].forEach(b => player.appendChild(b));

        const volBox = el('div', 'da-vol');
        const btnSpeaker = button('vol', 'Mute', 'da-ghost da-speaker');
        const slider = document.createElement('input');
        slider.type = 'range';
        slider.className = 'da-slider';
        slider.min = 0;
        slider.max = 100;
        slider.value = 100;
        volBox.appendChild(btnSpeaker);
        volBox.appendChild(slider);

        const status = el('span', 'da-dot');
        status.title = 'Nothing playing';

        top.appendChild(player);
        top.appendChild(volBox);
        top.appendChild(status);
        grid.appendChild(top);

        // second row, right of the item — scene cues, or what is playing
        const cueCell = el('div', 'da-cues');
        grid.appendChild(cueCell);

        // ── Data / state ────────────────────────────────────────────────────
        // The API shape, kept as it arrives: groups own scenarios, scenarios own items.
        let groups = [];         // { id, name, scenarios: [{ id, name, items: [{ id, name, type, mode, steps }] }] }
        let cueButtons = [];     // rendered chips of the selected scene
        let disposed = false;
        let connected = true;
        let lastActive = [];     // active entries from the last /api/state poll

        // Master level of the selected scenario. Echoed by /api/state, but the
        // poll must not fight the slider while it is being dragged, so local
        // edits win for a moment (see volumeHeldUntil).
        let volume = 1.0;
        let muted = false;
        let volumeHeldUntil = 0;
        let volumeSendTimer = null;

        function selectedGroup() {
            return groups[groupSelect.selectedIndex] || null;
        }

        function selectedScenario() {
            const group = selectedGroup();
            if (!group) return null;
            return group.scenarios[scenarioSelect.selectedIndex] || null;
        }

        function playingSelectedItem() {
            const scenario = selectedScenario();
            const item = selectedItem();
            if (!scenario || !item) return false;
            const entry = lastActive.find(function (a) { return a.scenarioId === scenario.id; });
            return !!entry && entry.currentItemId === item.id;
        }

        function selectedItem() {
            const scenario = selectedScenario();
            if (!scenario) return null;
            return scenario.items[itemSelect.selectedIndex] || null;
        }

        // ── Rendering ───────────────────────────────────────────────────────

        function renderGroups() {
            groupSelect.innerHTML = '';
            groups.forEach(function (group) {
                groupSelect.appendChild(option(group.name));
            });
        }

        function renderScenarios() {
            const group = selectedGroup();
            scenarioSelect.innerHTML = '';
            if (!group) return;
            group.scenarios.forEach(function (scenario) {
                scenarioSelect.appendChild(option(scenario.name));
            });
        }

        function renderItems() {
            const scenario = selectedScenario();
            itemSelect.innerHTML = '';
            if (!scenario) return;
            scenario.items.forEach(function (item) {
                itemSelect.appendChild(option(item.name));
            });
        }

        // All three share one narrow column, so any of the names can end up
        // ellipsised; the tooltip carries the whole thing.
        function renderSelectTitles() {
            const group = selectedGroup();
            const scenario = selectedScenario();
            const item = selectedItem();
            groupSelect.title = group ? 'Scenario group \u2014 ' + group.name : 'Scenario group';
            scenarioSelect.title = scenario ? 'Scenario \u2014 ' + scenario.name : 'Scenario';
            itemSelect.title = item ? 'Item \u2014 ' + item.name : 'Item';
        }

        // Cue chips for the selected item: contexts (contextual scenes) or
        // numbered steps plus an "advance" button (linear scenes). Non-scene
        // items get the now-playing line instead, so the cell is never empty.
        function renderCues() {
            const item = selectedItem();
            const isScene = !!item && item.type === 'Scene';
            const isLinear = isScene && item.mode !== 'Contextual';
            const cues = (item && item.steps) || [];

            cueButtons = [];
            cueCell.innerHTML = '';

            if (!isScene || !cues.length) {
                cueCell.appendChild(el('span', 'da-hint'));
                updateHint();
                return;
            }

            cues.forEach(function (name, index) {
                const chip = el('button', 'da-cue');
                if (isLinear) chip.appendChild(el('span', 'da-num', String(index + 1)));
                chip.appendChild(document.createTextNode(name));
                chip.title = isLinear ? 'Jump to step "' + name + '"'
                                      : 'Switch to context "' + name + '"';
                chip.addEventListener('click', function () {
                    const current = selectedItem();
                    const scenario = selectedScenario();
                    if (current && scenario) post('/api/scene/goto', {
                        scenarioId: scenario.id, itemId: current.id, stepIndex: index
                    });
                });
                cueButtons.push(chip);
                cueCell.appendChild(chip);
            });

            if (isLinear) {
                const advance = button('step', 'Advance to the next step', 'da-ghost');
                advance.addEventListener('click', function () {
                    const current = selectedItem();
                    const scenario = selectedScenario();
                    if (current && scenario) post('/api/scene/advance', {
                        scenarioId: scenario.id, itemId: current.id
                    });
                });
                cueCell.appendChild(advance);
            }
        }

        // The hint line only exists for non-scene items (scenes fill the cell
        // with chips); it doubles as the place to report a dead connection.
        function updateHint(text, isError) {
            const hint = cueCell.querySelector('.da-hint');
            if (!hint) return;
            hint.textContent = text || (connected ? 'Nothing playing' : 'DiceAudio not reachable');
            hint.title = hint.textContent;
            hint.classList.toggle('da-error', !!isError || !connected);
        }

        // Play/pause glyph and the animated background are one state: the
        // selected item is the one playing.
        function renderPlaybackState() {
            const playing = playingSelectedItem();
            btnPlay.innerHTML = playing ? ICON.pause : ICON.play;
            btnPlay.title = playing ? 'Pause' : 'Play';
            btnPlay.classList.toggle('da-primary', !playing);
            grid.classList.toggle('da-live', playing);
        }

        function renderVolume() {
            slider.value = Math.round(volume * 100);
            slider.style.setProperty('--da-p', (muted ? 0 : Math.round(volume * 100)) + '%');
            slider.title = muted ? 'Muted' : 'Volume ' + Math.round(volume * 100) + '%';
            btnSpeaker.innerHTML = (muted || volume === 0) ? ICON.mute
                                 : (volume < 0.45 ? ICON.vol1 : ICON.vol);
            btnSpeaker.title = muted ? 'Unmute' : 'Mute';
            volBox.classList.toggle('da-muted', muted);
        }

        // Highlights the cue actually running, but only when the selected item is
        // the one playing — otherwise the chips would lie about another item.
        function updateCueHighlight(activeItemId, activeIndex, isLinear) {
            const item = selectedItem();
            const mine = !!item && item.id === activeItemId;
            cueButtons.forEach(function (chip, index) {
                const on = mine && index === activeIndex;
                chip.classList.toggle('da-on', on);
                // Linear scenes: dim the steps already gone through.
                chip.classList.toggle('da-done', mine && isLinear && index < activeIndex);
            });
        }

        function setStatus(kind, text) {
            status.className = kind === 'playing' ? 'da-eq'
                             : (kind === 'offline' ? 'da-dot da-offline' : 'da-dot');
            status.innerHTML = kind === 'playing' ? '<i></i><i></i><i></i><i></i>' : '';
            status.title = text;
        }

        // ── Selection persistence ───────────────────────────────────────────

        function saveSelection() {
            const group = selectedGroup();
            const scenario = selectedScenario();
            const item = selectedItem();
            if (!group || !scenario) return;
            try {
                localStorage.setItem(storageKey, JSON.stringify({
                    groupId: group.id, scenarioId: scenario.id, itemId: item ? item.id : null
                }));
            } catch (e) { /* storage unavailable — selection just won't persist */ }
        }

        function restoreSelection() {
            let saved = null;
            try { saved = JSON.parse(localStorage.getItem(storageKey) || 'null'); }
            catch (e) { }
            if (!saved) return;

            // Selections written before the group dropdown existed name only the
            // scenario; find the group holding it so those notes keep their player.
            let groupIndex = groups.findIndex(function (group) {
                return group.id === saved.groupId;
            });
            if (groupIndex < 0) {
                groupIndex = groups.findIndex(function (group) {
                    return group.scenarios.some(function (scenario) {
                        return scenario.id === saved.scenarioId;
                    });
                });
            }
            if (groupIndex < 0) return;
            groupSelect.selectedIndex = groupIndex;
            renderScenarios();

            const scenarios = groups[groupIndex].scenarios;
            const scenarioIndex = scenarios.findIndex(function (scenario) {
                return scenario.id === saved.scenarioId;
            });
            // A saved scenario that is gone keeps the group and falls back to its
            // first scenario, rather than back to the first group.
            if (scenarioIndex >= 0) scenarioSelect.selectedIndex = scenarioIndex;
            renderItems();
            if (scenarioIndex < 0) return;

            const itemIndex = scenarios[scenarioIndex].items.findIndex(function (item) {
                return item.id === saved.itemId;
            });
            if (itemIndex >= 0) itemSelect.selectedIndex = itemIndex;
        }

        // ── Server ──────────────────────────────────────────────────────────

        // The group level is kept as the first step of the picker: a library of
        // many groups is unreadable as one flat scenario list. Scenarios with no
        // items, and groups left with no scenarios, are dropped — there is
        // nothing to play in them.
        async function loadGroups() {
            try {
                const res = await fetch(base + '/api/groups');
                const data = await res.json();
                groups = (data.groups || []).map(function (group) {
                    return {
                        id: group.id,
                        name: group.name,
                        scenarios: (group.scenarios || []).map(function (scenario) {
                            return {
                                id: scenario.id,
                                name: scenario.name,
                                items: (scenario.items || []).map(function (item) {
                                    return {
                                        id: item.id, name: item.name, type: item.type,
                                        mode: item.sceneMode, steps: item.steps
                                    };
                                })
                            };
                        }).filter(function (scenario) { return scenario.items.length; })
                    };
                }).filter(function (group) { return group.scenarios.length; });

                connected = true;
                renderGroups();
                renderScenarios();
                renderItems();
                restoreSelection();
                renderSelectTitles();
                renderCues();
                if (!groups.length) updateHint('No scenario items found in DiceAudio.', true);
            } catch (e) {
                connected = false;
                setStatus('offline', 'DiceAudio not reachable on ' + base);
                renderCues();
                updateHint('DiceAudio not reachable on ' + base + ' — is the app running with the control server enabled?', true);
            }
        }

        async function post(path, payload) {
            try {
                await fetch(base + path, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload || {})
                });
            } catch (e) {
                connected = false;
                setStatus('offline', 'Request failed — is DiceAudio running?');
            }
        }

        // Volume is streamed while dragging; throttled so a drag does not open one
        // request per pixel, with a trailing send so the final value always lands.
        function sendVolume() {
            const scenario = selectedScenario();
            if (!scenario) return;
            if (volumeSendTimer) return;
            volumeSendTimer = setTimeout(function () {
                volumeSendTimer = null;
                const target = selectedScenario();
                if (target) post('/api/volume', {
                    scenarioId: target.id, volume: volume, muted: muted
                });
            }, 120);
        }

        async function poll() {
            if (disposed) return;
            try {
                const res = await fetch(base + '/api/state');
                const data = await res.json();
                connected = true;

                const scenario = selectedScenario();
                const active = lastActive = (data.active || []);
                // Prefer the entry of the scenario this widget points at; fall back
                // to whatever plays, so the widget still reports app-wide activity.
                const mine = scenario ? active.find(function (a) { return a.scenarioId === scenario.id; }) : null;
                const shown = mine || active[0] || null;

                if (shown) {
                    let text = '♪ ' + shown.scenarioName + ' — ' + (shown.currentItemName || '');
                    if (shown.sceneStepName != null) {
                        text += shown.sceneMode === 'Contextual'
                            ? '  [context: ' + shown.sceneStepName + ']'
                            : '  [step ' + (shown.sceneStepIndex + 1) +
                              (shown.sceneStepCount ? '/' + shown.sceneStepCount : '') +
                              ': ' + shown.sceneStepName + ']';
                    }
                    setStatus('playing', text);
                    // Name the scenario when the sound belongs to another one, so
                    // the line is never mistaken for the current selection.
                    updateHint('♪ ' + (mine ? (shown.currentItemName || shown.scenarioName)
                                            : shown.scenarioName + ' — ' + (shown.currentItemName || '')));
                } else {
                    setStatus('idle', 'Nothing playing');
                    updateHint();
                }

                renderPlaybackState();
                updateCueHighlight(
                    mine ? mine.currentItemId : null,
                    mine ? mine.sceneStepIndex : -1,
                    !!mine && mine.sceneMode !== 'Contextual');

                // Adopt the app's level for the selected scenario, unless the user
                // is busy moving the slider.
                if (scenario && Date.now() >= volumeHeldUntil) {
                    const entry = (data.volumes || []).find(function (v) { return v.scenarioId === scenario.id; });
                    if (entry) {
                        const changed = Math.abs(entry.volume - volume) > 0.001 || entry.muted !== muted;
                        volume = entry.volume;
                        muted = !!entry.muted;
                        if (changed) renderVolume();
                    }
                }
            } catch (e) {
                connected = false;
                lastActive = [];
                renderPlaybackState();
                setStatus('offline', 'DiceAudio not reachable.');
                updateHint();
            }
            setTimeout(poll, 1000);
        }

        // ── Wiring ──────────────────────────────────────────────────────────
        groupSelect.addEventListener('change', function () {
            renderScenarios();
            renderItems();
            renderSelectTitles();
            renderCues();
            renderPlaybackState();
            saveSelection();
        });
        scenarioSelect.addEventListener('change', function () {
            renderItems();
            renderSelectTitles();
            renderCues();
            renderPlaybackState();
            saveSelection();
        });
        itemSelect.addEventListener('change', function () {
            renderSelectTitles();
            renderCues();
            renderPlaybackState();
            saveSelection();
        });
        btnPlay.addEventListener('click', function () {
            const scenario = selectedScenario();
            if (!scenario) return;
            const item = selectedItem();
            if (playingSelectedItem()) {
                post('/api/pause', { scenarioId: scenario.id });
                lastActive = lastActive.filter(function (a) { return a.scenarioId !== scenario.id; });
            } else {
                post('/api/play', { scenarioId: scenario.id, itemId: item ? item.id : null });
                if (item) lastActive = lastActive.concat([{ scenarioId: scenario.id, currentItemId: item.id }]);
            }
            renderPlaybackState();
        });
        btnStop.addEventListener('click', function () {
            const scenario = selectedScenario();
            post('/api/stop', scenario ? { scenarioId: scenario.id } : {});
            lastActive = scenario ? lastActive.filter(function (a) { return a.scenarioId !== scenario.id; }) : [];
            renderPlaybackState();
        });
        btnNext.addEventListener('click', function () {
            const scenario = selectedScenario();
            if (scenario) post('/api/next', { scenarioId: scenario.id });
        });
        btnPrev.addEventListener('click', function () {
            const scenario = selectedScenario();
            if (scenario) post('/api/prev', { scenarioId: scenario.id });
        });
        slider.addEventListener('input', function () {
            volume = Math.max(0, Math.min(100, parseInt(slider.value, 10) || 0)) / 100;
            muted = false;
            volumeHeldUntil = Date.now() + 1500;
            renderVolume();
            sendVolume();
        });
        btnSpeaker.addEventListener('click', function () {
            muted = !muted;
            volumeHeldUntil = Date.now() + 1500;
            renderVolume();
            sendVolume();
        });

        renderPlaybackState();
        renderVolume();
        loadGroups();
        poll();

        return {
            dispose: function () {
                disposed = true;
                if (volumeSendTimer) clearTimeout(volumeSendTimer);
                root.innerHTML = '';
                root.classList.remove('da-widget');
                root.removeAttribute('data-da-theme');
            }
        };

        // ── Helpers ─────────────────────────────────────────────────────────

        function button(icon, tooltip, extraClass) {
            const b = el('button', 'da-btn ' + (extraClass || ''));
            b.type = 'button';
            b.innerHTML = ICON[icon];
            b.title = tooltip;
            return b;
        }

        function option(text) {
            const o = document.createElement('option');
            o.textContent = text;
            return o;
        }
    }

    // ── Module-level helpers ────────────────────────────────────────────────

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text != null) node.textContent = text;
        return node;
    }

    // Injected once per document; every instance shares it.
    function ensureStyle() {
        if (document.getElementById(STYLE_ID)) return;
        const style = document.createElement('style');
        style.id = STYLE_ID;
        style.textContent = CSS;
        document.head.appendChild(style);
    }

    // Picks the palette from the note background rather than hard-coding a dark
    // theme, so the widget matches whichever Trilium theme is in use. Walks up
    // for the first opaque-enough background; Trilium's default is dark.
    function isDarkBackground(node) {
        while (node && node !== document.documentElement) {
            const background = getComputedStyle(node).backgroundColor;
            const parts = background && background.match(/rgba?\(([^)]+)\)/);
            if (parts) {
                const values = parts[1].split(',').map(parseFloat);
                const alpha = values.length > 3 ? values[3] : 1;
                if (alpha > 0.1) {
                    const luminance = 0.2126 * values[0] + 0.7152 * values[1] + 0.0722 * values[2];
                    return luminance < 128;
                }
            }
            node = node.parentElement;
        }
        // Nothing opaque anywhere (a bare page like the browser preview): fall
        // back to the OS preference rather than assuming dark.
        return !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
    }

    // Stable identity of this widget instance for localStorage:
    //   1. explicit data-diceaudio-key attribute when present, else
    //   2. the widget's position among all widgets on the page,
    // both scoped by the hosting Trilium note id (options.scope).
    function computeStorageKey(rootEl, opts) {
        const explicit = rootEl.getAttribute('data-diceaudio-key');
        const scope = (opts && opts.scope) || window.location.pathname || 'default';
        let instance = explicit;
        if (!instance) {
            const all = Array.prototype.slice.call(document.querySelectorAll('.diceaudio-widget'));
            const index = all.indexOf(rootEl);
            instance = 'w' + (index >= 0 ? index : 0);
        }
        return 'diceaudio.sel::' + scope + '::' + instance;
    }

    window.DiceAudioWidget = { mount: mount };
})();


/*
 * Trilium Render Note mount point.
 *
 * This file is intended to be pasted into a JavaScript frontend Code note
 * that is a DIRECT CHILD of the HTML Code note rendered by ~renderNote.
 *
 * It scopes the lookup to api.$container, so the same Render Note can be
 * included multiple times without duplicate-id conflicts. The hosting note's
 * id is passed as the persistence scope so each note remembers its own
 * widget selections.
 */
(function mountDiceAudioWidgetInTrilium() {
    'use strict';

    if (typeof api === 'undefined' || !api.$container) {
        throw new Error('DiceAudioWidgetJS.frontend.js must run as a Trilium JavaScript frontend note so api.$container exists.');
    }

    const root = api.$container.find('.diceaudio-widget')[0];

    if (!root) {
        throw new Error('DiceAudio widget root not found. The rendered HTML note must contain <div class="diceaudio-widget"></div>.');
    }

    if (root.__diceAudioWidgetInstance && typeof root.__diceAudioWidgetInstance.dispose === 'function') {
        root.__diceAudioWidgetInstance.dispose();
    }

    const rawPort = root.getAttribute('data-diceaudio-port') || '8765';
    const port = parseInt(rawPort, 10) || 8765;

    // Persistence scope: the note open in the active tab (i.e. the campaign
    // note that includes this widget). Falls back gracefully on older Trilium
    // versions or when no context note is available.
    let scope = null;
    try {
        const contextNote =
            (api.getActiveContextNote && api.getActiveContextNote()) ||
            (api.getActiveTabNote && api.getActiveTabNote());
        scope = contextNote ? contextNote.noteId : null;
    } catch (e) { /* keep null → location-based fallback */ }

    root.__diceAudioWidgetInstance = window.DiceAudioWidget.mount(root, { port: port, scope: scope });
})();
