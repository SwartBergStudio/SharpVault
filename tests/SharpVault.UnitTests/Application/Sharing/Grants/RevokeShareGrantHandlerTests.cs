// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Grants.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Sharing;
using SharpVault.UnitTests.Support.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.Grants;

[Trait("Category", "Security")]
public sealed class RevokeShareGrantHandlerTests : IDisposable
{
    private const string NameMarker = "ZZ-NAME-MARKER-31be";

    private readonly Guid _itemId = Guid.NewGuid();
    private readonly Guid _target = Guid.NewGuid();
    private readonly List<ShareGrantHandlerWorld> _worlds = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var world in _worlds)
        {
            world.Dispose();
        }
    }

    private static RevokeShareGrantCommandHandler Handler(ShareGrantHandlerWorld world) => new(
        world.Gate,
        world.GrantService,
        world.Chain.Store,
        world.Caller,
        world.Audit,
        world.LoggerFor<RevokeShareGrantCommandHandler>());

    private ShareGrantHandlerWorld World(bool callerIsOwner)
    {
        var world = new ShareGrantHandlerWorld(callerIsOwner);
        _worlds.Add(world);
        return world;
    }

    private RevokeShareGrantCommand Cmd(ShareGrantHandlerWorld world, Guid recipient, ShareItemKind kind = ShareItemKind.Secret) =>
        new(world.OwnerId, kind, _itemId, recipient);

    private ShareGrantHandlerWorld OwnerWorld(ShareItemKind kind = ShareItemKind.Secret)
    {
        var world = World(true);
        world.ArrangeItem(kind, _itemId);
        world.AddGrant(_target, world.OwnerId, SharePermission.Edit, kind, _itemId, NameMarker);
        return world;
    }

    [Theory]
    [InlineData(ShareItemKind.Secret, AuditEventType.SecretShareRevoked)]
    [InlineData(ShareItemKind.Folder, AuditEventType.FolderShareRevoked)]
    public async Task Handle_OwnerRevokes_RemovesTheGrantAndAudits(ShareItemKind kind, AuditEventType expected)
    {
        var world = OwnerWorld(kind);

        var result = await Handler(world).Handle(Cmd(world, _target, kind), Token);

        Assert.True(result.IsSuccess);
        Assert.Empty(world.Grants);
        var entry = Assert.Single(world.AuditLog.Entries);
        Assert.Equal(expected, entry.EventType);
        Assert.Equal(_target, entry.RecipientId);
        Assert.Equal("Edit", entry.SharePermission);
        await world.Inbox.Received(1).RemoveAsync(_target, world.OwnerId, kind, _itemId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ShareItemKind.Secret)]
    [InlineData(ShareItemKind.Folder)]
    public async Task Handle_OwnerRevokesGrantOnBinnedItem_Succeeds(ShareItemKind kind)
    {
        var world = World(true);
        world.ArrangeRecycledItem(kind, _itemId);
        world.AddGrant(_target, world.OwnerId, SharePermission.View, kind, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target, kind), Token);

        Assert.True(result.IsSuccess);
        Assert.Empty(world.Grants);
    }

    [Fact]
    public async Task Handle_ManagerOfBinnedSecret_ReturnsNotFoundAndKeepsTheGrant()
    {
        var world = World(false);
        world.ArrangeRecycledItem(ShareItemKind.Secret, _itemId);
        world.AddGrant(world.Caller.UserId, world.OwnerId, SharePermission.Manage, ShareItemKind.Secret, _itemId);
        world.AddGrant(_target, world.Caller.UserId, SharePermission.View, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.Submitted);
        await world.Inbox.DidNotReceiveWithAnyArgs().RemoveAsync(default, default, default, default, Token);
        Assert.Empty(world.AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_ManagerRevokesGrantTheyCreated_Succeeds()
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Secret, _itemId);
        world.AddGrant(_target, world.Caller.UserId, SharePermission.View, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target), Token);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(world.Grants, grant => grant.RecipientId == _target);
        Assert.Contains(world.Grants, grant => grant.RecipientId == world.Caller.UserId);
    }

    [Fact]
    public async Task Handle_ManagerThroughFolderGrantRevokesGrantTheyCreated_Succeeds()
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Folder, _itemId);
        world.AddGrant(_target, world.Caller.UserId, SharePermission.View, ShareItemKind.Folder, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target, ShareItemKind.Folder), Token);

        Assert.True(result.IsSuccess);
        Assert.Empty(world.Grants);
    }

    [Fact]
    public async Task Handle_ManagerRevokesGrantTheOwnerCreated_ReturnsNotFoundAndKeepsIt()
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Secret, _itemId);
        world.AddGrant(_target, world.OwnerId, SharePermission.Edit, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.Submitted);
        Assert.Contains(world.Grants, grant => grant.RecipientId == _target);
        await world.Inbox.DidNotReceiveWithAnyArgs().RemoveAsync(default, default, default, default, Token);
        Assert.Empty(world.AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_ManagerRevokesGrantAnotherManagerCreated_ReturnsNotFoundAndKeepsIt()
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Secret, _itemId);
        world.AddGrant(_target, Guid.NewGuid(), SharePermission.Edit, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Contains(world.Grants, grant => grant.RecipientId == _target);
    }

    [Fact]
    public async Task Handle_ManagerRevokesTheirOwnReceivedGrant_ReturnsNotFoundAndKeepsIt()
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Cmd(world, world.Caller.UserId), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.Submitted);
        Assert.Single(world.Grants);
    }

    [Fact]
    public async Task Handle_ManagerRevokesOwnGrantEvenWhenGrantedByIsTheCaller_ReturnsNotFound()
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Secret, _itemId);
        world.Grants.Single().GrantedById = world.Caller.UserId;

        var result = await Handler(world).Handle(Cmd(world, world.Caller.UserId), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Single(world.Grants);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SharePermission.View)]
    [InlineData(SharePermission.Edit)]
    public async Task Handle_CallerIsNotManager_ReturnsNotFoundAndKeepsTheGrant(SharePermission? received)
    {
        var world = World(false);
        world.ArrangeItem(ShareItemKind.Secret, _itemId);
        if (received is { } permission)
        {
            world.AddGrant(world.Caller.UserId, world.OwnerId, permission, ShareItemKind.Secret, _itemId);
        }

        world.AddGrant(_target, world.Caller.UserId, SharePermission.View, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Contains(world.Grants, grant => grant.RecipientId == _target);
    }

    [Fact]
    public async Task Handle_GrantMissing_ReturnsNotFoundWithoutAudit()
    {
        var world = World(true);
        world.ArrangeItem(ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_ItemMissing_ReturnsNotFound()
    {
        var world = World(true);
        world.AddGrant(_target, world.OwnerId, SharePermission.View, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Single(world.Grants);
    }

    [Fact]
    public async Task Handle_PersistentWriteConflict_ReturnsConflictKeepsTheGrantAndWritesNoAudit()
    {
        var world = OwnerWorld();
        world.NextWrite = new VaultWriteResult(StoreWriteResult.Conflict, 0, []);

        var result = await Handler(world).Handle(Cmd(world, _target), Token);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Single(world.Grants);
        Assert.Empty(world.AuditLog.Entries);
        Assert.Equal(3, world.Submitted.Count);
    }

    [Fact]
    public async Task Handle_Success_LogsAndAuditContainNoNames()
    {
        var world = OwnerWorld();

        await Handler(world).Handle(Cmd(world, _target), Token);

        Assert.DoesNotContain(NameMarker, JsonSerializer.Serialize(world.AuditLog.Entries), StringComparison.Ordinal);
        Assert.DoesNotContain(NameMarker, world.Logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_Conflict_LogsContainNoNames()
    {
        var world = OwnerWorld();
        world.NextWrite = new VaultWriteResult(StoreWriteResult.Conflict, 0, []);

        await Handler(world).Handle(Cmd(world, _target), Token);

        Assert.DoesNotContain(NameMarker, world.Logs.AllText, StringComparison.Ordinal);
    }
}
