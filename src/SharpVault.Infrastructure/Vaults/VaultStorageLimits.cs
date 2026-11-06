// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Infrastructure.Vaults;

public static class VaultStorageLimits
{
    public const int MaxActionsPerTransaction = 100;
    public const int MaxTransactionPayloadBytes = 3 * 1024 * 1024;
    public const int ActionOverheadBytes = 2048;
    public const int MaxStorageRequestsPerListCall = 20;
    public const int FolderPageSize = 1000;
}
