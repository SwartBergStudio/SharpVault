// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Models;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Infrastructure.Vaults;

[Collection(MapsterGlobalSettings.Name)]
public sealed class TableVaultStorePagingTests
{
    private readonly FakePagedVaultTableClient _table = new();
    private readonly TableVaultStore _store;
    private readonly Guid _owner = Guid.NewGuid();

    public TableVaultStorePagingTests()
    {
        var service = Substitute.For<TableServiceClient>();
        service.GetTableClient(TableNames.Vaults).Returns(_table);
        _store = new TableVaultStore(service, NullLogger<TableVaultStore>.Instance);
        TypeAdapterRegistration.EnsureInfrastructureMappings();
        TypeAdapterConfig.GlobalSettings.Scan(typeof(VaultKeys).Assembly);
    }

    private SecretTableEntity Row(int sequence)
    {
        var secret = VaultSamples.Secret(_owner, Guid.CreateVersion7(VaultSamples.Created.AddMilliseconds(sequence * 3)));
        return VaultStorageMappingRegister.ToEntity(secret);
    }

    [Fact]
    public async Task ListSecretsAsync_EmptyServerPagesThenItems_KeepsReadingUntilItFindsAnItem()
    {
        var rows = new[] { Row(0), Row(1) };
        _table.PageAt = index => index < 3
            ? FakePagedVaultTableClient.EmptyPageWithMore(index)
            : FakePagedVaultTableClient.PageOf(rows, "more");

        var page = await _store.ListSecretsAsync(_owner, SecretFolderFilter.InFolder(Guid.NewGuid()), 10, null, TestContext.Current.CancellationToken);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(ContinuationTokens.ForSecret(page.Items[^1].Value.Id), page.ContinuationToken);
        Assert.Equal(4, _table.PagesRead);
    }

    [Fact]
    public async Task ListSecretsAsync_LastServerPageWithoutContinuation_ReportsEndOfList()
    {
        _table.PageAt = _ => FakePagedVaultTableClient.PageOf([Row(0)], null);

        var page = await _store.ListSecretsAsync(_owner, SecretFolderFilter.All, 10, null, TestContext.Current.CancellationToken);

        Assert.Single(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListSecretsAsync_EmptyServerPageWithoutContinuation_ReportsEmptyEndOfList()
    {
        _table.PageAt = _ => FakePagedVaultTableClient.PageOf([], null);

        var page = await _store.ListSecretsAsync(_owner, SecretFolderFilter.All, 10, null, TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListSecretsAsync_EmptyServerPagesForever_StopsAfterTwentyStorageRequests()
    {
        _table.PageAt = FakePagedVaultTableClient.EmptyPageWithMore;

        var page = await _store.ListSecretsAsync(_owner, SecretFolderFilter.InFolder(Guid.NewGuid()), 10, null, TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.Equal(VaultStorageLimits.MaxStorageRequestsPerListCall, _table.PagesRead);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListSecretsAsync_TwentyEmptyServerPagesWithMoreData_DoesNotReportEndOfList()
    {
        _table.PageAt = FakePagedVaultTableClient.EmptyPageWithMore;

        var page = await _store.ListSecretsAsync(_owner, SecretFolderFilter.InFolder(Guid.NewGuid()), 10, null, TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.NotNull(page.ContinuationToken);
        Assert.True(ContinuationTokens.TryGetStorageToken(page.ContinuationToken, out var storageToken));
        Assert.Equal("next-" + (VaultStorageLimits.MaxStorageRequestsPerListCall - 1), storageToken);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("t")]
    [InlineData("t!!!")]
    [InlineData("0123456789ABCDEF0123456789abcdef")]
    [InlineData("01234567-89ab-cdef-0123-456789abcdef")]
    public async Task ListSecretsAsync_MalformedToken_ThrowsWithoutQueryingStorage(string token)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.ListSecretsAsync(_owner, SecretFolderFilter.All, 10, token, TestContext.Current.CancellationToken));

        Assert.Empty(_table.Filters);
    }

    [Fact]
    public async Task ListSecretsAsync_NonPositivePageSize_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _store.ListSecretsAsync(_owner, SecretFolderFilter.All, 0, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListSecretsAsync_FolderScopeWithoutFolderId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.ListSecretsAsync(_owner, new SecretFolderFilter(SecretFolderScope.Folder, null), 10, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListSecretsAsync_Filters_AlwaysStartWithThePartitionKeyAndNeverEmbedFolderIdsAsLiterals()
    {
        _table.PageAt = _ => FakePagedVaultTableClient.PageOf([], null);
        var folder = Guid.NewGuid();
        var after = Guid.NewGuid();

        await _store.ListSecretsAsync(_owner, SecretFolderFilter.InFolder(folder), 10, ContinuationTokens.ForSecret(after), TestContext.Current.CancellationToken);
        await _store.ListSecretsAsync(_owner, SecretFolderFilter.Unfiled, 10, null, TestContext.Current.CancellationToken);

        Assert.All(_table.Filters, filter => Assert.StartsWith($"PartitionKey eq '{_owner:D}' and RowKey", filter, StringComparison.Ordinal));
        Assert.Contains($"RowKey gt 's_{after:N}'", _table.Filters[0], StringComparison.Ordinal);
        Assert.Contains($"FolderKey eq '{folder:N}'", _table.Filters[0], StringComparison.Ordinal);
        Assert.Contains("RowKey ge 's_'", _table.Filters[1], StringComparison.Ordinal);
        Assert.Contains("FolderKey eq 'root'", _table.Filters[1], StringComparison.Ordinal);
        Assert.Contains("RowKey lt 's`'", _table.Filters[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListSecretsAsync_StorageRejectsAClientSuppliedToken_ThrowsInvalidContinuationToken()
    {
        _table.PageAt = _ => throw new RequestFailedException(400, "OutOfRangeInput");

        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() => _store.ListSecretsAsync(
            _owner, SecretFolderFilter.All, 10, ContinuationTokens.ForStorage("forged-but-well-formed"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListSecretsAsync_Storage400WithoutAClientToken_Propagates()
    {
        _table.PageAt = _ => throw new RequestFailedException(400, "OutOfRangeInput");

        await Assert.ThrowsAsync<RequestFailedException>(() => _store.ListSecretsAsync(
            _owner, SecretFolderFilter.All, 10, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListSecretsAsync_Storage500WithAClientToken_Propagates()
    {
        _table.PageAt = _ => throw new RequestFailedException(500, "InternalError");

        await Assert.ThrowsAsync<RequestFailedException>(() => _store.ListSecretsAsync(
            _owner, SecretFolderFilter.All, 10, ContinuationTokens.ForStorage("forged-but-well-formed"), TestContext.Current.CancellationToken));
    }
}
