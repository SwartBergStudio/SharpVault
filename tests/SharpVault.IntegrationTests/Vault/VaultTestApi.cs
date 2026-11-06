// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;

namespace SharpVault.IntegrationTests.Vault;

public sealed partial class VaultTestApi(HttpClient client)
{
    public const string IfMatchHeader = "If-Match";

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    public HttpClient Client { get; } = client;

    public static string Bind(string template, params (string Name, object Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(template);

        var path = template;
        foreach (var (name, value) in values)
        {
            var pattern = new Regex("\\{" + Regex.Escape(name) + "(:[^}]*)?\\}", RegexOptions.CultureInvariant);
            if (!pattern.IsMatch(path))
            {
                throw new InvalidOperationException($"The route has no placeholder named {name}.");
            }

            path = pattern.Replace(path, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        if (UnboundPlaceholder().IsMatch(path))
        {
            throw new InvalidOperationException("The route still has an unbound placeholder.");
        }

        return path;
    }

    public Task<(HttpResponseMessage Response, SecretPageDto? Body)> ListSecretsAsync(
        Guid? folderId = null,
        bool? unfiled = null,
        string? search = null,
        int? pageSize = null,
        string? continuationToken = null)
    {
        var query = new List<string>();
        AddQuery(query, ApiQuery.FolderId, folderId?.ToString("D"));
        AddQuery(query, ApiQuery.Unfiled, unfiled?.ToString().ToLowerInvariant());
        AddQuery(query, ApiQuery.Search, search);
        AddQuery(query, ApiQuery.PageSize, pageSize?.ToString(CultureInfo.InvariantCulture));
        AddQuery(query, ApiQuery.ContinuationToken, continuationToken);
        return SendAsync<SecretPageDto>(HttpMethod.Get, ApiRoutes.VaultSecrets + Join(query));
    }

    public Task<(HttpResponseMessage Response, SecretDto? Body)> GetSecretAsync(Guid secretId) =>
        SendAsync<SecretDto>(HttpMethod.Get, Bind(ApiRoutes.VaultSecret, ("secretId", secretId)));

    public Task<(HttpResponseMessage Response, SecretDto? Body)> CreateSecretAsync(CreateSecretRequestDto body) =>
        SendAsync<SecretDto>(HttpMethod.Post, ApiRoutes.VaultSecrets, body);

    public Task<(HttpResponseMessage Response, SecretDto? Body)> UpdateSecretAsync(Guid secretId, string? ifMatch, UpdateSecretRequestDto body) =>
        SendAsync<SecretDto>(HttpMethod.Put, Bind(ApiRoutes.VaultSecret, ("secretId", secretId)), body, ifMatch);

    public Task<(HttpResponseMessage Response, SecretDto? Body)> MoveSecretAsync(Guid secretId, string? ifMatch, MoveSecretRequestDto body) =>
        SendAsync<SecretDto>(HttpMethod.Post, Bind(ApiRoutes.VaultSecretMove, ("secretId", secretId)), body, ifMatch);

    public Task<HttpResponseMessage> DeleteSecretAsync(Guid secretId, string? ifMatch) =>
        SendRawAsync(HttpMethod.Delete, Bind(ApiRoutes.VaultSecret, ("secretId", secretId)), null, ifMatch);

    public Task<(HttpResponseMessage Response, RevealedValueDto? Body)> RevealSecretAsync(Guid secretId, RevealSecretRequestDto body) =>
        SendAsync<RevealedValueDto>(HttpMethod.Post, Bind(ApiRoutes.VaultSecretReveal, ("secretId", secretId)), body);

    public Task<(HttpResponseMessage Response, SecretVersionListDto? Body)> ListHistoryAsync(Guid secretId) =>
        SendAsync<SecretVersionListDto>(HttpMethod.Get, Bind(ApiRoutes.VaultSecretHistory, ("secretId", secretId)));

    public Task<(HttpResponseMessage Response, RevealedValueDto? Body)> RevealVersionAsync(Guid secretId, Guid versionId, RevealVersionRequestDto body) =>
        SendAsync<RevealedValueDto>(
            HttpMethod.Post,
            Bind(ApiRoutes.VaultSecretVersionReveal, ("secretId", secretId), ("versionId", versionId)),
            body);

    public Task<(HttpResponseMessage Response, SecretDto? Body)> RestoreVersionAsync(Guid secretId, Guid versionId, string? ifMatch) =>
        SendAsync<SecretDto>(
            HttpMethod.Post,
            Bind(ApiRoutes.VaultSecretVersionRestore, ("secretId", secretId), ("versionId", versionId)),
            null,
            ifMatch);

    public Task<(HttpResponseMessage Response, FolderListDto? Body)> ListFoldersAsync() =>
        SendAsync<FolderListDto>(HttpMethod.Get, ApiRoutes.VaultFolders);

    public Task<(HttpResponseMessage Response, FolderDto? Body)> CreateFolderAsync(CreateFolderRequestDto body) =>
        SendAsync<FolderDto>(HttpMethod.Post, ApiRoutes.VaultFolders, body);

    public Task<(HttpResponseMessage Response, FolderDto? Body)> RenameFolderAsync(Guid folderId, RenameFolderRequestDto body) =>
        SendAsync<FolderDto>(HttpMethod.Put, Bind(ApiRoutes.VaultFolder, ("folderId", folderId)), body);

    public Task<(HttpResponseMessage Response, FolderDto? Body)> MoveFolderAsync(Guid folderId, MoveFolderRequestDto body) =>
        SendAsync<FolderDto>(HttpMethod.Post, Bind(ApiRoutes.VaultFolderMove, ("folderId", folderId)), body);

    public Task<HttpResponseMessage> DeleteFolderAsync(Guid folderId, string? mode)
    {
        var path = Bind(ApiRoutes.VaultFolder, ("folderId", folderId));
        if (mode is not null)
        {
            path += $"?{ApiQuery.Mode}={Uri.EscapeDataString(mode)}";
        }

        return SendRawAsync(HttpMethod.Delete, path, null, null);
    }

    public Task<HttpResponseMessage> DeleteFolderAsync(Guid folderId, FolderDeleteModeDto mode) =>
        DeleteFolderAsync(folderId, mode.ToString());

    public Task<(HttpResponseMessage Response, RecycleBinPageDto? Body)> ListRecycleBinAsync(int? pageSize = null, string? continuationToken = null)
    {
        var query = new List<string>();
        AddQuery(query, ApiQuery.PageSize, pageSize?.ToString(CultureInfo.InvariantCulture));
        AddQuery(query, ApiQuery.ContinuationToken, continuationToken);
        return SendAsync<RecycleBinPageDto>(HttpMethod.Get, ApiRoutes.RecycleBin + Join(query));
    }

    public Task<(HttpResponseMessage Response, SecretDto? Body)> RestoreRecycledSecretAsync(Guid secretId) =>
        SendAsync<SecretDto>(HttpMethod.Post, Bind(ApiRoutes.RecycleBinSecretRestore, ("secretId", secretId)));

    public Task<(HttpResponseMessage Response, FolderDto? Body)> RestoreRecycledFolderAsync(Guid folderId) =>
        SendAsync<FolderDto>(HttpMethod.Post, Bind(ApiRoutes.RecycleBinFolderRestore, ("folderId", folderId)));

    public Task<HttpResponseMessage> PurgeRecycledSecretAsync(Guid secretId) =>
        SendRawAsync(HttpMethod.Delete, Bind(ApiRoutes.RecycleBinSecret, ("secretId", secretId)), null, null);

    public Task<HttpResponseMessage> PurgeRecycledFolderAsync(Guid folderId) =>
        SendRawAsync(HttpMethod.Delete, Bind(ApiRoutes.RecycleBinFolder, ("folderId", folderId)), null, null);

    public Task<HttpResponseMessage> PostRawAsync(string path, string json, string? ifMatch = null) =>
        SendRawAsync(HttpMethod.Post, path, json, ifMatch);

    public Task<HttpResponseMessage> PutRawAsync(string path, string json, string? ifMatch = null) =>
        SendRawAsync(HttpMethod.Put, path, json, ifMatch);

    public async Task<(HttpResponseMessage Response, T? Body)> SendAsync<T>(HttpMethod method, string path, object? body = null, string? ifMatch = null)
    {
        var json = body is null ? null : JsonSerializer.Serialize(body, body.GetType(), Json);
        var response = await SendRawAsync(method, path, json, ifMatch);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength == 0)
        {
            return (response, default);
        }

        var parsed = await response.Content.ReadFromJsonAsync<T>(Json, TestContext.Current.CancellationToken);
        return (response, parsed);
    }

    public async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, string? json, string? ifMatch)
    {
        using var request = new HttpRequestMessage(method, path);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation(IfMatchHeader, ifMatch);
        }

        return await Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static void AddQuery(List<string> query, string name, string? value)
    {
        if (value is not null)
        {
            query.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static string Join(List<string> query) => query.Count == 0 ? string.Empty : "?" + string.Join('&', query);

    [GeneratedRegex(@"\{[A-Za-z]+(:[^}]*)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex UnboundPlaceholder();
}
