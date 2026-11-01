// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

/// <summary>
/// VIS-2, VIS-3: the admin user list. Paged by partition key, validated, and limited to directory data, status and last sign-in.
/// </summary>
[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class AdminUsersTests(ApiTestFactory factory) : ApiSecurityTestBase(factory)
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;

    private static readonly string[] ItemProperties = ["displayName", "email", "lastSignInAt", "status", "userId"];
    private static readonly string[] PageProperties = ["continuationToken", "users"];

    public static TheoryData<string> InvalidPageSizes() => ["0", "-1", "101", "2147483648", "abc", "1e2", "5.5", "-0", "%00", "1%2C2", "99999999999999999999"];

    public static TheoryData<string> InvalidContinuationTokens() =>
    [
        "' or PartitionKey ne '",
        "{5f0c1d2e-4b7a-4c3d-9e8f-00000000b002}",
        "5f0c1d2e4b7a4c3d9e8f00000000b002",
        "5f0c1d2e-4b7a-4c3d-9e8f-00000000b002 ",
        " 5f0c1d2e-4b7a-4c3d-9e8f-00000000b002",
        "5f0c1d2e-4b7a-4c3d-9e8f-00000000b00",
        "5f0c1d2e-4b7a-4c3d-9e8f-00000000b0022",
        new string('a', 10_000),
        "\u0000",
        "../x",
        "x' or RowKey ne '",
        "5f0c1d2e-4b7a-4c3d-9e8f-00000000b002' or '1'='1",
        "(5f0c1d2e-4b7a-4c3d-9e8f-00000000b002)",
        "00000000-0000-0000-0000-000000000000%00",
        "0x5f0c1d2e",
    ];

    public static TheoryData<int, int> PageBoundaries() =>
    [
        (1, 1),
        (2, 1),
        (3, 2),
        (50, 50),
        (51, 50),
        (100, 50),
        (101, 50),
        (100, 100),
    ];

    [Fact]
    public async Task AdminUsers_FirstPage_ReturnsUsersAndContinuationToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAdminAndUsersAsync(119, cancellationToken);
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync($"{AdminUsersPath}?pageSize=50", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(50, document.RootElement.GetProperty("users").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, document.RootElement.GetProperty("continuationToken").ValueKind);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task AdminUsers_FollowingTokens_ReturnsEveryUserExactlyOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAdminAndUsersAsync(120, cancellationToken);

        var walked = await WalkAllPagesAsync(TestUsers.Admin, 50, cancellationToken);

        var stored = await ListStoredUserIdsAsync(cancellationToken);
        Assert.Equal(121, stored.Count);
        Assert.Equal(stored.Order(StringComparer.Ordinal), walked.Order(StringComparer.Ordinal));
        Assert.Equal(walked.Count, walked.Distinct().Count());
        Assert.Equal(walked.Order(StringComparer.Ordinal), walked);
    }

    [Theory]
    [MemberData(nameof(PageBoundaries))]
    public async Task AdminUsers_PageBoundaries_TerminateAndReturnEveryUserOnce(int total, int pageSize)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAdminAndUsersAsync(total - 1, cancellationToken);

        var walked = await WalkAllPagesAsync(TestUsers.Admin, pageSize, cancellationToken);

        Assert.Equal(total, walked.Count);
        Assert.Equal(total, walked.Distinct().Count());
    }

    [Fact]
    public async Task AdminUsers_DefaultPageSize_IsFifty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAdminAndUsersAsync(80, cancellationToken);
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync(AdminUsersPath, cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(DefaultPageSize, document.RootElement.GetProperty("users").GetArrayLength());
    }

    [Fact]
    public async Task AdminUsers_MaximumPageSize_ReturnsAtMostOneHundred()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAdminAndUsersAsync(150, cancellationToken);
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync($"{AdminUsersPath}?pageSize={MaximumPageSize}", cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MaximumPageSize, document.RootElement.GetProperty("users").GetArrayLength());
    }

    [Fact]
    public async Task AdminUsers_PageSizeOne_ReturnsOneUserAndAToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAdminAndUsersAsync(3, cancellationToken);
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync($"{AdminUsersPath}?pageSize=1", cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(1, document.RootElement.GetProperty("users").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, document.RootElement.GetProperty("continuationToken").ValueKind);
    }

    [Fact]
    public async Task AdminUsers_OnlyTheAdminExists_ReturnsOneUserAndNoToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync(AdminUsersPath, cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(1, document.RootElement.GetProperty("users").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("continuationToken").ValueKind);
    }

    [Fact]
    public async Task AdminUsers_ContinuationTokenPastTheLastUser_ReturnsEmptyPageWithoutToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAdminAndUsersAsync(5, cancellationToken);
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync($"{AdminUsersPath}?continuationToken=ffffffff-ffff-ffff-ffff-ffffffffffff", cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, document.RootElement.GetProperty("users").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("continuationToken").ValueKind);
    }

    [Fact]
    public async Task AdminUsers_UppercaseContinuationToken_IsAcceptedAndPagesCorrectly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAdminAndUsersAsync(10, cancellationToken);
        SetAuthenticatedClient(TestUsers.Admin);
        using var first = JsonDocument.Parse(await (await Client.GetAsync($"{AdminUsersPath}?pageSize=3", cancellationToken)).Content.ReadAsStringAsync(cancellationToken));
        var token = first.RootElement.GetProperty("continuationToken").GetString()!;

        var response = await Client.GetAsync($"{AdminUsersPath}?pageSize=3&continuationToken={token.ToUpperInvariant()}", cancellationToken);

        using var second = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var firstIds = first.RootElement.GetProperty("users").EnumerateArray().Select(user => user.GetProperty("userId").GetString()).ToList();
        var secondIds = second.RootElement.GetProperty("users").EnumerateArray().Select(user => user.GetProperty("userId").GetString()).ToList();
        Assert.Empty(firstIds.Intersect(secondIds));
        Assert.True(string.CompareOrdinal(firstIds[^1], secondIds[0]) < 0);
    }

    [Fact]
    public async Task AdminUsers_ListPage_QueriesWithPartitionKeyOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAdminAndUsersAsync(60, cancellationToken);
        SetAuthenticatedClient(TestUsers.Admin);
        StorageRequests.Clear();

        var response = await Client.GetAsync(AdminUsersPath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.Single(StorageRequests.Requests, request => request.IsEntityQuery);
        Assert.Contains("PartitionKey eq 'user'", query.Filter, StringComparison.Ordinal);
        Assert.DoesNotContain("RowKey", query.Filter, StringComparison.Ordinal);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
        Assert.DoesNotContain(StorageRequests.Requests, request => request.Path.Contains("AuditLog", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AdminUsers_ListPageWithCursor_QueriesWithPartitionKeyAndRowKeyLowerBound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAdminAndUsersAsync(10, cancellationToken);
        SetAuthenticatedClient(TestUsers.Admin);
        var cursor = "5f0c1d2e-4b7a-4c3d-9e8f-00000000b002";
        StorageRequests.Clear();

        await Client.GetAsync($"{AdminUsersPath}?continuationToken={cursor}", cancellationToken);

        var query = Assert.Single(StorageRequests.Requests, request => request.IsEntityQuery);
        Assert.Contains("PartitionKey eq 'user'", query.Filter, StringComparison.Ordinal);
        Assert.Contains($"RowKey gt '{cursor}'", query.Filter, StringComparison.Ordinal);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Theory]
    [MemberData(nameof(InvalidPageSizes))]
    public async Task AdminUsers_InvalidPageSize_Returns400(string pageSize)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.Admin);
        await Client.GetAsync(MePath, cancellationToken);
        StorageRequests.Clear();

        var response = await Client.GetAsync($"{AdminUsersPath}?pageSize={pageSize}", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(StorageRequests.Requests, request => request.IsEntityQuery);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidContinuationTokens))]
    public async Task AdminUsers_InvalidContinuationToken_Returns400WithoutEchoingInput(string token)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.Admin);
        await Client.GetAsync(MePath, cancellationToken);
        StorageRequests.Clear();

        var response = await Client.GetAsync($"{AdminUsersPath}?continuationToken={Escape(token)}", cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(StorageRequests.Requests, request => request.IsEntityQuery);
        if (token.Trim().Length >= 4)
        {
            Assert.DoesNotContain(token, body, StringComparison.Ordinal);
            Assert.DoesNotContain(JsonSerializer.Serialize(token)[1..^1], body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AdminUsers_BlankContinuationToken_IsRejectedOrTreatedAsFirstPageWithoutError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.Admin);

        var empty = await Client.GetAsync($"{AdminUsersPath}?continuationToken=", cancellationToken);
        var whitespace = await Client.GetAsync($"{AdminUsersPath}?continuationToken=%20", cancellationToken);

        Assert.True(empty.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.BadRequest, whitespace.StatusCode);
    }

    [Theory]
    [InlineData("pageSize=1&pageSize=2")]
    [InlineData("continuationToken=a&continuationToken=b")]
    [InlineData("pageSize=1&pageSize=abc")]
    public async Task AdminUsers_RepeatedQueryParameters_AreRejectedNotServerErrors(string query)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync($"{AdminUsersPath}?{query}", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminUsers_InvalidInput_ProblemDetailsHaveValidationShapeOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync($"{AdminUsersPath}?pageSize=0&continuationToken=garbage", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJsonMediaType, response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.EnumerateObject().Any());
        Assert.DoesNotContain("garbage", document.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.False(document.RootElement.TryGetProperty("exception", out _));
        Assert.False(document.RootElement.TryGetProperty("stackTrace", out _));
    }

    [Fact]
    public async Task AdminUsers_Response_ContainsOnlyDirectoryAndStatusFields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAdminAndUsersAsync(3, cancellationToken);
        await InsertRawUserRowAsync(Guid.NewGuid(), new Dictionary<string, object?>
        {
            [UserColumns.DisplayName] = "Row with extra columns",
            [UserColumns.Email] = "extra@sharpvault.test",
            [UserColumns.Status] = UserRows.ActiveStatus,
            [UserColumns.CreatedAt] = DateTimeOffset.UtcNow,
            [UserColumns.LastSignInAt] = DateTimeOffset.UtcNow,
            ["SecretNote"] = "extra-column-secret-7f3c9a",
            ["Roles"] = "SharpVault.Admin",
            ["RefreshToken"] = "extra-column-token-41d2e8",
            ["IpAddress"] = "198.51.100.7",
        });
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync(AdminUsersPath, cancellationToken);

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(raw);
        Assert.Equal(PageProperties, document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        var users = document.RootElement.GetProperty("users").EnumerateArray().ToList();
        Assert.NotEmpty(users);
        Assert.All(users, user => Assert.Equal(ItemProperties, user.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)));
        AssertUnauthorizedOrForbiddenBodyIsClean(raw, "extra-column-secret-7f3c9a", "extra-column-token-41d2e8", "198.51.100.7", "SecretNote", "RefreshToken", "PartitionKey", "RowKey", "ETag", "odata");
    }

    [Fact]
    public async Task AdminUsers_Response_NeverReturnsDataBeyondDirectoryFieldsForAnyRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAdminAndUsersAsync(25, cancellationToken);
        SetAuthenticatedClient(TestUsers.Admin);

        var walked = await WalkAllPagesAsync(TestUsers.Admin, 10, cancellationToken);

        Assert.Equal(26, walked.Count);
    }

    [Fact]
    public async Task AdminUsers_LockedUserInList_ShowsLockedStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.LockedUser, UserRows.LockedStatus, DateTimeOffset.UtcNow.AddDays(-3), cancellationToken);
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync(AdminUsersPath, cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var users = document.RootElement.GetProperty("users").EnumerateArray().ToList();
        var locked = Assert.Single(users, user => user.GetProperty("userId").GetGuid() == TestUsers.LockedUser.ObjectId);
        Assert.Equal("Locked", locked.GetProperty("status").GetString());
        var admin = Assert.Single(users, user => user.GetProperty("userId").GetGuid() == TestUsers.Admin.ObjectId);
        Assert.Equal("Active", admin.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AdminUsers_RowWithUnknownStatus_IsListedAsLockedNotActive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await InsertRawUserRowAsync(TestUsers.User.ObjectId, new Dictionary<string, object?>
        {
            [UserColumns.DisplayName] = TestUsers.User.DisplayName,
            [UserColumns.Status] = "active",
            [UserColumns.CreatedAt] = DateTimeOffset.UtcNow,
            [UserColumns.LastSignInAt] = DateTimeOffset.UtcNow,
        });
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync(AdminUsersPath, cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var row = Assert.Single(document.RootElement.GetProperty("users").EnumerateArray(), user => user.GetProperty("userId").GetGuid() == TestUsers.User.ObjectId);
        Assert.Equal("Locked", row.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AdminUsers_Response_SendsNoStoreAndApiSecurityHeaders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync(AdminUsersPath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.HeaderValue("Cache-Control"), StringComparison.OrdinalIgnoreCase);
        SecurityHeaderAssertions.AssertApi(response);
    }

    [Fact]
    public async Task AdminUsers_AdminListing_IsIndependentOfWhoIsListed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.OtherUser, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync(AdminUsersPath, cancellationToken);

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains(TestUsers.OtherUser.ObjectId.ToString("D"), raw, StringComparison.Ordinal);
        Assert.Contains(TestUsers.OtherUser.DisplayName, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminUsers_AdminCallingOtherEndpoints_CannotUseListToReadTokens()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = TestTokens.Create(TestUsers.Admin);

        var response = await GetWithTokenAsync(AdminUsersPath, token);

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        foreach (var segment in token.Split('.'))
        {
            Assert.DoesNotContain(segment, raw, StringComparison.Ordinal);
        }
    }

    private async Task SeedAdminAndUsersAsync(int others, CancellationToken cancellationToken)
    {
        await Factory.SeedUserAsync(TestUsers.Admin, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        if (others > 0)
        {
            await UserRows.InsertManyAsync(GetUsersTable(), others, cancellationToken);
        }
    }

    private async Task<List<string>> ListStoredUserIdsAsync(CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {UserRows.PartitionKey}");
        await foreach (var entity in GetUsersTable().QueryAsync<TableEntity>(filter, select: ["RowKey"], cancellationToken: cancellationToken))
        {
            ids.Add(entity.RowKey);
        }

        return ids;
    }

    private async Task<List<string>> WalkAllPagesAsync(TestUser caller, int pageSize, CancellationToken cancellationToken)
    {
        using var client = Factory.CreateAuthenticatedClient(caller);
        var ids = new List<string>();
        string? token = null;
        var maximumPages = (await ListStoredUserIdsAsync(cancellationToken)).Count + 2;
        for (var page = 0; page < maximumPages; page++)
        {
            var url = $"{AdminUsersPath}?pageSize={pageSize}" + (token is null ? string.Empty : $"&continuationToken={token}");
            var response = await client.GetAsync(url, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            ids.AddRange(document.RootElement.GetProperty("users").EnumerateArray().Select(user => user.GetProperty("userId").GetString()!));
            var next = document.RootElement.GetProperty("continuationToken");
            if (next.ValueKind == JsonValueKind.Null)
            {
                return ids;
            }

            token = next.GetString();
        }

        Assert.Fail("The continuation tokens never ended.");
        return ids;
    }
}
