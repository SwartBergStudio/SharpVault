// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mediator;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Dtos.Vault;

namespace SharpVault.Application.Features.Vault.Folders.Queries;

public sealed record ListFoldersQuery : IRequest<Result<FolderListDto>>;

public sealed class ListFoldersQueryHandler(IVaultStore store, VaultKeyRingService keyRings, ICurrentUser currentUser)
    : IRequestHandler<ListFoldersQuery, Result<FolderListDto>>
{
    public async Task<Result<FolderListDto>> Handle(ListFoldersQuery request, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;
        using var read = await keyRings.OpenForReadAsync(owner, cancellationToken);
        if (read.State == VaultReadState.NoRing)
        {
            return Result<FolderListDto>.Success(new FolderListDto());
        }

        var folders = await store.ListFoldersAsync(owner, cancellationToken);
        var items = folders
            .Select(folder => FolderDtoFactory.Create(folder, read.Vault))
            .OrderBy(folder => folder.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<FolderListDto>.Success(new FolderListDto { Items = items });
    }
}
