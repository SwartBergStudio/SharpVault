// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Azure.Data.Tables;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

public abstract class VaultFunctionalTestBase : VaultTestBase
{
    protected const int MaxFolderDepth = 10;

    protected VaultFunctionalTestBase(ApiTestFactory factory)
        : base(factory)
    {
        Api = ApiFor(Caller);
    }

    protected static TestUser Caller => TestUsers.User;

    protected VaultTestApi Api { get; }

    protected async Task<FolderDto> NewFolderAsync(string name, Guid? parentId = null) =>
        Require(await Api.CreateFolderAsync(new CreateFolderRequestDto { Name = name, ParentId = parentId }));

    protected Task<SecretDto> NewSecretAsync(Guid? folderId = null) => CreateSecretAsync(Api, new PlaintextMarkers(), folderId);

    protected async Task<IReadOnlyList<FolderDto>> NewFolderChainAsync(int depth, Guid? parentId = null)
    {
        var chain = new List<FolderDto>();
        for (var level = 0; level < depth; level++)
        {
            var folder = await NewFolderAsync($"Level {level + 1}", chain.Count == 0 ? parentId : chain[^1].Id);
            chain.Add(folder);
        }

        return chain;
    }

    protected static UpdateSecretRequestDto UpdateFrom(SecretDto secret) => new()
    {
        Type = secret.Type,
        Name = secret.Name ?? string.Empty,
        Username = secret.Username,
        Url = secret.Url,
        Tags = [.. secret.Tags],
    };

    protected async Task<SecretDto> ReplaceValueAsync(SecretDto secret, string value)
    {
        var body = UpdateFrom(secret);
        body.ReplaceValue = true;
        body.Value = value;
        return Require(await Api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, body));
    }

    protected async Task<string> RevealValueAsync(Guid secretId) =>
        Require(await Api.RevealSecretAsync(secretId, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View })).Value;

    protected async Task<string> RevealNotesAsync(Guid secretId) =>
        Require(await Api.RevealSecretAsync(secretId, new RevealSecretRequestDto { Field = RevealFieldDto.Notes, Intent = RevealIntentDto.View })).Value;

    protected async Task<string> RevealVersionAsync(Guid secretId, Guid versionId) =>
        Require(await Api.RevealVersionAsync(secretId, versionId, new RevealVersionRequestDto { Intent = RevealIntentDto.View })).Value;

    protected async Task<IReadOnlyList<RecycleBinItemDto>> WholeBinAsync()
    {
        var items = new List<RecycleBinItemDto>();
        string? token = null;
        do
        {
            var page = Require(await Api.ListRecycleBinAsync(pageSize: 200, continuationToken: token));
            items.AddRange(page.Items);
            token = page.ContinuationToken;
        }
        while (token is not null);

        return items;
    }

    protected async Task<IReadOnlyList<SecretDto>> WholeListAsync(Guid? folderId = null, bool? unfiled = null, string? search = null, int pageSize = 200)
    {
        var items = new List<SecretDto>();
        string? token = null;
        do
        {
            var page = Require(await Api.ListSecretsAsync(folderId, unfiled, search, pageSize, token));
            items.AddRange(page.Items);
            token = page.ContinuationToken;
        }
        while (token is not null);

        return items;
    }

    protected Task<IReadOnlyList<TableEntity>> VaultRowsAsync() => VaultRows.ListPartitionAsync(VaultsTable, Caller.ObjectId, Token);

    protected static void AssertStatus(HttpStatusCode expected, HttpResponseMessage response) => Assert.Equal(expected, response.StatusCode);
}
