/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using DiceAudio;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace DiceAudio
{
    public class FileHandler : IFileHandler
    {
        public async Task SaveFileAsync(string fileName, byte[] data)
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };

            picker.FileTypeChoices.Add("All Files", new[] { "." });
            picker.SuggestedFileName = fileName;

            var hwnd = ((MauiWinUIWindow)App.Current.Windows[0].Handler.PlatformView).WindowHandle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                using var stream = await file.OpenStreamForWriteAsync();
                stream.SetLength(0); // Clears the file content before writing new data
                await stream.WriteAsync(data, 0, data.Length);
            }
        }

        public async Task<bool> SaveStreamAsync(string suggestedName, string extension, string typeLabel, Func<Stream, Task> write)
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
            };
            picker.FileTypeChoices.Add(typeLabel, new List<string> { extension });

            var hwnd = ((MauiWinUIWindow)App.Current!.Windows[0].Handler!.PlatformView!).WindowHandle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return false;

            await using var stream = await file.OpenStreamForWriteAsync();
            stream.SetLength(0); // an existing file chosen in the picker is replaced, not patched
            await write(stream);
            await stream.FlushAsync();
            return true;
        }

        public async Task<DAPickedFile?> PickFileAsync(string extension, string typeLabel)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add(extension);

            var hwnd = ((MauiWinUIWindow)App.Current!.Windows[0].Handler!.PlatformView!).WindowHandle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return null;

            var stream = await file.OpenStreamForReadAsync();
            return new DAPickedFile(file.Name, string.IsNullOrEmpty(file.Path) ? null : file.Path, stream);
        }

        public async Task<byte[]> LoadFileAsync()
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };

            picker.FileTypeFilter.Add("*");

            var hwnd = ((MauiWinUIWindow)App.Current.Windows[0].Handler.PlatformView).WindowHandle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                using (var stream = await file.OpenStreamForReadAsync())
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        await stream.CopyToAsync(memoryStream);
                        return memoryStream.ToArray();
                    }
                }
            }

            return null;
        }
    }
}
