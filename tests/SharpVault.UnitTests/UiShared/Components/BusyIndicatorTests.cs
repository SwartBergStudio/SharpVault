// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class BusyIndicatorTests : MudTestContext
{
    [Fact]
    public void BusyIndicator_Rendered_HasStatusRole()
    {
        var cut = Render<BusyIndicator>(parameters => parameters.Add(indicator => indicator.Id, "vault-busy"));

        var root = cut.Find("#vault-busy");
        Assert.Equal("status", root.GetAttribute("role"));
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
