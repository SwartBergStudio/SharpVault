// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos.Sharing;

namespace SharpVault.Application.Features.Sharing.Common;

public static class SharingEnumMap
{
    public static SharePermissionDto ToDto(SharePermission permission) => permission switch
    {
        SharePermission.View => SharePermissionDto.View,
        SharePermission.Edit => SharePermissionDto.Edit,
        SharePermission.Manage => SharePermissionDto.Manage,
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null),
    };

    public static SharePermission ToDomain(SharePermissionDto permission) => permission switch
    {
        SharePermissionDto.View => SharePermission.View,
        SharePermissionDto.Edit => SharePermission.Edit,
        SharePermissionDto.Manage => SharePermission.Manage,
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null),
    };

    public static SecretPermission ToAccess(SharePermission permission) => permission switch
    {
        SharePermission.None => SecretPermission.None,
        SharePermission.View => SecretPermission.View,
        SharePermission.Edit => SecretPermission.Edit,
        SharePermission.Manage => SecretPermission.Manage,
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null),
    };

    public static SharedItemKindDto ToDto(ShareItemKind kind) => kind switch
    {
        ShareItemKind.Secret => SharedItemKindDto.Secret,
        ShareItemKind.Folder => SharedItemKindDto.Folder,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
