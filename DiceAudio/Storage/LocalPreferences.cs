/*
 * Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Shared by DiceAudio and DiceCombats, both released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */
// Mirrored in DiceCombats/DiceCombats/Storage/LocalPreferences.cs — keep both in sync.

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DiceAudio.Storage;

/// <summary>
/// Preferences that belong to this machine only: the data folder pointer, the
/// update-check cache, the last version seen. User preferences that travel
/// with the data go to settings.json instead.
/// </summary>
public interface ILocalPreferences
{
    T Get<T>(string key, T defaultValue);
    void Set<T>(string key, T value);
    bool ContainsKey(string key);
}

public static class LocalPreferences
{
    /// <summary>File that replaces the platform preferences in a debug run on a sandbox.</summary>
    public const string DebugFileName = ".dice-debug-preferences.json";

    /// <summary>
    /// The platform preferences, or, when a debug override folder is given, a
    /// file inside that folder, so that a debug run never reads or changes the
    /// real preferences of this machine.
    /// </summary>
    public static ILocalPreferences Create(string? debugOverride, Func<ILocalPreferences> platformPreferences)
    {
        if (!string.IsNullOrWhiteSpace(debugOverride))
        {
            return new JsonFileLocalPreferences(Path.Combine(debugOverride, DebugFileName));
        }
        return platformPreferences();
    }
}

/// <summary>
/// Local preferences kept in a small JSON file. Used for debug runs on a
/// sandbox folder; the file is never created outside the folder it is given.
/// </summary>
public sealed class JsonFileLocalPreferences : ILocalPreferences
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, JsonElement> _values;

    public JsonFileLocalPreferences(string path)
    {
        _path = path;
        _values = Read(path);
    }

    public T Get<T>(string key, T defaultValue)
    {
        lock (_gate)
        {
            if (!_values.TryGetValue(key, out var element)) return defaultValue;
            try
            {
                var value = element.Deserialize<T>();
                return value is null ? defaultValue : value;
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                Debug.WriteLine($"Local preference '{key}' could not be read: {ex.Message}");
                return defaultValue;
            }
        }
    }

    public void Set<T>(string key, T value)
    {
        lock (_gate)
        {
            _values[key] = JsonSerializer.SerializeToElement(value);
            Write();
        }
    }

    public bool ContainsKey(string key)
    {
        lock (_gate)
        {
            return _values.ContainsKey(key);
        }
    }

    private static Dictionary<string, JsonElement> Read(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));
                if (values != null) return new Dictionary<string, JsonElement>(values, StringComparer.Ordinal);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Local preferences file {path} could not be read: {ex.Message}");
        }
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }

    // Called under _gate. Written to a temporary file first, so that a crash
    // never leaves half a file behind. The folder is never created here.
    private void Write()
    {
        try
        {
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_values, WriteOptions), new UTF8Encoding(false));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Local preferences file {_path} could not be written: {ex.Message}");
        }
    }
}
