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
    /// <summary>A usage resolved against the audio library.</summary>
    public sealed record DAResolvedTrack(DAAudioUsage Usage, DAAudioItem Item);

    public class DAScenarioItemPlayer : IDisposable
    {
        public enum PlayState { Stop, Play, Pause, Finished }

        private readonly IAudioManager _audioManager;
        private readonly DiceAudioService _service;
        public readonly DAScenarioItem Item;

        private DAAudioPlayer? _currentPlayer;
        private readonly List<DAAudioPlayer?> _soundPlayers = new();
        private List<DAResolvedTrack> _resolvedPlaylist = new();
        private List<DAResolvedTrack> _resolvedSounds = new();
        private int _currentIndex = -1;
        private bool _disposed;

        public PlayState State { get; private set; } = PlayState.Stop;
        public event EventHandler<PlayState>? PlayStateChanged;
        public event EventHandler? PlayProgressionChanged;

        /// <summary>Drives the layered scene when the item is of type Scene.</summary>
        public DAScenePlayer? ScenePlayer { get; private set; }

        private bool IsSceneItem => Item.Type == DAScenarioItem.ItemType.Scene;

        private readonly System.Timers.Timer _progressTimer = new(500);

        public double CurrentPosition => _currentPlayer?.CurrentPosition ?? 0;
        public double Duration => _currentPlayer?.Duration ?? 0;

        // Duration to display before playback starts: the live player's when
        // playing, otherwise the probed length of the first track (-1 = not yet probed).
        private double _probedDuration = -1;
        public double DisplayDuration => _currentPlayer?.Duration ?? (_probedDuration > 0 ? _probedDuration : 0);

        /// <summary>
        /// Probes the first track's (clip-aware) length so the UI can show a real
        /// duration before playback. No-op for scenes and once already probed/playing.
        /// </summary>
        public async Task EnsureProbedDurationAsync()
        {
            if (_currentPlayer != null || _probedDuration >= 0 || IsSceneItem) return;
            _probedDuration = 0;   // mark attempted so we don't re-probe on every render

            var usage = Item.PlaylistTracks.FirstOrDefault();
            if (usage == null) return;
            var audioItem = _service.AudioItems.FirstOrDefault(a => a.Id == usage.AudioItemId);
            if (audioItem == null || string.IsNullOrEmpty(audioItem.LocalFileName)) return;

            var cachePath = GetPlayableCachePath(audioItem.LocalFileName);
            if (cachePath == null) return;

            _probedDuration = await Task.Run(() =>
            {
                double full = DAAudioPlayer.ProbeDurationSeconds(cachePath, _audioManager);
                double start = Math.Clamp(usage.ClipStartSeconds ?? 0, 0, full);
                double end = Math.Clamp(usage.ClipEndSeconds ?? full, 0, full);
                return Math.Max(0, end - start);
            });
        }
        public DAAudioItem? CurrentAudioItem =>
            (_currentIndex >= 0 && _currentIndex < _resolvedPlaylist.Count) ? _resolvedPlaylist[_currentIndex].Item : null;
        public DAAudioItem? NextAudioItem =>
            (_currentIndex + 1 < _resolvedPlaylist.Count) ? _resolvedPlaylist[_currentIndex + 1].Item : null;
        public IReadOnlyList<DAResolvedTrack> ResolvedSounds => _resolvedSounds;

        public DAScenarioItemPlayer(IAudioManager audioManager, DiceAudioService service, DAScenarioItem item)
        {
            _audioManager = audioManager;
            _service = service;
            Item = item;
            _progressTimer.Elapsed += (_, _) => PlayProgressionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Called after the item definition may have been edited: drops resolved
        /// content so it is re-resolved on the next play. No-op while playing so
        /// edits never interrupt active playback.
        /// </summary>
        public void InvalidateResolvedContent()
        {
            if (State == PlayState.Play || State == PlayState.Pause) return;
            _resolvedPlaylist.Clear();
            _currentIndex = -1;
            _probedDuration = -1;   // re-probe: the first track may have changed
            ResolveSounds();

            // The embedded scene object may have been replaced (preset insert).
            if (ScenePlayer != null && !ReferenceEquals(ScenePlayer.Scene, Item.Scene))
            {
                ScenePlayer.StateChanged -= OnSceneStateChanged;
                ScenePlayer.Dispose();
                ScenePlayer = null;
            }
        }

        private DAScenePlayer? EnsureScenePlayer()
        {
            if (Item.Scene == null) return null;
            if (ScenePlayer == null)
            {
                ScenePlayer = new DAScenePlayer(_audioManager, _service, Item.Scene);
                ScenePlayer.StateChanged += OnSceneStateChanged;
            }
            return ScenePlayer;
        }

        private void OnSceneStateChanged() => PlayStateChanged?.Invoke(this, State);

        public void ResolvePlaylist()
        {
            if (Item.Type == DAScenarioItem.ItemType.RandomWithCriteria)
            {
                _resolvedPlaylist = BuildRandomPlaylist();
            }
            else
            {
                _resolvedPlaylist = ResolveUsages(Item.PlaylistTracks);

                if (Item.PlaylistOrderIsRandom && Item.Type == DAScenarioItem.ItemType.Playlist)
                    _resolvedPlaylist = _resolvedPlaylist.OrderBy(_ => Random.Shared.Next()).ToList();
            }

            ResolveSounds();
        }

        private void ResolveSounds()
        {
            _resolvedSounds = ResolveUsages(Item.SoundEffects);

            while (_soundPlayers.Count < _resolvedSounds.Count) _soundPlayers.Add(null);
            while (_soundPlayers.Count > _resolvedSounds.Count)
            {
                int last = _soundPlayers.Count - 1;
                _soundPlayers[last]?.Dispose();
                _soundPlayers.RemoveAt(last);
            }
        }

        private List<DAResolvedTrack> ResolveUsages(List<DAAudioUsage> usages) =>
            usages
                .Select(u => (Usage: u, Item: _service.AudioItems.FirstOrDefault(a => a.Id == u.AudioItemId)))
                .Where(t => t.Item != null)
                .Select(t => new DAResolvedTrack(t.Usage, t.Item!))
                .ToList();

        /// <summary>
        /// Returns a playable cached path for the audio file, copying from the app
        /// data directory only when the cache copy is missing or outdated.
        /// Returns null when the source file is unavailable.
        /// </summary>
        internal static string? GetPlayableCachePath(string localFileName)
        {
            if (string.IsNullOrEmpty(localFileName)) return null;

            var srcPath = Path.Combine(FileSystem.AppDataDirectory, "Audio", localFileName);
            if (!File.Exists(srcPath)) return null;

            var cachePath = Path.Combine(FileSystem.CacheDirectory, localFileName);
            var srcInfo = new FileInfo(srcPath);
            var cacheInfo = new FileInfo(cachePath);
            if (!cacheInfo.Exists || cacheInfo.Length != srcInfo.Length)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                File.Copy(srcPath, cachePath, overwrite: true);
            }
            return cachePath;
        }

        public void Play()
        {
            if (IsSceneItem)
            {
                var scene = EnsureScenePlayer();
                if (scene == null) return;
                _ = scene.StartAsync();
                State = PlayState.Play;
                PlayStateChanged?.Invoke(this, State);
                return;
            }

            if (State == PlayState.Pause && _currentPlayer != null)
            {
                _currentPlayer.Play();
                State = PlayState.Play;
                _progressTimer.Start();
                PlayStateChanged?.Invoke(this, State);
                return;
            }

            if (_resolvedPlaylist.Count == 0) ResolvePlaylist();
            if (_resolvedPlaylist.Count == 0) return;

            if (_currentIndex < 0) _currentIndex = 0;
            PlayCurrent();
        }

        public void Pause()
        {
            if (IsSceneItem) return;   // scenes don't pause — use Stop or Advance

            _currentPlayer?.Pause();
            _progressTimer.Stop();
            State = PlayState.Pause;
            PlayStateChanged?.Invoke(this, State);
        }

        public void Stop()
        {
            if (IsSceneItem)
            {
                _ = ScenePlayer?.StopAsync(0.3f);
                State = PlayState.Stop;
                PlayStateChanged?.Invoke(this, State);
                return;
            }

            _progressTimer.Stop();
            DetachAndDisposeCurrent();
            StopAllSounds();
            _currentIndex = -1;
            State = PlayState.Stop;
            PlayStateChanged?.Invoke(this, State);
        }

        public async Task StopWithFadeAsync(float fadeSeconds = DAScenarioPlayer.HandoverFadeSeconds)
        {
            if (IsSceneItem)
            {
                if (ScenePlayer != null)
                    await ScenePlayer.StopAsync(fadeSeconds);
                State = PlayState.Stop;
                PlayStateChanged?.Invoke(this, State);
                return;
            }

            _progressTimer.Stop();
            await FadeAndDetachCurrentAsync(fadeSeconds);
            StopAllSounds();
            _currentIndex = -1;
            State = PlayState.Stop;
            PlayStateChanged?.Invoke(this, State);
        }

        /// <summary>Advances the embedded scene to its next step/context (Scene items only).</summary>
        public Task AdvanceSceneAsync() => ScenePlayer?.AdvanceAsync() ?? Task.CompletedTask;

        /// <summary>Jumps the embedded contextual scene to a specific context (starts the scene if needed).</summary>
        public Task EnterSceneContextAsync(DASceneContext context)
        {
            var scene = EnsureScenePlayer();
            if (scene == null) return Task.CompletedTask;
            MarkScenePlaying();
            return scene.EnterContextAsync(context);
        }

        /// <summary>Jumps the embedded scene to a cue index — step (linear) or context (contextual).</summary>
        public Task GoToSceneCueAsync(int cueIndex)
        {
            var scene = EnsureScenePlayer();
            if (scene == null) return Task.CompletedTask;
            MarkScenePlaying();
            return scene.GoToStepAsync(cueIndex);
        }

        private void MarkScenePlaying()
        {
            if (State != PlayState.Play)
            {
                State = PlayState.Play;
                PlayStateChanged?.Invoke(this, State);
            }
        }

        public async Task SkipForwardAsync()
        {
            if (_currentIndex < _resolvedPlaylist.Count - 1)
            {
                await FadeAndDetachCurrentAsync();
                _currentIndex++;
                PlayCurrent();
            }
        }

        public async Task SkipBackwardAsync()
        {
            if (_currentIndex > 0)
            {
                await FadeAndDetachCurrentAsync();
                _currentIndex--;
                PlayCurrent();
            }
        }

        public void Seek(double seconds) => _currentPlayer?.Seek(seconds);

        public void PlaySound(int soundIndex)
        {
            if (soundIndex < 0 || soundIndex >= _resolvedSounds.Count) return;
            StopSound(soundIndex);

            var (usage, audio) = _resolvedSounds[soundIndex];

            try
            {
                var cachePath = GetPlayableCachePath(audio.LocalFileName);
                if (cachePath == null) return;

                var player = DAAudioPlayer.Create(cachePath, usage, _audioManager);
                player.Play();
                _soundPlayers[soundIndex] = player;
            }
            catch { /* skip if audio fails */ }
        }

        public void StopSound(int soundIndex)
        {
            if (soundIndex < 0 || soundIndex >= _soundPlayers.Count) return;
            _soundPlayers[soundIndex]?.Dispose();
            _soundPlayers[soundIndex] = null;
        }

        public bool IsSoundPlaying(int soundIndex)
        {
            if (soundIndex < 0 || soundIndex >= _soundPlayers.Count) return false;
            return _soundPlayers[soundIndex]?.IsPlaying ?? false;
        }

        private void StopAllSounds()
        {
            for (int i = 0; i < _soundPlayers.Count; i++)
            {
                _soundPlayers[i]?.Dispose();
                _soundPlayers[i] = null;
            }
        }

        private void PlayCurrent()
        {
            DetachAndDisposeCurrent();

            if (_currentIndex < 0 || _currentIndex >= _resolvedPlaylist.Count) return;

            var (usage, audioItem) = _resolvedPlaylist[_currentIndex];

            try
            {
                var cachePath = GetPlayableCachePath(audioItem.LocalFileName);
                if (cachePath == null) return;

                _currentPlayer = DAAudioPlayer.Create(cachePath, usage, _audioManager);
                // In a scenario, looping is an item-level setting (a looping playlist
                // advances between tracks; only a Single item loops its one track).
                _currentPlayer.Loop = Item.PlayInLoop && Item.Type == DAScenarioItem.ItemType.Single;
                _currentPlayer.PlaybackEnded += OnPlaybackEnded;

                if (usage.FadeInSeconds > 0)
                    _ = _currentPlayer.FadeInAndPlayAsync(usage.Volume, usage.FadeInSeconds);
                else
                    _currentPlayer.Play();

                State = PlayState.Play;
                _progressTimer.Start();
                PlayStateChanged?.Invoke(this, State);
            }
            catch
            {
                State = PlayState.Stop;
                PlayStateChanged?.Invoke(this, State);
            }
        }

        private void OnPlaybackEnded(object? sender, EventArgs e)
        {
            bool isLast = _currentIndex >= _resolvedPlaylist.Count - 1;

            if (Item.PlayInLoop && Item.Type != DAScenarioItem.ItemType.Single)
            {
                _currentIndex = (_currentIndex + 1) % _resolvedPlaylist.Count;
                PlayCurrent();
            }
            else if (!isLast)
            {
                _currentIndex++;
                PlayCurrent();
            }
            else
            {
                _currentIndex = -1;
                _progressTimer.Stop();
                State = PlayState.Finished;
                PlayStateChanged?.Invoke(this, State);
            }
        }

        private async Task FadeAndDetachCurrentAsync(float fadeSeconds = DAScenarioPlayer.HandoverFadeSeconds)
        {
            // Claim the player synchronously (before the await) and null the field
            // immediately. Otherwise a second stop/handover arriving while the fade
            // is still running — e.g. the coordinator re-fading this item because a
            // manual stop hasn't flipped State to Stop yet — would pass the null
            // check too and double-dispose, NRE-ing on the now-null _currentPlayer.
            var player = _currentPlayer;
            if (player == null) return;
            _currentPlayer = null;

            player.PlaybackEnded -= OnPlaybackEnded;
            await player.FadeOutAndStopAsync(fadeSeconds);
            player.Dispose();
        }

        private void DetachAndDisposeCurrent()
        {
            if (_currentPlayer != null)
            {
                _currentPlayer.PlaybackEnded -= OnPlaybackEnded;
                _currentPlayer.Dispose();
                _currentPlayer = null;
            }
        }

        private List<DAResolvedTrack> BuildRandomPlaylist()
        {
            var matching = _service.AudioItems
                .Where(a => a.IsLocallyAvailable && a.Tags.Count > 0)
                .ToList();

            foreach (var criteria in Item.RandomPlayCriterias)
            {
                if (criteria.Condition == DARandomPlayCriteria.ConditionType.Not)
                    matching.RemoveAll(a => a.Tags.Any(t => criteria.TagNames.Contains(t.Name)));
                else if (criteria.Condition == DARandomPlayCriteria.ConditionType.And)
                    matching = matching.Where(a => criteria.TagNames.All(tn => a.Tags.Any(t => t.Name == tn))).ToList();
                else if (criteria.Condition == DARandomPlayCriteria.ConditionType.Or)
                    matching = matching.Where(a => criteria.TagNames.Any(tn => a.Tags.Any(t => t.Name == tn))).ToList();
            }

            return matching
                .OrderBy(_ => Random.Shared.Next())
                .Select(a => new DAResolvedTrack(new DAAudioUsage(a.Id), a))
                .ToList();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _progressTimer.Dispose();
            DetachAndDisposeCurrent();
            StopAllSounds();
            if (ScenePlayer != null)
            {
                ScenePlayer.StateChanged -= OnSceneStateChanged;
                ScenePlayer.Dispose();
                ScenePlayer = null;
            }
        }
    }
}
