// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Grants.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos.Sharing;
using SharpVault.UnitTests.Support.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.Grants;

[Trait("Category", "Security")]
public sealed class ChangeShareGrantHandlerTests : IDisposable
{
    private const string NameMarker = "ZZ-NAME-MARKER-77c1";

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

    private static ChangeShareGrantCommandHandler Handler(ShareGrantHandlerWorld world) => new(
        world.Gate,
        world.Chain.Store,
        world.Caller,
        world.Audit,
        world.Time,
        world.LoggerFor<ChangeShareGrantCommandHandler>());

    private static ChangeShareGrantCommand Command(ShareGrantHandlerWorld world, Guid recipient, SharePermissionDto permission, ShareItemKind kind = ShareItemKind.Secret, Guid? itemId = null, string? etag = null) =>
        new(world.OwnerId, kind, itemId ?? Guid.Empty, recipient, etag ?? world.ETagOf(recipient), permission);

    private ShareGrantHandlerWorld World(bool callerIsOwner)
    {
        var world = new ShareGrantHandlerWorld(callerIsOwner);
        _worlds.Add(world);
        return world;
    }

    private ChangeShareGrantCommand Cmd(ShareGrantHandlerWorld world, Guid recipient, SharePermissionDto permission, ShareItemKind kind = ShareItemKind.Secret, string? etag = null) =>
        Command(world, recipient, permission, kind, _itemId, etag);

    private ShareGrantHandlerWorld OwnerWorld(ShareItemKind kind = ShareItemKind.Secret, SharePermission current = SharePermission.View)
    {
        var world = World(true);
        world.ArrangeItem(kind, _itemId);
        world.AddGrant(_target, world.OwnerId, current, kind, _itemId, NameMarker);
        return world;
    }

    [Theory]
    [InlineData(ShareItemKind.Secret, AuditEventType.SecretShareChanged)]
    [InlineData(ShareItemKind.Folder, AuditEventType.FolderShareChanged)]
    public async Task Handle_OwnerChangesGrant_UpdatesPermissionTouchesModifiedAtAndAudits(ShareItemKind kind, AuditEventType expected)
    {
        var world = OwnerWorld(kind);
        var before = world.ETagOf(_target);
        world.Time.Advance(TimeSpan.FromHours(2));

        var result = await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Manage, kind), Token);

        Assert.True(result.IsSuccess);
        var grant = Assert.Single(world.Grants);
        Assert.Equal(SharePermission.Manage, grant.Permission);
        Assert.Equal(world.Time.GetUtcNow(), grant.ModifiedAt);
        Assert.NotEqual(before, result.Value!.ConcurrencyToken);
        Assert.Equal(SharePermissionDto.Manage, result.Value.Permission);
        Assert.True(result.Value.CanChange);
        var entry = Assert.Single(world.AuditLog.Entries);
        Assert.Equal(expected, entry.EventType);
        Assert.Equal(_target, entry.RecipientId);
        Assert.Equal("Manage", entry.SharePermission);
        Assert.Equal(world.Caller.UserId, entry.ActorId);
    }

    [Fact]
    public async Task Handle_OwnerChangeSubmitsAReplaceWithBothEtags()
    {
        var world = OwnerWorld();
        var token = world.ETagOf(_target);

        await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Edit), Token);

        var change = Assert.Single(Assert.Single(world.Submitted));
        Assert.Equal(VaultChangeKind.ReplaceShareGrant, change.Kind);
        Assert.Equal(token, change.ETag);
        Assert.Equal("recipient-row-etag", change.SecondaryETag);
    }

    [Fact]
    public async Task Handle_ManagerChangesGrantTheyCreated_Succeeds()
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Secret, _itemId);
        world.AddGrant(_target, world.Caller.UserId, SharePermission.View, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Edit), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(SharePermission.Edit, world.Grants.Single(grant => grant.RecipientId == _target).Permission);
    }

    [Fact]
    public async Task Handle_ManagerThroughFolderGrantChangesGrantTheyCreated_Succeeds()
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Folder, _itemId);
        world.AddGrant(_target, world.Caller.UserId, SharePermission.View, ShareItemKind.Folder, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Edit, ShareItemKind.Folder), Token);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ManagerChangesGrantTheOwnerCreated_ReturnsNotFoundAndChangesNothing()
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Secret, _itemId);
        world.AddGrant(_target, world.OwnerId, SharePermission.View, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Manage), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.Submitted);
        Assert.Equal(SharePermission.View, world.Grants.Single(grant => grant.RecipientId == _target).Permission);
        Assert.Empty(world.AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_ManagerChangesGrantAnotherManagerCreated_ReturnsNotFoundAndChangesNothing()
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Secret, _itemId);
        world.AddGrant(_target, Guid.NewGuid(), SharePermission.View, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Manage), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.Submitted);
        Assert.Equal(SharePermission.View, world.Grants.Single(grant => grant.RecipientId == _target).Permission);
    }

    [Theory]
    [InlineData(SharePermissionDto.View)]
    [InlineData(SharePermissionDto.Edit)]
    [InlineData(SharePermissionDto.Manage)]
    public async Task Handle_ManagerChangesTheirOwnReceivedGrant_ReturnsNotFoundAndChangesNothing(SharePermissionDto requested)
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Secret, _itemId);
        var own = world.Grants.Single();

        var result = await Handler(world).Handle(Cmd(world, world.Caller.UserId, requested), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.Submitted);
        Assert.Equal(SharePermission.Manage, own.Permission);
    }

    [Fact]
    public async Task Handle_ManagerChangesOwnGrantEvenWhenGrantedByIsTheCaller_ReturnsNotFound()
    {
        var world = World(false);
        world.MakeCallerManager(ShareItemKind.Secret, _itemId);
        var own = world.Grants.Single();
        own.GrantedById = world.Caller.UserId;

        var result = await Handler(world).Handle(Cmd(world, world.Caller.UserId, SharePermissionDto.Edit), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.Submitted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SharePermission.View)]
    [InlineData(SharePermission.Edit)]
    public async Task Handle_CallerIsNotManager_ReturnsNotFound(SharePermission? received)
    {
        var world = World(false);
        world.ArrangeItem(ShareItemKind.Secret, _itemId);
        if (received is { } permission)
        {
            world.AddGrant(world.Caller.UserId, world.OwnerId, permission, ShareItemKind.Secret, _itemId);
        }

        world.AddGrant(_target, world.Caller.UserId, SharePermission.View, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Manage), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.Submitted);
        Assert.Equal(SharePermission.View, world.Grants.Single(grant => grant.RecipientId == _target).Permission);
    }

    [Fact]
    public async Task Handle_GrantMissing_ReturnsNotFound()
    {
        var world = World(true);
        world.ArrangeItem(ShareItemKind.Secret, _itemId);
        world.AddGrant(_target, world.OwnerId, SharePermission.View, ShareItemKind.Secret, _itemId);
        var other = Guid.NewGuid();

        var result = await Handler(world).Handle(
            new ChangeShareGrantCommand(world.OwnerId, ShareItemKind.Secret, _itemId, other, world.ETagOf(_target), SharePermissionDto.Edit),
            Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.Submitted);
    }

    [Fact]
    public async Task Handle_ItemMissing_ReturnsNotFound()
    {
        var world = World(true);
        world.AddGrant(_target, world.OwnerId, SharePermission.View, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Edit), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.Submitted);
    }

    [Fact]
    public async Task Handle_StaleToken_ReturnsConflictAndChangesNothing()
    {
        var world = OwnerWorld();
        var stale = world.ETagOf(_target);
        await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Edit), Token);
        var submittedBefore = world.Submitted.Count;

        var result = await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Manage, etag: stale), Token);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Equal(submittedBefore, world.Submitted.Count);
        Assert.Equal(SharePermission.Edit, world.Grants.Single().Permission);
    }

    [Fact]
    public async Task Handle_TokenComparisonIsCaseSensitive()
    {
        var world = OwnerWorld();
        var token = world.ETagOf(_target).ToLowerInvariant();

        var result = await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Edit, etag: token), Token);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
    }

    [Theory]
    [InlineData(ShareItemKind.Secret)]
    [InlineData(ShareItemKind.Folder)]
    public async Task Handle_OwnerOfBinnedItem_ReturnsNotFound(ShareItemKind kind)
    {
        var world = World(true);
        world.ArrangeRecycledItem(kind, _itemId);
        world.AddGrant(_target, world.OwnerId, SharePermission.View, kind, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Edit, kind), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.Submitted);
    }

    [Fact]
    public async Task Handle_ManagerOfBinnedSecret_ReturnsNotFound()
    {
        var world = World(false);
        world.ArrangeRecycledItem(ShareItemKind.Secret, _itemId);
        world.AddGrant(world.Caller.UserId, world.OwnerId, SharePermission.Manage, ShareItemKind.Secret, _itemId);
        world.AddGrant(_target, world.Caller.UserId, SharePermission.View, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Edit), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.Submitted);
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    [InlineData(StoreWriteResult.AlreadyExists, ResultFailureKind.Conflict)]
    public async Task Handle_StoreFailure_MapsToTheResultAndWritesNoAudit(StoreWriteResult write, ResultFailureKind expected)
    {
        var world = OwnerWorld();
        world.NextWrite = new VaultWriteResult(write, 0, []);

        var result = await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Edit), Token);

        Assert.Equal(expected, result.FailureKind);
        Assert.Empty(world.AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_Success_LogsAndAuditContainNoNames()
    {
        var world = OwnerWorld();

        await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.Edit), Token);
        world.NextWrite = new VaultWriteResult(StoreWriteResult.Conflict, 0, []);
        await Handler(world).Handle(Cmd(world, _target, SharePermissionDto.View), Token);

        Assert.DoesNotContain(NameMarker, JsonSerializer.Serialize(world.AuditLog.Entries), StringComparison.Ordinal);
        Assert.DoesNotContain(NameMarker, world.Logs.AllText, StringComparison.Ordinal);
    }
}
