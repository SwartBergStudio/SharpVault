// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Sharing.Grants.Commands;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Users;
using SharpVault.Dtos.Sharing;
using SharpVault.UnitTests.Support.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.Grants;

[Trait("Category", "Security")]
public sealed class CreateShareGrantHandlerTests : IDisposable
{
    private const string NameMarker = "ZZ-NAME-MARKER-9f3a";
    private const string EmailMarker = "zz-email-marker-9f3a@example.test";

    private readonly Guid _itemId = Guid.NewGuid();
    private readonly Guid _recipient = Guid.NewGuid();
    private readonly List<ShareGrantHandlerWorld> _worlds = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var world in _worlds)
        {
            world.Dispose();
        }
    }

    private ShareGrantHandlerWorld World(bool callerIsOwner)
    {
        var world = new ShareGrantHandlerWorld(callerIsOwner);
        _worlds.Add(world);
        return world;
    }

    private static CreateShareGrantCommandHandler Handler(ShareGrantHandlerWorld world) => new(
        world.Gate,
        world.GrantService,
        world.Chain.Store,
        world.Directory,
        world.Users,
        world.Caller,
        world.Audit,
        world.Time,
        world.LoggerFor<CreateShareGrantCommandHandler>());

    private CreateShareGrantCommand Command(ShareGrantHandlerWorld world, ShareItemKind kind = ShareItemKind.Secret, Guid? recipient = null, SharePermissionDto permission = SharePermissionDto.View) =>
        new(world.OwnerId, kind, _itemId, recipient ?? _recipient, permission);

    private ShareGrantHandlerWorld OwnerWorld(ShareItemKind kind = ShareItemKind.Secret)
    {
        var world = World(true);
        world.ArrangeItem(kind, _itemId);
        world.KnownMember(_recipient, NameMarker, EmailMarker);
        return world;
    }

    private ShareGrantHandlerWorld ManagerWorld(ShareItemKind kind = ShareItemKind.Secret)
    {
        var world = World(false);
        world.MakeCallerManager(kind, _itemId);
        world.KnownMember(_recipient, NameMarker, EmailMarker);
        return world;
    }

    [Theory]
    [InlineData(ShareItemKind.Secret)]
    [InlineData(ShareItemKind.Folder)]
    public async Task Handle_OwnerSharesActiveItem_Succeeds(ShareItemKind kind)
    {
        var world = OwnerWorld(kind);

        var result = await Handler(world).Handle(Command(world, kind), Token);

        Assert.True(result.IsSuccess);
        Assert.Single(world.Grants);
    }

    [Fact]
    public async Task Handle_ItemNotFound_ReturnsNotFoundAndMakesNoDirectoryCall()
    {
        var world = World(true);
        world.KnownMember(_recipient);

        var result = await Handler(world).Handle(Command(world), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        await world.Directory.DidNotReceiveWithAnyArgs().FindMemberAsync(default, Token);
        Assert.Empty(world.Submitted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SharePermission.View)]
    [InlineData(SharePermission.Edit)]
    public async Task Handle_CallerIsNotManager_ReturnsNotFoundWithoutDirectoryOrUserLookups(SharePermission? received)
    {
        var world = World(false);
        world.ArrangeItem(ShareItemKind.Secret, _itemId);
        if (received is { } permission)
        {
            world.AddGrant(world.Caller.UserId, world.OwnerId, permission, ShareItemKind.Secret, _itemId);
        }

        world.KnownMember(_recipient);

        var result = await Handler(world).Handle(Command(world), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        await world.Directory.DidNotReceiveWithAnyArgs().FindMemberAsync(default, Token);
        await world.Users.DidNotReceiveWithAnyArgs().GetAsync(default, Token);
        Assert.Empty(world.Submitted);
        Assert.Empty(world.AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_NotManagerAndMissingItem_ReturnIdenticalResults()
    {
        var missing = World(false);
        var viewer = World(false);
        viewer.MakeCallerRecipient(ShareItemKind.Secret, _itemId, SharePermission.View);

        var first = await Handler(missing).Handle(Command(missing), Token);
        var second = await Handler(viewer).Handle(Command(viewer), Token);

        Assert.Equal(first.FailureKind, second.FailureKind);
        Assert.Equal(first.Errors, second.Errors);
    }

    [Theory]
    [InlineData(ShareItemKind.Secret)]
    [InlineData(ShareItemKind.Folder)]
    public async Task Handle_OwnerOfBinnedItem_ReturnsNotFound(ShareItemKind kind)
    {
        var world = World(true);
        world.ArrangeRecycledItem(kind, _itemId);
        world.KnownMember(_recipient);

        var result = await Handler(world).Handle(Command(world, kind), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.Submitted);
    }

    [Fact]
    public async Task Handle_ManagerOfBinnedSecret_ReturnsNotFoundAndWritesNothing()
    {
        var world = World(false);
        world.ArrangeRecycledItem(ShareItemKind.Secret, _itemId);
        world.AddGrant(world.Caller.UserId, world.OwnerId, SharePermission.Manage, ShareItemKind.Secret, _itemId);
        world.KnownMember(_recipient);

        var result = await Handler(world).Handle(Command(world), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(world.Submitted);
        await world.Inbox.DidNotReceiveWithAnyArgs().AddIfAbsentAsync(default!, Token);
        Assert.Empty(world.AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_ManagerSharesToTheOwner_IsRejectedAsUnavailable()
    {
        var world = ManagerWorld();

        var result = await Handler(world).Handle(Command(world, recipient: world.OwnerId), Token);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(SharingMessages.RecipientUnavailable, result.Error);
        await world.Directory.DidNotReceiveWithAnyArgs().FindMemberAsync(default, Token);
        Assert.Empty(world.Submitted);
    }

    [Fact]
    public async Task Handle_ManagerSharesToThemselves_IsRejectedAsUnavailable()
    {
        var world = ManagerWorld();
        world.KnownMember(world.Caller.UserId);
        world.UserRow(world.Caller.UserId, UserStatus.Active);

        var result = await Handler(world).Handle(Command(world, recipient: world.Caller.UserId, permission: SharePermissionDto.View), Token);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(SharingMessages.RecipientUnavailable, result.Error);
        Assert.Empty(world.Submitted);
        Assert.Equal(SharePermission.Manage, world.Grants.Single(grant => grant.RecipientId == world.Caller.UserId).Permission);
    }

    [Fact]
    public async Task Handle_OwnerSharesToThemselves_IsRejectedAsUnavailable()
    {
        var world = OwnerWorld();

        var result = await Handler(world).Handle(Command(world, recipient: world.Caller.UserId), Token);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(SharingMessages.RecipientUnavailable, result.Error);
        Assert.Empty(world.Submitted);
    }

    [Fact]
    public async Task Handle_RecipientUnknownToTheDirectory_IsRejectedAsUnavailable()
    {
        var world = OwnerWorld();
        var stranger = Guid.NewGuid();
        world.Directory.FindMemberAsync(stranger, Arg.Any<CancellationToken>()).Returns(Task.FromResult<DirectoryUser?>(null));

        var result = await Handler(world).Handle(Command(world, recipient: stranger), Token);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(SharingMessages.RecipientUnavailable, result.Error);
        Assert.Empty(world.Submitted);
        await world.Inbox.DidNotReceiveWithAnyArgs().AddIfAbsentAsync(default!, Token);
    }

    [Fact]
    public async Task Handle_RecipientLocked_IsRejectedAsUnavailable()
    {
        var world = OwnerWorld();
        world.UserRow(_recipient, UserStatus.Locked);

        var result = await Handler(world).Handle(Command(world), Token);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(SharingMessages.RecipientUnavailable, result.Error);
        Assert.Empty(world.Submitted);
        await world.Inbox.DidNotReceiveWithAnyArgs().AddIfAbsentAsync(default!, Token);
    }

    [Fact]
    public async Task Handle_UnknownLockedOwnerAndSelfRecipients_ReturnIdenticalResponses()
    {
        var unknown = Guid.NewGuid();
        var locked = Guid.NewGuid();
        var world = OwnerWorld();
        world.Directory.FindMemberAsync(unknown, Arg.Any<CancellationToken>()).Returns(Task.FromResult<DirectoryUser?>(null));
        world.KnownMember(locked);
        world.UserRow(locked, UserStatus.Locked);

        var results = new List<Result<ShareGrantDto>>();
        foreach (var recipient in new[] { unknown, locked, world.OwnerId })
        {
            results.Add(await Handler(world).Handle(Command(world, recipient: recipient), Token));
        }

        Assert.All(results, result =>
        {
            Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
            Assert.Equal(results[0].Errors, result.Errors);
        });
    }

    [Fact]
    public async Task Handle_RecipientKnownToTheDirectoryWithNoUsersRow_IsAllowed()
    {
        var world = OwnerWorld();
        world.Users.GetAsync(_recipient, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<User>?>(null));

        var result = await Handler(world).Handle(Command(world), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(_recipient, Assert.Single(world.Grants).RecipientId);
    }

    [Fact]
    public async Task Handle_RecipientWithActiveUsersRow_IsAllowed()
    {
        var world = OwnerWorld();
        world.UserRow(_recipient, UserStatus.Active);

        var result = await Handler(world).Handle(Command(world), Token);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_RecipientAlreadyPresent_ReturnsConflictAndWritesNothing()
    {
        var world = OwnerWorld();
        world.AddGrant(_recipient, world.OwnerId, SharePermission.View, ShareItemKind.Secret, _itemId);

        var result = await Handler(world).Handle(Command(world, permission: SharePermissionDto.Manage), Token);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Empty(world.Submitted);
        Assert.Equal(SharePermission.View, world.Grants.Single().Permission);
    }

    [Fact]
    public async Task Handle_AtTheLimit_IsRejectedAsTooManyRecipients()
    {
        var world = OwnerWorld();
        for (var index = 0; index < SharingLimits.MaxGrantsPerItem; index++)
        {
            world.AddGrant(Guid.NewGuid(), world.OwnerId, SharePermission.View, ShareItemKind.Secret, _itemId);
        }

        var result = await Handler(world).Handle(Command(world), Token);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(SharingMessages.TooManyRecipients, result.Error);
        Assert.Empty(world.Submitted);
    }

    [Fact]
    public async Task Handle_OneBelowTheLimit_Succeeds()
    {
        var world = OwnerWorld();
        for (var index = 0; index < SharingLimits.MaxGrantsPerItem - 1; index++)
        {
            world.AddGrant(Guid.NewGuid(), world.OwnerId, SharePermission.View, ShareItemKind.Secret, _itemId);
        }

        var result = await Handler(world).Handle(Command(world), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(SharingLimits.MaxGrantsPerItem, world.Grants.Count);
    }

    [Fact]
    public async Task Handle_GrantsOnOtherItemsDoNotCountTowardsTheLimit()
    {
        var world = OwnerWorld();
        for (var index = 0; index < SharingLimits.MaxGrantsPerItem; index++)
        {
            world.AddGrant(Guid.NewGuid(), world.OwnerId, SharePermission.View, ShareItemKind.Secret, Guid.NewGuid());
        }

        var result = await Handler(world).Handle(Command(world), Token);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_StoreReportsAlreadyExists_ReturnsConflictAndWritesNoAudit()
    {
        var world = OwnerWorld();
        world.NextWrite = new VaultWriteResult(StoreWriteResult.AlreadyExists, 0, []);

        var result = await Handler(world).Handle(Command(world), Token);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Empty(world.AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_StoreReportsConflict_ReturnsConflictAndWritesNoAudit()
    {
        var world = OwnerWorld();
        world.NextWrite = new VaultWriteResult(StoreWriteResult.Conflict, 0, []);

        var result = await Handler(world).Handle(Command(world), Token);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Empty(world.AuditLog.Entries);
    }

    [Theory]
    [InlineData(ShareItemKind.Secret, SharePermissionDto.Edit, AuditEventType.SecretShared)]
    [InlineData(ShareItemKind.Folder, SharePermissionDto.Manage, AuditEventType.FolderShared)]
    public async Task Handle_Success_WritesAuditWithRecipientAndPermission(ShareItemKind kind, SharePermissionDto permission, AuditEventType expected)
    {
        var world = OwnerWorld(kind);

        var result = await Handler(world).Handle(Command(world, kind, permission: permission), Token);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(world.AuditLog.Entries);
        Assert.Equal(expected, entry.EventType);
        Assert.Equal(world.Caller.UserId, entry.ActorId);
        Assert.Equal(world.OwnerId, entry.OwnerId);
        Assert.Equal(_recipient, entry.RecipientId);
        Assert.Equal(permission.ToString(), entry.SharePermission);
        Assert.Equal(_itemId.ToString("D"), entry.TargetId);
    }

    [Fact]
    public async Task Handle_Success_ReturnsTheGrantWithItsTokenAndCanChange()
    {
        var world = OwnerWorld();

        var result = await Handler(world).Handle(Command(world, permission: SharePermissionDto.Edit), Token);

        var dto = result.Value!;
        Assert.Equal(_recipient, dto.RecipientId);
        Assert.Equal(SharePermissionDto.Edit, dto.Permission);
        Assert.True(dto.CanChange);
        Assert.Equal(world.ETagOf(_recipient), dto.ConcurrencyToken);
        Assert.Equal(world.Time.GetUtcNow(), dto.GrantedAt);
    }

    [Fact]
    public async Task Handle_ManagerSharesUpToEdit_RecordsTheManagerAsGrantor()
    {
        var world = ManagerWorld();

        var result = await Handler(world).Handle(Command(world, permission: SharePermissionDto.Edit), Token);

        Assert.True(result.IsSuccess);
        var grant = world.Grants.Single(row => row.RecipientId == _recipient);
        Assert.Equal(SharePermission.Edit, grant.Permission);
        Assert.Equal(world.Caller.UserId, grant.GrantedById);
        Assert.Equal(world.OwnerId, grant.OwnerId);
    }

    [Fact]
    public async Task Handle_ManagerThroughFolderGrant_CanShareFolder()
    {
        var world = ManagerWorld(ShareItemKind.Folder);

        var result = await Handler(world).Handle(Command(world, ShareItemKind.Folder), Token);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_Success_AddsTheInboxRowForTheRecipient()
    {
        var world = OwnerWorld();

        await Handler(world).Handle(Command(world), Token);

        await world.Inbox.Received().AddIfAbsentAsync(
            Arg.Is<ShareInboxEntry>(entry => entry.RecipientId == _recipient && entry.OwnerId == world.OwnerId && entry.ItemId == _itemId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Success_LogsAndAuditContainNoNamesOrEmails()
    {
        var world = OwnerWorld();

        await Handler(world).Handle(Command(world, permission: SharePermissionDto.Manage), Token);

        var audit = JsonSerializer.Serialize(world.AuditLog.Entries);
        Assert.DoesNotContain(NameMarker, audit, StringComparison.Ordinal);
        Assert.DoesNotContain(EmailMarker, audit, StringComparison.Ordinal);
        Assert.DoesNotContain(NameMarker, world.Logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(EmailMarker, world.Logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_Conflict_LogsContainNoNamesOrEmails()
    {
        var world = OwnerWorld();
        world.NextWrite = new VaultWriteResult(StoreWriteResult.AlreadyExists, 0, []);

        await Handler(world).Handle(Command(world), Token);

        Assert.DoesNotContain(NameMarker, world.Logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(EmailMarker, world.Logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_RecipientNames_AreTakenFromTheDirectoryNotTheRequest()
    {
        var world = OwnerWorld();

        await Handler(world).Handle(Command(world), Token);

        var grant = Assert.Single(world.Grants);
        Assert.Equal(NameMarker, grant.RecipientDisplayName);
        Assert.Equal(EmailMarker, grant.RecipientEmail);
    }
}
