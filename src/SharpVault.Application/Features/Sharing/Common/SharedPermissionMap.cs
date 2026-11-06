// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Dtos.Sharing;

namespace SharpVault.Application.Features.Sharing.Common;

public static class SharedPermissionMap
{
    public static SharePermissionDto ToDto(SecretPermission permission) => permission switch
    {
        SecretPermission.View => SharePermissionDto.View,
        SecretPermission.Edit => SharePermissionDto.Edit,
        SecretPermission.Manage => SharePermissionDto.Manage,
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null),
    };
}
