/*
 * DiceAudio remote-control widget for Trilium Notes.
 *
 * Vanilla JS, no dependencies. Talks to the DiceAudio control server
 * (enable it in DiceAudio → Settings → Remote control).
 *
 * The selected scenario item of every widget instance is persisted in
 * localStorage, keyed by the hosting note and the widget's position on the
 * page (or by an explicit data-diceaudio-key attribute), so refreshing a
 * note restores each player's configuration.
 */
(function () {
    'use strict';

    function mount(root, options) {
        const port = (options && options.port) || 8765;
        const base = 'http://localhost:' + port;
        const storageKey = computeStorageKey(root, options);

        // ── UI skeleton ─────────────────────────────────────────────────────
        root.innerHTML = '';
        const box = el('div', {
            style: 'font-family:sans-serif; background:#1a1a30; border:1px solid #2a2a4a;' +
                   'border-radius:10px; padding:10px 12px; color:#ddd; max-width:460px; font-size:13px;'
        });
        root.appendChild(box);

        const title = el('div', { style: 'font-weight:bold; color:#23a6ab; margin-bottom:6px;' }, '🎲 DiceAudio');
        const select = el('select', {
            style: 'width:100%; background:#12121f; color:#ddd; border:1px solid #2a2a4a;' +
                   'border-radius:6px; padding:5px; margin-bottom:4px;'
        });
        // Shows "Group / Scenario" of the current selection.
        const summary = el('div', {
            style: 'font-size:11px; color:#8fa3b8; margin-bottom:8px; min-height:14px;' +
                   'white-space:nowrap; overflow:hidden; text-overflow:ellipsis;'
        });
        const stepRow = el('div', { style: 'display:none; margin-bottom:8px;' });
        const stepSelect = el('select', {
            style: 'width:100%; background:#12121f; color:#ddd; border:1px solid #2a2a4a;' +
                   'border-radius:6px; padding:5px;'
        });
        stepRow.appendChild(stepSelect);

        // Contextual scenes: one clickable button per named state (crossfades
        // between them); replaces the linear step selector for those scenes.
        const contextRow = el('div', {
            style: 'display:none; flex-wrap:wrap; gap:4px; margin-bottom:8px;'
        });

        const buttons = el('div', { style: 'display:flex; gap:6px; flex-wrap:wrap; margin-bottom:8px;' });
        const btnPrev = button('⏮', 'Previous item');
        const btnPlay = button('▶', 'Play', '#4caf50');
        const btnAdvance = button('⏭ Step', 'Advance scene step', '#90caf9');
        const btnNext = button('⏭', 'Next item');
        const btnStop = button('⏹', 'Stop', '#f44336');
        [btnPrev, btnPlay, btnAdvance, btnNext, btnStop].forEach(b => buttons.appendChild(b));

        const status = el('div', { style: 'font-size:12px; color:#888; min-height:16px;' }, 'Connecting…');

        box.appendChild(title);
        box.appendChild(select);
        box.appendChild(summary);
        box.appendChild(stepRow);
        box.appendChild(contextRow);
        box.appendChild(buttons);
        box.appendChild(status);

        // ── Data / state ────────────────────────────────────────────────────
        let entries = [];        // { scenarioId, itemId, label, type, mode, steps, groupName, scenarioName }
        let contextButtons = []; // rendered <button> per context (contextual scenes)
        let disposed = false;

        function selected() {
            return entries[select.selectedIndex] || null;
        }

        function refreshSceneControls() {
            const entry = selected();
            const isScene = entry && entry.type === 'Scene';
            const isContextual = isScene && entry.mode === 'Contextual';
            const isLinear = isScene && !isContextual;
            const cues = (entry && entry.steps) || [];

            // Linear scenes: sequential step selector + "advance step".
            // Contextual scenes: discrete state buttons you switch between freely.
            btnAdvance.style.display = isLinear ? '' : 'none';
            stepRow.style.display = isLinear && cues.length ? '' : 'none';
            contextRow.style.display = isContextual && cues.length ? '' : 'none';

            if (isLinear) {
                stepSelect.innerHTML = '';
                cues.forEach(function (name, i) {
                    stepSelect.appendChild(el('option', { value: String(i) }, (i + 1) + '. ' + name));
                });
            }

            contextButtons = [];
            contextRow.innerHTML = '';
            if (isContextual) {
                cues.forEach(function (name, i) {
                    const b = contextButton(name);
                    b.addEventListener('click', function () {
                        const e = selected();
                        if (e) post('/api/scene/goto', {
                            scenarioId: e.scenarioId, itemId: e.itemId, stepIndex: i
                        });
                    });
                    contextButtons.push(b);
                    contextRow.appendChild(b);
                });
            }

            summary.textContent = entry
                ? '📁 ' + entry.groupName + '  /  ' + entry.scenarioName
                : '';
            summary.title = summary.textContent;
        }

        // Highlights the active context button, but only when the selected item
        // is the one actually playing.
        function updateContextHighlight(activeItemId, activeIndex) {
            const entry = selected();
            const mine = entry && entry.type === 'Scene' && entry.mode === 'Contextual'
                      && entry.itemId === activeItemId;
            contextButtons.forEach(function (b, i) {
                setContextActive(b, !!mine && i === activeIndex);
            });
        }

        // ── Selection persistence ───────────────────────────────────────────

        function saveSelection() {
            const entry = selected();
            if (!entry) return;
            try {
                localStorage.setItem(storageKey, JSON.stringify({
                    scenarioId: entry.scenarioId, itemId: entry.itemId
                }));
            } catch (e) { /* storage unavailable — selection just won't persist */ }
        }

        function restoreSelection() {
            let saved = null;
            try { saved = JSON.parse(localStorage.getItem(storageKey) || 'null'); }
            catch (e) { }
            if (!saved) return;

            const index = entries.findIndex(function (entry) {
                return entry.scenarioId === saved.scenarioId && entry.itemId === saved.itemId;
            });
            if (index >= 0) select.selectedIndex = index;
        }

        async function loadGroups() {
            try {
                const res = await fetch(base + '/api/groups');
                const data = await res.json();
                entries = [];
                select.innerHTML = '';
                (data.groups || []).forEach(function (group) {
                    (group.scenarios || []).forEach(function (scenario) {
                        const og = el('optgroup', { label: group.name + ' / ' + scenario.name });
                        (scenario.items || []).forEach(function (item) {
                            entries.push({
                                scenarioId: scenario.id, itemId: item.id,
                                label: item.name, type: item.type,
                                mode: item.sceneMode, steps: item.steps,
                                groupName: group.name, scenarioName: scenario.name
                            });
                            og.appendChild(el('option', {}, item.name + (item.type === 'Scene' ? ' 🎬' : '')));
                        });
                        if (og.children.length) select.appendChild(og);
                    });
                });
                restoreSelection();
                status.textContent = entries.length ? 'Ready.' : 'No scenario items found in DiceAudio.';
                refreshSceneControls();
            } catch (e) {
                status.textContent = '⚠ DiceAudio not reachable on ' + base +
                    ' — is the app running with the control server enabled?';
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
                status.textContent = '⚠ Request failed — is DiceAudio running?';
            }
        }

        async function poll() {
            if (disposed) return;
            try {
                const res = await fetch(base + '/api/state');
                const data = await res.json();
                const active = (data.active && data.active[0]) || null;
                if (active) {
                    let text = '♪ ' + active.scenarioName + ' — ' + (active.currentItemName || '');
                    if (active.sceneStepName != null) {
                        text += active.sceneMode === 'Contextual'
                            ? '  [context: ' + active.sceneStepName + ']'
                            : '  [step ' + (active.sceneStepIndex + 1) +
                              (active.sceneStepCount ? '/' + active.sceneStepCount : '') +
                              ': ' + active.sceneStepName + ']';
                    }
                    status.textContent = text;
                    status.style.color = '#4caf50';
                    updateContextHighlight(
                        active.sceneMode === 'Contextual' ? active.currentItemId : null,
                        active.sceneMode === 'Contextual' ? active.sceneStepIndex : -1);
                } else {
                    status.textContent = 'Nothing playing.';
                    status.style.color = '#888';
                    updateContextHighlight(null, -1);
                }
            } catch (e) {
                status.textContent = '⚠ DiceAudio not reachable.';
                status.style.color = '#f0a03c';
            }
            setTimeout(poll, 1000);
        }

        // ── Wiring ──────────────────────────────────────────────────────────
        select.addEventListener('change', function () {
            refreshSceneControls();
            saveSelection();
        });
        btnPlay.addEventListener('click', function () {
            const e = selected();
            if (e) post('/api/play', { scenarioId: e.scenarioId, itemId: e.itemId });
        });
        btnStop.addEventListener('click', function () {
            const e = selected();
            post('/api/stop', e ? { scenarioId: e.scenarioId } : {});
        });
        btnNext.addEventListener('click', function () {
            const e = selected();
            if (e) post('/api/next', { scenarioId: e.scenarioId });
        });
        btnPrev.addEventListener('click', function () {
            const e = selected();
            if (e) post('/api/prev', { scenarioId: e.scenarioId });
        });
        btnAdvance.addEventListener('click', function () {
            const e = selected();
            if (e) post('/api/scene/advance', { scenarioId: e.scenarioId, itemId: e.itemId });
        });
        stepSelect.addEventListener('change', function () {
            const e = selected();
            if (e) post('/api/scene/goto', {
                scenarioId: e.scenarioId, itemId: e.itemId,
                stepIndex: parseInt(stepSelect.value, 10)
            });
        });

        loadGroups();
        poll();

        return { dispose: function () { disposed = true; root.innerHTML = ''; } };

        // ── Helpers ─────────────────────────────────────────────────────────

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

        function el(tag, attrs, text) {
            const node = document.createElement(tag);
            if (attrs) for (const k in attrs) node.setAttribute(k, attrs[k]);
            if (text != null) node.textContent = text;
            return node;
        }

        function button(label, tooltip, color) {
            const b = el('button', {
                title: tooltip,
                style: 'background:#12121f; color:' + (color || '#ddd') + '; border:1px solid #363660;' +
                       'border-radius:6px; padding:5px 10px; cursor:pointer; font-size:13px;'
            }, label);
            b.addEventListener('mouseenter', function () { b.style.background = '#2e2e58'; });
            b.addEventListener('mouseleave', function () { b.style.background = '#12121f'; });
            return b;
        }

        // Context state chip (contextual scenes). Active state is toggled by the
        // poll loop via setContextActive.
        function contextButton(label) {
            const b = el('button', {
                title: 'Switch to context "' + label + '"',
                style: contextStyle(false)
            }, label);
            b.__active = false;
            b.addEventListener('mouseenter', function () { if (!b.__active) b.style.background = '#2e2e58'; });
            b.addEventListener('mouseleave', function () { if (!b.__active) b.style.background = '#1e1e38'; });
            return b;
        }

        function setContextActive(b, on) {
            if (b.__active === on) return;   // keep hover state; only rewrite on change
            b.__active = on;
            b.setAttribute('style', contextStyle(on));
        }

        function contextStyle(on) {
            return 'border-radius:8px; padding:4px 9px; cursor:pointer; font-size:12px; user-select:none; ' +
                   (on
                       ? 'background:rgba(35,166,213,0.3); border:1px solid #23a6d5; color:#bfe8ff; font-weight:bold;'
                       : 'background:#1e1e38; border:1px solid #363660; color:#ddd;');
        }
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
