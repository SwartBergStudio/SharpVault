// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class LockRevokeTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string LockReason = "Dismissed, shares end with the employment";

    private const string RevokedEventType = "SecretShareRevoked";

    private static readonly string DismissedJson = $"{{\"category\":\"{nameof(ReasonCategoryDto.Dismissed)}\",\"reason\":\"{LockReason}\"}}";

    private static readonly string LeftCompanyJson = $"{{\"category\":\"{nameof(ReasonCategoryDto.LeftCompany)}\",\"reason\":\"{LockReason}\"}}";

    [Fact]
    public async Task Lock_DismissedWithReceivedShare_RemovesTheGrantFromTheOwnersList()
    {
        var secretId = await GrantSecretToOtherUserAsync();
        Assert.Contains(await OwnedGrantsAsync(), grant => grant.ItemId == secretId && grant.RecipientId == TestUsers.OtherUser.ObjectId);

        var response = await LockAsync(TestUsers.Admin, TestUsers.OtherUser.ObjectId, DismissedJson);

        Assert.True(response.IsSuccessStatusCode, $"Lock failed with {response.StatusCode}.");
        Assert.DoesNotContain(await OwnedGrantsAsync(), grant => grant.ItemId == secretId && grant.RecipientId == TestUsers.OtherUser.ObjectId);
    }

    [Fact]
    public async Task Lock_DismissedWithReceivedShare_WritesOneRevokeAuditNamingTheLockedRecipient()
    {
        var secretId = await GrantSecretToOtherUserAsync();

        await LockAsync(TestUsers.Admin, TestUsers.OtherUser.ObjectId, DismissedJson);

        var revokes = await RevokeAuditRowsAsync(secretId);
        var forRecipient = revokes.Where(row => row.GetString("RecipientId") == TestUsers.OtherUser.ObjectId.ToString()).ToList();
        Assert.Single(forRecipient);
        Assert.Equal(TestUsers.Admin.ObjectId.ToString(), forRecipient[0].GetString("ActorId"));
    }

    [Fact]
    public async Task Lock_LeftCompanyWithReceivedShare_KeepsTheGrant()
    {
        var secretId = await GrantSecretToOtherUserAsync();

        var response = await LockAsync(TestUsers.Admin, TestUsers.OtherUser.ObjectId, LeftCompanyJson);

        Assert.True(response.IsSuccessStatusCode, $"Lock failed with {response.StatusCode}.");
        Assert.Contains(await OwnedGrantsAsync(), grant => grant.ItemId == secretId && grant.RecipientId == TestUsers.OtherUser.ObjectId);
    }

    [Fact]
    public async Task Lock_DismissedWhenTheInboxNeverEnds_Returns500AndTheGrantStays()
    {
        var secretId = await GrantSecretToOtherUserAsync();
        UseHost(StuckInboxHost());

        var response = await LockAsync(TestUsers.Admin, TestUsers.OtherUser.ObjectId, DismissedJson);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(await OwnedGrantsAsync(), grant => grant.ItemId == secretId && grant.RecipientId == TestUsers.OtherUser.ObjectId);
        Assert.Empty(await RevokeAuditRowsAsync(secretId));
    }

    [Fact]
    public async Task Lock_DismissedWhenTheInboxNeverEnds_LeavesTheLockAppliedForTheNextRetry()
    {
        await GrantSecretToOtherUserAsync();
        UseHost(StuckInboxHost());

        await LockAsync(TestUsers.Admin, TestUsers.OtherUser.ObjectId, DismissedJson);

        var next = await ApiAs(TestUsers.OtherUser).SendRawAsync(HttpMethod.Get, ApiRoutes.Me, null, null);
        Assert.Equal(HttpStatusCode.Forbidden, next.StatusCode);
    }

    private WebApplicationFactory<Program> StuckInboxHost() =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IShareInboxStore>();
            services.AddSingleton<IShareInboxStore>(new NeverEndingInboxStore());
        }));

    private async Task<Guid> GrantSecretToOtherUserAsync()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        await SeedVaultAsync(TestUsers.User, secretCount: 1, folderCount: 0);
        var secretId = (await ListAllSecretsAsync(TestUsers.User)).Single().Id;
        var (response, _) = await new SharingTestApi(ApiAs(TestUsers.User).Client).CreateSecretGrantAsync(
            TestUsers.User.ObjectId,
            secretId,
            new CreateShareGrantRequestDto { RecipientId = TestUsers.OtherUser.ObjectId, Permission = SharePermissionDto.View });
        Assert.True(response.IsSuccessStatusCode, $"Grant failed with {response.StatusCode}.");
        return secretId;
    }

    private async Task<HttpResponseMessage> LockAsync(TestUser actor, Guid userId, string json) =>
        await ApiAs(actor).SendRawAsync(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.AdminUserLock, ("userId", userId)),
            json,
            null);

    private async Task<IReadOnlyList<OwnedShareDto>> OwnedGrantsAsync()
    {
        var (response, page) = await ApiAs(TestUsers.User).SendAsync<OwnedSharePageDto>(HttpMethod.Get, ApiRoutes.TransferOwnedShares);
        Assert.True(response.IsSuccessStatusCode, $"Owned shares failed with {response.StatusCode}.");
        return page?.Items ?? [];
    }

    private async Task<IReadOnlyList<TableEntity>> RevokeAuditRowsAsync(Guid secretId) =>
        [.. (await SecretAuditRowsAsync(TestUsers.User.ObjectId, secretId)).Where(row => row.GetString("EventType") == RevokedEventType)];

    private sealed class NeverEndingInboxStore : IShareInboxStore
    {
        private const string StuckToken = "stuck";

        public Task AddIfAbsentAsync(ShareInboxEntry entry, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Versioned<ShareInboxEntry>?> GetAsync(Guid recipientId, Guid ownerId, ShareItemKind kind, Guid itemId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoreWriteResult> SetFiledFolderAsync(ShareInboxEntry entry, string etag, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RemoveAsync(Guid recipientId, Guid ownerId, ShareItemKind kind, Guid itemId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShareInboxPage> ListAsync(Guid recipientId, Guid? filedFolderId, int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
            Task.FromResult(new ShareInboxPage([], StuckToken));
    }
}
