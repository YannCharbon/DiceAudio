/*
 * Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Shared by DiceAudio and DiceCombats, both released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */
// Mirrored in DiceCombats/DiceCombats/Storage/DataFolderLock.cs — keep both in sync.

namespace DiceAudio.Storage;

/// <summary>
/// The content of a dice.lock file (spec §5.5). Only the type exists for now,
/// because DataLocation reports another session's lock through it; writing,
/// reading and refreshing the lock come with the support for several PCs.
/// </summary>
public sealed class DataFolderLockInfo
{
    public string Format { get; init; } = "Dice.Lock";
    public int Version { get; init; } = 1;
    public string App { get; init; } = "";
    public Guid SessionId { get; init; }
    public string Machine { get; init; } = "";
    public int ProcessId { get; init; }
    public string AppVersion { get; init; } = "";
    public DateTime StartedUtc { get; init; }
    public DateTime HeartbeatUtc { get; init; }
}
