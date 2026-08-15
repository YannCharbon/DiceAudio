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
    /// Coordinates playback of the items within one scenario.
    /// This is the single authority over "which item is active": every play request
    /// (transport panel, item card, remote control) must go through it so that the
    /// previously active item is always faded out first.
    /// </summary>
    public class DAScenarioPlayer : IDisposable
    {
        /// <summary>Fade used when handing over from one scenario item to another.</summary>
        public const float HandoverFadeSeconds = 2.0f;

        private readonly IAudioManager _audioManager;
        private readonly DiceAudioService _service;
        public DAScenario Scenario { get; }

        private readonly Dictionary<Guid, DAScenarioItemPlayer> _itemPlayers = new();
        private Guid? _currentItemId;
        private bool _disposed;

        public bool IsPlaying { get; private set; }
        public event Action? StateChanged;

        public DAScenarioPlayer(IAudioManager audioManager, DiceAudioService service, DAScenario scenario)
        {
            _audioManager = audioManager;
            _service = service;
            Scenario = scenario;
        }

        public DAScenarioItem? CurrentItem =>
            _currentItemId is Guid id ? Scenario.Items.FirstOrDefault(i => i.Id == id) : null;

        public DAScenarioItemPlayer? CurrentItemPlayer =>
            _currentItemId is Guid id && _itemPlayers.TryGetValue(id, out var p) ? p : null;

        /// <summary>
        /// Reconciles the item players with the scenario definition without tearing
        /// down players that are still valid — so editing/adding items never
        /// interrupts what is currently playing.
        /// </summary>
        public void Initialize()
        {
            // Drop players whose item no longer exists
            var validIds = Scenario.Items.Select(i => i.Id).ToHashSet();
            foreach (var staleId in _itemPlayers.Keys.Where(k => !validIds.Contains(k)).ToList())
            {
                _itemPlayers[staleId].PlayStateChanged -= OnItemPlayStateChanged;
                _itemPlayers[staleId].Dispose();
                _itemPlayers.Remove(staleId);
                if (_currentItemId == staleId)
                {
                    _currentItemId = null;
                    IsPlaying = false;
                }
            }

            foreach (var item in Scenario.Items)
            {
                if (_itemPlayers.TryGetValue(item.Id, out var existing))
                {
                    // Item may have been edited: refresh its resolved content
                    // unless it is actively playing (change applies on next play).
                    existing.InvalidateResolvedContent();
                }
                else
                {
                    var player = new DAScenarioItemPlayer(_audioManager, _service, item);
                    player.PlayStateChanged += OnItemPlayStateChanged;
                    _itemPlayers[item.Id] = player;
                }
            }
        }

        public DAScenarioItemPlayer? GetItemPlayer(DAScenarioItem item)
        {
            _itemPlayers.TryGetValue(item.Id, out var p);
            return p;
        }

        /// <summary>
        /// Plays the given item, fading out (2 s) whatever else is active first.
        /// All play requests — transport panel and item cards alike — route here.
        /// </summary>
        public async Task PlayItemAsync(DAScenarioItem item)
        {
            if (!_itemPlayers.TryGetValue(item.Id, out var target)) return;

            // Fade out every other item that is currently audible.
            var others = _itemPlayers.Values
                .Where(p => p != target &&
                            (p.State == DAScenarioItemPlayer.PlayState.Play ||
                             p.State == DAScenarioItemPlayer.PlayState.Pause))
                .ToList();
            var fadeOut = Task.WhenAll(others.Select(p => p.StopWithFadeAsync(HandoverFadeSeconds)));

            // Scenes crossfade: start the next item while the scene fades out.
            // Plain items hand over sequentially (fade, then start).
            bool crossfade = others.Any(o => o.Item.Type == DAScenarioItem.ItemType.Scene)
                          || target.Item.Type == DAScenarioItem.ItemType.Scene;
            if (!crossfade)
                await fadeOut;
            else
                _ = fadeOut;   // let it complete in the background

            _currentItemId = item.Id;
            target.Play();
            IsPlaying = true;
            StateChanged?.Invoke();
        }

        /// <summary>Plays/resumes the current item (or the first one).</summary>
        public async Task PlayAsync()
        {
            var item = CurrentItem ?? Scenario.Items.FirstOrDefault();
            if (item == null) return;
            await PlayItemAsync(item);
        }

        public void Pause()
        {
            CurrentItemPlayer?.Pause();
            IsPlaying = false;
            StateChanged?.Invoke();
        }

        /// <summary>Fades out the current item, hard-stops any stragglers.</summary>
        public async Task StopAsync()
        {
            if (CurrentItemPlayer is { } current)
                await current.StopWithFadeAsync(HandoverFadeSeconds);
            foreach (var p in _itemPlayers.Values)
                p.Stop();
            _currentItemId = null;
            IsPlaying = false;
            StateChanged?.Invoke();
        }

        /// <summary>Immediate stop of everything (no fade).</summary>
        public void Stop()
        {
            foreach (var p in _itemPlayers.Values)
                p.Stop();
            _currentItemId = null;
            IsPlaying = false;
            StateChanged?.Invoke();
        }

        public Task PlayNextAsync() => PlayNeighborAsync(+1);
        public Task PlayPreviousAsync() => PlayNeighborAsync(-1);

        private async Task PlayNeighborAsync(int direction)
        {
            if (Scenario.Items.Count == 0) return;

            int currentIndex = CurrentItem is { } cur ? Scenario.Items.IndexOf(cur) : -1;
            int nextIndex = currentIndex + direction;

            if (nextIndex < 0) return;
            if (nextIndex >= Scenario.Items.Count)
            {
                // Ran past the last item: fade out and finish.
                await StopAsync();
                return;
            }

            await PlayItemAsync(Scenario.Items[nextIndex]);
        }

        private void OnItemPlayStateChanged(object? sender, DAScenarioItemPlayer.PlayState state)
        {
            if (state == DAScenarioItemPlayer.PlayState.Finished
                && IsPlaying
                && sender is DAScenarioItemPlayer p
                && p.Item.Id == _currentItemId)
            {
                // Natural end of the active item: hand over automatically.
                _ = PlayNextAsync();
            }
            else
            {
                StateChanged?.Invoke();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var p in _itemPlayers.Values)
            {
                p.PlayStateChanged -= OnItemPlayStateChanged;
                p.Dispose();
            }
            _itemPlayers.Clear();
        }
    }
}
