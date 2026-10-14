/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceAudio
{
    public interface IFileHandler
    {
        Task SaveFileAsync(string fileName, byte[] data);
        Task<byte[]> LoadFileAsync();

        /// <summary>
        /// Asks where to save a file of the given type, then lets
        /// <paramref name="write"/> fill it as a stream, so a large file is
        /// never held in memory. Returns false when the user cancels; throws
        /// when the file cannot be written.
        /// </summary>
        /// <param name="extension">With its dot, for example ".dicea".</param>
        Task<bool> SaveStreamAsync(string suggestedName, string extension, string typeLabel, Func<Stream, Task> write);

        /// <summary>
        /// Asks for a file of the given type. Null when the user cancels. The
        /// caller disposes the result, which closes the stream.
        /// </summary>
        Task<DAPickedFile?> PickFileAsync(string extension, string typeLabel);
    }

    /// <summary>A file the user picked, open for reading.</summary>
    public sealed class DAPickedFile : IAsyncDisposable
    {
        public DAPickedFile(string fileName, string? fullPath, Stream stream)
        {
            FileName = fileName;
            FullPath = fullPath;
            Stream = stream;
        }

        /// <summary>The file's name, without its folder.</summary>
        public string FileName { get; }

        /// <summary>Where the file is, when the platform says (Windows); used to look for a file next to it.</summary>
        public string? FullPath { get; }

        public Stream Stream { get; }

        public ValueTask DisposeAsync() => Stream.DisposeAsync();
    }
}
