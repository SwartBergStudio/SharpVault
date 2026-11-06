// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Domain.Audit;
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
public sealed class TransferApprovalTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string StaleToken = "W/\"datetime'2020-01-01T00%3A00%3A00.0000000Z'\"";


    private static TestUser DemotedAdmin => TestUsers.Admin with { Roles = [UserRole] };

    [Fact]
    public async Task Approve_ByRequester_Returns403AndStaysPending()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);

        var (response, _) = await ApproveRawAsync(TestUsers.Admin, transfer.Id, transfer.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(TransferStateDto.PendingApproval, (await GetTransferAsync(TestUsers.Admin, transfer.Id)).State);
    }

    [Fact]
    public async Task Approve_ByRequester_WritesApprovalDeniedAudit()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);

        await ApproveRawAsync(TestUsers.Admin, transfer.Id, transfer.ConcurrencyToken);

        var denied = await VaultAuditRowsAsync(AuditEventType.TransferApprovalDenied);
        Assert.Single(denied);
        Assert.Equal(TestUsers.Admin.ObjectId.ToString("D"), denied[0].GetString("ActorId"));
    }

    [Fact]
    public async Task Approve_TokenWithoutAdminRole_Returns403()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);

        var (response, _) = await ApproveRawAsync(WithoutRoles(TestUsers.SecondAdmin), transfer.Id, transfer.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(TransferStateDto.PendingApproval, (await GetTransferAsync(TestUsers.Admin, transfer.Id)).State);
    }

    [Fact]
    public async Task Approve_LockedAdminWithoutCache_Returns403FromThePolicy()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await SetUserStatusAsync(TestUsers.SecondAdmin.ObjectId, UserStatus.Locked);

        var (response, _) = await ApproveRawAsync(TestUsers.SecondAdmin, transfer.Id, transfer.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(TransferStateDto.PendingApproval, (await GetTransferAsync(TestUsers.Admin, transfer.Id)).State);
    }

    [Fact]
    public async Task Approve_ApproverLockedInsideCacheWindow_Returns403FromTheGuard()
    {
        UseHost(Factory.WithWebHostBuilder(builder => builder.UseSetting("UserAccess:CacheTtl", "00:00:30")));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        var warm = await ApiAs(TestUsers.SecondAdmin).Client.GetAsync(ApiRoutes.Me, Token);
        Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
        await SetUserStatusAsync(TestUsers.SecondAdmin.ObjectId, UserStatus.Locked);

        var (response, _) = await ApproveRawAsync(TestUsers.SecondAdmin, transfer.Id, transfer.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(TransferStateDto.PendingApproval, (await GetTransferAsync(TestUsers.Admin, transfer.Id)).State);
    }

    [Fact]
    public async Task Approve_OldAdminTokenAfterNewerTokenWithoutRole_Returns403()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        var issuedBefore = DateTimeOffset.UtcNow.AddHours(-1);
        var oldAdminToken = TestTokens.For(TestUsers.SecondAdmin)
            .WithClaim("iat", issuedBefore.ToUnixTimeSeconds())
            .NotBefore(issuedBefore.AddMinutes(-5))
            .Build();
        var newerTokenWithoutAdmin = TestTokens.For(TestUsers.SecondAdmin with { Roles = [UserRole] }).Build();
        var observed = await CreateClientFor(Host, newerTokenWithoutAdmin).GetAsync(ApiRoutes.Me, Token);
        Assert.Equal(HttpStatusCode.OK, observed.StatusCode);

        var response = await new VaultTestApi(CreateClientFor(Host, oldAdminToken)).SendRawAsync(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.AdminTransferApprove, ("transferId", transfer.Id)),
            null,
            transfer.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(TransferStateDto.PendingApproval, (await GetTransferAsync(TestUsers.Admin, transfer.Id)).State);
    }

    [Fact]
    public async Task Approve_ByTargetAdmin_Returns404()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.ThirdAdmin, TestUsers.User);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.ThirdAdmin);

        var (response, _) = await ApproveRawAsync(TestUsers.ThirdAdmin, transfer.Id, transfer.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Approve_BySourceAdmin_Returns404()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.ThirdAdmin, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.ThirdAdmin, TestUsers.OtherUser);

        var (response, _) = await ApproveRawAsync(TestUsers.ThirdAdmin, transfer.Id, transfer.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Approve_RequesterSinceLocked_Returns400AndStaysPending()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await SetUserStatusAsync(TestUsers.Admin.ObjectId, UserStatus.Locked);

        var (response, _) = await ApproveRawAsync(TestUsers.SecondAdmin, transfer.Id, transfer.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TransferStateDto.PendingApproval, (await GetTransferAsync(TestUsers.SecondAdmin, transfer.Id)).State);
    }

    [Fact]
    public async Task Approve_RequesterSinceDemoted_Returns400AndStaysPending()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await WarmUpUserAsync(DemotedAdmin);

        var (response, _) = await ApproveRawAsync(TestUsers.SecondAdmin, transfer.Id, transfer.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TransferStateDto.PendingApproval, (await GetTransferAsync(TestUsers.SecondAdmin, transfer.Id)).State);
    }

    [Fact]
    public async Task Approve_TargetLocked_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await SetUserStatusAsync(TestUsers.OtherUser.ObjectId, UserStatus.Locked);

        var (response, _) = await ApproveRawAsync(TestUsers.SecondAdmin, transfer.Id, transfer.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Approve_SourceUnlockedAfterRequest_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        await SetUserStatusAsync(TestUsers.User.ObjectId, UserStatus.Locked);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser, ReasonCategoryDto.Dismissed);
        await SetUserStatusAsync(TestUsers.User.ObjectId, UserStatus.Active);

        var (response, _) = await ApproveRawAsync(TestUsers.SecondAdmin, transfer.Id, transfer.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TransferStateDto.PendingApproval, (await GetTransferAsync(TestUsers.Admin, transfer.Id)).State);
    }

    [Fact]
    public async Task Approve_Twice_SecondReturns409AndOneJobRuns()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        var (second, _) = await ApproveRawAsync(TestUsers.SecondAdmin, transfer.Id, transfer.ConcurrencyToken);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Single(await VaultAuditRowsAsync(AuditEventType.TransferApproved));
    }

    [Fact]
    public async Task Approve_StaleConcurrencyToken_Returns409()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);

        var (response, _) = await ApproveRawAsync(TestUsers.SecondAdmin, transfer.Id, StaleToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(TransferStateDto.PendingApproval, (await GetTransferAsync(TestUsers.Admin, transfer.Id)).State);
    }

    [Fact]
    public async Task Approve_IfMatchStar_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);

        var (response, _) = await ApproveRawAsync(TestUsers.SecondAdmin, transfer.Id, "*");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Approve_MissingIfMatch_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);

        var (response, _) = await ApproveRawAsync(TestUsers.SecondAdmin, transfer.Id, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Approve_AfterRejected_Returns409()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await RejectAsync(TestUsers.SecondAdmin, transfer);

        var (response, _) = await ApproveRawAsync(TestUsers.SecondAdmin, transfer.Id, transfer.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Approve_ParallelTwoAdmins_ExactlyOneSucceeds()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.ThirdAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);

        var first = ApproveRawAsync(TestUsers.SecondAdmin, transfer.Id, transfer.ConcurrencyToken);
        var second = ApproveRawAsync(TestUsers.ThirdAdmin, transfer.Id, transfer.ConcurrencyToken);
        var results = await Task.WhenAll(first, second);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(1, results.Count(result => result.Response.IsSuccessStatusCode));
        Assert.Equal(1, results.Count(result => result.Response.StatusCode == HttpStatusCode.Conflict));
        Assert.Single(await VaultAuditRowsAsync(AuditEventType.TransferApproved));
    }

    [Fact]
    public async Task Create_RequesterIsTarget_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.User);

        var (response, _) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.Admin));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_RequesterIsSource_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.OtherUser);

        var (response, _) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.Admin, TestUsers.OtherUser));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_SourceEqualsTarget_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.User);

        var (response, _) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.User));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_TargetLocked_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await SetUserStatusAsync(TestUsers.OtherUser.ObjectId, UserStatus.Locked);

        var (response, _) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownTarget_Returns404()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.User);
        var body = CreateBody(TestUsers.User, TestUsers.OtherUser);
        body.TargetUserId = Guid.NewGuid();

        var (response, _) = await CreateRawAsync(TestUsers.Admin, body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_LeftCompanyWithActiveSource_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);

        var (response, _) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser, ReasonCategoryDto.LeftCompany));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_BodyWithRequestedByOrState_Returns400()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var extraFields = $",\"requestedBy\":\"{TestUsers.Admin.ObjectId:D}\",\"state\":\"{nameof(TransferStateDto.Approved)}\"";

        var withExtras = await ApiAs(TestUsers.Admin).SendRawAsync(HttpMethod.Post, ApiRoutes.AdminTransfers, CreateJsonBody(TestUsers.User, TestUsers.OtherUser, extraFields), ifMatch: null);
        var control = await ApiAs(TestUsers.Admin).SendRawAsync(HttpMethod.Post, ApiRoutes.AdminTransfers, CreateJsonBody(TestUsers.User, TestUsers.OtherUser), ifMatch: null);

        Assert.Equal(HttpStatusCode.BadRequest, withExtras.StatusCode);
        Assert.True(control.StatusCode == HttpStatusCode.Created, $"Control create returned {control.StatusCode}: {await control.Content.ReadAsStringAsync(Token)}");
    }

    [Fact]
    public async Task Create_ParallelForSameSource_ExactlyOneSucceeds()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser, TestUsers.ThirdAdmin);

        var first = CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser));
        var second = CreateRawAsync(TestUsers.SecondAdmin, CreateBody(TestUsers.User, TestUsers.ThirdAdmin));
        var results = await Task.WhenAll(first, second);
        var created = results.Count(result => result.Response.StatusCode == HttpStatusCode.Created);
        var refused = results.Where(result => result.Response.StatusCode != HttpStatusCode.Created).Select(result => result.Response.StatusCode).ToArray();

        Assert.Equal(1, created);
        Assert.Single(refused);
        Assert.Contains(refused[0], new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
    }

    [Fact]
    public async Task Create_AfterRejected_Succeeds()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await RejectAsync(TestUsers.SecondAdmin, transfer);

        var (response, _) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Reject_ByRequester_Succeeds()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);

        var (response, body) = await RejectRawAsync(TestUsers.Admin, transfer);

        Assert.True(response.IsSuccessStatusCode, $"Reject failed with {response.StatusCode}.");
        Assert.Equal(TransferStateDto.Rejected, body?.State);
    }

    [Fact]
    public async Task Reject_ByTargetAdmin_Returns404()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.ThirdAdmin, TestUsers.User);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.ThirdAdmin);

        var (response, _) = await RejectRawAsync(TestUsers.ThirdAdmin, transfer);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reject_AfterApproved_Returns409()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        var (response, _) = await RejectRawAsync(TestUsers.Admin, transfer);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private async Task<(HttpResponseMessage Response, TransferDto? Body)> RejectRawAsync(TestUser caller, TransferDto transfer) =>
        await ApiAs(caller).SendAsync<TransferDto>(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.AdminTransferReject, ("transferId", transfer.Id)),
            new TransferDecisionRequestDto { Reason = "Declined for the purpose of this test" },
            ifMatch: transfer.ConcurrencyToken);

    private async Task RejectAsync(TestUser caller, TransferDto transfer)
    {
        var (response, _) = await RejectRawAsync(caller, transfer);
        Assert.True(response.IsSuccessStatusCode, $"Reject failed with {response.StatusCode}.");
    }
}
