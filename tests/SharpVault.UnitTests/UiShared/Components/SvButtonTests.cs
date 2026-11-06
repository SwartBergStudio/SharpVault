// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using MudBlazor;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Icons;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class SvButtonTests : MudTestContext
{
    [Fact]
    public void Button_Rendered_HasTheIdAndText()
    {
        var cut = Render<SvButton>(parameters => parameters.Add(button => button.Id, "save-button").AddChildContent("Save"));

        var button = cut.Find("button#save-button");
        Assert.Equal("Save", button.TextContent.Trim());
        Assert.Equal("button", button.GetAttribute("type"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    [InlineData("1abc")]
    public void Button_InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<SvButton>(parameters => parameters.Add(button => button.Id, id)));
    }

    [Fact]
    public void Button_Busy_DisablesAndMarksBusy()
    {
        var clicks = 0;
        var cut = Render<SvButton>(parameters => parameters
            .Add(button => button.Id, "save-button")
            .Add(button => button.Busy, true)
            .Add(button => button.OnClick, () => clicks++));

        var button = cut.Find("#save-button");
        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal("true", button.GetAttribute("aria-busy"));
        Assert.Equal(0, clicks);
    }

    [Fact]
    public void Button_Disabled_IsDisabled()
    {
        var cut = Render<SvButton>(parameters => parameters
            .Add(button => button.Id, "save-button")
            .Add(button => button.Disabled, true));

        Assert.True(cut.Find("#save-button").HasAttribute("disabled"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Button_Rendered_LeaksNoNameOrValueAttribute()
    {
        var cut = Render<SvButton>(parameters => parameters
            .Add(button => button.Id, "save-button")
            .AddChildContent("Save"));

        var button = cut.Find("#save-button");
        Assert.False(button.HasAttribute("name"));
        Assert.False(button.HasAttribute("value"));
    }

    [Fact]
    public void Button_Text_WithMarkup_IsEncoded()
    {
        var cut = Render<SvButton>(parameters => parameters
            .Add(button => button.Id, "save-button")
            .Add(button => button.ChildContent, builder => builder.AddContent(0, "<b onclick=x>bold</b>")));

        Assert.Empty(cut.FindAll("b"));
    }
}
