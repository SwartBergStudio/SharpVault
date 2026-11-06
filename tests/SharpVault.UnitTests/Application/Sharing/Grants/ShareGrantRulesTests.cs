// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Domain.Sharing;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Sharing.Grants;

[Trait("Category", "Security")]
public sealed class ShareGrantRulesTests
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _caller = Guid.NewGuid();

    private ShareGrant GrantBy(Guid grantedBy, Guid recipient)
    {
        var grant = ShareSamples.Grant(_owner, recipient: recipient);
        grant.GrantedById = grantedBy;
        return grant;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanChange_Owner_IsAlwaysTrue(bool ownerGrantedIt)
    {
        var grant = GrantBy(ownerGrantedIt ? _owner : Guid.NewGuid(), Guid.NewGuid());

        Assert.True(ShareGrantRules.CanChange(SecretPermission.Owner, grant, _owner));
    }

    [Fact]
    public void CanChange_ManagerOnGrantTheyCreatedForSomeoneElse_IsTrue()
    {
        Assert.True(ShareGrantRules.CanChange(SecretPermission.Manage, GrantBy(_caller, Guid.NewGuid()), _caller));
    }

    [Fact]
    public void CanChange_ManagerOnGrantCreatedByTheOwner_IsFalse()
    {
        Assert.False(ShareGrantRules.CanChange(SecretPermission.Manage, GrantBy(_owner, Guid.NewGuid()), _caller));
    }

    [Fact]
    public void CanChange_ManagerOnGrantCreatedByAnotherManager_IsFalse()
    {
        Assert.False(ShareGrantRules.CanChange(SecretPermission.Manage, GrantBy(Guid.NewGuid(), Guid.NewGuid()), _caller));
    }

    [Fact]
    public void CanChange_ManagerOnTheirOwnReceivedGrant_IsFalse()
    {
        Assert.False(ShareGrantRules.CanChange(SecretPermission.Manage, GrantBy(_owner, _caller), _caller));
    }

    [Fact]
    public void CanChange_ManagerOnReceivedGrantWhoseGrantedByIsAlsoTheCaller_IsFalse()
    {
        Assert.False(ShareGrantRules.CanChange(SecretPermission.Manage, GrantBy(_caller, _caller), _caller));
    }

    [Fact]
    public void CanChange_NullGrant_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ShareGrantRules.CanChange(SecretPermission.Owner, null!, _caller));
    }
}
