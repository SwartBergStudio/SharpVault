// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos.Sharing;

namespace SharpVault.Application.Features.Sharing.Common;

public static class ShareGrantRules
{
    public static bool CanChange(SecretPermission gateResult, ShareGrant grant, Guid callerId)
    {
        ArgumentNullException.ThrowIfNull(grant);

        return gateResult == SecretPermission.Owner
            || (grant.GrantedById == callerId && grant.RecipientId != callerId);
    }

    public static bool CanGrant(SecretPermission caller, SharePermissionDto requested) =>
        caller == SecretPermission.Owner || requested <= SharingLimits.ManagerMaxGrant;
}
