// Draws pre-computed audio peaks onto a canvas (used by AudioClipEditor).
window.daWaveform = {
    draw: function (canvasId, peaks) {
        const canvas = document.getElementById(canvasId);
        if (!canvas || !peaks || peaks.length === 0) return;

        const dpr = window.devicePixelRatio || 1;
        const w = canvas.width = Math.max(1, canvas.clientWidth * dpr);
        const h = canvas.height = Math.max(1, canvas.clientHeight * dpr);
        const ctx = canvas.getContext('2d');
        ctx.clearRect(0, 0, w, h);

        const n = peaks.length;
        const bw = w / n;
        ctx.fillStyle = 'rgba(35,166,213,0.65)';
        for (let i = 0; i < n; i++) {
            const p = Math.min(1, Math.max(0.015, peaks[i]));
            const bh = p * h * 0.94;
            ctx.fillRect(i * bw, (h - bh) / 2, Math.max(1, bw * 0.75), bh);
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
