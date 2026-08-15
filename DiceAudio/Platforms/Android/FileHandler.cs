/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using Android.App;
using Android.Content;
using DiceAudio;
using System.IO;
using System.Threading.Tasks;

namespace DiceAudio
{
    public class FileHandler : IFileHandler
    {
        public async Task SaveFileAsync(string fileName, byte[] data)
        {
            var context = Android.App.Application.Context;
            var externalPath = context.GetExternalFilesDir(null).AbsolutePath;
            var filePath = Path.Combine(externalPath, fileName);

            await File.WriteAllBytesAsync(filePath, data);

            var fileUri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, context.PackageName + ".fileprovider", new Java.IO.File(filePath));
            var intent = new Intent(Intent.ActionSend);
            intent.SetType("*/*");
            intent.PutExtra(Intent.ExtraStream, fileUri);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);

            var chooser = Intent.CreateChooser(intent, "Share File");
            chooser.SetFlags(ActivityFlags.NewTask);

            var activity = MainActivity.Instance;
            if (activity == null)
                throw new InvalidOperationException("Current activity is not available.");

            var taskCompletionSource = new TaskCompletionSource<bool>();

            MainActivity.ActivityResult += OnActivityResult;

            activity.StartActivityForResult(chooser, 2000);

            await taskCompletionSource.Task;

            void OnActivityResult(int requestCode, Result resultCode, Intent data)
            {
                if (requestCode == 2000)
                {
                    MainActivity.ActivityResult -= OnActivityResult;
                    taskCompletionSource.SetResult(resultCode == Result.Ok);
                }
            }
        }

        public async Task<byte[]> LoadFileAsync()
        {
            var intent = new Intent(Intent.ActionGetContent);
            intent.SetType("*/*");
            intent.AddCategory(Intent.CategoryOpenable);

            var activity = MainActivity.Instance;
            if (activity == null)
                throw new InvalidOperationException("Current activity is not available.");

            var taskCompletionSource = new TaskCompletionSource<byte[]>();

            MainActivity.ActivityResult += OnActivityResult;

            activity.StartActivityForResult(intent, 1000);

            return await taskCompletionSource.Task;

            void OnActivityResult(int requestCode, Result resultCode, Intent data)
            {
                if (requestCode == 1000)
                {
                    MainActivity.ActivityResult -= OnActivityResult;

                    if (resultCode == Result.Ok && data?.Data != null)
                    {
                        var uri = data.Data;
                        var stream = activity.ContentResolver.OpenInputStream(uri);

                        using (var memoryStream = new MemoryStream())
                        {
                            stream.CopyTo(memoryStream);
                            taskCompletionSource.SetResult(memoryStream.ToArray());
                        }
                    }
                    else
                    {
                        taskCompletionSource.SetResult(null);
                    }
                }
            }
        }
    }
}
