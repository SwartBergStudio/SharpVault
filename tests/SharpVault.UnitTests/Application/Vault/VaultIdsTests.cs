// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Vault.Common;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class VaultIdsTests
{
    [Fact]
    public void DeletionBatchIdFor_SameFolder_IsStable()
    {
        var folder = Guid.NewGuid();

        Assert.Equal(VaultIds.DeletionBatchIdFor(folder), VaultIds.DeletionBatchIdFor(folder));
    }

    [Fact]
    public void DeletionBatchIdFor_DifferentFolders_Differ()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => VaultIds.DeletionBatchIdFor(Guid.NewGuid())).Distinct().Count();

        Assert.Equal(1000, ids);
    }

    [Fact]
    public void DeletionBatchIdFor_IsNeverEmptyAndNeverTheFolderId()
    {
        var folder = Guid.NewGuid();

        var batch = VaultIds.DeletionBatchIdFor(folder);

        Assert.NotEqual(Guid.Empty, batch);
        Assert.NotEqual(folder, batch);
    }
}
