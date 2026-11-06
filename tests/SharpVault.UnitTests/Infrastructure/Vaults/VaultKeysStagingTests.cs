// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Infrastructure.Vaults;

namespace SharpVault.UnitTests.Infrastructure.Vaults;

[Trait("Category", "Security")]
public sealed class VaultKeysStagingTests
{
    private static readonly Guid TransferId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid ItemId = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid VersionId = Guid.Parse("00000000-0000-0000-0000-0000000000c3");

    [Fact]
    public void StagedRowKeys_AreOutsideEveryReadRange()
    {
        var staged = new[]
        {
            VaultKeys.StagedFolderRowKey(TransferId, ItemId),
            VaultKeys.StagedSecretRowKey(TransferId, ItemId),
            VaultKeys.StagedSkippedRowKey(TransferId, ItemId),
            VaultKeys.StagedVersionRowKey(TransferId, ItemId, VersionId),
        };

        var inside = staged.Where(key => InsideAnyReadRange(key)).ToList();

        Assert.Empty(inside);
    }

    [Fact]
    public void FenceRow_IsOutsideEveryReadRange()
    {
        var fence = VaultKeys.StagingFenceRowKey(TransferId);
        var lower = VaultKeys.StagingRangeStart(TransferId);

        Assert.False(InsideAnyReadRange(fence));
        Assert.False(InRange(fence, lower, VaultKeys.UpperBound(lower)));
    }

    [Fact]
    public void StagingRange_CoversOneTransferOnly()
    {
        var lower = VaultKeys.StagingRangeStart(TransferId);
        var upper = VaultKeys.UpperBound(lower);
        var other = VaultKeys.StagedSecretRowKey(Guid.NewGuid(), ItemId);

        Assert.True(string.CompareOrdinal(lower, VaultKeys.StagedSecretRowKey(TransferId, ItemId)) <= 0);
        Assert.True(string.CompareOrdinal(VaultKeys.StagedSecretRowKey(TransferId, ItemId), upper) < 0);
        Assert.False(string.CompareOrdinal(lower, other) <= 0 && string.CompareOrdinal(other, upper) < 0);
    }

    private static bool InsideAnyReadRange(string key) =>
        InRange(key, VaultKeys.RecycleBinLowerBound, VaultKeys.RecycleBinUpperBound)
        || InRange(key, VaultKeys.InventoryLowerBound, VaultKeys.GrantRangeLowerBound)
        || InRange(key, VaultKeys.GrantRangeUpperBound, VaultKeys.InventoryUpperBound)
        || InRange(key, VaultKeys.FolderPrefix, VaultKeys.UpperBound(VaultKeys.FolderPrefix))
        || InRange(key, VaultKeys.SecretPrefix, VaultKeys.UpperBound(VaultKeys.SecretPrefix))
        || InRange(key, VaultKeys.VersionPrefix, VaultKeys.UpperBound(VaultKeys.VersionPrefix))
        || InRange(key, VaultKeys.GrantByItemPrefix, VaultKeys.UpperBound(VaultKeys.GrantByItemPrefix))
        || InRange(key, VaultKeys.GrantByRecipientPrefix, VaultKeys.UpperBound(VaultKeys.GrantByRecipientPrefix));

    private static bool InRange(string key, string lower, string upper) =>
        string.CompareOrdinal(key, lower) >= 0 && string.CompareOrdinal(key, upper) < 0;
}
