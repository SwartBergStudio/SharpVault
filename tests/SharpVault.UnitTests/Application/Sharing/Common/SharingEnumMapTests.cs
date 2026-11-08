// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.Common;

[Trait("Category", "Security")]
public sealed class SharingEnumMapTests
{
    [Theory]
    [InlineData(SharePermission.None, SecretPermission.None)]
    [InlineData(SharePermission.View, SecretPermission.View)]
    [InlineData(SharePermission.Edit, SecretPermission.Edit)]
    [InlineData(SharePermission.Manage, SecretPermission.Manage)]
    public void ToAccess_Permission_MapsEachValueAndNoneGrantsNoAccess(SharePermission permission, SecretPermission expected)
    {
        Assert.Equal(expected, SharingEnumMap.ToAccess(permission));
    }

    [Fact]
    public void ToAccess_EveryDefinedPermission_NeverReachesOwner()
    {
        foreach (var permission in Enum.GetValues<SharePermission>())
        {
            Assert.True(SharingEnumMap.ToAccess(permission) < SecretPermission.Owner);
        }
    }

    [Theory]
    [InlineData(SharePermission.View, SharePermissionDto.View)]
    [InlineData(SharePermission.Edit, SharePermissionDto.Edit)]
    [InlineData(SharePermission.Manage, SharePermissionDto.Manage)]
    public void ToDto_Permission_MapsEachValue(SharePermission permission, SharePermissionDto expected)
    {
        Assert.Equal(expected, SharingEnumMap.ToDto(permission));
    }

    [Theory]
    [InlineData(SharePermissionDto.View, SharePermission.View)]
    [InlineData(SharePermissionDto.Edit, SharePermission.Edit)]
    [InlineData(SharePermissionDto.Manage, SharePermission.Manage)]
    public void ToDomain_Permission_MapsEachValue(SharePermissionDto permission, SharePermission expected)
    {
        Assert.Equal(expected, SharingEnumMap.ToDomain(permission));
    }

    [Fact]
    public void ToDto_PermissionNone_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SharingEnumMap.ToDto(SharePermission.None));
    }

    [Fact]
    public void ToDomain_UnknownPermissionDto_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SharingEnumMap.ToDomain((SharePermissionDto)99));
    }

    [Fact]
    public void ToAccess_UnknownPermission_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SharingEnumMap.ToAccess((SharePermission)99));
    }
}
