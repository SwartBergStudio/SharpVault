// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Vaults;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Infrastructure.Vaults;

[Collection(MapsterGlobalSettings.Name)]
public sealed class TableVaultStoreBinPagingTests
{
    private readonly FakePagedVaultTableClient _table = new();
    private readonly TableVaultStore _store;
    private readonly Guid _owner = Guid.NewGuid();

    public TableVaultStoreBinPagingTests()
    {
        var service = Substitute.For<TableServiceClient>();
        service.GetTableClient(TableNames.Vaults).Returns(_table);
        _store = new TableVaultStore(service, NullLogger<TableVaultStore>.Instance);
        TypeAdapterRegistration.EnsureInfrastructureMappings();
        TypeAdapterConfig.GlobalSettings.Scan(typeof(VaultKeys).Assembly);
    }

    private TableEntity SecretRow(int sequence)
    {
        var secret = VaultSamples.Secret(_owner, Guid.CreateVersion7(VaultSamples.Created.AddMilliseconds(sequence * 3)));
        secret.DeletedAt = VaultSamples.Created.AddDays(1);
        secret.DeletionBatchId = Guid.NewGuid();
        return ToTableEntity(VaultStorageMappingRegister.ToEntity(secret));
    }

    private TableEntity FolderRow(int sequence)
    {
        var folder = VaultSamples.RecycledFolder(_owner, Guid.CreateVersion7(VaultSamples.Created.AddMilliseconds(sequence * 3)));
        return ToTableEntity(VaultStorageMappingRegister.ToEntity(folder));
    }

    private static TableEntity ToTableEntity(ITableEntity source)
    {
        var entity = new TableEntity(source.PartitionKey, source.RowKey);
        var skipped = new[] { nameof(ITableEntity.PartitionKey), nameof(ITableEntity.RowKey), nameof(ITableEntity.Timestamp), nameof(ITableEntity.ETag), "BinaryLength" };
        foreach (var property in source.GetType().GetProperties().Where(property => !skipped.Contains(property.Name)))
        {
            if (property.GetValue(source) is { } value)
            {
                entity[property.Name] = value;
            }
        }

        return entity;
    }

    [Fact]
    public async Task ListRecycleBinAsync_EmptyServerPagesThenItems_KeepsReadingUntilItFindsAnItem()
    {
        var rows = new[] { SecretRow(0), FolderRow(1) };
        _table.TablePageAt = index => index < 3
            ? FakePagedVaultTableClient.EmptyTablePageWithMore(index)
            : FakePagedVaultTableClient.TablePageOf(rows, "more");

        var page = await _store.ListRecycleBinAsync(_owner, 10, null, TestContext.Current.CancellationToken);

        Assert.Equal(2, page.Items.Count);
        Assert.NotNull(page.Items[0].Secret);
        Assert.NotNull(page.Items[1].Folder);
        Assert.Equal(4, _table.PagesRead);
        Assert.Equal(rows[1].RowKey[1..], page.ContinuationToken);
    }

    [Fact]
    public async Task ListRecycleBinAsync_LastServerPageWithoutContinuation_ReportsEndOfList()
    {
        _table.TablePageAt = _ => FakePagedVaultTableClient.TablePageOf([SecretRow(0)], null);

        var page = await _store.ListRecycleBinAsync(_owner, 10, null, TestContext.Current.CancellationToken);

        Assert.Single(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListRecycleBinAsync_EmptyServerPageWithoutContinuation_ReportsEmptyEndOfList()
    {
        _table.TablePageAt = _ => FakePagedVaultTableClient.TablePageOf([], null);

        var page = await _store.ListRecycleBinAsync(_owner, 10, null, TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListRecycleBinAsync_EmptyServerPagesForever_StopsAfterTwentyStorageRequests()
    {
        _table.TablePageAt = FakePagedVaultTableClient.EmptyTablePageWithMore;

        var page = await _store.ListRecycleBinAsync(_owner, 10, null, TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.Equal(VaultStorageLimits.MaxStorageRequestsPerListCall, _table.PagesRead);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListRecycleBinAsync_TwentyEmptyServerPagesWithMoreData_DoesNotReportEndOfList()
    {
        _table.TablePageAt = FakePagedVaultTableClient.EmptyTablePageWithMore;

        var page = await _store.ListRecycleBinAsync(_owner, 10, null, TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.NotNull(page.ContinuationToken);
        Assert.True(ContinuationTokens.TryGetStorageToken(page.ContinuationToken, out var storageToken));
        Assert.Equal("next-" + (VaultStorageLimits.MaxStorageRequestsPerListCall - 1), storageToken);
        Assert.True(ContinuationTokens.IsRecycleBinToken(page.ContinuationToken));
    }

    [Fact]
    public async Task ListRecycleBinAsync_NineteenEmptyPagesThenAnItem_ReturnsTheItem()
    {
        var row = SecretRow(0);
        _table.TablePageAt = index => index < VaultStorageLimits.MaxStorageRequestsPerListCall - 1
            ? FakePagedVaultTableClient.EmptyTablePageWithMore(index)
            : FakePagedVaultTableClient.TablePageOf([row], null);

        var page = await _store.ListRecycleBinAsync(_owner, 10, null, TestContext.Current.CancellationToken);

        Assert.Single(page.Items);
        Assert.Null(page.ContinuationToken);
        Assert.Equal(VaultStorageLimits.MaxStorageRequestsPerListCall, _table.PagesRead);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("t")]
    [InlineData("t!!!")]
    [InlineData("x_0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("s_0123456789ABCDEF0123456789abcdef")]
    public async Task ListRecycleBinAsync_MalformedToken_ThrowsWithoutQueryingStorage(string token)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.ListRecycleBinAsync(_owner, 10, token, TestContext.Current.CancellationToken));

        Assert.Empty(_table.Filters);
    }

    [Fact]
    public async Task ListRecycleBinAsync_NonPositivePageSize_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _store.ListRecycleBinAsync(_owner, 0, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListRecycleBinAsync_Filters_AlwaysStartWithThePartitionKeyAndEmbedNoTokenText()
    {
        _table.TablePageAt = _ => FakePagedVaultTableClient.TablePageOf([], null);
        var key = "s_" + Guid.NewGuid().ToString("N");

        await _store.ListRecycleBinAsync(_owner, 10, null, TestContext.Current.CancellationToken);
        await _store.ListRecycleBinAsync(_owner, 10, key, TestContext.Current.CancellationToken);
        await _store.ListRecycleBinAsync(_owner, 10, ContinuationTokens.ForStorage("' or PartitionKey ne '"), TestContext.Current.CancellationToken);

        Assert.All(_table.Filters, filter => Assert.StartsWith($"PartitionKey eq '{_owner:D}' and RowKey", filter, StringComparison.Ordinal));
        Assert.Contains($"RowKey gt 'b{key}'", _table.Filters[1], StringComparison.Ordinal);
        Assert.All(_table.Filters, filter => Assert.DoesNotContain("PartitionKey ne", filter, StringComparison.Ordinal));
        Assert.All(_table.PageSizes, size => Assert.Equal(10, size));
    }

    [Fact]
    public async Task ListRecycleBinAsync_StorageRejectsAClientSuppliedToken_ThrowsInvalidContinuationToken()
    {
        _table.TablePageAt = _ => throw new RequestFailedException(400, "OutOfRangeInput");

        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() => _store.ListRecycleBinAsync(
            _owner, 10, ContinuationTokens.ForStorage("forged-but-well-formed"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListRecycleBinAsync_Storage400WithoutAClientToken_Propagates()
    {
        _table.TablePageAt = _ => throw new RequestFailedException(400, "OutOfRangeInput");

        await Assert.ThrowsAsync<RequestFailedException>(() => _store.ListRecycleBinAsync(
            _owner, 10, null, TestContext.Current.CancellationToken));
    }
}
