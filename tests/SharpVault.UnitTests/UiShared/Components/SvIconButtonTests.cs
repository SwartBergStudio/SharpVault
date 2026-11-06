// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Icons;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class SvIconButtonTests : MudTestContext
{
    [Fact]
    public void IconButton_Rendered_HasAriaLabelAndId()
    {
        var cut = Render<SvIconButton>(parameters => parameters
            .Add(button => button.Id, "menu-button")
            .Add(button => button.Icon, SvIcons.Menu)
            .Add(button => button.Label, "Open menu"));

        var button = cut.Find("button#menu-button");
        Assert.Equal("Open menu", button.GetAttribute("aria-label"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IconButton_BlankLabel_Throws(string label)
    {
        ThrownExceptions.ArgumentFrom(() => Render<SvIconButton>(parameters => parameters
            .Add(button => button.Id, "menu-button")
            .Add(button => button.Icon, SvIcons.Menu)
            .Add(button => button.Label, label)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    public void IconButton_InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<SvIconButton>(parameters => parameters
            .Add(button => button.Id, id)
            .Add(button => button.Icon, SvIcons.Menu)
            .Add(button => button.Label, "Open menu")));
    }
}
