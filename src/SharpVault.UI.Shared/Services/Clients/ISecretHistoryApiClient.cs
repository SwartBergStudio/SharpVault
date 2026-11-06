// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.UI.Shared.Services.Clients;

public interface ISecretHistoryApiClient
{
    Task<ApiResponse<SecretVersionListDto>> ListVersionsAsync(Guid secretId, CancellationToken cancellationToken);

    Task<ApiResponse<RevealedValueDto>> RevealVersionAsync(Guid secretId, Guid versionId, RevealVersionRequestDto request, CancellationToken cancellationToken);

    Task<ApiResponse<SecretDto>> RestoreVersionAsync(Guid secretId, Guid versionId, string concurrencyToken, CancellationToken cancellationToken);
}
