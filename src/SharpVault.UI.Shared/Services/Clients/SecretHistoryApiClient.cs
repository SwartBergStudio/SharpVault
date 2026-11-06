// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UI.Shared.Services.Clients;

public sealed class SecretHistoryApiClient(
    HttpClient httpClient,
    IApiAccessTokenProvider tokenProvider,
    AuthenticationStateProvider authenticationStateProvider,
    ISessionValidator sessionValidator,
    ILogger<SecretHistoryApiClient> logger) : ApiClientBase(httpClient, tokenProvider, authenticationStateProvider, sessionValidator, logger), ISecretHistoryApiClient
{
    public Task<ApiResponse<SecretVersionListDto>> ListVersionsAsync(Guid secretId, CancellationToken cancellationToken) =>
        GetAsync<SecretVersionListDto>(ApiRoutes.VaultSecretHistory.Bind("secretId", secretId), cancellationToken);

    public Task<ApiResponse<RevealedValueDto>> RevealVersionAsync(Guid secretId, Guid versionId, RevealVersionRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<RevealVersionRequestDto, RevealedValueDto>(ApiRoutes.VaultSecretVersionReveal.Bind("secretId", secretId).Bind("versionId", versionId), request, null, cancellationToken);
    }

    public Task<ApiResponse<SecretDto>> RestoreVersionAsync(Guid secretId, Guid versionId, string concurrencyToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(concurrencyToken);
        return PostAsync<SecretDto>(ApiRoutes.VaultSecretVersionRestore.Bind("secretId", secretId).Bind("versionId", versionId), concurrencyToken, cancellationToken);
    }
}
