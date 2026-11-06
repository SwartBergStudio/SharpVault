// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos.Sharing;

namespace SharpVault.Application.Features.Sharing.Common;

public static class ShareGrantDtoFactory
{
    public static ShareGrantDto Create(Versioned<ShareGrant> grant, bool canChange)
    {
        ArgumentNullException.ThrowIfNull(grant);

        var row = grant.Value;
        return new ShareGrantDto
        {
            RecipientId = row.RecipientId,
            RecipientDisplayName = row.RecipientDisplayName,
            RecipientEmail = row.RecipientEmail,
            Permission = SharingEnumMap.ToDto(row.Permission),
            GrantedByDisplayName = row.GrantedByDisplayName,
            GrantedAt = row.GrantedAt,
            ModifiedAt = row.ModifiedAt,
            ConcurrencyToken = grant.ETag,
            CanChange = canChange,
        };
    }
}
