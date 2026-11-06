// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultAdminIsolationTests(ApiTestFactory factory) : VaultSecurityTestBase(factory)
{
    public static TheoryData<string> ItemEndpointIds() =>
        [.. VaultEndpointCalls.NonOwnerNotFoundIds.Where(id => id is not ("E3-folder" or "E12-parent"))];

    [Theory]
    [MemberData(nameof(ItemEndpointIds))]
    public async Task AdminNonOwner_EveryItemEndpoint_Returns404(string endpointId)
    {
        var scenario = await VaultScenario.ArrangeAsync(ApiFor(TestUsers.User));
        var before = await RowsOfAsync(TestUsers.User.ObjectId);

        var response = await VaultEndpointCalls.SendAsync(endpointId, ApiFor(TestUsers.Admin), scenario);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        scenario.Markers.AssertAbsentFrom(await BodyOfAsync(response));
        Assert.Equal(before, await RowsOfAsync(TestUsers.User.ObjectId));
    }

    [Fact]
    public async Task AdminLists_ReturnOnlyAdminsOwnVault()
    {
        var scenario = await VaultScenario.ArrangeAsync(ApiFor(TestUsers.User));
        var adminApi = ApiFor(TestUsers.Admin);
        var markers = new PlaintextMarkers();
        var folder = Require(await adminApi.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder }));
        var secret = await CreateSecretAsync(adminApi, markers, folder.Id);
        var binned = await CreateSecretAsync(adminApi, new PlaintextMarkers());
        Assert.Equal(HttpStatusCode.NoContent, (await adminApi.DeleteSecretAsync(binned.Id, binned.ConcurrencyToken)).StatusCode);

        var secrets = await adminApi.ListSecretsAsync(pageSize: 200);
        var folders = await adminApi.ListFoldersAsync();
        var bin = await adminApi.ListRecycleBinAsync();

        Assert.Equal([secret.Id], secrets.Body!.Items.Select(item => item.Id));
        Assert.Equal([folder.Id], folders.Body!.Items.Select(item => item.Id));
        Assert.Equal([binned.Id], bin.Body!.Items.Select(item => item.Id));
        foreach (var body in new[] { secrets.Response, folders.Response, bin.Response })
        {
            var text = await BodyOfAsync(body);
            scenario.Ids.ToList().ForEach(id => Assert.DoesNotContain(id.ToString("D"), text, StringComparison.OrdinalIgnoreCase));
            scenario.Markers.AssertAbsentFrom(text);
        }
    }

    [Fact]
    public async Task AdminOwnVault_FullLifecycle_WorksLikeUser()
    {
        var api = ApiFor(TestUsers.Admin);
        var markers = new PlaintextMarkers();
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder }));
        var created = await CreateSecretAsync(api, markers, folder.Id);
        var updated = Require(await api.UpdateSecretAsync(created.Id, created.ConcurrencyToken, UpdateRequest(markers, markers.Value + "-v2")));
        var moved = Require(await api.MoveSecretAsync(created.Id, updated.ConcurrencyToken, new MoveSecretRequestDto { FolderId = null }));

        Assert.Equal(markers.Value + "-v2", await RevealValueAsync(api, created.Id));
        var history = Require(await api.ListHistoryAsync(created.Id));
        var oldValue = await api.RevealVersionAsync(created.Id, history.Items[0].VersionId, new RevealVersionRequestDto { Intent = RevealIntentDto.View });
        Assert.Equal(markers.Value, oldValue.Body!.Value);
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteSecretAsync(created.Id, moved.ConcurrencyToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.RestoreRecycledSecretAsync(created.Id)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.PurgeRecycledFolderAsync(folder.Id)).StatusCode);
        Assert.Empty((await api.ListFoldersAsync()).Body!.Items);
    }
}
