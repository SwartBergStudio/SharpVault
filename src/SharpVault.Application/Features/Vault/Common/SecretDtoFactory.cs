// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;

namespace SharpVault.Application.Features.Vault.Common;

public static class SecretDtoFactory
{
    public static SecretDto ForRecipient(Versioned<Secret> secret, OpenedVault? vault)
    {
        var dto = Create(secret, vault);
        dto.FolderId = null;
        return dto;
    }

    public static SecretDto Create(Versioned<Secret> secret, OpenedVault? vault)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var row = secret.Value;
        var dto = new SecretDto
        {
            Id = row.Id,
            Type = VaultEnumMap.ToDto(row.Type),
            FolderId = row.FolderId,
            HasValue = row.ValueEnvelope is not null,
            HasNotes = row.NotesEnvelope is not null,
            CreatedAt = row.CreatedAt,
            ModifiedAt = row.ModifiedAt,
            ConcurrencyToken = secret.ETag,
        };

        if (vault is not null && SecretMetadataCipher.TryDecrypt(vault.Session, row, vault.MetadataKeyVersion, out var metadata))
        {
            dto.Name = metadata.Name;
            dto.Username = metadata.Username;
            dto.Url = metadata.Url;
            dto.Tags = [.. metadata.Tags];
            return dto;
        }

        dto.IsDamaged = true;
        return dto;
    }
}
