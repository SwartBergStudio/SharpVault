// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Support.Sharing;

internal sealed class ShareChainWorld
{
    private readonly List<ShareGrant> _folderGrants = [];
    private ShareGrant? _directGrant;

    public ShareChainWorld(Guid? ownerId = null)
    {
        OwnerId = ownerId ?? Guid.NewGuid();
        Store = Substitute.For<IVaultStore>();
        Store.GetShareGrantAsync(OwnerId, Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Versioned<ShareGrant>?>(
                _directGrant is not null && _directGrant.RecipientId == call.ArgAt<Guid>(3) && _directGrant.ItemId == call.ArgAt<Guid>(2) ? new Versioned<ShareGrant>(_directGrant, "g") : null));
        Store.ListFolderGrantsToRecipientAsync(OwnerId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<ShareGrant>>([.. _folderGrants.Where(grant => grant.RecipientId == call.ArgAt<Guid>(1))]));
        Store.GetSecretAsync(OwnerId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<Secret>?>(null));
        Store.GetFolderAsync(OwnerId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<Folder>?>(null));
        Store.GetRecycledSecretAsync(OwnerId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<Secret>?>(null));
        Store.GetRecycledFolderAsync(OwnerId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<Folder>?>(null));
    }

    public Guid OwnerId { get; }

    public Guid CallerId { get; set; }

    public IVaultStore Store { get; }

    public void DirectGrant(Guid secretId, SharePermission permission) =>
        _directGrant = ShareSamples.Grant(OwnerId, ShareItemKind.Secret, secretId, CallerId, permission);

    public void GrantFor(ShareGrant grant) => _directGrant = grant;

    public void FolderGrant(Guid folderId, SharePermission permission) =>
        _folderGrants.Add(ShareSamples.Grant(OwnerId, ShareItemKind.Folder, folderId, CallerId, permission));

    public void ActiveSecret(Guid secretId, Guid? folderId)
    {
        var secret = VaultSamples.Secret(OwnerId, secretId, folderId);
        Store.GetSecretAsync(OwnerId, secretId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<Secret>?>(new Versioned<Secret>(secret, "s")));
    }

    public void RecycledSecret(Guid secretId)
    {
        Store.GetRecycledSecretAsync(OwnerId, secretId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<Secret>?>(new Versioned<Secret>(VaultSamples.Recycled(OwnerId, secretId), "s")));
    }

    public void ActiveFolder(Guid folderId, Guid? parentId)
    {
        var folder = VaultSamples.Folder(OwnerId, folderId, parentId);
        Store.GetFolderAsync(OwnerId, folderId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<Folder>?>(new Versioned<Folder>(folder, "f")));
    }

    public void RecycledFolder(Guid folderId)
    {
        Store.GetRecycledFolderAsync(OwnerId, folderId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<Folder>?>(new Versioned<Folder>(VaultSamples.RecycledFolder(OwnerId, folderId), "f")));
    }

    public List<Guid> Chain(int length, Guid? terminalParent = null)
    {
        var ids = Enumerable.Range(0, length).Select(_ => Guid.NewGuid()).ToList();
        for (var index = 0; index < length; index++)
        {
            ActiveFolder(ids[index], index + 1 < length ? ids[index + 1] : terminalParent);
        }

        return ids;
    }

    public int Reads(string method) => Store.ReceivedCalls().Count(call => call.GetMethodInfo().Name == method);

    public int TotalReads => Store.ReceivedCalls().Count();
}
