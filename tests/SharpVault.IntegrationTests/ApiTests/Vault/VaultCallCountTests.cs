// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.RegularExpressions;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Performance")]
public sealed partial class VaultCallCountTests(ApiTestFactory factory) : VaultSecurityTestBase(factory)
{
    private static readonly Guid Owner = TestUsers.User.ObjectId;

    [GeneratedRegex(@"^/[^/]+/Vaults(\(|$)", RegexOptions.CultureInvariant)]
    private static partial Regex VaultsPath();

    [GeneratedRegex(@"^/[^/]+/\$batch$", RegexOptions.CultureInvariant)]
    private static partial Regex BatchPath();

    private VaultTestApi Api => ApiFor(TestUsers.User);

    private async Task<VaultTestApi> ArrangedApiAsync()
    {
        var api = Api;
        await WarmUpAsync(TestUsers.User);
        return api;
    }

    private Counts Measure() => new(
        StorageRequests.Requests.Count(request => request.Method == "GET" && VaultsPath().IsMatch(request.Path)),
        StorageRequests.Requests.Count(request => request.Method != "GET" && (VaultsPath().IsMatch(request.Path) || BatchPath().IsMatch(request.Path))),
        StorageRequests.AuditWrites().Count,
        Factory.KeyWraps.Unwraps,
        Factory.KeyWraps.Wraps);

    private async Task<(SecretDto Secret, VaultTestApi Api)> SecretAsync(Guid? folderId = null)
    {
        var api = await ArrangedApiAsync();
        var secret = await CreateSecretAsync(api, new PlaintextMarkers(), folderId);
        return (secret, api);
    }

    private async Task<(Guid Parent, IReadOnlyList<Guid> Children, VaultTestApi Api)> FolderWithContentsAsync(int secrets, int children)
    {
        var api = await ArrangedApiAsync();
        var parent = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "parent" }));
        var childIds = new List<Guid>();
        for (var index = 0; index < children; index++)
        {
            childIds.Add(Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = $"child-{index}", ParentId = parent.Id })).Id);
        }

        for (var index = 0; index < secrets; index++)
        {
            await CreateSecretAsync(api, new PlaintextMarkers(), parent.Id);
        }

        return (parent.Id, childIds, api);
    }

    [Fact]
    public async Task ListSecrets_Calls_MatchBudget()
    {
        var (_, api) = await SecretAsync();
        await CreateSecretAsync(api, new PlaintextMarkers());
        ResetCounters();

        var result = await api.ListSecretsAsync();

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal(new Counts(2, 0, 0, 1, 0), Measure());
    }

    [Fact]
    public async Task ListSecretsWithSearch_Calls_MatchBudget()
    {
        var (_, api) = await SecretAsync();
        ResetCounters();

        var result = await api.ListSecretsAsync(search: "SV-NAME");

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Single(result.Body!.Items);
        Assert.Equal(new Counts(2, 0, 0, 1, 0), Measure());
    }

    [Fact]
    public async Task GetSecret_Calls_MatchBudget()
    {
        var (secret, api) = await SecretAsync();
        ResetCounters();

        var result = await api.GetSecretAsync(secret.Id);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal(new Counts(2, 0, 0, 1, 0), Measure());
    }

    [Fact]
    public async Task CreateSecretFirstEver_Calls_MatchBudget()
    {
        var api = await ArrangedApiAsync();
        ResetCounters();

        var result = await api.CreateSecretAsync(NewSecretRequest(new PlaintextMarkers()));

        Assert.Equal(HttpStatusCode.Created, result.Response.StatusCode);
        Assert.Equal(new Counts(1, 2, 2, 0, 2), Measure());
    }

    [Fact]
    public async Task CreateSecret_Calls_MatchBudget()
    {
        var (_, api) = await SecretAsync();
        ResetCounters();

        var result = await api.CreateSecretAsync(NewSecretRequest(new PlaintextMarkers()));

        Assert.Equal(HttpStatusCode.Created, result.Response.StatusCode);
        Assert.Equal(new Counts(1, 1, 2, 1, 1), Measure());
    }

    [Fact]
    public async Task CreateSecretInFolder_Calls_MatchBudget()
    {
        var (_, api) = await SecretAsync();
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "target" }));
        ResetCounters();

        var result = await api.CreateSecretAsync(NewSecretRequest(new PlaintextMarkers(), folder.Id));

        Assert.Equal(HttpStatusCode.Created, result.Response.StatusCode);
        Assert.Equal(new Counts(2, 1, 2, 1, 1), Measure());
    }

    [Fact]
    public async Task UpdateSecretMetadata_Calls_MatchBudget()
    {
        var (secret, api) = await SecretAsync();
        ResetCounters();

        var result = await api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, UpdateRequest(new PlaintextMarkers()));

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal(new Counts(2, 1, 2, 1, 0), Measure());
    }

    [Fact]
    public async Task UpdateSecretValue_Calls_MatchBudget()
    {
        var (secret, api) = await SecretAsync();
        ResetCounters();

        var result = await api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, UpdateRequest(new PlaintextMarkers(), "new-value"));

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal(new Counts(3, 1, 2, 2, 0), Measure());
    }

    [Fact]
    public async Task MoveSecret_Calls_MatchBudget()
    {
        var (secret, api) = await SecretAsync();
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "target" }));
        ResetCounters();

        var result = await api.MoveSecretAsync(secret.Id, secret.ConcurrencyToken, new MoveSecretRequestDto { FolderId = folder.Id });

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal(new Counts(3, 1, 2, 1, 0), Measure());
    }

    [Fact]
    public async Task DeleteSecret_Calls_MatchBudget()
    {
        var (secret, api) = await SecretAsync();
        ResetCounters();

        var response = await api.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new Counts(1, 1, 2, 0, 0), Measure());
    }

    [Fact]
    public async Task RevealValue_Calls_MatchBudget()
    {
        var (secret, api) = await SecretAsync();
        ResetCounters();

        var result = await api.RevealSecretAsync(secret.Id, ViewValue);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal(new Counts(1, 0, 2, 1, 0), Measure());
    }

    [Fact]
    public async Task ListHistory_Calls_MatchBudget()
    {
        var api = await ArrangedApiAsync();
        var scenario = await VaultScenario.ArrangeAsync(api);
        ResetCounters();

        var result = await api.ListHistoryAsync(scenario.SecretS);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal(new Counts(2, 0, 0, 0, 0), Measure());
    }

    [Fact]
    public async Task RevealVersion_Calls_MatchBudget()
    {
        var api = await ArrangedApiAsync();
        var scenario = await VaultScenario.ArrangeAsync(api);
        ResetCounters();

        var result = await api.RevealVersionAsync(scenario.SecretS, scenario.VersionV, new RevealVersionRequestDto { Intent = RevealIntentDto.View });

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal(new Counts(2, 0, 2, 1, 0), Measure());
    }

    [Fact]
    public async Task RestoreVersion_Calls_MatchBudget()
    {
        var api = await ArrangedApiAsync();
        var scenario = await VaultScenario.ArrangeAsync(api);
        ResetCounters();

        var result = await api.RestoreVersionAsync(scenario.SecretS, scenario.VersionV, scenario.SecretToken);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal(new Counts(4, 1, 2, 2, 0), Measure());
    }

    [Fact]
    public async Task ListFolders_Calls_MatchBudget()
    {
        var api = await ArrangedApiAsync();
        Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "one" }));
        ResetCounters();

        var result = await api.ListFoldersAsync();

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal(new Counts(2, 0, 0, 1, 0), Measure());
    }

    [Fact]
    public async Task CreateFolder_Calls_MatchBudget()
    {
        var api = await ArrangedApiAsync();
        Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "one" }));
        ResetCounters();

        var result = await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "two" });

        Assert.Equal(HttpStatusCode.Created, result.Response.StatusCode);
        Assert.Equal(new Counts(2, 1, 1, 1, 0), Measure());
    }

    [Fact]
    public async Task RenameFolder_Calls_MatchBudget()
    {
        var api = await ArrangedApiAsync();
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "one" }));
        ResetCounters();

        var result = await api.RenameFolderAsync(folder.Id, new RenameFolderRequestDto { Name = "renamed" });

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal(new Counts(2, 1, 1, 1, 0), Measure());
    }

    [Fact]
    public async Task MoveFolder_Calls_MatchBudget()
    {
        var api = await ArrangedApiAsync();
        var parent = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "parent" }));
        var child = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "child" }));
        ResetCounters();

        var result = await api.MoveFolderAsync(child.Id, new MoveFolderRequestDto { ParentId = parent.Id });

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal(new Counts(3, 1, 1, 1, 0), Measure());
    }

    [Fact]
    public async Task DeleteFolderMoveToParent_Calls_MatchBudget()
    {
        const int secrets = 5;
        const int children = 2;
        var (parent, _, api) = await FolderWithContentsAsync(secrets, children);
        ResetCounters();

        var response = await api.DeleteFolderAsync(parent, FolderDeleteModeDto.MoveToParent);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new Counts(3, 1, (2 * secrets) + children + 1, 0, 0), Measure());
    }

    [Fact]
    public async Task DeleteFolderDeleteContents_Calls_MatchBudget()
    {
        const int secrets = 5;
        const int children = 2;
        var (parent, _, api) = await FolderWithContentsAsync(secrets, children);
        ResetCounters();

        var response = await api.DeleteFolderAsync(parent, FolderDeleteModeDto.DeleteContents);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var folders = children + 1;
        Assert.Equal(new Counts(2 + folders, 1, (2 * secrets) + folders, 0, 0), Measure());
    }

    [Fact]
    public async Task ListRecycleBin_Calls_MatchBudget()
    {
        var (secret, api) = await SecretAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken)).StatusCode);
        ResetCounters();

        var result = await api.ListRecycleBinAsync();

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Single(result.Body!.Items);
        Assert.Equal(new Counts(2, 0, 0, 1, 0), Measure());
    }

    [Fact]
    public async Task RestoreSecret_Calls_MatchBudget()
    {
        var api = await ArrangedApiAsync();
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "origin" }));
        var secret = await CreateSecretAsync(api, new PlaintextMarkers(), folder.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken)).StatusCode);
        ResetCounters();

        var result = await api.RestoreRecycledSecretAsync(secret.Id);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        var counts = Measure();
        Assert.InRange(counts.VaultReads, 2, 3);
        Assert.Equal(new Counts(counts.VaultReads, 1, 2, 1, 0), counts);
    }

    [Fact]
    public async Task PurgeSecret_Calls_MatchBudget()
    {
        var (secret, api) = await SecretAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken)).StatusCode);
        ResetCounters();

        var response = await api.PurgeRecycledSecretAsync(secret.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new Counts(3, 1, 2, 0, 0), Measure());
    }

    [Fact]
    public async Task PurgeFolder_Calls_MatchBudget()
    {
        const int secrets = 5;
        const int children = 2;
        var (parent, _, api) = await FolderWithContentsAsync(secrets, children);
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteFolderAsync(parent, FolderDeleteModeDto.DeleteContents)).StatusCode);
        ResetCounters();

        var response = await api.PurgeRecycledFolderAsync(parent);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new Counts(2 + secrets + (secrets + children + 1), secrets + 1, (2 * secrets) + children + 1, 0, 0), Measure());
    }

    [Fact]
    public async Task RestoreFolder_Calls_MatchBudget()
    {
        const int secrets = 5;
        const int children = 2;
        var (parent, _, api) = await FolderWithContentsAsync(secrets, children);
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteFolderAsync(parent, FolderDeleteModeDto.DeleteContents)).StatusCode);
        ResetCounters();

        var result = await api.RestoreRecycledFolderAsync(parent);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal(new Counts(4, 1, (2 * secrets) + children + 1, 1, 0), Measure());
    }

    [Fact]
    public async Task ListSecretsPageOf50_UnwrapsExactlyOnce()
    {
        var api = await ArrangedApiAsync();
        await VaultSeeder.SeedAsync(Factory, Owner, 60, 0, Token);
        ResetCounters();

        var result = await api.ListSecretsAsync(pageSize: 50);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal(50, result.Body!.Items.Count);
        Assert.All(result.Body.Items, item => Assert.False(item.IsDamaged));
        Assert.Equal(1, Factory.KeyWraps.Unwraps);
        Assert.Equal(0, Factory.KeyWraps.Wraps);
    }

    private sealed record Counts(int VaultReads, int VaultWrites, int AuditWrites, int Unwraps, int Wraps);
}
