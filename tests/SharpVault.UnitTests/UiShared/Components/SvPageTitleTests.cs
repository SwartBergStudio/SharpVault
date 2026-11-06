// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class SvPageTitleTests : MudTestContext
{
    [Fact]
    public void PageTitle_ContentWithMarkup_IsEncoded()
    {
        var cut = Render<SvPageTitle>(parameters => parameters
            .Add(title => title.Id, "bin-heading")
            .Add(title => title.ChildContent, builder => builder.AddContent(0, "<img src=x onerror=alert(1)>")));

        Assert.Empty(cut.FindAll("img"));
        Assert.Contains("&lt;img", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a b")]
    [InlineData("1abc")]
    [InlineData("Upper")]
    [InlineData("x\"onclick=\"y")]
    public void PageTitle_InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<SvPageTitle>(parameters => parameters.Add(title => title.Id, id)));
    }
}
