/*
 * Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Shared by DiceAudio and DiceCombats, both released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */
// Mirrored in DiceCombats/DiceCombats/Portability/ImportTypes.cs — keep both in sync.

namespace DiceAudio.Portability;

/// <summary>
/// What the user chose to export (spec §2.1). Everything else in a bundle is
/// a dependency of these. <see cref="Name"/> is used only by root kinds
/// without a GUID (a collection, a field template), <see cref="IncludeSubfolders"/>
/// only by a folder; <c>Kind = "Library"</c> with no Id is the whole library.
/// </summary>
public sealed class BundleRoot
{
    public string Kind { get; set; } = "";
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public bool? IncludeSubfolders { get; set; }
}

/// <summary>Something the exporter wants the importer to know (spec §2.1), shown on import.</summary>
public sealed class BundleWarning
{
    public string Code { get; set; } = "";
    public string? Kind { get; set; }
    public Guid? Id { get; set; }
    public string? Detail { get; set; }
}

/// <summary>How an imported asset compares with the library (workplan §1.1, spec §4.1).</summary>
public enum ImportStatus { New, UpToDate, Differs, NameUsed }

/// <summary>What to do with an imported asset (spec §4.0).</summary>
public enum ImportChoice
{
    Nothing,            // Up to date: no question
    Import,             // New
    Skip,               // New, Name already used
    ImportAsCopy,       // Differs (default)
    ReplaceLocal,       // Differs
    KeepLocal,          // Differs
    Merge,              // Differs, containers only
    ImportWithNewName,  // Name already used (default)
    KeepBothSameName,   // Name already used
    UseLocal,           // Name already used
}

/// <summary>One asset of the file, how it compares with the library, and what the user chose for it.</summary>
public sealed class ImportItem<TKind> where TKind : struct, Enum
{
    public required TKind Kind { get; init; }
    /// <summary>The bundle object (a record for audio items, folders and tags).</summary>
    public required object Incoming { get; init; }
    /// <summary>The matching local object, if any. Changes with the target of items that need one.</summary>
    public object? Local { get; set; }
    /// <summary>Null for kinds identified by name.</summary>
    public Guid? IncomingId { get; init; }
    public required string IncomingName { get; init; }
    /// <summary>Set by the planner; changes when a target is chosen (the scope of the name changes).</summary>
    public required ImportStatus Status { get; set; }
    public required IReadOnlyList<ImportChoice> AllowedChoices { get; set; }
    public ImportChoice Choice { get; set; }
    /// <summary>Required when <see cref="Choice"/> is ImportAsCopy or ImportWithNewName.</summary>
    public string? NewName { get; set; }
    /// <summary>Set on the children created by a Merge.</summary>
    public ImportItem<TKind>? Parent { get; init; }
    public List<ImportItem<TKind>> Children { get; } = new();
    /// <summary>Items that must go into a container the user chooses (a group or a scenario); false for everything else.</summary>
    public bool NeedsTarget { get; init; }
    /// <summary>The chosen container; <see cref="Guid.Empty"/> = create a new one. Null = not chosen yet.</summary>
    public Guid? TargetContainerId { get; set; }
    /// <summary>The name of that new container.</summary>
    public string? TargetContainerNewName { get; set; }

    /// <summary>True when the choice puts something into the library (as opposed to skipping or keeping the local one).</summary>
    public bool Inserts => Choice is ImportChoice.Import or ImportChoice.ImportAsCopy or ImportChoice.ReplaceLocal
                                  or ImportChoice.ImportWithNewName or ImportChoice.KeepBothSameName or ImportChoice.Merge;

    /// <summary>True when the choice needs <see cref="NewName"/>.</summary>
    public bool NeedsNewName => Choice is ImportChoice.ImportAsCopy or ImportChoice.ImportWithNewName;
}

public sealed class ImportPlan<TKind> where TKind : struct, Enum
{
    public List<ImportItem<TKind>> Items { get; } = new();
    public List<BundleWarning> Warnings { get; } = new();

    /// <summary>Every item, the children of merged containers included.</summary>
    public IEnumerable<ImportItem<TKind>> AllItems => Items.SelectMany(Flatten);

    private static IEnumerable<ImportItem<TKind>> Flatten(ImportItem<TKind> item) =>
        new[] { item }.Concat(item.Children.SelectMany(Flatten));

    /// <summary>True when the user has something to decide: the wizard is shown instead of a single confirmation.</summary>
    public bool HasConflicts => AllItems.Any(i => i.Status is ImportStatus.Differs or ImportStatus.NameUsed
                                                  || (i.TargetContainerId == null && i.NeedsTarget));
}

public sealed record ImportReport(int Added, int Replaced, int KeptLocal, int Merged, int Skipped, int UpToDate,
                                  IReadOnlyList<string> Problems);
