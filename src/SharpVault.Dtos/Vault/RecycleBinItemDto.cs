// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Vault;

public sealed class RecycleBinItemDto
{
    public RecycleBinItemKindDto Kind { get; set; }

    public Guid Id { get; set; }

    public string? Name { get; set; }

    public SecretTypeDto? Type { get; set; }

    public Guid DeletionBatchId { get; set; }

    public DateTimeOffset DeletedAt { get; set; }

    public DateTimeOffset PurgeAfter { get; set; }

    public bool IsDamaged { get; set; }
}
