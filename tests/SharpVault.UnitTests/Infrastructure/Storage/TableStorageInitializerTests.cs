// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables.Models;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.Infrastructure.Storage;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Storage;

public sealed class TableStorageInitializerTests
{
    private readonly TableServiceClient _client = Substitute.For<TableServiceClient>();
    private readonly CollectingLogger<TableStorageInitializer> _logger = new();

    [Fact]
    public async Task StartAsync_TableSetWithTwoNames_CreatesEachTableOnce()
    {
        var sut = CreateInitializer("Secrets", "Audit");

        await sut.StartAsync(TestContext.Current.CancellationToken);

        await _client.Received(1).CreateTableIfNotExistsAsync("Secrets", Arg.Any<CancellationToken>());
        await _client.Received(1).CreateTableIfNotExistsAsync("Audit", Arg.Any<CancellationToken>());
        Assert.Equal(2, _client.ReceivedCalls().Count());
    }

    [Fact]
    public async Task StartAsync_TableSetWithTwoNames_CreatesTablesInConfiguredOrder()
    {
        var created = new List<string>();
        _client.CreateTableIfNotExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                created.Add(call.Arg<string>());
                return Task.FromResult(Substitute.For<Response<TableItem>>());
            });
        var sut = CreateInitializer("Zebra", "Apple", "Mango");

        await sut.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Zebra", "Apple", "Mango"], created);
    }

    [Fact]
    public async Task StartAsync_EmptyTableSet_MakesNoStorageCalls()
    {
        var sut = CreateInitializer();

        await sut.StartAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_client.ReceivedCalls());
    }

    [Fact]
    public async Task StartAsync_WithCancellationToken_PassesTokenToEveryCall()
    {
        using var source = new CancellationTokenSource();
        var sut = CreateInitializer("Secrets", "Audit");

        await sut.StartAsync(source.Token);

        await _client.Received(2).CreateTableIfNotExistsAsync(Arg.Any<string>(), source.Token);
    }

    [Fact]
    public async Task StartAsync_StorageThrows_PropagatesException()
    {
        _client.CreateTableIfNotExistsAsync("Secrets", Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(503, "storage unavailable"));
        var sut = CreateInitializer("Secrets", "Audit");

        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => sut.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(503, exception.Status);
    }

    [Fact]
    public async Task StartAsync_FirstTableFails_DoesNotCreateRemainingTablesOrLogSuccess()
    {
        _client.CreateTableIfNotExistsAsync("Secrets", Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(503, "storage unavailable"));
        var sut = CreateInitializer("Secrets", "Audit");

        await Assert.ThrowsAsync<RequestFailedException>(() => sut.StartAsync(TestContext.Current.CancellationToken));

        await _client.DidNotReceive().CreateTableIfNotExistsAsync("Audit", Arg.Any<CancellationToken>());
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task StartAsync_TokenAlreadyCancelled_PropagatesOperationCancelled()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        _client.CreateTableIfNotExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromCanceled<Response<TableItem>>(call.Arg<CancellationToken>()));
        var sut = CreateInitializer("Secrets");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.StartAsync(source.Token));
    }

    [Fact]
    public async Task StartAsync_TableSetWithTwoNames_LogsOneInformationWithCountOnly()
    {
        var sut = CreateInitializer("SecretsTable", "AuditTable");

        await sut.StartAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Table Storage initialised with 2 tables", entry.Message);
        Assert.DoesNotContain("SecretsTable", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopAsync_Always_MakesNoStorageCalls()
    {
        var sut = CreateInitializer("Secrets");

        await sut.StopAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_client.ReceivedCalls());
        Assert.Empty(_logger.Entries);
    }

    private TableStorageInitializer CreateInitializer(params string[] names) =>
        new(_client, new TableStorageTableSet(names), _logger);
}
