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
public sealed class VaultStorageRulesTests(ApiTestFactory factory) : VaultSecurityTestBase(factory)
{
    private static readonly Guid Owner = TestUsers.User.ObjectId;

    public static TheoryData<string> EndpointIds() => [.. VaultEndpointCalls.AllIds];

    private async Task<(VaultTestApi Api, SecretDto Secret, Guid Version)> BinnedSecretAsync(PlaintextMarkers markers)
    {
        var api = ApiFor(TestUsers.User);
        var secret = await CreateSecretAsync(api, markers);
        var updated = Require(await api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, UpdateRequest(markers, markers.Value + "-v2")));
        var version = Require(await api.ListHistoryAsync(secret.Id)).Items[0].VersionId;
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteSecretAsync(secret.Id, updated.ConcurrencyToken)).StatusCode);
        return (api, updated, version);
    }

    [Theory]
    [MemberData(nameof(EndpointIds))]
    public async Task EveryVaultEndpoint_Queries_AllHavePartitionKey(string endpointId)
    {
        var scenario = await VaultScenario.ArrangeAsync(ApiFor(TestUsers.User));
        ResetCounters();

        await VaultEndpointCalls.SendAsync(endpointId, ApiFor(TestUsers.User), scenario);

        var queries = StorageRequests.Requests.Where(request => request.IsEntityQuery && request.Path.Contains("/Vaults", StringComparison.Ordinal)).ToList();
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
        Assert.All(queries, query => Assert.Contains(Owner.ToString("D"), query.Filter, StringComparison.Ordinal));
        Assert.NotEmpty(StorageRequests.VaultRequests());
    }

    [Fact]
    public async Task GetSecret_InRecycleBin_Returns404()
    {
        var markers = new PlaintextMarkers();
        var (api, secret, _) = await BinnedSecretAsync(markers);

        var result = await api.GetSecretAsync(secret.Id);

        Assert.Equal(HttpStatusCode.NotFound, result.Response.StatusCode);
        markers.AssertAbsentFrom(await BodyOfAsync(result.Response));
    }

    [Fact]
    public async Task RevealSecret_InRecycleBin_Returns404AndWritesNoAudit()
    {
        var markers = new PlaintextMarkers();
        var (api, secret, _) = await BinnedSecretAsync(markers);
        var before = await TotalAuditRowsAsync();

        var value = await api.RevealSecretAsync(secret.Id, ViewValue);
        var notes = await api.RevealSecretAsync(secret.Id, ViewNotes);

        Assert.Equal(HttpStatusCode.NotFound, value.Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notes.Response.StatusCode);
        markers.AssertAbsentFrom(await BodyOfAsync(value.Response));
        Assert.Equal(before, await TotalAuditRowsAsync());
    }

    [Fact]
    public async Task History_SecretInRecycleBin_Returns404()
    {
        var markers = new PlaintextMarkers();
        var (api, secret, version) = await BinnedSecretAsync(markers);
        var before = await TotalAuditRowsAsync();

        var list = await api.ListHistoryAsync(secret.Id);
        var reveal = await api.RevealVersionAsync(secret.Id, version, new RevealVersionRequestDto { Intent = RevealIntentDto.View });
        var restore = await api.RestoreVersionAsync(secret.Id, version, secret.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.NotFound, list.Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reveal.Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, restore.Response.StatusCode);
        Assert.Equal(before, await TotalAuditRowsAsync());
    }

    [Fact]
    public async Task UpdateSecret_InRecycleBin_Returns404()
    {
        var markers = new PlaintextMarkers();
        var (api, secret, _) = await BinnedSecretAsync(markers);
        var before = await RowsOfAsync(Owner);

        var update = await api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, UpdateRequest(markers, "changed"));
        var move = await api.MoveSecretAsync(secret.Id, secret.ConcurrencyToken, new MoveSecretRequestDto { FolderId = null });
        var delete = await api.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.NotFound, update.Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, move.Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task ListSecrets_ExcludesRecycledSecrets()
    {
        var markers = new PlaintextMarkers();
        var (api, secret, _) = await BinnedSecretAsync(markers);
        var kept = await CreateSecretAsync(api, new PlaintextMarkers());

        var all = await api.ListSecretsAsync(pageSize: 200);
        var unfiled = await api.ListSecretsAsync(unfiled: true);
        var searched = await api.ListSecretsAsync(search: markers.Name);

        Assert.Equal([kept.Id], all.Body!.Items.Select(item => item.Id));
        Assert.Equal([kept.Id], unfiled.Body!.Items.Select(item => item.Id));
        Assert.Empty(searched.Body!.Items);
        Assert.DoesNotContain(secret.Id.ToString("D"), await BodyOfAsync(all.Response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListFolders_ExcludesRecycledFolders()
    {
        var api = ApiFor(TestUsers.User);
        var kept = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "kept" }));
        var binned = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "binned" }));
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteFolderAsync(binned.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);

        var folders = await api.ListFoldersAsync();

        Assert.Equal([kept.Id], folders.Body!.Items.Select(item => item.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await api.RenameFolderAsync(binned.Id, new RenameFolderRequestDto { Name = "x" })).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.MoveFolderAsync(binned.Id, new MoveFolderRequestDto { ParentId = null })).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteFolderAsync(binned.Id, FolderDeleteModeDto.MoveToParent)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "child", ParentId = binned.Id })).Response.StatusCode);
    }

    [Fact]
    public async Task BinnedItems_NeverReturnedByActiveEndpoints()
    {
        var api = ApiFor(TestUsers.User);
        var scenario = await VaultScenario.ArrangeAsync(api);

        var secretList = await BodyOfAsync((await api.ListSecretsAsync(pageSize: 200)).Response);
        var folderList = await BodyOfAsync((await api.ListFoldersAsync()).Response);
        var binnedSecret = await api.GetSecretAsync(scenario.BinnedSecretB);
        var inBinnedFolder = await api.CreateSecretAsync(NewSecretRequest(new PlaintextMarkers(), scenario.BinnedFolderC));
        var binnedFolderListing = await api.ListSecretsAsync(folderId: scenario.BinnedFolderC);

        Assert.DoesNotContain(scenario.BinnedSecretB.ToString("D"), secretList, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(scenario.BinnedFolderC.ToString("D"), folderList, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, binnedSecret.Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inBinnedFolder.Response.StatusCode);
        Assert.Empty(binnedFolderListing.Body!.Items);
    }
}
