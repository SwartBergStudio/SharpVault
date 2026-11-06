// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class LocalTransferAuthorizationMatrixTests(LocalApiTestFactory factory) : IAsyncLifetime
{
    private const string Preview = "Preview";
    private const string List = "List";
    private const string Get = "Get";
    private const string Approve = "Approve";
    private const string Reject = "Reject";
    private const string Abort = "Abort";
    private const string Create = "Create";
    private const string Lock = "Lock";
    private const string Unlock = "Unlock";
    private const string Dismiss = "Dismiss";
    private const string Notices = "Notices";
    private const string OwnShares = "OwnShares";

    private const string Anonymous = "Anonymous";
    private const string AlgNone = "AlgNone";
    private const string UnknownKey = "UnknownKey";
    private const string WrongTenant = "WrongTenant";
    private const string EntraRs256 = "EntraRs256";
    private const string Expired = "Expired";
    private const string ForgedAdmin = "ForgedAdmin";
    private const string Guest = "Guest";
    private const string NoRole = "NoRole";
    private const string UserOne = "UserOne";
    private const string LockedUser = "LockedUser";
    private const string AdminOne = "AdminOne";
    private const string AdminTwo = "AdminTwo";
    private const string LockedAdmin = "LockedAdmin";

    private const string ValidReason = "Role change, the vault moves to the new owner";
    private const string LockReason = "Left the company, locked for the handover";
    private const string DeclineReason = "Declined for the access matrix test";

    private static readonly string[] CallerOrder =
    [
        Anonymous, AlgNone, UnknownKey, WrongTenant, EntraRs256, Expired, ForgedAdmin,
        Guest, NoRole, UserOne, LockedUser, AdminOne, AdminTwo, LockedAdmin,
    ];

    private static readonly Dictionary<string, int[]> ExpectedStatuses = new(StringComparer.Ordinal)
    {
        [Preview] = [401, 401, 401, 401, 401, 401, 401, 403, 403, 403, 403, 200, 200, 403],
        [List] = [401, 401, 401, 401, 401, 401, 401, 403, 403, 403, 403, 200, 200, 403],
        [Get] = [401, 401, 401, 401, 401, 401, 401, 403, 403, 403, 403, 200, 200, 403],
        [Approve] = [401, 401, 401, 401, 401, 401, 401, 403, 403, 403, 403, 403, 200, 403],
        [Reject] = [401, 401, 401, 401, 401, 401, 401, 403, 403, 403, 403, 200, 200, 403],
        [Abort] = [401, 401, 401, 401, 401, 401, 401, 403, 403, 403, 403, 409, 409, 403],
        [Create] = [401, 401, 401, 401, 401, 401, 401, 403, 403, 403, 403, 400, 400, 403],
        [Lock] = [401, 401, 401, 401, 401, 401, 401, 403, 403, 403, 403, 200, 200, 403],
        [Unlock] = [401, 401, 401, 401, 401, 401, 401, 403, 403, 403, 403, 400, 400, 403],
        [Dismiss] = [401, 401, 401, 401, 401, 401, 401, 403, 403, 404, 403, 404, 404, 403],
        [Notices] = [401, 401, 401, 401, 401, 401, 401, 403, 403, 200, 403, 200, 200, 403],
        [OwnShares] = [401, 401, 401, 401, 401, 401, 401, 403, 403, 200, 403, 200, 200, 403],
    };

    public static TheoryData<string, string, int> Matrix()
    {
        var data = new TheoryData<string, string, int>();
        foreach (var (endpoint, statuses) in ExpectedStatuses)
        {
            for (var index = 0; index < CallerOrder.Length; index++)
            {
                data.Add(endpoint, CallerOrder[index], statuses[index]);
            }
        }

        return data;
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
    [MemberData(nameof(Matrix))]
    public async Task LocalTransferAuthorizationMatrix_EveryEndpointAndCaller_ReturnsTheExpectedStatus(string endpoint, string caller, int expected)
    {
        var transfer = await RequestTransferAsync();
        await ApplyCallerStateAsync(caller);

        var response = await SendAsync(endpoint, caller, transfer);

        Assert.Equal(expected, (int)response.StatusCode);
        Assert.DoesNotContain("SV-", await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_ChainedWithAnOpenTransfer_Returns400WithTheChainMessage()
    {
        await RequestTransferAsync();

        var response = await ApiFor(factory.CreateClientForAccount(LocalAccounts.AdminOne)).SendRawAsync(
            HttpMethod.Post,
            ApiRoutes.AdminTransfers,
            CreateBody(LocalAccounts.UserTwo, LocalAccounts.UserOne),
            null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TransferMessages.TransferChainNotAllowed, await ReadDetailAsync(response));
    }

    private async Task<TransferDto> RequestTransferAsync()
    {
        var body = new CreateTransferRequestDto
        {
            SourceUserId = LocalAccounts.UserOne.Id,
            TargetUserId = LocalAccounts.UserTwo.Id,
            Scope = TransferScopeKindDto.AllItems,
            Category = ReasonCategoryDto.RoleChange,
            Reason = ValidReason,
        };
        var (response, transfer) = await ApiFor(factory.CreateClientForAccount(LocalAccounts.AdminOne)).SendAsync<TransferDto>(HttpMethod.Post, ApiRoutes.AdminTransfers, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return transfer ?? throw new InvalidOperationException("The create response had no body.");
    }

    private async Task ApplyCallerStateAsync(string caller)
    {
        if (caller == LockedUser)
        {
            await SetStatusAsync(LocalAccounts.UserOne.Id, UserRows.LockedStatus);
        }

        if (caller == LockedAdmin)
        {
            await SetStatusAsync(LocalAccounts.AdminTwo.Id, UserRows.LockedStatus);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string endpoint, string caller, TransferDto transfer)
    {
        var api = ApiFor(ClientForCaller(caller));
        return endpoint switch
        {
            Preview => await api.SendRawAsync(HttpMethod.Get, $"{ApiRoutes.AdminTransferPreview}?sourceUserId={LocalAccounts.UserOne.Id:D}", null, null),
            List => await api.SendRawAsync(HttpMethod.Get, ApiRoutes.AdminTransfers, null, null),
            Get => await api.SendRawAsync(HttpMethod.Get, VaultTestApi.Bind(ApiRoutes.AdminTransfer, ("transferId", transfer.Id)), null, null),
            Approve => await api.SendRawAsync(HttpMethod.Post, VaultTestApi.Bind(ApiRoutes.AdminTransferApprove, ("transferId", transfer.Id)), null, transfer.ConcurrencyToken),
            Reject => await api.SendRawAsync(HttpMethod.Post, VaultTestApi.Bind(ApiRoutes.AdminTransferReject, ("transferId", transfer.Id)), DecisionBody(DeclineReason), transfer.ConcurrencyToken),
            Abort => await api.SendRawAsync(HttpMethod.Post, VaultTestApi.Bind(ApiRoutes.AdminTransferAbort, ("transferId", transfer.Id)), DecisionBody(DeclineReason), transfer.ConcurrencyToken),
            Create => await api.SendRawAsync(HttpMethod.Post, ApiRoutes.AdminTransfers, CreateBody(LocalAccounts.UserTwo, LocalAccounts.UserOne), null),
            Lock => await api.SendRawAsync(HttpMethod.Post, VaultTestApi.Bind(ApiRoutes.AdminUserLock, ("userId", LocalAccounts.UserTwo.Id)), LockBody(), null),
            Unlock => await api.SendRawAsync(HttpMethod.Post, VaultTestApi.Bind(ApiRoutes.AdminUserUnlock, ("userId", LocalAccounts.UserTwo.Id)), LockBody(), null),
            Dismiss => await api.SendRawAsync(HttpMethod.Delete, VaultTestApi.Bind(ApiRoutes.TransferNotice, ("transferId", transfer.Id)), null, null),
            Notices => await api.SendRawAsync(HttpMethod.Get, ApiRoutes.TransferNotices, null, null),
            OwnShares => await api.SendRawAsync(HttpMethod.Get, ApiRoutes.TransferOwnedShares, null, null),
            _ => throw new InvalidOperationException("Unknown endpoint: " + endpoint),
        };
    }

    private static string DecisionBody(string reason) => $"{{\"reason\":\"{reason}\"}}";

    private static string LockBody() => $"{{\"category\":\"{nameof(ReasonCategoryDto.LeftCompany)}\",\"reason\":\"{LockReason}\"}}";

    private static string CreateBody(LocalAccount source, LocalAccount target) =>
        $"{{\"sourceUserId\":\"{source.Id:D}\",\"targetUserId\":\"{target.Id:D}\",\"scope\":\"{nameof(TransferScopeKindDto.AllItems)}\",\"category\":\"{nameof(ReasonCategoryDto.RoleChange)}\",\"reason\":\"{ValidReason}\"}}";

    private HttpClient ClientForCaller(string caller)
    {
        var wrongKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(LocalAuthenticationDefaults.KeyLength));
        return caller switch
        {
            Anonymous => factory.CreateUnauthenticatedClient(),
            AlgNone => factory.CreateClientWithToken(LocalTokens.AlgNone(LocalAccounts.AdminOne)),
            UnknownKey => factory.CreateClientWithToken(ForgedTokens.SignedWithUnknownKey()),
            WrongTenant => factory.CreateClientWithToken(Builder(LocalAccounts.AdminOne).WithClaim(EntraClaimTypes.TenantId, Guid.NewGuid().ToString("D")).Build()),
            EntraRs256 => factory.CreateClientWithToken(TestTokens.Create(TestUsers.Admin)),
            Expired => factory.CreateClientWithToken(Builder(LocalAccounts.AdminOne)
                .NotBefore(DateTimeOffset.UtcNow.AddMinutes(-10))
                .ExpiresAt(DateTimeOffset.UtcNow.AddMinutes(-2))
                .Build()),
            ForgedAdmin => factory.CreateClientWithToken(LocalTokens.Builder(LocalAccounts.AdminOne, wrongKey).Build()),
            Guest => factory.CreateClientWithToken(Builder(LocalAccounts.AdminOne).WithClaim(EntraClaimTypes.AccountType, EntraClaimTypes.GuestAccountType).Build()),
            NoRole => factory.CreateClientWithToken(Builder(LocalAccounts.UserOne).WithoutClaim(EntraClaimTypes.Roles).Build()),
            UserOne or LockedUser => factory.CreateClientForAccount(LocalAccounts.UserOne),
            AdminOne => factory.CreateClientForAccount(LocalAccounts.AdminOne),
            AdminTwo or LockedAdmin => factory.CreateClientForAccount(LocalAccounts.AdminTwo),
            _ => throw new ArgumentOutOfRangeException(nameof(caller), caller, null),
        };
    }

    private TestTokenBuilder Builder(LocalAccount account) => LocalTokens.Builder(factory.Services, account);

    private static VaultTestApi ApiFor(HttpClient client) => new(client);

    private static async Task<string> ReadDetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("detail").GetString() ?? string.Empty;
    }

    private async Task SetStatusAsync(Guid id, string status)
    {
        var table = factory.StorageAdminClient.GetTableClient(SharpVault.Infrastructure.Storage.TableNames.Users);
        var row = await UserRows.GetAsync(table, id, CancellationToken.None);
        row![UserColumns.Status] = status;
        await table.UpdateEntityAsync(row, row.ETag, Azure.Data.Tables.TableUpdateMode.Replace, CancellationToken.None);
    }
}
