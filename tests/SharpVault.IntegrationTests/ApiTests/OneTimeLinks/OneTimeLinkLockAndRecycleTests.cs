// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using Azure;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Audit;
using SharpVault.Dtos;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Users;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.OneTimeLinks;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class OneTimeLinkLockAndRecycleTests(ApiTestFactory factory) : OneTimeLinkTestBase(factory)
{
    private const string LockReason = "Left the company, links end with the employment";

    private static readonly string LeftCompanyJson = $"{{\"category\":\"{nameof(ReasonCategoryDto.LeftCompany)}\",\"reason\":\"{LockReason}\"}}";

    [Fact]
    public async Task Lock_LockedCreator_RevokesEveryOpenLinkAndAuditsEachNamingTheAdmin()
    {
        var secret = await NewSecretAsync();
        var first = await CreateLinkAsync(secret.Id, NewPayload(out _));
        var second = await CreateLinkAsync(secret.Id, NewPayload(out _));

        var locked = await AdminSendAsync(HttpMethod.Post, ApiRoutes.AdminUserLock, OwnerId, LeftCompanyJson);

        Assert.True(locked.IsSuccessStatusCode, $"Lock failed with {locked.StatusCode}.");
        foreach (var linkId in new[] { first, second })
        {
            using var opened = await OpenAsync(linkId);
            Assert.Equal(HttpStatusCode.NotFound, opened.StatusCode);
            var revoked = Assert.Single(await AuditRowsMentioningAsync(AuditEventType.OneTimeLinkRevoked, linkId));
            Assert.Equal(TestUsers.Admin.ObjectId.ToString(), revoked.GetString("ActorId"));
        }
    }

    [Fact]
    public async Task Lock_OtherUsersLinks_AreUntouched()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        await WarmUpAsync(TestUsers.OtherUser);

        using (var locked = await AdminSendAsync(HttpMethod.Post, ApiRoutes.AdminUserLock, TestUsers.OtherUser.ObjectId, LeftCompanyJson))
        {
            Assert.True(locked.IsSuccessStatusCode, $"Lock failed with {locked.StatusCode}.");
        }

        using var opened = await OpenAsync(linkId);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task Unlock_LockedCreatorWithSurvivingLink_RevokesTheLinkBeforeActivating()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        await LockUserAsync(OwnerId);

        using (var unlock = await AdminSendAsync(HttpMethod.Post, ApiRoutes.AdminUserUnlock, OwnerId, LeftCompanyJson))
        {
            Assert.True(unlock.IsSuccessStatusCode, $"Unlock failed with {unlock.StatusCode}.");
        }

        using var opened = await OpenAsync(linkId);
        Assert.Equal(HttpStatusCode.NotFound, opened.StatusCode);
        Assert.Single(await AuditRowsMentioningAsync(AuditEventType.OneTimeLinkRevoked, linkId));
    }

    [Fact]
    public async Task Unlock_ActiveUser_Returns400AndKeepsTheirLinks()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));

        using (var unlock = await AdminSendAsync(HttpMethod.Post, ApiRoutes.AdminUserUnlock, OwnerId, LeftCompanyJson))
        {
            Assert.Equal(HttpStatusCode.BadRequest, unlock.StatusCode);
        }

        using var opened = await OpenAsync(linkId);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task Open_CreatorLockedWithLinkRowStillPresent_ReturnsGeneric404()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        await LockUserAsync(OwnerId);
        try
        {
            using var refused = await OpenAsync(linkId);
            using var unknown = await OpenAsync(Guid.NewGuid());

            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            Assert.Equal(
                NormalizedProblem(await unknown.Content.ReadAsStringAsync(Token)),
                NormalizedProblem(await refused.Content.ReadAsStringAsync(Token)));
            Assert.NotNull(await Factory.Services.GetRequiredService<IOneTimeLinkStore>().GetAsync(linkId, Token));
        }
        finally
        {
            await UnlockUserAsync(OwnerId);
        }
    }

    [Fact]
    public async Task Open_CreatorRowMissing_ReturnsGeneric404()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        await GetUsersTable().DeleteEntityAsync(UserRows.PartitionKey, OwnerId.ToString("D"), ETag.All, Token);
        try
        {
            using var refused = await OpenAsync(linkId);
            using var unknown = await OpenAsync(Guid.NewGuid());

            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            Assert.Equal(
                NormalizedProblem(await unknown.Content.ReadAsStringAsync(Token)),
                NormalizedProblem(await refused.Content.ReadAsStringAsync(Token)));
            Assert.NotNull(await Factory.Services.GetRequiredService<IOneTimeLinkStore>().GetAsync(linkId, Token));
        }
        finally
        {
            await UserRows.InsertAsync(GetUsersTable(), Owner, UserRows.ActiveStatus, DateTimeOffset.UtcNow, Token);
        }
    }

    [Fact]
    public async Task Lock_LockedUsersLinksOnSomeoneElsesSecret_AreRevoked()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.OtherUser, SharePermissionDto.Manage);
        await WarmUpAsync(TestUsers.OtherUser);
        var linkId = await CreateLinkAsAsync(TestUsers.OtherUser, secret.Id);

        using (var locked = await AdminSendAsync(HttpMethod.Post, ApiRoutes.AdminUserLock, TestUsers.OtherUser.ObjectId, LeftCompanyJson))
        {
            Assert.True(locked.IsSuccessStatusCode, $"Lock failed with {locked.StatusCode}.");
        }

        using var opened = await OpenAsync(linkId);
        Assert.Equal(HttpStatusCode.NotFound, opened.StatusCode);
        Assert.Single(await AuditRowsMentioningAsync(AuditEventType.OneTimeLinkRevoked, linkId));
    }

    [Fact]
    public async Task Lock_OwnerLocked_LinkCreatedByTheirManagerStillOpens()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.OtherUser, SharePermissionDto.Manage);
        await WarmUpAsync(TestUsers.OtherUser);
        var linkId = await CreateLinkAsAsync(TestUsers.OtherUser, secret.Id);

        using (var locked = await AdminSendAsync(HttpMethod.Post, ApiRoutes.AdminUserLock, OwnerId, LeftCompanyJson))
        {
            Assert.True(locked.IsSuccessStatusCode, $"Lock failed with {locked.StatusCode}.");
        }

        try
        {
            using var opened = await OpenAsync(linkId);
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        }
        finally
        {
            await UnlockUserAsync(OwnerId);
        }
    }

    [Fact]
    public async Task Create_RecycledSecret_Returns404AndStoresNothing()
    {
        var secret = await NewSecretAsync();
        using (var recycled = await OwnerVault.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken))
        {
            Assert.True(recycled.IsSuccessStatusCode, $"Recycle failed with {recycled.StatusCode}.");
        }

        using var response = await PostCreateAsync(Owner, OwnerId, secret.Id, NewPayload(out _));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await AuditRowsMentioningAsync(AuditEventType.OneTimeLinkCreated, secret.Id));
    }

    [Fact]
    public async Task Snapshot_RecycledSecret_Returns404()
    {
        var secret = await NewSecretAsync();
        using (var recycled = await OwnerVault.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken))
        {
            Assert.True(recycled.IsSuccessStatusCode, $"Recycle failed with {recycled.StatusCode}.");
        }

        using var response = await PostSnapshotAsync(Owner, OwnerId, secret.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_ManageRevokedAfterSnapshot_Returns404AndStoresNothing()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.OtherUser, SharePermissionDto.Manage);
        using (var snapshot = await PostSnapshotAsync(TestUsers.OtherUser, OwnerId, secret.Id))
        {
            Assert.Equal(HttpStatusCode.OK, snapshot.StatusCode);
        }

        using (var revoked = await OwnerSharing.RevokeSecretGrantAsync(OwnerId, secret.Id, TestUsers.OtherUser.ObjectId))
        {
            Assert.True(revoked.IsSuccessStatusCode, $"Grant revoke failed with {revoked.StatusCode}.");
        }

        using var response = await PostCreateAsync(TestUsers.OtherUser, OwnerId, secret.Id, NewPayload(out _));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await AuditRowsMentioningAsync(AuditEventType.OneTimeLinkCreated, secret.Id));
    }

    private async Task<Guid> CreateLinkAsAsync(TestUser creator, Guid secretId)
    {
        using var response = await PostCreateAsync(creator, OwnerId, secretId, NewPayload(out _));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OneTimeLinkDto>(Token);
        Assert.NotNull(body);
        return body.Id;
    }

    private async Task<HttpResponseMessage> AdminSendAsync(HttpMethod method, string route, Guid userId, string? json) =>
        await new VaultTestApi(ClientFor(TestUsers.Admin)).SendRawAsync(
            method,
            VaultTestApi.Bind(route, ("userId", userId)),
            json,
            null);
}
