// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Users;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;

namespace SharpVault.IntegrationTests.ApiTests;

/// <summary>
/// C1, C2 and U3: the Users and AuditLog stores resolved from the running API.
/// </summary>
[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class UserStoreTests(ApiTestFactory factory) : ApiSecurityTestBase(factory)
{
    private const int Conflict = 409;

    public static TheoryData<string> HostileDisplayNames() =>
    [
        "O'Brien",
        "' or PartitionKey ne '",
        "\" or 1=1 --",
        "<script>alert(1)</script>",
        "name/with\\slashes#and?marks",
        "Zoë é 中文 😀",
        "",
    ];

    private IUserStore Users => Factory.Services.GetRequiredService<IUserStore>();

    private IAuditLog Audit => Factory.Services.GetRequiredService<IAuditLog>();

    [Fact]
    public async Task GetAsync_MissingUser_ReturnsNullWithOnePointRead()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        StorageRequests.Clear();

        var result = await Users.GetAsync(Guid.NewGuid(), cancellationToken);

        Assert.Null(result);
        var request = Assert.Single(StorageRequests.Requests);
        Assert.Equal("GET", request.Method);
        Assert.False(request.IsEntityQuery);
        Assert.Contains("Users(PartitionKey='user'", request.Path, StringComparison.Ordinal);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task AddAsync_NewUser_WritesTheRowAndReadsItBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = NewUser();

        var result = await Users.AddAsync(user, cancellationToken);

        Assert.Equal(StoreWriteResult.Written, result);
        var stored = await Users.GetAsync(user.Id, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(user.DisplayName, stored.Value.DisplayName);
        Assert.Equal(user.Email, stored.Value.Email);
        Assert.Equal(UserStatus.Active, stored.Value.Status);
        Assert.False(string.IsNullOrEmpty(stored.ETag));
    }

    [Theory]
    [MemberData(nameof(HostileDisplayNames))]
    public async Task AddAsync_HostileDisplayName_RoundTripsAsData(string displayName)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = NewUser();
        user.DisplayName = displayName;

        await Users.AddAsync(user, cancellationToken);
        var stored = await Users.GetAsync(user.Id, cancellationToken);

        Assert.NotNull(stored);
        Assert.Equal(displayName, stored.Value.DisplayName);
        Assert.Equal(1, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
    }

    [Fact]
    public async Task AddAsync_ExistingUser_ReturnsAlreadyExistsAndKeepsRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var original = NewUser();
        await Users.AddAsync(original, cancellationToken);
        var before = await Users.GetAsync(original.Id, cancellationToken);
        var duplicate = NewUser();
        duplicate.Id = original.Id;
        duplicate.DisplayName = "Overwriting name";
        duplicate.Status = UserStatus.Locked;

        var result = await Users.AddAsync(duplicate, cancellationToken);

        Assert.Equal(StoreWriteResult.AlreadyExists, result);
        var after = await Users.GetAsync(original.Id, cancellationToken);
        Assert.Equal(before!.ETag, after!.ETag);
        Assert.Equal(original.DisplayName, after.Value.DisplayName);
        Assert.Equal(UserStatus.Active, after.Value.Status);
    }

    [Fact]
    public async Task ReplaceAsync_CurrentETag_Writes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = NewUser();
        await Users.AddAsync(user, cancellationToken);
        var stored = await Users.GetAsync(user.Id, cancellationToken);
        user.DisplayName = "Changed name";

        var result = await Users.ReplaceAsync(user, stored!.ETag, cancellationToken);

        Assert.Equal(StoreWriteResult.Written, result);
        var after = await Users.GetAsync(user.Id, cancellationToken);
        Assert.Equal("Changed name", after!.Value.DisplayName);
        Assert.NotEqual(stored.ETag, after.ETag);
    }

    [Fact]
    public async Task ReplaceAsync_StaleETag_ReturnsConflictAndChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = NewUser();
        await Users.AddAsync(user, cancellationToken);
        var stale = await Users.GetAsync(user.Id, cancellationToken);
        user.DisplayName = "First writer";
        await Users.ReplaceAsync(user, stale!.ETag, cancellationToken);
        var current = await Users.GetAsync(user.Id, cancellationToken);
        user.DisplayName = "Second writer";
        user.Status = UserStatus.Locked;

        var result = await Users.ReplaceAsync(user, stale.ETag, cancellationToken);

        Assert.Equal(StoreWriteResult.Conflict, result);
        var after = await Users.GetAsync(user.Id, cancellationToken);
        Assert.Equal(current!.ETag, after!.ETag);
        Assert.Equal("First writer", after.Value.DisplayName);
        Assert.Equal(UserStatus.Active, after.Value.Status);
    }

    [Fact]
    public async Task ReplaceAsync_MissingRow_ReturnsNotFoundAndCreatesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = NewUser();

        var result = await Users.ReplaceAsync(user, "W/\"datetime'2020-01-01T00%3A00%3A00.0000000Z'\"", cancellationToken);

        Assert.Equal(StoreWriteResult.NotFound, result);
        Assert.Null(await Users.GetAsync(user.Id, cancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ReplaceAsync_BlankETag_DoesNotWriteAnUnconditionalUpdate(string etag)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = NewUser();
        await Users.AddAsync(user, cancellationToken);
        var before = await Users.GetAsync(user.Id, cancellationToken);
        var changed = NewUser();
        changed.Id = user.Id;
        changed.DisplayName = "Written without a valid ETag";
        changed.Status = UserStatus.Locked;

        var outcome = await Record.ExceptionAsync(() => Users.ReplaceAsync(changed, etag, cancellationToken));

        var after = await Users.GetAsync(user.Id, cancellationToken);
        Assert.Equal(before!.ETag, after!.ETag);
        Assert.Equal(user.DisplayName, after.Value.DisplayName);
        Assert.True(outcome is null or ArgumentException or RequestFailedException);
    }

    [Fact]
    public async Task ReplaceAsync_BlankETagForMissingRow_DoesNotCreateTheRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = NewUser();

        await Record.ExceptionAsync(() => Users.ReplaceAsync(user, string.Empty, cancellationToken));

        Assert.Null(await Users.GetAsync(user.Id, cancellationToken));
        Assert.Equal(0, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
    }

    [Fact]
    public async Task ReplaceAsync_LockedByAnotherWriter_IsNotOverwrittenByAStaleActiveCopy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = NewUser();
        await Users.AddAsync(user, cancellationToken);
        var readEarly = await Users.GetAsync(user.Id, cancellationToken);
        await LockUserAsync(user.Id);

        var result = await Users.ReplaceAsync(readEarly!.Value, readEarly.ETag, cancellationToken);

        var after = await Users.GetAsync(user.Id, cancellationToken);
        Assert.Equal(StoreWriteResult.Conflict, result);
        Assert.Equal(UserStatus.Locked, after!.Value.Status);
    }

    [Fact]
    public async Task ListAsync_CursorAtLastUser_ReturnsEmptyPageAndNullCursor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await UserRows.InsertManyAsync(GetUsersTable(), 5, cancellationToken);
        var last = (await Users.ListAsync(100, null, cancellationToken)).Users.Select(user => user.Id.ToString("D")).Max(StringComparer.Ordinal)!;

        var page = await Users.ListAsync(10, Guid.Parse(last), cancellationToken);

        Assert.Empty(page.Users);
        Assert.Null(page.NextAfterUserId);
    }

    [Fact]
    public async Task ListAsync_FirstPage_QueriesOnlyTheUserPartitionOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await UserRows.InsertManyAsync(GetUsersTable(), 30, cancellationToken);
        StorageRequests.Clear();

        var page = await Users.ListAsync(10, null, cancellationToken);

        Assert.Equal(10, page.Users.Count);
        Assert.NotNull(page.NextAfterUserId);
        var request = Assert.Single(StorageRequests.Requests);
        Assert.True(request.IsEntityQuery);
        Assert.Contains("PartitionKey eq 'user'", request.Filter, StringComparison.Ordinal);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task ListAsync_Pages_AreOrderedAndDoNotOverlap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await UserRows.InsertManyAsync(GetUsersTable(), 25, cancellationToken);

        var first = await Users.ListAsync(10, null, cancellationToken);
        var second = await Users.ListAsync(10, first.NextAfterUserId, cancellationToken);
        var third = await Users.ListAsync(10, second.NextAfterUserId, cancellationToken);

        var all = first.Users.Concat(second.Users).Concat(third.Users).Select(user => user.Id.ToString("D")).ToList();
        Assert.Equal(25, all.Count);
        Assert.Equal(all.Order(StringComparer.Ordinal), all);
        Assert.Equal(25, all.Distinct().Count());
        Assert.Null(third.NextAfterUserId);
    }

    [Fact]
    public async Task AppendAsync_NewEntry_IsStoredInTheMonthPartition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var entry = NewEntry();

        await Audit.AppendAsync(entry, cancellationToken);

        var stored = Assert.Single(await ListAuditRowsAsync());
        Assert.Equal(entry.OccurredAt.UtcDateTime.ToString("yyyyMM", System.Globalization.CultureInfo.InvariantCulture), stored.PartitionKey);
        Assert.Equal("SignIn", stored.GetString("EventType"));
        Assert.Equal(entry.ActorId.ToString("D"), stored.GetString("ActorId"));
    }

    [Fact]
    public async Task AppendAsync_DuplicateKeys_FailsInsteadOfOverwriting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var entry = NewEntry();
        await Audit.AppendAsync(entry, cancellationToken);
        var before = Assert.Single(await ListAuditRowsAsync());
        var tampered = new AuditEntry
        {
            Id = entry.Id,
            EventType = entry.EventType,
            ActorId = Guid.NewGuid(),
            OccurredAt = entry.OccurredAt,
            IpAddress = "198.51.100.9",
            Reason = "rewritten",
        };

        var exception = await Assert.ThrowsAsync<RequestFailedException>(() => Audit.AppendAsync(tampered, cancellationToken));

        Assert.Equal(Conflict, exception.Status);
        var after = Assert.Single(await ListAuditRowsAsync());
        Assert.Equal(before.ETag, after.ETag);
        Assert.Equal(entry.ActorId.ToString("D"), after.GetString("ActorId"));
        Assert.True(string.IsNullOrEmpty(after.GetString("Reason")));
    }

    [Fact]
    public async Task AppendAsync_ManyEntriesSameInstant_AreAllKept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instant = DateTimeOffset.UtcNow;
        var entries = Enumerable.Range(0, 10).Select(_ => NewEntry(instant)).ToList();

        await Task.WhenAll(entries.Select(entry => Audit.AppendAsync(entry, cancellationToken)));

        Assert.Equal(10, (await ListAuditRowsAsync()).Count);
    }

    private static User NewUser() => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Store test user",
        Email = "store@sharpvault.test",
        Status = UserStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        LastSignInAt = DateTimeOffset.UtcNow,
    };

    private static AuditEntry NewEntry(DateTimeOffset? at = null) => new()
    {
        Id = Guid.NewGuid(),
        EventType = AuditEventType.SignIn,
        ActorId = Guid.NewGuid(),
        OccurredAt = at ?? DateTimeOffset.UtcNow,
    };
}
