// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Brand;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed class VaultDoorTests : BunitContext
{
    [Fact]
    public void VaultDoor_Default_IsDecorativeAndStatic()
    {
        var cut = Render<VaultDoor>();

        var svg = cut.Find("svg");
        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.Equal("false", svg.GetAttribute("focusable"));
        Assert.Equal("vault-door vault-door--hero", svg.GetAttribute("class"));
        Assert.DoesNotContain("vault-door--sealing", svg.ClassList);
        Assert.False(svg.HasAttribute("id"));
        Assert.False(svg.HasAttribute("role"));
    }

    [Fact]
    public void VaultDoor_Sealing_AddsSealingClass()
    {
        var cut = Render<VaultDoor>(parameters => parameters.Add(door => door.Sealing, true));

        var classes = cut.Find("svg").ClassList;
        Assert.Contains("vault-door--sealing", classes);
        Assert.Contains("vault-door--hero", classes);
    }

    [Fact]
    public void VaultDoor_MarkVariant_AddsMarkClass()
    {
        var cut = Render<VaultDoor>(parameters => parameters.Add(door => door.Variant, VaultDoorVariant.Mark));

        var classes = cut.Find("svg").ClassList;
        Assert.Contains("vault-door--mark", classes);
        Assert.DoesNotContain("vault-door--hero", classes);
        Assert.DoesNotContain("vault-door--sealing", classes);
    }

    [Fact]
    public void VaultDoor_Rendered_HasPlannedShapeCounts()
    {
        var cut = Render<VaultDoor>();

        Assert.Equal("0 0 120 120", cut.Find("svg").GetAttribute("viewBox"));
        Assert.Equal(2, cut.FindAll("rect.vault-door__hinge").Count);
        Assert.Equal(8, cut.FindAll(".vault-door__bolts rect").Count);
        Assert.Equal(3, cut.FindAll(".vault-door__wheel line").Count);
        Assert.Equal(4, cut.FindAll(".vault-door__wheel circle").Count);
    }

    [Fact]
    public void VaultDoor_ValidId_IsRendered()
    {
        var cut = Render<VaultDoor>(parameters => parameters.Add(door => door.Id, "signed-out-door"));

        Assert.Equal("signed-out-door", cut.Find("svg").GetAttribute("id"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("has space")]
    [InlineData("a/b")]
    public void VaultDoor_InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<VaultDoor>(parameters => parameters.Add(door => door.Id, id)));
    }
}
