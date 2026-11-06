// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Users;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Application.Vault;
using SharpVault.UnitTests.Support.Sharing;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Sharing.SharedItems;

public abstract class SharedItemsHandlerTestBase : VaultHandlerTestBase
{
    private readonly List<ShareGrant> _folderGrants = [];

    private protected SharedItemsHandlerTestBase()
    {
        Sessions = new DisposalTrackingEncryptor(Harness.Encryptor);
        SharedRings = new VaultKeyRingService(Store, Sessions, Time, NullLogger<VaultKeyRingService>.Instance);
        RealPolicy = new SecretAccessPolicy(Current, new ShareAccessResolver(Store, Current));
        Users = Substitute.For<IUserStore>();
        Users.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult<Versioned<User>?>(
            new Versioned<User>(new User { Id = call.ArgAt<Guid>(0), DisplayName = SharerName }, "u")));
        Store.GetKeyRingAsync(Sharer, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Versioned<VaultKeyRing>?>(new Versioned<VaultKeyRing>(Ring, RingETag)));
        Store.GetSecretAsync(Sharer, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Secret>?>(null));
        Store.GetFolderAsync(Sharer, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Folder>?>(null));
        Store.GetShareGrantAsync(Sharer, Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<ShareGrant>?>(null));
        Store.ListFolderGrantsToRecipientAsync(Sharer, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<ShareGrant>>([.. _folderGrants.Where(grant => grant.RecipientId == call.ArgAt<Guid>(1))]));
        Store.ListFoldersAsync(Sharer, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Versioned<Folder>>>([]));
        Store.ListSecretsAsync(Sharer, Arg.Any<SecretFolderFilter>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SecretPage([], null)));
    }

    protected const string SharerName = "Sharing Sam";

    protected Guid Sharer { get; } = Guid.NewGuid();

    protected Guid Caller => Owner;

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    private protected DisposalTrackingEncryptor Sessions { get; }

    private protected VaultKeyRingService SharedRings { get; }

    private protected SecretAccessPolicy RealPolicy { get; }

    private protected IUserStore Users { get; }

    protected Versioned<Secret> SharedSecret(Guid? folderId = null, string name = "Shared name", Guid? ownerId = null)
    {
        var partition = ownerId ?? Sharer;
        var secret = VaultSamples.Secret(partition, Guid.NewGuid(), folderId);
        SecretMetadataCipher.Encrypt(MetadataSession, secret, new SecretMetadata(name, "shared-user", "https://shared.test/", ["shared-tag"]));
        secret.DataKey = SecretSession.WrappedKey;
        secret.ValueEnvelope = SecretSession.Encrypt(new EncryptionContext(VaultPurposes.SecretValue, secret.Id, partition), "shared-plain-value");
        secret.NotesEnvelope = SecretSession.Encrypt(new EncryptionContext(VaultPurposes.SecretNotes, secret.Id, partition), "shared-plain-notes");
        return GivenSecret(secret, partition: partition);
    }

    protected Folder SharedFolderRow(Guid? parentId = null, string name = "Shared folder")
    {
        var folder = VaultSamples.Folder(Sharer, Guid.NewGuid(), parentId);
        SecretMetadataCipher.EncryptFolderName(MetadataSession, folder, name);
        return folder;
    }

    protected IReadOnlyList<Versioned<Folder>> ActiveFolders(params Folder[] folders)
    {
        var list = folders.Select(folder => new Versioned<Folder>(folder, NextETag())).ToList();
        Store.ListFoldersAsync(Sharer, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Versioned<Folder>>>(list));
        foreach (var versioned in list)
        {
            Store.GetFolderAsync(Sharer, versioned.Value.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Folder>?>(versioned));
        }

        return list;
    }

    protected ShareGrant DirectGrant(Guid itemId, SharePermission permission, ShareItemKind kind = ShareItemKind.Secret)
    {
        var grant = ShareSamples.Grant(Sharer, kind, itemId, Caller, permission);
        Store.GetShareGrantAsync(Sharer, kind, itemId, Caller, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<ShareGrant>?>(new Versioned<ShareGrant>(grant, "g")));
        return grant;
    }

    protected ShareGrant FolderGrant(Guid folderId, SharePermission permission)
    {
        var grant = ShareSamples.Grant(Sharer, ShareItemKind.Folder, folderId, Caller, permission);
        _folderGrants.Add(grant);
        return grant;
    }

    protected void RevokeFolderGrants() => _folderGrants.Clear();

    protected void RecycleSecret(Versioned<Secret> secret) =>
        Store.GetSecretAsync(Sharer, secret.Value.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<Secret>?>(null));

    protected static readonly string[] GrantMethods = ["GetShareGrantAsync", "ListFolderGrantsToRecipientAsync"];

    protected IEnumerable<string> StoreMethodsCalled() => Store.ReceivedCalls().Select(call => call.GetMethodInfo().Name);

    protected int StoreCalls(string method) => Store.ReceivedCalls().Count(call => call.GetMethodInfo().Name == method);

    protected IEnumerable<Guid> PartitionsRead() =>
        Store.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<Guid>().Distinct();

    protected void GivenSharerRingUnreadable() =>
        Harness.Provider.UnwrapFailure = () => new SecretDecryptionException(DecryptionFailureReason.KeyMismatch);

    protected void GivenSharerWithoutRing() =>
        Store.GetKeyRingAsync(Sharer, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<VaultKeyRing>?>(null));
}
