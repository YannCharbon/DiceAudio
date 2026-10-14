/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiceAudio.Portability
{
    /// <summary>The kinds of asset a DiceAudio bundle carries (spec §3.2).</summary>
    public enum DAAssetKind { Tag, Folder, AudioItem, Scene, LocalScene, ScenarioItem, Scenario, ScenarioGroup, Preset }

    /// <summary>The <see cref="BundleRoot.Kind"/> values of a DiceAudio bundle (spec §2.3).</summary>
    public static class DARootKind
    {
        public const string AudioItem = "AudioItem";
        public const string Folder = "Folder";
        public const string Scene = "Scene";
        public const string LocalScene = "LocalScene";
        public const string ScenarioItem = "ScenarioItem";
        public const string Scenario = "Scenario";
        public const string ScenarioGroup = "ScenarioGroup";
        public const string Collection = "Collection";
        public const string Preset = "Preset";
        public const string Library = "Library";
    }

    /// <summary>The <see cref="BundleWarning.Code"/> values a DiceAudio export writes (spec §2.3).</summary>
    public static class DAWarningCode
    {
        /// <summary>An exported asset refers to an audio item or local scene that no longer exists. Detail = the referring asset's name.</summary>
        public const string BrokenReference = "BrokenReference";

        /// <summary>An item with no link whose file is not in the media pack. Detail = the item's name.</summary>
        public const string NoSourceNoFile = "NoSourceNoFile";

        /// <summary>Reserved.</summary>
        public const string LocalSceneFlattened = "LocalSceneFlattened";
    }

    /// <summary>A tag and the category list it is in: Type, Location, Expression, or Unknown (in no list).</summary>
    public sealed class DATagRecord
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "Unknown";
    }

    /// <summary>A folder. <see cref="Path"/> ("SWTOR / Combat") is for display only; folders are identified by name.</summary>
    public sealed class DAFolderRecord
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public Guid? ParentId { get; set; }
        public string Path { get; set; } = "";
    }

    /// <summary>
    /// An audio item as it travels (spec §2.3): what describes it, never where
    /// its file is on this computer.
    /// </summary>
    public sealed class DAAudioItemRecord
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        /// <summary>The item's folder; null = the root.</summary>
        public Guid? FolderId { get; set; }
        public AudioKind Kind { get; set; }
        public List<string> Tags { get; set; } = new();
        public double Volume { get; set; } = 1.0;
        public bool Loop { get; set; } = true;
        public int FadeInDuration { get; set; }
        public int StartDelay { get; set; }
        public string SourceURL { get; set; } = "";
        /// <summary>The bare file name on the exporting computer, a hint for matching; never a path.</summary>
        public string? OriginalFileName { get; set; }
        public double? DurationSeconds { get; set; }
        public long? SizeBytes { get; set; }
    }

    /// <summary>A scenario exported without its group.</summary>
    public sealed class DAScenarioRecord
    {
        public Guid SourceGroupId { get; set; }
        public string SourceGroupName { get; set; } = "";
        public DAScenario Scenario { get; set; } = new();
    }

    /// <summary>A scenario item exported without its scenario.</summary>
    public sealed class DAScenarioItemRecord
    {
        public Guid SourceGroupId { get; set; }
        public string SourceGroupName { get; set; } = "";
        public Guid SourceScenarioId { get; set; }
        public string SourceScenarioName { get; set; } = "";
        public DAScenarioItem Item { get; set; } = new();
    }

    /// <summary>A group-local scene exported without its group.</summary>
    public sealed class DALocalSceneRecord
    {
        public Guid SourceGroupId { get; set; }
        public string SourceGroupName { get; set; } = "";
        public DAScene Scene { get; set; } = new();
    }

    /// <summary>Describes the optional .diceamedia file that goes with a bundle (A6).</summary>
    public sealed class DAMediaPackInfo
    {
        public string FileName { get; set; } = "";
        public int Entries { get; set; }
        public long TotalBytes { get; set; }
        public bool HasChecksums { get; set; }
    }

    /// <summary>
    /// A DiceAudio export (<c>.dicea</c>, spec §2.1 and §2.3): JSON, never any
    /// audio data and never a local path.
    /// </summary>
    public sealed class DABundle
    {
        public const string FormatName = "DiceAudio.Bundle";
        public const int CurrentVersion = 1;
        public const string FileExtension = ".dicea";

        public string Format { get; set; } = FormatName;
        public int FormatVersion { get; set; } = CurrentVersion;
        public string AppVersion { get; set; } = "";
        public Guid BundleId { get; set; } = Guid.NewGuid();
        public string ExportedAtUtc { get; set; } = DateTime.UtcNow.ToString("o");
        public List<BundleRoot> Roots { get; set; } = new();

        public List<DATagRecord> Tags { get; set; } = new();
        public List<DAFolderRecord> Folders { get; set; } = new();
        public List<DAAudioItemRecord> AudioItems { get; set; } = new();
        public List<DAScene> Scenes { get; set; } = new();
        public List<DAScenarioGroup> ScenarioGroups { get; set; } = new();
        public List<DAScenarioRecord> Scenarios { get; set; } = new();
        public List<DAScenarioItemRecord> ScenarioItems { get; set; } = new();
        public List<DALocalSceneRecord> LocalScenes { get; set; } = new();
        public List<DAPreset> Presets { get; set; } = new();
        public DAMediaPackInfo? MediaPack { get; set; }

        public List<BundleWarning> Warnings { get; set; } = new();

        /// <summary>
        /// The service's write options (indented, enums by name), so a bundle
        /// reads like the data files.
        /// </summary>
        public static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public string Serialize() => JsonSerializer.Serialize(this, WriteOptions);

        /// <summary>Writes the bundle as UTF-8 JSON, without a BOM.</summary>
        public Task WriteAsync(Stream stream, CancellationToken ct = default) =>
            JsonSerializer.SerializeAsync(stream, this, WriteOptions, ct);
    }
}
