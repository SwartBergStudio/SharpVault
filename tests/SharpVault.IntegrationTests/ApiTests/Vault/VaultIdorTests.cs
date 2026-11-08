// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultIdorTests(ApiTestFactory factory) : VaultSecurityTestBase(factory)
{
    private static readonly Guid Owner = TestUsers.User.ObjectId;

    private static readonly Guid Attacker = TestUsers.OtherUser.ObjectId;

    public static TheoryData<string> SecretIds() => [.. VaultEndpointCalls.SecretItemIds];

    public static TheoryData<string> FolderIds() => [.. VaultEndpointCalls.FolderItemIds];

    public static TheoryData<string> MalformedTokens() =>
    [
        "not-a-token",
        "t",
        "' or PartitionKey ne '",
        new string('a', 1025),
        "F_00000000000000000000000000000000",
        "00000000-0000-0000-0000-000000000000",
    ];

    private async Task<(VaultScenario Scenario, VaultTestApi OwnerApi, VaultTestApi AttackerApi, SecretDto AttackerSecret)> ArrangeAsync()
    {
        var ownerApi = ApiFor(TestUsers.User);
        var scenario = await VaultScenario.ArrangeAsync(ownerApi);
        var attackerApi = ApiFor(TestUsers.OtherUser);
        var attackerSecret = await CreateSecretAsync(attackerApi, new PlaintextMarkers());
        return (scenario, ownerApi, attackerApi, attackerSecret);
    }

    [Theory]
    [MemberData(nameof(SecretIds))]
    public async Task SecretEndpoints_OtherUsersSecretId_Return404AndChangeNothing(string endpointId)
    {
        var (scenario, _, attackerApi, _) = await ArrangeAsync();
        var ownerBefore = await RowsOfAsync(Owner);
        var attackerBefore = await RowsOfAsync(Attacker);

        var response = await VaultEndpointCalls.SendAsync(endpointId, attackerApi, scenario);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await BodyOfAsync(response);
        scenario.Markers.AssertAbsentFrom(body);
        scenario.Ids.ToList().ForEach(id => Assert.DoesNotContain(id.ToString("D"), body, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ownerBefore, await RowsOfAsync(Owner));
        Assert.Equal(attackerBefore, await RowsOfAsync(Attacker));
    }

    [Theory]
    [MemberData(nameof(SecretIds))]
    public async Task SecretEndpoints_OtherUsersSecretId_WriteNoAuditEntry(string endpointId)
    {
        var (scenario, _, attackerApi, _) = await ArrangeAsync();
        var before = await TotalAuditRowsAsync();

        await VaultEndpointCalls.SendAsync(endpointId, attackerApi, scenario);

        Assert.Equal(before, await TotalAuditRowsAsync());
    }

    [Theory]
    [MemberData(nameof(FolderIds))]
    public async Task FolderEndpoints_OtherUsersFolderId_Return404AndChangeNothing(string endpointId)
    {
        var (scenario, _, attackerApi, _) = await ArrangeAsync();
        var ownerBefore = await RowsOfAsync(Owner);
        var attackerBefore = await RowsOfAsync(Attacker);

        var response = await VaultEndpointCalls.SendAsync(endpointId, attackerApi, scenario);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        scenario.Markers.AssertAbsentFrom(await BodyOfAsync(response));
        Assert.Equal(ownerBefore, await RowsOfAsync(Owner));
        Assert.Equal(attackerBefore, await RowsOfAsync(Attacker));
    }

    [Fact]
    public async Task CreateSecret_OtherUsersFolderId_Returns404AndCreatesNothing()
    {
        var ownerApi = ApiFor(TestUsers.User);
        var scenario = await VaultScenario.ArrangeAsync(ownerApi);
        var attackerApi = ApiFor(TestUsers.OtherUser);
        var ownerBefore = await RowsOfAsync(Owner);

        var result = await attackerApi.CreateSecretAsync(NewSecretRequest(new PlaintextMarkers(), scenario.FolderF));

        Assert.Equal(HttpStatusCode.NotFound, result.Response.StatusCode);
        Assert.Empty(await RowsOfAsync(Attacker));
        Assert.Equal(ownerBefore, await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task MoveSecret_IntoOtherUsersFolder_Returns404AndSecretUnchanged()
    {
        var (scenario, _, attackerApi, attackerSecret) = await ArrangeAsync();
        var ownerBefore = await RowsOfAsync(Owner);
        var attackerBefore = await RowsOfAsync(Attacker);

        var result = await attackerApi.MoveSecretAsync(attackerSecret.Id, attackerSecret.ConcurrencyToken, new MoveSecretRequestDto { FolderId = scenario.FolderF });

        Assert.Equal(HttpStatusCode.NotFound, result.Response.StatusCode);
        Assert.Equal(attackerBefore, await RowsOfAsync(Attacker));
        Assert.Equal(ownerBefore, await RowsOfAsync(Owner));
        Assert.Null((await attackerApi.GetSecretAsync(attackerSecret.Id)).Body!.FolderId);
    }

    [Fact]
    public async Task CreateFolder_UnderOtherUsersFolder_Returns404()
    {
        var (scenario, _, attackerApi, _) = await ArrangeAsync();
        var ownerBefore = await RowsOfAsync(Owner);
        var attackerBefore = await RowsOfAsync(Attacker);

        var result = await attackerApi.CreateFolderAsync(new CreateFolderRequestDto { Name = "intruder", ParentId = scenario.FolderF });

        Assert.Equal(HttpStatusCode.NotFound, result.Response.StatusCode);
        Assert.Equal(ownerBefore, await RowsOfAsync(Owner));
        Assert.Equal(attackerBefore, await RowsOfAsync(Attacker));
    }

    [Fact]
    public async Task MoveFolder_UnderOtherUsersFolder_Returns404AndTreeUnchanged()
    {
        var (scenario, _, attackerApi, _) = await ArrangeAsync();
        var own = Require(await attackerApi.CreateFolderAsync(new CreateFolderRequestDto { Name = "attacker-folder" }));
        var ownerBefore = await RowsOfAsync(Owner);
        var attackerBefore = await RowsOfAsync(Attacker);

        var result = await attackerApi.MoveFolderAsync(own.Id, new MoveFolderRequestDto { ParentId = scenario.FolderF });

        Assert.Equal(HttpStatusCode.NotFound, result.Response.StatusCode);
        Assert.Equal(ownerBefore, await RowsOfAsync(Owner));
        Assert.Equal(attackerBefore, await RowsOfAsync(Attacker));
        Assert.Null(Assert.Single((await attackerApi.ListFoldersAsync()).Body!.Items).ParentId);
    }

    [Fact]
    public async Task ListSecrets_OtherUsersFolderId_ReturnsEmptyPage()
    {
        var (scenario, _, attackerApi, _) = await ArrangeAsync();

        var result = await attackerApi.ListSecretsAsync(folderId: scenario.FolderF);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Empty(result.Body!.Items);
        Assert.Null(result.Body.ContinuationToken);
    }

    [Fact]
    public async Task RestoreSecret_OriginalFolderNowOtherUsersId_RestoresToRoot()
    {
        var (scenario, _, attackerApi, _) = await ArrangeAsync();
        var markers = new PlaintextMarkers();
        var folder = Require(await attackerApi.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder }));
        var secret = await CreateSecretAsync(attackerApi, markers, folder.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await attackerApi.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken)).StatusCode);
        await VaultRows.SetPropertyAsync(VaultsTable, Attacker, VaultKeys.SecretRowKey(secret.Id, recycled: true), "FolderKey", scenario.FolderF.ToString("N"), Token);

        var restored = await attackerApi.RestoreRecycledSecretAsync(secret.Id);

        Assert.Equal(HttpStatusCode.OK, restored.Response.StatusCode);
        Assert.Null(restored.Body!.FolderId);
        Assert.Null((await attackerApi.GetSecretAsync(secret.Id)).Body!.FolderId);
    }

    [Fact]
    public async Task HistoryReveal_OtherUsersVersionId_Returns404()
    {
        var (scenario, _, attackerApi, attackerSecret) = await ArrangeAsync();

        var result = await attackerApi.RevealVersionAsync(attackerSecret.Id, scenario.VersionV, new RevealVersionRequestDto { Intent = RevealIntentDto.View });

        Assert.Equal(HttpStatusCode.NotFound, result.Response.StatusCode);
        scenario.Markers.AssertAbsentFrom(await BodyOfAsync(result.Response));
    }

    [Fact]
    public async Task HistoryRestore_OtherUsersVersionId_Returns404AndChangesNothing()
    {
        var (scenario, _, attackerApi, attackerSecret) = await ArrangeAsync();
        var ownerBefore = await RowsOfAsync(Owner);
        var attackerBefore = await RowsOfAsync(Attacker);

        var result = await attackerApi.RestoreVersionAsync(attackerSecret.Id, scenario.VersionV, attackerSecret.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.NotFound, result.Response.StatusCode);
        Assert.Equal(ownerBefore, await RowsOfAsync(Owner));
        Assert.Equal(attackerBefore, await RowsOfAsync(Attacker));
    }

    [Fact]
    public async Task HistoryRestore_VersionOfAnotherOwnSecret_Returns404AndChangesNothing()
    {
        var api = ApiFor(TestUsers.User);
        var scenario = await VaultScenario.ArrangeAsync(api);
        var other = await CreateSecretAsync(api, new PlaintextMarkers());
        var before = await RowsOfAsync(Owner);

        var result = await api.RestoreVersionAsync(other.Id, scenario.VersionV, other.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.NotFound, result.Response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task HistoryReveal_VersionOfAnotherOwnSecret_Returns404()
    {
        var api = ApiFor(TestUsers.User);
        var scenario = await VaultScenario.ArrangeAsync(api);
        var other = await CreateSecretAsync(api, new PlaintextMarkers());

        var result = await api.RevealVersionAsync(other.Id, scenario.VersionV, new RevealVersionRequestDto { Intent = RevealIntentDto.View });

        Assert.Equal(HttpStatusCode.NotFound, result.Response.StatusCode);
        scenario.Markers.AssertAbsentFrom(await BodyOfAsync(result.Response));
    }

    [Fact]
    public async Task RecycleBinRestoreSecret_OtherUsersItem_Returns404AndItemStaysInTheirBin()
    {
        var (scenario, ownerApi, attackerApi, _) = await ArrangeAsync();
        var before = await RowsOfAsync(Owner);

        var result = await attackerApi.RestoreRecycledSecretAsync(scenario.BinnedSecretB);

        Assert.Equal(HttpStatusCode.NotFound, result.Response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
        Assert.Contains((await ownerApi.ListRecycleBinAsync()).Body!.Items, item => item.Id == scenario.BinnedSecretB);
    }

    [Fact]
    public async Task RecycleBinRestoreFolder_OtherUsersItem_Returns404()
    {
        var (scenario, ownerApi, attackerApi, _) = await ArrangeAsync();
        var before = await RowsOfAsync(Owner);

        var result = await attackerApi.RestoreRecycledFolderAsync(scenario.BinnedFolderC);

        Assert.Equal(HttpStatusCode.NotFound, result.Response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
        Assert.Contains((await ownerApi.ListRecycleBinAsync()).Body!.Items, item => item.Id == scenario.BinnedFolderC);
    }

    [Fact]
    public async Task RecycleBinPurgeSecret_OtherUsersItem_Returns404AndItemKept()
    {
        var (scenario, _, attackerApi, _) = await ArrangeAsync();
        var before = await RowsOfAsync(Owner);

        var response = await attackerApi.PurgeRecycledSecretAsync(scenario.BinnedSecretB);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task RecycleBinPurgeFolder_OtherUsersItem_Returns404AndItemKept()
    {
        var (scenario, _, attackerApi, _) = await ArrangeAsync();
        var before = await RowsOfAsync(Owner);

        var response = await attackerApi.PurgeRecycledFolderAsync(scenario.BinnedFolderC);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task RecycleBinList_ReturnsOnlyCallersItems()
    {
        var (scenario, ownerApi, attackerApi, attackerSecret) = await ArrangeAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await attackerApi.DeleteSecretAsync(attackerSecret.Id, attackerSecret.ConcurrencyToken)).StatusCode);

        var attackerBin = (await attackerApi.ListRecycleBinAsync()).Body!.Items;
        var ownerBin = (await ownerApi.ListRecycleBinAsync()).Body!.Items;

        Assert.Equal([attackerSecret.Id], attackerBin.Select(item => item.Id));
        Assert.DoesNotContain(ownerBin, item => item.Id == attackerSecret.Id);
        Assert.Contains(ownerBin, item => item.Id == scenario.BinnedSecretB);
        Assert.Contains(ownerBin, item => item.Id == scenario.BinnedFolderC);
    }

    [Fact]
    public async Task ListSecrets_ContinuationTokenFromOtherUsersVault_ReturnsOnlyCallersSecrets()
    {
        var (scenario, ownerApi, attackerApi, attackerSecret) = await ArrangeAsync();
        await CreateSecretAsync(ownerApi, new PlaintextMarkers());
        await CreateSecretAsync(ownerApi, new PlaintextMarkers());
        var ownerPage = (await ownerApi.ListSecretsAsync(pageSize: 1)).Body!;
        Assert.NotNull(ownerPage.ContinuationToken);
        var ownerIds = (await ownerApi.ListSecretsAsync(pageSize: 200)).Body!.Items.Select(item => item.Id).ToHashSet();
        var tokens = new[] { ownerPage.ContinuationToken!, ownerPage.Items[0].Id.ToString("N"), scenario.SecretS.ToString("N") };

        foreach (var token in tokens)
        {
            var result = await attackerApi.ListSecretsAsync(continuationToken: token);

            Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
            Assert.All(result.Body!.Items, item => Assert.DoesNotContain(item.Id, ownerIds));
            Assert.All(result.Body.Items, item => Assert.Equal(attackerSecret.Id, item.Id));
            scenario.Markers.AssertAbsentFrom(await BodyOfAsync(result.Response));
        }
    }

    [Theory]
    [MemberData(nameof(MalformedTokens))]
    public async Task ListSecrets_MalformedContinuationToken_Returns400(string token)
    {
        var api = ApiFor(TestUsers.User);
        await CreateSecretAsync(api, new PlaintextMarkers());

        var result = await api.ListSecretsAsync(continuationToken: token);

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(MalformedTokens))]
    public async Task ListRecycleBin_MalformedContinuationToken_Returns400(string token)
    {
        var api = ApiFor(TestUsers.User);
        await VaultScenario.ArrangeAsync(api);

        var result = await api.ListRecycleBinAsync(continuationToken: token);

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
    }
}
