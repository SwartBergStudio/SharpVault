// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Data.Tables;
using SharpVault.Infrastructure.Transfers;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.UnitTests.Infrastructure.Transfers;

[Trait("Category", "Security")]
public sealed class ActionGroupPackerTests
{
    private const int FenceActions = 1;
    private const int SingleActionGroupCount = 200;

    [Fact]
    public void Pack_ReservedActions_KeepsRoomForTheFence()
    {
        var groups = SingleActionGroups(SingleActionGroupCount);

        var packs = ActionGroupPacker.Pack(groups, FenceActions);

        Assert.Equal(3, packs.Count);
        Assert.All(packs, pack => Assert.True(ActionCount(pack) + FenceActions <= VaultStorageLimits.MaxActionsPerTransaction));
        Assert.Equal(SingleActionGroupCount, packs.Sum(pack => pack.Count));
    }

    [Fact]
    public void Pack_WithoutReservation_FillsEveryActionSlot()
    {
        var packs = ActionGroupPacker.Pack(SingleActionGroups(SingleActionGroupCount));

        Assert.Equal(2, packs.Count);
        Assert.All(packs, pack => Assert.Equal(VaultStorageLimits.MaxActionsPerTransaction, ActionCount(pack)));
    }

    [Fact]
    public void Pack_NoGroups_ReturnsNoPacks()
    {
        var packs = ActionGroupPacker.Pack([], FenceActions);

        Assert.Empty(packs);
    }

    [Fact]
    public void Pack_GroupThatOnlyFitsWithoutTheFence_Throws()
    {
        var groups = new List<ActionGroup> { new(Guid.NewGuid(), Actions(VaultStorageLimits.MaxActionsPerTransaction), 0) };

        Assert.Throws<InvalidOperationException>(() => ActionGroupPacker.Pack(groups, FenceActions));
    }

    [Fact]
    public void FitsOneTransaction_ExactlyAtTheActionAndByteLimits_FitsAndOneMoreDoesNot()
    {
        Assert.True(ActionGroupPacker.FitsOneTransaction(VaultStorageLimits.MaxActionsPerTransaction, 0));
        Assert.False(ActionGroupPacker.FitsOneTransaction(VaultStorageLimits.MaxActionsPerTransaction + 1, 0));
        Assert.True(ActionGroupPacker.FitsOneTransaction(1, VaultStorageLimits.MaxTransactionPayloadBytes));
        Assert.False(ActionGroupPacker.FitsOneTransaction(1, VaultStorageLimits.MaxTransactionPayloadBytes + 1));
    }

    [Fact]
    public void FitsOneTransaction_SecretPlusFence_FitsOnlyUpTo99Actions()
    {
        Assert.True(ActionGroupPacker.FitsOneTransaction(VaultStorageLimits.MaxActionsPerTransaction - 1 + FenceActions, 0));
        Assert.False(ActionGroupPacker.FitsOneTransaction(VaultStorageLimits.MaxActionsPerTransaction + FenceActions, 0));
    }

    private static int ActionCount(IEnumerable<ActionGroup> pack) => pack.Sum(group => group.Actions.Count);

    private static List<ActionGroup> SingleActionGroups(int count) =>
        Enumerable.Range(0, count).Select(_ => new ActionGroup(Guid.NewGuid(), Actions(1), 0)).ToList();

    private static List<TableTransactionAction> Actions(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => new TableTransactionAction(TableTransactionActionType.UpsertReplace, new TableEntity("partition", Guid.NewGuid().ToString("N"))))
            .ToList();
}
