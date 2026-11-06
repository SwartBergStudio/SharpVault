// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Sharing;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Navigation;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class SharingRoutesTests
{
    private static readonly Guid OwnerId = Guid.Parse("3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b");
    private static readonly Guid ItemId = Guid.Parse("a1b2c3d4-0000-4000-8000-000000000001");

    [Fact]
    public void SharingRoutes_BuildExpectedPaths()
    {
        Assert.Equal("/shared", SharingRoutes.Shared);
        Assert.Equal($"/shared/{OwnerId:D}/secrets/{ItemId:D}", SharingRoutes.Secret(OwnerId, ItemId));
        Assert.Equal($"/shared/{OwnerId:D}/secrets/{ItemId:D}/edit", SharingRoutes.EditSecret(OwnerId, ItemId));
        Assert.Equal($"/shared/{OwnerId:D}/folders/{ItemId:D}", SharingRoutes.Folder(OwnerId, ItemId));
    }

    [Fact]
    public void SharingRoutes_Paths_AreLowercaseAndFreeOfBracesQueryAndFragment()
    {
        var paths = new[] { SharingRoutes.Secret(OwnerId, ItemId), SharingRoutes.EditSecret(OwnerId, ItemId), SharingRoutes.Folder(OwnerId, ItemId) };

        Assert.All(paths, path =>
        {
            Assert.StartsWith("/shared/", path, StringComparison.Ordinal);
            Assert.DoesNotContain('{', path);
            Assert.DoesNotContain('?', path);
            Assert.DoesNotContain('#', path);
            Assert.Equal(path.ToLowerInvariant(), path);
        });
    }

    [Fact]
    public void SharingRoutes_SecretAndFolder_NeverCollideForTheSameIds()
    {
        Assert.NotEqual(SharingRoutes.Secret(OwnerId, ItemId), SharingRoutes.Folder(OwnerId, ItemId));
        Assert.NotEqual(SharingRoutes.Secret(OwnerId, ItemId), SharingRoutes.Secret(ItemId, OwnerId));
    }

    [Fact]
    public void PermissionLabels_CoverEveryValue()
    {
        var values = Enum.GetValues<SharePermissionDto>();

        var options = PermissionLabels.OptionsFor(canGrantManage: true);
        Assert.Equal(values.Length, options.Count);
        foreach (var value in values)
        {
            var label = PermissionLabels.Label(value);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual("Unknown", label);
            Assert.Contains(options, option => option.Value == value.ToString() && option.Text == label);
        }

        Assert.Equal(values.Length, values.Select(PermissionLabels.Label).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PermissionLabels_UndefinedValue_IsUnknown()
    {
        Assert.Equal("Unknown", PermissionLabels.Label((SharePermissionDto)0));
        Assert.Equal("Unknown", PermissionLabels.Label((SharePermissionDto)99));
    }

    [Theory]
    [InlineData("View", SharePermissionDto.View)]
    [InlineData("Edit", SharePermissionDto.Edit)]
    [InlineData("Manage", SharePermissionDto.Manage)]
    public void PermissionLabels_Parse_RoundTripsEveryOptionValue(string value, SharePermissionDto expected)
    {
        Assert.Equal(expected, PermissionLabels.Parse(value, SharePermissionDto.View == expected ? SharePermissionDto.Manage : SharePermissionDto.View));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("view")]
    [InlineData("MANAGE")]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("Owner")]
    public void PermissionLabels_Parse_RejectsAnythingButAnExactName(string? value)
    {
        Assert.Equal(SharePermissionDto.View, PermissionLabels.Parse(value, SharePermissionDto.View));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("View,Edit")]
    [InlineData("Edit,Manage")]
    public void PermissionLabels_Parse_NumericOrCombinedValue_UsesTheFallback(string value)
    {
        Assert.Equal(SharePermissionDto.View, PermissionLabels.Parse(value, SharePermissionDto.View));
    }
}
