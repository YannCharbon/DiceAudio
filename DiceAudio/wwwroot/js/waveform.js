// DiceAudio waveform rendering helpers.
window.daWaveform = {
    // Peak arrays kept per canvas so zooming and scrolling redraw from memory
    // instead of marshalling the whole array over interop on every frame.
    _peaks: {},

    load: function (canvasId, peaks) {
        this._peaks[canvasId] = peaks;
    },

    unload: function (canvasId) {
        delete this._peaks[canvasId];
    },

    // Draws the [startFrac, endFrac] slice of the loaded peaks (fractions of the
    // whole file, 0..1). One bar per device pixel, each bar showing the loudest
    // peak it covers, so zooming out never drops a transient.
    //
    // A bar is painted over exactly the time span it summarises, and a bucket is
    // counted by the column its centre lands in. Both matter: this used to draw
    // one bar per two device pixels and take each bar's buckets with a ceil, so a
    // level jump lit up the whole block that contained it and the block before —
    // the step appeared at the left edge of that pair, never to its right. At full
    // zoom-out on an hour-long track a block is about twelve seconds, which is
    // twelve seconds of reading the transition early. Returns the canvas width in
    // CSS pixels so the caller's cached geometry refreshes on every redraw.
    drawRange: function (canvasId, startFrac, endFrac) {
        const canvas = document.getElementById(canvasId);
        const peaks = this._peaks[canvasId];
        if (!canvas) return 0;
        const cssWidth = canvas.getBoundingClientRect().width;
        if (!peaks || peaks.length === 0) return cssWidth;

        // Size the backing store to the element, but only when it actually differs:
        // assigning width/height resets the canvas, and any layout that depends on
        // those attributes would otherwise grow by the pixel ratio on every redraw.
        const dpr = window.devicePixelRatio || 1;
        const w = Math.max(1, Math.round(canvas.clientWidth * dpr));
        const h = Math.max(1, Math.round(canvas.clientHeight * dpr));
        if (canvas.width !== w) canvas.width = w;
        if (canvas.height !== h) canvas.height = h;

        const ctx = canvas.getContext('2d');
        ctx.clearRect(0, 0, w, h);

        const n = peaks.length;
        let a = Math.min(Math.max(startFrac, 0), 1);
        let b = Math.min(Math.max(endFrac, 0), 1);
        if (b <= a) b = Math.min(1, a + 1e-9);

        const first = a * n;
        const span = (b - a) * n;

        ctx.fillStyle = 'rgba(35,166,213,0.65)';
        for (let i = 0; i < w; i++) {
            const s = first + (span * i) / w;
            const e = first + (span * (i + 1)) / w;

            // Buckets whose centre falls in this column. Taking the centre rather
            // than any overlap is what stops a bucket straddling a column edge
            // from brightening both and dragging a transient one column left.
            let lo = Math.ceil(s - 0.5);
            let hi = Math.ceil(e - 0.5);
            // Zoomed in past one bucket per column: show the bucket under the
            // column's own centre, so neighbouring columns repeat it rather than
            // leaving gaps.
            if (hi <= lo) { lo = Math.floor((s + e) / 2); hi = lo + 1; }
            if (lo < 0) lo = 0;
            if (hi > n) hi = n;

            let max = 0;
            for (let k = lo; k < hi; k++) if (peaks[k] > max) max = peaks[k];

            const p = Math.min(1, Math.max(0.015, max));
            const bh = p * h * 0.94;
            ctx.fillRect(i, (h - bh) / 2, 1, bh);
        }
        return cssWidth;
    },

    clear: function (canvasId) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        ctx.clearRect(0, 0, canvas.width, canvas.height);
    },

    // Rendered width in CSS pixels (used to convert click offsets to seconds).
    width: function (canvasId) {
        const canvas = document.getElementById(canvasId);
        return canvas ? canvas.getBoundingClientRect().width : 0;
    }
};
