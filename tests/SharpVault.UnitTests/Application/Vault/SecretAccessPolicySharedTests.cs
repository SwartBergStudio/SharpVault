// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;
using SharpVault.UnitTests.Support.Sharing;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

[Trait("Category", "Security")]
public sealed class SecretAccessPolicySharedTests
{
    private readonly StubCurrentUser _caller = new();
    private readonly Guid _secretId = Guid.NewGuid();
    private readonly ShareChainWorld _world;

    public SecretAccessPolicySharedTests()
    {
        _world = new ShareChainWorld { CallerId = _caller.UserId };
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ResolveSharedAsync_OwnerIsCaller_ReturnsNullWithoutReadingStorage()
    {
        var ownWorld = new ShareChainWorld(_caller.UserId) { CallerId = _caller.UserId };
        var policy = new SecretAccessPolicy(_caller, new ShareAccessResolver(ownWorld.Store, _caller));

        var grant = await policy.ResolveSharedAsync(_caller.UserId, _secretId, SecretPermission.View, Token);

        Assert.Null(grant);
        Assert.Equal(0, ownWorld.TotalReads);
    }

    [Fact]
    public async Task ResolveSharedAsync_NoneResult_ReturnsNull()
    {
        var grant = await NewPolicy().ResolveSharedAsync(_world.OwnerId, _secretId, SecretPermission.View, Token);

        Assert.Null(grant);
    }

    [Fact]
    public async Task ResolveSharedAsync_AdminWithoutGrant_ReturnsNull()
    {
        var admin = new StubCurrentUser(isAdmin: true);
        var world = new ShareChainWorld { CallerId = admin.UserId };
        var policy = new SecretAccessPolicy(admin, new ShareAccessResolver(world.Store, admin));

        var grant = await policy.ResolveSharedAsync(world.OwnerId, _secretId, SecretPermission.View, Token);

        Assert.Null(grant);
    }

    [Theory]
    [InlineData(SharePermission.View, SecretPermission.View)]
    [InlineData(SharePermission.Edit, SecretPermission.Edit)]
    [InlineData(SharePermission.Manage, SecretPermission.Manage)]
    public async Task ResolveSharedAsync_DirectGrant_ReturnsOwnerPartitionWithGrantedLevel(SharePermission permission, SecretPermission expected)
    {
        _world.DirectGrant(_secretId, permission);

        var grant = await NewPolicy().ResolveSharedAsync(_world.OwnerId, _secretId, SecretPermission.View, Token);

        Assert.NotNull(grant);
        Assert.Equal(_world.OwnerId, grant.OwnerId);
        Assert.Equal(expected, grant.Granted);
    }

    [Theory]
    [InlineData(SharePermission.View, SecretPermission.Edit)]
    [InlineData(SharePermission.View, SecretPermission.Manage)]
    [InlineData(SharePermission.View, SecretPermission.Owner)]
    [InlineData(SharePermission.Edit, SecretPermission.Manage)]
    [InlineData(SharePermission.Manage, SecretPermission.Owner)]
    public async Task RequireAsync_SharedOwnerGrantBelowRequired_ReturnsNull(SharePermission permission, SecretPermission required)
    {
        _world.DirectGrant(_secretId, permission);

        var grant = await NewPolicy().RequireAsync(_world.OwnerId, _secretId, required, Token);

        Assert.Null(grant);
    }

    [Theory]
    [InlineData(SharePermission.View, SecretPermission.View)]
    [InlineData(SharePermission.Edit, SecretPermission.Edit)]
    [InlineData(SharePermission.Manage, SecretPermission.Edit)]
    [InlineData(SharePermission.Manage, SecretPermission.Manage)]
    public async Task RequireAsync_SharedOwnerGrantMeetsRequired_ReturnsGrant(SharePermission permission, SecretPermission required)
    {
        _world.DirectGrant(_secretId, permission);

        var grant = await NewPolicy().RequireAsync(_world.OwnerId, _secretId, required, Token);

        Assert.NotNull(grant);
        Assert.Equal(_world.OwnerId, grant.OwnerId);
    }

    [Fact]
    public async Task RequireAsync_SharedOwnerNull_UsesOwnPartition()
    {
        var grant = await NewPolicy().RequireAsync((Guid?)null, _secretId, SecretPermission.Owner, Token);

        Assert.NotNull(grant);
        Assert.Equal(_caller.UserId, grant.OwnerId);
        Assert.Equal(0, _world.TotalReads);
    }

    [Fact]
    public async Task RequireAsync_SharedOwnerIsCaller_ReturnsNull()
    {
        var grant = await NewPolicy().RequireAsync(_caller.UserId, _secretId, SecretPermission.View, Token);

        Assert.Null(grant);
    }

    [Fact]
    public async Task RequireAsync_GrantOnAnotherSecret_ReturnsNull()
    {
        _world.DirectGrant(Guid.NewGuid(), SharePermission.Manage);

        var grant = await NewPolicy().RequireAsync(_world.OwnerId, _secretId, SecretPermission.View, Token);

        Assert.Null(grant);
    }

    private SecretAccessPolicy NewPolicy() => new(_caller, new ShareAccessResolver(_world.Store, _caller));
}
