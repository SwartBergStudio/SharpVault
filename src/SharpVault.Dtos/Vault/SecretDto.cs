// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Vault;

public sealed class SecretDto
{
    public Guid Id { get; set; }

    public SecretTypeDto Type { get; set; }

    public string? Name { get; set; }

    public string? Username { get; set; }

    public string? Url { get; set; }

    public List<string> Tags { get; set; } = [];

    public Guid? FolderId { get; set; }

    public bool HasValue { get; set; }

    public bool HasNotes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public string ConcurrencyToken { get; set; } = string.Empty;

    public bool IsDamaged { get; set; }
}
