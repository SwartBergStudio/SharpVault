// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using SharpVault.Application.Features.Transfers.Common;
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
    private const string List = "List";
    private const string Approve = "Approve";
    private const string Lock = "Lock";
    private const string Dismiss = "Dismiss";
    private const string Notices = "Notices";

    private const string Anonymous = "Anonymous";
    private const string UnknownKey = "UnknownKey";
    private const string UserOne = "UserOne";
    private const string LockedUser = "LockedUser";
    private const string AdminOne = "AdminOne";
    private const string AdminTwo = "AdminTwo";

    private const string ValidReason = "Role change, the vault moves to the new owner";
    private const string LockReason = "Left the company, locked for the handover";
    private const string DeclineReason = "Declined for the access matrix test";

    private static readonly string[] CallerOrder =
    [
        Anonymous, UnknownKey, UserOne, LockedUser, AdminOne, AdminTwo,
    ];

    private static readonly Dictionary<string, int[]> ExpectedStatuses = new(StringComparer.Ordinal)
    {
        [List] = [401, 401, 403, 403, 200, 200],
        [Approve] = [401, 401, 403, 403, 403, 200],
        [Lock] = [401, 401, 403, 403, 200, 200],
        [Dismiss] = [401, 401, 404, 403, 404, 404],
        [Notices] = [401, 401, 200, 403, 200, 200],
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

    }

    private async Task<HttpResponseMessage> SendAsync(string endpoint, string caller, TransferDto transfer)
    {
        var api = ApiFor(ClientForCaller(caller));
        return endpoint switch
        {
            List => await api.SendRawAsync(HttpMethod.Get, ApiRoutes.AdminTransfers, null, null),
            Approve => await api.SendRawAsync(HttpMethod.Post, VaultTestApi.Bind(ApiRoutes.AdminTransferApprove, ("transferId", transfer.Id)), null, transfer.ConcurrencyToken),
            Lock => await api.SendRawAsync(HttpMethod.Post, VaultTestApi.Bind(ApiRoutes.AdminUserLock, ("userId", LocalAccounts.UserTwo.Id)), LockBody(), null),
            Dismiss => await api.SendRawAsync(HttpMethod.Delete, VaultTestApi.Bind(ApiRoutes.TransferNotice, ("transferId", transfer.Id)), null, null),
            Notices => await api.SendRawAsync(HttpMethod.Get, ApiRoutes.TransferNotices, null, null),
            _ => throw new InvalidOperationException("Unknown endpoint: " + endpoint),
        };
    }

    private static string DecisionBody(string reason) => $"{{\"reason\":\"{reason}\"}}";

    private static string LockBody() => $"{{\"category\":\"{nameof(ReasonCategoryDto.LeftCompany)}\",\"reason\":\"{LockReason}\"}}";

    private static string CreateBody(LocalAccount source, LocalAccount target) =>
        $"{{\"sourceUserId\":\"{source.Id:D}\",\"targetUserId\":\"{target.Id:D}\",\"scope\":\"{nameof(TransferScopeKindDto.AllItems)}\",\"category\":\"{nameof(ReasonCategoryDto.RoleChange)}\",\"reason\":\"{ValidReason}\"}}";

    private HttpClient ClientForCaller(string caller)
    {
        return caller switch
        {
            Anonymous => factory.CreateUnauthenticatedClient(),
            UnknownKey => factory.CreateClientWithToken(ForgedTokens.SignedWithUnknownKey()),
            UserOne or LockedUser => factory.CreateClientForAccount(LocalAccounts.UserOne),
            AdminOne => factory.CreateClientForAccount(LocalAccounts.AdminOne),
            AdminTwo => factory.CreateClientForAccount(LocalAccounts.AdminTwo),
            _ => throw new ArgumentOutOfRangeException(nameof(caller), caller, null),
        };
    }

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
