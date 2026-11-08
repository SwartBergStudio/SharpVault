// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Authentication.Local;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class LocalAuthorizationMatrixTests(LocalApiTestFactory factory) : IAsyncLifetime
{
    private const string MePath = "/api/me";
    private const string AdminUsersPath = "/api/admin/users";
    private static readonly HttpStatusCode Ok = HttpStatusCode.OK;
    private static readonly HttpStatusCode Unauthorized = HttpStatusCode.Unauthorized;
    private static readonly HttpStatusCode Forbidden = HttpStatusCode.Forbidden;

    public static TheoryData<string, string, HttpStatusCode> Matrix()
    {
        var rows = new (string Caller, HttpStatusCode Me, HttpStatusCode Admin)[]
        {
            ("Anonymous", Unauthorized, Unauthorized),
            ("UnknownKey", Unauthorized, Unauthorized),
            ("UserOne", Ok, Forbidden),
            ("LockedUser", Forbidden, Forbidden),
            ("AdminOne", Ok, Ok),
        };

        var data = new TheoryData<string, string, HttpStatusCode>();
        foreach (var row in rows)
        {
            data.Add(row.Caller, MePath, row.Me);
            data.Add(row.Caller, AdminUsersPath, row.Admin);
        }

        return data;
    }

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAndSeedAsync(CancellationToken.None);
        factory.StorageRequests.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Assert.Empty(factory.StorageRequests.QueriesWithoutPartitionKey());
        return ValueTask.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task AuthorizationMatrix_LocalCallers_ReturnExpectedStatus(string caller, string path, HttpStatusCode expected)
    {
        if (caller == "LockedUser")
        {
            await Lock(LocalAccounts.UserOne.Id, UserRows.LockedStatus);
        }

        using var client = CreateClient(caller);

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task HealthEndpoints_Anonymous_Return200(string path)
    {
        using var client = factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(Ok, response.StatusCode);
    }

    [Fact]
    public async Task AdminList_AdminOne_ContainsDirectoryFieldsOnly()
    {
        using var client = factory.CreateClientForAccount(LocalAccounts.AdminOne);

        var body = await (await client.GetAsync(AdminUsersPath, TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var document = System.Text.Json.JsonDocument.Parse(body);
        foreach (var user in document.RootElement.GetProperty("users").EnumerateArray())
        {
            var names = user.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal);
            Assert.Equal(["displayName", "email", "lastSignInAt", "status", "userId"], names);
        }
    }

    private async Task Lock(Guid id, string status)
    {
        var table = factory.StorageAdminClient.GetTableClient(SharpVault.Infrastructure.Storage.TableNames.Users);
        var row = await UserRows.GetAsync(table, id, CancellationToken.None);
        row![UserColumns.Status] = status;
        await table.UpdateEntityAsync(row, row.ETag, Azure.Data.Tables.TableUpdateMode.Replace, CancellationToken.None);
    }

    private HttpClient CreateClient(string caller)
    {
        return caller switch
        {
            "Anonymous" => factory.CreateUnauthenticatedClient(),
            "UnknownKey" => factory.CreateClientWithToken(ForgedTokens.SignedWithUnknownKey()),
            "UserOne" or "LockedUser" => factory.CreateClientForAccount(LocalAccounts.UserOne),
            "AdminOne" => factory.CreateClientForAccount(LocalAccounts.AdminOne),
            _ => throw new ArgumentOutOfRangeException(nameof(caller), caller, null),
        };
    }
}
