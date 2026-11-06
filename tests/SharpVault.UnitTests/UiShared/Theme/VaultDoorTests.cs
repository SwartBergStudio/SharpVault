// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Brand;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed class VaultDoorTests : BunitContext
{
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
