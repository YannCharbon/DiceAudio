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

        private double _volume = 1.0;
        /// <summary>Volume 0..1 — applied live to the running backend.</summary>
        public double Volume
        {
            get => _volume;
            set { _volume = Math.Clamp(value, 0.0, 1.0); ApplyVolume(); }
        }

        // Watches for the clip end while playing (also drives clip looping).
        private System.Timers.Timer? _clipTimer;

        // ── Platform fields ──────────────────────────────────────────────────
#if WINDOWS
        private NAudio.Wave.WaveOutEvent? _waveOut;
        private NAudio.Wave.AudioFileReader? _reader;
        private bool _explicitStop;   // distinguishes Stop() from natural end
#else
        private IAudioPlayer? _maui;
#endif

        private DAAudioPlayer() { }

        // ── Factory ──────────────────────────────────────────────────────────

        /// <summary>Creates a player from an absolute file path.</summary>
        public static DAAudioPlayer Create(string filePath, IAudioManager? audioManager = null)
        {
            var p = new DAAudioPlayer();
            p.Init(filePath, audioManager);
            return p;
        }

        /// <summary>Creates a player configured from a per-usage settings object.</summary>
        public static DAAudioPlayer Create(string filePath, DAAudioUsage usage, IAudioManager? audioManager = null)
        {
            var p = Create(filePath, audioManager);
            p.Volume = usage.Volume;
            p.Loop = usage.Loop;
            p.ClipStart = usage.ClipStartSeconds;
            p.ClipEnd = usage.ClipEndSeconds;
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
            _waveOut.Init(_reader);
            // WaveOutEvent.Volume is the PROCESS-WIDE session volume (one shared
            // knob for the whole app — Windows even persists it across runs).
            // Older builds drove fades through it and may have left it near 0,
            // muting everything: pin it to a constant 1.0 here. Per-player volume
            // (fades, crossfades, layer levels) goes through AudioFileReader.Volume,
            // a per-stream sample multiplier — never ramp WaveOutEvent.Volume.
            try { _waveOut.Volume = 1.0f; } catch { /* not supported on some devices */ }
            _reader.Volume = (float)_volume;
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
        public void Seek(double seconds) =>
            RawSeek(EffectiveStart + Math.Clamp(seconds, 0, Duration));

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
                StartClipTimerIfNeeded();
        }

        // ── Playback control ─────────────────────────────────────────────────

        public void Play()
        {
            // Enter the clip region if we are outside it.
            if (RawPosition < EffectiveStart || RawPosition >= EffectiveEnd)
                RawSeek(EffectiveStart);

#if WINDOWS
            if (_waveOut == null) return;
            if (_reader != null) _reader.Volume = (float)_volume;
            _explicitStop = false;
            _waveOut.Play();
            IsPlaying = true;
#else
            if (_maui == null) return;
            _maui.Volume = _volume;
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
            if (_reader != null) _reader.Volume = (float)_volume;
#else
            if (_maui != null) _maui.Volume = _volume;
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
            // Only needed for an early clip end; natural end-of-file (including
            // clip-aware loop restarts) is handled by the backend-end events.
            if (ClipEnd == null) return;

            if (_clipTimer == null)
            {
                _clipTimer = new System.Timers.Timer(100);
                _clipTimer.Elapsed += (_, _) => CheckClipEnd();
            }
            _clipTimer.Start();
        }

        private void CheckClipEnd()
        {
            if (!IsPlaying) return;
            if (RawPosition < EffectiveEnd - 0.05) return;

            if (Loop)
            {
                RawSeek(EffectiveStart);
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
                    _reader.Volume = (float)_volume;
                    _waveOut.Init(_reader);
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
