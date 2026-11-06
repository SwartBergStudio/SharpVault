// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;

namespace SharpVault.Application.Features.Vault.Common;

public static class FolderDtoFactory
{
    public static FolderDto Create(Versioned<Folder> folder, OpenedVault? vault)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var row = folder.Value;
        var dto = new FolderDto
        {
            Id = row.Id,
            ParentId = row.ParentId,
            CreatedAt = row.CreatedAt,
            ModifiedAt = row.ModifiedAt,
        };

        if (vault is not null && SecretMetadataCipher.TryDecryptFolderName(vault.Session, row, vault.MetadataKeyVersion, out var name))
        {
            dto.Name = name;
            return dto;
        }

        dto.IsDamaged = true;
        return dto;
    }
}
