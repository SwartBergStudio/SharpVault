// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Azure.Data.Tables;
using Microsoft.Extensions.Time.Testing;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;

namespace SharpVault.IntegrationTests.ApiTests;

/// <summary>
/// TEN-5 and AUD-2: the user row is created once, the sign-in is recorded once per interval and audited once.
/// </summary>
[Collection(nameof(StorageTestGroup))]
public sealed partial class UserProvisioningTests(ApiTestFactory factory) : ApiSecurityTestBase(factory)
{
    private const string SignInEventType = "SignIn";
    private const string XForwardedFor = "X-Forwarded-For";
    private const string SpoofedAddress = "203.0.113.9";
    private const int ParallelRequests = 20;

    private static readonly TimeSpan CacheOff = TimeSpan.Zero;
    private static readonly TimeSpan SignInInterval = TimeSpan.FromHours(1);

    private static readonly HashSet<string> AllowedAuditProperties = new(StringComparer.Ordinal)
    {
        "PartitionKey",
        "RowKey",
        "Timestamp",
        "odata.etag",
        "EventType",
        "ActorId",
        "OccurredAt",
        "TargetId",
        "IpAddress",
        "Reason",
    };

    [Fact]
    public async Task FirstRequest_NewUser_CreatesActiveRowWithClaimsData()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);

        var response = await Client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await RequireUserRowAsync(TestUsers.User.ObjectId);
        Assert.Equal(UserRows.PartitionKey, row.PartitionKey);
        Assert.Equal(TestUsers.User.ObjectId.ToString("D"), row.RowKey);
        Assert.Equal(TestUsers.User.DisplayName, row.GetString(UserColumns.DisplayName));
        Assert.Equal(TestUsers.User.Email, row.GetString(UserColumns.Email));
        Assert.Equal(UserRows.ActiveStatus, row.GetString(UserColumns.Status));
        Assert.Equal(row.GetDateTimeOffset(UserColumns.CreatedAt), row.GetDateTimeOffset(UserColumns.LastSignInAt));
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task FirstRequest_NewUser_WritesOneSignInAuditEntry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);

        await Client.GetAsync(MePath, cancellationToken);

        var entry = Assert.Single(await ListAuditRowsAsync());
        Assert.Equal(SignInEventType, entry.GetString("EventType"));
        Assert.Equal(TestUsers.User.ObjectId.ToString("D"), entry.GetString("ActorId"));
        var occurredAt = entry.GetDateTimeOffset("OccurredAt")!.Value;
        Assert.Equal(occurredAt.UtcDateTime.ToString("yyyyMM", CultureInfo.InvariantCulture), entry.PartitionKey);
        Assert.Matches(AuditRowKey(), entry.RowKey);
        var invertedTicks = long.Parse(entry.RowKey[..19], CultureInfo.InvariantCulture);
        Assert.Equal(DateTime.MaxValue.Ticks - occurredAt.UtcTicks, invertedTicks);
        Assert.True(string.IsNullOrEmpty(entry.GetString("TargetId")));
        Assert.True(string.IsNullOrEmpty(entry.GetString("Reason")));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SignInAuditEntry_Content_HasOnlyIdsTimeEventAndIp()
    {
        var token = TestTokens.Create(TestUsers.User);
        var response = await GetWithTokenAsync(MePath, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entry = Assert.Single(await ListAuditRowsAsync());

        Assert.All(entry.Select(pair => pair.Key), key => Assert.Contains(key, AllowedAuditProperties));
        foreach (var value in entry.Select(pair => pair.Value).Select(value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty))
        {
            Assert.DoesNotContain(TestUsers.User.DisplayName, value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(TestUsers.User.Email!, value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(token, value, StringComparison.Ordinal);
            foreach (var segment in token.Split('.'))
            {
                Assert.DoesNotContain(segment, value, StringComparison.Ordinal);
            }
        }

    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task TwentyRequestsWithinInterval_WriteOneSignInAudit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);

        for (var request = 0; request < ParallelRequests; request++)
        {
            var response = await Client.GetAsync(MePath, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Single(await ListAuditRowsAsync());
        Assert.Equal(2, WriteRequests().Count);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task RequestAfterSignInInterval_WritesNewSignInAuditEntry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var timed = CreateTimedFactory(time, CacheOff);
        using var client = CreateClientFor(timed, TestTokens.Create(TestUsers.User));
        await client.GetAsync(MePath, cancellationToken);
        var first = await RequireUserRowAsync(TestUsers.User.ObjectId);

        time.Advance(TimeSpan.FromMinutes(61));
        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var second = await RequireUserRowAsync(TestUsers.User.ObjectId);
        Assert.Equal(2, (await ListAuditRowsAsync()).Count);
        Assert.NotEqual(first.ETag, second.ETag);
        Assert.True(second.GetDateTimeOffset(UserColumns.LastSignInAt) > first.GetDateTimeOffset(UserColumns.LastSignInAt));
        Assert.Equal(first.GetDateTimeOffset(UserColumns.CreatedAt), second.GetDateTimeOffset(UserColumns.CreatedAt));
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task RequestJustBeforeSignInInterval_DoesNotRecordAnotherSignIn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var timed = CreateTimedFactory(time, CacheOff);
        using var client = CreateClientFor(timed, TestTokens.Create(TestUsers.User));
        await client.GetAsync(MePath, cancellationToken);
        var first = await RequireUserRowAsync(TestUsers.User.ObjectId);

        time.Advance(SignInInterval - TimeSpan.FromSeconds(1));
        await client.GetAsync(MePath, cancellationToken);

        Assert.Single(await ListAuditRowsAsync());
        Assert.Equal(first.ETag, (await RequireUserRowAsync(TestUsers.User.ObjectId)).ETag);
    }

    [Fact]
    public async Task RequestExactlyAtSignInInterval_RecordsAnotherSignIn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var timed = CreateTimedFactory(time, CacheOff);
        using var client = CreateClientFor(timed, TestTokens.Create(TestUsers.User));
        await client.GetAsync(MePath, cancellationToken);

        time.Advance(SignInInterval);
        await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(2, (await ListAuditRowsAsync()).Count);
    }

    [Fact]
    public async Task RequestAfterSignInInterval_SignInIsRecordedByTheFirstAuthorizedRequestOnlyOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var timed = CreateTimedFactory(time, CacheOff);
        using var client = CreateClientFor(timed, TestTokens.Create(TestUsers.User));
        await client.GetAsync(MePath, cancellationToken);
        time.Advance(TimeSpan.FromHours(3));

        for (var request = 0; request < 5; request++)
        {
            await client.GetAsync(MePath, cancellationToken);
        }

        Assert.Equal(2, (await ListAuditRowsAsync()).Count);
    }

    [Fact]
    public async Task ApiRequest_EmailChangedForSameObjectId_UpdatesSameRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var timed = CreateTimedFactory(time, CacheOff);
        using var first = CreateClientFor(timed, TestTokens.Create(TestUsers.User));
        await first.GetAsync(MePath, cancellationToken);
        var renamed = TestUsers.User with { Email = "renamed@sharpvault.test", DisplayName = "Renamed user" };
        using var second = CreateClientFor(timed, TestTokens.Create(renamed));

        time.Advance(TimeSpan.FromMinutes(61));
        var response = await second.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
        var row = await RequireUserRowAsync(TestUsers.User.ObjectId);
        Assert.Equal("renamed@sharpvault.test", row.GetString(UserColumns.Email));
        Assert.Equal("Renamed user", row.GetString(UserColumns.DisplayName));
    }

    [Fact]
    public async Task ApiRequest_EmailChangedWithinInterval_IsNotWrittenYet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);
        await Client.GetAsync(MePath, cancellationToken);
        var renamed = TestUsers.User with { Email = "renamed@sharpvault.test" };

        await Factory.CreateAuthenticatedClient(renamed).GetAsync(MePath, cancellationToken);

        var row = await RequireUserRowAsync(TestUsers.User.ObjectId);
        Assert.Equal(TestUsers.User.Email, row.GetString(UserColumns.Email));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task TwentyParallelFirstRequests_CreateOneUserRowAndOneSignInAudit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clients = Enumerable.Range(0, ParallelRequests).Select(_ => Factory.CreateAuthenticatedClient(TestUsers.User)).ToList();

        var responses = await Task.WhenAll(clients.Select(client => client.GetAsync(MePath, cancellationToken)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
        Assert.Single(await ListAuditRowsAsync());
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task TwentyParallelSignInDueRequests_RecordOneSignInAndKeepTheRowActive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, DateTimeOffset.UtcNow.AddHours(-2), cancellationToken);
        var before = await RequireUserRowAsync(TestUsers.User.ObjectId);
        var clients = Enumerable.Range(0, ParallelRequests).Select(_ => Factory.CreateAuthenticatedClient(TestUsers.User)).ToList();

        var responses = await Task.WhenAll(clients.Select(client => client.GetAsync(MePath, cancellationToken)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Single(await ListAuditRowsAsync());
        var after = await RequireUserRowAsync(TestUsers.User.ObjectId);
        Assert.NotEqual(before.ETag, after.ETag);
        Assert.Equal(UserRows.ActiveStatus, after.GetString(UserColumns.Status));
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ParallelFirstRequestsOfDifferentUsers_CreateOneRowAndOneAuditEachWithoutCrossTalk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var users = new[] { TestUsers.User, TestUsers.OtherUser, TestUsers.Admin };
        var calls = users.SelectMany(user => Enumerable.Range(0, 5).Select(_ => (user, Factory.CreateAuthenticatedClient(user)))).ToList();

        var responses = await Task.WhenAll(calls.Select(call => call.Item2.GetAsync(MePath, cancellationToken)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(users.Length, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
        var audit = await ListAuditRowsAsync();
        Assert.Equal(users.Length, audit.Count);
        Assert.Equal(
            users.Select(user => user.ObjectId.ToString("D")).Order(StringComparer.Ordinal),
            audit.Select(entry => entry.GetString("ActorId")).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SignInAudit_XForwardedForHeader_IsNotRecorded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, MePath);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Create(TestUsers.User));
        request.Headers.Add(XForwardedFor, SpoofedAddress);
        request.Headers.Add("X-Real-IP", SpoofedAddress);
        request.Headers.Add("Forwarded", $"for={SpoofedAddress}");

        var response = await CreateUnauthenticatedClient().SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(await ListAuditRowsAsync());
        Assert.NotEqual(SpoofedAddress, entry.GetString("IpAddress"));
        Assert.All(entry.Select(pair => pair.Value), value => Assert.DoesNotContain(SpoofedAddress, Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task GetMe_CacheDisabled_MakesExactlyOneUsersReadPerRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        SetAuthenticatedClient(TestUsers.User);
        StorageRequests.Clear();

        for (var request = 0; request < 5; request++)
        {
            var response = await Client.GetAsync(MePath, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(5, StorageRequests.Count);
        Assert.All(StorageRequests.Requests, request =>
        {
            Assert.Equal("GET", request.Method);
            Assert.False(request.IsEntityQuery);
            Assert.Contains("Users(", request.Path, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task GetMe_ExistingUserSignInNotDue_WritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, DateTimeOffset.UtcNow.AddMinutes(-5), cancellationToken);
        var before = await RequireUserRowAsync(TestUsers.User.ObjectId);
        SetAuthenticatedClient(TestUsers.User);
        StorageRequests.Clear();

        await Client.GetAsync(MePath, cancellationToken);

        Assert.Empty(WriteRequests());
        Assert.Equal(before.ETag, (await RequireUserRowAsync(TestUsers.User.ObjectId)).ETag);
        Assert.Empty(await ListAuditRowsAsync());
    }

    [Fact]
    public async Task GetMe_ActiveRowWithDefaultLastSignIn_IsRecordedAsSignInDue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await InsertRawUserRowAsync(TestUsers.User.ObjectId, new Dictionary<string, object?>
        {
            [UserColumns.Status] = UserRows.ActiveStatus,
        });
        SetAuthenticatedClient(TestUsers.User);

        var response = await Client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await RequireUserRowAsync(TestUsers.User.ObjectId);
        Assert.True(row.GetDateTimeOffset(UserColumns.LastSignInAt) > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Single(await ListAuditRowsAsync());
    }

    [Fact]
    public async Task ApiContract_AdminAndUser_AreProvisionedIndependently()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await Factory.CreateAuthenticatedClient(TestUsers.Admin).GetAsync(MePath, cancellationToken);
        await Factory.CreateAuthenticatedClient(TestUsers.User).GetAsync(MePath, cancellationToken);

        var admin = await RequireUserRowAsync(TestUsers.Admin.ObjectId);
        var user = await RequireUserRowAsync(TestUsers.User.ObjectId);
        Assert.Equal(TestUsers.Admin.DisplayName, admin.GetString(UserColumns.DisplayName));
        Assert.Equal(TestUsers.User.DisplayName, user.GetString(UserColumns.DisplayName));
        Assert.Equal(2, (await ListAuditRowsAsync()).Count);
    }

    [GeneratedRegex(@"^\d{19}_[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex AuditRowKey();
}
