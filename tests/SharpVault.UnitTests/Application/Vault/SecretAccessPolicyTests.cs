// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

[Trait("Category", "Security")]
public sealed class SecretAccessPolicyTests
{
    [Theory]
    [InlineData(SecretPermission.View)]
    [InlineData(SecretPermission.Edit)]
    [InlineData(SecretPermission.Manage)]
    [InlineData(SecretPermission.Owner)]
    public async Task ResolveAsync_AnySecret_ReturnsCurrentUserAsOwner(SecretPermission required)
    {
        var user = new StubCurrentUser();
        var policy = NewPolicy(user);

        var grant = await policy.ResolveAsync(Guid.NewGuid(), required, TestContext.Current.CancellationToken);

        Assert.NotNull(grant);
        Assert.Equal(user.UserId, grant.OwnerId);
        Assert.Equal(SecretPermission.Owner, grant.Granted);
    }

    [Fact]
    public async Task ResolveAsync_AdminCaller_StillReturnsOnlyOwnPartition()
    {
        var admin = new StubCurrentUser(isAdmin: true);
        var policy = NewPolicy(admin);

        var grant = await policy.ResolveAsync(Guid.NewGuid(), SecretPermission.View, TestContext.Current.CancellationToken);

        Assert.NotNull(grant);
        Assert.Equal(admin.UserId, grant.OwnerId);
    }

    [Fact]
    public async Task ResolveAsync_TwoUsers_NeverShareAnOwnerPartition()
    {
        var first = NewPolicy(new StubCurrentUser());
        var second = NewPolicy(new StubCurrentUser());
        var secret = Guid.NewGuid();

        var firstGrant = await first.ResolveAsync(secret, SecretPermission.View, TestContext.Current.CancellationToken);
        var secondGrant = await second.ResolveAsync(secret, SecretPermission.View, TestContext.Current.CancellationToken);

        Assert.NotEqual(firstGrant!.OwnerId, secondGrant!.OwnerId);
    }

    private static SecretAccessPolicy NewPolicy(StubCurrentUser user) =>
        new(user, new ShareAccessResolver(Substitute.For<IVaultStore>(), user));
}
