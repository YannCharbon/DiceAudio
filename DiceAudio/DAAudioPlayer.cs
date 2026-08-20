/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using Plugin.Maui.Audio;

namespace DiceAudio
{
    /// <summary>
    /// Platform-unified audio player.
    /// Windows: NAudio WaveOutEvent. Android/other: Plugin.Maui.Audio IAudioPlayer.
    ///
    /// Supports live volume changes, smooth volume ramps (fade in/out) on all
    /// platforms, and non-destructive clip bounds: when <see cref="ClipStart"/> /
    /// <see cref="ClipEnd"/> are set, the player behaves as if the file only
    /// contained that region (Duration/CurrentPosition/Seek are clip-relative,
    /// loop restarts at ClipStart, playback ends at ClipEnd). The file on disk
    /// is never modified.
    /// </summary>
    public sealed class DAAudioPlayer : IDisposable
    {
        public event EventHandler? PlaybackEnded;

        public bool IsPlaying { get; private set; }
        public bool Loop { get; set; }

        /// <summary>Clip bounds in seconds within the file. Set before Play().</summary>
        public double? ClipStart { get; set; }
        public double? ClipEnd { get; set; }

        /// <summary>
        /// Ramps applied at the clip's own edges; null = none. They exist because a
        /// trim rarely lands on a zero crossing, so a hard cut steps the signal and
        /// clicks. Applied here rather than by callers so every use of the clip —
        /// and every loop iteration — gets them.
        /// </summary>
        public double? ClipFadeIn { get; set; }
        public double? ClipFadeOut { get; set; }

        /// <summary>
        /// Current position on the clip-edge ramp, 0..1. A multiplier on top of
        /// <see cref="Volume"/> rather than a writer of it, so it composes with
        /// scene crossfades and handover fades instead of fighting them.
        /// </summary>
        private double _clipEnvelope = 1.0;

        private double _volume = 1.0;
        /// <summary>Volume 0..1 — applied live to the running backend.</summary>
        public double Volume
        {
            get => _volume;
            set { _volume = Math.Clamp(value, 0.0, 1.0); ApplyVolume(); }
        }

        private DAAudioBus? _bus;
        /// <summary>
        /// Optional master fader this player is routed through (the owning
        /// scenario's bus). <see cref="Volume"/> keeps its own meaning — fades and
        /// per-usage levels write it — and the bus scales the result.
        /// </summary>
        public DAAudioBus? Bus
        {
            get => _bus;
            set
            {
                if (ReferenceEquals(_bus, value)) return;
                if (_bus != null) _bus.GainChanged -= ApplyVolume;
                _bus = value;
                if (_bus != null) _bus.GainChanged += ApplyVolume;
                ApplyVolume();
            }
        }

#if WINDOWS
        /// <summary>
        /// Level handed to the backend: own volume × bus gain. The clip ramp is NOT
        /// folded in here on Windows — it is applied per sample by
        /// <see cref="ClipFadeSampleProvider"/>, because NAudio only re-reads this
        /// value once per buffer (~150 ms), far too coarse for a half-second fade.
        /// </summary>
        private float EffectiveVolume => (float)(_volume * (_bus?.Gain ?? 1.0));
#else
        /// <summary>Level handed to the backend: own volume × bus gain × clip ramp.</summary>
        private float EffectiveVolume => (float)(_volume * (_bus?.Gain ?? 1.0) * _clipEnvelope);
#endif

        private bool HasClipFade => ClipFadeIn is > 0 || ClipFadeOut is > 0;

        /// <summary>
        /// The clip-edge ramp value at a given position in the file. The two ramps
        /// are capped at half the clip each so they cannot overlap and cancel out on
        /// a very short selection.
        /// </summary>
        private double EnvelopeAt(double positionSeconds)
        {
            double fadeIn = Math.Max(0, ClipFadeIn ?? 0);
            double fadeOut = Math.Max(0, ClipFadeOut ?? 0);
            if (fadeIn <= 0 && fadeOut <= 0) return 1.0;

            double start = EffectiveStart;
            double end = EffectiveEnd;
            double half = Math.Max(0, (end - start) / 2);
            if (half <= 0) return 1.0;

            fadeIn = Math.Min(fadeIn, half);
            fadeOut = Math.Min(fadeOut, half);

            double position = Math.Clamp(positionSeconds, start, end);
            double gain = 1.0;

            if (fadeIn > 0 && position < start + fadeIn)
                gain = Math.Min(gain, (position - start) / fadeIn);
            if (fadeOut > 0 && position > end - fadeOut)
                gain = Math.Min(gain, (end - position) / fadeOut);

            return Math.Clamp(gain, 0, 1);
        }

        private double ComputeClipEnvelope() => EnvelopeAt(RawPosition);

        /// <summary>
        /// Re-evaluates the clip ramp and pushes it to the backend if it moved.
        /// Only the non-Windows backend needs this: it exposes nothing finer than a
        /// volume property, so the ramp is stepped from the clip timer there.
        /// </summary>
        private void UpdateClipEnvelope()
        {
#if !WINDOWS
            double envelope = HasClipFade ? ComputeClipEnvelope() : 1.0;
            if (Math.Abs(envelope - _clipEnvelope) < 0.0005) return;
            _clipEnvelope = envelope;
            ApplyVolume();
#endif
        }

        // Watches for the clip end while playing (also drives clip looping).
        private System.Timers.Timer? _clipTimer;

        // ── Platform fields ──────────────────────────────────────────────────
#if WINDOWS
        private NAudio.Wave.WaveOutEvent? _waveOut;
        private NAudio.Wave.AudioFileReader? _reader;
        /// <summary>What WaveOut plays from: the reader wrapped in the clip-ramp stage.</summary>
        private NAudio.Wave.IWaveProvider? _waveSource;
        private bool _explicitStop;   // distinguishes Stop() from natural end

        /// <summary>
        /// Applies the clip-edge ramps one sample at a time as WaveOut pulls audio.
        ///
        /// The obvious approach — stepping AudioFileReader.Volume from a timer —
        /// cannot be smooth here: WaveOutEvent asks the reader for ~150 ms at a
        /// time (300 ms latency over two buffers), so a half-second fade would be
        /// built from about three volume values. Fading out that way is easy to
        /// miss because it ends in silence, but fading in from silence makes every
        /// step audible, which is exactly the jitter this removes. Scaling each
        /// sample as it is read is smooth by construction and costs one multiply.
        /// </summary>
        private sealed class ClipFadeSampleProvider : NAudio.Wave.ISampleProvider
        {
            private readonly NAudio.Wave.AudioFileReader _reader;
            private readonly DAAudioPlayer _owner;

            public ClipFadeSampleProvider(NAudio.Wave.AudioFileReader reader, DAAudioPlayer owner)
            {
                _reader = reader;
                _owner = owner;
            }

            public NAudio.Wave.WaveFormat WaveFormat => _reader.WaveFormat;

            public int Read(float[] buffer, int offset, int count)
            {
                // Where this block starts, captured before the read moves the reader on.
                double blockStart = _reader.CurrentTime.TotalSeconds;

                int read = _reader.Read(buffer, offset, count);
                if (read <= 0 || !_owner.HasClipFade) return read;

                int channels = Math.Max(1, WaveFormat.Channels);
                double secondsPerFrame = 1.0 / Math.Max(1, WaveFormat.SampleRate);

                // One gain per frame, shared by that frame's channels.
                int lastFrame = -1;
                float gain = 1f;
                for (int i = 0; i < read; i++)
                {
                    int frame = i / channels;
                    if (frame != lastFrame)
                    {
                        lastFrame = frame;
                        gain = (float)_owner.EnvelopeAt(blockStart + frame * secondsPerFrame);
                    }
                    buffer[offset + i] *= gain;
                }
                return read;
            }
        }
#else
        private IAudioPlayer? _maui;
#endif

        private DAAudioPlayer() { }

        // ── Factory ──────────────────────────────────────────────────────────

        /// <summary>Creates a player from an absolute file path.</summary>
        public static DAAudioPlayer Create(string filePath, IAudioManager? audioManager = null, DAAudioBus? bus = null)
        {
            var p = new DAAudioPlayer();
            p.Init(filePath, audioManager);
            p.Bus = bus;
            return p;
        }

        /// <summary>Creates a player configured from a per-usage settings object.</summary>
        public static DAAudioPlayer Create(string filePath, DAAudioUsage usage, IAudioManager? audioManager = null, DAAudioBus? bus = null)
        {
            var p = Create(filePath, audioManager, bus);
            p.Volume = usage.Volume;
            p.Loop = usage.Loop;
            p.ClipStart = usage.ClipStartSeconds;
            p.ClipEnd = usage.ClipEndSeconds;
            p.ClipFadeIn = usage.ClipFadeInSeconds;
            p.ClipFadeOut = usage.ClipFadeOutSeconds;
            return p;
        }

        /// <summary>
        /// Reads a file's duration (seconds) without allocating playback
        /// resources — no WaveOutEvent, just the decoder. Cheap enough to call
        /// for UI that needs to show a track length before playback starts.
        /// Returns 0 on any failure.
        /// </summary>
        public static double ProbeDurationSeconds(string filePath, IAudioManager? audioManager = null)
        {
#if WINDOWS
            try
            {
                using var reader = new NAudio.Wave.AudioFileReader(filePath);
                return reader.TotalTime.TotalSeconds;
            }
            catch { return 0; }
#else
            try
            {
                using var probe = audioManager!.CreatePlayer(filePath);
                return probe.Duration;
            }
            catch { return 0; }
#endif
        }

        private void Init(string filePath, IAudioManager? audioManager)
        {
#if WINDOWS
            _reader   = new NAudio.Wave.AudioFileReader(filePath);
            _waveOut  = new NAudio.Wave.WaveOutEvent();
            // AudioFileReader already presents IEEE float, so wrapping it in the ramp
            // stage is transparent to the output format.
            _waveSource = new NAudio.Wave.SampleProviders.SampleToWaveProvider(
                new ClipFadeSampleProvider(_reader, this));
            _waveOut.Init(_waveSource);
            // WaveOutEvent.Volume is the PROCESS-WIDE session volume (one shared
            // knob for the whole app — Windows even persists it across runs).
            // Older builds drove fades through it and may have left it near 0,
            // muting everything: pin it to a constant 1.0 here. Per-player volume
            // (fades, crossfades, layer levels) goes through AudioFileReader.Volume,
            // a per-stream sample multiplier — never ramp WaveOutEvent.Volume.
            try { _waveOut.Volume = 1.0f; } catch { /* not supported on some devices */ }
            _reader.Volume = EffectiveVolume;
            _waveOut.PlaybackStopped += OnWindowsPlaybackStopped;
#else
            _maui = audioManager!.CreatePlayer(filePath);
            _maui.PlaybackEnded += OnMauiPlaybackEnded;
#endif
        }

        // ── Raw (whole-file) coordinates ─────────────────────────────────────

        /// <summary>Total duration of the underlying file, ignoring clip bounds.</summary>
        public double FileDuration
        {
            get
            {
#if WINDOWS
                return _reader?.TotalTime.TotalSeconds ?? 0;
#else
                return _maui?.Duration ?? 0;
#endif
            }
        }

        private double RawPosition
        {
            get
            {
#if WINDOWS
                return _reader?.CurrentTime.TotalSeconds ?? 0;
#else
                return _maui?.CurrentPosition ?? 0;
#endif
            }
        }

        private void RawSeek(double seconds)
        {
            seconds = Math.Clamp(seconds, 0, FileDuration);
#if WINDOWS
            if (_reader != null)
                _reader.CurrentTime = TimeSpan.FromSeconds(seconds);
#else
            _maui?.Seek(seconds);
#endif
        }

        // ── Clip-relative coordinates (what the UI sees) ─────────────────────

        private double EffectiveStart => Math.Clamp(ClipStart ?? 0, 0, FileDuration);
        private double EffectiveEnd => Math.Clamp(ClipEnd ?? FileDuration, 0, FileDuration);

        /// <summary>Playable duration — clip length when clipped, else file length.</summary>
        public double Duration => Math.Max(0, EffectiveEnd - EffectiveStart);

        /// <summary>Position within the playable (clip) region.</summary>
        public double CurrentPosition => Math.Clamp(RawPosition - EffectiveStart, 0, Duration);

        /// <summary>Seeks within the playable (clip) region.</summary>
        public void Seek(double seconds)
        {
            RawSeek(EffectiveStart + Math.Clamp(seconds, 0, Duration));
            UpdateClipEnvelope();   // landing inside a ramp must take its level
        }

        /// <summary>
        /// Updates the clip bounds while (possibly) playing — used by the clip
        /// editor so the preview follows handle drags live. Ensures the clip-end
        /// watcher runs if an end bound appears mid-playback.
        /// </summary>
        public void SetClipBounds(double? start, double? end)
        {
            ClipStart = start;
            ClipEnd = end;
            if (IsPlaying)
            {
                StartClipTimerIfNeeded();
                UpdateClipEnvelope();
            }
        }

        /// <summary>
        /// Updates the clip-edge ramps while (possibly) playing, so the editor can
        /// audition them without restarting the preview.
        /// </summary>
        public void SetClipFades(double? fadeIn, double? fadeOut)
        {
            ClipFadeIn = fadeIn;
            ClipFadeOut = fadeOut;
            if (IsPlaying)
            {
                StartClipTimerIfNeeded();
                UpdateClipEnvelope();
            }
            else
            {
                _clipEnvelope = 1.0;
            }
        }

        // ── Playback control ─────────────────────────────────────────────────

        public void Play()
        {
            // Enter the clip region if we are outside it.
            if (RawPosition < EffectiveStart || RawPosition >= EffectiveEnd)
                RawSeek(EffectiveStart);

            // Before the backend is handed a level: starting at full volume for even
            // one buffer is the click the fade-in exists to remove.
            _clipEnvelope = HasClipFade ? ComputeClipEnvelope() : 1.0;

#if WINDOWS
            if (_waveOut == null) return;
            if (_reader != null) _reader.Volume = EffectiveVolume;
            _explicitStop = false;
            _waveOut.Play();
            IsPlaying = true;
#else
            if (_maui == null) return;
            _maui.Volume = EffectiveVolume;
            // Looping is handled by this class (clip-aware); never by the backend.
            _maui.Play();
            IsPlaying = true;
#endif
            StartClipTimerIfNeeded();
        }

        public void Pause()
        {
            _clipTimer?.Stop();
#if WINDOWS
            _waveOut?.Pause();
#else
            _maui?.Pause();
#endif
            IsPlaying = false;
        }

        public void Stop()
        {
            _clipTimer?.Stop();
#if WINDOWS
            _explicitStop = true;
            _waveOut?.Stop();
            if (_reader != null) _reader.CurrentTime = TimeSpan.FromSeconds(EffectiveStart);
#else
            _maui?.Stop();
#endif
            IsPlaying = false;
        }

        // ── Volume ramps / fades (cross-platform) ────────────────────────────

        private void ApplyVolume()
        {
#if WINDOWS
            if (_reader != null) _reader.Volume = EffectiveVolume;
#else
            if (_maui != null) _maui.Volume = EffectiveVolume;
#endif
        }

        /// <summary>Smoothly ramps the volume to <paramref name="target"/> over the given duration.</summary>
        public async Task RampVolumeAsync(double target, double seconds, CancellationToken ct = default)
        {
            target = Math.Clamp(target, 0.0, 1.0);
            if (seconds <= 0)
            {
                Volume = target;
                return;
            }

            double initial = _volume;
            int steps = Math.Max(1, (int)(seconds * 20));   // 50 ms resolution
            int stepMs = Math.Max(1, (int)(seconds * 1000 / steps));
            for (int i = 1; i <= steps; i++)
            {
                if (ct.IsCancellationRequested) return;
                Volume = initial + (target - initial) * i / steps;
                try { await Task.Delay(stepMs, ct); }
                catch (TaskCanceledException) { return; }
            }
            Volume = target;
        }

        /// <summary>Starts playback at volume 0 and fades in to <paramref name="targetVolume"/>.</summary>
        public async Task FadeInAndPlayAsync(double targetVolume, double seconds, CancellationToken ct = default)
        {
            Volume = seconds > 0 ? 0 : targetVolume;
            Play();
            if (seconds > 0)
                await RampVolumeAsync(targetVolume, seconds, ct);
        }

        /// <summary>Ramps volume to 0 over <paramref name="durationSeconds"/> then stops.</summary>
        public async Task FadeOutAndStopAsync(float durationSeconds = 1.0f)
        {
            if (IsPlaying && durationSeconds > 0)
                await RampVolumeAsync(0, durationSeconds);
            Stop();
        }

        // ── Clip end enforcement ─────────────────────────────────────────────

        private void StartClipTimerIfNeeded()
        {
            // Needed for an early clip end, and to step the clip-edge ramps. A
            // natural end-of-file (including clip-aware loop restarts) is still
            // handled by the backend-end events.
            if (ClipEnd == null && !HasClipFade) return;

            if (_clipTimer == null)
            {
                _clipTimer = new System.Timers.Timer();
                _clipTimer.Elapsed += (_, _) => OnClipTick();
            }
#if WINDOWS
            // The ramp is applied per sample, so this timer only watches the clip end.
            _clipTimer.Interval = 100;
#else
            // No sample-level hook on this backend: step the ramp from here instead,
            // finely enough that a half-second fade is not heard as a staircase.
            _clipTimer.Interval = HasClipFade ? 20 : 100;
#endif
            _clipTimer.Start();
        }

        private void OnClipTick()
        {
            if (!IsPlaying) return;
            UpdateClipEnvelope();
            // Unchanged trigger: only an early clip end needs watching here, so a
            // fade alone never takes over end-of-file from the backend events.
            if (ClipEnd != null) CheckClipEnd();
        }

        private void CheckClipEnd()
        {
            if (!IsPlaying) return;
            if (RawPosition < EffectiveEnd - 0.05) return;

            if (Loop)
            {
                RawSeek(EffectiveStart);
                UpdateClipEnvelope();   // every pass gets the fade-in, not just the first
            }
            else
            {
                Stop();
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
            }
        }

        // ── Event handlers ───────────────────────────────────────────────────

#if WINDOWS
        private void OnWindowsPlaybackStopped(object? sender, NAudio.Wave.StoppedEventArgs e)
        {
            if (_reader == null || _waveOut == null) return;
            if (_explicitStop) return;   // Stop() was called, not natural end

            // Natural end detected when the reader is within 500 ms of the total time
            bool naturalEnd = _reader.CurrentTime >= _reader.TotalTime - TimeSpan.FromMilliseconds(500);
            if (!naturalEnd) return;

            if (Loop)
            {
                // Restart on a separate thread to avoid deadlocking the NAudio callback
                double restartAt = EffectiveStart;
                Task.Run(() =>
                {
                    Thread.Sleep(20);
                    if (_reader == null || _waveOut == null) return;
                    _reader.CurrentTime = TimeSpan.FromSeconds(restartAt);
                    _clipEnvelope = HasClipFade ? ComputeClipEnvelope() : 1.0;
                    _reader.Volume = EffectiveVolume;
                    _waveOut.Init(_waveSource ?? (NAudio.Wave.IWaveProvider)_reader);
                    _waveOut.Play();
                });
            }
            else
            {
                IsPlaying = false;
                _clipTimer?.Stop();
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
            }
        }
#else
        private void OnMauiPlaybackEnded(object? sender, EventArgs e)
        {
            if (Loop && _maui != null)
            {
                // Clip-aware restart (backend loop would restart at 0, not ClipStart).
                _maui.Seek(EffectiveStart);
                _maui.Play();
                return;
            }
            IsPlaying = false;
            _clipTimer?.Stop();
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
#endif

        // ── Dispose ──────────────────────────────────────────────────────────

        public void Dispose()
        {
            Bus = null;   // unsubscribes from GainChanged
            _clipTimer?.Dispose();
            _clipTimer = null;
#if WINDOWS
            if (_waveOut != null)
            {
                _waveOut.PlaybackStopped -= OnWindowsPlaybackStopped;
                _explicitStop = true;
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }
            _reader?.Dispose();
            _reader = null;
            // Holds a reference to the reader; drop it so the disposed reader
            // cannot be reached from a late Init.
            _waveSource = null;
#else
            if (_maui != null)
            {
                _maui.PlaybackEnded -= OnMauiPlaybackEnded;
                _maui.Stop();
                _maui.Dispose();
                _maui = null;
            }
#endif
            IsPlaying = false;
        }
    }
}
