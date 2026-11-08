// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class LoadingSkeletonTests : MudTestContext
{
    [Fact]
    public void LoadingSkeleton_Rendered_AnnouncesLoadingAndHidesItsBlocksFromScreenReaders()
    {
        var cut = Render<LoadingSkeleton>(parameters => parameters.Add(skeleton => skeleton.Id, "vault-loading"));

        var root = cut.Find("#vault-loading");
        Assert.Equal("status", root.GetAttribute("role"));
        Assert.Contains("Loading", cut.Find(".sv-visually-hidden").TextContent, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find("[aria-hidden]").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void LoadingSkeleton_RowCount_RendersThatManyRows()
    {
        var cut = Render<LoadingSkeleton>(parameters => parameters
            .Add(skeleton => skeleton.Id, "vault-loading")
            .Add(skeleton => skeleton.Rows, 5));

        Assert.Equal(5, cut.FindAll(".mud-skeleton").Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Not Valid")]
    public void LoadingSkeleton_InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<LoadingSkeleton>(parameters => parameters.Add(skeleton => skeleton.Id, id)));
    }
}
