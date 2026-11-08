// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging.Abstractions;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.Infrastructure.OneTimeLinks;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.ApiTests;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.Storage;

[Collection(nameof(StorageTestGroup))]
public sealed class OneTimeLinkStoreAmbiguousWriteTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private const int IndexInsertPost = 2;
    private const int LinkInsertPost = 3;
    private const int ServerErrorStatus = 500;
    private const int ConflictStatus = 409;
    private const int PurgeBatchSize = 100;
    private static readonly TimeSpan AlreadyExpiredBy = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task CreateAsync_LinkRowFailsAfterItCommitted_PurgeStillReapsTheLink()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreFailingAfterCommit(LinkInsertPost, new RequestFailedException(ServerErrorStatus, "The request timed out after it was committed."));
        var (link, index) = NewPair();

        await Assert.ThrowsAsync<RequestFailedException>(() => store.CreateAsync(link, index, cancellationToken));
        await PurgeExpiredAsync(link.ExpiresAt, cancellationToken);

        Assert.Null(await PlainStore().GetAsync(link.Id, cancellationToken));
        Assert.Null(await PlainStore().GetIndexEntryAsync(link.CreatorId, link.Id, cancellationToken));
    }

    [Fact]
    public async Task CreateAsync_LinkRowConflictsAfterSdkRetryCommitted_ReturnsAlreadyExistsAndPurgeReapsTheLink()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreFailingAfterCommit(LinkInsertPost, new RequestFailedException(ConflictStatus, "The first attempt committed."));
        var (link, index) = NewPair();

        var result = await store.CreateAsync(link, index, cancellationToken);
        await PurgeExpiredAsync(link.ExpiresAt, cancellationToken);

        Assert.Equal(StoreWriteResult.AlreadyExists, result);
        Assert.Null(await PlainStore().GetAsync(link.Id, cancellationToken));
        Assert.Null(await PlainStore().GetIndexEntryAsync(link.CreatorId, link.Id, cancellationToken));
    }

    [Fact]
    public async Task CreateAsync_IndexRowFailsAfterItCommitted_PurgeStillReapsTheIndex()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreFailingAfterCommit(IndexInsertPost, new RequestFailedException(ServerErrorStatus, "The index write timed out after it was committed."));
        var (link, index) = NewPair();

        await Assert.ThrowsAsync<RequestFailedException>(() => store.CreateAsync(link, index, cancellationToken));
        await PurgeExpiredAsync(link.ExpiresAt, cancellationToken);

        Assert.Null(await PlainStore().GetIndexEntryAsync(link.CreatorId, link.Id, cancellationToken));
        Assert.Null(await PlainStore().GetAsync(link.Id, cancellationToken));
    }

    private static (OneTimeLink Link, OneTimeLinkIndexEntry Index) NewPair()
    {
        var expiresAt = DateTimeOffset.UtcNow - AlreadyExpiredBy;
        var link = new OneTimeLink
        {
            Id = Guid.NewGuid(),
            CreatorId = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            SecretId = Guid.NewGuid(),
            Payload = [0x80, 1, 2, 3],
            CreatedAt = expiresAt,
            ExpiresAt = expiresAt,
        };
        var index = new OneTimeLinkIndexEntry
        {
            LinkId = link.Id,
            CreatorId = link.CreatorId,
            OwnerId = link.OwnerId,
            SecretId = link.SecretId,
            NameEnvelope = [1],
            CreatedAt = link.CreatedAt,
            ExpiresAt = link.ExpiresAt,
        };
        return (link, index);
    }

    private TableOneTimeLinkStore StoreFailingAfterCommit(int postNumber, Exception failure)
    {
        var options = new TableClientOptions();
        options.AddPolicy(new CommitThenFailPolicy(postNumber, failure), HttpPipelinePosition.PerCall);
        return new TableOneTimeLinkStore(new TableServiceClient(Factory.AzuriteConnectionString, options), NullLogger<TableOneTimeLinkStore>.Instance);
    }

    private TableOneTimeLinkStore PlainStore() =>
        new(Factory.StorageAdminClient, NullLogger<TableOneTimeLinkStore>.Instance);

    private async Task PurgeExpiredAsync(DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var store = PlainStore();
        var now = DateTimeOffset.UtcNow;
        for (var hour = FloorToHour(expiresAt); hour <= FloorToHour(now); hour = hour.AddHours(1))
        {
            string? continuationToken = null;
            do
            {
                var page = await store.ListExpiryBucketAsync(hour, PurgeBatchSize, continuationToken, cancellationToken);
                foreach (var entry in page.Items.Where(entry => entry.ExpiresAt <= now))
                {
                    await store.DeleteExpiredAsync(entry, cancellationToken);
                }

                continuationToken = page.ContinuationToken;
            }
            while (continuationToken is not null);
        }
    }

    private static DateTimeOffset FloorToHour(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerHour), TimeSpan.Zero);

    private sealed class CommitThenFailPolicy(int failingPost, Exception failure) : HttpPipelinePolicy
    {
        private int _posts;

        public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline) =>
            throw new NotSupportedException("The commit-then-fail fault is injected on the asynchronous path only.");

        public override async ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            if (message.Request.Method != RequestMethod.Post)
            {
                await ProcessNextAsync(message, pipeline);
                return;
            }

            _posts++;
            await ProcessNextAsync(message, pipeline);
            if (_posts == failingPost)
            {
                throw failure;
            }
        }
    }
}
