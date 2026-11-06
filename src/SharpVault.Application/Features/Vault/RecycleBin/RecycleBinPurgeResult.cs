// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Vault.RecycleBin;

public sealed record RecycleBinPurgeResult(bool Skipped, int UsersScanned, int SecretsPurged, int FoldersPurged, int Conflicts, int GrantsErased = 0)
{
    public static RecycleBinPurgeResult NotRun { get; } = new(true, 0, 0, 0, 0, 0);
}
