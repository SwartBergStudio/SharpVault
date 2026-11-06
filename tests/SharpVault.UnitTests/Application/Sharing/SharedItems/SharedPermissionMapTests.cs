// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Dtos.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.SharedItems;

public sealed class SharedPermissionMapTests
{
    [Theory]
    [InlineData(SecretPermission.View, SharePermissionDto.View)]
    [InlineData(SecretPermission.Edit, SharePermissionDto.Edit)]
    [InlineData(SecretPermission.Manage, SharePermissionDto.Manage)]
    public void ToDto_GrantableLevels_MapOneToOne(SecretPermission permission, SharePermissionDto expected)
    {
        Assert.Equal(expected, SharedPermissionMap.ToDto(permission));
    }

    [Theory]
    [InlineData(SecretPermission.None)]
    [InlineData(SecretPermission.Owner)]
    [InlineData((SecretPermission)99)]
    public void ToDto_NoneOwnerAndUnknown_Throw(SecretPermission permission)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SharedPermissionMap.ToDto(permission));
    }
}
