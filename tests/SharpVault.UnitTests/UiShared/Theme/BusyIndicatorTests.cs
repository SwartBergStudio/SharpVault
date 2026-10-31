// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed class BusyIndicatorTests : BunitContext
{
    [Fact]
    public void BusyIndicator_Rendered_HasStatusRoleAndTextLabel()
    {
        var cut = Render<BusyIndicator>(parameters => parameters.Add(indicator => indicator.Id, "vault-busy"));

        var root = cut.Find("#vault-busy");
        Assert.Equal("status", root.GetAttribute("role"));
        Assert.Equal("Working", cut.Find(".sv-busy__label").TextContent);
        Assert.Equal("true", cut.Find("svg.sv-busy__dial").GetAttribute("aria-hidden"));
        Assert.Equal(12, cut.FindAll(".sv-busy__ticks line").Count);
        Assert.Single(cut.FindAll(".sv-busy__pointer"));
    }

    [Fact]
    public void BusyIndicator_CustomLabel_IsRendered()
    {
        var cut = Render<BusyIndicator>(parameters => parameters
            .Add(indicator => indicator.Id, "vault-busy")
            .Add(indicator => indicator.Label, "Decrypting"));

        Assert.Equal("Decrypting", cut.Find(".sv-busy__label").TextContent);
    }

    [Fact]
    public void BusyIndicator_Ticks_AreSpacedThirtyDegrees()
    {
        var cut = Render<BusyIndicator>(parameters => parameters.Add(indicator => indicator.Id, "vault-busy"));

        var transforms = cut.FindAll(".sv-busy__ticks line").Select(line => line.GetAttribute("transform")).ToList();

        Assert.Equal(Enumerable.Range(0, 12).Select(index => $"rotate({index * 30} 12 12)"), transforms);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    public void BusyIndicator_InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<BusyIndicator>(parameters => parameters.Add(indicator => indicator.Id, id)));
    }
}
