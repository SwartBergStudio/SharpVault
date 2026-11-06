// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using SharpVault.Dtos;
using SharpVault.Dtos.Directory;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;

namespace SharpVault.IntegrationTests.Vault;

public sealed class SharingTestApi(HttpClient client)
{
    public HttpClient Client { get; } = client;

    private VaultTestApi Api { get; } = new(client);

    public Task<(HttpResponseMessage Response, SharedItemPageDto? Body)> ListSharedItemsAsync(
        int? pageSize = null,
        string? continuationToken = null,
        Guid? filedFolderId = null)
    {
        var query = new List<string>();
        AddQuery(query, ApiQuery.PageSize, pageSize?.ToString(CultureInfo.InvariantCulture));
        AddQuery(query, ApiQuery.ContinuationToken, continuationToken);
        AddQuery(query, ApiQuery.FiledFolderId, filedFolderId?.ToString("D"));
        return Api.SendAsync<SharedItemPageDto>(HttpMethod.Get, ApiRoutes.SharedItems + Join(query));
    }

    public Task<(HttpResponseMessage Response, SharedSecretDto? Body)> GetSharedSecretAsync(Guid ownerId, Guid secretId) =>
        Api.SendAsync<SharedSecretDto>(HttpMethod.Get, SecretPath(ApiRoutes.SharedSecret, ownerId, secretId));

    public Task<(HttpResponseMessage Response, SecretDto? Body)> UpdateSharedSecretAsync(Guid ownerId, Guid secretId, string? ifMatch, UpdateSecretRequestDto body) =>
        Api.SendAsync<SecretDto>(HttpMethod.Put, SecretPath(ApiRoutes.SharedSecret, ownerId, secretId), body, ifMatch);

    public Task<(HttpResponseMessage Response, RevealedValueDto? Body)> RevealSharedSecretAsync(Guid ownerId, Guid secretId, RevealSecretRequestDto body) =>
        Api.SendAsync<RevealedValueDto>(HttpMethod.Post, SecretPath(ApiRoutes.SharedSecretReveal, ownerId, secretId), body);

    public Task<(HttpResponseMessage Response, SharedFolderContentsDto? Body)> GetSharedFolderAsync(
        Guid ownerId,
        Guid folderId,
        int? pageSize = null,
        string? continuationToken = null)
    {
        var query = new List<string>();
        AddQuery(query, ApiQuery.PageSize, pageSize?.ToString(CultureInfo.InvariantCulture));
        AddQuery(query, ApiQuery.ContinuationToken, continuationToken);
        return Api.SendAsync<SharedFolderContentsDto>(HttpMethod.Get, FolderPath(ApiRoutes.SharedFolder, ownerId, folderId) + Join(query));
    }

    public Task<HttpResponseMessage> FileSharedSecretAsync(Guid ownerId, Guid secretId, FileSharedItemRequestDto body) =>
        SendNoBodyAsync(HttpMethod.Put, SecretPath(ApiRoutes.SharedSecretFiling, ownerId, secretId), body);

    public Task<HttpResponseMessage> FileSharedFolderAsync(Guid ownerId, Guid folderId, FileSharedItemRequestDto body) =>
        SendNoBodyAsync(HttpMethod.Put, FolderPath(ApiRoutes.SharedFolderFiling, ownerId, folderId), body);

    public Task<(HttpResponseMessage Response, ShareGrantListDto? Body)> ListSecretGrantsAsync(Guid ownerId, Guid secretId) =>
        Api.SendAsync<ShareGrantListDto>(HttpMethod.Get, SecretPath(ApiRoutes.SecretGrants, ownerId, secretId));

    public Task<(HttpResponseMessage Response, ShareGrantDto? Body)> CreateSecretGrantAsync(Guid ownerId, Guid secretId, CreateShareGrantRequestDto body) =>
        Api.SendAsync<ShareGrantDto>(HttpMethod.Post, SecretPath(ApiRoutes.SecretGrants, ownerId, secretId), body);

    public Task<(HttpResponseMessage Response, ShareGrantDto? Body)> ChangeSecretGrantAsync(Guid ownerId, Guid secretId, Guid recipientId, string? ifMatch, ChangeShareGrantRequestDto body) =>
        Api.SendAsync<ShareGrantDto>(HttpMethod.Put, SecretGrantPath(ownerId, secretId, recipientId), body, ifMatch);

    public Task<HttpResponseMessage> RevokeSecretGrantAsync(Guid ownerId, Guid secretId, Guid recipientId) =>
        Api.SendRawAsync(HttpMethod.Delete, SecretGrantPath(ownerId, secretId, recipientId), null, null);

    public Task<(HttpResponseMessage Response, ShareGrantListDto? Body)> ListFolderGrantsAsync(Guid ownerId, Guid folderId) =>
        Api.SendAsync<ShareGrantListDto>(HttpMethod.Get, FolderPath(ApiRoutes.FolderGrants, ownerId, folderId));

    public Task<(HttpResponseMessage Response, ShareGrantDto? Body)> CreateFolderGrantAsync(Guid ownerId, Guid folderId, CreateShareGrantRequestDto body) =>
        Api.SendAsync<ShareGrantDto>(HttpMethod.Post, FolderPath(ApiRoutes.FolderGrants, ownerId, folderId), body);

    public Task<(HttpResponseMessage Response, ShareGrantDto? Body)> ChangeFolderGrantAsync(Guid ownerId, Guid folderId, Guid recipientId, string? ifMatch, ChangeShareGrantRequestDto body) =>
        Api.SendAsync<ShareGrantDto>(HttpMethod.Put, FolderGrantPath(ownerId, folderId, recipientId), body, ifMatch);

    public Task<HttpResponseMessage> RevokeFolderGrantAsync(Guid ownerId, Guid folderId, Guid recipientId) =>
        Api.SendRawAsync(HttpMethod.Delete, FolderGrantPath(ownerId, folderId, recipientId), null, null);

    public Task<(HttpResponseMessage Response, DirectoryUserListDto? Body)> SearchDirectoryAsync(string? search)
    {
        var query = new List<string>();
        AddQuery(query, ApiQuery.Search, search);
        return Api.SendAsync<DirectoryUserListDto>(HttpMethod.Get, ApiRoutes.DirectoryUsers + Join(query));
    }

    private static string SecretPath(string template, Guid ownerId, Guid secretId) =>
        VaultTestApi.Bind(template, ("ownerId", ownerId), ("secretId", secretId));

    private static string FolderPath(string template, Guid ownerId, Guid folderId) =>
        VaultTestApi.Bind(template, ("ownerId", ownerId), ("folderId", folderId));

    private static string SecretGrantPath(Guid ownerId, Guid secretId, Guid recipientId) =>
        VaultTestApi.Bind(ApiRoutes.SecretGrant, ("ownerId", ownerId), ("secretId", secretId), ("recipientId", recipientId));

    private static string FolderGrantPath(Guid ownerId, Guid folderId, Guid recipientId) =>
        VaultTestApi.Bind(ApiRoutes.FolderGrant, ("ownerId", ownerId), ("folderId", folderId), ("recipientId", recipientId));

    private static void AddQuery(List<string> query, string name, string? value)
    {
        if (value is not null)
        {
            query.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static string Join(List<string> query) => query.Count == 0 ? string.Empty : "?" + string.Join('&', query);

    private async Task<HttpResponseMessage> SendNoBodyAsync(HttpMethod method, string path, object body)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(body, body.GetType(), VaultTestApi.Json);
        return await Api.SendRawAsync(method, path, json, null);
    }
}
