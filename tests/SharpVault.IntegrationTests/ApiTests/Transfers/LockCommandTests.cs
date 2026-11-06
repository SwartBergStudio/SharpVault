// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using SharpVault.Domain.Audit;
using SharpVault.Dtos;
using SharpVault.Dtos.Users;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class LockCommandTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string LockReason = "Left the company, locked for the handover";

    private const string CannotLockSelfDetail = "You cannot lock your own account.";

    private const string AlreadyLockedDetail = "This user is already locked.";

    private const string AlreadyActiveDetail = "This user is already active.";

    private const int PaddingLength = 40_000;

    private static readonly string LeftCompanyJson = $"{{\"category\":\"{nameof(ReasonCategoryDto.LeftCompany)}\",\"reason\":\"{LockReason}\"}}";

    [Fact]
    public async Task Lock_ValidToken_RefusedOnTheNextRequest()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.User);
        var before = await ApiAs(TestUsers.User).SendRawAsync(HttpMethod.Get, ApiRoutes.Me, null, null);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var response = await LockAsync(TestUsers.Admin, TestUsers.User.ObjectId, LeftCompanyJson);

        Assert.True(response.IsSuccessStatusCode, $"Lock failed with {response.StatusCode}.");
        var next = await ApiAs(TestUsers.User).SendRawAsync(HttpMethod.Get, ApiRoutes.Me, null, null);
        Assert.Equal(HttpStatusCode.Forbidden, next.StatusCode);
    }

    [Fact]
    public async Task Lock_NumericCategory_Returns400AndTheUserStaysActive()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.User);
        var numericJson = $"{{\"category\":{(int)ReasonCategoryDto.LeftCompany},\"reason\":\"{LockReason}\"}}";

        var response = await LockAsync(TestUsers.Admin, TestUsers.User.ObjectId, numericJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stillActive = await ApiAs(TestUsers.User).SendRawAsync(HttpMethod.Get, ApiRoutes.Me, null, null);
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);
    }

    [Fact]
    public async Task Lock_Self_Returns400WithTheSelfLockMessageAndStaysActive()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin);

        var response = await LockAsync(TestUsers.Admin, TestUsers.Admin.ObjectId, LeftCompanyJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CannotLockSelfDetail, await ReadDetailAsync(response));
        var me = await ApiAs(TestUsers.Admin).SendRawAsync(HttpMethod.Get, ApiRoutes.Me, null, null);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Lock_SelfWithValidBody_DoesNotWriteAnAuditEntry()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin);

        await LockAsync(TestUsers.Admin, TestUsers.Admin.ObjectId, LeftCompanyJson);

        Assert.Equal(0, await CountAuditEventsAsync(AuditEventType.UserLocked, TestUsers.Admin.ObjectId));
    }

    [Fact]
    public async Task Lock_ThenUnlock_WritesOneAuditRowForEachNamingTheTargetUser()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User);

        var locked = await LockAsync(TestUsers.Admin, TestUsers.User.ObjectId, LeftCompanyJson);
        var unlocked = await UnlockAsync(TestUsers.Admin, TestUsers.User.ObjectId, LeftCompanyJson);

        Assert.True(locked.IsSuccessStatusCode, $"Lock failed with {locked.StatusCode}.");
        Assert.True(unlocked.IsSuccessStatusCode, $"Unlock failed with {unlocked.StatusCode}.");
        Assert.Equal(1, await CountAuditEventsAsync(AuditEventType.UserLocked, TestUsers.User.ObjectId));
        Assert.Equal(1, await CountAuditEventsAsync(AuditEventType.UserUnlocked, TestUsers.User.ObjectId));
    }

    [Fact]
    public async Task Lock_AlreadyLocked_Returns400WithTheAlreadyLockedMessageAndOneAuditRow()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User);
        await LockAsync(TestUsers.Admin, TestUsers.User.ObjectId, LeftCompanyJson);

        var again = await LockAsync(TestUsers.Admin, TestUsers.User.ObjectId, LeftCompanyJson);

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(AlreadyLockedDetail, await ReadDetailAsync(again));
        Assert.Equal(1, await CountAuditEventsAsync(AuditEventType.UserLocked, TestUsers.User.ObjectId));
    }

    [Fact]
    public async Task Lock_ValidReasonPaddedWithSpaces_Returns200AndOneAuditRow()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User);

        var response = await LockAsync(TestUsers.Admin, TestUsers.User.ObjectId, PaddedJson());

        Assert.True(response.IsSuccessStatusCode, $"Lock failed with {response.StatusCode}.");
        Assert.Equal(1, await CountAuditEventsAsync(AuditEventType.UserLocked, TestUsers.User.ObjectId));
        var next = await ApiAs(TestUsers.User).SendRawAsync(HttpMethod.Get, ApiRoutes.Me, null, null);
        Assert.Equal(HttpStatusCode.Forbidden, next.StatusCode);
    }

    [Fact]
    public async Task Unlock_ValidReasonPaddedWithSpaces_Returns200AndOneAuditRow()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User);
        await LockAsync(TestUsers.Admin, TestUsers.User.ObjectId, LeftCompanyJson);

        var response = await UnlockAsync(TestUsers.Admin, TestUsers.User.ObjectId, PaddedJson());

        Assert.True(response.IsSuccessStatusCode, $"Unlock failed with {response.StatusCode}.");
        Assert.Equal(1, await CountAuditEventsAsync(AuditEventType.UserUnlocked, TestUsers.User.ObjectId));
        var next = await ApiAs(TestUsers.User).SendRawAsync(HttpMethod.Get, ApiRoutes.Me, null, null);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Fact]
    public async Task Unlock_AlreadyActive_Returns400WithTheAlreadyActiveMessageAndNoAuditRow()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User);

        var response = await UnlockAsync(TestUsers.Admin, TestUsers.User.ObjectId, LeftCompanyJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AlreadyActiveDetail, await ReadDetailAsync(response));
        Assert.Equal(0, await CountAuditEventsAsync(AuditEventType.UserUnlocked, TestUsers.User.ObjectId));
    }

    private static string PaddedJson() =>
        JsonSerializer.Serialize(new { category = nameof(ReasonCategoryDto.LeftCompany), reason = LockReason + new string(' ', PaddingLength) });

    private async Task<HttpResponseMessage> UnlockAsync(TestUser actor, Guid userId, string json) =>
        await ApiAs(actor).SendRawAsync(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.AdminUserUnlock, ("userId", userId)),
            json,
            null);

    private async Task<HttpResponseMessage> LockAsync(TestUser actor, Guid userId, string json) =>
        await ApiAs(actor).SendRawAsync(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.AdminUserLock, ("userId", userId)),
            json,
            null);

    private static async Task<string> ReadDetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("detail").GetString() ?? string.Empty;
    }
}
