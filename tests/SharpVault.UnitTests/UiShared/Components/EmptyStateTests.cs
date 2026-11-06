// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class EmptyStateTests : MudTestContext
{
    [Fact]
    public void EmptyState_Rendered_HasIdTitleAndLabelledBy()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(state => state.Id, "vault-empty")
            .Add(state => state.Title, "Nothing here yet"));

        var panel = cut.Find("#vault-empty");
        Assert.Equal("vault-empty-title", panel.GetAttribute("aria-labelledby"));
        Assert.Equal("Nothing here yet", cut.Find("h2#vault-empty-title").TextContent);
    }

    [Fact]
    public void EmptyState_WithDescription_RendersItBelowTheTitleWithAnId()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(state => state.Id, "vault-empty")
            .Add(state => state.Title, "Nothing")
            .Add(state => state.Description, "Do this next."));

        Assert.Equal("Do this next.", cut.Find("#vault-empty-description").TextContent);
    }

    [Fact]
    public void EmptyState_WithoutDescription_RendersNoDescription()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(state => state.Id, "vault-empty")
            .Add(state => state.Title, "Nothing"));

        Assert.Empty(cut.FindAll("#vault-empty-description"));
    }

    [Fact]
    public void EmptyState_Role_IsStatusNotAlert()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(state => state.Id, "vault-empty")
            .Add(state => state.Title, "T"));

        Assert.Equal("status", cut.Find("#vault-empty").GetAttribute("role"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    public void EmptyState_InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<EmptyState>(parameters => parameters
            .Add(state => state.Id, id)
            .Add(state => state.Title, "T")));
    }
}
