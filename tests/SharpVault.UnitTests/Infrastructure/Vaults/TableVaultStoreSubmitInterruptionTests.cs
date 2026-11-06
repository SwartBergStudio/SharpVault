// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Infrastructure.Vaults;

[Collection(MapsterGlobalSettings.Name)]
public sealed class TableVaultStoreSubmitInterruptionTests
{
    private const int ChangesAcrossSeveralChunks = 250;

    private readonly ThrowingTransactionTableClient _table = new();
    private readonly TableVaultStore _store;
    private readonly Guid _owner = Guid.NewGuid();

    public TableVaultStoreSubmitInterruptionTests()
    {
        var service = Substitute.For<TableServiceClient>();
        service.GetTableClient(TableNames.Vaults).Returns(_table);
        _store = new TableVaultStore(service, NullLogger<TableVaultStore>.Instance);
        TypeAdapterRegistration.EnsureInfrastructureMappings();
        TypeAdapterConfig.GlobalSettings.Scan(typeof(VaultKeys).Assembly);
    }

    private List<VaultChange> Changes() =>
        [.. Enumerable.Range(0, ChangesAcrossSeveralChunks).Select(_ => VaultChange.PurgeSecret(VaultSamples.Secret(_owner), "\"etag\""))];

    [Fact]
    public async Task SubmitAsync_LaterChunkThrows_ReturnsCompletedCountAndInterruption()
    {
        var failure = new TimeoutException("storage down");
        _table.FailOnCall = 2;
        _table.Failure = failure;

        var write = await _store.SubmitAsync(_owner, Changes(), TestContext.Current.CancellationToken);

        Assert.True(write.CompletedChanges > 0);
        Assert.True(write.CompletedChanges < ChangesAcrossSeveralChunks);
        Assert.NotEqual(StoreWriteResult.Written, write.Result);
        Assert.NotNull(write.Interruption);
        var thrown = Assert.Throws<TimeoutException>(() => write.Interruption.Throw());
        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task SubmitAsync_LaterChunkCancelled_ReturnsInterruption()
    {
        _table.FailOnCall = 2;
        _table.Failure = new OperationCanceledException();

        var write = await _store.SubmitAsync(_owner, Changes(), TestContext.Current.CancellationToken);

        Assert.True(write.CompletedChanges > 0);
        Assert.NotNull(write.Interruption);
    }

    [Fact]
    public async Task SubmitAsync_FirstChunkThrows_PropagatesUnchanged()
    {
        _table.FailOnCall = 1;
        _table.Failure = new TimeoutException("storage down");

        await Assert.ThrowsAsync<TimeoutException>(() => _store.SubmitAsync(_owner, Changes(), TestContext.Current.CancellationToken));
    }

    private sealed class ThrowingTransactionTableClient : TableClient
    {
        private int _calls;

        public int FailOnCall { get; set; }

        public Exception Failure { get; set; } = new InvalidOperationException();

        public override Task<Response<IReadOnlyList<Response>>> SubmitTransactionAsync(
            IEnumerable<TableTransactionAction> transactionActions,
            CancellationToken cancellationToken = default)
        {
            if (++_calls == FailOnCall)
            {
                throw Failure;
            }

            var actions = transactionActions.ToList();
            IReadOnlyList<Response> responses = [.. actions.Select(_ => (Response)new FakeAzureResponse(204, "\"w\""))];
            return Task.FromResult(Response.FromValue(responses, new FakeAzureResponse(202, null)));
        }
    }
}
