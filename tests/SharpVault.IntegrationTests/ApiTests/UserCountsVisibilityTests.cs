// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using Azure.Data.Tables;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Users;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.ApiTests.Sharing;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class UserCountsVisibilityTests(ApiTestFactory factory) : SharingFunctionalTestBase(factory)
{
    private const string CountsPathPrefix = "/api/admin/users/";
    private const string CountsPathSuffix = "/counts";
    private const int SecretsSeededForOtherUser = 1;
    private const int SecretsSeededForOwner = 2;

    [Fact]
    public async Task GetUserCounts_UserWithNoKeyRing_ReturnsZeros()
    {
        await WarmUpAsync(TestUsers.Admin);
        await WarmUpAsync(TestUsers.ShareEditor);

        using var client = ClientFor(TestUsers.Admin);
        var counts = await GetCountsAsync(client, TestUsers.ShareEditor.ObjectId);

        Assert.Equal(0, counts.ItemCount);
        Assert.Equal(0, counts.ShareCount);
        Assert.False(counts.CountsAreCapped);
    }

    [Fact]
    public async Task GetUserCounts_LockedTarget_Returns200()
    {
        await WarmUpAsync(TestUsers.Admin);
        await WarmUpAsync(TestUsers.User);
        await NewSecretAsync();
        await LockUserAsync(TestUsers.User.ObjectId);

        using var client = ClientFor(TestUsers.Admin);
        var response = await client.GetAsync(CountsPath(TestUsers.User.ObjectId), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var counts = await response.Content.ReadFromJsonAsync<UserCountsDto>(TestContext.Current.CancellationToken);
        Assert.Equal(1, counts?.ItemCount);
    }

    [Fact]
    public async Task GetUserCounts_TwoUsers_EachSeesOnlyTheirOwnRows()
    {
        await WarmUpAsync(TestUsers.Admin);
        await WarmUpAsync(TestUsers.User);
        await WarmUpAsync(TestUsers.OtherUser);
        for (var index = 0; index < SecretsSeededForOwner; index++)
        {
            await NewSecretAsync();
        }

        await VaultSeeder.SeedAsync(Factory, TestUsers.OtherUser.ObjectId, SecretsSeededForOtherUser, 0, TestContext.Current.CancellationToken);
        using var client = ClientFor(TestUsers.Admin);

        var owner = await GetCountsAsync(client, TestUsers.User.ObjectId);
        var other = await GetCountsAsync(client, TestUsers.OtherUser.ObjectId);

        Assert.Equal(SecretsSeededForOwner, owner.ItemCount);
        Assert.Equal(SecretsSeededForOtherUser, other.ItemCount);
    }

    [Fact]
    public async Task GetUserCounts_StagedRowsAtTheTarget_AreNotCounted()
    {
        await WarmUpAsync(TestUsers.Admin);
        await WarmUpAsync(TestUsers.OtherUser);
        var stagedRowKey = VaultKeys.StagedSecretRowKey(Guid.NewGuid(), Guid.NewGuid());
        var staged = new TableEntity(VaultKeys.PartitionKey(TestUsers.OtherUser.ObjectId), stagedRowKey);
        await VaultsTable.UpsertEntityAsync(staged, TableUpdateMode.Replace, TestContext.Current.CancellationToken);
        var stored = await VaultsTable.GetEntityIfExistsAsync<TableEntity>(staged.PartitionKey, stagedRowKey, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(stored.HasValue, "The staged row was not written, so the case proves nothing.");

        using var client = ClientFor(TestUsers.Admin);
        var counts = await GetCountsAsync(client, TestUsers.OtherUser.ObjectId);

        Assert.Equal(0, counts.ItemCount);
        Assert.Equal(0, counts.ShareCount);
    }

    [Fact]
    public async Task GetUserCounts_AfterRevoke_ShareCountDrops()
    {
        await WarmUpAsync(TestUsers.Admin);
        await WarmUpAsync(TestUsers.ShareViewer);
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        using var client = ClientFor(TestUsers.Admin);
        Assert.Equal(1, (await GetCountsAsync(client, OwnerId)).ShareCount);

        var revoked = await OwnerSharing.RevokeSecretGrantAsync(OwnerId, secret.Id, TestUsers.ShareViewer.ObjectId);

        Assert.True(revoked.IsSuccessStatusCode, $"Revoke failed with {revoked.StatusCode}.");
        Assert.Equal(0, (await GetCountsAsync(client, OwnerId)).ShareCount);
    }

    [Fact]
    public async Task GetUserCounts_SecretMovedToTheBin_LeavesTheCount()
    {
        await WarmUpAsync(TestUsers.Admin);
        await WarmUpAsync(TestUsers.User);
        await NewSecretAsync();
        var binned = await NewSecretAsync();
        using var client = ClientFor(TestUsers.Admin);
        Assert.Equal(2, (await GetCountsAsync(client, OwnerId)).ItemCount);

        var deleted = await OwnerVault.DeleteSecretAsync(binned.Id, binned.ConcurrencyToken);

        Assert.True(deleted.IsSuccessStatusCode, $"Delete failed with {deleted.StatusCode}.");
        Assert.Equal(1, (await GetCountsAsync(client, OwnerId)).ItemCount);
    }

    private static async Task<UserCountsDto> GetCountsAsync(HttpClient client, Guid userId)
    {
        var response = await client.GetAsync(CountsPath(userId), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var counts = await response.Content.ReadFromJsonAsync<UserCountsDto>(TestContext.Current.CancellationToken);
        return Assert.IsType<UserCountsDto>(counts);
    }

    private static string CountsPath(Guid userId) => CountsPathPrefix + userId.ToString("D") + CountsPathSuffix;
}
