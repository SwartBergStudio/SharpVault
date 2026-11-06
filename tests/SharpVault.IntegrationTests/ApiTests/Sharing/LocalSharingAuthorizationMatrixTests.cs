// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Security.Cryptography;
using Azure.Data.Tables;
using Microsoft.IdentityModel.Tokens;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class LocalSharingAuthorizationMatrixTests(LocalApiTestFactory factory) : IAsyncLifetime
{
    private const string RequestName = "local-matrix-request-name";

    private static readonly SharePermissionDto[] Permissions = [SharePermissionDto.View, SharePermissionDto.Edit, SharePermissionDto.Manage];

    private static readonly string[] DeniedCallers =
    [
        "Anonymous", "AlgNone", "UnknownKey", "WrongTenant", "Guest", "NoRole", "LockedUser", "LockedAdmin",
    ];

    private static readonly string[] OtherCallers = ["Owner", "AdminOne", "AdminTwo"];

    private static readonly IReadOnlyList<LocalEndpoint> Endpoints =
    [
        new("E21", (api, w) => Response(api.ListSharedItemsAsync()), (r, p) => Ok),
        new("E22-S", (api, w) => Response(api.GetSharedSecretAsync(w.Owner, w.SecretS)), (r, p) => RecipientOnly(r, Ok)),
        new("E22-T", (api, w) => Response(api.GetSharedSecretAsync(w.Owner, w.SecretT)), (r, p) => RecipientOnly(r, Ok)),
        new("E23-S", (api, w) => Response(api.UpdateSharedSecretAsync(w.Owner, w.SecretS, w.SecretToken, UpdateBody())), (r, p) => RecipientAtLeast(r, p, SharePermissionDto.Edit, Ok)),
        new("E23-T", (api, w) => Response(api.UpdateSharedSecretAsync(w.Owner, w.SecretT, w.NestedToken, UpdateBody())), (r, p) => RecipientAtLeast(r, p, SharePermissionDto.Edit, Ok)),
        new("E24-S", (api, w) => Response(api.RevealSharedSecretAsync(w.Owner, w.SecretS, RevealBody())), (r, p) => RecipientOnly(r, Ok)),
        new("E24-T", (api, w) => Response(api.RevealSharedSecretAsync(w.Owner, w.SecretT, RevealBody())), (r, p) => RecipientOnly(r, Ok)),
        new("E25-F", (api, w) => Response(api.GetSharedFolderAsync(w.Owner, w.FolderF)), (r, p) => RecipientOnly(r, Ok)),
        new("E25-G", (api, w) => Response(api.GetSharedFolderAsync(w.Owner, w.FolderG)), (r, p) => RecipientOnly(r, Ok)),
        new("E26-S", (api, w) => api.FileSharedSecretAsync(w.Owner, w.SecretS, new FileSharedItemRequestDto { FolderId = null }), (r, p) => RecipientOnly(r, HttpStatusCode.NoContent)),
        new("E27-F", (api, w) => api.FileSharedFolderAsync(w.Owner, w.FolderF, new FileSharedItemRequestDto { FolderId = null }), (r, p) => RecipientOnly(r, HttpStatusCode.NoContent)),
        new("E28-S", (api, w) => Response(api.ListSecretGrantsAsync(w.Owner, w.SecretS)), (r, p) => OwnerOrManager(r, p, Ok)),
        new("E28-T", (api, w) => Response(api.ListSecretGrantsAsync(w.Owner, w.SecretT)), (r, p) => OwnerOrManager(r, p, Ok)),
        new("E29-S", (api, w) => Response(api.CreateSecretGrantAsync(w.Owner, w.SecretS, NewGrant())), (r, p) => OwnerOrManager(r, p, HttpStatusCode.Created)),
        new("E29-T", (api, w) => Response(api.CreateSecretGrantAsync(w.Owner, w.SecretT, NewGrant())), (r, p) => OwnerOrManager(r, p, HttpStatusCode.Created)),
        new("E30-S", (api, w) => Response(api.ChangeSecretGrantAsync(w.Owner, w.SecretS, LocalAccounts.UserTwo.Id, w.SecretGrantToken, ChangeBody(w.Permission))), (r, p) => OwnerOnly(r, Ok)),
        new("E31-S", (api, w) => api.RevokeSecretGrantAsync(w.Owner, w.SecretS, LocalAccounts.UserTwo.Id), (r, p) => OwnerOnly(r, HttpStatusCode.NoContent)),
        new("E32-F", (api, w) => Response(api.ListFolderGrantsAsync(w.Owner, w.FolderF)), (r, p) => OwnerOrManager(r, p, Ok)),
        new("E33-F", (api, w) => Response(api.CreateFolderGrantAsync(w.Owner, w.FolderF, NewGrant())), (r, p) => OwnerOrManager(r, p, HttpStatusCode.Created)),
        new("E33-G", (api, w) => Response(api.CreateFolderGrantAsync(w.Owner, w.FolderG, NewGrant())), (r, p) => OwnerOrManager(r, p, HttpStatusCode.Created)),
        new("E34-F", (api, w) => Response(api.ChangeFolderGrantAsync(w.Owner, w.FolderF, LocalAccounts.UserTwo.Id, w.FolderGrantToken, ChangeBody(w.Permission))), (r, p) => OwnerOnly(r, Ok)),
        new("E35-F", (api, w) => api.RevokeFolderGrantAsync(w.Owner, w.FolderF, LocalAccounts.UserTwo.Id), (r, p) => OwnerOnly(r, HttpStatusCode.NoContent)),
        new("E36", (api, w) => Response(api.SearchDirectoryAsync("two")), (r, p) => Ok),
    ];

    public static TheoryData<string> EndpointIds() => [.. Endpoints.Select(endpoint => endpoint.Id)];

    public static TheoryData<string, string, SharePermissionDto, HttpStatusCode> MatrixCases()
    {
        var cases = new TheoryData<string, string, SharePermissionDto, HttpStatusCode>();
        foreach (var endpoint in Endpoints)
        {
            foreach (var caller in DeniedCallers)
            {
                cases.Add(endpoint.Id, caller, SharePermissionDto.View, caller is "LockedUser" or "LockedAdmin" or "Guest" or "NoRole" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
            }

            foreach (var caller in OtherCallers)
            {
                cases.Add(endpoint.Id, caller, SharePermissionDto.View, endpoint.Expected((caller == "Owner" ? Role.Owner : Role.NoGrant), SharePermissionDto.View));
            }

            foreach (var permission in Permissions)
            {
                cases.Add(endpoint.Id, "Recipient", permission, endpoint.Expected(Role.Recipient, permission));
            }
        }

        return cases;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

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
    [MemberData(nameof(MatrixCases))]
    public async Task LocalSharingAuthorizationMatrix_EveryEndpointAndCaller_ReturnsExpectedStatus(string endpointId, string caller, SharePermissionDto permission, HttpStatusCode expected)
    {
        var world = await ArrangeAsync(permission);
        if (caller == "LockedUser")
        {
            await LockAsync(LocalAccounts.UserOne.Id);
        }

        if (caller == "LockedAdmin")
        {
            await LockAsync(LocalAccounts.AdminTwo.Id);
        }

        using var client = CreateClient(caller);

        var response = await Endpoints.Single(endpoint => endpoint.Id == endpointId).Send(new SharingTestApi(client), world);

        Assert.Equal(expected, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Token);
        if (!response.IsSuccessStatusCode)
        {
            world.Markers.AssertAbsentFrom(body);
            Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        }
        else if (!endpointId.StartsWith("E24", StringComparison.Ordinal))
        {
            Assert.DoesNotContain(world.Markers.Value, body, StringComparison.Ordinal);
            Assert.DoesNotContain(world.Markers.Notes, body, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(EndpointIds))]
    public async Task LocalSharingAuthorizationMatrix_DeniedCallers_MakeNoVaultOrInboxCalls(string endpointId)
    {
        var world = await ArrangeAsync(SharePermissionDto.Manage);
        await LockAsync(LocalAccounts.UserOne.Id);
        await LockAsync(LocalAccounts.AdminTwo.Id);
        var endpoint = Endpoints.Single(candidate => candidate.Id == endpointId);
        factory.StorageRequests.Clear();

        foreach (var caller in DeniedCallers.Concat(["Owner"]))
        {
            using var client = CreateClient(caller == "Owner" ? "LockedUser" : caller);
            var response = await endpoint.Send(new SharingTestApi(client), world);
            Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"Unexpected status {response.StatusCode}.");
        }

        Assert.Empty(factory.StorageRequests.VaultRequests());
        Assert.Empty(factory.StorageRequests.ShareInboxRequests());
    }

    [Theory]
    [MemberData(nameof(EndpointIds))]
    public async Task LocalSharingAuthorizationMatrix_AdminWithNoGrant_LeavesEveryRowUnchanged(string endpointId)
    {
        var world = await ArrangeAsync(SharePermissionDto.Manage);
        var endpoint = Endpoints.Single(candidate => candidate.Id == endpointId);
        var before = await SnapshotAsync();

        foreach (var caller in new[] { "AdminOne", "AdminTwo" })
        {
            using var client = CreateClient(caller);
            await endpoint.Send(new SharingTestApi(client), world);
        }

        var after = await SnapshotAsync();
        Assert.Equal(before, after);
    }

    private static HttpStatusCode Ok => HttpStatusCode.OK;

    private static HttpStatusCode RecipientOnly(Role role, HttpStatusCode success) => role == Role.Recipient ? success : HttpStatusCode.NotFound;

    private static HttpStatusCode OwnerOnly(Role role, HttpStatusCode success) => role == Role.Owner ? success : HttpStatusCode.NotFound;

    private static HttpStatusCode RecipientAtLeast(Role role, SharePermissionDto granted, SharePermissionDto required, HttpStatusCode success) =>
        role == Role.Recipient && granted >= required ? success : HttpStatusCode.NotFound;

    private static HttpStatusCode OwnerOrManager(Role role, SharePermissionDto granted, HttpStatusCode success) =>
        role == Role.Owner || (role == Role.Recipient && granted == SharePermissionDto.Manage) ? success : HttpStatusCode.NotFound;

    private static async Task<HttpResponseMessage> Response<T>(Task<(HttpResponseMessage Response, T? Body)> call) => (await call).Response;

    private static CreateShareGrantRequestDto NewGrant() => new() { RecipientId = LocalAccounts.AdminTwo.Id, Permission = SharePermissionDto.View };

    private static ChangeShareGrantRequestDto ChangeBody(SharePermissionDto current) =>
        new() { Permission = current == SharePermissionDto.View ? SharePermissionDto.Edit : SharePermissionDto.View };

    private static UpdateSecretRequestDto UpdateBody() => new() { Type = SecretTypeDto.Login, Name = RequestName };

    private static RevealSecretRequestDto RevealBody() => new() { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View };

    private static T Require<T>((HttpResponseMessage Response, T? Body) result)
        where T : class
    {
        Assert.True(result.Response.IsSuccessStatusCode, $"Arrangement failed with {result.Response.StatusCode}.");
        return result.Body ?? throw new InvalidOperationException("The arrangement response had no body.");
    }

    private async Task<LocalWorld> ArrangeAsync(SharePermissionDto permission)
    {
        using var ownerClient = factory.CreateClientForAccount(LocalAccounts.UserOne);
        var vault = new VaultTestApi(ownerClient);
        var sharing = new SharingTestApi(ownerClient);
        var markers = new PlaintextMarkers();
        var nested = new PlaintextMarkers();

        var folderF = Require(await vault.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "-F" }));
        var folderG = Require(await vault.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "-G", ParentId = folderF.Id }));
        var secretS = Require(await vault.CreateSecretAsync(SharingScenario.NewSecret(markers, null)));
        var secretT = Require(await vault.CreateSecretAsync(SharingScenario.NewSecret(nested, folderG.Id)));
        var secretGrant = Require(await sharing.CreateSecretGrantAsync(
            LocalAccounts.UserOne.Id,
            secretS.Id,
            new CreateShareGrantRequestDto { RecipientId = LocalAccounts.UserTwo.Id, Permission = permission }));
        var folderGrant = Require(await sharing.CreateFolderGrantAsync(
            LocalAccounts.UserOne.Id,
            folderF.Id,
            new CreateShareGrantRequestDto { RecipientId = LocalAccounts.UserTwo.Id, Permission = permission }));
        var current = Require(await vault.GetSecretAsync(secretS.Id));
        var nestedCurrent = Require(await vault.GetSecretAsync(secretT.Id));
        factory.StorageRequests.Clear();

        return new LocalWorld(
            LocalAccounts.UserOne.Id,
            markers,
            folderF.Id,
            folderG.Id,
            secretS.Id,
            secretT.Id,
            current.ConcurrencyToken,
            nestedCurrent.ConcurrencyToken,
            secretGrant.ConcurrencyToken,
            folderGrant.ConcurrencyToken,
            permission);
    }

    private async Task LockAsync(Guid id)
    {
        var table = factory.StorageAdminClient.GetTableClient(TableNames.Users);
        var row = await UserRows.GetAsync(table, id, Token);
        row![UserColumns.Status] = UserRows.LockedStatus;
        await table.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace, Token);
    }

    private async Task<List<string>> SnapshotAsync()
    {
        var rows = new List<string>();
        foreach (var table in new[] { TableNames.Vaults, TableNames.ShareInbox, TableNames.SecretAuditLog })
        {
            await foreach (var row in factory.StorageAdminClient.GetTableClient(table).QueryAsync<TableEntity>(cancellationToken: Token))
            {
                rows.Add($"{table}|{row.PartitionKey}|{row.RowKey}|{row.ETag}");
            }
        }

        return [.. rows.Order(StringComparer.Ordinal)];
    }

    private HttpClient CreateClient(string caller)
    {
        var wrongKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(LocalAuthenticationDefaults.KeyLength));
        return caller switch
        {
            "Anonymous" => factory.CreateUnauthenticatedClient(),
            "AlgNone" => factory.CreateClientWithToken(LocalTokens.AlgNone(LocalAccounts.AdminOne)),
            "UnknownKey" => factory.CreateClientWithToken(LocalTokens.Builder(LocalAccounts.AdminOne, wrongKey).Build()),
            "WrongTenant" => factory.CreateClientWithToken(Builder(LocalAccounts.UserOne).WithClaim(EntraClaimTypes.TenantId, Guid.NewGuid().ToString("D")).Build()),
            "Guest" => factory.CreateClientWithToken(Builder(LocalAccounts.UserOne).WithClaim(EntraClaimTypes.AccountType, EntraClaimTypes.GuestAccountType).Build()),
            "NoRole" => factory.CreateClientWithToken(Builder(LocalAccounts.UserOne).WithoutClaim(EntraClaimTypes.Roles).Build()),
            "Owner" or "LockedUser" => factory.CreateClientForAccount(LocalAccounts.UserOne),
            "Recipient" => factory.CreateClientForAccount(LocalAccounts.UserTwo),
            "AdminOne" => factory.CreateClientForAccount(LocalAccounts.AdminOne),
            "AdminTwo" or "LockedAdmin" => factory.CreateClientForAccount(LocalAccounts.AdminTwo),
            _ => throw new ArgumentOutOfRangeException(nameof(caller), caller, null),
        };
    }

    private TestTokenBuilder Builder(LocalAccount account) => LocalTokens.Builder(factory.Services, account);

    private enum Role
    {
        Owner,
        Recipient,
        NoGrant,
    }

    private sealed record LocalEndpoint(
        string Id,
        Func<SharingTestApi, LocalWorld, Task<HttpResponseMessage>> Send,
        Func<Role, SharePermissionDto, HttpStatusCode> Rule)
    {
        public HttpStatusCode Expected(Role role, SharePermissionDto permission) => Rule(role, permission);
    }

    private sealed record LocalWorld(
        Guid Owner,
        PlaintextMarkers Markers,
        Guid FolderF,
        Guid FolderG,
        Guid SecretS,
        Guid SecretT,
        string SecretToken,
        string NestedToken,
        string SecretGrantToken,
        string FolderGrantToken,
        SharePermissionDto Permission);
}
