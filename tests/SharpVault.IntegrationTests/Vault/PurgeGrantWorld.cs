// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging.Abstractions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.RecycleBin;
using SharpVault.Domain.Encryption;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;

namespace SharpVault.IntegrationTests.Vault;

public sealed class PurgeGrantWorld(IVaultStore store, IShareInboxStore inbox, TableClient vaultsTable, TableClient inboxTable, Guid owner)
{
    private static readonly DateTimeOffset Epoch = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private const int GrantsPerSubmit = 40;

    public Guid Owner { get; } = owner;

    public IVaultStore Store { get; } = store;

    public RecycledItemEraser Eraser => EraserOver(Store);

    public RecycledItemEraser EraserOver(IVaultStore over) =>
        new(over, inbox, TimeProvider.System, NullLogger<RecycledItemEraser>.Instance);

    public async Task<(Versioned<Secret> Binned, List<ShareGrant> Grants)> SeedRecycledSecretAsync(int grantCount, int versionCount, CancellationToken cancellationToken)
    {
        var secret = NewSecret();
        await Submit([VaultChange.AddSecret(secret)], cancellationToken);
        for (var index = 0; index < versionCount; index++)
        {
            await Submit([VaultChange.AddVersion(NewVersion(secret, index))], cancellationToken);
        }

        var grants = await SeedGrantsAsync(secret.Id, grantCount, cancellationToken);
        var active = (await Store.GetSecretAsync(Owner, secret.Id, cancellationToken))!;
        secret.DeletedAt = Epoch;
        secret.DeletionBatchId = Guid.NewGuid();
        await Submit([VaultChange.RecycleSecret(secret, active.ETag)], cancellationToken);
        var binned = (await Store.GetRecycledSecretAsync(Owner, secret.Id, cancellationToken))!;
        return (binned, grants);
    }

    public async Task<List<ShareGrant>> SeedGrantsAsync(Guid secretId, int grantCount, CancellationToken cancellationToken)
    {
        var grants = Enumerable.Range(0, grantCount).Select(_ => NewGrant(secretId)).ToList();
        foreach (var batch in grants.Chunk(GrantsPerSubmit))
        {
            await Submit([.. batch.Select(VaultChange.AddShareGrant)], cancellationToken);
        }

        foreach (var grant in grants)
        {
            await inbox.AddIfAbsentAsync(
                new ShareInboxEntry
                {
                    RecipientId = grant.RecipientId,
                    OwnerId = Owner,
                    ItemKind = ShareItemKind.Secret,
                    ItemId = secretId,
                    SharedAt = Epoch,
                },
                cancellationToken);
        }

        return grants;
    }

    public async Task<IReadOnlyList<TableEntity>> PartitionRowsAsync(CancellationToken cancellationToken) =>
        await VaultRows.ListPartitionAsync(vaultsTable, Owner, cancellationToken);

    public async Task<int> InboxRowCountAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        await foreach (var _ in inboxTable.QueryAsync<TableEntity>(cancellationToken: cancellationToken))
        {
            count++;
        }

        return count;
    }

    public async Task<int> GrantsStillStoredAsync(IEnumerable<ShareGrant> grants, CancellationToken cancellationToken)
    {
        var remaining = 0;
        foreach (var grant in grants)
        {
            var rows = await Store.GetShareGrantRowsAsync(Owner, grant.ItemKind, grant.ItemId, grant.RecipientId, cancellationToken);
            if (rows is not null)
            {
                remaining++;
            }
        }

        return remaining;
    }

    public async Task RestoreAsync(Guid secretId, CancellationToken cancellationToken)
    {
        var binned = (await Store.GetRecycledSecretAsync(Owner, secretId, cancellationToken))!;
        binned.Value.DeletedAt = null;
        binned.Value.DeletionBatchId = null;
        var result = await Submit([VaultChange.RestoreSecret(binned.Value, binned.ETag)], cancellationToken);
        Assert.Equal(StoreWriteResult.Written, result.Result);
    }

    public async Task<VaultWriteResult> Submit(IReadOnlyList<VaultChange> changes, CancellationToken cancellationToken)
    {
        var result = await Store.SubmitAsync(Owner, changes, cancellationToken);
        Assert.Equal(StoreWriteResult.Written, result.Result);
        return result;
    }

    public ShareGrant NewGrant(Guid secretId) => new()
    {
        OwnerId = Owner,
        ItemKind = ShareItemKind.Secret,
        ItemId = secretId,
        RecipientId = Guid.NewGuid(),
        Permission = SharePermission.View,
        RecipientDisplayName = "Recipient " + Guid.NewGuid().ToString("N")[..6],
        RecipientEmail = "recipient@example.test",
        GrantedById = Owner,
        GrantedByDisplayName = "Granting Owner",
        GrantedAt = Epoch,
        ModifiedAt = Epoch,
    };

    private Secret NewSecret() => new()
    {
        Id = Guid.CreateVersion7(DateTimeOffset.UtcNow),
        OwnerId = Owner,
        Type = SecretType.Login,
        NameEnvelope = RandomNumberGenerator.GetBytes(40),
        UsernameEnvelope = RandomNumberGenerator.GetBytes(30),
        ValueEnvelope = RandomNumberGenerator.GetBytes(50),
        DataKey = new WrappedDataKey("key-1", "RSA-OAEP-256", RandomNumberGenerator.GetBytes(48)),
        MetadataKeyVersion = 1,
        Revision = 1,
        CreatedAt = Epoch,
        ModifiedAt = Epoch,
    };

    private SecretVersion NewVersion(Secret secret, int index) => new()
    {
        Id = Guid.CreateVersion7(Epoch.AddMinutes(index + 1)),
        SecretId = secret.Id,
        OwnerId = Owner,
        ValueEnvelope = RandomNumberGenerator.GetBytes(60),
        Revision = index + 1,
        ReplacedAt = Epoch.AddHours(index + 1),
    };
}
