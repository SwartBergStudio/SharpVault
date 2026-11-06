// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Domain.Sharing;
using SharpVault.UnitTests.Support.Sharing;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Sharing.Common;

[Trait("Category", "Security")]
public sealed class ShareManagementGateTests
{
    private readonly Guid _itemId = Guid.NewGuid();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(ShareItemKind.Secret)]
    [InlineData(ShareItemKind.Folder)]
    public async Task RequireManageAsync_OwnerOfActiveItem_ReturnsOwner(ShareItemKind kind)
    {
        var (gate, world) = OwnerGate();
        ArrangeActive(world, kind);

        var result = await gate.RequireManageAsync(world.OwnerId, kind, _itemId, false, Token);

        Assert.Equal(SecretPermission.Owner, result);
        Assert.Equal(0, world.Reads("GetShareGrantAsync"));
        Assert.Equal(0, world.Reads("ListFolderGrantsToRecipientAsync"));
    }

    [Theory]
    [InlineData(ShareItemKind.Secret)]
    [InlineData(ShareItemKind.Folder)]
    public async Task RequireManageAsync_OwnerOfMissingItem_ReturnsNull(ShareItemKind kind)
    {
        var (gate, world) = OwnerGate();

        var result = await gate.RequireManageAsync(world.OwnerId, kind, _itemId, true, Token);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(ShareItemKind.Secret)]
    [InlineData(ShareItemKind.Folder)]
    public async Task RequireManageAsync_OwnerOfRecycledItemWhenRecycledAllowed_ReturnsOwner(ShareItemKind kind)
    {
        var (gate, world) = OwnerGate();
        ArrangeRecycled(world, kind);

        var result = await gate.RequireManageAsync(world.OwnerId, kind, _itemId, true, Token);

        Assert.Equal(SecretPermission.Owner, result);
    }

    [Theory]
    [InlineData(ShareItemKind.Secret)]
    [InlineData(ShareItemKind.Folder)]
    public async Task RequireManageAsync_OwnerOfRecycledItemWhenRecycledNotAllowed_ReturnsNull(ShareItemKind kind)
    {
        var (gate, world) = OwnerGate();
        ArrangeRecycled(world, kind);

        var result = await gate.RequireManageAsync(world.OwnerId, kind, _itemId, false, Token);

        Assert.Null(result);
        Assert.Equal(0, world.Reads("GetRecycledSecretAsync"));
        Assert.Equal(0, world.Reads("GetRecycledFolderAsync"));
    }

    [Fact]
    public async Task RequireManageAsync_ManageRecipientOfActiveSecret_ReturnsManage()
    {
        var (gate, world) = RecipientGate();
        world.DirectGrant(_itemId, SharePermission.Manage);
        world.ActiveSecret(_itemId, null);

        var result = await gate.RequireManageAsync(world.OwnerId, ShareItemKind.Secret, _itemId, false, Token);

        Assert.Equal(SecretPermission.Manage, result);
    }

    [Theory]
    [InlineData(SharePermission.None)]
    [InlineData(SharePermission.View)]
    [InlineData(SharePermission.Edit)]
    public async Task RequireManageAsync_RecipientBelowManageOfSecret_ReturnsNull(SharePermission permission)
    {
        var (gate, world) = RecipientGate();
        world.DirectGrant(_itemId, permission);
        world.ActiveSecret(_itemId, null);

        var result = await gate.RequireManageAsync(world.OwnerId, ShareItemKind.Secret, _itemId, false, Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task RequireManageAsync_ManageRecipientOfBinnedSecret_ReturnsNull()
    {
        var (gate, world) = RecipientGate();
        world.DirectGrant(_itemId, SharePermission.Manage);
        world.RecycledSecret(_itemId);

        var result = await gate.RequireManageAsync(world.OwnerId, ShareItemKind.Secret, _itemId, true, Token);

        Assert.Null(result);
        Assert.Equal(0, world.Reads("GetRecycledSecretAsync"));
    }

    [Fact]
    public async Task RequireManageAsync_ManageViaFolderOfActiveSecret_ReturnsManage()
    {
        var (gate, world) = RecipientGate();
        var folders = world.Chain(2);
        world.ActiveSecret(_itemId, folders[0]);
        world.FolderGrant(folders[1], SharePermission.Manage);

        var result = await gate.RequireManageAsync(world.OwnerId, ShareItemKind.Secret, _itemId, false, Token);

        Assert.Equal(SecretPermission.Manage, result);
    }

    [Fact]
    public async Task RequireManageAsync_EditViaFolderOfActiveSecret_ReturnsNull()
    {
        var (gate, world) = RecipientGate();
        var folders = world.Chain(2);
        world.ActiveSecret(_itemId, folders[0]);
        world.FolderGrant(folders[1], SharePermission.Edit);

        var result = await gate.RequireManageAsync(world.OwnerId, ShareItemKind.Secret, _itemId, false, Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task RequireManageAsync_ManageRecipientOfFolderViaAncestor_ReturnsManage()
    {
        var (gate, world) = RecipientGate();
        var folders = world.Chain(3);
        world.FolderGrant(folders[2], SharePermission.Manage);

        var result = await gate.RequireManageAsync(world.OwnerId, ShareItemKind.Folder, folders[0], false, Token);

        Assert.Equal(SecretPermission.Manage, result);
    }

    [Theory]
    [InlineData(SharePermission.View)]
    [InlineData(SharePermission.Edit)]
    public async Task RequireManageAsync_RecipientBelowManageOfFolder_ReturnsNull(SharePermission permission)
    {
        var (gate, world) = RecipientGate();
        var folders = world.Chain(1);
        world.FolderGrant(folders[0], permission);

        var result = await gate.RequireManageAsync(world.OwnerId, ShareItemKind.Folder, folders[0], false, Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task RequireManageAsync_ManageRecipientOfBinnedFolder_ReturnsNull()
    {
        var (gate, world) = RecipientGate();
        world.FolderGrant(_itemId, SharePermission.Manage);
        world.RecycledFolder(_itemId);

        var result = await gate.RequireManageAsync(world.OwnerId, ShareItemKind.Folder, _itemId, true, Token);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(ShareItemKind.Secret)]
    [InlineData(ShareItemKind.Folder)]
    public async Task RequireManageAsync_StrangerWithNoGrants_ReturnsNull(ShareItemKind kind)
    {
        var (gate, world) = RecipientGate();
        ArrangeActive(world, kind);

        var result = await gate.RequireManageAsync(world.OwnerId, kind, _itemId, true, Token);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(ShareItemKind.Secret)]
    [InlineData(ShareItemKind.Folder)]
    public async Task RequireManageAsync_AdminWhoIsNotOwnerAndHasNoGrant_ReturnsNull(ShareItemKind kind)
    {
        var admin = new StubCurrentUser(isAdmin: true);
        var world = new ShareChainWorld { CallerId = admin.UserId };
        var gate = new ShareManagementGate(world.Store, new ShareAccessResolver(world.Store, admin), admin);
        ArrangeActive(world, kind);

        var result = await gate.RequireManageAsync(world.OwnerId, kind, _itemId, true, Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task RequireManageAsync_GrantOfAnotherRecipient_DoesNotApplyToTheCaller()
    {
        var (gate, world) = RecipientGate();
        world.ActiveSecret(_itemId, null);
        var other = ShareSamples.Grant(world.OwnerId, ShareItemKind.Secret, _itemId, Guid.NewGuid(), SharePermission.Manage);
        world.GrantFor(other);

        var result = await gate.RequireManageAsync(world.OwnerId, ShareItemKind.Secret, _itemId, false, Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task RequireManageAsync_UnknownKindForOwner_Throws()
    {
        var (gate, world) = OwnerGate();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            gate.RequireManageAsync(world.OwnerId, (ShareItemKind)99, _itemId, false, Token));
    }

    [Fact]
    public async Task RequireManageAsync_UnknownKindForRecipient_Throws()
    {
        var (gate, world) = RecipientGate();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            gate.RequireManageAsync(world.OwnerId, (ShareItemKind)99, _itemId, false, Token));
    }

    private static (ShareManagementGate Gate, ShareChainWorld World) OwnerGate()
    {
        var caller = new StubCurrentUser();
        var world = new ShareChainWorld(caller.UserId) { CallerId = caller.UserId };
        return (new ShareManagementGate(world.Store, new ShareAccessResolver(world.Store, caller), caller), world);
    }

    private static (ShareManagementGate Gate, ShareChainWorld World) RecipientGate()
    {
        var caller = new StubCurrentUser();
        var world = new ShareChainWorld { CallerId = caller.UserId };
        return (new ShareManagementGate(world.Store, new ShareAccessResolver(world.Store, caller), caller), world);
    }

    private void ArrangeActive(ShareChainWorld world, ShareItemKind kind)
    {
        if (kind == ShareItemKind.Secret)
        {
            world.ActiveSecret(_itemId, null);
        }
        else
        {
            world.ActiveFolder(_itemId, null);
        }
    }

    private void ArrangeRecycled(ShareChainWorld world, ShareItemKind kind)
    {
        if (kind == ShareItemKind.Secret)
        {
            world.RecycledSecret(_itemId);
        }
        else
        {
            world.RecycledFolder(_itemId);
        }
    }
}
