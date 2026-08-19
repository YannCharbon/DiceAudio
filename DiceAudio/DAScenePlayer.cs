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
    /// Playback engine for a layered <see cref="DAScene"/>.
    ///
    /// StartAsync applies Steps[0]; AdvanceAsync applies the next step; StopAsync
    /// fades everything out. Commands within a step run on independent delays
    /// (fire-and-forget tasks) so a step like "fade out birds over 5 s, then fade
    /// in rain over 5 s" never blocks the advance button. Pending delayed
    /// commands survive step advances (e.g. music scheduled at +30 s still enters
    /// if the user advances at +10 s) and are cancelled only on Stop.
    /// </summary>
    public class DAScenePlayer : IDisposable
    {
        public const float StopFadeSeconds = 2.0f;

        private readonly IAudioManager _audioManager;
        private readonly DiceAudioService _service;
        /// <summary>Master fader of the owning scenario; applied to every layer and one-shot.</summary>
        private readonly DAAudioBus? _bus;
        public DAScene Scene { get; }

        private readonly object _lock = new();
        private readonly Dictionary<Guid, DAAudioPlayer> _layerPlayers = new();
        private readonly Dictionary<Guid, CancellationTokenSource> _randomLoops = new();
        private readonly List<DAAudioPlayer> _oneShots = new();
        private CancellationTokenSource _sessionCts = new();
        private bool _disposed;

        // Contextual mode state
        private Guid? _currentContextId;
        // One ramp per layer at a time: entering a new context cancels the layer's
        // previous ramp so rapid context switches never fight over the volume.
        private readonly Dictionary<Guid, CancellationTokenSource> _layerRamps = new();
        // Per-context volume for SFX one-shots (overrides the layer's own volume).
        private readonly Dictionary<Guid, double> _sfxVolumeOverride = new();

        public int CurrentStepIndex { get; private set; } = -1;
        public bool IsPlaying { get; private set; }
        public event Action? StateChanged;

        public DASceneContext? CurrentContext =>
            _currentContextId is Guid id ? Scene.Contexts.FirstOrDefault(c => c.Id == id) : null;

        // Unified "cue" view over steps (linear) / contexts (contextual), used by
        // the UI and the remote-control API.
        public int CurrentCueIndex => Scene.Mode == DASceneMode.Contextual
            ? (CurrentContext is { } c ? Scene.Contexts.IndexOf(c) : -1)
            : CurrentStepIndex;
        public string? CurrentCueName => Scene.Mode == DASceneMode.Contextual
            ? CurrentContext?.Name
            : CurrentStep?.Name;
        public int CueCount => Scene.Mode == DASceneMode.Contextual
            ? Scene.Contexts.Count
            : Scene.Steps.Count;

        public DAScenePlayer(IAudioManager audioManager, DiceAudioService service, DAScene scene,
                             DAAudioBus? bus = null)
        {
            _audioManager = audioManager;
            _service = service;
            Scene = scene;
            _bus = bus;
        }

        public DASceneStep? CurrentStep =>
            CurrentStepIndex >= 0 && CurrentStepIndex < Scene.Steps.Count ? Scene.Steps[CurrentStepIndex] : null;

        public DASceneStep? NextStep =>
            CurrentStepIndex + 1 < Scene.Steps.Count ? Scene.Steps[CurrentStepIndex + 1] : null;

        public bool IsLayerAudible(Guid layerId)
        {
            lock (_lock)
                return _layerPlayers.TryGetValue(layerId, out var p) && p.IsPlaying;
        }

        public bool IsRandomLoopRunning(Guid layerId)
        {
            lock (_lock)
                return _randomLoops.ContainsKey(layerId);
        }

        // ── Transport ────────────────────────────────────────────────────────

        /// <summary>(Re)starts the scene: step 0 (linear) or the first context (contextual).</summary>
        public async Task StartAsync()
        {
            await StopAsync(0.3f);          // quick reset if something was running
            _sessionCts = new CancellationTokenSource();
            CurrentStepIndex = -1;
            IsPlaying = true;

            if (Scene.Mode == DASceneMode.Contextual)
            {
                // Enter the item's chosen default context; fall back to the first
                // one if none is set or it was since deleted.
                var target = Scene.Contexts.FirstOrDefault(c => c.Id == Scene.DefaultContextId)
                             ?? Scene.Contexts.FirstOrDefault();
                if (target != null) await EnterContextAsync(target);
                return;
            }

            await AdvanceAsync();
        }

        /// <summary>
        /// Linear: triggers the next step (no-op past the last step).
        /// Contextual: cycles to the next context.
        /// </summary>
        public async Task AdvanceAsync()
        {
            if (Scene.Mode == DASceneMode.Contextual)
            {
                if (Scene.Contexts.Count == 0) return;
                int index = CurrentContext is { } c ? Scene.Contexts.IndexOf(c) : -1;
                await EnterContextAsync(Scene.Contexts[(index + 1) % Scene.Contexts.Count]);
                return;
            }

            if (CurrentStepIndex + 1 >= Scene.Steps.Count) return;
            CurrentStepIndex++;
            IsPlaying = true;

            var step = Scene.Steps[CurrentStepIndex];
            var ct = _sessionCts.Token;
            foreach (var command in step.Commands)
                _ = RunCommandAsync(command, ct);

            StateChanged?.Invoke();
        }

        /// <summary>
        /// Jumps to a specific step. Intermediate steps are applied with their
        /// delays skipped and fades shortened so the scene lands in the right
        /// state quickly; the target step plays with full fidelity.
        /// </summary>
        public async Task GoToStepAsync(int stepIndex)
        {
            if (Scene.Mode == DASceneMode.Contextual)
            {
                if (stepIndex >= 0 && stepIndex < Scene.Contexts.Count)
                    await EnterContextAsync(Scene.Contexts[stepIndex]);
                return;
            }

            if (stepIndex < 0 || stepIndex >= Scene.Steps.Count) return;

            if (stepIndex <= CurrentStepIndex || !IsPlaying)
            {
                // Backwards (or not started): restart silently and replay up to the target.
                await StopAsync(0.3f);
                _sessionCts = new CancellationTokenSource();
                CurrentStepIndex = -1;
                IsPlaying = true;
            }

            var ct = _sessionCts.Token;
            while (CurrentStepIndex < stepIndex - 1 && !ct.IsCancellationRequested)
            {
                CurrentStepIndex++;
                foreach (var command in Scene.Steps[CurrentStepIndex].Commands)
                    _ = RunCommandAsync(Compress(command), ct);
            }
            if (!ct.IsCancellationRequested)
                await AdvanceAsync();
        }

        // ── Contextual mode ──────────────────────────────────────────────────

        /// <summary>
        /// Smoothly transitions the scene into the given context: every layer is
        /// concurrently crossfaded to its state in the target context (start /
        /// ramp / fade-out), and SFX random loops are started or stopped.
        /// Also starts the scene when called while stopped.
        /// </summary>
        public async Task EnterContextAsync(DASceneContext context)
        {
            if (!Scene.Contexts.Contains(context)) return;

            if (!IsPlaying)
            {
                _sessionCts = new CancellationTokenSource();
                IsPlaying = true;
            }
            _currentContextId = context.Id;
            var sessionCt = _sessionCts.Token;
            double fade = Math.Max(0, context.TransitionSeconds);
            StateChanged?.Invoke();

            var transitions = new List<Task>();
            foreach (var layer in Scene.Layers)
            {
                var state = context.LayerStates.FirstOrDefault(s => s.LayerId == layer.Id);
                bool active = state?.Active ?? false;
                double volume = Math.Clamp(state?.Volume ?? 0, 0, 1);

                if (layer.Role == DALayerRole.SoundEffect)
                {
                    // SFX layers: context controls the random one-shot loop.
                    lock (_lock) _sfxVolumeOverride[layer.Id] = volume;
                    if (active)
                    {
                        if (!IsRandomLoopRunning(layer.Id))
                            StartRandomLoop(layer, sessionCt);
                    }
                    else
                    {
                        StopRandomLoop(layer.Id);
                    }
                    continue;
                }

                // Ambience/Music layers: crossfade to the target volume.
                var ct = ReplaceLayerRamp(layer.Id, sessionCt);

                DAAudioPlayer? player;
                lock (_lock) _layerPlayers.TryGetValue(layer.Id, out player);

                if (active && player != null)
                {
                    transitions.Add(player.RampVolumeAsync(volume, fade, ct));
                }
                else if (active)
                {
                    transitions.Add(StartLayerAsync(layer, fade, volume, ct));
                }
                else if (player != null)
                {
                    var toStop = player;
                    var layerId = layer.Id;
                    transitions.Add(Task.Run(async () =>
                    {
                        await toStop.RampVolumeAsync(0, fade, ct);
                        if (ct.IsCancellationRequested) return;   // a newer context reclaimed this layer
                        lock (_lock)
                        {
                            if (_layerPlayers.TryGetValue(layerId, out var current) && current == toStop)
                                _layerPlayers.Remove(layerId);
                        }
                        toStop.Dispose();
                    }));
                }
            }

            try { await Task.WhenAll(transitions); }
            catch (TaskCanceledException) { /* superseded by a newer transition */ }
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Applies a layer volume immediately to the running scene (editor slider
        /// while previewing). Ambience/Music: sets the active player's volume;
        /// SoundEffect: updates the one-shot override used by the next one-shots.
        /// Does not touch the persisted context state — the caller owns that.
        /// </summary>
        public void SetLayerVolumeLive(DASceneLayer layer, double volume)
        {
            volume = Math.Clamp(volume, 0.0, 1.0);

            if (layer.Role == DALayerRole.SoundEffect)
            {
                lock (_lock) _sfxVolumeOverride[layer.Id] = volume;
                return;
            }

            DAAudioPlayer? player;
            lock (_lock)
            {
                // Cancel any in-flight crossfade ramp so it doesn't keep
                // overwriting the value we are about to set.
                if (_layerRamps.TryGetValue(layer.Id, out var ramp))
                {
                    ramp.Cancel();
                    _layerRamps.Remove(layer.Id);
                }
                _layerPlayers.TryGetValue(layer.Id, out player);
            }
            if (player != null)
                player.Volume = volume;
        }

        /// <summary>Cancels the layer's previous volume ramp and issues a token for the new one.</summary>
        private CancellationToken ReplaceLayerRamp(Guid layerId, CancellationToken sessionCt)
        {
            lock (_lock)
            {
                if (_layerRamps.TryGetValue(layerId, out var old)) old.Cancel();
                var cts = CancellationTokenSource.CreateLinkedTokenSource(sessionCt);
                _layerRamps[layerId] = cts;
                return cts.Token;
            }
        }

        private static DASceneCommand Compress(DASceneCommand c) => new()
        {
            Type = c.Type,
            LayerId = c.LayerId,
            DelaySeconds = 0,
            DurationSeconds = Math.Min(c.DurationSeconds, 0.5),
            TargetVolume = c.TargetVolume,
        };

        /// <summary>Fades out all layers, cancels pending commands and random loops.</summary>
        public async Task StopAsync(float fadeSeconds = StopFadeSeconds)
        {
            _sessionCts.Cancel();

            List<DAAudioPlayer> layers;
            List<DAAudioPlayer> shots;
            lock (_lock)
            {
                layers = _layerPlayers.Values.ToList();
                _layerPlayers.Clear();
                shots = _oneShots.ToList();
                _oneShots.Clear();
                foreach (var cts in _randomLoops.Values) cts.Cancel();
                _randomLoops.Clear();
                foreach (var cts in _layerRamps.Values) cts.Cancel();
                _layerRamps.Clear();
                _sfxVolumeOverride.Clear();
            }
            _currentContextId = null;

            await Task.WhenAll(layers.Concat(shots).Select(async p =>
            {
                await p.FadeOutAndStopAsync(fadeSeconds);
                p.Dispose();
            }));

            CurrentStepIndex = -1;
            IsPlaying = false;
            StateChanged?.Invoke();
        }

        // ── Command execution ────────────────────────────────────────────────

        private async Task RunCommandAsync(DASceneCommand command, CancellationToken ct)
        {
            try
            {
                if (command.DelaySeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(command.DelaySeconds), ct);
                if (ct.IsCancellationRequested) return;

                var layer = Scene.Layers.FirstOrDefault(l => l.Id == command.LayerId);
                if (layer == null) return;

                switch (command.Type)
                {
                    case DACommandType.StartLayer:
                        await StartLayerAsync(layer, 0, command.TargetVolume, ct);
                        break;
                    case DACommandType.FadeInLayer:
                        await StartLayerAsync(layer, command.DurationSeconds, command.TargetVolume, ct);
                        break;
                    case DACommandType.StopLayer:
                        await StopLayerAsync(layer.Id, 0);
                        break;
                    case DACommandType.FadeOutLayer:
                        await StopLayerAsync(layer.Id, command.DurationSeconds);
                        break;
                    case DACommandType.SetVolume:
                        {
                            DAAudioPlayer? player;
                            lock (_lock) _layerPlayers.TryGetValue(layer.Id, out player);
                            if (player != null)
                                await player.RampVolumeAsync(command.TargetVolume, command.DurationSeconds, ct);
                            break;
                        }
                    case DACommandType.StartRandom:
                        StartRandomLoop(layer, ct);
                        break;
                    case DACommandType.StopRandom:
                        StopRandomLoop(layer.Id);
                        break;
                }

                StateChanged?.Invoke();
            }
            catch (TaskCanceledException) { /* scene stopped while waiting */ }
        }

        private async Task StartLayerAsync(DASceneLayer layer, double fadeSeconds, double targetVolume, CancellationToken ct)
        {
            await StopLayerAsync(layer.Id, 0);   // replace any previous instance

            var player = CreatePlayerFor(layer.Audio);
            if (player == null) return;

            lock (_lock) _layerPlayers[layer.Id] = player;
            await player.FadeInAndPlayAsync(Math.Clamp(targetVolume, 0.0, 1.0), fadeSeconds, ct);
        }

        private async Task StopLayerAsync(Guid layerId, double fadeSeconds)
        {
            DAAudioPlayer? player;
            lock (_lock)
            {
                if (!_layerPlayers.TryGetValue(layerId, out player)) return;
                _layerPlayers.Remove(layerId);
            }
            await player.FadeOutAndStopAsync((float)fadeSeconds);
            player.Dispose();
        }

        // ── Random one-shot loops (e.g. dog barks, thunder) ──────────────────

        private void StartRandomLoop(DASceneLayer layer, CancellationToken sessionCt)
        {
            StopRandomLoop(layer.Id);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(sessionCt);
            lock (_lock) _randomLoops[layer.Id] = cts;

            _ = Task.Run(async () =>
            {
                var ct = cts.Token;
                try
                {
                    bool first = true;
                    while (!ct.IsCancellationRequested)
                    {
                        if (!(first && layer.FireImmediatelyOnStart))
                        {
                            double min = Math.Max(0.1, layer.RandomMinSeconds);
                            double max = Math.Max(min, layer.RandomMaxSeconds);
                            double wait = min + Random.Shared.NextDouble() * (max - min);
                            await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                        }
                        first = false;

                        PlayOneShot(layer);
                        StateChanged?.Invoke();

                        if (!layer.RandomRepeat) break;
                    }
                }
                catch (TaskCanceledException) { }
            });
        }

        private void StopRandomLoop(Guid layerId)
        {
            lock (_lock)
            {
                if (_randomLoops.TryGetValue(layerId, out var cts))
                {
                    cts.Cancel();
                    _randomLoops.Remove(layerId);
                }
            }
        }

        /// <summary>Plays one overlapping instance of the layer's audio (SFX may stack).</summary>
        private void PlayOneShot(DASceneLayer layer)
        {
            var player = CreatePlayerFor(layer.Audio);
            if (player == null) return;

            // In contextual mode the active context dictates the SFX loudness.
            double volume;
            lock (_lock)
            {
                volume = _sfxVolumeOverride.TryGetValue(layer.Id, out var v)
                    ? v
                    : layer.Audio.Volume;
            }

            player.Loop = false;   // one-shots never loop
            lock (_lock) _oneShots.Add(player);

            player.PlaybackEnded += (_, _) =>
            {
                lock (_lock) _oneShots.Remove(player);
                player.Dispose();
            };

            if (layer.Audio.FadeInSeconds > 0)
                _ = player.FadeInAndPlayAsync(volume, layer.Audio.FadeInSeconds);
            else
            {
                player.Volume = volume;
                player.Play();
            }
        }

        private DAAudioPlayer? CreatePlayerFor(DAAudioUsage usage)
        {
            var item = _service.AudioItems.FirstOrDefault(a => a.Id == usage.AudioItemId);
            if (item == null) return null;

            var cachePath = DAScenarioItemPlayer.GetPlayableCachePath(item.LocalFileName);
            if (cachePath == null) return null;

            try { return DAAudioPlayer.Create(cachePath, usage, _audioManager, _bus); }
            catch { return null; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sessionCts.Cancel();
            lock (_lock)
            {
                foreach (var cts in _randomLoops.Values) cts.Cancel();
                _randomLoops.Clear();
                foreach (var cts in _layerRamps.Values) cts.Cancel();
                _layerRamps.Clear();
                _sfxVolumeOverride.Clear();
                foreach (var p in _layerPlayers.Values) p.Dispose();
                _layerPlayers.Clear();
                foreach (var p in _oneShots) p.Dispose();
                _oneShots.Clear();
            }
            IsPlaying = false;
        }
    }
}
