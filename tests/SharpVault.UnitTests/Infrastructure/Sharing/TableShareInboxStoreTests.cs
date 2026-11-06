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
using SharpVault.Infrastructure.Sharing;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Infrastructure.Sharing;

[Collection(MapsterGlobalSettings.Name)]
public sealed class TableShareInboxStoreTests
{
    private const int PreconditionFailed = 412;
    private const int NotFound = 404;
    private const int BadRequest = 400;

    private readonly FakeShareTableClient _table = new();
    private readonly CollectingLogger<TableShareInboxStore> _logger = new();
    private readonly TableShareInboxStore _store;
    private readonly Guid _recipient = Guid.NewGuid();

    public TableShareInboxStoreTests()
    {
        var service = Substitute.For<TableServiceClient>();
        service.GetTableClient(TableNames.ShareInbox).Returns(_table);
        _store = new TableShareInboxStore(service, _logger);
        TypeAdapterRegistration.EnsureInfrastructureMappings();
        TypeAdapterConfig.GlobalSettings.Scan(typeof(VaultKeys).Assembly);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ShareInboxTableEntity Row() => ShareSamples.InboxRow(ShareSamples.InboxEntry(_recipient));

    private Task<ShareInboxPage> ListAsync(Guid? folder = null, int pageSize = 10, string? token = null) =>
        _store.ListAsync(_recipient, folder, pageSize, token, Token);

    [Fact]
    public async Task ListAsync_EmptyFirstPageWithContinuation_ContinuesUpToTheLimit()
    {
        var rows = new[] { Row(), Row() };
        _table.InboxPageAt = index => index < 3
            ? FakeShareTableClient.EmptyInboxPageWithMore(index)
            : FakeShareTableClient.InboxPageOf(rows, "more");

        var page = await ListAsync(Guid.NewGuid());

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(ContinuationTokens.ForInbox(rows[^1].RowKey), page.ContinuationToken);
        Assert.Equal(4, _table.PagesRead);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListAsync_TwentyEmptyPages_ReturnsWrappedToken()
    {
        _table.InboxPageAt = FakeShareTableClient.EmptyInboxPageWithMore;

        var page = await ListAsync(Guid.NewGuid());

        Assert.Empty(page.Items);
        Assert.Equal(VaultStorageLimits.MaxStorageRequestsPerListCall, _table.PagesRead);
        Assert.NotNull(page.ContinuationToken);
        Assert.True(ContinuationTokens.TryGetStorageToken(page.ContinuationToken, out var storageToken));
        Assert.Equal(FakeShareTableClient.TokenAfter(VaultStorageLimits.MaxStorageRequestsPerListCall - 1), storageToken);
    }

    [Fact]
    public async Task ListAsync_WrappedTokenFromTheLimit_ResumesTheStorageQueryAndFindsLaterRows()
    {
        var row = Row();
        _table.InboxPageAt = index => index < 25
            ? FakeShareTableClient.EmptyInboxPageWithMore(index)
            : FakeShareTableClient.InboxPageOf([row], null);
        var first = await ListAsync(Guid.NewGuid());

        var second = await ListAsync(Guid.NewGuid(), token: first.ContinuationToken);

        Assert.Empty(first.Items);
        Assert.Single(second.Items);
        Assert.Null(second.ContinuationToken);
        Assert.Equal(FakeShareTableClient.TokenAfter(VaultStorageLimits.MaxStorageRequestsPerListCall - 1), _table.ResumeTokens[^1]);
    }

    [Fact]
    public async Task ListAsync_EmptyPageWithoutContinuation_ReportsEmptyEndOfList()
    {
        _table.InboxPageAt = _ => FakeShareTableClient.InboxPageOf([], null);

        var page = await ListAsync();

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListAsync_PageOfOnlyDamagedRowsWithMore_ReturnsEmptyItemsAndAKeyTokenFromTheLastRawRowAfterOneRequest()
    {
        var damaged = new[] { ShareSamples.DamagedInboxRow(_recipient), ShareSamples.DamagedInboxRow(_recipient) };
        _table.InboxPageAt = _ => FakeShareTableClient.InboxPageOf(damaged, "more");

        var page = await ListAsync();

        Assert.Empty(page.Items);
        Assert.Equal(1, _table.PagesRead);
        Assert.Equal(ContinuationTokens.ForInbox(damaged[^1].RowKey), page.ContinuationToken);
        Assert.True(ContinuationTokens.TryGetInboxKey(page.ContinuationToken!, out _));
    }

    [Fact]
    public async Task ListAsync_PageOfOnlyDamagedRowsWithMore_NextCallResumesAfterTheLastRawRowWithoutLooping()
    {
        var damaged = ShareSamples.DamagedInboxRow(_recipient);
        var good = Row();
        _table.InboxPageAt = _ => FakeShareTableClient.InboxPageOf([damaged], "more");
        var first = await ListAsync();
        _table.InboxPageAt = _ => FakeShareTableClient.InboxPageOf([good], null);

        var second = await ListAsync(token: first.ContinuationToken);

        Assert.Single(second.Items);
        Assert.Null(second.ContinuationToken);
        Assert.Contains($"RowKey gt '{damaged.RowKey}'", _table.Filters[^1], StringComparison.Ordinal);
        Assert.Null(_table.ResumeTokens[^1]);
    }

    [Fact]
    public async Task ListAsync_LastPageOfOnlyDamagedRows_EndsTheListing()
    {
        _table.InboxPageAt = _ => FakeShareTableClient.InboxPageOf([ShareSamples.DamagedInboxRow(_recipient)], null);

        var page = await ListAsync();

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ListAsync_DamagedRowAmongGoodOnes_SkipsItAndLogsOnlyTheCount()
    {
        var damaged = ShareSamples.DamagedInboxRow(_recipient);
        _table.InboxPageAt = _ => FakeShareTableClient.InboxPageOf([Row(), damaged, Row()], null);

        var page = await ListAsync();

        Assert.Equal(2, page.Items.Count);
        var warning = Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("1", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(damaged.RowKey, warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_recipient.ToString("D"), warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("t")]
    [InlineData("t!!!")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("s_0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef_s_0123456789abcdef0123456789abcdef\n")]
    [InlineData("' or PartitionKey ne '")]
    [Trait("Category", "Security")]
    public async Task ListAsync_MalformedToken_ThrowsWithoutQueryingStorage(string token)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ListAsync(token: token));

        Assert.Empty(_table.Filters);
    }

    [Fact]
    public async Task ListAsync_NonPositivePageSize_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ListAsync(pageSize: 0));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListAsync_TokenNamingAnotherOwnersRow_StaysInTheCallersPartition()
    {
        var token = ContinuationTokens.ForInbox(ShareInboxKeys.RowKey(Guid.NewGuid(), ShareItemKind.Secret, Guid.NewGuid()));

        await ListAsync(Guid.NewGuid(), token: token);

        Assert.StartsWith($"PartitionKey eq '{_recipient:D}' and RowKey gt", _table.Filters.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_StorageRejectsAStorageToken_ThrowsInvalidContinuationToken()
    {
        _table.InboxPageAt = _ => throw new RequestFailedException(BadRequest, "bad token");
        var token = ContinuationTokens.ForStorage("next-3");

        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() => ListAsync(token: token));
    }

    [Fact]
    public async Task ListAsync_StorageRejectsAFirstPageQuery_PropagatesTheStorageError()
    {
        _table.InboxPageAt = _ => throw new RequestFailedException(BadRequest, "bad filter");

        await Assert.ThrowsAsync<RequestFailedException>(() => ListAsync());
    }

    [Fact]
    public async Task ListAsync_Filters_AlwaysStartWithThePartitionKeyAndNeverEmbedFolderIds()
    {
        _table.InboxPageAt = _ => FakeShareTableClient.InboxPageOf([], null);
        var folder = Guid.NewGuid();
        var after = ContinuationTokens.ForInbox(ShareInboxKeys.RowKey(Guid.NewGuid(), ShareItemKind.Folder, Guid.NewGuid()));

        await ListAsync();
        await ListAsync(folder);
        await ListAsync(token: after);
        await ListAsync(folder, token: after);

        Assert.Equal(4, _table.Filters.Count);
        Assert.All(_table.Filters, filter => Assert.StartsWith($"PartitionKey eq '{_recipient:D}'", filter, StringComparison.Ordinal));
        Assert.Contains("FiledFolderKey eq", _table.Filters[1], StringComparison.Ordinal);
        Assert.Contains("FiledFolderKey eq", _table.Filters[3], StringComparison.Ordinal);
        Assert.DoesNotContain("FiledFolderKey", _table.Filters[0], StringComparison.Ordinal);
        Assert.DoesNotContain("FiledFolderKey", _table.Filters[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_PageSize_IsPassedToStorage()
    {
        _table.InboxPageAt = _ => FakeShareTableClient.InboxPageOf([], null);

        await ListAsync(pageSize: 37);

        Assert.Equal(37, _table.PageSizes.Single());
    }

    [Fact]
    public async Task RemoveAsync_FiledConcurrently_RetriesOnce()
    {
        _table.CurrentInboxRow = Row;
        _table.DeleteOutcome = attempt => attempt == 1 ? new RequestFailedException(PreconditionFailed, "changed") : null;

        var removed = await _store.RemoveAsync(_recipient, Guid.NewGuid(), ShareItemKind.Secret, Guid.NewGuid(), Token);

        Assert.True(removed);
        Assert.Equal(2, _table.Gets);
        Assert.Equal(2, _table.Deletes);
    }

    [Fact]
    public async Task RemoveAsync_FiledConcurrentlyTwice_PropagatesAfterTheOneRetry()
    {
        _table.CurrentInboxRow = Row;
        _table.DeleteOutcome = _ => new RequestFailedException(PreconditionFailed, "changed");

        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => _store.RemoveAsync(_recipient, Guid.NewGuid(), ShareItemKind.Secret, Guid.NewGuid(), Token));

        Assert.Equal(PreconditionFailed, exception.Status);
        Assert.Equal(2, _table.Gets);
        Assert.Equal(2, _table.Deletes);
    }

    [Fact]
    public async Task RemoveAsync_RowGoneBetweenReadAndDelete_ReturnsFalse()
    {
        _table.CurrentInboxRow = Row;
        _table.DeleteOutcome = _ => new RequestFailedException(NotFound, "gone");

        var removed = await _store.RemoveAsync(_recipient, Guid.NewGuid(), ShareItemKind.Secret, Guid.NewGuid(), Token);

        Assert.False(removed);
        Assert.Equal(1, _table.Deletes);
    }

    [Fact]
    public async Task RemoveAsync_Missing_ReturnsFalseWithoutDeleting()
    {
        var removed = await _store.RemoveAsync(_recipient, Guid.NewGuid(), ShareItemKind.Folder, Guid.NewGuid(), Token);

        Assert.False(removed);
        Assert.Equal(0, _table.Deletes);
    }

    [Fact]
    public async Task GetAsync_DamagedRow_ReturnsNull()
    {
        _table.CurrentInboxRow = () => ShareSamples.DamagedInboxRow(_recipient);

        var found = await _store.GetAsync(_recipient, Guid.NewGuid(), ShareItemKind.Secret, Guid.NewGuid(), Token);

        Assert.Null(found);
    }

    [Fact]
    public async Task SetFiledFolderAsync_BlankETagOrNullEntry_Throws()
    {
        var entry = ShareSamples.InboxEntry(_recipient);

        await Assert.ThrowsAsync<ArgumentException>(() => _store.SetFiledFolderAsync(entry, " ", Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.SetFiledFolderAsync(null!, "etag", Token));
    }
}
