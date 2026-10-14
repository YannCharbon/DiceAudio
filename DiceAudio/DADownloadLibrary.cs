/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

namespace DiceAudio
{
    /// <summary>
    /// The library as the download queue sees it: <see cref="DiceAudioService"/>,
    /// with every change made on the UI thread. The pages read the lists there
    /// and the saves serialise them there, so a download that finishes in the
    /// background can no longer change a list while it is being written
    /// ("Collection was modified").
    /// </summary>
    public sealed class DADownloadLibrary : IDADownloadLibrary
    {
        private readonly DiceAudioService _service;

        public DADownloadLibrary(DiceAudioService service)
        {
            _service = service;
        }

        public bool CanWriteData => _service.CanWriteData;

        public string AudioDir => _service.AudioDir;

        public IReadOnlyList<DAAudioItem> AudioItems => _service.AudioItems;

        public void AddAudioItem(DAAudioItem item, DAVirtualAudioFolder? folder)
        {
            item.Folder = folder ?? DiceAudioService.NewRootFolderMarker();
            _service.AddAudioItem(item);
        }

        public async Task SaveItemsAndFoldersAsync()
        {
            await _service.SaveAudioItemListAsync();
            await _service.SaveAudioVirtualFoldersAsync();
        }

        public Task<T> OnUiAsync<T>(Func<T> read) => MainThread.InvokeOnMainThreadAsync(read);

        public Task OnUiAsync(Func<Task> action) => MainThread.InvokeOnMainThreadAsync(action);
    }
}
