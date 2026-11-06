// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Infrastructure.Directory;

internal sealed class GraphUserResponse
{
    public List<GraphUserItem> Value { get; set; } = [];
}

internal sealed class GraphUserItem
{
    public Guid Id { get; set; }

    public string? DisplayName { get; set; }

    public string? Mail { get; set; }

    public string? UserPrincipalName { get; set; }
}
