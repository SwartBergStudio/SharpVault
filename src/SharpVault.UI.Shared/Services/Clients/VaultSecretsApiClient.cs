// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UI.Shared.Services.Clients;

public sealed class VaultSecretsApiClient(
    HttpClient httpClient,
    IApiAccessTokenProvider tokenProvider,
    AuthenticationStateProvider authenticationStateProvider,
    ISessionValidator sessionValidator,
    ILogger<VaultSecretsApiClient> logger) : ApiClientBase(httpClient, tokenProvider, authenticationStateProvider, sessionValidator, logger), IVaultSecretsApiClient
{
    public Task<ApiResponse<SecretPageDto>> ListSecretsAsync(Guid? folderId, bool unfiled, string? search, int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
        GetAsync<SecretPageDto>(ApiRoutes.VaultSecrets + QueryString(folderId, unfiled, search, pageSize, continuationToken), cancellationToken);

    public Task<ApiResponse<SecretDto>> GetSecretAsync(Guid secretId, CancellationToken cancellationToken) =>
        GetAsync<SecretDto>(ApiRoutes.VaultSecret.Bind("secretId", secretId), cancellationToken);

    public Task<ApiResponse<SecretDto>> CreateSecretAsync(CreateSecretRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<CreateSecretRequestDto, SecretDto>(ApiRoutes.VaultSecrets, request, null, cancellationToken);
    }

    public Task<ApiResponse<SecretDto>> UpdateSecretAsync(Guid secretId, string concurrencyToken, UpdateSecretRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(concurrencyToken);
        ArgumentNullException.ThrowIfNull(request);
        return PutAsync<UpdateSecretRequestDto, SecretDto>(ApiRoutes.VaultSecret.Bind("secretId", secretId), request, concurrencyToken, cancellationToken);
    }

    public Task<ApiResponse<SecretDto>> MoveSecretAsync(Guid secretId, string concurrencyToken, MoveSecretRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(concurrencyToken);
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<MoveSecretRequestDto, SecretDto>(ApiRoutes.VaultSecretMove.Bind("secretId", secretId), request, concurrencyToken, cancellationToken);
    }

    public Task<ApiResponse<bool>> DeleteSecretAsync(Guid secretId, string concurrencyToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(concurrencyToken);
        return DeleteAsync(ApiRoutes.VaultSecret.Bind("secretId", secretId), concurrencyToken, cancellationToken);
    }

    public Task<ApiResponse<RevealedValueDto>> RevealSecretAsync(Guid secretId, RevealSecretRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<RevealSecretRequestDto, RevealedValueDto>(ApiRoutes.VaultSecretReveal.Bind("secretId", secretId), request, null, cancellationToken);
    }

    private static string QueryString(Guid? folderId, bool unfiled, string? search, int pageSize, string? continuationToken)
    {
        var parts = new List<string>(5);
        if (folderId is not null)
        {
            parts.Add($"{ApiQuery.FolderId}={folderId.Value:D}");
        }

        if (unfiled)
        {
            parts.Add($"{ApiQuery.Unfiled}=true");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            parts.Add($"{ApiQuery.Search}={Uri.EscapeDataString(search)}");
        }

        parts.Add($"{ApiQuery.PageSize}={pageSize.ToString(CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrEmpty(continuationToken))
        {
            parts.Add($"{ApiQuery.ContinuationToken}={Uri.EscapeDataString(continuationToken)}");
        }

        return "?" + string.Join('&', parts);
    }
}
