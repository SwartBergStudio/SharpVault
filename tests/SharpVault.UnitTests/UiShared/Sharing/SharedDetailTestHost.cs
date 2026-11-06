// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;

namespace SharpVault.UnitTests.UiShared.Sharing;

public abstract class SharedDetailTestHost : SharingTestHost
{
    protected SharedDetailTestHost()
    {
        OwnerId = Guid.NewGuid();
        SecretId = Guid.NewGuid();
    }

    protected ISharedItemsApiClient SharedItemsClient => SharedClient;

    protected Guid OwnerId { get; }

    protected Guid SecretId { get; }

    protected SharedSecretDto ReturnsShared(SharePermissionDto permission, bool damaged = false, bool hasNotes = true)
    {
        var shared = new SharedSecretDto
        {
            OwnerId = OwnerId,
            OwnerDisplayName = "Ada Lovelace",
            Permission = permission,
            Item = new SecretDto
            {
                Id = SecretId,
                Name = "Router login",
                Type = SecretTypeDto.Login,
                Username = "admin",
                Tags = ["lab"],
                HasValue = true,
                HasNotes = hasNotes,
                IsDamaged = damaged,
                ConcurrencyToken = "etag-shared",
            },
        };
        SharedItemsClient.GetSecretAsync(OwnerId, SecretId, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedSecretDto>.Success(shared)));
        return shared;
    }

    protected void ReturnsReveal(string value) =>
        SharedItemsClient.RevealSecretAsync(OwnerId, SecretId, Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = value })));
}
