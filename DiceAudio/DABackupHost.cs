/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using DiceAudio.Storage;
using System.Diagnostics;

namespace DiceAudio
{
    /// <summary>What backups and restores need from the running app.</summary>
    public sealed class DABackupHost : IBackupHost
    {
        /// <summary>How long a restore waits for a cancelled download to wind down.</summary>
        private static readonly TimeSpan DownloadStopTimeout = TimeSpan.FromSeconds(5);

        private readonly DiceAudioService _service;
        private readonly DAPlaybackCoordinator _playback;
        private readonly DADownloadQueue _downloads;
        private readonly DAControlServer _controlServer;
        private readonly DAStorageNotices _notices;
        private readonly AppSettings _settings;
        private readonly DAExternalChanges _external;
        private IDisposable? _watchPause;

        /// <summary>The port the remote-control server ran on before a restore paused it; null when it was not running.</summary>
        private int? _pausedServerPort;

        public DABackupHost(DiceAudioService service, DAPlaybackCoordinator playback, DADownloadQueue downloads,
                            DAControlServer controlServer, DAStorageNotices notices, AppSettings settings, DAExternalChanges external)
        {
            _external = external;
            _settings = settings;
            _service = service;
            _playback = playback;
            _downloads = downloads;
            _controlServer = controlServer;
            _notices = notices;
        }

        public string AppVersion => _service.GetVersion();

        public Task SaveEverythingAsync() => _service.SaveEverythingAsync();

        /// <summary>
        /// Every scenario stops, the downloads stop (and the restore waits for
        /// the one running to wind down, so that it cannot add an item in the
        /// middle), and the remote-control server pauses, so that no other
        /// program starts playback while the data is replaced.
        /// </summary>
        public async Task StopActivityAsync()
        {
            // The restore replaces the files: that is not a change made elsewhere.
            _watchPause ??= _external.PauseWatching();

            _downloads.CancelAll();
            var idle = _downloads.WhenIdleAsync();
            if (await Task.WhenAny(idle, Task.Delay(DownloadStopTimeout)) != idle)
                Debug.WriteLine("The downloads did not stop within 5 s; the restore goes on.");

            if (_controlServer.IsRunning)
            {
                _pausedServerPort = _controlServer.Port;
                _controlServer.Stop();
            }

            await _playback.StopAllAsync();
        }

        /// <summary>The remote-control server starts again if the restore paused it.</summary>
        public Task ResumeActivityAsync()
        {
            _watchPause?.Dispose();
            _watchPause = null;
            if (_pausedServerPort is int port)
            {
                _pausedServerPort = null;
                _controlServer.Start(port);
            }
            return Task.CompletedTask;
        }

        public async Task ReloadAsync()
        {
            await _service.ReloadAsync();
            // settings.json too: a load that works lifts its restore block.
            await _settings.ReloadAsync();
        }

        public void ReportLoadFailures() => _notices.ReportLoadFailures();
    }
}
