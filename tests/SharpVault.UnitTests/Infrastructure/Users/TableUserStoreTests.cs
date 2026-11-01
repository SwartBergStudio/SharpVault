// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Users;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Users;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Users;

[Collection(MapsterGlobalSettings.Name)]
public sealed class TableUserStoreTests
{
    private readonly TableClient _table = Substitute.For<TableClient>();
    private readonly TableUserStore _store;

    public TableUserStoreTests()
    {
        var service = Substitute.For<TableServiceClient>();
        service.GetTableClient(TableNames.Users).Returns(_table);
        _store = new TableUserStore(service);
        TypeAdapterRegistration.EnsureInfrastructureMappings();
    }

    [Fact]
    public async Task GetAsync_ExistingUser_ReadsPointAndReturnsETag()
    {
        var id = Guid.NewGuid();
        var entity = new UserTableEntity
        {
            PartitionKey = "user",
            RowKey = id.ToString("D"),
            Status = "Active",
            DisplayName = "Alice",
            ETag = new ETag("W/\"datetime'2026'\""),
        };
        var fake = new FakeUsersTableClient { Entity = entity };

        var result = await StoreOver(fake).GetAsync(id, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(id, result.Value.Id);
        Assert.Equal(UserStatus.Active, result.Value.Status);
        Assert.Equal(entity.ETag.ToString(), result.ETag);
        Assert.Equal([("user", id.ToString("D"))], fake.PointReads);
        Assert.Empty(fake.Queries);
    }

    [Fact]
    public async Task GetAsync_MissingUser_ReturnsNull()
    {
        var result = await StoreOver(new FakeUsersTableClient()).GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task GetAsync_RowWithUnknownStatus_ReturnsLockedUser()
    {
        var id = Guid.NewGuid();
        var entity = new UserTableEntity { PartitionKey = "user", RowKey = id.ToString("D"), Status = "Superuser" };
        var result = await StoreOver(new FakeUsersTableClient { Entity = entity }).GetAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(UserStatus.Locked, result!.Value.Status);
    }

    [Fact]
    public async Task AddAsync_NewUser_ReturnsWritten()
    {
        var result = await _store.AddAsync(NewUser(), TestContext.Current.CancellationToken);

        Assert.Equal(StoreWriteResult.Written, result);
        await _table.Received(1).AddEntityAsync(Arg.Is<UserTableEntity>(entity => entity.PartitionKey == "user"), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AddAsync_ExistingUser_ReturnsAlreadyExistsAndNeverUpserts()
    {
        _table.AddEntityAsync(Arg.Any<UserTableEntity>(), Arg.Any<CancellationToken>()).ThrowsAsync(new RequestFailedException(409, "exists"));

        var result = await _store.AddAsync(NewUser(), TestContext.Current.CancellationToken);

        Assert.Equal(StoreWriteResult.AlreadyExists, result);
        await _table.DidNotReceive().UpsertEntityAsync(Arg.Any<UserTableEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>());
        await _table.DidNotReceive().UpdateEntityAsync(Arg.Any<UserTableEntity>(), Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task AddAsync_OtherStorageFailure_Propagates(int status)
    {
        _table.AddEntityAsync(Arg.Any<UserTableEntity>(), Arg.Any<CancellationToken>()).ThrowsAsync(new RequestFailedException(status, "failure"));

        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => _store.AddAsync(NewUser(), TestContext.Current.CancellationToken));

        Assert.Equal(status, exception.Status);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ReplaceAsync_Success_SendsGivenETagWithReplaceMode()
    {
        var result = await _store.ReplaceAsync(NewUser(), "etag-9", TestContext.Current.CancellationToken);

        Assert.Equal(StoreWriteResult.Written, result);
        await _table.Received(1).UpdateEntityAsync(
            Arg.Any<UserTableEntity>(),
            Arg.Is<ETag>(etag => etag == new ETag("etag-9") && etag != ETag.All),
            TableUpdateMode.Replace,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ReplaceAsync_StaleETag_ReturnsConflict()
    {
        _table.UpdateEntityAsync(Arg.Any<UserTableEntity>(), Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(412, "precondition"));

        var result = await _store.ReplaceAsync(NewUser(), "stale", TestContext.Current.CancellationToken);

        Assert.Equal(StoreWriteResult.Conflict, result);
    }

    [Fact]
    public async Task ReplaceAsync_RowGone_ReturnsNotFound()
    {
        _table.UpdateEntityAsync(Arg.Any<UserTableEntity>(), Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "missing"));

        var result = await _store.ReplaceAsync(NewUser(), "etag", TestContext.Current.CancellationToken);

        Assert.Equal(StoreWriteResult.NotFound, result);
    }

    [Theory]
    [InlineData(409)]
    [InlineData(500)]
    public async Task ReplaceAsync_OtherStorageFailure_Propagates(int status)
    {
        _table.UpdateEntityAsync(Arg.Any<UserTableEntity>(), Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(status, "failure"));

        await Assert.ThrowsAsync<RequestFailedException>(
            () => _store.ReplaceAsync(NewUser(), "etag", TestContext.Current.CancellationToken));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReplaceAsync_MissingETag_ThrowsInsteadOfOverwriting(string? etag)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _store.ReplaceAsync(NewUser(), etag!, TestContext.Current.CancellationToken));

        await _table.DidNotReceive().UpdateEntityAsync(Arg.Any<UserTableEntity>(), Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListAsync_FirstPage_FiltersByPartitionKeyOnly()
    {
        var fake = new FakeUsersTableClient { Pages = () => [Page([], null)] };

        await StoreOver(fake).ListAsync(25, null, TestContext.Current.CancellationToken);

        Assert.Equal([("PartitionKey eq 'user'", (int?)25)], fake.Queries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ListAsync_WithCursor_FiltersByPartitionKeyAndRowKeyAfterCursor()
    {
        var after = Guid.NewGuid();
        var fake = new FakeUsersTableClient { Pages = () => [Page([], null)] };

        await StoreOver(fake).ListAsync(25, after, TestContext.Current.CancellationToken);

        Assert.Equal($"PartitionKey eq 'user' and RowKey gt '{after:D}'", Assert.Single(fake.Queries).Filter);
    }

    [Fact]
    public async Task ListAsync_PageWithContinuation_ReturnsLastItemAsNextCursor()
    {
        var first = UserEntity();
        var last = UserEntity();
        var fake = new FakeUsersTableClient { Pages = () => [Page([first, last], "next")] };

        var page = await StoreOver(fake).ListAsync(2, null, TestContext.Current.CancellationToken);

        Assert.Equal(2, page.Users.Count);
        Assert.Equal(Guid.ParseExact(last.RowKey, "D"), page.NextAfterUserId);
    }

    [Fact]
    public async Task ListAsync_LastPage_ReturnsNullNextCursor()
    {
        var fake = new FakeUsersTableClient { Pages = () => [Page([UserEntity()], null)] };

        var page = await StoreOver(fake).ListAsync(2, null, TestContext.Current.CancellationToken);

        Assert.Null(page.NextAfterUserId);
    }

    [Fact]
    public async Task ListAsync_EmptyPageWithContinuation_ReturnsIncomingCursor()
    {
        var after = Guid.NewGuid();
        var fake = new FakeUsersTableClient { Pages = () => [Page([], "next")] };

        var page = await StoreOver(fake).ListAsync(2, after, TestContext.Current.CancellationToken);

        Assert.Empty(page.Users);
        Assert.Equal(after, page.NextAfterUserId);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task ListAsync_MultiplePagesAvailable_ReadsOnlyTheFirstPage()
    {
        var pagesRead = 0;
        IEnumerable<Page<UserTableEntity>> Generate()
        {
            pagesRead++;
            yield return Page([UserEntity()], "next");
            pagesRead++;
            yield return Page([UserEntity()], "more");
            pagesRead++;
            yield return Page([UserEntity()], null);
        }

        var fake = new FakeUsersTableClient { Pages = Generate };

        var page = await StoreOver(fake).ListAsync(1, null, TestContext.Current.CancellationToken);

        Assert.Single(page.Users);
        Assert.Equal(1, pagesRead);
    }

    [Fact]
    public async Task ListAsync_NoPagesReturned_ReturnsEmptyPage()
    {
        var page = await StoreOver(new FakeUsersTableClient()).ListAsync(5, null, TestContext.Current.CancellationToken);

        Assert.Empty(page.Users);
        Assert.Null(page.NextAfterUserId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ListAsync_InvalidPageSize_Throws(int pageSize)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _store.ListAsync(pageSize, null, TestContext.Current.CancellationToken));
    }

    private static User NewUser() =>
        new()
        {
            Id = Guid.NewGuid(),
            DisplayName = "Alice",
            Status = UserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastSignInAt = DateTimeOffset.UtcNow,
        };

    private static UserTableEntity UserEntity() =>
        new() { PartitionKey = "user", RowKey = Guid.NewGuid().ToString("D"), Status = "Active", DisplayName = "Alice" };

    private static Page<UserTableEntity> Page(IReadOnlyList<UserTableEntity> values, string? continuation) =>
        Azure.Page<UserTableEntity>.FromValues(values, continuation, Substitute.For<Response>());

    private static TableUserStore StoreOver(TableClient table)
    {
        var service = Substitute.For<TableServiceClient>();
        service.GetTableClient(TableNames.Users).Returns(table);
        return new TableUserStore(service);
    }
}
