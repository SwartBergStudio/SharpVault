// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Authorization;

namespace SharpVault.Api.Security;

public sealed class SharpVaultAccessRequirement(IReadOnlyList<string> acceptedRoles) : IAuthorizationRequirement
{
    public IReadOnlyList<string> AcceptedRoles { get; } = acceptedRoles;
}
