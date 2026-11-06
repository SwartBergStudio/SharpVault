// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Sharing.Common;

[Trait("Category", "Security")]
public sealed class ShareGateBinnedSecretTests
{
    private readonly StubCurrentUser _caller = new();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _secretId = Guid.NewGuid();
    private readonly IVaultStore _store = Substitute.For<IVaultStore>();

    [Fact]
    public async Task RequireManageAsync_ManageRecipientOfBinnedSecret_ReturnsNull()
    {
        ArrangeDirectManageGrant();
        _store.GetSecretAsync(_ownerId, _secretId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Secret>?>(null));

        var result = await NewGate().RequireManageAsync(_ownerId, ShareItemKind.Secret, _secretId, allowRecycled: true, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task RequireManageAsync_ManageRecipientOfActiveSecret_ReturnsManage()
    {
        ArrangeDirectManageGrant();
        _store.GetSecretAsync(_ownerId, _secretId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<Secret>?>(new Versioned<Secret>(VaultSamples.Secret(_ownerId, _secretId), "etag")));

        var result = await NewGate().RequireManageAsync(_ownerId, ShareItemKind.Secret, _secretId, allowRecycled: false, TestContext.Current.CancellationToken);

        Assert.Equal(SecretPermission.Manage, result);
    }

    private ShareManagementGate NewGate() => new(_store, new ShareAccessResolver(_store, _caller), _caller);

    private void ArrangeDirectManageGrant()
    {
        var grant = new ShareGrant
        {
            OwnerId = _ownerId,
            ItemKind = ShareItemKind.Secret,
            ItemId = _secretId,
            RecipientId = _caller.UserId,
            Permission = SharePermission.Manage,
        };
        _store.GetShareGrantAsync(_ownerId, ShareItemKind.Secret, _secretId, _caller.UserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<ShareGrant>?>(new Versioned<ShareGrant>(grant, "g")));
        _store.ListFolderGrantsToRecipientAsync(_ownerId, _caller.UserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ShareGrant>>([]));
    }
}
