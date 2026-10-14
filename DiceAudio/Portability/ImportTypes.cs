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
