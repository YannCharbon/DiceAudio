/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using DiceAudio.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DiceAudio
{
    public sealed class DAAudioTagsProvider
    {
        private static readonly DAAudioTagsProvider instance = new DAAudioTagsProvider();

        public const string TypeFileName = "TagsCategoryType.json";
        public const string LocationFileName = "TagsCategoryLocation.json";
        public const string ExpressionFileName = "TagsCategoryExpression.json";

        /// <summary>The three tag files, in the order they are loaded.</summary>
        public static readonly IReadOnlyList<string> FileNames = new[] { TypeFileName, LocationFileName, ExpressionFileName };

        private static readonly List<string> _categoryTypeDefaultList = new List<string>
        {
            "Ambience", "Music", "SFX"
        };

        private static readonly List<string> _categoryLocationDefaultList = new List<string>
        {
            "Generic", "Forest", "Jungle", "Desert", "Plains", "Swamp",
            "Mountain", "Nature", "Industrial", "Storm", "Village", "City", "Medieval",
            "Futuristic", "Cold", "Hot", "Snow", "Ice", "Balroom", "Tavern",
            "Market", "Council", "Cave", "Sewer"
        };

        private static readonly List<string> _categoryExpressionDefaultList = new List<string>
        {
            "Calm", "Stressed", "Joy", "Energic", "Love", "Emotional",
            "Sad", "Mysterious", "Battle", "Trip", "Dark", "Heroic",
            "Dramatic", "Beautiful", "ViewReveal", "PlotReveal", "Noble", "Jazzy",
            "Comedy", "Dream", "Magical", "Classical", "Contemplation", "Adventure",
            "Desolation", "Desperate", "Hope", "Mystic", "Ethnic", "IntroTheme"
        };

        /// The options the tag files have always been written with, kept as they
        /// are so an unchanged list writes the same bytes and is not saved again.
        private static readonly JsonSerializerOptions TagFileOptions = new()
        {
            WriteIndented = true,
            DefaultBufferSize = 15 * 1024 * 1024 // 15MiB
        };

        public List<string> CategoryTypes = new List<string>();
        public List<string> CategoryLocations = new List<string>();
        public List<string> CategoryExpressions = new List<string>();

        private SafeFileStore? _store;

        /// Completes once <see cref="LoadAsync"/> has run. A save waits for it, so
        /// it can never write the empty lists over files not read yet.
        private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>True once the tag files have been read; the tags editor stays disabled until then.</summary>
        public bool IsLoaded => _loaded.Task.IsCompleted;

        /// <summary>Completes once the tag files have been read.</summary>
        public Task WhenLoaded => _loaded.Task;

        // Explicit static constructor to tell C# compiler
        // not to mark type as beforefieldinit
        static DAAudioTagsProvider()
        {
        }

        private DAAudioTagsProvider()
        {
        }

        /// <summary>
        /// Reads the three tag files, each on its own. A missing file starts from
        /// the default tags and is written; a file that cannot be read keeps the
        /// list in memory as it was, and is never saved over in this session.
        /// <see cref="DiceAudioService"/> calls this once while it loads.
        /// </summary>
        public async Task LoadAsync(SafeFileStore store)
        {
            _store = store;
            try
            {
                CategoryTypes = await LoadCategoryAsync(store, TypeFileName, _categoryTypeDefaultList, CategoryTypes).ConfigureAwait(false);
                CategoryLocations = await LoadCategoryAsync(store, LocationFileName, _categoryLocationDefaultList, CategoryLocations).ConfigureAwait(false);
                CategoryExpressions = await LoadCategoryAsync(store, ExpressionFileName, _categoryExpressionDefaultList, CategoryExpressions).ConfigureAwait(false);
            }
            finally
            {
                _loaded.TrySetResult();
            }
        }

        private static async Task<List<string>> LoadCategoryAsync(SafeFileStore store, string fileName, List<string> defaults, List<string> current)
        {
            var result = await store.LoadJsonAsync<List<string>>(fileName, TagFileOptions).ConfigureAwait(false);
            switch (result.Outcome)
            {
                case LoadOutcome.Loaded:
                    return result.Value!;

                case LoadOutcome.Missing:
                    var created = new List<string>(defaults);
                    try
                    {
                        await store.SaveJsonAsync(fileName, created, TagFileOptions).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Reported through SaveFailed; the next save tries again.
                        Debug.WriteLine($"Could not write the default tags to {fileName}: {ex.Message}");
                    }
                    return created;

                default:
                    // The file is blocked; the list in memory stays as it was
                    // (after a restore, the tags from before it). The defaults
                    // are only for a file that does not exist.
                    Debug.WriteLine($"Failed to load {fileName}: {result.Error}");
                    return current;
            }
        }

        public async Task SaveTagsAsync()
        {
            // No store means the load has not even started, and may never: waiting
            // for it would hang the caller (the close handler among them).
            if (_store == null) return;
            await _loaded.Task;

            // Each list is serialised before the next await, so the three files
            // are a snapshot of the same moment.
            var type = _store.SaveJsonAsync(TypeFileName, CategoryTypes, TagFileOptions, overwriteExternalChange: true);
            var location = _store.SaveJsonAsync(LocationFileName, CategoryLocations, TagFileOptions, overwriteExternalChange: true);
            var expression = _store.SaveJsonAsync(ExpressionFileName, CategoryExpressions, TagFileOptions, overwriteExternalChange: true);
            await Task.WhenAll(type, location, expression);
        }

        public static DAAudioTagsProvider Instance
        {
            get
            {
                return instance;
            }
        }
    }
}
