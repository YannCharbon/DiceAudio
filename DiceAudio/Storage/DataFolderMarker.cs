/*
 * Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Shared by DiceAudio and DiceCombats, both released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */
// Mirrored in DiceCombats/DiceCombats/Storage/DataFolderMarker.cs — keep both in sync.

using System.Text.Json;

namespace DiceAudio.Storage;

public sealed class DataFolderWriter
{
    public string Machine { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string Utc { get; set; } = "";
}

/// <summary>
/// dice-data.json (spec §5.4): says which app's data a folder holds, which
/// library it is (LibraryId, created once and kept when the data moves), and
/// who wrote it last.
/// </summary>
public sealed class DataFolderMarker
{
    public const string FileName = "dice-data.json";
    public const string FormatName = "Dice.DataFolder";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = CurrentVersion;
    public string App { get; set; } = "";
    public Guid LibraryId { get; set; }
    public DataFolderWriter LastWriter { get; set; } = new();

    /// <summary>The marker of a folder; null when there is none; throws InvalidDataException when it cannot be read.</summary>
    public static DataFolderMarker? Read(string folder)
    {
        string path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) return null;
        try
        {
            var marker = JsonSerializer.Deserialize<DataFolderMarker>(File.ReadAllText(path), ReadOptions)
                         ?? throw new InvalidDataException("Empty marker");
            if (marker.Format != FormatName) throw new InvalidDataException($"Format '{marker.Format}'");
            return marker;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidDataException($"{FileName} cannot be read: {ex.Message}", ex);
        }
    }

    public static DataFolderMarker Create(string appName, Guid libraryId, string machine, string appVersion) => new()
    {
        App = appName,
        LibraryId = libraryId,
        LastWriter = new DataFolderWriter { Machine = machine, AppVersion = appVersion, Utc = DateTime.UtcNow.ToString("o") },
    };

    public string ToJson() => JsonSerializer.Serialize(this, WriteOptions);

    /// <summary>Writes the marker into a folder directly (a folder the app does not use yet), through a temporary file.</summary>
    public void WriteTo(string folder)
    {
        string path = Path.Combine(folder, FileName);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, path, overwrite: true);
    }
}
