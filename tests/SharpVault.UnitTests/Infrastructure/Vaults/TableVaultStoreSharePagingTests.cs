// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using Mapster;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Infrastructure.Vaults;

[Collection(MapsterGlobalSettings.Name)]
public sealed class TableVaultStoreSharePagingTests
{
    private const int BadRequest = 400;

    private readonly FakeShareTableClient _table = new();
    private readonly CollectingLogger<TableVaultStore> _logger = new();
    private readonly TableVaultStore _store;
    private readonly Guid _owner = Guid.NewGuid();

    public TableVaultStoreSharePagingTests()
    {
        var service = Substitute.For<TableServiceClient>();
        service.GetTableClient(TableNames.Vaults).Returns(_table);
        _store = new TableVaultStore(service, _logger);
        TypeAdapterRegistration.EnsureInfrastructureMappings();
        TypeAdapterConfig.GlobalSettings.Scan(typeof(VaultKeys).Assembly);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ShareGrantTableEntity Row() => ShareSamples.GrantRow(ShareSamples.Grant(_owner));

    private ShareGrantTableEntity Damaged() => ShareSamples.DamagedGrantRow(_owner);

    private Task<ShareGrantPage> ListAsync(int pageSize = 10, string? token = null) =>
        _store.ListShareGrantsByOwnerAsync(_owner, pageSize, token, Token);

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_PageOfOnlyDamagedRowsWithMore_ReturnsEmptyItemsAndAStorageTokenAfterOneRequest()
    {
        _table.GrantPageAt = index => FakeShareTableClient.GrantPageOf([Damaged(), Damaged()], FakeShareTableClient.TokenAfter(index));

        var page = await ListAsync();

        Assert.Empty(page.Items);
        Assert.Equal(1, _table.PagesRead);
        Assert.NotNull(page.ContinuationToken);
        Assert.True(ContinuationTokens.TryGetStorageToken(page.ContinuationToken, out var storageToken));
        Assert.Equal(FakeShareTableClient.TokenAfter(0), storageToken);
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_PageOfOnlyDamagedRowsWithMore_NextCallResumesFromTheStorageTokenWithoutLooping()
    {
        var good = Row();
        _table.GrantPageAt = index => index == 0
            ? FakeShareTableClient.GrantPageOf([Damaged()], FakeShareTableClient.TokenAfter(0))
            : FakeShareTableClient.GrantPageOf([good], null);
        var first = await ListAsync();

        var second = await ListAsync(token: first.ContinuationToken);

        Assert.Empty(first.Items);
        Assert.Single(second.Items);
        Assert.Null(second.ContinuationToken);
        Assert.NotEqual(first.ContinuationToken, second.ContinuationToken);
        Assert.Equal(FakeShareTableClient.TokenAfter(0), _table.ResumeTokens[^1]);
        Assert.Equal(2, _table.PagesRead);
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_WalkingEveryPageOfDamagedAndGoodRows_VisitsEachPageOnceAndEnds()
    {
        var good = new[] { Row(), Row() };
        _table.GrantPageAt = index => index switch
        {
            0 => FakeShareTableClient.GrantPageOf([Damaged()], FakeShareTableClient.TokenAfter(0)),
            1 => FakeShareTableClient.GrantPageOf([good[0]], FakeShareTableClient.TokenAfter(1)),
            2 => FakeShareTableClient.GrantPageOf([Damaged(), Damaged()], FakeShareTableClient.TokenAfter(2)),
            3 => FakeShareTableClient.GrantPageOf([good[1]], FakeShareTableClient.TokenAfter(3)),
            _ => FakeShareTableClient.GrantPageOf([Damaged()], null),
        };
        var seen = new List<ShareGrant>();
        var tokens = new List<string>();
        string? token = null;

        for (var calls = 0; calls < 10; calls++)
        {
            var page = await ListAsync(token: token);
            seen.AddRange(page.Items.Select(item => item.Value));
            token = page.ContinuationToken;
            if (token is null)
            {
                break;
            }

            tokens.Add(token);
        }

        Assert.Null(token);
        Assert.Equal(good.Select(row => row.RowKey).Order(), seen.Select(grant => VaultKeys.GrantByItemRowKey(grant.ItemKind, grant.ItemId, grant.RecipientId)).Order());
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
        Assert.Equal(5, _table.PagesRead);
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_LastPageOfOnlyDamagedRows_EndsTheListing()
    {
        _table.GrantPageAt = _ => FakeShareTableClient.GrantPageOf([Damaged()], null);

        var page = await ListAsync();

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_EmptyPagesThenGrants_KeepsReadingUntilItFindsOne()
    {
        var rows = new[] { Row(), Row() };
        _table.GrantPageAt = index => index < 3
            ? FakeShareTableClient.EmptyGrantPageWithMore(index)
            : FakeShareTableClient.GrantPageOf(rows, "more");

        var page = await ListAsync();

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(4, _table.PagesRead);
        Assert.NotNull(page.ContinuationToken);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListShareGrantsByOwnerAsync_TwentyEmptyPagesWithMoreData_DoesNotReportEndOfList()
    {
        _table.GrantPageAt = FakeShareTableClient.EmptyGrantPageWithMore;

        var page = await ListAsync();

        Assert.Empty(page.Items);
        Assert.Equal(VaultStorageLimits.MaxStorageRequestsPerListCall, _table.PagesRead);
        Assert.True(ContinuationTokens.TryGetStorageToken(page.ContinuationToken!, out var storageToken));
        Assert.Equal(FakeShareTableClient.TokenAfter(VaultStorageLimits.MaxStorageRequestsPerListCall - 1), storageToken);
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_EmptyPageWithoutContinuation_ReportsEmptyEndOfList()
    {
        _table.GrantPageAt = _ => FakeShareTableClient.GrantPageOf([], null);

        var page = await ListAsync();

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_DamagedRowAmongGoodOnes_SkipsItAndLogsOnlyTheCount()
    {
        var damaged = Damaged();
        _table.GrantPageAt = _ => FakeShareTableClient.GrantPageOf([Row(), damaged, Row()], null);

        var page = await ListAsync();

        Assert.Equal(2, page.Items.Count);
        var warning = Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.DoesNotContain(damaged.RowKey, warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_owner.ToString("D"), warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("t")]
    [InlineData("t!!!")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("s_0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef_s_0123456789abcdef0123456789abcdef")]
    [Trait("Category", "Security")]
    public async Task ListShareGrantsByOwnerAsync_MalformedToken_ThrowsWithoutQueryingStorage(string token)
    {
        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() => ListAsync(token: token));

        Assert.Empty(_table.Filters);
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_NonPositivePageSize_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ListAsync(pageSize: 0));
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_StorageRejectsAStorageToken_ThrowsInvalidContinuationToken()
    {
        _table.GrantPageAt = _ => throw new RequestFailedException(BadRequest, "bad token");

        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() => ListAsync(token: ContinuationTokens.ForStorage("next-3")));
    }

    [Fact]
    public async Task ListShareGrantsByOwnerAsync_StorageRejectsAFirstPageQuery_PropagatesTheStorageError()
    {
        _table.GrantPageAt = _ => throw new RequestFailedException(BadRequest, "bad filter");

        await Assert.ThrowsAsync<RequestFailedException>(() => ListAsync());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListShareGrantsByOwnerAsync_Filter_IsBoundToTheOwnerAndTheByItemRange()
    {
        _table.GrantPageAt = _ => FakeShareTableClient.GrantPageOf([], null);

        await ListAsync(pageSize: 25);

        var filter = _table.Filters.Single();
        Assert.StartsWith($"PartitionKey eq '{_owner:D}' and RowKey ge 'gi_' and RowKey lt", filter, StringComparison.Ordinal);
        Assert.Equal(25, _table.PageSizes.Single());
    }

    [Fact]
    public async Task ListShareGrantsForItemAsync_NonPositiveLimit_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _store.ListShareGrantsForItemAsync(_owner, ShareItemKind.Secret, Guid.NewGuid(), 0, Token));
    }

    [Fact]
    public async Task ListShareGrantsForItemAsync_MoreRowsThanTheLimit_ReturnsExactlyTheLimitAndStopsReading()
    {
        _table.GrantPageAt = index => FakeShareTableClient.GrantPageOf([Row(), Row(), Row()], FakeShareTableClient.TokenAfter(index));

        var grants = await _store.ListShareGrantsForItemAsync(_owner, ShareItemKind.Secret, Guid.NewGuid(), 4, Token);

        Assert.Equal(4, grants.Count);
        Assert.Equal(2, _table.PagesRead);
        Assert.Equal(4, _table.PageSizes.Single());
    }

    [Fact]
    public async Task ListShareGrantsForItemAsync_DamagedRowsOnAPage_AreSkippedAndReadingContinues()
    {
        _table.GrantPageAt = index => index == 0
            ? FakeShareTableClient.GrantPageOf([Damaged(), Damaged()], FakeShareTableClient.TokenAfter(0))
            : FakeShareTableClient.GrantPageOf([Row()], null);

        var grants = await _store.ListShareGrantsForItemAsync(_owner, ShareItemKind.Secret, Guid.NewGuid(), 5, Token);

        Assert.Single(grants);
        Assert.Equal(2, _table.PagesRead);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task GrantRangeFilters_AreAlwaysBoundToTheOwnerPartition()
    {
        _table.GrantPageAt = _ => FakeShareTableClient.GrantPageOf([], null);
        var recipient = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await _store.ListShareGrantsForItemAsync(_owner, ShareItemKind.Folder, itemId, 5, Token);
        await _store.ListFolderGrantsToRecipientAsync(_owner, recipient, Token);
        await _store.ListGrantsToRecipientBetweenAsync(_owner, recipient, new ShareItemRef(ShareItemKind.Folder, itemId), new ShareItemRef(ShareItemKind.Secret, itemId), Token);

        Assert.Equal(3, _table.Filters.Count);
        Assert.All(_table.Filters, filter => Assert.StartsWith($"PartitionKey eq '{_owner:D}' and RowKey ge", filter, StringComparison.Ordinal));
        Assert.Contains($"gi_f_{itemId:N}_", _table.Filters[0], StringComparison.Ordinal);
        Assert.Contains($"gr_{recipient:N}_f_", _table.Filters[1], StringComparison.Ordinal);
        Assert.Contains($"RowKey ge 'gr_{recipient:N}_f_{itemId:N}' and RowKey le 'gr_{recipient:N}_s_{itemId:N}'", _table.Filters[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListGrantsToRecipientBetweenAsync_NullBounds_Throw()
    {
        var bound = new ShareItemRef(ShareItemKind.Secret, Guid.NewGuid());

        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.ListGrantsToRecipientBetweenAsync(_owner, Guid.NewGuid(), null!, bound, Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.ListGrantsToRecipientBetweenAsync(_owner, Guid.NewGuid(), bound, null!, Token));
    }
}
