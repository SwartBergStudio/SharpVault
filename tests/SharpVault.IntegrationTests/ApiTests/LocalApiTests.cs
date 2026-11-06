// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;
using SharpVault.Dtos.Users;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class LocalApiTests(LocalApiTestFactory factory) : IAsyncLifetime
{
    private const string MePath = "/api/me";
    private const string AdminUsersPath = "/api/admin/users";
    private const string UnknownPath = "/does-not-exist";

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAndSeedAsync(CancellationToken.None);
        factory.StorageRequests.Clear();
        factory.Logs.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Assert.Empty(factory.StorageRequests.QueriesWithoutPartitionKey());
        return ValueTask.CompletedTask;
    }

    public static TheoryData<string, string> RejectedClaimChanges() => new()
    {
        { "iss", "https://other.invalid/" },
        { "aud", "api://other" },
        { "tid", "5a1d0000-0000-4000-8000-0000000000ff" },
        { "scp", "other_scope" },
        { "idtyp", "app" },
    };

    [Fact]
    public async Task Seeder_FreshStorage_InsertsExactlyTheFourAccountsWithEpochSignIn()
    {
        var rows = await ReadUsersAsync();

        Assert.Equal(LocalAccounts.All.Count, rows.Count);
        foreach (var account in LocalAccounts.All)
        {
            var row = Assert.Single(rows, entity => entity.RowKey == account.Id.ToString("D"));
            Assert.Equal(account.DisplayName, row.GetString(UserColumns.DisplayName));
            Assert.Equal(account.Email, row.GetString(UserColumns.Email));
            Assert.Equal(UserRows.ActiveStatus, row.GetString(UserColumns.Status));
            Assert.Equal(DateTimeOffset.UnixEpoch, row.GetDateTimeOffset(UserColumns.LastSignInAt));
        }
    }

    [Fact]
    public async Task Seeder_Run_MakesFourStorageCallsAllWithPartitionKeys()
    {
        await factory.ResetAndSeedAsync(TestContext.Current.CancellationToken);
        factory.StorageRequests.Clear();
        await DeleteUsersAsync();
        factory.StorageRequests.Clear();

        var stopwatch = Stopwatch.StartNew();
        await factory.RunSeederAsync(TestContext.Current.CancellationToken);
        stopwatch.Stop();

        var requests = factory.StorageRequests.Requests;
        Assert.Equal(4, requests.Count(request => request.Method == "POST"));
        Assert.DoesNotContain(requests, request => request.Method == "GET");
        Assert.DoesNotContain(requests, request => request.IsEntityQuery);
        Assert.Empty(factory.StorageRequests.QueriesWithoutPartitionKey());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Seeder_Rerun_MakesNoReadsOrReplacesAndKeepsEtags()
    {
        var before = await ReadUsersAsync();
        factory.StorageRequests.Clear();

        var stopwatch = Stopwatch.StartNew();
        await factory.RunSeederAsync(TestContext.Current.CancellationToken);
        stopwatch.Stop();

        var requests = factory.StorageRequests.Requests.ToList();
        var after = await ReadUsersAsync();
        Assert.Equal(4, requests.Count(request => request.Method == "POST"));
        Assert.DoesNotContain(requests, request => request.Method == "GET");
        Assert.DoesNotContain(requests, request => request.Method is "PUT" or "MERGE" or "PATCH");
        Assert.Equal(
            before.OrderBy(row => row.RowKey, StringComparer.Ordinal).Select(row => row.ETag.ToString()),
            after.OrderBy(row => row.RowKey, StringComparer.Ordinal).Select(row => row.ETag.ToString()));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Seeder_RerunAfterLock_DoesNotChangeStatusOrEtag()
    {
        await SetStatusAsync(LocalAccounts.UserOne.Id, UserRows.LockedStatus);
        var locked = await UserRows.GetAsync(UsersTable(), LocalAccounts.UserOne.Id, TestContext.Current.CancellationToken);

        await factory.RunSeederAsync(TestContext.Current.CancellationToken);

        var after = await UserRows.GetAsync(UsersTable(), LocalAccounts.UserOne.Id, TestContext.Current.CancellationToken);
        Assert.Equal(UserRows.LockedStatus, after!.GetString(UserColumns.Status));
        Assert.Equal(locked!.ETag, after.ETag);
    }

    [Fact]
    public async Task Seeder_AfterTheTableInitializer_NeverFailsOnMissingTables()
    {
        var warnings = factory.Logs.Entries.Where(entry => entry.Level >= LogLevel.Warning && entry.Category.Contains("LocalAccountSeeder", StringComparison.Ordinal));

        await factory.RunSeederAsync(TestContext.Current.CancellationToken);

        Assert.Empty(warnings);
        Assert.Equal(LocalAccounts.All.Count, (await ReadUsersAsync()).Count);
    }

    [Fact]
    public async Task Seeder_Entries_ContainNoCredentialsOrSecrets()
    {
        var rows = await ReadUsersAsync();

        var allowed = new[] { "PartitionKey", "RowKey", "Timestamp", "odata.etag", UserColumns.DisplayName, UserColumns.Email, UserColumns.Status, UserColumns.CreatedAt, UserColumns.LastSignInAt, UserColumns.IsAdmin, UserColumns.RoleObservedAt };
        foreach (var row in rows)
        {
            Assert.All(row.Keys, key => Assert.Contains(key, allowed));
        }
    }

    [Fact]
    public async Task AdminUsers_AdminOne_ListsAllFourSeededAccounts()
    {
        using var client = factory.CreateClientForAccount(LocalAccounts.AdminOne);

        var response = await client.GetAsync(AdminUsersPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var users = document.RootElement.GetProperty("users");
        Assert.Equal(LocalAccounts.All.Count, users.GetArrayLength());
        var ids = users.EnumerateArray().Select(user => user.GetProperty("userId").GetGuid()).ToHashSet();
        Assert.True(ids.SetEquals(LocalAccounts.All.Select(account => account.Id)));
    }

    [Fact]
    public async Task AdminUsers_UserOne_Returns403()
    {
        using var client = factory.CreateClientForAccount(LocalAccounts.UserOne);

        var response = await client.GetAsync(AdminUsersPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Me_UserOne_ReturnsUserNotAdmin()
    {
        var profile = await GetProfileAsync(LocalAccounts.UserOne);

        Assert.Equal(LocalAccounts.UserOne.Id, profile.UserId);
        Assert.Equal("User One", profile.DisplayName);
        Assert.False(profile.IsAdmin);
    }

    [Fact]
    public async Task Me_AdminOne_ReturnsAdmin()
    {
        var profile = await GetProfileAsync(LocalAccounts.AdminOne);

        Assert.Equal(LocalAccounts.AdminOne.Id, profile.UserId);
        Assert.True(profile.IsAdmin);
    }

    [Fact]
    public async Task Me_SeededUserFirstRequest_RecordsSignInAndWritesOneAuditEntry()
    {
        var auditBefore = (await AuditRows.ListAsync(AuditTable(), TestContext.Current.CancellationToken)).Count;

        await GetProfileAsync(LocalAccounts.UserTwo);

        var row = await UserRows.GetAsync(UsersTable(), LocalAccounts.UserTwo.Id, TestContext.Current.CancellationToken);
        Assert.True(row!.GetDateTimeOffset(UserColumns.LastSignInAt) > DateTimeOffset.UnixEpoch.AddYears(1));
        var auditAfter = (await AuditRows.ListAsync(AuditTable(), TestContext.Current.CancellationToken)).Count;
        Assert.Equal(auditBefore + 1, auditAfter);
    }

    [Fact]
    public async Task Me_SeededUserFirstRequest_CompletesUnder500MillisecondsWithThreeStorageCalls()
    {
        using var client = factory.CreateClientForAccount(LocalAccounts.UserOne);
        await factory.CreateUnauthenticatedClient().GetAsync("/health", TestContext.Current.CancellationToken);
        factory.StorageRequests.Clear();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync(MePath, TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, factory.StorageRequests.Count);
        Assert.Empty(factory.StorageRequests.QueriesWithoutPartitionKey());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task Me_SecondRequestWithinCacheWindow_MakesNoStorageCalls()
    {
        using var cached = factory.WithWebHostBuilder(builder => builder.UseSetting("UserAccess:CacheTtl", "00:00:30"));
        using var client = LocalApiTestFactory.CreateClientFor(cached, LocalTokens.For(cached.Services, LocalAccounts.UserOne));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MePath, TestContext.Current.CancellationToken)).StatusCode);
        factory.StorageRequests.Clear();

        var response = await client.GetAsync(MePath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, factory.StorageRequests.Count);
    }

    [Fact]
    public async Task LockedSeededUser_ValidToken_Returns403OnNextRequest()
    {
        using var client = factory.CreateClientForAccount(LocalAccounts.UserOne);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MePath, TestContext.Current.CancellationToken)).StatusCode);

        await SetStatusAsync(LocalAccounts.UserOne.Id, UserRows.LockedStatus);
        var response = await client.GetAsync(MePath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Token_ExpiredBeyondSkew_Returns401()
    {
        var token = LocalTokens.Builder(factory.Services, LocalAccounts.UserOne)
            .NotBefore(DateTimeOffset.UtcNow.AddMinutes(-10))
            .ExpiresAt(DateTimeOffset.UtcNow.AddMinutes(-2))
            .Build();

        await AssertUnauthorizedAsync(token);
    }

    [Fact]
    public async Task Token_SignedWithAnotherKey_Returns401()
    {
        var key = new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(LocalAuthenticationDefaults.KeyLength));
        var token = LocalTokens.Builder(LocalAccounts.UserOne, key).Build();

        await AssertUnauthorizedAsync(token);
    }

    [Fact]
    public async Task Token_AlgNone_Returns401()
    {
        await AssertUnauthorizedAsync(LocalTokens.AlgNone(LocalAccounts.AdminOne));
    }

    [Fact]
    public async Task Token_Rs256FromTheEntraTestKey_Returns401()
    {
        await AssertUnauthorizedAsync(TestTokens.Create(TestUsers.Admin));
    }

    [Theory]
    [MemberData(nameof(RejectedClaimChanges))]
    public async Task Token_WrongIssuer_WrongAudience_WrongTenant_MissingScope_AppOnly_Return401(string claim, string value)
    {
        var token = LocalTokens.Builder(factory.Services, LocalAccounts.UserOne).WithClaim(claim, value).Build();

        await AssertUnauthorizedAsync(token);
    }

    [Fact]
    public async Task Token_ForgedAdminRoleSignedByAttacker_Returns401()
    {
        var attackerKey = new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(LocalAuthenticationDefaults.KeyLength));
        var token = LocalTokens.Builder(LocalAccounts.AdminOne, attackerKey).Build();

        using var client = factory.CreateClientWithToken(token);
        var response = await client.GetAsync(AdminUsersPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Startup_Notice_LogsOneWarningAndNoKeyOrToken()
    {
        factory.Logs.Clear();

        using var restarted = factory.WithWebHostBuilder(_ => { });
        _ = restarted.Services;

        var notices = factory.Logs.Entries
            .Where(entry => entry.Category.Contains("LocalAuthenticationStartupNotice", StringComparison.Ordinal))
            .ToList();
        var notice = Assert.Single(notices);
        Assert.Equal(LogLevel.Warning, notice.Level);
        Assert.Contains("Local authentication is enabled", notice.Message, StringComparison.Ordinal);
        var key = Convert.ToBase64String(factory.Services.GetRequiredService<LocalSigningKey>().Key.Key);
        Assert.False(factory.Logs.ContainsText(key));
    }

    [Fact]
    public async Task Logs_AfterRequests_ContainNoTokenOrKeyMaterial()
    {
        var token = LocalTokens.For(factory.Services, LocalAccounts.AdminOne);
        using var client = factory.CreateClientWithToken(token);
        await client.GetAsync(MePath, TestContext.Current.CancellationToken);
        await client.GetAsync(AdminUsersPath, TestContext.Current.CancellationToken);
        await factory.CreateClientWithToken(ForgedTokens.SignedWithUnknownKey()).GetAsync(MePath, TestContext.Current.CancellationToken);

        var key = Convert.ToBase64String(factory.Services.GetRequiredService<LocalSigningKey>().Key.Key);
        Assert.False(factory.Logs.ContainsText(token));
        Assert.False(factory.Logs.ContainsText(token.Split('.')[2]));
        Assert.False(factory.Logs.ContainsText(key));
    }

    [Fact]
    public async Task Errors_Responses_ContainNoStackTracesOrKeyMaterial()
    {
        var key = Convert.ToBase64String(factory.Services.GetRequiredService<LocalSigningKey>().Key.Key);
        using var anonymous = factory.CreateUnauthenticatedClient();
        using var forged = factory.CreateClientWithToken(ForgedTokens.SignedWithUnknownKey());
        using var user = factory.CreateClientForAccount(LocalAccounts.UserOne);

        var bodies = new List<string>();
        foreach (var response in new[]
        {
            await anonymous.GetAsync(MePath, TestContext.Current.CancellationToken),
            await forged.GetAsync(MePath, TestContext.Current.CancellationToken),
            await user.GetAsync(AdminUsersPath, TestContext.Current.CancellationToken),
            await user.GetAsync(UnknownPath, TestContext.Current.CancellationToken),
        })
        {
            bodies.Add(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            bodies.Add(response.Headers.ToString());
        }

        Assert.All(bodies, body =>
        {
            Assert.DoesNotContain(" at SharpVault.", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
            Assert.DoesNotContain(key, body, StringComparison.Ordinal);
            Assert.DoesNotContain("sharpvault.invalid/local", body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Me_AdminAccounts_CarryBothRoles()
    {
        var one = await GetProfileAsync(LocalAccounts.AdminOne);
        var two = await GetProfileAsync(LocalAccounts.AdminTwo);

        Assert.True(one.IsAdmin);
        Assert.True(two.IsAdmin);
        Assert.Contains(AppRoles.User, LocalAccounts.AdminOne.Roles);
        Assert.Contains(AppRoles.Admin, LocalAccounts.AdminOne.Roles);
    }

    private async Task AssertUnauthorizedAsync(string token)
    {
        foreach (var path in new[] { MePath, AdminUsersPath, UnknownPath })
        {
            using var client = factory.CreateClientWithToken(token);
            var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    private async Task<CurrentUserDto> GetProfileAsync(LocalAccount account)
    {
        using var client = factory.CreateClientForAccount(account);
        var response = await client.GetAsync(MePath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<CurrentUserDto>(TestContext.Current.CancellationToken);
        return Assert.IsType<CurrentUserDto>(profile);
    }

    private TableClient UsersTable() => factory.StorageAdminClient.GetTableClient(TableNames.Users);

    private TableClient AuditTable() => factory.StorageAdminClient.GetTableClient(TableNames.AuditLog);

    private async Task<IReadOnlyList<TableEntity>> ReadUsersAsync()
    {
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {UserRows.PartitionKey}");
        var rows = new List<TableEntity>();
        await foreach (var entity in UsersTable().QueryAsync<TableEntity>(filter, cancellationToken: TestContext.Current.CancellationToken))
        {
            rows.Add(entity);
        }

        return rows;
    }

    private async Task DeleteUsersAsync()
    {
        foreach (var row in await ReadUsersAsync())
        {
            await UsersTable().DeleteEntityAsync(row.PartitionKey, row.RowKey, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private async Task SetStatusAsync(Guid id, string status)
    {
        var row = await UserRows.GetAsync(UsersTable(), id, TestContext.Current.CancellationToken);
        row![UserColumns.Status] = status;
        await UsersTable().UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace, TestContext.Current.CancellationToken);
    }
}
