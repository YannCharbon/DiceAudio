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
    // whole file, 0..1). One bar per couple of device pixels, each bar showing the
    // loudest peak it covers, so zooming out never drops a transient.
    drawRange: function (canvasId, startFrac, endFrac) {
        const canvas = document.getElementById(canvasId);
        const peaks = this._peaks[canvasId];
        if (!canvas || !peaks || peaks.length === 0) return;

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

        const barW = Math.max(1, Math.round(2 * dpr));
        const bars = Math.max(1, Math.floor(w / barW));

        ctx.fillStyle = 'rgba(35,166,213,0.65)';
        for (let i = 0; i < bars; i++) {
            const s = first + (span * i) / bars;
            const e = first + (span * (i + 1)) / bars;

            let lo = Math.floor(s);
            let hi = Math.ceil(e);
            if (hi <= lo) hi = lo + 1;
            if (lo < 0) lo = 0;
            if (hi > n) hi = n;

            let max = 0;
            for (let k = lo; k < hi; k++) if (peaks[k] > max) max = peaks[k];

            const p = Math.min(1, Math.max(0.015, max));
            const bh = p * h * 0.94;
            ctx.fillRect(i * barW, (h - bh) / 2, Math.max(1, barW - 1), bh);
        }
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
