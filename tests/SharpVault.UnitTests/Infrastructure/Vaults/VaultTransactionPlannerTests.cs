// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Vaults;
using SharpVault.Infrastructure.Vaults;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Infrastructure.Vaults;

public sealed class VaultTransactionPlannerTests
{
    private const string ETagText = "W/\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"";
    private const int OneMebibyte = 1024 * 1024;

    private readonly Guid _owner = Guid.NewGuid();

    private List<VaultChange> Adds(int count) =>
        Enumerable.Range(0, count).Select(_ => VaultChange.AddSecret(VaultSamples.Secret(_owner))).ToList();

    private static int EstimatedBytes(PlannedTransaction transaction) =>
        transaction.Actions.Sum(action => VaultStorageLimits.ActionOverheadBytes + PayloadOf(action.Entity));

    private static int PayloadOf(ITableEntity entity) =>
        entity is IBinaryPayload payload ? (int)(((long)payload.BinaryLength * 4 + 2) / 3) : 0;

    [Fact]
    public void Plan_Empty_ReturnsNoTransactions()
    {
        var plan = VaultTransactionPlanner.Plan(_owner, []);

        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_NullChanges_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => VaultTransactionPlanner.Plan(_owner, null!));
    }

    [Fact]
    public void Plan_RecycleSecret_ProducesDeleteThenAdd()
    {
        var recycled = VaultSamples.Recycled(_owner);

        var plan = Assert.Single(VaultTransactionPlanner.Plan(_owner, [VaultChange.RecycleSecret(recycled, ETagText)]));

        Assert.Equal(2, plan.Actions.Count);
        Assert.Equal(TableTransactionActionType.Delete, plan.Actions[0].ActionType);
        Assert.Equal("s_" + recycled.Id.ToString("N"), plan.Actions[0].Entity.RowKey);
        Assert.Equal(new ETag(ETagText), plan.Actions[0].ETag);
        Assert.Equal(TableTransactionActionType.Add, plan.Actions[1].ActionType);
        Assert.Equal("bs_" + recycled.Id.ToString("N"), plan.Actions[1].Entity.RowKey);
        Assert.Equal([1], plan.PrimaryActionIndexes);
    }

    [Fact]
    public void Plan_RestoreSecret_DeletesTheRecycledRowThenAddsTheActiveRow()
    {
        var restored = VaultSamples.Secret(_owner);

        var plan = Assert.Single(VaultTransactionPlanner.Plan(_owner, [VaultChange.RestoreSecret(restored, ETagText)]));

        Assert.Equal("bs_" + restored.Id.ToString("N"), plan.Actions[0].Entity.RowKey);
        Assert.Equal(TableTransactionActionType.Delete, plan.Actions[0].ActionType);
        Assert.Equal("s_" + restored.Id.ToString("N"), plan.Actions[1].Entity.RowKey);
        Assert.Equal(TableTransactionActionType.Add, plan.Actions[1].ActionType);
    }

    [Fact]
    public void Plan_FolderRecycleAndRestore_UseFolderRowKeys()
    {
        var recycled = VaultSamples.RecycledFolder(_owner);
        var restored = VaultSamples.Folder(_owner);

        var plan = VaultTransactionPlanner.Plan(_owner, [VaultChange.RecycleFolder(recycled, ETagText), VaultChange.RestoreFolder(restored, ETagText)]);

        var actions = Assert.Single(plan).Actions;
        Assert.Equal(["f_" + recycled.Id.ToString("N"), "bf_" + recycled.Id.ToString("N"), "bf_" + restored.Id.ToString("N"), "f_" + restored.Id.ToString("N")], actions.Select(action => action.Entity.RowKey));
        Assert.Equal([TableTransactionActionType.Delete, TableTransactionActionType.Add, TableTransactionActionType.Delete, TableTransactionActionType.Add], actions.Select(action => action.ActionType));
    }

    [Fact]
    public void Plan_EveryChangeKind_ProducesTheDocumentedAction()
    {
        var secret = VaultSamples.Secret(_owner);
        var folder = VaultSamples.Folder(_owner);
        var version = VaultSamples.Version(_owner, secret.Id);
        var folderId = Guid.NewGuid();
        var changes = new List<VaultChange>
        {
            VaultChange.AddSecret(secret),
            VaultChange.ReplaceSecret(secret, ETagText),
            VaultChange.PurgeSecret(secret, ETagText),
            VaultChange.AddVersion(version),
            VaultChange.DeleteVersion(version, ETagText),
            VaultChange.AddFolder(folder),
            VaultChange.ReplaceFolder(folder, ETagText),
            VaultChange.PurgeFolder(folder, ETagText),
            VaultChange.TouchFolder(folderId, ETagText),
        };

        var actions = Assert.Single(VaultTransactionPlanner.Plan(_owner, changes)).Actions;

        Assert.Equal(
            [
                (TableTransactionActionType.Add, "s_" + secret.Id.ToString("N")),
                (TableTransactionActionType.UpdateReplace, "s_" + secret.Id.ToString("N")),
                (TableTransactionActionType.Delete, "bs_" + secret.Id.ToString("N")),
                (TableTransactionActionType.Add, VaultKeys.VersionRowKey(secret.Id, version.Id)),
                (TableTransactionActionType.Delete, VaultKeys.VersionRowKey(secret.Id, version.Id)),
                (TableTransactionActionType.Add, "f_" + folder.Id.ToString("N")),
                (TableTransactionActionType.UpdateReplace, "f_" + folder.Id.ToString("N")),
                (TableTransactionActionType.Delete, "bf_" + folder.Id.ToString("N")),
                (TableTransactionActionType.UpdateMerge, "f_" + folderId.ToString("N")),
            ],
            actions.Select(action => (action.ActionType, action.Entity.RowKey)));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Plan_EveryAction_StaysInTheOwnersPartition()
    {
        var folderId = Guid.NewGuid();
        var changes = new List<VaultChange>(Adds(30))
        {
            VaultChange.RecycleSecret(VaultSamples.Recycled(_owner), ETagText),
            VaultChange.TouchFolder(folderId, ETagText),
            VaultChange.AddFolder(VaultSamples.Folder(_owner)),
        };

        var plan = VaultTransactionPlanner.Plan(_owner, changes);

        Assert.All(plan.SelectMany(chunk => chunk.Actions), action => Assert.Equal(_owner.ToString("D"), action.Entity.PartitionKey));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Plan_ChangeOwnedBySomeoneElse_ThrowsArgumentException()
    {
        var foreign = VaultSamples.Secret(Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => VaultTransactionPlanner.Plan(_owner, [VaultChange.AddSecret(foreign)]));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Plan_FolderVersionOrRecycleOwnedBySomeoneElse_ThrowsArgumentException()
    {
        var other = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => VaultTransactionPlanner.Plan(_owner, [VaultChange.AddFolder(VaultSamples.Folder(other))]));
        Assert.Throws<ArgumentException>(() => VaultTransactionPlanner.Plan(_owner, [VaultChange.AddVersion(VaultSamples.Version(other, Guid.NewGuid()))]));
        Assert.Throws<ArgumentException>(() => VaultTransactionPlanner.Plan(_owner, [VaultChange.RecycleSecret(VaultSamples.Recycled(other), ETagText)]));
    }

    [Fact]
    public void Plan_150AddSecrets_SplitsInto100And50()
    {
        var plan = VaultTransactionPlanner.Plan(_owner, Adds(150));

        Assert.Equal([100, 50], plan.Select(chunk => chunk.Actions.Count));
        Assert.Equal(Enumerable.Range(0, 100), plan[0].ChangeIndexes);
        Assert.Equal(Enumerable.Range(100, 50), plan[1].ChangeIndexes);
        Assert.All(plan, chunk => Assert.False(chunk.LeadsWithInjectedKeyRingTouch));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(100, 1)]
    [InlineData(101, 2)]
    [InlineData(200, 2)]
    [InlineData(201, 3)]
    public void Plan_ActionCountBoundaries_ProduceExpectedChunkCounts(int count, int chunks)
    {
        var plan = VaultTransactionPlanner.Plan(_owner, Adds(count));

        Assert.Equal(chunks, plan.Count);
        Assert.Equal(count, plan.Sum(chunk => chunk.ChangeIndexes.Count));
        Assert.All(plan, chunk => Assert.InRange(chunk.Actions.Count, 1, VaultStorageLimits.MaxActionsPerTransaction));
    }

    [Fact]
    public void Plan_WithTouchKeyRing_EveryChunkStartsWithTouchAnd99Changes()
    {
        var changes = new List<VaultChange> { VaultChange.TouchKeyRing(ETagText) };
        changes.AddRange(Adds(250));

        var plan = VaultTransactionPlanner.Plan(_owner, changes);

        Assert.Equal([100, 100, 53], plan.Select(chunk => chunk.Actions.Count));
        Assert.All(plan, chunk =>
        {
            Assert.Equal(TableTransactionActionType.UpdateMerge, chunk.Actions[0].ActionType);
            Assert.Equal(VaultKeys.KeyRingRowKey, chunk.Actions[0].Entity.RowKey);
            Assert.Equal(new ETag(ETagText), chunk.Actions[0].ETag);
        });
        Assert.False(plan[0].LeadsWithInjectedKeyRingTouch);
        Assert.True(plan[1].LeadsWithInjectedKeyRingTouch);
        Assert.True(plan[2].LeadsWithInjectedKeyRingTouch);
        Assert.Equal(Enumerable.Range(0, 251), plan.SelectMany(chunk => chunk.ChangeIndexes).Where(index => index >= 0).Distinct().Order());
    }

    [Fact]
    public void Plan_WithTouchKeyRingOnly_ProducesOneChunkWithOneAction()
    {
        var plan = Assert.Single(VaultTransactionPlanner.Plan(_owner, [VaultChange.TouchKeyRing(ETagText)]));

        Assert.Single(plan.Actions);
        Assert.Equal([0], plan.PrimaryActionIndexes);
    }

    [Fact]
    public void Plan_TouchKeyRingNotFirst_ThrowsArgumentException()
    {
        var changes = new List<VaultChange>(Adds(1)) { VaultChange.TouchKeyRing(ETagText) };

        Assert.Throws<ArgumentException>(() => VaultTransactionPlanner.Plan(_owner, changes));
    }

    [Fact]
    public void Plan_TwoTouchKeyRings_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => VaultTransactionPlanner.Plan(_owner, [VaultChange.TouchKeyRing(ETagText), VaultChange.TouchKeyRing(ETagText)]));
    }

    [Fact]
    public void Plan_LargeEnvelopes_SplitByPayloadBelow3MiB()
    {
        var changes = Enumerable.Range(0, 7)
            .Select(_ => VaultChange.AddSecret(VaultSamples.Secret(_owner, nameLength: OneMebibyte)))
            .ToList();

        var plan = VaultTransactionPlanner.Plan(_owner, changes);

        Assert.True(plan.Count >= 3);
        Assert.All(plan, chunk => Assert.True(EstimatedBytes(chunk) <= VaultStorageLimits.MaxTransactionPayloadBytes, $"{EstimatedBytes(chunk)} bytes"));
        Assert.Equal(7, plan.Sum(chunk => chunk.ChangeIndexes.Count));
        Assert.Equal(Enumerable.Range(0, 7), plan.SelectMany(chunk => chunk.ChangeIndexes));
    }

    [Fact]
    public void Plan_SingleChangeLargerThanTheLimit_GetsItsOwnChunkInsteadOfLoopingOrDropping()
    {
        var huge = VaultChange.AddSecret(VaultSamples.Secret(_owner, nameLength: 4 * OneMebibyte));
        var small = Adds(2);

        var plan = VaultTransactionPlanner.Plan(_owner, [small[0], huge, small[1]]);

        Assert.Equal(3, plan.Count);
        Assert.Equal([0, 1, 2], plan.SelectMany(chunk => chunk.ChangeIndexes));
    }

    [Fact]
    public void Plan_RecycleIsNeverSplitAcrossChunks()
    {
        var changes = new List<VaultChange>(Adds(99)) { VaultChange.RecycleSecret(VaultSamples.Recycled(_owner), ETagText) };

        var plan = VaultTransactionPlanner.Plan(_owner, changes);

        Assert.Equal([99, 2], plan.Select(chunk => chunk.Actions.Count));
        Assert.Equal(TableTransactionActionType.Delete, plan[1].Actions[0].ActionType);
        Assert.Equal(TableTransactionActionType.Add, plan[1].Actions[1].ActionType);
    }

    [Fact]
    public void Plan_RecycleAtTheBoundary_FitsWhenExactlyTwoActionsRemain()
    {
        var changes = new List<VaultChange>(Adds(98)) { VaultChange.RecycleSecret(VaultSamples.Recycled(_owner), ETagText) };

        var plan = Assert.Single(VaultTransactionPlanner.Plan(_owner, changes));

        Assert.Equal(100, plan.Actions.Count);
    }

    [Fact]
    public void Plan_PrimaryActionIndexes_PointAtAddOrReplace()
    {
        var secret = VaultSamples.Secret(_owner);
        var changes = new List<VaultChange>
        {
            VaultChange.AddSecret(secret),
            VaultChange.RecycleSecret(VaultSamples.Recycled(_owner), ETagText),
            VaultChange.PurgeSecret(VaultSamples.Recycled(_owner), ETagText),
            VaultChange.ReplaceSecret(secret, ETagText),
            VaultChange.TouchFolder(Guid.NewGuid(), ETagText),
        };

        var plan = Assert.Single(VaultTransactionPlanner.Plan(_owner, changes));

        Assert.Equal([0, 2, null, 4, 5], plan.PrimaryActionIndexes);
        Assert.Equal(TableTransactionActionType.Add, plan.Actions[0].ActionType);
        Assert.Equal(TableTransactionActionType.Add, plan.Actions[2].ActionType);
        Assert.Equal(TableTransactionActionType.UpdateReplace, plan.Actions[4].ActionType);
    }

    [Fact]
    public void Plan_WithInjectedTouch_PrimaryIndexesAccountForTheTouchAction()
    {
        var changes = new List<VaultChange> { VaultChange.TouchKeyRing(ETagText) };
        changes.AddRange(Adds(120));

        var plan = VaultTransactionPlanner.Plan(_owner, changes);

        Assert.Equal(1, plan[1].PrimaryActionIndexes[0]);
        Assert.Equal(plan[1].ChangeIndexes.Count, plan[1].Actions.Count - 1);
    }

    [Fact]
    public void Plan_RecycleWithoutDeletedAt_ThrowsArgumentException()
    {
        var notRecycled = VaultSamples.Secret(_owner);

        Assert.Throws<ArgumentException>(() => VaultChange.RecycleSecret(notRecycled, ETagText));
        Assert.Throws<ArgumentException>(() => VaultChange.RecycleFolder(VaultSamples.Folder(_owner), ETagText));
    }

    [Fact]
    public void Plan_RestoreWithDeletedAtStillSet_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => VaultChange.RestoreSecret(VaultSamples.Recycled(_owner), ETagText));
        Assert.Throws<ArgumentException>(() => VaultChange.RestoreFolder(VaultSamples.RecycledFolder(_owner), ETagText));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Plan_NoActionEverUsesAWildcardETag()
    {
        var changes = new List<VaultChange>
        {
            VaultChange.TouchKeyRing(ETagText),
            VaultChange.ReplaceSecret(VaultSamples.Secret(_owner), ETagText),
            VaultChange.RecycleSecret(VaultSamples.Recycled(_owner), ETagText),
            VaultChange.PurgeFolder(VaultSamples.Folder(_owner), ETagText),
        };

        var plan = VaultTransactionPlanner.Plan(_owner, changes);

        Assert.All(
            plan.SelectMany(chunk => chunk.Actions).Where(action => action.ActionType != TableTransactionActionType.Add),
            action =>
            {
                Assert.NotEqual(ETag.All, action.ETag);
                Assert.Equal(new ETag(ETagText), action.ETag);
            });
    }
}
