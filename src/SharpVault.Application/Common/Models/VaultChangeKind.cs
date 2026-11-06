// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Common.Models;

public enum VaultChangeKind
{
    AddSecret = 1,
    ReplaceSecret = 2,
    RecycleSecret = 3,
    RestoreSecret = 4,
    PurgeSecret = 5,
    AddVersion = 6,
    DeleteVersion = 7,
    AddFolder = 8,
    ReplaceFolder = 9,
    RecycleFolder = 10,
    RestoreFolder = 11,
    PurgeFolder = 12,
    TouchFolder = 13,
    TouchKeyRing = 14,
    AddShareGrant = 15,
    ReplaceShareGrant = 16,
    DeleteShareGrant = 17,
    DeleteShareGrantByItem = 18,
    RemoveSecret = 19,
    RemoveRecycledSecret = 20,
    RemoveFolder = 21,
    RemoveRecycledFolder = 22,
}
