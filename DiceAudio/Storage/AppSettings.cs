/*
 * Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Shared by DiceAudio and DiceCombats, both released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */
// Mirrored in DiceCombats/DiceCombats/Storage/AppSettings.cs — keep both in sync.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiceAudio.Storage;

/// <summary>
/// The user's preferences that travel with the data (spec §6): settings.json
/// in the data folder, saved through SafeFileStore. The machine-only ones stay
/// in ILocalPreferences. The app gives the list of keys that moved.
/// </summary>
public sealed class AppSettings
{
    public const string FileName = "settings.json";
    public const string FormatName = "Dice.Settings";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly SafeFileStore _store;
    private readonly string _appName;
    private readonly object _gate = new();
    private Dictionary<string, JsonElement> _values = new(StringComparer.Ordinal);

    public AppSettings(SafeFileStore store, string appName)
    {
        _store = store;
        _appName = appName;
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
                Debug.WriteLine($"Setting '{key}' could not be read: {ex.Message}");
                return defaultValue;
            }
        }
    }

    /// <summary>Changes a setting and saves settings.json; the write finishes in the background.</summary>
    public void Set<T>(string key, T value)
    {
        lock (_gate)
        {
            _values[key] = JsonSerializer.SerializeToElement(value);
        }
        _ = SaveAsync();
    }

    /// <summary>True once settings.json has been read (or created).</summary>
    public bool IsInitialized { get; private set; }

    public bool ContainsKey(string key)
    {
        lock (_gate)
        {
            return _values.ContainsKey(key);
        }
    }

    /// <summary>
    /// Reads settings.json. When it does not exist yet, each moved key the
    /// local preferences hold is copied into it, once; the local values stay,
    /// so that an older version of the app still finds them. A file that
    /// cannot be read leaves the settings empty, and is never saved over.
    /// </summary>
    public async Task InitializeAsync(ILocalPreferences migrateFrom, IReadOnlyList<string> movedKeys)
    {
        IsInitialized = true;
        var result = await _store.LoadTextAsync(FileName);
        if (result.Outcome == LoadOutcome.Loaded)
        {
            var values = Parse(result.Value!);
            if (values == null)
            {
                _store.Block(FileName, "settings.json is not a settings file of this version");
                return;
            }
            lock (_gate) { _values = values; }
            return;
        }
        if (result.Outcome == LoadOutcome.Failed)
        {
            return;
        }

        var migrated = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (string key in movedKeys)
        {
            if (!migrateFrom.ContainsKey(key)) continue;
            // The type is not known here: read as text first, then as a
            // boolean, an integer, a number.
            migrated[key] = ReadLocal(migrateFrom, key);
        }
        lock (_gate) { _values = migrated; }
        await SaveAsync();
    }

    /// <summary>Reads settings.json again, after a restore. A file now missing gives empty settings.</summary>
    public async Task ReloadAsync()
    {
        var result = await _store.LoadTextAsync(FileName);
        if (result.Outcome == LoadOutcome.Missing)
        {
            lock (_gate) { _values = new(StringComparer.Ordinal); }
        }
        else if (result.Outcome == LoadOutcome.Loaded && Parse(result.Value!) is { } values)
        {
            lock (_gate) { _values = values; }
        }
    }

    private Task<SaveOutcome> SaveAsync()
    {
        JsonObject root;
        lock (_gate)
        {
            var valuesNode = new JsonObject();
            foreach (var (key, value) in _values.OrderBy(v => v.Key, StringComparer.Ordinal))
            {
                valuesNode[key] = JsonNode.Parse(value.GetRawText());
            }
            root = new JsonObject
            {
                ["Format"] = FormatName,
                ["Version"] = CurrentVersion,
                ["App"] = _appName,
                ["Values"] = valuesNode,
            };
        }
        return _store.SaveTextAsync(FileName, root.ToJsonString(WriteOptions), overwriteExternalChange: true);
    }

    // Null when the file is another format, another app's or a newer version.
    private Dictionary<string, JsonElement>? Parse(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root) return null;
            if ((string?)root["Format"] != FormatName) return null;
            if ((string?)root["App"] != _appName) return null;
            int version = root["Version"] is JsonNode v ? v.GetValue<int>() : 1;
            if (version > CurrentVersion) return null;

            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (root["Values"] is JsonObject node)
            {
                foreach (var (key, value) in node)
                {
                    values[key] = JsonSerializer.SerializeToElement(value);
                }
            }
            return values;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static JsonElement ReadLocal(ILocalPreferences prefs, string key)
    {
        // Preferences keep a type per key and refuse to read it as another one;
        // a wrong type returns the default given, which tells them apart.
        const string marker = "\u0001not-a-string";
        string text = SafeGet(prefs, key, marker);
        if (text != marker) return JsonSerializer.SerializeToElement(text);
        if (SafeGet(prefs, key, false) || !SafeGet(prefs, key, true)) return JsonSerializer.SerializeToElement(SafeGet(prefs, key, false));
        if (SafeGet(prefs, key, int.MinValue) is int i && i != int.MinValue) return JsonSerializer.SerializeToElement(i);
        return JsonSerializer.SerializeToElement(SafeGet(prefs, key, 0d));
    }

    private static T SafeGet<T>(ILocalPreferences prefs, string key, T defaultValue)
    {
        try { return prefs.Get(key, defaultValue); }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or ArgumentException or JsonException) { return defaultValue; }
    }
}
