// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Infrastructure.Users;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

/// <summary>
/// LCK-1: a locked user with a still valid token is denied on every request, and a lock is never overwritten by a sign-in.
/// </summary>
[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class LockEnforcementTests(ApiTestFactory factory) : ApiSecurityTestBase(factory)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LongCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TwoHours = TimeSpan.FromHours(2);
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);
    private static readonly string[] BothRoles = ["SharpVault.Admin", "SharpVault.User"];

    public static TheoryData<string> LockedRequestPaths() => [MePath, UnknownPath];

    [Fact]
    public async Task LockedUser_ExistingValidToken_RejectedOnNextRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);
        var first = await Client.GetAsync(MePath, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        await LockUserAsync(TestUsers.User.ObjectId);
        var second = await Client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task LockedAdmin_ExistingValidToken_AdminEndpointRejectedOnNextRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.Admin);
        var first = await Client.GetAsync(AdminUsersPath, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        await LockUserAsync(TestUsers.Admin.ObjectId);
        var me = await Client.GetAsync(MePath, cancellationToken);
        var list = await Client.GetAsync(AdminUsersPath, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task LockedUser_UnlockedAgain_IsAllowedOnNextRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);
        await Client.GetAsync(MePath, cancellationToken);
        await LockUserAsync(TestUsers.User.ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync(MePath, cancellationToken)).StatusCode);

        await SetUserStatusAsync(TestUsers.User.ObjectId, UserRows.ActiveStatus);
        var response = await Client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(LockedRequestPaths))]
    public async Task LockedUser_Request_DoesNotRecordSignInOrUpdateRow(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var twoHoursAgo = DateTimeOffset.UtcNow.AddHours(-2);
        await Factory.SeedUserAsync(TestUsers.LockedUser, UserRows.LockedStatus, twoHoursAgo, cancellationToken);
        var before = await RequireUserRowAsync(TestUsers.LockedUser.ObjectId);
        SetAuthenticatedClient(TestUsers.LockedUser);
        StorageRequests.Clear();

        var response = await Client.GetAsync(path, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var after = await RequireUserRowAsync(TestUsers.LockedUser.ObjectId);
        Assert.Equal(before.ETag, after.ETag);
        Assert.Equal(before.GetDateTimeOffset(UserColumns.LastSignInAt), after.GetDateTimeOffset(UserColumns.LastSignInAt));
        Assert.Empty(await ListAuditRowsAsync());
        Assert.Empty(WriteRequests());
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task LockedUser_ManyRequests_AreAllDeniedAndNothingIsWritten()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.LockedUser, UserRows.LockedStatus, DateTimeOffset.UtcNow.AddHours(-3), cancellationToken);
        var before = await RequireUserRowAsync(TestUsers.LockedUser.ObjectId);
        SetAuthenticatedClient(TestUsers.LockedUser);
        StorageRequests.Clear();

        for (var attempt = 0; attempt < 15; attempt++)
        {
            var path = attempt % 2 == 0 ? MePath : UnknownPath;
            var response = await Client.GetAsync(path, cancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        var after = await RequireUserRowAsync(TestUsers.LockedUser.ObjectId);
        Assert.Equal(before.ETag, after.ETag);
        Assert.Empty(await ListAuditRowsAsync());
        Assert.Empty(WriteRequests());
    }

    [Fact]
    public async Task LockedUser_ParallelRequests_AreAllDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.LockedUser, UserRows.LockedStatus, DateTimeOffset.UtcNow.AddHours(-3), cancellationToken);
        var before = await RequireUserRowAsync(TestUsers.LockedUser.ObjectId);
        var clients = Enumerable.Range(0, 20).Select(_ => Factory.CreateAuthenticatedClient(TestUsers.LockedUser)).ToList();

        var responses = await Task.WhenAll(clients.Select(client => client.GetAsync(MePath, cancellationToken)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
        var after = await RequireUserRowAsync(TestUsers.LockedUser.ObjectId);
        Assert.Equal(before.ETag, after.ETag);
        Assert.Empty(await ListAuditRowsAsync());
    }

    [Fact]
    public async Task LockedUser_UppercaseObjectIdInToken_StillDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.LockedUser, UserRows.LockedStatus, DateTimeOffset.UtcNow, cancellationToken);
        var token = TestTokens.For(TestUsers.LockedUser)
            .WithClaim("oid", TestUsers.LockedUser.ObjectId.ToString("D").ToUpperInvariant())
            .Build();

        var response = await GetWithTokenAsync(MePath, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
        Assert.Empty(await ListAuditRowsAsync());
    }

    [Fact]
    public async Task LockedUser_TokenClaimsAdminRoleAndNewName_StillDeniedAndRowUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.LockedUser, UserRows.LockedStatus, DateTimeOffset.UtcNow.AddHours(-5), cancellationToken);
        var before = await RequireUserRowAsync(TestUsers.LockedUser.ObjectId);
        var token = TestTokens.For(TestUsers.LockedUser with { DisplayName = "Renamed after lock", Email = "renamed@sharpvault.test" })
            .WithClaim("roles", BothRoles)
            .Build();

        var me = await GetWithTokenAsync(MePath, token);
        var list = await GetWithTokenAsync(AdminUsersPath, token);

        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        var after = await RequireUserRowAsync(TestUsers.LockedUser.ObjectId);
        Assert.Equal(before.ETag, after.ETag);
        Assert.Equal(before.GetString(UserColumns.DisplayName), after.GetString(UserColumns.DisplayName));
    }

    [Fact]
    public async Task LockedUser_CachedActiveState_DeniedOnceTtlExpires()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var cached = CreateTimedFactory(time, CacheTtl);
        using var client = CreateClientFor(cached, TestTokens.Create(TestUsers.User));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MePath, cancellationToken)).StatusCode);

        await LockUserAsync(TestUsers.User.ObjectId);
        var withinTtl = await client.GetAsync(MePath, cancellationToken);
        time.Advance(CacheTtl + TimeSpan.FromMilliseconds(1));
        var afterTtl = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, withinTtl.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, afterTtl.StatusCode);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task LockedUser_CachedActiveStateJustInsideTtl_StillWithinStalenessBudget()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var cached = CreateTimedFactory(time, CacheTtl);
        using var client = CreateClientFor(cached, TestTokens.Create(TestUsers.User));
        await client.GetAsync(MePath, cancellationToken);
        await LockUserAsync(TestUsers.User.ObjectId);

        time.Advance(CacheTtl - TimeSpan.FromMilliseconds(1));
        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LockedUser_CachedActiveStateInvalidatedOnWritingInstance_DeniedImmediately()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var cached = CreateTimedFactory(time, LongCacheTtl);
        using var client = CreateClientFor(cached, TestTokens.Create(TestUsers.User));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MePath, cancellationToken)).StatusCode);
        await LockUserAsync(TestUsers.User.ObjectId);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MePath, cancellationToken)).StatusCode);

        cached.Services.GetRequiredService<IUserAccessCache>().Invalidate(TestUsers.User.ObjectId);
        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LockedUser_CachedLockedStateAfterSignInInterval_DeniesWithoutStorageCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await Factory.SeedUserAsync(TestUsers.LockedUser, UserRows.LockedStatus, time.GetUtcNow() - TwoHours, cancellationToken);
        using var cached = CreateTimedFactory(time, LongCacheTtl);
        using var client = CreateClientFor(cached, TestTokens.Create(TestUsers.LockedUser));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(MePath, cancellationToken)).StatusCode);
        StorageRequests.Clear();

        time.Advance(OneSecond);
        var second = await client.GetAsync(MePath, cancellationToken);
        time.Advance(OneSecond);
        var third = await client.GetAsync(UnknownPath, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, third.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
        Assert.Empty(await ListAuditRowsAsync());
    }

    [Fact]
    public async Task LockedUser_CachedLockedState_ReadsStorageAgainOnlyAfterTtl()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await Factory.SeedUserAsync(TestUsers.LockedUser, UserRows.LockedStatus, time.GetUtcNow() - TwoHours, cancellationToken);
        using var cached = CreateTimedFactory(time, CacheTtl);
        using var client = CreateClientFor(cached, TestTokens.Create(TestUsers.LockedUser));
        await client.GetAsync(MePath, cancellationToken);
        await SetUserStatusAsync(TestUsers.LockedUser.ObjectId, UserRows.ActiveStatus);

        var withinTtl = await client.GetAsync(MePath, cancellationToken);
        time.Advance(CacheTtl + TimeSpan.FromMilliseconds(1));
        var afterTtl = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, withinTtl.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterTtl.StatusCode);
    }

    [Fact]
    public async Task CachedUsers_LockOfOneUser_DoesNotAffectAnotherUser()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var cached = CreateTimedFactory(time, CacheTtl);
        using var first = CreateClientFor(cached, TestTokens.Create(TestUsers.User));
        using var second = CreateClientFor(cached, TestTokens.Create(TestUsers.OtherUser));
        await first.GetAsync(MePath, cancellationToken);
        await second.GetAsync(MePath, cancellationToken);

        await LockUserAsync(TestUsers.User.ObjectId);
        time.Advance(CacheTtl + TimeSpan.FromMilliseconds(1));

        Assert.Equal(HttpStatusCode.Forbidden, (await first.GetAsync(MePath, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync(MePath, cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task CachedUsers_LockedUserCached_DoesNotDenyAnotherUserWithSameEmailAndName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await Factory.SeedUserAsync(TestUsers.LockedUser, UserRows.LockedStatus, time.GetUtcNow(), cancellationToken);
        using var cached = CreateTimedFactory(time, LongCacheTtl);
        using var locked = CreateClientFor(cached, TestTokens.Create(TestUsers.LockedUser));
        var lookalike = TestUsers.User with { DisplayName = TestUsers.LockedUser.DisplayName, Email = TestUsers.LockedUser.Email };
        using var other = CreateClientFor(cached, TestTokens.Create(lookalike));

        Assert.Equal(HttpStatusCode.Forbidden, (await locked.GetAsync(MePath, cancellationToken)).StatusCode);
        var response = await other.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SignInRecord_LockWrittenBetweenReadAndReplace_KeepsLockAndReturns403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, DateTimeOffset.UtcNow.AddHours(-2), cancellationToken);
        using var interleaved = CreateInterleavedFactory(TestUsers.User.ObjectId, _ => LockUserAsync(TestUsers.User.ObjectId));
        using var client = CreateClientFor(interleaved, TestTokens.Create(TestUsers.User));

        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var row = await RequireUserRowAsync(TestUsers.User.ObjectId);
        Assert.Equal(UserRows.LockedStatus, row.GetString(UserColumns.Status));
        Assert.Empty(await ListAuditRowsAsync());
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task SignInRecord_LockWrittenBetweenReadAndReplace_LaterRequestsStayDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, DateTimeOffset.UtcNow.AddHours(-2), cancellationToken);
        using var interleaved = CreateInterleavedFactory(TestUsers.User.ObjectId, _ => LockUserAsync(TestUsers.User.ObjectId));
        using var client = CreateClientFor(interleaved, TestTokens.Create(TestUsers.User));

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            statuses.Add((await client.GetAsync(MePath, cancellationToken)).StatusCode);
        }

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.Forbidden, status));
        Assert.Empty(await ListAuditRowsAsync());
    }

    [Fact]
    public async Task NewUser_LockedRowInsertedBetweenReadAndAdd_Returns403WithoutAudit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var interleaved = CreateInterleavedFactory(
            TestUsers.User.ObjectId,
            token => UserRows.InsertAsync(GetUsersTable(), TestUsers.User, UserRows.LockedStatus, DateTimeOffset.UtcNow, token));
        using var client = CreateClientFor(interleaved, TestTokens.Create(TestUsers.User));

        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var row = await RequireUserRowAsync(TestUsers.User.ObjectId);
        Assert.Equal(UserRows.LockedStatus, row.GetString(UserColumns.Status));
        Assert.Empty(await ListAuditRowsAsync());
    }

    [Fact]
    public async Task NewUser_ActiveRowInsertedBetweenReadAndAdd_Returns200WithoutDuplicateAudit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var interleaved = CreateInterleavedFactory(
            TestUsers.User.ObjectId,
            token => UserRows.InsertAsync(GetUsersTable(), TestUsers.User, UserRows.ActiveStatus, DateTimeOffset.UtcNow, token));
        using var client = CreateClientFor(interleaved, TestTokens.Create(TestUsers.User));

        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
        Assert.Empty(await ListAuditRowsAsync());
    }

    [Fact]
    public async Task SignInDue_RowReplacedByAnotherInstance_Returns200WithoutOverwritingOrDuplicateAudit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, DateTimeOffset.UtcNow.AddHours(-2), cancellationToken);
        var otherInstanceWrite = DateTimeOffset.UtcNow;
        using var interleaved = CreateInterleavedFactory(TestUsers.User.ObjectId, async _ =>
        {
            var row = await RequireUserRowAsync(TestUsers.User.ObjectId);
            row[UserColumns.LastSignInAt] = otherInstanceWrite;
            row[UserColumns.DisplayName] = "Written by another instance";
            await GetUsersTable().UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace, cancellationToken);
        });
        using var client = CreateClientFor(interleaved, TestTokens.Create(TestUsers.User));

        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await RequireUserRowAsync(TestUsers.User.ObjectId);
        Assert.Equal("Written by another instance", row.GetString(UserColumns.DisplayName));
        Assert.Empty(await ListAuditRowsAsync());
    }

    [Fact]
    public async Task SignInDue_RowDeletedBetweenReadAndReplace_FailsClosed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, DateTimeOffset.UtcNow.AddHours(-2), cancellationToken);
        using var interleaved = CreateInterleavedFactory(
            TestUsers.User.ObjectId,
            token => GetUsersTable().DeleteEntityAsync(UserRows.PartitionKey, TestUsers.User.ObjectId.ToString("D"), cancellationToken: token));
        using var client = CreateClientFor(interleaved, TestTokens.Create(TestUsers.User));

        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(await ListAuditRowsAsync());
    }

    [Fact]
    public async Task SignInDue_ParallelRequestsWhileLocking_NeverEndWithAnActiveRowAfterTheLock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, DateTimeOffset.UtcNow.AddHours(-2), cancellationToken);
        var clients = Enumerable.Range(0, 10).Select(_ => Factory.CreateAuthenticatedClient(TestUsers.User)).ToList();
        var requests = clients.Select(client => client.GetAsync(MePath, cancellationToken)).ToList();

        await LockUserAsync(TestUsers.User.ObjectId);
        await Task.WhenAll(requests);

        var row = await RequireUserRowAsync(TestUsers.User.ObjectId);
        Assert.Equal(UserRows.LockedStatus, row.GetString(UserColumns.Status));
        var after = await Factory.CreateAuthenticatedClient(TestUsers.User).GetAsync(MePath, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    private WebApplicationFactory<Program> CreateInterleavedFactory(Guid targetUserId, Func<CancellationToken, Task> afterFirstGet) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUserStore>();
            services.AddSingleton<IUserStore>(provider => new InterleavingUserStore(
                new TableUserStore(provider.GetRequiredService<TableServiceClient>()),
                targetUserId,
                afterFirstGet));
        }));
}
