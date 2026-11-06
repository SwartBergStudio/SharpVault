// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.ApiTests.Sharing;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class AdminUserCountsTests(ApiTestFactory factory) : SharingFunctionalTestBase(factory)
{
    private const string ReplacedValue = "value-replaced-for-the-count";

    [Fact]
    public async Task GetUserCounts_UserWithSecretsRecycledFoldersVersionsAndShares_ReturnsExactCounts()
    {
        await WarmUpAsync(TestUsers.Admin);
        await WarmUpAsync(TestUsers.User);
        await WarmUpAsync(TestUsers.ShareViewer);
        await WarmUpAsync(TestUsers.ShareEditor);
        await WarmUpAsync(TestUsers.ShareManager);
        var first = await NewSecretAsync();
        var second = await NewSecretAsync();
        var third = await NewSecretAsync();
        var recycled = await NewSecretAsync();
        var (replaceResponse, _) = await OwnerVault.UpdateSecretAsync(first.Id, first.ConcurrencyToken, EditBody(first.Name ?? string.Empty, ReplacedValue));
        Assert.True(replaceResponse.IsSuccessStatusCode, $"Replace failed with {replaceResponse.StatusCode}.");
        Assert.True((await OwnerVault.DeleteSecretAsync(recycled.Id, recycled.ConcurrencyToken)).IsSuccessStatusCode);
        var folder = await NewFolderAsync("Counted folder");
        await ShareSecretAsync(second.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareSecretAsync(third.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        await ShareFolderAsync(folder.Id, TestUsers.ShareManager, SharePermissionDto.View);

        using var client = ClientFor(TestUsers.Admin);
        var response = await client.GetAsync(CountsPath(Owner.ObjectId), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var counts = await response.Content.ReadFromJsonAsync<UserCountsDto>(Token);
        Assert.NotNull(counts);
        Assert.Equal(3, counts.ItemCount);
        Assert.Equal(3, counts.ShareCount);
        Assert.False(counts.CountsAreCapped);
    }

    private static string CountsPath(Guid userId) => ApiRoutes.AdminUserCounts.Replace("{userId:guid}", userId.ToString("D"), StringComparison.Ordinal);
}
