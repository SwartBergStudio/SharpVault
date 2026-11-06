// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.Infrastructure.Storage;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Storage;

[Trait("Category", "Security")]
public sealed class TableStorageInitializerListTests
{
    private const int ServiceUnavailable = 503;

    private readonly TableServiceClient _client = Substitute.For<TableServiceClient>();
    private readonly CollectingLogger<TableStorageInitializer> _logger = new();

    public TableStorageInitializerListTests()
    {
        TablesExist();
    }

    [Fact]
    public async Task StartAsync_ListFails_Throws()
    {
        _client.QueryAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(ServiceUnavailable, "storage unavailable"));
        var sut = CreateInitializer("Secrets", "Audit");

        var exception = await Assert.ThrowsAsync<RequestFailedException>(() => sut.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ServiceUnavailable, exception.Status);
        await _client.DidNotReceive().CreateTableIfNotExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_SomeTablesMissing_CreatesOnlyThose()
    {
        TablesExist("Secrets");
        var sut = CreateInitializer("Secrets", "Audit");

        await sut.StartAsync(TestContext.Current.CancellationToken);

        await _client.Received(1).CreateTableIfNotExistsAsync("Audit", Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateTableIfNotExistsAsync("Secrets", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_NameDiffersByCase_CreatesNothing()
    {
        TablesExist("secrets");
        var sut = CreateInitializer("Secrets");

        await sut.StartAsync(TestContext.Current.CancellationToken);

        await _client.DidNotReceive().CreateTableIfNotExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private void TablesExist(params string[] names) =>
        _client.QueryAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(
            AsyncPageable<TableItem>.FromPages([Page<TableItem>.FromValues(names.Select(name => new TableItem(name)).ToList(), null, Substitute.For<Response>())]));

    private TableStorageInitializer CreateInitializer(params string[] names) =>
        new(_client, new TableStorageTableSet(names), _logger);
}
