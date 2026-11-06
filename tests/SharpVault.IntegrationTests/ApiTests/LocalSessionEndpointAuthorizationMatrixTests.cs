// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using Azure.Data.Tables;
using SharpVault.Authentication.Local;
using SharpVault.Dtos;
using SharpVault.Dtos.Users;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class LocalSessionEndpointAuthorizationMatrixTests(LocalApiTestFactory factory) : IAsyncLifetime
{
    private const string Anonymous = "Anonymous";
    private const string AlgNone = "AlgNone";
    private const string UserOne = "UserOne";
    private const string UserTwo = "UserTwo";
    private const string AdminOne = "AdminOne";
    private const string AdminTwo = "AdminTwo";
    private const string LockedUser = "LockedUser";
    private const int SessionHours = 1;

    public static TheoryData<string, string, HttpStatusCode> Matrix()
    {
        var data = new TheoryData<string, string, HttpStatusCode>();
        foreach (var path in new[] { ApiRoutes.MeSessionCheck, ApiRoutes.MeSessionRevoke })
        {
            data.Add(Anonymous, path, HttpStatusCode.Unauthorized);
            data.Add(AlgNone, path, HttpStatusCode.Unauthorized);
            data.Add(UserOne, path, HttpStatusCode.OK);
            data.Add(UserTwo, path, HttpStatusCode.OK);
            data.Add(AdminOne, path, HttpStatusCode.OK);
            data.Add(AdminTwo, path, HttpStatusCode.OK);
            data.Add(LockedUser, path, HttpStatusCode.Forbidden);
        }

        return data;
    }

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAndSeedAsync(TestContext.Current.CancellationToken);
        factory.StorageRequests.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Assert.Empty(factory.StorageRequests.QueriesWithoutPartitionKey());
        return ValueTask.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task LocalSessionEndpoints_AuthorizationMatrix_ReturnsExpectedStatus(string caller, string path, HttpStatusCode expected)
    {
        if (caller == LockedUser)
        {
            await SetLockedAsync(LocalAccounts.UserTwo.Id);
        }

        using var client = ClientFor(caller);
        var body = new SessionReferenceRequestDto
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(SessionHours),
        };

        var response = await client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    private HttpClient ClientFor(string caller) => caller switch
    {
        Anonymous => factory.CreateUnauthenticatedClient(),
        AlgNone => factory.CreateClientWithToken(LocalTokens.AlgNone(LocalAccounts.UserOne)),
        UserOne => factory.CreateClientForAccount(LocalAccounts.UserOne),
        UserTwo => factory.CreateClientForAccount(LocalAccounts.UserTwo),
        LockedUser => factory.CreateClientForAccount(LocalAccounts.UserTwo),
        AdminOne => factory.CreateClientForAccount(LocalAccounts.AdminOne),
        _ => factory.CreateClientForAccount(LocalAccounts.AdminTwo),
    };

    private async Task SetLockedAsync(Guid id)
    {
        var table = factory.StorageAdminClient.GetTableClient(TableNames.Users);
        var row = await UserRows.GetAsync(table, id, TestContext.Current.CancellationToken) ?? throw new InvalidOperationException("The user row is missing.");
        row[UserColumns.Status] = UserRows.LockedStatus;
        await table.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace, TestContext.Current.CancellationToken);
    }
}
