// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using AutoFixture;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
public sealed class TestInfrastructureTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task ResetStorage_AfterTableCreated_RemovesTable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tableName = "Reset" + Guid.NewGuid().ToString("N")[..12];
        await Factory.StorageAdminClient.CreateTableIfNotExistsAsync(tableName, cancellationToken);

        await Factory.ResetStorageAsync(cancellationToken);

        var remaining = new List<string>();
        await foreach (var table in Factory.StorageAdminClient.QueryAsync(cancellationToken: cancellationToken))
        {
            remaining.Add(table.Name);
        }

        Assert.DoesNotContain(tableName, remaining);
    }

    [Fact]
    public async Task ResetStorage_RunTwice_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await Factory.ResetStorageAsync(cancellationToken);
        var exception = await Record.ExceptionAsync(() => Factory.ResetStorageAsync(cancellationToken));

        Assert.Null(exception);
    }

    [Fact]
    public async Task StorageRequestRecorder_QueryWithoutPartitionKey_IsReported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tableName = "Scan" + Guid.NewGuid().ToString("N")[..12];
        await Factory.StorageAdminClient.CreateTableIfNotExistsAsync(tableName, cancellationToken);
        var appClient = Factory.Services.GetRequiredService<TableServiceClient>();
        StorageRequests.Clear();

        await foreach (var entity in appClient.GetTableClient(tableName).QueryAsync<TableEntity>("RowKey eq 'x'", cancellationToken: cancellationToken))
        {
            Assert.NotNull(entity);
        }

        var scans = StorageRequests.QueriesWithoutPartitionKey();
        var scan = Assert.Single(scans);
        Assert.True(scan.IsEntityQuery);
        Assert.False(scan.HasPartitionKeyCondition);
        Assert.Contains(tableName, scan.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StorageRequestRecorder_QueryWithPartitionKey_IsNotReported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tableName = "Keyed" + Guid.NewGuid().ToString("N")[..12];
        var partition = Fixture.Create<Guid>().ToString();
        await Factory.StorageAdminClient.CreateTableIfNotExistsAsync(tableName, cancellationToken);
        var appClient = Factory.Services.GetRequiredService<TableServiceClient>();
        StorageRequests.Clear();

        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition}");
        await foreach (var entity in appClient.GetTableClient(tableName).QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken))
        {
            Assert.NotNull(entity);
        }

        var recorded = Assert.Single(StorageRequests.Requests);
        Assert.True(recorded.IsEntityQuery);
        Assert.True(recorded.HasPartitionKeyCondition);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task StorageRequestRecorder_FilterContainingPartitionKeyOnlyInValue_IsReported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tableName = "Tricky" + Guid.NewGuid().ToString("N")[..12];
        await Factory.StorageAdminClient.CreateTableIfNotExistsAsync(tableName, cancellationToken);
        var appClient = Factory.Services.GetRequiredService<TableServiceClient>();
        StorageRequests.Clear();

        await foreach (var entity in appClient.GetTableClient(tableName).QueryAsync<TableEntity>("PartitionKey ne 'a'", cancellationToken: cancellationToken))
        {
            Assert.NotNull(entity);
        }

        Assert.Single(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task StorageRequestRecorder_TableCreation_IsRecordedButNotAnEntityQuery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tableName = "Create" + Guid.NewGuid().ToString("N")[..12];
        var appClient = Factory.Services.GetRequiredService<TableServiceClient>();
        StorageRequests.Clear();

        await appClient.CreateTableIfNotExistsAsync(tableName, cancellationToken);

        var recorded = Assert.Single(StorageRequests.Requests);
        Assert.Equal("POST", recorded.Method);
        Assert.False(recorded.IsEntityQuery);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public void CapturingLoggerProvider_AppLogsMessage_IsCaptured()
    {
        var marker = "probe-" + Fixture.Create<Guid>();
        var logger = Factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("IntegrationProbe");

        ProbeLog.Information(logger, marker);

        Assert.True(Logs.ContainsText(marker));
    }

    [Fact]
    public void CapturingLoggerProvider_ExceptionTextLogged_IsSearchable()
    {
        var marker = "boom-" + Fixture.Create<Guid>();
        var logger = Factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("IntegrationProbe");

        ProbeLog.Failure(logger, new InvalidOperationException(marker));

        Assert.True(Logs.ContainsText(marker));
    }

    [Fact]
    public void CapturingLoggerProvider_Clear_RemovesEntries()
    {
        var marker = "cleared-" + Fixture.Create<Guid>();
        var logger = Factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("IntegrationProbe");
        ProbeLog.Information(logger, marker);

        Logs.Clear();

        Assert.False(Logs.ContainsText(marker));
    }

    [Fact]
    public async Task TestTokens_ValidUser_IsAcceptedByApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);

        var response = await Client.GetAsync("/api/me", cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public void TestTokens_DefaultClaims_MatchEntraV2AccessTokenShape()
    {
        var token = TestTokens.Create(TestUsers.Admin);

        using var payload = DecodePayload(token);
        var root = payload.RootElement;
        Assert.Equal("2.0", root.GetProperty("ver").GetString());
        Assert.Equal(TestApi.ClientId.ToString("D"), root.GetProperty("aud").GetString());
        Assert.Equal(TestIdentityProvider.Issuer(TestTenant.Id), root.GetProperty("iss").GetString());
        Assert.Equal(TestTenant.Id.ToString("D"), root.GetProperty("tid").GetString());
        Assert.Equal(TestUsers.Admin.ObjectId.ToString("D"), root.GetProperty("oid").GetString());
        Assert.Equal(TestApi.Scope, root.GetProperty("scp").GetString());
        Assert.Equal(0, root.GetProperty("acct").GetInt32());
        var roles = root.GetProperty("roles");
        Assert.Equal(System.Text.Json.JsonValueKind.Array, roles.ValueKind);
        Assert.Equal(["SharpVault.Admin"], roles.EnumerateArray().Select(role => role.GetString()).ToArray());
        Assert.True(root.GetProperty("nbf").GetInt64() < root.GetProperty("exp").GetInt64());
        Assert.False(root.TryGetProperty("idp", out _));
    }

    [Fact]
    public void TestTokens_UserWithoutRoles_OmitsRolesClaim()
    {
        var token = TestTokens.Create(TestUsers.AuthenticatedWithoutRole);

        using var payload = DecodePayload(token);
        Assert.False(payload.RootElement.TryGetProperty("roles", out _));
    }

    [Fact]
    public void TestTokens_Guest_HasAcctOneAndForeignIdp()
    {
        var token = TestTokens.Create(TestUsers.Guest);

        using var payload = DecodePayload(token);
        var root = payload.RootElement;
        Assert.Equal(1, root.GetProperty("acct").GetInt32());
        Assert.Equal($"https://sts.windows.net/{TestTenant.OtherId:D}/", root.GetProperty("idp").GetString());
    }

    [Fact]
    public void TestTokens_OtherTenantUser_HasOtherTenantIssuerAndTenantClaim()
    {
        var token = TestTokens.Create(TestUsers.OtherTenantUser);

        using var payload = DecodePayload(token);
        var root = payload.RootElement;
        Assert.Equal(TestIdentityProvider.Issuer(TestTenant.OtherId), root.GetProperty("iss").GetString());
        Assert.Equal(TestTenant.OtherId.ToString("D"), root.GetProperty("tid").GetString());
    }

    [Fact]
    public void TestTokens_ForObjectIdUnknown_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => TestTokens.ForObjectId(Guid.NewGuid()));
    }

    [Fact]
    public void TestSigningKeys_OtherWithSameKeyId_HasSameKeyIdAndDifferentKeyMaterial()
    {
        Assert.Equal(TestSigningKeys.Primary.KeyId, TestSigningKeys.OtherWithSameKeyId.KeyId);
        Assert.NotEqual(
            TestSigningKeys.Primary.Rsa.ExportParameters(false).Modulus,
            TestSigningKeys.OtherWithSameKeyId.Rsa.ExportParameters(false).Modulus);
    }

    [Fact]
    public void TestUsers_All_HaveUniqueObjectIds()
    {
        Assert.Equal(TestUsers.All.Count, TestUsers.All.Select(user => user.ObjectId).Distinct().Count());
    }

    [Fact]
    public async Task UserRows_SeededRow_IsReadByApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.User, UserRows.LockedStatus, DateTimeOffset.UtcNow, cancellationToken);
        SetAuthenticatedClient(TestUsers.User);

        var response = await Client.GetAsync("/api/me", cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task UserRows_SeededActiveRow_IsReadByApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        SetAuthenticatedClient(TestUsers.User);

        var response = await Client.GetAsync("/api/me", cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
        Assert.Empty(await AuditRows.ListAsync(GetAuditTable(), cancellationToken));
    }

    [Fact]
    public async Task SpoofedTestHeaders_Added_DoNotAuthenticate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        SpoofedTestHeaders.Add(request.Headers, TestUsers.Admin);

        var response = await Client.SendAsync(request, cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Fact]
    public async Task ArrangeCaller_LockedCaller_SeedsLockedRowsForBothLockedUsers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = await ArrangeCallerAsync(CallerKind.LockedUser);

        foreach (var locked in new[] { TestUsers.LockedUser, TestUsers.LockedAdmin })
        {
            var row = await UserRows.GetAsync(GetUsersTable(), locked.ObjectId, cancellationToken);
            Assert.NotNull(row);
            Assert.Equal(UserRows.LockedStatus, row.GetString(UserColumns.Status));
        }

        Assert.NotNull(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task ArrangeCaller_CalledTwice_Succeeds()
    {
        using var first = await ArrangeCallerAsync(CallerKind.LockedUser);

        var exception = await Record.ExceptionAsync(() => ArrangeCallerAsync(CallerKind.LockedAdmin));

        Assert.Null(exception);
    }

    [Fact]
    public void ForgedTokens_AlgNone_HasThreeSegmentsAndEmptySignature()
    {
        var token = ForgedTokens.AlgNone();

        var segments = token.Split('.');
        Assert.Equal(3, segments.Length);
        Assert.Equal(string.Empty, segments[2]);
    }

    [Fact]
    public void ForgedTokens_SignedWithUnknownKey_DiffersBetweenCalls()
    {
        Assert.NotEqual(ForgedTokens.SignedWithUnknownKey(), ForgedTokens.SignedWithUnknownKey());
    }

    private static System.Text.Json.JsonDocument DecodePayload(string token)
    {
        var segments = token.Split('.');
        Assert.Equal(3, segments.Length);
        return System.Text.Json.JsonDocument.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(segments[1]));
    }
}
