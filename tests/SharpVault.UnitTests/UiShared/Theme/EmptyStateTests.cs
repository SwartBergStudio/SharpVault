// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Icons;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed class EmptyStateTests : BunitContext
{
    [Fact]
    public void EmptyState_Rendered_HasIdTitleAndLabelledBy()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(state => state.Id, "vault-empty")
            .Add(state => state.Title, "Nothing here yet"));

        var section = cut.Find("section#vault-empty");
        Assert.Equal("vault-empty-title", section.GetAttribute("aria-labelledby"));
        var heading = cut.Find("h2#vault-empty-title");
        Assert.Equal("Nothing here yet", heading.TextContent);
        Assert.Contains("sv-empty", section.ClassList);
    }

    [Fact]
    public void EmptyState_WithoutActions_RendersNoActionsContainer()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(state => state.Id, "vault-empty")
            .Add(state => state.Title, "Nothing"));

        Assert.Empty(cut.FindAll(".sv-empty__actions"));
    }

    [Fact]
    public void EmptyState_WithActions_RendersActionsContainer()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(state => state.Id, "vault-empty")
            .Add(state => state.Title, "Nothing")
            .Add(state => state.Actions, "<button id=\"add-secret\" type=\"button\">Add</button>"));

        Assert.NotNull(cut.Find(".sv-empty__actions #add-secret"));
    }

    [Fact]
    public void EmptyState_DefaultIcon_IsVaultAndCustomIconDiffers()
    {
        var withDefault = Render<EmptyState>(parameters => parameters
            .Add(state => state.Id, "vault-empty")
            .Add(state => state.Title, "T"));
        var withCustom = Render<EmptyState>(parameters => parameters
            .Add(state => state.Id, "vault-empty")
            .Add(state => state.Title, "T")
            .Add(state => state.Icon, IconName.Search));
        var vaultIcon = Render<Icon>(parameters => parameters
            .Add(icon => icon.Name, IconName.Vault)
            .Add(icon => icon.CssClass, "sv-icon--xl"));

        Assert.Equal(vaultIcon.Find("svg").InnerHtml, withDefault.Find(".sv-empty__visual svg").InnerHtml);
        Assert.NotEqual(withDefault.Find(".sv-empty__visual svg").InnerHtml, withCustom.Find(".sv-empty__visual svg").InnerHtml);
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
