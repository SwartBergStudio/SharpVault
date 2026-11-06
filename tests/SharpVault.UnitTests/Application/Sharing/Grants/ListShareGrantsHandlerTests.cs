// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Text.Json;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Sharing.Grants.Queries;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos.Sharing;
using SharpVault.UnitTests.Support.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.Grants;

[Trait("Category", "Security")]
public sealed class ListShareGrantsHandlerTests : IDisposable
{
    private readonly Guid _itemId = Guid.NewGuid();
    private readonly List<ShareGrantHandlerWorld> _worlds = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var world in _worlds)
        {
            world.Dispose();
        }
    }

    private static ListShareGrantsQueryHandler Handler(ShareGrantHandlerWorld world) =>
        new(world.Gate, world.Chain.Store, world.Caller, Microsoft.Extensions.Logging.Abstractions.NullLogger<ListShareGrantsQueryHandler>.Instance);

    private ShareGrantHandlerWorld World(bool callerIsOwner)
    {
        var world = new ShareGrantHandlerWorld(callerIsOwner);
        _worlds.Add(world);
        return world;
    }

    private ListShareGrantsQuery Query(ShareGrantHandlerWorld world, ShareItemKind kind = ShareItemKind.Secret) =>
        new(world.OwnerId, kind, _itemId);

    [Fact]
    public async Task Handle_Owner_SeesEveryGrantWithCanChangeTrue()
    {
        var world = World(true);
        world.ArrangeItem(ShareItemKind.Secret, _itemId);
        world.AddGrant(Guid.NewGuid(), world.OwnerId, SharePermission.View, ShareItemKind.Secret, _itemId, "Alpha");
        world.AddGrant(Guid.NewGuid(), Guid.NewGuid(), SharePermission.Manage, ShareItemKind.Secret, _itemId, "Bravo");

        var result = await Handler(world).Handle(Query(world), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.All(result.Value.Items, item => Assert.True(item.CanChange));
    }

    [Fact]
    public async Task Handle_Manager_CanChangeOnlyGrantsTheyCreatedForOthers()
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Secret, _itemId);
        var mine = Guid.NewGuid();
        var ownersGrant = Guid.NewGuid();
        var otherManagers = Guid.NewGuid();
        world.AddGrant(mine, world.Caller.UserId, SharePermission.View, ShareItemKind.Secret, _itemId, "Mine");
        world.AddGrant(ownersGrant, world.OwnerId, SharePermission.Edit, ShareItemKind.Secret, _itemId, "Owners");
        world.AddGrant(otherManagers, Guid.NewGuid(), SharePermission.Edit, ShareItemKind.Secret, _itemId, "Other");

        var result = await Handler(world).Handle(Query(world), Token);

        var byId = result.Value!.Items.ToDictionary(item => item.RecipientId);
        Assert.True(byId[mine].CanChange);
        Assert.False(byId[ownersGrant].CanChange);
        Assert.False(byId[otherManagers].CanChange);
        Assert.False(byId[world.Caller.UserId].CanChange);
    }

    [Fact]
    public async Task Handle_ManagerThroughFolderGrant_ListsFolderGrants()
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Folder, _itemId);
        var mine = Guid.NewGuid();
        world.AddGrant(mine, world.Caller.UserId, SharePermission.View, ShareItemKind.Folder, _itemId);

        var result = await Handler(world).Handle(Query(world, ShareItemKind.Folder), Token);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(result.Value!.Items).CanChange);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SharePermission.View)]
    [InlineData(SharePermission.Edit)]
    public async Task Handle_CallerIsNotManager_ReturnsNotFoundWithoutReadingGrants(SharePermission? received)
    {
        var world = World(false);
        world.ArrangeItem(ShareItemKind.Secret, _itemId);
        if (received is { } permission)
        {
            world.AddGrant(world.Caller.UserId, world.OwnerId, permission, ShareItemKind.Secret, _itemId);
        }

        var result = await Handler(world).Handle(Query(world), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        await world.Chain.Store.DidNotReceiveWithAnyArgs().ListShareGrantsForItemAsync(default, default, default, default, Token);
    }

    [Fact]
    public async Task Handle_ItemMissing_ReturnsNotFound()
    {
        var world = World(true);

        var result = await Handler(world).Handle(Query(world), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Theory]
    [InlineData(ShareItemKind.Secret)]
    [InlineData(ShareItemKind.Folder)]
    public async Task Handle_OwnerOfBinnedItem_StillSeesTheGrants(ShareItemKind kind)
    {
        var world = World(true);
        world.ArrangeRecycledItem(kind, _itemId);
        world.AddGrant(Guid.NewGuid(), world.OwnerId, SharePermission.View, kind, _itemId);

        var result = await Handler(world).Handle(Query(world, kind), Token);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task Handle_ManagerOfBinnedSecret_ReturnsNotFound()
    {
        var world = World(false);
        world.ArrangeRecycledItem(ShareItemKind.Secret, _itemId);
        world.AddGrant(world.Caller.UserId, world.OwnerId, SharePermission.Manage, ShareItemKind.Secret, _itemId);
        world.AddGrant(Guid.NewGuid(), world.OwnerId, SharePermission.View, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Query(world), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    public async Task Handle_Grants_AreSortedByDisplayNameIgnoringCase()
    {
        var world = World(true);
        world.ArrangeItem(ShareItemKind.Secret, _itemId);
        foreach (var name in new[] { "charlie", "Alpha", "bravo", "Delta", "alpha2" })
        {
            world.AddGrant(Guid.NewGuid(), world.OwnerId, SharePermission.View, ShareItemKind.Secret, _itemId, name);
        }

        var result = await Handler(world).Handle(Query(world), Token);

        Assert.Equal(["Alpha", "alpha2", "bravo", "charlie", "Delta"], result.Value!.Items.Select(item => item.RecipientDisplayName));
    }

    [Fact]
    public async Task Handle_NoGrants_ReturnsAnEmptyList()
    {
        var world = World(true);
        world.ArrangeItem(ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Query(world), Token);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task Handle_ReadsGrantsOnceAndOnlyUpToTheLimit()
    {
        var world = World(true);
        world.ArrangeItem(ShareItemKind.Secret, _itemId);

        await Handler(world).Handle(Query(world), Token);

        var calls = world.Chain.Store.ReceivedCalls().Where(call => call.GetMethodInfo().Name == "ListShareGrantsForItemAsync").ToList();
        var call = Assert.Single(calls);
        Assert.True((int)call.GetArguments()[3]! <= SharingLimits.MaxGrantsPerItem * 2);
        Assert.Equal(0, world.Chain.Reads("GetShareGrantAsync"));
    }

    [Fact]
    public async Task Handle_OwnerWithOneGrantOverTheLimit_StillSeesEveryStoredGrant()
    {
        var world = World(true);
        world.ArrangeItem(ShareItemKind.Secret, _itemId);
        for (var index = 0; index <= SharingLimits.MaxGrantsPerItem; index++)
        {
            world.AddGrant(Guid.NewGuid(), world.OwnerId, SharePermission.View, ShareItemKind.Secret, _itemId, "Person " + index.ToString("D3", CultureInfo.InvariantCulture));
        }

        var result = await Handler(world).Handle(Query(world), Token);

        Assert.Equal(world.Grants.Count, result.Value!.Items.Count);
    }

    [Fact]
    public async Task Handle_Dtos_CarryNoOwnerGrantorIdOrStatus()
    {
        var world = World(true);
        world.ArrangeItem(ShareItemKind.Secret, _itemId);
        world.AddGrant(Guid.NewGuid(), world.OwnerId, SharePermission.View, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Query(world), Token);

        var json = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain(world.OwnerId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("status", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("locked", json, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<ShareGrantListDto>(result.Value);
    }

    [Fact]
    public async Task Handle_Manager_ListReportsCanGrantManageFalse()
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Secret, _itemId);
        world.AddGrant(Guid.NewGuid(), Guid.NewGuid(), SharePermission.View, ShareItemKind.Secret, _itemId, "Alpha");

        var result = await Handler(world).Handle(Query(world), Token);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.CanGrantManage);
    }

    [Fact]
    public async Task Handle_Owner_ListReportsCanGrantManageTrue()
    {
        var world = World(true);
        world.ArrangeItem(ShareItemKind.Secret, _itemId);
        world.AddGrant(Guid.NewGuid(), Guid.NewGuid(), SharePermission.View, ShareItemKind.Secret, _itemId, "Alpha");

        var result = await Handler(world).Handle(Query(world), Token);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CanGrantManage);
    }
}
