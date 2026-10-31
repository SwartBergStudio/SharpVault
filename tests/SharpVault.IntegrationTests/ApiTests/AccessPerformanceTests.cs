// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc.Testing;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.ServiceDefaults;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(PerformanceTestGroup))]
[Trait("Category", "Performance")]
public sealed class AccessPerformanceTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private const string MePath = "/api/me";
    private const string AdminUsersPath = "/api/admin/users";
    private const string CacheTtlSetting = "UserAccess:CacheTtl";
    private const string ThirtySecondTtl = "00:00:30";
    private const string UsersTablePathSegment = "/Users";
    private const string AuditTablePathSegment = "/AuditLog";
    private const string PartitionKeyFilter = "PartitionKey eq 'user'";
    private const int WarmUpRequests = 5;
    private const int MeasuredRequests = 50;
    private const int SeededUserCount = 5000;
    private const int PageSize = 50;

    private static readonly TimeSpan DeniedBudget = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan CacheHitBudget = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan CacheMissBudget = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan WriteBudget = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan AdminListBudget = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SignInInterval = TimeSpan.FromHours(1);

    [Fact]
    public async Task GetMe_CachedAccess_FiftyRequestsEachUnder50MillisecondsWithoutStorageCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        using var cached = CreateCachedFactory();
        using var client = CreateClientFor(cached, TestUsers.User);
        await WarmUpAsync(client, MePath, HttpStatusCode.OK, cancellationToken);
        StorageRequests.Clear();

        var timings = await MeasureManyAsync(client, MePath, HttpStatusCode.OK, CacheHitBudget, cancellationToken);

        Assert.Equal(MeasuredRequests, timings.Count);
        Assert.Equal(0, StorageRequests.Count);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task GetMe_CachedAccessForAdmin_FiftyRequestsEachUnder50MillisecondsWithoutStorageCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.Admin, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        using var cached = CreateCachedFactory();
        using var client = CreateClientFor(cached, TestUsers.Admin);
        await WarmUpAsync(client, MePath, HttpStatusCode.OK, cancellationToken);
        StorageRequests.Clear();

        await MeasureManyAsync(client, MePath, HttpStatusCode.OK, CacheHitBudget, cancellationToken);

        Assert.Equal(0, StorageRequests.Count);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task GetMe_CacheDisabled_FiftyRequestsEachUnder200MillisecondsWithOnePointReadEach()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        await WarmUpAsync(client, MePath, HttpStatusCode.OK, cancellationToken);
        StorageRequests.Clear();

        await MeasureManyAsync(client, MePath, HttpStatusCode.OK, CacheMissBudget, cancellationToken);

        Assert.Equal(MeasuredRequests, StorageRequests.Count);
        Assert.All(StorageRequests.Requests, request => AssertPointReadOnUsers(request));
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task FirstRequest_NewUser_CompletesUnder500MillisecondsWithThreeStorageCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.OtherUser, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        using var warmUpClient = Factory.CreateAuthenticatedClient(TestUsers.OtherUser);
        await WarmUpAsync(warmUpClient, MePath, HttpStatusCode.OK, cancellationToken);
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        StorageRequests.Clear();

        var elapsed = await MeasureAsync(client, MePath, HttpStatusCode.OK, cancellationToken);

        Assert.True(elapsed < WriteBudget, $"First request of a new user took {elapsed.TotalMilliseconds:F0} ms");
        var requests = StorageRequests.Requests;
        Assert.Equal(3, requests.Count);
        AssertPointReadOnUsers(requests[0]);
        AssertInsert(requests[1], UsersTablePathSegment);
        AssertInsert(requests[2], AuditTablePathSegment);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task SignInDue_ExistingUser_CompletesUnder500MillisecondsWithThreeStorageCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var longAgo = DateTimeOffset.UtcNow - (SignInInterval * 2);
        await Factory.SeedUserAsync(TestUsers.OtherUser, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, longAgo, cancellationToken);
        using var warmUpClient = Factory.CreateAuthenticatedClient(TestUsers.OtherUser);
        await WarmUpAsync(warmUpClient, MePath, HttpStatusCode.OK, cancellationToken);
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        StorageRequests.Clear();

        var elapsed = await MeasureAsync(client, MePath, HttpStatusCode.OK, cancellationToken);

        Assert.True(elapsed < WriteBudget, $"Sign-in due request took {elapsed.TotalMilliseconds:F0} ms");
        var requests = StorageRequests.Requests;
        Assert.Equal(3, requests.Count);
        AssertPointReadOnUsers(requests[0]);
        Assert.Equal("PUT", requests[1].Method);
        Assert.Contains(UsersTablePathSegment + "(", requests[1].Path, StringComparison.Ordinal);
        AssertInsert(requests[2], AuditTablePathSegment);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task SignInDue_SecondRequestAfterRecording_MakesOnlyAPointRead()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var longAgo = DateTimeOffset.UtcNow - (SignInInterval * 2);
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, longAgo, cancellationToken);
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        await MeasureAsync(client, MePath, HttpStatusCode.OK, cancellationToken);
        StorageRequests.Clear();

        var elapsed = await MeasureAsync(client, MePath, HttpStatusCode.OK, cancellationToken);

        Assert.True(elapsed < CacheMissBudget, $"Second request took {elapsed.TotalMilliseconds:F0} ms");
        var request = Assert.Single(StorageRequests.Requests);
        AssertPointReadOnUsers(request);
    }

    [Fact]
    public async Task AdminUsers_PageOfFiftyFromFiveThousand_CompletesUnder500MillisecondsWithOnePartitionQuery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.Admin, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        await UserRows.InsertManyAsync(GetUsersTable(), SeededUserCount, cancellationToken);
        using var cached = CreateCachedFactory();
        using var client = CreateClientFor(cached, TestUsers.Admin);
        var path = $"{AdminUsersPath}?pageSize={PageSize}";
        await WarmUpAsync(client, path, HttpStatusCode.OK, cancellationToken);
        StorageRequests.Clear();

        var elapsed = await MeasureAsync(client, path, HttpStatusCode.OK, cancellationToken);

        Assert.True(elapsed < AdminListBudget, $"Admin user list took {elapsed.TotalMilliseconds:F0} ms");
        var request = Assert.Single(StorageRequests.Requests);
        Assert.True(request.IsEntityQuery);
        Assert.True(request.HasPartitionKeyCondition);
        Assert.Contains(PartitionKeyFilter, request.Filter, StringComparison.Ordinal);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task AdminUsers_CacheDisabled_MakesOnePointReadAndOnePartitionQuery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.Admin, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        await UserRows.InsertManyAsync(GetUsersTable(), SeededUserCount, cancellationToken);
        using var client = Factory.CreateAuthenticatedClient(TestUsers.Admin);
        var path = $"{AdminUsersPath}?pageSize={PageSize}";
        await WarmUpAsync(client, path, HttpStatusCode.OK, cancellationToken);
        StorageRequests.Clear();

        var elapsed = await MeasureAsync(client, path, HttpStatusCode.OK, cancellationToken);

        Assert.True(elapsed < AdminListBudget, $"Admin user list took {elapsed.TotalMilliseconds:F0} ms");
        var requests = StorageRequests.Requests;
        Assert.Equal(2, requests.Count);
        Assert.Single(requests, request => request.IsEntityQuery && request.HasPartitionKeyCondition);
        Assert.Single(requests, request => !request.IsEntityQuery);
        AssertNoQueryWithoutPartitionKey();
    }

    [Theory]
    [InlineData(CallerKind.Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(CallerKind.AlgNoneToken, HttpStatusCode.Unauthorized)]
    [InlineData(CallerKind.UnknownKeyToken, HttpStatusCode.Unauthorized)]
    [InlineData(CallerKind.OtherTenantUser, HttpStatusCode.Unauthorized)]
    [InlineData(CallerKind.AuthenticatedWithoutRole, HttpStatusCode.Forbidden)]
    [InlineData(CallerKind.Guest, HttpStatusCode.Forbidden)]
    public async Task DeniedRequests_EachUnder50MillisecondsWithoutStorageCalls(CallerKind caller, HttpStatusCode expected)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await ArrangeCallerAsync(caller);
        await WarmUpAsync(client, MePath, expected, cancellationToken);
        StorageRequests.Clear();

        await MeasureManyAsync(client, MePath, expected, DeniedBudget, cancellationToken);

        Assert.Equal(0, StorageRequests.Count);
        AssertNoQueryWithoutPartitionKey();
    }

    [Theory]
    [InlineData(CallerKind.Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(CallerKind.AuthenticatedWithoutRole, HttpStatusCode.Forbidden)]
    [InlineData(CallerKind.Guest, HttpStatusCode.Forbidden)]
    public async Task DeniedAdminRequests_EachUnder50MillisecondsWithoutStorageCalls(CallerKind caller, HttpStatusCode expected)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await ArrangeCallerAsync(caller);
        await WarmUpAsync(client, AdminUsersPath, expected, cancellationToken);
        StorageRequests.Clear();

        await MeasureManyAsync(client, AdminUsersPath, expected, DeniedBudget, cancellationToken);

        Assert.Equal(0, StorageRequests.Count);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task AdminUsers_PlainUserWithCacheDisabled_IsForbiddenUnder50MillisecondsWithoutStorageCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        await WarmUpAsync(client, AdminUsersPath, HttpStatusCode.Forbidden, cancellationToken);
        StorageRequests.Clear();

        await MeasureManyAsync(client, AdminUsersPath, HttpStatusCode.Forbidden, DeniedBudget, cancellationToken);

        Assert.Equal(0, StorageRequests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockedUser_CachedDenial_FiftyRequestsEachUnder50MillisecondsWithoutStorageCalls(bool signInDue)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var lastSignInAt = signInDue ? DateTimeOffset.UtcNow - (SignInInterval * 2) : DateTimeOffset.UtcNow;
        await Factory.SeedUserAsync(TestUsers.LockedUser, UserRows.LockedStatus, lastSignInAt, cancellationToken);
        using var cached = CreateCachedFactory();
        using var client = CreateClientFor(cached, TestUsers.LockedUser);
        await WarmUpAsync(client, MePath, HttpStatusCode.Forbidden, cancellationToken);
        StorageRequests.Clear();

        await MeasureManyAsync(client, MePath, HttpStatusCode.Forbidden, CacheHitBudget, cancellationToken);

        Assert.Equal(0, StorageRequests.Count);
        AssertNoQueryWithoutPartitionKey();
        var stored = await UserRows.GetAsync(GetUsersTable(), TestUsers.LockedUser.ObjectId, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(UserRows.LockedStatus, stored[UserColumns.Status]);
        Assert.Equal(0, GetAuditRowCountOrZero());
    }

    [Fact]
    public async Task LockedAdmin_CachedDenialOnAdminList_FiftyRequestsEachUnder50MillisecondsWithoutStorageCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.LockedAdmin, UserRows.LockedStatus, DateTimeOffset.UtcNow - (SignInInterval * 2), cancellationToken);
        using var cached = CreateCachedFactory();
        using var client = CreateClientFor(cached, TestUsers.LockedAdmin);
        await WarmUpAsync(client, AdminUsersPath, HttpStatusCode.Forbidden, cancellationToken);
        StorageRequests.Clear();

        await MeasureManyAsync(client, AdminUsersPath, HttpStatusCode.Forbidden, CacheHitBudget, cancellationToken);

        Assert.Equal(0, StorageRequests.Count);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task LockedUser_CacheDisabled_FiftyRequestsEachUnder200MillisecondsWithOnePointReadEach()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.LockedUser, UserRows.LockedStatus, DateTimeOffset.UtcNow, cancellationToken);
        using var client = Factory.CreateAuthenticatedClient(TestUsers.LockedUser);
        await WarmUpAsync(client, MePath, HttpStatusCode.Forbidden, cancellationToken);
        StorageRequests.Clear();

        await MeasureManyAsync(client, MePath, HttpStatusCode.Forbidden, CacheMissBudget, cancellationToken);

        Assert.Equal(MeasuredRequests, StorageRequests.Count);
        Assert.All(StorageRequests.Requests, request => AssertPointReadOnUsers(request));
    }

    [Fact]
    public async Task AliveEndpoint_FiftyRequestsWithUserToken_EachUnder50MillisecondsWithoutStorageCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        await WarmUpAsync(client, HealthEndpoints.AlivePath, HttpStatusCode.OK, cancellationToken);
        StorageRequests.Clear();

        await MeasureManyAsync(client, HealthEndpoints.AlivePath, HttpStatusCode.OK, DeniedBudget, cancellationToken);

        Assert.Equal(0, StorageRequests.Count);
    }

    private WebApplicationFactory<Program> CreateCachedFactory() =>
        Factory.WithWebHostBuilder(builder => builder.UseSetting(CacheTtlSetting, ThirtySecondTtl));

    private static HttpClient CreateClientFor(WebApplicationFactory<Program> host, TestUser user)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return client;
    }

    private int GetAuditRowCountOrZero()
    {
        var count = 0;
        foreach (var _ in GetAuditTable().Query<TableEntity>(select: ["RowKey"]))
        {
            count++;
        }

        return count;
    }

    private static void AssertPointReadOnUsers(RecordedStorageRequest request)
    {
        Assert.Equal("GET", request.Method);
        Assert.False(request.IsEntityQuery, $"Expected a point read but saw a query: {request.Path}");
        Assert.Contains(UsersTablePathSegment + "(PartitionKey=", request.Path, StringComparison.Ordinal);
        Assert.Contains("RowKey=", request.Path, StringComparison.Ordinal);
    }

    private static void AssertInsert(RecordedStorageRequest request, string tablePathSegment)
    {
        Assert.Equal("POST", request.Method);
        Assert.EndsWith(tablePathSegment, request.Path.TrimEnd('/'), StringComparison.Ordinal);
    }

    private static async Task<TimeSpan> MeasureAsync(
        HttpClient client,
        string path,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var response = await client.GetAsync(path, cancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(started);
        Assert.Equal(expected, response.StatusCode);
        return elapsed;
    }

    private static async Task<List<TimeSpan>> MeasureManyAsync(
        HttpClient client,
        string path,
        HttpStatusCode expected,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        var timings = new List<TimeSpan>();
        for (var i = 0; i < MeasuredRequests; i++)
        {
            var elapsed = await MeasureAsync(client, path, expected, cancellationToken);
            timings.Add(elapsed);
        }

        var slowest = timings.Max();
        Assert.True(
            slowest < budget,
            $"Slowest of {MeasuredRequests} requests to {path} took {slowest.TotalMilliseconds:F0} ms, budget {budget.TotalMilliseconds:F0} ms. {FormatTimings(timings)}");
        return timings;
    }

    private static async Task WarmUpAsync(
        HttpClient client,
        string path,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < WarmUpRequests; i++)
        {
            using var response = await client.GetAsync(path, cancellationToken);
            Assert.Equal(expected, response.StatusCode);
        }
    }

    private static string FormatTimings(IEnumerable<TimeSpan> timings) =>
        "Timings (ms): " + string.Join(", ", timings.Select(timing => timing.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)));
}
