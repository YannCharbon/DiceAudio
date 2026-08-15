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
    /// Singleton owner of all live players. UI components borrow players from
    /// here instead of creating their own, so playback state survives page
    /// navigation and any surface (UI pages, the remote-control server) can
    /// observe and drive the same players.
    /// </summary>
    public class DAPlaybackCoordinator : IDisposable
    {
        private readonly IAudioManager _audioManager;
        private readonly DiceAudioService _service;

        private readonly Dictionary<Guid, DAScenarioPlayer> _scenarioPlayers = new();
        private bool _disposed;

        /// <summary>Raised whenever any owned player's state changes.</summary>
        public event Action? StateChanged;

        public DAPlaybackCoordinator(IAudioManager audioManager, DiceAudioService service)
        {
            _audioManager = audioManager;
            _service = service;
        }

        public DAScenarioPlayer GetOrCreateScenarioPlayer(DAScenario scenario)
        {
            if (_scenarioPlayers.TryGetValue(scenario.Id, out var existing))
            {
                if (ReferenceEquals(existing.Scenario, scenario))
                {
                    existing.Initialize();   // reconcile with possible edits
                    return existing;
                }
                // The scenario object was replaced (e.g. data reloaded): rebuild.
                RemovePlayer(existing);
            }

            var player = new DAScenarioPlayer(_audioManager, _service, scenario);
            player.StateChanged += OnPlayerStateChanged;
            player.Initialize();
            _scenarioPlayers[scenario.Id] = player;
            return player;
        }

        /// <summary>Stops and disposes the player of a deleted scenario.</summary>
        public void RemoveScenarioPlayer(DAScenario scenario)
        {
            if (_scenarioPlayers.TryGetValue(scenario.Id, out var player))
                RemovePlayer(player);
        }

        private void RemovePlayer(DAScenarioPlayer player)
        {
            player.StateChanged -= OnPlayerStateChanged;
            player.Stop();
            player.Dispose();
            _scenarioPlayers.Remove(player.Scenario.Id);
            StateChanged?.Invoke();
        }

        /// <summary>Scenario players that are currently audible.</summary>
        public IReadOnlyList<DAScenarioPlayer> ActiveScenarioPlayers =>
            _scenarioPlayers.Values.Where(p => p.IsPlaying).ToList();

        /// <summary>All players the coordinator currently owns.</summary>
        public IReadOnlyCollection<DAScenarioPlayer> ScenarioPlayers => _scenarioPlayers.Values;

        /// <summary>Fades out and stops everything that is playing.</summary>
        public async Task StopAllAsync()
        {
            await Task.WhenAll(_scenarioPlayers.Values
                .Where(p => p.IsPlaying)
                .Select(p => p.StopAsync()));
            StateChanged?.Invoke();
        }

        private void OnPlayerStateChanged() => StateChanged?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var p in _scenarioPlayers.Values)
            {
                p.StateChanged -= OnPlayerStateChanged;
                p.Dispose();
            }
            _scenarioPlayers.Clear();
        }
    }
}
