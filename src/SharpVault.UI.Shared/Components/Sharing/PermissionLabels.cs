// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Sharing;
using SharpVault.UI.Shared.Components.Common;

namespace SharpVault.UI.Shared.Components.Sharing;

public static class PermissionLabels
{
    public static IReadOnlyList<SvSelectOption> OptionsFor(bool canGrantManage) =>
    [
        .. Enum.GetValues<SharePermissionDto>()
            .Where(permission => canGrantManage || permission != SharePermissionDto.Manage)
            .Select(permission => new SvSelectOption(permission.ToString(), Label(permission))),
    ];

    public static string Label(SharePermissionDto permission) => permission switch
    {
        SharePermissionDto.View => "Can view and copy",
        SharePermissionDto.Edit => "Can edit",
        SharePermissionDto.Manage => "Can edit and share",
        _ => "Unknown",
    };

    public static SharePermissionDto Parse(string? value, SharePermissionDto fallback) =>
        Enum.TryParse<SharePermissionDto>(value, ignoreCase: false, out var permission) && Enum.GetName(permission) == value ? permission : fallback;
}
