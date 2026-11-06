// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Encryption;
using SharpVault.Domain.Vaults;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
public sealed class VaultStoreTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly DateTimeOffset Epoch = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly Guid _owner = Guid.NewGuid();

    private IVaultStore Store
    {
        get
        {
            _ = Factory.Server;
            return Factory.Services.GetRequiredService<IVaultStore>();
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Guid OrderedId(int sequence) => Guid.CreateVersion7(Epoch.AddMilliseconds(sequence * 3));

    private static WrappedDataKey Key() => new("key-1", "RSA-OAEP-256", RandomNumberGenerator.GetBytes(48));

    private Secret NewSecret(int sequence, Guid? folderId = null) => new()
    {
        Id = OrderedId(sequence),
        OwnerId = _owner,
        Type = SecretType.Login,
        FolderId = folderId,
        NameEnvelope = RandomNumberGenerator.GetBytes(40),
        UsernameEnvelope = RandomNumberGenerator.GetBytes(30),
        ValueEnvelope = RandomNumberGenerator.GetBytes(50),
        DataKey = Key(),
        MetadataKeyVersion = 1,
        Revision = 1,
        CreatedAt = Epoch,
        ModifiedAt = Epoch,
    };

    private static Secret Recycled(Secret secret, DateTimeOffset deletedAt, Guid? batch = null)
    {
        secret.DeletedAt = deletedAt;
        secret.DeletionBatchId = batch ?? Guid.NewGuid();
        return secret;
    }

    private Folder NewFolder(int sequence, Guid? parent = null) => new()
    {
        Id = OrderedId(sequence),
        OwnerId = _owner,
        ParentId = parent,
        NameEnvelope = RandomNumberGenerator.GetBytes(40),
        MetadataKeyVersion = 1,
        CreatedAt = Epoch,
        ModifiedAt = Epoch,
    };

    private VaultKeyRing NewKeyRing(Guid? owner = null) => new()
    {
        OwnerId = owner ?? _owner,
        MetadataKey = Key(),
        MetadataKeyVersion = 1,
        CreatedAt = Epoch,
    };

    private async Task<Versioned<VaultKeyRing>> ArrangeKeyRingAsync()
    {
        var added = await Store.AddKeyRingAsync(NewKeyRing(), Token);
        return added ?? throw new InvalidOperationException("The key ring already existed.");
    }

    private async Task AddAsync(params VaultChange[] changes)
    {
        var result = await Store.SubmitAsync(_owner, changes, Token);
        Assert.Equal(StoreWriteResult.Written, result.Result);
    }

    private async Task<List<Secret>> ListAllSecretsAsync(SecretFolderFilter filter, int pageSize)
    {
        var all = new List<Secret>();
        string? after = null;
        for (var guard = 0; guard < 500; guard++)
        {
            var page = await Store.ListSecretsAsync(_owner, filter, pageSize, after, Token);
            all.AddRange(page.Items.Select(item => item.Value));
            if (page.ContinuationToken is null)
            {
                return all;
            }

            after = page.ContinuationToken;
        }

        throw new InvalidOperationException("The listing never ended.");
    }

    [Fact]
    public async Task AddKeyRingAsync_Twice_SecondReturnsNull()
    {
        var first = await Store.AddKeyRingAsync(NewKeyRing(), Token);
        var second = await Store.AddKeyRingAsync(NewKeyRing(), Token);

        Assert.NotNull(first);
        Assert.Null(second);
        var stored = await Store.GetKeyRingAsync(_owner, Token);
        Assert.NotNull(stored);
        Assert.Equal(first.Value.MetadataKey.Value, stored.Value.MetadataKey.Value);
        Assert.Equal(first.ETag, stored.ETag);
    }

    [Fact]
    public async Task GetKeyRingAsync_NoRing_ReturnsNull()
    {
        Assert.Null(await Store.GetKeyRingAsync(_owner, Token));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task GetKeyRingAsync_OtherOwner_ReturnsNull()
    {
        await ArrangeKeyRingAsync();

        Assert.Null(await Store.GetKeyRingAsync(Guid.NewGuid(), Token));
    }

    [Fact]
    public async Task SubmitAsync_Empty_WritesNothingAndMakesNoRequest()
    {
        _ = Factory.Server;
        StorageRequests.Clear();

        var result = await Store.SubmitAsync(_owner, [], Token);

        Assert.Equal(StoreWriteResult.Written, result.Result);
        Assert.Equal(0, result.CompletedChanges);
        Assert.Empty(result.ETags);
        Assert.Empty(StorageRequests.Requests);
    }

    [Fact]
    public async Task SubmitAsync_ChainedKeyRingTouch_AllChunksCommit()
    {
        var ring = await ArrangeKeyRingAsync();
        var changes = new List<VaultChange> { VaultChange.TouchKeyRing(ring.ETag) };
        changes.AddRange(Enumerable.Range(0, 250).Select(index => VaultChange.AddSecret(NewSecret(index))));

        var result = await Store.SubmitAsync(_owner, changes, Token);

        Assert.Equal(StoreWriteResult.Written, result.Result);
        Assert.Equal(251, result.CompletedChanges);
        Assert.Equal(251, result.ETags.Count);
        var listed = await ListAllSecretsAsync(SecretFolderFilter.All, 200);
        Assert.Equal(250, listed.Count);
        Assert.Equal(250, listed.Select(secret => secret.Id).Distinct().Count());
        var after = await Store.GetKeyRingAsync(_owner, Token);
        Assert.NotNull(after);
        Assert.NotEqual(ring.ETag, after.ETag);
    }

    [Fact]
    public async Task SubmitAsync_MultiChunkTouch_ReturnsTheCurrentKeyRingETagForTheTouchChange()
    {
        var ring = await ArrangeKeyRingAsync();
        var changes = new List<VaultChange> { VaultChange.TouchKeyRing(ring.ETag) };
        changes.AddRange(Enumerable.Range(0, 250).Select(index => VaultChange.AddSecret(NewSecret(index))));

        var result = await Store.SubmitAsync(_owner, changes, Token);

        var stored = await Store.GetKeyRingAsync(_owner, Token);
        Assert.NotNull(stored);
        Assert.Equal(stored.ETag, result.ETags[0]);
    }

    [Fact]
    public async Task SubmitAsync_ChainedKeyRingTouch_AfterwardsTheOldETagIsStale()
    {
        var ring = await ArrangeKeyRingAsync();
        var first = await Store.SubmitAsync(_owner, [VaultChange.TouchKeyRing(ring.ETag), VaultChange.AddSecret(NewSecret(0))], Token);

        var second = await Store.SubmitAsync(_owner, [VaultChange.TouchKeyRing(ring.ETag), VaultChange.AddSecret(NewSecret(1))], Token);

        Assert.Equal(StoreWriteResult.Written, first.Result);
        Assert.Equal(StoreWriteResult.Conflict, second.Result);
        Assert.Equal(0, second.CompletedChanges);
        Assert.Single(await ListAllSecretsAsync(SecretFolderFilter.All, 50));
    }

    [Fact]
    public async Task SubmitAsync_StaleTouch_ReturnsConflictAndNoChange()
    {
        var ring = await ArrangeKeyRingAsync();
        await AddAsync(VaultChange.TouchKeyRing(ring.ETag));
        var secret = NewSecret(0);

        var result = await Store.SubmitAsync(_owner, [VaultChange.TouchKeyRing(ring.ETag), VaultChange.AddSecret(secret)], Token);

        Assert.Equal(StoreWriteResult.Conflict, result.Result);
        Assert.Equal(0, result.CompletedChanges);
        Assert.Null(await Store.GetSecretAsync(_owner, secret.Id, Token));
    }

    [Fact]
    public async Task SubmitAsync_FailureInSecondChunk_KeepsTheFirstChunkAndReportsHowFarItGot()
    {
        var ring = await ArrangeKeyRingAsync();
        var existing = NewSecret(1000);
        await AddAsync(VaultChange.AddSecret(existing));
        var changes = new List<VaultChange> { VaultChange.TouchKeyRing(ring.ETag) };
        changes.AddRange(Enumerable.Range(0, 99).Select(index => VaultChange.AddSecret(NewSecret(index))));
        changes.Add(VaultChange.AddSecret(existing));
        Logs.Clear();

        var result = await Store.SubmitAsync(_owner, changes, Token);

        Assert.Equal(StoreWriteResult.AlreadyExists, result.Result);
        Assert.Equal(100, result.CompletedChanges);
        Assert.Equal(101, result.ETags.Count);
        Assert.Null(result.ETags[100]);
        Assert.Equal(100, (await ListAllSecretsAsync(SecretFolderFilter.All, 200)).Count);
        var warning = Assert.Single(Logs.Entries, entry => entry.Category == typeof(TableVaultStore).FullName && entry.Level == LogLevel.Warning);
        Assert.Contains("100 of 101", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_owner.ToString("D"), warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitAsync_FailureInFirstChunk_LogsNoPartialWriteWarning()
    {
        var existing = NewSecret(0);
        await AddAsync(VaultChange.AddSecret(existing));
        Logs.Clear();

        var result = await Store.SubmitAsync(_owner, [VaultChange.AddSecret(existing)], Token);

        Assert.Equal(StoreWriteResult.AlreadyExists, result.Result);
        Assert.Equal(0, result.CompletedChanges);
        Assert.DoesNotContain(Logs.Entries, entry => entry.Category == typeof(TableVaultStore).FullName && entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SubmitAsync_MissingDelete_ReturnsNotFound()
    {
        var recycled = Recycled(NewSecret(0), Epoch);

        var result = await Store.SubmitAsync(_owner, [VaultChange.PurgeSecret(recycled, "W/\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"")], Token);

        Assert.Equal(StoreWriteResult.NotFound, result.Result);
        Assert.Equal(0, result.CompletedChanges);
    }

    [Fact]
    public async Task SubmitAsync_ReturnsETagsAlignedWithChanges()
    {
        var first = NewSecret(0);
        var second = NewSecret(1);
        var toRecycle = NewSecret(2);
        await AddAsync(VaultChange.AddSecret(toRecycle));
        var stored = await Store.GetSecretAsync(_owner, toRecycle.Id, Token);
        Assert.NotNull(stored);
        var recycled = Recycled(NewSecret(2), Epoch);
        recycled.Id = toRecycle.Id;
        var folder = NewFolder(5);

        var result = await Store.SubmitAsync(
            _owner,
            [VaultChange.AddSecret(first), VaultChange.RecycleSecret(recycled, stored.ETag), VaultChange.AddFolder(folder), VaultChange.AddSecret(second)],
            Token);

        Assert.Equal(StoreWriteResult.Written, result.Result);
        Assert.Equal(4, result.ETags.Count);
        Assert.Equal((await Store.GetSecretAsync(_owner, first.Id, Token))!.ETag, result.ETags[0]);
        Assert.Equal((await Store.GetRecycledSecretAsync(_owner, recycled.Id, Token))!.ETag, result.ETags[1]);
        Assert.Equal((await Store.GetFolderAsync(_owner, folder.Id, Token))!.ETag, result.ETags[2]);
        Assert.Equal((await Store.GetSecretAsync(_owner, second.Id, Token))!.ETag, result.ETags[3]);
        Assert.Null(await Store.GetSecretAsync(_owner, recycled.Id, Token));
    }

    [Fact]
    public async Task SubmitAsync_PurgeAndDeleteVersion_ReturnNullETags()
    {
        var secret = NewSecret(0);
        var version = new SecretVersion { Id = OrderedId(9), SecretId = secret.Id, OwnerId = _owner, ValueEnvelope = RandomNumberGenerator.GetBytes(20), Revision = 1, ReplacedAt = Epoch };
        await AddAsync(VaultChange.AddVersion(version));
        var storedVersion = await Store.GetVersionAsync(_owner, secret.Id, version.Id, Token);
        Assert.NotNull(storedVersion);

        var result = await Store.SubmitAsync(_owner, [VaultChange.DeleteVersion(version, storedVersion.ETag)], Token);

        Assert.Equal(StoreWriteResult.Written, result.Result);
        Assert.Null(result.ETags[0]);
        Assert.Null(await Store.GetVersionAsync(_owner, secret.Id, version.Id, Token));
    }

    [Fact]
    public async Task SubmitAsync_ReplaceWithStaleETag_ReturnsConflictAndKeepsTheStoredRow()
    {
        var secret = NewSecret(0);
        await AddAsync(VaultChange.AddSecret(secret));
        var first = (await Store.GetSecretAsync(_owner, secret.Id, Token))!;
        secret.Revision = 2;
        await AddAsync(VaultChange.ReplaceSecret(secret, first.ETag));
        secret.Revision = 3;

        var result = await Store.SubmitAsync(_owner, [VaultChange.ReplaceSecret(secret, first.ETag)], Token);

        Assert.Equal(StoreWriteResult.Conflict, result.Result);
        Assert.Equal(2, (await Store.GetSecretAsync(_owner, secret.Id, Token))!.Value.Revision);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SubmitAsync_ForeignOwnerInChange_IsRejectedBeforeAnyRequest()
    {
        _ = Factory.Server;
        var foreign = NewSecret(0);
        foreign.OwnerId = Guid.NewGuid();
        StorageRequests.Clear();

        await Assert.ThrowsAsync<ArgumentException>(() => Store.SubmitAsync(_owner, [VaultChange.AddSecret(foreign)], Token));

        Assert.Empty(StorageRequests.Requests);
    }

    [Fact]
    public async Task ListSecretsAsync_FolderFilterOnlyMatchingRows()
    {
        var folder = Guid.NewGuid();
        var inFolder = Enumerable.Range(0, 5).Select(index => NewSecret(index * 2, folder)).ToList();
        var others = Enumerable.Range(0, 5).Select(index => NewSecret((index * 2) + 1)).ToList();
        await AddAsync([.. inFolder.Concat(others).Select(VaultChange.AddSecret)]);

        var inside = await ListAllSecretsAsync(SecretFolderFilter.InFolder(folder), 50);
        var unfiled = await ListAllSecretsAsync(SecretFolderFilter.Unfiled, 50);
        var everything = await ListAllSecretsAsync(SecretFolderFilter.All, 50);

        Assert.Equal(inFolder.Select(secret => secret.Id).Order(), inside.Select(secret => secret.Id).Order());
        Assert.All(inside, secret => Assert.Equal(folder, secret.FolderId));
        Assert.Equal(others.Select(secret => secret.Id).Order(), unfiled.Select(secret => secret.Id).Order());
        Assert.All(unfiled, secret => Assert.Null(secret.FolderId));
        Assert.Equal(10, everything.Count);
    }

    [Fact]
    public async Task ListSecretsAsync_FolderFilterWithSmallPages_ReturnsEveryMatchOnceAcrossPages()
    {
        var folder = Guid.NewGuid();
        var all = Enumerable.Range(0, 60).Select(index => NewSecret(index, index % 4 == 0 ? folder : null)).ToList();
        await AddAsync([.. all.Select(VaultChange.AddSecret)]);

        var inside = await ListAllSecretsAsync(SecretFolderFilter.InFolder(folder), 4);

        Assert.Equal(15, inside.Count);
        Assert.Equal(15, inside.Select(secret => secret.Id).Distinct().Count());
        Assert.Equal(all.Where(secret => secret.FolderId == folder).Select(secret => secret.Id), inside.Select(secret => secret.Id));
    }

    [Fact]
    public async Task ListSecretsAsync_Paging_IsExclusiveOfTheCursorAndKeepsCreationOrder()
    {
        var secrets = Enumerable.Range(0, 7).Select(index => NewSecret(index)).ToList();
        await AddAsync([.. secrets.Select(VaultChange.AddSecret)]);

        var first = await Store.ListSecretsAsync(_owner, SecretFolderFilter.All, 3, null, Token);
        var second = await Store.ListSecretsAsync(_owner, SecretFolderFilter.All, 3, first.ContinuationToken, Token);

        Assert.Equal(secrets.Take(3).Select(secret => secret.Id), first.Items.Select(item => item.Value.Id));
        Assert.Equal(ContinuationTokens.ForSecret(first.Items[^1].Value.Id), first.ContinuationToken);
        Assert.Equal(secrets.Skip(3).Take(3).Select(secret => secret.Id), second.Items.Select(item => item.Value.Id));
    }

    [Fact]
    public async Task ListSecretsAsync_Empty_ReturnsEmptyPageWithoutToken()
    {
        var page = await Store.ListSecretsAsync(_owner, SecretFolderFilter.All, 50, null, Token);

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListSecretsAsync_ExactlyOnePage_EndsWithoutLosingItems()
    {
        await AddAsync([.. Enumerable.Range(0, 50).Select(index => VaultChange.AddSecret(NewSecret(index)))]);

        var all = await ListAllSecretsAsync(SecretFolderFilter.All, 50);

        Assert.Equal(50, all.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListSecretsAsync_ExcludesRecycledRowsVersionsFoldersAndTheKeyRing()
    {
        await ArrangeKeyRingAsync();
        var active = NewSecret(0);
        var binned = Recycled(NewSecret(1), Epoch);
        await AddAsync(
            VaultChange.AddSecret(active),
            VaultChange.AddSecret(binned),
            VaultChange.AddFolder(NewFolder(2)),
            VaultChange.AddVersion(new SecretVersion { Id = OrderedId(3), SecretId = active.Id, OwnerId = _owner, ValueEnvelope = [1], Revision = 1, ReplacedAt = Epoch }));

        var listed = await ListAllSecretsAsync(SecretFolderFilter.All, 50);

        Assert.Equal([active.Id], listed.Select(secret => secret.Id));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListSecretsAsync_OtherOwnersRows_AreNeverReturned()
    {
        var other = Guid.NewGuid();
        var foreign = NewSecret(0);
        foreign.OwnerId = other;
        var otherResult = await Store.SubmitAsync(other, [VaultChange.AddSecret(foreign)], Token);
        Assert.Equal(StoreWriteResult.Written, otherResult.Result);
        await AddAsync(VaultChange.AddSecret(NewSecret(1)));

        var mine = await ListAllSecretsAsync(SecretFolderFilter.All, 50);

        Assert.Single(mine);
        Assert.Null(await Store.GetSecretAsync(_owner, foreign.Id, Token));
    }

    [Fact]
    public async Task ListSecretsInFolderAsync_RespectsTheLimit()
    {
        var folder = Guid.NewGuid();
        await AddAsync([.. Enumerable.Range(0, 12).Select(index => VaultChange.AddSecret(NewSecret(index, folder)))]);

        var limited = await Store.ListSecretsInFolderAsync(_owner, folder, 5, Token);
        var all = await Store.ListSecretsInFolderAsync(_owner, folder, 100, Token);
        var root = await Store.ListSecretsInFolderAsync(_owner, null, 100, Token);

        Assert.Equal(5, limited.Count);
        Assert.Equal(12, all.Count);
        Assert.Empty(root);
    }

    [Fact]
    public async Task ListFoldersAsync_ReturnsActiveFoldersOnly()
    {
        var active = NewFolder(0);
        var child = NewFolder(1, active.Id);
        var binned = NewFolder(2);
        binned.DeletedAt = Epoch;
        binned.DeletionBatchId = Guid.NewGuid();
        await AddAsync(VaultChange.AddFolder(active), VaultChange.AddFolder(child), VaultChange.AddFolder(binned), VaultChange.AddSecret(NewSecret(3)));

        var folders = await Store.ListFoldersAsync(_owner, Token);

        Assert.Equal([active.Id, child.Id], folders.Select(folder => folder.Value.Id).Order());
        Assert.Equal(active.Id, folders.Single(folder => folder.Value.Id == child.Id).Value.ParentId);
    }

    [Fact]
    public async Task ListVersionsAsync_ReturnsOnlyTheRequestedSecretsVersions()
    {
        var secret = NewSecret(0);
        var other = NewSecret(1);
        var versions = Enumerable.Range(10, 4)
            .Select(index => new SecretVersion { Id = OrderedId(index), SecretId = secret.Id, OwnerId = _owner, ValueEnvelope = [1, 2], Revision = index, ReplacedAt = Epoch })
            .ToList();
        var foreign = new SecretVersion { Id = OrderedId(30), SecretId = other.Id, OwnerId = _owner, ValueEnvelope = [3], Revision = 1, ReplacedAt = Epoch };
        await AddAsync([.. versions.Select(VaultChange.AddVersion), VaultChange.AddVersion(foreign)]);

        var listed = await Store.ListVersionsAsync(_owner, secret.Id, Token);

        Assert.Equal(versions.Select(version => version.Id), listed.Select(version => version.Value.Id));
        Assert.All(listed, version => Assert.Equal(secret.Id, version.Value.SecretId));
    }

    [Fact]
    public async Task ListRecycleBinAsync_Paging_ReturnsFoldersThenSecretsExactlyOnce()
    {
        var folders = Enumerable.Range(0, 3).Select(index => NewFolder(index)).ToList();
        foreach (var folder in folders)
        {
            folder.DeletedAt = Epoch;
            folder.DeletionBatchId = Guid.NewGuid();
        }

        var secrets = Enumerable.Range(10, 5).Select(index => Recycled(NewSecret(index), Epoch)).ToList();
        await AddAsync([.. folders.Select(VaultChange.AddFolder), .. secrets.Select(VaultChange.AddSecret), VaultChange.AddSecret(NewSecret(99))]);

        var seen = new List<RecycleBinEntry>();
        string? after = null;
        var tokens = new List<string>();
        do
        {
            var page = await Store.ListRecycleBinAsync(_owner, 2, after, Token);
            seen.AddRange(page.Items);
            after = page.ContinuationToken;
            if (after is not null)
            {
                tokens.Add(after);
            }
        }
        while (after is not null);

        Assert.Equal(8, seen.Count);
        Assert.Equal(3, seen.Count(entry => entry.Folder is not null));
        Assert.Equal(5, seen.Count(entry => entry.Secret is not null));
        Assert.Equal(folders.Select(folder => folder.Id).Order(), seen.Where(entry => entry.Folder is not null).Select(entry => entry.Folder!.Value.Id).Order());
        Assert.Equal(secrets.Select(secret => secret.Id).Order(), seen.Where(entry => entry.Secret is not null).Select(entry => entry.Secret!.Value.Id).Order());
        Assert.True(seen.Take(3).All(entry => entry.Folder is not null));
        Assert.All(tokens, token => Assert.Matches("^[fs]_[0-9a-f]{32}$", token));
    }

    [Fact]
    public async Task ListRecycleBinAsync_Cursor_IsExclusiveAndSkipsToTheNextRow()
    {
        var secrets = Enumerable.Range(0, 4).Select(index => Recycled(NewSecret(index), Epoch)).ToList();
        await AddAsync([.. secrets.Select(VaultChange.AddSecret)]);

        var first = await Store.ListRecycleBinAsync(_owner, 2, null, Token);
        var second = await Store.ListRecycleBinAsync(_owner, 2, first.ContinuationToken, Token);

        Assert.Equal(secrets.Take(2).Select(secret => secret.Id), first.Items.Select(entry => entry.Secret!.Value.Id));
        Assert.Equal("s_" + secrets[1].Id.ToString("N"), first.ContinuationToken);
        Assert.Equal(secrets.Skip(2).Select(secret => secret.Id), second.Items.Select(entry => entry.Secret!.Value.Id));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListRecycleBinAsync_ExcludesActiveRowsKeyRingAndVersions()
    {
        await ArrangeKeyRingAsync();
        var binned = Recycled(NewSecret(1), Epoch);
        await AddAsync(
            VaultChange.AddSecret(NewSecret(0)),
            VaultChange.AddSecret(binned),
            VaultChange.AddFolder(NewFolder(2)),
            VaultChange.AddVersion(new SecretVersion { Id = OrderedId(3), SecretId = binned.Id, OwnerId = _owner, ValueEnvelope = [1], Revision = 1, ReplacedAt = Epoch }));

        var page = await Store.ListRecycleBinAsync(_owner, 50, null, Token);

        var entry = Assert.Single(page.Items);
        Assert.Equal(binned.Id, entry.Secret!.Value.Id);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListRecycleBinAsync_Empty_ReturnsEmptyPage()
    {
        var page = await Store.ListRecycleBinAsync(_owner, 50, null, Token);

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListRecycledBeforeAsync_UsesDeletedAtFilter()
    {
        var old = Recycled(NewSecret(0), Epoch.AddDays(-40));
        var recent = Recycled(NewSecret(1), Epoch.AddDays(-1));
        var oldFolder = NewFolder(2);
        oldFolder.DeletedAt = Epoch.AddDays(-45);
        oldFolder.DeletionBatchId = Guid.NewGuid();
        await AddAsync(VaultChange.AddSecret(old), VaultChange.AddSecret(recent), VaultChange.AddFolder(oldFolder), VaultChange.AddSecret(NewSecret(3)));

        var batch = await Store.ListRecycledBeforeAsync(_owner, Epoch.AddDays(-30), Token);

        Assert.Equal([old.Id], batch.Secrets.Select(secret => secret.Value.Id));
        Assert.Equal([oldFolder.Id], batch.Folders.Select(folder => folder.Value.Id));
    }

    [Fact]
    public async Task ListRecycledBeforeAsync_ExactlyAtTheCutoff_IsNotIncluded()
    {
        var atCutoff = Recycled(NewSecret(0), Epoch);
        await AddAsync(VaultChange.AddSecret(atCutoff));

        var batch = await Store.ListRecycledBeforeAsync(_owner, Epoch, Token);

        Assert.Empty(batch.Secrets);
    }

    [Fact]
    public async Task ListRecycledBatchAsync_ReturnsOnlyTheBatch()
    {
        var batchId = Guid.NewGuid();
        var inBatch = Recycled(NewSecret(0), Epoch, batchId);
        var otherBatch = Recycled(NewSecret(1), Epoch);
        var folder = NewFolder(2);
        folder.DeletedAt = Epoch;
        folder.DeletionBatchId = batchId;
        await AddAsync(VaultChange.AddSecret(inBatch), VaultChange.AddSecret(otherBatch), VaultChange.AddFolder(folder));

        var batch = await Store.ListRecycledBatchAsync(_owner, batchId, Token);

        Assert.Equal([inBatch.Id], batch.Secrets.Select(secret => secret.Value.Id));
        Assert.Equal([folder.Id], batch.Folders.Select(item => item.Value.Id));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task EveryStoreQuery_CarriesAPartitionKeyCondition()
    {
        var secret = NewSecret(0);
        await AddAsync(VaultChange.AddSecret(secret));
        _ = Factory.Server;
        StorageRequests.Clear();

        await Store.ListSecretsAsync(_owner, SecretFolderFilter.InFolder(Guid.NewGuid()), 10, ContinuationTokens.ForSecret(secret.Id), Token);
        await Store.ListSecretsInFolderAsync(_owner, null, 10, Token);
        await Store.ListFoldersAsync(_owner, Token);
        await Store.ListVersionsAsync(_owner, secret.Id, Token);
        await Store.ListRecycleBinAsync(_owner, 10, "s_" + secret.Id.ToString("N"), Token);
        await Store.ListRecycledBatchAsync(_owner, Guid.NewGuid(), Token);
        await Store.ListRecycledBeforeAsync(_owner, Epoch, Token);

        Assert.NotEmpty(StorageRequests.Requests);
        AssertNoQueryWithoutPartitionKey();
    }

    [Theory]
    [InlineData("A")]
    [InlineData("t")]
    [InlineData("t!!!")]
    [InlineData("S_00000000000000000000000000000000")]
    [InlineData("f_00000000000000000000000000000000")]
    [Trait("Category", "Security")]
    public async Task ListSecretsAsync_MalformedToken_Throws(string token)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store.ListSecretsAsync(_owner, SecretFolderFilter.All, 10, token, Token));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("t")]
    [InlineData("t!!!")]
    [InlineData("F_00000000000000000000000000000000")]
    [InlineData("00000000000000000000000000000000")]
    [Trait("Category", "Security")]
    public async Task ListRecycleBinAsync_MalformedToken_Throws(string token)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store.ListRecycleBinAsync(_owner, 10, token, Token));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListSecretsAsync_StorageTokenNamingAnotherOwnersPartition_NeverReturnsTheirRows()
    {
        var other = Guid.NewGuid();
        var foreign = NewSecret(0);
        foreign.OwnerId = other;
        var otherResult = await Store.SubmitAsync(other, [VaultChange.AddSecret(foreign)], Token);
        Assert.Equal(StoreWriteResult.Written, otherResult.Result);
        await AddAsync(VaultChange.AddSecret(NewSecret(1)));

        foreach (var forged in new[] { $"{other:D} bs_{foreign.Id:N}", $"{other:D} ", $"{other:D}" })
        {
            var page = await Store.ListSecretsAsync(_owner, SecretFolderFilter.All, 50, ContinuationTokens.ForStorage(forged), Token);

            Assert.DoesNotContain(page.Items, item => item.Value.OwnerId == other || item.Value.Id == foreign.Id);
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListRecycleBinAsync_StorageTokenNamingAnotherOwnersPartition_NeverReturnsTheirRows()
    {
        var other = Guid.NewGuid();
        var foreign = Recycled(NewSecret(0), Epoch);
        foreign.OwnerId = other;
        var otherResult = await Store.SubmitAsync(other, [VaultChange.AddSecret(foreign)], Token);
        Assert.Equal(StoreWriteResult.Written, otherResult.Result);
        await AddAsync(VaultChange.AddSecret(Recycled(NewSecret(1), Epoch)));

        foreach (var forged in new[] { $"{other:D} bs_{foreign.Id:N}", $"{other:D} ", $"{other:D}" })
        {
            var page = await Store.ListRecycleBinAsync(_owner, 50, ContinuationTokens.ForStorage(forged), Token);

            Assert.DoesNotContain(page.Items, entry => entry.Secret!.Value.OwnerId == other);
        }
    }

    [Fact]
    public async Task SubmitAsync_StoredRows_ContainNoComputedBinaryLengthColumn()
    {
        var folder = NewFolder(1);
        var secret = NewSecret(2, folder.Id);
        var version = new SecretVersion { Id = OrderedId(3), SecretId = secret.Id, OwnerId = _owner, ValueEnvelope = [1, 2], Revision = 1, ReplacedAt = Epoch };
        await AddAsync(VaultChange.AddFolder(folder), VaultChange.AddSecret(secret), VaultChange.AddVersion(version));

        var table = Factory.StorageAdminClient.GetTableClient(SharpVault.Infrastructure.Storage.TableNames.Vaults);
        var folderRow = await IntegrationTests.Vault.VaultRows.GetAsync(table, _owner, VaultKeys.FolderRowKey(folder.Id, recycled: false), Token);
        var secretRow = await IntegrationTests.Vault.VaultRows.GetAsync(table, _owner, VaultKeys.SecretRowKey(secret.Id, recycled: false), Token);
        var versionRow = await IntegrationTests.Vault.VaultRows.GetAsync(table, _owner, VaultKeys.VersionRowKey(secret.Id, version.Id), Token);

        Assert.NotNull(folderRow);
        Assert.NotNull(secretRow);
        Assert.NotNull(versionRow);
        Assert.DoesNotContain("BinaryLength", folderRow.Keys);
        Assert.DoesNotContain("BinaryLength", secretRow.Keys);
        Assert.DoesNotContain("BinaryLength", versionRow.Keys);
        Assert.Equal(
            ["CreatedAt", "MetadataKeyVersion", "ModifiedAt", "NameEnvelope", "ParentKey", "PartitionKey", "RowKey", "Timestamp", "odata.etag"],
            folderRow.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["CreatedAt", "DataKeyAlgorithm", "DataKeyId", "FolderKey", "MetadataKeyVersion", "ModifiedAt", "NameEnvelope", "PartitionKey", "Revision", "RowKey", "Timestamp", "Type", "UsernameEnvelope", "ValueEnvelope", "WrappedDataKey", "odata.etag"],
            secretRow.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["PartitionKey", "ReplacedAt", "Revision", "RowKey", "SecretId", "Timestamp", "ValueEnvelope", "odata.etag"],
            versionRow.Keys.Order(StringComparer.Ordinal));
    }
}
