// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;
using SharpVault.Infrastructure.Vaults;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Infrastructure.Vaults;

public sealed class VaultTransactionPlannerShareGrantTests
{
    private const string ByItemETag = "W/\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"";
    private const string ByRecipientETag = "W/\"datetime'2026-03-01T09%3A00%3A01.0000000Z'\"";

    private readonly Guid _owner = Guid.NewGuid();

    private ShareGrant Grant(ShareItemKind kind = ShareItemKind.Secret) => ShareSamples.Grant(_owner, kind);

    private static string ByItemKey(ShareGrant grant) => VaultKeys.GrantByItemRowKey(grant.ItemKind, grant.ItemId, grant.RecipientId);

    private static string ByRecipientKey(ShareGrant grant) => VaultKeys.GrantByRecipientRowKey(grant.RecipientId, grant.ItemKind, grant.ItemId);

    [Fact]
    public void Plan_AddShareGrant_AddsBothRowsInOneChunkWithTheByItemRowPrimary()
    {
        var grant = Grant();

        var plan = Assert.Single(VaultTransactionPlanner.Plan(_owner, [VaultChange.AddShareGrant(grant)]));

        Assert.Equal(2, plan.Actions.Count);
        Assert.All(plan.Actions, action => Assert.Equal(TableTransactionActionType.Add, action.ActionType));
        Assert.Equal(ByItemKey(grant), plan.Actions[0].Entity.RowKey);
        Assert.Equal(ByRecipientKey(grant), plan.Actions[1].Entity.RowKey);
        Assert.All(plan.Actions, action => Assert.Equal(_owner.ToString("D"), action.Entity.PartitionKey));
        Assert.Equal([0], plan.PrimaryActionIndexes);
    }

    [Fact]
    public void Plan_ReplaceShareGrant_ReplacesBothRowsEachWithItsOwnETag()
    {
        var grant = Grant(ShareItemKind.Folder);

        var plan = Assert.Single(VaultTransactionPlanner.Plan(_owner, [VaultChange.ReplaceShareGrant(grant, ByItemETag, ByRecipientETag)]));

        Assert.All(plan.Actions, action => Assert.Equal(TableTransactionActionType.UpdateReplace, action.ActionType));
        Assert.Equal(ByItemKey(grant), plan.Actions[0].Entity.RowKey);
        Assert.Equal(new ETag(ByItemETag), plan.Actions[0].ETag);
        Assert.Equal(ByRecipientKey(grant), plan.Actions[1].Entity.RowKey);
        Assert.Equal(new ETag(ByRecipientETag), plan.Actions[1].ETag);
        Assert.Equal([0], plan.PrimaryActionIndexes);
    }

    [Fact]
    public void Plan_DeleteShareGrant_DeletesBothRowsEachWithItsOwnETagAndHasNoPrimary()
    {
        var grant = Grant();

        var plan = Assert.Single(VaultTransactionPlanner.Plan(_owner, [VaultChange.DeleteShareGrant(grant, ByItemETag, ByRecipientETag)]));

        Assert.All(plan.Actions, action => Assert.Equal(TableTransactionActionType.Delete, action.ActionType));
        Assert.Equal(ByItemKey(grant), plan.Actions[0].Entity.RowKey);
        Assert.Equal(new ETag(ByItemETag), plan.Actions[0].ETag);
        Assert.Equal(ByRecipientKey(grant), plan.Actions[1].Entity.RowKey);
        Assert.Equal(new ETag(ByRecipientETag), plan.Actions[1].ETag);
        Assert.Equal([(int?)null], plan.PrimaryActionIndexes);
    }

    [Fact]
    public void Plan_DeleteShareGrantByItem_DeletesOnlyTheByItemRowWithItsETag()
    {
        var grant = Grant();

        var plan = Assert.Single(VaultTransactionPlanner.Plan(_owner, [VaultChange.DeleteShareGrantByItem(grant, ByItemETag)]));

        var action = Assert.Single(plan.Actions);
        Assert.Equal(TableTransactionActionType.Delete, action.ActionType);
        Assert.Equal(ByItemKey(grant), action.Entity.RowKey);
        Assert.Equal(new ETag(ByItemETag), action.ETag);
        Assert.Equal([(int?)null], plan.PrimaryActionIndexes);
    }

    [Fact]
    public void Plan_ReplaceShareGrant_NeverUsesTheWildcardETag()
    {
        var plan = Assert.Single(VaultTransactionPlanner.Plan(_owner, [VaultChange.ReplaceShareGrant(Grant(), ByItemETag, ByRecipientETag)]));

        Assert.All(plan.Actions, action => Assert.NotEqual(ETag.All, action.ETag));
    }

    [Theory]
    [InlineData(VaultChangeKind.AddShareGrant)]
    [InlineData(VaultChangeKind.ReplaceShareGrant)]
    [InlineData(VaultChangeKind.DeleteShareGrant)]
    [Trait("Category", "Security")]
    public void Plan_ShareGrantOfOtherOwner_Throws(VaultChangeKind kind)
    {
        var foreign = ShareSamples.Grant(Guid.NewGuid());
        var change = kind switch
        {
            VaultChangeKind.AddShareGrant => VaultChange.AddShareGrant(foreign),
            VaultChangeKind.ReplaceShareGrant => VaultChange.ReplaceShareGrant(foreign, ByItemETag, ByRecipientETag),
            _ => VaultChange.DeleteShareGrant(foreign, ByItemETag, ByRecipientETag),
        };

        Assert.Throws<ArgumentException>(() => VaultTransactionPlanner.Plan(_owner, [change]));
    }

    [Fact]
    public void Plan_ShareGrantPairAtTheActionLimit_MovesTheWholePairToTheNextChunk()
    {
        var changes = Enumerable.Range(0, 99).Select(_ => VaultChange.AddSecret(VaultSamples.Secret(_owner))).ToList();
        var grant = Grant();
        changes.Add(VaultChange.AddShareGrant(grant));

        var plan = VaultTransactionPlanner.Plan(_owner, changes);

        Assert.Equal(2, plan.Count);
        Assert.Equal(99, plan[0].Actions.Count);
        Assert.Equal([ByItemKey(grant), ByRecipientKey(grant)], plan[1].Actions.Select(action => action.Entity.RowKey));
    }

    [Fact]
    public void Plan_FiftyGrantPairs_FitOneChunkAndFiftyOneSplitsWithoutBreakingAPair()
    {
        var fifty = Enumerable.Range(0, 50).Select(_ => VaultChange.AddShareGrant(Grant())).ToList();
        var fiftyOne = Enumerable.Range(0, 51).Select(_ => VaultChange.AddShareGrant(Grant())).ToList();

        var one = VaultTransactionPlanner.Plan(_owner, fifty);
        var two = VaultTransactionPlanner.Plan(_owner, fiftyOne);

        Assert.Single(one);
        Assert.Equal(100, one[0].Actions.Count);
        Assert.Equal(2, two.Count);
        Assert.Equal(100, two[0].Actions.Count);
        Assert.Equal(2, two[1].Actions.Count);
        Assert.Single(two[1].ChangeIndexes);
    }

    [Fact]
    public void Plan_ShareGrantWithKeyRingTouch_KeepsEachPairTogetherInEveryChunk()
    {
        var changes = new List<VaultChange> { VaultChange.TouchKeyRing(ByItemETag) };
        changes.AddRange(Enumerable.Range(0, 60).Select(_ => VaultChange.AddShareGrant(Grant())));

        var plan = VaultTransactionPlanner.Plan(_owner, changes);

        Assert.Equal(2, plan.Count);
        Assert.All(plan, chunk => Assert.Equal(0, (chunk.Actions.Count - 1) % 2));
        Assert.All(plan, chunk => Assert.Equal(TableTransactionActionType.UpdateMerge, chunk.Actions[0].ActionType));
    }

    [Fact]
    public void Plan_ShareGrantRows_AreNeverInTheSecretFolderOrBinKeyRanges()
    {
        var grant = Grant();

        var plan = Assert.Single(VaultTransactionPlanner.Plan(_owner, [VaultChange.AddShareGrant(grant)]));

        Assert.All(plan.Actions, action => Assert.Matches("^g[ir]_", action.Entity.RowKey));
    }
}
