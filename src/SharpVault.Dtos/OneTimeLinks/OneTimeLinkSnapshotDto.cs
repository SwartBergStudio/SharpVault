// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.Dtos.OneTimeLinks;

public sealed class OneTimeLinkSnapshotDto
{
    public SecretTypeDto Type { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Username { get; set; }

    public string? Url { get; set; }

    public string? Value { get; set; }

    public string? Notes { get; set; }
}
