// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Vault;

public sealed class FolderDto
{
    public Guid Id { get; set; }

    public Guid? ParentId { get; set; }

    public string? Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public bool IsDamaged { get; set; }
}
