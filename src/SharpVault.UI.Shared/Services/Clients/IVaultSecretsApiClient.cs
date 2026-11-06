// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.UI.Shared.Services.Clients;

public interface IVaultSecretsApiClient
{
    Task<ApiResponse<SecretPageDto>> ListSecretsAsync(Guid? folderId, bool unfiled, string? search, int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task<ApiResponse<SecretDto>> GetSecretAsync(Guid secretId, CancellationToken cancellationToken);

    Task<ApiResponse<SecretDto>> CreateSecretAsync(CreateSecretRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<SecretDto>> UpdateSecretAsync(Guid secretId, string concurrencyToken, UpdateSecretRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<SecretDto>> MoveSecretAsync(Guid secretId, string concurrencyToken, MoveSecretRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<bool>> DeleteSecretAsync(Guid secretId, string concurrencyToken, CancellationToken cancellationToken);

    Task<ApiResponse<RevealedValueDto>> RevealSecretAsync(Guid secretId, RevealSecretRequestDto request, CancellationToken cancellationToken);
}
