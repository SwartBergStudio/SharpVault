// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Sharing;
using SharpVault.UI.Shared.Components.Sharing;

namespace SharpVault.UnitTests.UiShared.Sharing;

[Trait("Category", "Security")]
public sealed class PermissionLabelsTests
{
    private const string ManageValue = "Manage";

    [Fact]
    public void PermissionLabels_OptionsFor_Rules()
    {
        var withManage = PermissionLabels.OptionsFor(true).Select(option => option.Value).ToList();
        var withoutManage = PermissionLabels.OptionsFor(false).Select(option => option.Value).ToList();

        Assert.Contains(ManageValue, withManage);
        Assert.DoesNotContain(ManageValue, withoutManage);
        Assert.Equal(withManage.Where(value => value != ManageValue), withoutManage);
        Assert.Equal(Enum.GetNames<SharePermissionDto>().Length, withManage.Count);
    }

    [Fact]
    public void PermissionLabels_OptionsFor_TextMatchesTheLabelRule()
    {
        var options = PermissionLabels.OptionsFor(true);

        Assert.Equal(PermissionLabels.Label(SharePermissionDto.Manage), options.Single(option => option.Value == ManageValue).Text);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void PermissionLabels_Label_DescribesWhatEachPermissionAllows()
    {
        var view = PermissionLabels.Label(SharePermissionDto.View);
        var edit = PermissionLabels.Label(SharePermissionDto.Edit);
        var manage = PermissionLabels.Label(SharePermissionDto.Manage);

        Assert.Contains("share", manage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("share", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("share", edit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("edit", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("view", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("edit", edit, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("edit", manage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Manage", "Manage")]
    [InlineData("manage", "View")]
    [InlineData("MANAGE", "View")]
    [InlineData("Bogus", "View")]
    [InlineData(null, "View")]
    public void PermissionLabels_Parse_RejectsUnknownAndCaseChangedValues(string? value, string expected)
    {
        var parsed = PermissionLabels.Parse(value, SharePermissionDto.View);

        Assert.Equal(expected, parsed.ToString());
    }
}
