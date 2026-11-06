// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Domain.Users;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferAuthorizationMatrixTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string Preview = "Preview";
    private const string List = "List";
    private const string Get = "Get";
    private const string Approve = "Approve";
    private const string Reject = "Reject";
    private const string Notices = "Notices";
    private const string OwnShares = "OwnShares";
    private const string Create = "Create";
    private const string Abort = "Abort";
    private const string Lock = "Lock";
    private const string Unlock = "Unlock";
    private const string Dismiss = "Dismiss";

    private const string Anonymous = "Anonymous";
    private const string NoRole = "NoRole";
    private const string PlainUser = "PlainUser";
    private const string Guest = "Guest";
    private const string OtherTenant = "OtherTenant";
    private const string AlgNone = "AlgNone";
    private const string UnknownKey = "UnknownKey";
    private const string LockedAdmin = "LockedAdmin";
    private const string Requester = "Requester";
    private const string SecondAdmin = "SecondAdmin";

    private const string LockReason = "Left the company, locked for the handover";
    private const string DeclineReason = "Declined for the access matrix test";

    private static readonly string[] CallerOrder =
    [
        Anonymous, NoRole, PlainUser, Guest, OtherTenant, AlgNone, UnknownKey, LockedAdmin, Requester, SecondAdmin,
    ];

    private static readonly Dictionary<string, int[]> ExpectedStatuses = new(StringComparer.Ordinal)
    {
        [Preview] = [401, 403, 403, 403, 401, 401, 401, 403, 200, 200],
        [List] = [401, 403, 403, 403, 401, 401, 401, 403, 200, 200],
        [Get] = [401, 403, 403, 403, 401, 401, 401, 403, 200, 200],
        [Approve] = [401, 403, 403, 403, 401, 401, 401, 403, 403, 200],
        [Reject] = [401, 403, 403, 403, 401, 401, 401, 403, 200, 200],
        [Abort] = [401, 403, 403, 403, 401, 401, 401, 403, 409, 409],
        [Create] = [401, 403, 403, 403, 401, 401, 401, 403, 201, 201],
        [Lock] = [401, 403, 403, 403, 401, 401, 401, 403, 200, 200],
        [Unlock] = [401, 403, 403, 403, 401, 401, 401, 403, 400, 400],
        [Dismiss] = [401, 403, 404, 403, 401, 401, 401, 403, 404, 404],
        [Notices] = [401, 403, 200, 403, 401, 401, 401, 403, 200, 200],
        [OwnShares] = [401, 403, 200, 403, 401, 401, 401, 403, 200, 200],
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

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task TransferAuthorizationMatrix_EveryEndpointAndCaller_ReturnsTheExpectedStatus(string endpoint, string caller, int expected)
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser, TestUsers.LockedAdmin, TestUsers.FolderManager, TestUsers.FolderViewer);
        await SeedVaultAsync(TestUsers.User, secretCount: 1, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        if (caller == LockedAdmin)
        {
            await SetUserStatusAsync(TestUsers.LockedAdmin.ObjectId, UserStatus.Locked);
        }

        var response = await SendAsync(endpoint, caller, transfer);

        Assert.Equal(expected, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain("SV-", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransferAuthorizationMatrix_DeniedCallers_MakeNoTransferVaultOrKeyVaultCalls()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        var unwrapsBefore = Factory.KeyWraps.Unwraps;
        var rowsBefore = await VaultRowCountAsync(TestUsers.OtherUser.ObjectId);

        await SendAsync(Approve, PlainUser, transfer);
        await SendAsync(Approve, Requester, transfer);
        await SendAsync(Get, PlainUser, transfer);

        Assert.Equal(unwrapsBefore, Factory.KeyWraps.Unwraps);
        Assert.Equal(rowsBefore, await VaultRowCountAsync(TestUsers.OtherUser.ObjectId));
        Assert.Equal(TransferStateDto.PendingApproval, (await GetTransferAsync(TestUsers.Admin, transfer.Id)).State);
    }

    [Fact]
    public async Task TransferAuthorizationMatrix_LockedRequester_ReturnsForbidden()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await SetUserStatusAsync(TestUsers.Admin.ObjectId, SharpVault.Domain.Users.UserStatus.Locked);

        var response = await SendAsync(Get, Requester, transfer);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TransferVisibility_TargetAdmin_OmitsTheRowFromListAndGet()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.ThirdAdmin, TestUsers.User);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.ThirdAdmin);

        var (listResponse, page) = await ApiAs(TestUsers.ThirdAdmin).SendAsync<TransferPageDto>(HttpMethod.Get, ApiRoutes.AdminTransfers);
        var (getResponse, _) = await GetRawAsync(TestUsers.ThirdAdmin, transfer.Id);

        Assert.True(listResponse.IsSuccessStatusCode);
        Assert.DoesNotContain(page?.Items ?? [], item => item.Id == transfer.Id);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task TransferVisibility_SourceAdmin_OmitsTheRowFromListAndGet()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.ThirdAdmin, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.ThirdAdmin, TestUsers.OtherUser);

        var (listResponse, page) = await ApiAs(TestUsers.ThirdAdmin).SendAsync<TransferPageDto>(HttpMethod.Get, ApiRoutes.AdminTransfers);
        var (getResponse, _) = await GetRawAsync(TestUsers.ThirdAdmin, transfer.Id);

        Assert.True(listResponse.IsSuccessStatusCode);
        Assert.DoesNotContain(page?.Items ?? [], item => item.Id == transfer.Id);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task TransferVisibility_BeforeApproval_TargetVaultIsUnchanged()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        await SeedVaultAsync(TestUsers.User, secretCount: 2, folderCount: 1);
        var rowsBefore = await VaultRowCountAsync(TestUsers.OtherUser.ObjectId);

        await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);

        Assert.Equal(rowsBefore, await VaultRowCountAsync(TestUsers.OtherUser.ObjectId));
        Assert.Empty(await ListAllSecretsAsync(TestUsers.OtherUser));
    }

    [Fact]
    public async Task Notices_OtherUser_SeesOnlyTheirOwn()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.ThirdAdmin, TestUsers.User, TestUsers.OtherUser);
        await SeedVaultAsync(TestUsers.User, secretCount: 1, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Single(await NoticesAsync(TestUsers.OtherUser));
        Assert.Empty(await NoticesAsync(TestUsers.ThirdAdmin));
    }

    [Fact]
    public async Task DismissNotice_OtherUsersTransfer_Returns404AndKeepsTheRow()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.ThirdAdmin, TestUsers.User, TestUsers.OtherUser);
        await SeedVaultAsync(TestUsers.User, secretCount: 1, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        var response = await ApiAs(TestUsers.ThirdAdmin).SendRawAsync(HttpMethod.Delete, VaultTestApi.Bind(ApiRoutes.TransferNotice, ("transferId", transfer.Id)), null, null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(await NoticesAsync(TestUsers.OtherUser));
    }

    private async Task<IReadOnlyList<TransferNoticeDto>> NoticesAsync(TestUser viewer)
    {
        var (response, body) = await ApiAs(viewer).SendAsync<TransferNoticeListDto>(HttpMethod.Get, ApiRoutes.TransferNotices);
        Assert.True(response.IsSuccessStatusCode, $"Notices failed with {response.StatusCode}.");
        return body?.Items ?? [];
    }

    private async Task<HttpResponseMessage> SendAsync(string endpoint, string caller, TransferDto transfer)
    {
        var client = ClientForCaller(caller);
        var api = new VaultTestApi(client);
        var rejectBody = "{\"reason\":\"Declined for the access matrix test\"}";
        return endpoint switch
        {
            Preview => await api.SendRawAsync(HttpMethod.Get, $"{ApiRoutes.AdminTransferPreview}?sourceUserId={TestUsers.User.ObjectId:D}", null, null),
            List => await api.SendRawAsync(HttpMethod.Get, ApiRoutes.AdminTransfers, null, null),
            Get => await api.SendRawAsync(HttpMethod.Get, VaultTestApi.Bind(ApiRoutes.AdminTransfer, ("transferId", transfer.Id)), null, null),
            Approve => await api.SendRawAsync(HttpMethod.Post, VaultTestApi.Bind(ApiRoutes.AdminTransferApprove, ("transferId", transfer.Id)), null, transfer.ConcurrencyToken),
            Reject => await api.SendRawAsync(HttpMethod.Post, VaultTestApi.Bind(ApiRoutes.AdminTransferReject, ("transferId", transfer.Id)), rejectBody, transfer.ConcurrencyToken),
            Notices => await api.SendRawAsync(HttpMethod.Get, ApiRoutes.TransferNotices, null, null),
            OwnShares => await api.SendRawAsync(HttpMethod.Get, ApiRoutes.TransferOwnedShares, null, null),
            Create => await api.SendRawAsync(HttpMethod.Post, ApiRoutes.AdminTransfers, CreateJsonBody(TestUsers.FolderManager, TestUsers.FolderViewer), null),
            Abort => await api.SendRawAsync(HttpMethod.Post, VaultTestApi.Bind(ApiRoutes.AdminTransferAbort, ("transferId", transfer.Id)), DecisionBody, transfer.ConcurrencyToken),
            Lock => await api.SendRawAsync(HttpMethod.Post, VaultTestApi.Bind(ApiRoutes.AdminUserLock, ("userId", TestUsers.OtherUser.ObjectId)), LockBody, null),
            Unlock => await api.SendRawAsync(HttpMethod.Post, VaultTestApi.Bind(ApiRoutes.AdminUserUnlock, ("userId", TestUsers.OtherUser.ObjectId)), LockBody, null),
            Dismiss => await api.SendRawAsync(HttpMethod.Delete, VaultTestApi.Bind(ApiRoutes.TransferNotice, ("transferId", transfer.Id)), null, null),
            _ => throw new InvalidOperationException("Unknown endpoint: " + endpoint),
        };
    }

    private static string DecisionBody => $"{{\"reason\":\"{DeclineReason}\"}}";

    private static string LockBody => $"{{\"category\":\"{nameof(ReasonCategoryDto.LeftCompany)}\",\"reason\":\"{LockReason}\"}}";

    private HttpClient ClientForCaller(string caller) => caller switch
    {
        Anonymous => CreateClientFor(Host),
        NoRole => CreateClientFor(Host, TestTokens.Create(WithoutRoles(TestUsers.OtherUser))),
        PlainUser => CreateClientFor(Host, TestTokens.Create(TestUsers.OtherUser)),
        Guest => CreateClientFor(Host, TestTokens.Create(TestUsers.Guest)),
        OtherTenant => CreateClientFor(Host, TestTokens.Create(TestUsers.OtherTenantUser)),
        AlgNone => CreateClientFor(Host, ForgedTokens.AlgNone()),
        UnknownKey => CreateClientFor(Host, ForgedTokens.SignedWithUnknownKey()),
        LockedAdmin => CreateClientFor(Host, TestTokens.Create(TestUsers.LockedAdmin)),
        Requester => CreateClientFor(Host, TestTokens.Create(TestUsers.Admin)),
        SecondAdmin => CreateClientFor(Host, TestTokens.Create(TestUsers.SecondAdmin)),
        _ => throw new InvalidOperationException("Unknown caller: " + caller),
    };
}
