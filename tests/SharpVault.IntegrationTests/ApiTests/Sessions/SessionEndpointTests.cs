// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Azure.Data.Tables;
using SharpVault.Dtos;
using SharpVault.Dtos.Users;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;

namespace SharpVault.IntegrationTests.ApiTests.Sessions;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class SessionEndpointTests(ApiTestFactory factory) : ApiSecurityTestBase(factory)
{
    private const string AnonymousCaller = "Anonymous";
    private const string OtherTenantCaller = "OtherTenant";
    private const string GuestCaller = "Guest";
    private const string LockedCaller = "Locked";
    private const string NoRoleCaller = "NoRole";
    private const string UserCaller = "User";
    private const string AdminCaller = "Admin";
    private const string PartitionDayFormat = "yyyyMMdd";
    private const string SessionRevocationsTable = "SessionRevocations";
    private const string JsonMediaType = "application/json";
    private const string MalformedBody = "{\"sessionId\": ";
    private const int SessionIdLength = 32;
    private const int TooLongSessionIdLength = 33;
    private const int TooShortSessionIdLength = 31;
    private const string InjectionSessionId = "' or PartitionKey ne '";
    private const string ControlCharacterSessionId = "\u0001";
    private const string TrailingNewlineSuffix = "\n";
    private const string PathSeparatorSuffix = "/";
    private const string PathCharacterSuffix = "#";
    private const string QueryCharacterSuffix = "?";
    private const string BackslashSuffix = "\\";
    private const int ValidSessionHours = 1;
    private const int TooFarAheadHours = 10;
    private const int ExpiredMinutes = -1;
    private const int BudgetMilliseconds = 200;
    private const int PointReadCount = 1;
    private const int WriteCount = 1;

    public static TheoryData<string, string, int> AuthorizationMatrix()
    {
        var data = new TheoryData<string, string, int>();
        foreach (var path in new[] { ApiRoutes.MeSessionCheck, ApiRoutes.MeSessionRevoke })
        {
            data.Add(path, AnonymousCaller, 401);
            data.Add(path, OtherTenantCaller, 401);
            data.Add(path, GuestCaller, 403);
            data.Add(path, LockedCaller, 403);
            data.Add(path, NoRoleCaller, 403);
            data.Add(path, UserCaller, 200);
            data.Add(path, AdminCaller, 200);
        }

        return data;
    }

    public static TheoryData<string> InvalidSessionIds() =>
        new()
        {
            string.Empty,
            " ",
            new string('a', TooShortSessionIdLength),
            new string('a', TooLongSessionIdLength),
            new string('A', SessionIdLength),
            new string('a', SessionIdLength) + TrailingNewlineSuffix,
            InjectionSessionId,
            new string('a', TooShortSessionIdLength) + PathSeparatorSuffix,
            new string('a', TooShortSessionIdLength) + PathCharacterSuffix,
            new string('a', TooShortSessionIdLength) + QueryCharacterSuffix,
            new string('a', TooShortSessionIdLength) + BackslashSuffix,
            new string('a', TooShortSessionIdLength) + ControlCharacterSessionId,
        };

    [Theory]
    [MemberData(nameof(AuthorizationMatrix))]
    public async Task SessionEndpoints_AuthorizationMatrix_ReturnsExpectedStatus(string path, string caller, int expected)
    {
        using var client = await ClientForCallerAsync(caller);

        var response = await client.PostAsJsonAsync(path, ValidBody(), TestContext.Current.CancellationToken);

        Assert.Equal(expected, (int)response.StatusCode);
    }

    [Fact]
    public async Task SessionCheck_FreshSession_ReportsActive()
    {
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);

        var status = await PostStatusAsync(client, ApiRoutes.MeSessionCheck, ValidBody());

        Assert.True(status.IsActive);
    }

    [Fact]
    public async Task SessionRevoke_ThenCheck_ReportsInactive()
    {
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        var body = ValidBody();

        await PostStatusAsync(client, ApiRoutes.MeSessionRevoke, body);
        var status = await PostStatusAsync(client, ApiRoutes.MeSessionCheck, body);

        Assert.False(status.IsActive);
    }

    [Fact]
    public async Task SessionRevoke_Twice_SucceedsWithOneRow()
    {
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        var body = ValidBody();

        var first = await client.PostAsJsonAsync(ApiRoutes.MeSessionRevoke, body, TestContext.Current.CancellationToken);
        var second = await client.PostAsJsonAsync(ApiRoutes.MeSessionRevoke, body, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, await RevocationRowsAsync(TestUsers.User.ObjectId, body.ExpiresAt));
    }

    [Fact]
    public async Task SessionCheck_ExpiredReference_ReportsInactiveWithNoStorageCall()
    {
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        var body = ExpiredBody();
        StorageRequests.Clear();

        var status = await PostStatusAsync(client, ApiRoutes.MeSessionCheck, body);

        Assert.False(status.IsActive);
        Assert.Empty(RevocationRequests());
    }

    [Fact]
    public async Task SessionRevoke_ExpiredReference_WritesNothing()
    {
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        var body = ExpiredBody();
        StorageRequests.Clear();

        var status = await PostStatusAsync(client, ApiRoutes.MeSessionRevoke, body);

        Assert.False(status.IsActive);
        Assert.DoesNotContain(RevocationRequests(), request => request.Method != "GET");
    }

    [Fact]
    public async Task SessionRevoke_OtherUsersSessionId_DoesNotAffectThatUser()
    {
        var body = ValidBody();
        using var revoker = Factory.CreateAuthenticatedClient(TestUsers.User);
        using var other = Factory.CreateAuthenticatedClient(TestUsers.OtherUser);

        await PostStatusAsync(revoker, ApiRoutes.MeSessionRevoke, body);
        var status = await PostStatusAsync(other, ApiRoutes.MeSessionCheck, body);

        Assert.True(status.IsActive);
    }

    [Fact]
    public async Task SessionCheck_OtherUsersRevokedSession_ReportsActiveForTheCaller()
    {
        var body = ValidBody();
        using var other = Factory.CreateAuthenticatedClient(TestUsers.OtherUser);
        using var user = Factory.CreateAuthenticatedClient(TestUsers.User);

        await PostStatusAsync(other, ApiRoutes.MeSessionRevoke, body);
        var status = await PostStatusAsync(user, ApiRoutes.MeSessionCheck, body);

        Assert.True(status.IsActive);
    }

    [Theory]
    [MemberData(nameof(InvalidSessionIds))]
    public async Task SessionEndpoints_InvalidSessionId_Returns400(string sessionId)
    {
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        StorageRequests.Clear();
        var body = new SessionReferenceRequestDto { SessionId = sessionId, ExpiresAt = DateTimeOffset.UtcNow.AddHours(ValidSessionHours) };

        var response = await client.PostAsJsonAsync(ApiRoutes.MeSessionRevoke, body, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(RevocationRequests(), request => request.Method != "GET");
    }

    [Fact]
    public async Task SessionEndpoints_ExpiryTooFarAhead_Returns400()
    {
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        var body = new SessionReferenceRequestDto { SessionId = NewSessionId(), ExpiresAt = DateTimeOffset.UtcNow.AddHours(TooFarAheadHours) };

        var response = await client.PostAsJsonAsync(ApiRoutes.MeSessionCheck, body, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SessionEndpoints_InvalidBody_NoStorageWrite()
    {
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        StorageRequests.Clear();
        using var content = new StringContent(MalformedBody, Encoding.UTF8, JsonMediaType);

        var response = await client.PostAsync(ApiRoutes.MeSessionRevoke, content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(RevocationRequests(), request => request.Method != "GET");
    }

    [Fact]
    public async Task SessionEndpoints_RequestsAndFailures_LogNoSessionIdOrToken()
    {
        Logs.Clear();
        var token = TestTokens.Create(TestUsers.User);
        using var client = Factory.CreateClientWithToken(token);
        var body = ValidBody();

        await PostStatusAsync(client, ApiRoutes.MeSessionRevoke, body);
        await PostStatusAsync(client, ApiRoutes.MeSessionCheck, body);
        await client.PostAsJsonAsync(ApiRoutes.MeSessionCheck, new SessionReferenceRequestDto { SessionId = InjectionSessionId, ExpiresAt = body.ExpiresAt }, TestContext.Current.CancellationToken);

        Assert.False(Logs.ContainsText(body.SessionId));
        Assert.False(Logs.ContainsText(token));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task SessionCheck_Valid_OnePointReadUnder200Ms()
    {
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        var body = ValidBody();
        await PostStatusAsync(client, ApiRoutes.MeSessionCheck, body);
        StorageRequests.Clear();
        var stopwatch = Stopwatch.StartNew();

        await PostStatusAsync(client, ApiRoutes.MeSessionCheck, body);

        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < BudgetMilliseconds, $"The session check took {stopwatch.ElapsedMilliseconds} ms, budget {BudgetMilliseconds} ms.");
        Assert.Equal(PointReadCount, RevocationRequests().Count(request => request.Method == "GET"));
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task SessionRevoke_Valid_OneWriteUnder200Ms()
    {
        using var client = Factory.CreateAuthenticatedClient(TestUsers.User);
        await PostStatusAsync(client, ApiRoutes.MeSessionCheck, ValidBody());
        var body = ValidBody();
        StorageRequests.Clear();
        var stopwatch = Stopwatch.StartNew();

        await PostStatusAsync(client, ApiRoutes.MeSessionRevoke, body);

        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < BudgetMilliseconds, $"The session revoke took {stopwatch.ElapsedMilliseconds} ms, budget {BudgetMilliseconds} ms.");
        Assert.Equal(WriteCount, RevocationRequests().Count(request => request.Method != "GET"));
        AssertNoQueryWithoutPartitionKey();
    }

    private static SessionReferenceRequestDto ValidBody() => new()
    {
        SessionId = NewSessionId(),
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(ValidSessionHours),
    };

    private static SessionReferenceRequestDto ExpiredBody() => new()
    {
        SessionId = NewSessionId(),
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(ExpiredMinutes),
    };

    private static string NewSessionId() => Guid.NewGuid().ToString("N");

    private async Task<HttpClient> ClientForCallerAsync(string caller)
    {
        switch (caller)
        {
            case AnonymousCaller:
                return Factory.CreateUnauthenticatedClient();
            case OtherTenantCaller:
                return Factory.CreateAuthenticatedClient(TestUsers.OtherTenantUser);
            case GuestCaller:
                return Factory.CreateAuthenticatedClient(TestUsers.Guest);
            case LockedCaller:
                return await LockedClientAsync();
            case NoRoleCaller:
                return Factory.CreateAuthenticatedClient(TestUsers.AuthenticatedWithoutRole);
            case AdminCaller:
                return Factory.CreateAuthenticatedClient(TestUsers.Admin);
            default:
                return Factory.CreateAuthenticatedClient(TestUsers.User);
        }
    }

    private async Task<HttpClient> LockedClientAsync()
    {
        using var warmUp = Factory.CreateAuthenticatedClient(TestUsers.LockedUser);
        var me = await warmUp.GetAsync(MePath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        await LockUserAsync(TestUsers.LockedUser.ObjectId);
        return Factory.CreateAuthenticatedClient(TestUsers.LockedUser);
    }

    private static async Task<SessionStatusDto> PostStatusAsync(HttpClient client, string path, SessionReferenceRequestDto body)
    {
        var response = await client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<SessionStatusDto>(TestContext.Current.CancellationToken);
        return Assert.IsType<SessionStatusDto>(status);
    }

    private IReadOnlyList<RecordedStorageRequest> RevocationRequests() =>
        [.. StorageRequests.Requests.Where(request => request.Path.Contains(SessionRevocationsTable, StringComparison.Ordinal))];

    private async Task<int> RevocationRowsAsync(Guid userId, DateTimeOffset expiresAt)
    {
        var table = GetTableClient(TableNames.SessionRevocations);
        var partition = expiresAt.UtcDateTime.ToString(PartitionDayFormat, System.Globalization.CultureInfo.InvariantCulture);
        var prefix = userId.ToString("N") + "_";
        var count = 0;
        await foreach (var row in table.QueryAsync<TableEntity>(TableClient.CreateQueryFilter($"PartitionKey eq {partition}"), select: ["RowKey"], cancellationToken: TestContext.Current.CancellationToken))
        {
            if (row.RowKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }
}
